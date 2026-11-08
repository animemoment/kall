using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Godot;
using Game.UI.Tools;

namespace Game.Tests;

/// <summary>
/// 21-02 / 21-05 / 21-06 для BuildTool.
///
/// 21-02. Призрак стены перерисовывался ЦЕЛИКОМ на каждое событие MouseMotion: ClearGhost()
/// по всем прошлым клеткам + SetCellsTerrainConnect по всей рамке (до 250 000 клеток,
/// автотайлинг пересчитывает соседей). Теперь считается разница «прошлая рамка → новая»
/// полосами и применяется только она: сдвиг на одну клетку стоит периметр, а не площадь.
/// Проверяем чистую функцию ComputeRectDifference (без Godot-рендера) и то, что OnDrag
/// действительно зовёт её, а не старую полную перерисовку.
///
/// 21-05. Отсечка cellCount &gt; MaxScannedCells молча выходила из OnRelease: игрок тащил
/// большую рамку, отпускал — и не появлялось ничего, без причины. Теперь отсечка стоит
/// ДО разности рамок/гейтов/отрисовки и объясняет себя сообщением.
///
/// 21-06. В файле было два гейта «остров/мир» (адаптер + своя формула границы).
/// Остался один: WorldPlacementPass.IsOnIsland → WorldModel.IsInsideIsland (R-008).
///
/// Запуск: godot --headless --path . --script res://tests/run_build_tool_ghost.gd
/// </summary>
public partial class TestBuildToolGhost : RefCounted
{
    private const string ToolPath = "res://src/ui/tools/BuildTool.cs";
    private const string Tag = "[BUILD-GHOST]";

    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print($"{Tag} 21-02/21-05/21-06: призрак стены, лимит, один гейт");
        GD.Print("==================================================");

        bool ok = true;
        ok &= ShiftByOnePaintsPerimeterOnly();
        ok &= DifferenceMatchesBruteForce();
        ok &= LimitIsCutBeforeHeavyWork();
        ok &= LimitCutExplainsItself();
        ok &= SingleIslandGate();
        ok &= DragAppliesOnlyDifferenceInSource();

