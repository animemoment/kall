using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using Game.Core;

namespace Game.Simulation;

/// <summary>
/// Снимок культур для рендера: по каждой фазе — позиции клеток и ТИП культуры
/// (byte = <see cref="Game.Core.CropType"/>) в параллельных массивах, индекс
/// типа согласован с индексом позиции.
///
/// Массивы берутся из переиспользуемого ring-буфера <see cref="CropGrowthManager"/>
/// (3 слота; в очереди одновременно не больше 2 снимков), поэтому генерация
/// снапшота НЕ аллоцирует массивы на тик/кадр. Потребитель (CropRenderer) читает
/// снимок на главном потоке; слот не будет переписан минимум 2 генерации, что
/// при троттлинге снапшотов (~5-20 Гц) заведомо больше кадра.
/// </summary>
public sealed class CropRenderSnapshot
{
    /// <summary>[4] массива Vector2[MaxCrops] — позиции клеток по фазам Stage 1..4.</summary>
    public readonly Vector2[][] PositionsByStage;

    /// <summary>[4] массива byte[MaxCrops] — тип клетки ((byte)CropType) по фазам.</summary>
    public readonly byte[][] TypesByStage;

    /// <summary>[4] число валидных записей на фазу.</summary>
    public readonly int[] Counts;

    public CropRenderSnapshot(Vector2[][] positionsByStage, byte[][] typesByStage, int[] counts)
    {
        PositionsByStage = positionsByStage;
        TypesByStage = typesByStage;
        Counts = counts;
    }
}

public sealed class CropGrowthManager
{
    public static CropGrowthManager Instance { get; } = new();

    private const float StageDuration = 60.0f; // 1 минута на каждую фазу
    private const int MaxCrops = 16384;

    private readonly object _lock = new();
    private readonly Dictionary<(int X, int Y), CropData> _crops = new(MaxCrops);

    private bool _isDirty = true;

    // Finding 6: ring из 3 снапшот-буферов (см. GroundItemManager).
    // CropRenderer drain'ит очередь до последнего (аналогично GroundItemRenderer),
    // cap = 2, ротация = 3: перезаписи читаемого буфера нет.
    // В слоте — позиции И типы клеток по фазам (CropRenderSnapshot), массивы
    // создаются один раз на слот и переиспользуются.
    private readonly CropRenderSnapshot[] _snapRing = new CropRenderSnapshot[3];
    private int _snapCursor;

    // Переиспользуемый буфер копии словаря под lock. GenerateSnapshot зовётся
    // ТОЛЬКО из sim-потока (AgentSimulationThread), поэтому после выхода из
    // критической секции буфер никто параллельно не перезаписывает — и сама
    // генерация снапшота не аллоцирует массивов (GC-pressure 0 на тик).
    private KeyValuePair<(int X, int Y), CropData>[] _snapCopy =
        System.Array.Empty<KeyValuePair<(int X, int Y), CropData>>();

    public ConcurrentQueue<CropRenderSnapshot> SnapshotQueue { get; } = new();

    public CropGrowthManager()
    {
    }

    public void PlantCrop(int x, int y, int zoneId, Game.Core.CropType cropType = Game.Core.CropType.Wheat)
    {
        lock (_lock)
        {
            _crops[(x, y)] = new CropData
            {
                X = x,
                Y = y,
                ZoneId = zoneId,
                Type = cropType,
                Stage = 1,
                GrowthTimer = 0f,
                IsHarvestQueued = false,
                IsPlantingQueued = false
            };
            _isDirty = true;
        }
    }

    public bool HasCrop(int x, int y)
    {
        lock (_lock)
        {
            return _crops.ContainsKey((x, y));
        }
    }

    public bool TryGetCrop(int x, int y, out CropData crop)
    {
        lock (_lock)
        {
            return _crops.TryGetValue((x, y), out crop);
        }
    }

    // AUDIT task-17 (perf): переиспользуемые буферы прохода роста. Раньше
    // UpdateGrowth на КАЖДЫЙ вызов (2 раза/сек на 1x, на высоких скоростях чаще)
    // аллоцировал KeyValuePair[count] + List<((int,int),CropData)> на 64 записи +
    // List для готовых к сбору — при 10k грядок это сотни КБ мусора в секунду,
    // хотя заголовок класса и комментарий обещают «без аллокаций в горячем пути».
    // Проход вызывает только sim-поток (AgentSimulationThread), поэтому буферы
    // можно держать полями без блокировки.
    private KeyValuePair<(int X, int Y), CropData>[] _growthSnapshot =
        System.Array.Empty<KeyValuePair<(int X, int Y), CropData>>();
    private readonly List<((int X, int Y) Pos, CropData Crop)> _grownBuffer = new(64);
    private readonly List<(int X, int Y)> _readyBuffer = new(16);
    private readonly List<(int X, int Y)> _deadBuffer = new(16);

