using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.Simulation;

public sealed class SimulationContext
{
    public TileType[,] Ground { get; }
    public HumidityMap Humidity { get; }
    /// <summary>Плодородие почвы 0..200. Может быть null (старые вызовы) — рост тогда без множителя.</summary>
    public FertilityMap Fertility { get; }
    /// <summary>Гибридный двухконтурный движок почвы (Sparse Farm Domain + Event-Driven Wild Soil).</summary>
    public Game.Core.Soil.HybridSoilEngine SoilEngine { get; }
    /// <summary>Next-Gen Dual-Domain Pull Job System (Workplaces + Spatial Grid).</summary>
    public Game.Simulation.NextGenJobs.NextGenJobSystem JobsNextGen { get; }
    public bool[,] TreeOnGrass { get; }
    /// <summary>Каменные россыпи (добыча, Mining). Может быть null в старых тестах.</summary>
    public bool[,] StoneOnGrass { get; }
    /// <summary>
    /// Руда в клетке (task-3): плоские row-major массивы (индекс = y*MapWidth+x),
    /// ССЫЛКИ на массивы MapData — копий нет. Могут быть null (старые тесты/сейвы).
    /// </summary>
    public byte[] OreType { get; }
    /// <summary>Состояние руды (<see cref="Game.Core.OreState"/>), производное от запаса.</summary>
    public byte[] OreState { get; }
    /// <summary>Текущий запас руды клетки, единицы.</summary>
    public ushort[] OreReserve { get; }
    /// <summary>Запас при генерации — база порогов rich/normal/poor.</summary>
    public ushort[] OreReserveInitial { get; }
    /// <summary>Есть ли слой руды вообще (false = карта без руды).</summary>
    public bool HasOreLayer => OreType != null;
    public bool[,] SolidWalls { get; }
    public List<(int X, int Y)> WalkableTiles { get; }
    public AgentSpatialGrid SpatialGrid { get; }
    public AgentMovementService Movement { get; }
    public Random Random { get; }
    public int TileSize { get; }
    public int MapWidth { get; }
    public int MapHeight { get; }

    public SimulationContext(
        TileType[,] ground,
        HumidityMap humidity,
        bool[,] treeOnGrass,
        bool[,] solidWalls,
        List<(int X, int Y)> walkableTiles,
        AgentSpatialGrid spatialGrid,
        AgentMovementService movement,
        Random random,
        int tileSize = 64,
        bool[,] stoneOnGrass = null,
        FertilityMap fertility = null,
        byte[] oreType = null,
        byte[] oreState = null,
        ushort[] oreReserve = null,
        ushort[] oreReserveInitial = null,
        Game.Core.Soil.HybridSoilEngine soilEngine = null,
        Game.Simulation.NextGenJobs.NextGenJobSystem jobsNextGen = null)
    {
        Ground = ground ?? throw new ArgumentNullException(nameof(ground));
        Humidity = humidity; // может быть null (старые вызовы) — рост тогда без множителя
        Fertility = fertility; // может быть null — рост тогда без множителя плодородия
        SoilEngine = soilEngine;
        JobsNextGen = jobsNextGen ?? Game.Simulation.NextGenJobs.NextGenJobSystem.Instance;
        TreeOnGrass = treeOnGrass ?? throw new ArgumentNullException(nameof(treeOnGrass));
        StoneOnGrass = stoneOnGrass; // null = карты без камня (старые сейвы/тесты)
        SolidWalls = solidWalls ?? throw new ArgumentNullException(nameof(solidWalls));
        WalkableTiles = walkableTiles ?? throw new ArgumentNullException(nameof(walkableTiles));
        SpatialGrid = spatialGrid ?? throw new ArgumentNullException(nameof(spatialGrid));
        Movement = movement ?? throw new ArgumentNullException(nameof(movement));
        Random = random ?? throw new ArgumentNullException(nameof(random));
        TileSize = tileSize;
        MapWidth = ground.GetLength(0);
        MapHeight = ground.GetLength(1);

        // Руда: слой опционален (старые тесты/сейвы), но если хоть один массив
        // передан — размер должен совпадать с картой (иначе добыча читала бы мусор).
        int oreCells = MapWidth * MapHeight;
        OreType = ValidateOreArray(oreType, oreCells, nameof(oreType));
        OreState = ValidateOreArray(oreState, oreCells, nameof(oreState));
        OreReserve = ValidateOreArray(oreReserve, oreCells, nameof(oreReserve));
        OreReserveInitial = ValidateOreArray(oreReserveInitial, oreCells, nameof(oreReserveInitial));
    }

    /// <summary>Плоский индекс руды клетки (x,y): y*MapWidth+x. Границы НЕ проверяет.</summary>
    public int OreIndex(int x, int y) => y * MapWidth + x;

    private static T[] ValidateOreArray<T>(T[] array, int expectedLength, string name)
    {
        if (array == null)
            return null;
        if (array.Length != expectedLength)
            throw new ArgumentException(
                $"{name}: длина {array.Length} не совпадает с картой {expectedLength} клеток",
                name);
        return array;
    }
}