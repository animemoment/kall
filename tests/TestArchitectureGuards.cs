using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Godot;

namespace Game.Tests;

/// <summary>
/// Исполняемая конституция проекта: не даёт вернуть «второй мир».
///
/// Идея: агент (или человек) не ломает правила потому, что «знает инструкцию» —
/// он ломает их потому, что код это позволяет. Поэтому правила проверяются
/// сборкой, а не документацией.
///
/// Механика «храповик» (ratchet):
///   - в tests/arch_baseline.json лежит текущее число нарушений по каждому правилу;
///   - тест падает, если нарушений СТАЛО БОЛЬШЕ;
///   - тест подсказывает, когда нарушений стало меньше, — тогда планку надо опустить.
/// Так запрет начинает работать с первого дня, не требуя сначала всё вычистить.
///
/// Запуск: godot --headless --path . --script res://tests/run_architecture_guards.gd
/// </summary>
public partial class TestArchitectureGuards : RefCounted
{
    private const string BaselinePath = "res://tests/arch_baseline.json";

    /// <summary>Правило: имя, описание, регексы, область, считать вхождения, требовать ВСЕ регексы (иначе — любой).</summary>
    private readonly record struct Rule(string Id, string Title, string[] Patterns, string Scope, bool CountMatches, bool AllPatterns = true);

    private static readonly Rule[] Rules =
    {
        new("magic_1024",
            "Литерал 1024 в игровом коде (граница двух миров)",
            new[] { @"\b1024\b" }, "src", false),

        new("coverage_512",
            "Сигнатура 512-клампа: ChunkShift = 4 ВМЕСТЕ с ChunkDim = 32",
            new[] { @"ChunkShift\s*=\s*4\b", @"ChunkDim\s*=\s*32\b" }, "src", false),

        new("is_island_cell_defs",
            "Второе определение IsIslandCell (понятие «остров» протекло в код)",
            new[] { @"bool\s+IsIslandCell\s*\(" }, "src", true),

        new("decor_pass_defs",
            "Вторая формула декора (DecorPass / IslandDecorPass)",
            new[] { @"(static\s+\(bool\s+HasTree,\s*bool\s+HasStone\)\s+(Island)?DecorPass\s*\()" }, "src", true),

        new("core_to_ui",
            "Ссылка core → ui (нарушение слоёв, разрастание цикла)",
            new[] { @"Game\.UI" }, "src/core", false),

        new("bypass_generator_sim",
            "Симуляция зовёт генератор в обход WorldModel",
            new[] { @"WorldCellQuery\.", @"UnifiedWorldGenerator\.SampleCell" }, "src/simulation", false, false),

        new("bypass_generator_ui",
            "UI зовёт генератор в обход WorldModel",
            new[] { @"WorldCellQuery\.", @"UnifiedWorldGenerator\.SampleCell" }, "src/ui", false, false),
    };

    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print("[GUARDS] Architecture guards — проверка конституции мира");
        GD.Print("==================================================");

        string root = ProjectSettings.GlobalizePath("res://");
        string srcRoot = Path.Combine(root, "src");
        if (!Directory.Exists(srcRoot))
        {
            GD.PrintErr($"[GUARDS] не найден каталог исходников: {srcRoot}");
            return false;
        }

        Dictionary<string, int> current = Measure(srcRoot);
        Dictionary<string, int> baseline = LoadBaseline(root, current);

        bool ok = true;
        var report = new StringBuilder();
        report.Append("правило                     сейчас  планка  статус\n");
        report.Append("---------------------------------------------------------\n");

        foreach (Rule rule in Rules)
        {
            int now = current.TryGetValue(rule.Id, out int c) ? c : 0;
            int was = baseline.TryGetValue(rule.Id, out int b) ? b : 0;

            string status;
            if (now > was)
            {
                status = "РЕГРЕСС +" + (now - was);
                ok = false;
            }
            else if (now < was)
            {
                status = "можно опустить до " + now;
            }
            else
            {
                status = "ok";
            }

            report.Append(rule.Id.PadRight(27))
                  .Append(now.ToString().PadLeft(6))
                  .Append(was.ToString().PadLeft(8))
                  .Append("  ")
                  .Append(status)
                  .Append('\n');
        }

        report.Append("---------------------------------------------------------\n");
        GD.Print(report.ToString());

        // Детализация по файлам для правил, которые уже нарушены.
        foreach (Rule rule in Rules)
        {
            List<string> files = FilesMatching(srcRoot, rule);
            if (files.Count == 0) continue;
            GD.Print($"[GUARDS] {rule.Id}: {rule.Title}");
            foreach (string f in files)
                GD.Print("           " + f);
        }

