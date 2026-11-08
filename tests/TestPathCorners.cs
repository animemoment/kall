using System;
using System.Collections.Generic;
using Godot;
using Game.Core;
using Game.Core.World;
using Game.Core.WorldStreaming;
using Game.Simulation;

namespace Game.Tests;

/// <summary>
/// 05-06 (path layer) / 07-04 (agent layer): one corner-cutting rule for both.
///
/// A diagonal step is legal only when BOTH orthogonal corner cells are passable.
/// Before the fix:
///   • ComputeLocalSegment checked only the destination cell, so A* (and the
///     direct LOS / smoothing) returned (0,0)->(1,1) between walls (1,0)/(0,1) —
///     the destination cell (1,1) was free and the two shared corners were never
///     read;
///   • IsTileBlocked checked only four orthogonal neighbours, so at 100x speed a
///     60 px step from (32,32) to (74,74) teleported through the same joint: both
///     the start and the destination points were free.
///
/// Checks:
///   1. walls (1,0)+(0,1), start (0,0), goal (1,1): no direct diagonal. The start is
///      enclosed by the two walls and the map border, so an empty path is valid;
///      if a detour ever appears, every step is validated against the corner rule.
///   2. a diagonal with free corners is still allowed (normal case not broken).
///   3. AgentMovementService.IsTileBlocked + IsStepBlocked agree with the path
///      layer: the destination point (1,1) is free, while the swept step is blocked.
///      The same case is driven through the public MoveTowards (100x step): the
///      agent must not teleport from (32,32) to (74,74).
///   4. negative tiles and the 1020..1028 edge behave symmetrically (no exception,
///      guard counts, mirrored wall pairs at both ends of the 1024 island).
///
/// Run: godot --headless --path . --script res://tests/run_path_corners.gd
/// </summary>
public partial class TestPathCorners : RefCounted
{
    private const int Small = 64;
    private const int Island = 1024;
    private const float TileSize = 64f;
    private const float HalfTile = 32f;

    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print("[PATH-CORNERS] 05-06 / 07-04: диагональ через стык двух стен");
        GD.Print("==================================================");

        WorldModel.Reset();

        bool ok = true;
        ok &= CornerPairBlocksDirectDiagonal();
        ok &= FreeDiagonalStillAllowed();
        ok &= AgentStepMatchesPath();
        ok &= NegativeAndSeamAreSymmetric();

