using System;
using System.Numerics;
using Game.Core;

namespace Game.Simulation.Jobs;

/// <summary>
/// Производство v1 (task-20): агент у станка перерабатывает сырьё по рецепту.
///
/// Цикл работы (IJobHandler-протокол):
///   OnStart: резервирует входы (склад → земля) и идёт к источнику;
///   Commit MovingToSource: забирает зарезервированное, идёт к станку;
///   Commit MovingToTarget: у станка → AgentState.Working;
///   ExecuteParallel Working: копит WorkProgress, рисует ProcessOfWork;
///   Commit Working (progress ≥ DurationSec): списывает входы, кладёт выход на
///   землю у станка (hauling увозит на склад), снимает производственную работу.
///
/// Работа регистрируется ProductionManager по потребности: если сырья нет —
/// работы нет и станок простаивает (не крутится вхолостую).
/// </summary>
public sealed class ProductionJobHandler : IJobHandler
{
    private const float ReachDist = 20.0f;
    private const float MaxCarryWeight = 25.0f;
    private const float StuckTimeoutSec = 3.0f;

    public JobTypeId TypeId => JobTypeId.Production;
    public JobExecutionType ExecutionType => JobExecutionType.Stationary;
    public JobPriorityTier DefaultPriority => JobPriorityTier.Production;
    public ToolRequirement RequiredTool => ToolRequirement.None;

    public bool CanAgentExecute(int agentIndex, in JobData job, AgentDataPool pool, SimulationContext ctx)
    {
        if (!StructureManager.Instance.TryGetOperationalStructure(job.TargetX, job.TargetY, out var station))
            return false;
        Recipe recipe = RecipeRegistry.FindRecipeByInput(station.Type, job.TargetItemId)
                        ?? RecipeRegistry.GetForStation(station.Type);
        if (recipe == null)
            return false;
        // Работа берётся только если сырьё реально есть (склад или земля).
        return ProductionManager.HasInputsAvailable(recipe);
    }

    public void OnStart(int agentIndex, in JobData job, AgentDataPool pool, SimulationContext ctx)
    {
        pool.WaypointCount[agentIndex] = 0;
        pool.WaypointIndex[agentIndex] = 0;
        pool.CarriedItemId[agentIndex] = ItemId.None;
        pool.CarriedItemCount[agentIndex] = 0;
        pool.ReservedItemCount[agentIndex] = 0;
        pool.FoodFromStockpile[agentIndex] = false;
        pool.WorkProgress[agentIndex] = 0f;
        pool.StuckTimer[agentIndex] = 0f;
        // Целевая клетка — станок (по ней идёт визуал прогресса и снятие работы).
        pool.TargetCellX[agentIndex] = job.TargetX;
        pool.TargetCellY[agentIndex] = job.TargetY;

        if (!StructureManager.Instance.TryGetOperationalStructure(job.TargetX, job.TargetY, out var station))
        {
            FailStart(agentIndex, pool, ctx);
            return;
        }
        Recipe recipe = RecipeRegistry.FindRecipeByInput(station.Type, job.TargetItemId)
                        ?? RecipeRegistry.GetForStation(station.Type);
        if (recipe == null)
        {
            FailStart(agentIndex, pool, ctx);
            return;
        }

        if (!TryReserveInputs(agentIndex, pool, recipe, job.TargetItemId,
                out ItemId inputItem, out (int X, int Y) sourceCell, out bool fromStock))
        {
            // AUDIT task-17 (P3-1): именно отказ РЕЗЕРВА (а не отсутствие станции) может
            // повторяться бесконечно: ProductionManager видел «входы есть» по суммарным
            // тоталам и держал работу. Снимаем работу и ставим станции кулдаун —
            // Tick пересоздаст её по факту доступности, но не раньше чем через 5 с.
            FailStartReservation(agentIndex, pool, ctx);
            return;
        }

        // CarriedItemId до подбора хранит НАМЕРЕНИЕ «что берём» (см. PickUpReserved).
        pool.CarriedItemId[agentIndex] = inputItem;
        pool.ReservedItemCount[agentIndex] = recipe.InputCount;
        pool.FoodFromStockpile[agentIndex] = fromStock;
        pool.SourceCellX[agentIndex] = sourceCell.X;
        pool.SourceCellY[agentIndex] = sourceCell.Y;
        pool.States[agentIndex] = AgentState.MovingToSource;
        pool.TargetPositionX[agentIndex] = sourceCell.X * ctx.TileSize + 32f;
        pool.TargetPositionY[agentIndex] = sourceCell.Y * ctx.TileSize + 32f;
    }

