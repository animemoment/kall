using Godot;
using Game.Core;
using Game.Core.World;
using Game.Core.WorldStreaming;
using Game.Core.WorldStreaming.Integration;
using Game.Simulation;
using System;
using System.Collections.Generic;

namespace Game.UI;

/// <summary>
/// Оверлей дополнительных РЕЖИМОВ КАРТЫ (task-6): месторождения, высоты, склад,
/// работы. Влажность и плодородие остаются за своими рендерерами (Humidity/
/// FertilityOverlayRenderer) — здесь только новые режимы.
///
/// Принципы:
///  * одна текстура на размер карты (W×H, RGBA8), полностью прозрачная вне данных;
///    растянута на мир (W*64 × H*64), фильтр NEAREST — клетки видны квадратами;
///  * перестроение — НЕ в кадре: сразу по переключению режима, дальше по событию
///    (изменение руды/склада) или 1 раз/сек для режима работ (набор работ меняется
///    постоянно, события на него нет). Пока режим не активен, ничего не считается;
///  * буферы пикселей/Image переиспользуются, аллокаций на перестроение — только
///    снимки менеджеров (раз в тик обновления, не в кадре);
///  * «известные месторождения»: v1 показывает ВСЕ залежи острова; структура
///    оставлена под фильтр разведанности (см. IsOreKnown — точка расширения).
///
/// Источник данных: MapData.OreType/OreReserve/OreReserveInitial (остров),
/// WorldModel (высоты), StockpileManager (+ слой зон склада MapRenderer),
/// JobSpatialPyramid (работы).
/// </summary>
public partial class MapModeOverlayRenderer : TextureRect
{
    private const float JobsRefreshIntervalSec = 1.0f;
    private const float DirtyRefreshIntervalSec = 0.35f;
    private const int MaxJobDraw = 100_000;

    private MapMode _mode = MapMode.Normal;
    private MapData _map;
    private int _w;
    private int _h;

    private byte[] _pixels;
    private Image _image;
    private ImageTexture _texture;

    private TileMapLayer _stockpileLayer;

    private bool _dirty;
    private float _timer;
    private bool _built;

    // Переиспользуемые буферы снимков (не аллоцируем в кадре).
    private readonly List<int> _jobIds = new(8192);
    private readonly List<(int X, int Y, ItemId Item, int Count)> _storage = new(4096);
    private readonly List<Vector2I> _zoneCells = new(8192);

    public MapMode Mode => _mode;

