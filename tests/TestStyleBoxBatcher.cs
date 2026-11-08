using Godot;
using Game.UI;
using System;

namespace Game.Tests;

/// <summary>
/// StyleBoxBatcher (F3 draw-call batching) must not change layout or lose styles.
///
/// Checks:
/// 1. Layout: sizes/positions of buttons, fields and the panel are identical with and
///    without the batcher (content margins carried over to StyleBoxEmpty).
/// 2. Capture: every batched control has an empty placeholder and the original style
///    is remembered for every button state.
/// 3. Refresh keeps the original style when nothing changed and picks up a new one.
/// 4. Freed controls are dropped without errors.
/// 5. The F3 panel font has subpixel positioning off (the cause of 150–900 draw calls).
///
/// Запуск: godot --headless --path . --script res://tests/run_stylebox_batcher.gd
/// </summary>
public partial class TestStyleBoxBatcher : RefCounted
{
    public bool Run(SceneTree tree)
    {
        GD.Print("==================================================");
        GD.Print("[SBB] StyleBoxBatcher: раскладка и стили без изменений");
        GD.Print("==================================================");
        try
        {
            bool ok = true;
            ok &= CheckLayoutAndCapture(tree);
            ok &= CheckRefreshAndFree(tree);
            ok &= CheckOverlayFont(tree);
            GD.Print(ok ? "[SBB] PASS" : "[SBB] FAIL");
            return ok;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SBB] FAIL: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static StyleBoxFlat Style(Color c, int margin) => new()
    {
        BgColor = c,
        ContentMarginLeft = margin,
        ContentMarginRight = margin,
        ContentMarginTop = margin / 2f,
        ContentMarginBottom = margin / 2f,
    };

    /// <summary>Panel → VBox → HBox with buttons + LineEdit, like the F3 panel.</summary>
    private static (PanelContainer panel, Button[] buttons, LineEdit edit) Build(Node host)
    {
        var panel = new PanelContainer { Position = new Vector2(16, 16) };
        panel.AddThemeStyleboxOverride("panel", Style(new Color(0.1f, 0.1f, 0.2f), 14));
        var root = new VBoxContainer();
        panel.AddChild(root);
        var row = new HBoxContainer();
        root.AddChild(row);
        var buttons = new Button[4];
        for (int i = 0; i < buttons.Length; i++)
        {
            buttons[i] = new Button { Text = "Button " + i, CustomMinimumSize = new Vector2(0, 26) };
            buttons[i].AddThemeStyleboxOverride("normal", Style(new Color(0.2f, 0.2f, 0.2f), 6 + i));
            buttons[i].AddThemeStyleboxOverride("hover", Style(new Color(0.3f, 0.3f, 0.3f), 6 + i));
            row.AddChild(buttons[i]);
        }
        var edit = new LineEdit { PlaceholderText = "filter", CustomMinimumSize = new Vector2(140, 26) };
        row.AddChild(edit);
        host.AddChild(panel);
        return (panel, buttons, edit);
    }

    private static string Snapshot(PanelContainer panel, Button[] buttons, LineEdit edit)
    {
        // Force layout now (headless: no frame needed).
        panel.ResetSize();
        var s = $"panel {panel.Size}";
        foreach (var b in buttons)
            s += $" | {b.Position} {b.Size}";
        s += $" | edit {edit.Position} {edit.Size}";
        return s;
    }

    private static bool CheckLayoutAndCapture(SceneTree tree)
    {
        var hostA = new Control();
        var hostB = new Control();
        tree.Root.AddChild(hostA);
        tree.Root.AddChild(hostB);

        var a = Build(hostA);
        var b = Build(hostB);
        var batchers = StyleBoxBatcher.Attach(b.panel, host: hostB);

        string sa = Snapshot(a.panel, a.buttons, a.edit);
        string sb = Snapshot(b.panel, b.buttons, b.edit);
        bool ok = true;
        if (sa != sb)
        {
            GD.PrintErr($"[SBB] FAIL: раскладка сдвинулась\n  без: {sa}\n  с  : {sb}");
            ok = false;
        }

        if (batchers.Count != 1 || batchers[0].Count != 1 + b.buttons.Length + 1)
        {
            GD.PrintErr($"[SBB] FAIL: ожидался 1 батчер на 6 контролов, получено {batchers.Count} / {(batchers.Count > 0 ? batchers[0].Count : -1)}");
            ok = false;
        }

        if (b.panel.GetThemeStylebox("panel") is not StyleBoxEmpty)
        {
            GD.PrintErr("[SBB] FAIL: панель всё ещё рисует свой фон сама");
            ok = false;
        }
        foreach (var btn in b.buttons)
        {
            foreach (var state in new[] { "normal", "hover", "pressed", "disabled", "hover_pressed" })
            {
                if (btn.GetThemeStylebox(state) is not StyleBoxEmpty)
                {
                    GD.PrintErr($"[SBB] FAIL: {btn.Text}.{state} не заменён на пустой");
                    ok = false;
                }
            }
        }
        if (batchers.Count == 1 && b.buttons[0].GetParent() != null && hostB.GetChild(0) != batchers[0])
        {
            GD.PrintErr("[SBB] FAIL: батчер должен быть первым ребёнком (рисуется под контролами)");
            ok = false;
        }

        GD.Print($"[SBB] раскладка: {(sa == sb ? "одинаковая" : "РАЗНАЯ")}, контролов в батчере: {(batchers.Count > 0 ? batchers[0].Count : 0)}");
        hostA.QueueFree();
        hostB.QueueFree();
        return ok;
    }

    private static bool CheckRefreshAndFree(SceneTree tree)
    {
        var host = new Control();
        tree.Root.AddChild(host);
        var built = Build(host);
        var batcher = StyleBoxBatcher.Attach(built.panel, host: host)[0];
        bool ok = true;

        // Refresh without changes: style must stay captured (placeholder is not "new").
        batcher.Refresh(built.buttons[0]);
        if (built.buttons[0].GetThemeStylebox("normal") is not StyleBoxEmpty)
        {
            GD.PrintErr("[SBB] FAIL: Refresh без изменений вернул кнопке собственный фон");
            ok = false;
        }

        // New style after attach → Refresh picks it up and empties it again.
        built.buttons[1].AddThemeStyleboxOverride("normal", Style(Colors.Red, 9));
        batcher.Refresh(built.buttons[1]);
        var after = built.buttons[1].GetThemeStylebox("normal");
        if (after is not StyleBoxEmpty || after.GetMargin(Side.Left) != 9)
        {
            GD.PrintErr($"[SBB] FAIL: Refresh не подхватил новый стиль (margin {after?.GetMargin(Side.Left)})");
            ok = false;
        }

        // Freed control: drawing must drop it, not crash.
        int before = batcher.Count;
        built.buttons[2].Free();
        batcher._Draw();
        if (batcher.Count != before - 1)
        {
            GD.PrintErr($"[SBB] FAIL: удалённая кнопка не выброшена ({before} → {batcher.Count})");
            ok = false;
        }

        host.QueueFree();
        return ok;
    }

    private static bool CheckOverlayFont(SceneTree tree)
    {
        var overlay = new PerformanceOverlay();
        tree.Root.AddChild(overlay);
        bool ok = true;

        var label = overlay.FindChildren("*", "RichTextLabel", true, false);
        if (label.Count == 0)
        {
            GD.PrintErr("[SBB] FAIL: в F3 нет RichTextLabel");
            overlay.QueueFree();
            return false;
        }
        var rtl = (RichTextLabel)label[0];
        Font normal = rtl.GetThemeFont("normal_font");
        Font bold = rtl.GetThemeFont("bold_font");
        if (normal is FontFile ff && ff.SubpixelPositioning != TextServer.SubpixelPositioning.Disabled)
        {
            GD.PrintErr($"[SBB] FAIL: шрифт F3 с subpixel={ff.SubpixelPositioning} (рвёт склейку букв)");
            ok = false;
        }
        if (bold != normal)
        {
            GD.PrintErr("[SBB] FAIL: [b] в F3 рисуется отдельным шрифтом");
            ok = false;
        }
        if (overlay.GetChild(0) is not StyleBoxBatcher)
        {
            GD.PrintErr("[SBB] FAIL: у F3 нет StyleBoxBatcher под панелью");
            ok = false;
        }
        GD.Print($"[SBB] F3: шрифт {normal?.GetType().Name}, батчер {(overlay.GetChild(0) is StyleBoxBatcher sbb ? sbb.Count : 0)} контролов");
        overlay.QueueFree();
        return ok;
    }
}
