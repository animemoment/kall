using Godot;
using Game.Core;
using Game.Simulation;
using Game.UI;
using Game.UI.Tools;
using System;
using System.Collections.Generic;

namespace Game.UI.Tools;

/// <summary>
/// Черновик зоны со стенами (режим окна стройки).
/// Мульти-прямоугольники: каждый завершённый ЛКМ-драг ДОПИСЫВАЕТСЯ к черновику,
/// старое не сносится. Ghost перестраивается живьём во время драга.
/// ПКМ во время драга — отменить только текущий драг (накопленное не трогаем).
/// DoorMode: ЛКМ-клик по ghost-стене снимает её из черновика (будущий проём).
/// ПКМ-драг по уже поставленной зоне снимает её целиком: тайлы зоны (склад/ферма) и
/// кольцо стен вокруг (иначе повторная постановка упирается в «клетка занята» — баг #6).
/// Commit (CheckMark): зона по типу + стены чертежами WoodWall. Cancel: чистка ghost.
/// </summary>
public class ZoneDraftTool : ITool
{
    private readonly SelectionBox _selectionBox;
    private readonly MapData _mapData;
    private readonly TileMapLayer _ghostLayer;

    private readonly string _zoneKind;
    /// <summary>
    /// task-13: ОТРАСЛЬ черновика. Farm — грядки (легаси), Warehouse — склад
    /// (легаси StockpileManager), остальные — рабочая зона-отрасль (центр+радиус).
    /// </summary>
    private readonly ZoneKind _industryKind;
    /// <summary>true — черновик создаёт зону-ОТРАСЛЬ (а не грядку/склад).</summary>
    private readonly bool _isIndustryZone;

    private string _wallMaterialId = "wood";
    private bool _doorMode;

    private bool _dragging;
    private Vector2I _dragStart;
    private Vector2I _dragCurrent;

    // Накопленный черновик (все завершённые прямоугольники) + активный драг.
    // Активный драг считается живьём из _dragStart/_dragCurrent и НЕ пишется
    // в множества до Release — поэтому второй ЛКМ ничего старого не сносит.

    private readonly HashSet<Vector2I> _zoneCells = new(1024);
    private readonly HashSet<Vector2I> _wallCells = new(1024);
    // §28: клетки черновика за кромкой острова (мир) — зона и кольцо стен.
    private readonly HashSet<Vector2I> _worldZoneCells = new(1024);
    private readonly HashSet<Vector2I> _worldWallCells = new(1024);
    private readonly HashSet<Vector2I> _removedWalls = new(64);

    // Баг дублей: RebuildDraft чистил _zoneCells/_wallCells ДО ClearGhost, поэтому
    // стирались только новые клетки, а старые ghost-клетки оставались навсегда.
    // _painted — всё, что реально нарисовано в ghost-слое (зона+стены), стираем по нему.
    private readonly HashSet<Vector2I> _painted = new(2048);
    private readonly List<Vector2I> _ringBuffer = new(256);

    private static readonly Color ZoneFill = new(0.4f, 0.6f, 0.2f, 0.35f);
    private static readonly Color ZoneBorder = new(0.5f, 0.8f, 0.2f, 0.9f);

    public ZoneDraftTool(SelectionBox selectionBox, MapData mapData, TileMapLayer ghostLayer, string zoneKind)
    {
        _selectionBox = selectionBox;
        _mapData = mapData;
        _ghostLayer = ghostLayer;
        _zoneKind = zoneKind ?? "";
        _industryKind = ResolveZoneKind(_zoneKind, out _isIndustryZone);
    }

    /// <summary>
    /// task-13: черновик сразу с ОТРАСЛЬЮ (а не со строкой). HUD передаёт ZoneKind,
    /// полученный из каталога отраслей — строковый id выводится из профиля.
    /// </summary>
    public ZoneDraftTool(SelectionBox selectionBox, MapData mapData, TileMapLayer ghostLayer, ZoneKind kind)
    {
        _selectionBox = selectionBox;
        _mapData = mapData;
        _ghostLayer = ghostLayer;
        _industryKind = kind.NormalizeLegacy();
        _isIndustryZone = _industryKind != ZoneKind.Farm && _industryKind != ZoneKind.Warehouse;
        _zoneKind = _isIndustryZone
            ? ZoneIndustryRegistry.IdFor(_industryKind)
            : (_industryKind == ZoneKind.Farm ? "farming" : "warehouse");
    }

    /// <summary>
    /// Строковый вид черновика → отрасль. Легаси "farming"/"farm" = грядки,
    /// "warehouse" = склад (StockpileManager), id отрасли из реестра = отрасль,
    /// неизвестная строка — прежнее поведение (грядки).
    /// </summary>
    private static ZoneKind ResolveZoneKind(string zoneKind, out bool isIndustry)
    {
        isIndustry = false;
        if (string.IsNullOrEmpty(zoneKind))
            return ZoneKind.Farm;
        if (zoneKind == "farming" || zoneKind == "farm")
            return ZoneKind.Farm;
        if (zoneKind == "warehouse")
            return ZoneKind.Warehouse;
        if (ZoneIndustryRegistry.TryGet(zoneKind, out var profile))
        {
            if (profile.Kind == ZoneKind.Farm)
                return ZoneKind.Farm;
            if (profile.Kind == ZoneKind.Warehouse)
                return ZoneKind.Warehouse;
            isIndustry = true;
            return profile.Kind;
        }
        return ZoneKind.Farm;
    }

    /// <summary>Черновик грядки (легаси Farm-зона).</summary>
    private bool IsFarm => _industryKind == ZoneKind.Farm;

    /// <summary>Черновик склада (легаси StockpileManager-зона).</summary>
    private bool IsWarehouse => _industryKind == ZoneKind.Warehouse && !_isIndustryZone;