        GD.Print("==================================================");
        GD.Print(ok ? $"{Tag} PASS" : $"{Tag} FAIL");
        GD.Print("==================================================");
        return ok;
    }

    /// <summary>
    /// (1) Инкрементальность: при сдвиге рамки на одну клетку добавленных/убранных клеток
    /// ровно по стороне рамки, а не по её площади. Красный на старом коде: старого метода
    /// не было вовсе, а OnDrag перерисовывал всю рамку.
    /// </summary>
    private bool ShiftByOnePaintsPerimeterOnly()
    {
        var added = new List<BuildTool.GhostBand>();
        var removed = new List<BuildTool.GhostBand>();

        // Рамка 100×100 сдвинулась на клетку вправо: ушла колонка (100 клеток), пришла колонка.
        BuildTool.ComputeRectDifference(0, 0, 99, 99, 1, 0, 100, 99, added, removed);
        long addCells = BuildTool.SumBandCells(added);
        long removeCells = BuildTool.SumBandCells(removed);
        if (addCells != 100 || removeCells != 100)
        {
            GD.PrintErr($"{Tag} FAIL: сдвиг 100×100 на клетку дал add={addCells}, remove={removeCells} " +
                        "(ожидалось 100/100, площадь 10 000)");
            return false;
        }

        // Полос не больше четырёх — разность прямоугольников, а не поштучный обход.
        if (added.Count > 4 || removed.Count > 4)
        {
            GD.PrintErr($"{Tag} FAIL: разность рамок разложилась в {added.Count}/{removed.Count} полос " +
                        "(ожидалось не больше 4)");
            return false;
        }

        // 250×250 (62 500 клеток) сдвиг на клетку вниз — та же стоимость по периметру.
        BuildTool.ComputeRectDifference(0, 0, 249, 249, 0, 1, 249, 250, added, removed);
        addCells = BuildTool.SumBandCells(added);
        removeCells = BuildTool.SumBandCells(removed);
        if (addCells != 250 || removeCells != 250)
        {
            GD.PrintErr($"{Tag} FAIL: сдвиг 250×250 на клетку дал add={addCells}, remove={removeCells} " +
                        "(ожидалось 250/250, площадь 62 500)");
            return false;
        }

        // Та же рамка без изменений — работы ноль (иначе драг «на месте» всё равно перерисовывал бы).
        BuildTool.ComputeRectDifference(7, 7, 106, 106, 7, 7, 106, 106, added, removed);
        if (BuildTool.SumBandCells(added) != 0 || BuildTool.SumBandCells(removed) != 0)
        {
            GD.PrintErr($"{Tag} FAIL: одинаковая рамка дала непустую разницу");
            return false;
        }

        GD.Print($"{Tag} OK: сдвиг на клетку — {250} клеток периметра вместо 62 500 площади");
        return true;
    }

    /// <summary>
    /// Полосы разности обязаны ТОЧНО покрывать разницу множеств и не пересекаться.
    /// Сверяем с честным перебором объединения рамок, включая отрицательные клетки
    /// и кромку 1024 (проект живёт в координатах, которые бывают &lt; 0 и ≥ 1024).
    /// </summary>
    private bool DifferenceMatchesBruteForce()
    {
        // old min/max, new min/max
        if (!CheckCase("сдвиг вправо", 0, 0, 9, 9, 1, 0, 10, 9)) return false;
        if (!CheckCase("сдвиг влево-вверх", 0, 0, 9, 9, -1, -1, 8, 8)) return false;
        if (!CheckCase("рост во все стороны", 2, 2, 5, 5, 0, 0, 7, 7)) return false;
        if (!CheckCase("сжатие внутрь", 0, 0, 11, 7, 3, 2, 6, 4)) return false;
        if (!CheckCase("разъехались совсем", 0, 0, 4, 4, 20, 20, 24, 24)) return false;
        if (!CheckCase("та же рамка", -3, -3, 3, 3, -3, -3, 3, 3)) return false;
        if (!CheckCase("отрицательные клетки", -12, -9, -3, -1, -14, -9, -5, -1)) return false;
        if (!CheckCase("кромка 1024", 1018, 1018, 1025, 1025, 1026, 1018, 1033, 1025)) return false;

        GD.Print($"{Tag} OK: полосы разности совпали с перебором на 8 случаях (в т.ч. <0 и кромка)");
        return true;
    }

    private bool CheckCase(string name, int oldMinX, int oldMinY, int oldMaxX, int oldMaxY,
        int newMinX, int newMinY, int newMaxX, int newMaxY)
    {
        var added = new List<BuildTool.GhostBand>();
        var removed = new List<BuildTool.GhostBand>();
        BuildTool.ComputeRectDifference(oldMinX, oldMinY, oldMaxX, oldMaxY,
            newMinX, newMinY, newMaxX, newMaxY, added, removed);

        HashSet<(int X, int Y)> expectedAdded = ExpectedDifference(true,
            oldMinX, oldMinY, oldMaxX, oldMaxY, newMinX, newMinY, newMaxX, newMaxY);
        HashSet<(int X, int Y)> expectedRemoved = ExpectedDifference(false,
            oldMinX, oldMinY, oldMaxX, oldMaxY, newMinX, newMinY, newMaxX, newMaxY);

        HashSet<(int X, int Y)> actualAdded = Expand(added, out long addedSum);
        HashSet<(int X, int Y)> actualRemoved = Expand(removed, out long removedSum);

        // sum == Count означает, что полосы не пересекаются; SetEquals — что покрытие точное.
        if (addedSum != actualAdded.Count || !actualAdded.SetEquals(expectedAdded))
        {
            GD.PrintErr($"{Tag} FAIL: [{name}] added не совпал: полос {added.Count}, " +
                        $"клеток {actualAdded.Count} (сумма полос {addedSum}), ожидалось {expectedAdded.Count}");
            return false;
        }
        if (removedSum != actualRemoved.Count || !actualRemoved.SetEquals(expectedRemoved))
        {
            GD.PrintErr($"{Tag} FAIL: [{name}] removed не совпал: полос {removed.Count}, " +
                        $"клеток {actualRemoved.Count} (сумма полос {removedSum}), ожидалось {expectedRemoved.Count}");
            return false;
        }
        return true;
    }

    /// <summary>Честный перебор разницы двух рамок (тест маленький, скорость не важна).</summary>
    private static HashSet<(int X, int Y)> ExpectedDifference(bool newMinusOld,
        int oldMinX, int oldMinY, int oldMaxX, int oldMaxY,
        int newMinX, int newMinY, int newMaxX, int newMaxY)
    {
        var set = new HashSet<(int X, int Y)>();
        int minX = Math.Min(oldMinX, newMinX);
        int maxX = Math.Max(oldMaxX, newMaxX);
        int minY = Math.Min(oldMinY, newMinY);
        int maxY = Math.Max(oldMaxY, newMaxY);

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                bool inOld = x >= oldMinX && x <= oldMaxX && y >= oldMinY && y <= oldMaxY;
                bool inNew = x >= newMinX && x <= newMaxX && y >= newMinY && y <= newMaxY;
                if (newMinusOld ? (inNew && !inOld) : (inOld && !inNew))
                    set.Add((x, y));
            }
        }
        return set;
    }

    private static HashSet<(int X, int Y)> Expand(List<BuildTool.GhostBand> bands, out long sum)
    {
        var set = new HashSet<(int X, int Y)>();
        sum = 0;
        for (int i = 0; i < bands.Count; i++)
        {
            BuildTool.GhostBand band = bands[i];
            sum += band.CellCount;
            for (int x = band.MinX; x <= band.MaxX; x++)
            {
                for (int y = band.MinY; y <= band.MaxY; y++)
                    set.Add((x, y));
            }
        }
        return set;
    }

    /// <summary>
    /// (2) Лимит площади: считается без переполнения int (50k×50k = 2.5 млрд не влезает),
    /// и в OnDrag отсечка стоит ДО разности рамок/отрисовки — тяжёлой работы на такой
    /// рамке не выполняется вовсе.
    /// </summary>
    private bool LimitIsCutBeforeHeavyWork()
    {
        if (BuildTool.RectCellCount(0, 0, 499, 499) != 250_000)
        {
            GD.PrintErr($"{Tag} FAIL: 500×500 должно быть 250 000 клеток");
            return false;
        }
        if (!BuildTool.IsWithinDragLimit(250_000))
        {
            GD.PrintErr($"{Tag} FAIL: ровно лимит должен проходить");
            return false;
        }
        if (BuildTool.RectCellCount(0, 0, 500, 499) != 250_500 || BuildTool.IsWithinDragLimit(250_500))
        {
            GD.PrintErr($"{Tag} FAIL: 501×500 должно быть 250 500 и отсекаться лимитом");
            return false;
        }

        // int-переполнение: (60001)² = 3 600 120 001 > int.MaxValue.
        long huge = BuildTool.RectCellCount(0, 0, 60_000, 60_000);
        if (huge != 60_001L * 60_001L)
        {
            GD.PrintErr($"{Tag} FAIL: RectCellCount переполнился: {huge} вместо {60_001L * 60_001L}");
            return false;
        }
        if (BuildTool.IsWithinDragLimit(huge))
        {
            GD.PrintErr($"{Tag} FAIL: 60 001² клеток не отсеклись");
            return false;
        }

        // Пустая/вывернутая рамка — ноль, а не отрицательное число.
        if (BuildTool.RectCellCount(5, 5, 4, 10) != 0 || BuildTool.RectCellCount(0, 0, 0, 0) != 1)
        {
            GD.PrintErr($"{Tag} FAIL: пустая рамка считается неверно");
            return false;
        }

        string source = LoadSource(out bool found);
        if (!found) return false;
        string drag = Slice(source, "public void OnDrag(", "public void OnRelease(");
        if (drag.Length == 0)
        {
            GD.PrintErr($"{Tag} FAIL: не найден OnDrag в {ToolPath}");
            return false;
        }

        int limit = drag.IndexOf("IsWithinDragLimit", StringComparison.Ordinal);
        int diff = drag.IndexOf("ComputeRectDifference", StringComparison.Ordinal);
        int paint = drag.IndexOf("PaintGhostBands", StringComparison.Ordinal);
        int erase = drag.IndexOf("EraseGhostBands", StringComparison.Ordinal);
        if (limit < 0 || diff < 0 || paint < 0 || erase < 0)
        {
            GD.PrintErr($"{Tag} FAIL: в OnDrag нет ожидаемых вызовов " +
                        $"(limit={limit}, diff={diff}, paint={paint}, erase={erase})");
            return false;
        }
        if (limit > diff || limit > paint || limit > erase)
        {
            GD.PrintErr($"{Tag} FAIL: отсечка лимита стоит ПОСЛЕ тяжёлой работы " +
                        $"(limit={limit}, diff={diff}, paint={paint}, erase={erase})");
            return false;
        }

        // Полная очистка (ClearGhost) допустима только в ветке лимита.
        int clear = drag.IndexOf("ClearGhost", StringComparison.Ordinal);
        if (clear >= 0 && clear < limit)
        {
            GD.PrintErr($"{Tag} FAIL: ClearGhost стоит ДО отсечки — стирание всей рамки на каждое событие");
            return false;
        }

        GD.Print($"{Tag} OK: лимит (long, без переполнения) отсекается до разности и отрисовки");
        return true;
    }

    /// <summary>
    /// (3) Отсечка объясняет себя игроку: текст сообщения содержит и запрошенную площадь,
    /// и лимит, а оба выхода (OnDrag и OnRelease) зовут один и тот же путь сообщения.
    /// </summary>
    private bool LimitCutExplainsItself()
    {
        string message = BuildTool.DragLimitMessage(250_500);
        if (message.Length < 20)
        {
            GD.PrintErr($"{Tag} FAIL: сообщение об отсечке слишком короткое: \u0022{message}\u0022");
            return false;
        }
        if (!message.Contains("250500", StringComparison.Ordinal))
        {
            GD.PrintErr($"{Tag} FAIL: в сообщении нет запрошенного числа клеток: \u0022{message}\u0022");
            return false;
        }
        if (!message.Contains("250000", StringComparison.Ordinal))
        {
            GD.PrintErr($"{Tag} FAIL: в сообщении нет лимита: \u0022{message}\u0022");
            return false;
        }

        string source = LoadSource(out bool found);
        if (!found) return false;
        string drag = Slice(source, "public void OnDrag(", "public void OnRelease(");
        string release = Slice(source, "public void OnRelease(", "public void Cancel(");
        if (!drag.Contains("ReportDragLimit", StringComparison.Ordinal))
        {
            GD.PrintErr($"{Tag} FAIL: OnDrag по-прежнему молча выходит по лимиту");
            return false;
        }
        if (!release.Contains("ReportDragLimit", StringComparison.Ordinal))
        {
            GD.PrintErr($"{Tag} FAIL: OnRelease по-прежнему молча выходит по лимиту (21-05)");
            return false;
        }
        if (!source.Contains("ShowPlayerNotice", StringComparison.Ordinal))
        {
            GD.PrintErr($"{Tag} FAIL: сообщение игроку никуда не выводится");
            return false;
        }

        GD.Print($"{Tag} OK: отсечка сообщает причину игроку: \u0022{message}\u0022");
        return true;
    }

    /// <summary>
    /// (4) 21-06: в BuildTool остался ОДИН гейт «остров/мир» — адаптер инструмента или
    /// WorldModel.IsInsideIsland через WorldPlacementPass.IsOnIsland. Второй копии
    /// (IsIslandCell / MapRenderer.MapWidth) в файле быть не должно.
    /// </summary>
    private bool SingleIslandGate()
    {
        string source = LoadSource(out bool found);
        if (!found) return false;

        if (Regex.IsMatch(source, @"IsIslandCell\s*\("))
        {
            GD.PrintErr($"{Tag} FAIL: в BuildTool снова два гейта — вернулся второй IsIslandCell");
            return false;
        }
        if (Regex.IsMatch(source, @"MapRenderer\.MapWidth|MapRenderer\.MapHeight"))
        {
            GD.PrintErr($"{Tag} FAIL: в BuildTool своя формула границы острова (MapRenderer.MapWidth/Height)");
            return false;
        }

        int gates = Regex.Matches(source, @"WorldPlacementPass\.IsOnIsland\(").Count;
        if (gates != 1)
        {
            GD.PrintErr($"{Tag} FAIL: гейтов «остров/мир» {gates}, ожидался ровно один");
            return false;
        }

        if (!Regex.IsMatch(source,
                @"IsValidCoord\(int x, int y\)\s*=>\s*WorldPlacementPass\.IsOnIsland\(\s*x\s*,\s*y\s*,\s*WorldPlacement\s*\)"))
        {
            GD.PrintErr($"{Tag} FAIL: IsValidCoord больше не делегирует единому гейту IsOnIsland");
            return false;
        }

        GD.Print($"{Tag} OK: один гейт острова (WorldPlacementPass.IsOnIsland → WorldModel)");
        return true;
    }

    /// <summary>
    /// (5) Статическая страховка от отката: OnDrag применяет разницу полос и не содержит
    /// старой полной перерисовки; в горячем пути нет создания коллекций на событие.
    /// </summary>
    private bool DragAppliesOnlyDifferenceInSource()
    {
        string source = LoadSource(out bool found);
        if (!found) return false;

        string drag = Slice(source, "public void OnDrag(", "public void OnRelease(");
        if (drag.Length == 0)
        {
            GD.PrintErr($"{Tag} FAIL: не найден OnDrag в {ToolPath}");
            return false;
        }

        if (drag.Contains("PaintGhostPreview", StringComparison.Ordinal))
        {
            GD.PrintErr($"{Tag} FAIL: в OnDrag вернулась полная перерисовка PaintGhostPreview");
            return false;
        }
        if (!drag.Contains("EraseGhostBands", StringComparison.Ordinal)
            || !drag.Contains("PaintGhostBands", StringComparison.Ordinal))
        {
            GD.PrintErr($"{Tag} FAIL: OnDrag не применяет полосы разницы");
            return false;
        }
        if (drag.Contains("new List<", StringComparison.Ordinal)
            || drag.Contains("new Godot.Collections.Array", StringComparison.Ordinal))
        {
            GD.PrintErr($"{Tag} FAIL: OnDrag создаёт коллекцию на каждое движение мыши");
            return false;
        }
        if (Regex.IsMatch(drag, @"_ghostLayer\.SetCell"))
        {
            GD.PrintErr($"{Tag} FAIL: OnDrag ставит тайлы в обход общего буфера");
            return false;
        }
        if (source.Contains("new Godot.Collections.Array<Vector2I>", StringComparison.Ordinal))
        {
            GD.PrintErr($"{Tag} FAIL: Godot-массив создаётся на событие (нужен переиспользуемый буфер)");
            return false;
        }
        if (!source.Contains("_ghostArray.Clear()", StringComparison.Ordinal))
        {
            GD.PrintErr($"{Tag} FAIL: буфер отрисовки не переиспользуется (_ghostArray.Clear)");
            return false;
        }

        GD.Print($"{Tag} OK: OnDrag рисует только разницу и не аллоцирует на событие");
        return true;
    }

    // ─────────────────────────────── утилиты теста ───────────────────────────────

    private static string LoadSource(out bool found)
    {
        if (!Godot.FileAccess.FileExists(ToolPath))
        {
            GD.PrintErr($"{Tag} FAIL: нет {ToolPath}");
            found = false;
            return "";
        }
        found = true;
        return Godot.FileAccess.GetFileAsString(ToolPath);
    }

    /// <summary>Кусок исходника от <paramref name="from"/> до <paramref name="to"/> (to — не включая).</summary>
    private static string Slice(string source, string from, string to)
    {
        int start = source.IndexOf(from, StringComparison.Ordinal);
        if (start < 0) return "";
        int end = source.IndexOf(to, start + from.Length, StringComparison.Ordinal);
        if (end < 0) return source.Substring(start);
        return source.Substring(start, end - start);
    }
}