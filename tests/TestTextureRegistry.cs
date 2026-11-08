using Godot;
using System;
using Game.UI.Resources;

namespace Game.Tests;

/// <summary>
/// §129: all texture references live in <c>ui/assets/textures/textures.json</c>, loaded through
/// <see cref="TextureRegistry"/>.
///
/// Why this test exists: references used to be const strings scattered across seven files. A
/// typo there only showed up as a console error in the running game, and moving a texture meant
/// grepping the whole tree. Two things must stay true:
///   1. no C# file carries a raw <c>uid://</c> or texture <c>.png</c> path any more;
///   2. every key in the JSON actually resolves (a dead UID is a missing texture in game).
///
/// Run: godot --headless --path . --script res://tests/run_texture_registry.gd
/// </summary>
public partial class TestTextureRegistry : RefCounted
{
    private const string Tag = "[TEXTURES]";
    private const string JsonPath = "res://ui/assets/textures/textures.json";

    /// <summary>Files that used to hold texture consts; none may hold a raw reference.</summary>
    private static readonly string[] MigratedFiles =
    {
        "res://src/ui/renderers/MapRenderer.cs",
        "res://src/ui/renderers/AgentRenderer.cs",
        "res://src/ui/renderers/CropRenderer.cs",
        "res://src/ui/tools/ToolVisualSpec.cs",
        "res://src/ui/streaming/StreamingWorldView.cs",
        "res://src/ui/controllers/HUDController.cs",
        "res://src/core/items/ItemRegistry.cs"
    };

    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print($"{Tag} единый реестр текстур: пути в JSON, код по ключам");
        GD.Print("==================================================");

        bool ok = true;
        ok &= RegistryLoads();
        ok &= EveryReferenceResolves();
        ok &= NoRawReferencesInCode();
        return ok;
    }

    private bool RegistryLoads()
    {
        GD.Print($"{Tag} --- реестр ---");
        bool ok = Expect(FileAccess.FileExists(JsonPath), "textures.json exists");
        if (!ok)
            return false;

        int count = TextureRegistry.Count;
        ok &= Expect(string.IsNullOrEmpty(TextureRegistry.LastError),
            $"registry parsed without errors (got \"{TextureRegistry.LastError}\")");
        ok &= Expect(count > 30, $"registry holds the whole texture set (got {count})");

        foreach (string key in new[]
                 {
                     TextureRegistry.Grass, TextureRegistry.Water, TextureRegistry.Stone,
                     TextureRegistry.WoodenWall, TextureRegistry.Bed, TextureRegistry.WheatIcon,
                     TextureRegistry.ProcessOfWork, TextureRegistry.Agents
                 })
        {
            ok &= Expect(TextureRegistry.ReferenceFor(key) is string reference
                         && reference.Length > 0,
                $"key '{key}' is present and non-empty");
        }
        return ok;
    }

    private bool EveryReferenceResolves()
    {
        GD.Print($"{Tag} --- каждая ссылка грузится ---");
        int broken = 0;
        int empty = 0;
        int loaded = 0;

        foreach (string key in TextureRegistry.Keys)
        {
            string reference = TextureRegistry.ReferenceFor(key);
            if (string.IsNullOrEmpty(reference))
            {
                empty++;
                continue;
            }
            if (TextureRegistry.Get(key) == null)
            {
                GD.PrintErr($"{Tag} FAIL: '{key}' -> '{reference}' does not load");
                broken++;
            }
            else
            {
                loaded++;
            }
        }

        GD.Print($"{Tag}     загружено {loaded}, пустых (арт не нарисован) {empty}, битых {broken}");
        // Empty entries are allowed on purpose: the crop phase art does not exist yet.
        return Expect(broken == 0, $"no dead texture references (broken: {broken})");
    }

    private bool NoRawReferencesInCode()
    {
        GD.Print($"{Tag} --- в коде не осталось сырых ссылок ---");
        int violations = 0;

        foreach (string path in MigratedFiles)
        {
            string source = FileAccess.GetFileAsString(path) ?? string.Empty;
            if (source.Length == 0)
            {
                GD.PrintErr($"{Tag} FAIL: cannot read {path}");
                violations++;
                continue;
            }

            foreach (string line in source.Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("//", StringComparison.Ordinal))
                    continue;
                if (trimmed.Contains("uid://", StringComparison.Ordinal)
                    || trimmed.Contains("res://ui/assets/textures/", StringComparison.Ordinal))
                {
                    GD.PrintErr($"{Tag} FAIL: raw texture reference in {path}: {trimmed}");
                    violations++;
                }
            }
        }

        return Expect(violations == 0,
            $"every texture goes through TextureRegistry (violations: {violations})");
    }

    private bool Expect(bool condition, string message)
    {
        if (condition)
        {
            GD.Print($"{Tag} OK: {message}");
            return true;
        }
        GD.PrintErr($"{Tag} FAIL: {message}");
        return false;
    }
}
