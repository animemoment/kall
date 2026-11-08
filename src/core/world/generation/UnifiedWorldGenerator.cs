using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Game.Core;
using Game.Core.WorldStreaming.Integration;

namespace Game.Core.WorldStreaming;

/// <summary>
/// Единый детерминированный генератор мира (UnifiedWorldGenerator).
/// Заменяет собой устаревшие разрозненные генераторы (MapGenerator, WorldChunkGenerator,
/// GpuChunkGenerator, ParallelChunkGenerator, WorldFeatureGenerator).
/// 
/// Генерирует террейн, биомы, растительность, камни и климатические поля для любой точки
/// мира (wx, wy) и любого чанка (cx, cy) напрямую из физической климатической модели планеты
/// (WorldClimateGenerator / BiomeMapper).
/// </summary>
public static class UnifiedWorldGenerator
{
    public const uint FeatureSchemaVersion = 1;
    public const uint DefaultGeneratorVersion = 3;
    public const int StartClearRadius = 14;

    /// <summary>
    /// S-04 (бывший W-04 AUDIT-A): шаг ЕДИНОЙ климатической решётки мира, в клетках.
    /// Узлы климата стоят на абсолютных координатах, кратных этому шагу; по ним
    /// интерполируют и чанк (GenerateChunk), и точечные запросы
    /// (WorldCellQuery.SampleLatticeAbsolute / WorldCellQuery.CellAt). Один шаг —
    /// один источник истины: гейты размещения и рендер видят один и тот же террейн.
    /// </summary>
    public const int ClimateLatticeStep = 16;

    /// <summary>
    /// Глобальное смещение центра мира (WorldOrigin) в мировых клетках.
    /// Позволяет привязать локальную систему координат колонии и стриминга (0..512)
    /// к любому выбранному континентальному региону планеты.
    ///
    /// S-04: origin ОБЯЗАН быть кратен <see cref="ClimateLatticeStep"/>, иначе узлы
    /// решётки чанка (origin + 64*chunk + 16*k) съезжают с абсолютных узлов точечных
    /// запросов (16*k), и для одной клетки появляются два разных террейна — ровно
    /// тот дефект, что закрывает S-04. Инвариант проверяется в SetWorldOrigin.
    /// </summary>
    public static long WorldOriginX { get; private set; } = 0;
    public static long WorldOriginY { get; private set; } = 0;
    public static ulong ActiveWorldSeed { get; set; } = 0;