    /// <summary>Черновик зоны-ОТРАСЛИ: создаётся рабочая зона (центр+радиус).</summary>
    private bool IsIndustry => _isIndustryZone;

    public bool DoorMode => _doorMode;

    /// <summary>Строковый id вида зоны (легаси "farming"/"warehouse" или id отрасли).</summary>
    public string ZoneKindId => _zoneKind;

    /// <summary>task-13: отрасль черновика (Farm/склад/…/жильё).</summary>
    public ZoneKind Kind => _industryKind;

    /// <summary>Черновик создаёт зону-отрасль (центр+радиус), а не грядку/склад.</summary>
    public bool IsIndustryZone => _isIndustryZone;

    public string WallMaterialId => _wallMaterialId;
    public bool HasDraft => _zoneCells.Count > 0 || _wallCells.Count > 0
        || _worldZoneCells.Count > 0 || _worldWallCells.Count > 0;

    public void SetWallMaterial(string id) => _wallMaterialId = string.IsNullOrEmpty(id) ? "wood" : id;

    /// <summary>Мир за кромкой острова (§28): зона и кольцо стен ставятся метками мира.</summary>
    public IToolWorldPlacement WorldPlacement { get; set; }
    public void SetDoorMode(bool enabled) => _doorMode = enabled;

    public void OnHover(Vector2I tilePos, Vector2 worldPos) { }

    private static readonly Color CancelFill = new(0.9f, 0.2f, 0.2f, 0.25f);
    private static readonly Color CancelBorder = new(0.9f, 0.2f, 0.2f, 0.9f);
    private bool _isRightDragging;

    public void OnClick(Vector2I tilePos, Vector2 worldPos, bool isLeftClick)
    {
        // C-05: в режиме двери ПКМ не начинает драг удаления. Раньше ветка ПКМ стояла
        // ДО проверки _doorMode: она успевала выставить _isRightDragging и запустить
        // красную рамку, а OnDrag/OnRelease в дверном режиме выходили раньше
        // EndSelection() — красная рамка и _isRightDragging «залипали».
        if (_doorMode && !isLeftClick)
            return;

        if (!isLeftClick)
        {
            // ПКМ: удаление клеток из черновика или отмена чертежей
            _isRightDragging = true;
            _dragStart = tilePos;
            _dragCurrent = tilePos;
            if (_selectionBox != null)
            {
                _selectionBox.SetStyle(CancelFill, CancelBorder);
                _selectionBox.StartSelection(worldPos);
            }
            return;
        }

        // Режим двери: клик по ghost-стене снимает её из черновика.
        if (_doorMode)
        {
            var p = tilePos;
            if (_wallCells.Remove(p) || _worldWallCells.Remove(p))
            {
                _removedWalls.Add(p);
                _ghostLayer?.EraseCell(p);
                _painted.Remove(p);
            }
            return;
        }

        // Новый драг: старое НЕ чистим, дописываем по Release.
        _isRightDragging = false;
        _dragging = true;
        _dragStart = tilePos;
        _dragCurrent = tilePos;
        // Мусор от снесённой ПКМ зоны не должен глушить превью кольца нового драга
        // (сам RecalcWalls на Release тоже чистит, но превью рисуется раньше).
        PruneStaleRemovedWalls();
        if (_selectionBox != null)
        {
            _selectionBox.SetStyle(ZoneFill, ZoneBorder);
            _selectionBox.StartSelection(worldPos);
        }
        RepaintGhost();
    }

    public void OnDrag(Vector2I startTile, Vector2I currentTile, Vector2 currentWorldPos)
    {
        if (!_dragging && !_isRightDragging)
            return;
        if (_doorMode)
            return;
        if (_dragCurrent == currentTile)
            return;
        _dragCurrent = currentTile;
        _selectionBox?.UpdateSelection(currentWorldPos);
        if (_dragging)
        {
            RepaintGhost();
            if (IsWarehouse)
            {
                float centerX = (_dragStart.X + _dragCurrent.X + 1) * 0.5f * MapRenderer.TileSizePx;
                float centerY = (_dragStart.Y + _dragCurrent.Y + 1) * 0.5f * MapRenderer.TileSizePx;
                ServiceRadiusOverlayRenderer.Instance?.SetCustomRadius(
                    new Vector2(centerX, centerY),
                    64 * MapRenderer.TileSizePx,
                    new Color(0.75f, 0.45f, 0.95f, 0.85f));
            }
        }
    }

