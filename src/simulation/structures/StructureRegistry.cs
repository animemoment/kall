using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.Simulation;

public static class StructureRegistry
{
    private static readonly Dictionary<string, StructureDefinition> _byId = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<BuildingType, StructureDefinition> _byType = new();

    public static StructureDefinition WoodWall { get; }
    public static StructureDefinition WorkTable { get; }
    public static StructureDefinition Bed { get; }
    public static StructureDefinition Bench { get; }
    public static StructureDefinition Nightstand { get; }
    public static StructureDefinition CentralBlock { get; }

    // ---- Станки отраслей (task-12). Спрайты уже нарисованы:
    // res://ui/assets/textures/workplaces/<id>.png; здесь — материалы и время.
    public static StructureDefinition Sawmill { get; }
    public static StructureDefinition Smelter { get; }
    public static StructureDefinition Anvil { get; }
    public static StructureDefinition Workbench { get; }
    public static StructureDefinition SpinningWheel { get; }
    public static StructureDefinition Loom { get; }
    public static StructureDefinition Mill { get; }
    public static StructureDefinition AssemblyLine { get; }

    // ---- Полы и перекрытия (3 варианта) ----
    public static StructureDefinition WoodFloor { get; }
    public static StructureDefinition StoneFloor { get; }
    public static StructureDefinition ConcreteFloor { get; }

    // ---- Крыши ----
    public static StructureDefinition RoofResidential { get; }
    public static StructureDefinition RoofIndustrial { get; }

