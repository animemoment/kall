using System;
using System.Text.RegularExpressions;
using Godot;

namespace Game.Tests;

/// <summary>
/// 02-04 / R-02: дренаж `_pendingCells` в `MapRenderer._Process` не должен превращаться
/// в 16 384 `SetCell` за кадр.
///
/// Было: `int cellsBudget = _pendingCells.Count > 4096 ? 16384 : MaxCellsPerFrame;`.
/// Массовая разметка зоны/фермы (до 250 000 клеток) держала очередь непустой много
/// кадров, и каждый такой кадр делал до 16 384 `SetCell` (TileMapLayer инвалидирует
/// квадрант и перестраивает меш) — 2–10 мс на кадр, спайк «клик назначения работы».
/// Плюс `_pendingCells.Count` обходил сегменты ConcurrentQueue каждый кадр даже при
/// пустой очереди.
///
/// Стало: бюджет ПО ВРЕМЕНИ (1.5 мс, `Stopwatch.GetTimestamp` раз в 64 клетки) и один
/// дешёвый `IsEmpty` как быстрый выход; остаток очереди переносится на следующие кадры.
///
/// ПОЧЕМУ ПРОВЕРКА ПО ИСТОЧНИКУ, А НЕ СЕКУНДОМЕРОМ: бюджет меряется в живой игре —
/// цена `SetCell` это инвалидация квадранта и перестройка меша в рендер-сервере, а в
/// `--headless` рендер-сервер пустой и замер не отражает реальность. Поэтому тест
/// закрепляет корень (что именно и как часто делается за кадр), а не микросекунды:
///  1) очередь не обходится каждый кадр, дренаж живёт в одном месте;
///  2) порция ограничена временем, а не числом клеток;
///  3) прогресс гарантирован (минимум одна клетка за кадр, иначе очередь встанет);
///  4) в горячем пути нет аллокаций и часы не читаются на каждую клетку.
///
/// Запуск: godot --headless --path . --script res://tests/run_map_renderer_budget.gd
/// </summary>
public partial class TestMapRendererBudget : RefCounted
{
    private const string SourcePath = "res://src/ui/renderers/MapRenderer.cs";
    private const string FlushSignature = "private void FlushPendingCells()";
    private const string ProcessSignature = "public override void _Process(double delta)";

    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print("[MAP-BUDGET] MapRenderer: бюджет дренажа клеток по времени (02-04/R-02)");
        GD.Print("==================================================");

        string src = Godot.FileAccess.GetFileAsString(SourcePath);
        if (string.IsNullOrEmpty(src))
        {
            GD.PrintErr($"[MAP-BUDGET] FAIL: не удалось прочитать {SourcePath}");
            return false;
        }

        string process = ExtractMethod(src, ProcessSignature);
        string flush = ExtractMethod(src, FlushSignature);
        if (process == null || flush == null)
        {
            GD.PrintErr("[MAP-BUDGET] FAIL: не найдены методы — " +
                        $"_Process={process != null}, FlushPendingCells={flush != null}. " +
                        "Похоже, дренаж снова встроен прямо в _Process");
            return false;
        }

        bool ok = true;
        ok &= NoQueueWalkEveryFrame(process, flush);
        ok &= TimeBudgetInsteadOfCellCount(src, flush);
        ok &= SingleDrainSite(src, process);
        ok &= ProgressGuaranteed(src, flush);
        ok &= HotPathWithoutAllocations(src, flush);

