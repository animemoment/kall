using Godot;
using System;
using System.Collections.Generic;

namespace Game.UI.Resources;

/// <summary>
/// One entry of the raw-resource atlas (see _plan/RESOURCE_ATLAS_LAYOUT.md).
/// Ids and indexes are frozen: a new resource is appended, old numbers never move.
/// </summary>
public readonly struct ResourceAtlasEntry
{
    public readonly int Index;
    public readonly string Id;
    public readonly string NameRu;
    public readonly string Category;
    /// <summary>Absolute cell rectangle inside the atlas texture.</summary>
    public readonly Rect2 Cell;
    /// <summary>Alpha bounding box inside the cell (padding is kept by the UI).</summary>
    public readonly Rect2 Trim;

    public ResourceAtlasEntry(int index, string id, string nameRu, string category, Rect2 cell, Rect2 trim)
    {
        Index = index;
        Id = id;
        NameRu = nameRu;
        Category = category;
        Cell = cell;
        Trim = trim;
    }
}

/// <summary>
/// Reads <c>resources_raw_atlas.json</c> once and hands out atlas entries and their textures.
///
/// The JSON is the single source of truth for the commodity list: 90 ids, Russian names,
/// categories and cell/trim rectangles. The ids must not be duplicated in C# — otherwise the
/// list silently drifts from the atlas (see _plan/RESOURCE_ATLAS_LAYOUT.md §6).
///
/// Texture lookup: the atlas PNG is loaded through Godot's resource loader, and every entry
/// becomes an <see cref="AtlasTexture"/> cropped to its cell + trim rectangle, which keeps the
/// 2 px padding without resampling the source.
///
/// Used by two windows at once: the exchange commodity list (§128) and the HUD resource panel.
/// </summary>
public static class ResourceAtlasCatalog
{
    public const string JsonPath = "res://ui/assets/textures/resources_raw/resources_raw_atlas.json";

    private static List<ResourceAtlasEntry> _entries;
    private static Texture2D _atlas;
    private static bool _loadAttempted;
    private static string _lastError = "";

    /// <summary>Last load error, empty when the atlas parsed fine.</summary>
    public static string LastError => _lastError;

    /// <summary>Entries in atlas order (index 0..89). Empty when the JSON is missing.</summary>
    public static IReadOnlyList<ResourceAtlasEntry> Entries
    {
        get
        {
            EnsureLoaded();
            return _entries;
        }
    }

    /// <summary>Atlas texture, or null when it is not imported yet.</summary>
    public static Texture2D Atlas
    {
        get
        {
            EnsureLoaded();
            return _atlas;
        }
    }

    public static int Count => Entries.Count;

    /// <summary>Atlas texture cropped to one entry, or null when the atlas is unavailable.</summary>
    public static AtlasTexture TextureFor(ResourceAtlasEntry entry)
    {
        if (_atlas == null)
            return null;
        var texture = new AtlasTexture
        {
            Atlas = _atlas,
            Region = RegionFor(entry)
        };
        return texture;
    }

    /// <summary>
    /// Absolute rectangle of one entry inside the atlas.
    ///
    /// The JSON stores <c>trim</c> RELATIVE TO THE CELL (it is the alpha bounding box inside
    /// that 64×64 cell), while <see cref="AtlasTexture.Region"/> is absolute in the atlas. Using
    /// the raw trim showed the top-left corner of the atlas for 89 of 90 entries — every row
    /// looked like the same picture shifted by its own trim.
    /// </summary>
    public static Rect2 RegionFor(ResourceAtlasEntry entry)
    {
        Rect2 cell = entry.Cell;
        Rect2 trim = entry.Trim;
        if (trim.Size.X <= 0f || trim.Size.Y <= 0f)
            return cell;
        return new Rect2(cell.Position + trim.Position, trim.Size);
    }

    public static AtlasTexture TextureForId(string id)
    {
        foreach (ResourceAtlasEntry entry in Entries)
        {
            if (string.Equals(entry.Id, id, StringComparison.Ordinal))
                return TextureFor(entry);
        }
        return null;
    }

