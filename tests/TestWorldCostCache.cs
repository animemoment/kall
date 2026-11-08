using System;
using System.Diagnostics;
using Godot;
using Game.Core.World;
using Game.Core.WorldStreaming;

namespace Game.Tests;

/// <summary>
/// Проверка кэша стоимости прохода: он обязан совпадать с миром клетка в клетку,
/// работать на любых координатах (включая отрицательные и далёкие) и быть быстрым.
/// Запуск: godot --headless --path . --script res://tests/run_world_cost_cache.gd
/// </summary>
public partial class TestWorldCostCache : RefCounted
{
    private const ulong Seed = 0xC0FFEE1234ABCDEFul;
    private const uint Version = 3;

    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print("[COST] Кэш стоимости прохода");
        GD.Print("==================================================");

        WorldModel.Reset();
        WorldModel.Configure(Seed, Version);
        WorldCostCache.Clear();

        bool ok = true;
        ok &= CheckConsistency();
        ok &= CheckFarAndNegative();
        ok &= CheckFillRect();
        ok &= CheckSpeed();

        WorldCostCache.Clear();
        WorldModel.Reset();

        GD.Print("==================================================");
        GD.Print(ok ? "[COST] ALL COST CACHE TESTS PASSED! (Code 0)" : "[COST] FAIL");
        GD.Print("==================================================");
        return ok;
    }

    /// <summary>Кэш обязан совпадать с миром: и внутри 1024, и сразу за границей.</summary>
    private static bool CheckConsistency()
    {
        int mismatches = 0;
        int blocked = 0;
        int total = 0;
        int grass = 0, water = 0, mountain = 0;

        // Берём ОБИТАЕМУЮ область, а не (0,0): сид может дать океан у начала
        // координат, и тогда тест проверял бы воду водой (2420/2420 «блок» —
        // ровно так и случилось в первом прогоне; нулевая проверка хуже её отсутствия).
        RegionKey spawn = UnifiedWorldGenerator.FindHabitableSpawn(Seed, Version);
        long landX = spawn.X * 512;
        long landY = spawn.Y * 512;

        var origins = new System.Collections.Generic.List<long[]>
        {
            new long[] { landX, landY },
            new long[] { landX + 480, landY + 480 },
            new long[] { 480, 480 },
            new long[] { 992, 992 },
            new long[] { 1024, 1024 },
            new long[] { 2048, 2048 },
        };

        foreach (long[] origin in origins)
        {
            long ox = origin[0];
            long oy = origin[1];
            for (long y = oy; y < oy + 64; y += 3)
            {
                for (long x = ox; x < ox + 64; x += 3)
                {
                    byte cost = WorldCostCache.CostAt(x, y);
                    BaseTerrainKind terrain = WorldModel.TerrainAt(x, y);
                    if (terrain == BaseTerrainKind.Grass) grass++;
                    else if (terrain == BaseTerrainKind.Water) water++;
                    else mountain++;
                    bool expectedWalkable = terrain == BaseTerrainKind.Grass;
                    bool actualWalkable = cost == WorldCostCache.CostGrass;
                    total++;
                    if (!expectedWalkable) blocked++;
                    if (expectedWalkable != actualWalkable) mismatches++;
                }
            }
        }

        GD.Print($"[COST] согласованность: клеток {total}, непроходимых {blocked}, расхождений {mismatches}");
        GD.Print($"[COST] состав: трава {grass}, вода {water}, горы {mountain}");

        // Защита от нулевой проверки: если везде вода, тест ничего не проверил.
        if (grass == 0)
        {
            GD.PrintErr("[COST] FAIL: ни одной клетки травы — проверка вырождена.");
            return false;
        }
        if (mismatches != 0)
        {
            GD.PrintErr("[COST] FAIL: кэш расходится с миром.");
            return false;
        }
        return true;
    }

    /// <summary>Далёкие и отрицательные координаты не должны падать и должны давать осмысленный ответ.</summary>
    private static bool CheckFarAndNegative()
    {
        long[][] probes =
        {
            new long[] { -1, -1 },
            new long[] { -5000, 7000 },
            new long[] { 1_000_000, -1_000_000 },
            new long[] { 123456789, 987654321 },
        };

        foreach (long[] p in probes)
        {
            byte c = WorldCostCache.CostAt(p[0], p[1]);
            if (c != WorldCostCache.CostBlocked && c != WorldCostCache.CostGrass)
            {
                GD.PrintErr($"[COST] FAIL: недопустимая стоимость {c} в ({p[0]},{p[1]}).");
                return false;
            }
            GD.Print($"[COST] дальняя клетка ({p[0]},{p[1]}) = {(c == 0 ? "блок" : "проход")}");
        }
        return true;
    }

    /// <summary>FillRect обязан совпадать с поштучным чтением (по нему патфайндер строит окно).</summary>
    private static bool CheckFillRect()
    {
        const int w = 40, h = 24;
        long ox = 1000, oy = 1000;
        var buf = new byte[w * h];
        WorldCostCache.FillRect(ox, oy, w, h, buf);

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (buf[y * w + x] != WorldCostCache.CostAt(ox + x, oy + y))
                {
                    GD.PrintErr($"[COST] FAIL: FillRect расходится в ({x},{y}).");
                    return false;
                }
            }
        }
        GD.Print($"[COST] FillRect {w}×{h} от ({ox},{oy}) — совпадает");
        return true;
    }

    /// <summary>Скорость: миллион чтений по уже прогретым чанкам.</summary>
    private static bool CheckSpeed()
    {
        const int Iterations = 1_000_000;
        for (long y = 512; y < 576; y++)
            for (long x = 512; x < 576; x++)
                WorldCostCache.CostAt(x, y);

        var sw = Stopwatch.StartNew();
        int acc = 0;
        for (int i = 0; i < Iterations; i++)
        {
            int x = 512 + (i & 63);
            int y = 512 + ((i >> 6) & 63);
            acc += WorldCostCache.CostAt(x, y);
        }
        sw.Stop();

        double opsPerSec = Iterations / sw.Elapsed.TotalSeconds;
        GD.Print($"[COST] {Iterations:N0} чтений за {sw.ElapsedMilliseconds} мс — {opsPerSec / 1_000_000.0:F1} млн/с (acc={acc})");
        if (opsPerSec < 2_000_000)
        {
            GD.PrintErr("[COST] FAIL: кэш медленнее 2 млн чтений/с — он не кэш.");
            return false;
        }
        return true;
    }
}