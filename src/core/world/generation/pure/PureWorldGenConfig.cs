using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Game.Core.WorldStreaming;

public sealed class PureWorldProfile
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "PlanetaryRealismWorld";

    [JsonPropertyName("sea_level")]
    public ushort SeaLevel { get; set; } = 32768;

    [JsonPropertyName("mountain_threshold")]
    public ushort MountainThreshold { get; set; } = 48000;

    [JsonPropertyName("tree_line_elevation")]
    public ushort TreeLineElevation { get; set; } = 43000;

    [JsonPropertyName("continental_scale")]
    public int ContinentalScale { get; set; } = 1024;

    [JsonPropertyName("sub_continental_scale")]
    public int SubContinentalScale { get; set; } = 256;

    [JsonPropertyName("rift_strait_width")]
    public int RiftStraitWidth { get; set; } = 24;

    [JsonPropertyName("rift_frequency")]
    public int RiftFrequency { get; set; } = 768;

    [JsonPropertyName("archipelago_chance")]
    public int ArchipelagoChance { get; set; } = 35;

    [JsonPropertyName("archipelago_scale")]
    public int ArchipelagoScale { get; set; } = 64;

    [JsonPropertyName("great_river_period")]
    public int GreatRiverPeriod { get; set; } = 1024;

    [JsonPropertyName("great_river_width")]
    public int GreatRiverWidth { get; set; } = 900;

    [JsonPropertyName("riparian_zone_multiplier")]
    public int RiparianZoneMultiplier { get; set; } = 4;

    [JsonPropertyName("great_lake_threshold")]
    public int GreatLakeThreshold { get; set; } = 3200;

    [JsonPropertyName("great_lake_scale")]
    public int GreatLakeScale { get; set; } = 512;

    [JsonPropertyName("domain_warp_meso_scale")]
    public int DomainWarpMesoScale { get; set; } = 64;

    [JsonPropertyName("domain_warp_micro_scale")]
    public int DomainWarpMicroScale { get; set; } = 16;

    [JsonPropertyName("forest_min_q16")]
    public ushort ForestMinQ16 { get; set; } = 32000;

    [JsonPropertyName("stone_min_q16")]
    public ushort StoneMinQ16 { get; set; } = 38000;
}

public sealed class PureWorldGenConfig
{
    public PureWorldProfile Profile { get; set; } = new();

    private static readonly Lazy<PureWorldGenConfig> _cachedDefault = new(LoadDefaultOrFallback);
    public static PureWorldGenConfig Default => _cachedDefault.Value;

    private static PureWorldGenConfig LoadDefaultOrFallback()
    {
        try
        {
            // 1. Попытка через Godot FileAccess (для рантайма и экспортированного .pck/.exe)
            string godotPath = "res://config/worldgen/world_profile.json";
            if (Godot.FileAccess.FileExists(godotPath))
            {
                using var fa = Godot.FileAccess.Open(godotPath, Godot.FileAccess.ModeFlags.Read);
                if (fa != null)
                {
                    string json = fa.GetAsText();
                    var prof = JsonSerializer.Deserialize<PureWorldProfile>(json);
                    if (prof != null)
                        return new PureWorldGenConfig { Profile = prof };
                }
            }
        }
        catch { }

        try
        {
            // 2. Попытка через System.IO
            string[] searchPaths = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "worldgen", "world_profile.json"),
                Path.Combine("config", "worldgen", "world_profile.json"),
                Path.Combine("_test_worldgen", "config", "world_profile.json")
            };

            foreach (var p in searchPaths)
            {
                if (File.Exists(p))
                {
                    string json = File.ReadAllText(p);
                    var prof = JsonSerializer.Deserialize<PureWorldProfile>(json);
                    if (prof != null)
                        return new PureWorldGenConfig { Profile = prof };
                }
            }
        }
        catch { }

        // 3. Гарантированный дефолт в коде
        return new PureWorldGenConfig { Profile = new PureWorldProfile() };
    }
}