    /// <summary>Drop the cache (tests load the catalog after touching files).</summary>
    public static void Reset()
    {
        _entries = null;
        _atlas = null;
        _loadAttempted = false;
        _lastError = "";
    }

    private static void EnsureLoaded()
    {
        if (_loadAttempted)
            return;
        _loadAttempted = true;
        _entries = new List<ResourceAtlasEntry>(96);

        if (!Godot.FileAccess.FileExists(JsonPath))
        {
            _lastError = $"atlas json not found: {JsonPath}";
            GD.PushWarning($"[Exchange] {_lastError}");
            return;
        }

        string text = Godot.FileAccess.GetFileAsString(JsonPath);
        if (string.IsNullOrEmpty(text))
        {
            _lastError = "atlas json is empty";
            GD.PushWarning($"[Exchange] {_lastError}");
            return;
        }

        var json = new Json();
        Error parse = json.Parse(text);
        if (parse != Error.Ok)
        {
            _lastError = $"atlas json parse error: {json.GetErrorLine()}: {json.GetErrorMessage()}";
            GD.PushWarning($"[Exchange] {_lastError}");
            return;
        }

        if (json.Data.VariantType != Variant.Type.Dictionary)
        {
            _lastError = "atlas json root is not a dictionary";
            GD.PushWarning($"[Exchange] {_lastError}");
            return;
        }

        var root = json.Data.AsGodotDictionary();
        if (root.TryGetValue("atlas", out Variant atlasVariant)
            && atlasVariant.VariantType == Variant.Type.Dictionary)
        {
            var atlasDict = atlasVariant.AsGodotDictionary();
            if (atlasDict.TryGetValue("path", out Variant pathVariant))
                _atlas = GD.Load<Texture2D>(pathVariant.AsString());
        }

        if (!root.TryGetValue("entries", out Variant entriesVariant)
            || entriesVariant.VariantType != Variant.Type.Array)
        {
            _lastError = "atlas json has no entries array";
            GD.PushWarning($"[Exchange] {_lastError}");
            return;
        }

        foreach (Variant item in entriesVariant.AsGodotArray())
        {
            if (item.VariantType != Variant.Type.Dictionary)
                continue;
            var dict = item.AsGodotDictionary();
            ResourceAtlasEntry? entry = ReadEntry(dict);
            if (entry.HasValue)
                _entries.Add(entry.Value);
        }

        if (_atlas == null)
            GD.PushWarning("[Exchange] atlas texture not loaded — commodity rows will have no icons");
    }

    private static ResourceAtlasEntry? ReadEntry(Godot.Collections.Dictionary dict)
    {
        string id = dict.TryGetValue("id", out Variant idVariant) ? idVariant.AsString() : "";
        if (string.IsNullOrEmpty(id))
            return null;

        int index = dict.TryGetValue("index", out Variant indexVariant) ? indexVariant.AsInt32() : -1;
        string nameRu = dict.TryGetValue("name_ru", out Variant nameVariant) ? nameVariant.AsString() : id;
        string category = dict.TryGetValue("category", out Variant categoryVariant) ? categoryVariant.AsString() : "";

        Rect2 cell = ReadRect(dict, "cell");
        Rect2 trim = ReadRect(dict, "trim");
        return new ResourceAtlasEntry(index, id, nameRu, category, cell, trim);
    }

    private static Rect2 ReadRect(Godot.Collections.Dictionary dict, string key)
    {
        if (!dict.TryGetValue(key, out Variant variant) || variant.VariantType != Variant.Type.Dictionary)
            return new Rect2();
        var rectDict = variant.AsGodotDictionary();
        float x = rectDict.TryGetValue("x", out Variant xv) ? xv.AsSingle() : 0f;
        float y = rectDict.TryGetValue("y", out Variant yv) ? yv.AsSingle() : 0f;
        float w = rectDict.TryGetValue("w", out Variant wv) ? wv.AsSingle() : 0f;
        float h = rectDict.TryGetValue("h", out Variant hv) ? hv.AsSingle() : 0f;
        return new Rect2(x, y, w, h);
    }
}
