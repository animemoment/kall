using System;
using System.Numerics;
using Game.Core;
using Game.Core.World;
using Game.Core.WorldStreaming;

namespace Game.Simulation.Jobs;

/// <summary>
/// Добыча руды из жилы (task-4 для острова, task-8 для стримингового мира).
/// Жила — ХРАНИЛИЩЕ с запасом:
///  - агент приходит и работает 4 игровых часа (<see cref="BuildConfig.MineDurationSec"/>);
///  - за цикл снимает порцию 8–15, на землю падает дроп ItemId руды;
///  - OreState пересчитывается по порогам; при смене — событие перерисовки;
///  - запас 0 → клетка очищается (остров: MapData.OreType=0; мир: мутация в
///    OreWorldState), метка/задача снимаются, тайл гаснет;
///  - запас остался → следующий цикл (метка живёт).
///
/// Два мира:
///  - остров (target внутри ctx.MapWidth/Height) — массивы MapData через ctx;
///  - мир за кромкой — детерминированное поле <see cref="OreWorldField"/> +
///    мутации <see cref="OreWorldState"/> (координаты job'а — локальный холст).
/// </summary>
public sealed class OreMiningJobHandler : IJobHandler
{
    private const float ReachDist = 20.0f;

    public JobTypeId TypeId => JobTypeId.OreMining;
    public JobExecutionType ExecutionType => JobExecutionType.Stationary;
    public JobPriorityTier DefaultPriority => JobPriorityTier.TreeChopping;
    public ToolRequirement RequiredTool => ToolRequirement.None;

    public bool CanAgentExecute(int agentIndex, in JobData job, AgentDataPool pool, SimulationContext ctx)
    {
        if (ctx == null)
            return false;

        if (IsIslandCell(job.TargetX, job.TargetY, ctx))
        {
            if (ctx.OreType == null)
                return false;
            int idx = job.TargetY * ctx.MapWidth + job.TargetX;
            if (ctx.OreType[idx] == OreConfig.None)
                return false;
            return ctx.OreReserve == null || ctx.OreReserve[idx] > 0;
        }

        // Мир за кромкой: валидность = руда в детерминированном поле мира.
        ulong seed = WorldModel.Seed;
        uint ver = WorldModel.Version;
        if (seed == 0)
            return false;
        return OreWorldField.GetEffectiveTypeLocal(job.TargetX, job.TargetY, seed, ver) != OreConfig.None;
    }

    public void OnStart(int agentIndex, in JobData job, AgentDataPool pool, SimulationContext ctx)
    {
        pool.WaypointCount[agentIndex] = 0;
        pool.WaypointIndex[agentIndex] = 0;

        pool.States[agentIndex] = AgentState.MovingToSource;
        pool.TargetCellX[agentIndex] = job.TargetX;
        pool.TargetCellY[agentIndex] = job.TargetY;
        pool.TargetPositionX[agentIndex] = job.StandX * ctx.TileSize + 32f;
        pool.TargetPositionY[agentIndex] = job.StandY * ctx.TileSize + 32f;
        // Продолжаем с per-cell прогресса (прерывание цикла ≠ сброс в ноль).
        pool.WorkProgress[agentIndex] =
            OreJobManager.Instance.MineProgress.GetFraction(job.TargetX, job.TargetY) * BuildConfig.MineDurationSec;
        pool.StuckTimer[agentIndex] = 0f;
    }

    public void ExecuteParallel(int agentIndex, float deltaTime, AgentDataPool pool, SimulationContext ctx)
    {
        var state = pool.States[agentIndex];
        if (state == AgentState.MovingToSource)
        {
            Vector2 target = new(pool.TargetPositionX[agentIndex], pool.TargetPositionY[agentIndex]);
            ctx.Movement.MoveTowards(agentIndex, target, ReachDist, deltaTime, pool, ctx);
        }
        else if (state == AgentState.Working)
        {
            pool.WorkProgress[agentIndex] += deltaTime;
            WorkProgressTracker.Instance.ReportFraction(
                pool.TargetCellX[agentIndex], pool.TargetCellY[agentIndex],
                pool.WorkProgress[agentIndex] / BuildConfig.MineDurationSec);
        }
    }

