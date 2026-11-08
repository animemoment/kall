using System;
using System.Linq;
using System.Reflection;
using Godot;
using Game.Core;
using Game.Simulation;

namespace Game.Tests;

/// <summary>
/// J-05: register 500 jobs in x=[600..620], y=[0..99]. On old ChunkDim=32 all jobs clamp
/// into ONE chunk (31*32+31 = 1023) and the eastern/southern half of the island is lost.
/// On new ChunkDim=64 they spread over 2 chunks (600>>4=37 .. 620>>4=38) — 4096 chunks
/// cover the full 1024x1024 island.
///
/// Verify via reflection on JobSpatialPyramid._chunkCount how many chunks actually contain jobs.
/// Old code: 1 chunk. New code: 2 chunks.
/// </summary>
public partial class TestJ05ChunkDistribution : RefCounted
{
    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print("[J-05 CHUNK DISTRIBUTION] Testing job chunk clustering...");
        GD.Print("==================================================");

        var pyramid = new JobSpatialPyramid();
        var jobs = new List<JobData>(500);

        // 500 jobs in a 21x100 rectangle at x=[600..620] (all in the second half of the island)
        for (int y = 0; y < 100; y++)
        {
            for (int x = 600; x <= 620; x++)
            {
                jobs.Add(new JobData
                {
                    TypeId = JobTypeId.TreeChopping,
                    ExecutionType = JobExecutionType.Stationary,
                    PriorityTier = JobPriorityTier.TreeChopping,
                    TargetX = x,
                    TargetY = y,
                    StandX = x,
                    StandY = y,
                    MaxWorkers = 1,
                    WorkDuration = 5f
                });
            }
        }

        pyramid.RegisterBatch(jobs);
        GD.Print($" -> registered {pyramid.TotalCount} jobs");

        // Access private _chunkCount array via reflection
        var chunkCountField = typeof(JobSpatialPyramid).GetField(
            "_chunkCount",
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (chunkCountField == null)
            throw new Exception("Cannot find private field _chunkCount via reflection!");

        var chunkCounts = (int[])chunkCountField.GetValue(pyramid);
        int chunksWithJobs = chunkCounts.Count(c => c > 0);

        GD.Print($" -> chunks with jobs: {chunksWithJobs} of {JobSpatialPyramid.ChunkCount} " +
                 $"(ChunkDim={JobSpatialPyramid.ChunkDim})");

        // Old code (ChunkDim=32): all 500 jobs clamp into chunk 31*32+31 = 1023 -> 1 chunk.
        // New code (ChunkDim=64): jobs spread over chunks 37 and 38 -> 2 chunks.
        if (chunksWithJobs != 2)
            throw new InvalidOperationException(
                $"J-05: jobs clamped into {chunksWithJobs} chunk(s) instead of 2. " +
                "ChunkDim is still 32 (512x512) — bug not fixed!");

        // Spot check: chunk 37 and chunk 38 must contain jobs; chunk 1023 must NOT (old clamped home).
        if (chunkCounts[37] <= 0)
            throw new Exception("Chunk 37 should have jobs (x=600..615).");
        if (chunkCounts[38] <= 0)
            throw new Exception("Chunk 38 should have jobs (x=616..620).");
        if (chunkCounts[1023] > 0)
            throw new Exception("Chunk 1023 (old clamped home for x>=512) should be empty!");

        GD.Print(" -> PASS: jobs correctly distributed over the full 1024x1024 island (ChunkDim=64, no clamp).");
        return true;
    }
}
