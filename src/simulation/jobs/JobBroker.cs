using System;
using System.Collections.Generic;
using Game.Core;
using Game.Core.World;
using Game.Core.WorldStreaming;

namespace Game.Simulation;

public sealed class JobBroker
{
    public static JobBroker Instance { get; } = new();

    public int ActiveBlueprintCount => JobDispatcher.Instance.JobIndex.TotalCount;

    /// <summary>
    /// Находит клетку стояния рядом с целью (не вода, не стена, не дерево).
    /// Нужно, чтобы рабочий не стоял прямо в клетке-цели (будущей стене/грядке),
    /// из-за чего он зависал «внутри» механики и толпился в стене.
    /// Возвращает true, если найдена свободная соседняя клетка, иначе false
    /// (тогда вызывающий должен использовать саму клетку цели).
    /// </summary>
    // Направления соседей — static (без new int[] на вызов из горячего OnStart).
    private static readonly int[] StandDx = { 0, 0, -1, 1 };
    private static readonly int[] StandDy = { -1, 1, 0, 0 };

    public static bool FindStandPosition(int tx, int ty, SimulationContext ctx, out (int X, int Y) stand)
    {
        if (ctx == null)
        {
            stand = (tx, ty);
            return false;
        }

        // 1. Кардинальные соседи (расстояние 1)
        for (int i = 0; i < 4; i++)
        {
            int nx = tx + StandDx[i];
            int ny = ty + StandDy[i];
            if (IsWalkable(nx, ny, ctx))
            {
                stand = (nx, ny);
                return true;
            }
        }

        // 2. Диагональные соседи (расстояние 1)
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = tx + dx;
                int ny = ty + dy;
                if (IsWalkable(nx, ny, ctx))
                {
                    stand = (nx, ny);
                    return true;
                }
            }
        }

        // 3. Расширенный радиус до 3 клеток
        for (int r = 2; r <= 3; r++)
        {
            for (int dy = -r; dy <= r; dy++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue;
                    int nx = tx + dx;
                    int ny = ty + dy;
                    if (IsWalkable(nx, ny, ctx))
                    {
                        stand = (nx, ny);
                        return true;
                    }
                }
            }
        }

        stand = (tx, ty);
        return false;
    }

    private static bool IsWalkable(int x, int y, SimulationContext ctx)
    {
        if ((uint)x >= (uint)ctx.MapWidth || (uint)y >= (uint)ctx.MapHeight)
            return false;
        if (ctx.SolidWalls != null && ctx.SolidWalls[x, y])
            return false;
        if (ctx.TreeOnGrass != null && ctx.TreeOnGrass[x, y])
            return false;
        if (ctx.StoneOnGrass != null && ctx.StoneOnGrass[x, y])
            return false;
        if (ctx.Ground != null)
        {
            var g = ctx.Ground[x, y];
            if (g == TileType.Water || g == TileType.Mountain)
                return false;
        }
        return true;
    }

    public void RegisterTreeChop(int x, int y, SimulationContext ctx = null)
    {
        var standPos = (x, y);
        if (ctx != null)
        {
            GridHelper.TryFindAdjacentWalkable(x, y, ctx.Ground, ctx.SolidWalls, ctx.TreeOnGrass, out standPos);
        }

        JobDispatcher.Instance.RegisterJob(new JobData
        {
            TypeId = JobTypeId.TreeChopping,
            ExecutionType = JobExecutionType.Stationary,
            PriorityTier = JobPriorityTier.TreeChopping,
            TargetX = x,
            TargetY = y,
            StandX = standPos.Item1,
            StandY = standPos.Item2,
            MaxWorkers = 1,
            WorkDuration = 8.0f
        });
        // §119 P1: пуш роутеру зон (пуш = уведомление; реестр — источник истины).
        int treeJobId = JobDispatcher.Instance.JobIndex.TryGetJobIdByPos(x, y, JobTypeId.TreeChopping, out _);
        if (treeJobId >= 0)
            ZoneJobRouter.Instance.Push(JobTypeId.TreeChopping, x, y, treeJobId);
    }

    public void RegisterTreeChopBatch(List<(int X, int Y)> trees, SimulationContext ctx = null)
    {
        if (trees == null || trees.Count == 0) return;

        var batch = new List<JobData>(trees.Count);
        foreach (var (x, y) in trees)
        {
            var standPos = (x, y);
            if (ctx != null)
            {
                GridHelper.TryFindAdjacentWalkable(x, y, ctx.Ground, ctx.SolidWalls, ctx.TreeOnGrass, out standPos);
            }

            batch.Add(new JobData
            {
                TypeId = JobTypeId.TreeChopping,
                ExecutionType = JobExecutionType.Stationary,
                PriorityTier = JobPriorityTier.TreeChopping,
                TargetX = x,
                TargetY = y,
                StandX = standPos.Item1,
                StandY = standPos.Item2,
                MaxWorkers = 1,
                WorkDuration = 8.0f
            });
        }

        JobDispatcher.Instance.RegisterBatch(batch);

        // §119 P1: батч-пуш роутеру зон (реестр — источник истины, id по позиции).
        var treeIndex = JobDispatcher.Instance.JobIndex;
        foreach (var (x, y) in trees)
        {
            int jid = treeIndex.TryGetJobIdByPos(x, y, JobTypeId.TreeChopping, out _);
            if (jid >= 0) // J-21: jobId == 0 - valid id (TryGetJobIdByPos returns -1 on miss)
                ZoneJobRouter.Instance.Push(JobTypeId.TreeChopping, x, y, jid);
        }
    }

    public void UnregisterTreeChop(int x, int y) =>
        JobDispatcher.Instance.UnregisterJobByPos(x, y, JobTypeId.TreeChopping);

    public void UnregisterTreeChopBatch(List<(int X, int Y)> trees) =>
        JobDispatcher.Instance.UnregisterBatchByPositions(trees, JobTypeId.TreeChopping);

    /// <summary>
    /// Добыча камня (Mining, россыпи StoneJobManager). Stand — соседняя свободная
    /// клетка (не камень/дерево/стена), иначе рабочий встаёт в саму россыпь.
    /// </summary>
    public void RegisterStoneMine(int x, int y, SimulationContext ctx = null)
    {
        var standPos = (x, y);
        if (ctx != null)
        {
            GridHelper.TryFindAdjacentWalkable(x, y, ctx.Ground, ctx.SolidWalls, ctx.TreeOnGrass, ctx.StoneOnGrass, out standPos);
        }

        JobDispatcher.Instance.RegisterJob(new JobData
        {
            TypeId = JobTypeId.Mining,
            ExecutionType = JobExecutionType.Stationary,
            PriorityTier = JobPriorityTier.TreeChopping,
            TargetX = x,
            TargetY = y,
            StandX = standPos.Item1,
            StandY = standPos.Item2,
            MaxWorkers = 1,
            WorkDuration = 8.0f
        });
        // §119 P1: пуш роутеру зон.
        int mineJobId = JobDispatcher.Instance.JobIndex.TryGetJobIdByPos(x, y, JobTypeId.Mining, out _);
        if (mineJobId >= 0)
            ZoneJobRouter.Instance.Push(JobTypeId.Mining, x, y, mineJobId);
    }

    public void RegisterStoneMineBatch(List<(int X, int Y)> stones, SimulationContext ctx = null)
    {
        if (stones == null || stones.Count == 0) return;

        var batch = new List<JobData>(stones.Count);
        foreach (var (x, y) in stones)
        {
            var standPos = (x, y);
            if (ctx != null)
            {
                GridHelper.TryFindAdjacentWalkable(x, y, ctx.Ground, ctx.SolidWalls, ctx.TreeOnGrass, ctx.StoneOnGrass, out standPos);
            }

            batch.Add(new JobData
            {
                TypeId = JobTypeId.Mining,
                ExecutionType = JobExecutionType.Stationary,
                PriorityTier = JobPriorityTier.TreeChopping,
                TargetX = x,
                TargetY = y,
                StandX = standPos.Item1,
                StandY = standPos.Item2,
                MaxWorkers = 1,
                WorkDuration = 8.0f
            });
        }

        JobDispatcher.Instance.RegisterBatch(batch);

        // §119 P1: батч-пуш роутеру зон.
        var stoneIndex = JobDispatcher.Instance.JobIndex;
        foreach (var (x, y) in stones)
        {
            int jid = stoneIndex.TryGetJobIdByPos(x, y, JobTypeId.Mining, out _);
            if (jid >= 0) // J-21: jobId == 0 - valid id (TryGetJobIdByPos returns -1 on miss)
                ZoneJobRouter.Instance.Push(JobTypeId.Mining, x, y, jid);
        }
    }

    public void UnregisterStoneMine(int x, int y) =>
        JobDispatcher.Instance.UnregisterJobByPos(x, y, JobTypeId.Mining);

    public void UnregisterStoneMineBatch(List<(int X, int Y)> stones) =>
        JobDispatcher.Instance.UnregisterBatchByPositions(stones, JobTypeId.Mining);

    /// <summary>
    /// Добыча руды (task-4, OreMining, жилы OreJobManager). Руда проходима, поэтому
    /// рабочий встаёт на соседнюю свободную клетку как у камня; без ctx — на цель.
    /// </summary>
    public void RegisterOreMine(int x, int y, SimulationContext ctx = null)
    {
        var standPos = (x, y);
        if (ctx != null)
        {
            GridHelper.TryFindAdjacentWalkable(x, y, ctx.Ground, ctx.SolidWalls, ctx.TreeOnGrass, ctx.StoneOnGrass, out standPos);
        }

        JobDispatcher.Instance.RegisterJob(new JobData
        {
            TypeId = JobTypeId.OreMining,
            ExecutionType = JobExecutionType.Stationary,
            PriorityTier = JobPriorityTier.TreeChopping,
            TargetX = x,
            TargetY = y,
            StandX = standPos.Item1,
            StandY = standPos.Item2,
            MaxWorkers = 1,
            WorkDuration = BuildConfig.MineDurationSec
        });
        int oreJobId = JobDispatcher.Instance.JobIndex.TryGetJobIdByPos(x, y, JobTypeId.OreMining, out _);
        if (oreJobId >= 0)
            ZoneJobRouter.Instance.Push(JobTypeId.OreMining, x, y, oreJobId);
    }

    public void RegisterOreMineBatch(List<(int X, int Y)> cells, SimulationContext ctx = null)
    {
        if (cells == null || cells.Count == 0)
            return;

        var batch = new List<JobData>(cells.Count);
        foreach (var (x, y) in cells)
        {
            var standPos = (x, y);
            if (ctx != null)
            {
                GridHelper.TryFindAdjacentWalkable(x, y, ctx.Ground, ctx.SolidWalls, ctx.TreeOnGrass, ctx.StoneOnGrass, out standPos);
            }

            batch.Add(new JobData
            {
                TypeId = JobTypeId.OreMining,
                ExecutionType = JobExecutionType.Stationary,
                PriorityTier = JobPriorityTier.TreeChopping,
                TargetX = x,
                TargetY = y,
                StandX = standPos.Item1,
                StandY = standPos.Item2,
                MaxWorkers = 1,
                WorkDuration = BuildConfig.MineDurationSec
            });
        }

        JobDispatcher.Instance.RegisterBatch(batch);

        var oreIndex = JobDispatcher.Instance.JobIndex;
        foreach (var (x, y) in cells)
        {
            int jid = oreIndex.TryGetJobIdByPos(x, y, JobTypeId.OreMining, out _);
            if (jid >= 0) // J-21: jobId == 0 - valid id
                ZoneJobRouter.Instance.Push(JobTypeId.OreMining, x, y, jid);
        }
    }

    public void UnregisterOreMine(int x, int y) =>
        JobDispatcher.Instance.UnregisterJobByPos(x, y, JobTypeId.OreMining);

    public void UnregisterOreMineBatch(List<(int X, int Y)> cells) =>
        JobDispatcher.Instance.UnregisterBatchByPositions(cells, JobTypeId.OreMining);

    public void RegisterStockpileHaul(int x, int y)
    {
        JobDispatcher.Instance.RegisterJob(new JobData
        {
            TypeId = JobTypeId.StockpileHauling,
            ExecutionType = JobExecutionType.Hauling,
            PriorityTier = JobPriorityTier.StockpileHauling,
            SourceX = x,
            SourceY = y,
            TargetX = x,
            TargetY = y,
            StandX = x,
            StandY = y,
            MaxWorkers = 1
        });
        // §119 P1: пуш роутеру зон.
        int haulJobId = JobDispatcher.Instance.JobIndex.TryGetJobIdByPos(x, y, JobTypeId.StockpileHauling, out _);
        if (haulJobId >= 0)
            ZoneJobRouter.Instance.Push(JobTypeId.StockpileHauling, x, y, haulJobId);
    }

    /// <summary>
    /// Гарантирует, что для каждого предмета, лежащего вне складской зоны,
    /// зарегистрирована haul-работа «отнести на склад». Закрывает пробел:
    /// склад может быть нарисован ПОСЛЕ того, как брёвна/зерно выпали на землю —
    /// в этом случае SpawnItems не создал работу, а sweep её добьёт.
    /// </summary>
    // P0-2: wall-clock троттлинг sweep — не чаще раза в 2с реального времени.
    // Sweep рос 0.1→23 мс total: скан Ground-словаря + IsZoneTile на кандидата
    // на каждый проход. Опоздание регистрации на 2с безвредно (работы и так
    // копятся быстрее разбора — см. рост Dispatch ×135).
    private long _lastSweepWallTicks;
    private static readonly TimeSpan SweepMinInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// J-14: когда реестр переполнен (&gt;2048 unclaimed), проход переноски не
    /// выключается, а идёт реже (раз в 10с) и меньшей пачкой. Иначе большая ферма
    /// глобально морила стройку без брёвен в складе.
    /// </summary>
    private long _lastBacklogSweepWallTicks;
    private static readonly TimeSpan BacklogSweepMinInterval = TimeSpan.FromSeconds(10);
    // P0-2: переиспользуемый буфер ТОЛЬКО для sim-потока. Sweep зовётся и из
    // главного потока (AddZoneTile при рисовании склада) — там свой локальный
    // список (редкий вызов, аллокация не важна), общий буфер означал бы гонку.
    private readonly List<(int X, int Y)> _sweepTiles = new(128);

    public void SweepStockpileHaulJobs()
    {
        if (!StockpileManager.Instance.HasFreeSpace)
        {
            return;
        }

        long now = DateTime.UtcNow.Ticks;
        if (now - _lastSweepWallTicks < SweepMinInterval.Ticks)
            return;
        _lastSweepWallTicks = now;

        // P0-2: кап 128 вместо 512 — проход дешевле в 4 раза; хвост дождётся
        // следующего прохода через 2с.
        // J-14: раньше при переполненном реестре (>2048 unclaimed) проход
        // выключался ЦЕЛИКОМ. На большой ферме это глобально морило стройку:
        // бревёшки оставались лежать на земле, переноска на склад не появлялась,
        // и стройка стояла без материала — при этом «работ и так навалом» было
        // верно только для работ фермы. Теперь вместо выключения — мягкая
        // деградация: редкий проход и маленькая пачка. Реестр не раздувается
        // (≤16 работ за проход не чаще раза в 10с), но переноска живёт всегда.
        // ST-05: не душить перенос ресурсов из-за строительных чертежей стен
        bool backlogged = JobDispatcher.Instance.JobIndex.UnclaimedCount > 512;
        if (backlogged && now - _lastBacklogSweepWallTicks < BacklogSweepMinInterval.Ticks)
            return;
        if (backlogged)
            _lastBacklogSweepWallTicks = now;
        int tilesPerSweep = backlogged ? 32 : 128;

        // Главный поток (рисование склада) — со своим списком, без общего буфера.
        bool isSimThread = System.Threading.Thread.CurrentThread.IsThreadPoolThread
            || System.Threading.Thread.CurrentThread.IsBackground;
        var itemTiles = isSimThread ? _sweepTiles
            : new List<(int X, int Y)>(tilesPerSweep);
        GroundItemManager.Instance.CollectItemTilesOutsideStockpiles(itemTiles, tilesPerSweep);

        if (itemTiles.Count == 0)
        {
            return;
        }

        // Батч: N× lock(_registerLock) → 1 lock. Дедуп по _posMap внутри
        // RegisterBatch, семантика та же (stale/двойные — no-op).
        var batch = new System.Collections.Generic.List<JobData>(itemTiles.Count);
        for (int i = 0; i < itemTiles.Count; i++)
        {
            var (x, y) = itemTiles[i];
            batch.Add(new JobData
            {
                TypeId = JobTypeId.StockpileHauling,
                ExecutionType = JobExecutionType.Hauling,
                PriorityTier = JobPriorityTier.StockpileHauling,
                SourceX = x,
                SourceY = y,
                TargetX = x,
                TargetY = y,
                StandX = x,
                StandY = y,
                MaxWorkers = 1
            });
        }
        JobDispatcher.Instance.RegisterBatch(batch);
        // §119 P1: батч-пуш haul-работ роутеру зон (sweep-регистрации тоже идут через роутер).
        var haulIndex = JobDispatcher.Instance.JobIndex;
        for (int i = 0; i < itemTiles.Count; i++)
        {
            var (x, y) = itemTiles[i];
            int jid = haulIndex.TryGetJobIdByPos(x, y, JobTypeId.StockpileHauling, out _);
            if (jid >= 0) // J-21: jobId == 0 - valid id (TryGetJobIdByPos returns -1 on miss)
                ZoneJobRouter.Instance.Push(JobTypeId.StockpileHauling, x, y, jid);
        }
    }

    /// <summary>
    /// Зарегистрировать доставку для клетки чертежа (task-19). Выбирает СЛЕДУЮЩИЙ
    /// недовезённый материал сайта (порядок RequiredMaterials: бревно, затем камень)
    /// и везёт именно его (<c>JobData.TargetItemId</c>). На клетку — один активный
    /// delivery-джоб, поэтому материалы возятся ПОСЛЕДОВАТЕЛЬНО.
    /// </summary>
    public void RegisterBlueprint(int x, int y, BuildingType type, int targetLogs)
    {
        ResolveNextMaterial(x, y, targetLogs, out ItemId item, out int remaining);
        RegisterBlueprintJob(x, y, item, remaining);
        JobDispatcher.Instance.TriggerImmediateDispatch();
    }

    /// <summary>Типизированная регистрация одной delivery-работы клетки.</summary>
    private void RegisterBlueprintJob(int x, int y, ItemId item, int remaining)
    {
        if (remaining <= 0 || item == ItemId.None)
        {
            // Везти нечего: сайт снабжён (или битые данные) — сразу монтаж/группа.
            ConstructionManager.Instance.NotifyMaterialsComplete(x, y);
            return;
        }

        // Заявка типизированная; ВСЕ старые заявки клетки снимаем, иначе после
        // смены материала висел бы фантомный спрос на предыдущий тип.
        DemandRegistry.Instance.UnregisterAllDemandsAt(x, y);
        DemandRegistry.Instance.RegisterDemand(DemandType.Blueprint, x, y, item, remaining);

        JobDispatcher.Instance.RegisterJob(new JobData
        {
            TypeId = JobTypeId.BlueprintDelivery,
            ExecutionType = JobExecutionType.Hauling,
            PriorityTier = JobPriorityTier.BlueprintSupply,
            TargetX = x,
            TargetY = y,
            StandX = x,
            StandY = y,
            TargetItemId = item,
            TargetItemCount = remaining,
            // Прогресс именно ЭТОГО материала: сайт хранит довезённое потипно,
            // поэтому работа начинается с нуля (остаток уже учтён в remaining).
            CurrentDeliveredCount = 0,
            MaxWorkers = 1
        });
        // §119 P1: пуш роутеру зон.
        int bpJobId = JobDispatcher.Instance.JobIndex.TryGetJobIdByPos(x, y, JobTypeId.BlueprintDelivery, out _);
        if (bpJobId >= 0)
            ZoneJobRouter.Instance.Push(JobTypeId.BlueprintDelivery, x, y, bpJobId);
    }

    /// <summary>
    /// Следующий материал сайта. Если сайта нет (мир/реплей/старые данные) —
    /// прежний контракт: везём брёвна (fallbackLogs).
    /// </summary>
    private static void ResolveNextMaterial(int x, int y, int fallbackLogs,
        out ItemId item, out int remaining)
    {
        if (ConstructionManager.Instance.TryGetSite(x, y, out var site) && site != null)
        {
            if (site.TryGetNextNeededMaterial(out item, out remaining))
                return;
            item = ItemId.None;
            remaining = 0;
            return;
        }
        item = ItemId.Log;
        remaining = Math.Max(1, fallbackLogs);
    }

    public void RegisterBlueprintBatch(List<(int X, int Y)> cells, BuildingType type, int targetLogs)
    {
        if (cells == null || cells.Count == 0)
            return;

        // Тип материала разный по клеткам (у станков бревно+камень, у стен — только
        // бревно), поэтому батч собирается пофакторно; пустые клетки (уже снабжены)
        // сразу уходят в монтаж.
        var batch = new List<JobData>(cells.Count);
        for (int i = 0; i < cells.Count; i++)
        {
            (int x, int y) = cells[i];
            ResolveNextMaterial(x, y, targetLogs, out ItemId item, out int remaining);
            if (remaining <= 0 || item == ItemId.None)
            {
                ConstructionManager.Instance.NotifyMaterialsComplete(x, y);
                continue;
            }
            DemandRegistry.Instance.UnregisterAllDemandsAt(x, y);
            DemandRegistry.Instance.RegisterDemand(DemandType.Blueprint, x, y, item, remaining);
            batch.Add(new JobData
            {
                TypeId = JobTypeId.BlueprintDelivery,
                ExecutionType = JobExecutionType.Hauling,
                PriorityTier = JobPriorityTier.BlueprintSupply,
                TargetX = x,
                TargetY = y,
                StandX = x,
                StandY = y,
                TargetItemId = item,
                TargetItemCount = remaining,
                CurrentDeliveredCount = 0,
                MaxWorkers = 1
            });
        }

        if (batch.Count == 0)
            return;
        JobDispatcher.Instance.RegisterBatch(batch);

        // §119 P1: батч-пуш роутеру зон (только реально зарегистрированные клетки).
        var bpIndex = JobDispatcher.Instance.JobIndex;
        for (int i = 0; i < batch.Count; i++)
        {
            JobData job = batch[i];
            int jid = bpIndex.TryGetJobIdByPos(job.TargetX, job.TargetY, JobTypeId.BlueprintDelivery, out _);
            if (jid >= 0) // J-21: jobId == 0 - valid id (TryGetJobIdByPos returns -1 on miss)
                ZoneJobRouter.Instance.Push(JobTypeId.BlueprintDelivery, job.TargetX, job.TargetY, jid);
        }
        JobDispatcher.Instance.TriggerImmediateDispatch();
    }

    public void UnregisterBlueprint(int x, int y)
    {
        // task-19: снять заявки ВСЕХ материалов клетки, не только брёвен.
        DemandRegistry.Instance.UnregisterAllDemandsAt(x, y);
        JobDispatcher.Instance.UnregisterJobByPos(x, y, JobTypeId.BlueprintDelivery);
        JobDispatcher.Instance.UnregisterJobByPos(x, y, JobTypeId.Construction);
    }

    public void UnregisterBlueprintBatch(List<(int X, int Y)> cells)
    {
        if (cells != null)
        {
            for (int i = 0; i < cells.Count; i++)
                DemandRegistry.Instance.UnregisterAllDemandsAt(cells[i].X, cells[i].Y);
        }
        JobDispatcher.Instance.UnregisterBatchByPositions(cells, JobTypeId.BlueprintDelivery);
        JobDispatcher.Instance.UnregisterBatchByPositions(cells, JobTypeId.Construction);
    }

    /// <summary>
    /// Приём доставленного материала агентом (task-19: ТИП материала — параметр,
    /// а не хардкод ItemId.Log). Сайт копит потипно; когда довезён текущий материал,
    /// работа закрывается и регистрируется следующий, пока не довезено ВСЁ.
    /// </summary>
    public void DeliverMaterialToBlueprint(int x, int y, ItemId item, int count)
    {
        if (count <= 0 || item == ItemId.None)
            return;

        // Если работы уже нет (снята/завершена конкурентом) — материал НЕ теряем.
        if (!JobDispatcher.Instance.JobIndex.TryGetJobByPos(x, y, JobTypeId.BlueprintDelivery, out var jobBefore))
        {
            SpawnOverflowItems(x, y, item, count);
            return;
        }

        // Работа возит конкретный тип; чужой материал не принимаем (вернём на землю).
        ItemId jobItem = jobBefore.TargetItemId != ItemId.None ? jobBefore.TargetItemId : ItemId.Log;
        if (jobItem != item)
        {
            SpawnOverflowItems(x, y, item, count);
            return;
        }

        ConstructionManager.Instance.DeliverMaterial(x, y, item, count, out int siteAccepted, out _);

        if (siteAccepted <= 0)
        {
            WarnDeliveryRejected(x, y, count);
            SpawnOverflowItems(x, y, item, count);
            if (jobBefore.TargetItemCount > 0 &&
                jobBefore.CurrentDeliveredCount >= jobBefore.TargetItemCount)
            {
                AdvanceBlueprintJob(x, y);
            }
            return;
        }

        _deliveryRejectStreak.Remove((x, y));

        if (!JobDispatcher.Instance.JobIndex.TryAddJobProgressClamped(
                x, y, JobTypeId.BlueprintDelivery, siteAccepted,
                out _, out bool isCompleted, out _))
        {
            // Работа исчезла между проверкой и начислением. Материал сайт уже принял.
            DemandRegistry.Instance.UpdateDelivered(x, y, item, siteAccepted);
            return;
        }

        DemandRegistry.Instance.UpdateDelivered(x, y, item, siteAccepted);

        // Излишек (сверх нужды материала) — на землю рядом, а не в никуда.
        int overflow = count - siteAccepted;
        if (overflow > 0)
            SpawnOverflowItems(x, y, item, overflow);

        // Материал довезён полностью → следующий материал или монтаж.
        // Дополнительно ловим случай «сайт уже полностью снабжён» (DeliverMaterial
        // мог продвинуть стадию другим путём) — тогда работа не должна висеть.
        bool fullySupplied = ConstructionManager.Instance.TryGetSite(x, y, out var site)
            && site != null && site.IsFullySupplied;
        if (isCompleted || fullySupplied)
            AdvanceBlueprintJob(x, y);
    }

    /// <summary>Совместимость: старый вызов доставки брёвен.</summary>
    public void DeliverLogsToBlueprint(int x, int y, int count)
        => DeliverMaterialToBlueprint(x, y, ItemId.Log, count);

    /// <summary>
    /// Материал текущего джоба довезён: снять работу и либо перейти к следующему
    /// материалу сайта, либо запустить монтаж (стройка/станок).
    /// </summary>
    private void AdvanceBlueprintJob(int x, int y)
    {
        JobDispatcher.Instance.JobIndex.RemoveJobByPos(x, y, JobTypeId.BlueprintDelivery, out _);

        if (!ConstructionManager.Instance.TryGetSite(x, y, out var site) || site == null)
            return;

        if (site.IsFullySupplied)
        {
            DemandRegistry.Instance.UnregisterAllDemandsAt(x, y);
            // Этап 2→3 пайплайна стройки (обычный монтаж или группа WaitForAll).
            ConstructionManager.Instance.NotifySupplyDone(x, y);
            ConstructionManager.Instance.NotifyMaterialsComplete(x, y);
            return;
        }

        if (site.Stage == ConstructionStage.Supplying)
        {
            // Ещё есть недовезённые материалы — следующий тип (бревно→камень).
            RegisterBlueprint(x, y, site.Definition.Type, site.Definition.TotalRequiredItems);
        }
    }

    /// <summary>
    /// Излишек материала — на первую свободную клетку рядом с целью (не на клетку
    /// стройки: там монтаж, а Construction.Can требует пустую клетку).
    /// </summary>
    private static void SpawnOverflowItems(int x, int y, ItemId item, int count)
    {
        if (count <= 0 || item == ItemId.None)
            return;
        (int X, int Y) spill = (x, y);
        bool found = false;
        for (int oy = -2; oy <= 2 && !found; oy++)
        {
            for (int ox = -2; ox <= 2 && !found; ox++)
            {
                if (ox == 0 && oy == 0) continue;
                int nx = x + ox, ny = y + oy;
                if (Game.Core.World.WorldModel.IsInsideIsland(nx, ny) &&
                    Game.Core.World.WorldModel.CellAtLocal(nx, ny).Terrain == BaseTerrainKind.Grass &&
                    !StructureManager.Instance.IsWallAt(nx, ny) &&
                    !GroundItemManager.Instance.HasItemsAt(nx, ny))
                {
                    spill = (nx, ny);
                    found = true;
                }
            }
        }
        GroundItemManager.Instance.SpawnItems(spill.X, spill.Y, item, count);
    }
    // FIX (bughunt 08-5): счётчик отказов доставки на клетку сайта. Нужен, чтобы
    // предупредить один раз и не спамить GD.PushWarning каждый тик. Словарь живёт
    // только в sim-потоке (DeliverLogsToBlueprint зовут хендлеры), поэтому без лока;
    // при переполнении просто чистится (редкий путь, аллокация не в горячем цикле).
    private readonly Dictionary<(int X, int Y), int> _deliveryRejectStreak = new();
    private const int MaxDeliveryRejectSites = 256;

    private void WarnDeliveryRejected(int x, int y, int count)
    {
        var key = (x, y);
        int streak;
        if (_deliveryRejectStreak.TryGetValue(key, out streak))
        {
            streak++;
            _deliveryRejectStreak[key] = streak;
            // 1-е, 10-е, 100-е… предупреждения: видно, что проблема не разовая,
            // но лог не заливается на каждом тике.
            if (streak != 10 && streak != 100 && streak != 1000)
                return;
        }
        else
        {
            if (_deliveryRejectStreak.Count >= MaxDeliveryRejectSites)
                _deliveryRejectStreak.Clear();
            streak = 1;
            _deliveryRejectStreak[key] = streak;
        }

        Godot.GD.PushWarning(
            $"[JobBroker] Доставка брёвен на чертёж ({x},{y}) отклонена сайтом " +
            $"(принято 0 из {count}, повторов: {streak}). Материал возвращён на землю.");
    }

    /// <summary>
    /// Создать Construction-работу на клетке (этап 3 пайплайна + reconcile).
    /// Дедуп по индексу: повтор — no-op.
    /// </summary>
    public void RegisterConstructionAt(int x, int y)
    {
        if (JobDispatcher.Instance.JobIndex.HasJobAt(x, y, JobTypeId.Construction))
            return;
        JobDispatcher.Instance.RegisterJob(new JobData
        {
            TypeId = JobTypeId.Construction,
            ExecutionType = JobExecutionType.Stationary,
            PriorityTier = JobPriorityTier.Construction,
            TargetX = x,
            TargetY = y,
            StandX = x,
            StandY = y,
            MaxWorkers = 1,
            WorkDuration = WorldTime.SecondsPerHour * 4f
        });
        // §119 P1: пуш роутеру зон (ConstructionManager — внешняя точка, спека task-15).
        int consJobId = JobDispatcher.Instance.JobIndex.TryGetJobIdByPos(x, y, JobTypeId.Construction, out _);
        if (consJobId >= 0)
            ZoneJobRouter.Instance.Push(JobTypeId.Construction, x, y, consJobId);
    }

    public void RegisterFarmPlot(int x, int y)
    {
        JobDispatcher.Instance.RegisterJob(new JobData
        {
            TypeId = JobTypeId.Farming,
            ExecutionType = JobExecutionType.Stationary,
            PriorityTier = JobPriorityTier.Farming,
            TargetX = x,
            TargetY = y,
            StandX = x,
            StandY = y,
            MaxWorkers = 1,
            WorkDuration = Game.Simulation.Jobs.FarmingJobHandler.TillDuration
        });
        // §119 P1: пуш роутеру зон.
        int farmJobId = JobDispatcher.Instance.JobIndex.TryGetJobIdByPos(x, y, JobTypeId.Farming, out _);
        if (farmJobId >= 0)
            ZoneJobRouter.Instance.Push(JobTypeId.Farming, x, y, farmJobId);
    }

    public void RegisterFarmPlotBatch(List<(int X, int Y)> plots)
    {
        if (plots == null || plots.Count == 0) return;
        var batch = new List<JobData>(plots.Count);
        foreach (var (x, y) in plots)
        {
            batch.Add(new JobData
            {
                TypeId = JobTypeId.Farming,
                ExecutionType = JobExecutionType.Stationary,
                PriorityTier = JobPriorityTier.Farming,
                TargetX = x,
                TargetY = y,
                StandX = x,
                StandY = y,
                MaxWorkers = 1,
                WorkDuration = Game.Simulation.Jobs.FarmingJobHandler.TillDuration
            });
        }
        JobDispatcher.Instance.RegisterBatch(batch);
        // §119 P1: батч-пуш роутеру зон.
        var farmIndex = JobDispatcher.Instance.JobIndex;
        foreach (var (x, y) in plots)
        {
            int jid = farmIndex.TryGetJobIdByPos(x, y, JobTypeId.Farming, out _);
            if (jid >= 0) // J-21: jobId == 0 - valid id (TryGetJobIdByPos returns -1 on miss)
                ZoneJobRouter.Instance.Push(JobTypeId.Farming, x, y, jid);
        }
    }

    public void UnregisterFarmPlot(int x, int y) =>
        JobDispatcher.Instance.UnregisterJobByPos(x, y, JobTypeId.Farming);

    public void UnregisterFarmPlotBatch(List<(int X, int Y)> plots) =>
        JobDispatcher.Instance.UnregisterBatchByPositions(plots, JobTypeId.Farming);

    public void RegisterPlanting(int x, int y, int zoneId)
    {
        JobDispatcher.Instance.RegisterJob(new JobData
        {
            TypeId = JobTypeId.Planting,
            ExecutionType = JobExecutionType.Hauling,
            PriorityTier = JobPriorityTier.Farming,
            TargetX = x,
            TargetY = y,
            StandX = x,
            StandY = y,
            MaxWorkers = 1,
            WorkDuration = 10.0f
        });
        // §119 P1: пуш роутеру зон.
        int plantJobId = JobDispatcher.Instance.JobIndex.TryGetJobIdByPos(x, y, JobTypeId.Planting, out _);
        if (plantJobId >= 0)
            ZoneJobRouter.Instance.Push(JobTypeId.Planting, x, y, plantJobId);
    }

    public void UnregisterPlanting(int x, int y) =>
        JobDispatcher.Instance.UnregisterJobByPos(x, y, JobTypeId.Planting);

    public void RegisterHarvest(int x, int y)
    {
        JobDispatcher.Instance.RegisterJob(new JobData
        {
            TypeId = JobTypeId.Harvesting,
            ExecutionType = JobExecutionType.Stationary,
            PriorityTier = JobPriorityTier.Farming,
            TargetX = x,
            TargetY = y,
            StandX = x,
            StandY = y,
            MaxWorkers = 1,
            WorkDuration = 5.0f
        });
        // §119 P1: пуш роутеру зон.
        int harvJobId = JobDispatcher.Instance.JobIndex.TryGetJobIdByPos(x, y, JobTypeId.Harvesting, out _);
        if (harvJobId >= 0)
            ZoneJobRouter.Instance.Push(JobTypeId.Harvesting, x, y, harvJobId);
    }

    public void UnregisterHarvest(int x, int y) =>
        JobDispatcher.Instance.UnregisterJobByPos(x, y, JobTypeId.Harvesting);
}