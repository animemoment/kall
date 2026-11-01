using System;
using System.Threading.Tasks;
using Game.Core.WorldStreaming;

namespace Game.Core.WorldStreaming.Integration;

/// <summary>
/// Deterministic read-only sampling of the generated base world. These queries do not
/// require a resident chunk; they sample the generator at absolute signed coordinates.
/// </summary>
public static class WorldCellQuery
{
    /// <summary>
    /// Полная клетка одним семплом генератора. Нужна гейтам, которым требуются
    /// и террейн, и влажность/лес: два отдельных запроса — это два полных семпла.
    /// </summary>
    public static GeneratedCell CellAt(ulong seed, uint ver, long x, long y)
        => Sample(seed, ver, x, y);

    public static BaseTerrainKind TerrainAt(ulong seed, uint ver, long x, long y)
        => Sample(seed, ver, x, y).Terrain;

    /// <summary>Raw ushort Q0.16 elevation value, retained as a short legacy-friendly name.</summary>
    public static ushort ElevationAt(ulong seed, uint ver, long x, long y)
        => ElevationQ16At(seed, ver, x, y);

    public static ushort ElevationQ16At(ulong seed, uint ver, long x, long y)
        => Sample(seed, ver, x, y).ElevationQ16;

    public static ushort MoistureQ16At(ulong seed, uint ver, long x, long y)
        => Sample(seed, ver, x, y).MoistureQ16;

    public static ushort ForestQ16At(ulong seed, uint ver, long x, long y)
        => Sample(seed, ver, x, y).ForestQ16;

    public static ushort StoneQ16At(ulong seed, uint ver, long x, long y)
        => Sample(seed, ver, x, y).StoneQ16;

    public static bool IsWaterAt(ulong seed, uint ver, long x, long y)
        => TerrainAt(seed, ver, x, y) == BaseTerrainKind.Water;

    public static bool IsMountainAt(ulong seed, uint ver, long x, long y)
        => TerrainAt(seed, ver, x, y) == BaseTerrainKind.Mountain;

    private static GeneratedCell Sample(ulong seed, uint ver, long x, long y)
        => UnifiedWorldGenerator.SampleCell(seed, ver, UnifiedWorldGenerator.WorldOriginX + x, UnifiedWorldGenerator.WorldOriginY + y);

    // ------------------------------------------------------------------------------------
    // Массовый быстрый путь (PLAN §117, задача 3): BeginBulkSample.
    // ------------------------------------------------------------------------------------

    /// <summary>
    /// Массовый быстрый путь для последовательных запросов по прямоугольнику (окна террейна
    /// MapRenderer и т.п.): климатическая сетка с шагом 16 клеток (как GenerateChunk /
    /// GenerateColonyWindow) считается ОДИН раз, дальше каждая клетка — билинейный
    /// LerpClimate + SampleCell(overrideClimate). Это устраняет полный
    /// SampleBlendedCell (4 региона + BlendBilinear 18 полей) с каждого вызова.
    ///
    /// Координаты originX/originY — в том же пространстве, что и CellAt
    /// (к нему прибавляется текущий WorldOrigin, снапшот на момент Begin).
    ///
    /// Значения клеток ПОБИТОВО идентичны GenerateColonyWindow(…, originX, originY, w, h)
    /// для того же окна; при origin, выровненном кратно 64, идентичны и GenerateChunk
    /// (узлы сетки совпадают с чанковыми). Вне окна [originX..originX+width)×
    /// [originY..originY+height) CellAt прозрачно падает в полный путь (как
    /// WorldCellQuery.CellAt) — значения те же, что у одиночных запросов.
    ///
    /// Структура потокобезопасна для параллельного чтения после создания:
    /// сетка иммутабельна, SampleCell/LerpClimate1D не имеют общего мутабельного
    /// состояния (ThreadStatic-кэши внутри).
    /// </summary>
    public static BulkCellSampler BeginBulkSample(ulong seed, uint ver, long originX, long originY, int width, int height)
        => new BulkCellSampler(seed, ver, originX, originY, width, height);

    /// <summary>
    /// Перегрузка с ЯВНЫМ снапшотом WorldOrigin и капом параллелизма сетки.
    /// Вызывает её WorldModel: origin снимается на момент запроса, чтобы окно
    /// отвечало про тот же мир, даже если WorldOrigin сменится в другой нити
    /// между конфигурацией и семплом. gridOptions = null — без капа (как раньше).
    /// </summary>
    public static BulkCellSampler BeginBulkSample(
        ulong seed, uint ver, long originX, long originY, int width, int height,
        long worldOriginX, long worldOriginY,
        ParallelOptions gridOptions)
        => new BulkCellSampler(seed, ver, originX, originY, width, height, worldOriginX, worldOriginY, gridOptions);

