using System;

namespace Game.Simulation.NextGenJobs;

/// <summary>
/// Bitmask flags representing job categories for 1-cycle O(1) CPU bitwise filtering.
/// Each bit corresponds to a distinct labor discipline.
/// </summary>
[Flags]
public enum JobCategoryFlags : uint
{
    None        = 0,
    Haul        = 1 << 0,  // Moving items, logistics, supply
    Mine        = 1 << 1,  // Mining rock, mountain excavation, ore veins
    Build       = 1 << 2,  // Construction, repairing, blueprint placement
    Harvest     = 1 << 3,  // Harvesting wild plants, berry bushes, crops
    Chop        = 1 << 4,  // Tree felling, cutting timber, wood clearing
    Dig         = 1 << 5,  // Earthmoving, ditch digging, dirt excavation
    Craft       = 1 << 6,  // General workshop crafting, tailoring, woodworking
    Farm        = 1 << 7,  // Agricultural soil tilling, sowing seeds, watering
    Research    = 1 << 8,  // Laboratory study, tech development, transcribing
    Guard       = 1 << 9,  // Guard posts, sentry duty, patrolling territory
    Clean       = 1 << 10, // Sweeping debris, sanitizing, cleaning filth
    Cook        = 1 << 11, // Kitchen food preparation, butchery, brewing
    Smith       = 1 << 12, // Blacksmithing, metal smelting, weapon/tool forging

    // Category Groups
    AllGeneralLabor = Haul | Mine | Build | Harvest | Chop | Dig | Clean,
    AllWorkplace    = Craft | Farm | Research | Guard | Cook | Smith,
    All             = 0xFFFFFFFF
}

/// <summary>
/// Internal lifecycle and operational state flags for individual jobs.
/// </summary>
[Flags]
public enum JobFlags : ushort
{
    None           = 0,
    Active         = 1 << 0,  // Job is live and valid in the pool
    Claimed        = 1 << 1,  // Job has been claimed by a worker
    Completed      = 1 << 2,  // Job has finished successfully
    Cancelled      = 1 << 3,  // Job was cancelled by user, world event, or failure
    IsWorkplaceJob = 1 << 4,  // Job originated from a stationary workplace
    HighPriority   = 1 << 5,  // Expedited job priority
    Urgent         = 1 << 6,  // Critical / life-saving / colony-survival priority
    RequiresTool   = 1 << 7   // Worker must possess specific tool in hand
}
