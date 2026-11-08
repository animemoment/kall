using System;
using Godot;
using Game.Core;
using Game.Core.World;
using Game.Core.WorldStreaming;

namespace Game.Tests;

/// <summary>
/// Совпадает ли КЭШ острова (MapData, из которого строятся cost-сетка и рендер)
/// с тем, что отвечает генератор через единую дверь WorldModel?
///
/// Это решающее измерение перед этапом «один рендерер»: если расхождений 0,
/// то два рендерера рисовали ОДИН мир разными способами (шов — вопрос техники),
/// и объединение безопасно. Если расхождения есть — значит кэш и генератор
/// разошлись, и именно это чинили бы ещё неделю по кускам.
///
/// Запуск: godot --headless --path . --script res://tests/run_island_world_agreement.gd
/// </summary>
public partial class TestIslandWorldAgreement : RefCounted
{
    private const ulong Seed = 0xC0FFEE1234ABCDEFul;
    private const uint Version = 3;

    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print("[AGREE] Кэш острова против генератора (WorldModel)");
        GD.Print("==================================================");

        WorldModel.Reset();
        WorldModel.Configure(Seed, Version);
        WorldCostCache.Clear();

        MapData map;
        try
        {
            map = UnifiedWorldGenerator.GenerateColonyWindow(Seed, Version, 0, 0, 1024, 1024);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[AGREE] FAIL: окно колонии не сгенерировалось: {ex.GetType().Name}: {ex.Message}");
            return false;
        }

        int total = 0, mismatch = 0;
        int mapWaterWorldGrass = 0, mapGrassWorldWater = 0;
        int mapMountainWorldNot = 0, mapNotWorldMountain = 0;
        int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;

        const int Step = 2;
        for (int y = 0; y < map.Height; y += Step)
        {
            for (int x = 0; x < map.Width; x += Step)
            {
                TileType fromCache = map.Ground[x, y];
                BaseTerrainKind fromWorld = WorldModel.TerrainAt(x, y);
                total++;

                if ((int)fromCache == (int)fromWorld) continue;
                mismatch++;

                if (mismatch <= 8)
                    GD.Print($"[AGREE]   расхождение #{mismatch}: ({x},{y}) кэш={fromCache}, мир={fromWorld}");
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;

                if (fromCache == TileType.Water && fromWorld == BaseTerrainKind.Grass) mapWaterWorldGrass++;
                else if (fromCache == TileType.Grass && fromWorld == BaseTerrainKind.Water) mapGrassWorldWater++;
                else if (fromCache == TileType.Mountain && fromWorld != BaseTerrainKind.Mountain) mapMountainWorldNot++;
                else if (fromCache != TileType.Mountain && fromWorld == BaseTerrainKind.Mountain) mapNotWorldMountain++;
            }
        }

        double pct = total == 0 ? 0.0 : 100.0 * mismatch / total;

        GD.Print($"[AGREE] проверено клеток: {total}, расхождений: {mismatch} ({pct:F2}%)");
        GD.Print($"[AGREE]   кэш=вода, мир=трава:   {mapWaterWorldGrass}");
        GD.Print($"[AGREE]   кэш=трава, мир=вода:   {mapGrassWorldWater}");
        GD.Print($"[AGREE]   кэш=гора, мир=не гора: {mapMountainWorldNot}");
        GD.Print($"[AGREE]   кэш=не гора, мир=гора: {mapNotWorldMountain}");
        if (mismatch > 0)
            GD.Print($"[AGREE]   область расхождений: x[{minX}..{maxX}] y[{minY}..{maxY}]");

        WorldCostCache.Clear();
        WorldModel.Reset();

        if (total == 0)
        {
            GD.PrintErr("[AGREE] FAIL: не проверено ни одной клетки.");
            return false;
        }

        // Это ОБЯЗАТЕЛЬНЫЙ инвариант, а не измерение: если кэш острова и генератор
        // разошлись хотя бы в одной клетке — где-то снова завёлся второй мир.
        if (mismatch != 0)
        {
            GD.PrintErr($"[AGREE] FAIL: кэш расходится с генератором в {mismatch} клетках ({pct:F2}%).");
            GD.PrintErr("[AGREE] Скорее всего правило мира живёт в GenerateColonyWindow, а не в WorldModel.");
            return false;
        }

        GD.Print("==================================================");
        GD.Print("[AGREE] КЭШ И ГЕНЕРАТОР СОВПАДАЮТ ПОЛНОСТЬЮ (Code 0)");
        GD.Print("==================================================");
        return true;
    }
}