    /// <summary>
    /// Резерв входов рецепта. Сначала склад (точное количество, типизированный
    /// резерв), затем земля. Нужно РОВНО InputCount из одного источника:
    /// частичный резерв снимается (иначе висящие брони).
    /// </summary>
    private static bool TryReserveInputs(int agentIndex, AgentDataPool pool, Recipe recipe, ItemId preferredItem,
        out ItemId inputItem, out (int X, int Y) sourceCell, out bool fromStock)
    {
        inputItem = ItemId.None;
        sourceCell = (-1, -1);
        fromStock = false;
        Vector2 pos = pool.GetPosition(agentIndex);
        int need = recipe.InputCount;

        // 1) Склад: сперва preferredItem (если задан и подходит), затем остальные варианты.
        if (preferredItem != ItemId.None && recipe.AcceptsInput(preferredItem))
        {
            if (TryReserveStockpile(pos, preferredItem, need, out sourceCell))
            {
                inputItem = preferredItem;
                fromStock = true;
                return true;
            }
        }
        for (int i = 0; i < recipe.InputItems.Count; i++)
        {
            ItemId item = recipe.InputItems[i];
            if (item == preferredItem)
                continue;
            if (TryReserveStockpile(pos, item, need, out sourceCell))
            {
                inputItem = item;
                fromStock = true;
                return true;
            }
        }

        // 2) Земля: сперва preferredItem, затем остальные варианты.
        if (preferredItem != ItemId.None && recipe.AcceptsInput(preferredItem))
        {
            if (TryReserveGround(pos, preferredItem, need, out sourceCell))
            {
                inputItem = preferredItem;
                fromStock = false;
                return true;
            }
        }
        for (int i = 0; i < recipe.InputItems.Count; i++)
        {
            ItemId item = recipe.InputItems[i];
            if (item == preferredItem)
                continue;
            if (TryReserveGround(pos, item, need, out sourceCell))
            {
                inputItem = item;
                fromStock = false;
                return true;
            }
        }

        return false;
    }

    private static bool TryReserveStockpile(Vector2 pos, ItemId item, int need, out (int X, int Y) cell)
    {
        cell = (-1, -1);
        if (StockpileManager.Instance.GetLiveItemCount(item) < need)
            return false;
        int accepted = 0;
        bool ok = StockpileManager.Instance.TryReserveStockpileItem(
            pos, item, need, 0, out cell, out accepted);
        if (ok && accepted >= need)
            return true;
        if (accepted > 0)
            StockpileManager.Instance.CancelWithdrawalReservation(cell.X, cell.Y, accepted);
        cell = (-1, -1);
        return false;
    }

    private static bool TryReserveGround(Vector2 pos, ItemId item, int need, out (int X, int Y) cell)
    {
        cell = (-1, -1);
        if (GroundItemManager.Instance.GetAvailableCount(item) < need)
            return false;
        float weight = need * ItemRegistry.Get(item).Weight;
        if (weight > MaxCarryWeight)
            return false;
        bool ok = GroundItemManager.Instance.TryReserveGroundItems(
            pos, weight, false, item, 0, out cell, out var gotItem, out int reserved);
        if (ok && gotItem == item && reserved >= need)
            return true;
        if (reserved > 0)
            GroundItemManager.Instance.ReleaseReservation(cell.X, cell.Y, reserved);
        cell = (-1, -1);
        return false;
    }

    public void ExecuteParallel(int agentIndex, float deltaTime, AgentDataPool pool, SimulationContext ctx)
    {
        var state = pool.States[agentIndex];
        if (state == AgentState.MovingToSource || state == AgentState.MovingToTarget)
        {
            Vector2 target = new(pool.TargetPositionX[agentIndex], pool.TargetPositionY[agentIndex]);
            ctx.Movement.MoveTowards(agentIndex, target, ReachDist, deltaTime, pool, ctx);
        }
        else if (state == AgentState.Working)
        {
            pool.WorkProgress[agentIndex] += deltaTime;
            Recipe recipe = GetRecipeAt(pool.TargetCellX[agentIndex], pool.TargetCellY[agentIndex], pool.CarriedItemId[agentIndex]);
            float duration = recipe != null ? recipe.DurationSec : 1f;
            WorkProgressTracker.Instance.ReportFraction(
                pool.TargetCellX[agentIndex], pool.TargetCellY[agentIndex],
                pool.WorkProgress[agentIndex] / duration);
        }
    }

