using System;
using System.Threading.Tasks;

namespace Game.Core.WorldStreaming;

/// <summary>
/// Генератор мировой карты макро-регионов (512x512 клеток на 1 регион).
/// Полностью синхронизирован с локальным генератором PureWorldGenerator (100% agreement).
/// </summary>
public static class PureWorldMapGenerator
{
    public const int RegionCells = 512;

    public readonly struct RegionMacroData
    {
        public readonly ushort Elevation;
        public readonly ushort Temperature;
        public readonly ushort Moisture;
        public readonly byte Biome;
        public readonly BaseTerrainKind Terrain;
        public readonly byte StrategicResource;
        public readonly byte Flags;

        public bool IsLand => Terrain != BaseTerrainKind.Water;

        public RegionMacroData(
            ushort elevation, ushort temperature, ushort moisture,
            byte biome, BaseTerrainKind terrain, byte strategicResource, byte flags)
        {
            Elevation = elevation;
            Temperature = temperature;
            Moisture = moisture;
            Biome = biome;
            Terrain = terrain;
            StrategicResource = strategicResource;
            Flags = flags;
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static RegionMacroData SampleRegion(ulong seed, long rx, long ry, PureWorldGenConfig config = null)
    {
        config ??= PureWorldGenConfig.Default;
        long wx = rx * RegionCells + RegionCells / 2;
        long wy = ry * RegionCells + RegionCells / 2;

        var macro = PureWorldGenerator.SampleMacroLattice(seed, wx, wy, config);
        var tile = PureWorldGenerator.EvaluateCellFromMacro(seed, wx, wy, config, in macro);

        return new RegionMacroData(
            tile.Elevation,
            tile.Temperature,
            tile.Moisture,
            tile.Biome,
            tile.Terrain,
            tile.StrategicResource,
            tile.Flags
        );
    }

    public static RegionKey FindBestSpawn(ulong seed, int mapWidth = 2048, int mapHeight = 1024, PureWorldGenConfig config = null)
    {
        config ??= PureWorldGenConfig.Default;
        int centerRx = mapWidth / 2;
        int centerRy = mapHeight / 2;

        RegionKey bestFallback = new RegionKey(centerRx, centerRy);
        int bestFallbackScore = -1;

        for (int r = 0; r <= mapHeight / 2; r += 4)
        {
            if (r == 0)
            {
                var sample = SampleRegion(seed, centerRx, centerRy, config);
                if (sample.IsLand && sample.Terrain != BaseTerrainKind.Mountain && (sample.Biome == 4 || sample.Biome == 5 || sample.Biome == 1))
                    return new RegionKey(centerRx, centerRy);
                continue;
            }

            int minX = Math.Max(0, centerRx - r);
            int maxX = Math.Min(mapWidth - 1, centerRx + r);
            int minY = Math.Max(0, centerRy - r);
            int maxY = Math.Min(mapHeight - 1, centerRy + r);

            for (int x = minX; x <= maxX; x += 4)
            {
                if (CheckCandidate(x, minY, out var best)) return best;
                if (CheckCandidate(x, maxY, out best)) return best;
            }

            for (int y = minY; y <= maxY; y += 4)
            {
                if (CheckCandidate(minX, y, out var best)) return best;
                if (CheckCandidate(maxX, y, out best)) return best;
            }
        }

        return bestFallback;

        bool CheckCandidate(int rx, int ry, out RegionKey match)
        {
            match = default;
            var reg = SampleRegion(seed, rx, ry, config);
            if (!reg.IsLand || reg.Terrain == BaseTerrainKind.Mountain)
                return false;

            int score = (int)reg.Elevation;
            if (reg.Biome == 4 || reg.Biome == 5) score += 20000; // Plains or Forest
            if (reg.Biome == 1) score += 10000;                   // Steppe

            if (score > bestFallbackScore)
            {
                bestFallbackScore = score;
                bestFallback = new RegionKey(rx, ry);
            }

            if (reg.Biome == 4 || reg.Biome == 5)
            {
                match = new RegionKey(rx, ry);
                return true;
            }
            return false;
        }
    }

    public static void GeneratePreview(
        ulong seed, uint version, int width, int height,
        short[] outTiles, byte[] outPixels, byte[] tileColors, PureWorldGenConfig config = null)
    {
        config ??= PureWorldGenConfig.Default;
        int stride = 4;
        var popt = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };

        Parallel.For(0, height, popt, py =>
        {
            int y = py * stride;
            int rowOffset = py * width;
            for (int px = 0; px < width; px++)
            {
                int x = px * stride;
                var reg = SampleRegion(seed, x, y, config);
                int idx = rowOffset + px;

                short tileId = (short)reg.Biome;
                if (reg.Terrain == BaseTerrainKind.Water)
                    tileId = 2; // DeepWater
                else if (reg.Terrain == BaseTerrainKind.Mountain)
                    tileId = 3; // Mountain

                outTiles[idx] = tileId;

                int p = idx * 4;
                if (tileColors != null && tileColors.Length >= (tileId + 1) * 3)
                {
                    int c = tileId * 3;
                    outPixels[p] = tileColors[c];
                    outPixels[p + 1] = tileColors[c + 1];
                    outPixels[p + 2] = tileColors[c + 2];
                    outPixels[p + 3] = 255;
                }
                else
                {
                    uint colorHex = GetBiomeColorHex(reg.Biome, reg.Terrain, reg.StrategicResource);
                    outPixels[p] = (byte)((colorHex >> 24) & 0xFF);
                    outPixels[p + 1] = (byte)((colorHex >> 16) & 0xFF);
                    outPixels[p + 2] = (byte)((colorHex >> 8) & 0xFF);
                    outPixels[p + 3] = 255;
                }
            }
        });
    }

    public static void GenerateFullMap(
        ulong seed, uint version, int width, int height,
        short[] outTiles, byte[] outPixels, byte[] tileColors, PureWorldGenConfig config = null)
    {
        config ??= PureWorldGenConfig.Default;
        var popt = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };

        Parallel.For(0, height, popt, y =>
        {
            int rowOffset = y * width;
            for (int x = 0; x < width; x++)
            {
                var reg = SampleRegion(seed, x, y, config);
                int idx = rowOffset + x;

                short tileId = (short)reg.Biome;
                if (reg.Terrain == BaseTerrainKind.Water)
                    tileId = 2;
                else if (reg.Terrain == BaseTerrainKind.Mountain)
                    tileId = 3;

                outTiles[idx] = tileId;

                int p = idx * 4;
                if (tileColors != null && tileColors.Length >= (tileId + 1) * 3)
                {
                    int c = tileId * 3;
                    outPixels[p] = tileColors[c];
                    outPixels[p + 1] = tileColors[c + 1];
                    outPixels[p + 2] = tileColors[c + 2];
                    outPixels[p + 3] = 255;
                }
                else
                {
                    uint colorHex = GetBiomeColorHex(reg.Biome, reg.Terrain, reg.StrategicResource);
                    outPixels[p] = (byte)((colorHex >> 24) & 0xFF);
                    outPixels[p + 1] = (byte)((colorHex >> 16) & 0xFF);
                    outPixels[p + 2] = (byte)((colorHex >> 8) & 0xFF);
                    outPixels[p + 3] = 255;
                }
            }
        });
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static uint GetBiomeColorHex(byte biome, BaseTerrainKind terrain, byte strategicResource)
    {
        if (strategicResource > 0)
        {
            return strategicResource switch
            {
                1 => 0x222222_FFu, // OilField (черный)
                2 => 0x00E5FF_FFu, // NaturalGas (голубой неон)
                3 => 0x424242_FFu, // CoalBasin (темно-серый)
                4 => 0xC62828_FFu, // BIF Iron (ржаво-красный)
                5 => 0xFFD700_FFu, // CopperGoldArc (золотой)
                6 => 0xFF3D00_FFu, // GeothermalRift (огненно-оранжевый)
                _ => 0xFFFFFF_FFu
            };
        }

        if (terrain == BaseTerrainKind.Water)
            return 0x12294A_FFu;

        if (terrain == BaseTerrainKind.Mountain)
            return 0x8D847B_FFu;

        return biome switch
        {
            0 => 0xDDD19D_FFu, // Desert
            1 => 0xBFB377_FFu, // Steppe
            2 => 0x12294A_FFu, // DeepWater
            3 => 0x8D847B_FFu, // Mountain
            4 => 0x6E9B34_FFu, // Plains
            5 => 0x2E6B26_FFu, // Forest
            6 => 0x3E5848_FFu, // Swamp
            7 => 0xE2EEF3_FFu, // Glacier
            _ => 0x557A30_FFu
        };
    }
}
