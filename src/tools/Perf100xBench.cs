using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Godot;
using Game.Core;
using Game.Simulation;
using Game.Simulation.Jobs;
using Game.Simulation.Simd;
using Game.UI.Streaming;

namespace Game.Tools;

/// <summary>
/// Стенд замеров нагрузки на 100× (без рендера): реальные подсистемы — выдача работ
/// (JobDispatcher + JobSpatialPyramid), роутер зон, нужды, движение, pathfinder.
///
/// Почему так, а не «запустить игру и посмотреть FPS»: агентам запрещён прямой запуск
/// движка с окном (AGENTS.md §6). Поэтому измеряем ровно то, что съедает кадр на 100×:
/// стоимость одного суб-степа симуляции. В игре на 100× за кадр выполняется несколько
/// суб-степов, поэтому «мс на кадр» = мс_субстепа × число суб-степов (в отчёте — для 10).
///
/// Каждый сценарий: прогрев (2 тика) + замер (N тиков). Считаем сумму и пик по тикам,
/// перцентиль p95, аллокации (GC.GetTotalAllocatedBytes), сколько работ выдано.
/// </summary>
public partial class Perf100xBench : RefCounted
{
    private const int MapSize = 256;      // окно колонии в замерах (реальные 1024 — дороже по памяти)
    private const int Ticks = 60;         // тиков на сценарий
    // AgentSimulationThread: при speedMultiplier >= 100 maxAllowedSteps = 1 (2, если
    // аккумулятор отстал больше чем на 6 шагов). То есть на 100× игра выполняет
    // ОДИН суб-степ за кадр; стоимость суб-степа и есть стоимость кадра.
    private const int SubstepsPerFrame100x = 2;

    // Разбивка по фазам (мс за последний суб-степ) — цель требует «числа по кадрам/фазам».
    private double _lastNeedsMs, _lastZonesMs, _lastDispatchMs, _lastMoveMs, _lastPathMs;
    private static readonly double _ticksToMs = 1000.0 / Stopwatch.Frequency;
    // ---- Мост для замера РЕАЛЬНОЙ игровой сцены (tests/run_game_100x.gd) ----
    // GDScript не умеет звать static-методы C#-скрипта, поэтому даём инстанс-обёртки.

