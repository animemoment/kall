using Godot;
using Game.Simulation;
using System;
using System.Collections.Generic;

namespace Game.UI;

/// <summary>
/// Рендер РАДИУСНЫХ зон-отраслей (task-18): §119-зона = центр + радиус 64..512,
/// клетки НЕ материализуются, поэтому рисуем КОНТУР окружности + очень
/// прозрачную заливку, а не 823k клеток.
///
/// Почему отдельный рендерер, а не FarmZoneRenderer: тот рисует тайловые Farm-зоны
/// (прямоугольники по клеткам). Радиальные зоны тайлов не имеют, у них другая
/// геометрия (DrawCircle/DrawArc) и другой цветовой язык (цвет = отрасль).
///
/// Инварианты:
///  * перестроение ТОЛЬКО по событию ZoneManager (create/remove/radius/select/hover),
///    события коалесцируются в один deferred-кадр; в _Draw — только примитивы,
///    никаких обходов клеток и аллокаций;
///  * ZIndex = 8: выше земли/декора/стен (0), ниже агентов (AgentRenderer = 10),
///    рядом с FarmZoneRenderer (8);
///  * толщина линии и размер подписи пересчитываются из зума камеры, поэтому
///    контур не «плывёт» и не мигает при движении камеры (рисуем в мировых
///    координатах — двигать ничего не нужно).
/// </summary>
public partial class IndustryZoneRenderer : Node2D
{
    private const float TileSize = 64f;

    /// <summary>Толщина контура в ЭКРАННЫХ пикселях (переводится в мировые по зуму).</summary>
    private const float BorderScreenPx = 2.5f;
    private const float SelectedBorderScale = 1.8f;
    private const float HoverBorderScale = 1.4f;

    /// <summary>Заявленная прозрачность заливки (task-18: 0.06..0.10).</summary>
    private const float FillAlpha = 0.055f;
    private const float FillAlphaHover = 0.08f;
    private const float FillAlphaSelected = 0.10f;

    private const string FontPath = "res://assets/fonts/Handjet-VariableFont_ELGR,ELSH,wght.ttf";

    /// <summary>Снимок одной зоны для _Draw (без ссылки на мутируемый Zone).</summary>
    private struct ZoneVisual
    {
        public int Id;
        public Vector2 Center;
        public float Radius;
        public Color Border;
        public Color Fill;
        public bool Enabled;
        public string Name;
        public string IndustryTitle;
        public bool HasIndustryLine;
        /// <summary>Заранее построенный многоугольник заливки (аллокация — по событию, не в кадре).</summary>
        public Vector2[] FillPoints;
    }

    // Переиспользуемый буфер: перестраивается по событию, _Draw только читает.
    private readonly List<ZoneVisual> _visuals = new(16);

    // Кэш многоугольников заливки: id зоны → (радиус, точки). Радиус меняется
    // слайдером редко, поэтому при hover/выборе массивы не пересоздаются.
    private readonly Dictionary<int, (long RadiusKey, Vector2[] Points)> _polyCache = new(16);
    private readonly HashSet<int> _liveIds = new(16);
    private readonly List<int> _staleIds = new(16);

    private Font _font;
    private bool _dirty;
    private bool _flushQueued;
    private bool _active;

    /// <summary>Цвет отрасли: приглушённая палитра 1920-х (не кислотная), 13 оттенков.</summary>
    public static Color IndustryColor(ZoneKind kind) => kind switch
    {
        ZoneKind.Farm => new Color(0.45f, 0.55f, 0.26f),          // оливковый
        ZoneKind.Forestry => new Color(0.28f, 0.45f, 0.30f),      // хвойный
        ZoneKind.Mining => new Color(0.55f, 0.44f, 0.28f),        // охра
        ZoneKind.Metallurgy => new Color(0.66f, 0.35f, 0.25f),    // ржавый
        ZoneKind.NonFerrous => new Color(0.30f, 0.55f, 0.52f),    // патина
        ZoneKind.OilRefining => new Color(0.28f, 0.36f, 0.52f),   // сланцево-синий
        ZoneKind.Chemistry => new Color(0.48f, 0.36f, 0.56f),     // фиолетовый
        ZoneKind.BuildingMaterials => new Color(0.56f, 0.50f, 0.40f), // глина
        ZoneKind.Textile => new Color(0.62f, 0.42f, 0.48f),       // пыльная роза
        ZoneKind.MachineBuilding => new Color(0.38f, 0.46f, 0.56f), // сталь
        ZoneKind.Assembly => new Color(0.62f, 0.48f, 0.28f),      // бронза
        ZoneKind.Warehouse => new Color(0.46f, 0.36f, 0.56f),     // лиловый
        ZoneKind.Housing => new Color(0.62f, 0.55f, 0.40f),       // песочный
        _ => new Color(0.5f, 0.5f, 0.5f),
    };

