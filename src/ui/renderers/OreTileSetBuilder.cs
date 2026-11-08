using Godot;
using Game.Core;
using Game.UI.Resources;
using System;
using System.Collections.Generic;

namespace Game.UI;

/// <summary>
/// Кодовый TileSet руды (task-3) — ленивая сборка композитных атласов из арта
/// <c>ui/assets/textures/world/ore47/&lt;ore_id&gt;/&lt;state&gt;/t&lt;mask&gt;.png</c>.
///
/// Устройство: один <see cref="TileSetAtlasSource"/> на пару (ore_type, state).
/// Для пары читаются 47 файлов масок (значения из <see cref="OreMaskHelper.ValidMasks"/>)
/// и складываются в композит 8×6 тайлов 64 px; ячейка = индекс маски в ValidMasks.
/// Источники создаются ТОЛЬКО для пар, реально встреченных на карте: месторождений
/// 0–5, поэтому грузятся сотни файлов, а не 23×3×47 = 3243.
///
/// Арт опционален: нет файла — клетка пропускается (рендер не падает), в лог
/// уходит ОДИН warning на билдер, чтобы не залить консоль на 47×N файлов.
/// Загрузка: FileAccess.FileExists → ResourceLoader.Load, затем fallback
/// Image.LoadPngFromBuffer для PNG, ещё не проимпортированного редактором.
///
/// Только главный поток (Godot TileSet/TileMapLayer).
/// </summary>
public sealed class OreTileSetBuilder
{
    /// <summary>Сторона тайла руды в пикселях (как у остальных слоёв карты).</summary>
    public const int TilePx = 64;

    /// <summary>Столбцов в композитном атласе одной пары (8×6 = 48 ≥ 47 масок).</summary>
    public const int AtlasColumns = 8;

    /// <summary>Строк в композитном атласе одной пары.</summary>
    public const int AtlasRows = 6;

    /// <summary>Первый source id: сценовые ids карты ≤ ~16, с запасом от коллизий.</summary>
    private const int FirstSourceId = 1200;

    /// <summary>Корень арта руды (делает параллельный агент-артист).</summary>
    public const string ArtRoot = "res://ui/assets/textures/world/ore47";

    /// <summary>Категория «руда» в resources_raw_atlas.json.</summary>
    public const string OreAtlasCategory = "руда";

    private readonly TileSet _tileSet = new() { TileSize = new Vector2I(TilePx, TilePx) };
    private readonly string[] _oreIds;               // index = oreType-1
    private readonly Dictionary<int, Combo> _combos = new();
    private readonly HashSet<int> _failed = new();
    private int _nextSourceId = FirstSourceId;
    private bool _missingArtWarned;
    private bool _missingAtlasWarned;

    /// <summary>Готовый (наполняемый лениво) TileSet слоя руды.</summary>
    public TileSet TileSet => _tileSet;

    /// <summary>Сколько файлов арта уже не нашлось (диагностика/лог).</summary>
    public int MissingArtCount { get; private set; }

    public OreTileSetBuilder()
    {
        _oreIds = LoadOreIds();
        if (_oreIds.Length == 0 && !_missingAtlasWarned)
        {
            _missingAtlasWarned = true;
            GD.PushWarning(
                $"[Ore] в {ResourceAtlasCatalog.JsonPath} нет записей категории «{OreAtlasCategory}» — " +
                "слой руды останется пустым (симуляция руды работает).");
        }
    }

    /// <summary>Ячейка композита для индекса маски 0..46.</summary>
    public static Vector2I TileCoords(int maskIndex)
        => new(maskIndex % AtlasColumns, maskIndex / AtlasColumns);

    /// <summary>
    /// Тайл (source id + ячейка атласа) для пары (oreType, state) и маски.
    /// false — арта нет, клетку пропускаем (warning уже отдан).
    /// </summary>
    public bool TryGetTile(int oreType, int state, int maskIndex,
                           out int sourceId, out Vector2I atlasCoords)
    {
        sourceId = -1;
        atlasCoords = Vector2I.Zero;
        if (maskIndex < 0 || maskIndex >= OreMaskHelper.ValidMaskCount)
            return false;
        if (oreType <= 0 || oreType > OreConfig.TypeCount || state < 0 || state > 2)
            return false;

        Combo combo = GetCombo(oreType, state);
        if (combo == null || !combo.MaskReady[maskIndex])
            return false;

        sourceId = combo.SourceId;
        atlasCoords = TileCoords(maskIndex);
        return true;
    }

    /// <summary>Есть ли для пары хоть один загруженный тайл (диагностика).</summary>
    public bool HasArt(int oreType, int state)
        => GetCombo(oreType, state) != null;

    private Combo GetCombo(int oreType, int state)
    {
        int key = oreType * 4 + state;
        if (_combos.TryGetValue(key, out Combo cached))
            return cached;
        if (_failed.Contains(key))
            return null;

        Combo built = BuildCombo(oreType, state);
        if (built == null)
        {
            _failed.Add(key);
            return null;
        }
        _combos[key] = built;
        return built;
    }