    // ------------------------------------------------------------------------------------
    // Решётка 16×16 по АБСОЛЮТНЫМ координатам (форма мира — WorldModel.SampleLatticeAbsolute)
    // ------------------------------------------------------------------------------------

    /// <summary>Кэш последнего 16×16-блока решётки для одного потока (4 узла).</summary>
    private sealed class LatticeBlock
    {
        public ulong Seed;
        public uint Ver;
        public long BX;
        public long BY;
        public WorldClimateParams NW;
        public WorldClimateParams NE;
        public WorldClimateParams SW;
        public WorldClimateParams SE;
    }

    [ThreadStatic] private static LatticeBlock _latticeBlock;

    /// <summary>
    /// Клетка в ФОРМЕ МИРА: решётка 16×16 по АБСОЛЮТНЫМ координатам (узлы кратно 16 —
    /// та же решётка, что у GenerateChunk/GenerateColonyWindow/BulkCellSampler), билинейный
    /// климат, затем SampleCell(override). Одиночная клетка обязана отвечать ТОЧНО так же,
    /// как регион, — иначе дверь (WorldModel) расходится с рендером регионом у берегов
    /// (наблюдалось: 1049/7622 клеток). ThreadStatic-кэш последнего блока: последовательные
    /// запросы из одного блока 16×16 не пересемплируют 4 узла.
    /// </summary>
    public static GeneratedCell SampleLatticeAbsolute(ulong seed, uint ver, long wx, long wy)
    {
        long bx = wx & ~15L;
        long by = wy & ~15L;
        uint u = (uint)((wx - bx) * 65535 / 16);
        uint v = (uint)((wy - by) * 65535 / 16);

        LatticeBlock block = _latticeBlock ??= new LatticeBlock();
        if (block.Seed != seed || block.Ver != ver || block.BX != bx || block.BY != by)
        {
            block.NW = WorldClimateGenerator.SampleBlendedCell(seed, ver, bx, by);
            block.NE = WorldClimateGenerator.SampleBlendedCell(seed, ver, bx + 16, by);
            block.SW = WorldClimateGenerator.SampleBlendedCell(seed, ver, bx, by + 16);
            block.SE = WorldClimateGenerator.SampleBlendedCell(seed, ver, bx + 16, by + 16);
            block.Seed = seed;
            block.Ver = ver;
            block.BX = bx;
            block.BY = by;
        }

        WorldClimateParams top = UnifiedWorldGenerator.LerpClimate1D(in block.NW, in block.NE, u);
        WorldClimateParams bot = UnifiedWorldGenerator.LerpClimate1D(in block.SW, in block.SE, u);
        WorldClimateParams climate = UnifiedWorldGenerator.LerpClimate1D(in top, in bot, v);
        return UnifiedWorldGenerator.SampleCell(seed, ver, wx, wy, climate);
    }
}

