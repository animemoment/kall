using System;
using Godot;
using Game.Core;
using Game.Simulation;

namespace Game.Tests;

/// <summary>
/// 08-03: перенос кучи не должен оставлять «фантомный дефицит».
///
/// Как было: агент резервировал N предметов в клетке, кучу сдвигали рядом
/// (`TrySpillItemsAdjacent`), причём чужой резерв переезжал вместе с ней. Агент потом
/// освобождал резерв по СТАРОЙ клетке — клетки уже нет, `ReleaseReservation` ничего не
/// возвращал, и эти N предметов навсегда выпадали из тотала. В игре: «брёвен нет»,
/// хотя они лежат в двух клетках рядом; работа встаёт.
///
/// Теперь резерв при переносе возвращается в тотал, а не едет с кучей.
///
/// Запуск: godot --headless --path . --script res://tests/run_item_spill_reserve.gd
/// </summary>
public partial class TestItemSpillReserve : RefCounted
{
    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print("[SPILL-RESERVE] Перенос кучи не съедает резерв (08-03)");
        GD.Print("==================================================");

        GroundItemManager gm = GroundItemManager.Instance;
        gm.ClearAll();

        const int x = 40, y = 40;
        const int total = 10;
        gm.SpawnItems(x, y, ItemId.Log, total);

        int before = gm.AvailableCountOf(ItemId.Log);
        if (before != total)
        {
            GD.PrintErr($"[SPILL-RESERVE] FAIL: после спавна доступно {before}, ожидалось {total}");
            return false;
        }

        // Резервируем часть кучи — как это делает агент перед переноской.
        if (!gm.TryReserveSpecificCell(x, y, 200f, out ItemId reservedId, out int reserved)
            || reservedId != ItemId.Log || reserved <= 0)
        {
            GD.PrintErr($"[SPILL-RESERVE] FAIL: не удалось зарезервировать ({reservedId}, {reserved})");
            return false;
        }

        int afterReserve = gm.AvailableCountOf(ItemId.Log);
        if (afterReserve != total - reserved)
        {
            GD.PrintErr($"[SPILL-RESERVE] FAIL: после резерва доступно {afterReserve}, " +
                        $"ожидалось {total - reserved}");
            return false;
        }

        if (!gm.TrySpillItemsAdjacent(x, y, 64, 64))
        {
            GD.PrintErr("[SPILL-RESERVE] FAIL: куча не сдвинулась (нет свободной соседней клетки)");
            return false;
        }

        if (gm.PeekItemAt(x, y) != ItemId.None)
        {
            GD.PrintErr("[SPILL-RESERVE] FAIL: старая клетка всё ещё занята");
            return false;
        }

        // Главное: доступное количество вернулось к полному — резерв не потерялся.
        int afterSpill = gm.AvailableCountOf(ItemId.Log);
        if (afterSpill != total)
        {
            GD.PrintErr($"[SPILL-RESERVE] FAIL: после переноса доступно {afterSpill}, " +
                        $"ожидалось {total} — «фантомный дефицит» на месте");
            return false;
        }

        // Освобождение резерва по старой клетке не должно ничего ломать.
        gm.ReleaseReservation(x, y, reserved);
        int afterRelease = gm.AvailableCountOf(ItemId.Log);
        if (afterRelease != total)
        {
            GD.PrintErr($"[SPILL-RESERVE] FAIL: после освобождения по старой клетке " +
                        $"доступно {afterRelease}, ожидалось {total}");
            return false;
        }

        GD.Print($"[SPILL-RESERVE] OK: резерв {reserved} вернулся в тотал, доступно {total} до и после переноса");
        gm.ClearAll();
        return true;
    }
}