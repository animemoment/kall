using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Godot;
using Game.Core;
using Game.Core.World;
using Game.Simulation;

namespace Game.Tests;

/// <summary>
/// 08-08: the farm clearance spill must use the island boundary (WorldModel.IslandTiles),
/// not the old hardcoded half-island window.
///
/// Root: FarmJobManager passed the old half-island window as mapWidth/mapHeight to
/// GroundItemManager.TrySpillItemsAdjacent. The spill rejects a neighbour unless
/// 0 &lt;= n &lt; mapWidth/Height, so past the old half-island window (x or y at or after
/// seam+1) every neighbour looked "outside the map": the pile never moved off the plot and
/// the plot stayed blocked forever. A cell exactly on the seam still worked, because its
/// first neighbour (seam-1) was inside the old window.
///
/// The test is intentionally independent of TestFarmClearance (which owns the
/// job-registration side): here it verifies the spill geometry and the "no item lost"
/// invariant only.
///
/// Checks:
///   1) source guard: the farm spill call has no half-island literal and takes its bounds
///      from WorldModel.IslandTiles (this alone is red on the old code);
///   2) (600,300) east half: the pile leaves the plot for the first free neighbour;
///   3) (500,300) west half: the new call lands exactly where the old window landed;
///   4) the old seam and its two neighbours (seam-1 / seam / seam+1): pile leaves, no
///      exception, total kept; seam+1 is the first cell the old window could not clear;
///   5) negative cells: no exception, the item total is neither lost nor duplicated;
///   6) island edge (IslandTiles - 4 .. IslandTiles + 4): no exception, total kept, and
///      a spill from an island cell never pushes the pile off the island.
///
/// Run: godot --headless --path . --script res://tests/run_spill_bounds.gd
/// </summary>
public partial class TestSpillBounds : RefCounted
{
    private const string FarmSourcePath = "res://src/simulation/farming/FarmJobManager.cs";

    private static int _failures;

