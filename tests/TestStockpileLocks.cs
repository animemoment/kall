using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Game.Core;
using Game.Simulation;
using Vector2 = System.Numerics.Vector2;

namespace Game.Tests;

/// <summary>
/// Склад: снос с входящим резервом (08-07) и порядок блокировок склад ↔ стройка (08-02).
///
/// 08-07. Как было: снос клетки склада выбрасывал только физически лежащий `Count`,
/// а `ReservedIncoming`/`ReservedOutgoing` просто исчезали вместе со слотом. Груз
/// «в пути» при этом уцелевал только потому, что 08-05 научил `DepositItems`
/// возвращать непринятое на землю, — но возвращал он его в САМУ клетку, не глядя
/// на стену/стройплощадку. Агент до такой кучи не дойдёт, работа на неё создаётся
/// и вечно упирается в `MarkJobUnreachable`: предметы «висят» в счётчиках и
/// недоступны игроку. Теперь и снос, и отказ депозита возвращают груз по одному
/// правилу (`StockpileManager.FindCargoDropCell`): не в складскую зону и не под
/// стену/стройку, а в проходимого соседа. Резервы слота снимаются вместе с ним,
/// входящий резерв НЕ материализуется (груз по нему ещё не в слоте — иначе
/// получились бы копии), а возвращает его в мир штатный путь носильщика.
/// Инвариант: предметы не создаются и не исчезают — `GetLiveItemCount` плюс
/// доступное на земле до и после сноса совпадают.
///
/// 08-02. Как было: `TryReserveStockpileSlot` держал `StockpileManager._lock` и
/// внутри спирального скана на КАЖДУЮ клетку брал `ConstructionManager._lock`
/// (`IsSiteAt`) и `StructureManager._lock` (`IsWallAt`), а `StartSitesBatch` держал
/// свой lock и внутри звал `GroundItemManager.HasItemsAt`/`StructureManager.HasStructureAt`,
/// а `GroundItemManager` для клеток за островом берёт складской lock в `IsZoneTile`.
/// Получался цикл S→C→G→S: стройка держит C и ждёт G, земляной поток держит G и
/// ждёт S, носильщик держит S и ждёт C. Теперь складской и строительный lock —
/// листья: чужие вызовы вынесены из-под них (сбор кандидатов под локом → проверка
/// стены/стройки вне лока → фиксация вторым коротким локом).
///
/// Тест падает на старом коде: (1) груз после сноса оказывается под стеной,
/// (2) сценарий «стройка зовёт склад, склад зовёт стройку» может встать намертво
/// и не уложиться в таймаут.
///
/// Запуск: godot --headless --path . --script res://tests/run_stockpile_locks.gd
/// </summary>
public partial class TestStockpileLocks : RefCounted
{
    // ItemRegistry.Log/Grain/Stone.MaxStack = 100.
    private const int StackMax = 100;
    private const int TileSize = 64;

    // Сценарий 08-02 — состязательный; ждём его не дольше этого. Мёртвая
    // блокировка даёт «вечное» ожидание, поэтому таймаут и есть проверка.
    private const int LockStressTimeoutMs = 15000;

    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print("[STOCK-LOCKS] Снос склада с резервом и порядок локов склад↔стройка (08-07/08-02)");
        GD.Print("==================================================");

        if (!CheckDemolitionReleasesIncomingCargo())
            return false;
        if (!CheckDemolitionDropsCargoOutOfWalls())
            return false;
        // Последним: если на старом коде тут встанет намертво — остальное уже проверено,
        // а процесс всё равно завершится по quit() (потоки пула фоновые).
        if (!CheckNoLockCycleUnderLoad())
            return false;

