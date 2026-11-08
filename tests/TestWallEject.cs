using System;
using System.Collections.Generic;
using Godot;
using Game.Core;
using Game.Core.World;
using Game.Simulation;

namespace Game.Tests;

/// <summary>
/// 07-09: finishing a wall may eject ONLY the agent that really stands in that cell.
///
/// How it broke: <c>EjectFromWall</c> moved whoever the caller pointed at without looking at
/// the agent's position. <c>StructureManager</c> walks the spatial grid (rebuilt once per tick)
/// and <c>ConstructionJobHandler</c> passes the site cell from <c>TargetCellX/Y</c>; both are
/// snapshots. An agent that had already stepped into the neighbouring cell was teleported back
/// next to the new wall.
///
/// Contract checked here: the decision is made from <c>PositionX/Y</c>, a stale cell (spatial
/// grid snapshot or <c>CurrentCellX/Y</c>) never moves anybody, and the landing tile is free
/// and inside the colony island. Case 1 (the real occupant is ejected) must keep working;
/// cases 2, 3a, 4b, 4c, 4d and 5 are red on the old code.
///
/// Run: godot --headless --path . --script res://tests/run_wall_eject.gd
/// </summary>
public partial class TestWallEject : RefCounted
{
    // Same tile geometry the simulation uses: TileShift = 6 => 64 px.
    private const int TilePx = 64;
    private const int TileShift = 6;

    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print("[WALL-EJECT] 07-09: a stale wall cell must not teleport agents");
        GD.Print("==================================================");

        bool ok = true;
        try
        {
            ok &= OccupantIsEjected();
            ok &= DepartedAgentIsNotTouched();
            ok &= StaleCellCacheIsIgnored();
            ok &= IslandEdgeAndNegativeCells();
            ok &= StaleSpatialGridDoesNotTeleport();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[WALL-EJECT] FAIL: unexpected exception: {ex}");
            return false;
        }

