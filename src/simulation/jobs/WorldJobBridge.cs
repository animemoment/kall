using System;
using System.Collections.Generic;
using Game.Core;
using Game.Core.WorldStreaming;
using Game.Core.WorldStreaming.Integration;
using Game.UI.Streaming;

// L-02: МЁРТВЫЙ КОД (2026-11-05). мост к мёртвой WorldJobBoard: создаётся только тестами, в игровом коде `new WorldJobBridge(` отсутствует.

// Удаление — отдельная задача чистки (см. _plan/DEFECTS_MASTER.md). Не подключать без решения владельца.

namespace Game.Simulation;

/// <summary>
/// §123: мост «WorldJobBoard ↔ JobDispatcher».
/// Каждые ~1с (60 тиков):
///   1. board.Refresh() — обновить список мировых чертежей (загруженные чанки).
///   2. Для каждого чертежа без работы — RegisterJob(JobData) в диспетчере.
///   3. Агент берёт задачу → WorldPlanHandler.OnStart → идёт (прямое движение,
///      AgentMovementService §123) → работает → Commit → board.Complete(plan)
///      → чертёж заменяется готовым (BuildingPlan → Building).
///   4. Если чертёж исчез (чанк выгрузился) — JobValidator снимает задачу.
///
/// Только main thread. Создаётся Main'ом после инициализации стриминга.
/// </summary>
public sealed class WorldJobBridge
{
    private readonly WorldJobBoard _board;
    private readonly JobDispatcher _dispatcher;
    private readonly Dictionary<(int X, int Y), int> _registeredJobs = new(64);
    // -1: первый Tick() даёт _tickCounter=0 → 0 % N == 0 → обрабатывает сразу.
    private int _tickCounter = -1;

    /// <summary>Сколько мировых задач сейчас зарегистрировано в диспетчере.</summary>
    public int RegisteredCount => _registeredJobs.Count;

    /// <summary>
    /// §123: ускорение моста. План -> регистрация была раз в 60 тиков (~1с),
    /// а обновление очереди держало max=128 планов и искало по КАМЕРЕ, а не по
    /// острову — работы за кромкой выдавались пачкой раз в секунду, отсюда
    /// «ОЧЕНЬ медленно». Тикаем чаще, пока есть работа (иначе редко).
    /// </summary>
    private const int RefreshEveryNTicks = 15; // ~0.25с при 60 fps — в 4 раза чаще
    private const int MaxRegistrationsPerTick = 96;

    public WorldJobBridge(WorldJobBoard board, JobDispatcher dispatcher)
    {
        _board = board ?? throw new ArgumentNullException(nameof(board));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    /// <summary>
    /// Тик из Main._Process (main thread). Обновляет очередь и регистрирует новые задачи.
    /// §124: также дренит очередь завершений из sim-потока — board.Complete
    /// (мутации тайлмапов и _board) выполняются ТОЛЬКО здесь, на главном потоке.
    /// </summary>
    public void Tick(long cameraCellX, long cameraCellY)
    {
        // §124: сначала завершённые работы (sim-поток положил координаты).
        DrainCompletions();

        _tickCounter++;
        if (_tickCounter % RefreshEveryNTicks != 0)
            return;

        // 1. Обновить очередь мировых чертежей (ближние к фокусу первыми).
        // План -> регистрация больше не держит только 128 «ближайших»:
        // distant-work also matters (агентам идти далеко нормально — WorldPlanHandler).
        _board.Refresh(cameraCellX, cameraCellY, max: 512);

        // 2. Снять регистрации для задач, покинувших диспетчер (завершены/сняты).
        RemoveStaleJobs();

        // 3. Зарегистрировать новые планы.
        RegisterNewJobs(cameraCellX, cameraCellY);
    }

    /// <summary>
    /// §124: завершения из sim-потока. Для каждой клетки находим план (перебор
    /// kind'ов — IsStillPlanned, главный поток = безопасно) и Complete.
    /// Вызывается КАЖДЫЙ тик (не раз в 60) — задержка завершения ≤ 1 кадр.
    /// </summary>
    private void DrainCompletions()
    {
        int guard = 256; // защита от бесконечного цикла при аномалиях
        while (guard-- > 0 && WorldPlanHandler.CompletionQueue.TryDequeue(out var cell))
        {
            var plan = FindPlanAt(cell.X, cell.Y);
            _board.Complete(plan);
        }
    }

    /// <summary>Найти WorldPlan в клетке: перебор 6 kind'ов (глава — только main thread).</summary>
    private WorldPlan FindPlanAt(long tx, long ty)
    {
        byte[] planKinds =
        {
            WorldDeltaKind.WallPlan,
            WorldDeltaKind.BuildingPlan,
            WorldDeltaKind.CropPlan,
            WorldDeltaKind.StockpilePlan,
            WorldDeltaKind.CutTreePlan,
            WorldDeltaKind.MinedStonePlan
        };
        foreach (byte kind in planKinds)
        {
            var candidate = new WorldPlan(kind, tx, ty);
            if (_board.IsStillPlanned(candidate))
                return candidate;
        }
        return new WorldPlan(WorldDeltaKind.BuildingPlan, tx, ty);
    }

    private void RemoveStaleJobs()
    {
        if (_registeredJobs.Count == 0)
            return;
        _staleBuffer.Clear();
        foreach (var ((x, y), _) in _registeredJobs)
        {
            // Задача больше не в индексе: завершена агентом или снята JobValidator'ом.
            if (!_dispatcher.JobIndex.HasJobAt(x, y, JobTypeId.WorldPlan))
                _staleBuffer.Add((x, y));
        }
        foreach (var key in _staleBuffer)
            _registeredJobs.Remove(key);
    }

    private readonly List<(int X, int Y)> _staleBuffer = new(32);

    private void RegisterNewJobs(long cameraX, long cameraY)
    {
        int iterations = Math.Min(_board.PendingCount, MaxRegistrationsPerTick);
        for (int i = 0; i < iterations; i++)
        {
            // TryTakeNearest клеймит план; после проверки Release возвращает в очередь.
            if (!_board.TryTakeNearest(cameraX, cameraY, out WorldPlan plan))
                break;
            try
            {
                var key = ((int)plan.CellX, (int)plan.CellY);
                if (_registeredJobs.ContainsKey(key))
                    continue; // уже зарегистрирована

                var job = new JobData
                {
                    TypeId = JobTypeId.WorldPlan,
                    ExecutionType = JobExecutionType.Stationary,
                    PriorityTier = JobPriorityTier.Construction,
                    RequiredTool = ToolRequirement.None,
                    TargetX = (int)plan.CellX,
                    TargetY = (int)plan.CellY,
                    StandX = (int)plan.CellX,
                    StandY = (int)plan.CellY,
                    SourceX = (int)plan.CellX,
                    SourceY = (int)plan.CellY,
                    MaxWorkers = 1,
                    AssignedWorkers = 0,
                    WorkDuration = WorldPlanHandler.WorkDurationSeconds,
                    IsActive = true
                };
                int jobId = _dispatcher.RegisterJob(job);
                if (jobId >= 0) // id=0 валиден (первый job в индексе)
                    _registeredJobs[key] = jobId;
            }
            finally
            {
                // Снять клейм — план возвращается в очередь WorldJobBoard.
                _board.Release(plan);
            }
        }
    }
}
