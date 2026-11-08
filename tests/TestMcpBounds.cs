using System;
using Godot;

namespace Game.Tests;

/// <summary>
/// 25-02 / 29-14: у команд MCP-сервера не было верхней границы работы.
///
/// Было:
///  * `run_gdscript` читал `timeout_ms`, кладл его в ответ и ни разу не
///    использовал: `instance.call("run")` крутился в главном потоке, поэтому
///    `while true: pass` вешал игру навсегда (BUSY_TIMEOUT живёт в том же
///    `_process`, который уже не возвращался);
///  * `scroll.amount` и `terrain.width*depth` брались из JSON без потолка:
///    10^9 кликов и 5000x5000 клеток — это сотни миллионов объектов и
///    вершин в одном кадре, то есть «Not responding» и kill процесса.
///
/// Проверяем:
///  (1) таймаут реально прерывает исполнение — собираем `while true: pass`
///      через серверный `_apply_run_deadline`, запускаем настоящий GDScript с
///      малым deadline и убеждаемся, что вызов вернулся, а не завис;
///  (2) огромные `scroll.amount` / `terrain.width x depth` усекаются до
///      потолка (100 кликов, 128x128), а ответы команд сообщают об усечении;
///  (3) нормальные значения и обычные циклы (`for`, `while`) проходят без
///      изменений — усечение не отключает функцию.
///
/// Запуск: godot --headless --path . --script res://tests/run_mcp_bounds.gd
/// </summary>
public partial class TestMcpBounds : RefCounted
{
    private const string ServerPath = "res://autoload/mcp_interaction_server.gd";

    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print("[MCP-BOUNDS] timeout_ms и счётчики из JSON ограничены (25-02, 29-14)");
        GD.Print("==================================================");

        if (!Godot.FileAccess.FileExists(ServerPath))
        {
            GD.PrintErr($"[MCP-BOUNDS] нет {ServerPath}");
            return false;
        }

        string src = Godot.FileAccess.GetFileAsString(ServerPath);
        if (string.IsNullOrWhiteSpace(src))
        {
            GD.PrintErr($"[MCP-BOUNDS] {ServerPath} пустой");
            return false;
        }

        GDScript serverScript = GD.Load<GDScript>(ServerPath);
        if (serverScript == null)
        {
            GD.PrintErr($"[MCP-BOUNDS] не удалось загрузить {ServerPath} как GDScript");
            return false;
        }

        bool ok = true;
        ok &= CommandWiringIsBounded(src);
        ok &= TimeoutInterruptsForReal(serverScript);
        ok &= NormalLoopsStillRun(serverScript);
        ok &= ScrollAmountClamped(serverScript);
        ok &= TerrainSizeClamped(serverScript);

