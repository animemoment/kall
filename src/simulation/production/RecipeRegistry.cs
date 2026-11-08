using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Core;

namespace Game.Simulation;

/// <summary>
/// Рецепт производства (task-20, v1): станок + сырьё → товар за N игровых секунд.
///
/// Ограничения v1 (сознательно, «не раздувать»): один входной слот (count штук
/// одного из перечисленных вариантов) и один выходной слот. Варианты входа нужны
/// там, где сырьё приходит разными типами (железо: гематит/магнетит). Мультивход
/// (несколько РАЗНЫХ обязательных компонентов) — следующим шагом; рецепты с >1
/// обязательным компонентом в этот реестр не добавлять, ProductionJobHandler их
/// не обслужит.
/// </summary>
public sealed class Recipe
{
    /// <summary>Стабильный строковый id рецепта (сейвы/UI/логи).</summary>
    public string Id { get; }

    /// <summary>Станок, на котором выполняется рецепт (O(1) выбор по типу).</summary>
    public BuildingType Station { get; }

    /// <summary>Допустимые варианты входного сырья (например, два типа железной руды).</summary>
    public IReadOnlyList<ItemId> InputItems { get; }

    /// <summary>Сколько штук выбранного варианта нужно за цикл.</summary>
    public int InputCount { get; }

    /// <summary>Что получается за цикл.</summary>
    public ItemId OutputItem { get; }

    /// <summary>Сколько штук выхода за цикл.</summary>
    public int OutputCount { get; }

    /// <summary>Длительность работы у станка в ИГРОВЫХ секундах.</summary>
    public float DurationSec { get; }

    /// <summary>Готовая строка для UI: «2× Железная руда (гематит) → 1× Железный слиток».</summary>
    public string Summary { get; }

    /// <summary>Строка входа для UI: «2× Железная руда (гематит)».</summary>
    public string InputLabel { get; }

    /// <summary>Строка выхода для UI: «1× Железный слиток».</summary>
    public string OutputLabel { get; }

    public Recipe(string id, BuildingType station, ItemId[] inputItems, int inputCount,
        ItemId outputItem, int outputCount, float durationSec)
    {
        if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
        if (inputItems == null || inputItems.Length == 0)
            throw new ArgumentException("рецепт без входа", nameof(inputItems));
        Id = id;
        Station = station;
        InputItems = inputItems;
        InputCount = Math.Max(1, inputCount);
        OutputItem = outputItem;
        OutputCount = Math.Max(1, outputCount);
        DurationSec = MathF.Max(0.5f, durationSec);

        InputLabel = $"{InputCount}× {NameOf(inputItems[0])}";
        if (inputItems.Length > 1)
            InputLabel += " (любая из руд)";
        OutputLabel = $"{OutputCount}× {NameOf(outputItem)}";
        Summary = $"{InputLabel} → {OutputLabel}";
    }

    /// <summary>Подходит ли предмет как вход этого рецепта.</summary>
    public bool AcceptsInput(ItemId item)
    {
        for (int i = 0; i < InputItems.Count; i++)
        {
            if (InputItems[i] == item)
                return true;
        }
        return false;
    }

    private static string NameOf(ItemId item) =>
        ItemRegistry.TryGet(item, out var def) ? def.Name : item.ToString();
}

/// <summary>
/// Реестр рецептов производства v1. Выбор рецепта по станку — O(1) словарь,
/// список крошечный и статический: аллокаций в горячем пути нет.
///
/// Производство v1 (task-20), три рецепта:
///   * Печь (Smelter): 2 железной руды (гематит/магнетит) → 1 железный слиток;
///   * Пилорама (Sawmill): 1 бревно → 2 доски;
///   * Мельница (Mill): 2 зерна → 1 мука.
/// </summary>
public static class RecipeRegistry
{
    // Время в игровых секундах (WorldTime: 500 игровых секунд = 1 игровой час).
    // Цифры подобраны так, чтобы цикл был виден игроку, но станок не «шипел»
    // мгновенно. Крутить здесь.
    private const float SmeltDurationSec = 40f;  // ~5 игровых минут
    private const float SawDurationSec = 15f;    // ~2 игровые минуты
    private const float MillDurationSec = 25f;   // ~3 игровые минуты

    private static readonly Dictionary<BuildingType, List<Recipe>> _allByStation = new(8);
    private static readonly Dictionary<BuildingType, Recipe> _byStation = new(8);
    private static readonly Dictionary<string, Recipe> _byId = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Recipe[] _all;

