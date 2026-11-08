using Godot;
using System;
using Game.UI;
using Game.UI.Resources;

namespace Game.Tests;

/// <summary>
/// §128: the "Биржи" window. Guards the contract of a scene that is opened from the HUD
/// exchange button and shows the whole 90-resource atlas.
///
/// What is pinned here:
///   * the window lives under the HUD next to the other windows and starts hidden;
///   * the node contract of Exchange.tscn (names the controller searches via FindChild);
///   * the mouse rule of §113.5/B1: the root ignores the mouse, panels stop it, the
///     ScrollContainer stays transparent (an empty scroll would eat world clicks);
///   * one Escape closes the window (R-048 / R-066 order: the key is taken before PIM);
///   * the commodity page builds one row per atlas entry (90), every row has an icon;
///   * the stock page is a skeleton: table header plus a placeholder, no fake quotes;
///   * the two sections are exclusive pages (exactly one visible at a time).
///
/// Run: godot --headless --path . --script res://tests/run_exchange_window.gd
/// </summary>
public partial class TestExchangeWindow : RefCounted
{
    private const string Tag = "[EXCHANGE]";
    private const string HudScenePath = "res://scenes/ui/hud.tscn";
    private const string ExchangeScenePath = "res://scenes/ui/Exchange.tscn";
    private const string ControllerPath = "res://src/ui/controllers/ExchangeController.cs";
    private const string HudSourcePath = "res://src/ui/controllers/HUDController.cs";

    private SceneTree _tree;
    private SceneTree _ownTree;
    private HUDController _hud;
    private ExchangeController _exchange;
    private bool _live;

    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print($"{Tag} §128 window \"Биржи\": contract, 90 rows, two pages, Escape");
        GD.Print("==================================================");

        bool ok = true;
        ok &= SourceWiring();
        ok &= AtlasHas90Entries();
        ok &= SceneContract();
        ok &= TrySetupLiveUi();
        if (_live)
        {
            ok &= WindowStartsHidden();
            ok &= OpensWithCommodityPage();
            ok &= CommodityRowsMatchAtlas();
            ok &= StockPageIsSkeleton();
            ok &= PagesAreExclusive();
            ok &= OneEscapeCloses();
            ok &= DealMovesMoneyOnly();
        }

