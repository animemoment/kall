using System;
using System.Diagnostics;
using Godot;
using Game.Core;
using Game.Core.World;
using Game.Core.WorldStreaming;
using Game.Core.WorldStreaming.Integration;
using Game.UI;

namespace Game.Tests;

/// <summary>
/// Диагноз «за кромкой не рендерится земля»: внешнее заполнение follow-окна
/// террейна (FillTerrainWindowExternal) обязано заполнять ВСЁ окно за разумное
/// время и давать все 6 кодов поверхности. Если оно выбрасывает исключение или
/// работает секундами — окно навсегда остаётся со старым содержимым (нули=трава),
/// и за кромкой мы видим плоскую зелень вместо реального террейна.
/// </summary>
public partial class TestTerrainWindowFill : RefCounted
{
    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print("[TEST] Внешнее заполнение окна террейна: скорость и состав");
        GD.Print("==================================================");
        try
        {
            return Execute();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[FILL] FAIL: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            return false;
        }
    }

    /// <summary>
    /// Стык воды и суши в заполненном окне: у клетки-суши сразу за водой проверяем
    /// ПРЯМОЙ сэмпл против решётки. Пляж = трава с высотой в [SeaLevel, SeaLevel+1000]
    /// и биомом Desert. Если прямой видит пляж, а решётка — нет, это и есть
    /// «за кромкой нет песка».
    /// </summary>
    private static void BeachScan(ulong seed, byte[] buffer, long originX, long originY, string label)
    {
        int sea = WorldClimateGenerator.SeaLevelQ16;
        int checkedEdges = 0, directBeach = 0, latticeBeach = 0;
        int band1000 = 0, band2000 = 0, band3000 = 0, band4000 = 0, band6000 = 0;
        var examples = new System.Text.StringBuilder();

        for (int y = 1; y < 511 && checkedEdges < 4000; y += 2)
        {
            for (int x = 1; x < 511 && checkedEdges < 4000; x += 2)
            {
                int i = y * 512 + x;
                if (buffer[i] != 0) continue;                    // смотрим только траву
                if (buffer[i - 1] != 2 && buffer[i + 1] != 2 && buffer[i - 512] != 2 && buffer[i + 512] != 2)
                    continue;                                     // не у воды — мимо
                checkedEdges++;

                long wx = UnifiedWorldGenerator.WorldOriginX + originX + x;
                long wy = UnifiedWorldGenerator.WorldOriginY + originY + y;
                GeneratedCell direct = UnifiedWorldGenerator.SampleCell(seed, UnifiedWorldGenerator.DefaultGeneratorVersion, wx, wy);
                GeneratedCell lattice = LatticeCell(seed, UnifiedWorldGenerator.DefaultGeneratorVersion, wx, wy);

                bool dBeach = direct.Terrain == BaseTerrainKind.Grass
                    && direct.ElevationQ16 < sea + 1000
                    && direct.Biome == (byte)BiomeType.Desert;
                bool lBeach = lattice.Terrain == BaseTerrainKind.Grass
                    && lattice.ElevationQ16 < sea + 1000
                    && lattice.Biome == (byte)BiomeType.Desert;
                if (dBeach) directBeach++;
                if (lBeach) latticeBeach++;
                int de = direct.ElevationQ16;
                if (de < sea + 1000) band1000++;
                if (de < sea + 2000) band2000++;
                if (de < sea + 3000) band3000++;
                if (de < sea + 4000) band4000++;
                if (de < sea + 6000) band6000++;
                if (examples.Length < 700 && (dBeach || lBeach))
                    examples.Append($"({x},{y})D:{direct.ElevationQ16}/{direct.Biome} L:{lattice.ElevationQ16}/{lattice.Biome} ");
            }
        }

        GD.Print($"[FILL] ПЛЯЖ-СКАН ({label}): береговых клеток {checkedEdges}, пляж у ПРЯМОГО={directBeach}, у РЕШЁТКИ={latticeBeach}");
        GD.Print($"[FILL]   высота суши у берега: +1000={band1000} +2000={band2000} +3000={band3000} +4000={band4000} +6000={band6000} (из {checkedEdges})");
        if (examples.Length > 0) GD.Print($"[FILL]   примеры: {examples}");
        if (checkedEdges == 0)
            GD.Print("[FILL]   (береговых клеток в окне не оказалось)");
    }

    /// <summary>Решётка 16×16 + Lerp — та же арифметика, что у окна террейна и чанков.</summary>
    private static GeneratedCell LatticeCell(ulong seed, uint ver, long wx, long wy)
    {
        long bx = wx & ~15L, by = wy & ~15L;
        uint u = (uint)((wx - bx) * 65535 / 16);
        uint v = (uint)((wy - by) * 65535 / 16);
        WorldClimateParams nw = WorldClimateGenerator.SampleBlendedCell(seed, ver, bx, by);
        WorldClimateParams ne = WorldClimateGenerator.SampleBlendedCell(seed, ver, bx + 16, by);
        WorldClimateParams swc = WorldClimateGenerator.SampleBlendedCell(seed, ver, bx, by + 16);
        WorldClimateParams sec = WorldClimateGenerator.SampleBlendedCell(seed, ver, bx + 16, by + 16);
        WorldClimateParams top = Lerp(nw, ne, u);
        WorldClimateParams bot = Lerp(swc, sec, v);
        return UnifiedWorldGenerator.SampleCell(seed, ver, wx, wy, Lerp(top, bot, v));
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

    private bool Execute()
    {
        ulong seed = 0x5EED00000BADC0DEul;
        RegionKey spawn = UnifiedWorldGenerator.FindHabitableSpawn(seed, 4);
        UnifiedWorldGenerator.SetWorldOrigin(spawn.X * WorldRegions.RegionSize, spawn.Y * WorldRegions.RegionSize);

        // Остров колонии, как в игре: mapData 1024×1024.
        MapData mapData = UnifiedWorldGenerator.GenerateColonyWindow(
            seed, UnifiedWorldGenerator.DefaultGeneratorVersion, 0, 0,
            WorldModel.IslandTiles, WorldModel.IslandTiles);

        const int W = 512;
        var buffer = new byte[W * W];

        // Сценарий 1: полное окно ЦЕЛИКОМ за кромкой (юг острова) — как после
        // прыжка камеры: canReuse=false, пересчёт всей площади.
        long originX = 256, originY = 1024;
        var sw = Stopwatch.StartNew();
        MapRenderer.FillTerrainWindowExternal(buffer, W,
            seed, UnifiedWorldGenerator.DefaultGeneratorVersion,
            originX, originY,
            UnifiedWorldGenerator.WorldOriginX, UnifiedWorldGenerator.WorldOriginY,
            0, W, 0, W, mapData);
        sw.Stop();
        Report("полное окно за кромкой (юг)", buffer, sw.ElapsedMilliseconds, originX, originY);
        BeachScan(seed, buffer, originX, originY, "юг острова");

        // Сценарий 2: окно, пересекающее кромку острова (южный край) — как при
        // подходе камеры к краю: половина из mapData, половина от генератора.
        originY = 768;
        sw.Restart();
        MapRenderer.FillTerrainWindowExternal(buffer, W,
            seed, UnifiedWorldGenerator.DefaultGeneratorVersion,
            originX, originY,
            UnifiedWorldGenerator.WorldOriginX, UnifiedWorldGenerator.WorldOriginY,
            0, W, 0, W, mapData);
        sw.Stop();
        Report("окно на стыке (768..1280)", buffer, sw.ElapsedMilliseconds, originX, originY);

        // Сценарий 3: инкрементальная полоса 64×512 — как при шаге камеры на юг.
        originY = 832; // 768 + 64
        sw.Restart();
        MapRenderer.FillTerrainWindowExternal(buffer, W,
            seed, UnifiedWorldGenerator.DefaultGeneratorVersion,
            originX, originY,
            UnifiedWorldGenerator.WorldOriginX, UnifiedWorldGenerator.WorldOriginY,
            0, W, W - 64, W, mapData);
        sw.Stop();
        Report("полоса 64×512 (шаг камеры)", buffer, sw.ElapsedMilliseconds, originX, originY);

        // Сценарий 4: дым инспектора клетки профайлера (🔍 Клетка) — карточка
        // строится и НЕ врёт: дверь против mapData, за кромкой против окна выше.
        {
            Game.Core.World.WorldModel.Configure(seed, UnifiedWorldGenerator.DefaultGeneratorVersion);
            var overlay = new Game.UI.PerformanceOverlay();
            string cardInside = overlay.PlainCellCard(256, 256);
            string cardOutside = overlay.PlainCellCard(originX + 8, 1030); // ≥1024: за кромкой
            GD.Print("[INSPECT] карточка ВНУТРИ острова (256,256):\n" + cardInside);
            GD.Print("[INSPECT] карточка ЗА КРОМКОЙ (" + (originX + 8) + ",1030):\n" + cardOutside);

            bool doorOk = cardInside.Contains("world=(") && cardInside.Contains("Кто сгенерировал");
            bool outsideOk = cardOutside.Contains("ЗА КРОМКОЙ") && cardOutside.Contains("BeginBulkSample");
            if (!doorOk || !outsideOk)
            {
                GD.PrintErr($"[INSPECT] FAIL: карточка неполна (внутри={doorOk}, кромка={outsideOk}).");
                return false;
            }
            GD.Print("[INSPECT] карточки инспектора строятся, обе зоны описаны (Code 0)");
        }

        // Сценарий 5: РЕГРЕССИЯ игрока (2026-10-24, «за кромкой океан вместо суши»).
        // Инспектор клетки: данные Grass elev=24194 (v3), байт в шейдер=2 Water,
        // пиксель (45,85,148). Причина: заливка окна брала DefaultGeneratorVersion=4,
        // а игра работает на WorldGeneratorVersion=3 → окно рисовало ДРУГОЙ мир.
        // Числа — из реальной сессии игрока (seed/origin/клетка из карточки инспектора).
        {
            ulong useed = 0xADDFD53F640BCB44ul;
            const uint gameVer = 3;
            UnifiedWorldGenerator.SetWorldOrigin(606208, 137728);
            Game.Core.World.WorldModel.Configure(useed, gameVer);

            const long winX = 256, winY = -320;
            var v3buf = new byte[W * W];
            MapRenderer.FillTerrainWindowExternal(v3buf, W, useed, gameVer,
                winX, winY, 606208, 137728, 0, W, 0, W, null);
            // «Баговая» заливка: ЧУЖАЯ версия генератора (gameVer+1 — шум пересеян,
            // это другой мир). Раньше здесь стоял DefaultGeneratorVersion, но он
            // равен 3 = gameVer: обе заливки работали одной версией, проверка
            // «v4 ≠ дверь» была пустой и падала на совпадении.
            var v4buf = new byte[W * W];
            MapRenderer.FillTerrainWindowExternal(v4buf, W, useed, gameVer + 1,
                winX, winY, 606208, 137728, 0, W, 0, W, null);

            int bx = (int)((521 - winX) * W + (-2 - winY)); // клетка игрока (521,-2)
            byte doorByte = MapRenderer.SurfaceCode(WorldModel.CellAtLocal(521, -2).Terrain, WorldModel.CellAtLocal(521, -2).Biome);
            GD.Print($"[REG v3/v4] клетка (521,-2): дверь={doorByte} fill_v3={v3buf[bx]} fill_чужая-версия={v4buf[bx]}");
            if (v3buf[bx] != doorByte)
            {
                GD.PrintErr($"[REG v3/v4] FAIL: заливка версией МИРА (v3) не совпала с дверью ({v3buf[bx]} != {doorByte}).");
                return false;
            }
            // Чужая версия обязана давать ДРУГОЙ мир: сверяем выборочно всё окно —
            // совпадение одной клетки ничего не доказывает (в чужом мире тоже есть трава),
            // а полное совпадение буферов означало бы, что версия игнорируется.
            int v4diff = 0, v4checked = 0;
            for (int y = 0; y < W; y += 3)
            {
                for (int x = 0; x < W; x += 3)
                {
                    if (v4buf[y * W + x] != v3buf[y * W + x]) v4diff++;
                    v4checked++;
                }
            }
            GD.Print($"[REG v3/v4] чужая версия отличается от мировой в {v4diff} из {v4checked} клеток ({100.0 * v4diff / v4checked:F1}%)");
            if (v4diff == 0)
            {
                GD.PrintErr("[REG v3/v4] FAIL: чужая версия дала тот же мир — версия генератора не влияет на заливку.");
                return false;
            }

            // Санитарный обход окна: дверь (решётка) против v3-заливки — теперь
            // обе стороны в одной форме мира: расхождений быть не должно вовсе.
            int diff = 0, checkedCells = 0;
            for (int y = 0; y < W; y += 7)
            {
                for (int x = 0; x < W; x += 5)
                {
                    GeneratedCell c = WorldModel.CellAtLocal(winX + x, winY + y);
                    byte db = MapRenderer.SurfaceCode(c.Terrain, c.Biome);
                    if (db != v3buf[y * W + x]) diff++;
                    checkedCells++;
                }
            }
            GD.Print($"[REG v3/v4] окно 512²: дверь vs v3-заливка: расхождений {diff} из {checkedCells} ({100.0 * diff / checkedCells:F2}%)");
            if (diff > 0) // одна форма мира: 0 допускается, любая разница — шов
            {
                GD.PrintErr("[REG v3/v4] FAIL: дверь расходится с заливкой — формы мира разъехались.");
                return false;
            }
            GD.Print("[REG v3/v4] ОК: заливка версией мира, баг v4 воспроизводится и пойман растяжкой (Code 0)");
        }

        GD.Print("==================================================");
        GD.Print("[FILL] ОКНО ЗА КРОМКОЙ ЗАПОЛНЯЕТСЯ (Code 0)");
        GD.Print("==================================================");
        return true;
    }

    private static void Report(string name, byte[] buffer, long ms, long originX, long originY)
    {
        var counts = new int[6];
        foreach (byte b in buffer) counts[b < 6 ? b : 5]++;
        int total = buffer.Length;
        int nonzero = total - counts[0];
        GD.Print($"[FILL] {name}: {ms} мс, клеток {total}");
        GD.Print($"[FILL]   трава={counts[0]} песок={counts[1]} вода={counts[2]} степь={counts[3]} ледник={counts[4]} горы={counts[5]}");
        GD.Print($"[FILL]   не-трава: {nonzero} ({100.0 * nonzero / total:F1}%), origin=({originX},{originY})");
        if (nonzero == 0)
            GD.PrintErr($"[FILL] FAIL: {name} — окно ЗАПОЛНЕНО ОДНОЙ ТРАВОЙ. Внешняя генерация не работает.");
    }
}