using System;
using System.Collections.Generic;
using Godot;
using Game.Core;
using Game.Core.World;

namespace Game.Simulation;

public class FarmJobManager
{
    public static FarmJobManager Instance { get; } = new();

    private readonly object _lock = new();
    private readonly HashSet<(int X, int Y)> _markedPlots = new(1024);
    private readonly HashSet<(int X, int Y)> _completedBeds = new(1024);
    private readonly Dictionary<(int X, int Y), (bool NeedTree, bool NeedStone, bool NeedItem)> _plotsNeedingClearance = new();

    // GD-27: Таймер простоя вскопанных грядок без посевов (в секундах игрового времени).
    // Если грядка пустует более 3-5 игровых дней (4 дня = 4 * 12000 = 48000 сек), она зарастает обычной травой (TileType.Grass).
    public const float BedIdleOvergrowTimeSeconds = 4f * 12000f;
    private readonly Dictionary<(int X, int Y), float> _idleBedTimers = new();
    private readonly List<(int X, int Y)> _overgrownBuffer = new();

    /// <summary>
    /// P7б §119: per-cell прогресс вскопки 0..1 — переживает прерывание агента.
    /// Пишет OnCancel хендлера, читает OnStart, чистят Unmark*/CompletePlot/ClearAll.
    /// </summary>
    public CellProgressStore TillProgress { get; } = new();

    public event Action<(int X, int Y)> OnPlotMarked;
    public event Action<List<(int X, int Y)>> OnPlotsBatchMarked;
    public event Action<(int X, int Y)> OnPlotUnmarked;
    public event Action<List<(int X, int Y)>> OnPlotsBatchUnmarked;
    public event Action<(int X, int Y)> OnPlotCompleted;

    public void MarkPlot(int x, int y, bool[,] treeOnGrass, bool[,] stoneOnGrass = null)
    {
        bool spillItems = false;
        bool markTree = false;
        bool markStone = false;
        bool registerPlot = false;
        bool fireEvent = false;

        // FIX (bughunt 12-9): под _lock остаются ТОЛЬКО собственные множества фермы.
        // Маски деревьев/камней — read-only данные вызывающего (лока не требуют), а
        // GroundItemManager.HasItemsAt в прежнем коде брал СВОЙ глобальный лок внутри
        // нашего: держали чужой лок, пока ждали наш. Считаем «есть ли предметы» ДО
        // захвата _lock, а гонку «кучу увезли, пока мы шли к локу» закрываем парной
        // перепроверкой ПОСЛЕ него (см. блок ниже).
        bool hasTree = treeOnGrass != null && (uint)x < (uint)treeOnGrass.GetLength(0) && (uint)y < (uint)treeMaskHeight(treeOnGrass) && treeOnGrass[x, y];
        bool hasStone = stoneOnGrass != null && (uint)x < (uint)stoneOnGrass.GetLength(0) && (uint)y < (uint)stoneMaskHeight(stoneOnGrass) && stoneOnGrass[x, y];
        bool hasItems = GroundItemManager.Instance.HasItemsAt(x, y);

        lock (_lock)
        {
            if (_completedBeds.Contains((x, y))) return;

            if (_markedPlots.Add((x, y)))
            {
                if (hasTree || hasStone || hasItems)
                {
                    _plotsNeedingClearance[(x, y)] = (hasTree, hasStone, hasItems);
                    markTree = hasTree;
                    markStone = hasStone;
                    spillItems = hasItems;
                }
                else
                {
                    registerPlot = true;
                }
                fireEvent = true;
            }
        }

        // FIX (bughunt 12-9): кучу могли убрать между HasItemsAt и захватом _lock.
        // Тогда NeedItem=true осталось бы навсегда (запись о расчистке уже создана, а
        // NotifyItemCleared от носильщика пришёл раньше неё) и участок не получил бы
        // работу на вскопку. NotifyItemCleared идемпотентен: он сбросит NeedItem и,
        // если препятствий больше нет, зарегистрирует работу.
        if (spillItems && !GroundItemManager.Instance.HasItemsAt(x, y))
        {
            NotifyItemCleared(x, y);
        }

        // FIX (bughunt 12-6): границы спилла берём у ВЛАДЕЛЬЦА кромки острова —
        // WorldModel.IslandTiles (1024×1024), а не литерал половины острова. Со старым
        // литералом у любой клетки x ≥ 512 / y ≥ 512 все 8 соседей отбраковывались как
        // «за картой»: предметы с грядки на восточной/южной половине не сдвигались, клетка
        // навсегда оставалась «грязной», и работа на вскопку не появлялась. Тот же источник
        // размеров, что у остальных вызовов спилла в симуляции (ctx.MapWidth/MapHeight).
        if (spillItems) GroundItemManager.Instance.TrySpillItemsAdjacent(x, y, WorldModel.IslandTiles, WorldModel.IslandTiles);
        if (markTree) TreeJobManager.Instance.MarkTree(x, y);
        if (markStone) StoneJobManager.Instance.MarkStone(x, y, stoneOnGrass);
        if (registerPlot) JobBroker.Instance.RegisterFarmPlot(x, y);
        if (fireEvent) Callable.From(() => OnPlotMarked?.Invoke((x, y))).CallDeferred();
    }

