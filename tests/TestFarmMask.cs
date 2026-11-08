using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Godot;
using Game.Core;
using Game.Simulation;

namespace Game.Tests;

// ============================================================================
// 17-01 + 17-05 (review_2026-10-31, карточка 17_farming_offline).
//
// 17-01: маска леса/грядок для влаги и плодородия строилась ОДИН раз по флагу
//        «_environmentalMasksDirty», который никто не поднимал: вскопанные позже
//        грядки не тянули влагу, срубленный лес продолжал удобрять. Теперь маска
//        пересобирается из мира перед каждым тиком влаги/плодородия.
//        Проверяем поведением: грядка появилась/снялась, дерево срублено.
//
// 17-05: FarmZoneManager.Adapt копировал весь HashSet тайлов зоны на КАЖДЫЙ
//        вызов (аллокация в сим-потоке на каждую вскопанную клетку и сбор).
//        Теперь снимок кэшируется по id/набору тайлов, копия — одна на изменение.
//        Проверяем по исходнику (в теле Adapt нет new HashSet/new List/ToList)
//        и по счётчикам вызовов/аллокаций.
//
// Запуск: godot --headless --path . --script res://tests/run_farm_mask.gd
// ============================================================================

public partial class TestFarmMask : RefCounted
{
    private const string SimPath = "res://src/simulation/AgentSimulationThread.cs";
    private const string FacadePath = "res://src/simulation/farming/FarmZoneManagerFacade.cs";

    private static int _failures;

    private static void Check(bool cond, string name)
    {
        if (cond)
            GD.Print($"[FARM-MASK] OK: {name}");
        else
        {
            _failures++;
            GD.PrintErr($"[FARM-MASK] FAIL: {name}");
        }
    }

