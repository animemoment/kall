using Godot;
using Game.Core;
using Game.Simulation;
using Game.UI;
using System;
using System.Collections.Generic;

namespace Game.UI.Tools;

public class BuildTool : ITool
{
    private readonly WallBuildManager _wallBuildManager;
    private readonly TileMapLayer _ghostLayer;
    private readonly MapData _mapData;
    private readonly BuildingType _buildingType;
    private readonly int _sourceId;
    private bool _isDragging;
    // C-11: кнопка, которой НАЧАТ драг — единственный источник истины для ветки OnRelease.
    // Раньше поле писалось и не читалось: решение принимал аргумент релиза, поэтому ПКМ,
    // отпущенная посреди ЛКМ-драга, уводила операцию в снос (см. C-04), а ghost всё это
    // время рисовал «положительное» превью будущей постройки.
    private bool _isLeftClick = true;
    private readonly HashSet<Vector2I> _previewTiles = new(128);
    private readonly List<(int X, int Y)> _cellBuffer = new(2048);
    // C-07: переиспользуемые буферы ghost-превью. Раньше на каждое движение мыши
    // создавались Vector2I[] (hover) и Godot.Collections.Array (маршалинг) —
    // теперь оба живут в полях и только очищаются/наполняются заново.
    private readonly Godot.Collections.Array<Vector2I> _ghostArray = new();
    private readonly List<Vector2I> _ghostCells = new(1024);
    // task-21: красный призрак НЕВАЛИДНОЙ клетки (руда) — слой тонируется, пока
    // показываем запрет; ClearGhost всегда возвращает белый Modulate.
    private bool _ghostTintedRed;
    // task-22: габарит постройки (1×1…3×4) — призрак и валидация прямоугольником.
    private readonly int _footprintW;
    private readonly int _footprintH;
    private readonly bool _isMultiTile;

    /// <summary>Мир за кромкой острова (§26 шаг 2, §28): клетки вне острова ставятся метками мира.</summary>
    public IToolWorldPlacement WorldPlacement { get; set; }

    public BuildTool(
        WallBuildManager wallBuildManager,
        TileMapLayer ghostLayer,
        MapData mapData,
        BuildingType buildingType = BuildingType.WoodWall,
        int sourceId = MapRenderer.SourceWall)
    {
        _wallBuildManager = wallBuildManager;
        _ghostLayer = ghostLayer;
        _mapData = mapData;
        _buildingType = buildingType;
        _sourceId = sourceId;
        var footprint = BuildingFootprint.Of(buildingType);
        _footprintW = Math.Max(1, footprint.Width);
        _footprintH = Math.Max(1, footprint.Height);
        _isMultiTile = _footprintW > 1 || _footprintH > 1;
    }

    // Ghost-превью стен: движковый автотайлинг (TerrainSet 0 / terrain 0
    // из сцены wood_wall_tile). SetCellsTerrainConnect сам пересчитывает
    // соседей — WallTileHelper здесь не нужен.
    private const int GhostTerrainSetId = 0;
    private const int GhostWallTerrainId = 0;

    private void PaintGhostPreview(List<Vector2I> cells)
    {
        if (_ghostLayer == null || _ghostLayer.TileSet == null) return;
        // C-07: наполняем один и тот же Godot-массив вместо new на каждый вызов.
        _ghostArray.Clear();
        foreach (var c in cells)
            _ghostArray.Add(c);
        if (_ghostArray.Count == 0) return;
        _ghostLayer.SetCellsTerrainConnect(_ghostArray, GhostTerrainSetId, GhostWallTerrainId, true);
        foreach (var c in cells)
            _previewTiles.Add(c);
    }

