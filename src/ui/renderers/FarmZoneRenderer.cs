using Godot;
using Game.Simulation;
using System.Collections.Generic;

namespace Game.UI;

public partial class FarmZoneRenderer : Node2D
{
    private const float TileSize = 64f;
    private const float DefaultBorderThickness = 2.0f;
    private const float HighlightBorderThickness = 3.2f;

    private static readonly Color BaseBorderColor = new(1f, 1f, 1f, 0.40f);
    private static readonly Color HoverBorderColor = new(1f, 1f, 1f, 0.95f);
    private static readonly Color SelectedBorderColor = new(0.9f, 1.0f, 0.4f, 0.95f);

    private static readonly Color HoverFillColor = new(1f, 1f, 1f, 0.12f);
    private static readonly Color SelectedFillColor = new(0.9f, 1.0f, 0.4f, 0.18f);

    // AUDIT task-17 (N-1): «рабочие» (оранжевые) цвета удалены — Work-зон в этой
    // модели не существует, а радиусные отрасли рисует IndustryZoneRenderer.

    private const float RedrawThrottleSec = 0.1f;
    private float _redrawTimer = 1f;
    private bool _redrawPending;

    // R-06: O(N) геометрия (прямоугольники заливки + контур) считается один раз на
    // набор клеток зоны и кэшируется. Раньше _Draw рисовал прямоугольник НА КАЖДЫЙ
    // тайл и 4 DrawLine на каждую граничную клетку — для фермы 500×500 это 250k
    // примитивов на redraw (10 раз/с, пока зона выбрана/под курсором).
    private readonly FarmZoneGeometryCache _geometryCache = new();

    public override void _Ready()
    {
        ZIndex = 8; // Поверх тайлов и грядок, под агентами
        ZoneManager.Instance.OnZonesUpdated += OnZonesUpdatedThrottled;
    }

    public override void _Process(double delta)
    {
        if (!_redrawPending) return;
        _redrawTimer += (float)delta;
        if (_redrawTimer < RedrawThrottleSec) return;
        _redrawTimer = 0f;
        _redrawPending = false;
        QueueRedraw();
    }

    private void OnZonesUpdatedThrottled()
    {
        _redrawPending = true;
    }

    public override void _Draw()
    {
        // R-06: клетки здесь НЕ обходятся — только чтение готовой геометрии из кэша.
        //
        // AUDIT task-17 (N-1): рисуем ТОЛЬКО тайловые Farm-зоны. Радиусные
        // зоны-отрасли (§119: центр + радиус, клеток нет) отрисовывает
        // IndustryZoneRenderer; у них geometry пустая, поэтому старые «рабочие»
        // ветки цветов были недостижимы (Kind == ZoneKind.Work больше не
        // создаётся — Zone нормализует легаси-значение в отрасль).
        var zones = ZoneManager.Instance.GetAllZones();
        _geometryCache.Prune(zones);

        var hovered = ZoneManager.Instance.HoveredZone;
        var selected = ZoneManager.Instance.SelectedZone;

        for (int z = 0; z < zones.Count; z++)
        {
            var zone = zones[z];
            if (!zone.IsFarmZone)
                continue;
            bool isSelected = selected != null && selected.Id == zone.Id;
            bool isHovered = hovered != null && hovered.Id == zone.Id;

            var geometry = _geometryCache.Get(zone.Id, zone.Tiles);

            Color fillColor = isSelected ? SelectedFillColor : HoverFillColor;

            Color borderColor;
            if (isSelected)
                borderColor = SelectedBorderColor;
            else if (isHovered)
                borderColor = HoverBorderColor;
            else
                borderColor = BaseBorderColor;
            float thickness = (isSelected || isHovered) ? HighlightBorderThickness : DefaultBorderThickness;

            if (isSelected || isHovered)
            {
                var fills = geometry.FillRects;
                for (int i = 0; i < fills.Count; i++)
                    DrawRect(fills[i], fillColor);
            }

            var borders = geometry.BorderSegments;
            for (int i = 0; i < borders.Count; i++)
                DrawLine(borders[i].From, borders[i].To, borderColor, thickness);
        }
    }

    /// <summary>
    /// R-06: сборка геометрии зоны для кэша — единственная точка, где клетки обходятся.
    /// Вызывается из FarmZoneGeometryCache.Get при (пере)построении, а не из _Draw.
    /// </summary>
    public static ZoneGeometry BuildZoneGeometry(HashSet<(int X, int Y)> tiles)
    {
        var FillRects = new List<Rect2>();
        var BorderSegments = new List<(Vector2 From, Vector2 To)>();
        BuildFillRects(tiles, FillRects);
        BuildBorderSegments(tiles, BorderSegments);
        return new ZoneGeometry(FillRects, BorderSegments);
    }

