using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Game.Core;

namespace Game.Simulation.Jobs;

/// <summary>
/// Лёгкая система поведенческих реакций на потребности (без новых JobType).
/// Только свободные агенты (Idle): при превышении порогов переводит агента в
/// Evacuating + NeedBehavior и ведёт через AgentMovementService.MoveTowards.
/// Диспетчер Evacuating игнорирует, агент не будет отобран на работу.
/// </summary>
public sealed class NeedsJobSystem
{
    private const float ReachDist = 20.0f;

    /// <summary>
    /// FIX (bughunt 09-7): реальный ctx последнего тика. NeedsJobSystem.Commit(...)
    /// вызывается из буккипинга БЕЗ контекста (AgentSimulationThread.cs:1270),
    /// а OnCancel хендлеров разыменовывает ctx.TileSize — с null был NRE и груз
    /// не выкладывался. Write — из Phase2 (параллельные потоки пишут один и тот
    /// же объект сессии), Read — из Phase3a; Volatile даёт видимость между ними.
    /// </summary>
    private SimulationContext _cachedCtx;

    // P0.5: ThreadLocal RNG для Parallel-путей (Random.Shared contention, ctx.Random race).
    private static readonly System.Threading.ThreadLocal<Random> ThreadRandom = new(
        () => new Random(System.Environment.TickCount ^ System.Threading.Thread.CurrentThread.ManagedThreadId * 7919));

    /// <summary>
    /// Проверка триггеров для свободного агента (тайм-слайсинг 25% как поиск работы).
    /// Приоритет: голод &gt; сон/усталость &gt; окружение.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryAssignNeedsBehavior(int agentIndex, AgentDataPool pool, SimulationContext ctx)
    {
        System.Threading.Volatile.Write(ref _cachedCtx, ctx);
        if (pool.NeedsBehavior[agentIndex] != NeedBehavior.None)
            return true;
        if (pool.States[agentIndex] != AgentState.Idle)
            return false;

        // Догма 15 (GD-33, GD-34): Нервный срыв при критическом стрессе (Mood < 15) — ступор и отказ от работы
        if (pool.Mood[agentIndex] < 15f)
        {
            TryStartResting(agentIndex, pool, ctx);
            pool.JobSearchTimer[agentIndex] = 8.0f + (float)ParallelRng.NextDouble() * 6.0f;
            return true;
        }

        if (pool.Hunger[agentIndex] > AgentNeedsConfig.HungerSeekThreshold)
        {
            if (TryStartSeekingFood(agentIndex, pool, ctx))
                return true;
        }

        if (pool.Sleep[agentIndex] > AgentNeedsConfig.SleepRestThreshold ||
            pool.Fatigue[agentIndex] > AgentNeedsConfig.FatigueRestThreshold)
        {
            TryStartResting(agentIndex, pool, ctx);
            return true;
        }

        if (pool.EnvironmentSatisfaction[agentIndex] < AgentNeedsConfig.EnvironmentMigrateThreshold)
        {
            StartMigrating(agentIndex, pool, ctx);
            return true;
        }

        return false;
    }

