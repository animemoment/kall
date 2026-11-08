using System;
using System.Runtime.InteropServices;

namespace Game.UI.Renderers.World;

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct TileRenderData
{
    public ushort FloorId;
    public ushort WallId;
    public ushort ObjectId;
    public byte Elevation;
    public byte LightLevel;

    public bool HasFloor => FloorId > 0;
    public bool HasWall => WallId > 0;
    public bool HasObject => ObjectId > 0;

    public TileRenderData(ushort floorId, ushort wallId = 0, ushort objectId = 0, byte elevation = 0, byte light = 255)
    {
        FloorId = floorId;
        WallId = wallId;
        ObjectId = objectId;
        Elevation = elevation;
        LightLevel = light;
    }
}