        GD.Print(ok
            ? "[WALL-EJECT] OK: only the real occupant is ejected, always inside the island"
            : "[WALL-EJECT] FAIL: see errors above");
        return ok;
    }

    // ---------------------------------------------------------------- case 1
    private bool OccupantIsEjected()
    {
        MakeWorld(160, 160, 2, out var ctx, out var pool);
        const int wx = 100, wy = 100;

        ctx.SolidWalls[wx, wy] = true; // the wall has just been completed
        Place(pool, 0, wx, wy);
        pool.WaypointCount[0] = 3;
        pool.StuckTimer[0] = 5f;

        ctx.Movement.EjectFromWall(0, wx, wy, pool, ctx);

        int nx = CellOf(pool.PositionX[0]);
        int ny = CellOf(pool.PositionY[0]);

        bool ok = true;
        ok &= Check(nx != wx || ny != wy, $"agent stayed inside the finished wall cell ({nx},{ny})");
        ok &= Check(Math.Abs(nx - wx) <= 1 && Math.Abs(ny - wy) <= 1,
            $"ejection is not to a neighbouring cell: ({nx},{ny}) from wall ({wx},{wy})");
        ok &= Check(WorldModel.IsInsideIsland(nx, ny), $"ejected outside the island: ({nx},{ny})");
        ok &= Check(IsFreeGrass(ctx, nx, ny), $"ejected onto a blocked cell: ({nx},{ny})");
        ok &= Check(pool.CurrentCellX[0] == nx && pool.CurrentCellY[0] == ny,
            "the cell cache was not updated together with the position");
        ok &= Check(pool.TargetPositionX[0] == pool.PositionX[0] && pool.TargetPositionY[0] == pool.PositionY[0],
            "the movement target stayed behind in the wall cell");
        ok &= Check(pool.WaypointCount[0] == 0 && pool.WaypointIndex[0] == 0,
            "stale waypoints survived the ejection");
        ok &= Check(pool.StuckTimer[0] == 0f, "the stuck timer was not reset");
        return ok;
    }

    // ---------------------------------------------------------------- case 2
    private bool DepartedAgentIsNotTouched()
    {
        MakeWorld(160, 160, 2, out var ctx, out var pool);
        const int wx = 100, wy = 100;

        ctx.SolidWalls[wx, wy] = true;
        Place(pool, 0, wx + 1, wy); // the agent has already stepped into the next cell

        float posX = pool.PositionX[0], posY = pool.PositionY[0];
        float targetX = pool.TargetPositionX[0], targetY = pool.TargetPositionY[0];

        // The spatial-grid snapshot still reports the agent on the wall cell.
        ctx.Movement.EjectFromWall(0, wx, wy, pool, ctx);

        bool ok = true;
        ok &= Check(pool.PositionX[0] == posX && pool.PositionY[0] == posY,
            "[RED on old code] an agent that had already left the cell was teleported");
        ok &= Check(pool.TargetPositionX[0] == targetX && pool.TargetPositionY[0] == targetY,
            "the movement target of a departed agent was rewritten");
        ok &= Check(pool.CurrentCellX[0] == wx + 1 && pool.CurrentCellY[0] == wy,
            "the cell cache of a departed agent was overwritten");
        return ok;
    }

    // ---------------------------------------------------------------- case 3
    private bool StaleCellCacheIsIgnored()
    {
        MakeWorld(160, 160, 2, out var ctx, out var pool);
        const int wx = 100, wy = 100;
        ctx.SolidWalls[wx, wy] = true;

        bool ok = true;

        // 3a: the cache still says "wall cell", the position says "next cell" => position wins.
        Place(pool, 0, wx + 1, wy);
        pool.CurrentCellX[0] = wx;
        pool.CurrentCellY[0] = wy;
        float posX = pool.PositionX[0], posY = pool.PositionY[0];
        ctx.Movement.EjectFromWall(0, wx, wy, pool, ctx);
        ok &= Check(pool.PositionX[0] == posX && pool.PositionY[0] == posY,
            "[RED on old code] a stale CurrentCell moved an agent that stands next door");

        // 3b: the cache says "next cell", the position says "wall cell" => still ejected.
        Place(pool, 0, wx, wy);
        pool.CurrentCellX[0] = wx + 1;
        pool.CurrentCellY[0] = wy;
        ctx.Movement.EjectFromWall(0, wx, wy, pool, ctx);
        int nx = CellOf(pool.PositionX[0]);
        int ny = CellOf(pool.PositionY[0]);
        ok &= Check(nx != wx || ny != wy, "a stale CurrentCell kept the real occupant inside the wall");
        ok &= Check(WorldModel.IsInsideIsland(nx, ny), $"ejected outside the island: ({nx},{ny})");
        return ok;
    }

    // ---------------------------------------------------------------- case 4
    private bool IslandEdgeAndNegativeCells()
    {
        bool ok = true;
        const int edge = WorldModel.IslandTiles - 1;

        MakeWorld(WorldModel.IslandTiles, WorldModel.IslandTiles, 4, out var island, out var pool);

        // 4a: island corner — the occupant is ejected and stays on the island.
        island.SolidWalls[edge, edge] = true;
        Place(pool, 0, edge, edge);
        island.Movement.EjectFromWall(0, edge, edge, pool, island);
        int ax = CellOf(pool.PositionX[0]);
        int ay = CellOf(pool.PositionY[0]);
        ok &= Check(ax != edge || ay != edge, "the corner occupant stayed inside the finished wall cell");
        ok &= Check(WorldModel.IsInsideIsland(ax, ay), $"corner ejection left the island: ({ax},{ay})");
        ok &= Check(IsFreeGrass(island, ax, ay), $"corner ejection landed on a blocked cell: ({ax},{ay})");

        // 4b: at the corner the only free cells left are OUTSIDE the island (the arrays are
        //     wider than the island). The agent must not be thrown over the border.
        MakeWorld(WorldModel.IslandTiles + 6, WorldModel.IslandTiles + 6, 2, out var wide, out var widePool);
        wide.SolidWalls[edge, edge] = true;
        wide.SolidWalls[edge - 1, edge - 1] = true;
        wide.SolidWalls[edge - 1, edge] = true;
        wide.SolidWalls[edge, edge - 1] = true;
        Place(widePool, 0, edge, edge);
        float wideX = widePool.PositionX[0], wideY = widePool.PositionY[0];
        wide.Movement.EjectFromWall(0, edge, edge, widePool, wide);
        ok &= Check(widePool.PositionX[0] == wideX && widePool.PositionY[0] == wideY,
            "[RED on old code] the agent was ejected outside the island " +
            $"({CellOf(widePool.PositionX[0])},{CellOf(widePool.PositionY[0])})");
        ok &= Check(WorldModel.IsInsideIsland(CellOf(widePool.PositionX[0]), CellOf(widePool.PositionY[0])),
            "the agent ended up outside the island");

        // 4c: cells beyond the island belong to the world layer — a wall there must neither
        //     throw nor drag a world agent onto the island.
        ok &= Check(WorldCellIsNoOp(wide, widePool, 1, WorldModel.IslandTiles, WorldModel.IslandTiles),
            "wall cell 1024 moved an agent");
        ok &= Check(WorldCellIsNoOp(wide, widePool, 1, WorldModel.IslandTiles + 4, WorldModel.IslandTiles),
            "wall cell 1028 moved an agent");

        // 4d: negative cells (the local canvas can go below zero) — no exception, no teleport.
        Place(pool, 1, -1, -1);
        float negX = pool.PositionX[1], negY = pool.PositionY[1];
        island.Movement.EjectFromWall(1, -1, -1, pool, island);
        ok &= Check(pool.PositionX[1] == negX && pool.PositionY[1] == negY,
            "a negative wall cell teleported the agent");
        ok &= Check(CellOf(pool.PositionX[1]) == -1 && CellOf(pool.PositionY[1]) == -1,
            "the negative pixel -> cell conversion changed the agent cell");
        return ok;
    }

    // ---------------------------------------------------------------- case 5
    private bool StaleSpatialGridDoesNotTeleport()
    {
        MakeWorld(160, 160, 2, out var ctx, out var pool);
        const int wx = 120, wy = 120;

        // The builder finished the wall and stepped into the next cell...
        Place(pool, 0, wx + 1, wy);
        // ...but the spatial grid snapshot (rebuilt once per tick) still lists it on the wall.
        ctx.SpatialGrid.Insert(0, wx, wy, pool);

        float posX = pool.PositionX[0], posY = pool.PositionY[0];

        StructureManager.Instance.ClearAll(ctx);
        bool added = StructureManager.Instance.AddStructure(wx, wy, StructureRegistry.WoodWall, ctx, pool);
        bool ok = Check(added, "the wall was not registered (test setup problem)");
        ok &= Check(pool.PositionX[0] == posX && pool.PositionY[0] == posY,
            "[RED on old code] completing the wall teleported an agent that had already left the cell");

        // The opposite direction must still work end-to-end: the REAL occupant is ejected.
        const int wx2 = 130, wy2 = 130;
        Place(pool, 0, wx2, wy2);
        ctx.SpatialGrid.Clear();
        ctx.SpatialGrid.Insert(0, wx2, wy2, pool);
        bool added2 = StructureManager.Instance.AddStructure(wx2, wy2, StructureRegistry.WoodWall, ctx, pool);
        ok &= Check(added2, "the second wall was not registered (test setup problem)");
        int ex = CellOf(pool.PositionX[0]);
        int ey = CellOf(pool.PositionY[0]);
        ok &= Check(ex != wx2 || ey != wy2, "the real occupant was not ejected from the completed wall");
        ok &= Check(WorldModel.IsInsideIsland(ex, ey),
            $"ejection through StructureManager left the island: ({ex},{ey})");

        StructureManager.Instance.ClearAll(ctx);
        return ok;
    }

    // ---------------------------------------------------------------- helpers
    private static void MakeWorld(int width, int height, int agents,
        out SimulationContext ctx, out AgentDataPool pool)
    {
        var ground = new TileType[width, height];
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
                ground[x, y] = TileType.Grass;
        }

        var treeOnGrass = new bool[width, height];
        var solidWalls = new bool[width, height];
        var walkableTiles = new List<(int X, int Y)>();
        var spatialGrid = new AgentSpatialGrid(width, height);
        var movement = new AgentMovementService();
        pool = new AgentDataPool(agents);
        ctx = new SimulationContext(ground, null, treeOnGrass, solidWalls, walkableTiles,
            spatialGrid, movement, new Random(7), TilePx);
    }

    private static void Place(AgentDataPool pool, int index, int cellX, int cellY)
    {
        float px = cellX * TilePx + TilePx / 2f;
        float py = cellY * TilePx + TilePx / 2f;
        pool.PositionX[index] = px;
        pool.PositionY[index] = py;
        pool.TargetPositionX[index] = px;
        pool.TargetPositionY[index] = py;
        pool.CurrentCellX[index] = cellX;
        pool.CurrentCellY[index] = cellY;
        pool.WaypointCount[index] = 0;
        pool.WaypointIndex[index] = 0;
        pool.StuckTimer[index] = 0f;
    }

    /// <summary>Same pixels -> cell conversion the simulation uses (`(int)pos >> 6`).</summary>
    private static int CellOf(float pixel) => (int)pixel >> TileShift;

    private static bool IsFreeGrass(SimulationContext ctx, int x, int y) =>
        (uint)x < (uint)ctx.MapWidth && (uint)y < (uint)ctx.MapHeight
        && ctx.Ground[x, y] == TileType.Grass
        && !ctx.SolidWalls[x, y]
        && (ctx.TreeOnGrass == null || !ctx.TreeOnGrass[x, y]);

    /// <summary>Puts the agent on a world cell and expects the island eject service to be a no-op.</summary>
    private static bool WorldCellIsNoOp(SimulationContext ctx, AgentDataPool pool, int agent, int cellX, int cellY)
    {
        Place(pool, agent, cellX, cellY);
        float px = pool.PositionX[agent], py = pool.PositionY[agent];
        ctx.Movement.EjectFromWall(agent, cellX, cellY, pool, ctx);
        return pool.PositionX[agent] == px
            && pool.PositionY[agent] == py
            && pool.CurrentCellX[agent] == cellX
            && pool.CurrentCellY[agent] == cellY;
    }

    private static bool Check(bool condition, string failure)
    {
        if (!condition)
            GD.PrintErr($"[WALL-EJECT] FAIL: {failure}");
        return condition;
    }
}