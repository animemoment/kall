using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Game.Simulation.Pathfinding;

/// <summary>
/// Источник проходимости для портал-графа. В игре реализуется поверх
/// <see cref="Game.Core.World.WorldCostCache"/>; в стенде — поверх мока.
/// </summary>
public unsafe interface IPassabilitySource
{
    bool IsWalkable(long x, long y);
    /// <summary>Заполнить 64-строчную маску чанка (bit 1 = свободно).</summary>
    void FillChunkMask(long cx, long cy, ulong* mask64);
}

/// <summary>
/// HPA*-слой: портал-граф по чанкам 64×64.
///
/// Портал = непрерывный прогон клеток на границе чанка, свободных с ОБЕИХ сторон.
/// Такой прогон — единственная возможность перейти из чанка в соседний, поэтому
/// граф полный и точный по топологии. Двери (проёмы) попадают в порталы
/// автоматически: это просто свободная клетка в стене.
///
/// Стоимость ребра «внутри чанка» считается НАСТОЯЩИМ LocalChunkAStar (с кэшем),
/// а не евклидовой оценкой — иначе путь «ломается» о внутренние стены.
///
/// Результат: список портальных клеток (по одной на прогон) + цель. Плотную
/// доработку внутри чанков делает вызывающий (см. P3.1).
/// </summary>
public sealed class ChunkPortalGraph
{
    public const int ChunkShift = 6;
    public const int ChunkSize = 1 << ChunkShift;   // 64
    public const int ChunkMask = ChunkSize - 1;
    private const int MaxCachedChunks = 65536; // v1: без вытеснения (P3.1 — LRU); 10к-карта = 24 649 чанков
    // На шумной карте одна граница чанка может дать десятки прогонов; без потолка
    // матрица внутричанковых расстояний и поиск раздуваются. 12 на сторону —
    // компромисс: реальные проходы (двери/разрывы стен) укладываются с запасом.
    private const int MaxRunsPerSide = 12;
    private const int NoPath = int.MaxValue;
    private const int NoRun = -1;

    private readonly IPassabilitySource _source;

    private sealed class ChunkPortals
    {
        public long Key;
        public int Cx, Cy;
        public Run[] Runs = Array.Empty<Run>();
        public short[,] Intra = new short[0, 0]; // [runs,runs] стоимость или -1
        public ulong[] Mask = new ulong[ChunkSize];
        public bool MaskFilled;
    }

    private struct Run
    {
        public byte Side;   // 0=N(-y),1=S(+y),2=W(-x),3=E(+x)
        public byte Start;  // координата вдоль границы (x для N/S, y для W/E)
        public byte Len;
        public int NodeId;  // глобальный id портала
        public short RepX;  // клетка-представитель внутри чанка (для локального A*)
        public short RepY;
        public short MidX;  // середина прогона на границе (для waypoint)
        public short MidY;
    }

    private readonly Dictionary<long, ChunkPortals> _chunks = new(1024);
    private readonly List<long> _chunkLru = new(1024);
    private readonly List<(int Cx, int Cy)> _nodePos = new(4096); // nodeId -> чанк
    private readonly List<int> _nodeRun = new(4096);              // nodeId -> индекс прогона

    // Поиск: массивы растут по мере появления порталов.
    private int[] _g = new int[4096];
    private int[] _parent = new int[4096];
    private int[] _stamp = new int[4096];   // == _searchStamp: узел ЗАКРЫТ
    private int[] _seen = new int[4096];    // == _searchStamp: узел в очереди/уже видели
    private int _searchStamp;

    // P3.2 математика: goal-directed эвристика. Для текущего запроса считаем
    // точный «хвост» от каждого целевого портала до цели (goalTail) и используем
    // h(n) = min_p ( octile(n → p) + tail(p) ). Это допустимая (admissible) эвристика:
    // octile — нижняя граница пути до портала, tail — точная стоимость после него.
    // Слабая «октиль до цели» давала ~673 раскрытия; min по порталам режет их заметно.
    private const int MaxGoalPortals = 64;
    private readonly int[] _goalNodeIds = new int[MaxGoalPortals];
    private readonly long[] _goalMidX = new long[MaxGoalPortals];
    private readonly long[] _goalMidY = new long[MaxGoalPortals];
    private readonly int[] _goalTail = new int[MaxGoalPortals];
    private int _goalPortalCount;

    private readonly LocalChunkAStar _astar = new();
    private readonly LocalChunkAStar.LocalScratchpad _scratch = new();
    private long[] _openHeap = new long[4096];
    private int _openCount;

