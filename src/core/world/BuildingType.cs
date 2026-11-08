namespace Game.Core;

/// <summary>
/// Типы построек. Значения 0..5 заморожены (старые сейвы/дельты мира читают их
/// по номеру), новые типы ДОБАВЛЯЮТСЯ только в конец.
///
/// Станки (6..13) — рабочие места отраслей (task-12): у каждого уже нарисован
/// спрайт в res://ui/assets/textures/workplaces/&lt;id&gt;.png, регистрация
/// материалов/времени — StructureRegistry.
/// </summary>
public enum BuildingType : byte
{
    WoodWall = 0,
    WorkTable = 1,
    /// <summary>Кровать (bed.png 64x64).</summary>
    Bed = 2,
    /// <summary>Скамья (bench.png 64x64).</summary>
    Bench = 3,
    /// <summary>Тумбочка (nightstand.png 64x64).</summary>
    Nightstand = 4,
    /// <summary>Центральный блок (central.png 64x64) — якорь центра рабочей зоны (§119).</summary>
    CentralBlock = 5,

    // ---- Станки отраслей (task-12). Порядок = порядок арта workplaces/*.png ----

    /// <summary>Пилорама (sawmill.png) — лесозаготовка/стройматериалы.</summary>
    Sawmill = 6,
    /// <summary>Плавильня (smelter.png) — металлургия.</summary>
    Smelter = 7,
    /// <summary>Наковальня (anvil.png) — станкостроение.</summary>
    Anvil = 8,
    /// <summary>Столярный верстак (workbench.png) — станкостроение/сборка/химия.</summary>
    Workbench = 9,
    /// <summary>Прялка (spinning.png) — текстиль.</summary>
    SpinningWheel = 10,
    /// <summary>Ткацкий станок (loom.png) — текстиль.</summary>
    Loom = 11,
    /// <summary>Мельница (mill.png) — сельское хозяйство/стройматериалы.</summary>
    Mill = 12,
    /// <summary>Сборочная линия (assembly.png) — сборка.</summary>
    AssemblyLine = 13,

    // ---- Полы и перекрытия (3 варианта) ----
    /// <summary>Деревянный пол (floor_wood.png 64x64).</summary>
    WoodFloor = 14,
    /// <summary>Каменный пол (floor_stone.png 64x64).</summary>
    StoneFloor = 15,
    /// <summary>Бетонный пол (floor_concrete.png 64x64).</summary>
    ConcreteFloor = 16,

    // ---- Крыши ----
    /// <summary>Крыша жилого дома (roof_residential.png 64x64) — непрозрачное перекрытие жилья.</summary>
    RoofResidential = 17,
    /// <summary>Крыша промышленного цеха (roof_industrial.png 64x64) — полупрозрачное затемняющее перекрытие цеха со стропилами.</summary>
    RoofIndustrial = 18
}