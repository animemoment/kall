using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Godot;
using Game.Core;
using Game.Core.World;
using Game.Simulation;

namespace Game.Tests;

/// <summary>
/// CC-01: a marked farm plot with a pile lying on it must be cleared and must get its
/// till (Farming) job on the WHOLE island, not only in its north-west 512x512 quarter.
///
/// Root: FarmJobManager passed a hardcoded 512x512 window to
/// GroundItemManager.TrySpillItemsAdjacent. For x &gt;= 513 or y &gt;= 513 every neighbouring
/// cell was rejected as "outside the map", the pile never moved, NotifyItemCleared never
/// fired, _plotsNeedingClearance kept NeedItem forever and no Farming job was registered:
/// the plot was marked but nobody ever tilled it.
///
/// The real edge is 512|513, not 511|512: a plot at 512 still has the (511,511) neighbour
/// inside the old 512 window and therefore worked. The first cell whose EIGHT neighbours are
/// all &gt;= 512 is 513 - there the spill was rejected and the plot hung. 511/512 are kept as
/// the "last cells that worked" control; 513/1020/1023 are red on the old code.
///
/// Checks:
///   1) cells 511/512/513/1020/1023 and a MarkPlotsBatch sweep (the tools' path): the pile
///      is moved off the plot and a Farming job exists (513+ stay red on the old code);
///   2) a cell with no pile needs no clearance: the till job is registered directly,
///      both before and after a hauler has picked the pile up;
///   3) negative cells and both sides of the 1024 edge (1020..1028) do not throw, the
///      "pile or job" invariant holds, and no pile is pushed outside the island.
///
/// Run: godot --headless --path . --script res://tests/run_farm_clearance.gd
/// </summary>
public partial class TestFarmClearance : RefCounted
{
    private const string FarmSourcePath = "res://src/simulation/farming/FarmJobManager.cs";
    private static int _failures;

    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print("[FARM-CLEAR] CC-01: farm plot with a ground item on the east/south island");
        GD.Print("==================================================");

        try
        {
            SourceHasNoHalfIslandWindow();
            ClearanceWorksAcrossTheSeam();
            NoPileNeedsNoClearance();
            OffIslandAndNegativeCells();
        }
        catch (Exception ex)
        {
            _failures++;
            GD.PrintErr($"[FARM-CLEAR] FAIL: exception {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            ResetState();
        }

        GD.Print($"[FARM-CLEAR] {(_failures == 0 ? "ALL PASSED" : "FAILURES: " + _failures)}");
        return _failures == 0;
    }

    // ---------------------------------------------------------------- source guard

    /// <summary>
    /// The clearance call must not carry its own half-island window: the island boundary
    /// has exactly one owner (WorldModel.IslandTiles). Red on the old 512 code.
    /// </summary>
    private static void SourceHasNoHalfIslandWindow()
    {
        string src = Godot.FileAccess.GetFileAsString(FarmSourcePath);
        if (string.IsNullOrEmpty(src))
        {
            Check(false, $"cannot read {FarmSourcePath}");
            return;
        }

        Check(!Regex.IsMatch(src, @"TrySpillItemsAdjacent\([^)]*\b512\b"),
            "source: no TrySpillItemsAdjacent call with a hardcoded 512 window");
        Check(src.Contains("WorldModel.IslandTiles"),
            "source: spill bounds come from WorldModel.IslandTiles");
    }

    // ---------------------------------------------------------------- (1) clearance + job

    /// <summary>
    /// Cells on both sides of the old 512 seam: the pile leaves the plot and the plot gets
    /// a till job. 511/512 already worked on the old code (neighbour 510/511 is still inside
    /// the 512 window); 513/1020/1023 are the failing east/south half. Also covers the batch
    /// entry point the farming tools actually use.
    /// </summary>
    private static void ClearanceWorksAcrossTheSeam()
    {
        int[] cells = { 511, 512, 513, 1020, 1023 };
        foreach (int c in cells)
        {
            ResetState();
            GroundItemManager.Instance.SpawnItems(c, c, ItemId.Log, 3);
            Check(GroundItemManager.Instance.HasItemsAt(c, c),
                $"setup: a pile lies on plot {c},{c}");

            FarmJobManager.Instance.MarkPlot(c, c, null, null);

            Check(FarmJobManager.Instance.IsPlotMarked(c, c),
                $"plot {c},{c} is marked");
            Check(!GroundItemManager.Instance.HasItemsAt(c, c),
                $"plot {c},{c}: the pile was moved off the plot (513+ used to reject all 8 neighbours)");
            Check(JobDispatcher.Instance.JobIndex.HasJobAt(c, c, JobTypeId.Farming),
                $"plot {c},{c}: the till job was registered");
        }

        // FarmingTool/ZoneDraftTool mark plots in batches - the fixed call site too.
        ResetState();
        var batch = new List<(int X, int Y)> { (512, 640), (513, 640), (1023, 700) };
        foreach (var (bx, by) in batch)
            GroundItemManager.Instance.SpawnItems(bx, by, ItemId.Log, 1);

        FarmJobManager.Instance.MarkPlotsBatch(batch, null, null);

        foreach (var (bx, by) in batch)
        {
            Check(!GroundItemManager.Instance.HasItemsAt(bx, by),
                $"batch plot {bx},{by}: the pile was moved off the plot");
            Check(JobDispatcher.Instance.JobIndex.HasJobAt(bx, by, JobTypeId.Farming),
                $"batch plot {bx},{by}: the till job was registered");
        }
    }

