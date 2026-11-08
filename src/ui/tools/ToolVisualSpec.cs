using System;

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
    public const string TexStockpileZone = "res://ui/assets/textures/icons/logistic_zone.png";
    public const string TexGardenBed = "uid://47god8qachvh";                   // грядки (terrain из сцены)
    public const string TexCorn = "res://ui/assets/textures/food/corn.png";    // фолбэк фермы (нет сцены)
    public const string TexProcessOfWork = "uid://qqhtvxqngv4a";               // «идут работы»
    public const string TexWorkTable = "uid://rp2bpb5c7k2y";
    public const string TexBed = "uid://0uf8ok6dxhyi";
    public const string TexBench = "uid://bi667xckuklur";
    public const string TexNightstand = "uid://ir5jk74j01fg";
    public const string TexCentralBlock = "uid://dsiddo1lt1scb";

    // task-16: станки отраслей (арт в ui/assets/textures/building/). Призрак и
    // «готовый» вид в мировом рендере берут ту же текстуру, что стоит в MapRenderer.
    public const string TexSawmill = "res://ui/assets/textures/building/sawmill.png";
    public const string TexSmelter = "res://ui/assets/textures/building/smelter.png";
    public const string TexAnvil = "res://ui/assets/textures/building/anvil.png";
    public const string TexWorkbench = "res://ui/assets/textures/building/workbench.png";
    public const string TexSpinningWheel = "res://ui/assets/textures/building/spinning.png";
    public const string TexLoom = "res://ui/assets/textures/building/loom.png";
    public const string TexMill = "res://ui/assets/textures/building/mill.png";
    public const string TexAssemblyLine = "res://ui/assets/textures/building/assembly.png";

    /// <summary>Тонировка зоны склада (совпадает с историческим Modulate острова).</summary>
    public const float StockpileTintR = 0.65f, StockpileTintG = 0.15f,
        StockpileTintB = 0.9f, StockpileTintA = 0.45f;

    /// <summary>Текстура мебели/станка по типу (per-type, НЕ generic).</summary>
    public static string TextureForBuilding(Game.Core.BuildingType type) => type switch
    {
        Game.Core.BuildingType.WorkTable => TexWorkTable,
        Game.Core.BuildingType.Bed => TexBed,
        Game.Core.BuildingType.Bench => TexBench,
        Game.Core.BuildingType.Nightstand => TexNightstand,
        Game.Core.BuildingType.CentralBlock => TexCentralBlock,
        // task-16: станки отраслей — призрак/вид не должны падать в стену.
        Game.Core.BuildingType.Sawmill => TexSawmill,
        Game.Core.BuildingType.Smelter => TexSmelter,
        Game.Core.BuildingType.Anvil => TexAnvil,
        Game.Core.BuildingType.Workbench => TexWorkbench,
        Game.Core.BuildingType.SpinningWheel => TexSpinningWheel,
        Game.Core.BuildingType.Loom => TexLoom,
        Game.Core.BuildingType.Mill => TexMill,
        Game.Core.BuildingType.AssemblyLine => TexAssemblyLine,
        _ => TexWall,
    };

    /// <summary>Готовое (Wall/Building/Crop/Stockpile) — спрайты/terrain.</summary>
    public static ToolVisual ForDone(byte kind, Game.Core.BuildingType type = Game.Core.BuildingType.WoodWall) => kind switch
    {
        WorldKinds.Wall => new("wall", TexWall, ToolVisualRole.Terrain),
        WorldKinds.Building => new($"building:{type}", TextureForBuilding(type), ToolVisualRole.Sprite),
        WorldKinds.Crop => new("crop", TexGardenBed, ToolVisualRole.Terrain),
        WorldKinds.Stockpile => new("stockpile", TexStockpileZone, ToolVisualRole.Overlay,
            StockpileTintR, StockpileTintG, StockpileTintB, StockpileTintA),
        _ => new($"done:{kind}", TexProcessOfWork, ToolVisualRole.Overlay),
    };

    /// <summary>Чертёж (план) — полупрозрачный/оверлейный.</summary>
    public static ToolVisual ForPlan(byte kind, Game.Core.BuildingType type = Game.Core.BuildingType.WoodWall) => kind switch
    {
        WorldKinds.WallPlan => new("wall_plan", TexWall, ToolVisualRole.Plan),
        WorldKinds.BuildingPlan => new($"building_plan:{type}", TextureForBuilding(type), ToolVisualRole.Plan),
        WorldKinds.CropPlan => new("crop_plan", TexGardenBed, ToolVisualRole.Plan),
        WorldKinds.StockpilePlan => new("stockpile_plan", TexStockpileZone, ToolVisualRole.Plan,
            StockpileTintR, StockpileTintG, StockpileTintB, StockpileTintA),
        WorldKinds.CutTreePlan => new("cuttree_plan", TexProcessOfWork, ToolVisualRole.Plan),
        WorldKinds.MinedStonePlan => new("minestone_plan", TexProcessOfWork, ToolVisualRole.Plan),
        _ => new($"plan:{kind}", TexProcessOfWork, ToolVisualRole.Plan),
    };

    /// <summary>Ключи видов (для таблиц паритета в тестах).</summary>
    public static class WorldKinds
    {
        public const byte Wall = 0x01;
        public const byte Building = 0x03;
        public const byte Crop = 0x04;
        public const byte Stockpile = 0x07;
        public const byte WallPlan = 0x11;
        public const byte BuildingPlan = 0x12;
        public const byte CropPlan = 0x13;
        public const byte StockpilePlan = 0x14;
        public const byte CutTreePlan = 0x15;
        public const byte MinedStonePlan = 0x16;
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