using System;
using Godot;
using Game.Core;
using Game.Simulation;
using Vector2 = System.Numerics.Vector2;

namespace Game.Tests;

/// <summary>
/// Инварианты склада: 08-05, 08-06, 08-11.
///
/// Стенд повторяет РЕАЛЬНЫЙ путь груза, как в игре (см. StockpileHaulingJobHandler):
///   OnStart — `TryReserveStockpileSlot` кладёт принятое количество в ReservedIncoming,
///             оставляя Count = 0 (резерв «на будущее»);
///   Commit  — `DepositItems(слот, тип, сколько несёт)` переносит этот резерв в Count.
/// Поэтому заполнять слот надо парой «резерв → сдача», а не прямым DepositItems на
/// произвольное количество: иначе стенд проверяет состояние, которого в игре не бывает.
/// Для 08-06/08-11 используется прямой публичный путь (AddZoneTile / RestoreSlotRaw):
/// Callable/события рендера на результат не влияют, а IsWallAt читается синхронно.
///
/// 08-05. Как было: `DepositItems` писал `itemId` поверх типа слота и не проверял
/// ни существование слота, ни `MaxStack`. Если слота нет (клетка отложенной
/// pending-зоны или зону стёрли, пока носильщик шёл), метод молча ничего не делал,
/// а вызывающий (`StockpileHaulingJobHandler.Commit`) уже обнулил
/// `CarriedItemCount` — груз исчезал. Если в слоте лежал другой тип, старые
/// единицы физически пропадали, а `_liveTotals` врал. Если стак был полон,
/// получалось 166 зерна при лимите 100.
/// Теперь: слот проверяется, чужой тип отвергается, приём клампится по `MaxStack`,
/// а всё непринятое возвращается на землю рядом. Предметы не создаются и не
/// исчезают от неудачного депозита.
///
/// 08-06. Как было: `_freeSlotsCount` увеличивался на КАЖДУЮ новую клетку зоны,
/// не глядя на стену/стройплощадку, а `TryReserveStockpileSlot` такие клетки
/// пропускал. Кольцо стен и застройка поверх склада навсегда числились свободным
/// местом: `HasFreeSpace` истинно, haul-работы создаются вечно, слот не находится,
/// агенты бегают вхолостую. Теперь у счётчика и у резерва один и тот же гейт.
///
/// 08-11. Как было: `RestoreSlotRaw` клампил `count` только снизу, поэтому
/// порченый/чужой сейв давал `Count > MaxStack`, и слот больше никогда не
/// дозаполнялся (`totalCount &lt; MaxStack` не выполняется). Теперь клампится.
///
/// Тест падает на старом коде всеми тремя проверками.
///
/// Запуск: godot --headless --path . --script res://tests/run_stockpile_invariants.gd
/// </summary>
public partial class TestStockpileInvariants : RefCounted
{
    // ItemRegistry.Log/Grain/Stone.MaxStack = 100.
    private const int StackMax = 100;

    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print("[STOCK-INVARIANTS] Склад: депозит, свободные слоты, MaxStack (08-05/06/11)");
        GD.Print("==================================================");

        if (!CheckDepositKeepsItems())
            return false;
        if (!CheckBlockedSlotIsNotFreeSpace())
            return false;
        if (!CheckRestoreSlotClampsToMaxStack())
            return false;

