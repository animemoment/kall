using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.Simulation;

/// <summary>
/// Определение культуры: что сажаем, что собираем, сколько растёт.
/// Данные статические — без Godot API, доступны из любого потока.
/// </summary>
public readonly record struct CropDefinition(
    CropType Type,
    string Id,              // совпадает с id арта и с id сырья в атласе
    string DisplayName,
    ItemId HarvestItem,     // что падает при сборе
    CropCategory Category,
    float GrowthSeconds);   // полный цикл от посадки до зрелости

/// <summary>
/// Реестр культур. Один источник правды: и окно фермы, и симуляция, и рендер
/// берут данные отсюда. Соответствие «id ресурса → ItemId» строится из
/// <see cref="RawItemDefinitions"/>, чтобы не дублировать таблицу предметов.
/// </summary>
public static class CropRegistry
{
    /// <summary>Полный цикл роста: 4 стадии по 60 игровых секунд.</summary>
    public const float DefaultGrowthSeconds = 240f;

    private static readonly CropDefinition[] _byType = new CropDefinition[32];
    private static readonly Dictionary<string, ItemId> _itemByResource = BuildItemMap();

    static CropRegistry()
    {
        Register(CropType.Wheat, "wheat", "Пшеница", CropCategory.Food);
        Register(CropType.Rye, "rye", "Рожь", CropCategory.Food);
        Register(CropType.Barley, "barley", "Ячмень", CropCategory.Food);
        Register(CropType.Oats, "oats", "Овёс", CropCategory.Food);
        Register(CropType.Maize, "maize", "Кукуруза", CropCategory.Food);
        Register(CropType.Rice, "rice", "Рис", CropCategory.Food);
        Register(CropType.Potato, "potato", "Картофель", CropCategory.Food);
        Register(CropType.SugarBeet, "sugar_beet", "Сахарная свёкла", CropCategory.Food);
        Register(CropType.SugarCane, "sugar_cane", "Сахарный тростник", CropCategory.Food);
        Register(CropType.Peas, "peas", "Горох", CropCategory.Food);
        Register(CropType.Beans, "beans", "Фасоль", CropCategory.Food);
        Register(CropType.Sunflower, "sunflower", "Подсолнечник", CropCategory.Food);
        Register(CropType.Rapeseed, "rapeseed", "Рапс", CropCategory.Food);
        Register(CropType.FlaxSeed, "flax_seed", "Семя льна", CropCategory.Food);

        Register(CropType.Cotton, "cotton", "Хлопок", CropCategory.Fiber);
        Register(CropType.FlaxFiber, "flax_fiber", "Лён", CropCategory.Fiber);
        Register(CropType.HempFiber, "hemp_fiber", "Пенька", CropCategory.Fiber);
        Register(CropType.Jute, "jute", "Джут", CropCategory.Fiber);
    }

    private static void Register(CropType type, string id, string name, CropCategory category)
    {
        ItemId harvest = _itemByResource.TryGetValue(id, out ItemId item) ? item : ItemId.Grain;
        _byType[(byte)type] = new CropDefinition(type, id, name, harvest, category, DefaultGrowthSeconds);
    }

    /// <summary>Определение культуры. Для None вернёт пшеницу как безопасный дефолт.</summary>
    public static CropDefinition Get(CropType type)
    {
        int i = (byte)type;
        if (i > 0 && i < _byType.Length && _byType[i].Type == type)
            return _byType[i];
        return _byType[(byte)CropType.Wheat];
    }

    public static bool TryGet(CropType type, out CropDefinition def)
    {
        int i = (byte)type;
        if (i > 0 && i < _byType.Length && _byType[i].Type == type)
        {
            def = _byType[i];
            return true;
        }
        def = default;
        return false;
    }

    /// <summary>Все зарегистрированные культуры в порядке enum.</summary>
    public static IReadOnlyList<CropDefinition> All
    {
        get
        {
            var list = new List<CropDefinition>(18);
            for (int i = 1; i < _byType.Length; i++)
                if (_byType[i].Type != CropType.None)
                    list.Add(_byType[i]);
            return list;
        }
    }

    /// <summary>Культуры категории (для группировки в окне фермы).</summary>
    public static List<CropDefinition> ByCategory(CropCategory category)
    {
        var list = new List<CropDefinition>(14);
        for (int i = 1; i < _byType.Length; i++)
            if (_byType[i].Type != CropType.None && _byType[i].Category == category)
                list.Add(_byType[i]);
        return list;
    }

    // ------------------------------------------------------------------ //
    //  Соответствие «id ресурса → ItemId»                                 //
    // ------------------------------------------------------------------ //

    private static Dictionary<string, ItemId> BuildItemMap()
    {
        var map = new Dictionary<string, ItemId>(StringComparer.Ordinal);

        // 87 новых ресурсов: id восстанавливается тем же способом, каким создавались.
        IReadOnlyList<ItemDefinition> raw = RawItemDefinitions.All;
        for (int i = 0; i < raw.Count; i++)
            map[RawItemDefinitions.ResourceId(raw[i].Id)] = raw[i].Id;

        // 3 legacy-ресурса с историческими ItemId.
        map["wheat"] = ItemId.Grain;
        map["stone"] = ItemId.Stone;
        map["wood_softwood"] = ItemId.Log;

        return map;
    }
}