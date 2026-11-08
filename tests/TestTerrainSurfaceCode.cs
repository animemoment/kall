using System;
using Godot;
using Game.Core;
using Game.Core.World;
using Game.Core.WorldStreaming;
using Game.UI;

namespace Game.Tests;

/// <summary>
/// Карта кодов поверхности (`MapRenderer.SurfaceCode`) — данные, по которым шейдер
/// рисует землю. Раньше кодов было три (трава/песок/вода), и степь, ледник и горы
/// сваливались в траву: шейдер знал меньше типов, чем тайлы стриминга, поэтому землю
/// нельзя было рисовать им одним — за кромкой острова клались тайлы, и это давало шов.
///
/// Тест следит за двумя вещами:
///  1) каждый тип поверхности отображается в СВОЙ код (никакого схлопывания в траву);
///  2) вода и горы не путаются с сушей — по этим кодам шейдер решает, где вода.
/// </summary>
public partial class TestTerrainSurfaceCode : RefCounted
{
    private const ulong Seed = 0x5EED00000BADC0DEul;
    private const uint Version = 4;

    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print("[TEST] Коды поверхности terrain_map (6 типов, без схлопывания)");
        GD.Print("==================================================");
        try
        {
            return Execute();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SURF] FAIL: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private bool Execute()
    {
        RegionKey spawn = UnifiedWorldGenerator.FindHabitableSpawn(Seed, Version);
        long cx = spawn.X * WorldRegions.RegionSize;
        long cy = spawn.Y * WorldRegions.RegionSize;
        UnifiedWorldGenerator.SetWorldOrigin(cx, cy);
        WorldModel.Configure(Seed, Version);

        var counts = new int[6];
        int wrongWater = 0, wrongMountain = 0, wrongBiome = 0;

        // Климатические зоны лежат по ШИРОТЕ: у спавна на ±32K клеток только умеренная
// полоса (трава/вода/горы), поэтому обход идёт по Y на ±524K редким шагом.
        long[] xs = { cx - 8192, cx - 4096, cx, cx + 4096, cx + 8192 };
        for (long y = cy - 524288; y < cy + 524288; y += 4096)
        {
            foreach (long x in xs)
            {
                GeneratedCell cell = WorldModel.CellAt(x, y);
                byte code = MapRenderer.SurfaceCode(cell.Terrain, cell.Biome);
                if (code > 5) { wrongBiome++; continue; }
                counts[code]++;

                bool isWater = cell.Terrain == BaseTerrainKind.Water;
                bool isMountain = cell.Terrain == BaseTerrainKind.Mountain;
                if (isWater != (code == 2)) { wrongWater++; continue; }
                if (isMountain != (code == 5)) { wrongMountain++; continue; }

                if (isWater || isMountain) continue;

                byte expected = (BiomeType)cell.Biome switch
                {
                    BiomeType.Desert => 1,
                    BiomeType.Steppe => 3,
                    BiomeType.Glacier => 4,
                    _ => 0,
                };
                if (code != expected) wrongBiome++;
            }
        }

        GD.Print($"[SURF] трава={counts[0]} песок={counts[1]} вода={counts[2]} степь={counts[3]} ледник={counts[4]} горы={counts[5]}");
        GD.Print($"[SURF] ошибок: вода={wrongWater} горы={wrongMountain} биом={wrongBiome}");

        if (wrongWater != 0 || wrongMountain != 0 || wrongBiome != 0)
        {
            GD.PrintErr("[SURF] FAIL: тип поверхности отображается в неверный код.");
            return false;
        }

        int distinct = 0;
        for (int i = 0; i < counts.Length; i++) if (counts[i] > 0) distinct++;
        if (distinct < 4)
        {
            GD.PrintErr($"[SURF] FAIL: в выборке всего {distinct} типов — проверка схлопывания пустая (vacuous).");
            return false;
        }
        if (counts[3] + counts[4] == 0)
        {
            GD.PrintErr("[SURF] FAIL: степь и ледник не встретились — регресс «всё в траву» не проверен.");
            return false;
        }

        GD.Print("==================================================");
        GD.Print("[SURF] КОДЫ ПОВЕРХНОСТИ РАЗЛИЧАЮТСЯ, СХЛОПЫВАНИЯ В ТРАВУ НЕТ (Code 0)");
        GD.Print("==================================================");
        return true;
    }
}