    /// <summary>Ведёт агента с нуждой: движение + поедание/отдых/миграция.</summary>
    public void ExecuteParallel(int agentIndex, float deltaTime, AgentDataPool pool, SimulationContext ctx)
    {
        // FIX (bughunt 09-7): запоминаем ctx для Commit(...) из буккипинга.
        System.Threading.Volatile.Write(ref _cachedCtx, ctx);

        var behavior = pool.NeedsBehavior[agentIndex];
        if (behavior == NeedBehavior.None)
            return;

        if (behavior == NeedBehavior.Resting)
        {
            // FIX (bughunt 09-3): отдых ведётся/завершается здесь (CommitResting),
            // а не в Commit по «достижению цели» — цель отдыха совпадает с
            // собственной позицией и гасила поведение на первом тике.
            // GD-33: Если назначена кровать — сначала идём к ней
            var restTarget = new Vector2(pool.TargetPositionX[agentIndex], pool.TargetPositionY[agentIndex]);
            float distSq = (pool.GetPosition(agentIndex) - restTarget).LengthSquared();
            if (distSq > ReachDist * ReachDist)
            {
                ctx.Movement.MoveTowards(agentIndex, restTarget, ReachDist, deltaTime, pool, ctx);
                if (pool.StuckTimer[agentIndex] >= 3.0f)
                {
                    // Застрял по пути к кровати — освобождаем кровать и спим на месте на земле
                    if (pool.TargetCellX[agentIndex] >= 0 && pool.TargetCellY[agentIndex] >= 0)
                    {
                        _occupiedBeds.TryRemove((pool.TargetCellX[agentIndex], pool.TargetCellY[agentIndex]), out _);
                    }
                    pool.StuckTimer[agentIndex] = 0f;
                    pool.TargetPositionX[agentIndex] = pool.PositionX[agentIndex];
                    pool.TargetPositionY[agentIndex] = pool.PositionY[agentIndex];
                    pool.TargetCellX[agentIndex] = -1;
                    pool.TargetCellY[agentIndex] = -1;
                }
                return;
            }

            CommitResting(agentIndex, deltaTime, pool, ctx);
            return;
        }

        var target = new Vector2(pool.TargetPositionX[agentIndex], pool.TargetPositionY[agentIndex]);
        bool reached = ctx.Movement.MoveTowards(agentIndex, target, ReachDist, deltaTime, pool, ctx);
        if (!reached && pool.StuckTimer[agentIndex] >= 3.0f)
        {
            // P0-1: без SimEvents — stuck-ветка горячая при толпах, интерполяция
            // строки + два lock на каждое срабатывание.
            // Застрял по пути к еде: возвращаем резерв кучи, иначе зерно
            // навсегда вычитается из доступных (утечка _totalAvailableByType).
            // Складская еда уже списана из _storage атомарным взятием —
            // возвращать её некуда без новой кладки, резерв не висит.
            if (behavior == NeedBehavior.SeekingFood && pool.ReservedItemCount[agentIndex] > 0)
            {
                if (pool.FoodFromStockpile[agentIndex])
                {
                    StockpileManager.Instance.CancelWithdrawalReservation(
                        pool.SourceCellX[agentIndex], pool.SourceCellY[agentIndex],
                        pool.ReservedItemCount[agentIndex]);
                }
                else
                {
                    GroundItemManager.Instance.ReleaseReservation(
                        pool.SourceCellX[agentIndex], pool.SourceCellY[agentIndex],
                        pool.ReservedItemCount[agentIndex]);
                }
                pool.ReservedItemCount[agentIndex] = 0;
                pool.SourceCellX[agentIndex] = 0;
                pool.SourceCellY[agentIndex] = 0;
            }
            pool.StuckTimer[agentIndex] = 0f;
            FinishBehavior(agentIndex, pool, cooldownSec: 4f, ctx);
            return;
        }
        if (!reached)
            return;

        if (behavior == NeedBehavior.SeekingFood)
            CommitEating(agentIndex, pool, ctx);
        else if (behavior == NeedBehavior.Migrating)
            FinishBehavior(agentIndex, pool, cooldownSec: 2f, ctx);
    }

    /// <summary>Добор: достижение цели эвакуационной ходьбы (вызов из bookkeeping).</summary>
    public void Commit(int agentIndex, AgentDataPool pool)
    {
        if (pool.NeedsBehavior[agentIndex] == NeedBehavior.None)
            return;

        // FIX (bughunt 09-3): Resting НЕ завершаем по достижению цели. StartResting
        // ставит TargetPosition = собственной позиции (dist 0), поэтому этот
        // Commit гасил отдых на первом же тике — сон/усталость не восстанавливались.
        // Отдых живёт в ExecuteParallel → CommitResting и завершается там по
        // восстановлению потребностей (Sleep/Fatigue <= 5).
        if (pool.NeedsBehavior[agentIndex] == NeedBehavior.Resting)
            return;

        float dx = pool.PositionX[agentIndex] - pool.TargetPositionX[agentIndex];
        float dy = pool.PositionY[agentIndex] - pool.TargetPositionY[agentIndex];
        if (dx * dx + dy * dy > 16.0f)
            return;
        // FIX (bughunt 09-7): реальный ctx из Phase2 — Commit приходит без него.
        SimulationContext ctx = System.Threading.Volatile.Read(ref _cachedCtx);
        if (pool.NeedsBehavior[agentIndex] == NeedBehavior.SeekingFood)
            CommitEating(agentIndex, pool, ctx);
        else
            FinishBehavior(agentIndex, pool, cooldownSec: 2f, ctx);
    }

    private static bool TryStartSeekingFood(int a, AgentDataPool pool, SimulationContext ctx)
    {
        var pos = pool.GetPosition(a);

        // 1. Сначала ищем свежий Хлеб (утоляет 80 голода и даёт +10 к морали)
        if (TrySeekSpecificFood(a, pool, ctx, pos, ItemId.Bread, 1))
            return true;

        // 2. Если хлеба нет — ищем сырое зерно
        if (TrySeekSpecificFood(a, pool, ctx, pos, ItemId.Grain, Math.Max(1, AgentNeedsConfig.FoodEatGrainCount)))
            return true;

        // Еды нет ни на земле, ни на складе: backoff 30-60с в отдельном
        // NeedsRetryTimer (не JobSearchTimer — его затирает блуждание 6с), иначе 10k голодных
        // молотят скан каждые 4 тика вечно. ParallelRng — lock-free (Random.Shared — contention).
        pool.NeedsRetryTimer[a] = 30.0f + (float)ParallelRng.NextDouble() * 30.0f;
        return false;
    }