    public void MarkPlotsBatch(List<(int X, int Y)> plots, bool[,] treeOnGrass, bool[,] stoneOnGrass = null)
    {
        if (plots == null || plots.Count == 0) return;

        var addedList = new List<(int X, int Y)>(plots.Count);
        var readyToPlow = new List<(int X, int Y)>();
        var treesToChop = new List<(int X, int Y)>();
        var stonesToMine = new List<(int X, int Y)>();
        var itemsToClear = new List<(int X, int Y)>();

        // J-15: массовая разметка (70k клеток фермы) не должна держать _lock на весь
    // проход и не должна дёргать глобальный лок GroundItemManager на каждую клетку.
    // Раньше весь цикл шёл под одним lock(_lock), а HasItemsAt() брал ещё и
    // глобальный лок предметов — UI-поток блокировал sim-поток на секунды
    // (фризы S5), плюс на 70k клеток приходилось 70k захватов чужого лока.
    // Теперь: (а) лок берётся окнами по MarkChunkCells клеток, (б) при полностью
    // пустой земле проверка предметов вообще не выполняется (одно чтение счётчика).
    bool anyGroundItems = GroundItemManager.Instance.TotalGroundPilesCount > 0;

    const int MarkChunkCells = 4096;
    for (int start = 0; start < plots.Count; start += MarkChunkCells)
    {
        int end = Math.Min(start + MarkChunkCells, plots.Count);
        lock (_lock)
        {
            for (int pi = start; pi < end; pi++)
            {
                var pos = plots[pi];
                if (!_completedBeds.Contains(pos) && _markedPlots.Add(pos))
                {
                    addedList.Add(pos);
                    bool hasTree = treeOnGrass != null && (uint)pos.X < (uint)treeOnGrass.GetLength(0) && (uint)pos.Y < (uint)treeMaskHeight(treeOnGrass) && treeOnGrass[pos.X, pos.Y];
                    bool hasStone = stoneOnGrass != null && (uint)pos.X < (uint)stoneOnGrass.GetLength(0) && (uint)pos.Y < (uint)stoneMaskHeight(stoneOnGrass) && stoneOnGrass[pos.X, pos.Y];
                    // FIX (bughunt 12-9, аудит): здесь HasItemsAt СОЗНАТЕЛЬНО остаётся внутри
                    // окна лока. Это НЕ ABBA: порядок F->G существует только в эту сторону
                    // (GroundItemManager зовёт FarmJobManager.Notify* строго ПОСЛЕ снятия
                    // своего лока), обратного G->F нет. Вынести проверку за лок можно только
                    // двумя проходами на окно: (1) два лока на окно вместо одного и
                    // (2) «потерянное обновление» — кучу увозят между проверкой и локом, а
                    // NotifyItemCleared приходит раньше записи о расчистке (та же гонка, что
                    // закрыта в MarkPlot парной перепроверкой). Для 70k клеток это дороже
                    // бага, поэтому оставлено как было; J-15 уже держит окна и префильтр.
                    bool hasItems = anyGroundItems && GroundItemManager.Instance.HasItemsAt(pos.X, pos.Y);

                    if (hasTree || hasStone || hasItems)
                    {
                        _plotsNeedingClearance[pos] = (hasTree, hasStone, hasItems);
                        if (hasTree) treesToChop.Add(pos);
                        if (hasStone) stonesToMine.Add(pos);
                        if (hasItems) itemsToClear.Add(pos);
                    }
                    else
                    {
                        readyToPlow.Add(pos);
                    }
                }
            }
        }
    }

        if (itemsToClear.Count > 0)
        {
            foreach (var (ix, iy) in itemsToClear)
            {
                // FIX (bughunt 12-6): см. MarkPlot — реальная кромка острова вместо 512×512,
                // иначе пакетная разметка не двигала предметы на восточной/южной половине.
                GroundItemManager.Instance.TrySpillItemsAdjacent(ix, iy, WorldModel.IslandTiles, WorldModel.IslandTiles);
            }
        }

        if (stonesToMine.Count > 0)
        {
            StoneJobManager.Instance.MarkStonesBatch(stonesToMine, stoneOnGrass);
        }

        if (treesToChop.Count > 0)
        {
            TreeJobManager.Instance.MarkTreesBatch(treesToChop);
        }

        if (readyToPlow.Count > 0)
        {
            JobBroker.Instance.RegisterFarmPlotBatch(readyToPlow);
        }

        if (addedList.Count > 0)
        {
            Callable.From(() => OnPlotsBatchMarked?.Invoke(addedList)).CallDeferred();
        }
    }

