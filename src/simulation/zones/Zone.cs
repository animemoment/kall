using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.Simulation;

/// <summary>
/// Тип зоны = ОТРАСЛЬ (весь замысел владельца: «зона = условная отрасль, или её
/// отдельная часть, если она требует акцентирования внимания»).
///
/// Две служебные семантики, на которых держится симуляция:
///   * <see cref="ZoneKind.Farm"/> — грядки (тайлы + AutoPlant + культура);
///   * ЛЮБОЙ другой тип — рабочая зона-бригадир (центр + радиус, JobMask).
///
/// ЛЕГАСИ: <see cref="ZoneKind.Work"/> (1) — старая безымянная рабочая зона.
/// Оставлен числом/именем ради совместимости сборки и старых сейвов
/// (.zones.json: "kind":"work" без поля industry). При загрузке он мигрирует
/// в отрасль по своей JobMask (см. ZoneIndustryRegistry.FromLegacyJobMask),
/// по умолчанию — Warehouse. Новый код НЕ создаёт зону с Kind == Work.
/// </summary>
public enum ZoneKind : byte
{
    /// <summary>Сельское хозяйство: зона-грядка (тайлы, культура, AutoPlant).</summary>
    Farm = 0,

    /// <summary>ЛЕГАСИ-универсальная рабочая зона. Мигрирует в Warehouse/отрасль по маске.</summary>
    Work = 1,

    /// <summary>Лесозаготовка и деревообработка.</summary>
    Forestry = 2,

    /// <summary>Добыча: руда, камень, уголь.</summary>
    Mining = 3,

    /// <summary>Чёрная металлургия: чугун, сталь.</summary>
    Metallurgy = 4,

    /// <summary>Цветная металлургия: медь, свинец, цинк, алюминий.</summary>
    NonFerrous = 5,

    /// <summary>Нефтепереработка: керосин, мазут, смазки.</summary>
    OilRefining = 6,

    /// <summary>Химия: кислоты, щёлочи, красители.</summary>
    Chemistry = 7,

    /// <summary>Стройматериалы: доска, кирпич, известь, стекло.</summary>
    BuildingMaterials = 8,

    /// <summary>Текстиль: пряжа, ткань, одежда.</summary>
    Textile = 9,

    /// <summary>Станкостроение: станки, инструмент, оборудование.</summary>
    MachineBuilding = 10,

    /// <summary>Сборка: машины, двигатели, готовые изделия.</summary>
    Assembly = 11,

    /// <summary>Склад: приём, хранение и выдача грузов.</summary>
    Warehouse = 12,

    /// <summary>Жильё: кровати, скамьи, тумбочки.</summary>
    Housing = 13
}

/// <summary>
/// Семейство зоны: Farm (грядки) либо Work (всё остальное — отрасль-бригадир).
/// Сравнения старого кода «Kind == ZoneKind.Work» заменены на это семейство,
/// чтобы отрасли вели себя как рабочие зоны (диспатч, покрытие, радиус).
/// </summary>
public static class ZoneKindExtensions
{
    /// <summary>Farm-зона — грядки; тайлы материализуются.</summary>
    public static bool IsFarmZone(this ZoneKind kind) => kind == ZoneKind.Farm;

    /// <summary>Рабочая зона-бригадир (любая отрасль): центр + радиус, без тайлов.</summary>
    public static bool IsWorkZone(this ZoneKind kind) => kind != ZoneKind.Farm;

    /// <summary>Легаси-значение Work приводится к отрасли (по умолчанию — Склад).</summary>
    public static ZoneKind NormalizeLegacy(this ZoneKind kind) =>
        kind == ZoneKind.Work ? ZoneKind.Warehouse : kind;
}

/// <summary>
/// Единая зона: Farm — множество тайлов; любая отрасль — §119 P0: ЦЕНТР + РАДИУС
/// (64..512), клетки НЕ материализуются (при r=512 диск = 823k клеток =
/// гигабайты). Farm-специфика (AutoPlant/семена/культура) и Work-специфика
/// (маска/радиус/диспатч) живут в одной сущности, различаются по Kind.
/// Пересечение зон РАЗНЫХ отраслей допустимо (§119: разные типы друг друга не
/// режут); партиция спорных клеток ОДНОГО типа — P1 (ScheduleRepartition).
/// </summary>
public sealed class Zone
{
    /// <summary>Общий пустой набор тайлов для рабочих зон (тайлы не материализуются).</summary>
    private static readonly HashSet<(int X, int Y)> EmptyTiles = new();