    public void Commit(int agentIndex, float deltaTime, AgentDataPool pool, SimulationContext ctx)
    {
        var state = pool.States[agentIndex];
        Vector2 target = new(pool.TargetPositionX[agentIndex], pool.TargetPositionY[agentIndex]);

        if (state == AgentState.MovingToSource)
        {
            if ((pool.GetPosition(agentIndex) - target).Length() <= ReachDist)
                PickUpReserved(agentIndex, pool, ctx);
            else if (pool.StuckTimer[agentIndex] >= StuckTimeoutSec)
                Abort(agentIndex, pool, ctx, dropCarried: false);
        }
        else if (state == AgentState.MovingToTarget)
        {
            if ((pool.GetPosition(agentIndex) - target).Length() <= ReachDist)
            {
                pool.States[agentIndex] = AgentState.Working;
                pool.WorkProgress[agentIndex] = 0f;
                pool.StuckTimer[agentIndex] = 0f;
            }
            else if (pool.StuckTimer[agentIndex] >= StuckTimeoutSec)
            {
                Abort(agentIndex, pool, ctx, dropCarried: true);
            }
        }
        else if (state == AgentState.Working)
        {
            int tx = pool.TargetCellX[agentIndex];
            int ty = pool.TargetCellY[agentIndex];
            if (!StructureManager.Instance.TryGetOperationalStructure(tx, ty, out var station))
            {
                // Станок сломали/снесли во время работы — входы вернуть на землю.
                Abort(agentIndex, pool, ctx, dropCarried: true);
                return;
            }
            Recipe recipe = RecipeRegistry.FindRecipeByInput(station.Type, pool.CarriedItemId[agentIndex])
                            ?? RecipeRegistry.GetForStation(station.Type);
            if (recipe == null || pool.WorkProgress[agentIndex] < recipe.DurationSec)
                return;
            FinishCycle(agentIndex, pool, ctx, recipe);
        }
    }

    private static Recipe GetRecipeAt(int tx, int ty, ItemId inputItem = ItemId.None)
    {
        if (!StructureManager.Instance.TryGetOperationalStructure(tx, ty, out var station))
            return null;
        if (inputItem != ItemId.None)
        {
            var r = RecipeRegistry.FindRecipeByInput(station.Type, inputItem);
            if (r != null) return r;
        }
        return RecipeRegistry.GetForStation(station.Type);
    }

    /// <summary>Забрать зарезервированный вход из источника и пойти к станку.</summary>
    private static void PickUpReserved(int agentIndex, AgentDataPool pool, SimulationContext ctx)
    {
        int sx = pool.SourceCellX[agentIndex];
        int sy = pool.SourceCellY[agentIndex];
        ItemId want = pool.CarriedItemId[agentIndex];
        int reserved = pool.ReservedItemCount[agentIndex];
        bool fromStock = pool.FoodFromStockpile[agentIndex];
        int taken = 0;

        if (fromStock)
        {
            if (StockpileManager.Instance.TryWithdrawReservedItemTyped(sx, sy, want, reserved, out int got))
                taken = got;
        }
        else
        {
            if (GroundItemManager.Instance.PeekItemAt(sx, sy) == want)
            {
                (ItemId id, int got) = GroundItemManager.Instance.TakeItemsWithType(sx, sy, reserved);
                if (id == want)
                    taken = got;
                else if (got > 0)
                    GroundItemManager.Instance.SpawnItems(sx, sy, id, got); // подменили — вернуть чужое
            }
        }

        if (taken < reserved)
        {
            int left = reserved - taken;
            if (left > 0)
            {
                if (fromStock)
                    StockpileManager.Instance.CancelWithdrawalReservation(sx, sy, left);
                else
                    GroundItemManager.Instance.ReleaseReservation(sx, sy, left);
            }
            pool.ReservedItemCount[agentIndex] = 0;
            if (taken <= 0)
            {
                Abort(agentIndex, pool, ctx, dropCarried: false);
                return;
            }
        }

        pool.ReservedItemCount[agentIndex] = 0;
        pool.CarriedItemId[agentIndex] = want;
        pool.CarriedItemCount[agentIndex] = taken;
        pool.FoodFromStockpile[agentIndex] = false;

        int tx = pool.TargetCellX[agentIndex];
        int ty = pool.TargetCellY[agentIndex];
        (int X, int Y) stand = (tx, ty);
        JobBroker.FindStandPosition(tx, ty, ctx, out stand);
        pool.TargetPositionX[agentIndex] = stand.X * ctx.TileSize + 32f;
        pool.TargetPositionY[agentIndex] = stand.Y * ctx.TileSize + 32f;
        pool.States[agentIndex] = AgentState.MovingToTarget;
        pool.StuckTimer[agentIndex] = 0f;
    }

