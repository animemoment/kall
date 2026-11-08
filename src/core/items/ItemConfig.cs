namespace Game.Core;

/// <summary>
/// Общие лимиты и пути предметов.
/// Диапазон ItemId плотный: 0 = None, 1..3 — legacy (Log/Grain/Stone),
/// 4..90 — сырьё T0 атласа resources_raw_atlas.json в порядке index,
/// 91..93 — товары T1 первого производства (task-20: слиток/доска/мука).
/// </summary>
public static class ItemConfig
{
    /// <summary>
    /// Максимум типов предметов (= последний ItemId + 1). Все массивы-счётчики
    /// по типам (GroundItemManager / StockpileManager) обязаны брать размер ОТСЮДА.
    /// </summary>
    public const int MaxItemTypes = 101;

    /// <summary>Первое и последнее значение СЫРЬЕВЫХ ItemId (плотный диапазон enum, T0).</summary>
    public const int FirstRawItemId = 4;
    public const int LastRawItemId = 90;

    /// <summary>
    /// Первый/последний ТОВАРНЫЙ ItemId (T1/T2, продукция производства). Товары
    /// живут в том же enum и тех же массивах, но НЕ в сырьевом атласе/дропе T0.
    /// </summary>
    public const int FirstGoodsItemId = 91;
    public const int LastGoodsItemId = 100;

    /// <summary>Стандартный максимальный стак сырья на 1 клетку.</summary>
    public const int RawMaxStack = 100;

    /// <summary>Корень спрайтов дропа сырья: <c>&lt;root&gt;&lt;resource_id&gt;.png</c>.</summary>
    public const string DropTextureRoot = "res://ui/assets/textures/drop/";
}