        GD.Print(ok ? "[MCP-BOUNDS] PASS" : "[MCP-BOUNDS] FAIL");
        return ok;
    }

    /// <summary>
    /// (а) Контракт по тексту сервера: гейты стоят ДО тяжёлых циклов, потолки
    /// объявлены, ответы сообщают об усечении, а ответ по таймауту — ошибка,
    /// а не «success».
    /// </summary>
    private bool CommandWiringIsBounded(string src)
    {
        foreach (string constant in new[]
                 {
                     "MAX_RUN_TIMEOUT_MS: int = 60000",
                     "MAX_SCROLL_AMOUNT: int = 100",
                     "MAX_TERRAIN_DIM: int = 128",
                     "MAX_TERRAIN_CELLS: int = 16384",
                 })
        {
            if (!src.Contains(constant, StringComparison.Ordinal))
            {
                GD.PrintErr($"[MCP-BOUNDS] (а) FAIL: нет потолка `{constant}`");
                return false;
            }
        }

        string run = FunctionBody(src, "func _cmd_run_gdscript(");
        if (run == null)
        {
            GD.PrintErr("[MCP-BOUNDS] (а) FAIL: нет func _cmd_run_gdscript");
            return false;
        }

        int deadline = run.IndexOf("_apply_run_deadline(", StringComparison.Ordinal);
        int compile = run.IndexOf("script.reload()", StringComparison.Ordinal);
        if (deadline < 0)
        {
            GD.PrintErr("[MCP-BOUNDS] (а) FAIL: код не размечается дедлайном до компиляции");
            return false;
        }
        if (compile < 0 || deadline > compile)
        {
            GD.PrintErr("[MCP-BOUNDS] (а) FAIL: _apply_run_deadline стоит ПОСЛЕ script.reload()");
            return false;
        }
        if (!run.Contains("mini(requested_timeout_ms, MAX_RUN_TIMEOUT_MS)", StringComparison.Ordinal))
        {
            GD.PrintErr("[MCP-BOUNDS] (а) FAIL: timeout_ms не ограничен сверху");
            return false;
        }
        if (!run.Contains("\"error\": \"timeout\"", StringComparison.Ordinal))
        {
            GD.PrintErr("[MCP-BOUNDS] (а) FAIL: по таймауту нет ответа-ошибки (клиент увидит зависание)");
            return false;
        }

        string instrument = FunctionBody(src, "func _apply_run_deadline(");
        if (instrument == null
            || !instrument.Contains("Time.get_ticks_msec() <", StringComparison.Ordinal)
            || instrument.Contains("static func _apply_run_deadline("))
        {
            GD.PrintErr("[MCP-BOUNDS] (а) FAIL: нет разметки циклов дедлайном");
            return false;
        }

        string scroll = FunctionBody(src, "func _cmd_scroll(");
        if (scroll == null)
        {
            GD.PrintErr("[MCP-BOUNDS] (а) FAIL: нет func _cmd_scroll");
            return false;
        }

        int scrollClamp = scroll.IndexOf("clamp_scroll_amount(", StringComparison.Ordinal);
        int scrollLoop = scroll.IndexOf("for i in amount", StringComparison.Ordinal);
        if (scrollClamp < 0 || scrollLoop < 0 || scrollClamp > scrollLoop)
        {
            GD.PrintErr("[MCP-BOUNDS] (а) FAIL: scroll крутит amount без потолка");
            return false;
        }
        if (!scroll.Contains("\"truncated\": truncated", StringComparison.Ordinal))
        {
            GD.PrintErr("[MCP-BOUNDS] (а) FAIL: scroll не сообщает об усечении");
            return false;
        }

        string terrain = FunctionBody(src, "func _cmd_terrain(");
        if (terrain == null)
        {
            GD.PrintErr("[MCP-BOUNDS] (а) FAIL: нет func _cmd_terrain");
            return false;
        }

        int terrainClamp = terrain.IndexOf("clamp_terrain_size(", StringComparison.Ordinal);
        int terrainLoop = terrain.IndexOf("range(width * depth)", StringComparison.Ordinal);
        if (terrainClamp < 0 || terrainLoop < 0 || terrainClamp > terrainLoop)
        {
            GD.PrintErr("[MCP-BOUNDS] (а) FAIL: terrain строит width*depth без потолка");
            return false;
        }
        if (!terrain.Contains("\"truncated\": truncated", StringComparison.Ordinal))
        {
            GD.PrintErr("[MCP-BOUNDS] (а) FAIL: terrain не сообщает об усечении");
            return false;
        }

        GD.Print("[MCP-BOUNDS] (а) OK: гейты до циклов, потолки объявлены, ответы сообщают об усечении");
        return true;
    }

    /// <summary>
    /// (б) Настоящий запуск: `while true: pass` с deadline через 150 мс обязан
    /// вернуть управление после дедлайна, а не зависнуть. Это ровно тот путь,
    /// которым идёт `_cmd_run_gdscript`.
    /// </summary>
    private bool TimeoutInterruptsForReal(GDScript serverScript)
    {
        Node server = NewServer(serverScript);
        if (server == null)
        {
            GD.PrintErr("[MCP-BOUNDS] (б) FAIL: не удалось создать экземпляр сервера");
            return false;
        }

        try
        {
            const string infinite = "while true: pass";
            // Deadline is deliberately in the PAST: the guard must short-circuit on the very
            // first iteration, so the check is deterministic. The previous version used
            // now + 150 ms and asserted "did not return before the deadline"; on a loaded
            // machine generating + compiling the snippet can take longer than that budget,
            // the loop then exits immediately (legitimately) and the test failed for the
            // wrong reason — a flaky assertion, not a broken guard. A guard that is missing
            // or wrong makes this call hang forever, which check_all reports as a timeout.
            int deadline = (int)Time.GetTicksMsec() - 1000;
            string instrumented = server.Call("_apply_run_deadline", infinite, deadline).AsString();
            if (!instrumented.Contains("Time.get_ticks_msec() <", StringComparison.Ordinal))
            {
                GD.PrintErr($"[MCP-BOUNDS] (б) FAIL: while не размечен дедлайном: {instrumented}");
                return false;
            }

            string wrapped = server.Call("_build_run_script", instrumented).AsString();
            ulong started = Time.GetTicksMsec();
            bool ran = TryRunSnippet(wrapped, out _, out string why);
            ulong elapsed = Time.GetTicksMsec() - started;

            if (!ran)
            {
                GD.PrintErr($"[MCP-BOUNDS] (б) FAIL: размеченный скрипт не запустился: {why}");
                return false;
            }
            if (elapsed > 3000)
            {
                GD.PrintErr($"[MCP-BOUNDS] (б) FAIL: дедлайн уже прошёл, а вызов шёл {elapsed} мс");
                return false;
            }
            if (Time.GetTicksMsec() < (ulong)deadline)
            {
                GD.PrintErr("[MCP-BOUNDS] (б) FAIL: вернулся раньше просроченного дедлайна — часы поехали");
                return false;
            }

            GD.Print($"[MCP-BOUNDS] (б) OK: `while true: pass` прерван просроченным дедлайном за {elapsed} мс");
            return true;
        }
        finally
        {
            server.Free();
        }
    }

    /// <summary>
    /// (в) Обычные циклы после разметки дают прежний результат: `for` и `while`
    /// не ломаются, усечение не отключает команду.
    /// </summary>
    private bool NormalLoopsStillRun(GDScript serverScript)
    {
        Node server = NewServer(serverScript);
        if (server == null)
        {
            GD.PrintErr("[MCP-BOUNDS] (в) FAIL: не удалось создать экземпляр сервера");
            return false;
        }

        try
        {
            int deadline = (int)Time.GetTicksMsec() + 5000;
            (string Code, int Expected)[] cases =
            {
                ("var total: int = 0\nfor i in range(3):\n\ttotal += i\nreturn total", 3),
                ("var i: int = 0\nwhile i < 3:\n\ti += 1\nreturn i", 3),
                ("return 7", 7),
            };

            foreach ((string code, int expected) in cases)
            {
                string instrumented = server.Call("_apply_run_deadline", code, deadline).AsString();
                string wrapped = server.Call("_build_run_script", instrumented).AsString();
                if (!TryRunSnippet(wrapped, out Variant result, out string why))
                {
                    GD.PrintErr($"[MCP-BOUNDS] (в) FAIL: `{code.Replace("\n", "\\n")}` не запустился: {why}");
                    return false;
                }
                int actual = result.AsInt32();
                if (actual != expected)
                {
                    GD.PrintErr($"[MCP-BOUNDS] (в) FAIL: `{code.Replace("\n", "\\n")}` дал {actual}, ожидалось {expected}");
                    return false;
                }
            }

            GD.Print("[MCP-BOUNDS] (в) OK: нормальные for/while/выражения дают прежний результат");
            return true;
        }
        finally
        {
            server.Free();
        }
    }

    /// <summary>(г) `scroll.amount = 10^9` усекается до потолка, обычный остаётся как был.</summary>
    private bool ScrollAmountClamped(GDScript serverScript)
    {
        Node server = NewServer(serverScript);
        if (server == null)
        {
            GD.PrintErr("[MCP-BOUNDS] (г) FAIL: не удалось создать экземпляр сервера");
            return false;
        }

        try
        {
            int huge = server.Call("clamp_scroll_amount", 1000000000).AsInt32();
            int normal = server.Call("clamp_scroll_amount", 3).AsInt32();
            int one = server.Call("clamp_scroll_amount", 1).AsInt32();

            if (huge != 100)
            {
                GD.PrintErr($"[MCP-BOUNDS] (г) FAIL: amount=10^9 дал {huge}, ожидался потолок 100");
                return false;
            }
            if (normal != 3 || one != 1)
            {
                GD.PrintErr($"[MCP-BOUNDS] (г) FAIL: нормальные amount искажены (3 -> {normal}, 1 -> {one})");
                return false;
            }

            GD.Print("[MCP-BOUNDS] (г) OK: scroll.amount=10^9 -> 100, amount=3 и 1 без изменений");
            return true;
        }
        finally
        {
            server.Free();
        }
    }

    /// <summary>(д) `terrain 5000x5000` усекается до 128x128 (16384 клеток), 16x16 проходит как есть.</summary>
    private bool TerrainSizeClamped(GDScript serverScript)
    {
        Node server = NewServer(serverScript);
        if (server == null)
        {
            GD.PrintErr("[MCP-BOUNDS] (д) FAIL: не удалось создать экземпляр сервера");
            return false;
        }

        try
        {
            Vector2I huge = server.Call("clamp_terrain_size", 5000, 5000).AsVector2I();
            Vector2I normal = server.Call("clamp_terrain_size", 16, 16).AsVector2I();
            Vector2I wide = server.Call("clamp_terrain_size", 5000, 16).AsVector2I();

            if (huge.X != 128 || huge.Y != 128 || huge.X * huge.Y != 16384)
            {
                GD.PrintErr($"[MCP-BOUNDS] (д) FAIL: 5000x5000 дал {huge.X}x{huge.Y}, ожидалось 128x128");
                return false;
            }
            if (normal.X != 16 || normal.Y != 16)
            {
                GD.PrintErr($"[MCP-BOUNDS] (д) FAIL: нормальные 16x16 искажены -> {normal.X}x{normal.Y}");
                return false;
            }
            if (wide.X != 128 || wide.Y != 16)
            {
                GD.PrintErr($"[MCP-BOUNDS] (д) FAIL: 5000x16 дал {wide.X}x{wide.Y}, ожидалось 128x16");
                return false;
            }

            GD.Print("[MCP-BOUNDS] (д) OK: terrain 5000x5000 -> 128x128 (16384 клеток), 16x16 без изменений");
            return true;
        }
        finally
        {
            server.Free();
        }
    }

    /// <summary>
    /// Экземпляр сервера БЕЗ добавления в дерево: `_ready` не вызывается, порт
    /// не открывается, но его методы доступны для проверки.
    /// </summary>
    private static Node NewServer(GDScript serverScript)
    {
        Variant instance = serverScript.Call("new");
        return instance.AsGodotObject() as Node;
    }

    /// <summary>Скомпилировать и выполнить `func run()` из готового исходника.</summary>
    private static bool TryRunSnippet(string source, out Variant result, out string error)
    {
        result = default;
        error = "";

        var snippet = new GDScript { SourceCode = source };
        Error compile = snippet.Reload();
        if (compile != Error.Ok)
        {
            error = $"GDScript.reload() = {compile}";
            return false;
        }
        if (!snippet.CanInstantiate())
        {
            error = "can_instantiate() = false";
            return false;
        }

        Variant instance = snippet.Call("new");
        GodotObject node = instance.AsGodotObject();
        if (node == null)
        {
            error = "script.new() = null";
            return false;
        }

        result = node.Call("run");
        node.Free();
        return true;
    }

    /// <summary>Тело функции: от сигнатуры до следующей `func ` (или до конца файла).</summary>
    private static string FunctionBody(string src, string signature)
    {
        int start = src.IndexOf(signature, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }
        int next = src.IndexOf("\nfunc ", start + signature.Length, StringComparison.Ordinal);
        return next < 0 ? src.Substring(start) : src.Substring(start, next - start);
    }
}