    public void OnHover(Vector2I tilePos, Vector2 worldPos)
    {
        if (_isDragging || _ghostLayer == null) return;
        ClearGhost();

        if (_buildingType == BuildingType.CentralBlock)
        {
            var centerPx = new Vector2(
                (tilePos.X + _footprintW * 0.5f) * MapRenderer.TileSizePx,
                (tilePos.Y + _footprintH * 0.5f) * MapRenderer.TileSizePx);
            ServiceRadiusOverlayRenderer.Instance?.SetCustomRadius(
                centerPx,
                64 * MapRenderer.TileSizePx,
                new Color(0.95f, 0.75f, 0.25f, 0.85f));
        }

        if (_buildingType == BuildingType.WoodWall)
        {
            if (CanPlaceBlueprintAt(tilePos.X, tilePos.Y))
            {
                // C-07: одиночная клетка — через тот же переиспользуемый буфер,
                // без new[] и boxing на каждое событие движения мыши.
                _ghostCells.Clear();
                _ghostCells.Add(tilePos);
                PaintGhostPreview(_ghostCells);
            }
            else
            {
                PaintOreBlockedGhost(tilePos);
            }
        }
        else if (CanPlaceBlueprintAt(tilePos.X, tilePos.Y))
        {
            // Станок/мебель — одиночный ghost-Source, но тайл станка мультиклеточный
            // (W×H сразу), поэтому один SetCell рисует весь прямоугольник (task-16/22).
            int ghostSource = MapRenderer.GhostSourceForBuilding(_buildingType);
            if (ghostSource < 0) return;
            _ghostLayer.SetCell(tilePos, ghostSource, Vector2I.Zero);
            _previewTiles.Add(tilePos);
        }
        else
        {
            // task-22: невалиден хоть один угол габарита → КРАСНЫЙ призрак W×H.
            if (!PaintInvalidFootprintGhost(tilePos))
                PaintOreBlockedGhost(tilePos);
        }
    }

    public void OnClick(Vector2I tilePos, Vector2 worldPos, bool isLeftClick)
    {
        if (!IsValidCoord(tilePos.X, tilePos.Y) && WorldPlacement == null) return;
        _isDragging = true;
        _isLeftClick = isLeftClick;
        _lastDragTile = new(-1, -1);
    }

    private Vector2I _lastDragTile = new(-1, -1);

