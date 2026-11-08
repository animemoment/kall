using System;
using System.Collections.Generic;
using Godot;
using Game.Core;

namespace Game.Simulation;

/// <summary>
/// Единый менеджер строительных площадок и чертежей.
/// Полностью заменяет старые BlueprintManager, ConstructionPipeline и WorkProgressTracker.
/// Управляет жизненным циклом от расчистки до доставки и сборки.
/// </summary>
public sealed class ConstructionManager
{
    public static ConstructionManager Instance { get; } = new();

    private readonly object _lock = new();

    // task-21: мост UI→sim (как GroundItemManager.SetMapData): чтобы гейты руды
    // работали и для вызовов без mapData (BlueprintManager.AddBlueprint и т.п.).
    // Устанавливается из Main.OnMapApplied; в sim-потоке читается через ссылку.
    private MapData _mapData;
    private readonly Dictionary<(int X, int Y), ConstructionSite> _sites = new(2048);
    private int _nextGroupId = 1;
    private readonly Dictionary<int, HashSet<(int X, int Y)>> _groupToCells = new();
    private readonly Dictionary<(int X, int Y), int> _cellToGroup = new();
    private readonly Dictionary<int, List<(int X, int Y)>> _groupToPendingWarehouse = new();

    public event Action<ConstructionSite> OnSiteAdded;
    public event Action<List<ConstructionSite>> OnSitesBatchAdded;
    public event Action<(int X, int Y)> OnSiteRemoved;
    public event Action<List<(int X, int Y)>> OnSitesBatchRemoved;
    public event Action<ConstructionSite> OnSiteCompleted;
    public event Action<(int X, int Y), int> OnProgressStageChanged;

    private long _lastReconcileTicks;
    private static readonly long ReconcileIntervalTicks = TimeSpan.FromSeconds(5).Ticks;

    public bool IsSiteAt(int x, int y)
    {
        lock (_lock)
        {
            if (_sites.ContainsKey((x, y)))
                return true;
        }
        // task-22: ЛЮБАЯ клетка габарита сайта — «занято сайтом» (станок 3×4).
        return FootprintRegistry.TryGetOrigin(x, y, out var origin)
            && _sites.ContainsKey(origin);
    }

    /// <summary>
    /// task-22: origin сайта по любой его клетке (габарит); false — клетка не сайт.
    /// </summary>
    public bool TryGetSiteOrigin(int x, int y, out int originX, out int originY)
    {
        lock (_lock)
        {
            if (_sites.ContainsKey((x, y)))
            {
                originX = x;
                originY = y;
                return true;
            }
        }
        if (FootprintRegistry.TryGetOrigin(x, y, out var origin) && _sites.ContainsKey(origin))
        {
            originX = origin.X;
            originY = origin.Y;
            return true;
        }
        originX = x;
        originY = y;
        return false;
    }

    public bool TryGetSite(int x, int y, out ConstructionSite site)
    {
        lock (_lock) return _sites.TryGetValue((x, y), out site);
    }

    public Dictionary<(int X, int Y), ConstructionSite> GetAllSites()
    {
        lock (_lock) return new Dictionary<(int X, int Y), ConstructionSite>(_sites);
    }

    public HashSet<(int X, int Y)> GetWallSites()
    {
        lock (_lock)
        {
            var walls = new HashSet<(int X, int Y)>();
            foreach (var kvp in _sites)
            {
                if (kvp.Value.Definition.IsWall)
                    walls.Add(kvp.Key);
            }
            return walls;
        }
    }

    public bool IsWallSiteAt(int x, int y)
    {
        lock (_lock)
        {
            return _sites.TryGetValue((x, y), out var s) && s.Definition.IsWall;
        }
    }

    /// <summary>
    /// Проверяет, разрешена ли данная стадия работы на строительной площадке (x, y).
    /// </summary>
    public bool IsStageAllowed(int x, int y, JobTypeId typeId)
    {
        if (!TryGetSite(x, y, out var site))
            return true; // Вне чертежей разрешено

        if (typeId == JobTypeId.BlueprintDelivery)
            return site.Stage == ConstructionStage.Supplying;

        if (typeId == JobTypeId.Construction)
            return site.Stage == ConstructionStage.Assembling && site.IsFullySupplied;

        return true;
    }

    public void NotifyWallBuilt(int x, int y)
    {
        // Прогресс гаснет автоматически при завершении площадки
    }

