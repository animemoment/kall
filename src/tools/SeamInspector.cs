using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using Game.Core.World;
using Game.Core.WorldStreaming;
using Game.Core.WorldStreaming.Integration;
using Game.UI;
using Game.UI.Streaming;
using Game.UI.Tools;

namespace Game.Tools;

/// <summary>
/// СЕЙМ-ИНСПЕКТОР: диагностика стыка «остров колонии ↔ стриминговый мир».
///
/// Зачем: на кромке 1024 клетка принадлежит ровно одному рендереру
/// (MapRenderer внутри, StreamingWorldView снаружи), поэтому объект, пересекающий
/// границу, физически состоит из двух половин. Ошибки тут не точечные, а парные:
/// сосед слева нарисован одним слоем, сосед справа — другим.
///
/// Инструмент даёт два уровня:
///  1) ДАННЫЕ (работает headless): для каждой клетки полосы сравнивает мнение
///     «островного» генератора (WorldGenV2.GenerateArea) и «мирового»
///     (WorldCellQuery.CellAt + DecorPass): террейн, дерево/камень, вариант дерева.
///     Плюс сверяет три источника границы (WorldModel / MapRenderer / явный размер).
///  2) ЖИВОЙ ВИД (в запущенной игре): просит у обоих рендереров DescribeCellVisual —
///     что реально лежит в слоях, какая текстура, файл это или runtime-генерация.
///
/// Запуск headless:
///   godot --headless --path . --script res://tests/run_seam_inspector.gd
/// Живьём (agent_tools.eval_code):
///   var t = load("res://src/tools/SeamInspector.cs").new(); print(t.Visual(1023, 512))
/// </summary>
public partial class SeamInspector : RefCounted
{
    /// <summary>Сид по умолчанию, если мир ещё не сконфигурирован (как у CellInspector).</summary>
    public const ulong DefaultSeed = 0x1234567890ABCDEFul;

    /// <summary>Явный размер острова (Main.ColonySizeTiles / StreamingToolPlacement).</summary>
    /// <remarks>0 = взять MapRenderer.MapWidth/MapHeight.</remarks>
    public long IslandWidth { get; set; }

    public long IslandHeight { get; set; }

    /// <summary>0 = взять WorldModel.Seed, а если и он 0 — DefaultSeed.</summary>
    public ulong Seed { get; set; }

    /// <summary>0 = взять WorldModel.Version, а если и он 0 — UnifiedWorldGenerator.DefaultGeneratorVersion.</summary>
    public uint Version { get; set; }

    // Явные сеттеры для вызова из GDScript: C#-свойства не видны скриптам напрямую.
    public void SetSeed(ulong seed) => Seed = seed;

    public void SetVersion(uint version) => Version = version;

    public void SetIslandSize(long width, long height)
    {
        IslandWidth = width;
        IslandHeight = height;
    }

    /// <summary>
    /// Задать мировое начало координат (спавн-регион) для headless-прогона.
    /// ВНИМАНИЕ: меняет глобальное состояние UnifiedWorldGenerator — только для
    /// диагностического скрипта, не для живой игры (там origin уже настроен Main.cs).
    /// </summary>
    public void SetWorldOrigin(long originX, long originY)
        => UnifiedWorldGenerator.SetWorldOrigin(originX, originY);

    /// <summary>Текущий контекст замера: сид, версия, origin, границы.</summary>
    public string Info()
        => $"seed=0x{S:X16} version={V} origin=({UnifiedWorldGenerator.WorldOriginX},{UnifiedWorldGenerator.WorldOriginY}) "
         + $"island={W}x{H} boundary_sources=(WorldModel:{WorldModel.IslandTiles}, MapRenderer:{MapRenderer.MapWidth}x{MapRenderer.MapHeight})";

    private long W => IslandWidth > 0 ? IslandWidth : MapRenderer.MapWidth;
    private long H => IslandHeight > 0 ? IslandHeight : MapRenderer.MapHeight;
    private ulong S => Seed != 0 ? Seed : (WorldModel.Seed != 0 ? WorldModel.Seed : DefaultSeed);
    private uint V => Version != 0 ? Version : (WorldModel.Version != 0 ? WorldModel.Version : UnifiedWorldGenerator.DefaultGeneratorVersion);