    private static int treeMaskHeight(bool[,] mask) => mask != null ? mask.GetLength(1) : 0;
    private static int stoneMaskHeight(bool[,] mask) => mask != null ? mask.GetLength(1) : 0;

    public void NotifyTreeCleared(int x, int y)
    {
        // FIX (bughunt 12-9): HasItemsAt берёт лок GroundItemManager — не вызываем его
        // под нашим _lock. Гонки «кучу увезли сразу после чтения» нет: TakeItemsWithType
        // шлёт NotifyItemCleared ПОСЛЕ снятия Ground-лока, наш лок сериализует этот вызов
        // за нашей секцией, и NeedItem будет сброшен следующим уведомлением.
        bool hasItems = GroundItemManager.Instance.HasItemsAt(x, y);
        bool becameReady = false;
        lock (_lock)
        {
            if (_plotsNeedingClearance.TryGetValue((x, y), out var req))
            {
                req.NeedTree = false;
                req.NeedItem = hasItems;
                if (!req.NeedStone && !req.NeedItem)
                {
                    _plotsNeedingClearance.Remove((x, y));
                    becameReady = _markedPlots.Contains((x, y));
                }
                else
                {
                    _plotsNeedingClearance[(x, y)] = req;
                }
            }
        }
        if (becameReady)
        {
            JobBroker.Instance.RegisterFarmPlot(x, y);
        }
    }

