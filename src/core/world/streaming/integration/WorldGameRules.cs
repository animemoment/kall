#nullable enable
using System;

namespace Game.Core.WorldStreaming.Integration;

/// <summary>
/// Правила «основной игры» поверх стримингового мира — фасад, которому будут
/// соответствовать легаси-менеджеры (стены, здания, добыча камня, вырубка леса,
/// ферма, склады) при миграции главного экрана. Никаких правок легаси MapData/
/// MapRenderer/SimulationContext: всё через детерминированные cell-запросы
/// (WorldCellQuery) и мульти-kind метки (IBlockWorld).
/// Все гейты работают только на загруженных чанках (задания выполняются рядом
/// с игроком); вне загруженной области возвращается false.
/// Декор (дерево/камень) спрашивается ФАКТИЧЕСКИ видимый — той же формулой, которой
/// его рисует вид (WorldGenV2.DecorPass: порог поля + хеш-прореживание), а не только
/// по порогу шума: иначе «дерево» есть на каждой клетке рощи, где рендер пуст.
/// Пороговые значения — детерминированные константы для первой интеграции,
/// точные правила легаси уточнятся при подключении сцен основной игры.
/// </summary>
public sealed class WorldGameRules
{
    /// <summary>
    /// Минимум лесного шума (Q16). Оставлен для совместимости публичного API; канон —
    /// <see cref="WorldDecor.ForestMinQ16"/> (раньше здесь был дублирующий литерал 30_000).
    /// Гейты больше НЕ сравнивают один порог: видимость дерева — см. <see cref="GetVisibleDecor"/>.
    /// </summary>
    public const ushort TreeForestMin = WorldDecor.ForestMinQ16;

    /// <summary>Диапазон влажности (Q16) для пахоты: не пересушено и не болото.</summary>
    public const ushort FarmMoistureMin = 28_000;
    public const ushort FarmMoistureMax = 56_000;

    private readonly IBlockWorld _world;
    private readonly ulong _seed;
    private readonly uint _generatorVersion;

