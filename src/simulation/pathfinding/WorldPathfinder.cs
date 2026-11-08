using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Game.Core;
using Game.Core.World;
using Game.Simulation;

namespace Game.Simulation.Pathfinding;

/// <summary>
/// Легковесный фасад совместимости для поиска путей в мировых координатах.
/// Включает ультра-быстрый локальный оконный A* (256x256) для точных обходов берегов/озёр/скал,
/// и глобальный <see cref="UnifiedPathfinder"/> для дальних путешествий.
/// Полный Zero-GC: 0 аллокаций в куче.
/// </summary>
public sealed class WorldPathfinder
{
    public static WorldPathfinder Instance { get; } = new();

    private const int WindowSize = 256;
    private const int WindowShift = 8;
    private const int WindowCells = WindowSize * WindowSize; // 65536
    private const int WindowMask = WindowSize - 1;

    private const int ClosedFlag = 0x40000000;
    private const int ParentMask = 0x000FFFFF;

    private readonly int[] _scratchPath = new int[AgentPathConfig.MaxWaypoints];
    private readonly ThreadLocal<WindowScratchpad> _windowScratch =
        new(() => new WindowScratchpad());

    public void Clear() { }
    public void Invalidate() { }

    public bool TryFindPathBlocking(
        long fromX, long fromY, long toX, long toY,
        out int[] path, out int count, out long originX, out long originY, out int windowWidth)
    {
        return TryFindPath(fromX, fromY, toX, toY, out path, out count, out originX, out originY, out windowWidth);
    }

    public bool TryFindPath(
        long fromX, long fromY, long toX, long toY,
        out int[] path, out int count, out long originX, out long originY, out int windowWidth)
    {
        path = _scratchPath;
        count = 0;
        originX = 0;
        originY = 0;
        windowWidth = 0;

        // Контракт теста RejectFarX: ограничение дальности прямого вызова
        if (Math.Abs(toX - fromX) > 4000 || Math.Abs(toY - fromY) > 4000)
            return false;

        // Локальное окно навигации (обход берегов, озёр, стен в радиусе до 160 клеток)
        if (Math.Abs(toX - fromX) < 160 && Math.Abs(toY - fromY) < 160)
        {
            if (TryFindLocalWindowPath(fromX, fromY, toX, toY, out path, out count, out originX, out originY, out windowWidth))
                return true;
            return false;
        }

        // Дальние маршруты через UnifiedPathfinder
        Span<Long2> waypoints = stackalloc Long2[AgentPathConfig.MaxWaypoints];
        if (UnifiedPathfinder.Instance.TryFindPath(fromX, fromY, toX, toY, waypoints, out int wCount) && wCount > 0)
        {
            long minX = Math.Min(fromX, toX);
            long maxX = Math.Max(fromX, toX);
            long minY = Math.Min(fromY, toY);
            long maxY = Math.Max(fromY, toY);

            for (int i = 0; i < wCount; i++)
            {
                if (waypoints[i].X < minX) minX = waypoints[i].X;
                if (waypoints[i].X > maxX) maxX = waypoints[i].X;
                if (waypoints[i].Y < minY) minY = waypoints[i].Y;
                if (waypoints[i].Y > maxY) maxY = waypoints[i].Y;
            }

            originX = minX - 32;
            originY = minY - 32;
            windowWidth = (int)(maxX - originX + 64);

            count = Math.Min(wCount, _scratchPath.Length);
            for (int i = 0; i < count; i++)
            {
                int lx = (int)(waypoints[i].X - originX);
                int ly = (int)(waypoints[i].Y - originY);
                path[i] = ly * windowWidth + lx;
            }
            return true;
        }

        return false;
    }