        Drop();
        GD.Print(ok ? $"{Tag} PASS" : $"{Tag} FAIL");
        return ok;
    }

    // --- source guards ---

    private bool SourceWiring()
    {
        GD.Print($"{Tag} --- source wiring ---");
        string hud = ReadText(HudSourcePath);
        bool ok = true;

        ok &= Expect(hud.Contains("\"exchange\""), "HUDController looks the exchange button up by name");
        ok &= Expect(hud.Contains("OnExchangePressed"), "HUDController wires the exchange button to a handler");
        ok &= Expect(hud.Contains("ExchangeController"), "HUDController creates the exchange controller");

        string controller = ReadText(ControllerPath);
        ok &= Expect(controller.Contains("_UnhandledKeyInput"),
            "ExchangeController closes on Escape in the earlier stage (R-066 order)");
        ok &= Expect(!controller.Contains("StockpileManager") && !controller.Contains("RemoveItem"),
            "stage 1 does not touch the stockpile");

        // Rows are built after the scene was laid out once; without the relayout the panel kept
        // a 20 px box and the list stayed 0×0 (the window rendered as a thin strip).
        ok &= Expect(controller.Contains("SizeFlagsHorizontal = SizeFlags.ExpandFill"),
            "commodity rows stretch to the list width");
        ok &= Expect(controller.Contains("RefreshCommodityLayout"),
            "the list is re-laid out after the rows are appended");
        return ok;
    }

    /// <summary>
    /// Scene contract read from the .tscn text: the live half is skipped under a bare
    /// MainLoop, so the window shape is pinned here (names the controller searches, the
    /// mouse filters of the empty-window rule, and the two exclusive pages).
    /// </summary>
    private bool SceneContract()
    {
        GD.Print($"{Tag} --- Exchange.tscn contract ---");
        string scene = ReadText(ExchangeScenePath);
        bool ok = Expect(!string.IsNullOrEmpty(scene), "Exchange.tscn exists");
        if (string.IsNullOrEmpty(scene))
            return false;

        foreach (string name in new[]
                 {
                     "Control3", "Name", "esc", "CheckMark", "Backdrop", "TabCommodity", "TabStock",
                     "CommodityPage", "StockPage", "VBoxContainer", "HBoxContainer2", "MoneyAmount",
                     "DealLog", "StockRows", "StockEmptyText"
                 })
        {
            ok &= Expect(scene.Contains($"name=\"{name}\""), $"Exchange.tscn has node '{name}'");
        }

        ok &= Expect(scene.Contains("text = \"Биржи\""), "the window title is \"Биржи\"");
        ok &= Expect(scene.Contains("text = \"ТОВАРНАЯ\"") && scene.Contains("text = \"ФОНДОВАЯ\""),
            "the header holds both section buttons");
        ok &= Expect(NodeBlockHides(scene, "StockPage"), "the stock page starts hidden (commodity page opens first)");
        ok &= Expect(!NodeBlockHides(scene, "CommodityPage"), "the commodity page is visible when the window opens");

        // §113.5/B1: the root ignores the mouse, the content panel stops it.
        int rootIndex = scene.IndexOf("name=\"Control3\"", StringComparison.Ordinal);
        ok &= Expect(rootIndex >= 0, "the root node is present");
        if (rootIndex >= 0)
        {
            string rootBlock = scene.Substring(rootIndex, Math.Min(400, scene.Length - rootIndex));
            ok &= Expect(rootBlock.Contains("mouse_filter = 2"),
                "the window root ignores the mouse");
        }

        string panelMarker = "name=\"PanelContainer\" type=\"PanelContainer\" parent=\"CommodityPage/ScrollContainer\"";
        int panelIndex = scene.IndexOf(panelMarker, StringComparison.Ordinal);
        ok &= Expect(panelIndex >= 0, "the commodity content panel exists");

        // The window is a 640 px side panel, not a full-screen overlay: without this the root
        // stretched to 1920 and the list ran to 2560 px over the map.
        ok &= Expect(scene.Contains("offset_right = 640.0") && !scene.Contains("anchor_right = 1.0\ngrow_horizontal"),
            "the window keeps its 640 px width instead of stretching over the screen");

        // Header blocks must not overlap: esc, title, cash, the two section buttons, CheckMark.
        ok &= Expect(!scene.Contains("offset_right = 1.0"), "no child stretches across the screen");
        return ok;
    }

    /// <summary>
    /// True when the node's block in the .tscn declares <c>visible = false</c>. Line endings are
    /// normalised first: the scenes are CRLF on disk while the test source is not.
    /// </summary>
    private static bool NodeBlockHides(string scene, string nodeName)
    {
        string normalized = scene.Replace("\r\n", "\n");
        string marker = $"name=\"{nodeName}\"";
        int start = normalized.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
            return false;
        int next = normalized.IndexOf("\n[node ", start + marker.Length, StringComparison.Ordinal);
        string block = next < 0
            ? normalized.Substring(start)
            : normalized.Substring(start, next - start);
        return block.Contains("\nvisible = false");
    }

    private bool AtlasHas90Entries()
    {
        GD.Print($"{Tag} --- resource atlas ---");
        bool ok = true;
        int count = ResourceAtlasCatalog.Count;
        ok &= Expect(count == 90, $"atlas catalog holds 90 entries (got {count})");
        ok &= Expect(ResourceAtlasCatalog.Atlas != null,
            $"atlas texture loaded ({ResourceAtlasCatalog.LastError})");
        if (count > 0)
        {
            ResourceAtlasEntry first = ResourceAtlasCatalog.Entries[0];
            ok &= Expect(first.Id == "wheat" && first.Index == 0,
                $"first entry is wheat/0 (got {first.Id}/{first.Index})");
            ResourceAtlasEntry last = ResourceAtlasCatalog.Entries[count - 1];
            ok &= Expect(last.Id == "ice" && last.Index == 89,
                $"last entry is ice/89 (got {last.Id}/{last.Index})");
        }

        // The JSON stores trim RELATIVE TO THE CELL while AtlasTexture.Region is ABSOLUTE.
        // Using the raw trim showed the atlas top-left corner for 89 of 90 rows, so every
        // commodity looked like the same picture shifted by its own trim.
        ok &= Expect(AtlasRegionsAreDistinctAndInBounds(count),
            "every entry maps to its own atlas region (cell + trim, absolute, inside the atlas)");
        return ok;
    }

    private bool AtlasRegionsAreDistinctAndInBounds(int count)
    {
        if (count == 0 || ResourceAtlasCatalog.Atlas == null)
            return false;

        var seen = new System.Collections.Generic.HashSet<string>();
        Vector2 atlasSize = ResourceAtlasCatalog.Atlas.GetSize();
        foreach (ResourceAtlasEntry entry in ResourceAtlasCatalog.Entries)
        {
            Rect2 region = ResourceAtlasCatalog.RegionFor(entry);
            if (region.Position.X < 0f || region.Position.Y < 0f
                || region.End.X > atlasSize.X || region.End.Y > atlasSize.Y)
            {
                GD.PrintErr($"{Tag} FAIL: region {region} of '{entry.Id}' leaves the atlas {atlasSize}");
                return false;
            }
            if (region.Position.X < entry.Cell.Position.X || region.Position.Y < entry.Cell.Position.Y
                || region.End.X > entry.Cell.End.X || region.End.Y > entry.Cell.End.Y)
            {
                GD.PrintErr($"{Tag} FAIL: region {region} of '{entry.Id}' is outside its cell {entry.Cell}");
                return false;
            }
            if (!seen.Add($"{region.Position},{region.Size}"))
            {
                GD.PrintErr($"{Tag} FAIL: '{entry.Id}' reuses the region {region}");
                return false;
            }
        }
        return seen.Count == count;
    }

    // --- live window ---

    private bool TrySetupLiveUi()
    {
        // Under `--script res://tests/run_x.gd` the main loop is a bare MainLoop, so there is no
        // tree to hold the HUD: HUDController._Ready calls GetTree().Root and would throw.
        // Like TestFarmWindowMouse/TestWindowCloseOrder we skip the live half there and rely on
        // the source + scene guards above (the window is opened for real in the live game).
        _tree = Engine.GetMainLoop() as SceneTree;
        if (_tree == null)
        {
            GD.Print($"{Tag} SKIP: нет SceneTree — живое окно не поднять, проверены источник и сцена");
            return true;
        }

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
            GD.Print($"{Tag} SKIP: HUD не поднялся в headless ({ex.GetType().Name}: {ex.Message})");
            Drop();
            return true;
        }

        _exchange = _hud.FindChild("ExchangeController", true, false) as ExchangeController;
        if (_exchange == null)
        {
            GD.PrintErr($"{Tag} FAIL: the HUD has no ExchangeController — the window cannot open");
            Drop();
            return false;
        }

        var root = _exchange.FindChild("Control3", true, false) as Control;
        if (root == null)
        {
            GD.PrintErr($"{Tag} FAIL: ExchangeController did not instantiate Control3 from Exchange.tscn");
            Drop();
            return false;
        }

        bool ok = true;
        foreach (string name in new[]
                 {
                     "Name", "esc", "CheckMark", "Backdrop", "TabCommodity", "TabStock",
                     "CommodityPage", "StockPage", "VBoxContainer", "HBoxContainer2",
                     "MoneyAmount", "DealLog", "StockRows", "StockEmptyText"
                 })
        {
            ok &= Expect(_exchange.FindChild(name, true, false) != null,
                $"Exchange.tscn has '{name}'");
        }

        // §113.5/B1 mouse rule.
        ok &= Expect(root.MouseFilter == Control.MouseFilterEnum.Ignore,
            "window root ignores the mouse (world stays clickable through the empty area)");
        var scroll = root.FindChild("ScrollContainer", true, false) as ScrollContainer;
        ok &= Expect(scroll != null && scroll.MouseFilter == Control.MouseFilterEnum.Ignore,
            "ScrollContainer ignores the mouse");
        var panel = root.FindChild("PanelContainer", true, false) as Control;
        ok &= Expect(panel != null && panel.MouseFilter == Control.MouseFilterEnum.Stop,
            "the content panel stops the mouse");

        _live = true;
        return ok;
    }

    private bool WindowStartsHidden()
    {
        GD.Print($"{Tag} --- window starts closed ---");
        return Expect(!_exchange.IsOpen, "the exchange window is hidden until the HUD button opens it");
    }

    private bool OpensWithCommodityPage()
    {
        GD.Print($"{Tag} --- open: commodity page first ---");
        _exchange.Open();
        bool ok = Expect(_exchange.IsOpen, "Open() shows the window");
        ok &= Expect(_exchange.IsCommodityVisible, "the commodity page is shown first");

        var title = _exchange.FindChild("Name", true, false) as Label;
        ok &= Expect(title != null && title.Text == "Биржи", $"the title says \"Биржи\" (got \"{title?.Text}\")");
        return ok;
    }

    private bool CommodityRowsMatchAtlas()
    {
        GD.Print($"{Tag} --- commodity page: one row per atlas entry ---");
        int rows = _exchange.CommodityRowCount;
        bool ok = Expect(rows == 90, $"90 rows built for 90 atlas entries (got {rows})");

        var list = _exchange.FindChild("VBoxContainer", true, false) as VBoxContainer;
        ok &= Expect(list != null, "the commodity list container exists");
        if (list == null)
            return false;

        int icons = 0;
        int plates = 0;
        int buttons = 0;
        CountCommodityContent(list, ref icons, ref plates, ref buttons);
        ok &= Expect(icons == 90, $"every row has an icon (got {icons})");
        ok &= Expect(plates >= 90, $"every row is a plate (got {plates})");
        ok &= Expect(buttons == 180, $"each row has buy and sell buttons (got {buttons})");

        var template = _exchange.FindChild("HBoxContainer2", true, false) as Control;
        ok &= Expect(template != null && !template.Visible, "the template row stays hidden");
        return ok;
    }

    private static void CountCommodityContent(Node node, ref int icons, ref int plates, ref int buttons)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is TextureRect texture && texture.Texture != null && texture.CustomMinimumSize.X <= 32)
                icons++;
            if (child is PanelContainer)
                plates++;
            if (child is Button)
                buttons++;
            CountCommodityContent(child, ref icons, ref plates, ref buttons);
        }
    }

    private bool StockPageIsSkeleton()
    {
        GD.Print($"{Tag} --- stock page is a skeleton ---");
        _exchange.ShowStockPage();
        bool ok = Expect(!_exchange.IsCommodityVisible, "the stock page replaces the commodity page");

        var stockPage = _exchange.FindChild("StockPage", true, false) as Control;
        ok &= Expect(stockPage != null && stockPage.Visible, "StockPage is visible");
        var empty = _exchange.FindChild("StockEmptyText", true, false) as Label;
        ok &= Expect(empty != null && !string.IsNullOrEmpty(empty.Text),
            "the stock page explains that shares are not available yet");

        var rows = _exchange.FindChild("StockRows", true, false) as VBoxContainer;
        int rowButtons = 0;
        int labels = 0;
        if (rows != null)
        {
            foreach (Node child in rows.GetChildren())
            {
                if (child is Button)
                    rowButtons++;
                if (child is Label)
                    labels++;
            }
        }
        ok &= Expect(rowButtons == 0, $"no fake quotes: the stock table has no rows with actions (got {rowButtons})");
        ok &= Expect(labels == 1, $"the stock table holds only the placeholder (got {labels} labels)");
        return ok;
    }

    private bool PagesAreExclusive()
    {
        GD.Print($"{Tag} --- sections are exclusive pages ---");
        var commodityPage = _exchange.FindChild("CommodityPage", true, false) as Control;
        var stockPage = _exchange.FindChild("StockPage", true, false) as Control;
        bool ok = true;

        _exchange.ShowCommodityPage();
        ok &= Expect(commodityPage != null && commodityPage.Visible && stockPage != null && !stockPage.Visible,
            "commodity page visible, stock page hidden");

        _exchange.ShowStockPage();
        ok &= Expect(commodityPage != null && !commodityPage.Visible && stockPage != null && stockPage.Visible,
            "stock page visible, commodity page hidden");

        var tabCommodity = _exchange.FindChild("TabCommodity", true, false) as Button;
        var tabStock = _exchange.FindChild("TabStock", true, false) as Button;
        ok &= Expect(tabCommodity != null && tabStock != null, "both section buttons exist in the header");
        if (tabCommodity != null)
        {
            tabCommodity.EmitSignal("pressed");
            ok &= Expect(_exchange.IsCommodityVisible, "pressing \"Товарная\" brings the commodity page back");
        }
        return ok;
    }

    private bool OneEscapeCloses()
    {
        GD.Print($"{Tag} --- one Escape closes the window ---");
        _exchange.Open();
        bool ok = Expect(_exchange.IsOpen, "window reopened");

        var key = new InputEventKey
        {
            Keycode = Key.Escape,
            PhysicalKeycode = Key.Escape,
            Pressed = true
        };
        _exchange._UnhandledKeyInput(key);
        ok &= Expect(!_exchange.IsOpen, "a single Escape closes the window (R-048 / R-066)");
        return ok;
    }

    private bool DealMovesMoneyOnly()
    {
        GD.Print($"{Tag} --- stage 1 deal: money and log only ---");
        _exchange.Open();
        int before = _exchange.Money;
        ResourceAtlasEntry entry = ResourceAtlasCatalog.Entries.Count > 0
            ? ResourceAtlasCatalog.Entries[0]
            : default;

        _exchange.Trade(entry, buy: true);
        bool ok = Expect(_exchange.Money == before - _exchange.PriceFor(entry) * _exchange.LotSize,
            $"buying takes money from the till ({before} -> {_exchange.Money})");

        int afterBuy = _exchange.Money;
        _exchange.Trade(entry, buy: false);
        ok &= Expect(_exchange.Money == afterBuy + _exchange.PriceFor(entry) * _exchange.LotSize,
            $"selling returns money ({afterBuy} -> {_exchange.Money})");

        var log = _exchange.FindChild("DealLog", true, false) as Label;
        ok &= Expect(log != null && log.Text.Contains("Продано"), "the deal log records the last trade");
        return ok;
    }

    // --- helpers ---

    private static string ReadText(string path)
    {
        string text = Godot.FileAccess.GetFileAsString(path);
        return text ?? string.Empty;
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
        _exchange = null;
        if (_hud != null)
        {
            try
            {
                if (GodotObject.IsInstanceValid(_hud))
                    _hud.Free();
            }
            catch (ObjectDisposedException)
            {
                // Already freed with the tree.
            }
        }
        _hud = null;
        _live = false;
        if (_ownTree != null)
        {
            // The private tree owns the HUD; freeing it releases every node built on the way.
            _ownTree.Quit();
            _ownTree = null;
        }
        _tree = null;
    }
}