    private static bool TrySeekSpecificFood(int a, AgentDataPool pool, SimulationContext ctx, Vector2 pos, ItemId foodItem, int eatCount)
    {
        float eatWeight = ItemRegistry.Get(foodItem).Weight * eatCount;
        if (GroundItemManager.Instance.TryReserveGroundItems(
                pos, eatWeight, allowFromStockpile: true, foodItem, maxChunkRadius: 3,
                out var cell, out var itemId, out int resCount)
            && itemId == foodItem && resCount > 0)
        {
            int ax = (int)(pos.X / ctx.TileSize);
            int ay = (int)(pos.Y / ctx.TileSize);
            int maxR = AgentNeedsConfig.FoodSearchRadiusTiles;
            int ddx = cell.X - ax, ddy = cell.Y - ay;
            if (ddx * ddx + ddy * ddy > maxR * maxR)
            {
                GroundItemManager.Instance.ReleaseReservation(cell.X, cell.Y, resCount);
            }
            else
            {
                StartSeekingFood(a, pool, ctx, cell, resCount, fromStockpile: false, foodItem);
                return true;
            }
        }

        if (StockpileManager.Instance.TryTakeStockpileFood(
                new System.Numerics.Vector2(pos.X, pos.Y), foodItem, eatCount,
                AgentNeedsConfig.FoodSearchRadiusTiles, out var stockCell, out int stockRes)
            && stockRes > 0)
        {
            StartSeekingFood(a, pool, ctx, stockCell, stockRes, fromStockpile: true, foodItem);
            return true;
        }

        return false;
    }