    public void NotifyStoneCleared(int x, int y)
    {
        // FIX (bughunt 12-9): см. NotifyTreeCleared — чужой лок читаем ДО нашего.
        bool hasItems = GroundItemManager.Instance.HasItemsAt(x, y);
        bool becameReady = false;
        lock (_lock)
        {
            if (_plotsNeedingClearance.TryGetValue((x, y), out var req))
            {
                req.NeedStone = false;
                req.NeedItem = hasItems;
                if (!req.NeedTree && !req.NeedItem)
                {
                    _plotsNeedingClearance.Remove((x, y));
                    becameReady = _markedPlots.Contains((x, y));
                }
                else
                {
                    _plotsNeedingClearance[(x, y)] = req;
                }
            }
        }
        if (becameReady)
        {
            JobBroker.Instance.RegisterFarmPlot(x, y);
        }
    }

    public void NotifyItemCleared(int x, int y)
    {
        bool becameReady = false;
        lock (_lock)
        {
            if (_plotsNeedingClearance.TryGetValue((x, y), out var req))
            {
                req.NeedItem = false;
                if (!req.NeedTree && !req.NeedStone)
                {
                    _plotsNeedingClearance.Remove((x, y));
                    becameReady = _markedPlots.Contains((x, y));
                }
                else
                {
                    _plotsNeedingClearance[(x, y)] = req;
                }
            }
        }
        if (becameReady)
        {
            JobBroker.Instance.RegisterFarmPlot(x, y);
        }
    }

    /// <summary>
    /// GD-34: Снятие всех активных и запланированных работ фермы (вспашка, посадка, сбор) на клетке
    /// через JobDispatcher.Instance.TryUnregisterJob, чтобы колонисты не обрабатывали удалённые грядки.
    /// </summary>
    private static void CancelFarmJobsAt(int x, int y)
    {
        var dispatcher = JobDispatcher.Instance;
        if (dispatcher == null) return;
        var index = dispatcher.JobIndex;
        if (index == null) return;

        int farmJobId = index.TryGetJobIdByPos(x, y, JobTypeId.Farming, out bool hasFarm);
        if (hasFarm && farmJobId >= 0)
            dispatcher.TryUnregisterJob(farmJobId);

        int plantJobId = index.TryGetJobIdByPos(x, y, JobTypeId.Planting, out bool hasPlant);
        if (hasPlant && plantJobId >= 0)
            dispatcher.TryUnregisterJob(plantJobId);

        int harvJobId = index.TryGetJobIdByPos(x, y, JobTypeId.Harvesting, out bool hasHarv);
        if (hasHarv && harvJobId >= 0)
            dispatcher.TryUnregisterJob(harvJobId);
    }

    /// <summary>
    /// GD-34: Удаление/отмена грядки с принудительным снятием всех работ на клетке.
    /// </summary>
    public void UnregisterBed(int x, int y)
    {
        UnmarkPlot(x, y);
    }

    public void UnmarkPlot(int x, int y)
    {
        bool unregister = false;
        lock (_lock)
        {
            _plotsNeedingClearance.Remove((x, y));
            _completedBeds.Remove((x, y));
            _idleBedTimers.Remove((x, y));
            TillProgress.Clear(x, y);
            if (_markedPlots.Remove((x, y)))
            {
                unregister = true;
            }
        }
        if (unregister)
        {
            JobBroker.Instance.UnregisterFarmPlot(x, y);
        }
        // GD-34: Снимаем все запланированные и назначенные работы на удаляемой клетке
        CancelFarmJobsAt(x, y);
        Callable.From(() => OnPlotUnmarked?.Invoke((x, y))).CallDeferred();
    }

    public void UnmarkPlotsBatch(List<(int X, int Y)> plots)
    {
        if (plots == null || plots.Count == 0) return;

        var removedList = new List<(int X, int Y)>(plots.Count);
        lock (_lock)
        {
            foreach (var pos in plots)
            {
                _plotsNeedingClearance.Remove(pos);
                _completedBeds.Remove(pos);
                _markedPlots.Remove(pos);
                _idleBedTimers.Remove(pos);
                TillProgress.Clear(pos.X, pos.Y);
                removedList.Add(pos);
            }
        }

        if (removedList.Count > 0)
        {
            JobBroker.Instance.UnregisterFarmPlotBatch(removedList);
            // GD-34: Снимаем все запланированные и назначенные работы на удаляемых грядках
            for (int i = 0; i < removedList.Count; i++)
            {
                CancelFarmJobsAt(removedList[i].X, removedList[i].Y);
            }
            Callable.From(() => OnPlotsBatchUnmarked?.Invoke(removedList)).CallDeferred();
        }
    }