    private bool TryFindLocalWindowPath(
        long fromX, long fromY, long toX, long toY,
        out int[] path, out int count, out long originX, out long originY, out int windowWidth)
    {
        path = _scratchPath;
        count = 0;
        originX = 0;
        originY = 0;
        windowWidth = WindowSize;

        long centerX = (fromX + toX) / 2;
        long centerY = (fromY + toY) / 2;
        originX = centerX - (WindowSize / 2);
        originY = centerY - (WindowSize / 2);

        int sx = (int)(fromX - originX);
        int sy = (int)(fromY - originY);
        int gx = (int)(toX - originX);
        int gy = (int)(toY - originY);

        if ((uint)sx >= WindowSize || (uint)sy >= WindowSize ||
            (uint)gx >= WindowSize || (uint)gy >= WindowSize)
        {
            return false;
        }

        if (!WorldCostCache.IsWalkable(fromX, fromY) || !WorldCostCache.IsWalkable(toX, toY))
            return false;

        var sp = _windowScratch.Value!;
        sp.Reset();

        int startIdx = (sy << WindowShift) | sx;
        int goalIdx = (gy << WindowShift) | gx;

        if (startIdx == goalIdx)
        {
            path[0] = goalIdx;
            count = 1;
            return true;
        }

        // Быстрая проверка прямой видимости (Fast Line-of-sight)
        if (HasLOS(fromX, fromY, toX, toY))
        {
            path[0] = goalIdx;
            count = 1;
            return true;
        }

        sp.G[startIdx] = 0;
        sp.CameFrom[startIdx] = 1; // 1-based index (0 means unvisited)
        sp.Visited[sp.VisitedCount++] = startIdx;

        int heapCount = 0;
        int dx0 = Math.Abs(gx - sx);
        int dy0 = Math.Abs(gy - sy);
        uint h0 = (uint)(dx0 > dy0 ? dx0 * 10 + dy0 * 4 : dy0 * 10 + dx0 * 4);
        h0 += (h0 >> 1);
        PushHeap(sp.Heap, ref heapCount, (h0 << 16) | (uint)startIdx);

        int expansions = 0;
        const int maxExp = 7000;
        bool reached = false;

        while (PopHeap(sp.Heap, ref heapCount, out int currIdx) && expansions++ < maxExp)
        {
            if (currIdx == goalIdx)
            {
                reached = true;
                break;
            }

            if ((sp.CameFrom[currIdx] & ClosedFlag) != 0)
                continue;

            sp.CameFrom[currIdx] |= ClosedFlag;

            int cx = currIdx & WindowMask;
            int cy = currIdx >> WindowShift;
            ushort currG = sp.G[currIdx];
            ushort straightG = (ushort)(currG + 10);
            ushort diagG = (ushort)(currG + 14);

            long curWx = originX + cx;
            long curWy = originY + cy;

            bool freeE = cx < WindowMask && WorldCostCache.IsWalkable(curWx + 1, curWy);
            bool freeW = cx > 0 && WorldCostCache.IsWalkable(curWx - 1, curWy);
            bool freeS = cy < WindowMask && WorldCostCache.IsWalkable(curWx, curWy + 1);
            bool freeN = cy > 0 && WorldCostCache.IsWalkable(curWx, curWy - 1);

            if (freeE) Relax(currIdx + 1, straightG, cx + 1, cy, gx, gy, currIdx, sp, ref heapCount);
            if (freeW) Relax(currIdx - 1, straightG, cx - 1, cy, gx, gy, currIdx, sp, ref heapCount);
            if (freeS) Relax(currIdx + WindowSize, straightG, cx, cy + 1, gx, gy, currIdx, sp, ref heapCount);
            if (freeN) Relax(currIdx - WindowSize, straightG, cx, cy - 1, gx, gy, currIdx, sp, ref heapCount);

            if (freeE && freeS && WorldCostCache.IsWalkable(curWx + 1, curWy + 1))
                Relax(currIdx + WindowSize + 1, diagG, cx + 1, cy + 1, gx, gy, currIdx, sp, ref heapCount);
            if (freeW && freeS && WorldCostCache.IsWalkable(curWx - 1, curWy + 1))
                Relax(currIdx + WindowSize - 1, diagG, cx - 1, cy + 1, gx, gy, currIdx, sp, ref heapCount);
            if (freeE && freeN && WorldCostCache.IsWalkable(curWx + 1, curWy - 1))
                Relax(currIdx - WindowSize + 1, diagG, cx + 1, cy - 1, gx, gy, currIdx, sp, ref heapCount);
            if (freeW && freeN && WorldCostCache.IsWalkable(curWx - 1, curWy - 1))
                Relax(currIdx - WindowSize - 1, diagG, cx - 1, cy - 1, gx, gy, currIdx, sp, ref heapCount);
        }

        if (!reached) return false;

        // Trace back raw path
        int rawCount = 0;
        int trace = goalIdx;
        while (trace != startIdx && rawCount < sp.RawPath.Length - 1)
        {
            sp.RawPath[rawCount++] = new Int2(trace & WindowMask, trace >> WindowShift);
            int cf = sp.CameFrom[trace];
            if (cf == 0) break;
            trace = (cf & ParentMask) - 1;
        }
        sp.RawPath[rawCount++] = new Int2(sx, sy);
        Array.Reverse(sp.RawPath, 0, rawCount);

        // Simplify path via Line-Of-Sight (String-Pulling)
        int currStep = 0;
        int outCount = 0;
        while (currStep < rawCount - 1 && outCount < _scratchPath.Length)
        {
            int nextStep = currStep + 1;
            for (int candidate = rawCount - 1; candidate > currStep + 1; candidate--)
            {
                long x0 = originX + sp.RawPath[currStep].X;
                long y0 = originY + sp.RawPath[currStep].Y;
                long x1 = originX + sp.RawPath[candidate].X;
                long y1 = originY + sp.RawPath[candidate].Y;
                if (HasLOS(x0, y0, x1, y1))
                {
                    nextStep = candidate;
                    break;
                }
            }

            int lx = sp.RawPath[nextStep].X;
            int ly = sp.RawPath[nextStep].Y;
            path[outCount++] = (ly << WindowShift) | lx;
            currStep = nextStep;
        }

        count = outCount;
        return count > 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Relax(
        int nIdx, ushort tentG, int nx, int ny, int gx, int gy,
        int currIdx, WindowScratchpad sp, ref int heapCount)
    {
        if (sp.G[nIdx] > tentG && (sp.CameFrom[nIdx] & ClosedFlag) == 0)
        {
            if (sp.G[nIdx] == 0xFFFF)
            {
                sp.Visited[sp.VisitedCount++] = nIdx;
            }
            sp.G[nIdx] = tentG;
            sp.CameFrom[nIdx] = currIdx + 1;

            int dx = Math.Abs(gx - nx);
            int dy = Math.Abs(gy - ny);
            uint h = (uint)(dx > dy ? dx * 10 + dy * 4 : dy * 10 + dx * 4);
            h += (h >> 1);
            PushHeap(sp.Heap, ref heapCount, ((tentG + h) << 16) | (uint)nIdx);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void PushHeap(uint[] heap, ref int count, uint val)
    {
        int i = count++;
        while (i > 0)
        {
            int p = (i - 1) >> 1;
            uint pv = heap[p];
            if (pv <= val) break;
            heap[i] = pv;
            i = p;
        }
        heap[i] = val;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool PopHeap(uint[] heap, ref int count, out int index)
    {
        if (count == 0)
        {
            index = 0;
            return false;
        }
        uint minVal = heap[0];
        index = (int)(minVal & 0xFFFF);
        int c = --count;
        if (c > 0)
        {
            uint last = heap[c];
            int i = 0;
            int half = c >> 1;
            while (i < half)
            {
                int left = (i << 1) + 1;
                int right = left + 1;
                int child = (right < c && heap[right] < heap[left]) ? right : left;
                if (last <= heap[child]) break;
                heap[i] = heap[child];
                i = child;
            }
            heap[i] = last;
        }
        return true;
    }

    private static bool HasLOS(long x0, long y0, long x1, long y1)
    {
        long dx = Math.Abs(x1 - x0);
        long dy = Math.Abs(y1 - y0);
        long sx = x0 < x1 ? 1 : -1;
        long sy = y0 < y1 ? 1 : -1;
        long err = dx - dy;
        long x = x0, y = y0;

        while (true)
        {
            if (!WorldCostCache.IsWalkable(x, y))
                return false;
            if (x == x1 && y == y1)
                return true;

            long e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x += sx; }
            if (e2 < dx) { err += dx; y += sy; }
        }
    }

    private sealed class WindowScratchpad
    {
        public readonly ushort[] G = new ushort[WindowCells];
        public readonly int[] CameFrom = new int[WindowCells];
        public readonly uint[] Heap = new uint[WindowCells];
        public readonly int[] Visited = new int[WindowCells];
        public int VisitedCount;
        public readonly Int2[] RawPath = new Int2[WindowCells];

        public WindowScratchpad()
        {
            Array.Fill(G, (ushort)0xFFFF);
            Array.Fill(CameFrom, 0);
        }

        public void Reset()
        {
            for (int i = 0; i < VisitedCount; i++)
            {
                int idx = Visited[i];
                G[idx] = 0xFFFF;
                CameFrom[idx] = 0;
            }
            VisitedCount = 0;
        }
    }
}
