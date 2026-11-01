using System;
using System.Collections.Concurrent;
using System.Numerics;
using Game.Core;
using Game.Core.WorldStreaming;
using Game.Core.WorldStreaming.Integration;
using Game.UI.Streaming;

namespace Game.Simulation;

/// <summary>
/// §123: мост «мировые планы → агенты». Хендлер работает с чертежами
/// стримингового мира (WallPlan/BuildingPlan/CropPlan/... за кромкой 1024):
/// агент идёт к цели (прямое движение, AgentMovementService §123),
/// работает N секунд, Commit ставит координаты в <see cref="CompletionQueue"/>.
///
/// §124: Commit выполняется в ФОНОВОМ sim-потоке (AgentSimulationThread, параллельно).
/// Раньше он звал _board.Complete → view.AddBlock/RemoveBlock → RepaintCell →
/// TileMapLayer.SetCell — Godot API НЕ из главного потока (порча тайлмапов).
/// Теперь: Commit кладёт (x,y) в потокобезопасную очередь; WorldJobBridge.Tick
/// (главный поток) достаёт и зовёт _board.Complete — все мутации тайлмапов
/// и _board._claimed/_pending на главном потоке.
///
/// Изолирован от островных менеджеров: НЕ трогает ConstructionManager,
/// FarmJobManager и т.д. — весь мир живёт в StreamingWorldView._blocks.
/// </summary>
public sealed class WorldPlanHandler : IJobHandler
{
    private readonly WorldJobBoard _board;
    private const float ReachDist = 48f; // ¾ тайла — как у остальных хендлеров

    /// <summary>Сколько секунд агент работает над мировым чертежом.</summary>
    public const float WorkDurationSeconds = 5.0f;

    /// <summary>
    /// §124: завершённые работы (координаты клеток) — из sim-потока в главный.
    /// Достаёт WorldJobBridge.Tick на главном потоке.
    /// </summary>
    public static readonly ConcurrentQueue<(long X, long Y)> CompletionQueue = new();

    public WorldPlanHandler(WorldJobBoard board)
    {
        _board = board ?? throw new ArgumentNullException(nameof(board));
    }

    public JobTypeId TypeId => JobTypeId.WorldPlan;
    public JobExecutionType ExecutionType => JobExecutionType.Stationary;
    public JobPriorityTier DefaultPriority => JobPriorityTier.Construction;
    public ToolRequirement RequiredTool => ToolRequirement.None;

    public bool CanAgentExecute(int agentIndex, in JobData job, AgentDataPool pool, SimulationContext ctx)
        => _board.HasFreeWork; //PendingCount > ClaimedCount — только int-чтения, атомарно

    public void OnStart(int agentIndex, in JobData job, AgentDataPool pool, SimulationContext ctx)
    {
        pool.WaypointCount[agentIndex] = 0;
        pool.WaypointIndex[agentIndex] = 0;
        pool.States[agentIndex] = AgentState.MovingToSource;
        pool.TargetCellX[agentIndex] = job.TargetX;
        pool.TargetCellY[agentIndex] = job.TargetY;
        pool.TargetPositionX[agentIndex] = job.TargetX * ctx.TileSize + 32f;
        pool.TargetPositionY[agentIndex] = job.TargetY * ctx.TileSize + 32f;
        pool.WorkProgress[agentIndex] = 0f;
        pool.StuckTimer[agentIndex] = 0f;
    }

    public void ExecuteParallel(int agentIndex, float deltaTime, AgentDataPool pool, SimulationContext ctx)
    {
        var state = pool.States[agentIndex];
        if (state == AgentState.MovingToSource)
        {
            // §123: прямое движение к цели (мировая ветка AgentMovementService
            // обходит HierarchicalPathfinder — он не работает за [0,MapWidth)).
            Vector2 target = new(pool.TargetPositionX[agentIndex], pool.TargetPositionY[agentIndex]);
            ctx.Movement.MoveTowards(agentIndex, target, ReachDist, deltaTime, pool, ctx);
        }
        else if (state == AgentState.Working)
        {
            pool.WorkProgress[agentIndex] += deltaTime;
        }
    }

    public void Commit(int agentIndex, float deltaTime, AgentDataPool pool, SimulationContext ctx)
    {
        var state = pool.States[agentIndex];
        int tx = pool.TargetCellX[agentIndex];
        int ty = pool.TargetCellY[agentIndex];

        if (state == AgentState.MovingToSource)
        {
            // Прибытие: переходим к работе.
            Vector2 target = new(pool.TargetPositionX[agentIndex], pool.TargetPositionY[agentIndex]);
            Vector2 pos = new(pool.PositionX[agentIndex], pool.PositionY[agentIndex]);
            if ((pos - target).Length() <= ReachDist)
            {
                pool.States[agentIndex] = AgentState.Working;
                pool.StuckTimer[agentIndex] = 0f;
            }
            else if (pool.StuckTimer[agentIndex] >= 3.0f)
            {
                // Застрял (препятствие/вода в мире) — отпустить работу.
                JobDispatcher.Instance.ReleaseJobWorkerForStuck(agentIndex, pool, ctx);
            }
        }
        else if (state == AgentState.Working && pool.WorkProgress[agentIndex] >= WorkDurationSeconds)
        {
            // §124: sim-поток НЕ трогает Godot API. Координаты — в очередь,
            // WorldJobBridge.Tick (главный поток) сделает board.Complete.
            int jobId = pool.CurrentJobId[agentIndex]; // до сброса состояния
            CompletionQueue.Enqueue((tx, ty));

            // Снять задачу из JobIndex (как Mining/Construction — TryUnregisterJob).
            JobDispatcher.Instance.TryUnregisterJob(jobId);

            pool.States[agentIndex] = AgentState.Idle;
            pool.WorkProgress[agentIndex] = 0f;
            pool.CurrentJobId[agentIndex] = -1;
            pool.CurrentJobType[agentIndex] = JobTypeId.None;
            // Вернуть агента в пул бездельников — как все остальные хендлеры.
            // Раньше агент навсегда пропадал из выдачи: каждый выполненный
            // мировой план «съедал» одного Laborer'а → работа шла всё медленнее.
            if (pool.Classes[agentIndex] != AgentClass.Worker)
                JobDispatcher.Instance.IdleWorkers.AddIdleWorker(agentIndex, pool);
        }
    }

    public void OnCancel(int agentIndex, AgentDataPool pool, SimulationContext ctx)
    {
        // Прерывание (нужды, watchdog): план остаётся, агент освобождается.
        pool.WorkProgress[agentIndex] = 0f;
        pool.CurrentJobId[agentIndex] = 0;
        pool.CurrentJobType[agentIndex] = JobTypeId.None;
    }
}