    public void OnRelease(Vector2I startTile, Vector2I endTile, Vector2 worldPos, bool isLeftClick)
    {
        if (_doorMode)
            return;

        if (_selectionBox != null && _selectionBox.IsSelecting)
        {
            _selectionBox.EndSelection();
            _selectionBox.ResetDefaultStyle();
        }

        if (isLeftClick)
        {
            if (!_dragging)
                return;
            _dragging = false;
            _dragCurrent = endTile;
            // Фиксируем прямоугольник: дописать клетки, пересчитать кольцо, перекрасить.
            AccumulateRect(_dragStart, _dragCurrent);
            RecalcWalls();
            RepaintGhost();
        }
        else
        {
            if (!_isRightDragging)
                return;
            _isRightDragging = false;
            _dragCurrent = endTile;

            int minX = Math.Min(startTile.X, endTile.X);
            int maxX = Math.Max(startTile.X, endTile.X);
            int minY = Math.Min(startTile.Y, endTile.Y);
            int maxY = Math.Max(startTile.Y, endTile.Y);

            var cancelSites = new List<(int X, int Y)>();
            // Баг #6 («снял зону со стенами → поставил снова → стены не строятся, остаются
            // призраки чертежей»): рамка ПКМ снимала только то, что лежит ВНУТРИ неё, а Commit
            // создаёт больше состояния. Кольцо стройки у зоны лежит на 1 клетку НАРУЖУ рамки,
            // а зона склада уходит в отложенные (_pendingZones) и привязана к группе стройки.
            // Остаток: IsZoneTile/IsSiteAt навсегда true → повторный черновик отбрасывается
            // гейтами CanCommitZoneCell/CanPlaceWall, сайты стен не создаются, а на карте
            // висят призраки чертежей неснятого кольца. Поэтому снимаем зону ЦЕЛИКОМ:
            // её тайлы + кольцо стен — но только когда рамка накрыла зону полностью.
            // Частичный снос мир не трогает: иначе у соседней зоны отвалится общая стена,
            // а pending-зона «воскреснет» после достройки стен (ActivatePendingZone).
            var removedStockpile = new List<(int X, int Y)>();
            var removedFarm = new List<(int X, int Y)>();
            var ringSeen = new HashSet<Vector2I>();

            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    var p = new Vector2I(x, y);
                    _zoneCells.Remove(p);
                    _worldZoneCells.Remove(p);
                    // Кольцо глушим только там, где реально был чертёж стены черновика:
                    // иначе рамка ПКМ забивала _removedWalls сотнями тысяч «пустых» клеток
                    // (их же потом стирал ClearGhost) и навсегда глушила кольцо новых зон.
                    if (_wallCells.Remove(p) || _worldWallCells.Remove(p))
                        _removedWalls.Add(p);

                    if (ConstructionManager.Instance.IsSiteAt(x, y))
                    {
                        cancelSites.Add((x, y));
                    }
                    else if (BlueprintManager.Instance.IsBlueprintAt(x, y))
                    {
                        BlueprintManager.Instance.RemoveBlueprint(x, y, out _);
                    }

                    // GD-19: в чертежном инструменте драг ПКМ отменяет ТОЛЬКО чертежи,
                    // не трогая готовые работающие зоны складов и грядок
                    CollectZoneRingSites(x, y, minX, maxX, minY, maxY, cancelSites, ringSeen);

                    if (WorldPlacement != null)
                    {
                        WorldPlacement.TryRemoveWall(x, y);
                        WorldPlacement.TryRemoveBuilding(x, y);
                        WorldPlacement.TryRemoveFarmPlot(x, y);
                        WorldPlacement.TryRemoveStockpile(x, y);
                        WorldPlacement.TryCancelCutTree(x, y);
                        WorldPlacement.TryCancelMineStone(x, y);
                    }
                }
            }

            // Порядок важен: сначала сама зона (активная/отложенная), потом кольцо стройки —
            // CancelSitesBatch обнуляет группу и роняет привязанную к ней pending-зону склада
            // (и снимает «воскрешение» через ActivatePendingZone после достройки стен).
            if (removedStockpile.Count > 0)
                StockpileManager.Instance.RemoveZoneTilesBatch(removedStockpile);
            if (removedFarm.Count > 0)
                FarmZoneManager.Instance.RemoveTiles(removedFarm);

            if (cancelSites.Count > 0)
            {
                ConstructionManager.Instance.CancelSitesBatch(cancelSites);
            }

