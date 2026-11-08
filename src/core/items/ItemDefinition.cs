namespace Game.Core;

public enum ItemId : byte
{
    None = 0,
    Log = 1,   // Бревно
    Grain = 2, // Зерно / Пшеница
    Stone = 3, // Камень (добыча с каменных россыпей)
    // ---- 87 новых сырьевых ресурсов (task-4): 90 позиций категории T0 атласа
    // resources_raw_atlas.json минус три уже существующих (wheat→Grain, stone→Stone,
    // wood_softwood→Log). Значения заморожены: новый ресурс ДОБАВЛЯЕТСЯ в конец,
    // старые номера не двигаются. Идентификатор ресурса в арте выводится из имени
    // члена enum (PascalCase → snake_case) в RawItemDefinitions. ----
    Rye = 4, // rye
    Barley = 5, // barley
    Oats = 6, // oats
    Maize = 7, // maize
    Rice = 8, // rice
    Potato = 9, // potato
    SugarBeet = 10, // sugar_beet
    SugarCane = 11, // sugar_cane
    Peas = 12, // peas
    Beans = 13, // beans
    Sunflower = 14, // sunflower
    Rapeseed = 15, // rapeseed
    FlaxSeed = 16, // flax_seed
    Milk = 17, // milk
    Eggs = 18, // eggs
    Beef = 19, // beef
    Pork = 20, // pork
    Poultry = 21, // poultry
    FishSea = 22, // fish_sea
    IronHematite = 23, // iron_hematite
    IronMagnetite = 24, // iron_magnetite
    ManganeseOre = 25, // manganese_ore
    Chromite = 26, // chromite
    TungstenWolframite = 27, // tungsten_wolframite
    MolybdenumOre = 28, // molybdenum_ore
    VanadiumOre = 29, // vanadium_ore
    NickelOre = 30, // nickel_ore
    CobaltOre = 31, // cobalt_ore
    TitaniumIlmenite = 32, // titanium_ilmenite
    CopperChalcopyrite = 33, // copper_chalcopyrite
    CopperOxide = 34, // copper_oxide
    LeadGalena = 35, // lead_galena
    ZincSphalerite = 36, // zinc_sphalerite
    TinCassiterite = 37, // tin_cassiterite
    Bauxite = 38, // bauxite
    MercuryCinnabar = 39, // mercury_cinnabar
    Pyrite = 40, // pyrite
    GoldOre = 41, // gold_ore
    SilverOre = 42, // silver_ore
    PlatinumOre = 43, // platinum_ore
    UraniumPitchblende = 44, // uranium_pitchblende
    Monazite = 45, // monazite
    BituminousCoal = 46, // bituminous_coal
    Anthracite = 47, // anthracite
    CokingCoal = 48, // coking_coal
    OilLightParaffinic = 49, // oil_light_paraffinic
    OilHeavySour = 50, // oil_heavy_sour
    NaturalGasDry = 51, // natural_gas_dry
    Sulfur = 52, // sulfur
    RockSalt = 53, // rock_salt
    Potash = 54, // potash
    PhosphateRock = 55, // phosphate_rock
    NitrateChile = 56, // nitrate_chile
    Gypsum = 57, // gypsum
    Asbestos = 58, // asbestos
    Graphite = 59, // graphite
    Kaolin = 60, // kaolin
    Magnesite = 61, // magnesite
    Fluorite = 62, // fluorite
    Borax = 63, // borax
    Granite = 64, // granite
    Limestone = 65, // limestone
    Marble = 66, // marble
    Basalt = 67, // basalt
    Slate = 68, // slate
    Sand = 69, // sand
    Gravel = 70, // gravel
    Clay = 71, // clay
    Fireclay = 72, // fireclay
    Marl = 73, // marl
    WoodHardwood = 74, // wood_hardwood
    CorkBark = 75, // cork_bark
    Resin = 76, // resin
    RubberLatex = 77, // rubber_latex
    GuttaPercha = 78, // gutta_percha
    Cotton = 79, // cotton
    FlaxFiber = 80, // flax_fiber
    HempFiber = 81, // hemp_fiber
    Jute = 82, // jute
    Wool = 83, // wool
    Silk = 84, // silk
    Hides = 85, // hides
    Bone = 86, // bone
    Beeswax = 87, // beeswax
    Tallow = 88, // tallow
    Water = 89, // water
    Ice = 90, // ice

    // ---- T1/T2-товары производства. Дальше T0 — только в конец, номера не двигаем. ----
    IronIngot = 91,      // железный слиток (печь: 2 руды → 1 слиток)
    Plank = 92,          // доска (пилорама: 1 бревно → 2 доски)
    Flour = 93,          // мука (мельница: 2 зерна → 1 мука)
    Bread = 94,          // свежий хлеб (печь/пекарня: 1 мука → 2 хлеба)
    CopperIngot = 95,    // медный слиток (печь: 2 медной руды → 1 слиток)
    Brick = 96,          // обожжённый кирпич (печь: 2 глины → 2 кирпича)
    Cloth = 97,          // рулон ткани (ткацкий станок: 2 волокна → 1 ткань)
    Clothes = 98,        // теплая одежда (прялка: 1 ткань → 1 одежда)
    IronTools = 99,      // железные инструменты (наковальня: 1 слиток → 1 инструмент)
    FurnitureParts = 100 // мебельные заготовки (верстак: 2 доски → 1 заготовка)
}

public record ItemDefinition(
    ItemId Id,
    string Name,
    float Weight,      // Вес 1 штуки в кг
    int MaxStack,      // Максимальный стак на 1 клетку (100)
    string TextureUid  // UID спрайта
);