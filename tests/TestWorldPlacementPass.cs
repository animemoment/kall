using System;
using Godot;
using Game.Core;
using Game.UI.Tools;

namespace Game.Tests;

/// <summary>
/// 21-02 / WorldPlacementPass.Run: граница острова резолвится ОДИН раз на прогон,
/// а не делегатом на каждую клетку.
///
/// Почему это важно: Run просматривает до 250 000 клеток за одно событие драга
/// (MaxScannedCells). Пока `islandCell ?? placement.IsIslandCell` стоял внутри
/// двойного цикла, method group → делегат создавался на КАЖДУЮ клетку: до
/// 250 000 аллокаций за событие, GC-пики и фризы. Резолв вынесен до циклов;
/// сам фолбэк на адаптер сохранён — размер острова знает только адаптер, с
/// которым его создали (иначе ломается destroyer_streaming).
///
/// Тест красный на старом коде: NoPerCellAllocation видит ~8 МБ аллокаций
/// на прогон 500×500.
///
/// Запуск: godot --headless --path . --script res://tests/run_world_placement_pass.gd
/// </summary>
public partial class TestWorldPlacementPass : RefCounted
{
    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print("[PLACEMENT-PASS] Граница острова — один резолв на прогон");
        GD.Print("==================================================");

        bool ok = true;
        ok &= BoundaryDeclaredBeforeLoops();
        ok &= EmptyArgumentsAreNoOp();
        ok &= AdapterBoundaryIsFallback();
        ok &= ExplicitBoundaryWins();
        ok &= AppliesOnlyAcceptedWorldCells();
        ok &= NegativeAndSeamCoordinatesWork();
        ok &= LargeRectIsTruncatedCentered();
        ok &= NoPerCellAllocation();