    // ================== ДАННЫЕ (headless) ==================

    /// <summary>Карточка одной клетки в JSON: обе точки зрения + флаги расхождений.</summary>
    public string CellJson(long x, long y)
    {
        CellProbe p = ProbeCell(x, y);
        var sb = new StringBuilder(384);
        sb.Append("{\"cell\":[").Append(x).Append(',').Append(y).Append(']');
        sb.Append(",\"island\":{\"worldmodel\":").Append(p.InsideWorldModel ? "true" : "false")
          .Append(",\"placement\":").Append(p.InsidePlacement ? "true" : "false")
          .Append(",\"explicit\":").Append(p.InsideExplicit ? "true" : "false")
          .Append(",\"seam\":").Append(p.Seam ? "true" : "false").Append('}');

        sb.Append(",\"terrain\":{\"island\":\"").Append(p.IslandCell.Terrain)
          .Append("\",\"world\":\"").Append(p.WorldCell.Terrain)
          .Append("\",\"agree\":").Append(p.TerrainAgree ? "true" : "false").Append('}');

        sb.Append(",\"decor\":{\"island\":{\"tree\":").Append(p.IslandTree ? "true" : "false")
          .Append(",\"stone\":").Append(p.IslandStone ? "true" : "false").Append('}')
          .Append(",\"world\":{\"tree\":").Append(p.WorldTree ? "true" : "false")
          .Append(",\"stone\":").Append(p.WorldStone ? "true" : "false").Append('}')
          .Append(",\"agree\":").Append(p.DecorAgree ? "true" : "false").Append('}');

        sb.Append(",\"tree_variant\":{\"island\":").Append(p.IslandTreeVariant)
          .Append(",\"world\":").Append(p.WorldTreeVariant)
          .Append(",\"agree\":").Append(p.TreeVariantAgree ? "true" : "false").Append('}');

        sb.Append(",\"flags\":").Append(FlagsJson(p));
        if (p.Error != null)
            sb.Append(",\"probe_error\":\"").Append(Escape(p.Error)).Append('"');
        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>Горизонтальная полоса через границу: таблица клетка-в-клетку.</summary>
    public string ProbeLine(long x0, long x1, long y) => ProbeCore(x0, x1, y, true);

    /// <summary>Вертикальная полоса через границу.</summary>
    public string ProbeColumn(long y0, long y1, long x) => ProbeCore(y0, y1, x, false);

    /// <summary>JSON-массив карточек по линии (для машинной обработки/диффа).</summary>
    public string Json(long x0, long x1, long y)
    {
        long min = Math.Min(x0, x1);
        long max = Math.Max(x0, x1);
        var sb = new StringBuilder(1024);
        sb.Append('[');
        for (long x = min; x <= max; x++)
        {
            if (x > min)
                sb.Append(',');
            sb.Append(CellJson(x, y));
        }
        sb.Append(']');
        return sb.ToString();
    }

    private string ProbeCore(long from, long to, long fixedCoord, bool horizontal)
    {
        long min = Math.Min(from, to);
        long max = Math.Max(from, to);

        var sb = new StringBuilder(8192);
        AppendBoundaryHeader(sb);
        sb.Append('\n');
        sb.Append("x      terr I/W    decor I/W     var I/W  owner   flags\n");
        sb.Append("---------------------------------------------------------------\n");

        int terrainDiff = 0, decorDiff = 0, variantDiff = 0, srcDiff = 0, cells = 0;
        for (long i = min; i <= max; i++)
        {
            long x = horizontal ? i : fixedCoord;
            long y = horizontal ? fixedCoord : i;
            CellProbe p = ProbeCell(x, y);
            cells++;
            if (!p.TerrainAgree) terrainDiff++;
            if (!p.DecorAgree) decorDiff++;
            if (!p.TreeVariantAgree) variantDiff++;
            if (p.BoundarySourceDiff) srcDiff++;

            sb.Append((horizontal ? x : y).ToString().PadRight(7));
            sb.Append((p.IslandCell.Terrain + "/" + p.WorldCell.Terrain).PadRight(11));
            sb.Append((DecorText(p.IslandTree, p.IslandStone) + "/" + DecorText(p.WorldTree, p.WorldStone)).PadRight(14));
            string variantText = (p.IslandTree ? p.IslandTreeVariant.ToString() : "-")
                + "/" + (p.WorldTree ? p.WorldTreeVariant.ToString() : "-");
            sb.Append(variantText.PadRight(9));
            sb.Append((p.InsideWorldModel ? "island" : "world").PadRight(8));
            sb.Append(FlagsText(p));
            sb.Append('\n');
        }

        sb.Append("---------------------------------------------------------------\n");
        sb.Append($"cells={cells} terrain_diff={terrainDiff} decor_diff={decorDiff} tree_variant_diff={variantDiff} boundary_src_diff={srcDiff}\n");
        return sb.ToString();
    }

    private void AppendBoundaryHeader(StringBuilder sb)
    {
        bool wm = WorldModel.IslandTiles == W && WorldModel.IslandTiles == H;
        bool mr = MapRenderer.MapWidth == W && MapRenderer.MapHeight == H;
        sb.Append($"boundary sources: WorldModel={WorldModel.IslandTiles} MapRenderer={MapRenderer.MapWidth}x{MapRenderer.MapHeight} explicit={W}x{H}")
          .Append(wm && mr ? " agree\n" : " DISAGREE\n");
        sb.Append($"seed=0x{S:X16} version={V}\n");
    }

    // ================== ЖИВОЙ ВИД (в запущенной игре) ==================

    /// <summary>
    /// Полная карточка клетки: данные + что реально лежит в слоях обоих рендереров.
    /// Вне запущенной игры рендереры просто помечаются как отсутствующие.
    /// </summary>
    public string Visual(long x, long y)
    {
        var sb = new StringBuilder(2048);
        sb.Append(CellJson(x, y)).Append('\n');

        MapRenderer map = GodotObject.IsInstanceValid(MapRenderer.Instance) ? MapRenderer.Instance : null;
        StreamingWorldView world = FindWorldView();

        bool inIntRange = x >= int.MinValue && x <= int.MaxValue && y >= int.MinValue && y <= int.MaxValue;
        bool mapTiles = inIntRange && map != null && map.HasAnyTileAt((int)x, (int)y);
        bool worldTiles = world != null && world.HasAnyDrawnTileAt(x, y);
        bool worldBlocks = world != null && world.HasAnyBlockAt(x, y);
        bool inside = WorldModel.IsInsideIsland(x, y);

        sb.Append("live: mapTiles=").Append(mapTiles ? "y" : "n")
          .Append(" worldTiles=").Append(worldTiles ? "y" : "n")
          .Append(" worldBlocks=").Append(worldBlocks ? "y" : "n")
          .Append('\n');
        sb.Append("live-flags: ").Append(LiveFlagsText(inside, mapTiles, worldTiles, worldBlocks)).Append('\n');

        sb.Append(map != null && inIntRange
            ? map.DescribeCellVisual((int)x, (int)y)
            : "MapRenderer: <not in tree>\n");
        sb.Append(world != null
            ? world.DescribeCellVisual(x, y)
            : "StreamingWorldView: <not in tree>\n");
        return sb.ToString();
    }

    /// <summary>
    /// Полоса вокруг точки: печатает только клетки с живыми расхождениями/подозрениями.
    /// Радиус 2..4 — то, что реально нужно для «объект на стыке».
    /// </summary>
    public string VisualBand(long centerX, long centerY, int radius)
    {
        int r = Math.Clamp(radius, 1, 16);
        var sb = new StringBuilder(4096);
        sb.Append($"VisualBand center=({centerX},{centerY}) radius={r}\n");

        MapRenderer map = GodotObject.IsInstanceValid(MapRenderer.Instance) ? MapRenderer.Instance : null;
        StreamingWorldView world = FindWorldView();
        int flagged = 0;

        for (long dy = -r; dy <= r; dy++)
        {
            for (long dx = -r; dx <= r; dx++)
            {
                long x = centerX + dx;
                long y = centerY + dy;
                bool inIntRange = x >= int.MinValue && x <= int.MaxValue && y >= int.MinValue && y <= int.MaxValue;
                bool mapTiles = inIntRange && map != null && map.HasAnyTileAt((int)x, (int)y);
                bool worldTiles = world != null && world.HasAnyDrawnTileAt(x, y);
                bool worldBlocks = world != null && world.HasAnyBlockAt(x, y);
                bool inside = WorldModel.IsInsideIsland(x, y);

                string flags = LiveFlagsText(inside, mapTiles, worldTiles, worldBlocks);
                if (flags.Length == 0)
                    continue;

                flagged++;
                sb.Append($"cell=({x},{y}) inside={inside} map={mapTiles} world={worldTiles} blocks={worldBlocks} flags={flags}\n");
            }
        }

        sb.Append(flagged == 0 ? "no live mismatches in band\n" : $"flagged={flagged}\n");
        return sb.ToString();
    }

    /// <summary>
    /// Сетка size×size вокруг центра (по умолчанию 10×10): на каждую клетку —
    /// кто её рисует (I = остров, W = мир), какие слои непустые, какой файл/атлас
    /// и тинт. Ниже — сводка «какая текстура, сколько клеток, в каких слоях» и
    /// список живых расхождений. Это и есть ответ «как образована картинка».
    /// </summary>
    public string VisualGrid(long centerX, long centerY, int size = 10)
    {
        int n = Math.Clamp(size, 2, 24);
        int half = n / 2;
        long x0 = centerX - half;
        long y0 = centerY - half;

        MapRenderer map = GodotObject.IsInstanceValid(MapRenderer.Instance) ? MapRenderer.Instance : null;
        StreamingWorldView world = FindWorldView();

        var sb = new StringBuilder(16384);
        sb.Append($"=== SEAM GRID {n}x{n} around ({centerX},{centerY}) ===\n");
        sb.Append(Info()).Append('\n');
        if (map != null)
            sb.Append("terrainWindow=").Append(map.TerrainWindowOrigin).Append('\n');

        var textures = new Dictionary<string, (int Cells, HashSet<string> Layers)>(StringComparer.Ordinal);
        var flagged = new List<string>();
        int drawn = 0;

        for (int row = 0; row < n; row++)
        {
            long y = y0 + row;
            for (int col = 0; col < n; col++)
            {
                long x = x0 + col;
                bool inIntRange = x >= int.MinValue && x <= int.MaxValue && y >= int.MinValue && y <= int.MaxValue;

                bool islandDrawn = inIntRange && map != null && map.HasAnyTileAt((int)x, (int)y);
                bool worldDrawn = world != null && world.HasAnyDrawnTileAt(x, y);
                bool worldBlocks = world != null && world.HasAnyBlockAt(x, y);
                bool inside = WorldModel.IsInsideIsland(x, y);
                if (islandDrawn || worldDrawn)
                    drawn++;

                string islandLine = map != null && inIntRange
                    ? map.CompactCellVisual((int)x, (int)y)
                    : "<no map>";
                string worldLine = world != null
                    ? world.CompactCellVisual(x, y)
                    : "<no world>";

                sb.Append('(').Append(x).Append(',').Append(y)
                  .Append(") | I: ").Append(islandLine)
                  .Append(" | W: ").Append(worldLine)
                  .Append('\n');
                CollectTextures(islandLine, textures);
                CollectTextures(worldLine, textures);

                string flags = LiveFlagsText(inside, islandDrawn, worldDrawn, worldBlocks);
                if (flags.Length > 0)
                    flagged.Add($"cell=({x},{y}) inside={inside} flags={flags}");
            }
            sb.Append('\n'); // пустая строка между рядами сетки
        }

        sb.Append($"--- drawn={drawn}/{n * n} ---\n");
        sb.Append("--- textures ---\n");
        foreach (var pair in textures.OrderByDescending(p => p.Value.Cells))
        {
            sb.Append(pair.Key)
              .Append("  cells=").Append(pair.Value.Cells)
              .Append(" layers=").Append(string.Join(",", pair.Value.Layers.OrderBy(l => l, StringComparer.Ordinal)))
              .Append('\n');
        }

        sb.Append("--- flags ---\n");
        if (flagged.Count == 0)
            sb.Append("none\n");
        else
            foreach (string f in flagged)
                sb.Append(f).Append('\n');

        return sb.ToString();
    }

    private static void CollectTextures(string line, Dictionary<string, (int Cells, HashSet<string> Layers)> into)
    {
        foreach (string token in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = token.IndexOf('=');
            if (eq <= 0 || eq >= token.Length - 1)
                continue;

            string layer = token[..eq];
            string value = token[(eq + 1)..];
            if (!LooksLikeTexture(value))
                continue;

            if (!into.TryGetValue(value, out var entry))
                entry = (0, new HashSet<string>(StringComparer.Ordinal));
            entry.Layers.Add(layer);
            into[value] = (entry.Cells + 1, entry.Layers);
        }
    }

    private static bool LooksLikeTexture(string value)
    {
        if (value.StartsWith("rt:", StringComparison.Ordinal))
            return true;

        int hash = value.IndexOf('#');
        string file = hash > 0 ? value[..hash] : value;
        return file.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            || file.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
            || file.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)
            || file.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
            || file.EndsWith(".ktx", StringComparison.OrdinalIgnoreCase);
    }