    private static void StartSeekingFood(int a, AgentDataPool pool, SimulationContext ctx, (int X, int Y) cell, int eatCount, bool fromStockpile, ItemId foodItem)
    {
        JobDispatcher.Instance.IdleWorkers.RemoveIdleWorker(a, pool);
        pool.NeedsBehavior[a] = NeedBehavior.SeekingFood;
        pool.States[a] = AgentState.Evacuating;
        pool.SourceCellX[a] = cell.X;
        pool.SourceCellY[a] = cell.Y;
        pool.ReservedItemCount[a] = eatCount;
        pool.FoodFromStockpile[a] = fromStockpile;
        pool.CarriedItemId[a] = foodItem;
        pool.TargetPositionX[a] = cell.X * ctx.TileSize + 32f;
        pool.TargetPositionY[a] = cell.Y * ctx.TileSize + 32f;
        pool.StuckTimer[a] = 0f;
        pool.WaypointCount[a] = 0;
        pool.WaypointIndex[a] = 0;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(int X, int Y), int> _occupiedBeds = new();

    private static bool TryStartResting(int a, AgentDataPool pool, SimulationContext ctx)
    {
        int curX = (int)(pool.PositionX[a] / ctx.TileSize);
        int curY = (int)(pool.PositionY[a] / ctx.TileSize);

        // GD-33: Проверка наличия свободных кроватей (StructureManager.Instance.TryGetOperationalStructure с типом Bed)
        if (TryFindFreeBed(curX, curY, AgentNeedsConfig.MigrateRadiusTiles * 2, out var bedPos))
        {
            StartRestingInBed(a, pool, ctx, bedPos);
            return true;
        }

        StartRestingOnGround(a, pool);
        return true;
    }

    private static bool TryFindFreeBed(int curX, int curY, int maxRadiusTiles, out (int X, int Y) bedPos)
    {
        return StructureManager.Instance.TryFindNearestOperationalBed(curX, curY, maxRadiusTiles,
            (bx, by) => !_occupiedBeds.ContainsKey((bx, by)), out bedPos)
            && StructureManager.Instance.TryGetOperationalStructure(bedPos.X, bedPos.Y, out var s)
            && s.Definition.Type == BuildingType.Bed;
    }

    private static void StartRestingInBed(int a, AgentDataPool pool, SimulationContext ctx, (int X, int Y) bedPos)
    {
        _occupiedBeds[bedPos] = a;
        JobDispatcher.Instance.IdleWorkers.RemoveIdleWorker(a, pool);
        pool.NeedsBehavior[a] = NeedBehavior.Resting;
        pool.States[a] = AgentState.Evacuating;
        pool.TargetCellX[a] = bedPos.X;
        pool.TargetCellY[a] = bedPos.Y;
        pool.TargetPositionX[a] = bedPos.X * ctx.TileSize + 32f;
        pool.TargetPositionY[a] = bedPos.Y * ctx.TileSize + 32f;
        pool.StuckTimer[a] = 0f;
        pool.WaypointCount[a] = 0;
        pool.WaypointIndex[a] = 0;
    }

    private static void StartRestingOnGround(int a, AgentDataPool pool)
    {
        JobDispatcher.Instance.IdleWorkers.RemoveIdleWorker(a, pool);
        pool.NeedsBehavior[a] = NeedBehavior.Resting;
        pool.States[a] = AgentState.Evacuating;
        pool.TargetCellX[a] = -1;
        pool.TargetCellY[a] = -1;
        pool.TargetPositionX[a] = pool.PositionX[a];
        pool.TargetPositionY[a] = pool.PositionY[a];
        pool.StuckTimer[a] = 0f;
        pool.WaypointCount[a] = 0;
        pool.WaypointIndex[a] = 0;
    }

    private static void StartMigrating(int a, AgentDataPool pool, SimulationContext ctx)
    {
        int curX = (int)(pool.PositionX[a] / ctx.TileSize);
        int curY = (int)(pool.PositionY[a] / ctx.TileSize);
        int r = AgentNeedsConfig.MigrateRadiusTiles;
        // P0.5: ThreadLocal RNG вместо Random.Shared (внутр. lock → contention в Parallel)
        // и ctx.Random (не thread-safe → data race). Детерминизм сида в Parallel и так условен.
        var rng = ThreadRandom.Value;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            int tx = curX + rng.Next(-r, r + 1);
            int ty = curY + rng.Next(-r, r + 1);
            if ((uint)tx >= (uint)ctx.MapWidth || (uint)ty >= (uint)ctx.MapHeight)
                continue;
            if (ctx.SolidWalls[tx, ty] || ctx.TreeOnGrass[tx, ty])
                continue;
            if (ctx.Ground[tx, ty] != TileType.Grass)
                continue;
            if (ctx.SpatialGrid.IsCellOvercrowded(tx, ty, 5, pool))
                continue;
            JobDispatcher.Instance.IdleWorkers.RemoveIdleWorker(a, pool);
            pool.NeedsBehavior[a] = NeedBehavior.Migrating;
            pool.States[a] = AgentState.Evacuating;
            pool.TargetCellX[a] = tx;
            pool.TargetCellY[a] = ty;
            pool.TargetPositionX[a] = tx * ctx.TileSize + 32f;
            pool.TargetPositionY[a] = ty * ctx.TileSize + 32f;
            pool.StuckTimer[a] = 0f;
            pool.WaypointCount[a] = 0;
            pool.WaypointIndex[a] = 0;
            return;
        }
        TryStartResting(a, pool, ctx);
    }

    private void CommitEating(int a, AgentDataPool pool, SimulationContext ctx)
    {
        int taken;
        if (pool.FoodFromStockpile[a])
        {
            taken = StockpileManager.Instance.WithdrawReservedItem(
                pool.SourceCellX[a], pool.SourceCellY[a], pool.ReservedItemCount[a]);
            if (taken <= 0)
            {
                StockpileManager.Instance.CancelWithdrawalReservation(
                    pool.SourceCellX[a], pool.SourceCellY[a], pool.ReservedItemCount[a]);
            }
        }
        else
        {
            taken = GroundItemManager.Instance.TakeItems(
                pool.SourceCellX[a], pool.SourceCellY[a], pool.ReservedItemCount[a]);
            if (taken <= 0)
            {
                GroundItemManager.Instance.ReleaseReservation(
                    pool.SourceCellX[a], pool.SourceCellY[a], pool.ReservedItemCount[a]);
            }
        }
        if (taken > 0)
        {
            ItemId food = pool.CarriedItemId[a];
            if (food == ItemId.Bread)
            {
                float h = pool.Hunger[a] - 80f;
                pool.Hunger[a] = h < 0f ? 0f : h;
                pool.Morale[a] = MathF.Min(100f, pool.Morale[a] + 10f);
            }
            else
            {
                float fraction = Math.Clamp((float)taken / AgentNeedsConfig.FoodEatGrainCount, 0f, 1f);
                float h = pool.Hunger[a] - AgentNeedsConfig.FoodEatHungerRestore * fraction;
                pool.Hunger[a] = h < 0f ? 0f : h;
            }
        }
        pool.ReservedItemCount[a] = 0;
        pool.FoodFromStockpile[a] = false;
        pool.CarriedItemId[a] = ItemId.None;
        pool.SourceCellX[a] = 0;
        pool.SourceCellY[a] = 0;
        FinishBehavior(a, pool, cooldownSec: 4f, ctx);
    }

