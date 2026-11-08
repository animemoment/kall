using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Game.Simulation.NextGenJobs;

/// <summary>
/// High-performance spatial chunk bucket index for dynamic designations (General Labor).
/// Divides world into 32x32 tile chunks with O(1) bitmask category skipping and atomic pull claiming.
/// Uses a flat pre-allocated struct slab for 0 B GC memory allocations.
/// </summary>
public sealed class SpatialJobGrid
{
    public const int ChunkShift = 5;      // 32x32 tiles per chunk (1 << 5)
    public const int ChunkSize = 1 << ChunkShift;
    public const int ChunkMask = ChunkSize - 1;

    public readonly int WorldWidth;
    public readonly int WorldHeight;
    public readonly int ChunksX;
    public readonly int ChunksY;
    public readonly int TotalChunks;

    private readonly NextGenJobData[] _jobs;
    private readonly int[] _freeStack;
    private int _freeCount;
    private SpinLock _poolLock = new(false);

    private readonly Chunk[] _chunks;
    private readonly ushort[] _categoryCounts; // TotalChunks * 32

    private static readonly SpiralOffset[] SpiralTable;

    public struct SpiralOffset
    {
        public sbyte Dx;
        public sbyte Dy;
        public ushort DistSq;
    }

    public struct Chunk
    {
        public int HeadJobIndex;
        public int JobCount;
        public volatile uint AvailableCategoryMask;
        public SpinLock Lock;

        public void Init()
        {
            HeadJobIndex = NextGenJobData.NullIndex;
            JobCount = 0;
            AvailableCategoryMask = 0;
            Lock = new SpinLock(false);
        }
    }

    static SpatialJobGrid()
    {
        const int maxRadius = 32;
        int maxCount = (2 * maxRadius + 1) * (2 * maxRadius + 1);
        var temp = new SpiralOffset[maxCount];
        int count = 0;

        for (int dy = -maxRadius; dy <= maxRadius; dy++)
        {
            for (int dx = -maxRadius; dx <= maxRadius; dx++)
            {
                temp[count++] = new SpiralOffset
                {
                    Dx = (sbyte)dx,
                    Dy = (sbyte)dy,
                    DistSq = (ushort)(dx * dx + dy * dy)
                };
            }
        }

        Array.Sort(temp, 0, count, Comparer<SpiralOffset>.Create((a, b) => a.DistSq.CompareTo(b.DistSq)));
        SpiralTable = temp;
    }

    public SpatialJobGrid(int worldWidth = 1024, int worldHeight = 1024, int maxCapacity = 131072)
    {
        WorldWidth = worldWidth;
        WorldHeight = worldHeight;
        ChunksX = (worldWidth + ChunkSize - 1) >> ChunkShift;
        ChunksY = (worldHeight + ChunkSize - 1) >> ChunkShift;
        TotalChunks = ChunksX * ChunksY;

        _chunks = new Chunk[TotalChunks];
        for (int i = 0; i < TotalChunks; i++)
        {
            _chunks[i].Init();
        }

        _categoryCounts = new ushort[TotalChunks * 32];

        _jobs = new NextGenJobData[maxCapacity];
        _freeStack = new int[maxCapacity];
        _freeCount = maxCapacity;

        for (int i = 0; i < maxCapacity; i++)
        {
            _jobs[i].Reset();
            _jobs[i].Id = i;
            _freeStack[maxCapacity - 1 - i] = i;
        }
    }

