using System;
using System.Numerics;
using Game.Core;

namespace Game.Simulation.Jobs;

/// <summary>
/// Доставка материалов на площадку (task-19: ТИПИЗИРОВАНА).
/// Работа везёт ОДИН материал — <c>JobData.TargetItemId</c> (бревно, камень и т.д.),
/// а не только брёвна. Тип материала едет через всю цепочку:
/// резерв на земле/складе (typed API) → ноша → JobBroker.DeliverMaterialToBlueprint
/// → ConstructionSite._delivered[item].
///
/// Механика прежняя: один активный delivery-джоб на клетку, поэтому материалы
/// площадки возятся последовательно (Log, затем Stone — порядок RequiredMaterials);
/// сайт копит потипно, Supplying→Building только когда довезено ВСЁ.
///
/// <c>CarriedItemId</c> используется как «запланированный/несомый материал»: при
/// резерве (ещё без ноши) там лежит ожидаемый тип с CarriedItemCount=0 — это нужно
/// Commit'у, чтобы типизированно забрать со склада. Другие хендлеры читают пару
/// (id,count), поэтому count=0 безопасен.
/// </summary>
public sealed class BlueprintDeliveryJobHandler : IJobHandler
{
    private const float ReachDist = 20.0f;
    private const float MaxCarryWeight = 25.0f;

    public JobTypeId TypeId => JobTypeId.BlueprintDelivery;
    public JobExecutionType ExecutionType => JobExecutionType.Hauling;
    public JobPriorityTier DefaultPriority => JobPriorityTier.BlueprintSupply;
    public ToolRequirement RequiredTool => ToolRequirement.None;

    private static ItemId JobItem(in JobData job)
        => job.TargetItemId != ItemId.None ? job.TargetItemId : ItemId.Log;

    public bool CanAgentExecute(int agentIndex, in JobData job, AgentDataPool pool, SimulationContext ctx)
    {
        ItemId item = JobItem(in job);

        // Уже несёт этот материал — источник не нужен.
        bool carriesItem = pool != null && (uint)agentIndex < (uint)pool.Capacity &&
                           pool.CarriedItemId[agentIndex] == item &&
                           pool.CarriedItemCount[agentIndex] > 0;
        if (!carriesItem &&
            !GroundItemManager.Instance.HasAvailableItemsOfType(item) &&
            !StockpileManager.Instance.CanReserveAtLeast(item, 1))
            return false;

        // Поднос — только на клетках в стадии Supplying.
        if (ConstructionManager.Instance.TryGetSite(job.TargetX, job.TargetY, out var site))
        {
            if (site.Stage != ConstructionStage.Supplying)
                return false;
        }
        else if (!ConstructionManager.Instance.IsStageAllowed(job.TargetX, job.TargetY, JobTypeId.BlueprintDelivery))
        {
            return false;
        }

        if (ctx != null
            && (uint)job.TargetX < (uint)ctx.MapWidth && (uint)job.TargetY < (uint)ctx.MapHeight)
        {
            if (ctx.TreeOnGrass != null && ctx.TreeOnGrass[job.TargetX, job.TargetY])
                return false;
            if (ctx.StoneOnGrass != null && ctx.StoneOnGrass[job.TargetX, job.TargetY])
                return false;
            if (ctx.Ground != null && ctx.Ground[job.TargetX, job.TargetY] != TileType.Grass)
                return false;

            // Чужие предметы (не наш материал) мешают подносу; свой материал — нет.
            ItemId itemOnGround = GroundItemManager.Instance.PeekItemAt(job.TargetX, job.TargetY);
            if (itemOnGround != ItemId.None && itemOnGround != item)
                return false;
        }
        return true;
    }

