using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Game.Simulation.NextGenJobs;

/// <summary>
/// Cache-line aligned (64-byte) unified Job Data representation (Zero-GC, struct-based).
/// Fits exactly into a single 64-byte L1 CPU cache line to eliminate split cache line penalties.
/// Supports lock-free atomic claiming via Interlocked.CompareExchange on AssignedWorkerId.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 64)]
public struct NextGenJobData
{
    [FieldOffset(0)]  public int Id;                  // Unique Job ID (index into job pool)
    [FieldOffset(4)]  public NextGenJobType Type;     // Concrete job type (2 bytes)
    [FieldOffset(6)]  public JobFlags Flags;          // Operational flags (2 bytes)
    [FieldOffset(8)]  public short SourceX;           // Origin / Pickup X coordinate
    [FieldOffset(10)] public short SourceY;           // Origin / Pickup Y coordinate
    [FieldOffset(12)] public short TargetX;           // Target / Work site X coordinate
    [FieldOffset(14)] public short TargetY;           // Target / Work site Y coordinate
    [FieldOffset(16)] public short StandX;            // Adjacent safe tile for worker standing
    [FieldOffset(18)] public short StandY;            // Adjacent safe tile for worker standing
    [FieldOffset(20)] public ushort ItemId;           // Target resource / item ID (if hauling/crafting)
    [FieldOffset(22)] public ushort ItemCount;        // Item quantity
    [FieldOffset(24)] public int AssignedWorkerId;    // -1 = Unclaimed, >=0 = Worker ID (CAS target)
    [FieldOffset(28)] public int WorkplaceId;         // -1 = General designation, >=0 = Owning workplace
    [FieldOffset(32)] public float WorkDuration;      // Base execution duration in seconds
    [FieldOffset(36)] public float Progress;          // Accumulated work progress [0..WorkDuration]
    [FieldOffset(40)] public uint CategoryMask;       // Bitwise JobCategoryFlags representation
    [FieldOffset(44)] public byte Priority;           // Priority level (0 = low, 255 = critical)
    [FieldOffset(45)] public byte RequiredSkill;      // Skill requirement (0 = none, or skill ID)
    [FieldOffset(46)] public ushort CustomPayload;    // Extra context (recipe ID, crop stage, etc.)
    [FieldOffset(48)] public uint Generation;         // Slot generation counter for ABA safety
    [FieldOffset(52)] public int NextInChunk;         // Intrusive linked-list next index (-1 = end)
    [FieldOffset(56)] public int PrevInChunk;         // Intrusive linked-list prev index (-1 = start)
    [FieldOffset(60)] public int ChunkIndex;          // Owning spatial chunk index (-1 = none/workplace)

    public const int UnassignedWorker = -1;
    public const int NoWorkplace = -1;
    public const int NullIndex = -1;

    public bool IsActive
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (Flags & JobFlags.Active) != 0;
    }

    public bool IsClaimed
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => AssignedWorkerId >= 0;
    }

    public bool IsAvailable
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (Flags & JobFlags.Active) != 0 && AssignedWorkerId == UnassignedWorker;
    }

    public bool IsWorkplace
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (Flags & JobFlags.IsWorkplaceJob) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reset()
    {
        Id = NullIndex;
        Type = NextGenJobType.None;
        Flags = JobFlags.None;
        SourceX = 0; SourceY = 0;
        TargetX = 0; TargetY = 0;
        StandX = 0; StandY = 0;
        ItemId = 0; ItemCount = 0;
        AssignedWorkerId = UnassignedWorker;
        WorkplaceId = NoWorkplace;
        WorkDuration = 0f;
        Progress = 0f;
        CategoryMask = 0;
        Priority = 100;
        RequiredSkill = 0;
        CustomPayload = 0;
        Generation++;
        NextInChunk = NullIndex;
        PrevInChunk = NullIndex;
        ChunkIndex = NullIndex;
    }
}
