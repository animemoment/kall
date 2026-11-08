using System;
using System.Collections.Generic;
using Godot;
using Game.Core;

namespace Game.Simulation;

/// <summary>
/// Единый менеджер для ВСЕХ возведенных в мире структур (стены любых типов,
/// мебель, станки, двери и т.д.).
/// Полностью заменяет разрозненные WallBuildManager и BuildingManager.
/// Потокобезопасен. Автоматически обновляет физику SolidWalls и кэш навигации.
/// </summary>
public sealed class StructureManager
{
    public static StructureManager Instance { get; } = new();

    private readonly object _lock = new();
    private readonly Dictionary<(int X, int Y), StructureInstance> _structures = new(4096);
    private readonly Dictionary<(int X, int Y), StructureInstance> _floors = new(4096);
    private readonly Dictionary<(int X, int Y), StructureInstance> _roofs = new(4096);

    // События для рендера и подсистем
    public event Action<StructureInstance> OnStructureAdded;
    public event Action<(int X, int Y), StructureDefinition> OnStructureRemoved;
    public event Action<List<(int X, int Y)>> OnWallTilesUpdated;

    /// <summary>
    /// Хук сноса центрального блока (§119 P5): снесён CentralBlock в (x, y) → снести
    /// рабочую зону с центром в этой клетке (работы падают в непокрытые). Подписчик —
    /// WorkZoneManager (static ctor подключает RemoveZoneAt); до первого доступа к
    /// синглтону хук null — вызов молча пропускается.
    /// </summary>
    public static Action<int, int> CentralBlockRemovedHook { get; set; }

    // Дебаунс уведомлений для автотайлинга стен (перенесено из WallBuildManager)
    private readonly HashSet<(int X, int Y)> _pendingWallUpdates = new();
    private long _lastFlushTicks;
    private const long DebounceMs = 200;

    public bool HasStructureAt(int x, int y)
    {
        lock (_lock)
        {
            return _structures.ContainsKey((x, y));
        }
    }

    public bool HasFloorAt(int x, int y)
    {
        lock (_lock)
        {
            return _floors.ContainsKey((x, y));
        }
    }

    public bool HasRoofAt(int x, int y)
    {
        lock (_lock)
        {
            return _roofs.ContainsKey((x, y));
        }
    }

    public bool TryGetFloor(int x, int y, out StructureInstance floor)
    {
        lock (_lock)
        {
            return _floors.TryGetValue((x, y), out floor);
        }
    }

    public bool TryGetRoof(int x, int y, out StructureInstance roof)
    {
        lock (_lock)
        {
            return _roofs.TryGetValue((x, y), out roof);
        }
    }

    /// <summary>
    /// task-22: занята ли клетка для НОВОЙ постройки — готовой структурой (origin)
    /// ИЛИ любой клеткой габарита W×H (сайт/станок, FootprintRegistry).
    /// </summary>
    public bool IsOccupiedForBuildAt(int x, int y)
    {
        if (HasStructureAt(x, y))
            return true;
        return FootprintRegistry.IsOccupied(x, y);
    }

    public bool IsWallAt(int x, int y)
    {
        lock (_lock)
        {
            return _structures.TryGetValue((x, y), out var s) && s.Definition.IsWall;
        }
    }

    public bool TryGetStructure(int x, int y, out StructureInstance structure)
    {
        lock (_lock)
        {
            if (_structures.TryGetValue((x, y), out structure))
                return true;
        }
        // task-22: клик по ЛЮБОЙ клетке станка 3×4 находит его по origin.
        if (FootprintRegistry.TryGetOrigin(x, y, out var origin))
        {
            lock (_lock)
                return _structures.TryGetValue(origin, out structure);
        }
        return false;
    }

    /// <summary>
    /// Проверяет, возведена ли структура и готова ли она к эксплуатации (не находится на этапе стройки).
    /// </summary>
    public bool IsStructureOperational(int x, int y)
    {
        if (ConstructionManager.Instance.IsSiteAt(x, y))
            return false;

        {
            lock (_lock)
            {
                if (_structures.TryGetValue((x, y), out var s) && s.IsOperational)
                    return true;
            }
            // task-22: габарит станка — операционность по origin.
            if (FootprintRegistry.TryGetOrigin(x, y, out var origin))
            {
                lock (_lock)
                    return _structures.TryGetValue(origin, out var s2) && s2.IsOperational;
            }
            return false;
        }
    }

    /// <summary>
    /// Получить готовую к эксплуатации структуру (станки, мебель, производство).
    /// Если объект еще строится (есть в ConstructionManager), возвращает false.
    /// </summary>
    public bool TryGetOperationalStructure(int x, int y, out StructureInstance structure)
    {
        structure = null;
        if (ConstructionManager.Instance.IsSiteAt(x, y))
            return false;

        lock (_lock)
        {
            if (_structures.TryGetValue((x, y), out structure) && structure.IsOperational)
                return true;
        }
        // task-22: любая клетка габарита ссылается на станок в origin.
        if (FootprintRegistry.TryGetOrigin(x, y, out var origin))
        {
            lock (_lock)
            {
                if (_structures.TryGetValue(origin, out structure) && structure.IsOperational)
                    return true;
            }
        }
        structure = null;
        return false;
    }

