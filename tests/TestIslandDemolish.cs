using System;
using System.Collections.Generic;
using Godot;
using Game.Core;
using Game.Simulation;
using Game.UI.Tools;

namespace Game.Tests;

/// <summary>
/// 21-01: RMB with a build tool must demolish FINISHED structures (walls and furniture)
/// inside the island, not only construction sites and delivery blueprints.
///
/// What was wrong: the RMB branch of <see cref="BuildTool.OnRelease"/> called only
/// <c>ConstructionManager.CancelSitesBatch</c> + <c>BlueprintManager.RemoveBlueprintsBatch</c>.
/// A finished wall or table stayed in its cell forever, while
/// <c>StructureManager.RemoveStructure</c> existed and had no caller at all.
///
/// Two side conditions are guarded here as well:
///  - walkability: the UI runs on the main thread and has no SimulationContext, so the array
///    handed to <c>StructureManager.FillSolidWalls</c> is cleared on demolition. Otherwise
///    the cell stays impassable for the pathfinder ("phantom wall") until the colony is
///    reloaded — agents keep walking around a wall that is already gone;
///  - materials: demolition returns the build materials to the ground next to the cell, the
///    same rule a cancelled construction site already follows;
///  - the kind gate (wall tool -> walls, furniture tool -> furniture) matches the world
///    branch of the same RMB, so the island edge and the world keep one demolition rule.
///
/// On the old code the wall, wall-row and furniture checks are red: the structure is simply
/// not touched.
///
/// Run: godot --headless --path . --script res://tests/run_island_demolish.gd
/// </summary>
public partial class TestIslandDemolish : RefCounted
{
    private const string Tag = "[ISLAND-DEMOLISH]";
    private const int MapSize = 64;

    /// <summary>Furniture the audit card lists explicitly (21-01).</summary>
    private static readonly BuildingType[] FurnitureTypes =
    {
        BuildingType.WorkTable,
        BuildingType.Bed,
        BuildingType.Bench,
        BuildingType.Nightstand,
        BuildingType.CentralBlock,
    };

    private WallBuildManager _wallManager;

    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print($"{Tag} 21-01: RMB demolishes finished walls and furniture on the island");
        GD.Print("==================================================");