    public void NotifySupplyDone(int x, int y)
    {
        // В новой логике обработано внутри DeliverMaterial
    }

    /// <summary>
    /// task-19 / B-01 / U-02: материалы клетки довезены ПОЛНОСТЬЮ (все типы) — продвинуть
    /// пайплайн. Стройка начинается ТОЛЬКО после 100% доставки материалов на сайт.
    /// Один вход для обоих режимов: обычный монтаж по клетке либо
    /// групповой гейт WaitForAllSiteResources (здание целиком).
    /// </summary>
    public void NotifyMaterialsComplete(int x, int y)
    {
        int groupId = 0;
        bool shouldRegisterNow = false;
        lock (_lock)
        {
            if (!_sites.TryGetValue((x, y), out var site) || !site.IsFullySupplied)
                return;

            site.Stage = BuildConfig.WaitForAllSiteResources ? ConstructionStage.WaitingAll : ConstructionStage.Assembling;
            if (!BuildConfig.WaitForAllSiteResources)
            {
                shouldRegisterNow = true;
            }
            else
            {
                _cellToGroup.TryGetValue((x, y), out groupId);
            }
        }

        if (shouldRegisterNow)
        {
            JobBroker.Instance.RegisterConstructionAt(x, y);
        }
        else if (groupId > 0)
        {
            CheckGroupFullSupply(groupId);
        }
    }

    /// <summary>Инъекция MapData (Main.OnMapApplied) — нужна гейтам руды (task-21).</summary>
    public void SetMapData(MapData mapData) => _mapData = mapData;

    /// <summary>
    /// task-21 / AUDIT task-17 (P4-1): на клетке руды строить нельзя (месторождение
    /// теряется). Остров — по MapData.HasOre; мир за кромкой — по «известному» набору
    /// поля (<see cref="OreWorldField.TryPeekKnownOreLocal"/>): он переживает эвикцию
    /// кэша поля, а «неизвестно» трактуется как «руды нет» (не блокируем игрока из-за
    /// того, что чанк ещё не считан/кэш очищен).
    /// </summary>
    public bool IsOreBlockedCell(int x, int y, MapData mapData = null)
    {
        MapData md = mapData ?? _mapData;
        if (md != null && (uint)x < (uint)md.Width && (uint)y < (uint)md.Height)
            return md.HasOre(x, y);

        ulong seed = Game.Core.World.WorldModel.Seed;
        if (seed == 0)
            return false;
        return Game.Core.OreWorldField.TryPeekKnownOreLocal(x, y, out bool ore) && ore;
    }

    /// <summary>
    /// task-22: можно ли занять ВЕСЬ прямоугольник W×H клеток под постройку.
    /// Проверяются все клетки: в границах карты, трава (не вода/гора/песок),
    /// не руда (task-21), нет готовой структуры/сайта/грядки. Камень НЕ запрещаем:
    /// этап расчистки его добудет (как было со стенами). Мир (mapData==null) —
    /// гейты ставит инструмент/мир, здесь проверяем только занятость.
    /// До 12 итераций, без аллокаций.
    /// </summary>
    public bool CanOccupyFootprint(int originX, int originY, StructureDefinition def, MapData mapData = null)
    {
        if (def == null)
            return false;
        int w = Math.Max(1, def.Width);
        int h = Math.Max(1, def.Height);
        bool multiTile = w > 1 || h > 1;
        MapData md = mapData ?? _mapData;

        for (int y = originY; y < originY + h; y++)
        {
            for (int x = originX; x < originX + w; x++)
            {
                if (md != null)
                {
                    if ((uint)x >= (uint)md.Width || (uint)y >= (uint)md.Height)
                        return false;
                    if (md.Ground[x, y] != TileType.Grass)
                        return false;
                    if (md.HasOre(x, y))
                        return false;
                    if (multiTile && (md.TreeOnGrass[x, y] || md.StoneOnGrass[x, y]))
                        return false; // станок не прорастает сквозь дерево/камень
                }

                // Специфика слоев: полы и крыши не блокируются структурами
                if (def.IsFloor)
                {
                    if (StructureManager.Instance.TryGetFloor(x, y, out var fl) && fl.Definition.Type == def.Type)
                        return false;
                }
                else if (def.IsRoof)
                {
                    if (StructureManager.Instance.HasRoofAt(x, y))
                        return false;
                }
                else
                {
                    if (StructureManager.Instance.IsOccupiedForBuildAt(x, y))
                        return false;
                    if (BuildingManager.Instance.HasBuildingAt(x, y))
                        return false;
                    if (FarmJobManager.Instance.IsGardenBed(x, y) || FarmJobManager.Instance.IsPlotMarked(x, y))
                        return false;
                }
            }
        }
        return true;
    }