    /// <summary>
    /// GD-33: Поиск ближайшей готовой к эксплуатации свободной кровати (BuildingType.Bed) для отдыха.
    /// </summary>
    public bool TryFindNearestOperationalBed(int startX, int startY, int maxRadiusTiles, Func<int, int, bool> isFreePredicate, out (int X, int Y) bedPos)
    {
        bedPos = default;
        int bestDistSq = maxRadiusTiles * maxRadiusTiles;
        bool found = false;

        lock (_lock)
        {
            foreach (var kvp in _structures)
            {
                var s = kvp.Value;
                if (!s.IsOperational || s.Definition.Type != BuildingType.Bed)
                    continue;

                if (ConstructionManager.Instance.IsSiteAt(s.X, s.Y))
                    continue;

                if (isFreePredicate != null && !isFreePredicate(s.X, s.Y))
                    continue;

                int dx = s.X - startX;
                int dy = s.Y - startY;
                int dSq = dx * dx + dy * dy;
                if (dSq <= bestDistSq)
                {
                    bestDistSq = dSq;
                    bedPos = (s.X, s.Y);
                    found = true;
                }
            }
        }

        return found;
    }

    public bool AddStructure(int x, int y, StructureDefinition def, SimulationContext ctx = null, AgentDataPool pool = null)
    {
        if (def == null) return false;

        // Полы и крыши — независимые слои, не блокирующие движение и не занимающие Footprint
        if (def.IsFloor)
        {
            StructureInstance floorInst;
            lock (_lock)
            {
                floorInst = new StructureInstance(x, y, def);
                _floors[(x, y)] = floorInst;
            }
            Callable.From(() => OnStructureAdded?.Invoke(floorInst)).CallDeferred();
            return true;
        }

        if (def.IsRoof)
        {
            StructureInstance roofInst;
            lock (_lock)
            {
                roofInst = new StructureInstance(x, y, def);
                _roofs[(x, y)] = roofInst;
            }
            Callable.From(() => OnStructureAdded?.Invoke(roofInst)).CallDeferred();
            return true;
        }

        // task-22: занять ВЕСЬ габарит W×H (3×4 станок = 12 клеток). Если сайт
        // ещё не освободил клетки (порядок CompleteConstruction), это false.
        if (!FootprintRegistry.TryOccupy(x, y, def.Width, def.Height))
            return false;

        StructureInstance instance;
        lock (_lock)
        {
            if (_structures.ContainsKey((x, y)))
            {
                FootprintRegistry.ReleaseOrigin(x, y);
                return false;
            }

            instance = new StructureInstance(x, y, def);
            _structures[(x, y)] = instance;

            if (def.IsWall)
            {
                NotifyWallChangedLocked(x, y);
            }
        }

        // Обновление физической проходимости и навигации (по всему габариту)
        if (def.BlocksMovement && ctx != null)
        {
            for (int dy = 0; dy < def.Height; dy++)
            {
                for (int dx = 0; dx < def.Width; dx++)
                {
                    int cx = x + dx;
                    int cy = y + dy;
                    if ((uint)cx < (uint)ctx.MapWidth && (uint)cy < (uint)ctx.MapHeight)
                    {
                        ctx.SolidWalls[cx, cy] = true;
                    }
                }
            }
            FlowFieldManager.Instance?.ClearCache();
            HierarchicalPathfinder.Instance?.Invalidate();

            // Выселение агентов, если они стояли внутри габарита возведенной структуры
            if (ctx.SpatialGrid != null && ctx.Movement != null && pool != null)
            {
                for (int dy = 0; dy < def.Height; dy++)
                {
                    for (int dx = 0; dx < def.Width; dx++)
                    {
                        int cx = x + dx;
                        int cy = y + dy;
                        int insideAgent = ctx.SpatialGrid.GetFirstAgent(cx, cy);
                        while (insideAgent != -1)
                        {
                            ctx.Movement.EjectFromWall(insideAgent, cx, cy, pool, ctx);
                            insideAgent = ctx.SpatialGrid.GetNextAgent(insideAgent, pool);
                        }
                    }
                }
            }
        }

        Callable.From(() => OnStructureAdded?.Invoke(instance)).CallDeferred();
        return true;
    }

