using System.Runtime.InteropServices;

namespace Game.Core.WorldStreaming;

/// <summary>
/// Компактная 18-байтная структура клетки с полной поддержкой 23 типов руд,
/// стратегических ресурсов и тектонических флагов (Zero-GC).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PureWorldTileData
{
    public BaseTerrainKind Terrain;        // 1 байт (Grass=0, Water=1, Mountain=2)
    public byte Biome;                     // 1 байт (0..7 биомы Уиттекера)
    public byte TreeVariant;               // 1 байт (0..3 или 0xFF - нет дерева)
    public byte StoneVariant;              // 1 байт (0..3 или 0xFF - нет камня)
    public byte OreType;                   // 1 байт (1..23 тип руды, 0 - нет руды)
    public byte StrategicResource;         // 1 байт (1..6 стратегический ресурс, 0 - нет)
    public byte Flags;                     // 1 байт битовые флаги (0x01 GreatRiver, 0x02 GreatLake, 0x04 Beach, 0x08 Archipelago, 0x10 RiftStrait)
    public ushort Elevation;               // 2 байта (Q16: 0..65535)
    public ushort Moisture;                // 2 байта (Q16: 0..65535)
    public ushort Temperature;             // 2 байта (Q16: 0..65535)
    public ushort SoilFertility;           // 2 байта (Q16: 0..65535)
    public ushort OreReserve;              // 2 байта (запас руды)

    public bool IsGreatRiver => (Flags & 0x01) != 0;
    public bool IsGreatLake => (Flags & 0x02) != 0;
    public bool IsBeach => (Flags & 0x04) != 0;
    public bool IsArchipelago => (Flags & 0x08) != 0;
    public bool IsRiftStrait => (Flags & 0x10) != 0;

    public GeneratedCell ToGeneratedCell()
    {
        ushort forest = TreeVariant != 0xFF ? (ushort)45000 : (ushort)0;
        ushort stone = StoneVariant != 0xFF ? (ushort)40000 : (ushort)0;
        byte variant = TreeVariant != 0xFF ? TreeVariant : (StoneVariant != 0xFF ? StoneVariant : (byte)0);

        return new GeneratedCell(
            Terrain: Terrain,
            ElevationQ16: Elevation,
            MoistureQ16: Moisture,
            ForestQ16: forest,
            StoneQ16: stone,
            TemperatureQ16: Temperature,
            Biome: Biome,
            Variant: variant
        );
    }
}