    /// <summary>Завершить цикл: списать входы, выложить выход на землю, снять работу.</summary>
    private static void FinishCycle(int agentIndex, AgentDataPool pool, SimulationContext ctx, Recipe recipe)
    {
        int have = pool.CarriedItemCount[agentIndex];
        if (have < recipe.InputCount)
        {
            Abort(agentIndex, pool, ctx, dropCarried: true);
            return;
        }

        int tx = pool.TargetCellX[agentIndex];
        int ty = pool.TargetCellY[agentIndex];
        (int X, int Y) stand = (tx, ty);
        JobBroker.FindStandPosition(tx, ty, ctx, out stand);

        pool.CarriedItemCount[agentIndex] = have - recipe.InputCount;
        if (pool.CarriedItemCount[agentIndex] <= 0)
        {
            pool.CarriedItemCount[agentIndex] = 0;
            pool.CarriedItemId[agentIndex] = ItemId.None;
        }

        // Выход на землю у станка: GroundItemManager сам зарегистрирует hauling,
        // если на складе есть место и спроса нет — товар уедет на склад.
        GroundItemManager.Instance.SpawnItems(stand.X, stand.Y, recipe.OutputItem, recipe.OutputCount);
        WorkProgressTracker.Instance.Clear(tx, ty);

        int jobId = pool.CurrentJobId[agentIndex];
        if (jobId != -1)
        {
            if (!JobDispatcher.Instance.TryUnregisterJob(jobId))
                JobDispatcher.Instance.JobIndex.ReleaseWorkerClaim(jobId);
        }
        pool.CurrentJobId[agentIndex] = -1;
        pool.CurrentJobType[agentIndex] = JobTypeId.None;
        pool.States[agentIndex] = AgentState.Idle;
        pool.WorkProgress[agentIndex] = 0f;
        JobDispatcher.ReturnAgentAfterJob(agentIndex, pool); // J-10: Worker зоны не уходит в общий пул
    }

    /// <summary>Снять резерв/груз и освободить работу (станок недоступен, агент застрял).</summary>
    private static void Abort(int agentIndex, AgentDataPool pool, SimulationContext ctx, bool dropCarried)
    {
        int reserved = pool.ReservedItemCount[agentIndex];
        if (reserved > 0)
        {
            int sx = pool.SourceCellX[agentIndex];
            int sy = pool.SourceCellY[agentIndex];
            if (pool.FoodFromStockpile[agentIndex])
                StockpileManager.Instance.CancelWithdrawalReservation(sx, sy, reserved);
            else
                GroundItemManager.Instance.ReleaseReservation(sx, sy, reserved);
            pool.ReservedItemCount[agentIndex] = 0;
        }

        if (pool.CarriedItemCount[agentIndex] > 0 && ctx != null)
        {
            int cx = Math.Clamp((int)(pool.PositionX[agentIndex] / ctx.TileSize), 0, ctx.MapWidth - 1);
            int cy = Math.Clamp((int)(pool.PositionY[agentIndex] / ctx.TileSize), 0, ctx.MapHeight - 1);
            if (dropCarried)
                GroundItemManager.Instance.SpawnItems(cx, cy, pool.CarriedItemId[agentIndex],
                    pool.CarriedItemCount[agentIndex]);
            pool.CarriedItemCount[agentIndex] = 0;
        }
        pool.CarriedItemId[agentIndex] = ItemId.None;
        pool.FoodFromStockpile[agentIndex] = false;

        // Визуал прогресса снимаем; работу освобождает ReleaseJobWorker.
        WorkProgressTracker.Instance.Clear(pool.TargetCellX[agentIndex], pool.TargetCellY[agentIndex]);
        JobDispatcher.Instance.ReleaseJobWorker(agentIndex, pool, ctx);
    }

