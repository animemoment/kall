using System;
using System.Runtime.CompilerServices;

namespace Game.Simulation.NextGenJobs;

/// <summary>
/// High-performance registry of stationary workplaces and worker-to-workplace assignments.
/// Provides O(1) worker assignment lookups and direct dispatch without spatial scanning.
/// </summary>
public sealed class WorkplaceRegistry
{
    private readonly Workplace[] _workplaces;
    private readonly int[] _workerToWorkplace;
    private int _nextWorkplaceId = 0;
    private readonly int _maxWorkplaces;
    private readonly int _maxWorkers;

    public int RegisteredCount => _nextWorkplaceId;

    public WorkplaceRegistry(int maxWorkplaces = 4096, int maxWorkers = 65536)
    {
        _maxWorkplaces = maxWorkplaces;
        _maxWorkers = maxWorkers;
        _workplaces = new Workplace[maxWorkplaces];
        _workerToWorkplace = new int[maxWorkers];

        for (int i = 0; i < maxWorkers; i++)
        {
            _workerToWorkplace[i] = -1;
        }
    }

    /// <summary>
    /// Registers a new workplace in O(1) time.
    /// </summary>
    public int RegisterWorkplace(
        string name,
        JobCategoryFlags category,
        NextGenJobType defaultJobType,
        short x, short y,
        short standX, short standY,
        int maxWorkers = 4)
    {
        if (_nextWorkplaceId >= _maxWorkplaces) return -1;

        int id = _nextWorkplaceId++;
        var wp = new Workplace(id, name, category, defaultJobType, x, y, standX, standY, maxWorkers);
        _workplaces[id] = wp;
        return id;
    }

    /// <summary>
    /// Retrieves a workplace by ID.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Workplace GetWorkplace(int workplaceId)
    {
        if ((uint)workplaceId >= (uint)_nextWorkplaceId) return null;
        return _workplaces[workplaceId];
    }

    /// <summary>
    /// Assigns a worker to a workplace in O(1) time.
    /// </summary>
    public bool AssignWorker(int workerId, int workplaceId)
    {
        if ((uint)workerId >= (uint)_maxWorkers) return false;
        if ((uint)workplaceId >= (uint)_nextWorkplaceId) return false;

        var wp = _workplaces[workplaceId];
        if (wp == null) return false;

        int prev = _workerToWorkplace[workerId];
        if (prev >= 0 && prev < _nextWorkplaceId)
        {
            _workplaces[prev]?.UnassignWorker(workerId);
        }

        if (wp.AssignWorker(workerId))
        {
            _workerToWorkplace[workerId] = workplaceId;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Unassigns a worker from their current workplace.
    /// </summary>
    public bool UnassignWorker(int workerId)
    {
        if ((uint)workerId >= (uint)_maxWorkers) return false;

        int wpId = _workerToWorkplace[workerId];
        if (wpId >= 0 && wpId < _nextWorkplaceId)
        {
            _workplaces[wpId]?.UnassignWorker(workerId);
            _workerToWorkplace[workerId] = -1;
            return true;
        }

        return false;
    }

    /// <summary>
    /// O(1) query of worker's assigned workplace ID (-1 if unassigned).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetAssignedWorkplaceId(int workerId)
    {
        if ((uint)workerId >= (uint)_maxWorkers) return -1;
        return _workerToWorkplace[workerId];
    }

    /// <summary>
    /// Direct O(1) pull dispatch from worker's assigned workplace.
    /// Zero GC allocations.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryPullAssignedWork(int workerId, out NextGenJobData job)
    {
        job = default;
        if ((uint)workerId >= (uint)_maxWorkers) return false;

        int wpId = _workerToWorkplace[workerId];
        if (wpId < 0) return false;

        var wp = _workplaces[wpId];
        if (wp == null) return false;

        return wp.TryPullWork(workerId, out job);
    }

    /// <summary>
    /// Completes work order unit at workplace.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void CompleteWorkplaceJob(int workplaceId, int orderId)
    {
        if ((uint)workplaceId < (uint)_nextWorkplaceId)
        {
            _workplaces[workplaceId]?.CompleteOrderWork(orderId);
        }
    }

    /// <summary>
    /// Cancels or releases work order unit at workplace.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void CancelWorkplaceJob(int workplaceId, int orderId)
    {
        if ((uint)workplaceId < (uint)_nextWorkplaceId)
        {
            _workplaces[workplaceId]?.CancelOrderWork(orderId);
        }
    }
}