    static StructureRegistry()
    {
        WoodWall = Register(new StructureDefinition(
            id: "wood_wall",
            displayName: "Деревянная стена",
            type: BuildingType.WoodWall,
            category: StructureCategory.Wall,
            materials: new[] { (ItemId.Log, BuildConfig.LogsWall) },
            buildDurationSec: BuildConfig.BuildDurationSec,
            blocksMovement: true,
            castsShadow: true
        ));

        WorkTable = Register(new StructureDefinition(
            id: "work_table",
            displayName: "Верстак",
            type: BuildingType.WorkTable,
            category: StructureCategory.Production,
            materials: new[] { (ItemId.Log, BuildConfig.LogsWorkTable) },
            buildDurationSec: BuildConfig.BuildDurationSec,
            blocksMovement: true,
            castsShadow: true
        ));

        Bed = Register(new StructureDefinition(
            id: "bed",
            displayName: "Кровать",
            type: BuildingType.Bed,
            category: StructureCategory.Furniture,
            materials: new[] { (ItemId.Log, BuildConfig.LogsBed) },
            buildDurationSec: BuildConfig.BuildDurationSec,
            blocksMovement: false,
            castsShadow: false
        ));

        Bench = Register(new StructureDefinition(
            id: "bench",
            displayName: "Скамья",
            type: BuildingType.Bench,
            category: StructureCategory.Furniture,
            materials: new[] { (ItemId.Log, BuildConfig.LogsBench) },
            buildDurationSec: BuildConfig.BuildDurationSec,
            blocksMovement: false,
            castsShadow: false
        ));

        Nightstand = Register(new StructureDefinition(
            id: "nightstand",
            displayName: "Тумбочка",
            type: BuildingType.Nightstand,
            category: StructureCategory.Furniture,
            materials: new[] { (ItemId.Log, BuildConfig.LogsNightstand) },
            buildDurationSec: BuildConfig.BuildDurationSec,
            blocksMovement: false,
            castsShadow: false
        ));

        // §119 P5: центральный блок — якорь центра рабочей зоны. НЕ стена и НЕ
        // блокирует навигацию (проходим как декор). Отдельной «декор»-категории
        // в StructureCategory нет — ближайшая разумная Furniture (как мебель:
        // blocksMovement=false, castsShadow=false). Спрайт central.png.
        CentralBlock = Register(new StructureDefinition(
            id: "central_block",
            displayName: "Центральный блок",
            type: BuildingType.CentralBlock,
            category: StructureCategory.Furniture,
            materials: new[] { (ItemId.Log, BuildConfig.LogsCentralBlock) },
            buildDurationSec: BuildConfig.BuildDurationSec,
            blocksMovement: false,
            castsShadow: false
        ));

        // ---- Станки отраслей (task-12, task-16, task-19) ----
        // Габариты — из арта (BuildingFootprint), цены — из BuildConfig.
        // task-19: доставка ТИПИЗИРОВАНА (JobData.TargetItemId) — камень едет на
        // площадку наравне с брёвнами. RequiredMaterials = бревно (если>0) + камень
        // (если StonesFor>0); материалы возятся последовательно, сайт копит потипно,
        // Supplying→Building только когда довезено ВСЁ.

        Sawmill = Register(new StructureDefinition(
            id: "sawmill",
            displayName: "Пилорама",
            type: BuildingType.Sawmill,
            category: StructureCategory.Production,
            materials: Game.Simulation.StructureMaterials.Build(BuildingType.Sawmill),
            buildDurationSec: BuildConfig.BuildDurationSec * 3f,
            blocksMovement: true,
            castsShadow: true,
            width: BuildingFootprint.WidthOf(BuildingType.Sawmill),
            height: BuildingFootprint.HeightOf(BuildingType.Sawmill)
        ));

        Smelter = Register(new StructureDefinition(
            id: "smelter",
            displayName: "Плавильня",
            type: BuildingType.Smelter,
            category: StructureCategory.Production,
            materials: Game.Simulation.StructureMaterials.Build(BuildingType.Smelter),
            buildDurationSec: BuildConfig.BuildDurationSec * 4f,
            blocksMovement: true,
            castsShadow: true,
            width: BuildingFootprint.WidthOf(BuildingType.Smelter),
            height: BuildingFootprint.HeightOf(BuildingType.Smelter)
        ));

        Anvil = Register(new StructureDefinition(
            id: "anvil",
            displayName: "Наковальня",
            type: BuildingType.Anvil,
            category: StructureCategory.Production,
            materials: Game.Simulation.StructureMaterials.Build(BuildingType.Anvil),
            buildDurationSec: BuildConfig.BuildDurationSec * 2.5f,
            blocksMovement: true,
            castsShadow: true,
            width: BuildingFootprint.WidthOf(BuildingType.Anvil),
            height: BuildingFootprint.HeightOf(BuildingType.Anvil)
        ));

        Workbench = Register(new StructureDefinition(
            id: "workbench",
            displayName: "Столярный верстак",
            type: BuildingType.Workbench,
            category: StructureCategory.Production,
            materials: Game.Simulation.StructureMaterials.Build(BuildingType.Workbench),
            buildDurationSec: BuildConfig.BuildDurationSec * 2.5f,
            blocksMovement: true,
            castsShadow: true,
            width: BuildingFootprint.WidthOf(BuildingType.Workbench),
            height: BuildingFootprint.HeightOf(BuildingType.Workbench)
        ));

        SpinningWheel = Register(new StructureDefinition(
            id: "spinning_wheel",
            displayName: "Прялка",
            type: BuildingType.SpinningWheel,
            category: StructureCategory.Production,
            materials: Game.Simulation.StructureMaterials.Build(BuildingType.SpinningWheel),
            buildDurationSec: BuildConfig.BuildDurationSec * 2f,
            blocksMovement: true,
            castsShadow: true,
            width: BuildingFootprint.WidthOf(BuildingType.SpinningWheel),
            height: BuildingFootprint.HeightOf(BuildingType.SpinningWheel)
        ));

        Loom = Register(new StructureDefinition(
            id: "loom",
            displayName: "Ткацкий станок",
            type: BuildingType.Loom,
            category: StructureCategory.Production,
            materials: Game.Simulation.StructureMaterials.Build(BuildingType.Loom),
            buildDurationSec: BuildConfig.BuildDurationSec * 3f,
            blocksMovement: true,
            castsShadow: true,
            width: BuildingFootprint.WidthOf(BuildingType.Loom),
            height: BuildingFootprint.HeightOf(BuildingType.Loom)
        ));

        Mill = Register(new StructureDefinition(
            id: "mill",
            displayName: "Мельница",
            type: BuildingType.Mill,
            category: StructureCategory.Production,
            materials: Game.Simulation.StructureMaterials.Build(BuildingType.Mill),
            buildDurationSec: BuildConfig.BuildDurationSec * 3.5f,
            blocksMovement: true,
            castsShadow: true,
            width: BuildingFootprint.WidthOf(BuildingType.Mill),
            height: BuildingFootprint.HeightOf(BuildingType.Mill)
        ));

        AssemblyLine = Register(new StructureDefinition(
            id: "assembly_line",
            displayName: "Сборочная линия",
            type: BuildingType.AssemblyLine,
            category: StructureCategory.Production,
            materials: Game.Simulation.StructureMaterials.Build(BuildingType.AssemblyLine),
            buildDurationSec: BuildConfig.BuildDurationSec * 4f,
            blocksMovement: true,
            castsShadow: true,
            width: BuildingFootprint.WidthOf(BuildingType.AssemblyLine),
            height: BuildingFootprint.HeightOf(BuildingType.AssemblyLine)
        ));

        // ---- Полы (дерево, камень, бетон) ----
        WoodFloor = Register(new StructureDefinition(
            id: "floor_wood",
            displayName: "Деревянный пол",
            type: BuildingType.WoodFloor,
            category: StructureCategory.Floor,
            materials: new[] { (ItemId.Plank, 1) },
            buildDurationSec: 4f,
            blocksMovement: false,
            castsShadow: false
        ));

        StoneFloor = Register(new StructureDefinition(
            id: "floor_stone",
            displayName: "Каменный пол",
            type: BuildingType.StoneFloor,
            category: StructureCategory.Floor,
            materials: new[] { (ItemId.Stone, 1) },
            buildDurationSec: 5f,
            blocksMovement: false,
            castsShadow: false
        ));

        ConcreteFloor = Register(new StructureDefinition(
            id: "floor_concrete",
            displayName: "Бетонный пол",
            type: BuildingType.ConcreteFloor,
            category: StructureCategory.Floor,
            materials: new[] { (ItemId.Brick, 1) },
            buildDurationSec: 6f,
            blocksMovement: false,
            castsShadow: false
        ));

        // ---- Крыши ----
        // GD-33: жилые здания накрываются непрозрачной крышей — внутри жителей и кроватей не видно
        RoofResidential = Register(new StructureDefinition(
            id: "roof_residential",
            displayName: "Крыша жилого дома",
            type: BuildingType.RoofResidential,
            category: StructureCategory.Roof,
            materials: new[] { (ItemId.Plank, 2) },
            buildDurationSec: 8f,
            blocksMovement: false,
            castsShadow: true
        ));

        // Крыша промышленного цеха — полупрозрачная затемняющая интерьер
        RoofIndustrial = Register(new StructureDefinition(
            id: "roof_industrial",
            displayName: "Крыша промышленного цеха",
            type: BuildingType.RoofIndustrial,
            category: StructureCategory.Roof,
            materials: new[] { (ItemId.Plank, 2) },
            buildDurationSec: 6f,
            blocksMovement: false,
            castsShadow: false
        ));
    }

    public static StructureDefinition Register(StructureDefinition def)
    {
        if (def == null) throw new ArgumentNullException(nameof(def));
        _byId[def.Id] = def;
        _byType[def.Type] = def;
        return def;
    }

    public static StructureDefinition Get(BuildingType type)
    {
        if (_byType.TryGetValue(type, out var def))
            return def;
        return WoodWall;
    }

    public static StructureDefinition Get(string id)
    {
        if (string.IsNullOrEmpty(id)) return WoodWall;
        if (_byId.TryGetValue(id, out var def))
            return def;
        return WoodWall;
    }

    public static bool TryGet(string id, out StructureDefinition def)
    {
        if (string.IsNullOrEmpty(id))
        {
            def = null;
            return false;
        }
        return _byId.TryGetValue(id, out def);
    }

    public static IEnumerable<StructureDefinition> All => _byId.Values;
}
