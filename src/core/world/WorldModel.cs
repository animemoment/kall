using System;
using System.Runtime.CompilerServices;
using Game.Core.WorldStreaming;
using Game.Core.WorldStreaming.Integration;
using Game.Core.WorldStreaming.Mutation;

namespace Game.Core.World;

/// <summary>
/// ЕДИНСТВЕННАЯ дверь к миру: «что находится в клетке (x,y)».
///
/// Зачем этот класс существует (читай перед тем, как добавить свой путь):
/// раньше на один и тот же вопрос отвечали РАЗНЫЕ места — островной код брал
/// клетку из MapData, стриминговый из чанка, а симуляция звала генератор напрямую
/// и, что хуже, с ДРУГОЙ версией генератора. Отсюда «рассинхрон» и вечный цикл
/// правок: исправляешь один ответ, остальные продолжают врать.
///
/// Контракт:
///   • seed и version принадлежат МИРУ и живут здесь. Никто не передаёт их сам —
///     иначе два вызова могут уйти в разные вселенные (это уже случалось).
///   • Резидентность — это КЭШ, а не режим. <see cref="IsResident"/> отвечает
///     «чанк уже в памяти?», а не «это остров?». Остров — просто набор
///     закреплённых чанков.
///   • Мутации (срубленное, построенное, выпавшее) НЕ являются частью клетки:
///     они лежат в оверлее и переживают выгрузку чанка. Клетка из генератора —
///     базовая поверхность, мутация — отдельный слой поверх неё.
///
/// Правило для агентов и ИИ: если нужна клетка — зови сюда. Прямой вызов
/// WorldCellQuery/UnifiedWorldGenerator.SampleCell из ui или simulation ловится
/// тестом tests/TestArchitectureGuards.cs.
/// </summary>
public static class WorldModel
{
    private static ulong _seed;
    private static uint _version = UnifiedWorldGenerator.DefaultGeneratorVersion;

    private static volatile IWorldCellSource _resident;
    private static volatile IslandOverrideStore _mutations;

    /// <summary>
    /// Зарегистрировать расчищенную площадку поселения (центр в АБСОЛЮТНЫХ координатах).
    /// Хранилище правила — <see cref="WorldClearing"/> (см. там, почему не поле здесь).
    /// GenerateColonyWindow регистрирует площадку сам в момент расчистки; Main и тесты
    /// могут докинуть свою.
    /// </summary>
    public static void RegisterClearingSite(long x, long y, int radius) => WorldClearing.Register(x, y, radius);

    /// <summary>Попадает ли клетка в расчищенную площадку поселения.</summary>
    public static bool IsClearedSite(long x, long y) => WorldClearing.Contains(x, y);

    public static int ClearingSiteCount => WorldClearing.Count;

    /// <summary>Сид мира. Если мир ещё не сконфигурирован — берём активный сид генератора.</summary>
    public static ulong Seed => _seed != 0 ? _seed : UnifiedWorldGenerator.ActiveWorldSeed;

    /// <summary>Версия генератора мира. ОДНА на всю игру: рендер, симуляция, сохранения.</summary>
    public static uint Version => _version;

    /// <summary>
    /// Задать мир. Вызывается ровно один раз при загрузке/смене мира (Main).
    /// Это единственное место, где сид и версия попадают в игру.
    /// </summary>
    public static void Configure(ulong seed, uint version)
    {
        _seed = seed;
        _version = version;
        WorldClearing.Clear();
    }

    /// <summary>Подключить источник резидентных чанков (кэш). Может быть null — тогда всё генерится.</summary>
    public static void BindResident(IWorldCellSource source) => _resident = source;

    /// <summary>
    /// Массовый путь двери (окно террейна и т.п.): сэмплер прямоугольника с решёткой
    /// климата 16×16, ПОБИТОВО равной GenerateColonyWindow/чанкам. UI больше не зовёт
    /// генератор напрямую (гард bypass_generator_ui), а берёт сэмплер здесь.
    /// originLocalX/Y — локальные координаты (дверь сама прибавит worldOriginX/Y,
    /// переданный как СНЕПШОТ НА МОМЕНТ ЗАПРОСА); gridOptions — кап параллелизма
    /// сетки (анти-голодание ThreadPool), null = без капа.
    /// </summary>
    public static Game.Core.WorldStreaming.Integration.BulkCellSampler BeginBulkSample(
        ulong seed, uint version, long originLocalX, long originLocalY, int width, int height,
        long worldOriginX, long worldOriginY,
        System.Threading.Tasks.ParallelOptions gridOptions)
        => Game.Core.WorldStreaming.Integration.WorldCellQuery.BeginBulkSample(
            seed, version, originLocalX, originLocalY, width, height,
            worldOriginX, worldOriginY, gridOptions);

    /// <summary>Подключить оверлей мутаций. Может быть null (тесты, headless).</summary>
    public static void BindMutations(IslandOverrideStore store) => _mutations = store;

    /// <summary>Сброс к состоянию «мир не задан» — для тестов.</summary>
    public static void Reset()
    {
        _seed = 0;
        _version = UnifiedWorldGenerator.DefaultGeneratorVersion;
        _resident = null;
        _mutations = null;
        WorldClearing.Clear();
    }

