using System;
using System.Numerics;
using Game.Core;

namespace Game.Simulation.Jobs;

public sealed class PlantingJobHandler : IJobHandler
{
    private const float PlantDuration = 10.0f; // 10 секунд на посадку
    private const float ReachDist = 20.0f;

    public JobTypeId TypeId => JobTypeId.Planting;
    public JobExecutionType ExecutionType => JobExecutionType.Hauling;
    public JobPriorityTier DefaultPriority => JobPriorityTier.Farming;
    public ToolRequirement RequiredTool => ToolRequirement.None;

    public bool CanAgentExecute(int agentIndex, in JobData job, AgentDataPool pool, SimulationContext ctx)
    {
        if (!FarmJobManager.Instance.IsGardenBed(job.TargetX, job.TargetY))
            return false;

        // Сажаем урожаем самой культуры: пшеницу — зерном, лён — льноволокном и т.д.
        ItemId seed = SeedItemFor(job.TargetX, job.TargetY);
        return GroundItemManager.Instance.HasAvailableItemsOfType(seed) || StockpileManager.Instance.GetLiveItemCount(seed) > 0;
    }

    /// <summary>Чем сажать в этой клетке: урожай культуры зоны (по умолчанию — зерно).</summary>
    private static ItemId SeedItemFor(int tx, int ty)
    {
        if (FarmZoneManager.Instance.TryGetZoneAt(tx, ty, out var zone))
            return CropRegistry.Get(zone.Crop).HarvestItem;
        return ItemId.Grain;
    }

    public void OnStart(int agentIndex, in JobData job, AgentDataPool pool, SimulationContext ctx)
    {
        pool.WaypointCount[agentIndex] = 0;
        pool.WaypointIndex[agentIndex] = 0;

        Vector2 pos = pool.GetPosition(agentIndex);

        // Семя = урожай культуры этой зоны (лён сажаем льноволокном и т.п.).
        ItemId seedItem = SeedItemFor(job.TargetX, job.TargetY);

        bool reservedFromGround = GroundItemManager.Instance.TryReserveGroundItems(pos, 5.0f, true, seedItem, 3, out var itemCell, out var itemId, out int resCount);
        bool reservedFromStock = false;
        (int X, int Y) stockCell = (-1, -1);
        int stockResCount = 0;

        if (!reservedFromGround)
        {
            reservedFromStock = StockpileManager.Instance.TryReserveStockpileItem(pos, seedItem, 1, 0, out stockCell, out stockResCount);
        }

        if (reservedFromGround)
        {
            // FIX (bughunt 09-2): запоминаем, ОТКУДА взят резерв. В AgentDataPool
            // нет отдельного поля «источник — склад», поэтому переиспользуем
            // FoodFromStockpile: нужды и работы взаимоисключающи (нужды стартуют
            // только у Idle), а StartSeekingFood выставляет флаг сам.
            // Без этого Commit определял источник по IsZoneTile и мог списать
            // чужой складской слот вместо наземной кучи внутри складской зоны.
            pool.FoodFromStockpile[agentIndex] = false;
            pool.States[agentIndex] = AgentState.MovingToSource;
            pool.SourceCellX[agentIndex] = itemCell.X;
            pool.SourceCellY[agentIndex] = itemCell.Y;
            pool.TargetCellX[agentIndex] = job.TargetX;
            pool.TargetCellY[agentIndex] = job.TargetY;
            pool.ReservedItemCount[agentIndex] = 1;
            pool.TargetPositionX[agentIndex] = itemCell.X * ctx.TileSize + 32f;
            pool.TargetPositionY[agentIndex] = itemCell.Y * ctx.TileSize + 32f;
            pool.WorkProgress[agentIndex] = 0f;
            pool.StuckTimer[agentIndex] = 0f;

            if (resCount > 1)
            {
                GroundItemManager.Instance.ReleaseReservation(itemCell.X, itemCell.Y, resCount - 1);
            }
        }
        else if (reservedFromStock)
        {
            // FIX (bughunt 09-2): источник — склад (см. комментарий в ветке выше).
            pool.FoodFromStockpile[agentIndex] = true;
            pool.States[agentIndex] = AgentState.MovingToSource;
            pool.SourceCellX[agentIndex] = stockCell.X;
            pool.SourceCellY[agentIndex] = stockCell.Y;
            pool.TargetCellX[agentIndex] = job.TargetX;
            pool.TargetCellY[agentIndex] = job.TargetY;
            pool.ReservedItemCount[agentIndex] = 1;
            pool.TargetPositionX[agentIndex] = stockCell.X * ctx.TileSize + 32f;
            pool.TargetPositionY[agentIndex] = stockCell.Y * ctx.TileSize + 32f;
            pool.WorkProgress[agentIndex] = 0f;
            pool.StuckTimer[agentIndex] = 0f;
        }
        else
        {
            pool.FoodFromStockpile[agentIndex] = false;
            // Транзиентный фейл резерва зерна: работу оставляем + кулдаун (P0.2).
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
            JobDispatcher.ReturnAgentAfterJob(agentIndex, pool); // J-10: Worker зоны не уходит в общий пул
        }
    }

