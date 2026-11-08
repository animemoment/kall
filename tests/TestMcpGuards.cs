using System;
using Godot;

namespace Game.Tests;

/// <summary>
/// 29-01 / 29-07: деструктивные команды MCP-сервера не должны убивать игру.
///
/// Было:
///  * `remove_node` проверял только «нода существует» и делал `queue_free()` на
///    что угодно — включая `/root/Node2D` (корень Main.tscn; после этого
///    `Main._Process` не вызывается и симуляция стоит) и `/root/Node2D/MapRenderer`
///    (пропадает рендер суши, а `Main._mapRenderer` остаётся ссылкой на
///    освобождённый объект);
///  * `process_mode {mode:"disabled"}` ставился на любую ноду, включая `/root` и
///    `/root/Node2D`: дети в `PROCESS_MODE_INHERIT` наследуют DISABLED и встаёт
///    всё дерево.
/// Сервер жив в обоих случаях (он autoload с `PROCESS_MODE_ALWAYS`) и отвечал
/// `success`, поэтому снаружи это выглядело как зависание игры без причины.
///
/// Проверяем по тексту сервера, а не запуском: поднять сервер в тесте — значит
/// открыть TCP-порт, а гейтинг портов уже покрыт `tests/run_debug_ports.gd`
/// (этот тест его контракт только сторожит, см. (е)).
///
/// Запуск: godot --headless --path . --script res://tests/run_mcp_guards.gd
/// </summary>
public partial class TestMcpGuards : RefCounted
{
    private const string ServerPath = "res://autoload/mcp_interaction_server.gd";
    private const string GuardCall = "_protected_node_reason(";

    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print("[MCP-GUARD] Деструктивные команды не убивают игру (29-01, 29-07)");
        GD.Print("==================================================");

        if (!Godot.FileAccess.FileExists(ServerPath))
        {
            GD.PrintErr($"[MCP-GUARD] нет {ServerPath}");
            return false;
        }

        string src = Godot.FileAccess.GetFileAsString(ServerPath);
        if (string.IsNullOrWhiteSpace(src))
        {
            GD.PrintErr($"[MCP-GUARD] {ServerPath} пустой");
            return false;
        }

        bool ok = true;
        ok &= RemoveNodeGuarded(src);
        ok &= ProcessModeGuarded(src);
        ok &= HelperHasHardGuards(src);
        ok &= AutoloadDetectedFromProjectSettings(src);
        ok &= SpawnedNodesStayRemovable(src);
        ok &= PortGateStillBeforeListen(src);