    /// <summary>
    /// AUDIT task-17 (P3-1): резерв входов не удался. Работу СНИМАЕМ (иначе менеджер
    /// видел «работа есть, входы есть» и держал её) и ставим станции кулдаун, чтобы
    /// Tick не пересоздал её мгновенно. Агент уходит в Idle с обычной паузой.
    /// </summary>
    private static void FailStartReservation(int agentIndex, AgentDataPool pool, SimulationContext ctx)
    {
        int tx = pool.TargetCellX[agentIndex];
        int ty = pool.TargetCellY[agentIndex];
        int failId = pool.CurrentJobId[agentIndex];
        if (failId != -1)
        {
            JobDispatcher.Instance.JobIndex.RemoveJobByPos(tx, ty, JobTypeId.Production, out _);
            JobDispatcher.Instance.JobIndex.ReleaseWorkerClaim(failId);
            pool.CurrentJobId[agentIndex] = -1;
            pool.CurrentJobType[agentIndex] = JobTypeId.None;
        }
        ProductionManager.Instance.NotifyStartFailure(tx, ty);
        pool.States[agentIndex] = AgentState.Idle;
        pool.JobSearchTimer[agentIndex] = 4.0f + (float)ParallelRng.NextDouble() * 4.0f;
        JobDispatcher.ReturnAgentAfterJob(agentIndex, pool);
    }

    /// <summary>Работу взять не удалось (нет станции/рецепта) — кулдаун + освободить claim.</summary>
    private static void FailStart(int agentIndex, AgentDataPool pool, SimulationContext ctx)
    {
        int failId = pool.CurrentJobId[agentIndex];
        if (failId != -1)
        {
            JobDispatcher.Instance.JobIndex.MarkJobUnreachable(failId, 5000);
            JobDispatcher.Instance.JobIndex.ReleaseWorkerClaim(failId);
            pool.CurrentJobId[agentIndex] = -1;
            pool.CurrentJobType[agentIndex] = JobTypeId.None;
        }
        pool.States[agentIndex] = AgentState.Idle;
        pool.JobSearchTimer[agentIndex] = 4.0f + (float)ParallelRng.NextDouble() * 4.0f;
        JobDispatcher.ReturnAgentAfterJob(agentIndex, pool);
    }

    public void OnCancel(int agentIndex, AgentDataPool pool, SimulationContext ctx)
    {
        int reserved = pool.ReservedItemCount[agentIndex];
        if (reserved > 0)
        {
            int sx = pool.SourceCellX[agentIndex];
            int sy = pool.SourceCellY[agentIndex];
            if (pool.FoodFromStockpile[agentIndex])
                StockpileManager.Instance.CancelWithdrawalReservation(sx, sy, reserved);
            else
                GroundItemManager.Instance.ReleaseReservation(sx, sy, reserved);
            pool.ReservedItemCount[agentIndex] = 0;
        }

        if (pool.CarriedItemCount[agentIndex] > 0 && ctx != null)
        {
            int cx = Math.Clamp((int)(pool.PositionX[agentIndex] / ctx.TileSize), 0, ctx.MapWidth - 1);
            int cy = Math.Clamp((int)(pool.PositionY[agentIndex] / ctx.TileSize), 0, ctx.MapHeight - 1);
            GroundItemManager.Instance.SpawnItems(cx, cy,
                pool.CarriedItemId[agentIndex], pool.CarriedItemCount[agentIndex]);
            pool.CarriedItemCount[agentIndex] = 0;
        }
        pool.CarriedItemId[agentIndex] = ItemId.None;
        pool.FoodFromStockpile[agentIndex] = false;
        WorkProgressTracker.Instance.Clear(pool.TargetCellX[agentIndex], pool.TargetCellY[agentIndex]);
    }
}