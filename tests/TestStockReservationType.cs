using System;
using System.Collections.Generic;
using Godot;
using Game.Core;
using Game.Simulation;
using Game.Simulation.Jobs;
using Vector2 = System.Numerics.Vector2;

namespace Game.Tests;

/// <summary>
/// 08-04: источник и тип резерва читаются из записи о резерве, а не угадываются по
/// `IsZoneTile`.
///
/// Как было: в `OnStart` хендлер решал, откуда брать предмет (земля или склад) и
/// запоминал это только в локальной переменной, а `Commit`/`OnCancel` заново
/// выводили источник из геометрии клетки. Куча на земле может лежать НА клетке
/// склада — состояние устойчивое, — и тогда:
///   (а) `Commit` списывал со склада чужой тип, а агенту ставил захардкоженный
///       `CarriedItemId` (зерно→бревно), наземный резерв висел навсегда;
///   (б) `OnCancel` для складского резерва дополнительно звал `ReleaseReservation`
///       по этой же клетке и начислял `_totalAvailableByType` «+count» из воздуха.
///
/// Тест ставит кучи на клетку склада и проверяет, что после `Commit`/`OnCancel`
/// тип груза и тоталы совпадают с реальностью.
///
/// Запуск: godot --headless --path . --script res://tests/run_stock_reservation_type.gd
/// </summary>
public partial class TestStockReservationType : RefCounted
{
    private const int TileSize = 64;

    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print("[RESERVE-TYPE] Резерв не подменяет тип и не течёт (08-04)");
        GD.Print("==================================================");

        if (!CheckGroundReservationOnZoneTile())
            return false;
        if (!CheckStockpileCancelKeepsGroundTotals())
            return false;