        if (ok)
        {
            GD.Print("==================================================");
            GD.Print("[GUARDS] OK: второй мир не вернулся. (Code 0)");
            GD.Print("==================================================");
            return true;
        }

        GD.PrintErr("[GUARDS] FAIL: правила нарушены — см. таблицу выше.");
        return false;
    }

    // ================== измерение ==================

    private static Dictionary<string, int> Measure(string srcRoot)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Rule rule in Rules)
        {
            List<string> files = FilesMatching(srcRoot, rule);
            if (rule.CountMatches)
            {
                int total = 0;
                foreach (string rel in files)
                    total += CountMatches(Path.Combine(srcRoot, rel), rule.Patterns[0]);
                result[rule.Id] = total;
            }
            else
            {
                result[rule.Id] = files.Count;
            }
        }
        return result;
    }

    private static List<string> FilesMatching(string srcRoot, Rule rule)
    {
        string scopeRoot = rule.Scope == "src"
            ? srcRoot
            : Path.Combine(srcRoot, rule.Scope.Substring("src/".Length));

        var hits = new List<string>();
        if (!Directory.Exists(scopeRoot)) return hits;

        string srcPrefix = srcRoot + Path.DirectorySeparatorChar;
        foreach (string path in Directory.EnumerateFiles(scopeRoot, "*.cs", SearchOption.AllDirectories))
        {
            string rel = path.Replace(srcPrefix, "").Replace('\\', '/');
            if (IsExcluded(rel)) continue;
            string text = File.ReadAllText(path);
            // Строки-комментарии не считаем: упоминание в документации — не вызов.
            string scan = Regex.Replace(text, @"^[ \t]*//.*$", "", RegexOptions.Multiline);
            // Строковые литералы тоже не считаем (2026-10-24): текст карточки
            // инспектора со словами «…SampleCell» — это ДАННЫЕ, не вызов генератора.
            scan = Regex.Replace(scan, "\"(?:[^\"\\\\]|\\\\.)*\"", "\"\"");

            bool any = false;
            bool all = true;
            for (int i = 0; i < rule.Patterns.Length; i++)
            {
                bool hit = Regex.IsMatch(scan, rule.Patterns[i]);
                if (hit) any = true; else all = false;
            }
            if (rule.AllPatterns ? all : any) hits.Add(rel);
        }
        hits.Sort(StringComparer.Ordinal);
        return hits;
    }

    /// <summary>
    /// Что не считаем: сам страж, инспектор клетки и файлы тестов.
    /// ВАЖНО: исключается ровно каталог src/tools, а НЕ любой «tools» —
    /// src/ui/tools/ это игровой код (инструменты игрока), его считать обязательно.
    /// </summary>
    private static bool IsExcluded(string relPath) =>
        relPath.StartsWith("tools/", StringComparison.OrdinalIgnoreCase) ||
        relPath.EndsWith("TestArchitectureGuards.cs", StringComparison.OrdinalIgnoreCase) ||
        relPath.EndsWith("CellInspector.cs", StringComparison.OrdinalIgnoreCase);

    private static int CountMatches(string path, string pattern) =>
        Regex.Matches(File.ReadAllText(path), pattern).Count;

    // ================== baseline ==================

    private static Dictionary<string, int> LoadBaseline(string root, Dictionary<string, int> current)
    {
        string path = Path.Combine(root, "tests", "arch_baseline.json");
        if (File.Exists(path))
        {
            try
            {
                string json = File.ReadAllText(path);
                var parsed = JsonSerializer.Deserialize<Dictionary<string, int>>(json);
                if (parsed != null) return parsed;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[GUARDS] планка повреждена ({ex.Message}) — беру текущие значения.");
            }
        }

        GD.Print("[GUARDS] Планка не найдена. Текущие значения (скопируй в tests/arch_baseline.json):");
        var sb = new StringBuilder("{\n");
        int i = 0;
        foreach (Rule rule in Rules)
        {
            int v = current.TryGetValue(rule.Id, out int c) ? c : 0;
            sb.Append("  \"").Append(rule.Id).Append("\": ").Append(v);
            if (++i < Rules.Length) sb.Append(',');
            sb.Append('\n');
        }
        sb.Append("}\n");
        GD.Print("BASELINE_JSON_BEGIN");
        GD.Print(sb.ToString());
        GD.Print("BASELINE_JSON_END");
        return current;
    }
}