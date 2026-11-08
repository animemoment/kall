using Game.Core;

namespace Game.Simulation;

/// <summary>
/// ВСЕ цифры стройки в одном месте. Хочешь поменять баланс — меняешь здесь,
/// больше нигде лазить не надо. Простыми словами:
/// - сколько бревен надо на каждую постройку,
/// - сколько секунд длится каждая работа,
/// - сколько тащит агент.
/// </summary>
public static class BuildConfig
{
    // --- Сколько бревен надо на 1 клетку (меняй здесь) ---
    public static int LogsFor(BuildingType type)
    {
        return type switch
        {
            BuildingType.WorkTable => LogsWorkTable,
            BuildingType.Bed => LogsBed,
            BuildingType.Bench => LogsBench,
            BuildingType.Nightstand => LogsNightstand,
            BuildingType.CentralBlock => LogsCentralBlock,
            // --- Станки отраслей (task-16): не падаем в LogsWall ---
            BuildingType.Sawmill => LogsSawmill,
            BuildingType.Smelter => LogsSmelter,
            BuildingType.Anvil => LogsAnvil,
            BuildingType.Workbench => LogsWorkbench,
            BuildingType.SpinningWheel => LogsSpinningWheel,
            BuildingType.Loom => LogsLoom,
            BuildingType.Mill => LogsMill,
            BuildingType.AssemblyLine => LogsAssemblyLine,
            _ => LogsWall, // WoodWall и неизвестные
        };
    }

    public static int LogsWall = 15;
    public static int LogsWorkTable = 25;
    public static int LogsBed = 20;
    public static int LogsBench = 10;
    public static int LogsNightstand = 10;
    public static int LogsCentralBlock = 10; // §119 P5: центральный блок = 10 дерева

    // --- Станки отраслей (task-16) ---
    // Деревянная часть цены. task-19: доставка ТИПИЗИРОВАНА по JobData.TargetItemId,
    // поэтому камень (StonesFor) тоже физически едет на площадку и списывается
    // потипно (ConstructionSite._delivered/_required).
    public static int LogsSawmill = 30;        // пилорама: много дерева
    public static int LogsSmelter = 20;        // плавильня: печь, основа каменная
    public static int LogsAnvil = 12;          // наковальня: дерево только на станину (12 ≠ LogsWall, чтобы не читалось как «стена» по ошибке)
    public static int LogsWorkbench = 35;      // столярный верстак: почти всё дерево
    public static int LogsSpinningWheel = 18;  // прялка
    public static int LogsLoom = 25;           // ткацкий станок: рама
    public static int LogsMill = 30;           // мельница: корпус
    public static int LogsAssemblyLine = 25;   // сборочная линия: станина + настил

    // --- Камень в цене станков (task-16/19): теперь входит в поставку ---
    public static int StonesFor(BuildingType type)
    {
        return type switch
        {
            BuildingType.Sawmill => StonesSawmill,
            BuildingType.Smelter => StonesSmelter,
            BuildingType.Anvil => StonesAnvil,
            BuildingType.Workbench => StonesWorkbench,
            BuildingType.SpinningWheel => StonesSpinningWheel,
            BuildingType.Loom => StonesLoom,
            BuildingType.Mill => StonesMill,
            BuildingType.AssemblyLine => StonesAssemblyLine,
            _ => 0,
        };
    }

    public static int StonesSawmill = 5;
    public static int StonesSmelter = 40;      // плавильня: каменная печь
    public static int StonesAnvil = 25;        // наковальня: камень/металл
    public static int StonesWorkbench = 0;
    public static int StonesSpinningWheel = 0;
    public static int StonesLoom = 5;
    public static int StonesMill = 20;         // жернова
    public static int StonesAssemblyLine = 15;

    // --- Сколько длится работа (в игровых секундах, 500 = 1 час) ---
    public static float ChopDurationSec = WorldTime.SecondsPerHour * 6f;   // рубка дерева
    public static float MineDurationSec = WorldTime.SecondsPerHour * 4f;   // добыча камня
    public static float BuildDurationSec = WorldTime.SecondsPerHour * 4f;  // стройка стены
    public static float TillDurationSec = WorldTime.SecondsPerHour * 0.25f; // вскопка грядки
    public static float PlantDurationSec = 10f;   // посадка
    public static float HarvestDurationSec = 5f;  // сбор урожая

    // --- Сколько тащит агент ---
    public static float MaxCarryWeight = 25f;       // вес в кг
    public static int MaxCarryLogs => (int)(MaxCarryWeight / ItemRegistry.Get(ItemId.Log).Weight);

    // --- Сколько падает с дерева / камня (от и до) ---
    public static int LogDropMin = 21;
    public static int LogDropMax = 38; // не включая (как Random.Next)
    public static int StoneDropMin = 8;
    public static int StoneDropMax = 16;

    // --- Паузы и расстояния (тоже здесь) ---
    public static float ReachDist = 20f;      // как близко подойти чтобы работать
    public static float StuckTimeoutSec = 3f; // сколько ждать застрявшего
    public static float FailCooldownSec = 4f; // пауза после неудачи (потом +случайно 0..4)

    // --- Гейт 100%: стройка начинается только когда ВСЕ бревна привезены ---
    // True = ждем все ресурсы всей стройки, False = старое поведение (по клеткам).
    public static bool WaitForAllSiteResources = false;
}
