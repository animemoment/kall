using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using Game.Core;

namespace Game.Simulation;

/// <summary>
/// Иерархическая пирамида плотности задач (Hierarchical Spatial Work Matcher).
/// Трехуровневая структура:
/// - L0: Битовая маска и SoA хранилище задач.
/// - L1: Секторы 16x16 тайлов (1024 сектора на 512x512) с O(1) swap-and-pop списками.
/// - L2: 64-битная макро-маска (8x8 макро-секторов 64x64 тайла) с tzcnt поиском за 1 такт CPU.
/// Обеспечивает мгновенный подбор работы на любом расстоянии без радиусных ограничений и очередей.
/// </summary>
public sealed class JobSpatialPyramid
{
    public const int ChunkShift = 4;    // 16x16 тайлов на чанк/сектор
    public const int ChunkDim = 64;     // 64x64 чанка = 1024x1024 тайлов (остров)
    public const int ChunkCount = ChunkDim * ChunkDim; // 4096 чанков
    public const int ChunkGridDim = ChunkDim;

    public const int MacroShift = 7;    // 128x128 тайлов на макро-сектор (8x8 чанков)
    public const int MacroDim = 8;      // 8x8 макро-секторов = 1024 тайла
    public const int MacroCount = MacroDim * MacroDim; // 64 макро-сектора = ровно 1 ulong

    private const int InitialCapacity = 32768;
    private const int GrowFactor = 2;

    /// <summary>
    /// task-2/perf: максимум кандидатов одного чанка, просматриваемых за один claim-проход.
    /// Раньше сканировался весь чанк (до сотен работ: грядки, пачки haul) — на каждого
    /// безработного агента и на каждый пробуемый чанк (свой + до 48 кольцевых + до 64
    /// макро-чанков), то есть O(работ_в_чанке × чанков) на агента. Окно сдвигается по
    /// эпохе диспатча (см. TryClaimInChunk), поэтому весь чанк покрывается за несколько
    /// диспатчей; работы не теряются — они остаются зарегистрированными и доступными.
    /// При count &lt;= 64 поведение прежнее (полный скан чанка).
    /// </summary>
    private const int MaxClaimScanPerChunk = 64;

    /// <summary>
    /// Потолок ЧАНКОВ, которые один клейм может прощупать за вызов. Замеры 100×
    /// (src/tools/Perf100xBench.cs, _reports/perf_100x.md) показали: когда работы есть,
    /// но ни одна не подходит (чужой тип/этап/инструмент), TryClaimJobForAgent обходил
    /// ВСЕ 64 макро-сектора × 64 чанка = до 4096 проб на ОДНОГО агента — суб-степ
    /// раздувался до 58–89 мс (p95 46 мс) при бюджете кадра 16.7 мс.
    /// Теперь пробы ограничены; старт обхода макро-секторов смещается на эпоху
    /// диспатча, поэтому за несколько диспатчей агент осматривает всю карту —
    /// «слепых зон» не появляется, но один вызов ограничен по времени.
    /// </summary>
    private const int MaxChunkProbesPerClaim = 384;

    private readonly object _registerLock = new();

    // SoA — основные поля задачи (индекс = jobId)
    private int[] _targetX;
    private int[] _targetY;
    private int[] _standX;
    private int[] _standY;
    private int[] _sourceX;
    private int[] _sourceY;
    private JobTypeId[] _typeId;
    private JobExecutionType[] _executionType;
    private JobPriorityTier[] _priorityTier;
    private ToolRequirement[] _requiredTool;
    private int[] _maxWorkers;
    private int[] _assignedWorkers;       // CAS-цель
    private int[] _jobFailCd;             // кулдаун недостижимости
    private int[] _jobCdCounted;          // P8-г: 1 = работа учтена в Cd-агрегатах (CAS-владение переходом; int — для Interlocked)
    private int[] _targetItemCount;
    private int[] _currentDeliveredCount;
    private ItemId[] _targetItemId;
    private float[] _workDuration;
    private bool[] _active;

    // FIX (bughunt 08-2/08-3/08-7): ПОКОЛЕНИЕ СЛОТА.
    // Слоты переиспользуются через free-list (LIFO), поэтому «сырой» jobId ничего
    // не говорит о том, ЧТО сейчас в слоте: снятая работа и следующая за ней имеют
    // один и тот же id. Поколение инкрементится при КАЖДОЙ выдаче слота (регистрации),
    // т.е. является штампом «какая именно работа занимает слот». Клейм/удаление/
    // релиз сверяют поколение (см. TryClaimInChunk/TryClaimJobById/RemoveJob), а
    // слот с живыми клеймами вообще не попадает в free-list (см. _pendingFree) —
    // так протухший id не может указывать на чужую живую работу.
    private uint[] _slotGen;

    // FIX (bughunt 08-6): «единица учёта unclaimed». 1 = работа посчитана в
    // _unclaimedCount/_chunkUnclaimedCount/_macroJobCounts. Переходы 1→0 (клейм
    // насытил работу / удаление) и 0→1 (release вернул слот) делает ТОЛЬКО победитель
    // CAS. Раньше RemoveJob считал признак обычным чтением _assignedWorkers и мог
    // декрементировать ПАРАЛЛЕЛЬНО с клеймом (двойное списание → минус в чанке,
    // который прячет все работы чанка). Теперь декремент строго парен своему
    // инкременту: проигравший гонку CAS ничего не списывает.
    private int[] _slotUnclaimed;

    // FIX (bughunt 08-2/08-3): 1 = работа удалена, но на слоте ещё висят клеймы
    // (агенты, которые держат этот id и придут с ним позже: Commit/OnCancel/stuck).
    // Пока флаг стоит, слот НЕ возвращается в free-list и не может быть выдан под
    // другую работу → «поздний» RemoveJob/ReleaseWorkerClaim по протухшему id
    // гарантированно попадает в МЁРТВЫЙ слот (no-op), а не в чужую живую работу.
    // Слот освобождает тот, кто отпустил последний клейм (DrainPendingClaim).
    private int[] _pendingFree;
    private int _pendingFreeCount;

    // FIX (bughunt 08-6): диагностика клампа — сколько раз декремент счётчика был
    // предотвращён клампом на 0 (в норме 0; рост означает потерянную пару
    // инкремент/декремент и всегда виден снаружи через PendingFreeCount/агрегаты).
    private int _unclaimedClampHits;
    private int _chunkUnclaimedClampHits;
    private int _macroClampHits;
    // Клампы отката самого клейма (_assignedWorkers): в норме 0 — откат идёт 1→0.
    private int _claimClampHits;

    // Индекс позиции внутри чанка (для O(1) удаления из чанка)
    private int[] _chunkJobIndex;

    // Free-list для переиспользования jobId
    private int[] _nextFree;
    private int _freeHead;
    private int _capacity;

    // L1: Per-chunk плотные массивы jobId (swap-and-pop)
    private int[][] _chunkJobs;
    private int[] _chunkCount;
    private int[] _chunkUnclaimedCount;

    // L2: 64-битная битовая маска активных макро-секторов
    private ulong _macroActiveMask;
    private int[] _macroJobCounts;

    // Position-карта для дедупликации
    private readonly Dictionary<(int X, int Y, JobTypeId Type), int> _posMap = new(InitialCapacity);

    // Счётчики
    private int _totalCount;
    private int _unclaimedCount;

    // Memo эпоха и per-dispatch memoization (S-033)
    private int _claimEpoch;
    private int[] _canExecEpoch;
    private bool[] _canExecResult;
    // J-12: memo обязан быть привязан к агенту. Ответ CanAgentExecute зависит от
    // инвентаря/инструментов/состояния конкретного агента, поэтому один и тот же
    // jobId в пределах эпохи давал разные корректные ответы для разных агентов —
    // мемо первого агента применялось ко всем остальным (работа «не подходит»
    // или наоборот выдавалась тому, кто её выполнить не может).
    private int[] _canExecAgent;

    // P8в (task-18): иерархический отрицательный ответ — агрегированные флаги кулдаунов.
    // Проблема: промах TryClaimJobForAgent при массовом MarkJobUnreachable обходил все
    // 1024 чанка × 64 кандидата = O(всех работ) ≈ 5.7мс на агента (замер P8а).
    // Решение: счётчик unclaimed-работ-на-кулдауне на чанк + min-время истечения.
    // Чанк сканируется только если (unclaimed > cdCount) ИЛИ (сейчас >= earliest-expire).
    // Инвариант: unclaimed-работа либо отмечена на кулдауне (посчитана), либо сканируема.
    // Истечение кулдауна лениво (IsJobOnCooldown), поэтому earliest — КОНСЕРВАТИВНАЯ
    // нижняя граница: устарев вниз, она лишь заставляет сканировать чанк раньше.
    private int[] _chunkCdCount;
    private int[] _chunkCdEarliest;
    private int _globalCdCount;
    private int _globalCdEarliest;

    // P8-г (drift fix): гейт замены массива CD-флагов. EnsureCapacity пересоздаёт
    // _jobCdCounted через Array.Resize, а копирование НЕатомарно относительно lock-free
    // CAS на элементах массива. CAS, попавший в окно копирования, терялся (единица учёта
    // остаётся без флага → вечная утечка +1) либо «воскресал» в новой копии уже снятый
    // флаг → повторный декремент того же id (дрейф ВНИЗ: globalCd/chunkCdSum < 0).
    // Оба направления наблюдались в tests/TestJobRaceStress. Гейт делает подмену массива
    // атомарной относительно мутаций флага; локов в claim-путях не добавляет:
    // мутатор — InFlight++ → чтение Quiesce; ресайзер — Quiesce=1 → ожидание InFlight==0.
    // Порядок «Dekker» (инкремент ДО чтения Quiesce, запись Quiesce ДО чтения InFlight)
    // гарантирует, что обе стороны одновременно «промахнуться» не могут.
    private int _cdFlagQuiesce;   // 1 = идёт замена массива CD-флагов
    private int _cdFlagInFlight;  // CD-мутации флага «в полёте»

    public int UnclaimedCount => Volatile.Read(ref _unclaimedCount);
    public int TotalCount => Volatile.Read(ref _totalCount);

    /// <summary>
    /// FIX (bughunt 08-6): диагностика счётчиков. В норме все три нули; ненулевое
    /// значение означает, что декремент был остановлен клампом на 0 (защита от
    /// ухода в минус, который прячет работы чанка/останавливает диспетчеризацию).
    /// </summary>
    internal int UnclaimedClampHits => Volatile.Read(ref _unclaimedClampHits);
    internal int ChunkUnclaimedClampHits => Volatile.Read(ref _chunkUnclaimedClampHits);
    internal int MacroClampHits => Volatile.Read(ref _macroClampHits);