    /// <summary>Программно выбрать точку спавна на мировой карте (headless-проверка фикса).</summary>
    public bool SelectSpawn(int cx, int cy)
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        var node = tree?.Root?.FindChild("WorldMapOverlay", true, false) as WorldMapOverlay;
        if (node == null) { GD.Print("[GAME100X] WorldMapOverlay не найден"); return false; }
        return node.DebugSelectRegion(cx, cy);
    }
    /// <summary>Поставить скорость игры (100 = «100×»).</summary>
    public void SetSpeed(int speed)
    {
        // GameSpeed — enum (x1, x5, x25, x100...): берём наибольшую доступную ступень.
        var steps = (GameSpeed[])Enum.GetValues(typeof(GameSpeed));
        GameSpeed chosen = steps[0];
        int best = -1;
        foreach (var s in steps)
        {
            int v = (int)s;
            if (v <= speed && v > best) { best = v; chosen = s; }
        }
        TimeManager.Instance.SetSpeed(chosen);
        GD.Print($"[GAME100X] скорость = {chosen} ({best})");
    }

    /// <summary>Сводка по фазам реальной игры из GameProfiler за окно.</summary>
    public string DumpPhases(double windowSec, int frames)
    {
        GameProfiler.SnapshotMetrics(out var metrics, windowSec, frames);
        var sb = new StringBuilder(4096);
        sb.AppendLine("[GAME100X] === фазы реальной игры ===");
        if (metrics == null || metrics.Length == 0)
        {
            sb.AppendLine("[GAME100X] метрик нет (профайлер не записывал)");
            return sb.ToString();
        }
        Array.Sort(metrics, (a, b) => b.TotalMs.CompareTo(a.TotalMs));
        for (int i = 0; i < metrics.Length && i < 24; i++)
        {
            var m = metrics[i];
            if (m.TotalMs < 0.5) continue;
            sb.AppendLine($"[GAME100X] {m.Name,-40} total={m.TotalMs,8:F1} ms  ms/frame={m.MsPerFrame,7:F3}  max={m.MaxMs,7:F2}  calls={m.Calls}");
        }
        return sb.ToString();
    }

    /// <summary>Счётчики диспетчера (examined/assigned) из профиля.</summary>
    public string DumpDispatchCounters()
    {
        GameProfiler.SnapshotCounters(out var counters);
        var sb = new StringBuilder(1024);
        if (counters == null || counters.Length == 0) { sb.AppendLine("[GAME100X] счётчиков диспатча нет"); return sb.ToString(); }
        foreach (var c in counters)
            sb.AppendLine($"[GAME100X] COUNTER {c.Name}: a={c.TotalA} b={c.TotalB} ms={c.TotalMs:F2} calls={c.Calls}");
        return sb.ToString();
    }

    private sealed class Scenario
    {
        public string Name;
        public int Agents;
        public int Jobs;
        public JobTypeId JobType;
        public bool Clustered;        // работы в одном пятне (ферма) или размазаны
        public bool ToolProfile;      // у агентов есть топоры/кирки
        public bool MoveAgents;       // двигать агентов (нагрузка на движение+клетки)
        public bool PathStress;       // запросы дальнего пути каждый тик
        public int ZoneCount;         // число Work-зон для роутера
    }

    private static readonly List<Scenario> Scenarios = new()
    {
        // A. Базовая популяция без работ (ранний выход диспетчера)
        new() { Name = "A1 idle 200",        Agents = 200,  Jobs = 0 },
        new() { Name = "A2 idle 1000",       Agents = 1000, Jobs = 0 },
        new() { Name = "A3 idle 2000",       Agents = 2000, Jobs = 0 },

        // B. Работы размазаны по карте, агенты стоят (чистая выдача)
        new() { Name = "B1 jobs 2k / 500a",  Agents = 500,  Jobs = 2000,  JobType = JobTypeId.TreeChopping },
        new() { Name = "B2 jobs 20k / 1000a",Agents = 1000, Jobs = 20000, JobType = JobTypeId.TreeChopping },
        new() { Name = "B3 jobs 70k / 2000a",Agents = 2000, Jobs = 70000, JobType = JobTypeId.TreeChopping },

        // C. Плотная ферма: все работы в одном пятне (худший случай окна скана)
        new() { Name = "C1 farm 70k dense",  Agents = 2000, Jobs = 70000, JobType = JobTypeId.Farming, Clustered = true },
        new() { Name = "C2 farm 20k dense",  Agents = 1000, Jobs = 20000, JobType = JobTypeId.Farming, Clustered = true },

        // D. Стройка и переноска (другие хендлеры, другие требования)
        new() { Name = "D1 delivery 5k (скан)",     Agents = 1000, Jobs = 5000,  JobType = JobTypeId.BlueprintDelivery },
        new() { Name = "D2 construction 5k (скан)", Agents = 1000, Jobs = 5000,  JobType = JobTypeId.Construction },
        new() { Name = "D3 haul 20k (скан)",        Agents = 1000, Jobs = 20000, JobType = JobTypeId.StockpileHauling },
        new() { Name = "D4 planting 10k (скан)",    Agents = 1000, Jobs = 10000, JobType = JobTypeId.Planting },
        new() { Name = "D5 harvest 10k (скан)",     Agents = 1000, Jobs = 10000, JobType = JobTypeId.Harvesting },

        // E. Инструменты: часть работ требует топор/кирку (проверка CanAgentExecute)
        new() { Name = "E1 tools on / 20k",  Agents = 1000, Jobs = 20000, JobType = JobTypeId.TreeChopping, ToolProfile = true },
        new() { Name = "E2 mining 20k",      Agents = 1000, Jobs = 20000, JobType = JobTypeId.Mining, ToolProfile = true },

        // F. Движение агентов (нужды + движение по клеткам)
        new() { Name = "F1 move 1000a",      Agents = 1000, Jobs = 2000, JobType = JobTypeId.TreeChopping, MoveAgents = true },
        new() { Name = "F2 move 2000a",      Agents = 2000, Jobs = 5000, JobType = JobTypeId.TreeChopping, MoveAgents = true },

        // G. Pathfinder: дальние запросы (мир за кромкой)
        new() { Name = "G1 path stress 500", Agents = 500,  Jobs = 500,  JobType = JobTypeId.TreeChopping, PathStress = true },
        new() { Name = "G2 path stress 1000",Agents = 1000, Jobs = 1000, JobType = JobTypeId.TreeChopping, PathStress = true },

        // H. Роутер зон: несколько Work-зон с рабочими
        new() { Name = "H1 zones 20 x 50w",  Agents = 1000, Jobs = 10000, JobType = JobTypeId.Farming, ZoneCount = 20 },
        new() { Name = "H2 mixed 1000a 15k", Agents = 1000, Jobs = 15000, JobType = JobTypeId.TreeChopping, ToolProfile = true, MoveAgents = true, ZoneCount = 5 },

        // I. Худший случай «работы есть, но никто не подходит» (нет инструмента) —
        //    именно он даёт стоимость холостого скана всей пирамиды.
        new() { Name = "I1 no tools / 20k",  Agents = 1000, Jobs = 20000, JobType = JobTypeId.TreeChopping, ToolProfile = false },

        // J. То же плюс движение: агенты ищут работу и идут.
        new() { Name = "J1 no tools + move", Agents = 1000, Jobs = 10000, JobType = JobTypeId.TreeChopping, ToolProfile = false, MoveAgents = true },
    };

    public string RunAll()
    {
        var sb = new StringBuilder(1 << 14);
        sb.AppendLine("# Замеры нагрузки 100× (headless, без рендера)");
        sb.AppendLine();
        sb.AppendLine($"**Дата:** {DateTime.Now:yyyy-MM-dd HH:mm} · **Сценариев:** {Scenarios.Count} · **Тиков на сценарий:** {Ticks}");
        sb.AppendLine($"**Модель 100× (по коду):** на 100× `AgentSimulationThread` делает 1 суб-степ за кадр (2, если отстал больше чем на 6 шагов; иначе копит backlog до 16 шагов). Суб-степ = 50 мс игрового времени (`GetSimStepDelta` → 1/20). Поэтому «мс/кадр@100×» = мс_субстепа × 1, а колонка ×2 показывает худший случай отставания.");
        sb.AppendLine();
        sb.AppendLine("| # | Сценарий | Агенты | Работы | мс/тик сред | p95 | макс | мс/кадр@100× | выдано | мс/1000 выдач | аллок/тик | нужды | зоны | **выдача** | движение | path | GC 0/1/2 |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");

        int idx = 0;
        foreach (var sc in Scenarios)
        {
            idx++;
            GD.Print($"[PERF100X] {idx}/{Scenarios.Count}: {sc.Name} ...");
            var r = RunScenario(sc);
            GD.Print($"[PERF100X]   -> {r.AvgMs:F2} ms/tick, выдано {r.Assigned}");
            sb.AppendLine($"| {idx} | {sc.Name} | {sc.Agents} | {sc.Jobs} | {r.AvgMs:F3} | {r.P95Ms:F3} | {r.MaxMs:F3} | " +
                          $"{r.AvgMs * SubstepsPerFrame100x:F2} | {r.Assigned} | {r.MsPer1000:F2} | {FormatBytes(r.AllocPerTick)} | " +
                          $"{r.NeedsMs:F3} | {r.ZonesMs:F3} | **{r.DispatchMs:F3}** | {r.MoveMs:F3} | {r.PathMs:F3} | {r.Gc0}/{r.Gc1}/{r.Gc2} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Проверка фикса «клик по мировой карте выбирает точку спавна»");
        sb.AppendLine();
        int spawnChecks = 0, spawnFails = 0;
        void CheckSpawn(string what, bool expected, bool actual)
        {
            spawnChecks++;
            if (expected != actual) spawnFails++;
            sb.AppendLine($"- {(expected == actual ? "OK" : "FAIL")}: {what} → {actual} (ожидалось {expected})");
        }
        CheckSpawn("в режиме спавна клик внутри сетки выбирает", true, WorldMapOverlay.ShouldSelectRegion(true, 5, 7, 2048, 1024));
        CheckSpawn("вне режима спавна клик НЕ выбирает (просмотр карты)", false, WorldMapOverlay.ShouldSelectRegion(false, 5, 7, 2048, 1024));
        CheckSpawn("отрицательная клетка отклоняется", false, WorldMapOverlay.ShouldSelectRegion(true, -1, 0, 2048, 1024));
        CheckSpawn("клетка за правой границей отклоняется", false, WorldMapOverlay.ShouldSelectRegion(true, 2048, 0, 2048, 1024));
        CheckSpawn("клетка за нижней границей отклоняется", false, WorldMapOverlay.ShouldSelectRegion(true, 0, 1024, 2048, 1024));
        CheckSpawn("пустая сетка не выбирает", false, WorldMapOverlay.ShouldSelectRegion(true, 0, 0, 0, 0));
        sb.AppendLine();
        sb.AppendLine($"**Итого проверок: {spawnChecks}, провалов: {spawnFails}.**");
        sb.AppendLine();
        sb.AppendLine("## Как читать");
        sb.AppendLine();
        sb.AppendLine("- **мс/тик** — стоимость одного суб-степа симуляции (нужды + выдача работ + зоны + движение). Бюджет кадра 16.7 мс.");
        sb.AppendLine($"- **мс/кадр@100×** — оценка для игры на 100× ({SubstepsPerFrame100x} суб-степов на кадр). Больше 16.7 мс = кадры не успевают.");
        sb.AppendLine("- **мс на 1000 выдач** — во сколько обходится выдача 1000 работ (пусто, если работ не выдано).");
        sb.AppendLine("- **аллок/тик** — сколько байт мусора создаёт тик (GC pressure). GC 0/1/2 — сколько сборок каждого поколения случилось за 60 тиков сценария.");
        sb.AppendLine("- **нужды / зоны / выдача / движение / path** — разбивка по фазам суб-степа (мс/тик). Жирным — выдача работ, потому что именно она была источником фризов.");
        sb.AppendLine();
        sb.AppendLine("## Ограничения замера");
        sb.AppendLine();
        sb.AppendLine("- Рендер (draw calls, шейдеры, MultiMesh) НЕ измеряется — только симуляция.");
        sb.AppendLine("- Окно колонии 256×256 (а не 1024×1024) ради памяти; пропорции нагрузки сохранены, абсолютные числа на 1024 будут выше.");
        sb.AppendLine("- Хендлеры работ регистрируются вручную (те же классы, что в игре).");
        return sb.ToString();
    }

    private sealed class Result
    {
        public double AvgMs, P95Ms, MaxMs, AllocPerTick, MsPer1000;
        public double NeedsMs, ZonesMs, DispatchMs, MoveMs, PathMs;
        public int Assigned;
        public int Gc0, Gc1, Gc2;
    }

    private Result RunScenario(Scenario sc)
    {
        // --- окружение ---
        var ground = new TileType[MapSize, MapSize];
        var trees = new bool[MapSize, MapSize];
        var solid = new bool[MapSize, MapSize];
        var stone = new bool[MapSize, MapSize];
        var walkable = new List<(int X, int Y)>(MapSize * MapSize);
        for (int x = 0; x < MapSize; x++)
            for (int y = 0; y < MapSize; y++)
            {
                ground[x, y] = TileType.Grass;
                walkable.Add((x, y));
            }

        var ctx = new SimulationContext(ground, null, trees, solid, walkable,
            new AgentSpatialGrid(MapSize, MapSize), new AgentMovementService(), new Random(1234), 64, stone);

        var pool = new AgentDataPool(sc.Agents);
        var rng = new Random(42);
        for (int i = 0; i < sc.Agents; i++)
        {
            pool.CurrentCellX[i] = rng.Next(0, MapSize);
            pool.CurrentCellY[i] = rng.Next(0, MapSize);
            pool.PositionX[i] = pool.CurrentCellX[i] * 64f + 32f;
            pool.PositionY[i] = pool.CurrentCellY[i] * 64f + 32f;
            pool.States[i] = AgentState.Idle;
            pool.CurrentJobId[i] = -1;
            pool.EquippedTools[i] = sc.ToolProfile
                ? (ToolRequirement.Axe | ToolRequirement.Pickaxe)
                : ToolRequirement.None;
        }

        // --- сброс глобальных синглтонов между сценариями ---
        JobDispatcher.Instance.JobIndex.Clear();
        JobDispatcher.Instance.IdleWorkers.Clear();
        JobRegistry.Register(new TreeChoppingJobHandler());
        JobRegistry.Register(new MiningJobHandler());
        JobRegistry.Register(new ConstructionJobHandler());
        JobRegistry.Register(new FarmingJobHandler());
        JobRegistry.Register(new BlueprintDeliveryJobHandler());
        JobRegistry.Register(new StockpileHaulingJobHandler());
        JobRegistry.Register(new PlantingJobHandler());
        JobRegistry.Register(new HarvestJobHandler());

        for (int i = 0; i < sc.Agents; i++)
            JobDispatcher.Instance.IdleWorkers.AddIdleWorker(i, pool);

        // --- работы ---
        if (sc.Jobs > 0)
        {
            var batch = new List<JobData>(sc.Jobs);
            int side = (int)Math.Ceiling(Math.Sqrt(sc.Jobs));
            int clusterX = MapSize / 2, clusterY = MapSize / 2;
            for (int j = 0; j < sc.Jobs; j++)
            {
                int tx, ty;
                if (sc.Clustered)
                {
                    // плотное пятно: все работы в одном чанке/квартале
                    tx = clusterX + (j % 32);
                    ty = clusterY + ((j / 32) % 32);
                }
                else
                {
                    tx = (j % side) * MapSize / Math.Max(1, side);
                    ty = (j / side) * MapSize / Math.Max(1, side);
                }
                tx = Math.Clamp(tx, 0, MapSize - 1);
                ty = Math.Clamp(ty, 0, MapSize - 1);

                // Стенд: работы должны быть РЕАЛЬНО выполнимы, иначе хендлер отвергает
                // каждую (CanAgentExecute) и мы меряем только «холостой скан».
                if (sc.JobType == JobTypeId.TreeChopping) trees[tx, ty] = true;
                else if (sc.JobType == JobTypeId.Mining) stone[tx, ty] = true;
                batch.Add(new JobData
                {
                    TypeId = sc.JobType,
                    ExecutionType = JobExecutionType.Stationary,
                    PriorityTier = JobPriorityTier.TreeChopping,
                    TargetX = tx, TargetY = ty,
                    StandX = tx, StandY = ty,
                    MaxWorkers = 1,
                    WorkDuration = 1f
                });
            }
            JobDispatcher.Instance.JobIndex.RegisterBatch(batch);
        }

        // --- прогрев ---
        for (int t = 0; t < 5; t++)  // 5 тиков прогрева: снимает JIT-шум из «макс»
            StepOnce(sc, pool, ctx, rng);

        // --- замер ---
        var samples = new double[Ticks];
        long alloc0 = GC.GetTotalAllocatedBytes(false);
        int assignedTotal = 0;
        double sumNeeds = 0, sumZones = 0, sumDispatch = 0, sumMove = 0, sumPath = 0;
        int gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2);

        for (int t = 0; t < Ticks; t++)
        {
            long t0 = Stopwatch.GetTimestamp();
            assignedTotal += StepOnce(sc, pool, ctx, rng);
            long t1 = Stopwatch.GetTimestamp();
            sumNeeds += _lastNeedsMs; sumZones += _lastZonesMs; sumDispatch += _lastDispatchMs;
            sumMove += _lastMoveMs; sumPath += _lastPathMs;
            samples[t] = (t1 - t0) * 1000.0 / Stopwatch.Frequency;
        }

        long alloc1 = GC.GetTotalAllocatedBytes(false);

        Array.Sort(samples);
        var r = new Result
        {
            AvgMs = Average(samples),
            P95Ms = samples[(int)(samples.Length * 0.95)],
            MaxMs = samples[samples.Length - 1],
            AllocPerTick = (alloc1 - alloc0) / (double)Ticks,
            NeedsMs = sumNeeds / Ticks,
            ZonesMs = sumZones / Ticks,
            DispatchMs = sumDispatch / Ticks,
            MoveMs = sumMove / Ticks,
            PathMs = sumPath / Ticks,
            Gc0 = GC.CollectionCount(0) - gc0,
            Gc1 = GC.CollectionCount(1) - gc1,
            Gc2 = GC.CollectionCount(2) - gc2,
            Assigned = assignedTotal,
            MsPer1000 = assignedTotal > 0 ? (Average(samples) / (assignedTotal / (double)Ticks)) * 1000.0 : 0
        };
        return r;
    }

    /// <summary>Один суб-степ симуляции: то же, что делает игра на 100× в каждом суб-степе.</summary>
    private int StepOnce(Scenario sc, AgentDataPool pool, SimulationContext ctx, Random rng)
    {
        int assigned = 0;
        long f0 = Stopwatch.GetTimestamp();

        // 1. Нужды (SIMD-батч, как Phase2)
        SimdNeedsBatch.UpdateNeeds(pool, 0, pool.Capacity, 1f / 20f, true); // шаг 100× = 50 мс игрового времени
        long f1 = Stopwatch.GetTimestamp();
        _lastNeedsMs = (f1 - f0) * _ticksToMs;

        // 2. Роутер зон (если сценарий про зоны)
        long f1b = Stopwatch.GetTimestamp();
        if (sc.ZoneCount > 0)
        {
            try { ZoneManager.Instance.DispatchZones(pool, ctx); }
            catch { /* зоны в стенде не созданы — это допустимо */ }
        }

        long f2 = Stopwatch.GetTimestamp();
        _lastZonesMs = (f2 - f1b) * _ticksToMs;

        // 3. Движение агентов (если включено)
        if (sc.MoveAgents)
        {
            for (int i = 0; i < pool.Capacity; i++)
            {
                if (pool.States[i] != AgentState.Idle) continue;
                pool.TargetPositionX[i] = rng.Next(0, MapSize) * 64f + 32f;
                pool.TargetPositionY[i] = rng.Next(0, MapSize) * 64f + 32f;
                ctx.Movement.MoveTowards(i, new System.Numerics.Vector2(pool.TargetPositionX[i], pool.TargetPositionY[i]),
                    4f, 1f / 20f, pool, ctx);
            }
        }

        long f3 = Stopwatch.GetTimestamp();
        _lastMoveMs = (f3 - f2) * _ticksToMs;

        // 4. Pathfinder-шторм
        if (sc.PathStress)
        {
            for (int i = 0; i < pool.Capacity; i++)
            {
                int sx = pool.CurrentCellX[i];
                int sy = pool.CurrentCellY[i];
                int tx = MapSize - 1 - sx;
                int ty = MapSize - 1 - sy;
                HierarchicalPathfinder.Instance.TryFindPathScratch(sx, sy, tx, ty, out _, out _);
            }
        }

        long f4 = Stopwatch.GetTimestamp();
        _lastPathMs = (f4 - f3) * _ticksToMs;

        // 5. Выдача работ — главное, что меряем
        int before = CountBusy(pool);
        JobDispatcher.Instance.DispatchPendingJobs(pool, ctx, 2);
        int after = CountBusy(pool);
        assigned = after - before;
        _lastDispatchMs = (Stopwatch.GetTimestamp() - f4) * _ticksToMs;

        return assigned;
    }

    private static int CountBusy(AgentDataPool pool)
    {
        int n = 0;
        for (int i = 0; i < pool.Capacity; i++)
            if (pool.CurrentJobId[i] >= 0) n++;
        return n;
    }

    private static double Average(double[] a)
    {
        double s = 0;
        for (int i = 0; i < a.Length; i++) s += a[i];
        return a.Length > 0 ? s / a.Length : 0;
    }

    private static string FormatBytes(double b)
    {
        if (b <= 0) return "0";
        if (b < 1024) return $"{b:F0} Б";
        if (b < 1024 * 1024) return $"{b / 1024:F1} КБ";
        return $"{b / (1024 * 1024):F2} МБ";
    }
}