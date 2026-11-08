using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Godot;
using Game.UI;

namespace Game.Tests;

/// <summary>
/// 02-07: каждое срубленное дерево/добытый камень пересобирало ВЕСЬ набор теней —
/// RemoveCaster делал _anchors.Clear() и заново обходил все клетки острова (O(всех)).
/// Проверяем четыре вещи:
///  1) O(1)-удаление якоря через swap-remove (чистая функция над коллекциями);
///  2) правило дебаунса пачки изменений (чистая функция);
///  3) поведение рендерера: Add/Remove не пересобирают буфер сами, серия изменений
///     даёт ровно одну пересборку, после неё dirty-флаг чист;
///  4) структуру исходника: в RemoveCaster нет Clear/обхода всех клеток, окно не
///     сбрасывается каждым изменением, в пути изменения нет аллокаций.
///
/// Почему нужна и текстовая часть: цена O(N) видна только на большой карте в живой
/// игре, а headless не рисует MultiMesh. Поэтому структура вызова фиксируется тестом.
///
/// Запуск: godot --headless --path . --script res://tests/run_shadow_rebuild.gd
/// </summary>
public partial class TestShadowRebuild : RefCounted
{
    private const string SourcePath = "res://src/ui/renderers/ShadowCasterRenderer.cs";

    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print("[SHADOW-REBUILD] Пачка изменений теней -> одна пересборка (02-07)");
        GD.Print("==================================================");

        bool ok = true;
        ok &= CheckSwapRemove();
        ok &= CheckDebounceRule();
        ok &= CheckRendererCoalescing();
        ok &= CheckSourceStructure();

