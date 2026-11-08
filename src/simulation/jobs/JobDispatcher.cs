using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using Godot;
using Game.Core;

namespace Game.Simulation;

/// <summary>
/// Высокопроизводительный диспетчер задач (Hierarchical Work Matcher).
/// Способен распределять тысячи задач за доли миллисекунды без жестких капов (20/тик)
/// и слепых зон на основе JobSpatialPyramid и FlatIdleWorkerPool.
/// </summary>
public sealed class JobDispatcher
{
    public static JobDispatcher Instance { get; } = new();

    public JobSpatialPyramid JobIndex { get; } = new();
    public FlatIdleWorkerPool IdleWorkers { get; } = new();

    public const int MaxAssignmentsPerTick = 4096;

    /// <summary>Резерв-маркер CAS клейма агента (диспатчер + Worker-выдача).</summary>
    public const int AgentReservedMarker = -2;

    // Пакетный буфер свободных агентов на один проход диспетчеризации
    private const int MaxBatchWorkers = 4096;
    private readonly int[] _workerBatchBuffer = new int[MaxBatchWorkers];
    private readonly List<int> _reverseJobBuffer = new(32);

    // task-2/perf: курсор ротации стартового индекса батча свободных агентов.
    // См. комментарий в DispatchPendingJobs: без ротации «необслуживаемые» агенты в
    // голове FlatIdleWorkerPool выжигали квант 0.8 мс каждый диспатч, а хвост батча
    // не осматривался никогда. Доступ только из sim-потока (диспетчер) — volatile не нужен.
    private int _dispatchScanCursor;

    // Квант максимального времени на один тик диспетчеризации (0.8 мс)
    private const double MaxDispatchBudgetMs = 0.8;

    public void TriggerImmediateDispatch()
    {
    }