    public bool RemoveStructure(int x, int y, SimulationContext ctx = null)
    {
        StructureInstance removed = null;
        int ox = x, oy = y;
        lock (_lock)
        {
            // Сначала проверяем крышу, если снос направлен на крышу
            if (_roofs.TryGetValue((x, y), out removed))
            {
                _roofs.Remove((x, y));
            }
            else if (!_structures.TryGetValue((x, y), out removed))
            {
                // task-22: снос по ЛЮБОЙ клетке габарита сносит постройку целиком.
                if (FootprintRegistry.TryGetOrigin(x, y, out var origin)
                    && _structures.TryGetValue(origin, out removed))
                {
                    ox = origin.X;
                    oy = origin.Y;
                    _structures.Remove((ox, oy));
                }
                else if (_floors.TryGetValue((x, y), out removed))
                {
                    _floors.Remove((x, y));
                }
                else
                {
                    return false;
                }
            }
            else
            {
                _structures.Remove((ox, oy));
            }

            if (removed.Definition.IsWall)
            {
                NotifyWallChangedLocked(ox, oy);
            }
        }

        // task-22: освободить ВСЕ клетки габарита (вне _lock: у реестра свой).
        if (!removed.Definition.IsFloor && !removed.Definition.IsRoof)
        {
            FootprintRegistry.ReleaseOrigin(ox, oy);
        }
        x = ox;
        y = oy;

        if (removed.Definition.BlocksMovement && ctx != null)
        {
            for (int dy = 0; dy < removed.Definition.Height; dy++)
            {
                for (int dx = 0; dx < removed.Definition.Width; dx++)
                {
                    int cx = ox + dx;
                    int cy = oy + dy;
                    if ((uint)cx < (uint)ctx.MapWidth && (uint)cy < (uint)ctx.MapHeight)
                    {
                        ctx.SolidWalls[cx, cy] = false;
                    }
                }
            }
            FlowFieldManager.Instance?.ClearCache();
            HierarchicalPathfinder.Instance?.Invalidate();
        }

        var def = removed.Definition;
        // Возврат 60% затраченных материалов при сносе структуры
        if (def.RequiredMaterials != null && GroundItemManager.Instance != null)
        {
            foreach (var (matItem, matCount) in def.RequiredMaterials)
            {
                int refundCount = (int)Math.Ceiling(matCount * 0.6f);
                if (refundCount > 0)
                {
                    GroundItemManager.Instance.SpawnItems(ox, oy, matItem, refundCount);
                }
            }
        }
        // §119 P5: снос центрального блока = снос зоны, чьим якорем он был.
        if (def.Type == BuildingType.CentralBlock)
        {
            try
            {
                CentralBlockRemovedHook?.Invoke(x, y);
            }
            catch (Exception hookEx)
            {
                GD.PrintErr($"[StructureManager] CentralBlockRemovedHook failed at ({x},{y}): {hookEx.Message}");
            }
        }
        Callable.From(() => OnStructureRemoved?.Invoke((x, y), def)).CallDeferred();
        return true;
    }

    public HashSet<(int X, int Y)> GetAllWalls()
    {
        lock (_lock)
        {
            var walls = new HashSet<(int X, int Y)>();
            foreach (var kvp in _structures)
            {
                if (kvp.Value.Definition.IsWall)
                    walls.Add(kvp.Key);
            }
            return walls;
        }
    }

    public Dictionary<(int X, int Y), StructureInstance> GetAllStructures()
    {
        lock (_lock)
        {
            return new Dictionary<(int X, int Y), StructureInstance>(_structures);
        }
    }

    public void ClearAll(SimulationContext ctx = null)
    {
        List<StructureInstance> snapshot;
        lock (_lock)
        {
            snapshot = new List<StructureInstance>(_structures.Values);
            snapshot.AddRange(_floors.Values);
            snapshot.AddRange(_roofs.Values);
            _structures.Clear();
            _floors.Clear();
            _roofs.Clear();
            _pendingWallUpdates.Clear();
            FootprintRegistry.Clear();
        }

        foreach (var s in snapshot)
        {
            if (s.Definition.BlocksMovement && ctx != null)
            {
                if ((uint)s.X < (uint)ctx.MapWidth && (uint)s.Y < (uint)ctx.MapHeight)
                    ctx.SolidWalls[s.X, s.Y] = false;
            }
            var def = s.Definition;
            var pos = (s.X, s.Y);
            Callable.From(() => OnStructureRemoved?.Invoke(pos, def)).CallDeferred();
        }

        if (ctx != null)
        {
            FlowFieldManager.Instance?.ClearCache();
            HierarchicalPathfinder.Instance?.Invalidate();
        }
    }

    private void NotifyWallChangedLocked(int x, int y)
    {
        _pendingWallUpdates.Add((x, y));
        _pendingWallUpdates.Add((x, y - 1));
        _pendingWallUpdates.Add((x, y + 1));
        _pendingWallUpdates.Add((x - 1, y));
        _pendingWallUpdates.Add((x + 1, y));

        long now = System.Environment.TickCount64;
        if (now - _lastFlushTicks >= DebounceMs)
        {
            _lastFlushTicks = now;
            var changed = new List<(int X, int Y)>(_pendingWallUpdates);
            _pendingWallUpdates.Clear();
            Callable.From(() => OnWallTilesUpdated?.Invoke(changed)).CallDeferred();
        }
    }

    public void FlushPendingWallUpdates()
    {
        List<(int X, int Y)> changed = null;
        lock (_lock)
        {
            if (_pendingWallUpdates.Count == 0)
                return;
            _lastFlushTicks = System.Environment.TickCount64;
            changed = new List<(int X, int Y)>(_pendingWallUpdates);
            _pendingWallUpdates.Clear();
        }

        if (changed != null && changed.Count > 0)
        {
            Callable.From(() => OnWallTilesUpdated?.Invoke(changed)).CallDeferred();
        }
    }
}