    // ---------------------------------------------------------------- (2) nothing to clear

    /// <summary>
    /// (2) After the pile is removed from the cell no clearance work is created: nothing is
    /// spilled, the plot is marked ready at once and the till job is registered directly.
    /// </summary>
    private static void NoPileNeedsNoClearance()
    {
        ResetState();
        const int x = 700, y = 700;

        GroundItemManager.Instance.SpawnItems(x, y, ItemId.Log, 4);
        var (takenId, taken) = GroundItemManager.Instance.TakeItemsWithType(x, y, 4);
        Check(takenId == ItemId.Log && taken == 4,
            "the hauler picked the whole pile up");
        Check(!GroundItemManager.Instance.HasItemsAt(x, y),
            "the plot is empty after the pickup");

        FarmJobManager.Instance.MarkPlot(x, y, null, null);

        Check(JobDispatcher.Instance.JobIndex.HasJobAt(x, y, JobTypeId.Farming),
            "an empty plot needs no clearance: the till job is registered directly");
        Check(!AnyPileAround(x, y),
            "no clearance spill happened on an empty plot");
    }

    // ---------------------------------------------------------------- (3) seam and negatives

    /// <summary>
    /// Negative cells behave exactly like island cells (pile moves, job appears); both sides
    /// of the 1024 edge do not throw and never end in "empty plot without a job".
    /// </summary>
    private static void OffIslandAndNegativeCells()
    {
        // (3a) negative cell: must not index arrays and must clear like any other plot.
        ResetState();
        bool negativeThrew = false;
        try
        {
            GroundItemManager.Instance.SpawnItems(-1, -1, ItemId.Log, 2);
            FarmJobManager.Instance.MarkPlot(-1, -1, null, null);
        }
        catch (Exception ex)
        {
            negativeThrew = true;
            GD.PrintErr($"[FARM-CLEAR] negative cell threw {ex.GetType().Name}: {ex.Message}");
        }
        Check(!negativeThrew, "negative cell (-1,-1): MarkPlot does not throw");
        Check(!GroundItemManager.Instance.HasItemsAt(-1, -1),
            "negative cell (-1,-1): the pile was moved off the plot");
        Check(JobDispatcher.Instance.JobIndex.HasJobAt(-1, -1, JobTypeId.Farming),
            "negative cell (-1,-1): the till job was registered");

        // (3b) both sides of the island edge: 1020..1023 inside, 1024..1028 outside.
        for (int c = 1020; c <= 1028; c++)
        {
            ResetState();
            bool threw = false;
            try
            {
                GroundItemManager.Instance.SpawnItems(c, c, ItemId.Log, 1);
                FarmJobManager.Instance.MarkPlot(c, c, null, null);
            }
            catch (Exception ex)
            {
                threw = true;
                GD.PrintErr($"[FARM-CLEAR] seam cell {c},{c} threw {ex.GetType().Name}: {ex.Message}");
            }
            Check(!threw, $"seam cell {c},{c}: MarkPlot does not throw");
            if (threw)
                continue;

            bool pileLeft = GroundItemManager.Instance.HasItemsAt(c, c);
            bool hasJob = JobDispatcher.Instance.JobIndex.HasJobAt(c, c, JobTypeId.Farming);
            Check(pileLeft != hasJob,
                $"seam cell {c},{c}: never 'empty plot without a job' (pile={pileLeft}, job={hasJob})");

            if (WorldModel.IsInsideIsland(c, c))
            {
                Check(!pileLeft && hasJob,
                    $"island cell {c},{c}: pile cleared and till job registered");
            }

            Check(NoPileOutsideIslandAround(c, c),
                $"seam cell {c},{c}: no pile was pushed outside the island");
        }
    }

    // ---------------------------------------------------------------- helpers

    private static void ResetState()
    {
        GroundItemManager.Instance.ClearAll();
        FarmJobManager.Instance.ClearAll();
        JobDispatcher.Instance.JobIndex.Clear();
    }

    private static bool AnyPileAround(int x, int y)
    {
        for (int oy = -1; oy <= 1; oy++)
            for (int ox = -1; ox <= 1; ox++)
                if (GroundItemManager.Instance.HasItemsAt(x + ox, y + oy))
                    return true;
        return false;
    }

    /// <summary>Any pile that moved off the source cell must sit inside the island.</summary>
    private static bool NoPileOutsideIslandAround(int x, int y)
    {
        for (int oy = -1; oy <= 1; oy++)
        {
            for (int ox = -1; ox <= 1; ox++)
            {
                int nx = x + ox, ny = y + oy;
                if (nx == x && ny == y)
                    continue; // the source cell itself is not a spill result
                if (GroundItemManager.Instance.HasItemsAt(nx, ny) && !WorldModel.IsInsideIsland(nx, ny))
                    return false;
            }
        }
        return true;
    }

    private static void Check(bool condition, string message)
    {
        if (condition)
        {
            GD.Print($"[FARM-CLEAR] OK: {message}");
            return;
        }
        _failures++;
        GD.PrintErr($"[FARM-CLEAR] FAIL: {message}");
    }
}