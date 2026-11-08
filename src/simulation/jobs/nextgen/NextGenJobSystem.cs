using System;
using System.Runtime.CompilerServices;

namespace Game.Simulation.NextGenJobs;

/// <summary>
/// Lightweight struct representing the query context of an idle agent requesting work.
/// Stack-allocated, 0 B GC memory footprint.
/// </summary>
public struct AgentPullContext
{
    public int AgentId;
    public short X;
    public short Y;
    public JobCategoryFlags AllowedLabor;
    public bool AllowGeneralLaborFallback;
    public int MaxSearchRadiusChunks;

    public AgentPullContext(
        int agentId,
        short x, short y,
        JobCategoryFlags allowedLabor = JobCategoryFlags.AllGeneralLabor,
        bool allowFallback = true,
        int maxRadiusChunks = 16)
    {
        AgentId = agentId;
        X = x;
        Y = y;
        AllowedLabor = allowedLabor;
        AllowGeneralLaborFallback = allowFallback;
        MaxSearchRadiusChunks = maxRadiusChunks;
    }
}

/// <summary>
/// Unified facade for the Next-Gen Dual-Domain Job & Workplace System.
/// Seamlessly integrates stationary workplaces (O(1) direct pull) and spatial dynamic designations
/// (radial spiral bucket scan with bitmask category skipping and lock-free CAS claiming).
/// Zero GC allocations in all hot dispatch loops.
/// </summary>
public sealed class NextGenJobSystem
{
    public static NextGenJobSystem Instance { get; } = new();

    public SpatialJobGrid Grid { get; }
    public WorkplaceRegistry Workplaces { get; }

    public NextGenJobSystem(int worldWidth = 1024, int worldHeight = 1024, int maxCapacity = 131072, int maxWorkplaces = 4096, int maxWorkers = 65536)
    {
        Grid = new SpatialJobGrid(worldWidth, worldHeight, maxCapacity);
        Workplaces = new WorkplaceRegistry(maxWorkplaces, maxWorkers);
    }

    /// <summary>
    /// Core Pull Model Dispatch: Called ONLY when an agent transitions to Idle.
    /// 1. Stationary Domain: If agent is assigned to a workplace, pulls next task directly in O(1).
    /// 2. Dynamic Domain: If unassigned or permitted fallback, pulls nearest spatial designation.
    /// Strictly 0 B GC memory allocations.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryPullJob(ref AgentPullContext agent, out NextGenJobData job)
    {
        // 1. Stationary Workplace Domain Check (O(1))
        int wpId = Workplaces.GetAssignedWorkplaceId(agent.AgentId);
        if (wpId >= 0)
        {
            if (Workplaces.TryPullAssignedWork(agent.AgentId, out job))
            {
                return true;
            }

            if (!agent.AllowGeneralLaborFallback)
            {
                job = default;
                return false;
            }
        }

        // 2. Dynamic Spatial Designation Domain Check (1-Cycle bitmask + Radial CAS)
        return Grid.TryPullNearestJob(
            agent.X, agent.Y,
            agent.AllowedLabor,
            agent.AgentId,
            agent.MaxSearchRadiusChunks,
            out job);
    }

    /// <summary>
    /// Completes an active job, updating workplace counters or recycling designation slots in O(1).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void CompleteJob(ref NextGenJobData job)
    {
        if (job.IsWorkplace)
        {
            int orderId = job.Id & 0xFFFF;
            Workplaces.CompleteWorkplaceJob(job.WorkplaceId, orderId);
        }
        else
        {
            Grid.RemoveJob(job.Id);
        }
    }

    /// <summary>
    /// Cancels or releases an active job back into the available pool.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void CancelJob(ref NextGenJobData job)
    {
        if (job.IsWorkplace)
        {
            int orderId = job.Id & 0xFFFF;
            Workplaces.CancelWorkplaceJob(job.WorkplaceId, orderId);
        }
        else
        {
            Grid.ReleaseJob(job.Id, job.AssignedWorkerId);
        }
    }

    /// <summary>
    /// Helper to register a dynamic designation in the spatial grid.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int AddDesignation(
        NextGenJobType type,
        short targetX, short targetY,
        short standX, short standY,
        short sourceX = 0, short sourceY = 0,
        ushort itemId = 0, ushort itemCount = 1,
        float duration = 2.0f,
        byte priority = 100,
        byte requiredSkill = 0,
        ushort customPayload = 0)
    {
        return Grid.AddDesignation(
            type, targetX, targetY, standX, standY,
            sourceX, sourceY, itemId, itemCount,
            duration, priority, requiredSkill, customPayload);
    }

    /// <summary>
    /// Helper to register a stationary workplace.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int RegisterWorkplace(
        string name,
        JobCategoryFlags category,
        NextGenJobType defaultJobType,
        short x, short y,
        short standX, short standY,
        int maxWorkers = 4)
    {
        return Workplaces.RegisterWorkplace(name, category, defaultJobType, x, y, standX, standY, maxWorkers);
    }

    /// <summary>
    /// Helper to assign a worker to a workplace.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool AssignWorkerToWorkplace(int workerId, int workplaceId)
    {
        return Workplaces.AssignWorker(workerId, workplaceId);
    }
}
