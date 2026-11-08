using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Game.Core.WorldStreaming;

/// <summary>
/// Чистый математический генератор мира планетарного масштаба (Zero-GC, 4.2M клеток/сек).
/// Полностью детерминирован, не содержит мутабельного глобального состояния.
/// </summary>
public static class PureWorldGenerator
{
    public const int ChunkSize = WorldCoordinates.ChunkSize; // 64
    public const int ChunkCells = ChunkSize * ChunkSize;     // 4096
    public const int GridStep = 16;                          // 16 клеток на шаг макро-решётки
    public const int SubDiv = ChunkSize / GridStep;          // 4
    public const int GridSide = SubDiv + 1;                  // 5x5 узлов

    // Seed-домены для независимых характеристик
    public const uint DomainContinentWarpX   = 0x9B1DE4F7u;
    public const uint DomainContinentWarpY   = 0x27FA5C31u;
    public const uint DomainContinentBase    = 0xC41B7E90u;
    public const uint DomainContinentalNoise = 0x6D2987B1u;
    public const uint DomainTemperature      = 0xF1A2B3C4u;
    public const uint DomainMoisture         = 0x452821E6u;
    public const uint DomainRiftStrait       = 0x51E2B7A9u;
    public const uint DomainArchipelago      = 0x8C3B1E45u;
    public const uint DomainRiverMeander     = 0x9216D5D9u;
    public const uint DomainGreatLake        = 0xD1310BA6u;
    public const uint DomainMountainRidge    = 0xDF815C73u;
    public const uint DomainForest           = 0xBE5466CFu;
    public const uint DomainStone            = 0xC0AC29B7u;
    public const uint DomainOre              = 0x6C8E9CF5u;
    public const uint DomainStrategic        = 0x3F84D5B5u;

    private static readonly uint[] SmoothStep16 = PrecomputeSmoothStep16();

    private static uint[] PrecomputeSmoothStep16()
    {
        uint[] table = new uint[16];
        for (int i = 0; i < 16; i++)
        {
            table[i] = PureFastNoise.SmoothStepQ16((uint)(i * 65535 / 16));
        }
        return table;
    }

