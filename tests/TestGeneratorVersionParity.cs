using Godot;
using Game.Core.WorldStreaming;
using Game.Core.WorldStreaming.Integration;

namespace Game.Tests;

/// <summary>
/// Сколько клеток РАЗЛИЧАЮТСЯ между версиями генератора.
///
/// Зачем: игра грузила мир версией 3 (Main.WorldGeneratorVersion), а симуляция
/// спрашивала клетку версией 4 (DefaultGeneratorVersion). Тест отвечает на вопрос
/// «насколько это было плохо» числом, а не рассуждением. Если число 0 — расхождение
/// версий было безвредным (и это тоже надо знать, чтобы не врать).
///
/// Запуск: godot --headless --path . --script res://tests/run_generator_version_parity.gd
/// </summary>
public partial class TestGeneratorVersionParity : RefCounted
{
    private const ulong Seed = 0xC0FFEE1234ABCDEFul;

    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print("[PARITY] Версии генератора: сравнение клеток");
        GD.Print("==================================================");

        uint versionA = 3;                                   // то, чем грузится игра
        uint versionB = UnifiedWorldGenerator.DefaultGeneratorVersion; // то, чем спрашивала симуляция

        if (versionA == versionB)
        {
            GD.Print($"[PARITY] Версии совпадают ({versionA}) — расхождения быть не может.");
            return true;
        }

        int step = 4;          // 4096 клеток на 1024² — статистически достаточно
        long limit = 1024;

        int total = 0;
        int terrainDiff = 0;
        int elevationDiff = 0;
        int moistureDiff = 0;
        int forestDiff = 0;
        int stoneDiff = 0;
        int maxElevDelta = 0;

        for (long y = 0; y < limit; y += step)
        {
            for (long x = 0; x < limit; x += step)
            {
                GeneratedCell a = WorldCellQuery.CellAt(Seed, versionA, x, y);
                GeneratedCell b = WorldCellQuery.CellAt(Seed, versionB, x, y);
                total++;

                if (a.Terrain != b.Terrain) terrainDiff++;
                if (a.ElevationQ16 != b.ElevationQ16)
                {
                    elevationDiff++;
                    int d = Mathf.Abs((int)a.ElevationQ16 - b.ElevationQ16);
                    if (d > maxElevDelta) maxElevDelta = d;
                }
                if (a.MoistureQ16 != b.MoistureQ16) moistureDiff++;
                if (a.ForestQ16 != b.ForestQ16) forestDiff++;
                if (a.StoneQ16 != b.StoneQ16) stoneDiff++;
            }
        }

        double Pct(int n) => total == 0 ? 0.0 : 100.0 * n / total;

        GD.Print($"[PARITY] v{versionA} против v{versionB}, клеток проверено: {total}");
        GD.Print($"[PARITY]   террейн (вода/гора/трава): {terrainDiff} ({Pct(terrainDiff):F2}%)");
        GD.Print($"[PARITY]   высота:                     {elevationDiff} ({Pct(elevationDiff):F2}%), макс. дельта {maxElevDelta}");
        GD.Print($"[PARITY]   влажность:                  {moistureDiff} ({Pct(moistureDiff):F2}%)");
        GD.Print($"[PARITY]   лес:                        {forestDiff} ({Pct(forestDiff):F2}%)");
        GD.Print($"[PARITY]   камень:                     {stoneDiff} ({Pct(stoneDiff):F2}%)");

        // Это НЕ провал: тест измеряет, а не требует. Провал только если сравнение сломано.
        if (total == 0)
        {
            GD.PrintErr("[PARITY] FAIL: не проверено ни одной клетки.");
            return false;
        }

        GD.Print("==================================================");
        GD.Print($"[PARITY] ИЗМЕРЕНО: террейн расходится в {Pct(terrainDiff):F2}% клеток. (Code 0)");
        GD.Print("==================================================");
        return true;
    }
}