    public int Id { get; }
    public string Name { get; set; }

    /// <summary>Отрасль зоны = тип зоны. Для рабочих зон содержит отрасль.</summary>
    public ZoneKind Kind { get; }

    /// <summary>Зона-грядка (Farm): тайлы материализованы.</summary>
    public bool IsFarmZone => Kind.IsFarmZone();

    /// <summary>Рабочая зона-бригадир (любая отрасль): центр + радиус.</summary>
    public bool IsWorkZone => Kind.IsWorkZone();

    // Геометрия Farm: _tiles — единственный источник, заменяется ссылкой под локом
    // менеджера. Для рабочих зон всегда EmptyTiles (см. ContainsTile/GetTilesSnapshot).
    private volatile HashSet<(int X, int Y)> _tiles;
    public HashSet<(int X, int Y)> Tiles
    {
        get => _tiles;
        internal set => _tiles = value;
    }

    // --- Farm-поля (актуальны при Kind == Farm) ---
    public ItemId RequiredSeedItem { get; set; } = ItemId.Grain;

    /// <summary>Культура зоны-фермы: выбирается в окне фермы, одна на зону.</summary>
    public Game.Core.CropType Crop { get; set; } = Game.Core.CropType.Wheat;

    private int _autoPlantEnabled;
    public bool AutoPlantEnabled
    {
        get => System.Threading.Volatile.Read(ref _autoPlantEnabled) != 0;
        set => System.Threading.Volatile.Write(ref _autoPlantEnabled, value ? 1 : 0);
    }

    private int _plantedCount;
    public int PlantedCount
    {
        get => System.Threading.Volatile.Read(ref _plantedCount);
        set => System.Threading.Volatile.Write(ref _plantedCount, value);
    }

    // --- Work-поля (актуальны для любой отрасли) ---
    public int CenterX { get; }
    public int CenterY { get; }

    private int _radiusTiles;
    /// <summary>§119 P0: радиус зоны 64..512 (кламп обязательен — модель «центр+радиус»).</summary>
    public int RadiusTiles
    {
        get => System.Threading.Volatile.Read(ref _radiusTiles);
        internal set => System.Threading.Volatile.Write(ref _radiusTiles,
            Math.Clamp(value, WorkZone.MinRadiusTiles, WorkZone.MaxRadiusTiles));
    }

    private int _tilesTarget;
    /// <summary>
    /// ЛЕГАСИ (UI WorkZoneController читает до P2): раньше — целевая площадь,
    /// теперь храним радиус; слайдер Tiles в P2 станет радиусом 64..512.
    /// </summary>
    public int TilesTarget
    {
        get => System.Threading.Volatile.Read(ref _tilesTarget);
        internal set => System.Threading.Volatile.Write(ref _tilesTarget, value);
    }

    private int _desiredWorkers;
    /// <summary>§119: ЗАЯВКА на число рабочих, БЕЗ потолка (0..999; 0 = зона не просит рабочих).</summary>
    public int DesiredWorkers
    {
        get => System.Threading.Volatile.Read(ref _desiredWorkers);
        internal set => System.Threading.Volatile.Write(ref _desiredWorkers,
            Math.Clamp(value, 0, WorkZone.MaxDesiredWorkers));
    }

    /// <summary>ЛЕГАСИ-алиас DesiredWorkers (UI читает до P2). Семантика: заявка, потолка нет.</summary>
    public int MaxWorkers
    {
        get => DesiredWorkers;
        internal set => DesiredWorkers = value;
    }

    private long _jobMask;
    public ulong JobMask
    {
        get => (ulong)System.Threading.Volatile.Read(ref _jobMask);
        internal set => System.Threading.Volatile.Write(ref _jobMask, (long)value);
    }

    private int _priorityOverrideValue = -1;
    public int? PriorityOverride
    {
        get
        {
            int val = System.Threading.Volatile.Read(ref _priorityOverrideValue);
            return val >= 0 ? val : null;
        }
        internal set
        {
            System.Threading.Volatile.Write(ref _priorityOverrideValue, value.HasValue ? value.Value : -1);
        }
    }

