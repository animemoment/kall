using Godot;
using System;
using System.Collections.Generic;

namespace Game.UI.Resources;

/// <summary>
/// Reads <c>ui/assets/textures/textures.json</c> once and hands out every texture the game uses.
///
/// Why a registry: texture references used to be const strings scattered across seven files
/// (MapRenderer, ToolVisualSpec, StreamingWorldView, CropRenderer, ItemRegistry, AgentRenderer,
/// HUDController). Changing a texture meant grepping the whole tree, and a typo became a silent
/// "texture not found" in the console. Now the JSON is the single source of truth and every
/// lookup goes through this class.
///
/// Prefer <c>uid://</c> over <c>res://</c> in the JSON: a UID survives a file rename, a path
/// does not. A <c>null</c> value means the file does not exist yet (crop phase art): the getter
/// returns null so the caller can fall back deliberately instead of spamming load errors.
///
/// Keys are addressed as <c>"group/key"</c>, e.g. <see cref="Grass"/> = "terrain/grass".
/// </summary>
public static class TextureRegistry
{
    public const string JsonPath = "res://ui/assets/textures/textures.json";

    // ---- Keys. One place where "group/key" strings live; no raw uid anywhere else. ----
    public const string Grass = "terrain/grass";
    public const string Sand = "terrain/sand";
    public const string Water = "terrain/water";
    public const string Mountains = "terrain/mountains";
    public const string GardenBed = "terrain/garden_bed";

    public const string Tree = "objects/tree";
    public const string Tree1 = "objects/tree_1";
    public const string Tree2 = "objects/tree_2";
    public const string Tree3 = "objects/tree_3";
    public const string Stone = "objects/stone";
    public const string CentralBlock = "objects/central_block";
    public const string WoodenWall = "objects/wooden_wall";

    public const string WorkTable = "furniture/work_table";
    public const string Bed = "furniture/bed";
    public const string Bench = "furniture/bench";
    public const string Nightstand = "furniture/nightstand";

    public const string WheatIcon = "crops/wheat_icon";

    public const string LogItem = "items/log";
    public const string GrainItem = "items/grain";
    public const string StoneItem = "items/stone";

    public const string ProcessOfWork = "effects/process_of_work";
    public const string Agents = "effects/agents";

    public const string LogisticZone = "ui/logistic_zone";
    public const string ExchangeIcon = "ui/exchange";
    public const string FarmingIcon = "ui/farming";
    public const string CheckMark = "ui/check_mark";

    private static Dictionary<string, string> _refs;
    private static readonly Dictionary<string, Texture2D> _cache = new();
    private static bool _loadAttempted;
    private static string _lastError = "";

    /// <summary>Last load error, empty when the JSON parsed fine.</summary>
    public static string LastError => _lastError;

    /// <summary>Number of entries in the registry (for tests and diagnostics).</summary>
    public static int Count
    {
        get
        {
            EnsureLoaded();
            return _refs?.Count ?? 0;
        }
    }

    /// <summary>Every key of the registry, sorted. Used by tests and the seam inspector.</summary>
    public static IReadOnlyCollection<string> Keys
    {
        get
        {
            EnsureLoaded();
            if (_refs == null)
                return Array.Empty<string>();
            return new List<string>(_refs.Keys);
        }
    }

    /// <summary>
    /// The raw reference string ("uid://…", "res://…") for a key, or null when the key is
    /// absent or deliberately empty. Useful for tests and for reporting.
    /// </summary>
    public static string ReferenceFor(string key)
    {
        EnsureLoaded();
        if (_refs == null || string.IsNullOrEmpty(key))
            return null;
        return _refs.TryGetValue(key, out string reference) ? reference : null;
    }

    /// <summary>
    /// The reference string itself for a key, empty when the entry is absent.
    /// For callers that must keep a UID string instead of a loaded texture (ItemDefinition).
    /// </summary>
    public static string UidOf(string key) => ReferenceFor(key) ?? "";

    /// <summary>Loads (and caches) the texture of a key. Null when the entry is absent or empty.</summary>
    public static Texture2D Get(string key)
    {
        EnsureLoaded();
        if (_refs == null || string.IsNullOrEmpty(key))
            return null;
        if (_cache.TryGetValue(key, out Texture2D cached))
            return cached;
        if (!_refs.TryGetValue(key, out string reference) || string.IsNullOrEmpty(reference))
            return null;

        var texture = GD.Load<Texture2D>(reference);
        if (texture == null)
        {
            GD.PushError($"[TextureRegistry] cannot load '{key}' from '{reference}'");
            return null;
        }
        _cache[key] = texture;
        return texture;
    }

    /// <summary>
    /// Texture of a key with one fallback key: lets a caller say "phase art, else the icon"
    /// without a null check on every frame.
    /// </summary>
    public static Texture2D GetOr(string key, string fallbackKey)
        => Get(key) ?? Get(fallbackKey);

    /// <summary>Drop the cache (tests reload after touching files).</summary>
    public static void Reset()
    {
        _refs = null;
        _cache.Clear();
        _loadAttempted = false;
        _lastError = "";
    }

    private static void EnsureLoaded()
    {
        if (_loadAttempted)
            return;
        _loadAttempted = true;
        _refs = new Dictionary<string, string>(64);

        if (!Godot.FileAccess.FileExists(JsonPath))
        {
            _lastError = $"texture json not found: {JsonPath}";
            GD.PushError($"[TextureRegistry] {_lastError}");
            return;
        }

        string text = Godot.FileAccess.GetFileAsString(JsonPath);
        if (string.IsNullOrEmpty(text))
        {
            _lastError = "texture json is empty";
            GD.PushError($"[TextureRegistry] {_lastError}");
            return;
        }

        var json = new Json();
        Error parse = json.Parse(text);
        if (parse != Error.Ok)
        {
            _lastError = $"texture json parse error: {json.GetErrorLine()}: {json.GetErrorMessage()}";
            GD.PushError($"[TextureRegistry] {_lastError}");
            return;
        }

        if (json.Data.VariantType != Variant.Type.Dictionary)
        {
            _lastError = "texture json root is not a dictionary";
            GD.PushError($"[TextureRegistry] {_lastError}");
            return;
        }

        var root = json.Data.AsGodotDictionary();
        if (!root.TryGetValue("groups", out Variant groupsVariant)
            || groupsVariant.VariantType != Variant.Type.Dictionary)
        {
            _lastError = "texture json has no groups dictionary";
            GD.PushError($"[TextureRegistry] {_lastError}");
            return;
        }

        foreach (KeyValuePair<Variant, Variant> group in groupsVariant.AsGodotDictionary())
        {
            string groupName = group.Key.AsString();
            if (group.Value.VariantType != Variant.Type.Dictionary)
                continue;
            foreach (KeyValuePair<Variant, Variant> entry in group.Value.AsGodotDictionary())
            {
                string entryName = entry.Key.AsString();
                // null is a deliberate "not drawn yet" marker, stored as an empty reference.
                string reference = entry.Value.VariantType == Variant.Type.Nil
                    ? ""
                    : entry.Value.AsString();
                string key = groupName + "/" + entryName;
                if (_refs.ContainsKey(key))
                {
                    _lastError = $"duplicate texture key: {key}";
                    GD.PushError($"[TextureRegistry] {_lastError}");
                    continue;
                }
                _refs[key] = reference;
            }
        }
    }
}