        GD.Print(ok ? "[SHADOW-REBUILD] PASS" : "[SHADOW-REBUILD] FAIL");
        return ok;
    }

    // --- 1. O(1) swap-remove: список якорей не пересобирается ---
    private bool CheckSwapRemove()
    {
        bool ok = true;

        var anchors = new List<Vector2>(5);
        var cells = new List<(int X, int Y)>(5);
        var index = new Dictionary<(int X, int Y), int>(5);
        for (int i = 0; i < 5; i++)
        {
            index[(i, 0)] = anchors.Count;
            anchors.Add(new Vector2(i, 0));
            cells.Add((i, 0));
        }
        ok &= Check("исходные индексы согласованы", MappingConsistent(anchors, cells, index));

        // Удаляем середину: дырка закрывается последним якорем, без обхода списка.
        ok &= Check("удаление середины возвращает true",
            ShadowCasterRenderer.SwapRemoveAnchor(anchors, cells, index, 2, 0));
        ok &= Check("удаление середины: якорей стало на 1 меньше",
            anchors.Count == 4 && cells.Count == 4 && index.Count == 4);
        ok &= Check("удаление середины: клетки больше нет в словаре", !index.ContainsKey((2, 0)));
        ok &= Check("удаление середины: последний якорь переехал в дырку",
            anchors[2] == new Vector2(4, 0) && cells[2] == (4, 0));
        ok &= Check("удаление середины: индекс переехавшей клетки обновлён", index[(4, 0)] == 2);
        ok &= Check("удаление середины: соседи не сдвинуты",
            index[(0, 0)] == 0 && index[(1, 0)] == 1 && index[(3, 0)] == 3);
        ok &= Check("после удаления середины индексы согласованы",
            MappingConsistent(anchors, cells, index));

        // Удаление последнего — без перемещений.
        ok &= Check("удаление последнего",
            ShadowCasterRenderer.SwapRemoveAnchor(anchors, cells, index, 4, 0));
        ok &= Check("после удаления последнего индексы согласованы",
            MappingConsistent(anchors, cells, index));

        // Повтор/промах — no-op: массовая рубка не должна дёргать dirty зря.
        ok &= Check("повторное удаление — false",
            !ShadowCasterRenderer.SwapRemoveAnchor(anchors, cells, index, 4, 0));
        ok &= Check("удаление несуществующей клетки — false",
            !ShadowCasterRenderer.SwapRemoveAnchor(anchors, cells, index, 99, 99));
        ok &= Check("промах не изменил список", anchors.Count == 3 && index.Count == 3);

        // Удаляем всё подряд — пересборки списка нет, инварианты держатся.
        while (anchors.Count > 0)
        {
            var cell = cells[cells.Count - 1];
            ShadowCasterRenderer.SwapRemoveAnchor(anchors, cells, index, cell.X, cell.Y);
        }
        ok &= Check("после удаления всех якорей коллекции пусты",
            anchors.Count == 0 && cells.Count == 0 && index.Count == 0);

        return ok;
    }

    // --- 2. Правило дебаунса: окно отсчитывается от первого изменения пачки ---
    private bool CheckDebounceRule()
    {
        bool ok = true;
        ok &= Check("внутри окна не пересобираем",
            !ShadowCasterRenderer.CasterDebounceElapsed(0f, 0.016f, 0.15f));
        ok &= Check("на границе окна пересобираем",
            ShadowCasterRenderer.CasterDebounceElapsed(0.14f, 0.016f, 0.15f));
        ok &= Check("истёкшее окно пересобираем",
            ShadowCasterRenderer.CasterDebounceElapsed(0.2f, 0f, 0.15f));
        ok &= Check("нулевой delta окно не двигает",
            !ShadowCasterRenderer.CasterDebounceElapsed(0.05f, 0f, 0.15f));
        return ok;
    }

    // --- 3. Рендерер: пачка изменений -> ровно одна пересборка ---
    private bool CheckRendererCoalescing()
    {
        bool ok = true;
        const int count = 2000;
        var renderer = new ShadowCasterRenderer();

        for (int i = 0; i < count; i++)
            renderer.AddCaster(i, 0);
        ok &= Check("Add не пересобирает буфер сам",
            renderer.CasterRebuilds == 0 && renderer.CastersDirty);
        ok &= Check("якоря добавлены сразу, без ожидания пересборки",
            renderer.CasterCount == count);

        ok &= Check("короткий тик окно не закрывает", !renderer.FlushCasterChanges(0.016f));
        ok &= Check("ещё короткий тик окно не закрывает", !renderer.FlushCasterChanges(0.016f));
        ok &= Check("внутри окна пересборок нет", renderer.CasterRebuilds == 0);
        ok &= Check("окно истекло — ровно одна пересборка пачки",
            renderer.FlushCasterChanges(0.2f));
        ok &= Check("счётчик пересборок равен 1", renderer.CasterRebuilds == 1);
        ok &= Check("после пересборки dirty-флаг чист", !renderer.CastersDirty);
        ok &= Check("пустой тик без изменений не пересобирает",
            !renderer.FlushCasterChanges(1f) && renderer.CasterRebuilds == 1);

        // Массовая вырубка: 2000 RemoveCaster подряд — O(1) каждый, ни одной пересборки.
        for (int i = 0; i < count; i++)
            renderer.RemoveCaster(i, 0);
        ok &= Check("Remove не пересобирает буфер сам",
            renderer.CasterRebuilds == 1 && renderer.CastersDirty);
        ok &= Check("Remove применён сразу (O(1)), якорей 0", renderer.CasterCount == 0);
        ok &= Check("серия Remove слилась в одну пересборку",
            renderer.FlushCasterChanges(0.2f) && renderer.CasterRebuilds == 2);

        // Одиночное изменение — ровно одна пересборка, без полного обхода списка.
        renderer.AddCaster(7, 7);
        ok &= Check("одиночный Add не пересобирает сам", renderer.CasterRebuilds == 2);
        ok &= Check("одиночный Add даёт одну пересборку",
            renderer.FlushCasterChanges(0.2f) && renderer.CasterRebuilds == 3);

        int before = renderer.CasterCount;
        renderer.AddCaster(7, 7);
        ok &= Check("повторный Add той же клетки не меняет набор и не будит dirty",
            renderer.CasterCount == before && !renderer.CastersDirty);

        renderer.RemoveCaster(7, 7);
        ok &= Check("одиночный Remove не пересобирает сам",
            renderer.CasterRebuilds == 3 && renderer.CastersDirty);
        ok &= Check("одиночный Remove сливается в одну пересборку",
            renderer.FlushCasterChanges(0.2f) && renderer.CasterRebuilds == 4);

        // N одиночных изменений, каждое со своим коротким тиком, но в одном окне:
        // ни одного пересбора до истечения окна, затем ровно одна на всю пачку.
        bool noEarlyRebuild = true;
        for (int i = 0; i < 50; i++)
        {
            renderer.AddCaster(100 + i, 0);
            noEarlyRebuild &= !renderer.FlushCasterChanges(0.001f);
        }
        ok &= Check("N одиночных Add внутри окна не дают N пересборок",
            noEarlyRebuild && renderer.CasterRebuilds == 4);
        ok &= Check("накопленные N изменений слились в одну пересборку",
            renderer.FlushCasterChanges(0.2f) && renderer.CasterRebuilds == 5);
        ok &= Check("после пересборки dirty-флаг снова чист", !renderer.CastersDirty);

        renderer.Free();
        return ok;
    }

    // --- 4. Структура исходника: старого O(N)-пути и сброса окна больше нет ---
    private bool CheckSourceStructure()
    {
        bool ok = true;
        string src = Godot.FileAccess.GetFileAsString(SourcePath);
        if (string.IsNullOrEmpty(src))
        {
            GD.PrintErr($"[SHADOW-REBUILD] FAIL: не прочитал {SourcePath}");
            return false;
        }

        string add = ExtractMethod(src, "void AddCaster");
        string remove = ExtractMethod(src, "void RemoveCaster");
        string tick = ExtractMethod(src, "void Tick");
        string flush = ExtractMethod(src, "bool FlushCasterChanges");
        string swap = ExtractMethod(src, "bool SwapRemoveAnchor");

        ok &= Check("AddCaster найден целиком", add.Length > 0);
        ok &= Check("RemoveCaster найден целиком", remove.Length > 0);
        ok &= Check("Tick найден целиком", tick.Length > 0);
        ok &= Check("FlushCasterChanges найден целиком", flush.Length > 0);
        ok &= Check("SwapRemoveAnchor найден целиком", swap.Length > 0);

        // Регресс: старый RemoveCaster чистил _anchors и заново обходил все клетки.
        ok &= Check("RemoveCaster не чистит список якорей", !remove.Contains("_anchors.Clear()"));
        ok &= Check("RemoveCaster не обходит все клетки",
            !Regex.IsMatch(remove, @"\bforeach\b|\bfor\s*\(") && !remove.Contains(".Clear()"));
        ok &= Check("RemoveCaster делает O(1) swap-remove", remove.Contains("SwapRemoveAnchor("));

        // Дебаунс-флаг: изменение только помечает dirty, буфер заливает Tick.
        ok &= Check("AddCaster не заливает буфер",
            !add.Contains("PushBuffer(") && !add.Contains("_anchors.Clear()"));
        ok &= Check("RemoveCaster не заливает буфер", !remove.Contains("PushBuffer("));
        ok &= Check("Tick сливает пачку через FlushCasterChanges", tick.Contains("FlushCasterChanges("));
        ok &= Check("буфер заливается внутри FlushCasterChanges", flush.Contains("PushBuffer("));
        ok &= Check("окно дебаунса не сбрасывается каждым изменением",
            !remove.Contains("_casterTimer") && !add.Contains("_casterTimer"));
        ok &= Check("в пути изменения нет аллокаций",
            !add.Contains("new ") && !remove.Contains("new ") && !flush.Contains("new "));
        ok &= Check("swap-remove без Clear и повторного заполнения",
            swap.Length > 0 && !swap.Contains(".Clear()") && !swap.Contains("new "));
        ok &= Check("счётчик пересборок увеличивается в одном месте",
            Regex.Matches(src, @"_casterRebuilds\+\+").Count == 1);
        ok &= Check("RebuildStatic чистит словарь индексов", src.Contains("_cellIndex.Clear()"));
        ok &= Check("нет старого HashSet _cells",
            !Regex.IsMatch(src, @"HashSet<\(int X, int Y\)>\s+_cells"));
        ok &= Check("нет старого дебаунса _bufferDirty", !src.Contains("_bufferDirty"));

        return ok;
    }

    private static bool MappingConsistent(
        List<Vector2> anchors,
        List<(int X, int Y)> cells,
        Dictionary<(int X, int Y), int> index)
    {
        if (anchors.Count != cells.Count || cells.Count != index.Count)
            return false;
        for (int i = 0; i < cells.Count; i++)
        {
            if (!index.TryGetValue(cells[i], out int idx) || idx != i)
                return false;
        }
        return true;
    }

    private bool Check(string what, bool condition)
    {
        if (condition)
        {
            GD.Print($"[SHADOW-REBUILD] OK: {what}");
            return true;
        }
        GD.PrintErr($"[SHADOW-REBUILD] FAIL: {what}");
        return false;
    }

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