    public int CachedChunks => _chunks.Count;
    public int PortalCount => _nodePos.Count;

    /// <summary>
    /// Кэш ЦЕЛОГО маршрута: (чанк старта → чанк цели) ⇒ последовательность порталов + версия.
    /// Раньше кэш был по-хоповый (113 словарных проб на путь ≈ 85 мкс); целый маршрут —
    /// одна проба + копирование ≈ 1–2 мкс. Ключ не зависит от точной клетки старта:
    /// агент идёт по кэшу из своего чанка, поэтому маршрут общий для всего чанка.
    /// </summary>
    private sealed class RouteEntry
    {
        public int Version;
        public long[] Xs = Array.Empty<long>();
        public long[] Ys = Array.Empty<long>();
        public int Count;
    }

    private readonly Dictionary<long, RouteEntry> _routeCache = new(1 << 13);
    private const int MaxRouteCache = 8192;
    private int _version; // растёт при изменении проходимости; записи кэша стареют

    /// <summary>Версия мира: записи кэша, сделанные до изменения, считаются промахом.</summary>
    public int Version => _version;

    public long HopCacheHits { get; private set; }
    public long HopCacheMisses { get; private set; }

    /// <summary>
    /// Изменилась проходимость клетки (постройка/снос стены, смена мира).
    /// Ленивая инвалидация: помечаем «грязными» сам чанк и 8 соседей (их граничные
    /// прогоны зависят от соседних клеток) и поднимаем версию кэша маршрутов.
    /// Стоимость O(1); перестройка чанков — только при следующем обращении.
    /// </summary>
    public void NoteCellChanged(long wx, long wy)
    {
        _version++;
        int cx = (int)(wx >> ChunkShift);
        int cy = (int)(wy >> ChunkShift);
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                _chunks.Remove(PackChunk(cx + dx, cy + dy));
        // Кэш маршрутов не трогаем: записи сравнят версию и станут промахами.
    }

    /// <summary>Полная инвалидация (смена мира/колонии, массовое изменение стен).</summary>
    public void InvalidateAll()
    {
        _chunks.Clear();
        _routeCache.Clear();
        _nodePos.Clear();
        _nodeRun.Clear();
        _version++;
    }

    /// <summary>Сбросить только кэш маршрутов (например, после сноса/постройки стен).</summary>
    public void ClearHopCache() => _routeCache.Clear();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long HopKey(int fromCx, int fromCy, int goalCx, int goalCy)
        => ((long)(uint)fromCx << 48) | ((long)(uint)fromCy << 32) | ((long)(uint)goalCx << 16) | (uint)goalCy;

