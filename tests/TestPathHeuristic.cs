using System;
using Godot;
using Game.Core.World;
using Game.Core.WorldStreaming;
using Game.Simulation;
using Game.Simulation.Pathfinding;

namespace Game.Tests;

/// <summary>
/// Три карточки 05_ путь: 05-07 (эвристика), 05-03 (окно мира), 05-04 (один портал).
///
/// 1. 05-07 — `OctileDist` обязана быть ОКТИЛЕМ под цены шага этого A*
///    (прямо 10, диагональ 14, вода ×3): 10*max + 4*min. Проверяем:
///    а) формулу поштучно (старое `10*max - 4*min` даёт 6 вместо 14 на (1,1));
///    б) допустимость: эвристика НЕ БОЛЬШЕ реальной стоимости пути — на четырёх
///       сетках с препятствиями и воде, где стоимость пути считает Дейкстра в тесте;
///    в) точность на пустой сетке: там эвристика РАВНА стоимости (не занижена).
/// 2. 05-03 — окно обязано содержать цель: в мире ищется пара, которую СТАРЫЙ
///    расчёт окна (центр пары + AlignDown) детерминированно отвергал, выбрасывая
///    цель за границу (пример из карточки: start % 32 == 0, dx = 96).
/// 3. 05-04 — единственный портал кромки стал непроходимым после сборки оверлея
///    (стена построена позже / гонка пересборки): маршрут обязан найтись через
///    соседнюю проходимую клетку кромки, а не отдавать «пути нет».
///
/// Каждая из трёх проверок красная на старом коде и зелёная на новом.
///
/// Запуск: godot --headless --path . --script res://tests/run_path_heuristic.gd
/// </summary>
public partial class TestPathHeuristic : RefCounted
{
    // ---- 05-03: константы окна WorldPathfinder — нужны, чтобы смоделировать
    // поведение СТАРОГО кода и доказать, что тест бьёт именно в тот баг ----
    private const int MinWindow = 128;
    private const int MaxWindow = 640;
    private const int WindowShift = 5;
    private const int Margin = 16;

    // Тот же мир, что в TestWorldPathfinder: если он там даёт сушу у спавна,
    // здесь даёт её же — тест детерминирован по сиду.
    private const ulong Seed = 0xC0FFEE1234ABCDEFul;
    private const uint Version = 3;

    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print("[PATH-HEUR] 05-07 октиль / 05-03 окно / 05-04 портал");
        GD.Print("==================================================");

        bool ok = true;
        ok &= HeuristicMatchesStepCost();
        ok &= HeuristicIsAdmissible();
        ok &= WindowContainsGoalAtTheEdge();
        ok &= SinglePortalFallsBackToEdgeCell();

