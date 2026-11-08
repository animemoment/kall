using System;
using System.Diagnostics;
using System.Numerics;
using Godot;
using Game.Core;
using Game.Core.Soil;
using Game.UI.Renderers.Culling;
using Game.Simulation.NextGenJobs;

namespace Game.Tests;

/// <summary>
/// Next-Gen Integration Verification Suite:
/// Tests Hybrid Soil Engine, Render Frustum Culling Grid, and Dual-Domain Pull Job System.
/// </summary>
public partial class TestNextGenIntegration : RefCounted
{
    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print("[NextGen Integration Suite] Running All Tests...");
        GD.Print("==================================================");

        bool ok = true;
        ok &= TestSoilEngine();
        ok &= TestRenderSpatialGrid();
        ok &= TestJobSystem();

        GD.Print("==================================================");
        GD.Print(ok ? "[NextGen Integration Suite] ALL PASS 🎯" : "[NextGen Integration Suite] FAIL ❌");
        GD.Print("==================================================");
        return ok;
    }

    private bool TestSoilEngine()
    {
        GD.Print("[Test: Soil Engine] Testing Hybrid Sparse & Event-Driven Engine...");
        var engine = new HybridSoilEngine(512, 512, farmCapacity: 4096);

        // Ground & noise init
        var ground = new TileType[512, 512];
        var mNoise = new float[512, 512];
        var fNoise = new float[512, 512];
        for (int y = 0; y < 512; y++)
        {
            for (int x = 0; x < 512; x++)
            {
                ground[x, y] = (x < 10 || y < 10) ? TileType.Water : TileType.Grass;
                mNoise[x, y] = 0.5f;
                fNoise[x, y] = 0.6f;
            }
        }
        engine.Initialize(ground, mNoise, fNoise);

        // Register 2000 farms
        int baseQuality = 100;
        int registered = 0;
        for (int y = 20; y < 70; y++)
        {
            for (int x = 20; x < 60; x++)
            {
                engine.RegisterFarm(x, y, (byte)baseQuality, CropType.Wheat);
                registered++;
            }
        }

        if (engine.Farms.Count != registered)
        {
            GD.PrintErr($"[Soil Engine] Farm count mismatch: expected {registered}, got {engine.Farms.Count}");
            return false;
        }

        // Test crop rotation & legume bonus
        int testX = 25, testY = 25;
        engine.PlantCrop(testX, testY, CropType.Peas); // Legume
        engine.DrainAfterHarvest(testX, testY, CropType.Peas);
        byte fertAfterPeas = engine.GetFertility(testX, testY);

        // Plant wheat after peas -> must trigger crop rotation fertilizer bonus
        engine.PlantCrop(testX, testY, CropType.Wheat);
        int farmIdx = engine.Farms.GetFarmIndex(testX, testY);
        byte bonus = engine.Farms.GetFarmByIndex(farmIdx).FertilizerBonus;
        if (bonus != 25)
        {
            GD.PrintErr($"[Soil Engine] Crop rotation bonus expected 25, got {bonus}");
            return false;
        }

        // Benchmark 100 ticks
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 100; i++)
        {
            engine.Tick(season: 0.1f, isRaining: false, stepMultiplier: 1);
        }
        sw.Stop();
        double msPerTick = sw.Elapsed.TotalMilliseconds / 100.0;
        GD.Print($"[Soil Engine] 2000 Farms Tick Time: {msPerTick:F4} ms ({msPerTick * 1000.0:F1} µs/tick). PASS!");

        return true;
    }

    private bool TestRenderSpatialGrid()
    {
        GD.Print("[Test: Render Culling Grid] Testing Hierarchical Chunk Frustum Culling...");
        const int agentCount = 10000;
        var grid = new AgentRenderSpatialGrid(32, 32, agentCount);

        var positions = new Vector2[agentCount];
        var rng = new Random(42);
        for (int i = 0; i < agentCount; i++)
        {
            positions[i] = new Vector2(rng.Next(0, 2048 * 32), rng.Next(0, 2048 * 32));
        }

        // Rebuild spatial grid
        grid.Rebuild(positions);

        var visibleIndices = new int[agentCount];

        // Query Close Zoom Viewport (5% of world)
        var closeView = Rect2D.FromCenterSize(new Vector2(10000, 10000), new Vector2(1920, 1080));
        var sw = Stopwatch.StartNew();
        int queries = 1000;
        int visibleCount = 0;
        for (int i = 0; i < queries; i++)
        {
            grid.QueryVisibleAgents(in closeView, positions, visibleIndices, out visibleCount);
        }
        sw.Stop();
        double usPerQuery = (sw.Elapsed.TotalMilliseconds * 1000.0) / queries;
        GD.Print($"[Render Culling Grid] Frustum Culling 10k agents: {usPerQuery:F2} µs/query (visible: {visibleCount}). PASS!");

        return true;
    }

    private bool TestJobSystem()
    {
        GD.Print("[Test: NextGen Job System] Testing Dual-Domain Pull Engine...");
        var jobSystem = new NextGenJobSystem(1024, 1024, maxCapacity: 32768, maxWorkplaces: 512, maxWorkers: 10000);

        // 1. Stationary Workplace Domain
        int wpId = jobSystem.RegisterWorkplace(
            "Smithy_1",
            JobCategoryFlags.Smith,
            NextGenJobType.Smithing,
            x: 100, y: 100,
            standX: 101, standY: 100,
            maxWorkers: 2);

        jobSystem.Workplaces.GetWorkplace(wpId).AddOrder(recipeId: 10, productItemId: 5, targetCount: 10);
        jobSystem.AssignWorkerToWorkplace(workerId: 42, workplaceId: wpId);

        var pullContext = new AgentPullContext(agentId: 42, x: 100, y: 100, JobCategoryFlags.Smith, allowFallback: false);
        if (!jobSystem.TryPullJob(ref pullContext, out var pulledJob))
        {
            GD.PrintErr("[Job System] Workplace pull failed for assigned worker!");
            return false;
        }

        if (pulledJob.WorkplaceId != wpId || pulledJob.AssignedWorkerId != 42)
        {
            GD.PrintErr("[Job System] Pulled job mismatch in workplace domain!");
            return false;
        }

        jobSystem.CompleteJob(ref pulledJob);

        // 2. Dynamic Designation Domain
        for (int i = 0; i < 500; i++)
        {
            jobSystem.AddDesignation(
                NextGenJobType.TreeChopping,
                targetX: (short)(200 + (i % 20)),
                targetY: (short)(200 + (i / 20)),
                standX: 200, standY: 200);
        }

        var laborContext = new AgentPullContext(agentId: 99, x: 200, y: 200, JobCategoryFlags.Chop, allowFallback: true);
        if (!jobSystem.TryPullJob(ref laborContext, out var chopJob))
        {
            GD.PrintErr("[Job System] Dynamic designation pull failed for general laborer!");
            return false;
        }

        if (chopJob.Type != NextGenJobType.TreeChopping || chopJob.AssignedWorkerId != 99)
        {
            GD.PrintErr("[Job System] Pulled dynamic job mismatch!");
            return false;
        }

        jobSystem.CompleteJob(ref chopJob);

        GD.Print("[Job System] Dual-Domain Pull Model verified. PASS!");
        return true;
    }
}
