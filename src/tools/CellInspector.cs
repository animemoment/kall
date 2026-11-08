using System;
using System.Text;
using Godot;
using Game.Core.World;
using Game.Core.WorldStreaming;
using Game.Core.WorldStreaming.Integration;
using Game.Simulation;
using Game.UI.Streaming;
using Game.UI.Tools;

namespace Game.Tools;

/// <summary>
/// Инспектор клетки мира: спрашивает у КАЖДОЙ подсистемы её собственное мнение
/// о клетке (x,y) и показывает, где подсистемы расходятся.
///
/// Зачем: на линии IslandTiles существуют два независимых пути (остров в памяти и
/// стриминговый мир). Каждый баг на кромке — это расхождение двух ответов.
/// Инструмент делает расхождение видимым и считаемым, вместо «вроде норм глазами».
///
/// Запуск (headless, воспроизводимо):
///   godot --headless --path . --script res://tests/run_cell_inspector.gd
/// Живьём (через agent_tools.eval_code):
///   var t = load("res://src/tools/CellInspector.cs").new(); print(t.Inspect(1030, 512))
/// </summary>
public partial class CellInspector : RefCounted
{
    /// <summary>Фиксированный сид: замер должен быть воспроизводим у любого, кто запустит.</summary>
    public const ulong DefaultSeed = 0x1234567890ABCDEFul;

    // ================== публичный API ==================

    // Сначала настоящий мир (после Main.Configure), иначе — фиксированный сид:
// так замер воспроизводим в headless и при этом честен в живой игре.
    public string Inspect(long x, long y) =>
        Inspect(x, y, WorldModel.Seed != 0 ? WorldModel.Seed : DefaultSeed, WorldModel.Version);