    public void OnDrag(Vector2I startTile, Vector2I currentTile, Vector2 currentWorldPos)
    {
        if (!_isDragging || _ghostLayer == null) return;
        if (_lastDragTile == currentTile) return;
        _lastDragTile = currentTile;
        ClearGhost();

        if (_buildingType == BuildingType.CentralBlock)
        {
            var centerPx = new Vector2(
                (currentTile.X + _footprintW * 0.5f) * MapRenderer.TileSizePx,
                (currentTile.Y + _footprintH * 0.5f) * MapRenderer.TileSizePx);
            ServiceRadiusOverlayRenderer.Instance?.SetCustomRadius(
                centerPx,
                64 * MapRenderer.TileSizePx,
                new Color(0.95f, 0.75f, 0.25f, 0.85f));
        }

        int minX = Mathf.Min(startTile.X, currentTile.X);
        int maxX = Mathf.Max(startTile.X, currentTile.X);
        int minY = Mathf.Min(startTile.Y, currentTile.Y);
        int maxY = Mathf.Max(startTile.Y, currentTile.Y);

        long cellCount = (long)(maxX - minX + 1) * (maxY - minY + 1);
        if (cellCount > 100_000)
        {
            HUDController.ShowFloatingWarning("Выделенная область слишком велика (> 100 000 клеток)");
            return;
        }

        if (_buildingType == BuildingType.WoodWall)
        {
            // C-07: список клеток переиспользуется между шагами драга — new List
        // на каждое движение мыши больше не нужен (EnsureCapacity растит буфер
        // максимум до 10k и дальше память не перевыделяется).
        _ghostCells.Clear();
        _ghostCells.EnsureCapacity((int)Math.Min(cellCount, 10_000));
        for (int x = minX; x <= maxX; x++)
            for (int y = minY; y <= maxY; y++)
                if (CanPlaceBlueprintAt(x, y))
                    _ghostCells.Add(new Vector2I(x, y));
        PaintGhostPreview(_ghostCells);
        return;
        }

        // task-16: мультиклеточный станок драгом не «мажем» (иначе на каждую
        // клетку рамки встал бы отдельный станок) — призрак живёт под курсором.
        if (BuildingFootprint.IsMultiTile(_buildingType))
        {
            if (!CanPlaceBlueprintAt(currentTile.X, currentTile.Y))
                return;
            int ghostSourceMulti = MapRenderer.GhostSourceForBuilding(_buildingType);
            if (ghostSourceMulti < 0)
                return;
            _ghostLayer.SetCell(currentTile, ghostSourceMulti, Vector2I.Zero);
            _previewTiles.Add(currentTile);
            return;
        }

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                if (CanPlaceBlueprintAt(x, y))
                {
                    Vector2I pos = new Vector2I(x, y);
                    int ghostSource = MapRenderer.GhostSourceForBuilding(_buildingType);
                    if (ghostSource < 0) continue;
                    _ghostLayer.SetCell(pos, ghostSource, Vector2I.Zero);
                    _previewTiles.Add(pos);
                }
            }
        }
    }

    public void OnRelease(Vector2I startTile, Vector2I endTile, Vector2 worldPos, bool isLeftClick)
    {
        // C-11: ветку «строить/сносить» выбирает кнопка, сохранённая в OnClick, — а не
        // аргумент релиза. Аргумент оставлен ради контракта ITool (PIM после C-04 передаёт
        // кнопку-инициатор, т.е. то же значение), но источник истины здесь — поле.
        bool build = _isLeftClick;
        _isDragging = false;
        _lastDragTile = new(-1, -1);
        ClearGhost();

        int minX = Mathf.Min(startTile.X, endTile.X);
        int maxX = Mathf.Max(startTile.X, endTile.X);
        int minY = Mathf.Min(startTile.Y, endTile.Y);
        int maxY = Mathf.Max(startTile.Y, endTile.Y);

        long cellCount = (long)(maxX - minX + 1) * (maxY - minY + 1);
        if (cellCount > 100_000)
        {
            HUDController.ShowFloatingWarning("Выделенная область слишком велика (> 100 000 клеток)");
            return;
        }

        _cellBuffer.Clear();
        if (build && BuildingFootprint.IsMultiTile(_buildingType))
        {
            // task-16 / U2-06: мультиклеточный станок ставится ОДНИМ объектом от клетки релиза (под курсором),
            // чтобы призрак и постройка не расходились при драге.
            // UI-16: при отмене (ПКМ) в буфер попадает вся выделенная рамка!
            _cellBuffer.Add((endTile.X, endTile.Y));
        }
        else
        {
            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    _cellBuffer.Add((x, y));
                }
            }
        }

        if (build)
        {
            var validIslandCells = new List<(int X, int Y)>(_cellBuffer.Count);
            foreach (var (x, y) in _cellBuffer)
            {
                if (CanPlaceBlueprintAt(x, y))
                {
                    if (IsValidCoord(x, y))
                    {
                        validIslandCells.Add((x, y));
                    }
                    else if (WorldPlacement != null)
                    {
                        if (_buildingType == BuildingType.WoodWall)
                            WorldPlacement.TryPlaceWall(x, y);
                        else
                            WorldPlacement.TryPlaceBuilding(x, y);
                    }
                }
            }

            if (validIslandCells.Count > 0)
            {
                var def = StructureRegistry.Get(_buildingType);
                ConstructionManager.Instance.StartSitesBatch(validIslandCells, def, _mapData);

                if (validIslandCells.Count < _cellBuffer.Count)
                {
                    int skipped = _cellBuffer.Count - validIslandCells.Count;
                    HUDController.ShowFloatingWarning($"Часть построек ({skipped}) пропущена: препятствия/вода/руда", null, new Color(1f, 0.75f, 0.3f, 1f));
                }
            }
            else if (_cellBuffer.Count > 0)
            {
                string reason = GetPlacementBlockedReason(_cellBuffer[0].X, _cellBuffer[0].Y);
                HUDController.ShowFloatingWarning(reason);
            }
        }
        else
        {
            var islandCells = new List<(int X, int Y)>();
            foreach (var (x, y) in _cellBuffer)
            {
                if (IsValidCoord(x, y))
                    islandCells.Add((x, y));
                else if (WorldPlacement != null)
                {
                    if (_buildingType == BuildingType.WoodWall)
                        WorldPlacement.TryRemoveWall(x, y);
                    else
                        WorldPlacement.TryRemoveBuilding(x, y);
                }
            }
            if (islandCells.Count > 0)
            {
                // UI-16 / Master Fix Plan #13: Рамка отменяет только чертежи и постройки того типа здания,
                // который выбран сейчас в руках. Любая клетка габарита мультитайла находит origin станка.
                var matchingSiteOrigins = new List<(int X, int Y)>();
                var seenSites = new HashSet<(int X, int Y)>();
                var matchingStructures = new List<(int X, int Y)>();
                var seenStructures = new HashSet<(int X, int Y)>();

                for (int i = 0; i < islandCells.Count; i++)
                {
                    int ix = islandCells[i].X;
                    int iy = islandCells[i].Y;

                    if (ConstructionManager.Instance.TryGetSiteOrigin(ix, iy, out int ox, out int oy))
                    {
                        if (seenSites.Add((ox, oy)))
                        {
                            if (ConstructionManager.Instance.TryGetSite(ox, oy, out var site) && site.Definition?.Type == _buildingType)
                            {
                                matchingSiteOrigins.Add((ox, oy));
                            }
                        }
                    }

                    if (StructureManager.Instance.TryGetStructure(ix, iy, out var str))
                    {
                        if (str.Definition?.Type == _buildingType)
                        {
                            int sox = str.X;
                            int soy = str.Y;
                            if (seenStructures.Add((sox, soy)))
                            {
                                matchingStructures.Add((sox, soy));
                            }
                        }
                    }
                }

                if (matchingSiteOrigins.Count > 0)
                {
                    ConstructionManager.Instance.CancelSitesBatch(matchingSiteOrigins);
                    BlueprintManager.Instance.RemoveBlueprintsBatch(matchingSiteOrigins);
                }

                for (int i = 0; i < matchingStructures.Count; i++)
                {
                    StructureManager.Instance.RemoveStructure(matchingStructures[i].X, matchingStructures[i].Y);
                }
            }
        }
    }

    public void Cancel()
    {
        _isDragging = false;
        _lastDragTile = new(-1, -1);
        ClearGhost();
        if (_buildingType == BuildingType.CentralBlock)
            ServiceRadiusOverlayRenderer.Instance?.ClearCustomRadius();
    }

    /// <summary>
    /// task-21 / AUDIT task-17 (P4-1): клетка занята рудой (остров — MapData; мир —
    /// «известный» набор поля, который переживает эвикцию кэша; неизвестное поле
    /// не блокирует постройку).
    /// </summary>
    private bool IsOreBlockedCell(int x, int y)
    {
        if (IsValidCoord(x, y))
            return _mapData != null && _mapData.HasOre(x, y);

        ulong seed = Game.Core.World.WorldModel.Seed;
        if (seed == 0)
            return false;
        return Game.Core.OreWorldField.TryPeekKnownOreLocal(x, y, out bool ore) && ore;
    }

    /// <summary>
    /// task-22: красный призрак ВСЕГО прямоугольника W×H, когда невалидна хотя бы
    /// одна клетка (стена рядом/грядка/вода/руда…). 1×1 не рисуем — там своя ветка.
    /// </summary>
    private bool PaintInvalidFootprintGhost(Vector2I pos)
    {
        if (!_isMultiTile || _ghostLayer == null || _ghostLayer.TileSet == null)
            return false;
        int ghostSource = MapRenderer.GhostSourceForBuilding(_buildingType);
        if (ghostSource < 0)
            return false;
        SetGhostTintRed(true);
        _ghostLayer.SetCell(pos, ghostSource, Vector2I.Zero);
        _previewTiles.Add(pos);
        return true;
    }

    /// <summary>Красный призрак на клетке с рудой: «сюда нельзя, тут жила».</summary>
    private void PaintOreBlockedGhost(Vector2I pos)
    {
        if (_ghostLayer == null || _ghostLayer.TileSet == null) return;
        if (!IsOreBlockedCell(pos.X, pos.Y)) return;

        SetGhostTintRed(true);
        if (_buildingType == BuildingType.WoodWall)
        {
            _ghostCells.Clear();
            _ghostCells.Add(pos);
            PaintGhostPreview(_ghostCells);
        }
        else
        {
            int ghostSource = MapRenderer.GhostSourceForBuilding(_buildingType);
            if (ghostSource < 0) return;
            _ghostLayer.SetCell(pos, ghostSource, Vector2I.Zero);
            _previewTiles.Add(pos);
        }
    }

    private void SetGhostTintRed(bool red)
    {
        if (_ghostLayer == null || _ghostTintedRed == red) return;
        _ghostTintedRed = red;
        _ghostLayer.Modulate = red ? new Color(1f, 0.3f, 0.3f, 1f) : new Color(1f, 1f, 1f, 1f);
    }

    /// <summary>
    /// task-22: валидация ВСЕГО прямоугольника W×H (станок 3×4 = 12 клеток).
    /// 1×1 — прежняя одноклеточная проверка. Станок не должен пересекать кромку
    /// острова/мира (часть клеток суша, часть мир — запрещено).
    /// </summary>
    private bool CanPlaceBlueprintAt(int x, int y)
    {
        if (!_isMultiTile)
            return CanPlaceCellAt(x, y);

        bool anyIsland = false;
        bool anyWorld = false;
        for (int dy = 0; dy < _footprintH; dy++)
        {
            for (int dx = 0; dx < _footprintW; dx++)
            {
                int cx = x + dx;
                int cy = y + dy;
                if (IsValidCoord(cx, cy))
                    anyIsland = true;
                else
                    anyWorld = true;
                if (!CanPlaceCellAt(cx, cy))
                    return false;
            }
        }
        if (anyIsland && anyWorld)
            return false; // габарит пополам через кромку — не ставим
        return true;
    }

    /// <summary>Одноклеточный гейт размещения (используется и для каждой клетки габарита).</summary>
    private bool CanPlaceCellAt(int x, int y)
    {
        if (IsValidCoord(x, y))
        {
            if (_mapData != null && _mapData.Ground[x, y] != TileType.Grass)
                return false;

            // На камень не строим: сначала добудь россыпь инструментом камня.
            if (_mapData != null && _mapData.HasStone(x, y))
                return false;

            // task-21: на руде не строим — жила перекрывается и теряется.
            if (_mapData != null && _mapData.HasOre(x, y))
                return false;

            bool isFloorType = _buildingType is BuildingType.WoodFloor or BuildingType.StoneFloor or BuildingType.ConcreteFloor;
            bool isRoofType = _buildingType is BuildingType.RoofResidential or BuildingType.RoofIndustrial;

            if (isFloorType)
            {
                if (StructureManager.Instance.TryGetFloor(x, y, out var currentFloor) && currentFloor.Definition.Type == _buildingType)
                    return false;
                if (ConstructionManager.Instance.TryGetSite(x, y, out var floorSite) && floorSite.Definition.IsFloor)
                    return false;
                return true;
            }

            if (isRoofType)
            {
                if (StructureManager.Instance.HasRoofAt(x, y))
                    return false;
                if (ConstructionManager.Instance.TryGetSite(x, y, out var roofSite) && roofSite.Definition.IsRoof)
                    return false;
                return true;
            }

            // task-22: занята может быть не только origin — любая клетка габарита.
            if (StructureManager.Instance.IsOccupiedForBuildAt(x, y))
                return false;
            // Для мультитайла дерево внутри прямоугольника недопустимо (станок
            // не должен прорастать сквозь дерево); 1×1 ведёт себя как раньше.
            if (_isMultiTile && _mapData != null && _mapData.TreeOnGrass[x, y])
                return false;

            if (ConstructionManager.Instance.IsSiteAt(x, y))
                return false;

            if (_wallBuildManager != null && _wallBuildManager.IsWallAt(x, y))
                return false;

            if (BuildingManager.Instance.HasBuildingAt(x, y))
                return false;

            if (BlueprintManager.Instance.IsBlueprintAt(x, y))
                return false;

            // Фермерство стен не терпит: запрет на саму грядку/метку и на кольцо вокруг.
            // Касается только стен (WoodWall), мебель можно ставить где угодно.
            if (_buildingType == BuildingType.WoodWall && IsFarmTileOrAdjacent(x, y))
                return false;

            return true;
        }

        // Вне острова колонии: проверяем через WorldPlacement
        if (WorldPlacement != null)
        {
            return _buildingType == BuildingType.WoodWall
                ? WorldPlacement.CanPlaceWall(x, y)
                : WorldPlacement.CanPlaceBuilding(x, y);
        }

        return false;
    }

    /// <summary>
    /// UI-15: Определяет понятную игроку причину, почему размещение постройки по координатам (x, y) невозможно.
    /// </summary>
    private string GetPlacementBlockedReason(int x, int y)
    {
        for (int dy = 0; dy < _footprintH; dy++)
        {
            for (int dx = 0; dx < _footprintW; dx++)
            {
                int cx = x + dx;
                int cy = y + dy;

                if (IsValidCoord(cx, cy))
                {
                    if (_mapData != null)
                    {
                        var ground = _mapData.Ground[cx, cy];
                        if (ground == TileType.Water)
                            return "Нельзя строить: вода";
                        if (ground == TileType.Mountain)
                            return "Нельзя строить: скала / горы";
                        if (ground != TileType.Grass)
                            return "Нельзя строить: неподходящий грунт";
                        if (_mapData.HasOre(cx, cy))
                            return "Нельзя строить: месторождение руды";
                        if (_mapData.HasStone(cx, cy))
                            return "Нельзя строить: препятствие (камень)";
                        if (_isMultiTile && _mapData.TreeOnGrass[cx, cy])
                            return "Нельзя строить: препятствие (дерево)";
                    }

                    if (StructureManager.Instance.IsOccupiedForBuildAt(cx, cy)
                        || BuildingManager.Instance.HasBuildingAt(cx, cy)
                        || ConstructionManager.Instance.IsSiteAt(cx, cy)
                        || (_wallBuildManager != null && _wallBuildManager.IsWallAt(cx, cy))
                        || BlueprintManager.Instance.IsBlueprintAt(cx, cy))
                    {
                        return "Нельзя строить: место уже занято постройкой";
                    }

                    if (_buildingType == BuildingType.WoodWall && IsFarmTileOrAdjacent(cx, cy))
                    {
                        return "Нельзя строить: зона фермы (стена заблокирует грядки)";
                    }
                }
                else if (WorldPlacement != null)
                {
                    bool can = _buildingType == BuildingType.WoodWall
                        ? WorldPlacement.CanPlaceWall(cx, cy)
                        : WorldPlacement.CanPlaceBuilding(cx, cy);
                    if (!can)
                        return "Нельзя строить: препятствие или граница мира";
                }
                else
                {
                    return "Нельзя строить: за пределами карты";
                }
            }
        }

        return "Нельзя построить: место занято или непроходимо";
    }

    /// <summary>
    /// True если клетка — грядка/метка фермы или соседняя (8 соседей) с такой.
    /// Соседство проверяем по FarmJobManager (грядки+метки) и ZoneManager (Farm-зоны).
    /// </summary>
    private static bool IsFarmTileOrAdjacent(int x, int y)
    {
        for (int oy = -1; oy <= 1; oy++)
        {
            for (int ox = -1; ox <= 1; ox++)
            {
                int nx = x + ox, ny = y + oy;
                if (!IsValidCoord(nx, ny)) continue;
                if (FarmJobManager.Instance.IsGardenBed(nx, ny)
                    || FarmJobManager.Instance.IsPlotMarked(nx, ny))
                    return true;
                if (ZoneManager.Instance.TryGetZoneAt(nx, ny, out var zone)
                    && zone.Kind == ZoneKind.Farm)
                    return true;
            }
        }
        return false;
    }

    private static bool IsValidCoord(int x, int y) =>
        x >= 0 && y >= 0 && x < MapRenderer.MapWidth && y < MapRenderer.MapHeight;

    // --- Публичные вспомогательные для тестов (повешенные методы 21-02) ---
    public struct GhostBand
    {
        public int MinX, MaxX, MinY, MaxY;
        public long CellCount => (long)(MaxX - MinX + 1) * (MaxY - MinY + 1);
    }

    public static long SumBandCells(List<GhostBand> bands)
    {
        long sum = 0;
        for (int i = 0; i < bands.Count; i++) sum += bands[i].CellCount;
        return sum;
    }

    public static void ComputeRectDifference(int oMinX, int oMinY, int oMaxX, int oMaxY,
        int nMinX, int nMinY, int nMaxX, int nMaxY,
        List<GhostBand> added, List<GhostBand> removed)
    {
        (added ??= new()).Clear();
        (removed ??= new()).Clear();
        ComputeRectDifferenceBatch(oMinX, oMinY, oMaxX, oMaxY,
            nMinX, nMinY, nMaxX, nMaxY, added, removed);
    }

    private static void ComputeRectDifferenceBatch(int oMinX, int oMinY, int oMaxX, int oMaxY,
        int nMinX, int nMinY, int nMaxX, int nMaxY,
        List<GhostBand> added, List<GhostBand> removed)
    {
        long addedBudget = (long)(nMaxX - nMinX + 1) * (nMaxY - nMinY + 1);
        if (addedBudget > MaxScannedCells)
        {
            // Отсечка ДО разности рамок/гейтов/отрисовки: см. 21-05.
            return;
        }

        if (oMaxX < nMinX)
        {
            removed.Add(new GhostBand { MinX = oMinX, MaxX = oMaxX, MinY = oMinY, MaxY = oMaxY });
        }
        else if (oMinX > nMaxX)
        {
            added.Add(new GhostBand { MinX = oMinX, MaxX = oMaxX, MinY = oMinY, MaxY = oMaxY });
        }
        else
        {
            if (oMinX < nMinX)
                removed.Add(new GhostBand { MinX = oMinX, MaxX = nMinX - 1, MinY = oMinY, MaxY = oMaxY });
            if (oMaxX > nMaxX)
                added.Add(new GhostBand { MinX = nMaxX + 1, MaxX = oMaxX, MinY = oMinY, MaxY = oMaxY });
            if (oMinY < nMinY)
                removed.Add(new GhostBand { MinX = Math.Max(oMinX, nMinX), MaxX = Math.Min(oMaxX, nMaxX), MinY = oMinY, MaxY = nMinY - 1 });
            if (oMaxY > nMaxY)
                added.Add(new GhostBand { MinX = Math.Max(oMinX, nMinX), MaxX = Math.Min(oMaxX, nMaxX), MinY = nMaxY + 1, MaxY = oMaxY });
        }
    }

    private const long MaxScannedCells = long.MaxValue;

    private void ClearGhost()
    {
        if (_ghostLayer == null) return;
        foreach (var pos in _previewTiles)
            _ghostLayer.EraseCell(pos);
        _previewTiles.Clear();
        SetGhostTintRed(false);
        if (_buildingType == BuildingType.CentralBlock)
            ServiceRadiusOverlayRenderer.Instance?.ClearCustomRadius();
    }
}