    public WorldGameRules(IBlockWorld world, ulong seed, uint generatorVersion)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _seed = seed;
        _generatorVersion = generatorVersion;
    }

    /// <summary>Загружен ли чанк клетки (можно ли здесь выполнять задания/строить).</summary>
    public bool IsLoadedAt(long cellX, long cellY)
        => _world.IsLoaded(WorldCoordinates.ChunkForTile(cellX, cellY));

    /// <summary>Занята ли клетка постоянным объектом (стена/здание/склад/посев) или его планом (§29).</summary>
    public bool HasEntityBlocking(long cellX, long cellY)
    {
        byte[] kinds = WorldDeltaKind.BlockingKinds;
        for (int i = 0; i < kinds.Length; i++)
        {
            if (_world.HasBlock(kinds[i], cellX, cellY))
                return true;
        }
        return false;
    }

    // ---------- task-21 / AUDIT task-17 P4-1: руда ----------

    /// <summary>
    /// Есть ли руда в клетке по «известному» набору поля (<see cref="OreWorldField.TryPeekKnownOreLocal"/>).
    /// Набор переживает эвикцию кэша поля: геймплейный запрет больше не зависит от
    /// расписания рендера. false = руды нет ИЛИ поле ещё не считалось (оба случая
    /// гейты трактуют как «строить можно» — неизвестность не повод блокировать игрока).
    /// </summary>
    public bool HasKnownOreAt(long cellX, long cellY)
        => OreWorldField.TryPeekKnownOreLocal((int)cellX, (int)cellY, out bool ore) && ore;

    /// <summary>Поле руды чанка уже считалось (можно ли доверять ответу).</summary>
    public bool IsOreKnown(long cellX, long cellY)
        => OreWorldField.IsChunkKnownLocal((int)cellX, (int)cellY);

    // ---------- Гейты ----------

    /// <summary>Можно строить здание: загружено, трава, клетка свободна, здания ещё нет
    /// и на клетке НЕТ ИЗВЕСТНОЙ руды (task-21/P4-1: неизвестное поле не запрещает).</summary>
    public bool CanBuildAt(long cellX, long cellY)
        => IsReadyBase(cellX, cellY)
            && !_world.HasBlock(WorldDeltaKind.Building, cellX, cellY)
            && !HasKnownOreAt(cellX, cellY);

    /// <summary>Можно ставить стену: загружено, трава, клетка свободна, стены ещё нет
    /// и на клетке НЕТ ИЗВЕСТНОЙ руды (task-21/P4-1).</summary>
    public bool CanPlaceWallAt(long cellX, long cellY)
        => IsReadyBase(cellX, cellY)
            && !_world.HasBlock(WorldDeltaKind.Wall, cellX, cellY)
            && !HasKnownOreAt(cellX, cellY);

    /// <summary>
    /// Можно добывать камень: загружено, гора ИЛИ видимая россыпь камня (та же формула,
    /// что у рендера), не занято объектом, ещё не добыто и нет плана.
    /// </summary>
    public bool CanMineStoneAt(long cellX, long cellY)
    {
        if (!IsLoadedAt(cellX, cellY))
            return false;

        GeneratedCell cell = GetCell(cellX, cellY);
        // FIX (bughunt 06-1/06-2): камень — фактически видимый (WorldGenV2.DecorPass), а не только
        // порог StoneQ16: иначе метка «добыть» вставала на пустой траве, ресурс из ничего.
        // Исключение осмысленно и сохранено: гору копаем и без хеш-декора россыпи.
        (_, bool hasStone) = GetVisibleDecor(in cell, cellX, cellY);
        bool hasStoneSource = hasStone || cell.Terrain == BaseTerrainKind.Mountain;
        return hasStoneSource
            && !HasEntityBlocking(cellX, cellY)
            && !_world.HasBlock(WorldDeltaKind.MinedStone, cellX, cellY)
            && !_world.HasBlock(WorldDeltaKind.MinedStonePlan, cellX, cellY);
    }

    /// <summary>
    /// Можно вырубить дерево: загружено, на клетке ВИДИМОЕ дерево (порог поля + прореживание,
    /// как у рендера), не вода, не занято, ещё не вырублено и нет плана.
    /// </summary>
    public bool CanCutTreeAt(long cellX, long cellY)
    {
        if (!IsLoadedAt(cellX, cellY))
            return false;

        GeneratedCell cell = GetCell(cellX, cellY);
        // FIX (bughunt 06-1/06-2): сравнивать один ForestQ16 с порогом мало — рендер ещё и прореживает
        // рощу хешем (WorldGenV2.DecorPass). Без этого лесоруб рубил пустоту.
        (bool hasTree, _) = GetVisibleDecor(in cell, cellX, cellY);
        return hasTree
            && cell.Terrain != BaseTerrainKind.Water
            && !HasEntityBlocking(cellX, cellY)
            && !_world.HasBlock(WorldDeltaKind.CutTree, cellX, cellY)
            && !_world.HasBlock(WorldDeltaKind.CutTreePlan, cellX, cellY);
    }

    /// <summary>
    /// Можно засеять: загружено, пригодная ПОЧВА (голая земля), клетка свободна от постоянных
    /// объектов и чертежей. Гейт строгий по поверхности: «трава» как терреин ещё не значит земля —
    /// песок (пустыня/пляж), лёд и клетки под видимым декором (дерево/камень) не годятся,
    /// см. <see cref="IsArableSoil"/>. Прежний зажим влажности (FarmMoistureMin/Max) намеренно
    /// НЕ применяется: он снят как источник «пустот» при разметке (PLAN §56.1.2 / §56.2.1).
    /// </summary>
    public bool CanPlantAt(long cellX, long cellY)
    {
        if (!IsLoadedAt(cellX, cellY))
            return false;

        GeneratedCell cell = GetCell(cellX, cellY);
        return IsArableSoil(in cell, cellX, cellY)
            && !HasEntityBlocking(cellX, cellY)
            && !HasKnownOreAt(cellX, cellY) // task-21: грядку на жилу не ставим
            && !_world.HasBlock(WorldDeltaKind.Crop, cellX, cellY)
            && !_world.HasBlock(WorldDeltaKind.CropPlan, cellX, cellY);
    }

    /// <summary>Можно сложить склад: загружено, трава, клетка свободна, склада ещё нет.</summary>
    public bool CanStockpileAt(long cellX, long cellY)
        => IsReadyBase(cellX, cellY)
            && !HasKnownOreAt(cellX, cellY) // task-21: склад на жилу не ставим
            && !_world.HasBlock(WorldDeltaKind.Stockpile, cellX, cellY)
            && !_world.HasBlock(WorldDeltaKind.StockpilePlan, cellX, cellY);

    // ---------- Действия (гейт + метка) ----------

    public bool TryBuildAt(long cellX, long cellY)
        => CanBuildAt(cellX, cellY) && _world.AddBlock(WorldDeltaKind.Building, cellX, cellY);

    public bool TryPlaceWallAt(long cellX, long cellY)
        => CanPlaceWallAt(cellX, cellY) && _world.AddBlock(WorldDeltaKind.Wall, cellX, cellY);

    public bool TryMineStoneAt(long cellX, long cellY)
        => CanMineStoneAt(cellX, cellY) && _world.AddBlock(WorldDeltaKind.MinedStone, cellX, cellY);

    public bool TryCutTreeAt(long cellX, long cellY)
        => CanCutTreeAt(cellX, cellY) && _world.AddBlock(WorldDeltaKind.CutTree, cellX, cellY);

    public bool TryPlantAt(long cellX, long cellY)
        => CanPlantAt(cellX, cellY) && _world.AddBlock(WorldDeltaKind.Crop, cellX, cellY);

    public bool TryStockpileAt(long cellX, long cellY)
        => CanStockpileAt(cellX, cellY) && _world.AddBlock(WorldDeltaKind.Stockpile, cellX, cellY);

    // ---------- Служебное ----------

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private GeneratedCell GetCell(long cellX, long cellY)
    {
        // F-011 fast-path: чтение из уже загруженного в память чанка за O(1)
        if (_world is IWorldCellSource source && source.TryCellInfo(cellX, cellY, out GeneratedCell residentCell))
            return residentCell;
        return WorldCellQuery.CellAt(_seed, _generatorVersion, cellX, cellY);
    }

    /// <summary>
    /// Фактический ВИДИМЫЙ декор клетки — ровно та формула, которой его рисует
    /// StreamingWorldView.GetDecorIndex: <see cref="WorldGenV2.DecorPass"/> (порог поля +
    /// хеш-прореживание). Раньше гейты сравнивали только порог ForestQ16/StoneQ16, из-за
    /// чего дерево «было» на каждой клетке рощи, а рендер рисовал его на 40–60 %.
    /// cellX/cellY — координаты острова/стриминга (как у рендера), DecorPass ждёт
    /// АБСОЛЮТНЫЕ мировые wx/wy, поэтому прибавляем WorldOrigin — как GetDecorIndex.
    /// </summary>
    private (bool HasTree, bool HasStone) GetVisibleDecor(in GeneratedCell cell, long cellX, long cellY)
    {
        long wx = UnifiedWorldGenerator.WorldOriginX + cellX;
        long wy = UnifiedWorldGenerator.WorldOriginY + cellY;

        // FIX (bughunt 06-1/06-2): нужен SoilFertilityQ16 (порог рощ 55..80), но GeneratedCell
        // плодородия НЕ хранит (поля SoilFertilityQ16/FertilityQ16 нет). Пока передаём 0:
        // для клеток вне рощи это ТОЧНО (DecorPass читает fertility только при
        // ForestQ16 >= WorldDecor.ForestMinQ16), а в самой роще порог будет 55 вместо
        // 55 + fertility*25/65535 — плотность рощ может отличаться от рендера на ±порог.
        // Источник нужно связать позже (общее core-API плодородия, без ссылки core→UI).
        const ushort soilFertilityQ16 = 0;
        return WorldGenV2.DecorPass(wx, wy, in cell, soilFertilityQ16);
    }

    /// <summary>
    /// Пригодная под грядку/посев почва — строго голая ЗЕМЛЯ, а не «трава по террейну».
    /// Единый источник истины с рендером: пропускаются ровно те клетки, которые
    /// StreamingWorldView.GetCompositeGroundIndex рисует землёй (CompositeGrass/CompositeSteppe),
    /// и отсекаются вода/песок/лёд/ФАКТИЧЕСКИ нарисованные дерево/камень. Иначе «поставил
    /// чертёж на землю», а на экране он лежит на песке, льду, дереве или камне.
    /// </summary>
    private bool IsArableSoil(in GeneratedCell cell, long cellX, long cellY)
    {
        if (cell.Terrain != BaseTerrainKind.Grass)
            return false;

        // Песок (пустыня и прибрежный пляж) и лёд рисуются не землёй (см. рендер вида).
        if (cell.Biome == (byte)BiomeType.Desert
            || cell.Biome == (byte)BiomeType.Glacier
            || cell.Biome == (byte)BiomeType.DeepWater)
            return false;

        // Видимый декор: ровно то, что рисует вид (WorldGenV2.DecorPass), а не только порог
        // шума — иначе гейт отказывал в пахоте на ~100 % клеток рощи, где дерева нет (06-1/06-2).
        // Уже вырубленное/добытое (готовая метка) декор не рисует — клетка снова голая земля.
        (bool hasTree, bool hasStone) = GetVisibleDecor(in cell, cellX, cellY);
        if (hasTree && !_world.HasBlock(WorldDeltaKind.CutTree, cellX, cellY))
            return false;

        bool visibleStone = hasStone
            && !_world.HasBlock(WorldDeltaKind.MinedStone, cellX, cellY);
        return !visibleStone;
    }

    /// <summary>Общая база для «строительных» гейтов: загружено, трава, клетка не занята объектом.</summary>
    private bool IsReadyBase(long cellX, long cellY)
        => IsLoadedAt(cellX, cellY)
            && GetCell(cellX, cellY).Terrain == BaseTerrainKind.Grass
            && !HasEntityBlocking(cellX, cellY);
}