    static RecipeRegistry()
    {
        var list = new List<Recipe>(32);

        // 1. ПЛАВИЛЬНЯ / ПЕЧЬ (Smelter):
        // 1.1 Железная руда (гематит/магнетит) → Железный слиток
        list.Add(new Recipe(
            "smelt_iron_ingot",
            BuildingType.Smelter,
            new[] { ItemId.IronHematite, ItemId.IronMagnetite },
            inputCount: 2, ItemId.IronIngot, outputCount: 1,
            durationSec: SmeltDurationSec));

        // 1.2 Медная руда → Медный слиток
        list.Add(new Recipe(
            "smelt_copper_ingot",
            BuildingType.Smelter,
            new[] { ItemId.CopperChalcopyrite, ItemId.CopperOxide },
            inputCount: 2, ItemId.CopperIngot, outputCount: 1,
            durationSec: 35f));

        // 1.3 Глина → Кирпич
        list.Add(new Recipe(
            "bake_brick",
            BuildingType.Smelter,
            new[] { ItemId.Clay, ItemId.Fireclay, ItemId.Kaolin },
            inputCount: 2, ItemId.Brick, outputCount: 2,
            durationSec: 25f));

        // 1.4 Мука → Свежий хлеб
        list.Add(new Recipe(
            "bake_bread",
            BuildingType.Smelter,
            new[] { ItemId.Flour },
            inputCount: 1, ItemId.Bread, outputCount: 2,
            durationSec: 20f));

        // 2. ПИЛОРАМА (Sawmill): Бревно → Доска
        list.Add(new Recipe(
            "saw_planks",
            BuildingType.Sawmill,
            new[] { ItemId.Log, ItemId.WoodHardwood },
            inputCount: 1, ItemId.Plank, outputCount: 2,
            durationSec: SawDurationSec));

        // 3. МЕЛЬНИЦА (Mill): Зерно/Рожь/Ячмень/Овес/Кукуруза/Рис → Мука
        list.Add(new Recipe(
            "mill_flour",
            BuildingType.Mill,
            new[] { ItemId.Grain, ItemId.Rye, ItemId.Barley, ItemId.Oats, ItemId.Maize, ItemId.Rice },
            inputCount: 2, ItemId.Flour, outputCount: 1,
            durationSec: MillDurationSec));

        // 4. НАКОВАЛЬНЯ (Anvil):
        // 4.1 Слитки железа/меди → Инструменты
        list.Add(new Recipe(
            "forge_iron_tools",
            BuildingType.Anvil,
            new[] { ItemId.IronIngot, ItemId.CopperIngot },
            inputCount: 1, ItemId.IronTools, outputCount: 1,
            durationSec: 30f));

        // 4.2 Доски → Мебель / фурнитура
        list.Add(new Recipe(
            "forge_furniture_plank",
            BuildingType.Anvil,
            new[] { ItemId.Plank },
            inputCount: 2, ItemId.FurnitureParts, outputCount: 1,
            durationSec: 25f));

        // 5. СТОЛЯРНЫЙ ВЕРСТАК (Workbench):
        // 5.1 Доски → Мебельная фурнитура / Мебель
        list.Add(new Recipe(
            "craft_furniture_parts",
            BuildingType.Workbench,
            new[] { ItemId.Plank },
            inputCount: 2, ItemId.FurnitureParts, outputCount: 1,
            durationSec: 25f));

        // 5.2 Слитки железа/меди → Инструменты
        list.Add(new Recipe(
            "craft_tools_workbench",
            BuildingType.Workbench,
            new[] { ItemId.IronIngot, ItemId.CopperIngot },
            inputCount: 1, ItemId.IronTools, outputCount: 1,
            durationSec: 30f));

        // 6. ТКАЦКИЙ СТАНОК (Loom):
        // 6.1 Волокно (Хлопок/Лён/Пенька/Джут/Шерсть/Шелк) → Ткань
        list.Add(new Recipe(
            "weave_cloth",
            BuildingType.Loom,
            new[] { ItemId.Cotton, ItemId.FlaxFiber, ItemId.HempFiber, ItemId.Jute, ItemId.Wool, ItemId.Silk },
            inputCount: 2, ItemId.Cloth, outputCount: 1,
            durationSec: 25f));

        // 6.2 Ткань → Теплая одежда
        list.Add(new Recipe(
            "loom_clothes",
            BuildingType.Loom,
            new[] { ItemId.Cloth },
            inputCount: 1, ItemId.Clothes, outputCount: 1,
            durationSec: 25f));

        // 7. ПРЯЛКА (SpinningWheel):
        // 7.1 Волокно (Хлопок/Лён/Пенька/Джут/Шерсть/Шелк) → Ткань / Пряжа
        list.Add(new Recipe(
            "spin_fiber",
            BuildingType.SpinningWheel,
            new[] { ItemId.Cotton, ItemId.FlaxFiber, ItemId.HempFiber, ItemId.Jute, ItemId.Wool, ItemId.Silk },
            inputCount: 2, ItemId.Cloth, outputCount: 1,
            durationSec: 25f));

        // 7.2 Ткань → Теплая одежда
        list.Add(new Recipe(
            "tailor_clothes",
            BuildingType.SpinningWheel,
            new[] { ItemId.Cloth },
            inputCount: 1, ItemId.Clothes, outputCount: 1,
            durationSec: 25f));

        // 8. СБОРОЧНАЯ ЛИНИЯ (AssemblyLine):
        // 8.1 Доски → Мебель
        list.Add(new Recipe(
            "assembly_furniture",
            BuildingType.AssemblyLine,
            new[] { ItemId.Plank },
            inputCount: 2, ItemId.FurnitureParts, outputCount: 1,
            durationSec: 30f));

        // 8.2 Слитки → Инструменты
        list.Add(new Recipe(
            "assembly_tools",
            BuildingType.AssemblyLine,
            new[] { ItemId.IronIngot, ItemId.CopperIngot },
            inputCount: 1, ItemId.IronTools, outputCount: 1,
            durationSec: 25f));

        // 9. ВЕРСТАК ОБЫЧНЫЙ (WorkTable):
        // 9.1 Бревно → Доски
        list.Add(new Recipe(
            "worktable_planks",
            BuildingType.WorkTable,
            new[] { ItemId.Log, ItemId.WoodHardwood },
            inputCount: 1, ItemId.Plank, outputCount: 2,
            durationSec: 20f));

        // 9.2 Доски → Мебель
        list.Add(new Recipe(
            "worktable_furniture",
            BuildingType.WorkTable,
            new[] { ItemId.Plank },
            inputCount: 2, ItemId.FurnitureParts, outputCount: 1,
            durationSec: 25f));

        // 9.3 Слитки → Инструменты
        list.Add(new Recipe(
            "worktable_tools",
            BuildingType.WorkTable,
            new[] { ItemId.IronIngot, ItemId.CopperIngot },
            inputCount: 1, ItemId.IronTools, outputCount: 1,
            durationSec: 30f));

        for (int i = 0; i < list.Count; i++)
        {
            Recipe r = list[i];
            if (!_allByStation.TryGetValue(r.Station, out var stList))
            {
                stList = new List<Recipe>();
                _allByStation[r.Station] = stList;
                _byStation[r.Station] = r; // primary default recipe
            }
            stList.Add(r);
            _byId[r.Id] = r;
        }
        _all = list.ToArray();
    }

