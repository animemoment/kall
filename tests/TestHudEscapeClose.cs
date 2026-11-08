using System;
using System.Text.RegularExpressions;
using Godot;

namespace Game.Tests;

/// <summary>
/// 28-08: Escape inside the farm window / work-zone window deselects the zone and marks the
/// key as handled, but the window itself stays open. Both windows have only a button named
/// "Escape" and no keyboard handler of their own, and the main HUD is hidden while they are
/// open — so the player is stuck with the mouse on the screen where Escape matters most.
///
/// The check is source based on purpose: the behaviour needs a full scene (HUD + farm window
/// + zone manager) and a real key event, which the headless test harness cannot deliver.
///
/// Run: godot --headless --path . --script res://tests/run_hud_escape_close.gd
/// </summary>
public partial class TestHudEscapeClose : RefCounted
{
    private const string HudPath = "res://src/ui/controllers/HUDController.cs";
    private const string Tag = "[HUD-ESC]";

    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print($"{Tag} Escape closes the farm / work-zone window (28-08)");
        GD.Print("==================================================");

        string src = Godot.FileAccess.GetFileAsString(HudPath);
        if (string.IsNullOrEmpty(src))
        {
            GD.PrintErr($"{Tag} FAIL: cannot read {HudPath}");
            return false;
        }

        bool ok = true;
        string escape = ExtractMethod(src, "public override void _UnhandledInput");
        ok &= Check("_UnhandledInput found", escape.Length > 0);
        if (escape.Length == 0)
            return false;

        // The farm branch must both deselect and close the window.
        ok &= Check("farm window is closed on Escape",
            Regex.IsMatch(escape, @"_gardenController\.Close\(\)"));
        ok &= Check("work-zone window is closed on Escape",
            Regex.IsMatch(escape, @"WorkZoneController\.Instance\.Close\(\)"));
        ok &= Check("zone deselection is kept (old behaviour preserved)",
            escape.Contains("ZoneManager.Instance.DeselectZone()"));
        ok &= Check("the key is still consumed after closing",
            escape.Contains("SetInputAsHandled()"));

        // The close calls must live inside the "window is open" branch, i.e. before the menu
        // section that resets the active tool. Otherwise Escape would close on every press.
        int openBranch = escape.IndexOf("_gardenController.IsOpen", StringComparison.Ordinal);
        int closeCall = escape.IndexOf("_gardenController.Close()", StringComparison.Ordinal);
        int menuSection = escape.IndexOf("CloseAllMenus()", StringComparison.Ordinal);
        ok &= Check("close is guarded by IsOpen",
            openBranch >= 0 && closeCall > openBranch);
        ok &= Check("close happens before the menu reset section",
            closeCall > 0 && menuSection > closeCall);

        // Regressions guard: the old shape (deselect only, no Close) must be gone.
        bool oldShape = Regex.IsMatch(escape,
            @"_gardenController\.IsOpen\)\s*\|\|\s*\n\s*\(WorkZoneController\.Instance != null && WorkZoneController\.Instance\.IsOpen\)\)\s*\n\s*\{\s*\n\s*ZoneManager\.Instance\.DeselectZone\(\);\s*\n\s*handled = true;");
        ok &= Check("old deselect-only branch is gone", !oldShape);

        GD.Print(ok ? $"{Tag} PASS" : $"{Tag} FAIL");
        return ok;
    }

    private bool Check(string what, bool condition)
    {
        if (condition)
        {
            GD.Print($"{Tag} OK: {what}");
            return true;
        }
        GD.PrintErr($"{Tag} FAIL: {what}");
        return false;
    }

    private static string ExtractMethod(string src, string signature)
    {
        int decl = src.IndexOf(signature + "(", StringComparison.Ordinal);
        int start = decl >= 0 ? decl : src.IndexOf(signature, StringComparison.Ordinal);
        if (start < 0) return string.Empty;
        int open = src.IndexOf('{', start);
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