        GD.Print(ok ? "[MAP-BUDGET] PASS" : "[MAP-BUDGET] FAIL");
        return ok;
    }

    /// <summary>
    /// Каждый кадр раньше считался `_pendingCells.Count` (обход сегментов ConcurrentQueue),
    /// а дренаж шёл прямо в `_Process`. Теперь очередь читается одним `IsEmpty`
    /// (один volatile-read), и дренаж — единственный вызов `FlushPendingCells()`.
    /// </summary>
    private static bool NoQueueWalkEveryFrame(string process, string flush)
    {
        if (process.Contains("_pendingCells", StringComparison.Ordinal))
        {
            GD.PrintErr("[MAP-BUDGET] FAIL: _Process снова трогает _pendingCells напрямую — " +
                        "счёт.Count/дренаж в обход FlushPendingCells это лишняя работа каждый кадр");
            return false;
        }
        if (!process.Contains("FlushPendingCells()", StringComparison.Ordinal))
        {
            GD.PrintErr("[MAP-BUDGET] FAIL: _Process больше не вызывает FlushPendingCells() — " +
                        "клетки склада/фермы/зданий вообще не будут применяться");
            return false;
        }
        if (!flush.Contains("_pendingCells.IsEmpty", StringComparison.Ordinal))
        {
            GD.PrintErr("[MAP-BUDGET] FAIL: нет быстрого выхода по _pendingCells.IsEmpty — " +
                        "пустая очередь стоит Stopwatch/цикла каждый кадр");
            return false;
        }

        GD.Print("[MAP-BUDGET] OK: очередь не считается каждый кадр, дренаж — один вызов FlushPendingCells()");
        return true;
    }

    /// <summary>
    /// Корень бага: порция задавалась числом клеток (16 384 при бэклоге > 4096), а не
    /// временем. Проверяем, что бюджет остался по времени, разумный (0; 2] мс и что
    /// фиксированный счётчик не вернулся.
    /// </summary>
    private static bool TimeBudgetInsteadOfCellCount(string src, string flush)
    {
        bool ok = true;

        if (!flush.Contains("Stopwatch.GetTimestamp()", StringComparison.Ordinal)
            || !flush.Contains("PendingFlushBudgetTicks", StringComparison.Ordinal))
        {
            GD.PrintErr("[MAP-BUDGET] FAIL: в дренаже нет сравнения с бюджетом по времени " +
                        "(Stopwatch.GetTimestamp + PendingFlushBudgetTicks) — бюджет снова в клетках");
            ok = false;
        }

        if (Regex.IsMatch(src, @"_pendingCells\s*\.\s*Count\s*>\s*\d+\s*\?\s*\d+"))
        {
            GD.PrintErr("[MAP-BUDGET] FAIL: вернулся фиксированный счётчик клеток " +
                        "(`_pendingCells.Count > N ? M : K`) — это и есть спайк 02-04");
            ok = false;
        }

        double budgetMs = ConstDouble(src, "PendingFlushBudgetMs");
        if (!(budgetMs > 0.0) || budgetMs > 2.0)
        {
            GD.PrintErr($"[MAP-BUDGET] FAIL: PendingFlushBudgetMs={budgetMs} вне (0; 2.0] мс — " +
                        "бюджет кадра на симуляцию 1–2 мс, больше тратить нельзя");
            ok = false;
        }

        if (!Regex.IsMatch(src,
            @"PendingFlushBudgetMs\s*\*\s*System\.Diagnostics\.Stopwatch\.Frequency\s*/\s*1000"))
        {
            GD.PrintErr("[MAP-BUDGET] FAIL: мс не переведены в тики одномоментно " +
                        "(PendingFlushBudgetMs * Stopwatch.Frequency / 1000) — деление в горячем пути");
            ok = false;
        }

        if (ok)
            GD.Print($"[MAP-BUDGET] OK: бюджет по времени {budgetMs} мс (тики Stopwatch), " +
                     "фиксированного «16 384 клетки за кадр» нет");
        return ok;
    }

    /// <summary>Дренаж ровно один: второй цикл/второй вызов = та же работа дважды за кадр.</summary>
    private static bool SingleDrainSite(string src, string process)
    {
        int calls = Regex.Matches(process, @"FlushPendingCells\(\)\s*;").Count;
        if (calls != 1)
        {
            GD.PrintErr($"[MAP-BUDGET] FAIL: вызовов FlushPendingCells() в _Process {calls}, нужен ровно 1 — " +
                        "иначе бюджет кадра тратится дважды");
            return false;
        }

        int drains = Regex.Matches(src, @"_pendingCells\s*\.\s*TryDequeue").Count;
        if (drains != 1)
        {
            GD.PrintErr($"[MAP-BUDGET] FAIL: циклов дренажа _pendingCells {drains}, нужен ровно 1 — " +
                        "вторая копия делает ту же работу ещё раз");
            return false;
        }

        GD.Print("[MAP-BUDGET] OK: один вызов дренажа на кадр и один цикл TryDequeue");
        return true;
    }

    /// <summary>
    /// Минимум одна клетка за кадр: клетка применяется ДО проверки часов, иначе при кадре
    /// короче бюджета очередь не рассосётся никогда (и зона не дорисуется). Плюс страховка
    /// от прыгнувших часов (жёсткий потолок не больше прежнего худшего случая 16 384).
    /// </summary>
    private static bool ProgressGuaranteed(string src, string flush)
    {
        bool ok = true;

        int whileIdx = flush.IndexOf("while (_pendingCells.TryDequeue", StringComparison.Ordinal);
        int firstBreak = flush.IndexOf("break;", StringComparison.Ordinal);
        if (whileIdx < 0 || firstBreak < whileIdx)
        {
            GD.PrintErr("[MAP-BUDGET] FAIL: не найден цикл `while (_pendingCells.TryDequeue...)` " +
                        "с выходом по бюджету");
            return false;
        }

        string beforeBreak = flush.Substring(whileIdx, firstBreak - whileIdx);
        if (!beforeBreak.Contains("SetCell", StringComparison.Ordinal)
            || !beforeBreak.Contains("EraseCell", StringComparison.Ordinal))
        {
            GD.PrintErr("[MAP-BUDGET] FAIL: бюджет проверяется до применения клетки — " +
                        "при коротком кадре очередь может встать (минимум одной клетки нет)");
            ok = false;
        }

        int hardCap = ConstInt(src, "PendingFlushHardCapCells");
        if (hardCap <= 0 || hardCap > 16384)
        {
            GD.PrintErr($"[MAP-BUDGET] FAIL: PendingFlushHardCapCells={hardCap} вне (0; 16384] — " +
                        "страховка от замороженных часов не должна превышать прежний худший случай");
            ok = false;
        }
        if (!flush.Contains("PendingFlushHardCapCells", StringComparison.Ordinal))
        {
            GD.PrintErr("[MAP-BUDGET] FAIL: страховка PendingFlushHardCapCells объявлена, но в дренаже не применяется");
            ok = false;
        }

        if (ok)
            GD.Print($"[MAP-BUDGET] OK: минимум одна клетка за кадр, потолок страховки {hardCap}");
        return ok;
    }

    /// <summary>
    /// Горячий путь без аллокаций (правило проекта): никаких `new`, LINQ и чтения часов
    /// на каждую клетку — только маска раз в 64 клетки.
    /// </summary>
    private static bool HotPathWithoutAllocations(string src, string flush)
    {
        bool ok = true;

        if (Regex.IsMatch(flush, @"\bnew\s"))
        {
            GD.PrintErr("[MAP-BUDGET] FAIL: в дренаже появилась аллокация (new) — горячий путь обязан быть без GC");
            ok = false;
        }
        if (flush.Contains(".Select(", StringComparison.Ordinal)
            || flush.Contains(".Where(", StringComparison.Ordinal)
            || flush.Contains(".ToArray(", StringComparison.Ordinal)
            || flush.Contains(".ToList(", StringComparison.Ordinal))
        {
            GD.PrintErr("[MAP-BUDGET] FAIL: в дренаже появился LINQ (аллокации и лямбды)");
            ok = false;
        }

        int clockReads = Regex.Matches(flush, @"Stopwatch\.GetTimestamp\(\)").Count;
        if (clockReads < 2)
        {
            GD.PrintErr($"[MAP-BUDGET] FAIL: чтений часов {clockReads} — нет старта и/или проверки бюджета в цикле");
            ok = false;
        }
        else if (clockReads > 2)
        {
            GD.PrintErr($"[MAP-BUDGET] FAIL: чтений часов {clockReads} — GetTimestamp дёргается чаще, " +
                        "чем раз в пачку клеток, и сам съедает бюджет");
            ok = false;
        }

        int mask = ConstInt(src, "PendingFlushClockMask");
        if (mask < 15 || (mask & (mask + 1)) != 0)
        {
            GD.PrintErr($"[MAP-BUDGET] FAIL: PendingFlushClockMask={mask} не маска 2^k-1 (>=15) — " +
                        "проверка часов перестанет быть «раз в пачку клеток»");
            ok = false;
        }

        if (ok)
            GD.Print($"[MAP-BUDGET] OK: без аллокаций и LINQ, часы читаются 2 раза за кадр " +
                     $"(каждые {mask + 1} клеток)");
        return ok;
    }

    /// <summary>Числовая константа вида `const double Name = 1.5;` (−1, если не найдена).</summary>
    private static double ConstDouble(string src, string name)
    {
        Match m = Regex.Match(src, @"const\s+double\s+" + name + @"\s*=\s*([0-9]+(?:\.[0-9]+)?)");
        return m.Success
            ? double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)
            : -1.0;
    }

    /// <summary>Целая константа вида `const int Name = 64;` (−1, если не найдена).</summary>
    private static int ConstInt(string src, string name)
    {
        Match m = Regex.Match(src, @"const\s+int\s+" + name + @"\s*=\s*([0-9]+)");
        return m.Success
            ? int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)
            : -1;
    }

    /// <summary>
    /// Тело метода по сигнатуре: от первой `{` до парной `}` с пропуском строк и
    /// комментариев (в комментариях файла встречаются фигурные скобки).
    /// </summary>
    private static string ExtractMethod(string src, string signature)
    {
        int sig = src.IndexOf(signature, StringComparison.Ordinal);
        if (sig < 0) return null;

        int open = src.IndexOf('{', sig);
        if (open < 0) return null;

        int depth = 0;
        for (int i = open; i < src.Length; i++)
        {
            char c = src[i];
            if (c == '/' && i + 1 < src.Length && src[i + 1] == '/')
            {
                int nl = src.IndexOf('\n', i);
                if (nl < 0) return null;
                i = nl;
                continue;
            }
            if (c == '"' || c == '\'')
            {
                int end = SkipQuoted(src, i, c);
                if (end < 0) return null;
                i = end;
                continue;
            }
            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                    return src.Substring(open, i - open + 1);
            }
        }
        return null;
    }

    /// <summary>Индекс закрывающей кавычки (с учётом экранирования), −1 если не закрыта.</summary>
    private static int SkipQuoted(string src, int start, char quote)
    {
        for (int i = start + 1; i < src.Length; i++)
        {
            if (src[i] == '\\') { i++; continue; }
            if (src[i] == quote) return i;
            if (src[i] == '\n') return -1;
        }
        return -1;
    }
}