    /// <summary>
    /// R-06: заливка набора клеток склейкой прогонов — сначала горизонтальные прогоны в
    /// ряду, затем вертикальная склейка одинаковых прогонов в прямоугольники.
    /// Прямоугольник 200×200 → 1 Rect2 вместо 40 000 DrawRect; L-форма → 2 прямоугольника.
    /// Вызывается только из <see cref="ZoneGeometry"/> при (пере)построении кэша.
    /// </summary>
    public static void BuildFillRects(HashSet<(int X, int Y)> tiles, List<Rect2> rects)
    {
        rects.Clear();
        if (tiles == null || tiles.Count == 0) return;

        var rows = new SortedDictionary<int, List<int>>();
        foreach (var (x, y) in tiles)
        {
            if (!rows.TryGetValue(y, out var xs))
            {
                xs = new List<int>();
                rows[y] = xs;
            }
            xs.Add(x);
        }

        // Открытые прямоугольники предыдущего ряда: (startX, endX) → индекс в rects.
        var open = new Dictionary<(int Start, int End), int>();
        var built = new Dictionary<(int Start, int End), int>();
        var startY = new List<int>();

        foreach (var row in rows)
        {
            int y = row.Key;
            var xs = row.Value;
            xs.Sort();
            built.Clear();

            int i = 0;
            while (i < xs.Count)
            {
                int x0 = xs[i];
                int x1 = x0;
                int j = i + 1;
                while (j < xs.Count && xs[j] == x1 + 1)
                {
                    x1 = xs[j];
                    j++;
                }

                var key = (x0, x1);
                if (open.TryGetValue(key, out int idx))
                {
                    // Прогон продолжается сверху — растягиваем существующий прямоугольник.
                    rects[idx] = new Rect2(x0 * TileSize, startY[idx] * TileSize,
                        (x1 - x0 + 1) * TileSize, (y - startY[idx] + 1) * TileSize);
                    built[key] = idx;
                }
                else
                {
                    rects.Add(new Rect2(x0 * TileSize, y * TileSize,
                        (x1 - x0 + 1) * TileSize, TileSize));
                    startY.Add(y);
                    built[key] = rects.Count - 1;
                }

                i = j;
            }

            (open, built) = (built, open);
        }
    }

    /// <summary>
    /// R-06: контур зоны — только внешние грани, склеенные в максимальные отрезки.
    /// Периметр 200×200 → 4 линии вместо 800 отдельных DrawLine по клеткам, и каждая
    /// внешняя грань входит в контур ровно один раз.
    /// </summary>
    public static void BuildBorderSegments(HashSet<(int X, int Y)> tiles,
        List<(Vector2 From, Vector2 To)> lines)
    {
        lines.Clear();
        if (tiles == null || tiles.Count == 0) return;

        // Горизонтальные внешние грани: уровень y → список x, ребро (x,y)-(x+1,y).
        var horizontal = new Dictionary<int, List<int>>();
        // Вертикальные: уровень x → список y, ребро (x,y)-(x,y+1).
        var vertical = new Dictionary<int, List<int>>();

        foreach (var (x, y) in tiles)
        {
            if (!tiles.Contains((x, y - 1))) AddEdge(horizontal, y, x);
            if (!tiles.Contains((x, y + 1))) AddEdge(horizontal, y + 1, x);
            if (!tiles.Contains((x - 1, y))) AddEdge(vertical, x, y);
            if (!tiles.Contains((x + 1, y))) AddEdge(vertical, x + 1, y);
        }

        AppendMerged(horizontal, true, lines);
        AppendMerged(vertical, false, lines);
    }

    private static void AddEdge(Dictionary<int, List<int>> map, int level, int pos)
    {
        if (!map.TryGetValue(level, out var list))
        {
            list = new List<int>();
            map[level] = list;
        }
        list.Add(pos);
    }

    private static void AppendMerged(Dictionary<int, List<int>> edges, bool horizontal,
        List<(Vector2 From, Vector2 To)> lines)
    {
        var levels = new List<int>(edges.Keys);
        levels.Sort();

        for (int li = 0; li < levels.Count; li++)
        {
            int level = levels[li];
            var positions = edges[level];
            positions.Sort();

            int i = 0;
            while (i < positions.Count)
            {
                int start = positions[i];
                int end = start;
                int j = i + 1;
                while (j < positions.Count && positions[j] == end + 1)
                {
                    end = positions[j];
                    j++;
                }

                if (horizontal)
                    lines.Add((new Vector2(start * TileSize, level * TileSize),
                               new Vector2((end + 1) * TileSize, level * TileSize)));
                else
                    lines.Add((new Vector2(level * TileSize, start * TileSize),
                               new Vector2(level * TileSize, (end + 1) * TileSize)));

                i = j;
            }
        }
    }

    public override void _ExitTree()
    {
        ZoneManager.Instance.OnZonesUpdated -= OnZonesUpdatedThrottled;
        base._ExitTree();
    }
}