using System;
using System.Reflection;
using System.Text.RegularExpressions;
using Godot;
using Game.UI;
using Game.UI.Tools;

namespace Game.Tests;

/// <summary>
/// 03-02 + 03-06: two "one Escape / one CheckMark" ownership bugs of the same node.
///
/// 03-02 — empty draft + CheckMark: the build window closed but the zone-build tool stayed
///   armed in PlayerInteractionManager, so the player kept drawing after the window was gone.
///   HUDController.ConfirmDraft handed the tool back only when Commit() succeeded.
/// 03-06 — Escape with the build window open: PlayerInteractionManager resets the active tool
///   in _UnhandledInput and marks the key as handled there. Godot delivers _UnhandledInput
///   after _UnhandledKeyInput, and PIM is registered later (created at runtime by Main), so it
///   was called first: the first Escape only disarmed the tool, the window needed a second one.
///
/// The fix keeps the order in the controllers: BuildMenuController closes itself from
/// _UnhandledKeyInput (before PIM's stage), HUDController.ConfirmDraft always hands the default
/// tool back. Both are checked on the real hud.tscn (headless) plus a source guard for the
/// wiring that the live test cannot observe (which input stage a handler sits in).
///
/// Red on the old code: (1) the tool stays active after the empty CheckMark, (2) the window
/// stays open after one Escape.
///
/// Run: godot --headless --path . --script res://tests/run_window_close_order.gd
/// </summary>
public partial class TestWindowCloseOrder : RefCounted
{
    private const string HudPath = "res://src/ui/controllers/HUDController.cs";
    private const string BuildMenuPath = "res://src/ui/controllers/BuildMenuController.cs";
    private const string InteractionPath = "res://src/ui/PlayerInteractionManager.cs";
    private const string HudScenePath = "res://scenes/ui/hud.tscn";
    private const string Tag = "[WINDOW-CLOSE]";

    private SceneTree _tree;
    private HUDController _hud;
    private PlayerInteractionManager _pim;
    private BuildMenuController _buildMenu;
    private FarmController _farm;
    private TileMapLayer _wallLayer;
    private bool _live;

    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print($"{Tag} 03-02 empty CheckMark / 03-06 one Escape closes the top window");
        GD.Print("==================================================");

        bool ok = true;
        ok &= SourceWiring();
        ok &= TrySetupLiveUi();
        if (_live)
        {
            ok &= EmptyCheckMarkResetsTool();
            ok &= FirstEscapeClosesBuildWindow();
            ok &= EscapeWithoutWindowResetsTool();
            ok &= FarmWindowStillClosesOnEscape();
        }