    public static void SetWorldOrigin(long originX, long originY)
    {
        // S-04: проверка инварианта «origin лежит на решётке климата». Без неё чанк
        // (сетка от origin) и точечный семпл (сетка от абсолютных 16-кратных узлов)
        // расходятся, и гейт «можно ли строить» отвечает не то, что видит игрок.
        if ((originX & (ClimateLatticeStep - 1)) != 0 || (originY & (ClimateLatticeStep - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(originX),
                $"WorldOrigin ({originX},{originY}) не кратен шагу климатической решётки {ClimateLatticeStep}: " +
                "чанк и точечные запросы (гейты/пасфайндинг) начали бы отвечать про разный террейн одной клетки (S-04).");
        }

        WorldOriginX = originX;
        WorldOriginY = originY;
    }

    /// <summary>
    /// Автоматический поиск пригодного континентального региона для спавна колонии.
    /// Выполняется через новый PureWorldMapGenerator (синхронизирован с чанками на 100%).
    /// </summary>
    public static RegionKey FindHabitableSpawn(ulong worldSeed, uint generatorVersion)
    {
        return PureWorldMapGenerator.FindBestSpawn(worldSeed);
    }

    /// <summary>
    /// <summary>
    /// Генерация базового чанка 64x64 по глобальному ключу.
    /// Zero-alloc по отношению к долгоживущим объектам, потокобезопасен.
    /// Макро-климат планеты рассчитывается в 4 углах чанка и интерполируется,
    /// сокращая тяжелые вызовы тектоники/Voronoi на 99.9% и гарантируя 0 швов.
    /// </summary>
    public static WorldChunk GenerateChunk(ulong worldSeed, uint generatorVersion, ChunkKey key)
    {
        // S-05 (бывший W-05 AUDIT-A): origin фиксируется ОДИН раз на вызов.
        // Раньше он читался из мутабельной статики прямо в теле (WorldOriginX/Y), а
        // стример вызывает генерацию из воркеров, считая свой мир неизменяемым
        // (readonly _worldSeed/_generatorVersion). Смена origin посреди активного
        // стриминга (Main: SetWorldOrigin → генерация колонии → ResetWorld) давала
        // чанки одного окна, посчитанные от РАЗНЫХ origin'ов: скачок рельефа и
        // «остров внутри мира» на стыке. Теперь значение берётся один раз и дальше
        // используется только локальная копия.
        return GenerateChunk(worldSeed, generatorVersion, key, WorldOriginX, WorldOriginY);
    }

    /// <summary>
    /// S-05: генерация чанка с ЯВНО заданным origin. Вызывающий фиксирует origin на
    /// момент старта окна и передаёт его сюда — тогда результат не зависит от того,
    /// что успел поменять главный поток. Перегрузка без origin оставлена для
    /// совместимости и снимает значение статики один раз на вызов.
    /// </summary>
    public static WorldChunk GenerateChunk(
        ulong worldSeed, uint generatorVersion, ChunkKey key,
        long originX, long originY)
    {
        return PureWorldGenerator.GenerateWorldChunk(worldSeed, generatorVersion, key, originX, originY);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static ushort LerpQ16(ushort a, ushort b, uint t)
    {
        uint clampedT = Math.Min(t, (uint)65535);
        ulong value = (ulong)((long)a * (65535 - clampedT) + (long)b * clampedT + 32768);
        return (ushort)((value * 0x80008001UL) >> 47);
    }

    /// <summary>
    /// C01-сглаживание веса интерполяции (smoothstep): убирает излом производной
    /// на границах 16-клеточных блоков решётки климата — «угловатый берег каждые
    /// ~16 тайлов» и ступеньки изолиний (отчёт _reports/02_coastline_impl.md:
    /// разрыв производной 181.7 Q16/клетку в среднем, max 550 — при линейном весе;
    /// 0.000 — при smoothstep-весе). Q16 → Q16, монотонный, F(0)=0, F(65535)=65535.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal static uint SmoothStepQ16(uint t)
    {
        const ulong One = 65535ul;
        const ulong OneSq = One * One; // 4 294 836 225
        ulong x = Math.Min(t, (uint)One);
        // C-10: замена 64-битного IDIV (shaped / OneSq) на умножение с обратным множителем (2^64 / OneSq = 4295101697)
        ulong shaped = (x * x) * (3ul * One - 2ul * x) + OneSq / 2;
        return (uint)Math.Min(One, Math.BigMul(shaped, 4295101697UL, out _));
    }

    /// <summary>
    /// Интерполяция 18 климатических полей. Вес — smoothstep (SmoothStepQ16):
    /// узлы решётки 16×16 сшиваются без излома производной, изолиния берега
    /// (ElevationQ16 == SeaLevelQ16) — гладкая кривая, а не ломаная.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal static WorldClimateParams LerpClimate1D(in WorldClimateParams a, in WorldClimateParams b, uint t)
    {
        // 02_coastline_impl §2.1: ЕДИНСТВЕННАЯ строка правки — вес t → ts (smoothstep).
        // Сам Lerp (LerpQ16 по всем 18 полям) не меняется.
        uint ts = SmoothStepQ16(t);
        return new WorldClimateParams(
            ElevationQ16: LerpQ16(a.ElevationQ16, b.ElevationQ16, ts),
            ContinentalnessQ16: LerpQ16(a.ContinentalnessQ16, b.ContinentalnessQ16, ts),
            LatitudeQ16: LerpQ16(a.LatitudeQ16, b.LatitudeQ16, ts),
            TemperatureQ16: LerpQ16(a.TemperatureQ16, b.TemperatureQ16, ts),
            MoistureQ16: LerpQ16(a.MoistureQ16, b.MoistureQ16, ts),
            PrecipitationQ16: LerpQ16(a.PrecipitationQ16, b.PrecipitationQ16, ts),
            DrainageQ16: LerpQ16(a.DrainageQ16, b.DrainageQ16, ts),
            WindHeadingQ16: LerpQ16(a.WindHeadingQ16, b.WindHeadingQ16, ts),
            RainShadowQ16: LerpQ16(a.RainShadowQ16, b.RainShadowQ16, ts),
            TectonicPressureQ16: LerpQ16(a.TectonicPressureQ16, b.TectonicPressureQ16, ts),
            OceanInfluenceQ16: LerpQ16(a.OceanInfluenceQ16, b.OceanInfluenceQ16, ts),
            VegetationDensityQ16: LerpQ16(a.VegetationDensityQ16, b.VegetationDensityQ16, ts),
            SoilFertilityQ16: LerpQ16(a.SoilFertilityQ16, b.SoilFertilityQ16, ts),
            RockExposureQ16: LerpQ16(a.RockExposureQ16, b.RockExposureQ16, ts),
            GlaciationQ16: LerpQ16(a.GlaciationQ16, b.GlaciationQ16, ts),
            AridityQ16: LerpQ16(a.AridityQ16, b.AridityQ16, ts),
            ArchipelagoTendencyQ16: LerpQ16(a.ArchipelagoTendencyQ16, b.ArchipelagoTendencyQ16, ts),
            RiverDensityQ16: LerpQ16(a.RiverDensityQ16, b.RiverDensityQ16, ts));
    }

    /// <summary>
    /// Детерминированный расчет характеристик одной клетки в мировых координатах (wx, wy).
    /// Включает трехслойный каскадный Domain Warping, гипсометрический якорь суши (Coastline Anchoring),
    /// естественные бухты и мысы, песчаные пляжи для причалов и кластерную растительность.
    /// </summary>
    public static GeneratedCell SampleCell(ulong worldSeed, uint generatorVersion, long wx, long wy, WorldClimateParams? overrideClimate = null)
    {
        return PureWorldGenerator.SampleCell(worldSeed, generatorVersion, wx, wy);
    }

    /// <summary>
    /// Генерирует прямоугольное окно колонии (например, 512x512) вокруг стартовой позиции,
    /// заполняя MapData для локальной симуляции рабочих, ферм и рендера.
    /// Использует ту же 16x16 интерполяционную сетку климата, обеспечивая 100% бесшовное
    /// соответствие со стриминговым миром за кромкой острова.
    /// </summary>
    public static MapData GenerateColonyWindow(
        ulong worldSeed,
        uint generatorVersion,
        long originX,
        long originY,
        int width,
        int height,
        WorldClimateParams? explicitClimate = null)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Размеры окна колонии должны быть больше 0.");

        long baseOriginX = WorldOriginX + originX;
        long baseOriginY = WorldOriginY + originY;

        var data = new MapData(width, height)
        {
            Seed = unchecked((uint)worldSeed),
            Climate = explicitClimate ?? WorldClimateGenerator.SampleBlendedCell(
                worldSeed, generatorVersion, baseOriginX + width / 2, baseOriginY + height / 2)
        };

        var heightMap = new float[width, height];
        var moistNoise = new float[width, height];

        const int SubSize = 16;
        const int Q16One = 65535;
        int subDivX = Math.Max(1, width / SubSize);
        int subDivY = Math.Max(1, height / SubSize);

        // Предварительный расчет опорной сетки климата 16x16 (многопоточно через Parallel.For)
        // Исключает плоский разрыв между центром колонии и стримингом
        var grid = new WorldClimateParams[subDivX + 1, subDivY + 1];
        Parallel.For(0, subDivY + 1, gy =>
        {
            long gwy = baseOriginY + gy * SubSize;
            for (int gx = 0; gx <= subDivX; gx++)
            {
                long gwx = baseOriginX + gx * SubSize;
                grid[gx, gy] = explicitClimate ?? WorldClimateGenerator.SampleBlendedCell(worldSeed, generatorVersion, gwx, gwy);
            }
        });

        Parallel.For(0, height, ly =>
        {
            long wy = baseOriginY + ly;
            int by = Math.Min(subDivY - 1, ly / SubSize);
            uint v = (uint)(((long)(ly % SubSize)) * Q16One / SubSize);

            for (int lx = 0; lx < width; lx++)
            {
                long wx = baseOriginX + lx;
                int bx = Math.Min(subDivX - 1, lx / SubSize);
                uint u = (uint)(((long)(lx % SubSize)) * Q16One / SubSize);

                WorldClimateParams nw = grid[bx, by];
                WorldClimateParams ne = grid[bx + 1, by];
                WorldClimateParams swClimate = grid[bx, by + 1];
                WorldClimateParams se = grid[bx + 1, by + 1];

                WorldClimateParams top = LerpClimate1D(in nw, in ne, u);
                WorldClimateParams bot = LerpClimate1D(in swClimate, in se, u);
                WorldClimateParams cellClimate = LerpClimate1D(in top, in bot, v);

                GeneratedCell cell = SampleCell(worldSeed, generatorVersion, wx, wy, cellClimate);

                data.Ground[lx, ly] = cell.Terrain switch
                {
                    BaseTerrainKind.Water => TileType.Water,
                    BaseTerrainKind.Mountain => TileType.Mountain,
                    _ => TileType.Grass
                };

                // Строгий инвариант: деревья и камни могут быть ТОЛЬКО на чистой траве
                bool isGrass = (cell.Terrain == BaseTerrainKind.Grass) && (data.Ground[lx, ly] == TileType.Grass);

                // Естественная плотность рощ: внутри зоны леса (ForestQ16 >= 30000)
                // деревья растут группами с полянками и проходами; на плодородных почвах рощи гуще
                bool inForestGrove = isGrass && (cell.ForestQ16 >= WorldDecor.ForestMinQ16);
                int groveThreshold = 55 + (int)cellClimate.SoilFertilityQ16 * 25 / 65535;
                bool treePass = inForestGrove && (((wx ^ (wy * 3)) & 3) != 0) && (((wx * 17 ^ wy * 31) % 100) < groveThreshold);
                bool hasTree = inForestGrove && treePass;
                data.TreeOnGrass[lx, ly] = hasTree;
                data.TreeVariant[lx, ly] = (byte)TreeVariantFor((int)wx, (int)wy, (uint)worldSeed);

                // Камни: только на свободной сухой траве, не на дереве, не на воде, не на скалах
                // Плотность камней выше у подножия гор и на каменистых почвах
                bool inStoneDeposit = isGrass && !hasTree && (cell.StoneQ16 >= WorldDecor.StoneMinQ16);
                int stoneThreshold = 45 + (int)cell.StoneQ16 * 20 / 65535;
                bool stonePass = inStoneDeposit && (((wx * 7 ^ wy * 13) % 100) < stoneThreshold);
                bool hasStone = inStoneDeposit && stonePass;
                data.StoneOnGrass[lx, ly] = hasStone;

                data.Biome[lx, ly] = cell.Biome;
                data.Variant[lx, ly] = cell.Variant;

                heightMap[lx, ly] = cell.ElevationQ16 / 65535f;
                moistNoise[lx, ly] = cell.MoistureQ16 / 65535f;
            }
        });

        // Очищаем стартовую площадку в центре окна (поляна без деревьев, камней, воды и гор)
        int cx = width / 2;
        int cy = height / 2;
        int r2 = StartClearRadius * StartClearRadius;
        for (int y = Math.Max(0, cy - StartClearRadius); y <= Math.Min(height - 1, cy + StartClearRadius); y++)
        {
            for (int x = Math.Max(0, cx - StartClearRadius); x <= Math.Min(width - 1, cx + StartClearRadius); x++)
            {
                int dx = x - cx;
                int dy = y - cy;
                if (dx * dx + dy * dy <= r2)
                {
                    data.Ground[x, y] = TileType.Grass;
                    data.TreeOnGrass[x, y] = false;
                    data.StoneOnGrass[x, y] = false;
                }
            }
        }

        // Площадка — ПРАВИЛО МИРА, а не пост-обработка окна: регистрируем её в мировом
        // словаре (Game.Core.World.WorldClearing), чтобы дверь WorldModel/решётка/чанки
        // про те же клетки отвечали «трава», как и кэш окна. Иначе кэш острова говорит
        // «трава», а симуляция/стриминг — «вода» (шов 149/262144 у TestIslandWorldAgreement).
        // Через WorldClearing, а не WorldModel, чтобы не завести цикл файлов
        // «генератор → дверь → WorldCellQuery → генератор».
        Game.Core.World.WorldClearing.Register(baseOriginX + cx, baseOriginY + cy, StartClearRadius);

        // Инициализируем динамические слои почвы (влажность и плодородие)
        data.Humidity.Initialize(data.Ground, moistNoise, heightMap);
        data.Fertility.Initialize(data.Ground, moistNoise, heightMap, null, data.Humidity.WaterFeedAt);

        // task-5: рудные месторождения по геологическим правилам (_plan/ORES_GEOLOGY.md).
        // Кладём ПОСЛЕ расчистки и инициализации почвы: пасс читает Ground/декор
        // и климат-сетку grid (в GeneratedCell нет Aridity/RockExposure/
        // RiverDensity/Precipitation — поэтому передаём grid 16×16 явно, §7.1).
        // Абсолютный baseOrigin — детерминизм и бесшовность с миром.
        int oreDeposits = OreDepositGenerator.Generate(
            data, worldSeed, generatorVersion, baseOriginX, baseOriginY, grid, ClimateLatticeStep);

        return data;
    }

    /// <summary>
    /// Детерминированный вариант спрайта камня 0..3 из атласа 2x2.
    /// </summary>
    public static int StoneVariantFor(int x, int y, uint seed)
    {
        unchecked
        {
            uint hb = (uint)(x * 2246822519u) ^ (uint)(y * 3266489917u) ^ (seed * 668265263u);
            hb ^= hb >> 13;
            hb *= 1274126177u;
            hb ^= hb >> 16;
            return (int)(hb % 4u);
        }
    }

    /// <summary>
    /// Атласные координаты квадранта камня 0..3 -> (TX, TY).
    /// </summary>
    public static (int TX, int TY) StoneAtlasFor(int variant)
    {
        return (variant & 3) switch
        {
            1 => (1, 0),
            2 => (0, 1),
            3 => (1, 1),
            _ => (0, 0),
        };
    }

    /// <summary>
    /// Детерминированный вариант дерева 0..3.
    /// </summary>
    public static int TreeVariantFor(int x, int y, uint seed)
    {
        unchecked
        {
            uint hb = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (seed * 83492791u);
            hb ^= hb >> 13;
            hb *= 1274126177u;
            hb ^= hb >> 16;
            return (int)(hb % 4u);
        }
    }
}