    /// <summary>Поддерживает ли оверлей этот режим (иначе его рисуют другие рендереры).</summary>
    public static bool Handles(MapMode mode) =>
        mode == MapMode.Ore || mode == MapMode.Elevation
        || mode == MapMode.Stockpile || mode == MapMode.Jobs;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.TopLeft);
        Position = Vector2.Zero;
        Size = new Vector2(MapRenderer.MapWidth * MapRenderer.TileSizePx,
            MapRenderer.MapHeight * MapRenderer.TileSizePx);
        StretchMode = StretchModeEnum.Scale;
        TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
        MouseFilter = Control.MouseFilterEnum.Ignore;
        _texture = new ImageTexture();
        Texture = _texture;
        Visible = false;

        // События данных: только помечаем «грязно», перестроение — по таймеру.
        OreJobManager.Instance.OnOreChanged += OnOreCellChanged;
        OreJobManager.Instance.OnOreDepleted += OnOreCellChanged;
        StockpileManager.Instance.OnZoneTileAdded += OnStockpileTileChanged;
        StockpileManager.Instance.OnZoneTileRemoved += OnStockpileTileChanged;
        StockpileManager.Instance.OnZoneTilesBatchAdded += OnStockpileTilesBatchChanged;
        StockpileManager.Instance.OnZoneTilesBatchRemoved += OnStockpileTilesBatchChanged;
        StockpileManager.Instance.OnItemCountChanged += OnStockpileItemChanged;
    }

    public override void _ExitTree()
    {
        OreJobManager.Instance.OnOreChanged -= OnOreCellChanged;
        OreJobManager.Instance.OnOreDepleted -= OnOreCellChanged;
        StockpileManager.Instance.OnZoneTileAdded -= OnStockpileTileChanged;
        StockpileManager.Instance.OnZoneTileRemoved -= OnStockpileTileChanged;
        StockpileManager.Instance.OnZoneTilesBatchAdded -= OnStockpileTilesBatchChanged;
        StockpileManager.Instance.OnZoneTilesBatchRemoved -= OnStockpileTilesBatchChanged;
        StockpileManager.Instance.OnItemCountChanged -= OnStockpileItemChanged;
        base._ExitTree();
    }

    private void OnOreCellChanged((int X, int Y) pos)
    {
        if (_mode == MapMode.Ore)
            _dirty = true;
    }

    private void OnStockpileTileChanged((int X, int Y) pos)
    {
        if (_mode == MapMode.Stockpile)
            _dirty = true;
    }

    private void OnStockpileTilesBatchChanged((int X, int Y)[] cells)
    {
        if (_mode == MapMode.Stockpile)
            _dirty = true;
    }

    private void OnStockpileItemChanged(ItemId item, int count)
    {
        if (_mode == MapMode.Stockpile)
            _dirty = true;
    }

    /// <summary>Слой зон склада: из него берём клетки складских зон (данных-списка у менеджера нет).</summary>
    public void BindStockpileLayer(TileMapLayer layer) => _stockpileLayer = layer;

    /// <summary>Новая карта (ApplyMap) — пересоздать буферы под её размер.</summary>
    public void SetMap(MapData map)
    {
        _map = map;
        int w = map?.Width ?? 0;
        int h = map?.Height ?? 0;
        if (w <= 0 || h <= 0)
            return;
        if (w != _w || h != _h)
        {
            _w = w;
            _h = h;
            Size = new Vector2(w * MapRenderer.TileSizePx, h * MapRenderer.TileSizePx);
            // Пиксельный буфер и Image создаются лениво в Rebuild: если игрок
            // никогда не включает режимы карты, 8 МБ на карту не тратятся.
            _pixels = null;
            _image = null;
            _built = false;
        }
        _dirty = true;
        if (Visible && Handles(_mode))
            RefreshNow();
    }

    /// <summary>Переключить режим. Вызывающий сам решает Visible.</summary>
    public void SetMode(MapMode mode)
    {
        _mode = mode;
        _dirty = true;
        _timer = 0f;
        _built = false;
        if (Visible && Handles(mode))
            RefreshNow();
        if (!Handles(mode))
            ClearTexture();
    }

    /// <summary>Перестроить немедленно (переключение режима / смена карты).</summary>
    public void RefreshNow()
    {
        _dirty = false;
        _timer = 0f;
        Rebuild();
    }

    /// <summary>Троттлинг: звать из MapRenderer._Process, пока режим активен.</summary>
    public void RefreshThrottled(double delta)
    {
        if (!Visible || !Handles(_mode) || _map == null || _texture == null)
            return;
        float interval = _mode == MapMode.Jobs ? JobsRefreshIntervalSec : DirtyRefreshIntervalSec;
        _timer += (float)delta;
        if (!_dirty && !(_mode == MapMode.Jobs) && _built)
            return;
        if (_timer < interval)
            return;
        _timer = 0f;
        _dirty = false;
        Rebuild();
    }

    private void ClearTexture()
    {
        _texture?.SetImage(Image.CreateEmpty(1, 1, false, Image.Format.Rgba8));
    }

    // ============================ построение ============================

    private void Rebuild()
    {
        if (_map == null || _texture == null || _w <= 0 || _h <= 0)
            return;
        if (_pixels == null || _pixels.Length != _w * _h * 4)
            _pixels = new byte[_w * _h * 4];
        _image ??= Image.CreateEmpty(_w, _h, false, Image.Format.Rgba8);
        Array.Clear(_pixels, 0, _pixels.Length);

        switch (_mode)
        {
            case MapMode.Ore: BuildOre(); break;
            case MapMode.Elevation: BuildElevation(); break;
            case MapMode.Stockpile: BuildStockpile(); break;
            case MapMode.Jobs: BuildJobs(); break;
            default: break;
        }

        _image.SetData(_w, _h, false, Image.Format.Rgba8, _pixels);
        _texture.SetImage(_image);
        _built = true;
    }

    /// <summary>Месторождения: цвет = тип руды, альфа = доля остатка запаса.</summary>
    private void BuildOre()
    {
        byte[] oreType = _map.OreType;
        ushort[] reserve = _map.OreReserve;
        ushort[] initial = _map.OreReserveInitial;
        if (oreType == null)
            return;

        int w = _w, h = _h;
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                int idx = row + x;
                byte type = oreType[idx];
                if (type == 0)
                    continue;
                // «Известные месторождения»: v1 — все. Точка расширения под разведку.
                if (!IsOreKnown(x, y, type))
                    continue;
                int init = initial != null ? initial[idx] : 0;
                int left = reserve != null ? reserve[idx] : 0;
                float frac = init > 0 ? Math.Clamp(left / (float)init, 0f, 1f) : 1f;
                Color c = OreColor(type);
                // Совсем бедные жилы не исчезают совсем (альфа ≥0.25), богатые — плотные.
                c.A = 0.25f + 0.60f * frac;
                WritePixel(x, y, c);
            }
        }
    }

    /// <summary>
    /// Точка расширения «туман/разведка»: сейчас все залежи известны.
    /// Позже — проверка разведанности клетки/провинции.
    /// </summary>
    public static bool IsOreKnown(int x, int y, byte oreType) => true;

    /// <summary>Высоты: вода — синяя, суша — низина тёмная → вершины светлые.</summary>
    private void BuildElevation()
    {
        int w = _w, h = _h;
        BulkCellSampler sampler;
        bool hasSampler = true;
        try
        {
            sampler = WorldModel.BeginBulkSample(WorldModel.Seed, WorldModel.Version,
                0, 0, w, h,
                UnifiedWorldGenerator.WorldOriginX, UnifiedWorldGenerator.WorldOriginY, null);
        }
        catch
        {
            sampler = default;
            hasSampler = false;
        }

        ushort[] elev = new ushort[w * h];
        int min = int.MaxValue, max = int.MinValue;
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                ushort e = 0;
                if (hasSampler)
                {
                    try { e = sampler.CellAtLocal(x, y).ElevationQ16; }
                    catch { e = 0; hasSampler = false; }
                }
                elev[row + x] = e;
                if (e < min) min = e;
                if (e > max) max = e;
            }
        }
        float span = Math.Max(1, max - min);

        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                bool water = _map.Ground[x, y] != TileType.Grass;
                Color c;
                if (water)
                {
                    c = new Color(0.12f, 0.32f, 0.62f, 0.70f);
                }
                else
                {
                    float t = Math.Clamp((elev[row + x] - min) / span, 0f, 1f);
                    c = ElevationColor(t);
                    c.A = 0.78f;
                }
                WritePixel(x, y, c);
            }
        }
    }

    /// <summary>Склад: складские зоны (фиолетовая подложка) + занятые слоты цветом товара.</summary>
    private void BuildStockpile()
    {
        if (_stockpileLayer != null)
        {
            _zoneCells.Clear();
            foreach (Vector2I c in _stockpileLayer.GetUsedCells())
            {
                if ((uint)c.X < (uint)_w && (uint)c.Y < (uint)_h)
                    _zoneCells.Add(c);
            }
            Color zone = new(0.62f, 0.25f, 0.85f, 0.16f);
            for (int i = 0; i < _zoneCells.Count; i++)
                WritePixel(_zoneCells[i].X, _zoneCells[i].Y, zone);
        }

        _storage.Clear();
        List<(int X, int Y, ItemId Item, int Count)> snapshot;
        try
        {
            snapshot = StockpileManager.Instance.GetStorageSnapshot();
        }
        catch
        {
            return;
        }
        if (snapshot == null)
            return;
        _storage.AddRange(snapshot);
        for (int i = 0; i < _storage.Count; i++)
        {
            var s = _storage[i];
            if ((uint)s.X >= (uint)_w || (uint)s.Y >= (uint)_h)
                continue;
            Color c = StockpileItemColor(s.Item);
            // Чем полнее слот (по стаку 100), тем плотнее цвет.
            c.A = 0.45f + 0.45f * Math.Clamp(s.Count / 100f, 0f, 1f);
            WritePixel(s.X, s.Y, c);
        }
    }

    /// <summary>Работы: клетка = цель работы, цвет = тип работы.</summary>
    private void BuildJobs()
    {
        JobSpatialPyramid index = JobDispatcher.Instance.JobIndex;
        int total = index.TotalCount;
        _jobIds.Clear();
        if (total <= 0)
            return;
        int cursor = 0;
        try
        {
            index.SnapshotActiveJobs(_jobIds, Math.Min(total, MaxJobDraw), ref cursor);
        }
        catch
        {
            return;
        }
        for (int i = 0; i < _jobIds.Count; i++)
        {
            JobData job;
            try { job = index.GetJobData(_jobIds[i]); }
            catch { continue; }
            if (!job.IsActive || job.TypeId == JobTypeId.None)
                continue;
            int x = job.TargetX;
            int y = job.TargetY;
            if ((uint)x >= (uint)_w || (uint)y >= (uint)_h)
                continue;
            Color c = JobColor(job.TypeId);
            c.A = 0.55f;
            WritePixel(x, y, c);
        }
    }

    // ============================ запись пикселей ============================

    private void WritePixel(int x, int y, Color c)
    {
        int o = (y * _w + x) * 4;
        byte[] px = _pixels;
        // Простая альфа-запись поверх (данные не пересекаются, кроме склада).
        px[o + 0] = (byte)Math.Clamp((int)(c.R * 255f), 0, 255);
        px[o + 1] = (byte)Math.Clamp((int)(c.G * 255f), 0, 255);
        px[o + 2] = (byte)Math.Clamp((int)(c.B * 255f), 0, 255);
        px[o + 3] = (byte)Math.Clamp((int)(c.A * 255f), 0, 255);
    }

    // ============================ палитры ============================

    /// <summary>Приглушённая палитра 23 руд (по порядку OreType 1..23).</summary>
    public static Color OreColor(byte oreType) => oreType switch
    {
        1 => new Color(0.68f, 0.34f, 0.24f),  // железо (гематит)
        2 => new Color(0.36f, 0.38f, 0.44f),  // железо (магнетит)
        3 => new Color(0.48f, 0.40f, 0.56f),  // марганец
        4 => new Color(0.38f, 0.50f, 0.40f),  // хромит
        5 => new Color(0.36f, 0.47f, 0.60f),  // вольфрам
        6 => new Color(0.42f, 0.48f, 0.56f),  // молибден
        7 => new Color(0.50f, 0.42f, 0.58f),  // ванадий
        8 => new Color(0.42f, 0.56f, 0.44f),  // никель
        9 => new Color(0.34f, 0.44f, 0.66f),  // кобальт
        10 => new Color(0.56f, 0.56f, 0.60f), // титан
        11 => new Color(0.74f, 0.47f, 0.22f), // медь (колчедан)
        12 => new Color(0.32f, 0.62f, 0.52f), // медь (окисленная)
        13 => new Color(0.50f, 0.50f, 0.54f), // свинец (галенит)
        14 => new Color(0.54f, 0.60f, 0.64f), // цинк
        15 => new Color(0.57f, 0.62f, 0.57f), // олово
        16 => new Color(0.72f, 0.44f, 0.22f), // боксит
        17 => new Color(0.64f, 0.26f, 0.36f), // ртуть
        18 => new Color(0.76f, 0.66f, 0.26f), // пирит
        19 => new Color(0.86f, 0.74f, 0.26f), // золото
        20 => new Color(0.76f, 0.79f, 0.83f), // серебро
        21 => new Color(0.69f, 0.73f, 0.76f), // платина
        22 => new Color(0.46f, 0.66f, 0.30f), // уран
        23 => new Color(0.72f, 0.60f, 0.36f), // монацит
        _ => new Color(0.6f, 0.6f, 0.6f),
    };

    /// <summary>Градиент высоты: низ — тёмно-сине-зелёный, верх — почти белый.</summary>
    public static Color ElevationColor(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        if (t < 0.35f)
        {
            float k = t / 0.35f;
            return new Color(0.10f + 0.22f * k, 0.24f + 0.30f * k, 0.30f + 0.08f * k);
        }
        if (t < 0.7f)
        {
            float k = (t - 0.35f) / 0.35f;
            return new Color(0.32f + 0.36f * k, 0.54f - 0.06f * k, 0.38f - 0.06f * k);
        }
        float k2 = (t - 0.7f) / 0.3f;
        return new Color(0.68f + 0.28f * k2, 0.48f + 0.42f * k2, 0.32f + 0.58f * k2);
    }

    /// <summary>Цвет товара на складе (по типу предмета, различимый на глаз).</summary>
    public static Color StockpileItemColor(ItemId item)
    {
        if (item == ItemId.None)
            return new Color(0.7f, 0.7f, 0.7f);
        int idx = (int)item;
        float hue = (idx * 0.137f) % 1f;
        return Color.FromHsv(hue, 0.55f, 0.92f);
    }

    /// <summary>Цвет типа работы.</summary>
    public static Color JobColor(JobTypeId type) => type switch
    {
        JobTypeId.TreeChopping => new Color(0.26f, 0.50f, 0.24f),
        JobTypeId.Construction => new Color(0.36f, 0.52f, 0.78f),
        JobTypeId.Farming => new Color(0.58f, 0.48f, 0.22f),
        JobTypeId.BlueprintDelivery => new Color(0.86f, 0.76f, 0.36f),
        JobTypeId.StockpileHauling => new Color(0.30f, 0.72f, 0.76f),
        JobTypeId.ClearSite => new Color(0.62f, 0.42f, 0.30f),
        JobTypeId.Crafting => new Color(0.66f, 0.46f, 0.72f),
        JobTypeId.Mining => new Color(0.66f, 0.62f, 0.56f),
        JobTypeId.Planting => new Color(0.46f, 0.72f, 0.36f),
        JobTypeId.Harvesting => new Color(0.82f, 0.72f, 0.26f),
        JobTypeId.WorldPlan => new Color(0.52f, 0.52f, 0.92f),
        JobTypeId.OreMining => new Color(0.78f, 0.46f, 0.22f),
        JobTypeId.Production => new Color(0.84f, 0.54f, 0.24f),
        _ => new Color(0.6f, 0.6f, 0.6f),
    };
}