    /// <summary>Все рецепты для указанной станции.</summary>
    public static IReadOnlyList<Recipe> GetRecipesForStation(BuildingType station) =>
        _allByStation.TryGetValue(station, out var list) ? list : Array.Empty<Recipe>();

    /// <summary>Найти рецепт для станка по типу входного ресурса.</summary>
    public static Recipe FindRecipeByInput(BuildingType station, ItemId inputItem)
    {
        if (inputItem == ItemId.None)
            return null;
        if (_allByStation.TryGetValue(station, out var list))
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].AcceptsInput(inputItem))
                    return list[i];
            }
        }
        return null;
    }

    /// <summary>Все рецепты (порядок стабилен: руда, доска, мука).</summary>
    public static IReadOnlyList<Recipe> All => _all;

    /// <summary>Количество рецептов.</summary>
    public static int Count => _all.Length;

    /// <summary>Является ли тип постройки станцией производства.</summary>
    public static bool IsStation(BuildingType station) => _byStation.ContainsKey(station);

    /// <summary>Рецепт станка; null — это не станция производства.</summary>
    public static Recipe GetForStation(BuildingType station) =>
        _byStation.TryGetValue(station, out var recipe) ? recipe : null;

    public static bool TryGetForStation(BuildingType station, out Recipe recipe) =>
        _byStation.TryGetValue(station, out recipe);

    /// <summary>Рецепт по строковому id.</summary>
    public static bool TryGet(string id, out Recipe recipe)
    {
        recipe = null;
        return !string.IsNullOrEmpty(id) && _byId.TryGetValue(id, out recipe);
    }

    /// <summary>Короткое описание для подсказок UI: «40 с».</summary>
    public static string DurationLabel(Recipe recipe) =>
        recipe == null ? string.Empty
        : recipe.DurationSec.ToString("0", CultureInfo.InvariantCulture) + " с";
}