        Drop();
        GD.Print(ok ? $"{Tag} PASS" : $"{Tag} FAIL");
        return ok;
    }

    // --- 03-02: empty CheckMark ---

    /// <summary>
    /// Empty draft + CheckMark: the window closes AND the tool goes back to default. The draft
    /// is a real ZoneDraftTool without cells, so Commit() returns false exactly as in the game
    /// when the player presses CheckMark without drawing.
    /// </summary>
    private bool EmptyCheckMarkResetsTool()
    {
        GD.Print($"{Tag} --- 03-02: CheckMark with an empty draft ---");
        if (!OpenBuildWindow("farming"))
            return false;

        var draft = new ZoneDraftTool(null, null, null, "farming");
        _pim.SetTool(draft);
        SetPrivate(_hud, "_draftTool", draft);

        bool ok = true;
        ok &= Expect(!_pim.IsDefaultTool, "before: the zone-build tool is active");

        var checkMark = _buildMenu.FindChild("CheckMark", true, false) as BaseButton;
        ok &= Expect(checkMark != null, "Build.tscn: CheckMark button is wired");
        if (checkMark == null)
            return false;

        // The real click path: CheckMark.Pressed -> BuildMenuController.OnCheckMarkPressed
        // -> HUDController.ConfirmDraft.
        checkMark.EmitSignal("pressed");

        ok &= Expect(_buildMenu.IsOpen == false, "empty CheckMark still closes the window");
        ok &= Expect(_pim.IsDefaultTool, "empty CheckMark hands the default tool back (tool is null/None)");
        ok &= Expect(!draft.HasDraft, "the cancelled draft holds no cells to keep drawing with");
        return ok;
    }

    // --- 03-06: first Escape with the build window open ---

    /// <summary>
    /// One Escape press with the build window open. The window handles the key in
    /// _UnhandledKeyInput; PIM's _UnhandledInput (which would only reset the tool) is never
    /// needed. PIM is deliberately blind here (no WallLayer), so a closed window proves the key
    /// was consumed by the window and not by the tool-reset stage.
    /// </summary>
    private bool FirstEscapeClosesBuildWindow()
    {
        GD.Print($"{Tag} --- 03-06: first Escape with the build window open ---");
        if (!OpenBuildWindow("warehouse"))
            return false;

        var draft = new ZoneDraftTool(null, null, null, "warehouse");
        _pim.SetTool(draft);
        SetPrivate(_hud, "_draftTool", draft);
        _pim.WallLayer = null;

        bool ok = true;
        ok &= Expect(!_pim.IsDefaultTool, "before: the zone-build tool is active, Escape is contested");

        _buildMenu._UnhandledKeyInput(EscapeEvent());

        ok &= Expect(_buildMenu.IsOpen == false, "one Escape closes the build window");
        ok &= Expect(_pim.IsDefaultTool,
            "the close cancelled the draft and restored the default tool (PIM could not do it: no WallLayer)");
        return ok;
    }

    // --- 03-06: Escape without windows keeps the old behaviour ---

    /// <summary>
    /// No window open: Escape still reaches PIM and resets the active tool, exactly as before
    /// the fix (the crosshair/selection behaviour must not change).
    /// </summary>
    private bool EscapeWithoutWindowResetsTool()
    {
        GD.Print($"{Tag} --- 03-06: Escape without an open window ---");
        bool ok = Expect(!_buildMenu.IsOpen, "no window is open at this point");
        if (!ok)
            return false;

        var tool = new RecordingTool();
        _pim.WallLayer = _wallLayer;
        _pim.SetTool(tool);

        var ev = EscapeEvent();
        // Engine order for one press: the window's key stage first, PIM's unhandled stage next.
        _buildMenu._UnhandledKeyInput(ev);
        ok &= Expect(!_buildMenu.IsOpen, "the closed window ignored Escape");
        _pim._UnhandledInput(ev);

        ok &= Expect(_pim.IsDefaultTool, "Escape without a window still resets the tool");
        ok &= Expect(tool.CancelledFlag, "the replaced tool was cancelled, not just dropped");
        return ok;
    }

    // --- regression guard: farm window (R-048) ---

    /// <summary>
    /// The farm-window fix must stay: Escape closes the farm window and deselects the zone.
    /// The window is made visible the same way OnZoneSelected does it (root panel Visible), so
    /// the test checks the HUD branch instead of the whole zone/simulation setup.
    /// </summary>
    private bool FarmWindowStillClosesOnEscape()
    {
        GD.Print($"{Tag} --- regression: farm window closes on Escape (R-048) ---");
        // FarmController keeps the instantiated Farm.tscn root plus its own tooltip; the
        // window root is the Control that owns the scene's "esc" button.
        Control farmRoot = null;
        for (int i = 0; i < _farm.GetChildCount() && farmRoot == null; i++)
        {
            if (_farm.GetChild(i) is Control candidate && candidate.FindChild("esc", true, false) != null)
                farmRoot = candidate;
        }

        bool ok = Expect(farmRoot != null, "Farm.tscn: window root panel found");
        if (farmRoot == null)
            return false;

        farmRoot.Visible = true;
        ok &= Expect(_farm.IsOpen, "farm window is open");
        // Same state as the real farm flow: the zone window opens from the default tool, so
        // PIM does not take Escape and the HUD branch is the one that closes the window.
        ok &= Expect(_pim.IsDefaultTool, "no tool is armed, so PIM does not consume Escape");

        _hud._UnhandledInput(EscapeEvent());

        ok &= Expect(_farm.IsOpen == false, "Escape still closes the farm window");
        return ok;
    }

    // --- source guard (input stage / gating that the live test cannot see) ---

    private bool SourceWiring()
    {
        string hud = ReadText(HudPath);
        string build = ReadText(BuildMenuPath);
        string interaction = ReadText(InteractionPath);
        bool ok = Expect(!string.IsNullOrEmpty(hud) && !string.IsNullOrEmpty(build)
                         && !string.IsNullOrEmpty(interaction),
            "controllers are readable");

        // 03-02: ConfirmDraft must not gate the hand-back on a successful commit.
        string confirm = MethodBody(hud, @"public void ConfirmDraft\(\)");
        ok &= Expect(confirm.Length > 0, "HUDController.ConfirmDraft found");
        ok &= Expect(confirm.Contains("_draftTool?.Cancel()"),
            "03-02: an uncommitted (empty) draft cancels its preview");
        ok &= Expect(!confirm.Contains("if (committed)"),
            "03-02: the tool hand-back is no longer gated on a successful commit");
        ok &= Expect(confirm.Contains("PlayerInteractionManager.Instance?.ResetToDefault()"),
            "03-02: ConfirmDraft always returns the default tool");

        string onCheckMark = MethodBody(build, @"private void OnCheckMarkPressed\(\)");
        ok &= Expect(onCheckMark.Contains("ConfirmDraft()"),
            "03-02: CheckMark is still wired to ConfirmDraft");

        // 03-06: the window takes Escape one input stage earlier than PIM.
        string early = MethodBody(build, @"public override void _UnhandledKeyInput");
        ok &= Expect(early.Length > 0, "03-06: BuildMenuController handles a key stage before _UnhandledInput");
        ok &= Expect(early.Contains("CloseOnEscape()"),
            "03-06: that stage closes the window through the same path as the esc button");
        ok &= Expect(early.Contains("SetInputAsHandled()"),
            "03-06: the window consumes Escape so it cannot reach the tool reset");

        string pim = MethodBody(interaction, @"public override void _UnhandledInput");
        ok &= Expect(pim.Contains("Key.Escape") && pim.Contains("ResetToDefault()"),
            "03-06: PIM still resets the tool on Escape (but in a later stage)");

        // R-048 regression: the farm/work-zone branch stays in the HUD.
        string hudUnhandled = MethodBody(hud, @"public override void _UnhandledInput");
        ok &= Expect(hudUnhandled.Contains("_gardenController.Close()"),
            "R-048: Escape still closes the farm window in the HUD");
        ok &= Expect(hudUnhandled.Contains("WorkZoneController.Instance.Close()"),
            "R-048: Escape still closes the work-zone window in the HUD");
        return ok;
    }

    // --- live HUD + PIM ---

    private bool TrySetupLiveUi()
    {
        _tree = Engine.GetMainLoop() as SceneTree;
        if (_tree == null)
            return Skip("no SceneTree — live HUD cannot be built, source wiring checked above");

        try
        {
            var packed = GD.Load<PackedScene>(HudScenePath);
            _hud = packed?.Instantiate<HUDController>();
            if (_hud == null)
                return Skip($"{HudScenePath} did not instantiate (source wiring checked above)");
            _tree.Root.AddChild(_hud);
            if (!_hud.IsNodeReady())
                _hud._Ready();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"{Tag} FAIL: hud.tscn failed to build: {ex.GetType().Name}: {ex.Message}");
            Drop();
            return false;
        }

        _wallLayer = new TileMapLayer();
        _pim = new PlayerInteractionManager { Name = "PlayerInteractionManager" };
        _tree.Root.AddChild(_pim);
        _pim.Initialize(_wallLayer, new SelectionBox(), null);

        _buildMenu = _hud.FindChild("BuildMenuController", true, false) as BuildMenuController;
        _farm = _hud.FindChild("GardenController", true, false) as FarmController;
        if (_buildMenu == null || _farm == null || _pim.DefaultTool == null || !_buildMenu.IsInsideTree())
        {
            GD.PrintErr($"{Tag} FAIL: live HUD has no BuildMenuController/GardenController/default tool");
            Drop();
            return false;
        }

        _live = true;
        return true;
    }

    private bool OpenBuildWindow(string zoneId)
    {
        _buildMenu.Open(zoneId, "Test");
        return Expect(_buildMenu.IsOpen, $"build window opened for '{zoneId}'");
    }

    // --- helpers ---

    private static InputEventKey EscapeEvent() => new()
    {
        Keycode = Key.Escape,
        PhysicalKeycode = Key.Escape,
        Pressed = true
    };

    /// <summary>Body of a method: from the signature to the first class-level "    }" line.</summary>
    private static string MethodBody(string source, string signaturePattern)
    {
        Match match = Regex.Match(source, signaturePattern + @"[\s\S]*?\n    \}");
        return match.Success ? match.Value : string.Empty;
    }

    private static string ReadText(string path)
    {
        string text = Godot.FileAccess.GetFileAsString(path);
        return text ?? string.Empty;
    }

    private static void SetPrivate(object target, string name, object value)
    {
        FieldInfo field = target?.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
        field?.SetValue(target, value);
    }

    private bool Expect(bool condition, string message)
    {
        if (condition)
        {
            GD.Print($"{Tag} OK: {message}");
            return true;
        }
        GD.PrintErr($"{Tag} FAIL: {message}");
        return false;
    }

    private bool Skip(string message)
    {
        GD.Print($"{Tag} SKIP: {message}");
        return true;
    }

    private void Drop()
    {
        _buildMenu = null;
        _farm = null;
        // HUD first: its _ExitTree looks at PlayerInteractionManager.Instance and must not see
        // an already freed manager (children unsubscribe from their own signals on the way out).
        foreach (Node node in new Node[] { _hud, _pim })
        {
            if (node == null)
                continue;
            try
            {
                if (GodotObject.IsInstanceValid(node))
                    node.Free();
            }
            catch (ObjectDisposedException)
            {
                // Already freed together with the parent.
            }
        }
        _pim = null;
        _hud = null;
        _live = false;
    }

    /// <summary>Minimal ITool that records Cancel(): proves Escape really replaced the tool.</summary>
    private sealed class RecordingTool : ITool
    {
        public bool CancelledFlag { get; private set; }

        public void OnHover(Vector2I tilePos, Vector2 worldPos) { }
        public void OnClick(Vector2I tilePos, Vector2 worldPos, bool isLeftClick) { }
        public void OnDrag(Vector2I startTile, Vector2I currentTile, Vector2 currentWorldPos) { }
        public void OnRelease(Vector2I startTile, Vector2I endTile, Vector2 worldPos, bool isLeftClick) { }
        public void Cancel() => CancelledFlag = true;
    }
}