    public void OnStart(int agentIndex, in JobData job, AgentDataPool pool, SimulationContext ctx)
    {
        pool.WaypointCount[agentIndex] = 0;
        pool.WaypointIndex[agentIndex] = 0;

        ItemId item = JobItem(in job);
        int remaining = job.TargetItemCount - job.CurrentDeliveredCount;
        if (remaining <= 0)
        {
            ReturnIdle(agentIndex, pool);
            return;
        }

        // Агент уже держит ЭТОТ материал — сразу несёт на стройку.
        if (pool.CarriedItemId[agentIndex] == item && pool.CarriedItemCount[agentIndex] > 0)
        {
            pool.FoodFromStockpile[agentIndex] = false;
            pool.States[agentIndex] = AgentState.MovingToTarget;
            pool.TargetCellX[agentIndex] = job.TargetX;
            pool.TargetCellY[agentIndex] = job.TargetY;
            (int X, int Y) stand = (job.TargetX, job.TargetY);
            JobBroker.FindStandPosition(job.TargetX, job.TargetY, ctx, out stand);
            pool.TargetPositionX[agentIndex] = stand.X * ctx.TileSize + 32f;
            pool.TargetPositionY[agentIndex] = stand.Y * ctx.TileSize + 32f;
            pool.StuckTimer[agentIndex] = 0f;
            return;
        }

        float weight = ItemRegistry.Get(item).Weight;
        Vector2 pos = pool.GetPosition(agentIndex);
        float needWeight = Math.Min(MaxCarryWeight, remaining * weight);
        int needCount = Math.Max(1, (int)(needWeight / weight));

        bool reservedFromGround = GroundItemManager.Instance.TryReserveGroundItems(
            pos, needWeight, true, item, out var itemCell, out _, out int resCount);
        bool reservedFromStock = false;
        (int X, int Y) stockCell = (-1, -1);
        int stockResCount = 0;

        if (!reservedFromGround)
        {
            reservedFromStock = StockpileManager.Instance.TryReserveStockpileItem(
                pos, item, needCount, 0, out stockCell, out stockResCount);
        }

        if (reservedFromGround)
        {
            // FIX (bughunt 09-1): запоминаем ИСТОЧНИК резерва (FoodFromStockpile) и
            // ожидаемый тип (CarriedItemId с count=0) — Commit заберёт типизированно.
            pool.FoodFromStockpile[agentIndex] = false;
            pool.CarriedItemId[agentIndex] = item;
            pool.CarriedItemCount[agentIndex] = 0;
            pool.States[agentIndex] = AgentState.MovingToSource;
            pool.SourceCellX[agentIndex] = itemCell.X;
            pool.SourceCellY[agentIndex] = itemCell.Y;
            pool.TargetCellX[agentIndex] = job.TargetX;
            pool.TargetCellY[agentIndex] = job.TargetY;
            pool.ReservedItemCount[agentIndex] = resCount;
            pool.TargetPositionX[agentIndex] = itemCell.X * ctx.TileSize + 32f;
            pool.TargetPositionY[agentIndex] = itemCell.Y * ctx.TileSize + 32f;
            pool.StuckTimer[agentIndex] = 0f;
        }
        else if (reservedFromStock)
        {
            pool.FoodFromStockpile[agentIndex] = true;
            pool.CarriedItemId[agentIndex] = item;
            pool.CarriedItemCount[agentIndex] = 0;
            pool.States[agentIndex] = AgentState.MovingToSource;
            pool.SourceCellX[agentIndex] = stockCell.X;
            pool.SourceCellY[agentIndex] = stockCell.Y;
            pool.TargetCellX[agentIndex] = job.TargetX;
            pool.TargetCellY[agentIndex] = job.TargetY;
            pool.ReservedItemCount[agentIndex] = stockResCount;
            pool.TargetPositionX[agentIndex] = stockCell.X * ctx.TileSize + 32f;
            pool.TargetPositionY[agentIndex] = stockCell.Y * ctx.TileSize + 32f;
            pool.StuckTimer[agentIndex] = 0f;
        }
        else
        {
            // Материала нет: работа остаётся в индексе, агент — на кулдаун.
            pool.FoodFromStockpile[agentIndex] = false;
            pool.CarriedItemId[agentIndex] = ItemId.None;
            int failId = pool.CurrentJobId[agentIndex];
            if (failId != -1)
            {
                JobDispatcher.Instance.JobIndex.MarkJobUnreachable(failId, 2000);
                JobDispatcher.Instance.JobIndex.ReleaseWorkerClaim(failId);
                pool.CurrentJobId[agentIndex] = -1;
                pool.CurrentJobType[agentIndex] = JobTypeId.None;
            }
            pool.States[agentIndex] = AgentState.Idle;
            pool.JobSearchTimer[agentIndex] = 2.0f + (float)ParallelRng.NextDouble() * 2.0f;
            JobDispatcher.ReturnAgentAfterJob(agentIndex, pool); // J-10
        }
    }