    public void UpdateGrowth(float deltaTime, SimulationContext ctx)
    {
        _grownBuffer.Clear();
        _readyBuffer.Clear();
        _deadBuffer.Clear();

        // Двухфазно: снимок под коротким lock, итерация — без hold.
        // §20.3: земля на скорость не влияет, humidity/fertility здесь не читаем.
        KeyValuePair<(int X, int Y), CropData>[] snap;
        int snapCount;
        lock (_lock)
        {
            if (_crops.Count == 0) return;
            snapCount = _crops.Count;
            if (_growthSnapshot.Length < snapCount)
                _growthSnapshot = new KeyValuePair<(int X, int Y), CropData>[Math.Max(64, snapCount)];
            snap = _growthSnapshot;
            int si = 0;
            foreach (var kvp in _crops) snap[si++] = kvp;
        }

        // GD-16: Расчёт сезона для проверки заморозков (лето −1, зима +1 или наоборот по синусоиде: мороз при season < -0.6f)
        float gameTime = AgentSimulationThread.Instance != null ? AgentSimulationThread.Instance.GameTimeSeconds : 0f;
        double dayOfYear = (gameTime / WorldTime.SecondsPerDay) % 360.0;
        float season = MathF.Sin((float)(dayOfYear / 360.0 * Math.PI * 2.0) - MathF.PI / 2f);
        bool isFrost = season < -0.6f;

        // Вне лока: считаем приросты.
        // §20.3: скорость роста везде одинаковая — земля влияет только на УРОЖАЙ
        // (считается в HarvestCrop через FarmYield). humidity/fertility здесь не читаем.
        for (int i = 0; i < snapCount; i++)
        {
            var pos = snap[i].Key;
            var crop = snap[i].Value;
            if (crop.Stage < 1 || crop.Stage >= 4)
                continue;

            // GD-16: гибель или увядание культур при критической засухе (влажность почвы < 20 единиц) или заморозках (сезон < -0.6f)
            bool isDrought = ctx?.Humidity != null && ctx.Humidity.Get(pos.X, pos.Y) < 20;
            if (isDrought || isFrost)
            {
                _deadBuffer.Add(pos);
                continue;
            }

            // #15: while вместо if — при большом dt (100x: за тик приходит
            // сразу секундами) стадия росла максимум на 1 за проход, рост
            // искусственно тормозился в разы. Переносим весь накопленный
            // таймер через несколько стадий; готовым (Stage 4) — в harvest.
            crop.GrowthTimer += deltaTime;
            while (crop.GrowthTimer >= StageDuration && crop.Stage >= 1 && crop.Stage < 4)
            {
                crop.GrowthTimer -= StageDuration;
                crop.Stage++;
                if (crop.Stage == 4 && !crop.IsHarvestQueued)
                {
                    crop.IsHarvestQueued = true;
                    _readyBuffer.Add(pos);
                }
            }
            _grownBuffer.Add((pos, crop));
        }
        // Короткий writeback под lock: только изменившиеся.
        lock (_lock)
        {
            for (int i = 0; i < _deadBuffer.Count; i++)
            {
                var deadPos = _deadBuffer[i];
                if (_crops.Remove(deadPos))
                {
                    _isDirty = true;
                }
            }

            for (int i = 0; i < _grownBuffer.Count; i++)
            {
                var (pos, crop) = _grownBuffer[i];
                if (!_crops.TryGetValue(pos, out var cur))
                    continue; // конкурентный Harvest удалил — не воскрешаем
                // #15: while-проход выше мог продвинуть на несколько стадий:
                // пишем только вперёд (стадия и таймер новее), иначе затёрли бы
                // чужой конкурентный апдейт старым снимком.
                bool stageNewer = crop.Stage > cur.Stage;
                bool sameStageTimerNewer = crop.Stage == cur.Stage && crop.GrowthTimer > cur.GrowthTimer;
                if (stageNewer || sameStageTimerNewer)
                {
                    _crops[pos] = crop;
                    _isDirty = true;
                }
            }
        }

        // Чистим зарегистрированные harvest-задачи для погибших культур (если были)
        for (int i = 0; i < _deadBuffer.Count; i++)
        {
            var (dx, dy) = _deadBuffer[i];
            int harvJobId = JobDispatcher.Instance.JobIndex.TryGetJobIdByPos(dx, dy, JobTypeId.Harvesting, out _);
            if (harvJobId >= 0)
                JobDispatcher.Instance.TryUnregisterJob(harvJobId);
        }

        // GD-27: Проверка простоя вскопанных грядок без посевов — зарастание травой при простое >3-5 дней
        FarmJobManager.Instance.UpdateIdleBeds(deltaTime, ctx);

        if (_readyBuffer.Count == 0)
            return;

        // P0-1: без счётчиков — проход и так под lock, лишний lock(_cLock).
        for (int i = 0; i < _readyBuffer.Count; i++)
        {
            var (x, y) = _readyBuffer[i];
            JobBroker.Instance.RegisterHarvest(x, y);
        }
    }