    private int _enabled;
    /// <summary>§119: зона включена (выключенная зона не диспатчит — логика в P1).</summary>
    public bool Enabled
    {
        get => System.Threading.Volatile.Read(ref _enabled) != 0;
        internal set => System.Threading.Volatile.Write(ref _enabled, value ? 1 : 0);
    }

    private int _anchorBlockId = -1;
    /// <summary>§119 P5: id центрального блока-якоря (-1 = зона без якоря). Снос якоря = снос зоны.</summary>
    public int AnchorBlockId
    {
        get => System.Threading.Volatile.Read(ref _anchorBlockId);
        internal set => System.Threading.Volatile.Write(ref _anchorBlockId, value);
    }

    public int AssignedCount;
    /// <summary>
    /// Индексы чанков (16×16, сетка ChunkDim×ChunkDim), пересекающих диск —
    /// ВЫЧИСЛЯЕТСЯ аналитически, без материализации клеток. Маленький список
    /// (≤ ChunkDim² int'ов), читается диспатчем и UI (до P2).
    /// </summary>
    public List<int> ChunkIndices { get; internal set; }

    public int TotalTiles => _tiles?.Count ?? 0;

    /// <summary>
    /// Попадание клетки в зону: Farm — по набору тайлов; отрасль (§119 P0) — по
    /// формуле радиуса dx²+dy² ≤ r², O(1), без материализации.
    /// </summary>
    public bool ContainsTile(int x, int y)
    {
        if (Kind.IsWorkZone())
        {
            long dx = x - CenterX;
            long dy = y - CenterY;
            long r = RadiusTiles;
            return dx * dx + dy * dy <= r * r;
        }
        var snap = _tiles;
        return snap != null && snap.Contains((x, y));
    }

    /// <summary>
    /// Снапшот тайлов: Farm — копия набора; отрасль — пустой набор (тайлов нет).
    /// </summary>
    public HashSet<(int X, int Y)> GetTilesSnapshot()
    {
        var snap = _tiles;
        if (Kind.IsWorkZone() || snap == null)
            return new HashSet<(int X, int Y)>();
        return new HashSet<(int X, int Y)>(snap);
    }

    internal Zone(int id, string name, ZoneKind kind, HashSet<(int X, int Y)> tiles)
    {
        Id = id;
        Name = name;
        Kind = kind.NormalizeLegacy();
        // Рабочие зоны через этот ctor (тесты Phase5) — тайлы не материализуем.
        _tiles = Kind.IsWorkZone() ? EmptyTiles : tiles;
    }

    /// <summary>§119 P0 + отрасль: рабочая зона = центр + радиус. Клетки НЕ материализуются.</summary>
    internal Zone(int id, string name, ZoneKind kind, int centerX, int centerY, int radiusTiles,
        int desiredWorkers, ulong jobMask, List<int> chunkIndices)
        : this(id, name, kind, EmptyTiles)
    {
        CenterX = centerX;
        CenterY = centerY;
        RadiusTiles = radiusTiles;
        TilesTarget = RadiusTiles; // легаси-поле: до P2 хранит радиус
        DesiredWorkers = desiredWorkers;
        JobMask = jobMask;
        PriorityOverride = null;
        AssignedCount = 0;
        Enabled = true;
        AnchorBlockId = -1;
        ChunkIndices = chunkIndices;
    }

    /// <summary>ЛЕГАСИ-ctor: рабочая зона без отрасли (Work → Warehouse при создании).</summary>
    internal Zone(int id, string name, int centerX, int centerY, int radiusTiles,
        int desiredWorkers, ulong jobMask, List<int> chunkIndices)
        : this(id, name, ZoneKind.Work, centerX, centerY, radiusTiles,
            desiredWorkers, jobMask, chunkIndices)
    {
    }

    public bool Matches(JobTypeId t) => (JobMask & (1UL << (int)t)) != 0;

    public void RegisterAssigned() => System.Threading.Interlocked.Increment(ref AssignedCount);

    public void ResetAssigned() => System.Threading.Volatile.Write(ref AssignedCount, 0);
}