    public bool IsPlotMarked(int x, int y)
    {
        lock (_lock)
        {
            return _markedPlots.Contains((x, y));
        }
    }

    public bool IsGardenBed(int x, int y)
    {
        lock (_lock)
        {
            return _completedBeds.Contains((x, y));
        }
    }

    /// <summary>
    /// Снимок грядок в bool-маску W×H (один lock на всех, а не 262k).
    /// Для тика влажности: читается из Parallel.For без локов.
    /// OOB грядки отбрасываются.
    /// </summary>
    public bool[,] BuildGardenBedMask(int w, int h)
    {
        var mask = new bool[w, h];
        lock (_lock)
        {
            foreach (var (x, y) in _completedBeds)
            {
                if ((uint)x < (uint)w && (uint)y < (uint)h)
                    mask[x, y] = true;
            }
        }
        return mask;
    }

    /// <summary>
    /// Flat-версия маски (bool[w*h], индекс y*w+x) для HumidityMap.Tick:
    /// flat-чтение без bounds-check ×2 и вычисления индекса [,].
    /// </summary>
    public bool[] BuildGardenBedFlatMask(int w, int h)
    {
        var mask = new bool[w * h];
        lock (_lock)
        {
            foreach (var (x, y) in _completedBeds)
            {
                if ((uint)x < (uint)w && (uint)y < (uint)h)
                    mask[y * w + x] = true;
            }
        }
        return mask;
    }

    /// <summary>
    /// Заполнение переиспользуемого буфера маски (bool[w*h], индекс y*w+x) без аллокаций.
    /// </summary>
    public void FillGardenBedFlatMask(bool[] mask, int w, int h)
    {
        Array.Clear(mask, 0, mask.Length);
        lock (_lock)
        {
            foreach (var (x, y) in _completedBeds)
            {
                if ((uint)x < (uint)w && (uint)y < (uint)h)
                    mask[y * w + x] = true;
            }
        }
    }

    public HashSet<(int X, int Y)> GetAllMarkedPlots()
    {
        lock (_lock)
        {
            return new HashSet<(int X, int Y)>(_markedPlots);
        }
    }

    public HashSet<(int X, int Y)> GetAllCompletedBeds()
    {
        lock (_lock)
        {
            return new HashSet<(int X, int Y)>(_completedBeds);
        }
    }

    public void CompletePlot(int x, int y)
    {
        // FIX (bughunt 12-9): под _lock — ТОЛЬКО состояние самой фермы. Раньше внутри
        // секции вызывались JobBroker.UnregisterFarmPlot (берёт _registerLock индекса
        // работ) и Godot Callable: это давало инверсию порядка F->J против J->F из
        // JobValidator/JobDispatcher (ABBA-дедлок на массовой сдаче грядок), а
        // CallDeferred/Godot-API под локом — ещё и обращение к движку не из главного
        // потока. Сначала помечаем участок завершённым, потом (вне лока) снимаем работу:
        // обратный порядок оставил бы «призрачную» работу для уже завершённой грядки.
        lock (_lock)
        {
            _markedPlots.Remove((x, y));
            _completedBeds.Add((x, y));
            _idleBedTimers[(x, y)] = 0f;
            TillProgress.Clear(x, y);
        }

        JobBroker.Instance.UnregisterFarmPlot(x, y);
        Callable.From(() => OnPlotCompleted?.Invoke((x, y))).CallDeferred();

        if (FarmZoneManager.Instance.TryGetZoneAt(x, y, out var zone) && zone != null && zone.AutoPlantEnabled)
        {
            JobBroker.Instance.RegisterPlanting(x, y, zone.Id);
        }
    }