    public void ExecuteParallel(int agentIndex, float deltaTime, AgentDataPool pool, SimulationContext ctx)
    {
        var state = pool.States[agentIndex];
        Vector2 target = new(pool.TargetPositionX[agentIndex], pool.TargetPositionY[agentIndex]);

        if (state == AgentState.MovingToSource || state == AgentState.MovingToTarget)
        {
            ctx.Movement.MoveTowards(agentIndex, target, ReachDist, deltaTime, pool, ctx);
        }
        else if (state == AgentState.Working)
        {
            pool.WorkProgress[agentIndex] += deltaTime;
            // Визуал посадки: 6 стадий ProcessOfWork.
            WorkProgressTracker.Instance.ReportFraction(pool.TargetCellX[agentIndex], pool.TargetCellY[agentIndex],
                pool.WorkProgress[agentIndex] / PlantDuration);
        }
    }

    public void Commit(int agentIndex, float deltaTime, AgentDataPool pool, SimulationContext ctx)
    {
        var state = pool.States[agentIndex];
        Vector2 target = new(pool.TargetPositionX[agentIndex], pool.TargetPositionY[agentIndex]);

        if (state == AgentState.MovingToSource)
        {
            if ((pool.GetPosition(agentIndex) - target).Length() <= ReachDist)
            {
                int sx = pool.SourceCellX[agentIndex];
                int sy = pool.SourceCellY[agentIndex];
                int taken = 0;
                ItemId takenId = ItemId.None;
                ItemId seedItem = SeedItemFor(pool.TargetCellX[agentIndex], pool.TargetCellY[agentIndex]);

                // FIX (bughunt 09-2): источник берём из запомненного флага, а не из
                // IsZoneTile. Наземный резерв (allowFromStockpile: true) мог лежать
                // НА клетке склада — тогда старый код списывал чужой складской слот
                // (что угодно), а кучу на земле бросал с висящим резервом.
                if (pool.FoodFromStockpile[agentIndex])
                {
                    // FIX (bughunt 09-2): WithdrawReservedItem не принимает тип и
                    // списывает содержимое слота «как есть». Резерв выдан
                    // TryReserveStockpileItem(..., ItemId.Grain, ...) — он типизирован,
                    // а слот с активным ReservedOutgoing сменить тип не может
                    // (TryReserveStockpileSlot.canAccept). Поэтому фактический тип
                    // слота = зерно; точечного peek-API у StockpileManager нет.
                    // TODO (09-2): нужен атомарный typed-API
                    // (TryPeekSlot / WithdrawReservedItemTyped) — сейчас peek+списание
                    // неатомарны, а полный GetStorageSnapshot() под _lock на каждом
                    // прибытии к складу слишком дорог (O(слота) + аллокация).
                    // Дешёвая сверка «доступным способом»: если в хранилище физически
                    // не осталось зерна (live-тотал падает только при реальном
                    // списании), то и в слоте его нет — чужой предмет не берём.
                    taken = StockpileManager.Instance.GetLiveItemCount(seedItem) > 0
                        ? StockpileManager.Instance.WithdrawReservedItem(sx, sy, 1)
                        : 0;
                    if (taken > 0)
                    {
                        takenId = seedItem;
                        // Остаток брони (если слот отдал меньше) снимаем сразу,
                        // иначе он висит в ReservedOutgoing вечно.
                        int left = 1 - taken;
                        if (left > 0)
                            StockpileManager.Instance.CancelWithdrawalReservation(sx, sy, left);
                    }
                }
                else
                {
                    // FIX (bughunt 09-2): TakeItems отдаёт первый стак клетки без типа.
                    // Сверяем тип с зерном и забираем атомарно с типом.
                    if (GroundItemManager.Instance.PeekItemAt(sx, sy) == seedItem)
                    {
                        (ItemId item, int got) = GroundItemManager.Instance.TakeItemsWithType(sx, sy, 1);
                        if (item == seedItem)
                        {
                            taken = got;
                            takenId = seedItem;
                        }
                        else if (got > 0)
                        {
                            // Между peek и take стак подменили — возвращаем чужое как есть.
                            GroundItemManager.Instance.SpawnItems(sx, sy, item, got);
                        }
                    }
                    if (taken > 0)
                    {
                        int left = pool.ReservedItemCount[agentIndex] - taken;
                        if (left > 0)
                            GroundItemManager.Instance.ReleaseReservation(sx, sy, left);
                    }
                }

                if (taken > 0)
                {
                    // FIX (bughunt 09-2): груз — фактически взятый тип, не хардкод.
                    pool.CarriedItemId[agentIndex] = takenId;
                    pool.CarriedItemCount[agentIndex] = taken;
                    pool.ReservedItemCount[agentIndex] = 0;
                    pool.States[agentIndex] = AgentState.MovingToTarget;

                    (int X, int Y) stand = (pool.TargetCellX[agentIndex], pool.TargetCellY[agentIndex]);
                    Game.Simulation.JobBroker.FindStandPosition(stand.X, stand.Y, ctx, out stand);
                    pool.TargetPositionX[agentIndex] = stand.X * ctx.TileSize + 32f;
                    pool.TargetPositionY[agentIndex] = stand.Y * ctx.TileSize + 32f;
                }
                else
                {
                    JobDispatcher.Instance.ReleaseJobWorker(agentIndex, pool, ctx);
                }
            }
            else if (pool.StuckTimer[agentIndex] >= 3.0f)
            {
                JobDispatcher.Instance.ReleaseJobWorkerForStuck(agentIndex, pool, ctx);
                pool.JobSearchTimer[agentIndex] = 4.0f + (float)ParallelRng.NextDouble() * 4.0f;
            }
        }
        else if (state == AgentState.MovingToTarget)
        {
            if ((pool.GetPosition(agentIndex) - target).Length() <= ReachDist)
            {
                pool.States[agentIndex] = AgentState.Working;
                pool.WorkProgress[agentIndex] = 0f;
            }
            else if (pool.StuckTimer[agentIndex] >= 3.0f)
            {
                // Дроп рядом, а не на клетку цели: посадка на клетку будущей стройки/
                // грядки + Construction.Can=!HasItemsAt = вечный блок стройки.
                (int X, int Y) drop = (pool.TargetCellX[agentIndex], pool.TargetCellY[agentIndex]);
                Game.Simulation.JobBroker.FindStandPosition(drop.X, drop.Y, ctx, out drop);
                GroundItemManager.Instance.SpawnItems(
                    drop.X, drop.Y,
                    pool.CarriedItemId[agentIndex], pool.CarriedItemCount[agentIndex]);
                pool.CarriedItemCount[agentIndex] = 0;
                pool.CarriedItemId[agentIndex] = ItemId.None;
                JobDispatcher.Instance.ReleaseJobWorkerForStuck(agentIndex, pool, ctx);
                pool.JobSearchTimer[agentIndex] = 4.0f + (float)ParallelRng.NextDouble() * 4.0f;
            }
        }
        else if (state == AgentState.Working && pool.WorkProgress[agentIndex] >= PlantDuration)
        {
            int tx = pool.TargetCellX[agentIndex];
            int ty = pool.TargetCellY[agentIndex];

            int zoneId = -1;
            Game.Core.CropType crop = Game.Core.CropType.Wheat;
            if (FarmZoneManager.Instance.TryGetZoneAt(tx, ty, out var zone))
            {
                zoneId = zone.Id;
                crop = zone.Crop;
            }

            CropGrowthManager.Instance.PlantCrop(tx, ty, zoneId, crop);
            WorkProgressTracker.Instance.Clear(tx, ty);
            pool.CarriedItemCount[agentIndex] = 0;
            pool.CarriedItemId[agentIndex] = ItemId.None;

            // Эталон Farming: stale unregister → освободить claim вручную.
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
        }
    }

    public void OnCancel(int agentIndex, AgentDataPool pool, SimulationContext ctx)
    {
        if (pool.States[agentIndex] == AgentState.MovingToSource)
        {
            int sx = pool.SourceCellX[agentIndex];
            int sy = pool.SourceCellY[agentIndex];
            int count = pool.ReservedItemCount[agentIndex];
            // FIX (bughunt 09-2): релизим РОВНО тот источник, из которого бронировали.
            // Раньше для складского источника дополнительно вызывался наземный
            // ReleaseReservation — на клетке склада с наземной кучей это давало
            // фантомный +count в _totalAvailableByType.
            if (pool.FoodFromStockpile[agentIndex])
                StockpileManager.Instance.CancelWithdrawalReservation(sx, sy, count);
            else
                GroundItemManager.Instance.ReleaseReservation(sx, sy, count);
            pool.ReservedItemCount[agentIndex] = 0;
            pool.FoodFromStockpile[agentIndex] = false;
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
}