/// <summary>
/// Лёгкий сэмплер прямоугольника мира: сетка климата (width/16+1)×(height/16+1) узлов
/// через WorldClimateGenerator.SampleBlendedCell, затем дешёвый билинейный климат
/// на клетку + SampleCell(override). Создаётся через
/// <see cref="WorldCellQuery.BeginBulkSample"/>.
/// </summary>
public readonly struct BulkCellSampler
{
    private const int SubSize = 16;
    private const int Q16One = 65535;

    private readonly ulong _seed;
    private readonly uint _ver;
    /// <summary>Origin окна в координатах, относительных WorldOrigin (как у CellAt).</summary>
    private readonly long _originX;
    private readonly long _originY;
    /// <summary>Снапшот WorldOrigin на момент Begin (fallback-путь вне окна).</summary>
    private readonly long _worldOriginX;
    private readonly long _worldOriginY;
    private readonly int _width;
    private readonly int _height;
    private readonly int _subDivX;
    private readonly int _subDivY;
    /// <summary>Сетка климата, row-major по gy: idx = gy*(SubDivX+1) + gx.</summary>
    private readonly WorldClimateParams[] _grid;

    internal BulkCellSampler(ulong seed, uint ver, long originX, long originY, int width, int height)
        : this(seed, ver, originX, originY, width, height,
              UnifiedWorldGenerator.WorldOriginX, UnifiedWorldGenerator.WorldOriginY,
              gridOptions: null)
    {
    }

    /// <summary>
    /// Полный конструктор: ЯВНЫЙ снапшот WorldOrigin (как его снял вызывающий —
    /// WorldModel) и опциональный кап параллелизма сетки (анти-голодание
    /// ThreadPool при пересборке окна на лету).
    /// </summary>
    internal BulkCellSampler(
        ulong seed, uint ver, long originX, long originY, int width, int height,
        long worldOriginX, long worldOriginY,
        ParallelOptions gridOptions)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(width <= 0 ? nameof(width) : nameof(height),
                "Размеры окна bulk-семпла должны быть больше 0.");

        _seed = seed;
        _ver = ver;
        _originX = originX;
        _originY = originY;
        _worldOriginX = worldOriginX;
        _worldOriginY = worldOriginY;
        _width = width;
        _height = height;
        _subDivX = Math.Max(1, width / SubSize);
        _subDivY = Math.Max(1, height / SubSize);

        long baseOriginX = _worldOriginX + originX;
        long baseOriginY = _worldOriginY + originY;

        int gridW = _subDivX + 1;
        int gridH = _subDivY + 1;
        var grid = new WorldClimateParams[gridW * gridH];

        // Узлы сетки — как в GenerateColonyWindow (Parallel.For для больших окон).
        // parallelOptions == null допустим по контракту двери («без капа») — .NET
        // требует не-null, поэтому null переключает на перегрузку по умолчанию.
        int nodeCount = gridW * gridH;
        if (nodeCount >= 64)
        {
            Action<int> fillRow = gy =>
            {
                long gwy = baseOriginY + gy * SubSize;
                int row = gy * gridW;
                for (int gx = 0; gx < gridW; gx++)
                {
                    long gwx = baseOriginX + gx * SubSize;
                    grid[row + gx] = WorldClimateGenerator.SampleBlendedCell(seed, ver, gwx, gwy);
                }
            };
            if (gridOptions != null)
                Parallel.For(0, gridH, gridOptions, fillRow);
            else
                Parallel.For(0, gridH, fillRow);
        }
        else
        {
            for (int gy = 0; gy < gridH; gy++)
            {
                long gwy = baseOriginY + gy * SubSize;
                int row = gy * gridW;
                for (int gx = 0; gx < gridW; gx++)
                {
                    long gwx = baseOriginX + gx * SubSize;
                    grid[row + gx] = WorldClimateGenerator.SampleBlendedCell(seed, ver, gwx, gwy);
                }
            }
        }

        _grid = grid;
    }

    /// <summary>Ширина окна сэмплирования (клетки).</summary>
    public int Width => _width;

    /// <summary>Высота окна сэмплирования (клетки).</summary>
    public int Height => _height;

    /// <summary>Origin окна в координатах, относительных WorldOrigin (как у CellAt).</summary>
    public long OriginX => _originX;

    /// <summary>Origin окна в координатах, относительных WorldOrigin (как у CellAt).</summary>
    public long OriginY => _originY;

    /// <summary>
    /// Клетка по координатам в пространстве CellAt (относительно WorldOrigin).
    /// Внутри окна — быстрый сеточный путь (побитово = GenerateColonyWindow);
    /// вне окна — полный путь (как WorldCellQuery.CellAt). Потокобезопасен.
    /// </summary>
    public GeneratedCell CellAt(long x, long y)
    {
        long lx = x - _originX;
        long ly = y - _originY;
        if ((ulong)lx >= (ulong)_width || (ulong)ly >= (ulong)_height)
            return UnifiedWorldGenerator.SampleCell(_seed, _ver, _worldOriginX + x, _worldOriginY + y);
        return CellAtLocal((int)lx, (int)ly);
    }

    /// <summary>
    /// Клетка по ЛОКАЛЬНЫМ координатам окна [0..width)×[0..height) — самый быстрый путь
    /// для циклов по прямоугольнику. Потокобезопасен.
    /// </summary>
    public GeneratedCell CellAtLocal(int lx, int ly)
    {
        int gridW = _subDivX + 1;
        int bx = Math.Min(_subDivX - 1, lx / SubSize);
        int by = Math.Min(_subDivY - 1, ly / SubSize);
        uint u = (uint)(((long)(lx % SubSize)) * Q16One / SubSize);
        uint v = (uint)(((long)(ly % SubSize)) * Q16One / SubSize);

        int row = by * gridW;
        int nextRow = (by + 1) * gridW;

        WorldClimateParams nw = _grid[row + bx];
        WorldClimateParams ne = _grid[row + bx + 1];
        WorldClimateParams sw = _grid[nextRow + bx];
        WorldClimateParams se = _grid[nextRow + bx + 1];

        WorldClimateParams top = UnifiedWorldGenerator.LerpClimate1D(in nw, in ne, u);
        WorldClimateParams bot = UnifiedWorldGenerator.LerpClimate1D(in sw, in se, u);
        WorldClimateParams cellClimate = UnifiedWorldGenerator.LerpClimate1D(in top, in bot, v);

        long wx = _worldOriginX + _originX + lx;
        long wy = _worldOriginY + _originY + ly;

        return UnifiedWorldGenerator.SampleCell(_seed, _ver, wx, wy, cellClimate);
    }
}