        GD.Print("==================================================");
        GD.Print(ok
            ? "[PATH-HEUR] ALL PATH HEURISTIC TESTS PASSED! (Code 0)"
            : "[PATH-HEUR] FAIL");
        GD.Print("==================================================");
        return ok;
    }

    // ------------------------------------------------------------------
    // 05-07 (а): формула = октиль под реальные цены шага
    // ------------------------------------------------------------------

    /// <summary>
    /// Цена шага в ComputeLocalSegment: прямой 10, диагональ 14 (вода ×3 → 30/42).
    /// Точная стоимость по пустой сетке: (max-min) прямых + min диагональных =
    /// 10*(max-min) + 14*min = 10*max + 4*min. Старая формула 10*max - 4*min
    /// на (1,1) даёт 6 вместо 14 — тест красный уже здесь.
    /// </summary>
    private bool HeuristicMatchesStepCost()
    {
        GD.Print("[PATH-HEUR] --- 05-07: формула октиля под цены шага 10/14 ---");

        bool ok = true;
        int[] probes = { 0, 1, 2, 3, 5, 10, 31, 64 };

        for (int i = 0; i < probes.Length; i++)
        {
            int dx = probes[i];
            for (int j = 0; j < probes.Length; j++)
            {
                int dy = probes[j];
                int expected = 10 * Math.Max(dx, dy) + 4 * Math.Min(dx, dy);
                int got = HierarchicalPathfinder.OctileDist(dx, dy);

                if (got != expected)
                {
                    GD.PrintErr($"[PATH-HEUR] FAIL 05-07: OctileDist({dx},{dy}) = {got}, " +
                                $"ожидалось {expected} (10*max + 4*min)");
                    ok = false;
                }

                // Симметрия и независимость от знака: эвристика — расстояние.
                if (HierarchicalPathfinder.OctileDist(-dx, dy) != expected
                    || HierarchicalPathfinder.OctileDist(dx, -dy) != expected
                    || HierarchicalPathfinder.OctileDist(dy, dx) != expected)
                {
                    GD.PrintErr($"[PATH-HEUR] FAIL 05-07: OctileDist не симметрична на ({dx},{dy})");
                    ok = false;
                }
            }
        }

        if (ok)
        {
            GD.Print("[PATH-HEUR] OK 05-07: OctileDist = 10*max + 4*min " +
                     "(например (1,1)=14, (10,10)=140, (7,3)=82)");
        }

        return ok;
    }

    /// <summary>
    /// admissibility: h(S,T) ≤ стоимости реального пути. Считаем настоящую стоимость
    /// Дейкстрой по тем же правилам, что у A* в ComputeLocalSegment: 8 направлений,
    /// прямой 10 / диагональ 14, вход в воду ×3, стена (CostBlocked) непроходима.
    /// Плюс на ПУСТОЙ сетке требуем равенства — так ловится и занижение (второй
    /// признак старой формулы: 6 вместо 14 — это уже не октиль).
    /// </summary>
    private bool HeuristicIsAdmissible()
    {
        GD.Print("[PATH-HEUR] --- 05-07: допустимость (Дейкстра на сетках с препятствиями) ---");

        const int W = 40;
        const int H = 40;
        bool ok = true;
        int checks = 0;

        ok &= AdmissibleOnGrid("пустая", BuildEmpty(W, H), W, H, true, ref checks);
        ok &= AdmissibleOnGrid("стены-лабиринт", BuildMaze(W, H), W, H, false, ref checks);
        ok &= AdmissibleOnGrid("озеро", BuildLake(W, H), W, H, false, ref checks);
        ok &= AdmissibleOnGrid("диагональный барьер", BuildDiagonalBarrier(W, H), W, H,
            false, ref checks);

        if (ok)
        {
            GD.Print($"[PATH-HEUR] OK 05-07: эвристика нигде не больше стоимости пути ({checks} пар)");
        }

        return ok;
    }

    private static bool AdmissibleOnGrid(string label, byte[] cost, int w, int h,
        bool expectExact, ref int checks)
    {
        // Источники по углам/центру: покрывают прямые, диагональные и «через
        // препятствие» направления.
        int[] srcX = { 1, 1, w - 2, w / 2, 5, w - 3 };
        int[] srcY = { 1, h - 2, 1, h / 2, h - 5, 7 };

        bool ok = true;

        for (int s = 0; s < srcX.Length; s++)
        {
            int sx = srcX[s];
            int sy = srcY[s];
            if (cost[sy * w + sx] == HierarchicalPathfinder.CostBlocked)
                continue;

            int[] dist = Dijkstra(cost, w, h, sx, sy);

            for (int ty = 0; ty < h; ty++)
            {
                for (int tx = 0; tx < w; tx++)
                {
                    int d = dist[ty * w + tx];
                    if (d == int.MaxValue)
                        continue; // недостижимо: сравнивать не с чем

                    int hEst = HierarchicalPathfinder.OctileDist(tx - sx, ty - sy);
                    checks++;

                    if (hEst > d)
                    {
                        GD.PrintErr($"[PATH-HEUR] FAIL 05-07 ({label}): h({sx},{sy}→{tx},{ty}) = {hEst} " +
                                    $"> реальной стоимости {d} — эвристика НЕ допустима");
                        ok = false;
                    }
                    else if (expectExact && hEst != d)
                    {
                        GD.PrintErr($"[PATH-HEUR] FAIL 05-07 ({label}): h({sx},{sy}→{tx},{ty}) = {hEst} " +
                                    $"≠ стоимости {d} — эвристика занижена (это не октиль)");
                        ok = false;
                    }

                    if (!ok)
                        return false;
                }
            }
        }

        GD.Print($"[PATH-HEUR] OK 05-07 ({label}): {checks} проверок, {(expectExact ? "равенство" : "h ≤ стоимость")}");
        return true;
    }

    /// <summary>Дейкстра по сетке с ценой шага 10/14 и водой ×3 (как в ComputeLocalSegment).</summary>
    private static int[] Dijkstra(byte[] cost, int w, int h, int sx, int sy)
    {
        int n = w * h;
        var dist = new int[n];
        Array.Fill(dist, int.MaxValue);

        var nodes = new int[n * 9 + 1];
        var keys = new int[n * 9 + 1];
        int heapCount = 0;

        dist[sy * w + sx] = 0;
        HeapPush(nodes, keys, ref heapCount, sy * w + sx, 0);

        while (heapCount > 0)
        {
            int u = HeapPop(nodes, keys, ref heapCount, out int key);
            if (key != dist[u])
                continue; // устаревшая запись

            int ux = u % w;
            int uy = u / w;

            for (int dy = -1; dy <= 1; dy++)
            {
                int ny = uy + dy;
                if (ny < 0 || ny >= h)
                    continue;

                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0)
                        continue;

                    int nx = ux + dx;
                    if (nx < 0 || nx >= w)
                        continue;

                    byte cell = cost[ny * w + nx];
                    if (cell == HierarchicalPathfinder.CostBlocked)
                        continue;

                    int step = (dx != 0 && dy != 0) ? 14 : 10;
                    if (cell == HierarchicalPathfinder.CostWater)
                        step *= 3;

                    int nd = key + step;
                    int v = ny * w + nx;
                    if (nd < dist[v])
                    {
                        dist[v] = nd;
                        HeapPush(nodes, keys, ref heapCount, v, nd);
                    }
                }
            }
        }

        return dist;
    }

    private static void HeapPush(int[] nodes, int[] keys, ref int count, int node, int key)
    {
        int i = count++;
        while (i > 0)
        {
            int p = (i - 1) >> 1;
            if (keys[p] <= key)
                break;
            nodes[i] = nodes[p];
            keys[i] = keys[p];
            i = p;
        }
        nodes[i] = node;
        keys[i] = key;
    }

    private static int HeapPop(int[] nodes, int[] keys, ref int count, out int key)
    {
        int top = nodes[0];
        key = keys[0];
        count--;

        if (count > 0)
        {
            int node = nodes[count];
            int k = keys[count];
            int i = 0;
            while (true)
            {
                int left = i * 2 + 1;
                if (left >= count)
                    break;
                int right = left + 1;
                int child = (right < count && keys[right] < keys[left]) ? right : left;
                if (keys[child] >= k)
                    break;
                nodes[i] = nodes[child];
                keys[i] = keys[child];
                i = child;
            }
            nodes[i] = node;
            keys[i] = k;
        }

        return top;
    }

    private static byte[] BuildEmpty(int w, int h)
    {
        var cost = new byte[w * h];
        Array.Fill(cost, HierarchicalPathfinder.CostGrass);
        return cost;
    }

    /// <summary>Вертикальные стены с проходами через одну — «лабиринт» из 05-07.</summary>
    private static byte[] BuildMaze(int w, int h)
    {
        var cost = BuildEmpty(w, h);
        for (int x = 6; x < w; x += 7)
        {
            for (int y = 0; y < h; y++)
            {
                // проход каждые 9 клеток, чтобы сетка оставалась связной
                if (y % 9 == 4)
                    continue;
                cost[y * w + x] = HierarchicalPathfinder.CostBlocked;
            }
        }
        return cost;
    }

    /// <summary>Озеро воды в центре: вода проходима, но втрое дороже.</summary>
    private static byte[] BuildLake(int w, int h)
    {
        var cost = BuildEmpty(w, h);
        for (int y = 8; y < 28; y++)
        {
            for (int x = 12; x < 30; x++)
            {
                if ((x - 20) * (x - 20) + (y - 18) * (y - 18) <= 81)
                    cost[y * w + x] = HierarchicalPathfinder.CostWater;
            }
        }
        return cost;
    }

    /// <summary>Диагональный барьер с единственным проходом — обход требует многих раскрытий.</summary>
    private static byte[] BuildDiagonalBarrier(int w, int h)
    {
        var cost = BuildEmpty(w, h);
        for (int i = 0; i < Math.Min(w, h); i++)
        {
            if (i >= 18 && i <= 21)
                continue; // проход
            cost[i * w + i] = HierarchicalPathfinder.CostBlocked;
        }
        return cost;
    }

    // ------------------------------------------------------------------
    // 05-03: окно обязано содержать цель
    // ------------------------------------------------------------------

    /// <summary>
    /// Карточка 05-03: окно центрировалось на середине пары, а потом origin
    /// выравнивался ВНИЗ — выравнивание съедало правый запас, и цель оказывалась
    /// вне окна (отказ выдавался уже после сборки окна). Детерминированный пример
    /// из карточки: fromX ≡ 0 (mod 32), dx = 96 → width = 128, ox = fromX-32,
    /// правая граница окна = toX → InWindow(toX) = false.
    ///
    /// Тест ищет в мире пару ровно с такой геометрией (и с проходимой прямой
    /// между концами, чтобы путь заведомо существовал), проверяет по формулам
    /// СТАРОГО кода, что он эту пару отвергал, и требует, чтобы новый код нашёл.
    /// </summary>
    private bool WindowContainsGoalAtTheEdge()
    {
        GD.Print("[PATH-HEUR] --- 05-03: окно мира с целью на выровненном краю ---");

        WorldModel.Reset();
        WorldModel.Configure(Seed, Version);
        WorldCostCache.Clear();
        WorldPathfinder.Instance.Clear();

        try
        {
            RegionKey spawn = UnifiedWorldGenerator.FindHabitableSpawn(Seed, Version);
            long cx = spawn.X * 512;
            long cy = spawn.Y * 512;
            UnifiedWorldGenerator.SetWorldOrigin(cx, cy);
            GD.Print($"[PATH-HEUR] спавн региона: ({cx},{cy})");

            // Ищем пару по обеим осям: баг симметричен (съеденный правый ИЛИ нижний запас).
            if (TryFindWindowCase(cx, cy, true, out long fx, out long fy, out long tx, out long ty))
                return CheckWindowPair(fx, fy, tx, ty, true);

            if (TryFindWindowCase(cx, cy, false, out fx, out fy, out tx, out ty))
                return CheckWindowPair(fx, fy, tx, ty, false);

            GD.PrintErr("[PATH-HEUR] FAIL 05-03: не нашёл в мире пару с промахом окна " +
                        "(проверь сид/генератор — тест обязан бить в этот баг)");
            return false;
        }
        finally
        {
            WorldPathfinder.Instance.Clear();
            WorldCostCache.Clear();
            WorldModel.Reset();
        }
    }

    private static bool CheckWindowPair(long fx, long fy, long tx, long ty, bool horizontal)
    {
        bool found = WorldPathfinder.Instance.TryFindPathBlocking(
            fx, fy, tx, ty, out int[] path, out int count,
            out long originX, out long originY, out int width);

        if (!found)
        {
            GD.PrintErr($"[PATH-HEUR] FAIL 05-03: ({fx},{fy})→({tx},{ty}) вернул false, " +
                        "хотя прямая между концами проходима (окно не содержит цель?)");
            return false;
        }

        GD.Print($"[PATH-HEUR] OK 05-03: ({fx},{fy})→({tx},{ty}) [{(horizontal ? "dx" : "dy")}=96, " +
                 $"старый код отвергал выравниванием] найден путь: {count} точек, " +
                 $"окно origin=({originX},{originY}) width={width}");
        return true;
    }

    /// <summary>
    /// Ищем в мире пару, у которой СТАРЫЙ код выбрасывал цель выравниванием окна,
    /// а прямая между концами проходима (значит путь существует и отказ мог быть
    /// только из-за окна). Идём по проходимым ОТРЕЗКАМ: внутри отрезка любая пара
    /// видима, поэтому достаточно найти отрезок длиной больше пробного dx.
    /// Детерминированный пример из карточки: fromX ≡ 0 (mod 32), dx = 96 →
    /// width = 128, ox = fromX-32, правая граница окна = toX → цель вне окна.
    /// </summary>
    private static bool TryFindWindowCase(long cx, long cy, bool horizontal,
        out long fx, out long fy, out long tx, out long ty)
    {
        fx = fy = tx = ty = 0;
        const long Scan = 500;

        // outer — координата перпендикулярно паре, along — вдоль неё.
        long outerFrom = horizontal ? cy - Scan : cx - Scan;
        long outerTo = horizontal ? cy + Scan : cx + Scan;
        long alongFrom = (horizontal ? cx : cy) - Scan;
        long alongTo = alongFrom + 2 * Scan;

        for (long outer = outerFrom; outer < outerTo; outer++)
        {
            long runStart = -1;

            for (long along = alongFrom; along <= alongTo; along++)
            {
                bool walk = horizontal
                    ? WorldCostCache.IsWalkable(along, outer)
                    : WorldCostCache.IsWalkable(outer, along);

                if (walk)
                {
                    if (runStart < 0)
                        runStart = along;
                    continue;
                }

                if (runStart >= 0)
                {
                    if (TryWindowPairInRun(runStart, along - 1, outer, horizontal,
                            out fx, out fy, out tx, out ty))
                        return true;
                    runStart = -1;
                }
            }

            if (runStart >= 0 && TryWindowPairInRun(runStart, alongTo, outer, horizontal,
                    out fx, out fy, out tx, out ty))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Пара «start → start+d» внутри проходимого отрезка, которую СТАРЫЙ код
    /// отвергал выравниванием окна. Перебираем несколько начал и длин: важен не
    /// конкретный dx, а то, что старый расчёт окна выбрасывал цель (модель в
    /// <see cref="OldWindowRejectsGoal"/>), а прямая между концами проходима.
    /// </summary>
    private static bool TryWindowPairInRun(long runStart, long runEnd, long outer, bool horizontal,
        out long fx, out long fy, out long tx, out long ty)
    {
        fx = fy = tx = ty = 0;
        const int MaxTries = 64;
        long[] distances = { 96, 64, 128, 160, 224 };

        long tries = 0;
        for (long start = runStart; start <= runEnd && tries < MaxTries; start++, tries++)
        {
            for (int i = 0; i < distances.Length; i++)
            {
                long end = start + distances[i];
                if (end > runEnd)
                    continue;

                long fromX = horizontal ? start : outer;
                long fromY = horizontal ? outer : start;
                long toX = horizontal ? end : outer;
                long toY = horizontal ? outer : end;

                if (!OldWindowRejectsGoal(fromX, fromY, toX, toY))
                    continue;

                fx = fromX;
                fy = fromY;
                tx = toX;
                ty = toY;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Модель СТАРОГО (до 05-03) расчёта окна: центр пары, затем AlignDown.
    /// true — цель (или старт) оказывалась вне окна, то есть старый код отвергал
    /// пару ещё до поиска.
    /// </summary>
    private static bool OldWindowRejectsGoal(long fromX, long fromY, long toX, long toY)
    {
        long dx = Math.Abs(toX - fromX);
        long dy = Math.Abs(toY - fromY);

        int width = OldWindowSize(dx);
        int height = OldWindowSize(dy);

        long centerX = (fromX + toX) / 2;
        long centerY = (fromY + toY) / 2;
        long ox = AlignDown(centerX - width / 2);
        long oy = AlignDown(centerY - height / 2);

        bool start = InOldWindow((int)(fromX - ox), (int)(fromY - oy), width, height);
        bool goal = InOldWindow((int)(toX - ox), (int)(toY - oy), width, height);
        return !start || !goal;
    }

    private static int OldWindowSize(long d)
    {
        long raw = d + 2 * Margin;
        if (raw > MaxWindow)
            raw = MaxWindow;
        int aligned = (int)((raw + ((1 << WindowShift) - 1)) & ~((1 << WindowShift) - 1));
        if (aligned < MinWindow)
            aligned = MinWindow;
        if (aligned > MaxWindow)
            aligned = MaxWindow;
        return aligned;
    }

    private static bool InOldWindow(int x, int y, int width, int height)
        => x >= 0 && y >= 0 && x < width && y < height;

    private static long AlignDown(long v)
        => v & ~((1L << WindowShift) - 1);

    // ------------------------------------------------------------------
    // 05-04: один портал (и протухший якорь)
    // ------------------------------------------------------------------

    /// <summary>
    /// 05-04: кромка регионов имеет РОВНО один портал (одна непрерывная проходимая
    /// полоса по всей границе — типовая ситуация открытой местности). Якорь в
    /// реестре — СЕРЕДИНА этой полосы. Моделируем реальную рассинхронизацию
    /// (05-04/05-05): стена встала ПОСЛЕ сборки оверлея, поэтому якорь-середина
    /// стал непроходимым, а реестр порталов об этом не знает.
    ///
    /// Старый код: выбирал закрытый якорь → ComputeLocalSegment сразу возвращал
    /// null (IsCellBlocked цели) → сегмент не построен → «пути нет».
    /// Новый: закрытый якорь отбрасывается, берётся ближайшая к линии S–T
    /// проходимая клетка той же кромки → маршрут строится.
    /// </summary>
    private bool SinglePortalFallsBackToEdgeCell()
    {
        GD.Print("[PATH-HEUR] --- 05-04: единственный портал кромки стал непроходимым ---");

        HierarchicalPathfinder pf = HierarchicalPathfinder.Instance;
        const int W = 64;
        const int H = 64;

        try
        {
            var cost = new byte[W * H];
            Array.Fill(cost, HierarchicalPathfinder.CostGrass);

            // Стена в регионе (0,0): перекрывает прямую видимость S→T, но кромку
            // x=15|16 не трогает, поэтому портал там остаётся ОДИН (середина полосы).
            for (int y = 0; y <= 9; y++)
                cost[y * W + 8] = HierarchicalPathfinder.CostBlocked;

            pf.InitializeCost(cost, W, H);

            // Прогрев: строит оверлей (порталы, регион-граф) и реестр порталов.
            if (!pf.TryFindPathScratch(1, 1, 3, 1, out _, out _))
            {
                GD.PrintErr("[PATH-HEUR] FAIL 05-04: прогрев не нашёл тривиальный путь (1,1)→(3,1)");
                return false;
            }

            // Кромка регионов (0,0)|(1,0) — столбец x=15|16. Середина полосы
            // (y=0..15) — клетка (15,7): именно её положил в реестр BuildEdgePortals.
            // Стена встаёт ПОЗЖЕ сборки оверлея: реестр протух, якорь закрыт.
            cost[7 * W + 15] = HierarchicalPathfinder.CostBlocked;

            bool found = pf.TryFindPathScratch(2, 2, 30, 2, out int[] path, out int count);

            if (!found)
            {
                GD.PrintErr("[PATH-HEUR] FAIL 05-04: (2,2)→(30,2) = false, хотя кромка x=15|16 " +
                            "проходима в других клетках (единственный протухший портал убил маршрут)");
                return false;
            }

            if (!ValidatePath(path, count, W, cost, 2, 2, 30, 2))
                return false;

            GD.Print($"[PATH-HEUR] OK 05-04: маршрут через кромку с одним порталом найден " +
                     $"({count} точек), фолбэк — ближайшая проходимая клетка кромки");
            return true;
        }
        finally
        {
            pf.Reset();
        }
    }

    /// <summary>Контракт HPA: каждая точка проходима, последняя — цель.</summary>
    private static bool ValidatePath(int[] path, int count, int width, byte[] cost,
        int fromX, int fromY, int toX, int toY)
    {
        if (count == 0)
            return true; // прямая видимость — точек не требуется

        int lastX = -1;
        int lastY = -1;
        for (int i = 0; i < count; i++)
        {
            int lx = path[i] % width;
            int ly = path[i] / width;
            if (lx < 0 || ly < 0 || lx >= width || ly >= cost.Length / width
                || cost[ly * width + lx] == HierarchicalPathfinder.CostBlocked)
            {
                GD.PrintErr($"[PATH-HEUR] FAIL: точка пути ({lx},{ly}) непроходима");
                return false;
            }
            lastX = lx;
            lastY = ly;
        }

        if (lastX != toX || lastY != toY)
        {
            GD.PrintErr($"[PATH-HEUR] FAIL: путь закончился в ({lastX},{lastY}), цель ({toX},{toY})");
            return false;
        }

        return true;
    }
}