    public override void _Ready()
    {
        _active = true;
        ZIndex = 8; // поверх земли/декора/стен, под агентами (10)
        try
        {
            _font = ResourceLoader.Load<Font>(FontPath);
        }
        catch
        {
            _font = null; // без подписи, контур всё равно рисуется
        }

        ZoneManager.Instance.OnZonesUpdated += MarkDirty;
        ZoneManager.Instance.OnZoneSelected += OnZoneSelectedChanged;
        ZoneManager.Instance.OnZoneDeselected += OnZoneDeselectedChanged;
        ZoneManager.Instance.OnZoneHovered += OnZoneHoveredChanged;
        MarkDirty();
    }

    public override void _ExitTree()
    {
        // Статический ZoneManager не должен держать освобождённый рендерер.
        _active = false;
        ZoneManager.Instance.OnZonesUpdated -= MarkDirty;
        ZoneManager.Instance.OnZoneSelected -= OnZoneSelectedChanged;
        ZoneManager.Instance.OnZoneDeselected -= OnZoneDeselectedChanged;
        ZoneManager.Instance.OnZoneHovered -= OnZoneHoveredChanged;
        base._ExitTree();
    }

    private void OnZoneSelectedChanged(Zone zone) => MarkDirty();
    private void OnZoneDeselectedChanged() => MarkDirty();
    private void OnZoneHoveredChanged(Zone zone) => MarkDirty();

    /// <summary>
    /// Пометить «надо перерисовать». События коалесцируются: сколько бы зон ни
    /// изменилось за кадр, перестроение и QueueRedraw будут один раз.
    /// </summary>
    private void MarkDirty()
    {
        _dirty = true;
        if (_flushQueued)
            return;
        _flushQueued = true;
        Callable.From(FlushDirty).CallDeferred();
    }

    private void FlushDirty()
    {
        _flushQueued = false;
        // Нода могла быть освобождена между событием и deferred-кадром
        // (смена сцены): тихо выходим, к Godot-объекту не обращаемся.
        if (!_active || !_dirty)
            return;
        _dirty = false;
        RebuildVisuals();
        QueueRedraw();
    }

    /// <summary>
    /// Перестроение кэша по событию (не в кадре): копируем только нужные поля,
    /// чтобы _Draw не обходил зоны и не аллоцировал.
    /// </summary>
    private void RebuildVisuals()
    {
        _visuals.Clear();
        _liveIds.Clear();
        List<Zone> zones;
        try
        {
            zones = ZoneManager.Instance.GetAllZones();
        }
        catch
        {
            return;
        }
        for (int i = 0; i < zones.Count; i++)
        {
            Zone z = zones[i];
            if (z == null || !z.IsWorkZone || z.RadiusTiles <= 0)
                continue;

            Color baseColor = IndustryColor(z.Kind);
            string title = ZoneIndustryRegistry.TitleFor(z.Kind);
            string name = z.Name ?? title;
            var center = new Vector2(z.CenterX * TileSize + TileSize * 0.5f,
                                     z.CenterY * TileSize + TileSize * 0.5f);
            float radius = z.RadiusTiles * TileSize;
            _liveIds.Add(z.Id);
            long radiusKey = (long)Mathf.Round(radius);
            Vector2[] fillPoints;
            if (!_polyCache.TryGetValue(z.Id, out var cached) || cached.RadiusKey != radiusKey)
            {
                fillPoints = BuildFillPolygon(center, radius);
                _polyCache[z.Id] = (radiusKey, fillPoints);
            }
            else
            {
                fillPoints = cached.Points;
            }
            _visuals.Add(new ZoneVisual
            {
                Id = z.Id,
                Center = center,
                Radius = radius,
                Border = baseColor,
                Fill = new Color(baseColor.R, baseColor.G, baseColor.B, 1f),
                Enabled = z.Enabled,
                Name = name,
                IndustryTitle = title,
                HasIndustryLine = !string.Equals(name, title, StringComparison.Ordinal),
                FillPoints = fillPoints,
            });
        }

        // Чистка кэша удалённых зон (иначе массивы живут до конца сцены).
        _staleIds.Clear();
        foreach (int cachedId in _polyCache.Keys)
        {
            if (!_liveIds.Contains(cachedId))
                _staleIds.Add(cachedId);
        }
        for (int i = 0; i < _staleIds.Count; i++)
            _polyCache.Remove(_staleIds[i]);
    }

    /// <summary>
    /// Многоугольник заливки по границе радиуса. Строится по СОБЫТИЮ (не в кадре):
    /// сегментов ~2 на игровую клетку радиуса (128..720), поэтому хорда на
    /// максимальном r=512 кл. ~1 мировой px — дуга не выглядит многоугольником.
    /// </summary>
    private static Vector2[] BuildFillPolygon(Vector2 center, float radius)
    {
        int segments = Mathf.Clamp((int)(radius / TileSize) * 2, 128, 720);
        var points = new Vector2[segments];
        float step = Mathf.Tau / segments;
        for (int i = 0; i < segments; i++)
        {
            float a = i * step;
            points[i] = new Vector2(center.X + Mathf.Cos(a) * radius,
                                    center.Y + Mathf.Sin(a) * radius);
        }
        return points;
    }