    public bool Run()
    {
        _failures = 0;

        GD.Print("==================================================");
        GD.Print("[SPILL-BOUNDS] 08-08: farm spill must use the island boundary");
        GD.Print("==================================================");

        try
        {
            SourceUsesIslandBounds();
            EastHalfClears();
            FirstHalfMatchesOldWindow();
            BothSidesOfTheOldSeam();
            NegativeCellsKeepItems();
            IslandEdgeKeepsItems();
        }
        catch (Exception ex)
        {
            _failures++;
            GD.PrintErr($"[SPILL-BOUNDS] FAIL: unexpected exception {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            ResetState();
        }

        GD.Print($"[SPILL-BOUNDS] {(_failures == 0 ? "ALL PASSED" : "FAILURES: " + _failures)}");
        return _failures == 0;
    }

    // ---------------------------------------------------------------- source guard

    /// <summary>
    /// The clearance call must not carry its own window: the island boundary has exactly
    /// one owner. Red on the old code, where the spill call passed the half-island literal
    /// twice and never mentioned WorldModel.
    /// </summary>
    private static void SourceUsesIslandBounds()
    {
        string source = Godot.FileAccess.GetFileAsString(FarmSourcePath);
        if (string.IsNullOrEmpty(source))
        {
            Check(false, $"source guard: cannot read {FarmSourcePath}");
            return;
        }

        // Line comments are documentation, not code: they may mention the old window.
        string code = Regex.Replace(source, @"//[^\n]*", string.Empty);

        string halfIsland = (WorldModel.IslandTiles / 2).ToString(CultureInfo.InvariantCulture);
        Check(!Regex.IsMatch(code, @"(?<![\w])" + Regex.Escape(halfIsland) + @"(?![\w])"),
            "source guard: the farm spill call contains no half-island literal (" + halfIsland + ")");
        Check(Regex.IsMatch(code, @"TrySpillItemsAdjacent\s*\([^;]*WorldModel\.IslandTiles"),
            "source guard: the farm spill call takes its bounds from WorldModel.IslandTiles");
    }

    // ---------------------------------------------------------------- (2) east half

    /// <summary>
    /// The regression cell: x is past the old half-island boundary. On the old code every
    /// neighbour was rejected, the pile stayed on (600,300) and the total was unchanged but
    /// never moved. This is the cell that is red on the old code.
    /// </summary>
    private static void EastHalfClears()
    {
        const int x = 600, y = 300;
        ResetState();
        GroundItemManager.Instance.SpawnItems(x, y, ItemId.Log, 3);
        int before = TotalItemCount();

        bool threw = false;
        try
        {
            FarmJobManager.Instance.MarkPlot(x, y, null, null);
        }
        catch (Exception ex)
        {
            threw = true;
            GD.PrintErr($"[SPILL-BOUNDS] ({x},{y}) threw {ex.GetType().Name}: {ex.Message}");
        }

        Check(!threw, $"({x},{y}) east half: MarkPlot does not throw");
        Check(FarmJobManager.Instance.IsPlotMarked(x, y), $"({x},{y}) east half: the plot is marked");
        Check(TotalItemCount() == before, $"({x},{y}) east half: item total is unchanged");
        Check(GroundItemManager.Instance.GetItemCountAt(x, y) == 0,
            $"({x},{y}) east half: the pile left the plot (old code kept it: every neighbour was 'outside')");
        Check(GroundItemManager.Instance.GetItemCountAt(599, 299) == 3,
            $"({x},{y}) east half: the pile moved to the first free neighbour (599,299)");
    }

    // ---------------------------------------------------------------- (3) west half

    /// <summary>
    /// Control cell where the old window worked. The old behaviour is reproduced by calling
    /// the same spill with the old half-island bounds; the farm path must produce exactly the
    /// same target cell and count, so the fix does not change the first half of the map.
    /// The neighbour loop starts at (-1,-1), so the old target is (499,299).
    /// </summary>
    private static void FirstHalfMatchesOldWindow()
    {
        const int x = 500, y = 300;
        int halfIsland = WorldModel.IslandTiles / 2;

        ResetState();
        GroundItemManager.Instance.SpawnItems(x, y, ItemId.Log, 3);
        bool oldSpilled = GroundItemManager.Instance.TrySpillItemsAdjacent(x, y, halfIsland, halfIsland);
        Check(oldSpilled, $"({x},{y}) control: the old half-island window spills");
        Check(TryGetOnlyPile(out int oldX, out int oldY, out int oldCount),
            $"({x},{y}) control: exactly one pile after the old-window spill");
        Check(oldX == 499 && oldY == 299,
            $"({x},{y}) control: old-window target is (499,299), got ({oldX},{oldY})");
        Check(oldCount == 3, $"({x},{y}) control: old-window target keeps all 3 items, got {oldCount}");

        ResetState();
        GroundItemManager.Instance.SpawnItems(x, y, ItemId.Log, 3);
        FarmJobManager.Instance.MarkPlot(x, y, null, null);
        Check(TryGetOnlyPile(out int newX, out int newY, out int newCount),
            $"({x},{y}) west half: exactly one pile after the farm spill");
        Check(newX == oldX && newY == oldY,
            $"({x},{y}) west half: target ({newX},{newY}) matches the old window ({oldX},{oldY})");
        Check(newCount == oldCount,
            $"({x},{y}) west half: count {newCount} matches the old window {oldCount}");
        Check(GroundItemManager.Instance.GetItemCountAt(x, y) == 0,
            $"({x},{y}) west half: the pile left the plot");
    }

    // ---------------------------------------------------------------- (4) seam

    /// <summary>
    /// The old seam and both of its neighbours: the pile must leave the plot, no exception,
    /// and the total item count must be identical before and after (nothing lost, nothing
    /// duplicated). Cell seam+1 is the first one whose whole 3x3 window is past the old
    /// half-island boundary, so it is the first cell that is red on the old code.
    /// </summary>
    private static void BothSidesOfTheOldSeam()
    {
        int seam = WorldModel.IslandTiles / 2;
        int[] xs = { seam - 1, seam, seam + 1 };
        foreach (int x in xs)
        {
            const int y = 300;
            ResetState();
            GroundItemManager.Instance.SpawnItems(x, y, ItemId.Log, 2);
            int before = TotalItemCount();

            bool threw = false;
            try
            {
                FarmJobManager.Instance.MarkPlot(x, y, null, null);
            }
            catch (Exception ex)
            {
                threw = true;
                GD.PrintErr($"[SPILL-BOUNDS] seam ({x},{y}) threw {ex.GetType().Name}: {ex.Message}");
            }

            Check(!threw, $"seam ({x},{y}): MarkPlot does not throw");
            Check(TotalItemCount() == before, $"seam ({x},{y}): item total is unchanged");
            Check(GroundItemManager.Instance.GetItemCountAt(x, y) == 0,
                $"seam ({x},{y}): the pile left the plot");
            Check(GroundItemManager.Instance.GetItemCountAt(x - 1, y - 1) == 2,
                $"seam ({x},{y}): the pile moved to the first free neighbour ({x - 1},{y - 1})");
        }
    }

    // ---------------------------------------------------------------- (5) negatives

    /// <summary>
    /// Negative cells: no exception and no item loss/duplication. The spill itself keeps its
    /// explicit "never cross into negative coordinates" rule, so a pile may stay put - that
    /// is fine as long as the total is conserved.
    /// </summary>
    private static void NegativeCellsKeepItems()
    {
        int[][] cells =
        {
            new[] { -1, -1 },
            new[] { -1, 300 },
            new[] { -5, -1 },
            new[] { -1, -5 },
        };

        foreach (int[] cell in cells)
        {
            int x = cell[0], y = cell[1];
            ResetState();
            GroundItemManager.Instance.SpawnItems(x, y, ItemId.Log, 2);
            int before = TotalItemCount();

            bool threw = false;
            try
            {
                FarmJobManager.Instance.MarkPlot(x, y, null, null);
            }
            catch (Exception ex)
            {
                threw = true;
                GD.PrintErr($"[SPILL-BOUNDS] negative ({x},{y}) threw {ex.GetType().Name}: {ex.Message}");
            }

            Check(!threw, $"negative ({x},{y}): MarkPlot does not throw");
            Check(TotalItemCount() == before, $"negative ({x},{y}): item total is unchanged");

            if (GroundItemManager.Instance.GetItemCountAt(x, y) == 0)
            {
                Check(TryGetOnlyPile(out int px, out int py, out int pc)
                      && pc == 2 && Math.Abs(px - x) <= 1 && Math.Abs(py - y) <= 1,
                    $"negative ({x},{y}): the moved pile stays adjacent and keeps its 2 items");
            }
        }
    }

    // ---------------------------------------------------------------- (6) island edge

    /// <summary>
    /// Both sides of the island edge: cells still on the island must clear their pile within
    /// the island, cells past the edge must not throw and must not lose items.
    /// </summary>
    private static void IslandEdgeKeepsItems()
    {
        int first = WorldModel.IslandTiles - 4;
        int last = WorldModel.IslandTiles + 4;
        for (int c = first; c <= last; c++)
        {
            CheckSpillAt(c, 300);
            CheckSpillAt(300, c);
        }
    }

    private static void CheckSpillAt(int x, int y)
    {
        ResetState();
        GroundItemManager.Instance.SpawnItems(x, y, ItemId.Log, 2);
        int before = TotalItemCount();

        bool threw = false;
        try
        {
            FarmJobManager.Instance.MarkPlot(x, y, null, null);
        }
        catch (Exception ex)
        {
            threw = true;
            GD.PrintErr($"[SPILL-BOUNDS] edge ({x},{y}) threw {ex.GetType().Name}: {ex.Message}");
        }

        Check(!threw, $"edge ({x},{y}): MarkPlot does not throw");
        Check(TotalItemCount() == before, $"edge ({x},{y}): item total is unchanged");

        if (WorldModel.IsInsideIsland(x, y))
        {
            Check(GroundItemManager.Instance.GetItemCountAt(x, y) == 0,
                $"island cell ({x},{y}): the pile left the plot");
            Check(TryGetOnlyPile(out int px, out int py, out int pc)
                  && pc == 2 && (px != x || py != y)
                  && Math.Abs(px - x) <= 1 && Math.Abs(py - y) <= 1
                  && WorldModel.IsInsideIsland(px, py),
                $"island cell ({x},{y}): the pile moved to an adjacent island cell");
        }
    }

    // ---------------------------------------------------------------- helpers

    private static void ResetState()
    {
        GroundItemManager.Instance.ClearAll();
        FarmJobManager.Instance.ClearAll();
        JobDispatcher.Instance.JobIndex.Clear();
    }

    private static int TotalItemCount()
    {
        var snapshot = GroundItemManager.Instance.GetItemsSnapshot();
        int total = 0;
        for (int i = 0; i < snapshot.Count; i++)
            total += snapshot[i].Count;
        return total;
    }

    private static bool TryGetOnlyPile(out int x, out int y, out int count)
    {
        x = 0;
        y = 0;
        count = 0;
        var snapshot = GroundItemManager.Instance.GetItemsSnapshot();
        if (snapshot.Count != 1)
            return false;
        x = snapshot[0].X;
        y = snapshot[0].Y;
        count = snapshot[0].Count;
        return true;
    }

    private static void Check(bool condition, string message)
    {
        if (condition)
        {
            GD.Print($"[SPILL-BOUNDS] OK: {message}");
            return;
        }
        _failures++;
        GD.PrintErr($"[SPILL-BOUNDS] FAIL: {message}");
    }
}