        bool ok = true;
        try
        {
            ok &= FinishedWallDemolishedAndWalkable();
            ok &= WallRowDemolishedByDrag();
            ok &= FinishedFurnitureDemolished();
            ok &= ToolKindGateMatchesWorldPath();
            ok &= PlansAndBlueprintsStillLift();
            ok &= EmptyCellIsNoOp();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"{Tag} FAIL: exception: {ex}");
            ok = false;
        }
        finally
        {
            StructureManager.Instance.ClearAll();
            ConstructionManager.Instance.ClearAll();
            GroundItemManager.Instance.ClearAll();
            _wallManager?.Dispose();
            _wallManager = null;
        }

        GD.Print("==================================================");
        GD.Print(ok ? $"{Tag} PASS" : $"{Tag} FAIL");
        GD.Print("==================================================");
        return ok;
    }

    /// <summary>
    /// (1) A finished wall disappears and its cell becomes passable again. Red on the old
    /// code: the wall survived the RMB and SolidWalls stayed true forever.
    /// </summary>
    private bool FinishedWallDemolishedAndWalkable()
    {
        ResetWorld();
        var island = new Island();
        BuildTool tool = NewTool(BuildingType.WoodWall);

        if (!StructureManager.Instance.AddStructure(10, 10, StructureRegistry.WoodWall))
            return Fail("could not place the finished wall at (10,10) — test is invalid");

        island.FillSolidWalls();
        if (!island.IsSolid(10, 10))
            return Fail("wall is not blocking after FillSolidWalls — test is invalid");

        int logsBefore = LogsOnGround();
        Demolish(tool, 10, 10, 10, 10);

        if (StructureManager.Instance.HasStructureAt(10, 10))
            return Fail("21-01: finished wall at (10,10) survived RMB");

        // Same array the simulation thread reads through ctx.SolidWalls: it must be clear.
        if (island.IsSolid(10, 10) || island.Ctx.SolidWalls[10, 10])
            return Fail("phantom wall: (10,10) stayed impassable in SolidWalls after demolition");

        int expected = StructureRegistry.WoodWall.GetRequiredCount(ItemId.Log);
        int refund = LogsOnGround() - logsBefore;
        if (refund != expected)
            return Fail($"demolition returned {refund} logs, expected {expected}");

        GD.Print($"{Tag} OK: finished wall removed, (10,10) walkable again, {refund} logs refunded");
        return true;
    }

    /// <summary>
    /// (2) RMB drag over a row of walls demolishes every cell of the rectangle, symmetrically
    /// with the plan path. Red on the old code for the same reason as (1).
    /// </summary>
    private bool WallRowDemolishedByDrag()
    {
        ResetWorld();
        var island = new Island();
        BuildTool tool = NewTool(BuildingType.WoodWall);

        const int y = 20;
        const int firstX = 12;
        const int lastX = 15;

        for (int x = firstX; x <= lastX; x++)
        {
            if (!StructureManager.Instance.AddStructure(x, y, StructureRegistry.WoodWall))
                return Fail($"could not place the finished wall at ({x},{y}) — test is invalid");
        }

        island.FillSolidWalls();
        for (int x = firstX; x <= lastX; x++)
        {
            if (!island.IsSolid(x, y))
                return Fail($"wall ({x},{y}) is not blocking after FillSolidWalls — test is invalid");
        }

        int expectedRefund = (lastX - firstX + 1) * StructureRegistry.WoodWall.GetRequiredCount(ItemId.Log);
        int logsBefore = LogsOnGround();
        Demolish(tool, firstX, y, lastX, y);

        for (int x = firstX; x <= lastX; x++)
        {
            if (StructureManager.Instance.HasStructureAt(x, y))
                return Fail($"RMB drag left the finished wall at ({x},{y}) standing");
            if (island.IsSolid(x, y))
                return Fail($"RMB drag left a phantom wall at ({x},{y})");
        }

        int refund = LogsOnGround() - logsBefore;
        if (refund != expectedRefund)
            return Fail($"RMB drag returned {refund} logs, expected {expectedRefund}");

        GD.Print($"{Tag} OK: RMB drag removed {lastX - firstX + 1} walls, every cell walkable");
        return true;
    }

    /// <summary>
    /// (3) Every finished furniture piece (table / bed / bench / nightstand / central block)
    /// disappears on RMB and does not block the cell either before or after.
    /// </summary>
    private bool FinishedFurnitureDemolished()
    {
        ResetWorld();
        var island = new Island();

        const int y = 24;
        const int firstX = 14;
        int expectedRefund = 0;
        int x = firstX;

        foreach (BuildingType type in FurnitureTypes)
        {
            StructureDefinition def = StructureRegistry.Get(type);
            if (!StructureManager.Instance.AddStructure(x, y, def))
                return Fail($"could not place {def.Id} at ({x},{y}) — test is invalid");
            expectedRefund += def.GetRequiredCount(ItemId.Log);
            x++;
        }

        island.FillSolidWalls();
        for (int i = 0; i < FurnitureTypes.Length; i++)
        {
            if (island.IsSolid(firstX + i, y))
                return Fail($"furniture ({firstX + i},{y}) blocks movement — test is invalid");
        }

        int logsBefore = LogsOnGround();
        x = firstX;
        foreach (BuildingType type in FurnitureTypes)
        {
            StructureDefinition def = StructureRegistry.Get(type);
            BuildTool tool = NewTool(type);
            Demolish(tool, x, y, x, y);
            if (StructureManager.Instance.HasStructureAt(x, y))
                return Fail($"21-01: finished {def.Id} at ({x},{y}) survived RMB");
            x++;
        }

        int refund = LogsOnGround() - logsBefore;
        if (refund != expectedRefund)
            return Fail($"furniture demolition returned {refund} logs, expected {expectedRefund}");

        GD.Print($"{Tag} OK: {FurnitureTypes.Length} furniture kinds removed, {refund} logs refunded");
        return true;
    }

    /// <summary>
    /// (4) The island gate matches the world path (R-008): the wall tool removes walls only,
    /// a furniture tool removes furniture only. Otherwise the same RMB would behave
    /// differently on the two sides of the island edge.
    /// </summary>
    private bool ToolKindGateMatchesWorldPath()
    {
        ResetWorld();
        var island = new Island();

        if (!StructureManager.Instance.AddStructure(50, 30, StructureRegistry.WoodWall)
            || !StructureManager.Instance.AddStructure(51, 30, StructureRegistry.Bed))
            return Fail("could not place the gate-check pair — test is invalid");
        island.FillSolidWalls();

        Demolish(NewTool(BuildingType.Bed), 50, 30, 50, 30);
        if (!StructureManager.Instance.HasStructureAt(50, 30))
            return Fail("a furniture tool demolished a finished wall inside the island (world path does not)");
        if (!island.IsSolid(50, 30))
            return Fail("a standing wall lost its SolidWalls flag (path would go through it)");

        Demolish(NewTool(BuildingType.WoodWall), 51, 30, 51, 30);
        if (!StructureManager.Instance.HasStructureAt(51, 30))
            return Fail("the wall tool demolished furniture inside the island (world path does not)");

        GD.Print($"{Tag} OK: island demolition gate matches the world path (wall tool -> walls)");
        return true;
    }

    /// <summary>
    /// (5) The old path is not broken: a construction site (plan) and its delivery blueprint
    /// still lift on RMB, and the materials already delivered to the site still come back.
    /// </summary>
    private bool PlansAndBlueprintsStillLift()
    {
        ResetWorld();
        BuildTool tool = NewTool(BuildingType.WoodWall);

        var cells = new List<(int X, int Y)> { (30, 30), (31, 30) };
        if (ConstructionManager.Instance.StartSitesBatch(cells, StructureRegistry.WoodWall, null) == 0)
            return Fail("could not start the construction site batch — test is invalid");
        if (!BlueprintManager.Instance.IsBlueprintAt(30, 30)
            || !BlueprintManager.Instance.IsBlueprintAt(31, 30))
            return Fail("blueprints are missing right after StartSitesBatch — test is invalid");

        const int delivered = 5;
        ConstructionManager.Instance.DeliverMaterial(30, 30, ItemId.Log, delivered, out int accepted, out _);
        if (accepted != delivered)
            return Fail($"site accepted {accepted} logs of {delivered} — test is invalid");

        int logsBefore = LogsOnGround();
        Demolish(tool, 30, 30, 31, 30);

        if (ConstructionManager.Instance.IsSiteAt(30, 30) || ConstructionManager.Instance.IsSiteAt(31, 30))
            return Fail("RMB stopped lifting construction sites (old path broken)");
        if (BlueprintManager.Instance.IsBlueprintAt(30, 30)
            || BlueprintManager.Instance.IsBlueprintAt(31, 30))
            return Fail("RMB stopped lifting delivery blueprints (old path broken)");

        int refund = LogsOnGround() - logsBefore;
        if (refund != delivered)
            return Fail($"site refund changed: {refund} logs instead of {delivered}");

        GD.Print($"{Tag} OK: plan and blueprint still lift, {refund} delivered logs refunded");
        return true;
    }

    /// <summary>
    /// (6) RMB over empty cells is a silent no-op: no exception, no structure, no items.
    /// </summary>
    private bool EmptyCellIsNoOp()
    {
        ResetWorld();
        BuildTool tool = NewTool(BuildingType.WoodWall);
        int pilesBefore = GroundItemManager.Instance.TotalGroundPilesCount;

        try
        {
            Demolish(tool, 40, 40, 42, 40);
        }
        catch (Exception ex)
        {
            return Fail($"RMB over empty cells threw: {ex.Message}");
        }

        if (GroundItemManager.Instance.TotalGroundPilesCount != pilesBefore)
            return Fail("RMB over empty cells conjured ground items");

        for (int x = 40; x <= 42; x++)
        {
            if (StructureManager.Instance.HasStructureAt(x, 40))
                return Fail($"RMB over an empty cell created a structure at ({x},40)");
            if (ConstructionManager.Instance.IsSiteAt(x, 40))
                return Fail($"RMB over an empty cell created a site at ({x},40)");
        }

        GD.Print($"{Tag} OK: RMB over empty cells is a silent no-op");
        return true;
    }

    // ─────────────────────────────── utilities ───────────────────────────────

    private static bool Fail(string message)
    {
        GD.PrintErr($"{Tag} FAIL: {message}");
        return false;
    }

    private void ResetWorld()
    {
        StructureManager.Instance.ClearAll();
        ConstructionManager.Instance.ClearAll();
        GroundItemManager.Instance.ClearAll();
    }

    /// <summary>
    /// A headless test has no ghost TileMapLayer and no MapData: the RMB branch touches
    /// neither (it works on the structure/site stores only), so both are passed as null.
    /// </summary>
    private BuildTool NewTool(BuildingType type)
    {
        _wallManager ??= new WallBuildManager();
        return new BuildTool(_wallManager, null, null, type, 0);
    }

    /// <summary>One RMB gesture: press, then release on the same cell or over a dragged row.</summary>
    private static void Demolish(BuildTool tool, int startX, int startY, int endX, int endY)
    {
        tool.OnClick(new Vector2I(startX, startY), Vector2.Zero, false);
        tool.OnRelease(new Vector2I(startX, startY), new Vector2I(endX, endY), Vector2.Zero, false);
    }

    /// <summary>Logs available on the ground (every pile) — refunds are checked on this total.</summary>
    private static int LogsOnGround() => GroundItemManager.Instance.AvailableCountOf(ItemId.Log);

    /// <summary>
    /// Pristine 64×64 island world. The walkability array is shared with the SimulationContext
    /// exactly the way AgentSimulationThread does it (FillSolidWalls receives the very array
    /// the context keeps), so the test observes what the pathfinding thread would read.
    /// </summary>
    private sealed class Island
    {
        public readonly bool[,] Solid = new bool[MapSize, MapSize];
        public readonly SimulationContext Ctx;

        public Island()
        {
            var ground = new TileType[MapSize, MapSize];
            for (int x = 0; x < MapSize; x++)
            {
                for (int y = 0; y < MapSize; y++)
                    ground[x, y] = TileType.Grass;
            }

            Ctx = new SimulationContext(
                ground,
                null,
                new bool[MapSize, MapSize],
                Solid,
                new List<(int X, int Y)>(),
                new AgentSpatialGrid(MapSize, MapSize),
                new AgentMovementService(),
                new Random(2101));
        }

        /// <summary>Same call AgentSimulationThread.Start makes before creating its context.</summary>
        public int FillSolidWalls()
            => StructureManager.Instance.FillSolidWalls(Solid, MapSize, MapSize);

        public bool IsSolid(int x, int y) => Ctx.SolidWalls[x, y];
    }
}