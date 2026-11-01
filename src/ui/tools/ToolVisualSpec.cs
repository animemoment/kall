using System;
using Godot;
using Game.Core.WorldStreaming.Integration;

namespace Game.UI.Tools;

/// <summary>Роль слоя визуала (паритет «остров ↔ за кромкой», §120.10).</summary>
public enum ToolVisualRole
{
    /// <summary>Terrain-автотайл (стены/грядки — движок сам считает соседей).</summary>
    Terrain = 0,
    /// <summary>Оверлейная метка поверх земли (склад-зона).</summary>
    Overlay = 1,
    /// <summary>Полупрозрачный чертёж (план постройки/события).</summary>
    Plan = 2,
    /// <summary>Готовый спрайт объекта (мебель).</summary>
    Sprite = 3,
}

/// <summary>
/// ЕДИНЫЙ канон визуала инструментов стройки (§120.10, 2026-10-25): для каждого
/// вида метки — одна текстура, один оттенок, одна роль. ОБА рендерера (островной
/// MapRenderer и мировой StreamingWorldView) обязаны рисовать вид из этой таблицы:
/// «как выглядит склад/ферма/мебель» не может зависеть от того, внутри острова
/// клетка или за кромкой. Тест run_tool_parity проверяет, что таблицы рендереров
/// совпадают с этой (и друг с другом) — 0 расхождений.
/// Tint: ARGB-мультипликатор слоя; (1,1,1,1) = без тонировки.
/// </summary>
public readonly record struct ToolVisual(
    string Key,
    string TextureRef,
    ToolVisualRole Role,
    float TintR = 1f, float TintG = 1f, float TintB = 1f, float TintA = 1f);

public static class ToolVisualSpec
{
    // ---- Текстуры (единственные точки ссылок; uid:// работает в ResourceLoader) ----
    public const string TexWall = "uid://chubmh2ufwgwp";                       // wooden_wall (атлас 7×7)
    /// <summary>
    /// Иконка склада в HUD. Для КЛЕТОК зоны используется общий рантайм-тайл
    /// <see cref="CreateStockpileZoneTexture"/>: иконка-ящик на каждой клетке
    /// выглядела бы как груда ящиков, а не как зона, и не совпадала бы по alpha
    /// с мировым слоем.
    /// </summary>
    public const string TexStockpileZone = "res://ui/assets/textures/icons/logistic_zone.png";
    /// <summary>Маркер рантайм-текстуры в отчётах инспектора (не файл).</summary>
    public const string RuntimeStockpileZone = "rt:stockpile_zone";
    public const string TexGardenBed = "uid://47god8qachvh";                   // грядки (terrain из сцены)
    public const string TexCorn = "res://ui/assets/textures/food/corn.png";    // фолбэк фермы (нет сцены)
    public const string TexProcessOfWork = "uid://qqhtvxqngv4a";               // «идут работы»
    public const string TexWorkTable = "uid://rp2bpb5c7k2y";
    public const string TexBed = "uid://0uf8ok6dxhyi";
    public const string TexBench = "uid://bi667xckuklur";
    public const string TexNightstand = "uid://ir5jk74j01fg";
    public const string TexCentralBlock = "uid://dsiddo1lt1scb";

    /// <summary>Тонировка зоны склада (совпадает с историческим Modulate острова).</summary>
    public const float StockpileTintR = 0.65f, StockpileTintG = 0.15f,
        StockpileTintB = 0.9f, StockpileTintA = 0.45f;

    /// <summary>Прозрачность слоя чертежей (§29), общая для обоих рендереров.</summary>
    public const float PlanAlpha = 0.55f;

    /// <summary>
    /// Прозрачность СЛОЁВ чертежей (стены/постройки/грядки): ровно как у островных
    /// слоёв MapRenderer (Blueprint/WallPlan/FarmPlan = 0.5). Отдельная константа от
    /// <see cref="PlanAlpha"/>: та уходит в текстуру призрака, эта — в Modulate слоя.
    /// Рассинхрон 0.55↔0.5 ловит tests/run_seam_parity.gd (секция B2).
    /// </summary>
    public const float BlueprintAlpha = 0.5f;

    /// <summary>Размер клетки игры — один тайл зоны склада.</summary>
    public const int TilePx = 64;

    /// <summary>
    /// ОДИН тайл зоны склада для острова и мира: заливка + рамка, цвет запечён
    /// в текстуру. Слои-получатели обязаны держать Modulate = white, иначе цвет
    /// умножится дважды и «склад» снова станет розовым/чёрным.
    /// </summary>
    public static ImageTexture CreateStockpileZoneTexture(float alpha = StockpileTintA)
    {
        var img = Image.CreateEmpty(TilePx, TilePx, false, Image.Format.Rgba8);
        var fill = new Color(StockpileTintR, StockpileTintG, StockpileTintB,
            Math.Clamp(alpha, 0f, 1f));
        var border = new Color(0.85f, 0.35f, 1f, Math.Clamp(alpha + 0.4f, 0f, 1f));
        for (int y = 0; y < TilePx; y++)
        {
            for (int x = 0; x < TilePx; x++)
            {
                bool isBorder = x <= 1 || x >= TilePx - 2 || y <= 1 || y >= TilePx - 2;
                img.SetPixel(x, y, isBorder ? border : fill);
            }
        }
        return ImageTexture.CreateFromImage(img);
    }

