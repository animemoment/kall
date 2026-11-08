using System;
using System.IO;
using Godot;

namespace Game.UI;

/// <summary>
/// Хелпер для загрузки, кэширования и конвертации текстур полов (дерево, камень, бетон)
/// и непрозрачной крыши жилых зданий.
/// Гарантирует правильный размер 64x64 px и наличие файлов в структуре проекта.
/// </summary>
public static class FloorRoofTextureHelper
{
    public const string WoodFloorResPath = "res://ui/assets/textures/floors/floor_wood.png";
    public const string StoneFloorResPath = "res://ui/assets/textures/floors/floor_stone.png";
    public const string ConcreteFloorResPath = "res://ui/assets/textures/floors/floor_concrete.png";
    public const string RoofResidentialResPath = "res://ui/assets/textures/roofs/roof_residential.png";
    public const string RoofIndustrialResPath = "res://ui/assets/textures/roofs/roof_industrial.png";

    // Исходные пути из генерации артефактов
    private static readonly string BrainArtifactsDir = @"C:\Users\PC\.gemini\antigravity\brain\9851c947-1e4f-4a33-8ad9-6cf38c745263";
    private static readonly string ArtifactWoodFloor = Path.Combine(BrainArtifactsDir, "floor_wood_1794063208385.jpg");
    private static readonly string ArtifactStoneFloor = Path.Combine(BrainArtifactsDir, "floor_stone_1794063222606.jpg");
    private static readonly string ArtifactConcreteFloor = Path.Combine(BrainArtifactsDir, "floor_concrete_1794063237929.jpg");
    private static readonly string ArtifactRoofResidential = Path.Combine(BrainArtifactsDir, "roof_residential_v2_1794063267445.jpg");

    public static Texture2D GetWoodFloorTexture() => GetOrLoadTexture(WoodFloorResPath, ArtifactWoodFloor, "floor_wood");
    public static Texture2D GetStoneFloorTexture() => GetOrLoadTexture(StoneFloorResPath, ArtifactStoneFloor, "floor_stone");
    public static Texture2D GetConcreteFloorTexture() => GetOrLoadTexture(ConcreteFloorResPath, ArtifactConcreteFloor, "floor_concrete");
    public static Texture2D GetRoofResidentialTexture() => GetOrLoadTexture(RoofResidentialResPath, ArtifactRoofResidential, "roof_residential");
    public static Texture2D GetRoofIndustrialTexture() => GetOrLoadIndustrialRoofTexture();