    private void CommitResting(int a, float dt, AgentDataPool pool, SimulationContext ctx)
    {
        float s = pool.Sleep[a] - AgentNeedsConfig.RestSleepRecoveryPerGameSec * dt;
        pool.Sleep[a] = s < 0f ? 0f : s;
        float f = pool.Fatigue[a] - AgentNeedsConfig.RestFatigueRecoveryPerGameSec * dt;
        pool.Fatigue[a] = f < 0f ? 0f : f;
        if (pool.Sleep[a] <= 5f && pool.Fatigue[a] <= 5f)
        {
            // GD-33: Проверяем, спал ли агент в свободной операционной кровати
            bool sleptInBed = pool.TargetCellX[a] >= 0 && pool.TargetCellY[a] >= 0
                && StructureManager.Instance.TryGetOperationalStructure(pool.TargetCellX[a], pool.TargetCellY[a], out var bed)
                && bed.Definition.Type == BuildingType.Bed;

            if (sleptInBed)
            {
                // Сон в кровати даёт +20 к морали и не накладывает дебафф
                pool.Morale[a] = MathF.Min(100f, pool.Morale[a] + 20f);
            }
            else
            {
                // Дебафф сна на голой земле (-10 к морали)
                pool.Morale[a] = MathF.Max(0f, pool.Morale[a] - 10f);
            }

            FinishBehavior(a, pool, cooldownSec: 2f, ctx);
        }
    }

    private void FinishBehavior(int a, AgentDataPool pool, float cooldownSec, SimulationContext ctx)
    {
        if (pool.TargetCellX[a] >= 0 && pool.TargetCellY[a] >= 0)
        {
            _occupiedBeds.TryRemove((pool.TargetCellX[a], pool.TargetCellY[a]), out _);
        }
        // FIX (bughunt 09-7): передаём РЕАЛЬНЫЙ ctx (из Phase2/Commit), а не null:
        // OnCancel хендлеров разыменовывает ctx.TileSize, с null был NRE —
        // груз не выкладывался, резервы не снимались.
        ctx ??= System.Threading.Volatile.Read(ref _cachedCtx);
        pool.NeedsBehavior[a] = NeedBehavior.None;
        pool.FoodFromStockpile[a] = false;
        pool.States[a] = AgentState.Idle;
        // §119 P2 (замечание лида): тихий сброс CurrentJobId БЕЗ OnCancel был
        // дырой — работа теряла клейм мимо ReleaseJobWorker (Worker не возвращал
        // работу в очередь зоны, Laborer не снимал клейм реестра). Теперь:
        // если под нуждой вдруг оказался агент с задачей — честное снятие через
        // ReleaseJobWorker (OnCancel + возврат в очередь зоны для Worker'а /
        // ReleaseWorkerClaim для Laborer'а). В норме нужды начинаются ТОЛЬКО у
        // Idle (TryAssignNeedsBehavior гейтится по State==Idle) и CurrentJobId
        // здесь уже -1 — путь сработает как раньше (ветка пустая).
        if (pool.CurrentJobId[a] != -1 && pool.CurrentJobId[a] != JobDispatcher.AgentReservedMarker)
        {
            pool.States[a] = AgentState.Idle;
            JobDispatcher.Instance.ReleaseJobWorker(a, pool, ctx);
            // ReleaseJobWorker уже выставил Idle/JobSearchTimer/пул бездельников
            // (кроме Worker'а — его ведёт WorkerZoneJobSystem); здесь только
            // переопределяем кулдаун поиска под нужду (cooldownSec конкретной ветки).
            pool.NeedsBehavior[a] = NeedBehavior.None;
            pool.JobSearchTimer[a] = cooldownSec + (float)ThreadRandom.Value.NextDouble() * 2.0f;
            return;
        }
        pool.CurrentJobId[a] = -1;
        pool.CurrentJobType[a] = JobTypeId.None;
        pool.JobSearchTimer[a] = cooldownSec + (float)ThreadRandom.Value.NextDouble() * 2.0f;
        pool.WaypointCount[a] = 0;
        pool.WaypointIndex[a] = 0;
        JobDispatcher.ReturnAgentAfterJob(a, pool); // J-10: Worker зоны не уходит в общий пул
    }
}
