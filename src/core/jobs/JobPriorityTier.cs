namespace Game.Core;

/// <summary>
/// Уровни системных приоритетов выполнения работ.
/// </summary>
public enum JobPriorityTier : byte
{
    Emergency = 0,       // Аварийная расчистка, устранение угроз
    BlueprintSupply = 1, // Доставка стройматериалов на активную стройку
    Construction = 2,    // Строительство
    Farming = 3,         // Сельское хозяйство
    TreeChopping = 4,    // Лесозаготовка
    StockpileHauling = 5,// Переноска на склады
    Production = 6,      // Работа у станка (task-20): после снабжения, до фоновых
    Low = 7              // Фоновые и второстепенные работы
}