        GroundItemManager.Instance.ClearAll();
        StockpileManager.Instance.ClearAll();
        GD.Print("[RESERVE-TYPE] OK: источник/тип берутся из записи о резерве, тотал не течёт");
        return true;
    }

    /// <summary>
    /// (а) Куча зерна лежит на клетке склада с брёвнами. Резерв наземный — значит и
    /// списание наземное, зерном, а склад не трогаем.
    /// </summary>
    private static bool CheckGroundReservationOnZoneTile()
    {
        GroundItemManager gm = GroundItemManager.Instance;
        StockpileManager sm = StockpileManager.Instance;
        gm.ClearAll();
        sm.ClearAll();

        SimulationContext ctx = MakeContext(64, 64);

        const int x = 40, y = 40;

        MakeStockpileTile(sm, x, y);
        if (!sm.IsZoneTile(x, y))
        {
            GD.PrintErr("[RESERVE-TYPE] FAIL: клетка не стала складской — стенд неверен");
            return false;
        }
        sm.DepositItems(x, y, ItemId.Log, 10);
        gm.SpawnItems(x, y, ItemId.Grain, 5);

        var pool = new AgentDataPool(2);
        pool.SetPosition(0, new Vector2(x * TileSize + 32f, y * TileSize + 32f));
        pool.States[0] = AgentState.Idle;

        var job = new JobData
        {
            Id = 1,
            TypeId = JobTypeId.Planting,
            TargetX = x + 2,
            TargetY = y + 2
        };

        var handler = new PlantingJobHandler();
        handler.OnStart(0, in job, pool, ctx);

        if (pool.States[0] != AgentState.MovingToSource)
        {
            GD.PrintErr($"[RESERVE-TYPE] FAIL: OnStart не повёл агента к источнику (state={pool.States[0]})");
            return false;
        }
        if (pool.SourceCellX[0] != x || pool.SourceCellY[0] != y)
        {
            GD.PrintErr($"[RESERVE-TYPE] FAIL: источник ({pool.SourceCellX[0]},{pool.SourceCellY[0]}), " +
                        $"ожидался ({x},{y})");
            return false;
        }
        if (pool.ReservationFromStockpile(0))
        {
            GD.PrintErr("[RESERVE-TYPE] FAIL: зарезервировано со склада, хотя зерно лежит на земле");
            return false;
        }
        if (pool.ReservedItemId[0] != ItemId.Grain || pool.ReservedItemCount[0] != 1)
        {
            GD.PrintErr($"[RESERVE-TYPE] FAIL: запись о резерве ({pool.ReservedItemId[0]}, " +
                        $"{pool.ReservedItemCount[0]}), ожидалось (Grain, 1)");
            return false;
        }
        if (gm.AvailableCountOf(ItemId.Grain) != 4)
        {
            GD.PrintErr($"[RESERVE-TYPE] FAIL: после резерва доступно зерна " +
                        $"{gm.AvailableCountOf(ItemId.Grain)}, ожидалось 4");
            return false;
        }

        handler.Commit(0, 0.016f, pool, ctx);

        if (pool.CarriedItemId[0] != ItemId.Grain || pool.CarriedItemCount[0] != 1)
        {
            GD.PrintErr($"[RESERVE-TYPE] FAIL: агент несёт ({pool.CarriedItemId[0]}, " +
                        $"{pool.CarriedItemCount[0]}), ожидалось (Grain, 1)");
            return false;
        }

        int logs = sm.GetLiveItemCount(ItemId.Log);
        if (logs != 10)
        {
            GD.PrintErr($"[RESERVE-TYPE] FAIL: со склада списан чужой тип — брёвен {logs}, " +
                        "ожидалось 10 (зерно подменилось бревном)");
            return false;
        }

        int groundCount = gm.GetItemCountAt(x, y);
        int available = gm.AvailableCountOf(ItemId.Grain);
        if (groundCount != 4 || available != 4 || available != groundCount)
        {
            GD.PrintErr($"[RESERVE-TYPE] FAIL: на земле {groundCount}, доступно {available}, " +
                        "ожидалось 4/4 — резерв повис и вытек из тотала");
            return false;
        }

        return true;
    }

    /// <summary>
    /// (б) Складской резерв брёвен отменяется: наземное зерно на той же клетке не
    /// должно получить фантомный «+count» в тотале.
    /// </summary>
    private static bool CheckStockpileCancelKeepsGroundTotals()
    {
        GroundItemManager gm = GroundItemManager.Instance;
        StockpileManager sm = StockpileManager.Instance;
        gm.ClearAll();
        sm.ClearAll();

        SimulationContext ctx = MakeContext(64, 64);

        const int x = 44, y = 44;

        MakeStockpileTile(sm, x, y);
        // В слоте склада — брёвна, на земле той же клетки — зерно (чужой тип).
        sm.DepositItems(x, y, ItemId.Log, 4);
        gm.SpawnItems(x, y, ItemId.Grain, 3);

        var pool = new AgentDataPool(2);
        pool.SetPosition(0, new Vector2(x * TileSize + 32f, y * TileSize + 32f));
        pool.States[0] = AgentState.Idle;

        var job = new JobData
        {
            Id = 2,
            TypeId = JobTypeId.BlueprintDelivery,
            TargetX = x + 2,
            TargetY = y + 2,
            TargetItemCount = 2,
            CurrentDeliveredCount = 0
        };

        var handler = new BlueprintDeliveryJobHandler();
        handler.OnStart(0, in job, pool, ctx);

        if (pool.States[0] != AgentState.MovingToSource || !pool.ReservationFromStockpile(0))
        {
            GD.PrintErr($"[RESERVE-TYPE] FAIL: ожидался складской резерв бревна " +
                        $"(state={pool.States[0]}, fromStock={pool.ReservationFromStockpile(0)})");
            return false;
        }
        if (pool.ReservedItemId[0] != ItemId.Log
            || pool.ReservedItemCount[0] <= 0
            || pool.ReservedItemCount[0] > 4)
        {
            GD.PrintErr($"[RESERVE-TYPE] FAIL: запись о складском резерве " +
                        $"({pool.ReservedItemId[0]}, {pool.ReservedItemCount[0]}), ожидалось (Log, 1..4)");
            return false;
        }
        if (gm.AvailableCountOf(ItemId.Grain) != 3)
        {
            GD.PrintErr($"[RESERVE-TYPE] FAIL: складской резерв тронул наземное зерно " +
                        $"({gm.AvailableCountOf(ItemId.Grain)}), ожидалось 3");
            return false;
        }

        handler.OnCancel(0, pool, ctx);

        int grainAfterCancel = gm.AvailableCountOf(ItemId.Grain);
        int grainOnGround = gm.GetItemCountAt(x, y);
        if (grainAfterCancel != 3 || grainAfterCancel != grainOnGround)
        {
            GD.PrintErr($"[RESERVE-TYPE] FAIL: отмена складского резерва дала фантомный тотал зерна — " +
                        $"доступно {grainAfterCancel}, на земле {grainOnGround}, ожидалось 3/3");
            return false;
        }

        if (!sm.TryReserveStockpileItem(pool.GetPosition(0), ItemId.Log, 4, 0,
                out var logCell, out int logAvail) || logAvail != 4)
        {
            GD.PrintErr($"[RESERVE-TYPE] FAIL: складской резерв не снят — доступно брёвен " +
                        $"{logAvail}, ожидалось 4");
            return false;
        }
        sm.CancelWithdrawalReservation(logCell.X, logCell.Y, logAvail);

        return true;
    }

    /// <summary>
    /// Делает клетку складской синхронно: `ActivatePendingZone` ставит бит зоны и создаёт
    /// слот, не дёргая `CallDeferred` из `AddZoneTile` (в headless-старте это лишний риск).
    /// </summary>
    private static void MakeStockpileTile(StockpileManager sm, int x, int y)
    {
        sm.ActivatePendingZone(new List<(int X, int Y)> { (x, y) });
    }

    private static SimulationContext MakeContext(int w, int h)
    {
        var ground = new TileType[w, h];
        var trees = new bool[w, h];
        var stones = new bool[w, h];
        var solid = new bool[w, h];
        var walkable = new List<(int X, int Y)>(w * h);
        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                ground[x, y] = TileType.Grass;
                walkable.Add((x, y));
            }
        }

        return new SimulationContext(ground, null, trees, solid, walkable,
            new AgentSpatialGrid(w, h), new AgentMovementService(), new Random(42), TileSize, stones);
    }
}