    public override void _Draw()
    {
        if (_visuals.Count == 0)
            return;

        // Мировые пиксели -> экранные: держим толщину линии и подпись постоянными
        // при любом зуме камеры. При движении камеры ничего не пересчитываем.
        float zoom = 1f;
        Transform2D canvasXf = GetCanvasTransform();
        if (Mathf.Abs(canvasXf.Scale.X) > 0.0001f)
            zoom = Mathf.Abs(canvasXf.Scale.X);
        float borderWidth = Mathf.Clamp(BorderScreenPx / zoom, 1.5f, 64f);
        float markerRadius = Mathf.Clamp(2.5f / zoom, 1.5f, 24f);

        Zone selected = ZoneManager.Instance.SelectedZone;
        Zone hovered = ZoneManager.Instance.HoveredZone;
        int selectedId = selected != null ? selected.Id : -1;
        int hoveredId = hovered != null ? hovered.Id : -1;

        for (int i = 0; i < _visuals.Count; i++)
        {
            ZoneVisual v = _visuals[i];
            bool isSelected = v.Id == selectedId;
            bool isHovered = v.Id == hoveredId;
            Color border = v.Border;
            Color fill = v.Fill;

            if (!v.Enabled)
            {
                // Выключенная зона не работает — контур тусклый, заливки почти нет.
                border.A = 0.28f;
                fill.A = 0.02f;
            }
            else if (isSelected)
            {
                border.A = 0.95f;
                fill.A = FillAlphaSelected;
            }
            else if (isHovered)
            {
                border.A = 0.80f;
                fill.A = FillAlphaHover;
            }
            else
            {
                border.A = 0.50f;
                fill.A = FillAlpha;
            }

            // Радиус 64..512 клеток: одна заливка (заранее собранный полигон) + один
            // контур. Никаких обходов клеток: у зоны r=512 их было бы 823k.
            if (v.FillPoints != null)
                DrawColoredPolygon(v.FillPoints, fill);
            int points = Mathf.Clamp((int)(v.Radius / TileSize) * 2, 128, 720);
            float width = borderWidth;
            if (isSelected)
                width *= SelectedBorderScale;
            else if (isHovered)
                width *= HoverBorderScale;
            if (!v.Enabled)
                width *= 0.8f;
            DrawArc(v.Center, v.Radius, 0f, Mathf.Tau, points, border, width, true);

            // Точка центра: понять, откуда считается радиус.
            DrawCircle(v.Center, markerRadius, border);

            // Подпись имени у центра: у выбранной/наведённой — ярко и с отраслью,
            // у остальных — приглушённо, чтобы только что созданная зона была
            // видна даже когда её контур (r 64..512 кл. = 4k..32k px) за экраном.
            if (_font != null)
                DrawLabel(v, border, isSelected, isHovered, zoom);
        }
    }

    private void DrawLabel(in ZoneVisual v, Color border, bool isSelected, bool isHovered, float zoom)
    {
        int fontSize = Mathf.Clamp((int)(15f / zoom), 9, 128);
        if (!isSelected && !isHovered)
            fontSize = Mathf.Max(9, (int)(fontSize * 0.85f));
        float lineGap = fontSize * 1.15f;
        float alpha = isSelected ? 0.98f : isHovered ? 0.9f : 0.62f;
        Color textColor = new Color(0.96f, 0.95f, 0.92f, alpha);
        Color shadowColor = new Color(0f, 0f, 0f, isSelected || isHovered ? 0.85f : 0.6f);
        float shadow = Mathf.Max(1f, fontSize * 0.08f);

        DrawTextLine(v.Name, v.Center, -lineGap * 0.5f, fontSize, textColor, shadowColor, shadow);

        if (v.HasIndustryLine && (isSelected || isHovered))
        {
            Color industryColor = new Color(border.R, border.G, border.B, 0.95f);
            int smallSize = Mathf.Max(9, (int)(fontSize * 0.82f));
            DrawTextLine(v.IndustryTitle, v.Center, lineGap * 0.55f, smallSize,
                industryColor, shadowColor, shadow);
        }
    }

    private void DrawTextLine(string text, Vector2 center, float dy, int fontSize,
        Color color, Color shadowColor, float shadow)
    {
        if (string.IsNullOrEmpty(text))
            return;
        Vector2 size = _font.GetStringSize(text, HorizontalAlignment.Left, -1, fontSize);
        var pos = new Vector2(center.X - size.X * 0.5f, center.Y + dy + fontSize * 0.35f);
        // Тень: читаемость поверх пёстрой карты (два DrawString, без аллокаций).
        DrawString(_font, pos + new Vector2(shadow, shadow), text,
            HorizontalAlignment.Left, -1, fontSize, shadowColor);
        DrawString(_font, pos, text, HorizontalAlignment.Left, -1, fontSize, color);
    }
}