        ClearAll();
        GD.Print("[STOCK-LOCKS] OK: резервы снимаются сносом, груз возвращается в мир " +
                 "не под стену, цикл локов S->C->G->S разорван");
        return true;
    }

    /// <summary>
    /// 08-07: пока носильщик идёт на склад, его груз числится в `ReservedIncoming`
    /// слота (сам груз при этом ещё на земле или в руках). Снос склада не должен ни
    /// потерять, ни удвоить его: резерв снимается, груз возвращается в мир штатным
    /// путём носильщика (`DepositItems` → отказ → земля), а новый слот на этой же
    /// клетке не наследует мёртвый резерв.
    /// </summary>
    private static bool CheckDemolitionReleasesIncomingCargo()
    {
        ClearAll();
        StockpileManager sm = StockpileManager.Instance;
        GroundItemManager gm = GroundItemManager.Instance;

        const int srcX = 90, srcY = 100, slotX = 100, slotY = 100;

        // 1. Куча на земле + её резерв «под переноску» (StockpileHaulingJobHandler.OnStart).
        gm.SpawnItems(srcX, srcY, ItemId.Log, 40);
        // Тотал «в мире» до сноса: на складе 0, на земле доступно 40.
        int totalBefore = sm.GetLiveItemCount(ItemId.Log) + gm.AvailableCountOf(ItemId.Log);
        if (totalBefore != 40)
        {
            GD.PrintErr($"[STOCK-LOCKS] FAIL (стенд): куча на земле дала тотал {totalBefore}, " +
                        $"ожидалось 40 | {Dump(srcX, srcY)}");
            return false;
        }
        if (!gm.TryReserveSpecificCell(srcX, srcY, 500f, out var groundId, out int groundReserved)
            || groundId != ItemId.Log || groundReserved != 40)
        {
            GD.PrintErr($"[STOCK-LOCKS] FAIL (стенд): резерв кучи дал ({groundId}, {groundReserved}), " +
                        $"ожидалось ({ItemId.Log}, 40) | {Dump(srcX, srcY)}");
            return false;
        }

        // 2. Слот склада зарезервирован под эту же сдачу (ReservedIncoming, Count = 0).
        sm.ActivatePendingZone(new List<(int X, int Y)> { (slotX, slotY) });
        if (!sm.TryReserveStockpileSlot(Pos(slotX, slotY), ItemId.Log, groundReserved, out var slot, out int accepted)
            || slot != (slotX, slotY) || accepted != 40)
        {
            GD.PrintErr($"[STOCK-LOCKS] FAIL (стенд): резерв слота дал ({slot.X},{slot.Y}) x{accepted}, " +
                        $"ожидалось ({slotX},{slotY}) x40 | {Dump(slotX, slotY)}");
            return false;
        }
        if (!sm.TryGetSlotState(slotX, slotY, out int countInSlot, out int resIn, out int resOut)
            || countInSlot != 0 || resIn != 40 || resOut != 0)
        {
            GD.PrintErr($"[STOCK-LOCKS] FAIL (стенд): в слоте ({countInSlot}, {resIn}, {resOut}), " +
                        $"ожидалось (0, 40, 0) | {Dump(slotX, slotY)}");
            return false;
        }

        // 3. Носильщик забрал груз с земли (Commit: TakeItemsWithType).
        int taken = gm.TakeItems(srcX, srcY, groundReserved);
        if (taken != 40)
        {
            GD.PrintErr($"[STOCK-LOCKS] FAIL (стенд): забрано с земли {taken}, ожидалось 40 | {Dump(srcX, srcY)}");
            return false;
        }

        // 4. Игрок стирает склад, пока носильщик идёт.
        sm.RemoveZoneTilesBatch(new List<(int X, int Y)> { (slotX, slotY) });

        if (sm.TryGetSlotState(slotX, slotY, out _, out int leftIn, out int leftOut))
        {
            GD.PrintErr($"[STOCK-LOCKS] FAIL (08-07): после сноса остался слот " +
                        $"(ReservedIncoming={leftIn}, ReservedOutgoing={leftOut}) | {Dump(slotX, slotY)}");
            return false;
        }
        if (sm.GetLiveItemCount(ItemId.Log) != 0)
        {
            GD.PrintErr($"[STOCK-LOCKS] FAIL (08-07): снос выдумал груз на складе — " +
                        $"live={sm.GetLiveItemCount(ItemId.Log)} | {Dump(slotX, slotY)}");
            return false;
        }

        // 5. Носильщик доходит до стёртой клетки и сдаёт груз ровно как в Commit.
        if (sm.IsZoneTile(slotX, slotY))
            sm.DepositItems(slotX, slotY, ItemId.Log, taken);
        else
            gm.SpawnItems(slotX, slotY, ItemId.Log, taken);

        int totalAfter = sm.GetLiveItemCount(ItemId.Log) + gm.AvailableCountOf(ItemId.Log);
        if (totalAfter != totalBefore)
        {
            GD.PrintErr($"[STOCK-LOCKS] FAIL (08-07): тотал до/после сноса разъехался — " +
                        $"было {totalBefore}, стало {totalAfter} | {Dump(slotX, slotY)}");
            return false;
        }

        // 6. Клетка снова стала зоной: новый слот не наследует мёртвый резерв.
        sm.ActivatePendingZone(new List<(int X, int Y)> { (slotX, slotY) });
        if (!sm.TryGetSlotState(slotX, slotY, out int count2, out int resIn2, out int resOut2)
            || count2 != 0 || resIn2 != 0 || resOut2 != 0)
        {
            GD.PrintErr($"[STOCK-LOCKS] FAIL (08-07): новый слот унаследовал резерв " +
                        $"({count2}, {resIn2}, {resOut2}), ожидалось (0, 0, 0) | {Dump(slotX, slotY)}");
            return false;
        }
        if (!sm.TryReserveStockpileSlot(Pos(slotX, slotY), ItemId.Log, StackMax, out _, out int accepted2)
            || accepted2 != StackMax)
        {
            GD.PrintErr($"[STOCK-LOCKS] FAIL (08-07): после сноса слот не отдаёт полный стак — " +
                        $"принято {accepted2} из {StackMax} (мёртвый резерв в счётчике) | {Dump(slotX, slotY)}");
            return false;
        }
        sm.CancelReservation(slotX, slotY, accepted2);

        ClearAll();
        return true;
    }

    /// <summary>
    /// 08-07: груз снесённого слота возвращается в мир по тем же правилам, что отказ
    /// депозита (`DepositItems` → `SpawnRejectedDeposit`) и спилл
    /// (`TrySpillItemsAdjacent`): не в складскую зону и не под стену/стройплощадку.
    /// Прежний код кидал кучу в саму клетку — под стену, которую поставили поверх
    /// склада (R-016), и до неё нельзя дойти.
    /// </summary>
    private static bool CheckDemolitionDropsCargoOutOfWalls()
    {
        ClearAll();
        StockpileManager sm = StockpileManager.Instance;
        GroundItemManager gm = GroundItemManager.Instance;

        const int x = 120, y = 120;

        sm.ActivatePendingZone(new List<(int X, int Y)> { (x, y) });
        // Груз кладём штатным путём носильщика: резерв → сдача.
        if (!sm.TryReserveStockpileSlot(Pos(x, y), ItemId.Log, 70, out _, out int accepted) || accepted != 70)
        {
            GD.PrintErr($"[STOCK-LOCKS] FAIL (стенд): резерв слота дал x{accepted}, ожидалось x70 | {Dump(x, y)}");
            return false;
        }
        sm.DepositItems(x, y, ItemId.Log, accepted);
        if (sm.GetLiveItemCount(ItemId.Log) != 70)
        {
            GD.PrintErr($"[STOCK-LOCKS] FAIL (стенд): на складе {sm.GetLiveItemCount(ItemId.Log)}, " +
                        $"ожидалось 70 | {Dump(x, y)}");
            return false;
        }

        // Игрок ставит стену поверх склада (R-016) и стирает зону.
        if (!StructureManager.Instance.AddStructure(x, y, StructureRegistry.WoodWall)
            || !StructureManager.Instance.IsWallAt(x, y))
        {
            GD.PrintErr($"[STOCK-LOCKS] FAIL (стенд): не удалось поставить стену поверх склада | {Dump(x, y)}");
            return false;
        }

        sm.RemoveZoneTilesBatch(new List<(int X, int Y)> { (x, y) });

        int live = sm.GetLiveItemCount(ItemId.Log);
        int ground = gm.AvailableCountOf(ItemId.Log);
        int underWall = gm.GetItemCountAt(x, y);

        if (underWall != 0)
        {
            GD.PrintErr($"[STOCK-LOCKS] FAIL (08-07): груз снесённого склада остался под стеной " +
                        $"({underWall} брёвен в ({x},{y})) — агент до него не дойдёт | {Dump(x, y)}");
            return false;
        }
        if (live != 0 || ground != 70)
        {
            GD.PrintErr($"[STOCK-LOCKS] FAIL (08-07): тотал после сноса разъехался — " +
                        $"на складе {live}, на земле {ground}, ожидалось 0/70 | {Dump(x, y)}");
            return false;
        }

        // Груз должен лежать в проходимом соседе и вне складской зоны.
        int dropX = -1, dropY = -1;
        for (int oy = -1; oy <= 1 && dropX < 0; oy++)
        {
            for (int ox = -1; ox <= 1 && dropX < 0; ox++)
            {
                if (ox == 0 && oy == 0) continue;
                if (gm.GetItemCountAt(x + ox, y + oy) > 0)
                {
                    dropX = x + ox;
                    dropY = y + oy;
                }
            }
        }
        if (dropX < 0)
        {
            GD.PrintErr($"[STOCK-LOCKS] FAIL (08-07): груз не найден ни в одной соседней клетке | {Dump(x, y)}");
            return false;
        }
        if (StructureManager.Instance.IsWallAt(dropX, dropY))
        {
            GD.PrintErr($"[STOCK-LOCKS] FAIL (08-07): груз лёг под стену в ({dropX},{dropY}) | {Dump(x, y)}");
            return false;
        }
        if (sm.IsZoneTile(dropX, dropY))
        {
            GD.PrintErr($"[STOCK-LOCKS] FAIL (08-07): груз лёг в складскую зону ({dropX},{dropY}) — " +
                        $"хаулинг такие клетки игнорирует | {Dump(x, y)}");
            return false;
        }

        StructureManager.Instance.ClearAll();
        ClearAll();
        return true;
    }

    /// <summary>
    /// 08-02: сценарий «стройка зовёт склад, склад зовёт стройку» под нагрузкой.
    /// Три потока повторяют ровно те рёбра графа блокировок, из которых складывался
    /// цикл:
    ///   A — `StockpileManager.TryReserveStockpileSlot` (раньше S→C/S→St: держал
    ///       складской lock и на каждую клетку брал lock стройки и структур);
    ///   B — `ConstructionManager.StartSitesBatch` (раньше C→G/C→St: держал свой
    ///       lock и опрашивал землю и структуры);
    ///   C — `GroundItemManager.TryReserveGroundItems` по куче ЗА островом
    ///       (G→S: `IsZoneTile` на медленном пути берёт складской lock).
    /// Цикл S→C→G→S = мёртвая блокировка; после 08-02 рёбер S→C и C→G нет, поэтому
    /// сценарий обязан завершиться. Старый код мог встать намертво — тогда сработает
    /// таймаут и тест упадёт (а не повиснет).
    /// </summary>
    private static bool CheckNoLockCycleUnderLoad()
    {
        ClearAll();
        StockpileManager sm = StockpileManager.Instance;
        GroundItemManager gm = GroundItemManager.Instance;
        ConstructionManager cm = ConstructionManager.Instance;

        const int stockX = 140, stockY = 140;
        sm.ActivatePendingZone(new List<(int X, int Y)> { (stockX, stockY), (stockX + 1, stockY) });

        // Куча далеко за островом (1024): IsZoneTile там идёт медленным путём
        // через _lock склада — это и есть ребро G→S.
        const int farX = 2000, farY = 2000;
        gm.SpawnItems(farX, farY, ItemId.Log, 100);

        var cells = new List<(int X, int Y)>();
        for (int i = 0; i < 8; i++)
            cells.Add((300 + i, 300));

        var errors = new ConcurrentQueue<Exception>();

        Task[] tasks =
        {
            Task.Run(() =>
            {
                try
                {
                    for (int i = 0; i < 400; i++)
                    {
                        if (sm.TryReserveStockpileSlot(Pos(stockX, stockY), ItemId.Log, 5, out var slot, out int acc)
                            && acc > 0)
                        {
                            sm.CancelReservation(slot.X, slot.Y, acc);
                        }
                    }
                }
                catch (Exception e) { errors.Enqueue(e); }
            }),
            Task.Run(() =>
            {
                try
                {
                    for (int i = 0; i < 30; i++)
                    {
                        cm.StartSitesBatch(cells, StructureRegistry.WoodWall, null);
                        cm.CancelSitesBatch(cells);
                    }
                }
                catch (Exception e) { errors.Enqueue(e); }
            }),
            Task.Run(() =>
            {
                try
                {
                    var farPos = new Vector2(farX * TileSize + 32f, farY * TileSize + 32f);
                    for (int i = 0; i < 400; i++)
                    {
                        if (gm.TryReserveGroundItems(farPos, 25f, false, ItemId.None, 4, out _, out _, out int rc)
                            && rc > 0)
                        {
                            gm.ReleaseReservation(farX, farY, rc);
                        }
                    }
                }
                catch (Exception e) { errors.Enqueue(e); }
            })
        };

        bool finished = Task.WaitAll(tasks, LockStressTimeoutMs);
        if (!finished)
        {
            // Никакой уборки: застрявшие потоки держат lock'и, звать менеджеры нельзя.
            GD.PrintErr($"[STOCK-LOCKS] FAIL (08-02): сценарий «стройка зовёт склад, склад зовёт " +
                        $"стройку» не завершился за {LockStressTimeoutMs} мс — мёртвая блокировка " +
                        $"(цикл S->C->G->S) | zoneTiles={sm.ZoneTileCount}");
            return false;
        }

        bool threadFailed = false;
        while (errors.TryDequeue(out var err))
        {
            threadFailed = true;
            GD.PrintErr($"[STOCK-LOCKS] FAIL (08-02): исключение в потоке нагрузки — {err}");
        }
        if (threadFailed)
            return false;

        // Склад обязан остаться рабочим: счётчик свободных слотов не разъехался,
        // резерв по-прежнему выдаётся и снимается.
        if (!sm.TryReserveStockpileSlot(Pos(stockX, stockY), ItemId.Log, 1, out var afterSlot, out int afterAcc)
            || afterAcc != 1)
        {
            GD.PrintErr($"[STOCK-LOCKS] FAIL (08-02): после нагрузки склад не выдаёт слот " +
                        $"(acc={afterAcc}) — счётчик свободных слотов испорчен | {Dump(stockX, stockY)}");
            return false;
        }
        sm.CancelReservation(afterSlot.X, afterSlot.Y, afterAcc);

        ClearAll();
        return true;
    }

    private static Vector2 Pos(int x, int y) => new(x * TileSize + 32f, y * TileSize + 32f);

    /// <summary>Состояние клетки одной строкой — только для сообщений об ошибке.</summary>
    private static string Dump(int x, int y)
    {
        StockpileManager sm = StockpileManager.Instance;
        GroundItemManager gm = GroundItemManager.Instance;
        bool hasSlot = sm.TryGetSlotState(x, y, out int count, out int resIn, out int resOut);
        string slotText = hasSlot ? count + "/" + resIn + "/" + resOut : "нет";
        return $"слот({x},{y})={slotText}" +
               $" live[Log]={sm.GetLiveItemCount(ItemId.Log)}" +
               $" земля@({x},{y})={gm.GetItemCountAt(x, y)}" +
               $" земля[Log]={gm.AvailableCountOf(ItemId.Log)}" +
               $" HasFreeSpace={sm.HasFreeSpace}" +
               $" zoneTiles={sm.ZoneTileCount}" +
               $" site={ConstructionManager.Instance.IsSiteAt(x, y)}" +
               $" wall={StructureManager.Instance.IsWallAt(x, y)}";
    }

    private static void ClearAll()
    {
        GroundItemManager.Instance.ClearAll();
        StockpileManager.Instance.ClearAll();
        ConstructionManager.Instance.ClearAll();
        StructureManager.Instance.ClearAll();
    }
}