    /// <summary>Очистить все грядки и разметку при смене региона / перезапуске карты.</summary>
    public void ClearAll()
    {
        // FIX (bughunt 12-9): Godot Callable.From(...).CallDeferred() не должен вызываться
        // под нашим _lock (движок/главный поток + лишнее время в критической секции во
        // время смены региона). Снимок старых участков собираем под локом, событие шлём
        // после выхода из него.
        List<(int X, int Y)> oldPlots;
        lock (_lock)
        {
            oldPlots = new List<(int X, int Y)>(_markedPlots);
            foreach (var bed in _completedBeds)
            {
                if (!oldPlots.Contains(bed))
                    oldPlots.Add(bed);
            }
            _markedPlots.Clear();
            _plotsNeedingClearance.Clear();
            _completedBeds.Clear();
            _idleBedTimers.Clear();
            TillProgress.ClearAll();
        }
        if (oldPlots.Count > 0)
        {
            // GD-34: Снимаем все запланированные и назначенные работы на всех очищаемых грядках
            for (int i = 0; i < oldPlots.Count; i++)
            {
                CancelFarmJobsAt(oldPlots[i].X, oldPlots[i].Y);
            }
            Callable.From(() => OnPlotsBatchUnmarked?.Invoke(oldPlots)).CallDeferred();
        }
    }

    /// <summary>
    /// GD-27: Проверка простоя вскопанных грядок без посевов.
    /// Если грядка пустует более 3-5 игровых дней (BedIdleOvergrowTimeSeconds),
    /// она зарастает обычной травой (TileType.Grass).
    /// </summary>
    public void UpdateIdleBeds(float deltaTime, SimulationContext ctx)
    {
        _overgrownBuffer.Clear();

        lock (_lock)
        {
            if (_completedBeds.Count == 0)
            {
                if (_idleBedTimers.Count > 0)
                    _idleBedTimers.Clear();
                return;
            }

            foreach (var pos in _completedBeds)
            {
                // Если на грядке уже растёт культура — сбрасываем / удаляем таймер простоя
                if (CropGrowthManager.Instance.HasCrop(pos.X, pos.Y))
                {
                    _idleBedTimers.Remove(pos);
                    continue;
                }

                // Грядка пустует: накапливаем таймер простоя
                _idleBedTimers.TryGetValue(pos, out float idleTime);
                idleTime += deltaTime;
                if (idleTime >= BedIdleOvergrowTimeSeconds)
                {
                    _overgrownBuffer.Add(pos);
                }
                else
                {
                    _idleBedTimers[pos] = idleTime;
                }
            }

            for (int i = 0; i < _overgrownBuffer.Count; i++)
            {
                var pos = _overgrownBuffer[i];
                _completedBeds.Remove(pos);
                _markedPlots.Remove(pos);
                _plotsNeedingClearance.Remove(pos);
                _idleBedTimers.Remove(pos);
                TillProgress.Clear(pos.X, pos.Y);
            }
        }

        if (_overgrownBuffer.Count > 0)
        {
            for (int i = 0; i < _overgrownBuffer.Count; i++)
            {
                var (x, y) = _overgrownBuffer[i];
                JobBroker.Instance.UnregisterPlanting(x, y);
                JobBroker.Instance.UnregisterFarmPlot(x, y);

                if (ctx?.Ground != null && (uint)x < (uint)ctx.MapWidth && (uint)y < (uint)ctx.MapHeight)
                {
                    ctx.Ground[x, y] = TileType.Grass;
                }
            }

            var batchCopy = new List<(int X, int Y)>(_overgrownBuffer);
            Callable.From(() => OnPlotsBatchUnmarked?.Invoke(batchCopy)).CallDeferred();
        }
    }
}