            RecalcWalls();
            RepaintGhost();
        }
    }

    /// <summary>
    /// Прервать активное черчение, НЕ снося черновик: включили режим двери —
    /// текущий драг сбрасываем, накопленные прямоугольники и ghost остаются
    /// (пользователь вернётся в зона-режим и продолжит дописывать).
    /// </summary>
    public void StopDrawing()
    {
        if (!_dragging)
            return;
        _dragging = false;
        _selectionBox?.CancelSelection();
        _selectionBox?.ResetDefaultStyle();
        // RepaintGhost рисует накопленное уже без активного драга.
        RepaintGhost();
    }

    public void Cancel()
    {
        _dragging = false;
        ClearGhost();
        _zoneCells.Clear();
        _wallCells.Clear();
        _removedWalls.Clear();
        _worldZoneCells.Clear();
        _worldWallCells.Clear();
        _selectionBox?.CancelSelection();
        _selectionBox?.ResetDefaultStyle();
        if (IsWarehouse)
            ServiceRadiusOverlayRenderer.Instance?.ClearCustomRadius();
    }

    /// <summary>
    /// Дописать прямоугольник зоны в накопленный черновик (без стен).
    /// Непригодные клетки (вода/гора) отрезаются сразу — в черновик не попадают.
    /// </summary>
    private void AccumulateRect(Vector2I a, Vector2I b)
    {
        // §28: выделение больше не клампится к острову — клетки за кромкой копятся
        // отдельно и на Commit уходят метками мира (зона склада/фермы, кольцо стен).
        int minX = Math.Min(a.X, b.X);
        int maxX = Math.Max(a.X, b.X);
        int minY = Math.Min(a.Y, b.Y);
        int maxY = Math.Max(a.Y, b.Y);
        long scanned = 0;
        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                if (++scanned > WorldPlacementPass.MaxScannedCells)
                    return;
                if (WorldPlacementPass.IsIslandCell(x, y))
                {
                    if (CanCommitZoneCell(x, y))
                        _zoneCells.Add(new Vector2I(x, y));
                }
                else if (!IsIndustry && WorldPlacement != null && WorldPlacement.IsWorldCell(x, y))
                {
                    // Лимит, как у остальных инструментов: иначе один драг 500×500 положит
                    // сотни тысяч меток мира за один Commit (фриз + раздутая дельта).
                    if (_worldZoneCells.Count >= WorldPlacementPass.MaxAppliedCells)
                        return;
                    // Гейт тот же, что на Commit: в черновик (и в ghost) попадает только то,
                    // что реально встанет — вода/гора/сухая земля за кромкой не обещаются.
                    bool warehouse = IsWarehouse;
                    if (warehouse ? WorldPlacement.CanPlaceStockpile(x, y) : WorldPlacement.CanPlaceFarm(x, y))
                        _worldZoneCells.Add(new Vector2I(x, y));
                }
            }
        }
    }

    /// <summary>
    /// Пересчитать кольцо стен по ВСЕМ клеткам зоны: замкнутый контур с углами —
    /// каждая клетка зоны даёт своих 8 соседей (с дедупом), клетки зоны исключены,
    /// снятые дверью и непригодные — исключены.
    /// </summary>
    /// <summary>Фермерству стены не положены: кольцо не генерируем вообще.</summary>
    private bool WallsForbidden => IsFarm;

    /// <summary>
    /// Зоне нужна настоящая почва: у фермы — да (растения), у склада и у отрасли —
    /// нет (цех/склад на пляже осознанно разрешён, это не «грядка на песке»).
    /// </summary>
    private bool ZoneNeedsSoil => IsFarm;

    private void RecalcWalls()
    {
        _wallCells.Clear();
        _worldWallCells.Clear();
        // Чистим _removedWalls от записей снесённой зоны ДО построения кольца —
        // иначе они глушат кольцо новой зоны в тех же клетках (баг #6).
        PruneStaleRemovedWalls();
        if (WallsForbidden)
            return;
        foreach (var c in _zoneCells)
        {
            for (int ox = -1; ox <= 1; ox++)
                for (int oy = -1; oy <= 1; oy++)
                {
                    if (ox == 0 && oy == 0)
                        continue;
                    int x = c.X + ox, y = c.Y + oy;
                    var p = new Vector2I(x, y);
                    if (_zoneCells.Contains(p) || _worldZoneCells.Contains(p)
                        || _wallCells.Contains(p) || _worldWallCells.Contains(p))
                        continue;
                    if (_removedWalls.Contains(p))
                        continue;
                    // W-05: принадлежность кольца решает ОДИН предикат «остров + мир»,
                    // а не ветка «остров, иначе чужое». Раньше мировой сосед островной
                    // клетки либо пропускался как «чужой» (IsWorldCell = «чанк загружен»),
                    // либо попадал в черновик без гейта мира и тихо терялся на Commit —
                    // из-за этого авто-кольцо за кромкой 1024 оставалось пустым.
                    if (!IsRingWallable(x, y))
                        continue;
                    AddRingWallCell(p);
                }
        }

        // Кольцо стен вокруг мировых клеток зоны (§28) — клетки за кромкой острова.
        if (_worldZoneCells.Count == 0 || WorldPlacement == null)
            return;
        foreach (Vector2I c in _worldZoneCells)
        {
            for (int ox = -1; ox <= 1; ox++)
                for (int oy = -1; oy <= 1; oy++)
                {
                    if (ox == 0 && oy == 0)
                        continue;
                    var p = new Vector2I(c.X + ox, c.Y + oy);
                    if (_zoneCells.Contains(p) || _worldZoneCells.Contains(p)
                        || _wallCells.Contains(p) || _worldWallCells.Contains(p))
                        continue;
                    if (_removedWalls.Contains(p))
                        continue;
                    // W-05: тот же предикат, что и для кольца островной зоны. Островная
                    // сторона мировой зоны получает стену через ConstructionManager,
                    // мировая — чертёж мира; «чужой» клеток больше нет, есть два владельца.
                    if (!IsRingWallable(p.X, p.Y))
                        continue; // ghost без гейта обещал стену на воде — Commit её пропускал
                    AddRingWallCell(p);
                }
        }
    }

    /// <summary>
    /// W-05: ЕДИНЫЙ предикат принадлежности кольца стен — «остров ИЛИ мир».
    /// Границу задаёт <see cref="WorldPlacementPass.IsIslandCell"/> (1024 =
    /// <c>WorldModel.IslandTiles</c>/<c>MapRenderer.MapWidth</c>), а гейт берётся у того
    /// владельца, который эту клетку реально поставит: островную клетку строит
    /// ConstructionManager (<see cref="CanPlaceWall"/>), мировую — адаптер мира.
    /// Раньше ветки расходились и кольцо за кромкой 1024 либо обрывалось, либо
    /// обещало стены, которые Commit молча не ставил.
    /// </summary>
    private bool IsRingWallable(int x, int y)
    {
        if (WorldPlacementPass.IsIslandCell(x, y))
            return CanPlaceWall(x, y);
        return WorldPlacement != null && WorldPlacement.CanPlaceWall(x, y);
    }

    /// <summary>W-05: положить клетку кольца в ту половину черновика, которая её ставит.</summary>
    private void AddRingWallCell(Vector2I p)
    {
        if (WorldPlacementPass.IsIslandCell(p.X, p.Y))
            _wallCells.Add(p);
        else
            _worldWallCells.Add(p);
    }

    /// <summary>
    /// Перерисовать ghost: накопленное + активный драг (если идёт) живьём.
    /// Множества НЕ трогаем — только рисуем.
    /// </summary>
    // Ghost-превью стен: движковый автотайлинг (как в BuildTool) —
    // SetCellsTerrainConnect сам считает состыковку, код не нужен.
    private const int GhostTerrainSetId = 0;
    private const int GhostWallTerrainId = 0;

    private void PaintWallGhost(IEnumerable<Vector2I> cells)
    {
        if (_ghostLayer == null || _ghostLayer.TileSet == null) return;
        var arr = new Godot.Collections.Array<Vector2I>();
        foreach (var c in cells)
            arr.Add(c);
        if (arr.Count == 0) return;
        _ghostLayer.SetCellsTerrainConnect(arr, GhostTerrainSetId, GhostWallTerrainId, true);
        foreach (var c in cells)
            _painted.Add(c);
    }

    /// <summary>
    /// Клетка превью активного драга: рисуем ровно то, что примет AccumulateRect на Release
    /// (тот же гейт CanCommitZoneCell). Иначе ghost обещает воду/гору/визуальный песок/занятые
    /// клетки, а Commit их молча отбрасывает — «зона обещала встать, но не встала».
    /// </summary>
    /// <summary>
    /// W-04: единственная точка рисования клетки зоны. И остров, и мир обязаны звать её,
    /// чтобы «зона внутри» и «зона снаружи» 1024 не разошлись по source/тонировке.
    /// </summary>
    private void PaintZoneCell(Vector2I p, int zoneSource)
    {
        _ghostLayer.SetCell(p, zoneSource, Vector2I.Zero);
        _painted.Add(p);
    }

    private void PaintZoneGhostCell(int x, int y, int zoneSource)
    {
        if (!CanCommitZoneCell(x, y))
            return;
        PaintZoneCell(new Vector2I(x, y), zoneSource);
    }

    private void RepaintGhost()
    {
        if (_ghostLayer == null)
            return;
        ClearGhost();
        // Зону рисуем ghost-Source (дубликат TileSet с травой/грядкой).
        // SourceGrass/SourceGardenBed здесь НЕЛЬЗЯ: в ghost-наборе под этими
        // номерами сидят стены — призрак склада заливался стенами.
        // task-13: отрасль-зона визуально = рабочая зона (как склад), не грядка.
        string ghostKind = IsWarehouse || IsIndustry ? "warehouse" : "farm";
        int zoneSource = MapRenderer.GhostSourceForZone(ghostKind);
        // W-04: остров и мир рисуют зону ОДНИМ визуальным контрактом — один source,
        // нулевая тонировка (Overlay, без рамки). Раньше это были две независимые
        // ветки, и зона за кромкой 1024 могла получить чужой alpha/tint.
        // Инвариант: подтверждённая зона = ToolVisualSpec.ForDone(Stockpile) на обеих
        // сторонах; ПЛАН (рамка/полупрозрачность) — только недоделанное состояние.
        foreach (var c in _zoneCells)
            PaintZoneCell(c, zoneSource);
        // §28: мировые клетки черновика тоже рисуем — иначе за кромкой «зона не ставится»:
        // она ставилась, но её не было видно.
        foreach (var c in _worldZoneCells)
            PaintZoneCell(c, zoneSource);
        if (!WallsForbidden)
        {
            PaintWallGhost(_wallCells);
            PaintWallGhost(_worldWallCells);
        }
        // Активный драг — превью поверх накопленного (в множества не пишем).
        if (_dragging)
        {
            // ПЕРЕСЕЧЕНИЕ с островом, а не кламп: драг целиком за кромкой рисовал фантомную
            // клетку (0,0) и кольцо стен, которых в черновике нет.
            int minX = Mathf.Max(Mathf.Min(_dragStart.X, _dragCurrent.X), 0);
            int maxX = Mathf.Min(Mathf.Max(_dragStart.X, _dragCurrent.X), MapRenderer.MapWidth - 1);
            int minY = Mathf.Max(Mathf.Min(_dragStart.Y, _dragCurrent.Y), 0);
            int maxY = Mathf.Min(Mathf.Max(_dragStart.Y, _dragCurrent.Y), MapRenderer.MapHeight - 1);
            if (minX > maxX || minY > maxY)
            {
                // Островной части у драга нет — рисуем только мир.
                PaintWorldDragPreview(zoneSource);
                PaintWorldRingPreview();
                return;
            }
            long cellArea = (long)(maxX - minX + 1) * (maxY - minY + 1);
            if (cellArea > 2500)
            {
                // Для больших прямоугольников (более 50x50 клеток):
                // Заливка цветом зоны уже идеально и без лагов рисуется на GPU через _selectionBox.
                // В TileMapLayer рисуем только контур (рамку), чтобы не вешать CPU сотнями тысяч SetCell каждый кадр движения мыши.
                for (int x = minX; x <= maxX; x++)
                {
                    PaintZoneGhostCell(x, minY, zoneSource);
                    PaintZoneGhostCell(x, maxY, zoneSource);
                }
                for (int y = minY + 1; y < maxY; y++)
                {
                    PaintZoneGhostCell(minX, y, zoneSource);
                    PaintZoneGhostCell(maxX, y, zoneSource);
                }
            }
            else
            {
                for (int x = minX; x <= maxX; x++)
                    for (int y = minY; y <= maxY; y++)
                        PaintZoneGhostCell(x, y, zoneSource);
            }
            // Кольцо превью: соседи прямоугольника драга (для фермы — нет кольца).
            if (!WallsForbidden)
            {
                _ringBuffer.Clear();
                for (int x = minX - 1; x <= maxX + 1; x++)
                    for (int y = minY - 1; y <= maxY + 1; y++)
                    {
                        if (x < 0 || y < 0 || x >= MapRenderer.MapWidth || y >= MapRenderer.MapHeight)
                            continue;
                        bool inside = x >= minX && x <= maxX && y >= minY && y <= maxY;
                        if (inside)
                            continue;
                        var p = new Vector2I(x, y);
                        if (_removedWalls.Contains(p) || !CanPlaceWall(x, y))
                            continue;
                        _ringBuffer.Add(p);
                    }
                PaintWallGhost(_ringBuffer);
            }
            // Мировая часть активного драга: только клетки, которые пройдут гейт мира.
            PaintWorldDragPreview(zoneSource);
            // W-05: кольцо через кромку — тем же предикатом, что на Release/Commit.
            PaintWorldRingPreview();
        }
    }

    /// <summary>Превью мировых клеток активного драга — по тем же гейтам, что на Commit.</summary>
    private void PaintWorldDragPreview(int zoneSource)
    {
        // Зона-отрасль — диск по острову; мир за кромкой она не размечает.
        if (WorldPlacement == null || IsIndustry)
            return;
        bool warehouse = IsWarehouse;
        int minX = Math.Min(_dragStart.X, _dragCurrent.X);
        int maxX = Math.Max(_dragStart.X, _dragCurrent.X);
        int minY = Math.Min(_dragStart.Y, _dragCurrent.Y);
        int maxY = Math.Max(_dragStart.Y, _dragCurrent.Y);
        long worldArea = (long)(maxX - minX + 1) * (maxY - minY + 1);

        if (worldArea > 2500)
        {
            // Для огромных зон в мире рисуем периметр, заливку берет _selectionBox
            for (int x = minX; x <= maxX; x++)
            {
                PaintWorldCell(x, minY, zoneSource, warehouse);
                PaintWorldCell(x, maxY, zoneSource, warehouse);
            }
            for (int y = minY + 1; y < maxY; y++)
            {
                PaintWorldCell(minX, y, zoneSource, warehouse);
                PaintWorldCell(maxX, y, zoneSource, warehouse);
            }
            return;
        }

        long scanned = 0;
        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                if (++scanned > WorldPlacementPass.MaxScannedCells)
                    return;
                PaintWorldCell(x, y, zoneSource, warehouse);
            }
        }
    }

    private void PaintWorldCell(int x, int y, int zoneSource, bool warehouse)
    {
        if (WorldPlacementPass.IsIslandCell(x, y))
            return;
        if (!WorldPlacement.IsWorldCell(x, y))
            return;
        if (!(warehouse ? WorldPlacement.CanPlaceStockpile(x, y) : WorldPlacement.CanPlaceFarm(x, y)))
            return;
        var p = new Vector2I(x, y);
        PaintZoneCell(p, zoneSource);
    }

    /// <summary>
    /// W-05: кольцо активного драга ЧЕРЕЗ кромку 1024. Превью строилось в островных
    /// границах и у края обрывалось, поэтому казалось, что «вне активной области
    /// строительство недоступно». Тот же предикат, что у RecalcWalls/Commit.
    /// </summary>
    private void PaintWorldRingPreview()
    {
        if (WorldPlacement == null || WallsForbidden)
            return;
        int minX = Math.Min(_dragStart.X, _dragCurrent.X);
        int maxX = Math.Max(_dragStart.X, _dragCurrent.X);
        int minY = Math.Min(_dragStart.Y, _dragCurrent.Y);
        int maxY = Math.Max(_dragStart.Y, _dragCurrent.Y);
        // Драг целиком внутри острова — островное кольцо уже нарисовано выше.
        if (minX > 0 && minY > 0 && maxX < MapRenderer.MapWidth - 1 && maxY < MapRenderer.MapHeight - 1)
            return;
        _ringBuffer.Clear();
        long scanned = 0;
        for (int x = minX - 1; x <= maxX + 1; x++)
        {
            for (int y = minY - 1; y <= maxY + 1; y++)
            {
                if (++scanned > WorldPlacementPass.MaxScannedCells)
                {
                    PaintWallGhost(_ringBuffer);
                    return;
                }
                if (x >= minX && x <= maxX && y >= minY && y <= maxY)
                    continue;
                var p = new Vector2I(x, y);
                if (_removedWalls.Contains(p) || _ringBuffer.Contains(p))
                    continue;
                if (!IsRingWallable(x, y))
                    continue;
                _ringBuffer.Add(p);
            }
        }
        PaintWallGhost(_ringBuffer);
    }

    /// <summary>
    /// Клетка островной зоны, которая реально встанет на Commit. Один гейт для ghost и Commit:
    /// раньше ghost показывал траву, а Commit молча отбрасывал клетку в здании/на грядке/
    /// на чертеже/в чужой зоне — «поставил», и ничего не появлялось.
    /// </summary>
    private bool CanCommitZoneCell(int x, int y)
        => CanPlaceZone(x, y)
            && !StructureManager.Instance.IsOccupiedForBuildAt(x, y)
            && !ConstructionManager.Instance.IsSiteAt(x, y)
            && !BuildingManager.Instance.HasBuildingAt(x, y)
            && !FarmJobManager.Instance.IsGardenBed(x, y)
            && !FarmJobManager.Instance.IsPlotMarked(x, y)
            && !BlueprintManager.Instance.IsBlueprintAt(x, y)
            && !StockpileManager.Instance.IsZoneTile(x, y);

    /// <summary>Клетка пригодна под зону: только визуальная трава (вода/гора/песок-пустыня запрещены).</summary>
    private bool CanPlaceZone(int x, int y)
    {
        if (_mapData == null)
            return x >= 0 && y >= 0 && x < MapRenderer.MapWidth && y < MapRenderer.MapHeight;
        if ((uint)x >= (uint)_mapData.Width || (uint)y >= (uint)_mapData.Height)
            return false;
        if (_mapData.Ground[x, y] != TileType.Grass)
            return false;
        // Баг #4 («чертежи грядок на рандом»): пустыня/пляж имеют Ground == Grass
        // (UnifiedWorldGenerator кладёт траву всем не-вода/не-гора), но рендер красит их
        // песком (MapRenderer: Biome == Desert → Sand). Гейт по биому, а не по Ground —
        // иначе грядка встаёт на визуальный песок. Тот же гейт, что был в FarmingTool.
        if (ZoneNeedsSoil && _mapData.GetBiome(x, y) == Game.Core.WorldStreaming.BiomeType.Desert)
            return false;
        // task-21: зону/склад/грядку на жилу не ставим (месторождение теряется).
        if (_mapData.HasOre(x, y))
            return false;
        return true;
    }

    private bool CanPlaceWall(int x, int y)
    {
        if (_mapData != null && _mapData.Ground[x, y] != TileType.Grass)
            return false;
        // Камень НЕ исключаем: этап 1 пайплайна его добудет (Mining), стена встанет после.
        // task-22: любая клетка габарита станка/сайта занята — стену не ставим.
        if (StructureManager.Instance.IsOccupiedForBuildAt(x, y))
            return false;
        if (ConstructionManager.Instance.IsSiteAt(x, y))
            return false;
        var walls = MapRenderer.Instance?.WallBuildManager;
        if (walls != null && walls.IsWallAt(x, y))
            return false;
        if (BuildingManager.Instance.HasBuildingAt(x, y))
            return false;
        if (BlueprintManager.Instance.IsBlueprintAt(x, y))
            return false;
        // Рукотворные клетки не сносим: грядка под стеной — стена не ставится.
        if (FarmJobManager.Instance.IsGardenBed(x, y) || FarmJobManager.Instance.IsPlotMarked(x, y))
            return false;
        // task-21: кольцо стен по жиле не обещаем (жила сохраняется, добыча возможна).
        if (_mapData != null && _mapData.HasOre(x, y))
            return false;
        return true;
    }

    /// <summary>
    /// Клетка снимается ПКМ как «своя» (зона уходит целиком), а не как кусок живой зоны:
    /// вне рамки сноса [minX..maxX, minY..maxY] у неё не осталось тайлов-соседей зоны.
    /// Так частичный снос не рвёт соседнюю зону и не «воскрешает» pending-зону склада.
    /// </summary>
    private static bool IsSoleZoneCell(int x, int y, int minX, int maxX, int minY, int maxY)
    {
        for (int ox = -1; ox <= 1; ox++)
        {
            for (int oy = -1; oy <= 1; oy++)
            {
                int nx = x + ox, ny = y + oy;
                if (nx >= minX && nx <= maxX && ny >= minY && ny <= maxY)
                    continue; // сосед тоже внутри рамки — он тоже снимается
                if (StockpileManager.Instance.IsZoneTile(nx, ny))
                    return false;
                if (FarmJobManager.Instance.IsGardenBed(nx, ny) || FarmJobManager.Instance.IsPlotMarked(nx, ny))
                    return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Кольцо стен сносимой островной зоны: 8 соседей её клетки, где стоит площадка-стена
    /// и рядом (вне рамки) нет тайлов живой зоны. Без этого кольцо/призраки чертежей
    /// остаются после сноса зоны ПКМ, а повторная постановка видит «клетка занята».
    /// </summary>
    private static void CollectZoneRingSites(int x, int y, int minX, int maxX, int minY, int maxY,
        List<(int X, int Y)> cancelSites, HashSet<Vector2I> seen)
    {
        for (int ox = -1; ox <= 1; ox++)
        {
            for (int oy = -1; oy <= 1; oy++)
            {
                if (ox == 0 && oy == 0)
                    continue;
                int nx = x + ox, ny = y + oy;
                if (!WorldPlacementPass.IsIslandCell(nx, ny))
                    continue;
                if (!ConstructionManager.Instance.IsWallSiteAt(nx, ny))
                    continue;
                if (!IsSoleZoneCell(nx, ny, minX, maxX, minY, maxY))
                    continue; // стена принадлежит живой соседней зоне — не трогаем
                if (seen.Add(new Vector2I(nx, ny)))
                    cancelSites.Add((nx, ny));
            }
        }
    }

    /// <summary>
    /// Баг #6, вторая половина: клетки, снятые ПКМ, лежат в _removedWalls до Cancel/Commit,
    /// поэтому новый черновик в тех же клетках НАВСЕГДА терял кольцо стен («стены не строятся»),
    /// а закрытие/переоткрытие окна (новый ZoneDraftTool) «лечило». Держим в _removedWalls
    /// только то, что реально является кольцом ТЕКУЩЕГО черновика: у снесённой зоны кольца
    /// больше нет — записи уходят, проёмы двери (они всегда соседи клеток зоны) остаются.
    /// </summary>
    private void PruneStaleRemovedWalls()
    {
        if (_removedWalls.Count == 0)
            return;
        _removedWalls.RemoveWhere(p => !IsDraftRingCell(p));
    }

    private bool IsDraftRingCell(Vector2I p)
    {
        for (int ox = -1; ox <= 1; ox++)
        {
            for (int oy = -1; oy <= 1; oy++)
            {
                if (ox == 0 && oy == 0)
                    continue;
                var n = new Vector2I(p.X + ox, p.Y + oy);
                if (_zoneCells.Contains(n) || _worldZoneCells.Contains(n))
                    return true;
            }
        }
        return false;
    }

    private void ClearGhost()
    {
        if (_ghostLayer == null)
            return;
        foreach (var p in _painted)
            _ghostLayer.EraseCell(p);
        _painted.Clear();
        foreach (var r in _removedWalls)
            _ghostLayer.EraseCell(r);
        if (IsWarehouse)
            ServiceRadiusOverlayRenderer.Instance?.ClearCustomRadius();
    }

    /// <summary>
    /// Подтверждение (CheckMark): зона ставится СРАЗУ (её не строят), стены идут
    /// в трёхэтапный пайплайн: 1) расчистка → 2) чертежи+доставка → 3) стройка.
    /// Запреты: зона не ставится внутри зданий/на рукотворных клетках; в пределах
    /// одной стройки запрещены раздельные зоны (второй коммит того же типа поверх
    /// стройки — только через новый черновик, пересечения отрезаются здесь).
    /// </summary>
    public bool Commit()
    {
        if (!HasDraft)
            return false;
        var zoneList = new List<(int X, int Y)>(_zoneCells.Count);
        foreach (var c in _zoneCells)
        {
            // Общий гейт с ghost (CanCommitZoneCell): терраин мог измениться после драга,
            // плюс зона внутри здания / на грядке / на чертеже / на чужой зоне — не ставим.
            if (!CanCommitZoneCell(c.X, c.Y))
                continue;
            zoneList.Add((c.X, c.Y));
        }
        var wallList = new List<(int X, int Y)>(_wallCells.Count);
        foreach (var w in _wallCells)
            wallList.Add((w.X, w.Y));

        bool hasWalls = wallList.Count > 0 && !WallsForbidden;
        if (zoneList.Count > 0)
        {
            if (IsIndustry)
            {
                // task-13: зона = отрасль. Рабочая зона-бригадир создаётся как
                // ЦЕНТР+РАДИУС из рамки черновика (грядки/склад тут не при чём).
                CommitIndustryZone(zoneList);
            }
            else if (IsWarehouse)
            {
                if (!hasWalls)
                {
                    // Открытый склад без стен активируется сразу
                    StockpileManager.Instance.AddZoneTilesBatch(zoneList);
                }
                else
                {
                    // Склад со стенами: зона сразу отображается игроку (фиолетовый рендер), но активируется только после завершения стен
                    StockpileManager.Instance.AddPendingZoneTilesBatch(zoneList);
                }
            }
            else
            {
                FarmJobManager.Instance.MarkPlotsBatch(zoneList, _mapData?.TreeOnGrass, _mapData?.StoneOnGrass);
                FarmZoneManager.Instance.CreateZone(zoneList);
            }
        }

        if (hasWalls)
        {
            var mat = WallMaterialRegistry.ById(_wallMaterialId);
            var def = StructureRegistry.Get(mat.Type);
            List<(int X, int Y)> pendingWarehouseZone = IsWarehouse ? zoneList : null;
            ConstructionManager.Instance.StartSitesBatch(wallList, def, _mapData, pendingWarehouseZone);

            // Автоматическая установка пола и крыши для всего здания (пол по выбранному материалу, крыша по типу здания)
            if (zoneList.Count > 0)
            {
                var floorDef = StructureRegistry.Get(WallMaterialRegistry.GetFloorForMaterial(_wallMaterialId));
                var roofDef = StructureRegistry.Get(WallMaterialRegistry.GetRoofForZone(_industryKind));
                ConstructionManager.Instance.StartSitesBatch(zoneList, floorDef, _mapData);
                ConstructionManager.Instance.StartSitesBatch(zoneList, roofDef, _mapData);
            }
        }

        // §28: мировая часть черновика — зона (метки посева/склада) и кольцо стен.
        if (_worldZoneCells.Count > 0 && WorldPlacement != null)
        {
            bool warehouse = IsWarehouse;
            foreach (Vector2I c in _worldZoneCells)
            {
                if (warehouse)
                    WorldPlacement.TryPlaceStockpile(c.X, c.Y);
                else
                    WorldPlacement.TryPlaceFarmPlot(c.X, c.Y);
            }
        }
        if (_worldWallCells.Count > 0 && !WallsForbidden && WorldPlacement != null)
        {
            foreach (Vector2I w in _worldWallCells)
                WorldPlacement.TryPlaceWall(w.X, w.Y);
        }

        ClearGhost();
        _zoneCells.Clear();
        _wallCells.Clear();
        _removedWalls.Clear();
        _worldZoneCells.Clear();
        _worldWallCells.Clear();
        _dragging = false;
        _selectionBox?.CancelSelection();
        _selectionBox?.ResetDefaultStyle();
        return true;
    }

    /// <summary>
    /// task-13: создать зону ОТРАСЛИ из рамки черновика. Модель §119 — рабочая
    /// зона-бригадир = центр + радиус: берём габарит рамки, центр = середина,
    /// радиус = половина большей стороны (кламп 64..512 у менеджера). Маска работ
    /// и заявка на рабочих — из профиля отрасли (ZoneIndustryProfile).
    /// Грядки/склад здесь НЕ создаются: у отрасли своя сущность.
    /// </summary>
    private void CommitIndustryZone(List<(int X, int Y)> zoneList)
    {
        int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
        foreach (var (x, y) in zoneList)
        {
            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
        }
        if (minX > maxX || minY > maxY)
            return;

        var profile = ZoneIndustryRegistry.Get(_industryKind);
        if (ZoneIndustryRegistry.PrimaryJobMask(profile.Kind) == 0)
        {
            GD.PushWarning($"[ZoneDraft] отрасль «{profile.Title}» не создаёт рабочих зон (нет основной работы).");
            return;
        }

        int cx = (minX + maxX) / 2;
        int cy = (minY + maxY) / 2;
        int radius = Math.Max((maxX - minX) / 2 + 1, (maxY - minY) / 2 + 1);

        if (WorkZoneManager.Instance.CreateZoneAt(profile.Kind, cx, cy, radius,
                profile.DefaultWorkers, out var zone, out var failReason))
        {
            GD.Print($"[ZoneDraft] создана зона отрасли «{zone?.Name}» ({profile.Title}, r={zone?.RadiusTiles}).");
        }
        else
        {
            GD.PushWarning($"[ZoneDraft] зона отрасли «{profile.Title}» не создана: {failReason}");
        }
    }

    /// <summary>
    /// Отмена окна по крестику. Черновик ДО Commit в мир ничего не ставит: _zoneCells/_wallCells —
    /// это только ghost-превью, поэтому сносить по ним существующие зоны и чертежи нельзя
    /// (крестик стирал чужую складскую зону/грядки, попавшие под драг). После Commit инструмент
    /// отбрасывается (HUDController.ConfirmDraft → _draftTool = null), так что сюда попадает
    /// только отмена ещё не подтверждённого превью.
    /// </summary>
    public void Discard() => Cancel();
}
