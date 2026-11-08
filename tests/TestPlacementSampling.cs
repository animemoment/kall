using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Game.Core.WorldStreaming;
using Game.Core.WorldStreaming.Integration;

namespace Game.Tests;

/// <summary>
/// Audit 11-01 regression: the placement gate of StreamingWorldManager must ask for the
/// SAME cell the chunk generator renders (16×16 climate lattice anchored on absolute
/// coordinates), not a direct generator sample with a different climate.
///
/// Coordinates are LOCAL world cells (the gate adds WorldOrigin itself). The check runs
/// on both sides of the island edge (1016..1032 covers 1020..1028 and the 1023|1024
/// boundary) and on negative cells, including negative chunk boundaries.
///
/// Witnesses on the pre-fix code (seed 0x1234567890ABCDEF, generator version 3):
///   Edge phase, origin (-1024,-1536): local (1022,1016) = absolute (-2,-520) —
///     gate=Mountain, generated chunk=Grass (island side); local (1024,1016) =
///     absolute (0,-520) — gate=Mountain, chunk=Grass (world side, x=1024).
///   Negative phase, origin (512,512): local (-499,-507) = absolute (13,5) —
///     gate=Mountain, chunk=Grass; local (-500,-520) = absolute (12,-8) —
///     gate=Mountain, chunk=Grass.
/// The fixed gate uses WorldGenV2.SampleLatticeAbsolute and agrees with every cell.
/// </summary>
public partial class TestPlacementSampling : RefCounted
{
    private const ulong Seed = 0x1234567890ABCDEFul;
    private const uint Ver = 3;
    private const uint Schema = UnifiedWorldGenerator.FeatureSchemaVersion;

    // 512-aligned origins, exactly as Main.cs sets them (region.X * 512).
    private const long EdgeOriginX = -1024;
    private const long EdgeOriginY = -1536;
    private const long NegativeOriginX = 512;
    private const long NegativeOriginY = 512;

    private readonly Dictionary<ChunkKey, WorldChunk> _chunks = new();
    private readonly Dictionary<ChunkKey, WorldGenV2.Area> _areas = new();
    private readonly GateWorld _world = new();
    private long _originX;
    private long _originY;

    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print("[TEST] Placement gate sampling vs generated chunk (audit 11-01)");
        GD.Print("==================================================");

