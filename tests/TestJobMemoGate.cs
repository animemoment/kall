using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Game.Core;
using Game.Simulation;
using Game.Simulation.Jobs;

namespace Game.Tests;

/// <summary>
/// 06-02 + 06-03 regression guard for the job index (JobSpatialPyramid).
///
/// 06-02. Was: the per-dispatch CanAgentExecute memo is keyed by jobId only, while
/// BlueprintDeliveryJobHandler answers from the REQUESTING agent's carried item.
/// The first agent planted its verdict for the whole epoch and every other agent
/// received it, so part of the population was refused work it could do (or got a
/// job it could not do). Now: only handler types declared agent-independent share
/// the memo; agent-dependent types are evaluated per agent.
///
/// 06-03. Was: only _jobCdCounted was swapped through the resize gate, while
/// _assignedWorkers/_active/_unclaimedFlag/_jobFailCd were replaced by a bare
/// Array.Resize. A lock-free CAS/Exchange racing a capacity growth could land in
/// the discarded old copy (lost claim counter / lost unclaimed transition).
/// Now: all parallel per-job arrays are swapped as one gated group and every
/// lock-free mutation of those arrays goes through the same gate.
///
/// Run: godot --headless --path . --script res://tests/run_job_memo_gate.gd
/// </summary>
public partial class TestJobMemoGate : RefCounted
{
    private const int MapSize = 64;
    // JobSpatialPyramid.InitialCapacity (private); kept in sync by the test below.
    private const int InitialCapacity = 32768;

    private readonly AgentDependentHandler _dependentHandler = new();
    private readonly AgentIndependentHandler _independentHandler = new();
    private bool _ok = true;

    /// <summary>
    /// 06-02 probe: verdict depends on the requesting agent (same shape as the real
    /// BlueprintDeliveryJobHandler, which reads pool.CarriedItemId/CarriedItemCount).
    /// Registered under the real BlueprintDelivery type id, which must NOT be in the
    /// shared-memo allow-list.
    /// </summary>
    private sealed class AgentDependentHandler : IJobHandler
    {
        public int Calls;
        public int CallsAgent0;
        public int CallsAgent1;

        public JobTypeId TypeId => JobTypeId.BlueprintDelivery;
        public JobExecutionType ExecutionType => JobExecutionType.Stationary;
        public JobPriorityTier DefaultPriority => JobPriorityTier.BlueprintSupply;
        public ToolRequirement RequiredTool => ToolRequirement.None;

        public bool CanAgentExecute(int agentIndex, in JobData job, AgentDataPool pool, SimulationContext ctx)
        {
            Interlocked.Increment(ref Calls);
            if (agentIndex == 0)
                Interlocked.Increment(ref CallsAgent0);
            else if (agentIndex == 1)
                Interlocked.Increment(ref CallsAgent1);

            return pool != null
                && (uint)agentIndex < (uint)pool.Capacity
                && pool.CarriedItemId[agentIndex] == ItemId.Log
                && pool.CarriedItemCount[agentIndex] > 0;
        }

        public void OnStart(int agentIndex, in JobData job, AgentDataPool pool, SimulationContext ctx) { }
        public void ExecuteParallel(int agentIndex, float deltaTime, AgentDataPool pool, SimulationContext ctx) { }
        public void Commit(int agentIndex, float deltaTime, AgentDataPool pool, SimulationContext ctx) { }
        public void OnCancel(int agentIndex, AgentDataPool pool, SimulationContext ctx) { }
    }

    /// <summary>
    /// Agent-independent probe registered under TreeChopping (allow-listed). Used to
    /// prove the S-033 shared memo is still in effect for independent handlers: the
    /// second agent in the same epoch must be served from the memo (one handler call).
    /// </summary>
    private sealed class AgentIndependentHandler : IJobHandler
    {
        public int Calls;

        public JobTypeId TypeId => JobTypeId.TreeChopping;
        public JobExecutionType ExecutionType => JobExecutionType.Stationary;
        public JobPriorityTier DefaultPriority => JobPriorityTier.TreeChopping;
        public ToolRequirement RequiredTool => ToolRequirement.None;

        public bool CanAgentExecute(int agentIndex, in JobData job, AgentDataPool pool, SimulationContext ctx)
        {
            Interlocked.Increment(ref Calls);
            return true;
        }

        public void OnStart(int agentIndex, in JobData job, AgentDataPool pool, SimulationContext ctx) { }
        public void ExecuteParallel(int agentIndex, float deltaTime, AgentDataPool pool, SimulationContext ctx) { }
        public void Commit(int agentIndex, float deltaTime, AgentDataPool pool, SimulationContext ctx) { }
        public void OnCancel(int agentIndex, AgentDataPool pool, SimulationContext ctx) { }
    }

    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print("[MEMOGATE] 06-02 memo per agent + 06-03 gated array growth");
        GD.Print("==================================================");