    public void HarvestCrop(int x, int y, SimulationContext ctx)
    {
        int zoneId = -1;
        Game.Core.CropType cropType = Game.Core.CropType.Wheat;

        lock (_lock)
        {
            // FIX (bughunt 09-4/12-2): спавн зерна шёл ВНЕ проверки наличия культуры
            // и без проверки стадии — сбор с пустой клетки (5-40 зерна из воздуха)
            // и с недозревшей культуры. Stage == 4 — единственная зрелая фаза
            // (см. UpdateGrowth: рост идёт до Stage 4, она же queued на сбор;
            // HarvestJobHandler.CanAgentExecute проверяет то же самое).
            if (!_crops.TryGetValue((x, y), out var crop))
                return;
            if (crop.Stage != 4)
                return;

            zoneId = crop.ZoneId;
            cropType = crop.Type;
            // Удаляем только реально существующую и зрелую культуру.
            _crops.Remove((x, y));
            _isDirty = true;
        }

        // Выпадение 5-40 единиц зерна по качеству земли (§20.3):
        // влажно + плодородно = много, сухо + бедно = мало.
        // ParallelRng: ctx.Random не thread-safe
        // (HarvestCrop вызывается из параллельного Commit).
        int hum = ctx?.Humidity != null ? ctx.Humidity.Get(x, y) : 100;
        int fert = ctx?.Fertility != null ? ctx.Fertility.Get(x, y) : 100;
        float humMul = HumidityMap.GrowthMultiplier(hum);
        float fertMul = FertilityMap.GrowthMultiplier(fert);
        int dropAmount = FarmYield.RollDrop(humMul, fertMul, ParallelRng.NextDouble());

        // Сорт определяет, что именно падает: пшеница → зерно, лён → льноволокно и т.д.
        CropDefinition harvested = CropRegistry.Get(cropType);
        GroundItemManager.Instance.SpawnItems(x, y, harvested.HarvestItem, dropAmount);

        // §20.4: сбор истощает клетку — плодородие × 0.8.
        ctx?.Fertility?.DrainAfterHarvest(x, y);
        ctx?.SoilEngine?.DrainAfterHarvest(x, y, cropType);

        // Если автопосадка включена — ставим задачу на новую посадку
        FarmZone currentZone = null;
        if (FarmZoneManager.Instance.TryGetZoneAt(x, y, out var zoneAtPos))
        {
            currentZone = zoneAtPos;
        }
        else if (zoneId != -1 && FarmZoneManager.Instance.TryGetZoneById(zoneId, out var zoneById))
        {
            currentZone = zoneById;
        }

        if (currentZone != null && currentZone.AutoPlantEnabled)
        {
            JobBroker.Instance.RegisterPlanting(x, y, currentZone.Id);
        }
    }

    public void RemoveCrop(int x, int y)
    {
        lock (_lock)
        {
            if (_crops.Remove((x, y)))
            {
                _isDirty = true;
            }
        }
    }

