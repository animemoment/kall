using System;

namespace Game.Core.Soil;

[Flags]
public enum SoilCellFlags : byte
{
    None     = 0,
    Water    = 1 << 0,
    Grass    = 1 << 1,
    Mountain = 1 << 2,
    High     = 1 << 3,
    Forest   = 1 << 4,
    Farm     = 1 << 5,
    Downhill = 1 << 6
}