    public int ActiveJobCount
    {
        get
        {
            bool lockTaken = false;
            try
            {
                _poolLock.Enter(ref lockTaken);
                return _jobs.Length - _freeCount;
            }
            finally
            {
                if (lockTaken) _poolLock.Exit();
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetChunkIndex(int tileX, int tileY)
    {
        int cx = tileX >> ChunkShift;
        int cy = tileY >> ChunkShift;
        if ((uint)cx >= (uint)ChunksX || (uint)cy >= (uint)ChunksY) return -1;
        return cy * ChunksX + cx;
    }

    /// <summary>
    /// Registers a new dynamic world designation in O(1) time without any GC allocation.
    /// </summary>
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
        int chunkIdx = GetChunkIndex(targetX, targetY);
        if (chunkIdx < 0) return -1;

        int slot;
        bool poolLockTaken = false;
        try
        {
            _poolLock.Enter(ref poolLockTaken);
            if (_freeCount == 0) return -1;
            slot = _freeStack[--_freeCount];
        }
        finally
        {
            if (poolLockTaken) _poolLock.Exit();
        }

        uint categoryMask = (uint)type.GetCategory();
        int catBit = BitOperations.TrailingZeroCount(categoryMask);

        ref var job = ref _jobs[slot];
        job.Id = slot;
        job.Type = type;
        job.Flags = JobFlags.Active;
        job.SourceX = sourceX;
        job.SourceY = sourceY;
        job.TargetX = targetX;
        job.TargetY = targetY;
        job.StandX = standX;
        job.StandY = standY;
        job.ItemId = itemId;
        job.ItemCount = itemCount;
        job.AssignedWorkerId = NextGenJobData.UnassignedWorker;
        job.WorkplaceId = NextGenJobData.NoWorkplace;
        job.WorkDuration = duration;
        job.Progress = 0f;
        job.CategoryMask = categoryMask;
        job.Priority = priority;
        job.RequiredSkill = requiredSkill;
        job.CustomPayload = customPayload;
        job.ChunkIndex = chunkIdx;

        bool chunkLockTaken = false;
        try
        {
            _chunks[chunkIdx].Lock.Enter(ref chunkLockTaken);

            job.NextInChunk = _chunks[chunkIdx].HeadJobIndex;
            job.PrevInChunk = NextGenJobData.NullIndex;

            if (_chunks[chunkIdx].HeadJobIndex != NextGenJobData.NullIndex)
            {
                _jobs[_chunks[chunkIdx].HeadJobIndex].PrevInChunk = slot;
            }

            _chunks[chunkIdx].HeadJobIndex = slot;
            _chunks[chunkIdx].JobCount++;

            if (catBit < 32)
            {
                _categoryCounts[(chunkIdx << 5) | catBit]++;
            }
            _chunks[chunkIdx].AvailableCategoryMask |= categoryMask;
        }
        finally
        {
            if (chunkLockTaken) _chunks[chunkIdx].Lock.Exit();
        }

        return slot;
    }

    /// <summary>
    /// Removes a designation completely (upon completion or cancellation) in O(1) time.
    /// </summary>
    public bool RemoveJob(int jobId)
    {
        if ((uint)jobId >= (uint)_jobs.Length) return false;

        ref var job = ref _jobs[jobId];
        if ((job.Flags & JobFlags.Active) == 0) return false;

        int chunkIdx = job.ChunkIndex;
        if (chunkIdx < 0 || chunkIdx >= TotalChunks) return false;

        bool wasUnclaimed = job.AssignedWorkerId == NextGenJobData.UnassignedWorker;
        uint catMask = job.CategoryMask;
        int catBit = BitOperations.TrailingZeroCount(catMask);

        bool chunkLockTaken = false;
        try
        {
            _chunks[chunkIdx].Lock.Enter(ref chunkLockTaken);

            if (job.PrevInChunk != NextGenJobData.NullIndex)
            {
                _jobs[job.PrevInChunk].NextInChunk = job.NextInChunk;
            }
            else
            {
                _chunks[chunkIdx].HeadJobIndex = job.NextInChunk;
            }

            if (job.NextInChunk != NextGenJobData.NullIndex)
            {
                _jobs[job.NextInChunk].PrevInChunk = job.PrevInChunk;
            }

            _chunks[chunkIdx].JobCount--;

            if (wasUnclaimed && catBit < 32)
            {
                int catIndex = (chunkIdx << 5) | catBit;
                if (_categoryCounts[catIndex] > 0)
                {
                    _categoryCounts[catIndex]--;
                    if (_categoryCounts[catIndex] == 0)
                    {
                        _chunks[chunkIdx].AvailableCategoryMask &= ~catMask;
                    }
                }
            }

            job.Flags = JobFlags.None;
            job.AssignedWorkerId = NextGenJobData.UnassignedWorker;
            job.Generation++;
            job.NextInChunk = NextGenJobData.NullIndex;
            job.PrevInChunk = NextGenJobData.NullIndex;
            job.ChunkIndex = NextGenJobData.NullIndex;
        }
        finally
        {
            if (chunkLockTaken) _chunks[chunkIdx].Lock.Exit();
        }

        bool poolLockTaken = false;
        try
        {
            _poolLock.Enter(ref poolLockTaken);
            _freeStack[_freeCount++] = jobId;
        }
        finally
        {
            if (poolLockTaken) _poolLock.Exit();
        }

        return true;
    }

    /// <summary>
    /// Hot-loop pull query: radial nearest job scan with bitmask category skipping and atomic CAS claiming.
    /// Strictly 0 B GC memory allocations.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryPullNearestJob(
        short workerX, short workerY,
        JobCategoryFlags allowedCategories,
        int workerId,
        int maxRadiusChunks,
        out NextGenJobData claimedJob)
    {
        claimedJob = default;
        uint allowedMask = (uint)allowedCategories;
        if (allowedMask == 0) return false;

        int centerCx = workerX >> ChunkShift;
        int centerCy = workerY >> ChunkShift;

        int maxDistSq = maxRadiusChunks * maxRadiusChunks;
        int tableLength = SpiralTable.Length;

        for (int i = 0; i < tableLength; i++)
        {
            ref readonly var offset = ref SpiralTable[i];
            if (offset.DistSq > maxDistSq) break;

            int cx = centerCx + offset.Dx;
            int cy = centerCy + offset.Dy;

            if ((uint)cx >= (uint)ChunksX || (uint)cy >= (uint)ChunksY) continue;

            int chunkIdx = cy * ChunksX + cx;

            // 1-CYCLE FAST REJECTION
            if ((_chunks[chunkIdx].AvailableCategoryMask & allowedMask) == 0) continue;

            bool chunkLockTaken = false;
            try
            {
                _chunks[chunkIdx].Lock.Enter(ref chunkLockTaken);

                if ((_chunks[chunkIdx].AvailableCategoryMask & allowedMask) == 0) continue;

                int current = _chunks[chunkIdx].HeadJobIndex;
                while (current != NextGenJobData.NullIndex)
                {
                    ref var cand = ref _jobs[current];

                    if ((cand.CategoryMask & allowedMask) != 0 &&
                        cand.AssignedWorkerId == NextGenJobData.UnassignedWorker &&
                        (cand.Flags & JobFlags.Active) != 0)
                    {
                        if (Interlocked.CompareExchange(ref cand.AssignedWorkerId, workerId, NextGenJobData.UnassignedWorker) == NextGenJobData.UnassignedWorker)
                        {
                            cand.Flags |= JobFlags.Claimed;

                            int catBit = BitOperations.TrailingZeroCount(cand.CategoryMask);
                            if (catBit < 32)
                            {
                                int catIndex = (chunkIdx << 5) | catBit;
                                if (_categoryCounts[catIndex] > 0)
                                {
                                    _categoryCounts[catIndex]--;
                                    if (_categoryCounts[catIndex] == 0)
                                    {
                                        _chunks[chunkIdx].AvailableCategoryMask &= ~cand.CategoryMask;
                                    }
                                }
                            }

                            claimedJob = cand;
                            return true;
                        }
                    }

                    current = cand.NextInChunk;
                }
            }
            finally
            {
                if (chunkLockTaken) _chunks[chunkIdx].Lock.Exit();
            }
        }

        return false;
    }

    /// <summary>
    /// Releases a previously claimed job back into the available pool.
    /// </summary>
    public bool ReleaseJob(int jobId, int workerId)
    {
        if ((uint)jobId >= (uint)_jobs.Length) return false;

        ref var job = ref _jobs[jobId];
        if (job.AssignedWorkerId != workerId) return false;

        int chunkIdx = job.ChunkIndex;
        if (chunkIdx < 0 || chunkIdx >= TotalChunks) return false;

        bool chunkLockTaken = false;
        try
        {
            _chunks[chunkIdx].Lock.Enter(ref chunkLockTaken);

            if (Interlocked.CompareExchange(ref job.AssignedWorkerId, NextGenJobData.UnassignedWorker, workerId) == workerId)
            {
                job.Flags &= ~JobFlags.Claimed;

                int catBit = BitOperations.TrailingZeroCount(job.CategoryMask);
                if (catBit < 32)
                {
                    _categoryCounts[(chunkIdx << 5) | catBit]++;
                    _chunks[chunkIdx].AvailableCategoryMask |= job.CategoryMask;
                }
                return true;
            }
        }
        finally
        {
            if (chunkLockTaken) _chunks[chunkIdx].Lock.Exit();
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref readonly NextGenJobData GetJob(int jobId)
    {
        return ref _jobs[jobId];
    }
}