    public void GenerateSnapshot()
    {
        // Копируем записи под коротким lock в переиспользуемый буфер (без new
        // KeyValuePair[] на каждую генерацию), буфер строим вне критической секции.
        int copyCount;
        lock (_lock)
        {
            if (!_isDirty) return;
            _isDirty = false;
            copyCount = _crops.Count;
            if (_snapCopy.Length < copyCount)
                _snapCopy = new KeyValuePair<(int X, int Y), CropData>[Math.Max(64, copyCount)];
            int ci = 0;
            foreach (var kvp in _crops) _snapCopy[ci++] = kvp;
        }
        var copy = _snapCopy;
        int n = copyCount;

        // Finding 6: ring-буфер вместо fresh-аллокации (см. GroundItemManager).
        // Массивы позиций/типов создаются один раз на слот и переиспользуются:
        // на кадр (генерацию) здесь ноль новых массивов.
        int slot = _snapCursor % 3;
        _snapCursor++;
        var snap = _snapRing[slot];
        if (snap == null)
        {
            var positions = new Vector2[4][];
            var types = new byte[4][];
            for (int s = 0; s < 4; s++)
            {
                positions[s] = new Vector2[MaxCrops];
                types[s] = new byte[MaxCrops];
            }
            snap = new CropRenderSnapshot(positions, types, new int[4]);
            _snapRing[slot] = snap;
        }
        else
        {
            Array.Clear(snap.Counts, 0, snap.Counts.Length);
        }

        var currentSnap = snap.PositionsByStage;
        var currentTypes = snap.TypesByStage;
        var currentCounts = snap.Counts;

        for (int i = 0; i < n; i++)
        {
            var x = copy[i].Key.X; var y = copy[i].Key.Y; var crop = copy[i].Value;
            if (crop.Stage >= 1 && crop.Stage <= 4)
            {
                int stageIdx = crop.Stage - 1;
                int count = currentCounts[stageIdx];
                if (count < MaxCrops)
                {
                    currentSnap[stageIdx][count] = new Vector2(x * 64f + 32f, y * 64f + 32f);
                    // Тип идёт параллельно позиции: рендер выбирает текстуру по паре
                    // (CropType, Stage). CropType : byte — храним как есть.
                    currentTypes[stageIdx][count] = (byte)crop.Type;
                    currentCounts[stageIdx] = count + 1;
                }
            }
        }

        SnapshotQueue.Enqueue(snap);

        while (SnapshotQueue.Count > 2)
            SnapshotQueue.TryDequeue(out _);
    }

    /// <summary>
    /// §119 P7б (task-17 п.3-5): снимок культур для сейва (payload'ы Crop-меток):
    /// клетка, стадия 1..4, таймер текущей фазы, zoneId.
    /// </summary>
    public List<CropData> GetCropsSnapshot()
    {
        var result = new List<CropData>(_crops.Count);
        lock (_lock)
        {
            foreach (var kvp in _crops)
                result.Add(kvp.Value);
        }
        return result;
    }

    /// <summary>
    /// §119 P7б: восстановить культуру с СОСТОЯНИЕМ (стадия/таймер) — обратная
    /// сторона GetCropsSnapshot. Идемпотентен: существующая культура не затирается.
    /// Стадия 1..4; Stage 4 ставится с IsHarvestQueued (готова к сбору).
    /// Тип не передаётся — совместимость со старыми сейвами/вызовами (пшеница).
    /// </summary>
    public void RestoreCrop(int x, int y, int zoneId, int stage, float growthTimer)
        => RestoreCrop(x, y, zoneId, stage, growthTimer, Game.Core.CropType.Wheat);

    /// <summary>
    /// То же, но с ТИПОМ культуры: после загрузки региона клетка обязана остаться
    /// той же культурой (пшеница не должна превращаться в пшеницу-по-умолчанию,
    /// если сажали лён). Старый overload оставлен для совместимости.
    /// </summary>
    public void RestoreCrop(int x, int y, int zoneId, int stage, float growthTimer, Game.Core.CropType cropType)
    {
        if (stage < 1 || stage > 4)
            return;
        if (growthTimer < 0f || float.IsNaN(growthTimer) || float.IsInfinity(growthTimer))
            growthTimer = 0f;
        // Битый payload (type=0/неизвестный байт) не должен давать культуру без
        // спрайта и с чужим дропом — безопасная пшеница.
        if (!CropRegistry.TryGet(cropType, out _))
            cropType = Game.Core.CropType.Wheat;
        lock (_lock)
        {
            if (_crops.ContainsKey((x, y)))
                return; // идемпотентно
            _crops[(x, y)] = new CropData
            {
                X = x,
                Y = y,
                ZoneId = zoneId,
                Type = cropType,
                Stage = stage,
                GrowthTimer = stage >= 4 ? 0f : Math.Min(growthTimer, 60f),
                IsHarvestQueued = stage >= 4,
                IsPlantingQueued = false
            };
            _isDirty = true;
        }
        if (stage >= 4)
            JobBroker.Instance.RegisterHarvest(x, y);
    }

    public void ClearAll()
    {
        lock (_lock)
        {
            _crops.Clear();
            _isDirty = true;
        }
        while (SnapshotQueue.TryDequeue(out _)) { }
    }
}