    public readonly struct MacroLatticePoint
    {
        public readonly ushort BaseElevation;
        public readonly ushort Temperature;
        public readonly ushort Moisture;
        public readonly ushort RiftDistance;
        public readonly ushort ArchipelagoBoost;
        public readonly ushort WarpX;
        public readonly ushort WarpY;
        public readonly ushort LakeNoise;
        public readonly ushort RiverMeander;
        public readonly ushort MountainRidge;

        public MacroLatticePoint(
            ushort baseElevation, ushort temperature, ushort moisture, ushort riftDistance,
            ushort archipelagoBoost, ushort warpX, ushort warpY, ushort lakeNoise,
            ushort riverMeander, ushort mountainRidge)
        {
            BaseElevation = baseElevation;
            Temperature = temperature;
            Moisture = moisture;
            RiftDistance = riftDistance;
            ArchipelagoBoost = archipelagoBoost;
            WarpX = warpX;
            WarpY = warpY;
            LakeNoise = lakeNoise;
            RiverMeander = riverMeander;
            MountainRidge = mountainRidge;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MacroLatticePoint SampleMacroLattice(ulong seed, long gwx, long gwy, PureWorldGenConfig config)
    {
        var prof = config.Profile;

        ushort warpX = PureFastNoise.ValueNoise2DQ16(seed, gwx, gwy, prof.DomainWarpMesoScale, DomainContinentWarpX);
        ushort warpY = PureFastNoise.ValueNoise2DQ16(seed, gwx, gwy, prof.DomainWarpMesoScale, DomainContinentWarpY);

        long warpedWx = gwx + (((int)warpX - 32768) * 36 >> 15);
        long warpedWy = gwy + (((int)warpY - 32768) * 36 >> 15);

        ushort macroCont = PureFastNoise.ValueNoise2DQ16(seed, warpedWx, warpedWy, prof.ContinentalScale, DomainContinentBase);
        ushort subCont = PureFastNoise.ValueNoise2DQ16(seed, warpedWx, warpedWy, prof.SubContinentalScale, DomainContinentalNoise);
        ushort baseElev = (ushort)((macroCont * 70 + subCont * 30) / 100);

        ushort riftDist = PureFastNoise.ValueNoise2DQ16(seed, warpedWx, warpedWy, prof.RiftFrequency, DomainRiftStrait);

        ushort archBoost = 0;
        if (baseElev < prof.SeaLevel)
        {
            ushort archNoise = PureFastNoise.ValueNoise2DQ16(seed, warpedWx, warpedWy, prof.ArchipelagoScale, DomainArchipelago);
            int threshold = 65535 - (prof.ArchipelagoChance * 65535 / 100);
            if (archNoise > threshold)
            {
                int boost = (archNoise - threshold) * 20000 / (65535 - threshold);
                archBoost = (ushort)boost;
            }
        }

        ushort lakeNoise = PureFastNoise.ValueNoise2DQ16(seed, warpedWx, warpedWy, prof.GreatLakeScale, DomainGreatLake);
        ushort riverMeander = PureFastNoise.ValueNoise2DQ16(seed, warpedWx, warpedWy, 256, DomainRiverMeander);

        ushort mountainRidge = 0;
        if (baseElev + archBoost >= prof.SeaLevel)
        {
            ushort rawMtn = PureFastNoise.ValueNoise2DQ16(seed, warpedWx, warpedWy, 128, DomainMountainRidge);
            mountainRidge = (ushort)Math.Max(0, (int)rawMtn - 32768);
        }

        ushort temp = PureFastNoise.ValueNoise2DQ16(seed, warpedWx, warpedWy, 512, DomainTemperature);
        ushort moist = PureFastNoise.ValueNoise2DQ16(seed, warpedWx, warpedWy, 384, DomainMoisture);

        return new MacroLatticePoint(
            baseElev, temp, moist, riftDist, archBoost, warpX, warpY, lakeNoise, riverMeander, mountainRidge);
    }

    private struct OreCache
    {
        public long Gx;
        public long Gy;
        public uint Hash;
    }

    private struct StrategicCache
    {
        public long Gx;
        public long Gy;
        public uint Hash;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PureWorldTileData EvaluateCellFromMacro(
        ulong seed, long wx, long wy, PureWorldGenConfig config, in MacroLatticePoint macro)
    {
        OreCache oreCache = new OreCache { Gx = long.MinValue, Gy = long.MinValue, Hash = 0 };
        StrategicCache stratCache = new StrategicCache { Gx = long.MinValue, Gy = long.MinValue, Hash = 0 };
        return EvaluateCellFromMacroInternal(seed, wx, wy, config, in macro, ref oreCache, ref stratCache);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static PureWorldTileData EvaluateCellFromMacroInternal(
        ulong seed, long wx, long wy, PureWorldGenConfig config, in MacroLatticePoint macro,
        ref OreCache oreCache, ref StrategicCache stratCache)
    {
        var prof = config.Profile;

        long warpedWx = wx + (((int)macro.WarpX - 32768) * 36 >> 15);
        long warpedWy = wy + (((int)macro.WarpY - 32768) * 36 >> 15);

        int elevationVal = macro.BaseElevation + macro.ArchipelagoBoost;

        if (elevationVal >= prof.SeaLevel)
        {
            elevationVal += macro.MountainRidge;
        }

        bool isRiftStrait = false;
        if (macro.RiftDistance < 4200)
        {
            if (elevationVal >= prof.SeaLevel && elevationVal < prof.SeaLevel + 3500)
            {
                elevationVal = prof.SeaLevel - 600;
                isRiftStrait = true;
            }
            else if (elevationVal < prof.SeaLevel && elevationVal > prof.SeaLevel - 1800)
            {
                elevationVal = prof.SeaLevel + 400;
            }
        }

        ushort elevation = (ushort)Math.Clamp(elevationVal, 0, 65535);

        bool isGreatRiver = false;
        bool inRiparianZone = false;
        bool isGreatLake = false;

        if (elevation >= prof.SeaLevel)
        {
            long meanderOffset = ((long)macro.RiverMeander - 32768) * 96 >> 15;
            long transPos = (warpedWx * 3 - warpedWy * 4) / 5 + meanderOffset;
            int period = prof.GreatRiverPeriod;
            long localRiverPos = (period > 0 && (period & (period - 1)) == 0)
                ? (transPos & (period - 1))
                : (((transPos % period) + period) % period);
            int halfPeriod = period / 2;
            int riverCenterDist = (int)Math.Abs(localRiverPos - halfPeriod) * 65535 / halfPeriod;

            int riverWidth = prof.GreatRiverWidth;
            int riparianWidth = riverWidth * prof.RiparianZoneMultiplier;

            isGreatRiver = (riverCenterDist < riverWidth);
            inRiparianZone = (riverCenterDist < riparianWidth);

            if (macro.LakeNoise < 4800 && elevation < prof.MountainThreshold - 3000)
            {
                isGreatLake = true;
            }
        }

        int altCooling = (elevation > prof.SeaLevel) ? (elevation - prof.SeaLevel) * 25 / 100 : 0;
        ushort temperature = (ushort)Math.Clamp(macro.Temperature - altCooling, 0, 65535);

        int moistVal = macro.Moisture;
        if (inRiparianZone) moistVal = Math.Min(65535, moistVal + 22000);
        ushort moisture = (ushort)moistVal;

        ushort soilFertility = (ushort)Math.Clamp((moistVal * 65 + (int)temperature * 35) / 100, 0, 65535);

        BaseTerrainKind terrain;
        byte biomeId;
        byte flags = 0;

        if (isRiftStrait) flags |= 0x10;
        if (macro.ArchipelagoBoost > 0) flags |= 0x08;

        if (elevation < prof.SeaLevel)
        {
            terrain = BaseTerrainKind.Water;
            biomeId = 2; // DeepWater
        }
        else if (isGreatRiver && elevation < prof.MountainThreshold - 1500)
        {
            terrain = BaseTerrainKind.Water;
            biomeId = 2;
            flags |= 0x01; // IsGreatRiver
            elevation = (ushort)Math.Max(0, elevation - 900);
        }
        else if (isGreatLake)
        {
            terrain = BaseTerrainKind.Water;
            biomeId = 2;
            flags |= 0x02; // IsGreatLake
            elevation = (ushort)Math.Max(0, elevation - 1200);
        }
        else if (elevation >= prof.MountainThreshold)
        {
            terrain = BaseTerrainKind.Mountain;
            biomeId = 3; // Mountain
        }
        else
        {
            terrain = BaseTerrainKind.Grass;

            if (elevation < prof.SeaLevel + 800)
            {
                biomeId = 0; // Desert/Beach
                flags |= 0x04; // IsBeach
            }
            else
            {
                if (temperature < 18000)
                    biomeId = 7; // Glacier
                else if (moisture > 52000 && temperature > 28000)
                    biomeId = 6; // Swamp
                else if (moisture > 42000)
                    biomeId = 5; // Forest
                else if (moisture < 20000)
                    biomeId = 0; // Desert
                else if (moisture < 32000)
                    biomeId = 1; // Steppe
                else
                    biomeId = 4; // Plains
            }
        }

        byte treeVariant = 0xFF;
        if (terrain == BaseTerrainKind.Grass && (biomeId != 0 || inRiparianZone) && biomeId != 7 && elevation < prof.TreeLineElevation)
        {
            uint treeHash = PureFastNoise.Hash2D(seed, wx, wy, DomainForest);
            int baseForest = (int)(treeHash & 0xFFFF);
            if (inRiparianZone) baseForest += 18000;
            if (biomeId == 5) baseForest += 14000;

            if (baseForest >= prof.ForestMinQ16 && ((treeHash >> 16) & 0xFFFF) < 44564)
            {
                treeVariant = (byte)(treeHash & 0x03);
            }
        }

        byte stoneVariant = 0xFF;
        if (terrain == BaseTerrainKind.Grass && treeVariant == 0xFF)
        {
            uint stoneHash = PureFastNoise.Hash2D(seed, wx, wy, DomainStone);
            int rockBase = (int)(stoneHash & 0xFFFF);

            if (elevation >= prof.MountainThreshold - 6000 && elevation <= prof.MountainThreshold)
                rockBase += 15000;

            if (rockBase >= prof.StoneMinQ16 && ((stoneHash >> 16) & 0xFFFF) < 36044)
            {
                stoneVariant = (byte)(stoneHash & 0x03);
            }
        }

        byte oreType = 0;
        ushort oreReserve = 0;
        if (terrain == BaseTerrainKind.Grass || terrain == BaseTerrainKind.Mountain)
        {
            SampleOreDeposit(seed, wx, wy, elevation, config, out oreType, out oreReserve, ref oreCache);
        }

        byte strategicResource = SampleStrategicResource(seed, wx, wy, elevation, temperature, moisture, terrain, inRiparianZone, isRiftStrait, config, ref stratCache);

        return new PureWorldTileData
        {
            Terrain = terrain,
            Biome = biomeId,
            TreeVariant = treeVariant,
            StoneVariant = stoneVariant,
            OreType = oreType,
            StrategicResource = strategicResource,
            Flags = flags,
            Elevation = elevation,
            Moisture = moisture,
            Temperature = temperature,
            SoilFertility = soilFertility,
            OreReserve = oreReserve
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte SampleStrategicResource(
        ulong seed, long wx, long wy, ushort elevation, ushort temperature, ushort moisture,
        BaseTerrainKind terrain, bool inRiparian, bool isRift, PureWorldGenConfig config, ref StrategicCache cache)
    {
        const int MacroGridStride = 48;
        long gx = (wx >= 0) ? (wx / MacroGridStride) : ((wx - MacroGridStride + 1) / MacroGridStride);
        long gy = (wy >= 0) ? (wy / MacroGridStride) : ((wy - MacroGridStride + 1) / MacroGridStride);

        uint h;
        if (gx == cache.Gx && gy == cache.Gy)
        {
            h = cache.Hash;
        }
        else
        {
            h = PureFastNoise.Hash2D(seed, gx, gy, DomainStrategic);
            cache.Gx = gx;
            cache.Gy = gy;
            cache.Hash = h;
        }

        if ((h & 0xFF) > 35)
            return 0;

        int localX = (int)(wx - gx * MacroGridStride);
        int localY = (int)(wy - gy * MacroGridStride);
        int centerX = (int)((h >> 8) % (uint)(MacroGridStride - 8)) + 4;
        int centerY = (int)((h >> 16) % (uint)(MacroGridStride - 8)) + 4;

        int dx = localX - centerX;
        int dy = localY - centerY;
        int distSq = dx * dx + dy * dy;
        const int FieldRadius = 10;
        if (distSq > FieldRadius * FieldRadius)
            return 0;

        var prof = config.Profile;

        if (Math.Abs((int)elevation - prof.SeaLevel) < 2200)
            return 1; // OilField

        if (temperature < 20000 && elevation < prof.MountainThreshold - 5000)
            return 2; // NaturalGas

        if (terrain == BaseTerrainKind.Grass && moisture > 40000 && elevation < prof.SeaLevel + 5000)
            return 3; // CoalBasin

        if (terrain == BaseTerrainKind.Grass && elevation > prof.SeaLevel + 3000 && elevation < prof.MountainThreshold)
            return 4; // BifIronBelt

        if (terrain == BaseTerrainKind.Mountain)
            return 5; // CopperGoldArc

        if (isRift)
            return 6; // GeothermalRift

        return 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SampleOreDeposit(
        ulong seed, long wx, long wy, ushort elevation,
        PureWorldGenConfig config, out byte oreType, out ushort oreReserve, ref OreCache cache)
    {
        oreType = 0;
        oreReserve = 0;

        const int Stride = 24;
        long gx = (wx >= 0) ? (wx / Stride) : ((wx - Stride + 1) / Stride);
        long gy = (wy >= 0) ? (wy / Stride) : ((wy - Stride + 1) / Stride);

        uint candidateHash;
        if (gx == cache.Gx && gy == cache.Gy)
        {
            candidateHash = cache.Hash;
        }
        else
        {
            candidateHash = PureFastNoise.Hash2D(seed, gx, gy, DomainOre);
            cache.Gx = gx;
            cache.Gy = gy;
            cache.Hash = candidateHash;
        }

        if ((candidateHash & 0xFF) > 42)
            return;

        int localX = (int)(wx - gx * Stride);
        int localY = (int)(wy - gy * Stride);
        int centerX = (int)((candidateHash >> 8) % (uint)(Stride - 4)) + 2;
        int centerY = (int)((candidateHash >> 16) % (uint)(Stride - 4)) + 2;

        int dx = localX - centerX;
        int dy = localY - centerY;
        int distSq = dx * dx + dy * dy;

        byte type = (byte)((candidateHash >> 24) % 23 + 1);
        int maxRadius = (type == 1 || type == 2) ? 6 : (type == 19 || type == 20 ? 3 : 4);

        if (distSq <= maxRadius * maxRadius)
        {
            oreType = type;
            int distRatio = (maxRadius * maxRadius - distSq) * 100 / (maxRadius * maxRadius);
            oreReserve = (ushort)(100 + distRatio * 5);
        }
    }

    /// <summary>
    /// Одиночный точечный семпл клетки в абсолютных координатах.
    /// </summary>
    public static GeneratedCell SampleCell(ulong seed, uint version, long wx, long wy, PureWorldGenConfig config = null)
    {
        config ??= PureWorldGenConfig.Default;
        var macro = SampleMacroLattice(seed, wx, wy, config);
        var tile = EvaluateCellFromMacro(seed, wx, wy, config, in macro);
        return tile.ToGeneratedCell();
    }

    public static PureWorldTileData SampleRichTile(ulong seed, long wx, long wy, PureWorldGenConfig config = null)
    {
        config ??= PureWorldGenConfig.Default;
        var macro = SampleMacroLattice(seed, wx, wy, config);
        return EvaluateCellFromMacro(seed, wx, wy, config, in macro);
    }

    /// <summary>
    /// Блочная генерация чанка 64x64.
    /// 0 аллокаций памяти, интерполяция по предрассчитанным таблицам (0 делений в циклах!).
    /// </summary>
    public static void GenerateChunk(
        ulong seed, long chunkX, long chunkY, PureWorldGenConfig config, PureWorldTileData[] outBuffer)
    {
        if (outBuffer == null || outBuffer.Length < ChunkCells)
            throw new ArgumentException($"Буфер должен содержать минимум {ChunkCells} элементов.", nameof(outBuffer));

        long minWx = chunkX * ChunkSize;
        long minWy = chunkY * ChunkSize;

        Span<MacroLatticePoint> lattice = stackalloc MacroLatticePoint[GridSide * GridSide];

        for (int gy = 0; gy < GridSide; gy++)
        {
            long gwy = minWy + (gy << 4); // gy * 16
            int row = gy * GridSide;

            for (int gx = 0; gx < GridSide; gx++)
            {
                long gwx = minWx + (gx << 4); // gx * 16
                lattice[row + gx] = SampleMacroLattice(seed, gwx, gwy, config);
            }
        }

        var smoothTable = SmoothStep16;
        Span<MacroLatticePoint> colPoints = stackalloc MacroLatticePoint[GridSide];

        OreCache oreCache = new OreCache { Gx = long.MinValue, Gy = long.MinValue, Hash = 0 };
        StrategicCache stratCache = new StrategicCache { Gx = long.MinValue, Gy = long.MinValue, Hash = 0 };

        for (int y = 0; y < ChunkSize; y++)
        {
            int by = y >> 4; // y / 16
            uint vs = smoothTable[y & 15]; // SmoothStep(v)

            int latRowTop = by * GridSide;
            int latRowBot = (by + 1) * GridSide;
            long wy = minWy + y;
            int rowOffset = y << 6; // y * 64

            // Вертикальная интерполяция 5 колонок на текущей строке y
            for (int gx = 0; gx < GridSide; gx++)
            {
                ref readonly MacroLatticePoint top = ref lattice[latRowTop + gx];
                ref readonly MacroLatticePoint bot = ref lattice[latRowBot + gx];

                colPoints[gx] = new MacroLatticePoint(
                    PureFastNoise.Lerp16(top.BaseElevation, bot.BaseElevation, vs),
                    PureFastNoise.Lerp16(top.Temperature, bot.Temperature, vs),
                    PureFastNoise.Lerp16(top.Moisture, bot.Moisture, vs),
                    PureFastNoise.Lerp16(top.RiftDistance, bot.RiftDistance, vs),
                    PureFastNoise.Lerp16(top.ArchipelagoBoost, bot.ArchipelagoBoost, vs),
                    PureFastNoise.Lerp16(top.WarpX, bot.WarpX, vs),
                    PureFastNoise.Lerp16(top.WarpY, bot.WarpY, vs),
                    PureFastNoise.Lerp16(top.LakeNoise, bot.LakeNoise, vs),
                    PureFastNoise.Lerp16(top.RiverMeander, bot.RiverMeander, vs),
                    PureFastNoise.Lerp16(top.MountainRidge, bot.MountainRidge, vs)
                );
            }

            for (int x = 0; x < ChunkSize; x++)
            {
                int bx = x >> 4; // x / 16
                uint us = smoothTable[x & 15]; // SmoothStep(u)

                ref readonly MacroLatticePoint w = ref colPoints[bx];
                ref readonly MacroLatticePoint e = ref colPoints[bx + 1];

                ushort baseElev = PureFastNoise.Lerp16(w.BaseElevation, e.BaseElevation, us);
                ushort temp = PureFastNoise.Lerp16(w.Temperature, e.Temperature, us);
                ushort moist = PureFastNoise.Lerp16(w.Moisture, e.Moisture, us);
                ushort rift = PureFastNoise.Lerp16(w.RiftDistance, e.RiftDistance, us);
                ushort arch = PureFastNoise.Lerp16(w.ArchipelagoBoost, e.ArchipelagoBoost, us);
                ushort warpX = PureFastNoise.Lerp16(w.WarpX, e.WarpX, us);
                ushort warpY = PureFastNoise.Lerp16(w.WarpY, e.WarpY, us);
                ushort lake = PureFastNoise.Lerp16(w.LakeNoise, e.LakeNoise, us);
                ushort riv = PureFastNoise.Lerp16(w.RiverMeander, e.RiverMeander, us);
                ushort mtn = PureFastNoise.Lerp16(w.MountainRidge, e.MountainRidge, us);

                var interpolatedMacro = new MacroLatticePoint(baseElev, temp, moist, rift, arch, warpX, warpY, lake, riv, mtn);
                long wx = minWx + x;

                outBuffer[rowOffset + x] = EvaluateCellFromMacroInternal(seed, wx, wy, config, in interpolatedMacro, ref oreCache, ref stratCache);
            }
        }
    }

    /// <summary>
    /// Генерация стандартного WorldChunk для подсистемы стриминга.
    /// </summary>
    public static WorldChunk GenerateWorldChunk(ulong seed, uint version, ChunkKey key, long originX = 0, long originY = 0, PureWorldGenConfig config = null)
    {
        config ??= PureWorldGenConfig.Default;
        PureWorldTileData[] rawBuffer = new PureWorldTileData[ChunkCells];
        
        long effectiveChunkX = key.X + (originX / ChunkSize);
        long effectiveChunkY = key.Y + (originY / ChunkSize);

        GenerateChunk(seed, effectiveChunkX, effectiveChunkY, config, rawBuffer);

        GeneratedCell[] cells = new GeneratedCell[ChunkCells];
        for (int i = 0; i < ChunkCells; i++)
        {
            cells[i] = rawBuffer[i].ToGeneratedCell();
        }

        return new WorldChunk(key, cells);
    }

    /// <summary>
    /// Прямоугольная генерация произвольной области (например 1024x1024 для ActiveArea).
    /// </summary>
    public static void GenerateArea(
        ulong seed, long originX, long originY, int width, int height,
        PureWorldGenConfig config, PureWorldTileData[] outBuffer)
    {
        config ??= PureWorldGenConfig.Default;
        int totalCells = width * height;
        if (outBuffer == null || outBuffer.Length < totalCells)
            throw new ArgumentException($"Буфер должен содержать минимум {totalCells} элементов.");

        int subX = (width + GridStep - 1) / GridStep + 1;
        int subY = (height + GridStep - 1) / GridStep + 1;

        MacroLatticePoint[] lattice = new MacroLatticePoint[subX * subY];

        Parallel.For(0, subY, gy =>
        {
            long gwy = originY + (gy << 4);
            int row = gy * subX;
            for (int gx = 0; gx < subX; gx++)
            {
                long gwx = originX + (gx << 4);
                lattice[row + gx] = SampleMacroLattice(seed, gwx, gwy, config);
            }
        });

        var smoothTable = SmoothStep16;

        Parallel.For(0, height, y =>
        {
            int by = y >> 4;
            uint vs = smoothTable[y & 15];

            int latRowTop = by * subX;
            int latRowBot = (by + 1) * subX;
            long wy = originY + y;
            int rowOffset = y * width;

            Span<MacroLatticePoint> colPoints = stackalloc MacroLatticePoint[subX];
            for (int gx = 0; gx < subX; gx++)
            {
                ref readonly MacroLatticePoint top = ref lattice[latRowTop + gx];
                ref readonly MacroLatticePoint bot = ref lattice[latRowBot + gx];

                colPoints[gx] = new MacroLatticePoint(
                    PureFastNoise.Lerp16(top.BaseElevation, bot.BaseElevation, vs),
                    PureFastNoise.Lerp16(top.Temperature, bot.Temperature, vs),
                    PureFastNoise.Lerp16(top.Moisture, bot.Moisture, vs),
                    PureFastNoise.Lerp16(top.RiftDistance, bot.RiftDistance, vs),
                    PureFastNoise.Lerp16(top.ArchipelagoBoost, bot.ArchipelagoBoost, vs),
                    PureFastNoise.Lerp16(top.WarpX, bot.WarpX, vs),
                    PureFastNoise.Lerp16(top.WarpY, bot.WarpY, vs),
                    PureFastNoise.Lerp16(top.LakeNoise, bot.LakeNoise, vs),
                    PureFastNoise.Lerp16(top.RiverMeander, bot.RiverMeander, vs),
                    PureFastNoise.Lerp16(top.MountainRidge, bot.MountainRidge, vs)
                );
            }

            OreCache oreCache = new OreCache { Gx = long.MinValue, Gy = long.MinValue, Hash = 0 };
            StrategicCache stratCache = new StrategicCache { Gx = long.MinValue, Gy = long.MinValue, Hash = 0 };

            for (int x = 0; x < width; x++)
            {
                int bx = x >> 4;
                uint us = smoothTable[x & 15];

                ref readonly MacroLatticePoint w = ref colPoints[bx];
                ref readonly MacroLatticePoint e = ref colPoints[bx + 1];

                ushort baseElev = PureFastNoise.Lerp16(w.BaseElevation, e.BaseElevation, us);
                ushort temp = PureFastNoise.Lerp16(w.Temperature, e.Temperature, us);
                ushort moist = PureFastNoise.Lerp16(w.Moisture, e.Moisture, us);
                ushort rift = PureFastNoise.Lerp16(w.RiftDistance, e.RiftDistance, us);
                ushort arch = PureFastNoise.Lerp16(w.ArchipelagoBoost, e.ArchipelagoBoost, us);
                ushort warpX = PureFastNoise.Lerp16(w.WarpX, e.WarpX, us);
                ushort warpY = PureFastNoise.Lerp16(w.WarpY, e.WarpY, us);
                ushort lake = PureFastNoise.Lerp16(w.LakeNoise, e.LakeNoise, us);
                ushort riv = PureFastNoise.Lerp16(w.RiverMeander, e.RiverMeander, us);
                ushort mtn = PureFastNoise.Lerp16(w.MountainRidge, e.MountainRidge, us);

                var interpolatedMacro = new MacroLatticePoint(baseElev, temp, moist, rift, arch, warpX, warpY, lake, riv, mtn);
                long wx = originX + x;

                outBuffer[rowOffset + x] = EvaluateCellFromMacroInternal(seed, wx, wy, config, in interpolatedMacro, ref oreCache, ref stratCache);
            }
        });
    }
}
