using System;
using System.Reflection;
using Godot;
using Game.Core;
using Game.Core.World;
using Game.Core.WorldStreaming;
using Game.Core.WorldStreaming.Integration;
using Game.UI.Streaming;

namespace Game.Tests;

/// <summary>
/// Один рендер земли: землю во всём мире рисует шейдер MapRenderer/terrain_blend,
/// StreamingWorldView кладёт только ДЕКОР (деревья/камни, прозрачный фон).
///
/// Проверяем две вещи числом:
///  1. В атласе декора нет земли: фон прозрачный, спрайт нарисован.
///     (Иначе тайл-копия земли даёт квадраты 64×64 за кромкой.)
///  2. На слое декора стоит ровно то, что предписывает формула декора, и ничего кроме.
/// </summary>
public partial class TestStreamingWaterLayer : RefCounted
{
    private const ulong Seed = 0x5EED00000BADC0DEul;

    public bool Run(SceneTree tree)
    {
        GD.Print("==================================================");
        GD.Print("[TEST] Один рендер земли, декор отдельно");
        GD.Print("==================================================");
        try
        {
            return Execute(tree);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[DECOR] FAIL: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private bool Execute(SceneTree tree)
    {
        if (tree?.Root == null)
        {
            GD.PrintErr("[DECOR] FAIL: нет SceneTree.Root.");
            return false;
        }

        // Версия ЕДИНА на весь тест: спавн ищется той же версией, что генерирует
        // вид и дверь. Прежний FindHabitableSpawn(Seed, 4) при мире на v3 выбирал
        // «обитаемый» регион v4-мира — в v3 это сплошной океан (лучший скор 0).
        var view = new StreamingWorldView();
        uint ver = view.ActiveGeneratorVersion;
        RegionKey spawn = UnifiedWorldGenerator.FindHabitableSpawn(Seed, ver);
        UnifiedWorldGenerator.SetWorldOrigin(spawn.X * WorldRegions.RegionSize, spawn.Y * WorldRegions.RegionSize);
        tree.Root.AddChild(view);
        view.ResetWorld(Seed);
        WorldModel.Configure(Seed, ver);

        if (!CheckAtlas(view)) return false;
        return CheckPaintedDecor(tree, view);
    }

    /// <summary>Атлас: фон прозрачный (нет земли), спрайт нарисован.</summary>
    private static bool CheckAtlas(StreamingWorldView view)
    {
        TileSet tiles = FindField(view, "_tileSet")?.GetValue(view) as TileSet;
        var source = tiles?.GetSource(12) as TileSetAtlasSource; // SrcCompositeGround
        if (source?.Texture == null)
        {
            GD.PrintErr("[DECOR] FAIL: атлас декора не найден.");
            return false;
        }

        Image atlas = source.Texture.GetImage();
        const int TilePx = 64;
        int groundBaked = 0, emptySprites = 0;

        for (int index = 0; index < 8; index++) // 0..3 деревья, 4..7 камни
        {
            int transparent = 0, sprite = 0;
            for (int y = 0; y < TilePx; y++)
            {
                for (int x = 0; x < TilePx; x++)
                {
                    float a = atlas.GetPixel(index * TilePx + x, y).A;
                    if (a <= 0.01f) transparent++;
                    else sprite++;
                }
            }

            // Земля, вклеенная в тайл, делает его НЕПРОЗРАЧНЫМ целиком. Прозрачный
            // фон — единственный признак, что землю рисует шейдер, а не тайл.
            if (transparent < TilePx * TilePx / 100)
            {
                groundBaked++;
                GD.PrintErr($"[DECOR] FAIL: тайл {index} почти непрозрачен ({transparent} прозрачных пикселей) — в декор вклеена земля.");
            }
            if (sprite == 0)
            {
                emptySprites++;
                GD.PrintErr($"[DECOR] FAIL: тайл {index} пустой — спрайт не нарисован.");
            }
        }

        GD.Print($"[DECOR] атлас: тайлов с землёй {groundBaked}, пустых {emptySprites}");
        return groundBaked == 0 && emptySprites == 0;
    }

    private bool CheckPaintedDecor(SceneTree tree, StreamingWorldView view)
    {
        MethodInfo fertilityOf = view.GetType().GetMethod("CellSoilFertilityQ16",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (fertilityOf == null)
        {
            GD.PrintErr("[DECOR] FAIL: не найден CellSoilFertilityQ16.");
            return false;
        }

        // Автопоиск региона за кромкой с декором: фиксированные координаты были
        // слепыми (восточная полоса этого сида — океан; континент — архипелаг,
        // суша за кромкой живёт узкими полосами и островками). Две фазы:
        //  A) полосы, ПРИЛЕГАЮЩИЕ к кромкам острова (1024±) — там суша чаще всего;
        //  B) кольца регионов (512 тайлов) вокруг [0..1024)² — дальние острова.
        // Редкий скан двери (шаг 12, 256 проб на окно). Порог ≥1: точное равенство
        // слоя с формулой проверит ПОЛНЫЙ скан ниже — редкая проба только выбирает
        // окно; финальный expectedCount==0 всё равно честно фейлит тест.
        const long Side = 192;
        long bestX = 0, bestY = 0;
        int bestScore = 0;

        int ScoreWindow(long px, long py)
        {
            int score = 0;
            for (long y = py; y < py + Side; y += 12)
            {
                for (long x = px; x < px + Side; x += 12)
                {
                    GeneratedCell cell = WorldModel.CellAtLocal(x, y);
                    if (cell.Terrain != BaseTerrainKind.Grass) continue;
                    if ((BiomeType)cell.Biome == BiomeType.DeepWater) continue;
                    ushort fertility = cell.ForestQ16 >= WorldDecor.ForestMinQ16
                        ? (ushort)fertilityOf.Invoke(view, new object[] { x, y })
                        : (ushort)0;
                    var d = WorldGenV2.DecorPass(
                        UnifiedWorldGenerator.WorldOriginX + x,
                        UnifiedWorldGenerator.WorldOriginY + y, in cell, fertility);
                    if (d.HasTree || d.HasStone) score++;
                }
            }
            return score;
        }

        void Consider(long px, long py)
        {
            if (bestScore > 0) return;
            int score = ScoreWindow(px, py);
            if (score > bestScore)
            {
                bestScore = score;
                bestX = px;
                bestY = py;
            }
        }

        // Фаза A: полосы вплотную к четырём кромкам острова (шаг 192 по длине).
        for (long t = -768; t <= 1792 && bestScore == 0; t += Side)
        {
            Consider(1024 + 16, t);          // восточная кромка
            Consider(-Side - 16, t);         // западная кромка
            Consider(t, 1024 + 16);          // южная кромка
            Consider(t, -Side - 16);         // северная кромка
        }

        // Фаза B: кольца регионов вокруг острова.
        long originRx = UnifiedWorldGenerator.WorldOriginX / WorldRegions.RegionSize;
        long originRy = UnifiedWorldGenerator.WorldOriginY / WorldRegions.RegionSize;
        for (int ring = 1; ring <= 6 && bestScore == 0; ring++)
        {
            for (long ry = originRy - ring; ry <= originRy + ring + 1; ry++)
            {
                for (long rx = originRx - ring; rx <= originRx + ring + 1; rx++)
                {
                    // Остров занимает локальные регионы (0..1)² — их пропускаем.
                    if (rx >= originRx && rx <= originRx + 1 && ry >= originRy && ry <= originRy + 1)
                        continue;
                    Consider((rx - originRx) * WorldRegions.RegionSize + 160,
                        (ry - originRy) * WorldRegions.RegionSize + 160);
                }
            }
        }

        if (bestScore == 0)
        {
            GD.PrintErr("[DECOR] FAIL: за кромкой острова не нашлось НИ ОДНОЙ клетки с декором " +
                "(полосы кромок + кольца регионов до 6) — континент этого сида меньше окна острова.");
            return false;
        }
        long minX = bestX, minY = bestY;
        GD.Print($"[DECOR] регион автопоиска: ({minX},{minY})+192², редкий скор {bestScore}");

        var rect = new WorldRect(minX, minY, minX + Side, minY + Side);
        view.Update(rect);
        for (int frame = 0; frame < 900; frame++)
        {
            view._Process(0.016);
            OS.DelayMsec(6);
            if (frame % 300 == 299) view.Update(rect);
        }

        // Земляных слоёв быть не должно: иначе дубль вернулся.
        if (FindField(view, "_groundLayer") != null || FindField(view, "_waterLayer") != null)
        {
            GD.PrintErr("[DECOR] FAIL: у StreamingWorldView снова есть слой земли/воды.");
            return false;
        }

        TileMapLayer decor = FindField(view, "_decorLayer")?.GetValue(view) as TileMapLayer;
        if (decor == null)
        {
            GD.PrintErr("[DECOR] FAIL: слой декора не создан.");
            return false;
        }

        int expectedCount = 0, actualCount = 0, missing = 0, extra = 0;
        var expected = new System.Collections.Generic.HashSet<Vector2I>();

        // Диагностика пустой выборки: гистограмма террейна/биома региона —
        // «ожидалось 0» без объяснения проверяет только себя.
        int diagGrass = 0, diagWater = 0, diagMountain = 0, diagForestBiome = 0;
        int diagForestQ16 = 0, diagStoneQ16 = 0;

        for (long y = minY; y < minY + 192; y++)
        {
            for (long x = minX; x < minX + 192; x++)
            {
                if (!view.TryCellInfo(x, y, out GeneratedCell cell))
                    cell = WorldModel.CellAtLocal(x, y);
                if (cell.Terrain == BaseTerrainKind.Grass) diagGrass++;
                else if (cell.Terrain == BaseTerrainKind.Water) diagWater++;
                else diagMountain++;
                if ((BiomeType)cell.Biome == BiomeType.Forest) diagForestBiome++;
                if (cell.ForestQ16 >= WorldDecor.ForestMinQ16) diagForestQ16++;
                if (cell.StoneQ16 >= WorldDecor.StoneMinQ16) diagStoneQ16++;
                if (!HasDecor(view, fertilityOf, x, y, cell)) continue;

                expectedCount++;
                var pos = new Vector2I((int)x, (int)y);
                expected.Add(pos);
                if (decor.GetCellAtlasCoords(pos).X < 0) missing++;
            }
        }
        GD.Print($"[DECOR] регион: трава {diagGrass}, вода {diagWater}, горы {diagMountain}; " +
            $"биом Forest {diagForestBiome}; ForestQ16>порог {diagForestQ16}, StoneQ16>порог {diagStoneQ16}");

        foreach (Vector2I pos in decor.GetUsedCells())
        {
            if (pos.X < minX || pos.X >= minX + 192 || pos.Y < minY || pos.Y >= minY + 192) continue;
            actualCount++;
            if (!expected.Contains(pos)) extra++;
        }

        GD.Print($"[DECOR] клеток с декором: ожидалось {expectedCount}, на слое {actualCount}");
        GD.Print($"[DECOR] пропущено {missing}, лишних {extra}");

        if (expectedCount == 0)
        {
            GD.PrintErr("[DECOR] FAIL: пустая выборка — проверка ничего не проверила.");
            return false;
        }
        if (missing != 0 || extra != 0)
        {
            GD.PrintErr("[DECOR] FAIL: слой декора не совпал с формулой декора.");
            return false;
        }

        GD.Print("==================================================");
        GD.Print("[DECOR] ЗЕМЛЯ — ОДИН ШЕЙДЕР, ТАЙЛЫ — ТОЛЬКО ДЕКОР (Code 0)");
        GD.Print("==================================================");
        GC.KeepAlive(tree);
        return true;
    }

    private static bool HasDecor(StreamingWorldView view, MethodInfo fertilityOf, long x, long y, GeneratedCell cell)
    {
        if (cell.Terrain != BaseTerrainKind.Grass) return false;
        var biome = (BiomeType)cell.Biome;
        if (biome == BiomeType.DeepWater) return false;

        long wx = UnifiedWorldGenerator.WorldOriginX + x;
        long wy = UnifiedWorldGenerator.WorldOriginY + y;
        ushort fertility = cell.ForestQ16 >= WorldDecor.ForestMinQ16
            ? (ushort)fertilityOf.Invoke(view, new object[] { x, y })
            : (ushort)0;

        (bool hasTree, bool hasStone) = WorldGenV2.DecorPass(wx, wy, in cell, fertility);
        return hasTree || hasStone;
    }

    private static FieldInfo FindField(object target, string name)
        => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
}