    /// <summary>Полная карточка клетки: все мнения + список расхождений.</summary>
    public string Inspect(long x, long y, ulong seed, uint ver)
    {
        GeneratedCell cell = WorldCellQuery.CellAt(seed, ver, x, y);

        // 1. Покрытие островом (рендерер земли внутри окна IslandTiles) и стримингом.
        bool islandCovers = WorldPlacementPass.IsIslandCell(x, y);

        // 2. Декор: два независимых мнения об одной и той же клетке.
        bool genTree = false, genStone = false;
        try
        {
            var area = WorldGenV2.GenerateArea(seed, ver, x, y, 1, 1);
            genTree = area.GetTree(0, 0);
            genStone = area.GetStone(0, 0);
        }
        catch (Exception ex)
        {
            genTree = false;
            genStone = false;
            _lastError = ex.GetType().Name + ": " + ex.Message;
        }

        ushort fertility = StreamingWorldView.SoilFertilityAtCell(seed, ver, x, y);
        (bool streamTree, bool streamStone) = WorldGenV2.DecorPass(x, y, in cell, fertility);

        // 3. Индекс работ: сегодня — пирамида 512×512 с клампами (J-05, отдельный
        //    трек волны 2 мастер-плана). Честно показываем кламп: «clamped» = клетка
        //    за пределами покрытия пирамиды (ChunkDim чанков × 16 тайлов = 512).
        int chunkIndex = JobSpatialPyramid.GetChunkIndexStatic((int)x, (int)y);
        int chunkCx = chunkIndex % JobSpatialPyramid.ChunkDim;
        int chunkCy = chunkIndex / JobSpatialPyramid.ChunkDim;
        const int PyramidCoverageTiles = JobSpatialPyramid.ChunkDim << JobSpatialPyramid.ChunkShift;
        bool outsidePyramid = (ulong)x >= (ulong)PyramidCoverageTiles || (ulong)y >= (ulong)PyramidCoverageTiles;

        // 4. Размещение инструментов: гейт чертежей СНЯТ (констант
        //    WorldBlueprintsSupported больше нет — StreamingToolPlacement не блокирует,
        //    план за кромкой принимается). Исполнитель мировых планов (помпа
        //    Main.PumpWorldPlans, RENDER_SYNC_PLAN фаза 4) ещё не подключён: план
        //    за кромкой сегодня не становится работой — честно показываем это.
        bool worldWallsAccepted = true;
        bool worldExecutorConnected = false;
        bool toolCanPlace = islandCovers || worldWallsAccepted;

        var problems = new StringBuilder();
        if (genTree != streamTree)
            problems.Append($"decor.tree: worldgen={genTree} streaming={streamTree}; ");
        if (genStone != streamStone)
            problems.Append($"decor.stone: worldgen={genStone} streaming={streamStone}; ");
        if (!islandCovers && !worldExecutorConnected)
            problems.Append("tools: план за кромкой принимается, но исполнитель не подключён (Main.PumpWorldPlans, фаза 4); ");

        int count = 0;
        if (genTree != streamTree) count++;
        if (genStone != streamStone) count++;
        if (!toolCanPlace) count++;

        var sb = new StringBuilder(1 << 10);
        sb.Append("{\"cell\":[").Append(x).Append(',').Append(y).Append(']');
        sb.Append(",\"seed\":\"").Append(seed.ToString("X16")).Append('"');
        sb.Append(",\"version\":").Append(ver);
        sb.Append(",\"island_covers\":").Append(islandCovers ? "true" : "false");
        // Землю во всём мире (и внутри острова, и за кромкой) рисует ОДИН шейдер —
        // MapRenderer/terrain_blend. Стриминг кладёт только декор.
        sb.Append(",\"ground_drawn_by\":\"MapRenderer/terrain_blend\"");
        sb.Append(",\"decor_drawn_by\":\"").Append(islandCovers ? "MapRenderer" : "StreamingWorldView/decor").Append('"');

        sb.Append(",\"generator\":{")
          .Append("\"terrain\":\"").Append(cell.Terrain).Append('"')
          .Append(",\"elevationQ16\":").Append(cell.ElevationQ16)
          .Append(",\"moistureQ16\":").Append(cell.MoistureQ16)
          .Append(",\"forestQ16\":").Append(cell.ForestQ16)
          .Append(",\"stoneQ16\":").Append(cell.StoneQ16)
          .Append('}');

        sb.Append(",\"decor\":{")
          .Append("\"worldgen_island\":{\"tree\":").Append(genTree ? "true" : "false")
          .Append(",\"stone\":").Append(genStone ? "true" : "false").Append('}')
          .Append(",\"streaming_pass\":{\"tree\":").Append(streamTree ? "true" : "false")
          .Append(",\"stone\":").Append(streamStone ? "true" : "false").Append('}')
          .Append(",\"agrees\":").Append(genTree == streamTree && genStone == streamStone ? "true" : "false")
          .Append('}');

        sb.Append(",\"jobs_index\":{\"chunk\":[").Append(chunkCx).Append(',').Append(chunkCy)
          .Append("],\"world\":").Append(outsidePyramid ? "true" : "false")
          .Append(",\"clamped\":").Append(outsidePyramid ? "true" : "false") // J-05: клампы пирамиды на месте, отдельный трек
          .Append(",\"coverage_tiles\":").Append(PyramidCoverageTiles)
          .Append('}');

        sb.Append(",\"placement\":{\"island_cell\":").Append(islandCovers ? "true" : "false")
          .Append(",\"world_blueprints_accepted\":").Append(worldWallsAccepted ? "true" : "false")
          .Append(",\"world_executor\":\"").Append(worldExecutorConnected ? "connected" : "pending: Main.PumpWorldPlans (RENDER_SYNC_PLAN фаза 4)")
          .Append("\",\"tool_can_place\":").Append(toolCanPlace ? "true" : "false")
          .Append('}');

        sb.Append(",\"disagreements\":").Append(count);
        sb.Append(",\"problems\":\"").Append(Escape(problems.ToString().TrimEnd(' ', ';'))).Append('"');
        if (_lastError != null)
        {
            sb.Append(",\"probe_error\":\"").Append(Escape(_lastError)).Append('"');
            _lastError = null;
        }
        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>
    /// Прогон по линии координат: ищет, где ответы подсистем расходятся.
    /// Возвращает компактную таблицу — по строке на клетку с флагами.
    /// </summary>
    public string ProbeLine(long x0, long x1, long y) => ProbeLine(x0, x1, y, DefaultSeed, UnifiedWorldGenerator.DefaultGeneratorVersion);

    public string ProbeLine(long x0, long x1, long y, ulong seed, uint ver) => ProbeCore(x0, x1, y, seed, ver, false);

    /// <summary>То же по вертикали: прогон по Y при фиксированном X.</summary>
    public string ProbeColumn(long y0, long y1, long x) => ProbeCore(y0, y1, x, DefaultSeed, UnifiedWorldGenerator.DefaultGeneratorVersion, true);

    private string ProbeCore(long from, long to, long fixedCoord, ulong seed, uint ver, bool vertical)
    {
        var sb = new StringBuilder(4096);
        sb.Append(vertical ? "y      terr     deco  jobs   tools  flags\n" : "x      terr     deco  jobs   tools  flags\n");
        sb.Append("----------------------------------------------------------\n");

        int decorDiff = 0, jobClamp = 0, toolBlocked = 0, total = 0;
        for (long i = from; i <= to; i++)
        {
            long x = vertical ? fixedCoord : i;
            long y = vertical ? i : fixedCoord;
            GeneratedCell cell = WorldCellQuery.CellAt(seed, ver, x, y);
            bool islandCovers = WorldPlacementPass.IsIslandCell(x, y);

            bool genTree = false, genStone = false;
            try
            {
                var area = WorldGenV2.GenerateArea(seed, ver, x, y, 1, 1);
                genTree = area.GetTree(0, 0);
                genStone = area.GetStone(0, 0);
            }
            catch { /* оставляем false */ }

            ushort fertility = StreamingWorldView.SoilFertilityAtCell(seed, ver, x, y);
            (bool streamTree, bool streamStone) = WorldGenV2.DecorPass(x, y, in cell, fertility);

            int chunkIndex = JobSpatialPyramid.GetChunkIndexStatic((int)x, (int)y);
            int cx = chunkIndex % JobSpatialPyramid.ChunkDim;
            int cy = chunkIndex / JobSpatialPyramid.ChunkDim;
            bool clamped = (x >> JobSpatialPyramid.ChunkShift) > JobSpatialPyramid.ChunkDim - 1
                        || (y >> JobSpatialPyramid.ChunkShift) > JobSpatialPyramid.ChunkDim - 1
                        || x < 0 || y < 0;

            bool toolOk = true; // гейт снят: размещение за кромкой принимается (исполнитель — RENDER_SYNC_PLAN фаза 4)

            bool decorOk = genTree == streamTree && genStone == streamStone;
            if (!decorOk) decorDiff++;
            if (clamped) jobClamp++;
            if (!toolOk) toolBlocked++;
            total++;

            sb.Append(x.ToString().PadRight(7));
            sb.Append(cell.Terrain.ToString().PadRight(9));
            sb.Append((decorOk ? "ok" : "DIFF").PadRight(6));
            sb.Append((clamped ? $"clamp({cx},{cy})" : "ok").PadRight(7));
            sb.Append((toolOk ? "ok" : "BLOCKED").PadRight(7));
            if (!decorOk) sb.Append(" decor");
            if (clamped) sb.Append(" jobs");
            if (!toolOk) sb.Append(" tools");
            sb.Append('\n');
        }

        sb.Append("----------------------------------------------------------\n");
        sb.Append($"cells={total}  decor_disagree={decorDiff}  jobs_clamped={jobClamp}  tools_blocked={toolBlocked}\n");
        return sb.ToString();
    }

    // ================== внутреннее ==================

    private string _lastError;

    private static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ");
    }
}