    /// <summary>
    /// FIX (bughunt 08-2): сколько слотов удалено, но ещё ждёт дренажа клеймов
    /// (в free-list не входят). Растёт только на время «позднего» держателя id;
    /// постоянный рост означал бы утекший клейм (агент не вернул работу).
    /// </summary>
    internal int PendingFreeCount => Volatile.Read(ref _pendingFreeCount);

    public JobSpatialPyramid()
    {
        _capacity = InitialCapacity;
        AllocateArrays(_capacity);
        InitFreeList(_capacity);

        _chunkJobs = new int[ChunkCount][];
        _chunkCount = new int[ChunkCount];
        _chunkUnclaimedCount = new int[ChunkCount];
        for (int i = 0; i < ChunkCount; i++)
        {
            _chunkJobs[i] = new int[64];
        }

        _macroJobCounts = new int[MacroCount];
        _macroActiveMask = 0UL;

        // P8в: агрегированные флаги кулдаунов (1024 чанка — фиксированный размер).
        _chunkCdCount = new int[ChunkCount];
        _chunkCdEarliest = new int[ChunkCount];
        for (int i = 0; i < ChunkCount; i++)
            _chunkCdEarliest[i] = int.MaxValue;
        _globalCdCount = 0;
        _globalCdEarliest = int.MaxValue;
    }

    private void AllocateArrays(int capacity)
    {
        _targetX = new int[capacity];
        _targetY = new int[capacity];
        _standX = new int[capacity];
        _standY = new int[capacity];
        _sourceX = new int[capacity];
        _sourceY = new int[capacity];
        _typeId = new JobTypeId[capacity];
        _executionType = new JobExecutionType[capacity];
        _priorityTier = new JobPriorityTier[capacity];
        _requiredTool = new ToolRequirement[capacity];
        _maxWorkers = new int[capacity];
        _assignedWorkers = new int[capacity];
        _jobFailCd = new int[capacity];
        _jobCdCounted = new int[capacity];
        _targetItemCount = new int[capacity];
        _currentDeliveredCount = new int[capacity];
        _targetItemId = new ItemId[capacity];
        _workDuration = new float[capacity];
        _active = new bool[capacity];
        _slotGen = new uint[capacity];
        _slotUnclaimed = new int[capacity];
        _pendingFree = new int[capacity];
        _nextFree = new int[capacity];
        _chunkJobIndex = new int[capacity];
        _canExecEpoch = new int[capacity];
        _canExecResult = new bool[capacity];
        _canExecAgent = new int[capacity];
        Array.Fill(_canExecAgent, -1);
    }

