using System.Collections.Generic;

namespace Game.Core;

public static class ItemRegistry
{
    public static readonly ItemDefinition Log = new(
        ItemId.Log,
        "Бревно",
        0.85f,
        100,
        "uid://by88ysblfuqqu"
    );

    public static readonly ItemDefinition Grain = new(
        ItemId.Grain,
        "Зерно",
        0.15f,
        100,
        "uid://byln5m1aam7tg"
    );

    // Камень: тяжёлый (1.5кг — агент унесёт ~16 шт за раз при лимите 25кг),
    // спрайт — 128x128 атлас 2x2, кусок выбирается по варианту россыпи.
    // UID всего файла; конкретный квадрант режется кодом (AtlasTexture).
    public static readonly ItemDefinition Stone = new(
        ItemId.Stone,
        "Камень",
        1.5f,
        100,
        "uid://du6ur8mvtu3le"
    );

    // ---- T1-товары производства (task-20). Арт — ui/assets/textures/drop/<id>.png.
    // Спрайты подобраны из существующего дропа (слиток ← руда, доска ← брёвна,
    // мука ← пшеница): отдельного арта товаров пока нет, но рендер/склад работают.

    /// <summary>Железный слиток (печь: 2 железной руды → 1 слиток).</summary>
    public static readonly ItemDefinition IronIngot = new(
        ItemId.IronIngot,
        "Железный слиток",
        2.0f,
        ItemConfig.RawMaxStack,
        "res://ui/assets/textures/drop/iron_ingot.png"
    );

    /// <summary>Доска (пилорама: 1 бревно → 2 доски).</summary>
    public static readonly ItemDefinition Plank = new(
        ItemId.Plank,
        "Доска",
        0.6f,
        ItemConfig.RawMaxStack,
        "res://ui/assets/textures/drop/plank.png"
    );

    /// <summary>Мука (мельница: 2 зерна → 1 мука).</summary>
    public static readonly ItemDefinition Flour = new(
        ItemId.Flour,
        "Мука",
        0.5f,
        ItemConfig.RawMaxStack,
        "res://ui/assets/textures/drop/flour.png"
    );

    /// <summary>Хлеб (печь: 1 мука → 2 хлеба).</summary>
    public static readonly ItemDefinition Bread = new(
        ItemId.Bread,
        "Хлеб",
        0.3f,
        ItemConfig.RawMaxStack,
        "res://ui/assets/textures/drop/bread.png"
    );

    /// <summary>Медный слиток (печь: 2 медной руды → 1 слиток).</summary>
    public static readonly ItemDefinition CopperIngot = new(
        ItemId.CopperIngot,
        "Медный слиток",
        1.8f,
        ItemConfig.RawMaxStack,
        "res://ui/assets/textures/drop/copper_ingot.png"
    );

    /// <summary>Обожжённый кирпич (печь: 2 глины → 2 кирпича).</summary>
    public static readonly ItemDefinition Brick = new(
        ItemId.Brick,
        "Кирпич",
        1.2f,
        ItemConfig.RawMaxStack,
        "res://ui/assets/textures/drop/brick.png"
    );

    /// <summary>Рулон ткани (ткацкий станок: 2 волокна → 1 ткань).</summary>
    public static readonly ItemDefinition Cloth = new(
        ItemId.Cloth,
        "Ткань",
        0.4f,
        ItemConfig.RawMaxStack,
        "res://ui/assets/textures/drop/cloth.png"
    );

    /// <summary>Теплая одежда (прялка: 1 ткань → 1 одежда).</summary>
    public static readonly ItemDefinition Clothes = new(
        ItemId.Clothes,
        "Одежда",
        0.5f,
        ItemConfig.RawMaxStack,
        "res://ui/assets/textures/drop/clothes.png"
    );

    /// <summary>Железные инструменты (наковальня: 1 слиток → 1 инструмент).</summary>
    public static readonly ItemDefinition IronTools = new(
        ItemId.IronTools,
        "Инструменты",
        1.5f,
        ItemConfig.RawMaxStack,
        "res://ui/assets/textures/drop/iron_tools.png"
    );

    /// <summary>Мебельные заготовки (верстак: 2 доски → 1 заготовка).</summary>
    public static readonly ItemDefinition FurnitureParts = new(
        ItemId.FurnitureParts,
        "Фурнитура",
        1.0f,
        ItemConfig.RawMaxStack,
        "res://ui/assets/textures/drop/furniture_parts.png"
    );

    /// <summary>
    /// Все определения предметов.
    /// </summary>
    private static readonly Dictionary<ItemId, ItemDefinition> _items = new(ItemConfig.MaxItemTypes);

    static ItemRegistry()
    {
        Register(Log);
        Register(Grain);
        Register(Stone);
        Register(IronIngot);
        Register(Plank);
        Register(Flour);
        Register(Bread);
        Register(CopperIngot);
        Register(Brick);
        Register(Cloth);
        Register(Clothes);
        Register(IronTools);
        Register(FurnitureParts);
        IReadOnlyList<ItemDefinition> raw = RawItemDefinitions.All;
        for (int i = 0; i < raw.Count; i++)
            Register(raw[i]);
    }

    private static void Register(ItemDefinition definition) => _items[definition.Id] = definition;

    public static ItemDefinition Get(ItemId id) => _items[id];

    /// <summary>Есть ли определение (None и мусорные id — false, без исключения).</summary>
    public static bool TryGet(ItemId id, out ItemDefinition definition)
        => _items.TryGetValue(id, out definition);

    // ------------------------------------------------------------------ //
    //  Руда (task-3/task-4): OreType 1..23 ↔ ItemId 23..45.                //
    //  Порядок: OreType N — N-я запись категории «руда» в атласе; enum     //
    //  сырья собран в порядке index атласа, поэтому блок руды непрерывен.  //
    // ------------------------------------------------------------------ //

    /// <summary>ItemId первой руды (iron_hematite).</summary>
    public const int OreItemIdFirst = (int)ItemId.IronHematite; // 23

    /// <summary>ItemId последней руды (monazite).</summary>
    public const int OreItemIdLast = (int)ItemId.Monazite; // 45

    /// <summary>Тип руды (1..23) → ItemId дропа. Невалидный тип → None.</summary>
    public static ItemId ForOreType(int oreType)
    {
        if (oreType < 1 || oreType > OreConfig.TypeCount)
            return ItemId.None;
        return (ItemId)(OreItemIdFirst + oreType - 1);
    }

    /// <summary>Является ли предмет рудой (для HUD/склада/будущих рецептов).</summary>
    public static bool IsOreItem(ItemId id)
    {
        int value = (int)id;
        return value >= OreItemIdFirst && value <= OreItemIdLast;
    }
}