    private static string LiveFlagsText(bool inside, bool mapTiles, bool worldTiles, bool worldBlocks)
    {
        var sb = new StringBuilder(64);
        if (inside && worldTiles)
            sb.Append("WORLD_DRAWS_ISLAND,");
        if (!inside && mapTiles)
            sb.Append("ISLAND_DRAWS_OUTSIDE,");
        if (worldBlocks && !worldTiles)
            sb.Append("BLOCK_NOT_PAINTED,");
        if (mapTiles && worldTiles)
            sb.Append("BOTH_DRAW,");
        return sb.Length == 0 ? string.Empty : sb.ToString(0, sb.Length - 1);
    }

    private static StreamingWorldView FindWorldView()
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
            return null;
        return tree.Root?.FindChild("StreamingWorldView", true, false) as StreamingWorldView;
    }

    // ================== ЯДРО ЗАМЕРА ==================

    private readonly record struct CellProbe(
        bool InsideWorldModel,
        bool InsidePlacement,
        bool InsideExplicit,
        bool BoundarySourceDiff,
        bool Seam,
        GeneratedCell IslandCell,
        GeneratedCell WorldCell,
        bool IslandTree,
        bool IslandStone,
        int IslandTreeVariant,
        bool WorldTree,
        bool WorldStone,
        int WorldTreeVariant,
        bool TerrainAgree,
        bool DecorAgree,
        bool TreeVariantAgree,
        string Error);

    private CellProbe ProbeCell(long x, long y)
    {
        long w = W;
        long h = H;

        bool insideWorldModel = WorldModel.IsInsideIsland(x, y);
        bool insidePlacement = WorldPlacementPass.IsOnIsland(x, y, w, h);
        bool insideExplicit = WorldPlacementPass.IsOnIsland(x, y, w, h);
        // IsOnIsland без явных размеров отвечает через WorldModel.IsInsideIsland —
        // единственный источник границы в проекте; сверяем его отдельно (R-008).
        bool insideDefault = WorldPlacementPass.IsOnIsland(x, y);
        bool boundarySrcDiff = insideWorldModel != insidePlacement || insidePlacement != insideDefault;

        bool seam = x == w - 1 || x == w || y == h - 1 || y == h;

        ulong seed = S;
        uint version = V;

        GeneratedCell worldCell = WorldCellQuery.CellAt(seed, version, x, y);
        ushort fertility = StreamingWorldView.SoilFertilityAtCell(seed, version, x, y);
        (bool worldTree, bool worldStone) = WorldGenV2.DecorPass(x, y, in worldCell, fertility);

        GeneratedCell islandCell = worldCell;
        bool islandTree = false, islandStone = false;
        int islandTreeVariant = -1;
        string error = null;

        try
        {
            // ВАЖНО: GenerateArea принимает АБСОЛЮТНЫЕ мировые координаты (WorldOrigin уже
            // внутри), а WorldCellQuery.CellAt — локальные и прибавляет WorldOrigin сам.
            // Раньше CellInspector звал GenerateArea с локальными x,y: сравнивались две
            // РАЗНЫЕ клетки мира, и «расхождения» были артефактом замера.
            long absoluteX = UnifiedWorldGenerator.WorldOriginX + x;
            long absoluteY = UnifiedWorldGenerator.WorldOriginY + y;

            WorldGenV2.Area area = WorldGenV2.GenerateArea(seed, version, absoluteX, absoluteY, 1, 1);
            islandCell = area.GetCell(0, 0);
            islandTree = area.GetTree(0, 0);
            islandStone = area.GetStone(0, 0);
            islandTreeVariant = islandTree ? area.GetTreeVariant(0, 0) : -1;
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name + ": " + ex.Message;
        }

        int worldTreeVariant = -1;
        if (worldTree)
        {
            // Та же формула, что StreamingWorldView.GetDecorIndex: вариант дерева
            // считается по МИРОВЫМ координатам (WorldOrigin + локальная клетка).
            int wx = (int)(UnifiedWorldGenerator.WorldOriginX + x);
            int wy = (int)(UnifiedWorldGenerator.WorldOriginY + y);
            worldTreeVariant = UnifiedWorldGenerator.TreeVariantFor(wx, wy, (uint)seed) & 3;
        }

        bool terrainAgree = islandCell.Terrain == worldCell.Terrain;
        bool decorAgree = islandTree == worldTree && islandStone == worldStone;
        bool variantAgree = !(islandTree && worldTree) || islandTreeVariant == worldTreeVariant;

        return new CellProbe(
            insideWorldModel, insidePlacement, insideExplicit, boundarySrcDiff, seam,
            islandCell, worldCell,
            islandTree, islandStone, islandTreeVariant,
            worldTree, worldStone, worldTreeVariant,
            terrainAgree, decorAgree, variantAgree, error);
    }

    private static string DecorText(bool tree, bool stone)
        => tree ? "T" : stone ? "S" : "-";

    private static string FlagsText(in CellProbe p)
    {
        var sb = new StringBuilder(48);
        if (p.BoundarySourceDiff) sb.Append("BOUNDARY_SRC_DIFF ");
        if (!p.TerrainAgree) sb.Append("TERRAIN_DIFF ");
        if (!p.DecorAgree) sb.Append("DECOR_DIFF ");
        if (!p.TreeVariantAgree) sb.Append("TREE_VAR_DIFF ");
        if (p.Seam) sb.Append("seam ");
        if (p.Error != null) sb.Append("PROBE_ERROR ");
        return sb.Length == 0 ? "-" : sb.ToString().TrimEnd();
    }

    private static string FlagsJson(in CellProbe p)
    {
        var sb = new StringBuilder(96);
        sb.Append('[');
        AppendFlag(sb, p.BoundarySourceDiff, "BOUNDARY_SRC_DIFF");
        AppendFlag(sb, !p.TerrainAgree, "TERRAIN_DIFF");
        AppendFlag(sb, !p.DecorAgree, "DECOR_DIFF");
        AppendFlag(sb, !p.TreeVariantAgree, "TREE_VAR_DIFF");
        AppendFlag(sb, p.Seam, "SEAM");
        if (sb.Length > 1)
            sb.Length--; // хвостовая запятая
        sb.Append(']');
        return sb.ToString();
    }

    private static void AppendFlag(StringBuilder sb, bool condition, string name)
    {
        if (!condition)
            return;
        sb.Append('"').Append(name).Append("\",");
    }

    private static string Escape(string s)
        => string.IsNullOrEmpty(s)
            ? string.Empty
            : s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ");
}