namespace Game.Core;

/// <summary>
/// Тип выращиваемой культуры. Id совпадают с id арта
/// (<c>_sprites/out/world/plants/&lt;id&gt;_s1..s4.png</c>) и с id сырья в атласе,
/// поэтому отдельная таблица соответствий не нужна.
/// </summary>
public enum CropType : byte
{
    None = 0,

    // --- пищевые (14) ---------------------------------------------------
    Wheat = 1,
    Rye = 2,
    Barley = 3,
    Oats = 4,
    Maize = 5,
    Rice = 6,
    Potato = 7,
    SugarBeet = 8,
    SugarCane = 9,
    Peas = 10,
    Beans = 11,
    Sunflower = 12,
    Rapeseed = 13,
    FlaxSeed = 14,

    // --- волокнистые (4) ------------------------------------------------
    Cotton = 15,
    FlaxFiber = 16,
    HempFiber = 17,
    Jute = 18,
}

/// <summary>Категория культуры: еда или волокно. Влияет на группировку в окне фермы.</summary>
public enum CropCategory : byte
{
    Food = 0,
    Fiber = 1,
}