        GD.Print("==================================================");
        GD.Print(ok
            ? "[PATH-CORNERS] ALL CORNER TESTS PASSED! (Code 0)"
            : "[PATH-CORNERS] FAIL");
        GD.Print("==================================================");
        return ok;
    }

    // ------------------------------------------------------------------
    // 1. Stair-step walls: (1,0) and (0,1), start (0,0), goal (1,1)
    // ------------------------------------------------------------------

    private bool CornerPairBlocksDirectDiagonal()
    {
        GD.Print("[PATH-CORNERS] --- (1) стены (1,0)+(0,1), старт (0,0), цель (1,1) ---");

        HierarchicalPathfinder pf = HierarchicalPathfinder.Instance;
        try
        {
            byte[] cost = EmptyCost(Small, Small);
            Block(cost, Small, 1, 0);
            Block(cost, Small, 0, 1);

            // Proof that this grid really is the old corner-cut case: the OLD LOS
            // (cell checks only) returned true for the direct diagonal.
            bool oldDirect = OldBresenhamPasses(cost, Small, Small, 0, 0, 1, 1);
            if (!oldDirect)
            {
                GD.PrintErr("[PATH-CORNERS] FAIL (1): сетка не воспроизводит старый corner-cut " +
                            "(старый LOS не проходит) — проверь индексацию клеток");
                return false;
            }

            pf.InitializeCost(cost, Small, Small);
            bool found = pf.TryFindPathScratch(0, 0, 1, 1, out int[] path, out int count);

            if (!found)
            {
                if (!StartIsEnclosed(cost, Small, Small, 0, 0))
                {
                    GD.PrintErr("[PATH-CORNERS] FAIL (1): пути нет, хотя из (0,0) есть обход — " +
                                "диагональ запрещена слишком широко");
                    return false;
                }

                GD.Print("[PATH-CORNERS] OK (1): пути нет — (0,0) заперт стенами (1,0)/(0,1) и " +
                         "кромкой; прямой диагонали не выдано (старый код вернул бы LOS (0,0)->(1,1))");
                return true;
            }

            if (count == 0)
            {
                GD.PrintErr("[PATH-CORNERS] FAIL (1): прямой LOS (0,0)->(1,1) снова прошёл сквозь стык " +
                            "стен (1,0)/(0,1): клетка (1,1) проходила, ортогональные углы не проверялись");
                return false;
            }

            if (!ValidateNoCornerCut(path, count, cost, Small, Small, 0, 0, 1, 1))
                return false;

            GD.Print($"[PATH-CORNERS] OK (1): найден обход из {count} точек, ни один шаг не режет угол");
            return true;
        }
        finally
        {
            pf.Reset();
        }
    }

    // ------------------------------------------------------------------
    // 2. Free corners: the diagonal stays allowed
    // ------------------------------------------------------------------

    private bool FreeDiagonalStillAllowed()
    {
        GD.Print("[PATH-CORNERS] --- (2) свободные углы: диагональ разрешена ---");

        HierarchicalPathfinder pf = HierarchicalPathfinder.Instance;
        try
        {
            byte[] cost = EmptyCost(Small, Small);
            pf.InitializeCost(cost, Small, Small);

            bool ok = true;

            bool shortDiag = pf.TryFindPathScratch(5, 5, 6, 6, out int[] shortPath, out int shortCount);
            ok &= Expect(shortDiag, "(2) короткая диагональ (5,5)->(6,6) разрешена");
            if (shortDiag && shortCount > 0)
                ok &= ValidateNoCornerCut(shortPath, shortCount, cost, Small, Small, 5, 5, 6, 6);

            bool longDiag = pf.TryFindPathScratch(5, 5, 20, 20, out int[] longPath, out int longCount);
            ok &= Expect(longDiag, "(2) длинная диагональ (5,5)->(20,20) разрешена");
            if (longDiag && longCount > 0)
                ok &= ValidateNoCornerCut(longPath, longCount, cost, Small, Small, 5, 5, 20, 20);

            bool negativeDx = pf.TryFindPathScratch(20, 5, 5, 20, out int[] negPath, out int negCount);
            ok &= Expect(negativeDx, "(2) диагональ с отрицательным dx (20,5)->(5,20) разрешена");
            if (negativeDx && negCount > 0)
                ok &= ValidateNoCornerCut(negPath, negCount, cost, Small, Small, 20, 5, 5, 20);

            bool rule = HierarchicalPathfinder.IsDiagonalPassable(true, true)
                && !HierarchicalPathfinder.IsDiagonalPassable(true, false)
                && !HierarchicalPathfinder.IsDiagonalPassable(false, true)
                && !HierarchicalPathfinder.IsDiagonalPassable(false, false);
            if (!rule)
            {
                GD.PrintErr("[PATH-CORNERS] FAIL (2): IsDiagonalPassable не соответствует правилу " +
                            "«разрешено только если ОБА угла проходимы»");
                ok = false;
            }
            else
            {
                GD.Print("[PATH-CORNERS] OK (2): предикат требует оба угла; нормальная диагональ идёт напрямую");
            }

            return ok;
        }
        finally
        {
            pf.Reset();
        }
    }

    // ------------------------------------------------------------------
    // 3. Agent step: the same answer as the path layer
    // ------------------------------------------------------------------

    private bool AgentStepMatchesPath()
    {
        GD.Print("[PATH-CORNERS] --- (3) агентский шаг: IsTileBlocked + IsStepBlocked ---");

        HierarchicalPathfinder pf = HierarchicalPathfinder.Instance;
        (int X, int Y)[] walls = { (1, 0), (0, 1), (2, 1) };
        SimulationContext ctx = NewContext(Small, Small, walls);

        bool ok = true;
        try
        {
            byte[] cost = EmptyCost(Small, Small);
            Block(cost, Small, 1, 0);
            Block(cost, Small, 0, 1);
            Block(cost, Small, 2, 1);

            pf.InitializeCost(cost, Small, Small);
            // Build the overlay first: the agent must read the SAME cost grid as the pathfinder.
            pf.TryFindPathScratch(0, 0, 1, 1, out _, out _);

            AgentMovementService move = ctx.Movement;

            // Cell (1,1) itself is free; only the swept diagonal through its corner is illegal.
            // At a normal speed the body margin already rejects the near-corner point
            // (subX/subY < 8), so the defect is specifically the long 100x step.
            bool destPoint = move.IsTileBlocked(74f, 74f, ctx);
            bool nearCornerPoint = move.IsTileBlocked(70f, 70f, ctx);
            bool cornerStep = move.IsStepBlocked(32f, 32f, 74f, 74f, ctx);

            if (!nearCornerPoint)
            {
                GD.PrintErr("[PATH-CORNERS] FAIL (3): point query (70,70) не видит соседнюю стену — " +
                            "сломан обычный margin-путь");
                ok = false;
            }

            if (destPoint)
            {
                GD.PrintErr("[PATH-CORNERS] FAIL (3): клетка (1,1) сама по себе блокирована — " +
                            "тест не про corner-cut");
                ok = false;
            }
            if (!cornerStep)
            {
                GD.PrintErr("[PATH-CORNERS] FAIL (3): шаг (0,0)->(1,1) сквозь стык стен (1,0)/(0,1) " +
                            "разрешён; старая проверка точки пропускала клетку (1,1)");
                ok = false;
            }

            // Behavioral proof on the PUBLIC API: on the old code this exact call
            // teleported the agent to (74,74) because only the destination point was
            // checked; the old test therefore goes red without needing a new method.
            var pool = new AgentDataPool(1);
            pool.PositionX[0] = 32f;
            pool.PositionY[0] = 32f;
            pool.StuckTimer[0] = 0f;
            bool arrived = move.MoveTowards(
                0, new System.Numerics.Vector2(74f, 74f), 16f, 0.5f, pool, ctx);
            if (arrived || pool.PositionX[0] != 32f || pool.PositionY[0] != 32f)
            {
                GD.PrintErr($"[PATH-CORNERS] FAIL (3): MoveTowards 100x (32,32)->(74,74): " +
                            $"arrived={arrived}, pos=({pool.PositionX[0]},{pool.PositionY[0]}) — " +
                            "агент перепрыгнул стык стен (1,0)/(0,1)");
                ok = false;
            }

            // Normal diagonal with free corners must stay allowed.
            if (move.IsStepBlocked(5 * TileSize + HalfTile, 5 * TileSize + HalfTile,
                    6 * TileSize + HalfTile, 6 * TileSize + HalfTile, ctx))
            {
                GD.PrintErr("[PATH-CORNERS] FAIL (3): свободная диагональ (5,5)->(6,6) запрещена");
                ok = false;
            }

            // One blocked corner is enough: (1,1)->(2,2), wall (2,1).
            if (!move.IsStepBlocked(1 * TileSize + HalfTile, 1 * TileSize + HalfTile,
                    2 * TileSize + HalfTile, 2 * TileSize + HalfTile, ctx))
            {
                GD.PrintErr("[PATH-CORNERS] FAIL (3): шаг (1,1)->(2,2) разрешён, хотя угол (2,1) блокирован");
                ok = false;
            }

            // Path and agent agree on the tested pair: both forbid it.
            bool pathFound = pf.TryFindPathScratch(0, 0, 1, 1, out _, out _);
            if (pathFound)
            {
                GD.PrintErr("[PATH-CORNERS] FAIL (3): путь (0,0)->(1,1) найден, а шаг агента запрещён — " +
                            "слои разъехались");
                ok = false;
            }

            if (ok)
            {
                GD.Print("[PATH-CORNERS] OK (3): точка (1,1) свободна, но шаг через её углы запрещён; " +
                         "свободная диагональ разрешена; путь и агент согласованы");
            }

            // Fallback branch (overlay not built): the same rule from SolidWalls.
            pf.Reset();
            bool fallbackStep = move.IsStepBlocked(32f, 32f, 74f, 74f, ctx);
            if (!fallbackStep)
            {
                GD.PrintErr("[PATH-CORNERS] FAIL (3): в fallback-ветке (без оверлея) шаг сквозь угол " +
                            "снова разрешён");
                ok = false;
            }
            else
            {
                GD.Print("[PATH-CORNERS] OK (3): fallback-ветка (SolidWalls) даёт то же правило");
            }

            return ok;
        }
        finally
        {
            pf.Reset();
        }
    }

    // ------------------------------------------------------------------
    // 4. Negative tiles and the 1020..1028 edge
    // ------------------------------------------------------------------

    private bool NegativeAndSeamAreSymmetric()
    {
        GD.Print("[PATH-CORNERS] --- (4) отрицательные клетки и кромка 1020..1028 ---");

        HierarchicalPathfinder pf = HierarchicalPathfinder.Instance;
        bool ok = true;
        try
        {
            byte[] cost = EmptyCost(Island, Island);
            // Mirror of the same corner pair at both ends of the 1024 island.
            Block(cost, Island, 1, 0);
            Block(cost, Island, 0, 1);
            Block(cost, Island, 1022, 1023);
            Block(cost, Island, 1023, 1022);

            pf.InitializeCost(cost, Island, Island);

            bool nearOrigin = pf.TryFindPathScratch(0, 0, 1, 1, out _, out _);
            bool nearSeam = pf.TryFindPathScratch(1022, 1022, 1023, 1023, out _, out _);
            if (nearOrigin || nearSeam)
            {
                GD.PrintErr($"[PATH-CORNERS] FAIL (4): corner-cut у кромки: origin={nearOrigin}, " +
                            $"seam={nearSeam}; ожидалось false на обоих концах (поведение симметрично)");
                ok = false;
            }
            else
            {
                GD.Print("[PATH-CORNERS] OK (4): (0,0)->(1,1) и (1022,1022)->(1023,1023) одинаково запрещены");
            }

            long before = pf.FailOutOfBounds;
            bool negForward = pf.TryFindPathScratch(-1, -1, 1, 1, out _, out _);
            bool negBackward = pf.TryFindPathScratch(1, 1, -1, -1, out _, out _);
            bool beyond = pf.TryFindPathScratch(1022, 1022, 1025, 1025, out _, out _);
            long delta = pf.FailOutOfBounds - before;
            if (negForward || negBackward || beyond || delta != 3)
            {
                GD.PrintErr($"[PATH-CORNERS] FAIL (4): гард границ: forward={negForward}, " +
                            $"backward={negBackward}, beyond={beyond}, delta={delta} " +
                            "(ожидалось false/false/false/3)");
                ok = false;
            }
            else
            {
                GD.Print("[PATH-CORNERS] OK (4): (-1,-1)<->(1,1) и (1025,1025) отсечены гардом, без исключений");
            }

            (int X, int Y)[] walls =
            {
                (1, 0), (0, 1), (1022, 1023), (1023, 1022)
            };
            SimulationContext ctx = NewContext(Island, Island, walls);

            // The 1024 overlay is already built above: the agent resolves via the same grid.
            AgentMovementService move = ctx.Movement;
            bool nearStep = move.IsStepBlocked(
                0 * TileSize + HalfTile, 0 * TileSize + HalfTile,
                1 * TileSize + 10f, 1 * TileSize + 10f, ctx);
            bool seamStep = move.IsStepBlocked(
                1022 * TileSize + HalfTile, 1022 * TileSize + HalfTile,
                1023 * TileSize + 10f, 1023 * TileSize + 10f, ctx);
            if (!nearStep || !seamStep)
            {
                GD.PrintErr($"[PATH-CORNERS] FAIL (4): агентский шаг: near={nearStep}, seam={seamStep}; " +
                            "ожидалось true на обоих концах кромки");
                ok = false;
            }
            else
            {
                GD.Print("[PATH-CORNERS] OK (4): агентский шаг симметрично запрещён у (0,0) и у (1022,1022)");
            }

            // Negative side: no exception, and the answer must match the same corner
            // rule applied to the terrain that the world door reports for the corners.
            // (Destination (0,0) is inside the island and free; only the two outside
            // corner cells decide the diagonal step.)
            bool negCornerX = !OutsideBlocked(0, -1);
            bool negCornerY = !OutsideBlocked(-1, 0);
            bool expectedNeg = !HierarchicalPathfinder.IsDiagonalPassable(negCornerX, negCornerY);
            bool actualNeg = move.IsStepBlocked(-32f, -32f, 10f, 10f, ctx);
            if (actualNeg != expectedNeg)
            {
                GD.PrintErr($"[PATH-CORNERS] FAIL (4): шаг (-1,-1)->(0,0) = {actualNeg}, " +
                            $"по правилу углов ожидалось {expectedNeg}");
                ok = false;
            }
            else
            {
                GD.Print($"[PATH-CORNERS] OK (4): отрицательная клетка (-1,-1) обработана по тому же " +
                         $"правилу углов (blocked={actualNeg}), без исключения");
            }

            return ok;
        }
        finally
        {
            pf.Reset();
            WorldModel.Reset();
        }
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static bool Expect(bool value, string label)
    {
        if (value)
        {
            GD.Print($"[PATH-CORNERS] OK {label}");
            return true;
        }

        GD.PrintErr($"[PATH-CORNERS] FAIL {label}");
        return false;
    }

    private static byte[] EmptyCost(int w, int h)
    {
        var cost = new byte[w * h];
        Array.Fill(cost, HierarchicalPathfinder.CostGrass);
        return cost;
    }

    private static void Block(byte[] cost, int w, int x, int y)
        => cost[y * w + x] = HierarchicalPathfinder.CostBlocked;

    private static bool InBounds(int x, int y, int w, int h)
        => (uint)x < (uint)w && (uint)y < (uint)h;

    private static bool CellFree(byte[] cost, int w, int h, int x, int y)
        => InBounds(x, y, w, h) && cost[y * w + x] != HierarchicalPathfinder.CostBlocked;

    /// <summary>
    /// Can the agent leave (sx,sy) at all? Orthogonal free neighbours are an escape;
    /// a free diagonal neighbour is an escape only when both of its corners are free
    /// (the same rule under test).
    /// </summary>
    private static bool StartIsEnclosed(byte[] cost, int w, int h, int sx, int sy)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0)
                    continue;

                int nx = sx + dx;
                int ny = sy + dy;
                if (!CellFree(cost, w, h, nx, ny))
                    continue;

                if (dx == 0 || dy == 0)
                    return false;

                bool cornerX = CellFree(cost, w, h, nx, sy);
                bool cornerY = CellFree(cost, w, h, sx, ny);
                if (HierarchicalPathfinder.IsDiagonalPassable(cornerX, cornerY))
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// OLD LOS: Bresenham cell checks only, no corner rule (the bug). Used to prove
    /// that the test grid is exactly the card's corner-cut case.
    /// </summary>
    private static bool OldBresenhamPasses(byte[] cost, int w, int h, int x0, int y0, int x1, int y1)
    {
        int dx = Math.Abs(x1 - x0);
        int dy = Math.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;

        while (true)
        {
            if (!CellFree(cost, w, h, x0, y0))
                return false;
            if (x0 == x1 && y0 == y1)
                return true;

            int e2 = err << 1;
            if (e2 > -dy) { err -= dy; x0 += sx; }
            if (e2 < dx) { err += dx; y0 += sy; }
        }
    }

    /// <summary>
    /// Independent validator for a returned path: every point is passable, the last
    /// point is the goal, and every Bresenham step of every segment obeys the shared
    /// corner rule. <paramref name="path"/> excludes the start point.
    /// </summary>
    private static bool ValidateNoCornerCut(int[] path, int count, byte[] cost,
        int w, int h, int sx, int sy, int tx, int ty)
    {
        if (path == null || count <= 0 || count > path.Length)
        {
            GD.PrintErr($"[PATH-CORNERS] FAIL: пустой/битый путь ({count} точек)");
            return false;
        }

        int lastX = sx;
        int lastY = sy;

        for (int i = 0; i < count; i++)
        {
            int px = path[i] % w;
            int py = path[i] / w;

            if (!CellFree(cost, w, h, px, py))
            {
                GD.PrintErr($"[PATH-CORNERS] FAIL: точка пути ({px},{py}) непроходима");
                return false;
            }

            if (!SegmentCornerSafe(cost, w, h, lastX, lastY, px, py))
            {
                GD.PrintErr($"[PATH-CORNERS] FAIL: отрезок ({lastX},{lastY})->({px},{py}) " +
                            "проходит по диагонали через стык двух стен");
                return false;
            }

            lastX = px;
            lastY = py;
        }

        if (lastX != tx || lastY != ty)
        {
            GD.PrintErr($"[PATH-CORNERS] FAIL: путь закончился в ({lastX},{lastY}), цель ({tx},{ty})");
            return false;
        }

        return true;
    }

    /// <summary>Same Bresenham walk as the pathfinder, with the corner rule.</summary>
    private static bool SegmentCornerSafe(byte[] cost, int w, int h,
        int x0, int y0, int x1, int y1)
    {
        int dx = Math.Abs(x1 - x0);
        int dy = Math.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;

        while (true)
        {
            if (!CellFree(cost, w, h, x0, y0))
                return false;
            if (x0 == x1 && y0 == y1)
                return true;

            int e2 = err << 1;
            int nx = x0;
            int ny = y0;
            if (e2 > -dy) { err -= dy; nx += sx; }
            if (e2 < dx) { err += dx; ny += sy; }

            if (nx != x0 && ny != y0
                && !HierarchicalPathfinder.IsDiagonalPassable(
                    CellFree(cost, w, h, nx, y0), CellFree(cost, w, h, x0, ny)))
                return false;

            x0 = nx;
            y0 = ny;
        }
    }

    private static bool OutsideBlocked(int tx, int ty)
    {
        // Mirror of AgentMovementService.IsCellBlocked for tiles beyond the island:
        // no configured world means "not walkable", otherwise ask the world door.
        if (WorldModel.Seed == 0)
            return true;

        long wx = UnifiedWorldGenerator.WorldOriginX + tx;
        long wy = UnifiedWorldGenerator.WorldOriginY + ty;
        var terrain = WorldModel.TerrainAt(wx, wy);
        return terrain == BaseTerrainKind.Water || terrain == BaseTerrainKind.Mountain;
    }

    private static SimulationContext NewContext(int w, int h, (int X, int Y)[] walls)
    {
        var ground = new TileType[w, h];
        var trees = new bool[w, h];
        var solid = new bool[w, h];

        for (int i = 0; i < walls.Length; i++)
        {
            int x = walls[i].X;
            int y = walls[i].Y;
            if (InBounds(x, y, w, h))
                solid[x, y] = true;
        }

        var walkable = new List<(int X, int Y)>();
        return new SimulationContext(
            ground,
            null,
            trees,
            solid,
            walkable,
            new AgentSpatialGrid(1, 1),
            new AgentMovementService(),
            new Random(42),
            64);
    }
}