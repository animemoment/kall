using Godot;
using System;
using Game.UI.Resources;

namespace Game.Tests;

/// <summary>
/// §121/§128: the "Ресурсы" panel of hud.tscn (right column) lists EVERY resource of the raw
/// atlas, each with its own icon and the live stockpile count.
///
/// The panel used to be two hard-wired rows (bread + wood, the bread one dead). It is now built
/// from the atlas registry, so a resource that reaches the game shows up on its own.
///
/// Pinned here:
///   * the scene keeps the old contract nodes (R-014) and gains ResourceScroll/ResourceRows;
///   * the read-only list does not turn the panel into a mouse trap (28-09): panel and scroll
///     stay Ignore, otherwise the right-bottom corner would eat world clicks;
///   * the icon of every row is its own atlas region, not the atlas corner;
///   * HUDController maps the three live ItemIds onto atlas ids and refreshes on the
///     stockpile event.
///
/// Run: godot --headless --path . --script res://tests/run_hud_resource_panel.gd
/// </summary>
public partial class TestHudResourcePanel : RefCounted
{
    private const string Tag = "[HUD-RESOURCES]";
    private const string HudScenePath = "res://scenes/ui/hud.tscn";
    private const string HudSourcePath = "res://src/ui/controllers/HUDController.cs";

    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print($"{Tag} панель «Ресурсы»: все ресурсы атласа, свои иконки, живые числа");
        GD.Print("==================================================");

        bool ok = true;
        ok &= SceneKeepsContract();
        ok &= PanelIgnoresMouse();
        ok &= ControllerBuildsFromAtlas();
        ok &= CatalogGivesDistinctIcons();
        return ok;
    }

    private bool SceneKeepsContract()
    {
        GD.Print($"{Tag} --- hud.tscn ---");
        string scene = ReadText(HudScenePath);
        bool ok = Expect(!string.IsNullOrEmpty(scene), "hud.tscn is readable");
        if (string.IsNullOrEmpty(scene))
            return false;

        // R-014: the old nodes stay; they are only hidden.
        foreach (string name in new[] { "AmountOfResources", "BreadContainer", "BreadAmount", "WoodContainer", "WoodAmount" })
            ok &= Expect(scene.Contains($"name=\"{name}\""), $"hud.tscn still has '{name}'");

        ok &= Expect(scene.Contains("name=\"ResourceScroll\""), "the panel has a scroll container");
        ok &= Expect(scene.Contains("name=\"ResourceRows\""), "the panel has a rows container");
        return ok;
    }

    private bool PanelIgnoresMouse()
    {
        GD.Print($"{Tag} --- 28-09: панель не съедает клики по миру ---");
        string scene = ReadText(HudScenePath).Replace("\r\n", "\n");
        bool ok = true;

        ok &= Expect(BlockHas(scene, "AmountOfResources", "mouse_filter = 2"),
            "AmountOfResources stays Ignore in the scene");
        // The scroll must be Pass, not Ignore: an Ignore control never sees the wheel, so it
        // travelled into the world and zoomed the camera while the list stood still (R-074).
        ok &= Expect(BlockHas(scene, "ResourceScroll", "mouse_filter = 1"),
            "the read-only resource scroll is Pass (takes the wheel, lets clicks through)");

        string hud = ReadText(HudSourcePath);
        ok &= Expect(hud.Contains("ScrollContainer) return Control.MouseFilterEnum.Pass")
                   || hud.Contains("is ScrollContainer) return Control.MouseFilterEnum.Pass"),
            "HUDController resolves every scroll to Pass");
        return ok;
    }

    private bool ControllerBuildsFromAtlas()
    {
        GD.Print($"{Tag} --- HUDController ---");
        string hud = ReadText(HudSourcePath);
        bool ok = Expect(!string.IsNullOrEmpty(hud), "HUDController.cs is readable");
        if (string.IsNullOrEmpty(hud))
            return false;

        ok &= Expect(hud.Contains("SetupResourceRows"), "the rows are built in code");
        ok &= Expect(hud.Contains("ResourceAtlasCatalog.Entries"),
            "the rows come from the atlas registry, not from a hard-wired pair");
        ok &= Expect(hud.Contains("GetTotalItemCount"),
            "the numbers are the live stockpile totals");
        ok &= Expect(hud.Contains("ItemIdForAtlasId"),
            "atlas ids are mapped onto the live ItemIds");

        // Every ItemId that can reach the stockpile must be mapped to some atlas row.
        foreach (string item in new[] { "ItemId.Log", "ItemId.Grain", "ItemId.Stone" })
            ok &= Expect(hud.Contains(item), $"{item} is mapped to an atlas row");
        return ok;
    }

    private bool CatalogGivesDistinctIcons()
    {
        GD.Print($"{Tag} --- иконки ---");
        int count = ResourceAtlasCatalog.Count;
        bool ok = Expect(count == 90, $"the atlas registry holds 90 resources (got {count})");
        ok &= Expect(ResourceAtlasCatalog.Atlas != null, "the atlas texture loads");

        if (count > 0 && ResourceAtlasCatalog.Atlas != null)
        {
            var seen = new System.Collections.Generic.HashSet<string>();
            int outside = 0;
            foreach (ResourceAtlasEntry entry in ResourceAtlasCatalog.Entries)
            {
                Rect2 region = ResourceAtlasCatalog.RegionFor(entry);
                if (region.Position.X < entry.Cell.Position.X || region.Position.Y < entry.Cell.Position.Y
                    || region.End.X > entry.Cell.End.X || region.End.Y > entry.Cell.End.Y)
                    outside++;
                seen.Add($"{region.Position},{region.Size}");
            }
            ok &= Expect(outside == 0, $"every region stays inside its cell (broken: {outside})");
            ok &= Expect(seen.Count == count, $"90 distinct icons (got {seen.Count})");
        }
        return ok;
    }

    /// <summary>True when the node's block in the .tscn contains the given line.</summary>
    private static bool BlockHas(string scene, string nodeName, string expectedLine)
    {
        string marker = $"name=\"{nodeName}\"";
        int start = scene.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
            return false;
        int next = scene.IndexOf("\n[node ", start + marker.Length, StringComparison.Ordinal);
        string block = next < 0 ? scene.Substring(start) : scene.Substring(start, next - start);
        return block.Contains(expectedLine);
    }

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
}
