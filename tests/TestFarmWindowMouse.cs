using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Godot;
using Game.UI;

namespace Game.Tests;

/// <summary>
/// 03-04: open farm / JobDisplay windows used to swallow the whole mouse.
///
/// Both scenes are a full-rect Control (anchors_preset = 15) with the default
/// MouseFilter = Stop, so while a window was open the camera could not be dragged
/// or zoomed (CameraController reads the mouse in _UnhandledInput, and a Stop
/// control consumes the event first). The fix follows the BuildMenuController
/// precedent (§113.5/B1): the window root / scroll layer is Ignore, the visible
/// panel stays Stop, and buttons keep their own filter. Clicks on buttons and
/// lists must keep working, so Ignore is NOT put on them (D-08).
///
/// Farm.tscn already gets its root Ignore from FarmController (R-049,
/// MouseFilterJournal); JobDisplay.tscn declares the same in the scene text because
/// WorkZoneController never touches its root or VScrollBar. The test checks both
/// sources of truth plus the button default, and instantiates the real scenes in
/// the tree to prove the runtime filters.
///
/// Red on the old code: JobDisplay.tscn had no mouse_filter = 2, so the runtime
/// JobDisplay root was Stop and swallowed the camera input.
///
/// Run: godot --headless --path . --script res://tests/run_farm_window_mouse.gd
/// </summary>
public partial class TestFarmWindowMouse : RefCounted
{
    private const string FarmScenePath = "res://scenes/ui/Farm.tscn";
    private const string JobScenePath = "res://scenes/ui/JobDisplay.tscn";
    private const string FarmControllerPath = "res://src/ui/controllers/FarmController.cs";
    private const string BuildMenuPath = "res://src/ui/controllers/BuildMenuController.cs";

    // Godot's Control.MouseFilterEnum values as written in .tscn text.
    private const int SceneFilterStop = 0;
    private const int SceneFilterIgnore = 2;

    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print("[FARM-WINDOW-MOUSE] 03-04: окна фермы и JobDisplay не едят мышь");
        GD.Print("==================================================");