    private void InitFreeList(int capacity)
    {
        for (int i = 0; i < capacity - 1; i++)
            _nextFree[i] = i + 1;
        _nextFree[capacity - 1] = -1;
        _freeHead = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetChunkIndexStatic(int tileX, int tileY)
    {
        // Индексы валидны (0..1023): JobValidator.IsTargetWalkable отбрасывает
        // работы c TargetX/TargetY >= MapWidth. Кламп больше не нужен — он
        // схлопывал всё, что >= 512, в последний чанк (баг J-05).
        int cx = tileX >> ChunkShift;
        int cy = tileY >> ChunkShift;
        return cy * ChunkDim + cx;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetMacroIndex(int tileX, int tileY)
    {
        // MacroShift=7 → macroIndex ∈ [0..63] для клетки в пределах острова,
        // поэтому `1UL << macroIndex` по-прежнему укладывается в один ulong.
        int mx = tileX >> MacroShift;
        int my = tileY >> MacroShift;
        return my * MacroDim + mx;
    }

    private void EnsureCapacity(int neededId)
    {
        if (neededId < _capacity) return;
        int newCap = Math.Max(_capacity * GrowFactor, neededId + 1024);
        Array.Resize(ref _targetX, newCap);
        Array.Resize(ref _targetY, newCap);
        Array.Resize(ref _standX, newCap);
        Array.Resize(ref _standY, newCap);
        Array.Resize(ref _sourceX, newCap);
        Array.Resize(ref _sourceY, newCap);
        Array.Resize(ref _typeId, newCap);
        Array.Resize(ref _executionType, newCap);
        Array.Resize(ref _priorityTier, newCap);
        Array.Resize(ref _requiredTool, newCap);
        Array.Resize(ref _maxWorkers, newCap);
        Array.Resize(ref _assignedWorkers, newCap);
        Array.Resize(ref _jobFailCd, newCap);
        // P8-г (drift fix): замена массива CD-флагов идёт через гейт — обычный Array.Resize
        // терял/воскрешал флаги при гонке с lock-free CAS (см. описание полей _cdFlag*).
        ResizeCdFlagArray(newCap);
        Array.Resize(ref _targetItemCount, newCap);
        Array.Resize(ref _currentDeliveredCount, newCap);
        Array.Resize(ref _targetItemId, newCap);
        Array.Resize(ref _workDuration, newCap);
        Array.Resize(ref _active, newCap);
        // FIX (bughunt 08-2/08-6/08-7): новые массивы слот-состояния растут вместе
        // с остальными; Array.Resize добивает новые элементы нулями: поколение 0 =
        // «слот ещё не выдавался», флаг unclaimed 0, pending 0.
        Array.Resize(ref _slotGen, newCap);
        Array.Resize(ref _slotUnclaimed, newCap);
        Array.Resize(ref _pendingFree, newCap);
        Array.Resize(ref _nextFree, newCap);
        Array.Resize(ref _chunkJobIndex, newCap);
        Array.Resize(ref _canExecEpoch, newCap);
        Array.Resize(ref _canExecResult, newCap);
        int oldCanExecAgentLen = _canExecAgent.Length;
        Array.Resize(ref _canExecAgent, newCap);
        for (int i = oldCanExecAgentLen; i < newCap; i++)
            _canExecAgent[i] = -1;

        for (int i = _capacity; i < newCap - 1; i++)
            _nextFree[i] = i + 1;
        _nextFree[newCap - 1] = _freeHead;
        _freeHead = _capacity;
        _capacity = newCap;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public JobData GetJobData(int jobId)
    {
        return new JobData
        {
            Id = jobId,
            TypeId = _typeId[jobId],
            ExecutionType = _executionType[jobId],
            PriorityTier = _priorityTier[jobId],
            RequiredTool = _requiredTool[jobId],
            SourceX = _sourceX[jobId],
            SourceY = _sourceY[jobId],
            TargetX = _targetX[jobId],
            TargetY = _targetY[jobId],
            StandX = _standX[jobId],
            StandY = _standY[jobId],
            TargetItemId = _targetItemId[jobId],
            TargetItemCount = _targetItemCount[jobId],
            CurrentDeliveredCount = _currentDeliveredCount[jobId],
            MaxWorkers = _maxWorkers[jobId],
            AssignedWorkers = Volatile.Read(ref _assignedWorkers[jobId]),
            WorkDuration = _workDuration[jobId],
            IsActive = _active[jobId]
        };
    }

    public void BeginClaimEpoch()
    {
        int next = unchecked(_claimEpoch + 1);
        if (next == int.MaxValue)
        {
            if (_canExecEpoch != null)
                Array.Clear(_canExecEpoch, 0, _canExecEpoch.Length);
            next = 1;
        }
        Volatile.Write(ref _claimEpoch, next);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetMemoCanExecute(int jobId, int agentIndex, out bool result)
    {
        int epoch = Volatile.Read(ref _claimEpoch);
        if (epoch != 0 && jobId >= 0 && jobId < _capacity &&
            Volatile.Read(ref _canExecEpoch[jobId]) == epoch &&
            _canExecAgent[jobId] == agentIndex)
        {
            result = _canExecResult[jobId];
            return true;
        }
        result = false;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetMemoCanExecute(int jobId, int agentIndex, bool result)
    {
        int epoch = Volatile.Read(ref _claimEpoch);
        if (epoch != 0 && jobId >= 0 && jobId < _capacity)
        {
            _canExecResult[jobId] = result;
            _canExecAgent[jobId] = agentIndex;
            Volatile.Write(ref _canExecEpoch[jobId], epoch);
        }
    }

    public int RegisterJob(JobData job)
    {
        lock (_registerLock)
        {
            var key = (job.TargetX, job.TargetY, job.TypeId);
            if (_posMap.TryGetValue(key, out int existingId))
                return existingId;

            int id;
            if (_freeHead != -1)
            {
                id = _freeHead;
                _freeHead = _nextFree[id];
                _nextFree[id] = -1;
            }
            else
            {
                id = _capacity;
                EnsureCapacity(id + 1);
                // P8-г (task-20) фикс: EnsureCapacity ставит в голову free-list ВСЕ новые
                // слоты, включая слот id, который мы занимаем этой работой. Без вытолкивания
                // слот оставался в free-list будучи активным → следующая после роста
                // регистрация переиспользовала ЖИВОЙ id (дубль: один id под двумя pos-ключами,
                // данные живой работы затирались). 3 роста ёмкости = 3 дубля в стресс-тесте.
                _freeHead = _nextFree[id];
                _nextFree[id] = -1;
            }

            // P8-г (drift fix): слот мог достаться «грязным». Раньше здесь стоял прямой
            // неатомарный `_jobCdCounted[id] = 0` — если у прежнего владельца слота единица
            // учёта ещё висела (например, Release-add прилетел после насыщения клеймом и
            // RemoveJob не снял её, потому что работа была claimed), сброс флага отрывал
            // флаг от счётчика: _chunkCdCount/_globalCdCount оставались +1 НАВСЕГДА, парного
            // декремента уже не случится (CAS 1→0 провалится). Дрейф ВВЕРХ → глобальный гейт
            // (cdGlobal >= unclaimed && now < earliest) прячет живые работы.
            // Теперь остаток снимается ПАРНО через CAS 1→0 и ДО перезаписи позиции, чтобы
            // декремент попал ровно в тот чанк, куда шёл инкремент. Чтение-фильтр перед
            // CAS-ом: в норме флаг уже 0 (все пути снятия парные), поэтому регистрация не
            // платит ни одного interlocked-а; если же add прилетел позже нашего чтения,
            // флаг останется 1 и единицу снимет любой следующий clear/claim/remove этого id
            // (без флага она не «залипает»).
            if (Volatile.Read(ref _jobCdCounted[id]) != 0)
                CdCountingTryRemove(id);

            _targetX[id] = job.TargetX;
            _targetY[id] = job.TargetY;
            _standX[id] = job.StandX;
            _standY[id] = job.StandY;
            _sourceX[id] = job.SourceX;
            _sourceY[id] = job.SourceY;
            _typeId[id] = job.TypeId;
            _executionType[id] = job.ExecutionType;
            _priorityTier[id] = job.PriorityTier;
            _requiredTool[id] = job.RequiredTool;
            _maxWorkers[id] = Math.Max(1, job.MaxWorkers);
            _assignedWorkers[id] = 0;
            _jobFailCd[id] = 0;
            _targetItemCount[id] = job.TargetItemCount;
            // FIX (bughunt 08-5): прогресс доставки НЕ обнуляем слепо. Раньше здесь
            // стоял `_currentDeliveredCount[id] = 0`, и пересоздание blueprint-работы
            // (ReconcileTick/JobValidator после снятия) начинало счёт с нуля при полном
            // TargetItemCount → носильщики тащили лишние брёвна, сайт их клампил
            // (ConstructionSite.AddDelivered), а излишек исчезал. Теперь вызывающий
            // передаёт уже доставленное в JobData.CurrentDeliveredCount (JobBroker берёт
            // его из сайта — «по ключу цели»), а мы клампим в [0, TargetItemCount].
            int targetItems = job.TargetItemCount;
            int carried = job.CurrentDeliveredCount;
            if (carried < 0) carried = 0;
            if (targetItems > 0 && carried > targetItems) carried = targetItems;
            if (targetItems <= 0) carried = 0;
            _currentDeliveredCount[id] = carried;
            _targetItemId[id] = job.TargetItemId;
            _workDuration[id] = job.WorkDuration;
            // FIX (bughunt 08-2/08-7): публикуем живость волатильно — её читают claim/release
            // с других потоков без лока.
            Volatile.Write(ref _active[id], true);

            // FIX (bughunt 08-2/08-3/08-6): слот выдан заново — новое поколение,
            // свежая единица unclaimed-учёта, флаг дренажа снят (слот пришёл из
            // free-list, т.е. предыдущая работа уже была освобождена полностью).
            unchecked { _slotGen[id]++; }
            Volatile.Write(ref _slotUnclaimed[id], 1);
            Volatile.Write(ref _pendingFree[id], 0);

            _posMap[key] = id;
            _totalCount++;
            Interlocked.Increment(ref _unclaimedCount);

            // Вставка в чанк L1
            int chunkIndex = GetChunkIndexStatic(job.TargetX, job.TargetY);
            ref var cJobs = ref _chunkJobs[chunkIndex];
            int count = _chunkCount[chunkIndex];
            if (count >= cJobs.Length)
            {
                Array.Resize(ref cJobs, cJobs.Length * 2);
            }
            cJobs[count] = id;
            _chunkJobIndex[id] = count;
            _chunkCount[chunkIndex] = count + 1;
            Interlocked.Increment(ref _chunkUnclaimedCount[chunkIndex]);

            // Обновление L2 макро-маски.
            // Счётчик мутируется и ВНЕ _registerLock (Interlocked в ReleaseWorkerClaim /
            // TryClaimJobForAgent), поэтому обычный ++ терял обновления, счётчик уезжал
            // в 0 при живых задачах и бит L2-маски гас — агенты переставали видеть работу
            // в целом макро-секторе. Маску пишем под локом (все её записи — под ним).
            int macroIndex = GetMacroIndex(job.TargetX, job.TargetY);
            Interlocked.Increment(ref _macroJobCounts[macroIndex]);
            _macroActiveMask |= (1UL << macroIndex);

            return id;
        }
    }

    public void RegisterBatch(List<JobData> jobs)
    {
        if (jobs == null || jobs.Count == 0) return;
        lock (_registerLock)
        {
            for (int i = 0; i < jobs.Count; i++)
            {
                RegisterJobLocked(jobs[i]);
            }
        }
    }

    private void RegisterJobLocked(JobData job)
    {
        var key = (job.TargetX, job.TargetY, job.TypeId);
        if (_posMap.ContainsKey(key))
            return;

        int id;
        if (_freeHead != -1)
        {
            id = _freeHead;
            _freeHead = _nextFree[id];
            _nextFree[id] = -1;
        }
        else
        {
            id = _capacity;
            EnsureCapacity(id + 1);
            // P8-г (task-20) фикс: тот же баг, что в RegisterJob — EnsureCapacity
            // оставляет занимаемый слот в голове free-list → дубль id после роста.
            _freeHead = _nextFree[id];
            _nextFree[id] = -1;
        }

        // P8-г (drift fix): то же, что в RegisterJob — никакого прямого `_jobCdCounted[id] = 0`.
        // Остаток учёта прежнего владельца снимается ПАРНО и до перезаписи позиции.
        if (Volatile.Read(ref _jobCdCounted[id]) != 0)
            CdCountingTryRemove(id);

        _targetX[id] = job.TargetX;
        _targetY[id] = job.TargetY;
        _standX[id] = job.StandX;
        _standY[id] = job.StandY;
        _sourceX[id] = job.SourceX;
        _sourceY[id] = job.SourceY;
        _typeId[id] = job.TypeId;
        _executionType[id] = job.ExecutionType;
        _priorityTier[id] = job.PriorityTier;
        _requiredTool[id] = job.RequiredTool;
        _maxWorkers[id] = Math.Max(1, job.MaxWorkers);
        _assignedWorkers[id] = 0;
        _jobFailCd[id] = 0;
        _targetItemCount[id] = job.TargetItemCount;
        // FIX (bughunt 08-5): то же, что в RegisterJob — прогресс доставки берём из
        // JobData (клампим в [0, TargetItemCount]), а не обнуляем.
        int targetItems = job.TargetItemCount;
        int carried = job.CurrentDeliveredCount;
        if (carried < 0) carried = 0;
        if (targetItems > 0 && carried > targetItems) carried = targetItems;
        if (targetItems <= 0) carried = 0;
        _currentDeliveredCount[id] = carried;
        _targetItemId[id] = job.TargetItemId;
        _workDuration[id] = job.WorkDuration;
        Volatile.Write(ref _active[id], true);

        // FIX (bughunt 08-2/08-3/08-6): новое поколение слота + свежий unclaimed-учёт.
        unchecked { _slotGen[id]++; }
        Volatile.Write(ref _slotUnclaimed[id], 1);
        Volatile.Write(ref _pendingFree[id], 0);

        _posMap[key] = id;
        _totalCount++;
        Interlocked.Increment(ref _unclaimedCount);

        int chunkIndex = GetChunkIndexStatic(job.TargetX, job.TargetY);
        ref var cJobs = ref _chunkJobs[chunkIndex];
        int count = _chunkCount[chunkIndex];
        if (count >= cJobs.Length)
        {
            Array.Resize(ref cJobs, Math.Max(cJobs.Length * 2, count + 64));
        }
        cJobs[count] = id;
        _chunkJobIndex[id] = count;
        _chunkCount[chunkIndex] = count + 1;
        Interlocked.Increment(ref _chunkUnclaimedCount[chunkIndex]);

        int macroIndex = GetMacroIndex(job.TargetX, job.TargetY);
        Interlocked.Increment(ref _macroJobCounts[macroIndex]);
        _macroActiveMask |= (1UL << macroIndex);
    }

    /// <summary>
    /// Удаление работы по id. FIX (bughunt 08-2): это путь ЗАВЕРШЕНИЯ — так работу
    /// снимает хендлер, который её же и выполнял (TryUnregisterJob в
    /// TreeChopping/Mining/Harvest/Farming/Planting/Construction и т.п.), либо аудит
    /// бесхозных работ (JobValidator снимает только AssignedWorkers == 0). Поэтому
    /// удаление «съедает» ровно один клейм вызывающего (retireClaim) и не оставляет
    /// слот в дренаже. Внешнее удаление (снятие метки/зоны/чертежа) идёт по ПОЗИЦИИ —
    /// там клейм работающего агента не трогаем (см. RemoveJobByPos).
    /// ВАЖНО: вызывающий по id обязан быть держателем клейма этой работы (или работа
    /// должна быть unclaimed). Иначе (например, UnregisterJob(job.Id) из чужого
    /// хендлера) будет снят ЧУЖОЙ клейм — для внешних снятий используйте
    /// RemoveJobByPos/RemoveBatchByPositions.
    /// Сверка поколения доступна в строгой перегрузке RemoveJob(jobId, generation, ...).
    /// </summary>
    public bool RemoveJob(int jobId, out JobData removedJob) => RemoveJobCore(jobId, true, out removedJob);

    /// <summary>
    /// FIX (bughunt 08-2): строгий вариант удаления — вызывающий предъявляет поколение
    /// слота, которое он видел при клейме (JobSpatialPyramid.GetSlotGeneration).
    /// Если слот уже переиспользован под другую работу (поколение не совпало), удаления
    /// НЕ происходит. Публичную сигнатуру RemoveJob(jobId, out) не меняем: старые
    /// вызывающие защищены инвариантом «слот с клеймами не переиспользуется».
    /// </summary>
    public bool RemoveJob(int jobId, uint expectedGeneration, out JobData removedJob)
    {
        lock (_registerLock)
        {
            if (jobId < 0 || jobId >= _capacity || !_active[jobId] ||
                _slotGen[jobId] != expectedGeneration)
            {
                removedJob = default;
                return false;
            }
            return RemoveJobCore(jobId, true, out removedJob);
        }
    }

    /// <summary>FIX (bughunt 08-2): поколение слота (0 — слот ещё не выдавался).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint GetSlotGeneration(int jobId)
    {
        return (uint)jobId < (uint)_capacity ? Volatile.Read(ref _slotGen[jobId]) : 0u;
    }

    private bool RemoveJobCore(int jobId, bool retireClaim, out JobData removedJob)
    {
        lock (_registerLock)
        {
            if (jobId < 0 || jobId >= _capacity || !_active[jobId])
            {
                removedJob = default;
                return false;
            }

            removedJob = GetJobData(jobId);
            // FIX (bughunt 08-2/08-7): снятие живости обязано быть видно другим потокам
            // ДО любых проверок протухшего id (см. ReleaseWorkerClaim/RemoveJobCore).
            Volatile.Write(ref _active[jobId], false);

            var key = (_targetX[jobId], _targetY[jobId], _typeId[jobId]);
            _posMap.Remove(key);
            _totalCount--;

            // FIX (bughunt 08-6): «unclaimed» забираем CAS-переходом 1→0. Только один
            // из двух участников (клейм насытил работу / удаление) списывает единицу.
            // Раньше признак читался обычным сравнением assigned < max и декремент мог
            // пройти ДВАЖДЫ (удаление успевало прочитать assigned==0 до CAS клейма) —
            // у чанка счётчик уходил в минус, и ChunkMayHaveClaimable навсегда прятал
            // все его работы; у глобального счётчика диспетчер вообще переставал раздавать.
            bool wasUnclaimed = UnclaimedTake(jobId);

            // P8-г (drift fix): снятие Cd-учёта БЕЗУСЛОВНО (не только под wasUnclaimed) и
            // циклом. Раньше единица, прилетевшая через Release-add/Mark-add в окне гонки
            // уже ПОСЛЕ насыщения работы клеймом, переживала удаление: у мёртвого id оставался
            // +1 в _chunkCdCount/_globalCdCount, снять его было некому (ClearJobCooldown по
            // удалённому id никто не зовёт) → вечный фантом, который раздувает глобальный
            // гейт и позволяет объявить «все работы на кулдауне» при живых работах.
            // CAS 1→0 идемпотентен, поэтому безусловный вызов безопасен; цикл (обычно 1
            // итерация) добивает add, проскочивший между снятием и удалением.
            for (int guard = 0; guard < 4 && CdCountingTryRemove(jobId); guard++) { }

            if (wasUnclaimed)
            {
                DecClamp0(ref _unclaimedCount, ref _unclaimedClampHits);
            }

            // Удаление из L1 чанка за O(1) swap-and-pop
            int chunkIndex = GetChunkIndexStatic(removedJob.TargetX, removedJob.TargetY);
            var cJobs = _chunkJobs[chunkIndex];
            int count = _chunkCount[chunkIndex];
            int idx = _chunkJobIndex[jobId];

            if (idx >= 0 && idx < count)
            {
                int lastIdx = count - 1;
                int lastJobId = cJobs[lastIdx];

                cJobs[idx] = lastJobId;
                _chunkJobIndex[lastJobId] = idx;
                cJobs[lastIdx] = -1;

                _chunkCount[chunkIndex] = lastIdx;
                if (wasUnclaimed)
                {
                    // FIX (bughunt 08-6): кламп на 0 — минус в чанке прячет ВСЕ его
                    // работы (ChunkMayHaveClaimable при <= 0 всегда false).
                    DecClamp0(ref _chunkUnclaimedCount[chunkIndex], ref _chunkUnclaimedClampHits);
                }
            }
            else if (wasUnclaimed)
            {
                // Индекс чанка разошёлся (не должно случаться) — единицу всё равно
                // списываем, иначе чанк навсегда останется «с работой».
                DecClamp0(ref _chunkUnclaimedCount[chunkIndex], ref _chunkUnclaimedClampHits);
            }

            // Обновление L2 макро-маски: декрементируем только если задача была unclaimed.
            // Атомарный декремент с клампом на 0 (семантика Math.Max(0, x-1) без гонки):
            // обычный RMW под _registerLock НЕ сериализуется против Interlocked-мутаций
            // из ReleaseWorkerClaim и TryClaimJobForAgent → lost update.
            if (wasUnclaimed)
            {
                MacroRemove(GetMacroIndex(removedJob.TargetX, removedJob.TargetY));
            }

            // FIX (bughunt 08-2/08-3): слот возвращается в free-list ТОЛЬКО когда на нём
            // не осталось клеймов. Раньше он уходил туда всегда, даже если агент ещё
            // работал по этому id (работа снята извне — метку/зону/чертёж убрали).
            // Слот тут же выдавался под другую работу, и «поздний» Commit агента вызы-
            // вал RemoveJob/ReleaseWorkerClaim по тому же id — убивал/размыкал уже ЧУЖУЮ
            // живую работу. Теперь такой слот помечается pending и освобождается тем,
            // кто отпустит последний клейм (DrainPendingClaim в ReleaseWorkerClaim).
            if (retireClaim && Volatile.Read(ref _assignedWorkers[jobId]) > 0)
            {
                // Удаление по id = вызывающий закончил с этой работой: снимаем ЕГО клейм.
                // Остаточный риск: если хендлер после успешного unregister ещё и вызовет
                // ReleaseWorkerClaim по тому же id (двойной релиз, см. StockpileHauling),
                // второй вызов в микроскопическом окне может попасть уже в переисполь-
                // зованный слот. Полностью закрывается только строгой перегрузкой с
                // поколением (GetSlotGeneration + RemoveJob/ReleaseWorkerClaim(gen)).
                DecClamp0(ref _assignedWorkers[jobId], ref _claimClampHits);
            }

            if (Volatile.Read(ref _assignedWorkers[jobId]) <= 0)
            {
                // Счётчик уже 0 (DecClamp0 не уходит в минус) — слот свободен.
                PushFreeSlotLocked(jobId);
            }
            else
            {
                if (_pendingFree[jobId] == 0)
                {
                    _pendingFree[jobId] = 1;
                    Interlocked.Increment(ref _pendingFreeCount);
                }
            }

            return true;
        }
    }

    public bool RemoveJobByPos(int x, int y, JobTypeId type, out JobData removedJob)
    {
        lock (_registerLock)
        {
            if (_posMap.TryGetValue((x, y, type), out int id))
            {
                // FIX (bughunt 08-2): внешнее удаление (снятие метки/зоны/чертежа) —
                // клейм работающего агента не трогаем, слот уйдёт в дренаж.
                return RemoveJobCore(id, false, out removedJob);
            }
            removedJob = default;
            return false;
        }
    }

    public void RemoveBatchByPositions(List<(int X, int Y)> positions, JobTypeId type)
    {
        if (positions == null || positions.Count == 0) return;
        lock (_registerLock)
        {
            for (int i = 0; i < positions.Count; i++)
            {
                var pos = positions[i];
                if (_posMap.TryGetValue((pos.X, pos.Y, type), out int id))
                {
                    // FIX (bughunt 08-2): внешнее удаление — без снятия чужого клейма.
                    RemoveJobCore(id, false, out _);
                }
            }
        }
    }

    public bool TryGetJob(int id, out JobData job)
    {
        if (id < 0 || id >= _capacity || !_active[id])
        {
            job = default;
            return false;
        }
        job = GetJobData(id);
        return true;
    }

    public bool TryGetJobByPos(int x, int y, JobTypeId type, out JobData job)
    {
        lock (_registerLock)
        {
            if (_posMap.TryGetValue((x, y, type), out int id))
            {
                job = GetJobData(id);
                return true;
            }
            job = default;
            return false;
        }
    }

    /// <summary>
    /// §119 P1: дешёвый lookup id по позиции (для пуша роутеру зон после
    /// Register*Job, когда JobData уже записан в реестр). Возвращает -1, если
    /// работы по позиции+типу нет. GetJobData НЕ строится — дешевле TryGetJobByPos.
    /// </summary>
    public int TryGetJobIdByPos(int x, int y, JobTypeId type, out bool found)
    {
        lock (_registerLock)
        {
            if (_posMap.TryGetValue((x, y, type), out int id) && id >= 0 && id < _capacity && _active[id])
            {
                found = true;
                return id;
            }
            found = false;
            return -1;
        }
    }

    public bool HasJobAt(int x, int y, JobTypeId type)
    {
        lock (_registerLock)
        {
            return _posMap.TryGetValue((x, y, type), out int id) && id >= 0 && id < _capacity && _active[id];
        }
    }

    public int GetChunkJobCount(int chunkIndex)
    {
        if (chunkIndex < 0 || chunkIndex >= ChunkCount) return 0;
        return Volatile.Read(ref _chunkUnclaimedCount[chunkIndex]);
    }

    /// <summary>
    /// Освобождение слота задачи (агент освободился или был отменен).
    /// FIX (bughunt 08-3): по протухшему id НЕЛЬЗЯ снимать клейм с чужой работы.
    /// Инвариант слота: пока на нём висят клеймы, он не возвращается в free-list
    /// (см. _pendingFree/RemoveJobCore), поэтому «поздний» release по снятому id
    /// попадает в МЁРТВЫЙ слот и лишь дренирует его клеймы — новую работу он не
    /// увидит. Для полностью живого слота дополнительно сверяется поколение
    /// (строгая перегрузка ReleaseWorkerClaim(jobId, generation)).
    /// </summary>
    public void ReleaseWorkerClaim(int jobId)
    {
        if (jobId < 0 || jobId >= _capacity)
            return;

        // FIX (bughunt 08-3): поколение слота на входе. Если за время вызова слот
        // успели переиспользовать, наш id уже относится к другой работе — учёт
        // unclaimed трогать нельзя (иначе спишем единицу чужой работы).
        uint gen = Volatile.Read(ref _slotGen[jobId]);

        // FIX (bughunt 08-2/08-3): слот уже мёртв — это поздний release по stale-id
        // (работу сняли извне, пока агент её выполнял). Учёт unclaimed уже снят
        // RemoveJob-ом, поэтому здесь только дренаж: снимаем клейм и, если он был
        // последним, возвращаем слот в free-list.
        if (!Volatile.Read(ref _active[jobId]))
        {
            DrainPendingClaim(jobId);
            return;
        }

        // Кламп на 0 через CAS: слепой Decrement уводил счётчик в минус, и следующая
        // CAS-атака (while cur < _maxWorkers) выдавала ВТОРОЙ claim на задачу с MaxWorkers=1.
        int cur;
        do
        {
            cur = Volatile.Read(ref _assignedWorkers[jobId]);
            if (cur <= 0)
                return;
        }
        while (Interlocked.CompareExchange(ref _assignedWorkers[jobId], cur - 1, cur) != cur);

        int newAssigned = cur - 1;

        // FIX (bughunt 08-2): работа умерла в окне между чтением _active и декрементом —
        // единицу unclaimed уже забрал RemoveJob, поэтому только дренаж мёртвого слота.
        if (!Volatile.Read(ref _active[jobId]))
        {
            CompleteClaimRelease(jobId, newAssigned);
            return;
        }

        if (newAssigned == _maxWorkers[jobId] - 1)
        {
            // FIX (bughunt 08-3): слот переиспользован, пока мы считали клейм —
            // возвращать «unclaimed» уже другой работе нельзя.
            if (Volatile.Read(ref _slotGen[jobId]) != gen)
                return;

            // FIX (bughunt 08-6): переход 0→1 «работа снова учтена как unclaimed»
            // делает ровно один победитель CAS. Без владения release мог добавить
            // единицу ПАРАЛЛЕЛЬНО с RemoveJob-ом (тот уже списал) → фантомный +1.
            if (!UnclaimedGive(jobId))
                return;

            Interlocked.Increment(ref _unclaimedCount);
            int ci = GetChunkIndexStatic(_targetX[jobId], _targetY[jobId]);
            Interlocked.Increment(ref _chunkUnclaimedCount[ci]);

            // P8в/P8-г: работа вернулась в unclaimed с активным кулдауном — учитываем
            // в флагах; CAS 0→1 гарантирует ровно один +1 при параллельном mark/clear.
            // Гонка «Release-add ↔ Claim забрал последний слот»: add мог сработать ПОСЛЕ
            // клейма → единица осталась на claimed-работе (фантом). Recheck после add:
            // если работу тут же заклеймили — снимаем свой +1; если клейм случится позже,
            // его собственный TryRemove снимет нашу единицу (counted=1 уже стоит).
            // P8-г (drift fix): recheck расширен до ПОЛНОЙ пригодности учёта
            // (unclaimed + active + cd != 0). Порядок доказательства: и клейм (CAS
            // _assignedWorkers), и RemoveJob (_active=false), и Clear (_jobFailCd=0)
            // выставляют своё состояние ДО собственного CdCountingTryRemove. Если их
            // remove отработал раньше нашего add, то наш add идёт после их перехода, а
            // recheck — после add, поэтому Volatile-чтения обязательно видят завершённый
            // переход и снимают «позднюю» единицу. Без этого единица навсегда оставалась
            // на мёртвом/заклеймлённом/уже-сброшенном id (фантом раздувает _globalCdCount
            // и глобальный гейт начинает прятать живые работы — ИНВАРИАНТ(а) стресс-теста).
            if (_jobFailCd[jobId] != 0 && CdCountingTryAdd(jobId) &&
                (Volatile.Read(ref _assignedWorkers[jobId]) >= _maxWorkers[jobId] ||
                 !Volatile.Read(ref _active[jobId]) ||
                 Volatile.Read(ref _jobFailCd[jobId]) == 0))
            {
                CdCountingTryRemove(jobId);
            }

            MacroAdd(GetMacroIndex(_targetX[jobId], _targetY[jobId]));

            // FIX (bughunt 08-6): повторная проверка после add. Работа могла быть
            // удалена/заклеймлена между проверкой _active и переходом 0→1 — тогда наша
            // единица «поздняя»: забираем её обратно CAS-ом 1→0 и снимаем счётчики.
            // Порядок «сначала добавили, потом проверили» гарантирует, что чужая
            // RemoveTake (1→0) уже случилась и проиграет нам только один из двух.
            if (!Volatile.Read(ref _active[jobId]))
            {
                if (Volatile.Read(ref _slotGen[jobId]) == gen && UnclaimedTake(jobId))
                {
                    DecClamp0(ref _unclaimedCount, ref _unclaimedClampHits);
                    DecClamp0(ref _chunkUnclaimedCount[ci], ref _chunkUnclaimedClampHits);
                    MacroRemove(GetMacroIndex(_targetX[jobId], _targetY[jobId]));
                }

                // FIX (bughunt 08-2): работа умерла уже после нашего декремента, и
                // RemoveJob мог увидеть assigned > 0 и оставить слот в дренаже. Наш
                // декремент сделал его нулевым — добиваем дренаж, иначе слот останется
                // pending навсегда (утечка ёмкости).
                CompleteClaimRelease(jobId, Volatile.Read(ref _assignedWorkers[jobId]));
            }
        }
    }

    /// <summary>
    /// FIX (bughunt 08-3): строгий вариант release — вызывающий предъявляет поколение
    /// слота, которое видел при клейме. Если слот уже переиспользован под другую работу,
    /// release становится no-op (чужой клейм не снимается).
    /// </summary>
    public void ReleaseWorkerClaim(int jobId, uint expectedGeneration)
    {
        if (jobId < 0 || jobId >= _capacity)
            return;
        if (Volatile.Read(ref _active[jobId]) && Volatile.Read(ref _slotGen[jobId]) != expectedGeneration)
            return;
        ReleaseWorkerClaim(jobId);
    }

    /// <summary>
    /// FIX (bughunt 08-2): дренаж мёртвого слота. Вызывается ТОЛЬКО когда _active[jobId]
    /// уже false: снимает клейм «позднего» держателя id и, когда держателей не осталось,
    /// возвращает слот в free-list. До этого момента слот не переиспользуется, поэтому
    /// stale-id не может указывать на чужую живую работу.
    /// </summary>
    private void DrainPendingClaim(int jobId)
    {
        lock (_registerLock)
        {
            if (_pendingFree[jobId] == 0)
                return; // слот уже освобождён (или никогда не ждал дренажа) — no-op
            // FIX (bughunt 08-3): декремент атомарный (CAS-кламп) — параллельный клейм
            // того же слота не должен теряться из-за read+write под локом.
            if (DecClamp0(ref _assignedWorkers[jobId], ref _claimClampHits) > 0)
                return;
            PushFreeSlotLocked(jobId);
        }
    }

    /// <summary>FIX (bughunt 08-2): тот же дренаж, но клейм уже снят вызывающим (newAssigned).</summary>
    private void CompleteClaimRelease(int jobId, int remainingClaims)
    {
        if (remainingClaims > 0)
            return;
        lock (_registerLock)
        {
            if (_pendingFree[jobId] == 0)
                return;
            if (Volatile.Read(ref _assignedWorkers[jobId]) > 0)
                return;
            PushFreeSlotLocked(jobId);
        }
    }

    /// <summary>
    /// FIX (bughunt 08-2): единственная точка возврата слота в free-list. Слот попадает
    /// туда только без живых клеймов; флаг дренажа снимается, счётчик pending падает.
    /// Вызывать под _registerLock.
    /// </summary>
    private void PushFreeSlotLocked(int jobId)
    {
        if (_pendingFree[jobId] != 0)
        {
            _pendingFree[jobId] = 0;
            Interlocked.Decrement(ref _pendingFreeCount);
        }
        _nextFree[jobId] = _freeHead;
        _freeHead = jobId;
    }

    // ---------------- FIX (bughunt 08-6): владение переходами unclaimed ---------------- //

    /// <summary>Взять переход «unclaimed → посчитано-как-занятое». true — мы владелец единицы.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool UnclaimedTake(int jobId) =>
        Interlocked.CompareExchange(ref _slotUnclaimed[jobId], 0, 1) == 1;

    /// <summary>Взять переход «занято → unclaimed». true — мы владелец новой единицы.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool UnclaimedGive(int jobId) =>
        Interlocked.CompareExchange(ref _slotUnclaimed[jobId], 1, 0) == 0;

    /// <summary>FIX (bughunt 08-6): декремент с клампом на 0 и диагностикой.</summary>
    private static int DecClamp0(ref int location, ref int clampHits)
    {
        int cur = Volatile.Read(ref location);
        while (cur > 0)
        {
            int prev = Interlocked.CompareExchange(ref location, cur - 1, cur);
            if (prev == cur)
                return cur - 1;
            cur = prev;
        }
        Interlocked.Increment(ref clampHits);
        return 0;
    }

    /// <summary>FIX (bughunt 08-6): +1 unclaimed-работы в макро-сектор и зажжение бита L2.</summary>
    private void MacroAdd(int macroIndex)
    {
        Interlocked.Increment(ref _macroJobCounts[macroIndex]);
        lock (_registerLock)
        {
            _macroActiveMask |= (1UL << macroIndex);
        }
    }

    /// <summary>
    /// FIX (bughunt 08-6): −1 unclaimed-работы из макро-сектора с клампом на 0. Бит
    /// гасим только если счётчик реально дошёл до нуля, и перепроверяем под локом
    /// (как в TryClaimJobForAgent), иначе погасим сектор с живыми задачами.
    /// </summary>
    private void MacroRemove(int macroIndex)
    {
        int left = DecClamp0(ref _macroJobCounts[macroIndex], ref _macroClampHits);
        if (left <= 0)
        {
            lock (_registerLock)
            {
                if (Volatile.Read(ref _macroJobCounts[macroIndex]) <= 0)
                    _macroActiveMask &= ~(1UL << macroIndex);
            }
        }
    }

    public void MarkJobUnreachable(int jobId, int cooldownMs = 5000)
    {
        if (jobId < 0 || jobId >= _capacity || !_active[jobId])
            return;
        int expire = unchecked((int)System.Environment.TickCount + Math.Max(0, cooldownMs));

        // P8-г: владение переходом через CAS на самом cd: только установивший cd с 0
        // добавляет единицу учёта. Если cd уже != 0 — либо единица уже учтена, либо её
        // concurrently забрал клейм (тогда мы НЕ добавим: окно останется консервативным
        // «лишний скан», спрятать работу этим нельзя). Повторная отметка продлевает окно
        // обычной записью: гонка с Clear даст cd==0 при непустом cd-счётчике → лишь
        // лишний скан, консервативно в обе стороны.
        if (Interlocked.CompareExchange(ref _jobFailCd[jobId], expire, 0) == 0)
        {
            // P8-г: add ТОЛЬКО если работа пребывает unclaimed и активна. Фантомная
            // единица «Mark-add на удалённую/заклеймлённую работу» исключается
            // повторной проверкой после add: снимаем свой +1 лишь если мы владелец
            // (TryAdd вернул true) и работа стала claimed/мёртвой. Чужую единицу
            // (Release-add уже стоял) не трогаем — её владельцем остаётся Release.
            // P8-г (drift fix): в recheck добавлен cd == 0 (параллельный Clear мог снять
            // кулдаун и свой TryRemove ещё ДО нашего add) — иначе на живой работе оставался
            // «поздний» фантом с нулевым кулдауном.
            if (Volatile.Read(ref _assignedWorkers[jobId]) < _maxWorkers[jobId] &&
                Volatile.Read(ref _active[jobId]) &&
                CdCountingTryAdd(jobId))
            {
                if (!Volatile.Read(ref _active[jobId]) ||
                    Volatile.Read(ref _assignedWorkers[jobId]) >= _maxWorkers[jobId] ||
                    Volatile.Read(ref _jobFailCd[jobId]) == 0)
                {
                    CdCountingTryRemove(jobId);
                }
            }
        }
        else
        {
            _jobFailCd[jobId] = expire;
        }
    }

    public void ClearJobCooldown(int jobId)
    {
        if (jobId >= 0 && jobId < _capacity)
        {
            _jobFailCd[jobId] = 0;

            // P8-г: снятие учёта ПОЛНОСТЬЮ безусловно — CAS 1→0 идемпотентен. Проверки
            // _active/assigned здесь недопустимы: гонка «Release-add ↔ RemoveJob-TryRemove»
            // может оставить единицу на мёртвом id (Remove снял, Release-add вернул),
            // и фильтр _active делал фантом неснимаемым (вечный дрейф +1).
            // P8-г (drift fix): снимаем НЕ ОДНУ, а все единицы этого id — Mark/Release-add
            // может прилететь позже нашего CAS-а. Цикл (обычно 1 итерация) гарантирует
            // инвариант «после снятия всех кулдаунов счётчики ровно 0».
            for (int guard = 0; guard < 4 && CdCountingTryRemove(jobId); guard++) { }
        }
    }

    public void ClearJobCooldownAt(int x, int y, JobTypeId type)
    {
        lock (_registerLock)
        {
            if (_posMap.TryGetValue((x, y, type), out int id) && id >= 0 && id < _capacity)
            {
                ClearJobCooldown(id);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsJobOnCooldown(int jobId)
    {
        int expire = _jobFailCd[jobId];
        if (expire == 0) return false;
        return unchecked(expire - (int)System.Environment.TickCount) > 0;
    }

    // ---------------- P8в: иерархический отрицательный ответ ---------------- //

    /// <summary>
    /// task-20 (P8-г): тестовый аксессор агрегатов кулдаунов. Инвариант:
    /// _globalCdCount == Σ _chunkCdCount, Σ chunkUnclaimed == глобальный unclaimed.
    /// </summary>
    internal void GetCdAggregatesForTest(out int globalCdCount, out long chunkCdSum,
        out long chunkUnclaimedSum, out int globalUnclaimed)
    {
        long cd = 0, uncl = 0;
        for (int c = 0; c < ChunkCount; c++)
        {
            cd += Volatile.Read(ref _chunkCdCount[c]);
            uncl += Volatile.Read(ref _chunkUnclaimedCount[c]);
        }
        globalCdCount = Volatile.Read(ref _globalCdCount);
        chunkCdSum = cd;
        chunkUnclaimedSum = uncl;
        globalUnclaimed = Volatile.Read(ref _unclaimedCount);
    }

    /// <summary>
    /// P8в: может ли чанк предложить заявку к кандидату без полного скана?
    /// false гарантированно означает «сканирование не нашло бы ничего»:
    /// нет unclaimed, либо все unclaimed отмечены на кулдауне и ни один
    /// кулдаун не истёк (now &lt; min-expire). Иначе — сканируем как раньше.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool ChunkMayHaveClaimable(int chunkIndex, int now)
    {
        int unclaimed = Volatile.Read(ref _chunkUnclaimedCount[chunkIndex]);
        if (unclaimed <= 0)
            return false;

        int cd = Volatile.Read(ref _chunkCdCount[chunkIndex]);
        if (cd <= 0 || cd < unclaimed)
            return true; // есть unclaimed-работы БЕЗ кулдауна

        // Все unclaimed отмечены на кулдауне: сканируем только если
        // хотя бы один кулдаун мог истечь (earliest — консервативная min-граница).
        return now >= Volatile.Read(ref _chunkCdEarliest[chunkIndex]);
    }

    private static void CasMin(ref int location, int value)
    {
        int cur = Volatile.Read(ref location);
        while (value < cur)
        {
            int prev = Interlocked.CompareExchange(ref location, value, cur);
            if (prev == cur)
                break;
            cur = prev;
        }
    }

    private static void CasDecClamp0(ref int location)
    {
        int cur = Volatile.Read(ref location);
        while (cur > 0 && Interlocked.CompareExchange(ref location, cur - 1, cur) != cur)
            cur = Volatile.Read(ref location);
    }

    /// <summary>
    /// P8-г (task-20): CAS-владельное добавление работы в Cd-агрегаты.
    /// Возвращает true если переход 0→1 выполнен ИМЕННО здесь (ровно один поток
    /// получает +1 на пару счётчиков). Повторные mark не добавляют единицу —
    /// counting-флаг строго спарен с работой, дрейф невозможен по построению.
    /// </summary>
    private bool CdCountingTryAdd(int jobId)
    {
        if (CdFlagCas(jobId, 1, 0) != 0)
            return false; // уже учтена другим потоком
        int ci = GetChunkIndexStatic(_targetX[jobId], _targetY[jobId]);
        Interlocked.Increment(ref _chunkCdCount[ci]);
        Interlocked.Increment(ref _globalCdCount);
        int expire = _jobFailCd[jobId];
        CasMin(ref _chunkCdEarliest[ci], expire);
        CasMin(ref _globalCdEarliest, expire);
        return true;
    }

    /// <summary>
    /// P8-г (task-20): CAS-владельное удаление работы из Cd-агрегатов.
    /// Возвращает true если переход 1→0 выполнен ИМЕННО здесь — декремент строго
    /// спарен со своим инкрементом, clamp никогда не съедает лишнюю единицу.
    /// </summary>
    private bool CdCountingTryRemove(int jobId)
    {
        if (CdFlagCas(jobId, 0, 1) != 1)
            return false; // не учтена (или уже снята другим потоком)
        int ci = GetChunkIndexStatic(_targetX[jobId], _targetY[jobId]);
        Interlocked.Decrement(ref _chunkCdCount[ci]);
        Interlocked.Decrement(ref _globalCdCount);
        // earliest НЕ откатываем: устаревшая нижняя граница лишь разрешает
        // лишний скан чанка (консервативно), пропустить живую работу не может.
        return true;
    }

    /// <summary>
    /// P8-г (drift fix): единственная точка CAS-мутации counting-флага.
    /// Через гейт (см. поля _cdFlag*), потому что массив флагов живёт в _jobCdCounted,
    /// который EnsureCapacity ПЕРЕСОЗДАЁТ. Голый Array.Resize + concurrent CAS на элементах
    /// давал дрейф в обе стороны: потерянный CAS → единица учёта без флага (+1 навсегда,
    /// гейт прячет живые работы), «воскресший» в новой копии флаг → повторный декремент
    /// одного и того же id (счётчики уходят в МИНУС).
    /// Быстрый путь (гейт открыт, а он открыт всегда, кроме момента роста ёмкости) —
    /// 2 interlocked-операции на редком пути учёта; промахи в claim-гейте (горячий путь)
    /// сюда не заходят вовсе.
    /// </summary>
    private int CdFlagCas(int jobId, int value, int comparand)
    {
        SpinWait spin = default;
        while (true)
        {
            Interlocked.Increment(ref _cdFlagInFlight);      // ДО чтения Quiesce (Dekker)
            if (Volatile.Read(ref _cdFlagQuiesce) == 0)
            {
                int prev = Interlocked.CompareExchange(ref _jobCdCounted[jobId], value, comparand);
                Interlocked.Decrement(ref _cdFlagInFlight);
                return prev;
            }
            Interlocked.Decrement(ref _cdFlagInFlight);
            // Идёт замена массива (единицы времени, ~3 раза за жизнь): ждём и повторяем.
            while (Volatile.Read(ref _cdFlagQuiesce) != 0)
                spin.SpinOnce();
        }
    }

    /// <summary>
    /// P8-г (drift fix): замена массива CD-флагов, атомарная относительно CdFlagCas.
    /// Вызывается только из EnsureCapacity под _registerLock; в этот момент держатель
    /// лока не может быть в CdFlagCas (кроме собственного вызова), поэтому спин конечен.
    /// </summary>
    private void ResizeCdFlagArray(int newCapacity)
    {
        Interlocked.Exchange(ref _cdFlagQuiesce, 1);         // ДО чтения InFlight (Dekker)
        SpinWait spin = default;
        while (Volatile.Read(ref _cdFlagInFlight) != 0)
            spin.SpinOnce();
        Array.Resize(ref _jobCdCounted, newCapacity);
        Interlocked.Exchange(ref _cdFlagQuiesce, 0);
    }

    /// <summary>P8в: unclaimed-работа встала на кулдаун → +1 в чанк/глобу, min-expire вниз.</summary>
    private void CdMarkAdded(int jobId) => CdCountingTryAdd(jobId);

    /// <summary>P8в: unclaimed-работа ушла с кулдауна (сброс/клейм/удаление) → −1.</summary>
    private void CdMarkRemoved(int jobId) => CdCountingTryRemove(jobId);

    /// <summary>
    /// Иерархический поиск задачи для агента.
    /// Сначала проверяет свой чанк -> макро-сектор (4x4 чанка) -> глобальную пирамиду через ulong маску.
    /// </summary>
    public bool TryClaimJobForAgent(
        int agentX, int agentY,
        ToolRequirement tools,
        AgentDataPool pool,
        int agentIndex,
        SimulationContext ctx,
        bool hasStockpileSpace,
        bool hasAvailableLogs,
        out JobData claimedJob)
    {
        claimedJob = default;
        if (Volatile.Read(ref _unclaimedCount) <= 0)
            return false;

        // P8в: O(1) глобальный отрицательный ответ. Если ВСЕ unclaimed-работы
        // стоят на кулдауне и ни один кулдаун не истёк — промах без единого скана.
        int now = Environment.TickCount;
        int cdGlobal = Volatile.Read(ref _globalCdCount);
        if (cdGlobal > 0)
        {
            int unclaimedNow = Volatile.Read(ref _unclaimedCount);
            if (cdGlobal >= unclaimedNow && now < Volatile.Read(ref _globalCdEarliest))
            {
                // Перепроверка против гонки с параллельным Register/Release:
                // если счётчики шевельнулись — падаем в честный скан.
                if (Volatile.Read(ref _unclaimedCount) == unclaimedNow &&
                    Volatile.Read(ref _globalCdCount) == cdGlobal)
                    return false;
            }
        }

        // Бюджет проб на один клейм (см. MaxChunkProbesPerClaim).
        int probeBudget = MaxChunkProbesPerClaim;
        int epoch = Volatile.Read(ref _claimEpoch);

        int agentChunk = GetChunkIndexStatic(agentX, agentY);

        // 1. Быстрый проход по своему чанку
        if (ChunkMayHaveClaimable(agentChunk, now))
        {
            probeBudget--;
            if (TryClaimInChunk(agentChunk, agentX, agentY, tools, pool, agentIndex, ctx, hasStockpileSpace, hasAvailableLogs, out claimedJob))
                return true;
        }

        // 2. Локальный макро-поиск в радиусе 1..3 чанков
        int centerCx = agentChunk % ChunkDim;
        int centerCy = agentChunk / ChunkDim;

        for (int r = 1; r <= 3; r++)
        {
            int minX = Math.Max(0, centerCx - r);
            int maxX = Math.Min(ChunkDim - 1, centerCx + r);
            int minY = Math.Max(0, centerCy - r);
            int maxY = Math.Min(ChunkDim - 1, centerCy + r);

            for (int cy = minY; cy <= maxY; cy++)
            {
                for (int cx = minX; cx <= maxX; cx++)
                {
                    if (cx > minX && cx < maxX && cy > minY && cy < maxY)
                        continue; // кольцо

                    if (probeBudget <= 0)
                        break;
                    int ci = cy * ChunkDim + cx;
                    if (ChunkMayHaveClaimable(ci, now))
                    {
                        probeBudget--;
                        if (TryClaimInChunk(ci, agentX, agentY, tools, pool, agentIndex, ctx, hasStockpileSpace, hasAvailableLogs, out claimedJob))
                            return true;
                    }
                }
            }
        }

        // 3. Глобальный макро-поиск через 64-битную маску L2
        ulong mask = Volatile.Read(ref _macroActiveMask);
        if (mask == 0UL)
            return false;

        int agentMacro = GetMacroIndex(agentX, agentY);
        // Макро-сектор = (8x8 чанков) при MacroShift = ChunkShift + 3 (128x128 тайлов).
        // Декодируем macroIndex обратно в диапазон чанков: 8 чанков на сторону макро-сектора.
        // Ротация старта по эпохе: при ограниченном бюджете агент за диспатч осмотрит
        // часть секторов, а за следующие диспатчи — остальные (иначе дальние всегда
        // отрезались бы бюджетом и оставались невидимыми этому агенту).
        int macroRot = epoch > 0 ? (int)((uint)epoch % (uint)MacroCount) : 0;
        for (int mi = 0; mi < MacroCount; mi++)
        {
            if (probeBudget <= 0)
                return false;
            int macroIdx = (agentMacro + macroRot + mi) & 63;
            if ((mask & (1UL << macroIdx)) != 0 && Volatile.Read(ref _macroJobCounts[macroIdx]) > 0)
            {
                int baseChunkX = (macroIdx % MacroDim) * 8;
                int baseChunkY = (macroIdx / MacroDim) * 8;

                // 8x8 чанков покрывают макро-сектор 128x128 тайлов (MacroShift = ChunkShift + 3).
                for (int cy = 0; cy < 8; cy++)
                {
                    for (int cx = 0; cx < 8; cx++)
                    {
                        int ci = (baseChunkY + cy) * ChunkDim + (baseChunkX + cx);
                        if (ci >= 0 && ci < ChunkCount && ChunkMayHaveClaimable(ci, now))
                        {
                            if (probeBudget <= 0)
                                return false;
                            probeBudget--;
                            if (TryClaimInChunk(ci, agentX, agentY, tools, pool, agentIndex, ctx, hasStockpileSpace, hasAvailableLogs, out claimedJob))
                                return true;
                        }
                    }
                }
            }
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float GetAffinityPenalty(AgentDataPool pool, int agentIndex, JobTypeId jobType)
    {
        if (pool == null || agentIndex < 0 || agentIndex >= pool.Capacity) return 0f;
        int last = pool.LastJobCategory[agentIndex];
        return JobPriorityManager.Instance.GetCategory(jobType) == (JobCategory)last ? 0f : 400f;
    }

    private bool TryClaimInChunk(
        int chunkIndex,
        int agentX, int agentY,
        ToolRequirement tools,
        AgentDataPool pool,
        int agentIndex,
        SimulationContext ctx,
        bool hasStockpileSpace,
        bool hasAvailableLogs,
        out JobData claimedJob,
        ulong typeMask = 0)
    {
        claimedJob = default;
        int count = Volatile.Read(ref _chunkCount[chunkIndex]);
        if (count == 0) return false;

        var cJobs = _chunkJobs[chunkIndex];
        int scanLimit = Math.Min(count, cJobs.Length);

        // task-2/perf: окно скана кандидатов фиксированной длины с ротацией по эпохе
        // диспатча (тот же приём, что MaxClaimScanPerWorker в GenericJobSpatialIndex).
        // Полный скан чанка на каждую пробу (и на каждого агента) выжигал квант 0.8 мс
        // диспетчера на первых же безработных агентах: «очень медленно распределяют работу».
        // Ротация гарантирует, что хвост большого чанка тоже просматривается — за
        // последующие диспатчи (эпоха инкрементится в BeginClaimEpoch).
        int scanCount = scanLimit <= MaxClaimScanPerChunk ? scanLimit : MaxClaimScanPerChunk;
        int scanOffset = 0;
        if (scanLimit > scanCount)
        {
            int epoch = Volatile.Read(ref _claimEpoch);
            if (epoch <= 0) epoch = 1;
            // J-03: окно сдвигается на СВОЮ длину, а не на 1. Раньше сдвиг на 1 за
            // диспатч означал, что работа на позиции k ждала до (count - 63)
            // диспатчей: для фермы 70k (~242 работы на чанк) это ~179 диспатчей
            // (~45 игросекунд на 1×). Шаг на scanCount даёт полный обход чанка за
            // ceil(count / scanCount) диспатчей (для 242 — 4 диспатча), а
            // закольцовка ниже гарантирует, что окно не обрывается на краю массива.
            scanOffset = (int)(((long)epoch * scanCount) % scanLimit);
        }

        int bestJobId = -1;
        int bestPriority = -1;
        float bestDistSq = float.MaxValue;
        // FIX (bughunt 08-7): поколение лучшего кандидата, зафиксированное ПРИ СКАНЕ.
        // После CAS сверяем его: если слот переиспользован — кандидат уже не тот.
        uint bestGen = 0;

        for (int n = 0; n < scanCount; n++)
        {
            int i = scanOffset + n;
            if (i >= scanLimit) i -= scanLimit; // закольцовка окна (J-03)
            int jobId = cJobs[i];
            if (jobId < 0 || jobId >= _capacity || !_active[jobId])
                continue;

            if (Volatile.Read(ref _assignedWorkers[jobId]) >= _maxWorkers[jobId])
                continue;

            if (_requiredTool[jobId] != ToolRequirement.None && (tools & _requiredTool[jobId]) == 0)
                continue;

            // J-18: зона отдаёт только СВОИ типы работ. Раньше клейм шёл по чанку без
            // учёта маски зоны, и работа чужого типа выдавалась Worker'у, немедленно
            // отменялась (OnStart → OnCancel) и возвращалась в очередь — churn, из-за
            // которого работа зоны «дёргалась», а агент терял время.
            if (typeMask != 0 && (typeMask & (1UL << (byte)_typeId[jobId])) == 0)
                continue;

            if (_typeId[jobId] == JobTypeId.StockpileHauling && !hasStockpileSpace)
                continue;

            if (_typeId[jobId] == JobTypeId.BlueprintDelivery && !hasAvailableLogs)
                continue;

            if (IsJobOnCooldown(jobId))
                continue;

            // §119 P2: работу, простоявшую в очереди зоны МЕНЬШЕ 60с, Laborer'у
            // (общий диспатч) не отдаём — её ждёт Worker зоны. Охрана дешёвая
            // (одна ConcurrentDictionary.TryGetValue). После 60с простоя
            // квалифицированная работа доступна разнорабочему (спека §119).
            if (ZoneJobRouter.Instance.IsHeldForWorkerFresh(jobId))
                continue;

            // Быстрая проверка хэндлера CanAgentExecute с memoization на эпоху диспатча (S-033)
            if (JobRegistry.TryGetHandler(_typeId[jobId], out var handler))
            {
                bool canExec;
                if (TryGetMemoCanExecute(jobId, agentIndex, out bool memo))
                {
                    canExec = memo;
                }
                else
                {
                    canExec = handler.CanAgentExecute(agentIndex, GetJobData(jobId), pool, ctx);
                    SetMemoCanExecute(jobId, agentIndex, canExec);
                }

                if (!canExec)
                    continue;
            }

            int userPrio = JobPriorityManager.Instance.GetPriority(
                JobPriorityManager.Instance.GetCategory(_typeId[jobId]));
            // J-13: 0 — это «низший приоритет», а НЕ «выключено». Раньше `<= 0`
            // тихо выбрасывал ВСЕ работы категории: игрок/код выставлял слайдер в 0
            // (UI это разрешает — Math.Max(0, value)) и работы категории
            // переставали выдаваться без единого сообщения и без видимой причины.
            if (userPrio < 0)
                continue;

            int score = (userPrio << 8) | (255 - (byte)_priorityTier[jobId]);
            float dx = _standX[jobId] - agentX;
            float dy = _standY[jobId] - agentY;
            float distSq = dx * dx + dy * dy + GetAffinityPenalty(pool, agentIndex, _typeId[jobId]);

            if (score > bestPriority || (score == bestPriority && distSq < bestDistSq))
            {
                bestPriority = score;
                bestDistSq = distSq;
                bestJobId = jobId;
                bestGen = Volatile.Read(ref _slotGen[jobId]); // FIX (08-7): штамп слота при скане
            }
        }

        if (bestJobId == -1)
            return false;

        // CAS захват
        int cur = Volatile.Read(ref _assignedWorkers[bestJobId]);
        while (cur < _maxWorkers[bestJobId])
        {
            int prev = Interlocked.CompareExchange(ref _assignedWorkers[bestJobId], cur + 1, cur);
            if (prev == cur)
            {
                // FIX (bughunt 08-7): между сканом кандидата и CAS работу могли удалить
                // (снятие метки/зоны на main-потоке), а слот — отдать ДРУГОЙ работе
                // (free-list LIFO). CAS при этом инкрементит _assignedWorkers уже чужого
                // слота, а GetJobData ниже вернул бы агенту JobData новой работы,
                // которой он не выбирал (тип/клетка не те). Поэтому после успешного CAS
                // сверяем живость и ПОКОЛЕНИЕ слота с тем, что видели при скане.
                if (!Volatile.Read(ref _active[bestJobId]) ||
                    Volatile.Read(ref _slotGen[bestJobId]) != bestGen)
                {
                    // Откат: если поколение не сменилось — работа просто удалена, наш
                    // инкремент висит на мёртвом слоте, снимаем его. Если поколение
                    // сменилось, регистрация уже обнулила счётчик — ничего не трогаем.
                    if (Volatile.Read(ref _slotGen[bestJobId]) == bestGen)
                        DecClamp0(ref _assignedWorkers[bestJobId], ref _claimClampHits);
                    // FIX (bughunt 08-2): если слот в дренаже и наш откат сделал счётчик
                    // нулевым — добиваем дренаж (иначе слот останется pending навсегда).
                    CompleteClaimRelease(bestJobId, Volatile.Read(ref _assignedWorkers[bestJobId]));
                    claimedJob = default;
                    return false;
                }

                if (cur + 1 >= _maxWorkers[bestJobId])
                {
                    // FIX (bughunt 08-6): единицу unclaimed списывает только победитель
                    // CAS-перехода 1→0 — двойного списания с RemoveJob-ом быть не может.
                    if (UnclaimedTake(bestJobId))
                    {
                        DecClamp0(ref _unclaimedCount, ref _unclaimedClampHits);
                        DecClamp0(ref _chunkUnclaimedCount[chunkIndex], ref _chunkUnclaimedClampHits);

                        // P8в/P8-г: клейм уводит работу из unclaimed — CAS 1→0 снимает учёт
                        // ровно один раз (параллельный Clear может уже забрать единицу).
                        CdCountingTryRemove(bestJobId);

                        MacroRemove(GetMacroIndex(_targetX[bestJobId], _targetY[bestJobId]));
                    }
                }
                claimedJob = GetJobData(bestJobId);
                return true;
            }
            cur = Volatile.Read(ref _assignedWorkers[bestJobId]);
        }

        return false;
    }

    public List<int> SnapshotActiveJobs(List<int> destination, int maxCount, ref int cursor)
    {
        destination.Clear();
        lock (_registerLock)
        {
            if (_posMap.Count == 0)
            {
                cursor = 0;
                return destination;
            }
            int skipped = 0;
            int target = cursor % Math.Max(1, _posMap.Count);
            foreach (int id in _posMap.Values)
            {
                if (skipped < target)
                {
                    skipped++;
                    continue;
                }
                if (destination.Count >= maxCount)
                    break;
                if (id >= 0 && id < _capacity && _active[id])
                    destination.Add(id);
            }
            cursor += destination.Count;
            if (destination.Count < maxCount)
                cursor = 0;
        }
        return destination;
    }

    public bool TryAddJobProgress(int x, int y, JobTypeId type, int countToAdd, out bool isCompleted, out JobData jobSnapshot)
    {
        lock (_registerLock)
        {
            if (_posMap.TryGetValue((x, y, type), out int id) && _active[id])
            {
                int target = _targetItemCount[id];
                int remaining = target - _currentDeliveredCount[id];
                int accepted = Math.Min(countToAdd, Math.Max(0, remaining));
                _currentDeliveredCount[id] += accepted;
                isCompleted = _currentDeliveredCount[id] >= target;
                jobSnapshot = GetJobData(id);
                return true;
            }
            isCompleted = false;
            jobSnapshot = default;
            return false;
        }
    }

    public bool TryAddJobProgressClamped(int x, int y, JobTypeId type, int countToAdd, out int acceptedCount, out bool isCompleted, out JobData jobSnapshot)
    {
        acceptedCount = 0;
        lock (_registerLock)
        {
            if (_posMap.TryGetValue((x, y, type), out int id) && _active[id])
            {
                int target = _targetItemCount[id];
                int remaining = target - _currentDeliveredCount[id];
                acceptedCount = Math.Min(countToAdd, Math.Max(0, remaining));
                _currentDeliveredCount[id] += acceptedCount;
                isCompleted = _currentDeliveredCount[id] >= target;
                jobSnapshot = GetJobData(id);
                return true;
            }
            isCompleted = false;
            jobSnapshot = default;
            return false;
        }
    }

    public bool TryClaimForWorkerInChunk(
        int chunkIndex,
        int workerTileX, int workerTileY,
        ToolRequirement workerTools,
        AgentDataPool pool,
        int agentIndex,
        SimulationContext ctx,
        out JobData claimedJob,
        ulong typeMask = 0)
    {
        bool hasStockpileSpace = StockpileManager.Instance.HasFreeSpace;
        bool hasAvailableLogs = GroundItemManager.Instance.HasAvailableLogs;
        return TryClaimInChunk(
            chunkIndex, workerTileX, workerTileY,
            workerTools, pool, agentIndex, ctx,
            hasStockpileSpace, hasAvailableLogs,
            out claimedJob, typeMask);
    }

    /// <summary>
    /// §119 P2: клейм КОНКРЕТНОЙ работы по id (для Worker'а зоны: роутер выдал
    /// ZoneJob, здесь фиксируем клейм в реестре). Логика зеркальна CAS-блоку
    /// TryClaimInChunk: assignedWorkers++, при насыщении — unclaimed-счётчики.
    /// Валидацию исполнимости делает вызывающий — Worker валидирует сам после
    /// выдачи из роутера (см. WorkerZoneJobSystem).
    /// </summary>
    public bool TryClaimJobById(int jobId, out JobData claimedJob)
    {
        claimedJob = default;
        if (jobId < 0 || jobId >= _capacity || !Volatile.Read(ref _active[jobId]))
            return false;
        // FIX (bughunt 08-4): не выдавать работу, которая на кулдауне недостижимости.
        // Раньше проверки не было, а Worker'у кулдаун не ставится (JobDispatcher) —
        // бригада бесконечно дёргалась к одной клетке за стеной, остальные стояли.
        if (IsJobOnCooldown(jobId))
        {
            // Работа пришла из очереди зоны (DequeueNearest уже снял её с очереди и
            // положил в _assigned). Возвращаем её в очередь зоны, иначе живая работа
            // «зависла» бы вне очереди до Sweep, а он чистит только МЁРТВЫЕ id.
            ZoneJobRouter.Instance.Unassign(jobId);
            return false;
        }
        if (Volatile.Read(ref _assignedWorkers[jobId]) >= _maxWorkers[jobId])
            return false;

        // FIX (bughunt 08-7): поколение слота, зафиксированное ДО CAS. После успешного
        // CAS слот мог быть освобождён и переиспользован под другую задачу — тогда
        // агенту уйдёт JobData чужой задачи, а счётчики спишутся с чужого чанка.
        uint gen = Volatile.Read(ref _slotGen[jobId]);

        int cur = Volatile.Read(ref _assignedWorkers[jobId]);
        while (cur < _maxWorkers[jobId])
        {
            int prev = Interlocked.CompareExchange(ref _assignedWorkers[jobId], cur + 1, cur);
            if (prev != cur)
            {
                cur = Volatile.Read(ref _assignedWorkers[jobId]);
                continue;
            }
            // FIX (bughunt 08-7): перепроверка живости И поколения после CAS.
            // Если поколение сменилось — регистрация уже обнулила счётчик, откатывать
            // нечего; если не сменилось — работа удалена, снимаем свой инкремент с клампом.
            if (!Volatile.Read(ref _active[jobId]) || Volatile.Read(ref _slotGen[jobId]) != gen)
            {
                if (Volatile.Read(ref _slotGen[jobId]) == gen)
                    DecClamp0(ref _assignedWorkers[jobId], ref _claimClampHits);
                // FIX (bughunt 08-2): слот мог быть в дренаже — если откат обнулил счётчик,
                // добиваем дренаж, иначе слот останется pending навсегда.
                CompleteClaimRelease(jobId, Volatile.Read(ref _assignedWorkers[jobId]));
                return false;
            }
            if (cur + 1 >= _maxWorkers[jobId])
            {
                // FIX (bughunt 08-6): единицу unclaimed списывает только победитель
                // CAS-перехода 1→0 (без владения удаление могло списать её параллельно).
                if (UnclaimedTake(jobId))
                {
                    DecClamp0(ref _unclaimedCount, ref _unclaimedClampHits);
                    int chunkIndex = GetChunkIndexStatic(_targetX[jobId], _targetY[jobId]);
                    DecClamp0(ref _chunkUnclaimedCount[chunkIndex], ref _chunkUnclaimedClampHits);

                    // P8в/P8-г: клейм уводит работу из unclaimed — CAS 1→0 снимает учёт.
                    CdCountingTryRemove(jobId);

                    MacroRemove(GetMacroIndex(_targetX[jobId], _targetY[jobId]));
                }
            }
            claimedJob = GetJobData(jobId);
            return true;
        }
        return false;
    }

    public void Clear()
    {
        lock (_registerLock)
        {
            Array.Clear(_active, 0, _capacity);
            Array.Clear(_assignedWorkers, 0, _capacity);
            Array.Clear(_jobFailCd, 0, _capacity);
            Array.Clear(_jobCdCounted, 0, _capacity);
            Array.Clear(_chunkCount, 0, ChunkCount);
            Array.Clear(_chunkUnclaimedCount, 0, ChunkCount);
            Array.Clear(_macroJobCounts, 0, MacroCount);
            _macroActiveMask = 0UL;

            // P8в: сброс агрегированных флагов кулдаунов.
            Array.Clear(_chunkCdCount, 0, ChunkCount);
            for (int i = 0; i < ChunkCount; i++)
                _chunkCdEarliest[i] = int.MaxValue;
            _globalCdCount = 0;
            _globalCdEarliest = int.MaxValue;

            _posMap.Clear();
            _totalCount = 0;
            _unclaimedCount = 0;

            // FIX (bughunt 08-2/08-6/08-7): сброс слот-состояния вместе с остальным.
            // InitFreeList ниже возвращает ВСЕ слоты в free-list, поэтому «дренажные»
            // флаги и поколения обязаны быть обнулены — иначе слот, помеченный pending,
            // остался бы недоступным при живом free-list.
            Array.Clear(_slotUnclaimed, 0, _capacity);
            Array.Clear(_pendingFree, 0, _capacity);
            for (int i = 0; i < _capacity; i++)
                _slotGen[i] = 0u;
            _pendingFreeCount = 0;

            InitFreeList(_capacity);
        }
    }
}