    public void Commit(int agentIndex, float deltaTime, AgentDataPool pool, SimulationContext ctx)
    {
        var state = pool.States[agentIndex];
        int tx = pool.TargetCellX[agentIndex];
        int ty = pool.TargetCellY[agentIndex];

        if (state == AgentState.MovingToSource)
        {
            Vector2 target = new(pool.TargetPositionX[agentIndex], pool.TargetPositionY[agentIndex]);
            if ((pool.GetPosition(agentIndex) - target).Length() <= ReachDist)
            {
                pool.States[agentIndex] = AgentState.Working;
                pool.StuckTimer[agentIndex] = 0f;
            }
            else if (pool.StuckTimer[agentIndex] >= 3.0f)
            {
                JobDispatcher.Instance.ReleaseJobWorkerForStuck(agentIndex, pool, ctx);
            }
            return;
        }

        if (state != AgentState.Working || pool.WorkProgress[agentIndex] < BuildConfig.MineDurationSec)
            return;

        // Цикл закончен: порция 8–15 (ParallelRng: ctx.Random не thread-safe).
        int wanted = ParallelRng.Next(OreConfig.PortionMin, OreConfig.PortionMaxExclusive);
        OreExtractResult result;
        bool isIsland = IsIslandCell(tx, ty, ctx);
        if (isIsland)
        {
            result = OreJobManager.Instance.TryExtract(tx, ty, wanted, ctx);
        }
        else
        {
            ulong seed = WorldModel.Seed;
            uint ver = WorldModel.Version;
            result = OreJobManager.Instance.TryExtractWorld(tx, ty, wanted, seed, ver);
        }

        if (result.Extracted > 0 && result.Item != ItemId.None)
        {
            (int X, int Y) dropPos = isIsland ? FindDropPosition(tx, ty, ctx) : (tx, ty);
            GroundItemManager.Instance.SpawnItems(dropPos.X, dropPos.Y, result.Item, result.Extracted);
        }

        WorkProgressTracker.Instance.Clear(tx, ty);
        OreJobManager.Instance.MineProgress.Clear(tx, ty);

        // Эталон камня: stale unregister → освободить claim вручную.
        int jobId = pool.CurrentJobId[agentIndex];
        if (jobId != -1)
        {
            if (!JobDispatcher.Instance.TryUnregisterJob(jobId))
                JobDispatcher.Instance.JobIndex.ReleaseWorkerClaim(jobId);
        }
        pool.CurrentJobId[agentIndex] = -1;
        pool.CurrentJobType[agentIndex] = JobTypeId.None;
        pool.States[agentIndex] = AgentState.Idle;
        JobDispatcher.ReturnAgentAfterJob(agentIndex, pool); // J-10: Worker зоны не уходит в общий пул

        // Жила не выработана — следующий цикл (метка и тайл остаются).
        if (!result.Depleted && result.Extracted > 0)
        {
            if (isIsland)
                OreJobManager.Instance.RequeueCycle(tx, ty);
            else
                OreJobManager.Instance.MarkOreWorld(tx, ty);
        }
    }

    public void OnCancel(int agentIndex, AgentDataPool pool, SimulationContext ctx)
    {
        // Прерывание сохраняет долю цикла в клетку: следующий агент продолжит.
        OreJobManager.Instance.MineProgress.SaveFraction(
            pool.TargetCellX[agentIndex], pool.TargetCellY[agentIndex],
            pool.WorkProgress[agentIndex] / BuildConfig.MineDurationSec);
        WorkProgressTracker.Instance.Clear(pool.TargetCellX[agentIndex], pool.TargetCellY[agentIndex]);
    }

    private static bool IsIslandCell(int tx, int ty, SimulationContext ctx)
        => (uint)tx < (uint)ctx.MapWidth && (uint)ty < (uint)ctx.MapHeight;

    /// <summary>
    /// Куда упадёт дроп на острове. Обычно — на саму клетку (руда проходима).
    /// Если тут стройка/грядка — на соседнюю свободную траву (как у камня).
    /// </summary>
    private static (int X, int Y) FindDropPosition(int tx, int ty, SimulationContext ctx)
    {
        bool isSiteOrFarm = ConstructionManager.Instance.IsSiteAt(tx, ty) ||
                            FarmJobManager.Instance.IsPlotMarked(tx, ty) ||
                            FarmJobManager.Instance.IsGardenBed(tx, ty);
        if (!isSiteOrFarm)
            return (tx, ty);

        for (int oy = -1; oy <= 1; oy++)
        {
            for (int ox = -1; ox <= 1; ox++)
            {
                if (ox == 0 && oy == 0) continue;
                int nx = tx + ox, ny = ty + oy;
                if ((uint)nx >= (uint)ctx.MapWidth || (uint)ny >= (uint)ctx.MapHeight)
                    continue;
                if (ctx.Ground[nx, ny] != TileType.Grass)
                    continue;
                if (ctx.SolidWalls != null && ctx.SolidWalls[nx, ny])
                    continue;
                if (ctx.TreeOnGrass != null && ctx.TreeOnGrass[nx, ny])
                    continue;
                if (ConstructionManager.Instance.IsSiteAt(nx, ny) ||
                    FarmJobManager.Instance.IsPlotMarked(nx, ny))
                    continue;
                return (nx, ny);
            }
        }
        return (tx, ty);
    }
}