    /// <summary>Тонировка готовой зоны склада (для инспектора/тестов).</summary>
    public static Color StockpileTint => new(StockpileTintR, StockpileTintG, StockpileTintB, StockpileTintA);

    /// <summary>Текстура мебели по типу (per-type, НЕ generic).</summary>
    public static string TextureForBuilding(Game.Core.BuildingType type) => type switch
    {
        Game.Core.BuildingType.WorkTable => TexWorkTable,
        Game.Core.BuildingType.Bed => TexBed,
        Game.Core.BuildingType.Bench => TexBench,
        Game.Core.BuildingType.Nightstand => TexNightstand,
        Game.Core.BuildingType.CentralBlock => TexCentralBlock,
        _ => MissingBuildingTexture(type),
    };

    private static string MissingBuildingTexture(Game.Core.BuildingType type)
    {
        GD.PushError($"ToolVisualSpec: no texture registered for building type '{type}' ({(byte)type}).");
        return TexProcessOfWork;
    }

    /// <summary>Готовое (Wall/Building/Crop/Stockpile) — спрайты/terrain.</summary>
    public static ToolVisual ForDone(byte kind, Game.Core.BuildingType type = Game.Core.BuildingType.WoodWall) => kind switch
    {
        WorldKinds.Wall => new("wall", TexWall, ToolVisualRole.Terrain),
        WorldKinds.Building => new($"building:{type}", TextureForBuilding(type), ToolVisualRole.Sprite),
        WorldKinds.Crop => new("crop", TexGardenBed, ToolVisualRole.Terrain),
        WorldKinds.Stockpile => new("stockpile", RuntimeStockpileZone, ToolVisualRole.Overlay,
            StockpileTintR, StockpileTintG, StockpileTintB, StockpileTintA),
        _ => new($"done:{kind}", TexProcessOfWork, ToolVisualRole.Overlay),
    };

    /// <summary>Чертёж (план) — полупрозрачный/оверлейный.</summary>
    public static ToolVisual ForPlan(byte kind, Game.Core.BuildingType type = Game.Core.BuildingType.WoodWall) => kind switch
    {
        WorldKinds.WallPlan => new("wall_plan", TexWall, ToolVisualRole.Plan),
        WorldKinds.BuildingPlan => new($"building_plan:{type}", TextureForBuilding(type), ToolVisualRole.Plan),
        WorldKinds.CropPlan => new("crop_plan", TexGardenBed, ToolVisualRole.Plan),
        WorldKinds.StockpilePlan => new("stockpile_plan", RuntimeStockpileZone, ToolVisualRole.Plan,
            StockpileTintR, StockpileTintG, StockpileTintB, StockpileTintA),
        WorldKinds.CutTreePlan => new("cuttree_plan", TexProcessOfWork, ToolVisualRole.Plan),
        WorldKinds.MinedStonePlan => new("minestone_plan", TexProcessOfWork, ToolVisualRole.Plan),
        _ => new($"plan:{kind}", TexProcessOfWork, ToolVisualRole.Plan),
    };

    /// <summary>Ключи видов (для таблиц паритета в тестах).</summary>
    public static class WorldKinds
    {
        public const byte Wall = WorldDeltaKind.Wall;
        public const byte Building = WorldDeltaKind.Building;
        public const byte Crop = WorldDeltaKind.Crop;
        public const byte Stockpile = WorldDeltaKind.Stockpile;
        public const byte WallPlan = WorldDeltaKind.WallPlan;
        public const byte BuildingPlan = WorldDeltaKind.BuildingPlan;
        public const byte CropPlan = WorldDeltaKind.CropPlan;
        public const byte StockpilePlan = WorldDeltaKind.StockpilePlan;
        public const byte CutTreePlan = WorldDeltaKind.CutTreePlan;
        public const byte MinedStonePlan = WorldDeltaKind.MinedStonePlan;
    }

    /// <summary>Полный перебор видов для теста паритета: (kind, isPlan, type).</summary>
    public static (byte Kind, bool IsPlan, Game.Core.BuildingType Type)[] AllEntries()
    {
        var types = (Game.Core.BuildingType[])Enum.GetValues(typeof(Game.Core.BuildingType));
        var list = new System.Collections.Generic.List<(byte, bool, Game.Core.BuildingType)>();
        foreach (var t in types)
        {
            list.Add((WorldKinds.Building, false, t));
            list.Add((WorldKinds.BuildingPlan, true, t));
        }
        list.Add((WorldKinds.Wall, false, Game.Core.BuildingType.WoodWall));
        list.Add((WorldKinds.WallPlan, true, Game.Core.BuildingType.WoodWall));
        list.Add((WorldKinds.Crop, false, Game.Core.BuildingType.WoodWall));
        list.Add((WorldKinds.CropPlan, true, Game.Core.BuildingType.WoodWall));
        list.Add((WorldKinds.Stockpile, false, Game.Core.BuildingType.WoodWall));
        list.Add((WorldKinds.StockpilePlan, true, Game.Core.BuildingType.WoodWall));
        list.Add((WorldKinds.CutTreePlan, true, Game.Core.BuildingType.WoodWall));
        list.Add((WorldKinds.MinedStonePlan, true, Game.Core.BuildingType.WoodWall));
        return list.ToArray();
    }
}