    public void ExecuteParallel(int agentIndex, float deltaTime, AgentDataPool pool, SimulationContext ctx)
    {
        Vector2 target = new(pool.TargetPositionX[agentIndex], pool.TargetPositionY[agentIndex]);
        ctx.Movement.MoveTowards(agentIndex, target, ReachDist, deltaTime, pool, ctx);
    }

    public void Commit(int agentIndex, float deltaTime, AgentDataPool pool, SimulationContext ctx)
    {
        var state = pool.States[agentIndex];
        int tx = pool.TargetCellX[agentIndex];
        int ty = pool.TargetCellY[agentIndex];
        Vector2 standTarget = new(pool.TargetPositionX[agentIndex], pool.TargetPositionY[agentIndex]);
        Vector2 blueprintCenter = new(tx * ctx.TileSize + 32f, ty * ctx.TileSize + 32f);
        Vector2 agentPos = pool.GetPosition(agentIndex);

        bool reached = (agentPos - standTarget).Length() <= ReachDist ||
                       (state == AgentState.MovingToTarget && (agentPos - blueprintCenter).Length() <= 64.0f);

        if (reached)
        {
            pool.StuckTimer[agentIndex] = 0f;
            if (state == AgentState.MovingToSource)
            {
                TakeReservedMaterial(agentIndex, pool, ctx);
            }
            else if (state == AgentState.MovingToTarget)
            {
                int carried = pool.CarriedItemCount[agentIndex];
                ItemId carriedId = pool.CarriedItemId[agentIndex];
                if (carried > 0 && carriedId != ItemId.None)
                    JobBroker.Instance.DeliverMaterialToBlueprint(tx, ty, carriedId, carried);

                pool.CarriedItemCount[agentIndex] = 0;
                pool.CarriedItemId[agentIndex] = ItemId.None;
                JobDispatcher.Instance.ReleaseJobWorker(agentIndex, pool, ctx);
            }
        }
        else if (pool.StuckTimer[agentIndex] >= 3.0f)
        {
            JobDispatcher.Instance.ReleaseJobWorkerForStuck(agentIndex, pool, ctx);
        }
    }

