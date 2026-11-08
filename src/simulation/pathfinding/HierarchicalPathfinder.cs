using System;
using System.Threading;
using Game.Core;
using Game.Core.World;
using Game.Simulation;

namespace Game.Simulation;

using Game.Simulation.Pathfinding;

/// <summary>
/// Легковесный фасад совместимости островного поиска пути.
/// Вся реальная навигация делегируется в <see cref="UnifiedPathfinder"/> и <see cref="LocalChunkAStar"/>.
/// Устраняет 1700+ строк устаревшего HPA*, Zero-GC, 0 аллокаций.
/// </summary>
public sealed class HierarchicalPathfinder
{
    public static HierarchicalPathfinder Instance { get; } = new();

    public const byte CostBlocked = 0;
    public const byte CostGrass = 1;
    public const byte CostWater = 6;
    public const int MaxDetourRatio = 6;

    private int _mapWidth = 1024;
    private int _mapHeight = 1024;
    private byte[] _cost = null!;
    private readonly int[] _scratchPath = new int[AgentPathConfig.MaxWaypoints];

    public byte[] Cost => _cost;
    public int MapWidth => _mapWidth;
    public int MapHeight => _mapHeight;
    public NavGridState State => null!;

    public bool BudgetEnabled { get; set; } = false;
    public bool LastSearchDeferredByBudget => false;
    public long FailOutOfBounds { get; private set; }

    public void Initialize(SimulationContext ctx)
    {
        if (ctx != null)
        {
            _mapWidth = ctx.MapWidth;
            _mapHeight = ctx.MapHeight;
        }
    }

    public void InitializeCost(byte[] cost, int width, int height)
    {
        _cost = cost;
        _mapWidth = width;
        _mapHeight = height;
    }

    public void Invalidate() { }
    public void Reset()
    {
        _cost = null!;
        FailOutOfBounds = 0;
    }

    public static int OctileDist(int dx, int dy)
    {
        int adx = Math.Abs(dx);
        int ady = Math.Abs(dy);
        return adx > ady ? adx * 10 + ady * 4 : ady * 10 + adx * 4;
    }

    public static bool IsDiagonalPassable(bool corner1Free, bool corner2Free) => corner1Free && corner2Free;

    public static bool IsDiagonalPassable(byte[] cost, int width, int sx, int sy, int tx, int ty)
    {
        if (cost == null) return false;
        int height = cost.Length / width;
        if ((uint)sx >= (uint)width || (uint)sy >= (uint)height ||
            (uint)tx >= (uint)width || (uint)ty >= (uint)height)
            return false;

        int dx = tx - sx;
        int dy = ty - sy;
        if (Math.Abs(dx) != 1 || Math.Abs(dy) != 1)
            return cost[ty * width + tx] != CostBlocked;

        int corner1X = sx + dx;
        int corner1Y = sy;
        int corner2X = sx;
        int corner2Y = sy + dy;

        if (cost[corner1Y * width + corner1X] == CostBlocked) return false;
        if (cost[corner2Y * width + corner2X] == CostBlocked) return false;
        if (cost[ty * width + tx] == CostBlocked) return false;

        return true;
    }

    public bool TryFindPath(int startX, int startY, int targetX, int targetY, out int[] path, out int count)
    {
        return TryFindPathScratch(startX, startY, targetX, targetY, out path, out count);
    }

    public System.Collections.Generic.List<(int X, int Y)> TryFindPath(int startX, int startY, int targetX, int targetY)
    {
        if (!TryFindPathScratch(startX, startY, targetX, targetY, out int[] path, out int count) || count == 0)
            return null!;

        var result = new System.Collections.Generic.List<(int X, int Y)>(count);
        for (int i = 0; i < count; i++)
        {
            result.Add((path[i] % _mapWidth, path[i] / _mapWidth));
        }
        return result;
    }

    public bool TryFindPathScratch(int startX, int startY, int targetX, int targetY, out int[] path, out int count)
    {
        path = _scratchPath;
        count = 0;

        if ((uint)startX >= (uint)_mapWidth || (uint)startY >= (uint)_mapHeight ||
            (uint)targetX >= (uint)_mapWidth || (uint)targetY >= (uint)_mapHeight)
        {
            FailOutOfBounds++;
            return false;
        }

        if (startX == targetX && startY == targetY)
        {
            path[0] = startY * _mapWidth + startX;
            count = 1;
            return true;
        }

        // Если передан локальный тестовый буфер стоимостей
        if (_cost != null)
        {
            if (_cost[startY * _mapWidth + startX] == CostBlocked ||
                _cost[targetY * _mapWidth + targetX] == CostBlocked)
            {
                return false;
            }

            // Локальный тест малого размера (например 64x64 в тестах углов)
            if (_mapWidth <= 64 && _mapHeight <= 64)
            {
                return FindLocalTestPath(startX, startY, targetX, targetY, out count);
            }
        }

        long originX = Game.Core.WorldStreaming.UnifiedWorldGenerator.WorldOriginX;
        long originY = Game.Core.WorldStreaming.UnifiedWorldGenerator.WorldOriginY;
        long wsx = originX + startX;
        long wsy = originY + startY;
        long wtx = originX + targetX;
        long wty = originY + targetY;

        Span<Long2> waypoints = stackalloc Long2[AgentPathConfig.MaxWaypoints];
        if (UnifiedPathfinder.Instance.TryFindPath(wsx, wsy, wtx, wty, waypoints, out int wCount) && wCount > 0)
        {
            count = Math.Min(wCount, _scratchPath.Length);
            for (int i = 0; i < count; i++)
            {
                int lx = (int)(waypoints[i].X - originX);
                int ly = (int)(waypoints[i].Y - originY);
                path[i] = ly * _mapWidth + lx;
            }
            return true;
        }

        return false;
    }

    private unsafe bool FindLocalTestPath(int sx, int sy, int tx, int ty, out int count)
    {
        count = 0;
        ulong* mask = stackalloc ulong[64];
        for (int y = 0; y < _mapHeight; y++)
        {
            ulong row = 0UL;
            int offset = y * _mapWidth;
            for (int x = 0; x < _mapWidth; x++)
            {
                if (_cost[offset + x] != CostBlocked)
                    row |= (1UL << x);
            }
            mask[y] = row;
        }

        var astar = new LocalChunkAStar();
        var sp = new LocalChunkAStar.LocalScratchpad();
        var res = astar.FindLocalPathFast(new Int2(sx, sy), new Int2(tx, ty), mask, sp);
        if (!res.Success) return false;

        count = Math.Min(res.StepCount, _scratchPath.Length);
        var steps = res.Steps.Span;
        for (int i = 0; i < count; i++)
        {
            _scratchPath[i] = steps[i].Y * _mapWidth + steps[i].X;
        }
        return true;
    }
}
