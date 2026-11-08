using Game.Core.WorldStreaming;

namespace Game.Core;

/// <summary>
/// Хранит данные сгенерированной карты.
/// Ground — тип поверхности (трава/вода).
/// TreeOnGrass — наличие дерева на клетке (только если Ground == Grass).
/// TreeVariant — вариант спрайта дерева 0..3 (tree/tree_1/tree_2/tree_3).
/// StoneOnGrass — каменная россыпь на клетке (только если Ground == Grass,
/// дерева там нет). Вариант 0..3 считается хешем от координат+сида
/// (UnifiedWorldGenerator.StoneVariantFor) — не храним, детерминирован.
/// Seed — сид, которым карта была сгенерирована (для воспроизводимости/дебага).
/// </summary>
public sealed class MapData
{
    public TileType[,] Ground { get; }
    public bool[,] TreeOnGrass { get; }
    public byte[,] TreeVariant { get; }
    public bool[,] StoneOnGrass { get; }
    /// <summary>Почвенная влажность 0..200. Заливается генератором, живёт тиками.</summary>
    public HumidityMap Humidity { get; }
    /// <summary>Плодородие почвы 0..200. СВОЙ слой шума (сид +4242), живёт тиками.</summary>
    public FertilityMap Fertility { get; }
    /// <summary>
    /// Визуальный биом клетки (<see cref="WorldStreaming.BiomeType"/>), выведенный из
    /// климата мировой карты. ГЕЙМПЛЕЙ определяется только <see cref="Ground"/>
    /// (проходимость/вода/горы) — Biome влияет ИСКЛЮЧИТЕЛЬНО на выбор текстуры,
    /// поэтому симуляция, pathfinder и бесшовная логика его не читают.
    /// </summary>
    public byte[,] Biome { get; }

    /// <summary>
    /// Вариант текстуры внутри биома. Выбирается детерминированно-случайно
    /// (хеш координат+сида), используются ВСЕ варианты
    /// (см. <c>GroundTileAtlas.VariantsPerType</c>).
    /// </summary>
    public byte[,] Variant { get; }

    // ------------------------------------------------------------------ //
    //  Руда в клетке = хранилище с запасом (task-3).                      //
    //  ПЛОСКИЕ массивы row-major: индекс = y*Width + x (конвенция         //
    //  ActiveArea/WorldGenV2). Массивы плоские, а не [,] — горячий путь   //
    //  добычи/рендера читает их O(1) без двумерной индексации.            //
    // ------------------------------------------------------------------ //

    /// <summary>Тип руды: 0 = нет, 1..<see cref="OreConfig.TypeCount"/> — id категории «руда» атласа.</summary>
    public byte[] OreType { get; }

    /// <summary>Кэш состояния жилы (<see cref="Game.Core.OreState"/>), пересчитывается по запасу.</summary>
    public byte[] OreState { get; }

    /// <summary>Текущий запас клетки, единицы.</summary>
    public ushort[] OreReserve { get; }

    /// <summary>Запас при генерации — база порогов состояния rich/normal/poor.</summary>
    public ushort[] OreReserveInitial { get; }

    /// <summary>Климат региона, в котором сгенерирована карта (диагностика/HUD/тесты).</summary>
    public WorldClimateParams Climate { get; set; } = WorldClimateParams.DefaultTemperate;

    public uint Seed { get; set; }
    public int LakeCount { get; set; }
    public int[] LakeSizes { get; set; } = System.Array.Empty<int>();
    public int RiverBranchCount { get; set; }
    public int MainRiverLength { get; set; }
    public float LandConnectivity { get; set; } = 1f;


    public int Width => Ground.GetLength(0);
    public int Height => Ground.GetLength(1);

    public MapData(int width, int height)
    {
        Ground = new TileType[width, height];
        TreeOnGrass = new bool[width, height];
        TreeVariant = new byte[width, height];
        StoneOnGrass = new bool[width, height];
        Biome = new byte[width, height];
        Variant = new byte[width, height];
        Humidity = new HumidityMap(width, height);
        Fertility = new FertilityMap(width, height);
        // Руда: плоские row-major массивы (индекс = y*Width + x). 6 МБ на 1024² —
        // плата за O(1) доступ в горячем пути добычи/рендера.
        int cells = width * height;
        OreType = new byte[cells];
        OreState = new byte[cells];
        OreReserve = new ushort[cells];
        OreReserveInitial = new ushort[cells];
    }

    /// <summary>Плоский индекс руды для клетки (x,y): y*Width + x. Границы НЕ проверяет.</summary>
    public int OreIndex(int x, int y) => y * Width + x;

    /// <summary>Есть ли руда в клетке. Bounds-safe.</summary>
    public bool HasOre(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
            return false;
        return OreType[y * Width + x] != OreConfig.None;
    }

    /// <summary>Тип руды в клетке (0 = нет). Bounds-safe.</summary>
    public byte GetOreType(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
            return OreConfig.None;
        return OreType[y * Width + x];
    }

    /// <summary>Текущий запас руды в клетке (0 = нет/выработано). Bounds-safe.</summary>
    public int GetOreReserve(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
            return 0;
        return OreReserve[y * Width + x];
    }

    /// <summary>Состояние руды в клетке (rich/normal/poor). Bounds-safe.</summary>
    public Game.Core.OreState GetOreState(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
            return Game.Core.OreState.Rich;
        return (Game.Core.OreState)OreState[y * Width + x];
    }

    /// <summary>Визуальный биом клетки. Bounds-safe (вне карты — Plains).</summary>
    public WorldStreaming.BiomeType GetBiome(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
            return WorldStreaming.BiomeType.Plains;
        return (WorldStreaming.BiomeType)Biome[x, y];
    }

    /// <summary>Вариант текстуры клетки. Bounds-safe (вне карты — 0).</summary>
    public int GetGroundVariant(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
            return 0;
        return Variant[x, y];
    }

    /// <summary>
    /// Вариант дерева 0..3 для клетки. Безопасен к выходу за границы.
    /// </summary>
    public int GetTreeVariant(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
            return 0;
        return TreeVariant[x, y] & 3;
    }

    /// <summary>
    /// Есть ли каменная россыпь на клетке. Bounds-safe.
    /// </summary>
    public bool HasStone(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
            return false;
        return StoneOnGrass[x, y];
    }
}