    /// <summary>task-22: занять габарит сайта (atomic; false — клетки уже заняты).</summary>
    private static bool OccupySiteFootprint(int originX, int originY, StructureDefinition def)
    {
        if (def != null && (def.IsFloor || def.IsRoof))
            return true; // Полы и крыши не занимают Footprint
        return FootprintRegistry.TryOccupy(originX, originY,
            def != null ? Math.Max(1, def.Width) : 1,
            def != null ? Math.Max(1, def.Height) : 1);
    }

    public bool StartSite(int x, int y, StructureDefinition def, MapData mapData)
    {
        if (def == null) return false;
        int gid = StartSitesBatch(new List<(int X, int Y)> { (x, y) }, def, mapData);
        if (gid > 0 && TryGetSite(x, y, out var site))
        {
            Callable.From(() => OnSiteAdded?.Invoke(site)).CallDeferred();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Старт стройки по списку клеток с автоматической постановкой расчистки и снабжения.
    /// Если указан pendingWarehouseZone, клетки склада активируются только после постройки всех стен здания.
    /// </summary>
    public int StartSitesBatch(List<(int X, int Y)> cells, StructureDefinition def, MapData mapData, List<(int X, int Y)> pendingWarehouseZone = null)
    {
        if (def == null || (cells == null || cells.Count == 0) && (pendingWarehouseZone == null || pendingWarehouseZone.Count == 0))
            return 0;

        var addedSites = new List<ConstructionSite>();
        var treesToChop = new List<(int X, int Y)>();
        var stonesToMine = new List<(int X, int Y)>();
        var itemsToHaul = new List<(int X, int Y)>();

        bool[,] treeMask = mapData?.TreeOnGrass;
        bool[,] stoneMask = mapData?.StoneOnGrass;
        int groupId;

        lock (_lock)
        {
            groupId = _nextGroupId++;
            var groupCells = new HashSet<(int X, int Y)>();
            _groupToCells[groupId] = groupCells;
            if (pendingWarehouseZone != null && pendingWarehouseZone.Count > 0)
            {
                _groupToPendingWarehouse[groupId] = new List<(int X, int Y)>(pendingWarehouseZone);
                foreach (var pos in pendingWarehouseZone)
                {
                    bool hasTree = treeMask != null && (uint)pos.X < (uint)treeMask.GetLength(0) && (uint)pos.Y < (uint)treeMask.GetLength(1) && treeMask[pos.X, pos.Y];
                    bool hasStone = stoneMask != null && (uint)pos.X < (uint)stoneMask.GetLength(0) && (uint)pos.Y < (uint)stoneMask.GetLength(1) && stoneMask[pos.X, pos.Y];
                    bool hasItems = GroundItemManager.Instance.HasItemsAt(pos.X, pos.Y);

                    if (hasTree) treesToChop.Add(pos);
                    if (hasStone) stonesToMine.Add(pos);
                    if (hasItems) itemsToHaul.Add(pos);
                }
            }

            if (cells != null)
            {
                foreach (var pos in cells)
                {
                    if ((!def.IsFloor && !def.IsRoof && StructureManager.Instance.HasStructureAt(pos.X, pos.Y)) || _sites.ContainsKey(pos))
                        continue;
                    // task-21: чертёж на руде площадку не создаёт (жила сохраняется).
                    if (IsOreBlockedCell(pos.X, pos.Y, mapData))
                        continue;
                    // task-22: станок 3×4 валидируется и занимается ЦЕЛИКОМ — иначе
                    // площадка наезжает на стены/грядки/другой станок.
                    if (!CanOccupyFootprint(pos.X, pos.Y, def, mapData))
                        continue;
                    if (!OccupySiteFootprint(pos.X, pos.Y, def))
                        continue;

                    var site = new ConstructionSite(pos.X, pos.Y, def)
                    {
                        SiteId = groupId
                    };

                    int sW = def?.Width ?? 1;
                    int sH = def?.Height ?? 1;

                    bool hasTree = false;
                    bool hasStone = false;
                    bool hasItems = false;

                    for (int dy = 0; dy < sH; dy++)
                    {
                        for (int dx = 0; dx < sW; dx++)
                        {
                            var cell = new Vector2I(pos.X + dx, pos.Y + dy);
                            if (treeMask != null && (uint)cell.X < (uint)treeMask.GetLength(0) && (uint)cell.Y < (uint)treeMask.GetLength(1) && treeMask[cell.X, cell.Y])
                            {
                                hasTree = true;
                                treesToChop.Add((cell.X, cell.Y));
                            }
                            if (stoneMask != null && (uint)cell.X < (uint)stoneMask.GetLength(0) && (uint)cell.Y < (uint)stoneMask.GetLength(1) && stoneMask[cell.X, cell.Y])
                            {
                                hasStone = true;
                                stonesToMine.Add((cell.X, cell.Y));
                            }
                            if (GroundItemManager.Instance.HasItemsAt(cell.X, cell.Y))
                            {
                                hasItems = true;
                                itemsToHaul.Add((cell.X, cell.Y));
                            }
                        }
                    }

                    site.NeedTree = hasTree;
                    site.NeedStone = hasStone;
                    site.NeedHaul = hasItems;

                    if (!site.HasObstacles)
                    {
                        site.Stage = ConstructionStage.Supplying;
                    }

                    _sites[pos] = site;
                    _cellToGroup[pos] = groupId;
                    groupCells.Add(pos);
                    addedSites.Add(site);
                }
            }
        }

        // Запуск работ расчистки вне lock
        if (treesToChop.Count > 0)
            TreeJobManager.Instance.MarkTreesBatch(treesToChop);
        if (stonesToMine.Count > 0)
            StoneJobManager.Instance.MarkStonesBatch(stonesToMine, stoneMask);
        if (itemsToHaul.Count > 0)
        {
            if (StockpileManager.Instance.HasFreeSpace)
            {
                RegisterHaulBatch(itemsToHaul);
            }
            else
            {
                int mw = mapData?.Width ?? 512;
                int mh = mapData?.Height ?? 512;
                foreach (var (ix, iy) in itemsToHaul)
                {
                    GroundItemManager.Instance.TrySpillItemsAdjacent(ix, iy, mw, mh);
                }
            }
        }

        // Регистрация работ доставки для клеток, готовых сразу (без помех)
        var readySupply = new List<(int X, int Y)>();
        foreach (var s in addedSites)
        {
            if (s.Stage == ConstructionStage.Supplying)
                readySupply.Add((s.X, s.Y));
        }

        if (readySupply.Count > 0)
        {
            JobBroker.Instance.RegisterBlueprintBatch(readySupply, def.Type, def.TotalRequiredItems);
        }

        if (addedSites.Count > 0)
        {
            Callable.From(() => OnSitesBatchAdded?.Invoke(addedSites)).CallDeferred();
        }

        return groupId;
    }

    private static void RegisterHaulBatch(List<(int X, int Y)> items)
    {
        var batch = new List<JobData>(items.Count);
        foreach (var (x, y) in items)
        {
            batch.Add(new JobData
            {
                TypeId = JobTypeId.StockpileHauling,
                ExecutionType = JobExecutionType.Hauling,
                PriorityTier = JobPriorityTier.TreeChopping,
                SourceX = x,
                SourceY = y,
                TargetX = x,
                TargetY = y,
                StandX = x,
                StandY = y,
                MaxWorkers = 1
            });
        }
        JobDispatcher.Instance.RegisterBatch(batch);
    }

    public void NotifyTreeCleared(int x, int y)
    {
        CheckAndAdvanceObstacles(x, y, s => s.NeedTree = false);
    }

    public void NotifyStoneCleared(int x, int y)
    {
        CheckAndAdvanceObstacles(x, y, s => s.NeedStone = false);
    }

    public void NotifyHaulCleared(int x, int y)
    {
        CheckAndAdvanceObstacles(x, y, s => s.NeedHaul = false);
    }

    private void CheckAndAdvanceObstacles(int x, int y, Action<ConstructionSite> clearAction)
    {
        ConstructionSite site = null;
        bool becameReady = false;
        lock (_lock)
        {
            if (_sites.TryGetValue((x, y), out site))
            {
                clearAction(site);
                if (site.Stage == ConstructionStage.Clearance && !site.HasObstacles)
                {
                    site.Stage = ConstructionStage.Supplying;
                    becameReady = true;
                }
            }
        }

        if (becameReady && site != null)
        {
            JobBroker.Instance.RegisterBlueprint(x, y, site.Definition.Type, site.Definition.TotalRequiredItems);
        }
    }

    /// <summary>
    /// Прием доставленного ресурса агентом.
    /// </summary>
    public bool DeliverMaterial(int x, int y, ItemId item, int count, out int accepted, out bool isReadyToBuild)
    {
        accepted = 0;
        isReadyToBuild = false;
        ConstructionSite site;
        int groupId = 0;

        lock (_lock)
        {
            if (!_sites.TryGetValue((x, y), out site))
                return false;

            accepted = site.AddDelivered(item, count);
            if (site.IsFullySupplied)
            {
                site.Stage = BuildConfig.WaitForAllSiteResources ? ConstructionStage.WaitingAll : ConstructionStage.Assembling;
                _cellToGroup.TryGetValue((x, y), out groupId);
            }
        }

        if (site.IsFullySupplied)
        {
            if (!BuildConfig.WaitForAllSiteResources)
            {
                isReadyToBuild = true;
                JobBroker.Instance.RegisterConstructionAt(x, y);
            }
            else if (groupId > 0)
            {
                CheckGroupFullSupply(groupId);
            }
        }

        return accepted > 0;
    }

    private void CheckGroupFullSupply(int groupId)
    {
        List<(int X, int Y)> toAssemble = null;
        lock (_lock)
        {
            if (!_groupToCells.TryGetValue(groupId, out var cells))
                return;

            bool allReady = true;
            foreach (var pos in cells)
            {
                if (!_sites.TryGetValue(pos, out var s) || (!s.IsFullySupplied && s.Stage != ConstructionStage.Completed))
                {
                    allReady = false;
                    break;
                }
            }

            if (allReady)
            {
                toAssemble = new List<(int X, int Y)>();
                foreach (var pos in cells)
                {
                    if (_sites.TryGetValue(pos, out var s) && s.Stage == ConstructionStage.WaitingAll)
                    {
                        s.Stage = ConstructionStage.Assembling;
                        toAssemble.Add(pos);
                    }
                }
            }
        }

        if (toAssemble != null)
        {
            foreach (var (x, y) in toAssemble)
            {
                JobBroker.Instance.RegisterConstructionAt(x, y);
            }
        }
    }

    public void ReportWorkProgress(int x, int y, float progressFraction)
    {
        int stage = (int)(Math.Clamp(progressFraction, 0f, 1f) * 6);
        if (stage > 5) stage = 5;

        lock (_lock)
        {
            if (_sites.TryGetValue((x, y), out var site))
            {
                site.WorkProgress = progressFraction;
                if (site.WorkProgressStage == stage)
                    return;
                site.WorkProgressStage = stage;
            }
        }

        OnProgressStageChanged?.Invoke((x, y), stage);
    }

    public bool CompleteConstruction(int x, int y, SimulationContext ctx, out StructureDefinition completedDef)
    {
        return CompleteConstruction(x, y, ctx, null, out completedDef);
    }

    public bool CompleteConstruction(int x, int y, SimulationContext ctx, AgentDataPool pool, out StructureDefinition completedDef)
    {
        ConstructionSite site;
        List<(int X, int Y)> pendingWarehouseToActivate = null;
        lock (_lock)
        {
            // task-22: завершить можно по ЛЮБОЙ клетке габарита — приводим к origin.
            if (!TryGetSiteOrigin(x, y, out int originX, out int originY)
                || !_sites.TryGetValue((originX, originY), out site))
            {
                completedDef = null;
                return false;
            }
            x = originX;
            y = originY;

            site.Stage = ConstructionStage.Completed;
            _sites.Remove((x, y));
            if (_cellToGroup.TryGetValue((x, y), out int gid))
            {
                _cellToGroup.Remove((x, y));
                if (_groupToCells.TryGetValue(gid, out var gcells))
                {
                    gcells.Remove((x, y));
                    if (gcells.Count == 0)
                    {
                        _groupToCells.Remove(gid);
                        _groupToPendingWarehouse.Remove(gid, out pendingWarehouseToActivate);
                    }
                }
            }
        }

        completedDef = site.Definition;
        JobBroker.Instance.UnregisterBlueprint(x, y);

        // Гасим спрайт прогресса
        OnProgressStageChanged?.Invoke((x, y), -1);

        // task-22: сайт освобождает ВЕСЬ габарит, структура тут же занимает его
        // заново (W×H станка) — без окна «клетки свободны».
        FootprintRegistry.ReleaseOrigin(x, y);

        // Добавляем готовую структуру и выталкиваем агентов из возведенной стены
        StructureManager.Instance.AddStructure(x, y, completedDef, ctx, pool);

        // Автоматическая установка пола и крыши для станков (промышленные) и кроватей (жилые)
        if (completedDef.Category == StructureCategory.Production)
        {
            var floorDef = StructureRegistry.ConcreteFloor;
            var roofDef = StructureRegistry.RoofIndustrial;
            for (int dy = 0; dy < completedDef.Height; dy++)
            {
                for (int dx = 0; dx < completedDef.Width; dx++)
                {
                    int fx = x + dx, fy = y + dy;
                    if (!StructureManager.Instance.HasFloorAt(fx, fy))
                        StructureManager.Instance.AddStructure(fx, fy, floorDef, ctx, pool);
                    if (!StructureManager.Instance.HasRoofAt(fx, fy))
                        StructureManager.Instance.AddStructure(fx, fy, roofDef, ctx, pool);
                }
            }
        }
        else if (completedDef.Type == BuildingType.Bed)
        {
            var floorDef = StructureRegistry.WoodFloor;
            var roofDef = StructureRegistry.RoofResidential;
            if (!StructureManager.Instance.HasFloorAt(x, y))
                StructureManager.Instance.AddStructure(x, y, floorDef, ctx, pool);
            if (!StructureManager.Instance.HasRoofAt(x, y))
                StructureManager.Instance.AddStructure(x, y, roofDef, ctx, pool);
        }

        // Если здание склада полностью достроено — активируем его зону
        if (pendingWarehouseToActivate != null && pendingWarehouseToActivate.Count > 0)
        {
            StockpileManager.Instance.ActivatePendingZone(pendingWarehouseToActivate);
        }

        Callable.From(() => OnSiteCompleted?.Invoke(site)).CallDeferred();
        return true;
    }

    public void CancelSite(int x, int y)
    {
        ConstructionSite site;
        List<(int X, int Y)> pendingToRemove = null;
        lock (_lock)
        {
            if (!TryGetSiteOrigin(x, y, out int originX, out int originY)
                || !_sites.Remove((originX, originY), out site))
                return;
            x = originX;
            y = originY;

            if (_cellToGroup.TryGetValue((x, y), out int gid))
            {
                _cellToGroup.Remove((x, y));
                if (_groupToCells.TryGetValue(gid, out var gcells))
                {
                    gcells.Remove((x, y));
                    if (gcells.Count == 0)
                    {
                        _groupToCells.Remove(gid);
                        _groupToPendingWarehouse.Remove(gid, out pendingToRemove);
                    }
                }
            }
        }

        if (pendingToRemove != null && pendingToRemove.Count > 0)
        {
            StockpileManager.Instance.RemoveZoneTilesBatch(pendingToRemove);
        }

        // task-22: освободить все клетки габарита снесённого сайта.
        FootprintRegistry.ReleaseOrigin(x, y);

        UnregisterSiteJobsAndRefund(x, y, site);
        OnProgressStageChanged?.Invoke((x, y), -1);
        Callable.From(() => OnSiteRemoved?.Invoke((x, y))).CallDeferred();
    }

    public void CancelSitesBatch(List<(int X, int Y)> cells)
    {
        if (cells == null || cells.Count == 0) return;

        var removed = new List<((int X, int Y) Pos, ConstructionSite Site)>();
        var pendingBatchesToRemove = new List<List<(int X, int Y)>>();
        lock (_lock)
        {
            foreach (var rawPos in cells)
            {
                // task-22: клетки внутри габарита станка сносят его по origin.
                if (!TryGetSiteOrigin(rawPos.X, rawPos.Y, out int ox, out int oy))
                    continue;
                var pos = (X: ox, Y: oy);
                if (_sites.Remove(pos, out var site))
                {
                    removed.Add((pos, site));
                    if (_cellToGroup.TryGetValue(pos, out int gid))
                    {
                        _cellToGroup.Remove(pos);
                        if (_groupToCells.TryGetValue(gid, out var gcells))
                        {
                            gcells.Remove(pos);
                            if (gcells.Count == 0)
                            {
                                _groupToCells.Remove(gid);
                                if (_groupToPendingWarehouse.Remove(gid, out var pending))
                                {
                                    pendingBatchesToRemove.Add(pending);
                                }
                            }
                        }
                    }
                }
            }
        }

        foreach (var pList in pendingBatchesToRemove)
        {
            StockpileManager.Instance.RemoveZoneTilesBatch(pList);
        }

        var removedPosList = new List<(int X, int Y)>(removed.Count);
        foreach (var (pos, site) in removed)
        {
            removedPosList.Add(pos);
            // task-22: освободить весь габарит снесённого сайта.
            FootprintRegistry.ReleaseOrigin(pos.X, pos.Y);
            UnregisterSiteJobsAndRefund(pos.X, pos.Y, site);
            OnProgressStageChanged?.Invoke(pos, -1);
        }

        if (removedPosList.Count > 0)
        {
            Callable.From(() => OnSitesBatchRemoved?.Invoke(removedPosList)).CallDeferred();
        }
    }

    private static void UnregisterSiteJobsAndRefund(int x, int y, ConstructionSite site)
    {
        TreeJobManager.Instance.UnmarkTree(x, y);
        StoneJobManager.Instance.UnmarkStone(x, y);
        JobDispatcher.Instance.UnregisterJobByPos(x, y, JobTypeId.TreeChopping);
        JobDispatcher.Instance.UnregisterJobByPos(x, y, JobTypeId.Mining);
        JobDispatcher.Instance.UnregisterJobByPos(x, y, JobTypeId.StockpileHauling);
        JobBroker.Instance.UnregisterBlueprint(x, y);
        JobDispatcher.Instance.UnregisterJobByPos(x, y, JobTypeId.Construction);

        // Возврат привезенных ресурсов на землю рядом
        if (site != null)
        {
            foreach (var (item, count) in site.GetAllDelivered())
            {
                if (count > 0)
                {
                    (int X, int Y) stand = (x, y);
                    bool placed = false;
                    for (int oy = -2; oy <= 2 && !placed; oy++)
                    {
                        for (int ox = -2; ox <= 2 && !placed; ox++)
                        {
                            if (ox == 0 && oy == 0) continue;
                            int nx = x + ox, ny = y + oy;
                            if (Game.Core.World.WorldModel.IsInsideIsland(nx, ny) &&
                                !GroundItemManager.Instance.HasItemsAt(nx, ny) &&
                                !StructureManager.Instance.IsWallAt(nx, ny))
                            {
                                stand = (nx, ny);
                                placed = true;
                            }
                        }
                    }
                    GroundItemManager.Instance.SpawnItems(stand.X, stand.Y, item, count);
                }
            }
        }
    }

    public void ClearAll()
    {
        List<ConstructionSite> snapshot;
        lock (_lock)
        {
            snapshot = new List<ConstructionSite>(_sites.Values);
            _sites.Clear();
            _groupToCells.Clear();
            _cellToGroup.Clear();
            _groupToPendingWarehouse.Clear();
            // task-22: все занятые габариты сайтов снимаются вместе с картой.
            FootprintRegistry.Clear();
        }

        var positions = new List<(int X, int Y)>(snapshot.Count);
        foreach (var s in snapshot)
        {
            positions.Add((s.X, s.Y));
            UnregisterSiteJobsAndRefund(s.X, s.Y, s);
            OnProgressStageChanged?.Invoke((s.X, s.Y), -1);
        }

        if (positions.Count > 0)
        {
            Callable.From(() => OnSitesBatchRemoved?.Invoke(positions)).CallDeferred();
        }
    }

    public void ReconcileTick(SimulationContext ctx)
    {
        long now = DateTime.UtcNow.Ticks;
        if (now - _lastReconcileTicks < ReconcileIntervalTicks)
            return;
        _lastReconcileTicks = now;
        if (ctx == null) return;

        List<ConstructionSite> snapshot;
        lock (_lock)
        {
            snapshot = new List<ConstructionSite>(_sites.Values);
        }

        var idx = JobDispatcher.Instance.JobIndex;
        bool[,] treeArr = ctx.TreeOnGrass;
        bool[,] stoneArr = ctx.StoneOnGrass;

        foreach (var site in snapshot)
        {
            int x = site.X, y = site.Y;
            if (StructureManager.Instance.HasStructureAt(x, y))
            {
                CompleteConstruction(x, y, ctx, out _);
                continue;
            }

            if (site.Stage == ConstructionStage.Clearance)
            {
                bool hasTree = treeArr != null && (uint)x < (uint)treeArr.GetLength(0) && (uint)y < (uint)treeArr.GetLength(1) && treeArr[x, y];
                bool hasStone = stoneArr != null && (uint)x < (uint)stoneArr.GetLength(0) && (uint)y < (uint)stoneArr.GetLength(1) && stoneArr[x, y];
                bool hasItems = GroundItemManager.Instance.HasItemsAt(x, y);

                site.NeedTree = hasTree;
                site.NeedStone = hasStone;
                site.NeedHaul = hasItems;

                if (hasItems && !StockpileManager.Instance.HasFreeSpace)
                {
                    GroundItemManager.Instance.TrySpillItemsAdjacent(x, y, ctx.MapWidth, ctx.MapHeight, ctx.SolidWalls);
                }

                if (!site.HasObstacles)
                {
                    site.Stage = ConstructionStage.Supplying;
                    JobBroker.Instance.RegisterBlueprint(x, y, site.Definition.Type, site.Definition.TotalRequiredItems);
                }
            }
            else if (site.Stage == ConstructionStage.Supplying)
            {
                if (site.IsFullySupplied)
                {
                    NotifyMaterialsComplete(x, y);
                }
                else if (!idx.HasJobAt(x, y, JobTypeId.BlueprintDelivery))
                {
                    JobBroker.Instance.RegisterBlueprint(x, y, site.Definition.Type, site.Definition.TotalRequiredItems);
                }
            }
            else if (site.Stage == ConstructionStage.WaitingAll)
            {
                // J-14: у WaitingAll не было самопочинки — продвинуть группу могла
                // ТОЛЬКО очередная доставка (DeliverMaterial → CheckGroupFullSupply).
                // Если все клетки группы уже полностью снабжены, а доставок больше
                // не будет, группа висела в WaitingAll вечно: стройка «встала, хотя
                // всё расчищено и материал есть». Теперь состояние перепроверяется
                // на каждом reconcile-тике, и группа продвигается сама.
                if (_cellToGroup.TryGetValue((x, y), out int waitingGroupId) && waitingGroupId > 0)
                {
                    CheckGroupFullSupply(waitingGroupId);
                }

                // Всё ещё висим и материала не хватает — значит доставка потерялась
                // (работу сняли/выполнили мимо счётчика). Пересоздаём blueprint,
                // чтобы материал можно было донести; для уже снабжённой клетки
                // это было бы перепроизводство, поэтому только при !IsFullySupplied.
                if (site.Stage == ConstructionStage.WaitingAll
                    && !site.IsFullySupplied
                    && !idx.HasJobAt(x, y, JobTypeId.BlueprintDelivery))
                {
                    site.Stage = ConstructionStage.Supplying;
                    JobBroker.Instance.RegisterBlueprint(x, y, site.Definition.Type, site.Definition.TotalRequiredItems);
                }
            }
            else if (site.Stage == ConstructionStage.Assembling)
            {
                if (!site.IsFullySupplied)
                {
                    // B-01 / U-02: Если на стадии Assembling материалы не поставлены на 100%,
                    // возвращаем в Supplying и пересоздаем BlueprintDelivery.
                    site.Stage = ConstructionStage.Supplying;
                    if (idx.HasJobAt(x, y, JobTypeId.Construction))
                    {
                        JobDispatcher.Instance.UnregisterJobByPos(x, y, JobTypeId.Construction);
                    }
                    if (!idx.HasJobAt(x, y, JobTypeId.BlueprintDelivery))
                    {
                        JobBroker.Instance.RegisterBlueprint(x, y, site.Definition.Type, site.Definition.TotalRequiredItems);
                    }
                }
                else if (!idx.HasJobAt(x, y, JobTypeId.Construction))
                {
                    JobBroker.Instance.RegisterConstructionAt(x, y);
                }
            }
        }
    }
}