    public ChunkPortalGraph(IPassabilitySource source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long PackChunk(int cx, int cy) => ((long)cx << 32) ^ (uint)cy;

    /// <summary>
    /// Найти макро-путь. Возвращает число waypoint-ов (0 = не найдено).
    /// outXs/outYs — буферы равной длины; пишется стартовая клетка, порталы, цель.
    /// </summary>
    public unsafe int TryFindPortalPath(
        long fromX, long fromY, long toX, long toY,
        Span<long> outXs, Span<long> outYs,
        int maxExpansions = 65536)
    {
        if (outXs.Length == 0 || outXs.Length != outYs.Length)
            return 0;

        int scx = (int)(fromX >> ChunkShift), scy = (int)(fromY >> ChunkShift);
        int tcx = (int)(toX >> ChunkShift), tcy = (int)(toY >> ChunkShift);
        if (scx == tcx && scy == tcy)
            return 0; // локальный случай — вызывающий использует LocalChunkAStar

        ChunkPortals startChunk = GetChunk(scx, scy);
        ChunkPortals goalChunk = GetChunk(tcx, tcy);
        if (startChunk.Runs.Length == 0 || goalChunk.Runs.Length == 0)
            return 0;

        // P3.1: тёплый путь — только чтения по кэшу «следующий портал».
        if (TryWarmPath(scx, scy, tcx, tcy, fromX, fromY, toX, toY, outXs, outYs, out int warmW))
        {
            LastExpansions = 0;
            LastWaypoints = warmW;
            HopCacheHits++;
            return warmW;
        }
        HopCacheMisses++;

        BeginSearch();

        // Стартовые порталы: расстояние от старта до портала внутри стартового чанка.
        int startRunCount = startChunk.Runs.Length;
        for (int r = 0; r < startRunCount; r++)
            if (TryIntraDistance(startChunk, -1, StartCellX(fromX, scx), StartCellY(fromY, scy), r, out int cost))
                Relax(startChunk.Runs[r].NodeId, cost, -1, toX, toY);

        int expansions = 0;
        int bestNode = -1;
        int bestTotal = NoPath;
        bool greedyValid = false;
        int greedyCount = 0;

        // Жадный incumbent: дешёвая верхняя граница ДО A*. Без неё pruning по
        // bestTotal не работал (граница появлялась только к концу поиска).
        if (TryGreedyBound(scx, scy, tcx, tcy, fromX, fromY, toX, toY, outXs, outYs, out int gTotal, out int gCount))
        {
            bestTotal = gTotal;
            greedyValid = true;
            greedyCount = gCount;
        }

        // Считаем tailcost для целевых порталов (один раз на запрос).
        _goalPortalCount = 0;
        int goalLocalX = (int)(toX - ((long)tcx << ChunkShift));
        int goalLocalY = (int)(toY - ((long)tcy << ChunkShift));
        for (int r = 0; r < goalChunk.Runs.Length && _goalPortalCount < MaxGoalPortals; r++)
        {
            if (!TryIntraDistance(goalChunk, r, goalLocalX, goalLocalY, -1, out int tail))
                continue;
            var run = goalChunk.Runs[r];
            _goalNodeIds[_goalPortalCount] = run.NodeId;
            _goalMidX[_goalPortalCount] = ((long)tcx << ChunkShift) + run.MidX;
            _goalMidY[_goalPortalCount] = ((long)tcy << ChunkShift) + run.MidY;
            _goalTail[_goalPortalCount] = tail;
            _goalPortalCount++;
        }

        while (_openCount > 0 && expansions++ < maxExpansions)
        {
            int node = PopOpen(out int f);
            // Optimum найден, если нижняя граница f уже не лучше текущего лучшего пути.
            // Выходим только если f СТРОГО хуже incumbent: равенство может дать
            // корректный путь через целевой чанк (иначе отдавали жадный fallback и ratio рос).
            if (bestTotal != NoPath && f > bestTotal) break;
            int st = Stamp(node);
            if (st == _searchStamp) continue;
            _stamp[node] = _searchStamp;

            var (ncx, ncy) = _nodePos[node];
            // Если это портал целевого чанка — можно завершать: добавим финальный отрезок.
            if (ncx == tcx && ncy == tcy)
            {
                var chunk = GetChunk(ncx, ncy);
                int runIdx = _nodeRun[node];
                if (TryIntraDistance(chunk, runIdx, (int)(toX - ((long)tcx << ChunkShift)), (int)(toY - ((long)tcy << ChunkShift)), -1, out int tail))
                {
                    int total = _g[node] + tail;
                    if (total <= bestTotal)
                    {
                        bestTotal = total;
                        bestNode = node;
                    }
                }
            }

            Expand(node, ncx, ncy, toX, toY, bestTotal);
        }

        if (bestNode < 0)
        {
            // A* не нашёл лучше жадного — отдаём жадный маршрут и КЭШИРУЕМ его
            // (иначе тёплый проход для таких пар каждый раз пересчитывал бы заново).
            if (greedyValid && greedyCount >= 2)
            {
                SaveRouteFromSpan(outXs, outYs, greedyCount, scx, scy, tcx, tcy);
                LastExpansions = expansions;
                LastWaypoints = greedyCount;
                return greedyCount;
            }
            LastExpansions = expansions;
            LastWaypoints = 0;
            return 0;
        }
        int w = Reconstruct(bestNode, fromX, fromY, toX, toY, outXs, outYs);
        FillRouteCache(bestNode, scx, scy, tcx, tcy);
        LastExpansions = expansions;
        LastWaypoints = w;
        return w;
    }

    private static int StartCellX(long fromX, int scx) => (int)(fromX - ((long)scx << ChunkShift));
    private static int StartCellY(long fromY, int scy) => (int)(fromY - ((long)scy << ChunkShift));

    /// <summary>
    /// Жадный маршрут-верхняя-граница: на каждом чанке выбираем прогон, минимизирующий
    /// «стоимость дойти до него + октиль до цели». Даёт incumbent для A*-pruning.
    /// Пишет готовые waypoint-ы в outXs/outYs (если A* не найдёт лучше — вернём их).
    /// </summary>
    private bool TryGreedyBound(
        int scx, int scy, int tcx, int tcy,
        long fromX, long fromY, long toX, long toY,
        Span<long> outXs, Span<long> outYs, out int total, out int count)
    {
        total = 0;
        count = 0;
        if (outXs.Length < 6) return false;

        int curCx = scx, curCy = scy;
        int curRun = NoRun;
        int startLX = (int)(fromX - ((long)scx << ChunkShift));
        int startLY = (int)(fromY - ((long)scy << ChunkShift));
        int w = 0;
        outXs[w] = fromX; outYs[w] = fromY; w++;

        for (int guard = 0; guard < 1024; guard++)
        {
            var chunk = GetChunk(curCx, curCy);
            if (chunk.Runs.Length == 0) return false;

            int bestR = NoRun;
            int bestEst = int.MaxValue;
            int bestCost = 0;
            for (int r = 0; r < chunk.Runs.Length; r++)
            {
                if (r == curRun) continue;
                int legCost;
                bool ok = curRun < 0
                    ? TryIntraDistance(chunk, -1, startLX, startLY, r, out legCost)
                    : TryIntraDistance(chunk, curRun, 0, 0, r, out legCost);
                if (!ok) continue;
                long mx = ((long)curCx << ChunkShift) + chunk.Runs[r].MidX;
                long my = ((long)curCy << ChunkShift) + chunk.Runs[r].MidY;
                int est = legCost + Octile(mx, my, toX, toY);
                if (est < bestEst) { bestEst = est; bestR = r; bestCost = legCost; }
            }
            if (bestR == NoRun) return false;

            total += bestCost;
            var run = chunk.Runs[bestR];

            if (curCx == tcx && curCy == tcy)
            {
                int glx = (int)(toX - ((long)tcx << ChunkShift));
                int gly = (int)(toY - ((long)tcy << ChunkShift));
                if (!TryIntraDistance(chunk, bestR, glx, gly, -1, out int tail)) return false;
                total += tail;
                if (w + 2 > outXs.Length) return false;
                outXs[w] = ((long)curCx << ChunkShift) + run.MidX;
                outYs[w] = ((long)curCy << ChunkShift) + run.MidY;
                w++;
                outXs[w] = toX; outYs[w] = toY; w++;
                count = Compress(outXs, outYs, w);
                return count >= 2 && total > 0;
            }

            if (w + 3 > outXs.Length) return false;
            outXs[w] = ((long)curCx << ChunkShift) + run.MidX;
            outYs[w] = ((long)curCy << ChunkShift) + run.MidY;
            w++;

            int side = run.Side;
            int ncx = curCx + (side == 3 ? 1 : side == 2 ? -1 : 0);
            int ncy = curCy + (side == 1 ? 1 : side == 0 ? -1 : 0);
            int opposite = side == 0 ? 1 : side == 1 ? 0 : side == 2 ? 3 : 2;
            var neighbor = GetChunk(ncx, ncy);
            int match = FindRun(neighbor, opposite, run.Start);
            if (match == NoRun) return false;
            curCx = ncx; curCy = ncy; curRun = match;
        }
        return false;
    }

    private void Expand(int node, int ncx, int ncy, long toX, long toY, int bestBound)
    {
        var chunk = GetChunk(ncx, ncy);
        int runIdx = _nodeRun[node];
        int baseG = _g[node];

        // 1. Соседние прогоны того же чанка — точная внутричанковая стоимость.
        for (int r = 0; r < chunk.Runs.Length; r++)
        {
            if (r == runIdx) continue;
            if (!TryIntraDistance(chunk, runIdx, chunk.Runs[runIdx].RepX, chunk.Runs[runIdx].RepY, r, out int cost))
                continue;
            int ng = baseG + cost;
            // Pruning по incumbent: если даже оптимистичная оценка через этот прогон
            // не лучше текущего лучшего пути — ветку не раскрываем вообще.
            if (bestBound != NoPath)
            {
                long mx = ((long)ncx << ChunkShift) + chunk.Runs[r].MidX;
                long my = ((long)ncy << ChunkShift) + chunk.Runs[r].MidY;
                if (ng + Heuristic(mx, my, toX, toY) > bestBound)
                    continue;
            }
            Relax(chunk.Runs[r].NodeId, ng, node, toX, toY);
        }

        // 2. Тот же прогон в соседнем чанке через границу (стоимость 0).
        int side = chunk.Runs[runIdx].Side;
        int n2cx = ncx + (side == 3 ? 1 : side == 2 ? -1 : 0);
        int n2cy = ncy + (side == 1 ? 1 : side == 0 ? -1 : 0);
        ChunkPortals neighbor = GetChunk(n2cx, n2cy);
        int opposite = side == 0 ? 1 : side == 1 ? 0 : side == 2 ? 3 : 2;
        int match = FindRun(neighbor, opposite, chunk.Runs[runIdx].Start);
        if (match != NoRun)
        {
            if (bestBound == NoPath || baseG + Heuristic(
                    ((long)n2cx << ChunkShift) + neighbor.Runs[match].MidX,
                    ((long)n2cy << ChunkShift) + neighbor.Runs[match].MidY, toX, toY) < bestBound)
            {
                Relax(neighbor.Runs[match].NodeId, baseG, node, toX, toY);
            }
        }
    }

    private static int FindRun(ChunkPortals chunk, int side, int start)
    {
        for (int i = 0; i < chunk.Runs.Length; i++)
            if (chunk.Runs[i].Side == side && chunk.Runs[i].Start == start)
                return i;
        return NoRun;
    }

    /// <summary>
    /// Стоимость внутри чанка: от прогона runA (или от клетки, если runA = -1) до
    /// прогона runB (или до клетки cellX/cellY, если runB = -1).
    /// </summary>
    private unsafe bool TryIntraDistance(ChunkPortals chunk, int runA, int cellX, int cellY, int runB, out int cost)
    {
        cost = 0;
        if (!chunk.MaskFilled)
        {
            fixed (ulong* pm = chunk.Mask)
                _source.FillChunkMask(chunk.Cx, chunk.Cy, pm);
            chunk.MaskFilled = true;
        }

        int ax, ay, bx, by;
        if (runA < 0)
        {
            ax = cellX; ay = cellY;
        }
        else
        {
            ax = chunk.Runs[runA].RepX; ay = chunk.Runs[runA].RepY;
        }
        if (runB < 0)
        {
            bx = cellX; by = cellY;
        }
        else
        {
            if (runA >= 0 && chunk.Intra.Length > 0)
            {
                short cached = chunk.Intra[runA, runB];
                if (cached >= 0) { cost = cached; return true; }
                if (cached == -2) return false;
            }
            bx = chunk.Runs[runB].RepX; by = chunk.Runs[runB].RepY;
        }

        int dist;
        bool los;
        fixed (ulong* pm = chunk.Mask)
            los = LocalChunkAStar.HasDirectLOS(new Int2(ax, ay), new Int2(bx, by), pm);
        if (los)
        {
            // Прямая видимость: стоимость = 10/14-октиль вдоль линии (точно и дёшево).
            dist = Math.Abs(ax - bx) > Math.Abs(ay - by)
                ? Math.Abs(ax - bx) * 10 + Math.Abs(ay - by) * 4
                : Math.Abs(ay - by) * 10 + Math.Abs(ax - bx) * 4;
        }
        else
        {
            fixed (ulong* pm = chunk.Mask)
                dist = ComputeLocalCost(pm, ax, ay, bx, by);
        }
        bool ok = dist != NoPath;
        if (ok) cost = dist;

        if (runA >= 0 && runB >= 0 && chunk.Intra.Length > 0)
            chunk.Intra[runA, runB] = chunk.Intra[runB, runA] = ok ? (short)Math.Min(dist, short.MaxValue) : (short)-2;
        return ok;
    }

    private unsafe int ComputeLocalCost(ulong* mask, int ax, int ay, int bx, int by)
    {
        if (ax == bx && ay == by) return 0;
        var res = _astar.FindLocalPathFast(new Int2(ax, ay), new Int2(bx, by), mask, _scratch);
        if (!res.Success) return NoPath;
        int cost = 0;
        var steps = res.Steps.Span;
        for (int i = 1; i < steps.Length; i++)
        {
            int dx = Math.Abs(steps[i].X - steps[i - 1].X);
            int dy = Math.Abs(steps[i].Y - steps[i - 1].Y);
            cost += (dx != 0 && dy != 0) ? 14 : 10;
        }
        return cost;
    }

    private ChunkPortals GetChunk(int cx, int cy)
    {
        long key = PackChunk(cx, cy);
        if (_chunks.TryGetValue(key, out var c)) return c;
        if (_chunks.Count >= MaxCachedChunks)
        {
            _chunks.Clear();
            _chunkLru.Clear();
        }
        c = new ChunkPortals { Key = key, Cx = cx, Cy = cy };
        BuildPortals(c);
        if (c.Runs.Length > 0)
            c.Intra = new short[c.Runs.Length, c.Runs.Length];
        for (int i = 0; i < c.Runs.Length; i++)
            for (int j = 0; j < c.Runs.Length; j++)
                c.Intra[i, j] = -1;
        _chunks[key] = c;
        return c;
    }

    /// <summary>Собрать прогоны на 4 границах чанка, свободные с обеих сторон.</summary>
    private unsafe void BuildPortals(ChunkPortals c)
    {
        long baseX = (long)c.Cx << ChunkShift;
        long baseY = (long)c.Cy << ChunkShift;
        var runs = new List<Run>(16);

        // N (y = baseY) и S (y = baseY + 63)
        CollectRuns(runs, c, baseX, baseY, side: 0);
        CollectRuns(runs, c, baseX, baseY + ChunkMask, side: 1);
        // W (x = baseX) и E (x = baseX + 63)
        CollectRuns(runs, c, baseX, baseY, side: 2);
        CollectRuns(runs, c, baseX + ChunkMask, baseY, side: 3);

        c.Runs = runs.ToArray();
        if (c.Runs.Length == 0) return;

        // Глобальные id + представители.
        for (int i = 0; i < c.Runs.Length; i++)
        {
            ref var r = ref c.Runs[i];
            r.NodeId = _nodePos.Count;
            _nodePos.Add((c.Cx, c.Cy));
            _nodeRun.Add(i);
            // Представитель — клетка внутри чанка рядом с серединой прогона.
            int mid = r.Start + r.Len / 2;
            switch (r.Side)
            {
                case 0: r.MidX = (short)mid; r.MidY = 0; r.RepX = (short)mid; r.RepY = 1; break;
                case 1: r.MidX = (short)mid; r.MidY = ChunkMask; r.RepX = (short)mid; r.RepY = ChunkMask - 1; break;
                case 2: r.MidX = 0; r.MidY = (short)mid; r.RepX = 1; r.RepY = (short)mid; break;
                default: r.MidX = ChunkMask; r.MidY = (short)mid; r.RepX = ChunkMask - 1; r.RepY = (short)mid; break;
            }
        }

        GrowSearchArrays(_nodePos.Count);
    }

    private unsafe void CollectRuns(List<Run> runs, ChunkPortals c, long baseX, long baseY, int side)
    {
        bool horizontal = side <= 1; // N/S — прогон по X
        int runStart = -1;
        int sideRuns = 0;
        for (int i = 0; i < ChunkSize; i++)
        {
            long x = horizontal ? baseX + i : baseX;
            long y = horizontal ? baseY : baseY + i;
            bool open = _source.IsWalkable(x, y);
            if (open)
            {
                // Проверяем, что клетка свободна с обеих сторон (иначе это не проход).
                long ox = side == 3 ? 1 : side == 2 ? -1 : 0;
                long oy = side == 1 ? 1 : side == 0 ? -1 : 0;
                open = _source.IsWalkable(x + ox, y + oy);
            }
            if (open)
            {
                if (runStart < 0) runStart = i;
            }
            else if (runStart >= 0)
            {
                if (sideRuns < MaxRunsPerSide)
                {
                    runs.Add(new Run { Side = (byte)side, Start = (byte)runStart, Len = (byte)(i - runStart) });
                    sideRuns++;
                }
                runStart = -1;
            }
        }
        if (runStart >= 0 && sideRuns < MaxRunsPerSide)
            runs.Add(new Run { Side = (byte)side, Start = (byte)runStart, Len = (byte)(ChunkSize - runStart) });
    }

    // ---------------- A* по порталам ----------------

    private void BeginSearch()
    {
        _searchStamp++;
        if (_searchStamp == int.MaxValue)
        {
            Array.Clear(_stamp, 0, _stamp.Length);
            Array.Clear(_seen, 0, _seen.Length);
            _searchStamp = 1;
        }
        _openCount = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int Stamp(int node) => _stamp[node];

    private void GrowSearchArrays(int needed)
    {
        if (needed <= _g.Length) return;
        int size = Math.Max(needed * 2, 8192);
        Array.Resize(ref _g, size);
        Array.Resize(ref _parent, size);
        Array.Resize(ref _stamp, size);
        Array.Resize(ref _seen, size);
    }

    private static int Octile(long ax, long ay, long bx, long by)
    {
        long dx = Math.Abs(ax - bx), dy = Math.Abs(ay - by);
        return (int)(dx > dy ? dx * 10 + dy * 4 : dy * 10 + dx * 4);
    }

    private void Relax(int node, int g, int parent, long toX, long toY)
    {
        // Закрытый узел не переоткрываем: эвристика (октиль) согласована с 10/14-стоимостью,
        // а внутричанковые расстояния её не меньше — повторное раскрытие не улучшит g.
        if (_stamp[node] == _searchStamp) return;
        if (_seen[node] == _searchStamp && g >= _g[node]) return;

        _g[node] = g;
        _parent[node] = parent;
        _seen[node] = _searchStamp;
        var (cx, cy) = _nodePos[node];
        var chunk = GetChunk(cx, cy);
        var run = chunk.Runs[_nodeRun[node]];
        long wx = ((long)cx << ChunkShift) + run.MidX;
        long wy = ((long)cy << ChunkShift) + run.MidY;
        int h = Heuristic(wx, wy, toX, toY);
        PushOpen(node, g + h, h);
    }

    /// <summary>Допустимая goal-directed эвристика (см. комментарий к полям).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int Heuristic(long wx, long wy, long toX, long toY)
    {
        int best = int.MaxValue;
        for (int i = 0; i < _goalPortalCount; i++)
        {
            int h = Octile(wx, wy, _goalMidX[i], _goalMidY[i]) + _goalTail[i];
            if (h < best) best = h;
        }
        int result = best == int.MaxValue ? Octile(wx, wy, toX, toY) : best;
        if (HeuristicWeight != 1.0f)
            result = (int)(result * HeuristicWeight);
        return result;
    }

    /// <summary>
    /// Вес эвристики (weighted A*). 1.0 — оптимальный путь; 1.2–1.4 — заметно меньше
    /// раскрытий ценой пути до +20–40% (для колонии это обычно приемлемо).
    /// </summary>
    public float HeuristicWeight { get; set; } = 1.0f;

    private void PushOpen(int node, int f, int h)
    {
        if (_openCount >= _openHeap.Length)
        {
            int size = _openHeap.Length * 2;
            Array.Resize(ref _openHeap, size);
        }
        // КЛЮЧ 64-бит: старшие 32 = f*1024 + tie-break по меньшей h (то есть по большей g),
        // младшие 32 = id узла. Раньше было `(f << 12) | node` в uint — при id портала
        // >= 4096 (у нас 36 491) биты наезжали на f, и PopOpen возвращал ЧУЖОЙ узел.
        long key = ((long)(f * 1024 + Math.Min(1023, h >> 8)) << 32) | (uint)node;
        int i = _openCount++;
        while (i > 0)
        {
            int p = (i - 1) >> 1;
            if (_openHeap[p] <= key) break;
            _openHeap[i] = _openHeap[p];
            i = p;
        }
        _openHeap[i] = key;
    }

    /// <summary>Диагностика: сколько порталов раскрыто в последнем поиске.</summary>
    public int LastExpansions { get; private set; }
    /// <summary>Диагностика: сколько waypoint-ов вернул последний поиск.</summary>
    public int LastWaypoints { get; private set; }

    private int PopOpen(out int f)
    {
        long top = _openHeap[0];
        int node = (int)(top & 0xFFFFFFFFu);
        f = (int)(top >> 32) / 1024;
        _openCount--;
        if (_openCount <= 0) { _openCount = 0; return node; }
        long last = _openHeap[_openCount];
        int i = 0;
        while (true)
        {
            int l = (i << 1) + 1;
            if (l >= _openCount) break;
            int r = l + 1;
            int min = (r < _openCount && _openHeap[r] < _openHeap[l]) ? r : l;
            if (_openHeap[min] >= last) break;
            _openHeap[i] = _openHeap[min];
            i = min;
        }
        _openHeap[i] = last;
        return node;
    }

    // ---------------- Тёплый путь по кэшу «следующий портал» ----------------

/// <summary>
/// Повторный путь по кэшу ЦЕЛОГО маршрута: одна проба словаря + копирование
/// заранее вычисленной последовательности порталов. Никакого A*.
/// </summary>
private bool TryWarmPath(
        int scx, int scy, int tcx, int tcy,
        long fromX, long fromY, long toX, long toY,
        Span<long> outXs, Span<long> outYs, out int w)
    {
        w = 0;
        if (outXs.Length < 3) return false;
        if (!_routeCache.TryGetValue(HopKey(scx, scy, tcx, tcy), out var route) || route.Version != _version)
            return false;

        outXs[0] = fromX; outYs[0] = fromY; w = 1;

        int copy = Math.Min(route.Count, outXs.Length - 2);
        for (int i = 0; i < copy; i++)
        {
            outXs[w] = route.Xs[i];
            outYs[w] = route.Ys[i];
            w++;
        }
        if (copy == route.Count && w < outXs.Length)
        {
            outXs[w] = toX; outYs[w] = toY;
            w++;
        }
        return true;
    }

    /// <summary>
    /// Сжать waypoints: убрать промежуточные точки, лежащие на той же прямой.
    /// Нужно из-за `AgentPathConfig.MaxWaypoints = 96` — портальный путь на 100+
    /// чанков иначе не влезает (а на открытой местности схлопывается в 5–15 точек).
    /// </summary>
    private static int Compress(Span<long> xs, Span<long> ys, int n)
    {
        if (n <= 2) return n;
        int w = 1;
        for (int i = 1; i < n - 1; i++)
        {
            // Дедуп: портал на границе даёт одну и ту же клетку дважды (выход A и вход B).
            long dx1 = xs[i] - xs[w - 1], dy1 = ys[i] - ys[w - 1];
            if (dx1 == 0 && dy1 == 0) continue;

            long dx2 = xs[i + 1] - xs[i], dy2 = ys[i + 1] - ys[i];
            bool collinear = dx1 * dy2 - dy1 * dx2 == 0;      // cross == 0
            bool sameDir = dx1 * dx2 + dy1 * dy2 > 0;          // не разворот на 180°
            if (collinear && sameDir) continue;
            xs[w] = xs[i]; ys[w] = ys[i]; w++;
        }
        xs[w] = xs[n - 1]; ys[w] = ys[n - 1]; w++;
        return w;
    }

    /// <summary>Сохранить в кэш маршрут, пришедший извне (жадный fallback), без стартовой/целевой точки.</summary>
    private void SaveRouteFromSpan(Span<long> xs, Span<long> ys, int n, int scx, int scy, int tcx, int tcy)
    {
        if (n < 3 || _routeCache.Count > MaxRouteCache) return;
        int middle = n - 2;
        var entry = new RouteEntry
        {
            Version = _version,
            Count = middle,
            Xs = new long[middle],
            Ys = new long[middle]
        };
        for (int i = 0; i < middle; i++)
        {
            entry.Xs[i] = xs[i + 1];
            entry.Ys[i] = ys[i + 1];
        }
        _routeCache[HopKey(scx, scy, tcx, tcy)] = entry;
    }

    /// <summary>Сохранить найденный маршрут: последовательность порталов (сжатая) на пару чанков.</summary>
    private void FillRouteCache(int bestNode, int scx, int scy, int goalCx, int goalCy)
    {
        if (_routeCache.Count > MaxRouteCache)
            _routeCache.Clear();

        Span<int> chain = stackalloc int[2048];
        int count = 0;
        int cur = bestNode;
        while (cur >= 0 && count < chain.Length)
        {
            chain[count++] = cur;
            cur = _parent[cur];
        }
        if (count < 2) return;

        // chain[count-1] — ближе к старту; разворачиваем в порядок старт→цель.
        Span<long> xs = stackalloc long[2048];
        Span<long> ys = stackalloc long[2048];
        int n = 0;
        for (int i = count - 1; i >= 0 && n < xs.Length; i--)
        {
            var (cx, cy) = _nodePos[chain[i]];
            var chunk = GetChunk(cx, cy);
            var run = chunk.Runs[_nodeRun[chain[i]]];
            xs[n] = ((long)cx << ChunkShift) + run.MidX;
            ys[n] = ((long)cy << ChunkShift) + run.MidY;
            n++;
        }
        n = Compress(xs, ys, n);

        var entry = new RouteEntry
        {
            Version = _version,
            Count = n,
            Xs = new long[n],
            Ys = new long[n]
        };
        for (int i = 0; i < n; i++) { entry.Xs[i] = xs[i]; entry.Ys[i] = ys[i]; }
        _routeCache[HopKey(scx, scy, goalCx, goalCy)] = entry;
    }

    private int Reconstruct(int bestNode, long fromX, long fromY, long toX, long toY, Span<long> outXs, Span<long> outYs)
    {
        // Цепочка порталов назад.
        int count = 0;
        Span<int> chain = stackalloc int[2048];
        int cur = bestNode;
        while (cur >= 0 && count < chain.Length)
        {
            chain[count++] = cur;
            cur = _parent[cur];
        }

        int w = 0;
        if (w < outXs.Length) { outXs[w] = fromX; outYs[w] = fromY; w++; }
        for (int i = count - 1; i >= 0 && w < outXs.Length; i--)
        {
            var (cx, cy) = _nodePos[chain[i]];
            var chunk = GetChunk(cx, cy);
            var run = chunk.Runs[_nodeRun[chain[i]]];
            outXs[w] = ((long)cx << ChunkShift) + run.MidX;
            outYs[w] = ((long)cy << ChunkShift) + run.MidY;
            w++;
        }
        if (w < outXs.Length)
        {
            outXs[w] = toX; outYs[w] = toY;
            w++;
        }
        // Сжатие: дедуп выход/вход одной двери + склейка коллинеарных.
        // Иначе на путь в 113 чанков выходит ~296 точек, а в игре лимит 96.
        w = Compress(outXs, outYs, w);
        return w;
    }
}