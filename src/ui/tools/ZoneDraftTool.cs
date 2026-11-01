using Godot;
using Game.Core;
using Game.Simulation;
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
    }

    public bool DoorMode => _doorMode;
    public string ZoneKind => _zoneKind;
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
        }
    }

    public void OnRelease(Vector2I startTile, Vector2I endTile, Vector2 worldPos, bool isLeftClick)
    {
        // §122: ПКМ в режиме двери раньше выходил ДО закрытия SelectionBox —
        // красная рамка оставалась висеть. Снимаем рамку/состояние всегда.
        if (_selectionBox != null && _selectionBox.IsSelecting)
        {
            _selectionBox.EndSelection();
            _selectionBox.ResetDefaultStyle();
        }

        if (_doorMode)
        {
            _isRightDragging = false;
            return;
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
            long rightClickArea = ((long)maxX - minX + 1) * ((long)maxY - minY + 1);
            if (rightClickArea > WorldPlacementPass.MaxScannedCells)
            {
                RepaintGhost();
                return;
            }

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

                    // Клетка зоны снимается целиком, только если вне рамки у неё не осталось
                    // тайлов-соседей той же зоны (иначе это снос куска живой зоны).
                    if (IsSoleZoneCell(x, y, minX, maxX, minY, maxY))
                    {
                        if (StockpileManager.Instance.IsZoneTile(x, y))
                            removedStockpile.Add((x, y));
                        if (FarmJobManager.Instance.IsGardenBed(x, y) || FarmJobManager.Instance.IsPlotMarked(x, y))
                            removedFarm.Add((x, y));
                        CollectZoneRingSites(x, y, minX, maxX, minY, maxY, cancelSites, ringSeen);
                    }

                    if (WorldPlacement != null && !WorldPlacementPass.IsOnIsland(x, y, WorldPlacement))
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
        long width = (long)maxX - minX + 1;
        long height = (long)maxY - minY + 1;
        if (width <= 0 || height <= 0) return;
        long clippedWidth = Math.Min(width, WorldPlacementPass.MaxScannedCells);
        long clippedHeight = Math.Min(height, Math.Max(1, WorldPlacementPass.MaxScannedCells / clippedWidth));
        int scanMinX = (int)Math.Max(minX, (long)minX + width / 2 - clippedWidth / 2);
        int scanMinY = (int)Math.Max(minY, (long)minY + height / 2 - clippedHeight / 2);
        int scanMaxX = (int)Math.Min(maxX, (long)scanMinX + clippedWidth - 1);
        int scanMaxY = (int)Math.Min(maxY, (long)scanMinY + clippedHeight - 1);
        for (int x = scanMinX; x <= scanMaxX; x++)
        {
            for (int y = scanMinY; y <= scanMaxY; y++)
            {
                if (WorldPlacementPass.IsOnIsland(x, y, WorldPlacement))
                {
                    if (CanCommitZoneCell(x, y))
                        _zoneCells.Add(new Vector2I(x, y));
                }
                else if (WorldPlacement != null && WorldPlacement.IsWorldCell(x, y))
                {
                    // Лимит, как у остальных инструментов: иначе один драг 500×500 положит
                    // сотни тысяч меток мира за один Commit (фриз + раздутая дельта).
                    if (_worldZoneCells.Count >= WorldPlacementPass.MaxAppliedCells)
                        return;
                    // Гейт тот же, что на Commit: в черновик (и в ghost) попадает только то,
                    // что реально встанет — вода/гора/сухая земля за кромкой не обещаются.
                    bool warehouse = _zoneKind == "warehouse";
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
    private bool WallsForbidden => _zoneKind == "farming" || _zoneKind == "farm";

    /// <summary>
    /// Зоне нужна настоящая почва: у фермы — да (растения), у склада — нет
    /// (склад на пляже/у причала осознанно разрешён, его зона не «грядка на песке»).
    /// </summary>
    private bool ZoneNeedsSoil => _zoneKind == "farming" || _zoneKind == "farm";

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
                    // Seam: a zone spanning the island edge has cells in BOTH sets. A neighbour that
                    // is a zone cell of the other half is not a ring cell (was: double ring on the seam,
                    // and after Commit the island wall stayed on top of the zone).
                    if (IsDraftZoneCell(p) || _wallCells.Contains(p) || _worldWallCells.Contains(p))
                        continue;
                    if (_removedWalls.Contains(p))
                        continue;
                    if (WorldPlacementPass.IsOnIsland(x, y, WorldPlacement))
                    {
                        if (!CanPlaceWall(x, y))
                            continue;
                        _wallCells.Add(p);
                        continue;
                    }
                    // Стык с миром: у зоны, упирающейся в кромку острова, кольцо раньше
                    // обрывалось — мировые соседи островных клеток пропускались.
                    if (WorldPlacement != null && WorldPlacement.IsWorldCell(x, y)
                        && WorldPlacement.CanPlaceWall(x, y))
                        _worldWallCells.Add(p);
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
                    // Seam: island zone cells of the same draft are not ring cells (see above).
                    if (IsDraftZoneCell(p) || _worldWallCells.Contains(p) || _wallCells.Contains(p))
                        continue;
                    if (_removedWalls.Contains(p))
                        continue;
                    // Стык с островом: мировая зона, упёршаяся в кромку, получает стену и на
                    // островной стороне — иначе кольцо обрывалось ровно по кромке.
                    if (WorldPlacementPass.IsOnIsland(p.X, p.Y, WorldPlacement))
                    {
                        if (CanPlaceWall(p.X, p.Y))
                            _wallCells.Add(p);
                        continue;
                    }
                    if (!WorldPlacement.IsWorldCell(p.X, p.Y))
                        continue;
                    if (!WorldPlacement.CanPlaceWall(p.X, p.Y))
                        continue; // ghost без гейта обещал стену на воде — Commit её пропускал
                    _worldWallCells.Add(p);
                }
        }
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
    private void PaintZoneGhostCell(int x, int y, int zoneSource)
    {
        if (!CanCommitZoneCell(x, y))
            return;
        var p = new Vector2I(x, y);
        _ghostLayer.SetCell(p, zoneSource, Vector2I.Zero);
        _painted.Add(p);
    }

    private void RepaintGhost()
    {
        if (_ghostLayer == null)
            return;
        ClearGhost();
        // Зону рисуем ghost-Source (дубликат TileSet с травой/грядкой).
        // SourceGrass/SourceGardenBed здесь НЕЛЬЗЯ: в ghost-наборе под этими
        // номерами сидят стены — призрак склада заливался стенами.
        int zoneSource = MapRenderer.GhostSourceForZone(_zoneKind);
        foreach (var c in _zoneCells)
        {
            _ghostLayer.SetCell(c, zoneSource, Vector2I.Zero);
            _painted.Add(c);
        }
        // §28: мировые клетки черновика тоже рисуем — иначе за кромкой «зона не ставится»:
        // она ставилась, но её не было видно.
        foreach (var c in _worldZoneCells)
        {
            _ghostLayer.SetCell(c, zoneSource, Vector2I.Zero);
            _painted.Add(c);
        }
        if (!WallsForbidden && !_dragging)
        {
            // One terrain call for both halves: two separate SetCellsTerrainConnect calls
            // left an unconnected gap exactly at the island edge.
            _ringBuffer.Clear();
            foreach (var w in _wallCells)
                _ringBuffer.Add(w);
            foreach (var w in _worldWallCells)
                _ringBuffer.Add(w);
            PaintWallGhost(_ringBuffer);
        }
        // Активный драг — превью поверх накопленного (в множества не пишем).
        // Accumulated walls are painted together with the drag ring in PaintDragRingPreview.
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
                // Островной части у драга нет — рисуем только мир (и кольцо стен мира).
                PaintWorldDragPreview(zoneSource);
                PaintDragRingPreview();
                return;
            }
            long cellArea = ((long)maxX - minX + 1) * ((long)maxY - minY + 1);
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
            // Мировая часть активного драга: только клетки, которые пройдут гейт мира.
            PaintWorldDragPreview(zoneSource);
            PaintDragRingPreview();
            return;
        }
    }

    /// <summary>
    /// Ring preview of the active drag around the FULL (unclipped) rectangle, island and world
    /// cells alike. Was: ring around the island-clipped rect only → no ghost walls beyond the edge,
    /// and a bogus ghost column right on the edge (inside the drag) = "double wall on the seam".
    /// Only the perimeter is walked (O(perimeter), not O(area)).
    /// </summary>
    private void PaintDragRingPreview()
    {
        if (WallsForbidden)
            return;
        int minX = Math.Min(_dragStart.X, _dragCurrent.X) - 1;
        int maxX = Math.Max(_dragStart.X, _dragCurrent.X) + 1;
        int minY = Math.Min(_dragStart.Y, _dragCurrent.Y) - 1;
        int maxY = Math.Max(_dragStart.Y, _dragCurrent.Y) + 1;
        _ringBuffer.Clear();
        for (int x = minX; x <= maxX; x++)
        {
            TryAddRingPreviewCell(x, minY);
            TryAddRingPreviewCell(x, maxY);
        }
        for (int y = minY + 1; y < maxY; y++)
        {
            TryAddRingPreviewCell(minX, y);
            TryAddRingPreviewCell(maxX, y);
        }
        // Accumulated ring + drag ring in ONE terrain call so the autotile connects them
        // (and connects island/world halves across the edge).
        foreach (var w in _wallCells)
            _ringBuffer.Add(w);
        foreach (var w in _worldWallCells)
            _ringBuffer.Add(w);
        PaintWallGhost(_ringBuffer);
    }

    private void TryAddRingPreviewCell(int x, int y)
    {
        var p = new Vector2I(x, y);
        if (_removedWalls.Contains(p) || IsDraftZoneCell(p))
            return;
        if (CanPlaceRingWall(x, y))
            _ringBuffer.Add(p);
    }

    /// <summary>One wall gate for both sides of the island edge (ghost == commit).</summary>
    private bool CanPlaceRingWall(int x, int y)
    {
        if (WorldPlacementPass.IsOnIsland(x, y, WorldPlacement))
            return CanPlaceWall(x, y);
        return WorldPlacement != null && WorldPlacement.IsWorldCell(x, y) && WorldPlacement.CanPlaceWall(x, y);
    }

    /// <summary>Превью мировых клеток активного драга — по тем же гейтам, что на Commit.</summary>
    private void PaintWorldDragPreview(int zoneSource)
    {
        if (WorldPlacement == null)
            return;
        bool warehouse = _zoneKind == "warehouse";
        int minX = Math.Min(_dragStart.X, _dragCurrent.X);
        int maxX = Math.Max(_dragStart.X, _dragCurrent.X);
        int minY = Math.Min(_dragStart.Y, _dragCurrent.Y);
        int maxY = Math.Max(_dragStart.Y, _dragCurrent.Y);
        long worldArea = ((long)maxX - minX + 1) * ((long)maxY - minY + 1);

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
        if (WorldPlacementPass.IsOnIsland(x, y, WorldPlacement))
            return;
        if (!WorldPlacement.IsWorldCell(x, y))
            return;
        if (!(warehouse ? WorldPlacement.CanPlaceStockpile(x, y) : WorldPlacement.CanPlaceFarm(x, y)))
            return;
        var p = new Vector2I(x, y);
        _ghostLayer.SetCell(p, zoneSource, Vector2I.Zero);
        _painted.Add(p);
    }

    /// <summary>
    /// Клетка островной зоны, которая реально встанет на Commit. Один гейт для ghost и Commit:
    /// раньше ghost показывал траву, а Commit молча отбрасывал клетку в здании/на грядке/
    /// на чертеже/в чужой зоне — «поставил», и ничего не появлялось.
    /// </summary>
    private bool CanCommitZoneCell(int x, int y)
        => CanPlaceZone(x, y)
            && !StructureManager.Instance.HasStructureAt(x, y)
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
        return true;
    }

    private bool CanPlaceWall(int x, int y)
    {
        if ((uint)x >= (uint)MapRenderer.MapWidth || (uint)y >= (uint)MapRenderer.MapHeight)
            return false;
        if (_mapData == null || (uint)x >= (uint)_mapData.Width || (uint)y >= (uint)_mapData.Height)
            return false;
        if (_mapData.Ground[x, y] != TileType.Grass)
            return false;
        // Камень НЕ исключаем: этап 1 пайплайна его добудет (Mining), стена встанет после.
        if (StructureManager.Instance.HasStructureAt(x, y))
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
    private void CollectZoneRingSites(int x, int y, int minX, int maxX, int minY, int maxY,
        List<(int X, int Y)> cancelSites, HashSet<Vector2I> seen)
    {
        for (int ox = -1; ox <= 1; ox++)
        {
            for (int oy = -1; oy <= 1; oy++)
            {
                if (ox == 0 && oy == 0)
                    continue;
                int nx = x + ox, ny = y + oy;
                if (!WorldPlacementPass.IsOnIsland(nx, ny, WorldPlacement))
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

    /// <summary>Cell belongs to the draft zone on either side of the island edge.</summary>
    private bool IsDraftZoneCell(Vector2I p) => _zoneCells.Contains(p) || _worldZoneCells.Contains(p);

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
            if (_zoneKind == "warehouse")
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
            List<(int X, int Y)> pendingWarehouseZone = _zoneKind == "warehouse" ? zoneList : null;
            ConstructionManager.Instance.StartSitesBatch(wallList, def, _mapData, pendingWarehouseZone);
        }

        // §28: мировая часть черновика — зона (метки посева/склада) и кольцо стен.
        if (_worldZoneCells.Count > 0 && WorldPlacement != null)
        {
            bool warehouse = _zoneKind == "warehouse";
            foreach (Vector2I c in _worldZoneCells)
            {
                if (warehouse)
                    WorldPlacement.TryPlaceStockpile(c.X, c.Y);
                else
                    WorldPlacement.TryPlaceFarmPlot(c.X, c.Y);
            }

            // Баг #7: клик по ферме за кромкой обязан открывать фермерское окно,
            // как на острове. Поэтому зона МАТЕРИАЛИЗУЕТСЯ в ZoneManager (словари
            // _tileToZoneId/_zones работают с любыми локальными координатами,
            // FindZoneAt не проверяет границы), а грядки помечаются в FarmJobManager:
            // иначе окно фермы покажет зону без грядок, и авто-посадка там мертва.
            // Маски деревьев/камней — островные массивы, для мира null (проверено:
            // MarkPlotsBatch такие клетки не отбрасывает, а мировые метки CropPlan
            // пишет зеркало IslandMutationMirror).
            if (!warehouse)
            {
                var worldZoneTiles = new List<(int X, int Y)>(_worldZoneCells.Count);
                foreach (Vector2I c in _worldZoneCells)
                    worldZoneTiles.Add((c.X, c.Y));
                FarmJobManager.Instance.MarkPlotsBatch(worldZoneTiles, null, null);
                FarmZoneManager.Instance.CreateZone(worldZoneTiles);
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
    /// Отмена окна по крестику. Черновик ДО Commit в мир ничего не ставит: _zoneCells/_wallCells —
    /// это только ghost-превью, поэтому сносить по ним существующие зоны и чертежи нельзя
    /// (крестик стирал чужую складскую зону/грядки, попавшие под драг). После Commit инструмент
    /// отбрасывается (HUDController.ConfirmDraft → _draftTool = null), так что сюда попадает
    /// только отмена ещё не подтверждённого превью.
    /// </summary>
    public void Discard() => Cancel();
}
