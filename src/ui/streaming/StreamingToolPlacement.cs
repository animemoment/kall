using System;
using Godot;
using Game.Core.WorldStreaming.Integration;
using Game.UI.Tools;

namespace Game.UI.Streaming;

/// <summary>
/// Адаптер «инструменты → бесконечный мир» (PLAN §26 шаг 2, §28): гейты берутся у
/// <see cref="WorldGameRules"/> (вода/гора/декор/уже занято), запись — у
/// <see cref="StreamingWorldManager"/> (персистентные метки, дельтами). Клетки острова сюда
/// не попадают: их обрабатывает островная логика инструмента.
/// §29: инструмент ставит ЧЕРТЁЖ (план), а работу выполняют люди мира и записывают «готово».
/// </summary>
public sealed class StreamingToolPlacement : IToolWorldPlacement
{
	private readonly StreamingWorldManager _manager;
	private readonly WorldGameRules _rules;
	private readonly long _islandWidth;
	private readonly long _islandHeight;

	/// <summary>
	/// §117.2: размеры острова Main передаёт ЯВНО (Main.ColonySizeTiles, 1024×1024) —
	/// это развязывает границы от флипа MapRenderer.MapWidth/Height. Дефолты
	/// (MapWidth/Height) остаются только как fallback для тестов/других вызывающих.
	/// </summary>
	public StreamingToolPlacement(
		StreamingWorldManager manager,
		WorldGameRules rules,
		long islandWidth = MapRenderer.MapWidth,
		long islandHeight = MapRenderer.MapHeight)
	{
		_manager = manager ?? throw new ArgumentNullException(nameof(manager));
		_rules = rules ?? throw new ArgumentNullException(nameof(rules));
		_islandWidth = islandWidth;
		_islandHeight = islandHeight;
	}

	/// <summary>Сколько клеток реально поставлено/снято через адаптер (диагностика).</summary>
	public int PlacedCount { get; private set; }

	public bool IsWorldCell(long cellX, long cellY)
		=> !IsIslandCell(cellX, cellY) && _rules.IsLoadedAt(cellX, cellY);

	public bool CanPlaceStockpile(long cellX, long cellY)
		=> IsWorldCell(cellX, cellY) && _rules.CanStockpileAt(cellX, cellY);

	public bool CanPlaceFarm(long cellX, long cellY)
		=> IsWorldCell(cellX, cellY) && _rules.CanPlantAt(cellX, cellY);

	public bool TryPlaceWall(long cellX, long cellY)
		=> Apply(cellX, cellY, IsWorldCell(cellX, cellY)
			&& _rules.CanPlaceWallAt(cellX, cellY)
			&& _manager.TryPlaceBlock(WorldDeltaKind.WallPlan, cellX, cellY));

	public bool CanPlaceWall(long cellX, long cellY)
		=> IsWorldCell(cellX, cellY) && _rules.CanPlaceWallAt(cellX, cellY);

	/// <summary>
	/// 11-03: снос снимает и чертёж, и готовый вид. Игра ставит `*Plan`, работу доводит
	/// `WorldPlanHandler`, а `WorldJobBoard.Complete` меняет план на готовый вид через
	/// `TryReplaceBlock` (`WallPlan→Wall`). После этого плана в клетке уже нет, и старый
	/// снос молча возвращал false: объект оставался навсегда, а клетка (готовый вид входит
	/// в `WorldDeltaKind.BlockingKinds`) становилась непригодной для строительства.
	/// </summary>
	private bool RemovePlanOrDone(long cellX, long cellY, byte planKind)
		=> IsWorldCell(cellX, cellY)
			&& (_manager.TryRemoveBlock(planKind, cellX, cellY)
				|| _manager.TryRemoveBlock(WorldDeltaKind.DoneFor(planKind), cellX, cellY));

	public bool TryRemoveWall(long cellX, long cellY)
		=> Apply(cellX, cellY, RemovePlanOrDone(cellX, cellY, WorldDeltaKind.WallPlan));

	public bool TryPlaceStockpile(long cellX, long cellY)
		=> Apply(cellX, cellY, CanPlaceStockpile(cellX, cellY)
			&& _manager.TryPlaceBlock(WorldDeltaKind.StockpilePlan, cellX, cellY));

	public bool TryRemoveStockpile(long cellX, long cellY)
		=> Apply(cellX, cellY, RemovePlanOrDone(cellX, cellY, WorldDeltaKind.StockpilePlan));

	public bool TryPlaceFarmPlot(long cellX, long cellY)
		=> Apply(cellX, cellY, CanPlaceFarm(cellX, cellY)
			&& _manager.TryPlaceBlock(WorldDeltaKind.CropPlan, cellX, cellY));

	public bool TryRemoveFarmPlot(long cellX, long cellY)
		=> Apply(cellX, cellY, RemovePlanOrDone(cellX, cellY, WorldDeltaKind.CropPlan));

	public bool TryPlaceBuilding(long cellX, long cellY, Game.Core.BuildingType buildingType)
	{
		// §122: тип здания едет в payload WorldChunkDeltaRecord.Data (один байт).
		// Раньше он терялся здесь, и StreamingWorldView рисовал ProcessOfWork
		// вместо стола/кровати/скамьи.
		if (!CanPlaceBuilding(cellX, cellY))
			return false;
		byte type = (byte)buildingType;
		if (!Enum.IsDefined(typeof(Game.Core.BuildingType), buildingType))
			return false;
		Span<byte> payload = stackalloc byte[1];
		payload[0] = type;
		return Apply(cellX, cellY,
			_manager.TryPlaceBlock(WorldDeltaKind.BuildingPlan, cellX, cellY, payload));
	}

	public bool CanPlaceBuilding(long cellX, long cellY)
		=> IsWorldCell(cellX, cellY) && _rules.CanBuildAt(cellX, cellY);

	public bool TryRemoveBuilding(long cellX, long cellY)
		=> Apply(cellX, cellY, RemovePlanOrDone(cellX, cellY, WorldDeltaKind.BuildingPlan));

	public bool TryCutTree(long cellX, long cellY)
	{
		bool gate = IsWorldCell(cellX, cellY) && _rules.CanCutTreeAt(cellX, cellY);
		bool placed = gate && _manager.TryPlaceBlock(WorldDeltaKind.CutTreePlan, cellX, cellY);
		return Apply(cellX, cellY, placed);
	}

	public bool TryCancelCutTree(long cellX, long cellY)
		=> Apply(cellX, cellY, IsWorldCell(cellX, cellY)
			&& _manager.TryRemoveBlock(WorldDeltaKind.CutTreePlan, cellX, cellY));

	public bool TryMineStone(long cellX, long cellY)
		=> Apply(cellX, cellY, IsWorldCell(cellX, cellY)
			&& _rules.CanMineStoneAt(cellX, cellY)
			&& _manager.TryPlaceBlock(WorldDeltaKind.MinedStonePlan, cellX, cellY));

	public bool TryCancelMineStone(long cellX, long cellY)
		=> Apply(cellX, cellY, IsWorldCell(cellX, cellY)
			&& _manager.TryRemoveBlock(WorldDeltaKind.MinedStonePlan, cellX, cellY));

	/// <summary>Клетка острова колонии: [0, _islandWidth)×[0, _islandHeight).</summary>
	public bool IsIslandCell(long cellX, long cellY)
		=> WorldPlacementPass.IsOnIsland(cellX, cellY, _islandWidth, _islandHeight);

	private bool Apply(long cellX, long cellY, bool applied)
	{
		if (applied)
			PlacedCount++;
		return applied;
	}
}