        try
        {
            JobRegistry.Register(_dependentHandler);
            JobRegistry.Register(_independentHandler);

            AgentDependentMemoDoesNotLeakFalse();
            AgentDependentMemoDoesNotLeakTrue();
            AgentIndependentMemoStillShared();
            ParallelArraysGrowAsOneGroup();
            ConcurrentInsertAndReadAcrossGrowth();

            if (_ok)
            {
                GD.Print("==================================================");
                GD.Print("[MEMOGATE OK] memo gate and resize gate hold");
                GD.Print("==================================================");
            }
            else
            {
                GD.PrintErr("[MEMOGATE FAIL] see FAIL lines above");
            }
            return _ok;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[MEMOGATE] EXCEPTION: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            return false;
        }
    }

    // ---------------- 06-02: agent-dependent verdict must not be shared ---------------- //

    private void AgentDependentMemoDoesNotLeakFalse()
    {
        _dependentHandler.Calls = 0;
        _dependentHandler.CallsAgent0 = 0;
        _dependentHandler.CallsAgent1 = 0;

        var p = new JobSpatialPyramid();
        var pool = new AgentDataPool(8);
        var ctx = NewContext();

        // agent 1 carries a log; agent 0 does not.
        pool.CarriedItemId[1] = ItemId.Log;
        pool.CarriedItemCount[1] = 1;

        int jobId = p.RegisterJob(MakeJob(8, 8, JobTypeId.BlueprintDelivery, 1));
        p.BeginClaimEpoch();

        bool claimedByBare = p.TryClaimJobForAgent(
            8, 8, ToolRequirement.None, pool, 0, ctx, true, true, out _);
        bool claimedByCarrier = p.TryClaimJobForAgent(
            8, 8, ToolRequirement.None, pool, 1, ctx, true, true, out JobData carrierJob);

        Check("06-02: bare agent refused by its own (false) verdict", !claimedByBare);
        Check("06-02: carrier agent is not served the bare agent's false memo",
            claimedByCarrier && carrierJob.Id == jobId);
        // A missed claim scans own chunk, the ring and the macro sector, so the exact
        // number of calls is not fixed. What matters: BOTH agents reached the handler.
        Check($"06-02: both agents were evaluated (agent0={_dependentHandler.CallsAgent0}, " +
              $"agent1={_dependentHandler.CallsAgent1})",
            _dependentHandler.CallsAgent0 > 0 && _dependentHandler.CallsAgent1 > 0);
        Check("06-02: counters after per-agent claim", p.UnclaimedCount == 0 && p.TotalCount == 1);
    }

    private void AgentDependentMemoDoesNotLeakTrue()
    {
        _dependentHandler.Calls = 0;
        _dependentHandler.CallsAgent0 = 0;
        _dependentHandler.CallsAgent1 = 0;

        var p = new JobSpatialPyramid();
        var pool = new AgentDataPool(8);
        var ctx = NewContext();

        pool.CarriedItemId[1] = ItemId.Log;
        pool.CarriedItemCount[1] = 1;

        // MaxWorkers = 2: the first (true) claim does not saturate the job, so the second
        // agent really reaches the handler instead of failing on saturation.
        int jobId = p.RegisterJob(MakeJob(9, 9, JobTypeId.BlueprintDelivery, 2));
        p.BeginClaimEpoch();

        bool claimedByCarrier = p.TryClaimJobForAgent(
            9, 9, ToolRequirement.None, pool, 1, ctx, true, true, out _);
        bool claimedByBare = p.TryClaimJobForAgent(
            9, 9, ToolRequirement.None, pool, 0, ctx, true, true, out _);

        Check("06-02: carrier claims first", claimedByCarrier);
        Check("06-02: bare agent is not served the carrier's true memo", !claimedByBare);
        Check("06-02: one slot still unclaimed after the refused memo", p.UnclaimedCount == 1);
        Check("06-02: job stayed alive", p.TotalCount == 1 && p.TryGetJob(jobId, out _));
        Check($"06-02: both agents were evaluated (agent0={_dependentHandler.CallsAgent0}, " +
              $"agent1={_dependentHandler.CallsAgent1})",
            _dependentHandler.CallsAgent0 > 0 && _dependentHandler.CallsAgent1 > 0);
    }

    private void AgentIndependentMemoStillShared()
    {
        _independentHandler.Calls = 0;

        var p = new JobSpatialPyramid();
        var pool = new AgentDataPool(8);
        var ctx = NewContext();

        int jobId = p.RegisterJob(MakeJob(10, 10, JobTypeId.TreeChopping, 1));
        p.BeginClaimEpoch();

        bool first = p.TryClaimJobForAgent(
            10, 10, ToolRequirement.None, pool, 0, ctx, true, true, out _);
        p.ReleaseWorkerClaim(jobId);
        bool second = p.TryClaimJobForAgent(
            10, 10, ToolRequirement.None, pool, 1, ctx, true, true, out _);

        Check("06-02: independent handler: both agents claim", first && second);
        Check($"06-02: independent handler keeps the S-033 shared memo (calls={_independentHandler.Calls}, expected 1)",
            _independentHandler.Calls == 1);
    }

    // ---------------- 06-03: parallel arrays grow as one gated group ---------------- //

    private void ParallelArraysGrowAsOneGroup()
    {
        var p = new JobSpatialPyramid();
        int oldCapacity = p.CapacityForTest;
        Check($"06-03: fresh index starts at the documented capacity {InitialCapacity} (cap={oldCapacity})",
            oldCapacity == InitialCapacity);
        int total = oldCapacity + 1;

        // Unique (x, y) per job: 1024 x 64 cells are enough for oldCapacity + 1.
        for (int i = 0; i < total; i++)
            p.RegisterJob(MakeJob(i % 1024, i / 1024, JobTypeId.TreeChopping, 1));

        int cap = p.CapacityForTest;
        Check($"06-03: capacity grew past the old {oldCapacity} (cap={cap})", cap > oldCapacity);

        int[] lengths = p.GetParallelArrayLengthsForTest();
        bool sameLength = lengths.Length > 0;
        for (int i = 0; i < lengths.Length; i++)
        {
            if (lengths[i] != cap)
                sameLength = false;
        }
        Check("06-03: all parallel index arrays have the same Length (== capacity)", sameLength);

        Check("06-03: counters survive the growth",
            p.TotalCount == total && p.UnclaimedCount == total);
        p.GetCdAggregatesForTest(out int cdGlobal, out long cdChunk, out long unclaimedChunk, out int unclaimedGlobal);
        Check("06-03: chunk unclaimed sum equals the global counter after growth",
            cdGlobal == 0 && cdChunk == 0
            && unclaimedGlobal == total && unclaimedChunk == total);

        // The job that triggered the growth (id == oldCapacity) must be fully usable.
        int grownId = oldCapacity;
        Check("06-03: job beyond the old capacity is readable",
            p.TryGetJob(grownId, out JobData grown)
            && grown.Id == grownId
            && grown.TargetX == grownId % 1024
            && grown.TargetY == grownId / 1024);
        Check("06-03: job beyond the old capacity can be claimed", p.TryClaimJobById(grownId, out _));
        Check("06-03: claim on the grown slot is accounted", p.UnclaimedCount == total - 1);
    }

    private void ConcurrentInsertAndReadAcrossGrowth()
    {
        var p = new JobSpatialPyramid();
        var ctx = NewContext();
        var pool = new AgentDataPool(8);
        var errors = new ConcurrentQueue<string>();

        // Fill up to just below the initial capacity, then cross it under contention.
        int prefill = InitialCapacity - 68;
        const int Writers = 3;
        const int PerWriter = 200;          // prefill + 600 -> the growth happens concurrently
        const int Readers = 3;
        const int ReaderIterations = 4000;
        const int ClaimIterations = 2500;

        for (int i = 0; i < prefill; i++)
            p.RegisterJob(MakeJob(i % 1024, i / 1024, JobTypeId.TreeChopping, 1));

        int registered = prefill;

        var tasks = new List<Task>(Writers + Readers + 2);

        for (int w = 0; w < Writers; w++)
        {
            int writer = w;
            tasks.Add(Task.Run(() =>
            {
                try
                {
                    for (int k = 0; k < PerWriter; k++)
                    {
                        int idx = prefill + writer * PerWriter + k;
                        p.RegisterJob(MakeJob(idx % 1024, idx / 1024, JobTypeId.TreeChopping, 1));
                        Interlocked.Increment(ref registered);
                    }
                }
                catch (Exception ex)
                {
                    errors.Enqueue($"writer#{writer}: {ex.GetType().Name}: {ex.Message}");
                }
            }));
        }

        for (int r = 0; r < Readers; r++)
        {
            int reader = r;
            tasks.Add(Task.Run(() =>
            {
                var rng = new Random(7000 + reader);
                try
                {
                    for (int i = 0; i < ReaderIterations; i++)
                    {
                        int max = Volatile.Read(ref registered);
                        if (max <= 0)
                            continue;
                        int id = rng.Next(0, max);
                        if (id >= p.CapacityForTest)
                            continue;
                        p.TryGetJob(id, out _);
                        p.GetJobData(id);
                        if ((i & 31) == 0)
                            p.HasJobAt(rng.Next(0, 1024), rng.Next(0, 1024), JobTypeId.TreeChopping);
                        if ((i & 63) == 0)
                            p.GetChunkJobCount(rng.Next(0, JobSpatialPyramid.ChunkCount));
                    }
                }
                catch (Exception ex)
                {
                    errors.Enqueue($"reader#{reader}: {ex.GetType().Name}: {ex.Message}");
                }
            }));
        }

        // Claim-by-id churn: gated CAS/Exchange on _assignedWorkers/_unclaimedFlag while
        // the arrays are being swapped by the growth.
        tasks.Add(Task.Run(() =>
        {
            var rng = new Random(9001);
            try
            {
                for (int i = 0; i < ClaimIterations; i++)
                {
                    int max = Volatile.Read(ref registered);
                    if (max <= 0)
                        continue;
                    int id = rng.Next(0, max);
                    if (id >= p.CapacityForTest)
                        continue;
                    if (p.TryClaimJobById(id, out _))
                        p.ReleaseWorkerClaim(id);
                }
            }
            catch (Exception ex)
            {
                errors.Enqueue($"id-claimer: {ex.GetType().Name}: {ex.Message}");
            }
        }));

        // Claim-by-agent churn: exercises the memo + claim scan while arrays grow.
        tasks.Add(Task.Run(() =>
        {
            var rng = new Random(9002);
            try
            {
                for (int i = 0; i < ClaimIterations; i++)
                {
                    if (p.TryClaimJobForAgent(
                            rng.Next(0, 1024), rng.Next(0, 1024),
                            ToolRequirement.None, pool, 0, ctx, true, true, out JobData job))
                    {
                        p.ReleaseWorkerClaim(job.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Enqueue($"agent-claimer: {ex.GetType().Name}: {ex.Message}");
            }
        }));

        Task.WaitAll(tasks.ToArray());

        int expected = prefill + Writers * PerWriter;
        foreach (string e in errors)
            GD.PrintErr($"[MEMOGATE] task error: {e}");
        Check("06-03: no exceptions in concurrent insert/read/claim tasks", errors.IsEmpty);
        Check($"06-03: registered all jobs (total={p.TotalCount}, expected={expected})",
            p.TotalCount == expected);

        int cap = p.CapacityForTest;
        int[] lengths = p.GetParallelArrayLengthsForTest();
        bool sameLength = true;
        for (int i = 0; i < lengths.Length; i++)
        {
            if (lengths[i] != cap)
                sameLength = false;
        }
        Check($"06-03: arrays grew together under contention (cap={cap})", sameLength);

        // Every successful claim was paired with a release, so all jobs are unclaimed again.
        Check($"06-03: claim counters did not drift (unclaimed={p.UnclaimedCount}, expected={expected})",
            p.UnclaimedCount == expected);
        p.GetCdAggregatesForTest(out int cdGlobal, out long cdChunk, out long unclaimedChunk, out int unclaimedGlobal);
        Check("06-03: chunk unclaimed sum still equals the global counter",
            cdGlobal == 0 && cdChunk == 0
            && unclaimedGlobal == expected && unclaimedChunk == expected);
    }

    // ---------------- helpers ---------------- //

    private void Check(string what, bool condition)
    {
        if (condition)
        {
            GD.Print($"[MEMOGATE] OK: {what}");
        }
        else
        {
            GD.PrintErr($"[MEMOGATE] FAIL: {what}");
            _ok = false;
        }
    }

    private static JobData MakeJob(int x, int y, JobTypeId type, int maxWorkers)
    {
        return new JobData
        {
            TypeId = type,
            ExecutionType = JobExecutionType.Stationary,
            PriorityTier = JobPriorityTier.TreeChopping,
            RequiredTool = ToolRequirement.None,
            TargetX = x,
            TargetY = y,
            StandX = x,
            StandY = y,
            MaxWorkers = maxWorkers,
            WorkDuration = 1f
        };
    }

    private static SimulationContext NewContext()
    {
        var ground = new TileType[MapSize, MapSize];
        var trees = new bool[MapSize, MapSize];
        var solid = new bool[MapSize, MapSize];
        var walkable = new List<(int X, int Y)>(MapSize * MapSize);
        for (int x = 0; x < MapSize; x++)
            for (int y = 0; y < MapSize; y++)
                walkable.Add((x, y));

        return new SimulationContext(ground, null, trees, solid, walkable,
            new AgentSpatialGrid(MapSize, MapSize), new AgentMovementService(), new Random(1234), 64);
    }
}