        GD.Print("==================================================");
        GD.Print(ok ? "[PLACEMENT-PASS] PASS" : "[PLACEMENT-PASS] FAIL");
        GD.Print("==================================================");
        return ok;
    }

    /// <summary>
    /// Статическая страховка (как в TestWorldMapClick): объявление делегата должно
    /// стоять ДО циклов. Ловит откат правки детерминированно, даже если конкретный
    /// JIT сумеет закэшировать делегат и замер аллокаций не покраснеет.
    /// </summary>
    private bool BoundaryDeclaredBeforeLoops()
    {
        const string path = "res://src/ui/tools/WorldPlacementPass.cs";
        if (!Godot.FileAccess.FileExists(path))
        {
            GD.PrintErr($"[PLACEMENT-PASS] FAIL: нет {path}");
            return false;
        }

        string src = Godot.FileAccess.GetFileAsString(path);
        int decl = src.IndexOf("islandBoundary", StringComparison.Ordinal);
        int loop = src.IndexOf("for (long y = minY", StringComparison.Ordinal);
        if (decl < 0 || loop < 0 || decl > loop)
        {
            GD.PrintErr("[PLACEMENT-PASS] FAIL: islandBoundary резолвится внутри/после цикла — " +
                        "делегат на каждую клетку (decl=" + decl + ", loop=" + loop + ")");
            return false;
        }

        GD.Print("[PLACEMENT-PASS] OK: islandBoundary объявлен до циклов");
        return true;
    }

    private bool EmptyArgumentsAreNoOp()
    {
        var placement = new FakePlacement();
        Func<long, long, bool> apply = AcceptAll;

        if (WorldPlacementPass.Run(0, 0, 3, 3, null, apply) != 0)
        {
            GD.PrintErr("[PLACEMENT-PASS] FAIL: Run без адаптера должен вернуть 0");
            return false;
        }
        if (WorldPlacementPass.Run(0, 0, 3, 3, placement, null) != 0)
        {
            GD.PrintErr("[PLACEMENT-PASS] FAIL: Run без apply должен вернуть 0");
            return false;
        }
        if (placement.IslandQueries != 0 || placement.WorldQueries != 0)
        {
            GD.PrintErr("[PLACEMENT-PASS] FAIL: при null-аргументах адаптер не должен опрашиваться");
            return false;
        }

        GD.Print("[PLACEMENT-PASS] OK: null-аргументы — no-op");
        return true;
    }

    /// <summary>
    /// Фолбэк: когда предикат не передан, «остров» отвечает адаптер (его размеры —
    /// единственный источник истины), а не формула проекта.
    /// </summary>
    private bool AdapterBoundaryIsFallback()
    {
        var placement = new FakePlacement { IslandWidth = 8, IslandHeight = 8 };
        Func<long, long, bool> apply = AcceptAll;

        int applied = WorldPlacementPass.Run(0, 0, 15, 15, placement, apply);
        const int expected = 16 * 16 - 8 * 8;
        if (applied != expected)
        {
            GD.PrintErr($"[PLACEMENT-PASS] FAIL: применение={applied}, ожидалось {expected} " +
                        "(островные клетки адаптера должны пропускаться)");
            return false;
        }
        if (placement.IslandQueries != 16 * 16)
        {
            GD.PrintErr($"[PLACEMENT-PASS] FAIL: адаптер опрошен {placement.IslandQueries} раз, " +
                        "ожидалось по разу на клетку");
            return false;
        }

        GD.Print("[PLACEMENT-PASS] OK: без предиката границу отвечает адаптер");
        return true;
    }

    /// <summary>
    /// Явный предикат (у инструмента) имеет приоритет и замыкает `??`:
    /// адаптеру вопрос не задаётся ни разу.
    /// </summary>
    private bool ExplicitBoundaryWins()
    {
        var placement = new FakePlacement { IslandWidth = 64, IslandHeight = 64 };
        Func<long, long, bool> apply = AcceptAll;
        Func<long, long, bool> island = (x, y) => x < 4;

        int applied = WorldPlacementPass.Run(0, 0, 7, 7, placement, apply, island);
        if (applied != 4 * 8)
        {
            GD.PrintErr($"[PLACEMENT-PASS] FAIL: с явным предикатом применение={applied}, ожидалось 32");
            return false;
        }
        if (placement.IslandQueries != 0)
        {
            GD.PrintErr("[PLACEMENT-PASS] FAIL: при явном предикате адаптер опрашиваться не должен");
            return false;
        }

        GD.Print("[PLACEMENT-PASS] OK: явный предикат приоритетнее адаптера");
        return true;
    }

    /// <summary>IsWorldCell=false не применяется; apply=false не считается успехом.</summary>
    private bool AppliesOnlyAcceptedWorldCells()
    {
        var placement = new FakePlacement { IslandWidth = 2, IslandHeight = 2 };
        Func<long, long, bool> apply = OnlyEvenX;

        int applied = WorldPlacementPass.Run(0, 0, 3, 3, placement, apply);
        if (applied != 6)
        {
            GD.PrintErr($"[PLACEMENT-PASS] FAIL: применение={applied}, ожидалось 6 " +
                        "(12 мировых клеток, apply только на чётном x)");
            return false;
        }
        if (placement.WorldQueries != 12)
        {
            GD.PrintErr($"[PLACEMENT-PASS] FAIL: IsWorldCell опрошен {placement.WorldQueries} раз, " +
                        "ожидалось 12 (только неостровные)");
            return false;
        }

        placement.WorldOpen = false;
        int before = placement.WorldQueries;
        applied = WorldPlacementPass.Run(0, 0, 3, 3, placement, apply);
        if (applied != 0)
        {
            GD.PrintErr($"[PLACEMENT-PASS] FAIL: закрытый мир применил {applied} клеток");
            return false;
        }
        if (placement.WorldQueries - before != 12)
        {
            GD.PrintErr("[PLACEMENT-PASS] FAIL: IsWorldCell не спрашивают у каждой неостровной клетки");
            return false;
        }

        GD.Print("[PLACEMENT-PASS] OK: применяются только принятые мировые клетки");
        return true;
    }

    /// <summary>Отрицательные координаты и обе стороны кромки адаптера.</summary>
    private bool NegativeAndSeamCoordinatesWork()
    {
        var placement = new FakePlacement { IslandWidth = 16, IslandHeight = 16 };
        Func<long, long, bool> apply = AcceptAll;

        if (WorldPlacementPass.Run(-16, -16, -1, -1, placement, apply) != 16 * 16)
        {
            GD.PrintErr("[PLACEMENT-PASS] FAIL: отрицательный прямоугольник обработан неверно");
            return false;
        }

        // Рамка 12..20: 81 клетка, остров адаптера 12..15 по обеим осям даёт 4×4=16.
        int seam = WorldPlacementPass.Run(12, 12, 20, 20, placement, apply);
        if (seam != 81 - 16)
        {
            GD.PrintErr($"[PLACEMENT-PASS] FAIL: на кромке применение={seam}, ожидалось {81 - 16}");
            return false;
        }

        GD.Print("[PLACEMENT-PASS] OK: отрицательные координаты и кромка обработаны");
        return true;
    }

    /// <summary>Гигантская рамка обрезается по центру, флаг обрезки выставлен.</summary>
    private bool LargeRectIsTruncatedCentered()
    {
        var placement = new FakePlacement();
        bool corner = false;
        bool center = false;
        Func<long, long, bool> apply = (x, y) =>
        {
            if (x == 0 && y == 0) corner = true;
            if (x == 300 && y == 300) center = true;
            return true;
        };

        int applied = WorldPlacementPass.Run(0, 0, 599, 599, placement, apply);
        if (!WorldPlacementPass.LastTruncated)
        {
            GD.PrintErr("[PLACEMENT-PASS] FAIL: обрезка 600×600 не выставила LastTruncated");
            return false;
        }
        if (applied <= 0 || applied > WorldPlacementPass.MaxAppliedCells)
        {
            GD.PrintErr($"[PLACEMENT-PASS] FAIL: после обрезки применение={applied}");
            return false;
        }
        if (!center || corner)
        {
            GD.PrintErr($"[PLACEMENT-PASS] FAIL: обрезка не центрированная (center={center}, corner={corner})");
            return false;
        }

        GD.Print($"[PLACEMENT-PASS] OK: 600×600 обрезано по центру (применено {applied})");
        return true;
    }

    /// <summary>
    /// Регресс на корень бага: прогон 500×500 (250 000 клеток — предел драга) не должен
    /// аллоцировать делегат на клетку. Старый код: ~8 МБ, новый: единицы байт.
    /// </summary>
    private bool NoPerCellAllocation()
    {
        var placement = new FakePlacement();
        Func<long, long, bool> apply = AcceptAll;

        // Прогрев: JIT/статические делегаты не должны попасть в замер.
        WorldPlacementPass.Run(0, 0, 9, 9, placement, apply);

        // Первый вызов счётчика может инициализировать thread-local — прогреваем его
        // вне замера, чтобы в бюджет не попала разовая инициализация.
        System.GC.GetAllocatedBytesForCurrentThread();
        long before = System.GC.GetAllocatedBytesForCurrentThread();
        int applied = WorldPlacementPass.Run(0, 0, 499, 499, placement, apply);
        long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;

        GD.Print($"[PLACEMENT-PASS] 250 000 клеток: применено {applied}, " +
                 $"управляемых аллокаций {allocated} Б");

        if (applied != WorldPlacementPass.MaxAppliedCells)
        {
            GD.PrintErr($"[PLACEMENT-PASS] FAIL: применение={applied}, ожидалось {WorldPlacementPass.MaxAppliedCells}");
            return false;
        }

        const long budget = 256 * 1024;
        if (allocated > budget)
        {
            GD.PrintErr($"[PLACEMENT-PASS] FAIL: {allocated} Б на прогон — делегат границы " +
                        "аллоцируется на каждую клетку (бюджет 256 КиБ)");
            return false;
        }

        GD.Print("[PLACEMENT-PASS] OK: граница резолвится один раз на прогон");
        return true;
    }

    private static bool AcceptAll(long x, long y) => true;

    private static bool OnlyEvenX(long x, long y) => (x & 1) == 0;

    /// <summary>
    /// Адаптер-фейк: остров задаёт он сам (как StreamingToolPlacement знает свои
    /// _islandWidth/_islandHeight), поэтому тест не заводит формулу границы проекта.
    /// </summary>
    private sealed class FakePlacement : IToolWorldPlacement
    {
        public long IslandWidth;
        public long IslandHeight;
        public bool WorldOpen = true;

        public int IslandQueries;
        public int WorldQueries;

        public bool IsIslandCell(long cellX, long cellY)
        {
            IslandQueries++;
            return cellX >= 0 && cellY >= 0 && cellX < IslandWidth && cellY < IslandHeight;
        }

        public bool IsWorldCell(long cellX, long cellY)
        {
            WorldQueries++;
            return WorldOpen;
        }

        public bool CanPlaceStockpile(long cellX, long cellY) => false;
        public bool CanPlaceFarm(long cellX, long cellY) => false;
        public bool TryPlaceWall(long cellX, long cellY) => false;
        public bool CanPlaceWall(long cellX, long cellY) => false;
        public bool TryRemoveWall(long cellX, long cellY) => false;
        public bool TryPlaceStockpile(long cellX, long cellY) => false;
        public bool TryRemoveStockpile(long cellX, long cellY) => false;
        public bool TryPlaceFarmPlot(long cellX, long cellY) => false;
        public bool TryRemoveFarmPlot(long cellX, long cellY) => false;
        public bool TryPlaceBuilding(long cellX, long cellY) => false;
        public bool CanPlaceBuilding(long cellX, long cellY) => false;
        public bool TryRemoveBuilding(long cellX, long cellY) => false;
        public bool TryCutTree(long cellX, long cellY) => false;
        public bool TryCancelCutTree(long cellX, long cellY) => false;
        public bool TryMineStone(long cellX, long cellY) => false;
        public bool TryCancelMineStone(long cellX, long cellY) => false;
    }
}