        long savedOriginX = UnifiedWorldGenerator.WorldOriginX;
        long savedOriginY = UnifiedWorldGenerator.WorldOriginY;
        string dir = Path.Combine(OS.GetUserDataDir(), "test_placement_sampling_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var coordinator = new WorldSaveCoordinator(dir, Seed, Ver, Schema);
            var manager = new StreamingWorldManager(_world, coordinator);
            manager.Open(0, 0);

            int edgeCells = CheckPhase(manager, "edge 1016..1032 (both sides of 1024)", EdgeOriginX, EdgeOriginY, BuildEdgeCells());
            int negativeCells = CheckPhase(manager, "negative cells (chunk boundaries -65|-64, -1|0 and -520..-500)",
                NegativeOriginX, NegativeOriginY, BuildNegativeCells());

            GD.Print($"[TEST] gate == generated chunk on {edgeCells + negativeCells} cells " +
                     $"({edgeCells} edge, {negativeCells} negative); Wall and WallPlan verdicts match.");
            GD.Print("==================================================");
            GD.Print("[TEST] ALL PLACEMENT SAMPLING TESTS PASSED! (Code 0)");
            GD.Print("==================================================");
            return true;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[TEST] PLACEMENT SAMPLING FAILED: {ex.Message}");
            GD.PrintErr(ex.StackTrace);
            return false;
        }
        finally
        {
            UnifiedWorldGenerator.SetWorldOrigin(savedOriginX, savedOriginY);
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    private int CheckPhase(StreamingWorldManager manager, string phaseName, long originX, long originY,
        List<(long X, long Y)> cells)
    {
        UnifiedWorldGenerator.SetWorldOrigin(originX, originY);
        _originX = originX;
        _originY = originY;
        _chunks.Clear();
        _areas.Clear();
        _world.Clear();

        GD.Print($"[TEST] {phaseName}: {cells.Count} cells, origin ({originX},{originY})...");

        foreach ((long x, long y) in cells)
        {
            long absX = originX + x;
            long absY = originY + y;

            GeneratedCell chunkCell = ChunkCell(x, y);
            GeneratedCell areaCell = AreaCell(x, y);
            if (chunkCell.Terrain != areaCell.Terrain)
            {
                throw new Exception(
                    $"Generator disagreement at local ({x},{y}) absolute ({absX},{absY}): " +
                    $"GenerateChunk={chunkCell.Terrain}, WorldGenV2.GenerateArea={areaCell.Terrain}.");
            }

            bool expectedGrass = chunkCell.Terrain == BaseTerrainKind.Grass;
            bool expectedNonWater = chunkCell.Terrain != BaseTerrainKind.Water;

            bool canPlace = manager.CanPlace(x, y);
            if (canPlace != expectedGrass)
            {
                throw new Exception(
                    $"Placement gate disagrees with generated chunk at local ({x},{y}) absolute ({absX},{absY}) " +
                    $"[{phaseName}]: CanPlace={canPlace}, chunk terrain={chunkCell.Terrain} " +
                    $"(expected grass={expectedGrass}). Pre-fix gate samples a direct climate, not the chunk lattice.");
            }

            bool wallPlaced = manager.TryPlaceBlock(WorldDeltaKind.Wall, x, y);
            _world.Clear();
            if (wallPlaced != expectedGrass)
            {
                throw new Exception(
                    $"Wall gate disagrees with generated chunk at local ({x},{y}) absolute ({absX},{absY}) " +
                    $"[{phaseName}]: TryPlaceBlock(Wall)={wallPlaced}, chunk terrain={chunkCell.Terrain} " +
                    $"(expected grass={expectedGrass}).");
            }

            bool planPlaced = manager.TryPlaceBlock(WorldDeltaKind.WallPlan, x, y);
            _world.Clear();
            if (planPlaced != expectedNonWater)
            {
                throw new Exception(
                    $"Plan gate disagrees with generated chunk at local ({x},{y}) absolute ({absX},{absY}) " +
                    $"[{phaseName}]: TryPlaceBlock(WallPlan)={planPlaced}, chunk terrain={chunkCell.Terrain} " +
                    $"(expected non-water={expectedNonWater}).");
            }
        }

        GD.Print($"[TEST] {phaseName}: OK");
        return cells.Count;
    }

    private GeneratedCell ChunkCell(long localX, long localY)
    {
        ChunkKey key = WorldCoordinates.ChunkForTile(localX, localY);
        if (!_chunks.TryGetValue(key, out WorldChunk chunk))
        {
            chunk = UnifiedWorldGenerator.GenerateChunk(Seed, Ver, key);
            _chunks[key] = chunk;
        }
        LocalCell local = WorldCoordinates.LocalForTile(localX, localY);
        return chunk.GetCell(local.X, local.Y);
    }

    private GeneratedCell AreaCell(long localX, long localY)
    {
        ChunkKey key = WorldCoordinates.ChunkForTile(localX, localY);
        if (!_areas.TryGetValue(key, out WorldGenV2.Area area))
        {
            // GenerateArea takes ABSOLUTE coordinates: the same chunk origin the
            // streaming view asks GenerateChunk for.
            area = WorldGenV2.GenerateArea(
                Seed, Ver, _originX + key.X * WorldCoordinates.ChunkSize,
                _originY + key.Y * WorldCoordinates.ChunkSize, WorldCoordinates.ChunkSize, WorldCoordinates.ChunkSize);
            _areas[key] = area;
        }
        LocalCell local = WorldCoordinates.LocalForTile(localX, localY);
        return area.GetCell(local.X, local.Y);
    }

    private static List<(long X, long Y)> BuildEdgeCells()
    {
        var cells = new List<(long X, long Y)>();
        // 1016..1032 covers the required 1020..1028 band and the exact 1023|1024 edge.
        for (long y = 1016; y <= 1032; y++)
            for (long x = 1016; x <= 1032; x++)
                cells.Add((x, y));
        return cells;
    }

    private static List<(long X, long Y)> BuildNegativeCells()
    {
        var cells = new List<(long X, long Y)>();
        // Negative cells around the -520..-500 mismatch cluster (absolute -8..13).
        for (long y = -520; y <= -500; y++)
            for (long x = -520; x <= -500; x++)
                cells.Add((x, y));
        // Both sides of negative chunk boundaries -65|-64 and -1|0.
        for (long y = -66; y <= -63; y++)
            for (long x = -66; x <= -63; x++)
                cells.Add((x, y));
        for (long y = -8; y <= -1; y++)
            for (long x = -8; x <= -1; x++)
                cells.Add((x, y));
        return cells;
    }

    /// <summary>Minimal loaded IBlockWorld: the gate's terrain decision is all we measure.</summary>
    private sealed class GateWorld : IBlockWorld
    {
        private readonly HashSet<(byte Kind, long X, long Y)> _blocks = new();

        public bool IsLoaded(ChunkKey key) => true;
        public bool AddWall(long cellX, long cellY) => AddBlock(WorldDeltaKind.Wall, cellX, cellY);
        public bool RemoveWall(long cellX, long cellY) => RemoveBlock(WorldDeltaKind.Wall, cellX, cellY);
        public bool HasWall(long cellX, long cellY) => HasBlock(WorldDeltaKind.Wall, cellX, cellY);
        public int WallCount => BlockCount(WorldDeltaKind.Wall);

        public bool AddBlock(byte kind, long cellX, long cellY) => _blocks.Add((kind, cellX, cellY));
        public bool AddBlock(byte kind, long cellX, long cellY, ReadOnlySpan<byte> data) => AddBlock(kind, cellX, cellY);
        public bool RemoveBlock(byte kind, long cellX, long cellY) => _blocks.Remove((kind, cellX, cellY));
        public bool HasBlock(byte kind, long cellX, long cellY) => _blocks.Contains((kind, cellX, cellY));

        public int BlockCount(byte kind)
        {
            int count = 0;
            foreach (var block in _blocks)
                if (block.Kind == kind) count++;
            return count;
        }

        public void Clear() => _blocks.Clear();
    }
}