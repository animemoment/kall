using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Game.Simulation.NextGenJobs;

/// <summary>
/// Repeat mode for workplace production orders.
/// </summary>
public enum OrderRepeatMode : byte
{
    Count    = 0, // Produce fixed target count then deactivate
    Infinite = 1  // Produce continuously without stopping
}

/// <summary>
/// A single production bill/order queued at a workplace.
/// Struct-based, pre-allocated, zero heap churn.
/// </summary>
public struct ProductionOrder
{
    public int OrderId;
    public ushort RecipeId;
    public ushort ProductItemId;
    public int TargetCount;        // 0 = infinite when RepeatMode is Infinite
    public int CompletedCount;
    public int InProgressCount;
    public float WorkDuration;
    public OrderRepeatMode RepeatMode;
    public bool IsActive;

    public bool HasRemainingWork
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (!IsActive) return false;
            if (RepeatMode == OrderRepeatMode.Infinite) return true;
            return (CompletedCount + InProgressCount) < TargetCount;
        }
    }
}

/// <summary>
/// Stationary Workplace (e.g. Smithy, Kitchen, Workshop, Laboratory, Farm, Guard Post).
/// Workers assigned to this workplace pull tasks directly from its internal production queue in O(1) time
/// without scanning the global spatial designation grid.
/// </summary>
public sealed class Workplace
{
    public const int MaxOrdersPerWorkplace = 16;
    public const int MaxAssignedWorkers = 8;

    public readonly int Id;
    public readonly string Name;
    public readonly JobCategoryFlags Category;
    public readonly NextGenJobType WorkplaceJobType;
    public readonly short X;
    public readonly short Y;
    public readonly short StandX;
    public readonly short StandY;
    public readonly int MaxWorkers;

    // Assigned workers
    private readonly int[] _assignedWorkers;
    private int _assignedCount;

    // Production orders queue
    private readonly ProductionOrder[] _orders;
    private int _orderCount;
    private int _currentOrderIndex;
    private int _nextOrderId = 1;

    // Concurrency control for this specific workplace
    private SpinLock _lock = new(false);

    public int AssignedWorkerCount => _assignedCount;

    public Workplace(
        int id,
        string name,
        JobCategoryFlags category,
        NextGenJobType defaultJobType,
        short x, short y,
        short standX, short standY,
        int maxWorkers = 4)
    {
        Id = id;
        Name = name;
        Category = category;
        WorkplaceJobType = defaultJobType;
        X = x;
        Y = y;
        StandX = standX;
        StandY = standY;
        MaxWorkers = Math.Min(maxWorkers, MaxAssignedWorkers);

        _assignedWorkers = new int[MaxAssignedWorkers];
        for (int i = 0; i < MaxAssignedWorkers; i++) _assignedWorkers[i] = -1;

        _orders = new ProductionOrder[MaxOrdersPerWorkplace];
    }

    /// <summary>
    /// Assigns a worker to this workplace if capacity permits.
    /// </summary>
    public bool AssignWorker(int workerId)
    {
        bool lockTaken = false;
        try
        {
            _lock.Enter(ref lockTaken);
            if (_assignedCount >= MaxWorkers) return false;

            for (int i = 0; i < MaxWorkers; i++)
            {
                if (_assignedWorkers[i] == -1)
                {
                    _assignedWorkers[i] = workerId;
                    _assignedCount++;
                    return true;
                }
            }
            return false;
        }
        finally
        {
            if (lockTaken) _lock.Exit();
        }
    }

    /// <summary>
    /// Removes a worker from this workplace.
    /// </summary>
    public bool UnassignWorker(int workerId)
    {
        bool lockTaken = false;
        try
        {
            _lock.Enter(ref lockTaken);
            for (int i = 0; i < MaxWorkers; i++)
            {
                if (_assignedWorkers[i] == workerId)
                {
                    _assignedWorkers[i] = -1;
                    _assignedCount--;
                    return true;
                }
            }
            return false;
        }
        finally
        {
            if (lockTaken) _lock.Exit();
        }
    }

