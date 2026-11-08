using System;
using System.Threading;
using Godot;
using Game.Core;
using Game.Simulation;
using Game.Simulation.Pathfinding;

namespace Game.Tests;

/// <summary>
/// 05-01: дебаунс инвалидации пасфайндера острова обязан иметь «хвост».
///
/// Что доказываем: если две стены поставлены в разных тиках ближе 200 мс друг к
/// другу, вторая инвалидация не должна потеряться. Иначе оверлей остаётся
/// построенным по сетке, где второй стены ещё нет, и агент идёт сквозь неё.
///
/// Механика: `Invalidate()` внутри 200-мс окна раньше просто возвращал — флаг
/// «перестроить позже» не ставился. Тест ставит две стены подряд, ждёт конец
/// окна и просит путь: опубликованная стоимость второй стены обязана быть
/// непроходимой.
///
/// Запуск: godot --headless --path . --script res://tests/run_invalidate_tail.gd
/// </summary>
public partial class TestInvalidateTail : RefCounted
{
    private const int W = 64;
    private const int H = 64;

    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print("[INV-TAIL] Хвост дебаунса инвалидации (05-01)");
        GD.Print("==================================================");

        bool ok = true;
        ok &= SecondInvalidationApplied();
        ok &= SingleInvalidationStillWorks();

        HierarchicalPathfinder.Instance.Reset();
        return ok;
    }

    /// <summary>Две стены подряд внутри 200 мс: обе обязаны попасть в оверлей.</summary>
    private bool SecondInvalidationApplied()
    {
        var ground = new TileType[W, H];      // весь остров — трава
        var solid = new bool[W, H];
        var trees = new bool[W, H];
        var walkable = new System.Collections.Generic.List<(int X, int Y)>();
        var spatial = new AgentSpatialGrid(W, H);
        var move = new AgentMovementService();
        var ctx = new SimulationContext(ground, null, trees, solid, walkable, spatial, move, new Random(7));

        HierarchicalPathfinder pf = HierarchicalPathfinder.Instance;
        pf.Reset();
        pf.Initialize(ctx);

        // Первый запрос строит оверлей «чистого» острова.
        if (!pf.TryFindPath(2, 2, 30, 2, out _, out _))
        {
            GD.PrintErr("[INV-TAIL] прямой путь по чистому острову не найден — тест невалиден");
            return false;
        }

        // Стена №1: инвалидация принимается (окно пустое после Reset).
        solid[20, 10] = true;
        pf.Invalidate();

        // Между стенами агент успевает спросить путь — оверлей перестроен,
        // _dirty сброшен. Именно в этом окне и теряется вторая инвалидация.
        if (!pf.TryFindPath(2, 2, 30, 2, out _, out _))
            GD.Print("[INV-TAIL] (путь после первой стены не найден — не важно для теста)");

        int idx1 = 10 * pf.MapWidth + 20;
        if (pf.Cost == null || idx1 >= pf.Cost.Length || pf.Cost[idx1] != 0)
        {
            GD.PrintErr("[INV-TAIL] FAIL: первая стена не попала в оверлей");
            return false;
        }

        // Стена №2: инвалидация попадает внутрь 200-мс окна и должна быть отложена.
        solid[20, 11] = true;
        pf.Invalidate();

        // Ждём конец окна: отложенная инвалидация обязана досброситься на следующем запросе.
        Thread.Sleep(260);
        bool found = pf.TryFindPath(2, 2, 30, 2, out _, out _);
        GD.Print($"[INV-TAIL] путь после окна: found={found}");

        int idx = 11 * pf.MapWidth + 20;
        byte cost = pf.Cost != null && idx < pf.Cost.Length ? pf.Cost[idx] : (byte)255;
        GD.Print($"[INV-TAIL] стоимость второй стены в оверлее: {cost} (ожидается 0)");

        if (cost != 0)
        {
            GD.PrintErr("[INV-TAIL] FAIL: вторая стена не попала в оверлей — хвост инвалидации потерян");
            return false;
        }

        GD.Print("[INV-TAIL] OK: обе стены подряд видны пасфайндеру");
        return true;
    }

    /// <summary>Контроль: одиночная инвалидация вне окна работает как раньше.</summary>
    private bool SingleInvalidationStillWorks()
    {
        var ground = new TileType[W, H];
        var solid = new bool[W, H];
        var trees = new bool[W, H];
        var walkable = new System.Collections.Generic.List<(int X, int Y)>();
        var spatial = new AgentSpatialGrid(W, H);
        var move = new AgentMovementService();
        var ctx = new SimulationContext(ground, null, trees, solid, walkable, spatial, move, new Random(8));

        HierarchicalPathfinder pf = HierarchicalPathfinder.Instance;
        pf.Reset();
        pf.Initialize(ctx);

        if (!pf.TryFindPath(2, 2, 30, 2, out _, out _))
            return false;

        Thread.Sleep(220);                    // окно дебаунса пустое
        solid[30, 3] = true;
        pf.Invalidate();

        pf.TryFindPath(2, 2, 30, 2, out _, out _);
        int idx = 3 * pf.MapWidth + 30;
        byte cost = pf.Cost != null && idx < pf.Cost.Length ? pf.Cost[idx] : (byte)255;
        if (cost != 0)
        {
            GD.PrintErr("[INV-TAIL] FAIL: одиночная стена не видна (регресс обычного пути)");
            return false;
        }

        GD.Print("[INV-TAIL] OK: одиночная инвалидация работает");
        return true;
    }
}