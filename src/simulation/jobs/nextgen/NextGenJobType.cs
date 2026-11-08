using System;
using System.Runtime.CompilerServices;

namespace Game.Simulation.NextGenJobs;

/// <summary>
/// Specific job action types recognized across the next-gen simulation.
/// </summary>
public enum NextGenJobType : ushort
{
    None = 0,

    // Dynamic World Designations / General Labor
    TreeChopping      = 1,
    Mining            = 2,
    OreMining         = 3,
    Construction      = 4,
    BlueprintDelivery = 5,
    StockpileHauling  = 6,
    Digging           = 7,
    Harvesting        = 8,
    Planting          = 9,
    Tilling           = 10,
    Cleaning          = 11,

    // Stationary Workplace Tasks
    WorkshopCraft     = 50,
    Smithing          = 51,
    Smelting          = 52,
    Cooking           = 53,
    Brewing           = 54,
    Research          = 55,
    GuardPatrol       = 56,
    Tailoring         = 57
}

/// <summary>
/// Ultra-fast helper mapping NextGenJobType to JobCategoryFlags with zero branching via a flat lookup table.
/// </summary>
public static class NextGenJobTypeExtensions
{
    private const int MaxJobTypeValue = 128;
    private static readonly JobCategoryFlags[] CategoryLookup = new JobCategoryFlags[MaxJobTypeValue];

    static NextGenJobTypeExtensions()
    {
        CategoryLookup[(int)NextGenJobType.TreeChopping]      = JobCategoryFlags.Chop;
        CategoryLookup[(int)NextGenJobType.Mining]            = JobCategoryFlags.Mine;
        CategoryLookup[(int)NextGenJobType.OreMining]         = JobCategoryFlags.Mine;
        CategoryLookup[(int)NextGenJobType.Construction]      = JobCategoryFlags.Build;
        CategoryLookup[(int)NextGenJobType.BlueprintDelivery] = JobCategoryFlags.Haul;
        CategoryLookup[(int)NextGenJobType.StockpileHauling]  = JobCategoryFlags.Haul;
        CategoryLookup[(int)NextGenJobType.Digging]           = JobCategoryFlags.Dig;
        CategoryLookup[(int)NextGenJobType.Harvesting]        = JobCategoryFlags.Harvest;
        CategoryLookup[(int)NextGenJobType.Planting]          = JobCategoryFlags.Farm;
        CategoryLookup[(int)NextGenJobType.Tilling]           = JobCategoryFlags.Farm;
        CategoryLookup[(int)NextGenJobType.Cleaning]          = JobCategoryFlags.Clean;

        CategoryLookup[(int)NextGenJobType.WorkshopCraft]     = JobCategoryFlags.Craft;
        CategoryLookup[(int)NextGenJobType.Smithing]          = JobCategoryFlags.Smith;
        CategoryLookup[(int)NextGenJobType.Smelting]          = JobCategoryFlags.Smith;
        CategoryLookup[(int)NextGenJobType.Cooking]           = JobCategoryFlags.Cook;
        CategoryLookup[(int)NextGenJobType.Brewing]           = JobCategoryFlags.Cook;
        CategoryLookup[(int)NextGenJobType.Research]          = JobCategoryFlags.Research;
        CategoryLookup[(int)NextGenJobType.GuardPatrol]       = JobCategoryFlags.Guard;
        CategoryLookup[(int)NextGenJobType.Tailoring]         = JobCategoryFlags.Craft;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static JobCategoryFlags GetCategory(this NextGenJobType type)
    {
        int val = (int)type;
        return (uint)val < (uint)MaxJobTypeValue ? CategoryLookup[val] : JobCategoryFlags.None;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint GetCategoryMask(this NextGenJobType type)
    {
        return (uint)type.GetCategory();
    }
}