    /// <summary>
    /// Adds a production order / recipe to the workplace queue.
    /// </summary>
    public int AddOrder(ushort recipeId, ushort productItemId, int targetCount, float duration = 2.0f, OrderRepeatMode repeat = OrderRepeatMode.Count)
    {
        bool lockTaken = false;
        try
        {
            _lock.Enter(ref lockTaken);
            if (_orderCount >= MaxOrdersPerWorkplace) return -1;

            int orderId = _nextOrderId++;
            _orders[_orderCount++] = new ProductionOrder
            {
                OrderId = orderId,
                RecipeId = recipeId,
                ProductItemId = productItemId,
                TargetCount = targetCount,
                CompletedCount = 0,
                InProgressCount = 0,
                WorkDuration = duration,
                RepeatMode = repeat,
                IsActive = true
            };
            return orderId;
        }
        finally
        {
            if (lockTaken) _lock.Exit();
        }
    }

    /// <summary>
    /// O(1) direct pull: an assigned idle worker requests their next task directly from this workplace.
    /// Zero spatial scanning, zero GC allocations.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryPullWork(int workerId, out NextGenJobData job)
    {
        job = default;
        bool lockTaken = false;
        try
        {
            _lock.Enter(ref lockTaken);

            if (_orderCount == 0) return false;

            for (int i = 0; i < _orderCount; i++)
            {
                int idx = (_currentOrderIndex + i) % _orderCount;
                ref var order = ref _orders[idx];

                if (order.HasRemainingWork)
                {
                    order.InProgressCount++;

                    job.Id = (Id << 16) | (order.OrderId & 0xFFFF);
                    job.Type = WorkplaceJobType;
                    job.Flags = JobFlags.Active | JobFlags.Claimed | JobFlags.IsWorkplaceJob;
                    job.SourceX = X;
                    job.SourceY = Y;
                    job.TargetX = X;
                    job.TargetY = Y;
                    job.StandX = StandX;
                    job.StandY = StandY;
                    job.ItemId = order.ProductItemId;
                    job.ItemCount = 1;
                    job.AssignedWorkerId = workerId;
                    job.WorkplaceId = Id;
                    job.WorkDuration = order.WorkDuration;
                    job.Progress = 0f;
                    job.CategoryMask = (uint)Category;
                    job.Priority = 150;
                    job.RequiredSkill = 0;
                    job.CustomPayload = order.RecipeId;
                    job.ChunkIndex = NextGenJobData.NullIndex;

                    return true;
                }
            }

            return false;
        }
        finally
        {
            if (lockTaken) _lock.Exit();
        }
    }

    /// <summary>
    /// Completes a single unit of production for an order at this workplace.
    /// </summary>
    public void CompleteOrderWork(int orderId)
    {
        bool lockTaken = false;
        try
        {
            _lock.Enter(ref lockTaken);

            for (int i = 0; i < _orderCount; i++)
            {
                ref var order = ref _orders[i];
                if (order.OrderId == orderId)
                {
                    if (order.InProgressCount > 0) order.InProgressCount--;
                    order.CompletedCount++;

                    if (order.RepeatMode == OrderRepeatMode.Count && order.CompletedCount >= order.TargetCount)
                    {
                        order.IsActive = false;
                        _currentOrderIndex = (_currentOrderIndex + 1) % _orderCount;
                    }
                    return;
                }
            }
        }
        finally
        {
            if (lockTaken) _lock.Exit();
        }
    }

    /// <summary>
    /// Cancels or releases in-progress work for an order.
    /// </summary>
    public void CancelOrderWork(int orderId)
    {
        bool lockTaken = false;
        try
        {
            _lock.Enter(ref lockTaken);
            for (int i = 0; i < _orderCount; i++)
            {
                ref var order = ref _orders[i];
                if (order.OrderId == orderId && order.InProgressCount > 0)
                {
                    order.InProgressCount--;
                    return;
                }
            }
        }
        finally
        {
            if (lockTaken) _lock.Exit();
        }
    }
}