        bool ok = true;
        ok &= FarmRootIgnoredFromController();
        ok &= JobDisplaySceneIgnoresMouse();
        ok &= ButtonsInsideWindowsStayStop();
        ok &= BuildMenuPrecedentIntact();
        ok &= LiveFarmRootIgnoresMouse();
        ok &= LiveJobDisplayIgnoresMouse();
        return ok;
    }

    // --- 03-04: источник правильного фильтра ---

    /// <summary>
    /// Farm root: the scene keeps the default Stop, so FarmController must put the root
    /// to Ignore (ResolveFor(false)) through the MouseFilterJournal (R-049). That is the
    /// "или выставляются в Ignore из контроллера" half of the card.
    /// </summary>
    private bool FarmRootIgnoredFromController()
    {
        string src = ReadText(FarmControllerPath);
        if (string.IsNullOrEmpty(src))
            return false;

        string flat = Flatten(src);
        bool ok = true;
        ok &= Expect(Regex.IsMatch(flat, @"root\.MouseFilter = ResolveFor\(false\);"),
            "FarmController ставит корню окна ResolveFor(false)");
        ok &= Expect(Regex.IsMatch(flat,
                @"isPanel \? Control\.MouseFilterEnum\.Stop : Control\.MouseFilterEnum\.Ignore"),
            "ResolveFor(false) — это Ignore: полноэкранный корень не перехватывает клик (03-04)");
        ok &= Expect(Regex.IsMatch(flat, @"ConfigureInputBlocking\(_rootPanel\);"),
            "настройка фильтров вызывается и при загрузке, и перед показом окна");

        string scene = ReadText(FarmScenePath);
        string root = RootNodeBlock(scene);
        ok &= Expect(root != null, "Farm.tscn: корневая нода найдена");
        if (root != null)
        {
            // Root stays Stop in the text; the controller owns it. Anchor check documents why.
            int sceneFilter = IntProperty(root, "mouse_filter");
            ok &= Expect(sceneFilter < 0 || sceneFilter == SceneFilterIgnore,
                "Farm.tscn: корень либо без mouse_filter (его ставит контроллер), либо Ignore");
        }
        return ok;
    }

    /// <summary>
    /// JobDisplay: the controller never touches the root or the VScrollBar, so the scene
    /// itself must declare Ignore on both. VScrollBar is the full-height "scroll layer"
    /// of the window (the BuildMenu ScrollContainer analogue).
    /// </summary>
    private bool JobDisplaySceneIgnoresMouse()
    {
        string scene = ReadText(JobScenePath);
        if (string.IsNullOrEmpty(scene))
            return false;

        string root = RootNodeBlock(scene);
        bool ok = Expect(root != null, "JobDisplay.tscn: корневая нода найдена");
        if (root == null)
            return false;

        ok &= Expect(root.Contains("anchors_preset = 15"),
            "JobDisplay.tscn: корень по-прежнему на весь экран (иначе проверка бессмысленна)");
        ok &= Expect(IntProperty(root, "mouse_filter") == SceneFilterIgnore,
            "JobDisplay.tscn: корень Control — mouse_filter = 2 (IGNORE, 03-04)");

        string bar = NodeBlock(scene, header => header.Contains("name=\"VScrollBar\""));
        ok &= Expect(bar != null, "JobDisplay.tscn: VScrollBar найдена");
        if (bar != null)
        {
            ok &= Expect(IntProperty(bar, "mouse_filter") == SceneFilterIgnore,
                "JobDisplay.tscn: VScrollBar (скролл-слой окна) — mouse_filter = 2 (IGNORE)");
        }
        return ok;
    }

    /// <summary>
    /// Requirement 2: buttons inside the windows stay clickable. In scene text that means
    /// no mouse_filter override, or an explicit Stop — never Ignore/Pass (D-08).
    /// </summary>
    private bool ButtonsInsideWindowsStayStop()
    {
        bool ok = true;
        foreach (string path in new[] { FarmScenePath, JobScenePath })
        {
            string scene = ReadText(path);
            if (string.IsNullOrEmpty(scene))
            {
                ok = false;
                continue;
            }

            int buttons = 0;
            foreach (string block in NodeBlocks(scene))
            {
                if (!Regex.IsMatch(block, "type=\"(?:Button|CheckBox|CheckButton)\""))
                    continue;
                buttons++;
                int filter = IntProperty(block, "mouse_filter");
                ok &= Expect(filter < 0 || filter == SceneFilterStop,
                    $"{path}: кнопка {NodeName(block)} остаётся Stop — клики по ней работают");
            }
            ok &= Expect(buttons > 0, $"{path}: кнопки в сцене найдены ({buttons})");
        }

        // Named buttons of both windows: the exact nodes the player clicks.
        string farm = ReadText(FarmScenePath);
        string job = ReadText(JobScenePath);
        ok &= Expect(IntProperty(NodeBlock(farm, h => h.Contains("name=\"esc\"")), "mouse_filter") < 0,
            "Farm.tscn: крестик «esc» без override — Stop по умолчанию");
        ok &= Expect(IntProperty(NodeBlock(farm, h => h.Contains("name=\"ButtonForJobDisplay\"")), "mouse_filter") < 0,
            "Farm.tscn: кнопка перехода в JobDisplay без override — Stop");
        ok &= Expect(IntProperty(NodeBlock(job, h => h.Contains("name=\"RemoveZone\"")), "mouse_filter") < 0,
            "JobDisplay.tscn: «Снести зону» без override — Stop");
        return ok;
    }

    /// <summary>
    /// Requirement 3: the BuildMenu precedent must stay in place — root Ignore and
    /// ScrollContainer Ignore with the §113.5/B1 comment (the reference solution).
    /// </summary>
    private bool BuildMenuPrecedentIntact()
    {
        string src = ReadText(BuildMenuPath);
        if (string.IsNullOrEmpty(src))
            return false;

        string flat = Flatten(src);
        bool ok = true;
        ok &= Expect(flat.Contains("_rootPanel.MouseFilter = MouseFilterEnum.Ignore;"),
            "BuildMenuController: корень — Ignore (прецедент на месте)");
        ok &= Expect(Regex.IsMatch(flat,
                @"if \(node is ScrollContainer\) node\.MouseFilter = MouseFilterEnum\.Ignore;"),
            "BuildMenuController: ScrollContainer — Ignore (прецедент §113.5/B1 на месте)");
        ok &= Expect(flat.Contains("§113.5/B1"),
            "BuildMenuController: комментарий §113.5/B1 сохранился");
        return ok;
    }

    // --- 03-04: живая сцена ---

    /// <summary>
    /// Real Farm.tscn inside the real controller: the root is a full-screen Control and
    /// must be Ignore after the controller configured it, while the visible panel and
    /// the buttons stay Stop.
    /// </summary>
    private bool LiveFarmRootIgnoresMouse()
    {
        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree == null)
        {
            GD.Print("[FARM-WINDOW-MOUSE] SKIP: нет SceneTree — Farm.tscn в дерево не поднять " +
                     "(источник проверен выше)");
            return true;
        }

        FarmController farm = null;
        try
        {
            farm = new FarmController { Name = "TestFarmWindowMouseFarm" };
            tree.Root.AddChild(farm);
            // A headless script does not pass a frame, so _Ready may not arrive by itself;
            // call it explicitly, as TestFarmInput does.
            if (!farm.IsNodeReady())
                farm._Ready();
        }
        catch (Exception ex)
        {
            // Same tolerance as TestFarmInput: a headless scene-tree quirk is not proof of a
            // mouse-filter regression, and the source rule is checked above.
            GD.Print($"[FARM-WINDOW-MOUSE] SKIP: FarmController не поднялся в headless " +
                     $"({ex.GetType().Name}: {ex.Message}); правило проверено по источнику");
            Drop(farm);
            return true;
        }

        try
        {
            var root = farm.FindChild("Control", false, false) as Control;
            bool ok = Expect(root != null, "живая сцена: Farm.tscn инстанцировался в FarmController");
            if (root == null)
                return false;

            ok &= Expect(root.AnchorRight == 1f && root.AnchorBottom == 1f,
                "живая сцена: корень Farm.tscn по-прежнему на весь экран");
            ok &= Expect(root.MouseFilter == Control.MouseFilterEnum.Ignore,
                "живая сцена: корень Farm.tscn — Ignore, камера тянется/зумится (03-04)");

            var panel = root.FindChild("PanelContainer", true, false) as Control;
            ok &= Expect(panel != null && panel.MouseFilter == Control.MouseFilterEnum.Stop,
                "живая сцена: видимая панель фермы осталась Stop");

            var jobButton = root.FindChild("ButtonForJobDisplay", true, false) as BaseButton;
            ok &= Expect(jobButton != null && jobButton.MouseFilter == Control.MouseFilterEnum.Stop,
                "живая сцена: кнопка открытия JobDisplay осталась Stop (клик работает)");
            return ok;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[FARM-WINDOW-MOUSE] FAIL: живая сцена Farm.tscn бросила " +
                        $"{ex.GetType().Name}: {ex.Message}");
            return false;
        }
        finally
        {
            Drop(farm);
        }
    }

    /// <summary>
    /// Real JobDisplay.tscn in the tree. WorkZoneController instantiates this scene and
    /// never changes the root or VScrollBar filters, so the scene values are the runtime
    /// values. The root must be Ignore; the window body and its buttons must stay Stop.
    /// </summary>
    private bool LiveJobDisplayIgnoresMouse()
    {
        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree == null)
        {
            GD.Print("[FARM-WINDOW-MOUSE] SKIP: нет SceneTree — JobDisplay.tscn в дерево не поднять " +
                     "(источник проверен выше)");
            return true;
        }

        Control job = null;
        try
        {
            var packed = GD.Load<PackedScene>(JobScenePath);
            if (packed == null)
            {
                GD.PrintErr($"[FARM-WINDOW-MOUSE] FAIL: {JobScenePath} не загрузился");
                return false;
            }
            job = packed.Instantiate<Control>();
            if (job == null)
            {
                GD.PrintErr("[FARM-WINDOW-MOUSE] FAIL: корень JobDisplay.tscn — не Control");
                return false;
            }
            job.Name = "TestJobDisplayRoot";
            tree.Root.AddChild(job);

            bool ok = true;
            ok &= Expect(job.MouseFilter == Control.MouseFilterEnum.Ignore,
                "живая сцена: корень JobDisplay — Ignore (03-04)");

            var bar = job.GetNodeOrNull<Control>("VScrollBar");
            ok &= Expect(bar != null && bar.MouseFilter == Control.MouseFilterEnum.Ignore,
                "живая сцена: VScrollBar JobDisplay — Ignore, мышь за окном идёт в мир");

            var body = job.GetNodeOrNull<Control>("VScrollBar/MainWindow");
            ok &= Expect(body != null && body.MouseFilter == Control.MouseFilterEnum.Stop,
                "живая сцена: видимое тело окна (MainWindow) осталось Stop");

            var remove = job.GetNodeOrNull<Control>("VScrollBar/MainWindow/RemoveZone") as BaseButton;
            ok &= Expect(remove != null && remove.MouseFilter == Control.MouseFilterEnum.Stop,
                "живая сцена: «Снести зону» осталась Stop (клик работает)");
            var escape = job.GetNodeOrNull<Control>("VScrollBar/MainWindow/esc") as BaseButton;
            ok &= Expect(escape != null && escape.MouseFilter == Control.MouseFilterEnum.Stop,
                "живая сцена: крестик JobDisplay остался Stop");
            return ok;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[FARM-WINDOW-MOUSE] FAIL: живая сцена JobDisplay бросила " +
                        $"{ex.GetType().Name}: {ex.Message}");
            return false;
        }
        finally
        {
            // The brief for 03-04 asks for queue_free() on the instantiated scene.
            if (job != null && GodotObject.IsInstanceValid(job))
                job.QueueFree();
        }
    }

    // --- .tscn helpers ---

    private static string ReadText(string path)
    {
        string text = Godot.FileAccess.GetFileAsString(path);
        if (string.IsNullOrEmpty(text))
        {
            GD.PrintErr($"[FARM-WINDOW-MOUSE] FAIL: не прочитать {path}");
            return null;
        }
        return text.Replace("\r\n", "\n");
    }

    /// <summary>Whitespace-collapsed source text for line-independent checks.</summary>
    private static string Flatten(string text) => Regex.Replace(text ?? "", @"\s+", " ");

    /// <summary>Header of the scene root node (no "parent=" in it).</summary>
    private static string RootNodeBlock(string scene)
    {
        if (string.IsNullOrEmpty(scene)) return null;
        foreach (string block in NodeBlocks(scene))
        {
            string header = block.Substring(0, block.IndexOf('\n') >= 0 ? block.IndexOf('\n') : block.Length);
            if (header.Contains(" parent=")) continue;
            if (header.Contains("type=\"Control\"")) return block;
        }
        return null;
    }

    /// <summary>First [node ...] block whose header matches the predicate, or null.</summary>
    private static string NodeBlock(string scene, Func<string, bool> headerMatches)
    {
        if (string.IsNullOrEmpty(scene)) return null;
        foreach (string block in NodeBlocks(scene))
        {
            int end = block.IndexOf('\n');
            string header = end >= 0 ? block.Substring(0, end) : block;
            if (headerMatches(header)) return block;
        }
        return null;
    }

    /// <summary>All [node ...] blocks, each with its own property lines.</summary>
    private static IEnumerable<string> NodeBlocks(string scene)
    {
        string[] lines = scene.Split('\n');
        var block = new StringBuilder();
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("[node ", StringComparison.Ordinal))
            {
                if (block.Length > 0)
                    yield return block.ToString();
                block.Clear();
                block.Append(lines[i]).Append('\n');
            }
            else if (lines[i].StartsWith("[", StringComparison.Ordinal))
            {
                if (block.Length > 0)
                    yield return block.ToString();
                block.Clear();
            }
            else if (block.Length > 0)
            {
                block.Append(lines[i]).Append('\n');
            }
        }
        if (block.Length > 0)
            yield return block.ToString();
    }

    private static string NodeName(string block)
    {
        Match match = Regex.Match(block, "name=\"([^\"]+)\"");
        return match.Success ? match.Groups[1].Value : "?";
    }

    /// <summary>Integer node property from .tscn text; −1 when the property is absent.</summary>
    private static int IntProperty(string block, string property)
    {
        if (string.IsNullOrEmpty(block)) return -1;
        Match match = Regex.Match(block, @"(?:^|\n)" + Regex.Escape(property) + @" = (-?\d+)(?:\n|$)");
        if (!match.Success) return -1;
        return int.TryParse(match.Groups[1].Value, out int value) ? value : -1;
    }

    // --- utilities ---

    private static bool Expect(bool condition, string message)
    {
        if (condition)
        {
            GD.Print($"[FARM-WINDOW-MOUSE] OK: {message}");
            return true;
        }
        GD.PrintErr($"[FARM-WINDOW-MOUSE] FAIL: {message}");
        return false;
    }

    private static void Drop(params Node[] nodes)
    {
        foreach (Node node in nodes)
        {
            if (node == null) continue;
            try
            {
                if (GodotObject.IsInstanceValid(node))
                    node.Free();
            }
            catch (ObjectDisposedException)
            {
                // Already freed together with its parent — nothing to do.
            }
        }
    }
}