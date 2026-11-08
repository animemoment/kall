using System;
using System.Threading.Tasks;
using Game.Core.WorldStreaming;

namespace Game.Core;

/// <summary>
/// Климат для рудного пасса (task-5). Два источника:
///  1) <see cref="FromGrid"/> — готовая сетка 16×16 из
///     <c>UnifiedWorldGenerator.GenerateColonyWindow</c> (там она уже посчитана);
///  2) <see cref="Sample"/> — самостоятельный семпл сетки
///     <c>WorldClimateGenerator.SampleBlendedCell</c> (прод-путь
///     ActiveArea → MapDataShim, где готовой сетки нет).
///
/// Почему сетка, а не клетка: <c>GeneratedCell</c> НЕ хранит Aridity/RockExposure/
/// RiverDensity/Precipitation/OceanInfluence — без климат-сетки рудный пасс не смог
/// бы их достать (§7.1 _plan/ORES_GEOLOGY.md). Узлы сетки лежат на АБСОЛЮТНЫХ
/// мировых координатах (origin + g*step) — один и тот же пасс для острова и мира.
/// Чистый C#, без Godot.
/// </summary>
public sealed class OreClimateLattice
{
    public const int DefaultStep = 16;

    private readonly WorldClimateParams[] _nodes; // row-major: index = gy * Nx + gx
    private readonly int _nx;
    private readonly int _ny;
    private readonly long _originX;
    private readonly long _originY;
    private readonly int _step;

    public int Step => _step;
    public int NodeCountX => _nx;
    public int NodeCountY => _ny;
    public long OriginX => _originX;
    public long OriginY => _originY;

    private OreClimateLattice(WorldClimateParams[] nodes, int nx, int ny, long originX, long originY, int step)
    {
        _nodes = nodes;
        _nx = nx;
        _ny = ny;
        _originX = originX;
        _originY = originY;
        _step = step;
    }

    /// <summary>Взять уже посчитанную сетку климата (окно GenerateColonyWindow).</summary>
    public static OreClimateLattice FromGrid(WorldClimateParams[,] grid, long originX, long originY, int step)
    {
        if (grid == null)
            throw new ArgumentNullException(nameof(grid));
        if (step <= 0)
            step = DefaultStep;
        int gxCount = grid.GetLength(0);
        int gyCount = grid.GetLength(1);
        var nodes = new WorldClimateParams[gxCount * gyCount];
        for (int gy = 0; gy < gyCount; gy++)
        {
            int row = gy * gxCount;
            for (int gx = 0; gx < gxCount; gx++)
                nodes[row + gx] = grid[gx, gy];
        }
        return new OreClimateLattice(nodes, gxCount, gyCount, originX, originY, step);
    }

    /// <summary>
    /// Посчитать сетку климата самостоятельно. Число узлов = (width/step + 1)²,
    /// как у окна колонии (последний узел — на правой/нижней кромке).
    /// </summary>
    public static OreClimateLattice Sample(ulong worldSeed, uint generatorVersion,
        long originX, long originY, int width, int height, int step)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (step <= 0)
            step = DefaultStep;
        int nx = width / step + 1;
        int ny = height / step + 1;
        var nodes = new WorldClimateParams[nx * ny];
        // Параллельно по строкам: тот же объём, что сетка окна (4 225 тяжёлых семплов).
        Parallel.For(0, ny, gy =>
        {
            long wy = originY + (long)gy * step;
            int row = gy * nx;
            for (int gx = 0; gx < nx; gx++)
            {
                long wx = originX + (long)gx * step;
                nodes[row + gx] = WorldClimateGenerator.SampleBlendedCell(
                    worldSeed, generatorVersion, wx, wy);
            }
        });
        return new OreClimateLattice(nodes, nx, ny, originX, originY, step);
    }

    /// <summary>Ближайший узел (для полей условий типа руды). Bounds-safe — клампится.</summary>
    public WorldClimateParams Nearest(long worldX, long worldY)
    {
        int gx = NodeIndex(worldX, _originX, _nx);
        int gy = NodeIndex(worldY, _originY, _ny);
        return _nodes[gy * _nx + gx];
    }

    /// <summary>Высота клетки (Q16) — билинейно по решётке (как климат окна).</summary>
    public int Elevation(long worldX, long worldY)
    {
        int gx = LocalCell(worldX, _originX, out uint u);
        int gy = LocalCell(worldY, _originY, out uint v);
        // Клампы: координаты вне окна (кандидаты с запасом) не должны падать.
        int x0 = Math.Clamp(gx, 0, _nx - 1);
        int x1 = Math.Clamp(gx + 1, 0, _nx - 1);
        int y0 = Math.Clamp(gy, 0, _ny - 1);
        int y1 = Math.Clamp(gy + 1, 0, _ny - 1);
        uint a = _nodes[y0 * _nx + x0].ElevationQ16;
        uint b = _nodes[y0 * _nx + x1].ElevationQ16;
        uint c = _nodes[y1 * _nx + x0].ElevationQ16;
        uint d = _nodes[y1 * _nx + x1].ElevationQ16;
        uint top = LerpQ16(a, b, u);
        uint bottom = LerpQ16(c, d, u);
        return (int)LerpQ16(top, bottom, v);
    }

    /// <summary>Высота узла решётки (для slope/flat).</summary>
    public int NodeElevation(int gx, int gy)
    {
        int x = Math.Clamp(gx, 0, _nx - 1);
        int y = Math.Clamp(gy, 0, _ny - 1);
        return _nodes[y * _nx + x].ElevationQ16;
    }

    private int NodeIndex(long worldCoord, long origin, int count)
    {
        long g = LocalCell(worldCoord, origin, out uint frac);
        // Ближайший узел: frac >= половины шага — вправо.
        int shift = frac >= 32768 ? 1 : 0;
        return Math.Clamp((int)Math.Clamp(g + shift, 0, int.MaxValue), 0, count - 1);
    }

    private int LocalCell(long worldCoord, long origin, out uint frac)
    {
        long d = worldCoord - origin;
        long cell = WorldCoordinates.FloorDiv(d, _step);
        long mod = d - cell * _step; // [0, step)
        frac = (uint)(mod * 65535L / _step);
        if (cell > int.MaxValue) cell = int.MaxValue;
        if (cell < int.MinValue) cell = int.MinValue;
        return (int)cell;
    }

    private static uint LerpQ16(uint a, uint b, uint t)
    {
        long delta = (long)b - a;
        long value = a + (delta * t + (delta >= 0 ? 32768 : -32768)) / 65535L;
        return (uint)Math.Clamp(value, 0L, 65535L);
    }
}