    /// <summary>Забрать зарезервированный материал (типизированно) и идти к площадке.</summary>
    private void TakeReservedMaterial(int agentIndex, AgentDataPool pool, SimulationContext ctx)
    {
        int sx = pool.SourceCellX[agentIndex];
        int sy = pool.SourceCellY[agentIndex];
        int count = pool.ReservedItemCount[agentIndex];
        ItemId expected = pool.CarriedItemId[agentIndex];
        int taken = 0;
        ItemId takenId = ItemId.None;

        if (expected == ItemId.None)
        {
            JobDispatcher.Instance.ReleaseJobWorker(agentIndex, pool, ctx);
            return;
        }

        if (pool.FoodFromStockpile[agentIndex])
        {
            // task-19: типизированное снятие — со склада уходит именно ожидаемый
            // материал; при чужом типе в слоте резерв снимается без списания.
            if (StockpileManager.Instance.TryWithdrawReservedItemTyped(
                    sx, sy, expected, count, out int fromStock))
            {
                taken = fromStock;
                takenId = expected;
                int left = count - taken;
                if (left > 0)
                    StockpileManager.Instance.CancelWithdrawalReservation(sx, sy, left);
            }
        }
        else
        {
            // Земля: берём атомарно с типом и сверяем с ожидаемым материалом.
            if (GroundItemManager.Instance.PeekItemAt(sx, sy) == expected)
            {
                (ItemId item, int got) = GroundItemManager.Instance.TakeItemsWithType(sx, sy, count);
                if (item == expected)
                {
                    taken = got;
                    takenId = expected;
                }
                else if (got > 0)
                {
                    // Между peek и take стак подменили — возвращаем чужое как есть.
                    GroundItemManager.Instance.SpawnItems(sx, sy, item, got);
                }
            }
            if (taken > 0)
            {
                int left = count - taken;
                if (left > 0)
                    GroundItemManager.Instance.ReleaseReservation(sx, sy, left);
            }
        }

        if (taken > 0)
        {
            int tx = pool.TargetCellX[agentIndex];
            int ty = pool.TargetCellY[agentIndex];
            pool.CarriedItemId[agentIndex] = takenId;
            pool.CarriedItemCount[agentIndex] = taken;
            pool.ReservedItemCount[agentIndex] = 0;
            pool.States[agentIndex] = AgentState.MovingToTarget;
            (int X, int Y) stand = (tx, ty);
            JobBroker.FindStandPosition(tx, ty, ctx, out stand);
            pool.TargetPositionX[agentIndex] = stand.X * ctx.TileSize + 32f;
            pool.TargetPositionY[agentIndex] = stand.Y * ctx.TileSize + 32f;
        }
        else
        {
            pool.CarriedItemId[agentIndex] = ItemId.None;
            JobDispatcher.Instance.ReleaseJobWorker(agentIndex, pool, ctx);
        }
    }

    public void OnCancel(int agentIndex, AgentDataPool pool, SimulationContext ctx)
    {
        if (pool.States[agentIndex] == AgentState.MovingToSource)
        {
            int sx = pool.SourceCellX[agentIndex];
            int sy = pool.SourceCellY[agentIndex];
            int count = pool.ReservedItemCount[agentIndex];
            if (pool.FoodFromStockpile[agentIndex])
                StockpileManager.Instance.CancelWithdrawalReservation(sx, sy, count);
            else
                GroundItemManager.Instance.ReleaseReservation(sx, sy, count);
            pool.ReservedItemCount[agentIndex] = 0;
            pool.FoodFromStockpile[agentIndex] = false;
            pool.CarriedItemId[agentIndex] = ItemId.None;
            pool.CarriedItemCount[agentIndex] = 0;
        }
        else if (pool.CarriedItemCount[agentIndex] > 0)
        {
            int cx = Math.Clamp((int)(pool.PositionX[agentIndex] / ctx.TileSize), 0, ctx.MapWidth - 1);
            int cy = Math.Clamp((int)(pool.PositionY[agentIndex] / ctx.TileSize), 0, ctx.MapHeight - 1);
            GroundItemManager.Instance.SpawnItems(cx, cy, pool.CarriedItemId[agentIndex], pool.CarriedItemCount[agentIndex]);
            pool.CarriedItemCount[agentIndex] = 0;
            pool.CarriedItemId[agentIndex] = ItemId.None;
        }
    }

    private static void ReturnIdle(int agentIndex, AgentDataPool pool)
    {
        int failId = pool.CurrentJobId[agentIndex];
        if (failId != -1)
        {
            JobDispatcher.Instance.JobIndex.ReleaseWorkerClaim(failId);
            pool.CurrentJobId[agentIndex] = -1;
            pool.CurrentJobType[agentIndex] = JobTypeId.None;
        }
        pool.States[agentIndex] = AgentState.Idle;
        pool.JobSearchTimer[agentIndex] = 2.0f + (float)ParallelRng.NextDouble() * 2.0f;
        JobDispatcher.ReturnAgentAfterJob(agentIndex, pool); // J-10
    }
}