    public int Run()
    {
        GD.Print("==================================================");
        GD.Print("[FARM-MASK] 17-01 маска грядок/леса + 17-05 Adapt без копии");
        GD.Print("==================================================");

        try
        {
            SourceWiring();
            MaskFollowsGardenBeds();
            MaskFollowsTreeChopping();
            AdaptDoesNotCopyTiles();
        }
        catch (Exception ex)
        {
            _failures++;
            GD.PrintErr($"[FARM-MASK] FAIL: исключение {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            FarmJobManager.Instance.ClearAll();
            CropGrowthManager.Instance.ClearAll();
            JobDispatcher.Instance.JobIndex.Clear();
            ZoneManager.Instance.ClearAll();
        }

        GD.Print($"[FARM-MASK] {(_failures == 0 ? "ALL PASSED" : "FAILURES: " + _failures)}");
        return _failures == 0 ? 0 : 1;
    }

    // ---------------------------------------------------------------- 17-01: исходник

    /// <summary>
    /// Корень 17-01 — флаг «маски грязные», который продюсеры не поднимали.
    /// Проверяем по тексту, что флага больше нет и что обе карты (влага и
    /// плодородие) пересобирают маску ДО своего тика.
    /// </summary>
    private static void SourceWiring()
    {
        string sim = Godot.FileAccess.GetFileAsString(SimPath);
        if (string.IsNullOrEmpty(sim))
        {
            Check(false, $"не прочитать {SimPath}");
            return;
        }

        Check(!sim.Contains("_environmentalMasksDirty"),
            "17-01: флага «маски грязные» больше нет (маска не может застыть)");
        Check(sim.Contains("RebuildEnvironmentalMasks"),
            "17-01: маска пересобирается выделенным проходом");
        Check(Regex.IsMatch(sim, @"RebuildEnvironmentalMasks\([\s\S]{0,900}?humidityMap\.Tick\("),
            "17-01: маска собрана ДО тика влаги");
        Check(Regex.IsMatch(sim, @"RebuildEnvironmentalMasks\([\s\S]{0,200}?fertilityMap\.Tick\("),
            "17-01: маска собрана ДО тика плодородия");
    }

    // ---------------------------------------------------------------- 17-01: грядки

    private static void MaskFollowsGardenBeds()
    {
        const int W = 8, H = 8;
        const int bx = 3, by = 4;
        var ground = MakeGrass(W, H);
        var trees = new bool[W, H];
        var forest = new bool[W * H];
        var farm = new bool[W * H];
        var emptyFarm = new bool[W * H];

        AgentSimulationThread.RebuildEnvironmentalMasks(forest, farm, W, H, trees);
        Check(!farm[by * W + bx], "17-01: до вскопки клетка не считается грядкой");

        FarmJobManager.Instance.CompletePlot(bx, by);
        AgentSimulationThread.RebuildEnvironmentalMasks(forest, farm, W, H, trees);
        Check(farm[by * W + bx],
            "17-01: вскопанная ПОСЛЕ первого тика грядка попала в маску влаги/плодородия");

        // Влага: грядка тянет воду. Два одинаковых тика, разница только в маске.
        var withBed = new HumidityMap(W, H);
        var noBed = new HumidityMap(W, H);
        Fill(withBed, 100);
        Fill(noBed, 100);
        withBed.Tick(ground, null, forest, farm);
        noBed.Tick(ground, null, forest, emptyFarm);
        Check(withBed.Get(bx, by) < noBed.Get(bx, by),
            $"17-01: тик влаги учитывает грядку ({withBed.Get(bx, by)} < {noBed.Get(bx, by)})");

        // Сняли грядку — влияние исчезло (тикаем заново, карты одинаковые).
        FarmJobManager.Instance.UnmarkPlot(bx, by);
        AgentSimulationThread.RebuildEnvironmentalMasks(forest, farm, W, H, trees);
        Check(!farm[by * W + bx], "17-01: снятая грядка ушла из маски");

        var withNoBed = new HumidityMap(W, H);
        var noBed2 = new HumidityMap(W, H);
        Fill(withNoBed, 100);
        Fill(noBed2, 100);
        withNoBed.Tick(ground, null, forest, farm);
        noBed2.Tick(ground, null, forest, emptyFarm);
        Check(DiffCells(withNoBed, noBed2, W, H) == 0,
            "17-01: после снятия грядки тик влаги совпадает с «грядок нет»");
    }

    // ---------------------------------------------------------------- 17-01: лес

    private static void MaskFollowsTreeChopping()
    {
        const int W = 16, H = 16;
        var ground = MakeGrass(W, H);
        var trees = new bool[W, H];
        // (5,7) выбрано так, чтобы органика леса сработала на тике плодородия
        // (хеш-гейт клетки), иначе «лес удобряет» нечем измерить.
        trees[5, 7] = true;
        trees[9, 2] = true;
        var forest = new bool[W * H];
        var farm = new bool[W * H];

        AgentSimulationThread.RebuildEnvironmentalMasks(forest, farm, W, H, trees);
        Check(forest[7 * W + 5] && forest[2 * W + 9], "17-01: оба дерева в маске леса");
        Check(CountTrue(forest) == 2, $"17-01: в маске ровно 2 дерева (факт {CountTrue(forest)})");

        // Плодородие: лес удобряет — сравниваем с пустой маской.
        var fertForest = new FertilityMap(W, H);
        var fertNone = new FertilityMap(W, H);
        Fill(fertForest, 100);
        Fill(fertNone, 100);
        fertForest.Tick(ground, forest, null);
        fertNone.Tick(ground, null, null);
        Check(DiffCells(fertForest, fertNone, W, H) > 0,
            "17-01: плодородие видит лес (иначе лес бы не удобрял)");

        // Рубка: TreeChoppingJobHandler пишет прямо в ctx.TreeOnGrass.
        trees[5, 7] = false;
        AgentSimulationThread.RebuildEnvironmentalMasks(forest, farm, W, H, trees);
        Check(!forest[7 * W + 5], "17-01: срубленное дерево ушло из маски");
        Check(forest[2 * W + 9], "17-01: второе дерево осталось в маске");

        trees[9, 2] = false;
        AgentSimulationThread.RebuildEnvironmentalMasks(forest, farm, W, H, trees);
        Check(CountTrue(forest) == 0, "17-01: после вырубки маска леса пуста");

        var fertAfter = new FertilityMap(W, H);
        var fertEmpty = new FertilityMap(W, H);
        Fill(fertAfter, 100);
        Fill(fertEmpty, 100);
        fertAfter.Tick(ground, forest, null);
        fertEmpty.Tick(ground, null, null);
        Check(DiffCells(fertAfter, fertEmpty, W, H) == 0,
            "17-01: срубленный лес больше НЕ удобряет (тик совпал с пустой маской)");
    }

    // ---------------------------------------------------------------- 17-05

    private static void AdaptDoesNotCopyTiles()
    {
        string src = Godot.FileAccess.GetFileAsString(FacadePath);
        if (string.IsNullOrEmpty(src))
        {
            Check(false, $"не прочитать {FacadePath}");
            return;
        }

        string body = ExtractMethod(src, "FarmZone Adapt(Zone z)");
        Check(body.Length > 0, "17-05: тело Adapt найдено в исходнике");
        Check(!body.Contains("new HashSet"), "17-05: в теле Adapt нет new HashSet (тайлы не копируются)");
        Check(!body.Contains("new List"), "17-05: в теле Adapt нет new List");
        Check(!body.Contains(".ToList()"), "17-05: в теле Adapt нет .ToList()");
        Check(body.Contains("_adaptedZones"), "17-05: Adapt отдаёт кэшированный снимок зоны");

        ZoneManager.Instance.ClearAll();
        var tiles = new List<(int X, int Y)> { (10, 10), (11, 10), (12, 10) };
        List<Zone> created = ZoneManager.Instance.CreateFarmZones(tiles);
        Check(created.Count == 1, $"17-05: тестовая Farm-зона создана (факт {created.Count})");
        if (created.Count != 1)
            return;
        int zoneId = created[0].Id;

        Check(FarmZoneManager.Instance.TryGetZoneAt(11, 10, out FarmZone first) && first != null,
            "17-05: зона находится по клетке");
        if (first == null)
            return;

        int copiesBefore = FarmZoneManager.TileSetCopies;
        int callsBefore = FarmZoneManager.AdaptCalls;
        long allocBefore = GC.GetAllocatedBytesForCurrentThread();

        const int Repeats = 2000;
        int seen = 0;
        for (int i = 0; i < Repeats; i++)
        {
            if (!FarmZoneManager.Instance.TryGetZoneAt(11, 10, out FarmZone again) || again == null)
                break;
            if (again.Id != zoneId || again.TotalTiles != 3)
                break;
            seen++;
        }
        long alloc = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
        int copies = FarmZoneManager.TileSetCopies - copiesBefore;
        int calls = FarmZoneManager.AdaptCalls - callsBefore;

        Check(seen == Repeats, $"17-05: {Repeats} повторных TryGetZoneAt отдали зону (факт {seen})");
        Check(calls >= Repeats, $"17-05: счётчик вызовов Adapt растёт (факт {calls})");
        Check(copies == 0, $"17-05: повторные Adapt не копируют тайлы (копий {copies})");
        // Копия тайлов стоит сотни байт на вызов: 2000 копий = сотни КБ.
        // Порог с запасом на разовые служебные аллокации рантайма.
        Check(alloc < 1024, $"17-05: повторные Adapt не аллоцируют (байт {alloc} на {Repeats} вызовов)");

        // Смена тайлов зоны: публикуется НОВЫЙ снимок, старый не мутируется.
        int copiesBeforeChange = FarmZoneManager.TileSetCopies;
        ZoneManager.Instance.RemoveTiles(new List<(int X, int Y)> { (12, 10) });
        Check(FarmZoneManager.Instance.TryGetZoneById(zoneId, out FarmZone after) && after != null,
            "17-05: зона находится по id после удаления тайла");
        if (after == null)
            return;
        Check(!ReferenceEquals(first, after),
            "17-05: смена тайлов публикует новый снимок (старый не мутируется)");
        Check(after.TotalTiles == 2 && first.TotalTiles == 3,
            $"17-05: старый снимок держит 3 тайла ({first.TotalTiles}), новый — 2 ({after.TotalTiles})");
        Check(FarmZoneManager.TileSetCopies - copiesBeforeChange == 1,
            $"17-05: на смену тайлов ровно одна копия (факт {FarmZoneManager.TileSetCopies - copiesBeforeChange})");
    }

    // ---------------------------------------------------------------- помощники

    private static TileType[,] MakeGrass(int w, int h)
    {
        var ground = new TileType[w, h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                ground[x, y] = TileType.Grass;
        return ground;
    }

    private static void Fill(HumidityMap map, int value)
    {
        for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
                map.Set(x, y, value);
    }

    private static void Fill(FertilityMap map, int value)
    {
        for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
                map.Set(x, y, value);
    }

    private static int CountTrue(bool[] mask)
    {
        int n = 0;
        for (int i = 0; i < mask.Length; i++)
            if (mask[i]) n++;
        return n;
    }

    private static int DiffCells(HumidityMap a, HumidityMap b, int w, int h)
    {
        int diff = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (a.Get(x, y) != b.Get(x, y)) diff++;
        return diff;
    }

    private static int DiffCells(FertilityMap a, FertilityMap b, int w, int h)
    {
        int diff = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (a.Get(x, y) != b.Get(x, y)) diff++;
        return diff;
    }

    /// <summary>Тело метода по сигнатуре: от первого '{' до парной закрывающей скобки.</summary>
    private static string ExtractMethod(string src, string signature)
    {
        int decl = src.IndexOf(signature, StringComparison.Ordinal);
        if (decl < 0) return string.Empty;
        int open = src.IndexOf('{', decl);
        if (open < 0) return string.Empty;

        int depth = 0;
        for (int i = open; i < src.Length; i++)
        {
            char c = src[i];
            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                    return src.Substring(open, i - open + 1);
            }
        }
        return string.Empty;
    }
}