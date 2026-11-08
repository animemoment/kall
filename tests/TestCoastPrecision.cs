using System;
using System.Diagnostics;
using Godot;
using Game.Core;
using Game.Core.World;
using Game.Core.WorldStreaming;
using Game.Core.WorldStreaming.Integration;

namespace Game.Tests;

/// <summary>
/// Диагноз «за кромкой нет песка и ровная линия берега»: сравнить ПРЯМОЙ сэмпл
/// (как остров в mapData) и РЕШЁТКУ климата 16×16 (как окно террейна и чанки)
/// по одной и той же вертикальной полосе через кромку острова.
/// </summary>
public partial class TestCoastPrecision : RefCounted
{
    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print("[TEST] Точность берега: прямой сэмпл против решётки 16×16");
        GD.Print("==================================================");
        try
        {
            return Execute();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[COAST] FAIL: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            return false;
        }
    }

    private bool Execute()
    {
        ulong seed = 0x5EED00000BADC0DEul;
        uint ver = UnifiedWorldGenerator.DefaultGeneratorVersion;
        RegionKey spawn = UnifiedWorldGenerator.FindHabitableSpawn(seed, ver);
        UnifiedWorldGenerator.SetWorldOrigin(spawn.X * WorldRegions.RegionSize, spawn.Y * WorldRegions.RegionSize);
        long ox = UnifiedWorldGenerator.WorldOriginX;
        long oy = UnifiedWorldGenerator.WorldOriginY;

        int seaLevel = WorldClimateGenerator.SeaLevelQ16;

        // Вертикальный разрез через кромку: локальные y 990..1080 (кромка = 1024).
        // x фиксируем в нескольких местах, чтобы поймать разную геометрию берега.
        long[] xs = { 480, 512, 544, 960 };
        GD.Print($"[COAST] SeaLevel={seaLevel}, разрез y=990..1080 (кромка острова = 1024)");
        GD.Print("[COAST] формат: y | ПРЯМОЙ terrain/biome/elev | РЕШЁТКА terrain/biome/elev");

        int directSand = 0, latticeSand = 0;
        foreach (long lx in xs)
        {
            long wx = ox + lx;
            for (long ly = 990; ly <= 1080; ly += 2)
            {
                long wy = oy + ly;
                GeneratedCell direct = UnifiedWorldGenerator.SampleCell(seed, ver, wx, wy);
                GeneratedCell lattice = LatticeCell(seed, ver, wx, wy);

                if (direct.Biome == (byte)BiomeType.Desert && direct.Terrain == BaseTerrainKind.Grass) directSand++;
                if (lattice.Biome == (byte)BiomeType.Desert && lattice.Terrain == BaseTerrainKind.Grass) latticeSand++;

                // Печатаем только окно вокруг смены типа у прямого сэмпла (берег).
                if (Math.Abs(direct.ElevationQ16 - seaLevel) < 3000)
                {
                    GD.Print($"[COAST] x={lx} y={ly} | D: {direct.Terrain}/{direct.Biome}/{direct.ElevationQ16} | L: {lattice.Terrain}/{lattice.Biome}/{lattice.ElevationQ16}");
                }
            }
        }

        GD.Print($"[COAST] песок в разрезе: прямой={directSand}, решётка={latticeSand} (клеток в разрезе ~164)");

        // Тайминг прямого сэмпла: сколько стоит 8192 клетки.
        var sw = Stopwatch.StartNew();
        long acc = 0;
        for (long i = 0; i < 8192; i++)
        {
            GeneratedCell c = UnifiedWorldGenerator.SampleCell(seed, ver, ox + (i & 1023), oy + 990 + (i >> 10));
            acc += c.ElevationQ16;
        }
        sw.Stop();
        double perCellUs = sw.Elapsed.TotalMilliseconds * 1000.0 / 8192;
        GD.Print($"[COAST] прямой сэмпл: {perCellUs:F1} мкс/клетка → окно 512² = {perCellUs * 262144 / 1000:F0} мс");
        GD.Print($"[COAST] (acc={acc % 7})");

        GD.Print("==================================================");
        GD.Print("[COAST] РАЗРЕЗ ГОТОВ (Code 0)");
        GD.Print("==================================================");
        return true;
    }

    /// <summary>Решётка 16×16 + Lerp — та же арифметика, что у окна террейна и чанков.</summary>
    private static GeneratedCell LatticeCell(ulong seed, uint ver, long wx, long wy)
    {
        long bx = wx & ~15L, by = wy & ~15L;
        uint u = (uint)((wx - bx) * 65535 / 16);
        uint v = (uint)((wy - by) * 65535 / 16);
        WorldClimateParams nw = WorldClimateGenerator.SampleBlendedCell(seed, ver, bx, by);
        WorldClimateParams ne = WorldClimateGenerator.SampleBlendedCell(seed, ver, bx + 16, by);
        WorldClimateParams sw = WorldClimateGenerator.SampleBlendedCell(seed, ver, bx, by + 16);
        WorldClimateParams se = WorldClimateGenerator.SampleBlendedCell(seed, ver, bx + 16, by + 16);
        WorldClimateParams top = Lerp(nw, ne, u);
        WorldClimateParams bot = Lerp(sw, se, v);
        WorldClimateParams climate = Lerp(top, bot, v);
        return UnifiedWorldGenerator.SampleCell(seed, ver, wx, wy, climate);
    }

    private static WorldClimateParams Lerp(WorldClimateParams a, WorldClimateParams b, uint t)
    {
        t = UnifiedWorldGenerator.SmoothStepQ16(t);
        return new WorldClimateParams(
            LerpU(a.ElevationQ16, b.ElevationQ16, t),
            LerpU(a.ContinentalnessQ16, b.ContinentalnessQ16, t),
            LerpU(a.LatitudeQ16, b.LatitudeQ16, t),
            LerpU(a.TemperatureQ16, b.TemperatureQ16, t),
            LerpU(a.MoistureQ16, b.MoistureQ16, t),
            LerpU(a.PrecipitationQ16, b.PrecipitationQ16, t),
            LerpU(a.DrainageQ16, b.DrainageQ16, t),
            LerpU(a.WindHeadingQ16, b.WindHeadingQ16, t),
            LerpU(a.RainShadowQ16, b.RainShadowQ16, t),
            LerpU(a.TectonicPressureQ16, b.TectonicPressureQ16, t),
            LerpU(a.OceanInfluenceQ16, b.OceanInfluenceQ16, t),
            LerpU(a.VegetationDensityQ16, b.VegetationDensityQ16, t),
            LerpU(a.SoilFertilityQ16, b.SoilFertilityQ16, t),
            LerpU(a.RockExposureQ16, b.RockExposureQ16, t),
            LerpU(a.GlaciationQ16, b.GlaciationQ16, t),
            LerpU(a.AridityQ16, b.AridityQ16, t),
            LerpU(a.ArchipelagoTendencyQ16, b.ArchipelagoTendencyQ16, t),
            LerpU(a.RiverDensityQ16, b.RiverDensityQ16, t));
    }

    private static ushort LerpU(ushort x, ushort y, uint t)
    {
        long value = (long)x * (65535 - t) + (long)y * t + 32768;
        return (ushort)Math.Clamp(value / 65535, 0, 65535);
    }
}