        ClearAll();
        GD.Print("[STOCK-INVARIANTS] OK: груз цел при отказе депозита, " +
                 "слот под стеной не считается местом, MaxStack не превышается");
        return true;
    }

    /// <summary>
    /// 08-05: неудачный депозит не теряет и не создаёт предметы.
    /// (а) клетки-слота нет вовсе; (б) слот полон; (в) в слоте другой тип.
    /// </summary>
    private static bool CheckDepositKeepsItems()
    {
        StockpileManager sm = StockpileManager.Instance;
        GroundItemManager gm = GroundItemManager.Instance;

        // (а) Сдача в клетку, которой нет в _storage: зону стёрли, пока агент шёл,
        // либо клетка принадлежит отложенной (pending) зоне — IsZoneTile там true.
        ClearAll();
        const int ghostX = 50, ghostY = 50;
        sm.DepositItems(ghostX, ghostY, ItemId.Log, 30);

        int ghostGround = gm.GetItemCountAt(ghostX, ghostY);
        int ghostLive = sm.GetLiveItemCount(ItemId.Log);
        if (ghostGround != 30 || ghostLive != 0)
        {
            GD.PrintErr($"[STOCK-INVARIANTS] FAIL (08-05а): депозит в клетку без слота потерял груз — " +
                        $"на земле {ghostGround}, на складе {ghostLive}, ожидалось 30/0 | {Dump(ghostX, ghostY)}");
            return false;
        }

        // (б) Слот, забитый до MaxStack, и (в) чужой тип в нём.
        ClearAll();
        const int slotX = 60, slotY = 60;
        sm.AddZoneTile(slotX, slotY);
        if (!sm.IsZoneTile(slotX, slotY) || !sm.HasFreeSpace)
        {
            GD.PrintErr($"[STOCK-INVARIANTS] FAIL (стенд): новая клетка зоны не дала свободный слот | " +
                        $"{Dump(slotX, slotY)}");
            return false;
        }

        // Заполняем слот так же, как носильщик: OnStart резервирует, Commit сдаёт
        // ровно то, что несёт (ReservedIncoming == сдаваемое количество).
        if (!sm.TryReserveStockpileSlot(Pos(slotX, slotY), ItemId.Log, StackMax,
                out var reservedSlot, out int accepted)
            || reservedSlot != (slotX, slotY) || accepted != StackMax)
        {
            GD.PrintErr($"[STOCK-INVARIANTS] FAIL (стенд): резерв выдал " +
                        $"({reservedSlot.X},{reservedSlot.Y}) x{accepted}, ожидалось ({slotX},{slotY}) x{StackMax} | " +
                        $"{Dump(slotX, slotY)}");
            return false;
        }
        sm.DepositItems(slotX, slotY, ItemId.Log, accepted);

        var full = SlotAt(slotX, slotY);
        if (full.Item != ItemId.Log || full.Count != StackMax)
        {
            GD.PrintErr($"[STOCK-INVARIANTS] FAIL (стенд): после резерва x{accepted} и сдачи в слоте " +
                        $"({full.Item}, {full.Count}), ожидалось (Log, {StackMax}) | {Dump(slotX, slotY)}");
            return false;
        }

        // Сдача сверх предела: слот обязан отказать, а излишек — лечь на землю.
        int before = sm.GetLiveItemCount(ItemId.Log) + gm.AvailableCountOf(ItemId.Log);
        sm.DepositItems(slotX, slotY, ItemId.Log, 10);

        int stored = SlotAt(slotX, slotY).Count;
        int after = sm.GetLiveItemCount(ItemId.Log) + gm.AvailableCountOf(ItemId.Log);
        if (stored > StackMax)
        {
            GD.PrintErr($"[STOCK-INVARIANTS] FAIL (08-05б): слот превысил MaxStack — " +
                        $"в нём {stored} при пределе {StackMax} | {Dump(slotX, slotY)}");
            return false;
        }
        if (after != before + 10)
        {
            GD.PrintErr($"[STOCK-INVARIANTS] FAIL (08-05б): излишек не вернулся на землю — " +
                        $"всего стало {after} (было {before}), ожидалось {before + 10} | {Dump(slotX, slotY)}");
            return false;
        }

        sm.DepositItems(slotX, slotY, ItemId.Grain, 5);

        var kept = SlotAt(slotX, slotY);
        if (kept.Item != ItemId.Log || kept.Count != StackMax)
        {
            GD.PrintErr($"[STOCK-INVARIANTS] FAIL (08-05в): депозит чужого типа перезаписал слот — " +
                        $"в нём ({kept.Item}, {kept.Count}), ожидалось (Log, {StackMax}) | {Dump(slotX, slotY)}");
            return false;
        }
        if (sm.GetLiveItemCount(ItemId.Grain) != 0 || gm.AvailableCountOf(ItemId.Grain) != 5)
        {
            GD.PrintErr($"[STOCK-INVARIANTS] FAIL (08-05в): зерно от чужого слота разъехалось — " +
                        $"на складе {sm.GetLiveItemCount(ItemId.Grain)}, " +
                        $"на земле {gm.AvailableCountOf(ItemId.Grain)}, ожидалось 0/5 | {Dump(slotX, slotY)}");
            return false;
        }

        return true;
    }

    /// <summary>
    /// 08-06: клетка, занятая стеной, не даёт свободного слота, а обычная — даёт.
    /// </summary>
    private static bool CheckBlockedSlotIsNotFreeSpace()
    {
        ClearAll();
        StockpileManager sm = StockpileManager.Instance;

        const int wallX = 70, wallY = 70;
        if (!StructureManager.Instance.AddStructure(wallX, wallY, StructureRegistry.WoodWall)
            || !StructureManager.Instance.IsWallAt(wallX, wallY))
        {
            GD.PrintErr("[STOCK-INVARIANTS] FAIL (стенд): не удалось поставить стену на клетку зоны");
            return false;
        }

        // Склад нарисован поверх стены (R-016: стены строятся по клеткам зоны).
        sm.AddZoneTile(wallX, wallY);
        if (sm.HasFreeSpace)
        {
            GD.PrintErr($"[STOCK-INVARIANTS] FAIL (08-06): клетка под стеной числится свободным " +
                        $"слотом — HasFreeSpace обещает место, которого нет | {Dump(wallX, wallY)}");
            return false;
        }

        // Контроль: обычная клетка рядом место даёт (счётчик не сломан целиком).
        sm.AddZoneTile(wallX + 2, wallY + 2);
        if (!sm.HasFreeSpace)
        {
            GD.PrintErr($"[STOCK-INVARIANTS] FAIL (08-06): свободная клетка перестала считаться местом | " +
                        $"{Dump(wallX + 2, wallY + 2)}");
            return false;
        }

        // И резерв не должен выдать клетку со стеной.
        if (!sm.TryReserveStockpileSlot(Pos(wallX, wallY), ItemId.Log, 5, out var slot, out int accepted)
            || accepted <= 0)
        {
            GD.PrintErr($"[STOCK-INVARIANTS] FAIL (08-06): резерв не нашёл свободный слот рядом со стеной | " +
                        $"{Dump(wallX, wallY)}");
            return false;
        }
        if (slot == (wallX, wallY))
        {
            GD.PrintErr($"[STOCK-INVARIANTS] FAIL (08-06): резерв выдал клетку со стеной | {Dump(wallX, wallY)}");
            return false;
        }

        StructureManager.Instance.ClearAll();
        return true;
    }

    /// <summary>
    /// 08-11: количество из сейва клампится по MaxStack, тотал склада равен
    /// содержимому слота, а дозаливка уходит на землю.
    /// </summary>
    private static bool CheckRestoreSlotClampsToMaxStack()
    {
        ClearAll();
        StockpileManager sm = StockpileManager.Instance;
        GroundItemManager gm = GroundItemManager.Instance;

        const int slotX = 80, slotY = 80;
        sm.AddZoneTile(slotX, slotY);
        if (!sm.IsZoneTile(slotX, slotY) || !sm.HasFreeSpace)
        {
            GD.PrintErr($"[STOCK-INVARIANTS] FAIL (стенд): новая клетка зоны не дала свободный слот | " +
                        $"{Dump(slotX, slotY)}");
            return false;
        }

        // Порченый payload: в слот пишут в пять раз больше предела.
        if (!sm.RestoreSlotRaw(slotX, slotY, ItemId.Log, StackMax * 5))
        {
            GD.PrintErr($"[STOCK-INVARIANTS] FAIL (08-11): восстановление слота отказало | " +
                        $"{Dump(slotX, slotY)}");
            return false;
        }

        var slot = SlotAt(slotX, slotY);
        if (slot.Item != ItemId.Log || slot.Count > StackMax)
        {
            GD.PrintErr($"[STOCK-INVARIANTS] FAIL (08-11): в слоте ({slot.Item}, {slot.Count}) — " +
                        $"предел MaxStack {StackMax} превышен | {Dump(slotX, slotY)}");
            return false;
        }
        if (sm.GetLiveItemCount(ItemId.Log) != slot.Count)
        {
            GD.PrintErr($"[STOCK-INVARIANTS] FAIL (08-11): тотал склада " +
                        $"{sm.GetLiveItemCount(ItemId.Log)} не равен содержимому слота {slot.Count} | " +
                        $"{Dump(slotX, slotY)}");
            return false;
        }

        // Клампнутый слот полон: дозаливка не должна пробить предел.
        sm.DepositItems(slotX, slotY, ItemId.Log, 5);
        var after = SlotAt(slotX, slotY);
        if (after.Count > StackMax)
        {
            GD.PrintErr($"[STOCK-INVARIANTS] FAIL (08-11): после восстановления слот принял " +
                        $"{after.Count} при пределе {StackMax} | {Dump(slotX, slotY)}");
            return false;
        }
        if (gm.AvailableCountOf(ItemId.Log) != 5)
        {
            GD.PrintErr($"[STOCK-INVARIANTS] FAIL (08-11): излишек не вернулся на землю — " +
                        $"на земле {gm.AvailableCountOf(ItemId.Log)}, ожидалось 5 | {Dump(slotX, slotY)}");
            return false;
        }

        return true;
    }

    private static Vector2 Pos(int x, int y) => new(x * 64f + 32f, y * 64f + 32f);

    /// <summary>Содержимое слота склада по снимку (GetStorageSnapshot).</summary>
    private static (ItemId Item, int Count) SlotAt(int x, int y)
    {
        var snap = StockpileManager.Instance.GetStorageSnapshot();
        for (int i = 0; i < snap.Count; i++)
        {
            var s = snap[i];
            if (s.X == x && s.Y == y)
                return (s.Item, s.Count);
        }
        return (ItemId.None, 0);
    }

    /// <summary>Состояние площадки одной строкой — только для сообщений об ошибке.</summary>
    private static string Dump(int x, int y)
    {
        StockpileManager sm = StockpileManager.Instance;
        GroundItemManager gm = GroundItemManager.Instance;
        var slot = SlotAt(x, y);
        return $"слот({x},{y})=({slot.Item},{slot.Count})" +
               $" live[Log]={sm.GetLiveItemCount(ItemId.Log)}" +
               $" live[Grain]={sm.GetLiveItemCount(ItemId.Grain)}" +
               $" земля@({x},{y})={gm.GetItemCountAt(x, y)}" +
               $" земля[Log]={gm.AvailableCountOf(ItemId.Log)}" +
               $" земля[Grain]={gm.AvailableCountOf(ItemId.Grain)}" +
               $" HasFreeSpace={sm.HasFreeSpace}" +
               $" zoneTiles={sm.ZoneTileCount}";
    }

    private static void ClearAll()
    {
        GroundItemManager.Instance.ClearAll();
        StockpileManager.Instance.ClearAll();
        StructureManager.Instance.ClearAll();
    }
}