        GD.Print(ok ? "[MCP-GUARD] PASS" : "[MCP-GUARD] FAIL");
        return ok;
    }

    /// <summary>(а) `remove_node`: причина отказа считается ДО `queue_free()`, отказ возвращает управление.</summary>
    private bool RemoveNodeGuarded(string src)
    {
        string body = FunctionBody(src, "func _cmd_remove_node(");
        if (body == null)
        {
            GD.PrintErr("[MCP-GUARD] (а) FAIL: нет func _cmd_remove_node");
            return false;
        }

        int guard = body.IndexOf(GuardCall, StringComparison.Ordinal);
        int free = body.IndexOf("queue_free()", StringComparison.Ordinal);
        if (guard < 0)
        {
            GD.PrintErr("[MCP-GUARD] (а) FAIL: remove_node не зовёт _protected_node_reason");
            return false;
        }
        if (free < 0)
        {
            GD.PrintErr("[MCP-GUARD] (а) FAIL: remove_node больше не удаляет ноды вообще (нет queue_free)");
            return false;
        }
        if (guard > free)
        {
            GD.PrintErr("[MCP-GUARD] (а) FAIL: проверка стоит ПОСЛЕ queue_free — нода уже удалена");
            return false;
        }

        string between = body.Substring(guard, free - guard);
        if (between.IndexOf("return", StringComparison.Ordinal) < 0)
        {
            GD.PrintErr("[MCP-GUARD] (а) FAIL: после отказа нет return — управление доходит до queue_free");
            return false;
        }
        if (!between.Contains("\"success\": false"))
        {
            GD.PrintErr("[MCP-GUARD] (а) FAIL: отказ не помечен \"success\": false");
            return false;
        }
        if (!body.Contains("params.get(\"force\""))
        {
            GD.PrintErr("[MCP-GUARD] (а) FAIL: нет осознанного обхода force — возможности урезаны");
            return false;
        }

        GD.Print("[MCP-GUARD] (а) OK: remove_node проверяет ноду до queue_free и отказывает с причиной");
        return true;
    }

    /// <summary>
    /// (б) `process_mode`: проверка только для `disabled` (остальные режимы остаются
    /// доступными) и строго до записи `node.process_mode`.
    /// </summary>
    private bool ProcessModeGuarded(string src)
    {
        string body = FunctionBody(src, "func _cmd_process_mode(");
        if (body == null)
        {
            GD.PrintErr("[MCP-GUARD] (б) FAIL: нет func _cmd_process_mode");
            return false;
        }

        int disabled = body.IndexOf("if mode_val == Node.PROCESS_MODE_DISABLED:", StringComparison.Ordinal);
        if (disabled < 0)
        {
            GD.PrintErr("[MCP-GUARD] (б) FAIL: нет ветки `if mode_val == Node.PROCESS_MODE_DISABLED:`");
            return false;
        }

        int guard = body.IndexOf(GuardCall, StringComparison.Ordinal);
        int assign = body.IndexOf("node.process_mode = mode_val", StringComparison.Ordinal);
        if (guard < 0)
        {
            GD.PrintErr("[MCP-GUARD] (б) FAIL: process_mode не зовёт _protected_node_reason");
            return false;
        }
        if (assign < 0)
        {
            GD.PrintErr("[MCP-GUARD] (б) FAIL: process_mode больше не применяется (нет записи режима)");
            return false;
        }
        if (guard < disabled)
        {
            GD.PrintErr("[MCP-GUARD] (б) FAIL: проверка стоит до ветки disabled — режутся и безопасные режимы");
            return false;
        }
        if (guard > assign)
        {
            GD.PrintErr("[MCP-GUARD] (б) FAIL: проверка стоит ПОСЛЕ записи process_mode");
            return false;
        }

        string between = body.Substring(guard, assign - guard);
        if (between.IndexOf("return", StringComparison.Ordinal) < 0)
        {
            GD.PrintErr("[MCP-GUARD] (б) FAIL: после отказа нет return");
            return false;
        }
        if (!between.Contains("\"success\": false"))
        {
            GD.PrintErr("[MCP-GUARD] (б) FAIL: отказ не помечен \"success\": false");
            return false;
        }
        if (!body.Contains("params.get(\"force\""))
        {
            GD.PrintErr("[MCP-GUARD] (б) FAIL: нет осознанного обхода force — возможности урезаны");
            return false;
        }

        GD.Print("[MCP-GUARD] (б) OK: process_mode гасит только disabled и проверяет ноду до записи");
        return true;
    }

    /// <summary>
    /// (в) Общая проверка закрывает корень дерева, автолоады, корень текущей сцены,
    /// уже удаляемую ноду и явную метку; `force` стоит ПОСЛЕ жёстких проверок,
    /// то есть не снимает их.
    /// </summary>
    private bool HelperHasHardGuards(string src)
    {
        string body = FunctionBody(src, "func _protected_node_reason(");
        if (body == null)
        {
            GD.PrintErr("[MCP-GUARD] (в) FAIL: нет func _protected_node_reason");
            return false;
        }

        (string Token, string What)[] hard =
        {
            ("node == self", "сам MCP-сервер"),
            ("node == tree.root", "/root (корень SceneTree)"),
            ("_is_autoload_node(node)", "автолоад"),
            ("is_queued_for_deletion()", "уже удаляемая нода"),
            ("node.is_ancestor_of(scene_root)", "предок текущей сцены"),
            ("PROTECTED_GROUP", "явная метка/группа"),
        };

        int latest = -1;
        foreach (var entry in hard)
        {
            int idx = body.IndexOf(entry.Token, StringComparison.Ordinal);
            if (idx < 0)
            {
                GD.PrintErr($"[MCP-GUARD] (в) FAIL: нет защиты «{entry.What}» ({entry.Token})");
                return false;
            }
            latest = Math.Max(latest, idx);
        }

        // Корень текущей сцены обязан быть защищён жёстко (не только мягко).
        int sceneRoot = body.IndexOf("if node == scene_root:", StringComparison.Ordinal);
        if (sceneRoot < 0)
        {
            GD.PrintErr("[MCP-GUARD] (в) FAIL: корень текущей сцены не защищён");
            return false;
        }
        latest = Math.Max(latest, sceneRoot);

        int force = body.IndexOf("if force:", StringComparison.Ordinal);
        if (force < 0)
        {
            GD.PrintErr("[MCP-GUARD] (в) FAIL: нет ветки `if force:` — обхода не существует");
            return false;
        }
        if (force < latest)
        {
            GD.PrintErr("[MCP-GUARD] (в) FAIL: `if force:` стоит ДО жёстких проверок — force снимает защиту /root");
            return false;
        }

        // Мягкая защита: ноды текущей сцены (MapRenderer, HUD, менеджеры).
        int sceneChild = body.IndexOf("scene_root.is_ancestor_of(node)", StringComparison.Ordinal);
        if (sceneChild < 0 || sceneChild < force)
        {
            GD.PrintErr("[MCP-GUARD] (в) FAIL: нет мягкой защиты нод текущей сцены (MapRenderer и др.) после force");
            return false;
        }

        GD.Print("[MCP-GUARD] (в) OK: /root, автолоады и корень сцены защищены жёстко, force их не снимает");
        return true;
    }

    /// <summary>(г) Автолоады берутся из project.godot, а не из захардкоженного списка имён.</summary>
    private bool AutoloadDetectedFromProjectSettings(string src)
    {
        string body = FunctionBody(src, "func _is_autoload_node(");
        if (body == null)
        {
            GD.PrintErr("[MCP-GUARD] (г) FAIL: нет func _is_autoload_node");
            return false;
        }
        if (!body.Contains("ProjectSettings.has_setting(\"autoload/"))
        {
            GD.PrintErr("[MCP-GUARD] (г) FAIL: автолоады не определяются через ProjectSettings[\"autoload/...\"]");
            return false;
        }
        if (!body.Contains("get_parent() != tree.root"))
        {
            GD.PrintErr("[MCP-GUARD] (г) FAIL: нет проверки «прямой ребёнок /root»");
            return false;
        }

        GD.Print("[MCP-GUARD] (г) OK: автолоады определяются по project.godot");
        return true;
    }

    /// <summary>
    /// (д) Ноды, созданные самим сервером, остаются удаляемыми и внутри сцены:
    /// `spawn_node` / `instantiate_scene` ставят метку, проверка её уважает.
    /// Это и есть «без урезания возможностей».
    /// </summary>
    private bool SpawnedNodesStayRemovable(string src)
    {
        string owned = FunctionBody(src, "func _is_mcp_spawned(");
        if (owned == null || !owned.Contains("has_meta(MCP_SPAWNED_META)"))
        {
            GD.PrintErr("[MCP-GUARD] (д) FAIL: нет func _is_mcp_spawned / проверки метки");
            return false;
        }

        string guard = FunctionBody(src, "func _protected_node_reason(");
        if (guard == null || !guard.Contains("_is_mcp_spawned(node)"))
        {
            GD.PrintErr("[MCP-GUARD] (д) FAIL: мягкая защита не исключает ноды, созданные сервером");
            return false;
        }

        foreach (string signature in new[] { "func _cmd_spawn_node(", "func _cmd_instantiate_scene(" })
        {
            string body = FunctionBody(src, signature);
            if (body == null || !body.Contains("set_meta(MCP_SPAWNED_META, true)"))
            {
                GD.PrintErr($"[MCP-GUARD] (д) FAIL: {signature.TrimEnd('(')} не помечает свою ноду");
                return false;
            }
        }

        GD.Print("[MCP-GUARD] (д) OK: ноды, созданные сервером, по-прежнему удаляются");
        return true;
    }

    /// <summary>
    /// (е) Гейт портов не сдвинут: `OS.is_debug_build()` по-прежнему до `_server.listen(`.
    /// Это контракт run_debug_ports.gd (25-01), правка 29-01 не должна его задеть.
    /// </summary>
    private bool PortGateStillBeforeListen(string src)
    {
        int gate = src.IndexOf("OS.is_debug_build()", StringComparison.Ordinal);
        int listen = src.IndexOf("_server.listen(", StringComparison.Ordinal);
        if (gate < 0 || listen < 0 || gate > listen)
        {
            GD.PrintErr("[MCP-GUARD] (е) FAIL: гейт debug-сборки не стоит перед listen — сломан run_debug_ports");
            return false;
        }

        GD.Print("[MCP-GUARD] (е) OK: гейт debug-сборки по-прежнему до listen");
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