    /// <summary>
    /// Главная точка входа: пакетное распределение задач свободным агентам.
    /// Больше никаких 20 назначений за тик — распределяет весь доступный пул
    /// в пределах кванта времени (< 0.8 мс).
    /// </summary>
    public void DispatchPendingJobs(AgentDataPool pool, SimulationContext ctx, int chunkBatchScale = 1)
    {
        int unclaimed = JobIndex.UnclaimedCount;
        int idleCount = IdleWorkers.TotalIdleCount;
        if (unclaimed <= 0 || idleCount <= 0)
            return;

        using (GameProfiler.ScopeCustom("Simulation.JobDispatch"))
        {
            long startTicks = Stopwatch.GetTimestamp();
            double ticksToMs = 1000.0 / Stopwatch.Frequency;

            bool hasStockpileSpace = StockpileManager.Instance.HasFreeSpace;
            bool hasAvailableLogs = GroundItemManager.Instance.HasAvailableLogs;

            int toCollect = Math.Min(idleCount, MaxBatchWorkers);
            int collected = IdleWorkers.CollectIdleWorkers(toCollect, _workerBatchBuffer, pool);
            if (collected <= 0)
                return;

            JobIndex.BeginClaimEpoch();

            // task-2/perf: ротация стартового индекса пакета idle-агентов.
            // Батч обрабатывается до исчерпания кванта (MaxDispatchBudgetMs), а
            // CollectIdleWorkers всегда копирует пул с нулевого индекса и swap-and-pop
            // не двигает агентов, которым работа не подошла. Поэтому при фиксированном
            // старте одни и те же «необслуживаемые» агенты в голове массива выжигали
            // весь квант каждый диспатч, а агенты в хвосте батча не осматривались
            // НИКОГДА — визуально «работу распределяют очень медленно». Тот же приём
            // уже применён в ZoneManager (_zoneScanIndex) и FillPrioritizedUnclaimed.
            // Первый диспатч стартует с 0 — поведение не меняется.
            int scanStart = _dispatchScanCursor % collected;
            if (scanStart < 0)
                scanStart = 0;

            int assigned = 0;
            int examined = 0;

            // J-24: Симметричный быстрый диспатч O(K) при малом числе задач (K <= 32) и большом пуле (N >= 256)
            if (unclaimed <= 32 && collected >= 256)
            {
                int cursor = 0;
                var jobs = JobIndex.SnapshotActiveJobs(_reverseJobBuffer, 32, ref cursor);
                for (int j = 0; j < jobs.Count; j++)
                {
                    double elapsedMs = (Stopwatch.GetTimestamp() - startTicks) * ticksToMs;
                    if (elapsedMs >= MaxDispatchBudgetMs)
                        break;

                    int jobId = jobs[j];
                    if (!JobIndex.TryGetJob(jobId, out var jobData) || !jobData.IsActive)
                        continue;
                    if (jobData.AssignedWorkers >= jobData.MaxWorkers)
                        continue;
                    if (ZoneJobRouter.Instance.IsHeldForWorkerFresh(jobId))
                        continue;

                    if (jobData.TypeId == JobTypeId.StockpileHauling && !hasStockpileSpace)
                        continue;
                    if (jobData.TypeId == JobTypeId.BlueprintDelivery && !hasAvailableLogs)
                        continue;

                    if (!JobRegistry.TryGetHandler(jobData.TypeId, out var handler))
                        continue;

                    int bestAgent = -1;
                    float bestDistSq = float.MaxValue;

                    for (int w = 0; w < collected; w++)
                    {
                        int aIdx = _workerBatchBuffer[w];
                        if (aIdx < 0 || aIdx >= pool.Capacity || pool.States[aIdx] != AgentState.Idle)
                            continue;
                        if (pool.CurrentJobId[aIdx] != -1)
                            continue;

                        if (jobData.RequiredTool != ToolRequirement.None && (pool.EquippedTools[aIdx] & jobData.RequiredTool) == 0)
                            continue;

                        if (!handler.CanAgentExecute(aIdx, jobData, pool, ctx))
                            continue;

                        float dx = pool.PositionX[aIdx] - jobData.TargetX * ctx.TileSize;
                        float dy = pool.PositionY[aIdx] - jobData.TargetY * ctx.TileSize;
                        float d = dx * dx + dy * dy;
                        if (d < bestDistSq)
                        {
                            bestDistSq = d;
                            bestAgent = aIdx;
                        }
                    }

                    if (bestAgent >= 0)
                    {
                        if (Interlocked.CompareExchange(ref pool.CurrentJobId[bestAgent], AgentReservedMarker, -1) == -1)
                        {
                            if (JobIndex.TryClaimJobById(jobId, out var activeJob))
                            {
                                IdleWorkers.RemoveIdleWorker(bestAgent, pool);
                                pool.CurrentJobId[bestAgent] = activeJob.Id;
                                pool.CurrentJobType[bestAgent] = activeJob.TypeId;
                                try
                                {
                                    handler.OnStart(bestAgent, activeJob, pool, ctx);
                                    pool.LastJobCategory[bestAgent] = (int)JobPriorityManager.Instance.GetCategory(activeJob.TypeId);
                                    if (pool.States[bestAgent] == AgentState.Idle)
                                    {
                                        try { handler.OnCancel(bestAgent, pool, ctx); } catch { }
                                        JobIndex.ReleaseWorkerClaim(activeJob.Id);
                                        pool.CurrentJobId[bestAgent] = -1;
                                        pool.CurrentJobType[bestAgent] = JobTypeId.None;
                                        IdleWorkers.AddIdleWorker(bestAgent, pool);
                                    }
                                    else
                                    {
                                        assigned++;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    GD.PrintErr($"[JobDispatcher] ReverseDispatch OnStart error: {ex.Message}");
                                    try { handler.OnCancel(bestAgent, pool, ctx); } catch { }
                                    JobIndex.ReleaseWorkerClaim(activeJob.Id);
                                    pool.CurrentJobId[bestAgent] = -1;
                                    pool.CurrentJobType[bestAgent] = JobTypeId.None;
                                    pool.States[bestAgent] = AgentState.Idle;
                                    IdleWorkers.AddIdleWorker(bestAgent, pool);
                                }
                            }
                            else
                            {
                                pool.CurrentJobId[bestAgent] = -1;
                            }
                        }
                    }
                }

                if (assigned > 0 || jobs.Count > 0)
                {
                    _dispatchScanCursor = (_dispatchScanCursor + 1) % collected;
                    return;
                }
            }
            for (int n = 0; n < collected; n++)
            {
                examined = n + 1;

                // J-01: бюджет проверяется на КАЖДОЙ итерации, а не на кратных 64.
                // Раньше между проверками выполнялось до 64 полных попыток клейма,
                // каждая неограниченной стоимости (обход чанков/макро-секторов с
                // десятками тысяч проверок кандидатов) — отсюда спайки 60–370 мс
                // при заявленном кванте 0.8 мс. Стоимость Stopwatch.GetTimestamp
                // (~20 нс) на два порядка меньше одной попытки клейма, поэтому
                // проверка каждый раз дешевле, чем пропущенный квант.
                if (n > 0)
                {
                    double elapsedMs = (Stopwatch.GetTimestamp() - startTicks) * ticksToMs;
                    if (elapsedMs >= MaxDispatchBudgetMs)
                        break;
                }

                // J-01: MaxAssignmentsPerTick был объявлен, но нигде не применялся —
                // жёсткого потолка назначений за тик фактически не существовало.
                if (assigned >= MaxAssignmentsPerTick)
                    break;

                int i = scanStart + n;
                if (i >= collected)
                    i -= collected;

                int agentIndex = _workerBatchBuffer[i];
                if (agentIndex < 0 || agentIndex >= pool.Capacity || pool.States[agentIndex] != AgentState.Idle)
                    continue;

                // Атомарно резервируем агента
                if (Interlocked.CompareExchange(ref pool.CurrentJobId[agentIndex], AgentReservedMarker, -1) != -1)
                    continue;

                try
                {
                    int ax = pool.CurrentCellX[agentIndex];
                    int ay = pool.CurrentCellY[agentIndex];

                    if (JobIndex.TryClaimJobForAgent(
                        ax, ay,
                        pool.EquippedTools[agentIndex],
                        pool, agentIndex, ctx,
                        hasStockpileSpace, hasAvailableLogs,
                        out var activeJob))
                    {
                        IdleWorkers.RemoveIdleWorker(agentIndex, pool);
                        pool.CurrentJobId[agentIndex] = activeJob.Id;
                        pool.CurrentJobType[agentIndex] = activeJob.TypeId;

                        if (JobRegistry.TryGetHandler(activeJob.TypeId, out var handler))
                        {
                            try
                            {
                                handler.OnStart(agentIndex, activeJob, pool, ctx);
                                pool.LastJobCategory[agentIndex] = (int)JobPriorityManager.Instance.GetCategory(activeJob.TypeId);

                                if (pool.States[agentIndex] == AgentState.Idle)
                                {
                                    // Откат при отказе OnStart.
                                    // J-11: OnStart мог успеть взять резервы (склад,
                                    // предмет, материал) — без OnCancel они утекали
                                    // навсегда: работа оставалась «занятой» ресурсом,
                                    // которого никто не держит. OnCancel читает работу
                                    // из pool.CurrentJobId[agentIndex], поэтому зовём
                                    // его ДО сброса id.
                                    try
                                    {
                                        handler.OnCancel(agentIndex, pool, ctx);
                                    }
                                    catch (Exception cancelEx)
                                    {
                                        GD.PrintErr($"[JobDispatcher] OnCancel при откате: {cancelEx.Message}");
                                    }

                                    int rollbackId = pool.CurrentJobId[agentIndex];
                                    if (rollbackId != -1)
                                    {
                                        JobIndex.ReleaseWorkerClaim(rollbackId);
                                        pool.CurrentJobId[agentIndex] = -1;
                                        pool.CurrentJobType[agentIndex] = JobTypeId.None;
                                    }
                                    IdleWorkers.AddIdleWorker(agentIndex, pool);
                                }
                                else
                                {
                                    assigned++;
                                }
                            }
                            catch (Exception ex)
                            {
                                GD.PrintErr($"[JobDispatcher] OnStart ошибка: {ex.Message}");
                                // J-11: исключение посреди OnStart тоже могло оставить
                                // резервы — снимаем их парным OnCancel, не давая
                                // исключению из самого OnCancel затереть исходную ошибку.
                                try
                                {
                                    handler.OnCancel(agentIndex, pool, ctx);
                                }
                                catch (Exception cancelEx)
                                {
                                    GD.PrintErr($"[JobDispatcher] OnCancel после ошибки: {cancelEx.Message}");
                                }
                                JobIndex.ReleaseWorkerClaim(activeJob.Id);
                                pool.CurrentJobId[agentIndex] = -1;
                                pool.CurrentJobType[agentIndex] = JobTypeId.None;
                                pool.States[agentIndex] = AgentState.Idle;
                                IdleWorkers.AddIdleWorker(agentIndex, pool);
                            }
                        }
                        else
                        {
                            JobIndex.ReleaseWorkerClaim(activeJob.Id);
                            pool.CurrentJobId[agentIndex] = -1;
                            pool.CurrentJobType[agentIndex] = JobTypeId.None;
                            IdleWorkers.AddIdleWorker(agentIndex, pool);
                        }
                    }
                    else
                    {
                        // Работа не найдена — возвращаем агента
                        pool.CurrentJobId[agentIndex] = -1;
                    }
                }
                catch
                {
                    pool.CurrentJobId[agentIndex] = -1;
                    throw;
                }
            }

            // Курсор остаётся в [0, collected): следующий диспатч продолжит с того места,
            // на котором оборвался квант (при полном проходе — с прежней позиции).
            // L-06: наружу отдаём examined/assigned/время цикла. Раньше они
            // считались и умирали здесь, поэтому «медленную выдачу» нельзя было
            // ни подтвердить, ни адресно уменьшить. Это счётчики, а не второй
            // scope — время не задваивается с уже открытым Simulation.JobDispatch.
            // Ноль аллокаций: имя-литерал, запись предвыделена (см. GameProfiler.ScopeCounter).
            GameProfiler.ScopeCounter(
                "Simulation.JobDispatch",
                examined,
                assigned,
                (Stopwatch.GetTimestamp() - startTicks) * ticksToMs);

            _dispatchScanCursor = (_dispatchScanCursor + examined) % collected;
        }
    }

    public int RegisterJob(JobData job) => JobIndex.RegisterJob(job);
    public void RegisterBatch(List<JobData> jobs) => JobIndex.RegisterBatch(jobs);
    public void UnregisterJob(int jobId) => JobIndex.RemoveJob(jobId, out _);
    public bool TryUnregisterJob(int jobId) => JobIndex.RemoveJob(jobId, out _);
    public void UnregisterJobByPos(int x, int y, JobTypeId type) => JobIndex.RemoveJobByPos(x, y, type, out _);
    public void UnregisterBatchByPositions(List<(int X, int Y)> positions, JobTypeId type) => JobIndex.RemoveBatchByPositions(positions, type);

    public void ReleaseJobWorker(int agentIndex, AgentDataPool pool, SimulationContext ctx)
    {
        int jobId = pool.CurrentJobId[agentIndex];
        if (jobId == AgentReservedMarker)
        {
            pool.CurrentJobId[agentIndex] = -1;
            pool.CurrentJobType[agentIndex] = JobTypeId.None;
            return;
        }

        if (jobId != -1)
        {
            if (JobRegistry.TryGetHandler(pool.CurrentJobType[agentIndex], out var handler))
            {
                try
                {
                    handler.OnCancel(agentIndex, pool, ctx);
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[JobDispatcher] OnCancel error #{agentIndex}: {ex.Message}");
                }
            }

            // §119 P2: Worker'а прервали (нужды/watchdog/отмена) — работа
            // возвращается в ОЧЕРЕДЬ ЕГО ЗОНЫ (прогресс в клетке уже сохранён
            // хендлером через CellProgressStore/WorkProgressTracker). Клейм
            // пирамиды тоже снимается: работа снова unclaimed — следующий
            // Worker зоны может её заклеймить, а после 60с она доступна и
            // Laborer'ам. Laborer'у — легаси: только ReleaseWorkerClaim.
            if (pool.Classes[agentIndex] == AgentClass.Worker)
            {
                JobIndex.ReleaseWorkerClaim(jobId);
                ZoneJobRouter.Instance.Unassign(jobId);
            }
            else
                JobIndex.ReleaseWorkerClaim(jobId);
            pool.CurrentJobId[agentIndex] = -1;
            pool.CurrentJobType[agentIndex] = JobTypeId.None;
        }

        pool.WaypointCount[agentIndex] = 0;
        pool.WaypointIndex[agentIndex] = 0;
        pool.States[agentIndex] = AgentState.Idle;
        pool.JobSearchTimer[agentIndex] = 4.0f + (float)ParallelRng.NextDouble() * 4.0f;
        // §119 P2: Worker НЕ возвращается в общий пул бездельников — его
        // выдаёт только его зона (WorkerZoneJobSystem). Laborer — как раньше.
        if (pool.Classes[agentIndex] != AgentClass.Worker)
            IdleWorkers.AddIdleWorker(agentIndex, pool);
    }

    /// <summary>
    /// J-10: единая точка возврата агента ПОСЛЕ завершения работы.
    ///
    /// Раньше каждый хендлер (дерево, камень, стройка, доставка, ферма, сбор,
    /// планировка мира — 10+ мест) безусловно звал
    /// <c>IdleWorkers.AddIdleWorker</c>. Worker зоны при этом уходил в ОБЩИЙ пул
    /// бездельников: зона теряла своего работника, а её работы ещё 60с держались
    /// под Worker'а (ZoneJobRouter.IsHeldForWorkerFresh) и разнорабочим не
    /// отдавались — работы в зоне вставали, хотя снаружи полно свободных людей.
    ///
    /// Теперь класс агента соблюдается: Worker зоны остаётся Worker'ом (его работу
    /// выдаёт WorkerZoneJobSystem по очереди зоны), Laborer — как раньше, в пул.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ReturnAgentAfterJob(int agentIndex, AgentDataPool pool)
    {
        if (pool == null || (uint)agentIndex >= (uint)pool.Capacity)
            return;

        if (pool.Classes[agentIndex] == AgentClass.Worker && pool.AssignedZoneId[agentIndex] >= 0)
        {
            // Worker зоны: в общий пул НЕ кладём, а просим его систему немедленно
            // поискать следующую работу своей зоны.
            pool.JobSearchTimer[agentIndex] = 0f;
            return;
        }

        Instance.IdleWorkers.AddIdleWorker(agentIndex, pool);
    }

    public void ReleaseJobWorkerForStuck(int agentIndex, AgentDataPool pool, SimulationContext ctx, int cooldownMs = 5000)
    {
        int jobId = pool.CurrentJobId[agentIndex];
        if (jobId != -1 && jobId != AgentReservedMarker)
        {
            // §119 P2: Worker'у «упёрся в стену» кулдаун недостижимости НЕ ставим —
            // работа возвращается в очередь его зоны (ReleaseJobWorker ниже),
            // где её подберёт следующий Worker этой зоны. Laborer — как раньше.
            if (pool.Classes[agentIndex] != AgentClass.Worker)
                JobIndex.MarkJobUnreachable(jobId, cooldownMs);
        }
        ReleaseJobWorker(agentIndex, pool, ctx);
    }

    /// <summary>
    /// §119 P2: клейм РАБОТЫ ЗОНЫ для Worker'а (роутер уже выдал ZoneJob).
    /// Фиксирует клейм в реестре по id и инициализирует состояние агента
    /// хендлером (OnStart). Вызывается ТОЛЬКО из WorkerZoneJobSystem; валидацию
    /// работы вызывающий уже провёл — здесь чистая фиксация.
    /// </summary>
    public bool TryAssignWorkerToZoneJob(int agentIndex, int jobId, AgentDataPool pool, SimulationContext ctx)
    {
        // Атомарный резерв агента (тот же маркер, что в DispatchPendingJobs).
        if (Interlocked.CompareExchange(ref pool.CurrentJobId[agentIndex], AgentReservedMarker, -1) != -1)
            return false;
        try
        {
            if (!JobIndex.TryClaimJobById(jobId, out var activeJob))
            {
                pool.CurrentJobId[agentIndex] = -1;
                return false;
            }

            IdleWorkers.RemoveIdleWorker(agentIndex, pool);
            pool.CurrentJobId[agentIndex] = activeJob.Id;
            pool.CurrentJobType[agentIndex] = activeJob.TypeId;

            if (JobRegistry.TryGetHandler(activeJob.TypeId, out var handler))
            {
                try
                {
                    handler.OnStart(agentIndex, activeJob, pool, ctx);
                    pool.LastJobCategory[agentIndex] = (int)JobPriorityManager.Instance.GetCategory(activeJob.TypeId);
                    if (pool.States[agentIndex] == AgentState.Idle)
                    {
                        // OnStart отказал: откат по легаси-сценарию диспатчера.
                        JobIndex.ReleaseWorkerClaim(activeJob.Id);
                        ZoneJobRouter.Instance.Unassign(activeJob.Id);
                        pool.CurrentJobId[agentIndex] = -1;
                        pool.CurrentJobType[agentIndex] = JobTypeId.None;
                        // J-10: соблюдаем класс — Worker зоны остаётся у зоны.
                        ReturnAgentAfterJob(agentIndex, pool);
                        return false;
                    }
                    return true;
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[JobDispatcher] Worker OnStart ошибка: {ex.Message}");
                    JobIndex.ReleaseWorkerClaim(activeJob.Id);
                    ZoneJobRouter.Instance.Unassign(activeJob.Id);
                    pool.CurrentJobId[agentIndex] = -1;
                    pool.CurrentJobType[agentIndex] = JobTypeId.None;
                    pool.States[agentIndex] = AgentState.Idle;
                    ReturnAgentAfterJob(agentIndex, pool);
                    return false;
                }
            }

            JobIndex.ReleaseWorkerClaim(activeJob.Id);
            ZoneJobRouter.Instance.Unassign(activeJob.Id);
            pool.CurrentJobId[agentIndex] = -1;
            pool.CurrentJobType[agentIndex] = JobTypeId.None;
            ReturnAgentAfterJob(agentIndex, pool);
            return false;
        }
        catch
        {
            pool.CurrentJobId[agentIndex] = -1;
            throw;
        }
    }
}