    /// <summary>
    /// Чанк с этой клеткой уже в памяти? Это ВОПРОС ПРО КЭШ, а не про «остров».
    /// Если источник не подключён — считаем, что ничего не закэшировано,
    /// и мир отвечает генератором (корректно, просто медленнее).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsResident(long x, long y)
    {
        IWorldCellSource resident = _resident;
        return resident != null && resident.CoversChunk(x, y);
    }

    /// <summary>Остров колонии: единственное место, где записано, где он кончается.</summary>
    public const int IslandTiles = 1 << 10;

    /// <summary>
    /// Внутри ли стартового острова (ЛОКАЛЬНЫЕ координаты). Один ответ на весь проект:
    /// шейдер, стриминг и инструменты спрашивают его, а не каждый своё число.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsInsideIsland(long localX, long localY)
        => (ulong)localX < IslandTiles && (ulong)localY < IslandTiles;

    /// <summary>
    /// Базовая клетка мира (без мутаций). Координаты — АБСОЛЮТНЫЕ мировые.
    ///
    /// Почему это важно: WorldCellQuery.CellAt САМ прибавляет WorldOrigin. В игре
    /// WorldOrigin = origin региона спавна (сотни тысяч). Кто складывал origin сам
    /// и звал WorldCellQuery — спрашивал клетку в ДРУГОМ месте (origin²). Именно так
    /// проверки проходимости у агентов смотрели не туда, где идут.
    /// Здесь координата передаётся в генератор КАК ЕСТЬ, origin не прибавляется.
    /// Для координат острова (0..MapWidth) есть <see cref="CellAtLocal"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static GeneratedCell CellAt(long x, long y)
    {
        GeneratedCell cell;
        IWorldCellSource resident = _resident;
        if (resident != null && resident.CoversChunk(x, y) && resident.TryCellInfo(x, y, out GeneratedCell cached))
            cell = cached;
        else
        {
            // Форма мира = решётка 16×16 (как чанки/окна/остров): одиночная клетка
            // обязана отвечать ТОЧНО так же, иначе у берегов дверь расходилась с
            // рендером регионом (1049/7622 клеток). SampleLatticeAbsolute с кэшем.
            cell = Game.Core.WorldStreaming.Integration.WorldCellQuery.SampleLatticeAbsolute(Seed, Version, x, y);
        }

        // Площадка поселения расчищена: там всегда трава (и ни деревьев, ни камней).
        // Правило применяется К ЛЮБОМУ источнику клетки (и к резидентному чанку тоже):
        // чанки генерятся без знания о площадке, без этого у двери ответ зависел бы от
        // того, закэширован ли чанк (шов в чистом виде).
        if (IsClearedSite(x, y) && cell.Terrain != BaseTerrainKind.Grass)
        {
            cell = cell with
            {
                Terrain = BaseTerrainKind.Grass,
                ForestQ16 = 0,
                StoneQ16 = 0,
            };
        }

        return cell;
    }

    /// <summary>Клетка по ЛОКАЛЬНЫМ координатам острова (0..MapWidth): origin прибавляется здесь.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static GeneratedCell CellAtLocal(long localX, long localY)
        => CellAt(UnifiedWorldGenerator.WorldOriginX + localX, UnifiedWorldGenerator.WorldOriginY + localY);

    /// <summary>
    /// БАЗОВАЯ клетка БЕЗ правил мира (расчистка и пр.) — АБСОЛЮТНЫЕ координаты,
    /// в форме мира (решётка 16×16, как чанки/окна). Нужна диагностике (инспектор
    /// клетки: «дверь=Grass, сырой=Water — правило расчистки применено»),
    /// gameplay ВСЕГДА спрашивает <see cref="CellAt"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static GeneratedCell CellAtRaw(long x, long y)
        => Game.Core.WorldStreaming.Integration.WorldCellQuery.SampleLatticeAbsolute(Seed, Version, x, y);

    /// <summary>Базовая клетка без правил по ЛОКАЛЬНЫМ координатам (для инспектора).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static GeneratedCell CellAtLocalRaw(long localX, long localY)
        => CellAtRaw(UnifiedWorldGenerator.WorldOriginX + localX, UnifiedWorldGenerator.WorldOriginY + localY);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static BaseTerrainKind TerrainAt(long x, long y) => CellAt(x, y).Terrain;

    /// <summary>Террейн по локальным координатам острова.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static BaseTerrainKind TerrainAtLocal(long localX, long localY) => CellAtLocal(localX, localY).Terrain;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsWaterAt(long x, long y) => CellAt(x, y).Terrain == BaseTerrainKind.Water;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsMountainAt(long x, long y) => CellAt(x, y).Terrain == BaseTerrainKind.Mountain;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort MoistureQ16At(long x, long y) => CellAt(x, y).MoistureQ16;

    /// <summary>Влажность по локальным координатам острова.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort MoistureQ16AtLocal(long localX, long localY) => CellAtLocal(localX, localY).MoistureQ16;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort ForestQ16At(long x, long y) => CellAt(x, y).ForestQ16;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort StoneQ16At(long x, long y) => CellAt(x, y).StoneQ16;

    /// <summary>
    /// Есть ли мутация данного вида в клетке (срублено/построено/выпало).
    /// Мутации хранятся отдельно от кэша и переживают выгрузку чанка.
    /// </summary>
    public static bool HasMutation(byte kind, long x, long y)
    {
        IslandOverrideStore store = _mutations;
        return store != null && store.Contains(kind, x, y);
    }
}