using System;
using System.Text.RegularExpressions;
using Godot;

namespace Game.Tests;

/// <summary>
/// 02-01: при появлении/сносе стены грязной должна помечаться не только сама клетка,
/// но и восемь соседей — маска автотайла у стены зависит от соседей.
///
/// Почему проверка текстовая: визуальный результат (соединение автотайла) виден только
/// в живой игре, а headless Godot не рисует TileMapLayer. Поэтому тест фиксирует
/// структуру вызова: в обработчике изменения стен есть обход окрестности 3x3, в нём
/// помечаются ОБА стеновых слоя (построенные стены и чертежи), а тень ставится только
/// на саму изменившуюся клетку (иначе на каждую стену появлялось бы 9 теней).
///
/// Запуск: godot --headless --path . --script res://tests/run_wall_neighbour_dirty.gd
/// </summary>
public partial class TestWallNeighbourDirty : RefCounted
{
    private const string SourcePath = "res://src/ui/renderers/MapRenderer.cs";

    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print("[WALL-DIRTY] Стена помечает окрестность 3x3 (02-01)");
        GD.Print("==================================================");

        string src = Godot.FileAccess.GetFileAsString(SourcePath);
        if (string.IsNullOrEmpty(src))
        {
            GD.PrintErr($"[WALL-DIRTY] FAIL: не прочитал {SourcePath}");
            return false;
        }

        bool ok = true;
        ok &= Check("есть помощник MarkWallNeighbourhoodDirty", src.Contains("MarkWallNeighbourhoodDirty(int x, int y)"));

        string helper = ExtractMethod(src, "MarkWallNeighbourhoodDirty");
        ok &= Check("помощник найден целиком", helper.Length > 0);
        ok &= Check("обход по Y: -1..1", Regex.IsMatch(helper, @"for \(int dy = -1; dy <= 1; dy\+\+\)"));
        ok &= Check("обход по X: -1..1", Regex.IsMatch(helper, @"for \(int dx = -1; dx <= 1; dx\+\+\)"));
        ok &= Check("помечает построенные стены (_builtWalls)", helper.Contains("_builtWalls?.MarkDirty"));
        ok &= Check("помечает чертежи стен (_wallBlueprints)", helper.Contains("_wallBlueprints?.MarkDirty"));

        string handler = ExtractMethod(src, "OnWallTilesUpdated");
        ok &= Check("обработчик найден целиком", handler.Length > 0);
        ok &= Check("обработчик зовёт помощника окрестности",
            handler.Contains("MarkWallNeighbourhoodDirty("));
        ok &= Check("в цикле по изменённым клеткам (не после цикла)",
            Regex.IsMatch(handler, @"foreach \(var \(x, y\) in changedTiles\)\s*\{[\s\S]*?MarkWallNeighbourhoodDirty\(x, y\)"));

        // Тень: только на саму клетку — в цикле по changedTiles, не в помощнике.
        ok &= Check("тень ставится в обработчике, а не в помощнике", !helper.Contains("ShadowRenderer"));
        ok &= Check("тень ставится по самой клетке", handler.Contains("ShadowRenderer?.AddCaster(x, y)"));
        ok &= Check("тень снимается по самой клетке", handler.Contains("ShadowRenderer?.RemoveCaster(x, y)"));
        ok &= Check("соседи тени не получают",
            !Regex.IsMatch(handler, @"AddCaster\(x \+ dx|AddCaster\(x\+dx"));

        // Старый вариант (только сама клетка) — регресс.
        bool oldOnlyCell = Regex.IsMatch(handler,
            @"_builtWalls\?\.MarkDirty\(new Vector2I\(x, y\)\)") &&
            !handler.Contains("MarkWallNeighbourhoodDirty(");
        ok &= Check("регресс: грязной помечается только сама клетка", !oldOnlyCell);

        GD.Print(ok ? "[WALL-DIRTY] PASS" : "[WALL-DIRTY] FAIL");
        return ok;
    }

    private bool Check(string what, bool condition)
    {
        if (condition)
        {
            GD.Print($"[WALL-DIRTY] OK: {what}");
            return true;
        }
        GD.PrintErr($"[WALL-DIRTY] FAIL: {what}");
        return false;
    }

    /// <summary>Тело метода по имени: от строки объявления до парной закрывающей скобки.</summary>
    private static string ExtractMethod(string src, string name)
    {
        int decl = src.IndexOf(name + "(", StringComparison.Ordinal);
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