    private static Texture2D GetOrLoadTexture(string resPath, string fallbackArtifactPath, string debugName)
    {
        try
        {
            string globalPath = ProjectSettings.GlobalizePath(resPath);

            // Если файла в проекте ещё нет, но есть сгенерированный артефакт — копируем и масштабируем до 64x64
            if (!File.Exists(globalPath) && File.Exists(fallbackArtifactPath))
            {
                try
                {
                    string dir = Path.GetDirectoryName(globalPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    var img = Image.LoadFromFile(fallbackArtifactPath);
                    if (img != null)
                    {
                        if (img.GetWidth() != MapRenderer.TileSizePx || img.GetHeight() != MapRenderer.TileSizePx)
                        {
                            img.Resize(MapRenderer.TileSizePx, MapRenderer.TileSizePx, Image.Interpolation.Lanczos);
                        }
                        img.SavePng(globalPath);
                        GD.Print($"[FloorRoofTextureHelper] Текстура '{debugName}' успешно скопирована и масштабирована в '{resPath}'");
                        return ImageTexture.CreateFromImage(img);
                    }
                }
                catch (Exception copyEx)
                {
                    GD.PrintErr($"[FloorRoofTextureHelper] Ошибка подготовки файла '{resPath}' из артефакта: {copyEx.Message}");
                }
            }

            // Если файл существует в проекте
            if (File.Exists(globalPath))
            {
                var img = Image.LoadFromFile(globalPath);
                if (img != null)
                {
                    if (img.GetWidth() != MapRenderer.TileSizePx || img.GetHeight() != MapRenderer.TileSizePx)
                    {
                        img.Resize(MapRenderer.TileSizePx, MapRenderer.TileSizePx, Image.Interpolation.Lanczos);
                    }
                    return ImageTexture.CreateFromImage(img);
                }
            }

            // Прямая загрузка из артефакта как резерв
            if (File.Exists(fallbackArtifactPath))
            {
                var img = Image.LoadFromFile(fallbackArtifactPath);
                if (img != null)
                {
                    if (img.GetWidth() != MapRenderer.TileSizePx || img.GetHeight() != MapRenderer.TileSizePx)
                    {
                        img.Resize(MapRenderer.TileSizePx, MapRenderer.TileSizePx, Image.Interpolation.Lanczos);
                    }
                    return ImageTexture.CreateFromImage(img);
                }
            }

            if (ResourceLoader.Exists(resPath))
            {
                return ResourceLoader.Load<Texture2D>(resPath);
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[FloorRoofTextureHelper] Ошибка загрузки текстуры '{debugName}': {ex.Message}");
        }

        return CreateFallbackTexture(debugName);
    }

    private static Texture2D GetOrLoadIndustrialRoofTexture()
    {
        try
        {
            string globalPath = ProjectSettings.GlobalizePath(RoofIndustrialResPath);
            if (File.Exists(globalPath))
            {
                var loaded = Image.LoadFromFile(globalPath);
                if (loaded != null)
                {
                    if (loaded.GetWidth() != MapRenderer.TileSizePx || loaded.GetHeight() != MapRenderer.TileSizePx)
                        loaded.Resize(MapRenderer.TileSizePx, MapRenderer.TileSizePx, Image.Interpolation.Lanczos);
                    return ImageTexture.CreateFromImage(loaded);
                }
            }

            // Создаем полупрозрачную текстуру крыши промышленного цеха со стропилами и балочным перекрытием:
            // она делает интерьер чуть темнее, но оставляет станки, сырье и рабочих видимыми снаружи!
            var img = Image.CreateEmpty(MapRenderer.TileSizePx, MapRenderer.TileSizePx, false, Image.Format.Rgba8);
            var shadowColor = new Color(0.10f, 0.12f, 0.16f, 0.38f); // полупрозрачное затемнение
            var girderColor = new Color(0.18f, 0.20f, 0.24f, 0.65f); // стальная балка перекрытия
            var rivetColor = new Color(0.28f, 0.30f, 0.34f, 0.85f);  // заклепки/швы

            for (int y = 0; y < MapRenderer.TileSizePx; y++)
            {
                for (int x = 0; x < MapRenderer.TileSizePx; x++)
                {
                    // Балки крест-накрест или по краям модульного шага
                    bool isGirder = x is 0 or 1 or 62 or 63 or 31 or 32 || y is 0 or 1 or 62 or 63 or 31 or 32;
                    bool isDiagonalTruss = Math.Abs(x - y) <= 1 || Math.Abs(x - (63 - y)) <= 1;

                    if (isGirder)
                    {
                        bool isRivet = (x % 8 == 0 && (y == 1 || y == 31 || y == 62)) || (y % 8 == 0 && (x == 1 || x == 31 || x == 62));
                        img.SetPixel(x, y, isRivet ? rivetColor : girderColor);
                    }
                    else if (isDiagonalTruss && (x > 8 && x < 56 && y > 8 && y < 56))
                    {
                        img.SetPixel(x, y, girderColor);
                    }
                    else
                    {
                        img.SetPixel(x, y, shadowColor);
                    }
                }
            }

            string dir = Path.GetDirectoryName(globalPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            img.SavePng(globalPath);

            return ImageTexture.CreateFromImage(img);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[FloorRoofTextureHelper] Ошибка создания industrial roof: {ex.Message}");
            return CreateFallbackTexture("roof_industrial");
        }
    }

    private static Texture2D CreateFallbackTexture(string name)
    {
        var img = Image.CreateEmpty(MapRenderer.TileSizePx, MapRenderer.TileSizePx, false, Image.Format.Rgba8);
        Color baseCol = name switch
        {
            "floor_wood" => new Color(0.65f, 0.45f, 0.25f, 1f),
            "floor_stone" => new Color(0.5f, 0.5f, 0.52f, 1f),
            "floor_concrete" => new Color(0.7f, 0.7f, 0.72f, 1f),
            "roof_residential" => new Color(0.8f, 0.35f, 0.2f, 1f),
            _ => new Color(0.6f, 0.6f, 0.6f, 1f)
        };
        Color borderCol = baseCol.Darkened(0.25f);

        for (int y = 0; y < MapRenderer.TileSizePx; y++)
        {
            for (int x = 0; x < MapRenderer.TileSizePx; x++)
            {
                bool border = x == 0 || x == MapRenderer.TileSizePx - 1 || y == 0 || y == MapRenderer.TileSizePx - 1;
                img.SetPixel(x, y, border ? borderCol : baseCol);
            }
        }

        return ImageTexture.CreateFromImage(img);
    }
}