    /// <summary>Собрать композит одной пары (47 файлов масок) или null, если нет ни одного.</summary>
    private Combo BuildCombo(int oreType, int state)
    {
        string oreId = OreIdFor(oreType);
        if (string.IsNullOrEmpty(oreId))
            return null;

        string stateName = OreConfig.StateName((OreState)state);
        string dir = $"{ArtRoot}/{oreId}/{stateName}";

        var composite = Image.CreateEmpty(AtlasColumns * TilePx, AtlasRows * TilePx, false, Image.Format.Rgba8);
        var maskReady = new bool[OreMaskHelper.ValidMaskCount];
        int loaded = 0;
        string firstMissingPath = null;

        for (int i = 0; i < OreMaskHelper.ValidMaskCount; i++)
        {
            byte mask = OreMaskHelper.ValidMasks[i];
            string path = $"{dir}/t{mask}.png";
            Texture2D tex = LoadTileTexture(path);
            if (tex == null)
            {
                firstMissingPath ??= path;
                MissingArtCount++;
                continue;
            }
            Image img = tex.GetImage();
            if (img == null)
            {
                firstMissingPath ??= path;
                MissingArtCount++;
                continue;
            }
            // BlitRect требует одинаковый формат и ожидаемый размер: арт может быть
            // RGB8/сжатым или не 64×64 — приводим/пропускаем, чтобы не ловить
            // движковые ошибки на каждой клетке.
            if (img.GetWidth() != TilePx || img.GetHeight() != TilePx)
            {
                firstMissingPath ??= path;
                MissingArtCount++;
                continue;
            }
            if (img.GetFormat() != Image.Format.Rgba8)
            {
                img = img.Duplicate() as Image ?? img;
                img.Convert(Image.Format.Rgba8);
            }
            Vector2I cell = TileCoords(i);
            composite.BlitRect(img,
                new Rect2I(0, 0, TilePx, TilePx),
                new Vector2I(cell.X * TilePx, cell.Y * TilePx));
            maskReady[i] = true;
            loaded++;
        }

        if (loaded == 0)
        {
            WarnMissingArt(oreId, stateName, firstMissingPath);
            return null;
        }
        if (loaded < OreMaskHelper.ValidMaskCount)
            WarnMissingArt(oreId, stateName, firstMissingPath); // частичный арт — тот же однократный warning

        var source = new TileSetAtlasSource
        {
            Texture = ImageTexture.CreateFromImage(composite),
            TextureRegionSize = new Vector2I(TilePx, TilePx)
        };
        for (int i = 0; i < OreMaskHelper.ValidMaskCount; i++)
            if (maskReady[i])
                source.CreateTile(TileCoords(i));

        int sourceId = _nextSourceId++;
        _tileSet.AddSource(source, sourceId);
        return new Combo(sourceId, maskReady);
    }

    private void WarnMissingArt(string oreId, string stateName, string path)
    {
        if (_missingArtWarned)
            return;
        _missingArtWarned = true;
        GD.PushWarning(
            $"[Ore] арт руды не найден ({ArtRoot}/{oreId}/{stateName}/t<mask>.png): " +
            $"первый отсутствующий '{path ?? "?"}'. Клетки этой пары пропускаются; " +
            "остальная игра работает. Всего warning один — полный список смотрите в каталоге арта.");
    }

    /// <summary>oreType (1..23) → id категории «руда» в атласе (порядок index).</summary>
    private string OreIdFor(int oreType)
    {
        if (_oreIds.Length == 0 || oreType < 1 || oreType > _oreIds.Length)
            return null;
        return _oreIds[oreType - 1];
    }

    /// <summary>Список id руды из атласа ресурсов (единственный источник имён).</summary>
    private static string[] LoadOreIds()
    {
        var ids = new List<string>(OreConfig.TypeCount);
        try
        {
            IReadOnlyList<ResourceAtlasEntry> entries = ResourceAtlasCatalog.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                ResourceAtlasEntry entry = entries[i];
                if (string.Equals(entry.Category, OreAtlasCategory, StringComparison.Ordinal))
                    ids.Add(entry.Id);
            }
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[Ore] не удалось прочитать атлас ресурсов: {ex.Message}");
        }
        return ids.ToArray();
    }

    /// <summary>Файл PNG руды → Texture2D. null, если файла нет/не читается (не спамим ошибками).</summary>
    private static Texture2D LoadTileTexture(string resPath)
    {
        try
        {
            if (!Godot.FileAccess.FileExists(resPath))
                return null;
            Texture2D texture = ResourceLoader.Load<Texture2D>(resPath);
            if (texture != null)
                return texture;

            // Файл есть на диске, но ещё не проимпортирован редактором — читаем PNG сами.
            byte[] bytes = Godot.FileAccess.GetFileAsBytes(resPath);
            if (bytes == null || bytes.Length == 0)
                return null;
            var image = new Image();
            if (image.LoadPngFromBuffer(bytes) != Error.Ok)
                return null;
            return ImageTexture.CreateFromImage(image);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private sealed class Combo
    {
        public readonly int SourceId;
        public readonly bool[] MaskReady;

        public Combo(int sourceId, bool[] maskReady)
        {
            SourceId = sourceId;
            MaskReady = maskReady;
        }
    }
}
