using Godot;
using Game.Core;
using Game.Core.World;
using Game.Core.WorldStreaming;
using Game.Core.WorldStreaming.Integration;
using Game.Simulation;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Game.UI;

/// <summary>Режим просмотра карты (оверлеи поверх основных тайлов).</summary>
public enum MapMode
{
	Normal,
	Humidity,
	Fertility,
	// task-6: оверлеи MapModeOverlayRenderer (руды/высоты/склад/работы).
	Ore,
	Elevation,
	Stockpile,
	Jobs
}

public partial class MapRenderer : Node2D
{
	public static MapRenderer Instance { get; private set; }

	/// <summary>
	/// Размер играбельного острова колонии в тайлах (§117.2: 512→1024).
	/// Main генерирует окно колонии ColonySizeTiles=1024 и передаёт размер явно;
	/// эти константы обязаны совпадать с ним — иначе ApplyMap отрисует только
	/// квадрант карты, а skip-проверки стриминга (PaintCell/IsOccludedByColony)
	/// будут считать чужую кромку. Точка отката: вернуть 512.
	/// </summary>
	public const int MapWidth = 1024;
	public const int MapHeight = 1024;
	public const int TileSizePx = 64;

	/// <summary>
	/// Размер follow-окна террейна в тайлах (ColorRect _groundSurface + R8-текстура
	/// terrain_map). ДЕКУПЛИРОВАН от MapWidth/MapHeight (§117.2): когда остров колонии
	/// вырастет 512→1024, follow-окно остаётся 512×512 и продолжает следить за камерой —
	/// его размер обусловлен зоной видимости камеры, а не размером острова.
	/// </summary>
	public const int TerrainWindowTiles = 1024;

	/// <summary>Квант сдвига follow-окна в тайлах: 1 чанк стриминга = 64 клетки.</summary>
	private const int TerrainWindowShift = 64;

	/// <summary>Шаг сетки макро-климата для внешних клеток (как GenerateChunk / GenerateColonyWindow).</summary>
	private const int TerrainClimateStep = 16;

	public const int SourceGrass     = 0;
	public const int SourceWater     = 1;
	public const int SourceTree0     = 2;
	public const int SourceWall      = 3;
	public const int SourceTree1     = 4;
	public const int SourceWorkTable = 5;
	public const int SourceGardenBed = 6;
	public const int SourceTree2     = 7;
	public const int SourceTree3     = 8;
	public const int SourceMountain  = 9;
	// Камень-россыпь: 4 отдельных Source (по одному квадранту 64x64 из
	// stone.png 128x128) — как 4 варианта деревьев (SourceTree0..3).
	public const int SourceStone0    = 10;
	public const int SourceStone1    = 11;
	public const int SourceStone2    = 12;
	public const int SourceStone3    = 13;
	// Мебель: одиночные спрайты 64x64 (как WorkTable).
	public const int SourceBed        = 14;
	public const int SourceBench      = 15;
	public const int SourceNightstand = 16;
	public const int SourceSand       = 17;
	// §119 P5: центральный блок (central.png) — одиночный спрайт 64x64.
	public const int SourceCentralBlock = 18;
	// Композитный объединенный атлас декораций (деревья + камни) для батчинга в 1 DrawCall
	public const int SourceObjectComposite = 20;
	// task-16: СТАНКИ отраслей. Каждый — отдельный Source; тайл мультиклеточный
	// (размер из арта, BuildingFootprint), чтобы спрайт и габариты совпадали.
	public const int SourceSawmill      = 21;
	public const int SourceSmelter      = 22;
	public const int SourceAnvil        = 23;
	public const int SourceWorkbench    = 24;
	public const int SourceSpinningWheel = 25;
	public const int SourceLoom         = 26;
	public const int SourceMill         = 27;
	public const int SourceAssemblyLine = 28;
	// Полы (дерево, камень, бетон) и крыши (жилая непрозрачная, промышленная затемняющая)
	public const int SourceFloorWood     = 30;
	public const int SourceFloorStone    = 31;
	public const int SourceFloorConcrete = 32;
	public const int SourceRoofResidential = 33;
	public const int SourceRoofIndustrial  = 34;
	// Ghost-призрак: TileSet _ghostLayer — ДУБЛЬ сценового wall-набора с
	// добавленными одиночными Source (трава/грядка/мебель). Id взяты с запасом
	// от сценовых (там максимум ~16), чтобы не пересекаться с terrain-источниками.
	// Зону рисуем ТОЛЬКО этими константами (НЕ SourceGrass/SourceGardenBed —
	// их номера в ghost-наборе заняты стенами, иначе призрак = стены).
	public const int GhostGrassSource      = 100;
	public const int GhostGardenBedSource  = 101;
	public const int GhostWorkTableSource  = 105;
	public const int GhostStockpileSource  = 106;
	public const int GhostBedSource        = 114;
	public const int GhostBenchSource      = 115;
	public const int GhostNightstandSource = 116;
	// §119 P5: ghost центрального блока.
	public const int GhostCentralBlockSource = 117;
	// task-16: ghost станков (мультиклеточные призраки по габаритам арта).
	public const int GhostSawmillSource       = 118;
	public const int GhostSmelterSource       = 119;
	public const int GhostAnvilSource         = 120;
	public const int GhostWorkbenchSource     = 121;
	public const int GhostSpinningWheelSource = 122;
	public const int GhostLoomSource          = 123;
	public const int GhostMillSource          = 124;
	public const int GhostAssemblyLineSource  = 125;
	public const int GhostFloorWoodSource       = 130;
	public const int GhostFloorStoneSource      = 131;
	public const int GhostFloorConcreteSource   = 132;
	public const int GhostRoofResidentialSource = 133;
	public const int GhostRoofIndustrialSource  = 134;

	/// <summary>
	/// Сид карты. 0 = случайный каждый запуск, иначе фиксированный (для дебага/повторов).
	/// Задаётся в инспекторе Godot у MapRenderer.
	/// </summary>
	[Export] public int SeedOverride = 0;

	/// <summary>Отложить автоматическую генерацию карты (например, в режиме выбора спавна из меню).</summary>
	[Export] public bool DeferAutoGeneration { get; set; } = false;

	public static uint CurrentMapSeed { get; private set; }

	/// <summary>Внешняя карта (выбор спавна игрока на мировой карте). Применяется вместо автогенерации.</summary>
	public MapData ExternalPendingMap 
	{ 
		set 
		{ 
			_hasExternalMap = true;
			Interlocked.Exchange(ref _pendingMapData, value); 
		} 
	}
	
	/// <summary>Применить внешнюю карту синхронно (вызывается из Main.cs после установки ExternalPendingMap).</summary>
	public void StartAsync()
	{
		if (_pendingMapData != null)
			ApplyMap();
	}
	
	private volatile bool _hasExternalMap = false;

	private const string TextureGrass     = "uid://bw85uoku784o5";
	private const string TextureWater     = "uid://cmi8pjecdx35";
	private const string TextureTree0     = "uid://c70p6ktr0vcx6";
	private const string TextureTree1     = "uid://ddkl165hm35rp";
	private const string TextureTree2     = "uid://do5qth6ieq2q4";
	private const string TextureTree3     = "uid://n3xkro0lffbt";
	private const string TextureWoodWall  = "uid://chubmh2ufwgwp";
	private const string TextureWorkTable = "uid://rp2bpb5c7k2y";
	private const string TextureGardenBed = "uid://47god8qachvh"; // <-- Исправлен правильный UID грядки
	private const string TextureMountain = "uid://de7mit41ukuoq";
	private const string TextureProcessOfWork = "uid://qqhtvxqngv4a";
	private const string TextureStone = "uid://du6ur8mvtu3le";
	private const string TextureBed = "uid://0uf8ok6dxhyi";
	private const string TextureBench = "uid://bi667xckuklur";
	private const string TextureNightstand = "uid://ir5jk74j01fg";
	// §119 P5: спрайт центрального блока (uid из спеки, res://ui/assets/textures/objects/central.png).
	private const string TextureCentralBlock = "uid://dsiddo1lt1scb";
	private const string TextureSand = "uid://cmxrvsia7sdqi";

	// task-16: станки отраслей. Арт скопирован в ui/assets/textures/building/,
	// размеры 64..192 × 64..256 px (1×1…3×4 клетки) — габариты в BuildingFootprint.
	// res://-путь, а не uid: новые файлы импортируются редактором (или .import,
	// подготовленным рядом с png), uid ещё не закреплён в сцене.
	private const string TextureSawmill      = "res://ui/assets/textures/building/sawmill.png";
	private const string TextureSmelter      = "res://ui/assets/textures/building/smelter.png";
	private const string TextureAnvil        = "res://ui/assets/textures/building/anvil.png";
	private const string TextureWorkbench    = "res://ui/assets/textures/building/workbench.png";
	private const string TextureSpinningWheel = "res://ui/assets/textures/building/spinning.png";
	private const string TextureLoom         = "res://ui/assets/textures/building/loom.png";
	private const string TextureMill         = "res://ui/assets/textures/building/mill.png";
	private const string TextureAssemblyLine = "res://ui/assets/textures/building/assembly.png";

	private const int WallAtlasColumns = 7;
	private const int WallAtlasRows = 7;
	private const int WallAtlasTilePx = 64;

	private static readonly Vector2I AtlasOrigin = Vector2I.Zero;

	private const string TerrainBlendShaderPath = "res://src/shaders/terrain_blend.gdshader";
	private ShaderMaterial _terrainMaterial;
	private ImageTexture _terrainTypeTexture;
	private ColorRect _groundSurface;
	private float _waterAccumulatedTime;
	private long _currentTerrainOriginX = long.MinValue;
	private long _currentTerrainOriginY = long.MinValue;
	private bool _terrainTextureNeedsUpdate = true;
	private byte[] _terrainBuffer;

	private TileMapLayer _groundLayer;
	private TileMapLayer _mountainLayer;
	private TileMapLayer _farmLayer;
	private TileMapLayer _farmBlueprintLayer;
	private TerrainTileLayer _farmTerrain;
	private TerrainTileLayer _farmBlueprintTerrain;
	private HumidityOverlayRenderer _humidityOverlay;
	private FertilityOverlayRenderer _fertilityOverlay;
	// task-6: оверлей режимов Ore/Elevation/Stockpile/Jobs (одна текстура на карту).
	private MapModeOverlayRenderer _mapModeOverlay;
	private TileMapLayer _stockpileLayer;
	private TileMapLayer _objectLayer;
	// Руда (task-3): отдельный слой, Z над травой/фермой/складом, под деревьями
	// и агентами. Тайл выбирается по нормализованной маске соседей (47 значений),
	// композитные атласы строит OreTileSetBuilder лениво по (ore_type, state).
	private TileMapLayer _oreLayer;
	private OreTileSetBuilder _oreTiles;
	private TileMapLayer _floorLayer;
	private TileMapLayer _wallLayer;
	private TileMapLayer _buildingLayer;
	private TileMapLayer _roofLayer;
	private TileMapLayer _ghostLayer;
	private TileMapLayer _blueprintLayer;

	private DesignationRenderer _designationRenderer;

	private MapData _pendingMapData;
	private WallBuildManager _wallBuildManager;

	// C-08: поколение карты. Очереди батчинга хранят клетки в координатах и слоях
	// текущей карты; при ApplyMap счётчик растёт, и всё, что успело попасть в очередь
	// от прошлого мира (задержка дренажа, ещё живой старый sim-поток), отбрасывается
	// при дренаже, а не рисуется поверх новой карты.
	private int _mapGeneration;

	// П.2: батчинг SetCell/EraseCell. События менеджеров только складывают клетки в очередь,
	// реальный TileMap трогаем раз в кадр ограниченным бюджетом — иначе 1000 завершений
	// за тик = 1000 дорогих SetCell + микрофриз главного потока.
	private readonly struct PendingCell
	{
		public readonly TileMapLayer Layer;
		public readonly Vector2I Pos;
		public readonly int Source;
		public readonly Vector2I Atlas;
		public readonly bool Erase;
		// C-08: поколение карты на момент постановки в очередь.
		public readonly int Generation;
		public PendingCell(TileMapLayer layer, Vector2I pos, int source, Vector2I atlas, bool erase, int generation)
		{
			Layer = layer; Pos = pos; Source = source; Atlas = atlas; Erase = erase; Generation = generation;
		}
	}
	private readonly ConcurrentQueue<PendingCell> _pendingCells = new();

	// C-08: то же поколение для очереди стирания деревьев/камня.
	private readonly struct ChoppedCell
	{
		public readonly Vector2I Pos;
		public readonly int Generation;
		public ChoppedCell(Vector2I pos, int generation) { Pos = pos; Generation = generation; }
	}
	private readonly ConcurrentQueue<ChoppedCell> _choppedTreesQueue = new();

	// C-08: единая точка постановки клетки в очередь — штампует текущим поколением.
	private void EnqueuePending(TileMapLayer layer, Vector2I pos, int source, Vector2I atlas, bool erase)
		=> _pendingCells.Enqueue(new PendingCell(layer, pos, source, atlas, erase, _mapGeneration));

	private const int MaxCellsPerFrame = 2048;

	// R-02: бюджет дренажа очереди клеток задан ВРЕМЕНЕМ, а не числом клеток.
	// 1.5 мс на кадр — цена, которую ещё можно отдать симуляции (было: фиксированные
	// 16384 SetCell = 2–10 мс при непустом бэклоге). PendingFlushHardCapCells — страховка
	// от прыгнувших/замороженных часов, не больше прежнего худшего случая за кадр.
	private const double PendingFlushBudgetMs = 1.5;
	private const int PendingFlushHardCapCells = 4096;
	// Маска проверки часов: раз в 64 применённые клетки (2^k-1).
	private const int PendingFlushClockMask = 63;
	// R-05: счётчик кадров для редкой проверки «в слое призрака есть клетки».
	private const int GhostVisibilityCheckFrames = 4;
	private int _ghostVisibilityFrame;
	private bool _blueprintRefreshQueued;
	private long _lastBlueprintRefreshMs;
	// Стандарт слоёв: движковый автотайлинг через TerrainTileLayer
	// (SetCellsTerrainConnect нельзя звать из фоновых потоков — копим грязные
	// клетки и пересчитываем одним вызовом на слой в _Process).
	private TileMapLayer _wallBlueprintLayer;
	private TerrainTileLayer _builtWalls;
	private TerrainTileLayer _wallBlueprints;
	private TerrainTileLayer _mountains;
	private const int WallTerrainSetId = 0;
	private const int WallTerrainId = 0;
	private const int MountainTerrainSetId = 0;
	private const int MountainTerrainId = 0;

	// Прогресс ломки/стройки: отдельный слой поверх всего (атлас ProcessOfWork
	// 3×2 = 6 стадий). Источник данных — WorkProgressTracker (хендлеры пишут
	// стадии, здесь DrainDirty в _Process). Сцену не трогаем — TileSet строим
	// кодом из uid://qqhtvxqngv4a.
	public const int SourceWorkProgress = 200;
	private TileMapLayer _workProgressLayer;
	private readonly List<(int X, int Y, int Stage)> _progressDrain = new(256);
	private readonly List<(int X, int Y)> _progressStale = new(128);
	private float _progressSweepTimer;
	// FIX (bughunt 14-2): пока слой прогресса скрыт LOD'ом, дельты стадий
	// нельзя выбрасывать — DrainDirty снимает клетку из _queued, и после
	// приближения иконка навсегда залипала на устаревшей стадии. Копим
	// последнюю стадию по клетке здесь (-1 = стереть при показе слоя).
	private readonly Dictionary<Vector2I, int> _progressHiddenStages = new(128);
	// Предел буфера скрытого слоя: живых работ на карте заведомо меньше.
	// Переполнение = аномальная сессия ⇒ не копим память, а помечаем, что
	// при следующем показе слой надо перерисовать целиком.
	private const int ProgressHiddenStagesLimit = 4096;
	private bool _progressFullRedrawNeeded;

	private Action<(int X, int Y)> _onZoneTileAdded;
	private Action<(int X, int Y)> _onZoneTileRemoved;
	private Action<(int X, int Y)[]> _onZoneTilesBatchAdded;
	private Action<(int X, int Y)[]> _onZoneTilesBatchRemoved;

	// FIX (bughunt 14-3): подписки на ConstructionManager были анонимными
	// лямбдами — снять их в _ExitTree было невозможно, менеджер держал
	// освобождённый MapRenderer в invocation list и звал его после смены сцены.
	private Action<List<ConstructionSite>> _onSitesBatchAdded;
	private Action<ConstructionSite> _onSiteAdded;
	private Action<(int X, int Y)> _onSiteRemoved;
	private Action<List<(int X, int Y)>> _onSitesBatchRemoved;
	private Action<ConstructionSite> _onSiteCompleted;

	private Action<(int X, int Y), BuildingType> _onBuildingPlaced;
	private Action<(int X, int Y)> _onBuildingRemoved;
	private Action<StructureInstance> _onStructureAdded;
	private Action<(int X, int Y), StructureDefinition> _onStructureRemoved;

	private Action<(int X, int Y)> _onPlotMarked;
	private Action<List<(int X, int Y)>> _onPlotsBatchMarked;
	private Action<(int X, int Y)> _onPlotUnmarked;
	private Action<List<(int X, int Y)>> _onPlotsBatchUnmarked;
	private Action<(int X, int Y)> _onPlotCompleted;

	private MapData _currentMapData;
	public MapData MapData => _currentMapData ?? _pendingMapData;
	public HumidityMap Humidity => (_currentMapData ?? _pendingMapData)?.Humidity;
	public FertilityMap Fertility => (_currentMapData ?? _pendingMapData)?.Fertility;
	public MapMode CurrentMapMode { get; private set; } = MapMode.Normal;
	public event Action<MapMode> OnMapModeChanged;
	public WallBuildManager WallBuildManager => _wallBuildManager;
	public TileMapLayer GhostLayer => _ghostLayer;
	public TileMapLayer WallLayer => _wallLayer;
	public TileMapLayer FloorLayer => _floorLayer;
	public TileMapLayer RoofLayer => _roofLayer;
	/// <summary>Слой руды (task-3) — для инструментов/диагностики, только чтение.</summary>
	public TileMapLayer OreLayer => _oreLayer;
	public ShadowCasterRenderer ShadowRenderer { get; private set; }
	public StockpileItemRenderer StockpileItems { get; private set; }

	// --- Диагностика шва для SeamInspector (src/tools): только чтение ---

	/// <summary>Любой тайл островных слоёв в этой клетке — диагностика шва.</summary>
	public bool HasAnyTileAt(int x, int y)
	{
		var pos = new Vector2I(x, y);
		return HasSourceId(_groundLayer, pos)
			|| HasSourceId(_mountainLayer, pos)
			|| HasSourceId(_farmLayer, pos)
			|| HasSourceId(_oreLayer, pos)
			|| HasSourceId(_objectLayer, pos)
			|| HasSourceId(_wallLayer, pos)
			|| HasSourceId(_buildingLayer, pos)
			|| HasSourceId(_blueprintLayer, pos)
			|| HasSourceId(_wallBlueprintLayer, pos);
	}

	private static bool HasSourceId(TileMapLayer layer, Vector2I pos)
		=> layer != null && layer.GetCellSourceId(pos) != -1;

	/// <summary>Текущий левый-верхний угол follow-окна террейна (диагностика шва).</summary>
	public Vector2I TerrainWindowOrigin
		=> new(_currentTerrainOriginX == long.MinValue ? 0 : (int)_currentTerrainOriginX,
			   _currentTerrainOriginY == long.MinValue ? 0 : (int)_currentTerrainOriginY);

	/// <summary>Что нарисовано в клетке острова во всех слоях — диагностика шва.</summary>
	public string DescribeCellVisual(int x, int y)
	{
		var pos = new Vector2I(x, y);
		var sb = new System.Text.StringBuilder(256);
		sb.Append("MapRenderer: cell=(").Append(x).Append(',').Append(y).Append(")");
		AppendLayer(sb, "ground", _groundLayer, pos);
		AppendLayer(sb, "mountain", _mountainLayer, pos);
		AppendLayer(sb, "farm", _farmLayer, pos);
		AppendLayer(sb, "ore", _oreLayer, pos);
		AppendLayer(sb, "object", _objectLayer, pos);
		AppendLayer(sb, "wall", _wallLayer, pos);
		AppendLayer(sb, "building", _buildingLayer, pos);
		AppendLayer(sb, "blueprint", _blueprintLayer, pos);
		return sb.Append('\n').ToString();
	}

	/// <summary>Компактная строка диагностики клетки (для полосы/сводки).</summary>
	public string CompactCellVisual(int x, int y)
	{
		var pos = new Vector2I(x, y);
		return HasAnyTileAt(x, y) ? $"map:(src={CompactSources(pos)})" : "map:-";

		string CompactSources(Vector2I p)
		{
			var parts = new System.Collections.Generic.List<string>(4);
			void Add(string name, TileMapLayer layer)
			{
				int src = layer != null ? layer.GetCellSourceId(p) : -1;
				if (src == -1) return;
				parts.Add(name + "=" + src + ":" + layer.GetCellAtlasCoords(p));
			}
			Add("g", _groundLayer);
			Add("w", _wallLayer);
			Add("b", _buildingLayer);
			return string.Join(",", parts);
		}
	}

	private static void AppendLayer(System.Text.StringBuilder sb, string name, TileMapLayer layer, Vector2I pos)
	{
		if (layer == null) return;
		int src = layer.GetCellSourceId(pos);
		if (src == -1) return;
		sb.Append(' ').Append(name).Append('=').Append(src).Append(':').Append(layer.GetCellAtlasCoords(pos));
	}

	public event Action OnMapApplied;

	public override void _Ready()
	{
		Instance = this;

		_groundSurface = new ColorRect
		{
			Name = "GroundSurface",
			Color = new Color(0f, 0f, 0f),
			// Размер = follow-окно террейна (НЕ остров): TerrainWindowTiles, чтобы
			// рост MapWidth 512→1024 (§117.2) не растягивал камера-следящий ColorRect.
			Size = new Vector2(TerrainWindowTiles * TileSizePx, TerrainWindowTiles * TileSizePx),
			Position = Vector2.Zero,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		AddChild(_groundSurface);

		_groundLayer = new TileMapLayer { Name = "GroundLayer", RenderingQuadrantSize = 128 };
		Shader terrainShader = null;
		try
		{
			terrainShader = GD.Load<Shader>(TerrainBlendShaderPath);
		}
		catch (Exception)
		{
			// Fallback на стандартный рендер плиток без шейдера
		}
		if (terrainShader != null)
		{
			_terrainMaterial = new ShaderMaterial { Shader = terrainShader };
			var grassTex = LoadTexture(TextureGrass, "grass");
			var sandTex = LoadTexture(TextureSand, "sand");
			var waterTex = LoadTexture(TextureWater, "water");
			if (grassTex != null) _terrainMaterial.SetShaderParameter("tex_grass", grassTex);
			if (sandTex != null) _terrainMaterial.SetShaderParameter("tex_sand", sandTex);
			if (waterTex != null) _terrainMaterial.SetShaderParameter("tex_water", waterTex);
			_terrainMaterial.SetShaderParameter("map_size", new Vector2(TerrainWindowTiles, TerrainWindowTiles));
			_groundSurface.Material = _terrainMaterial;
			_groundSurface.Visible = true;
			_groundLayer.Visible = false;
		}
		AddChild(_groundLayer);

		_mountainLayer = new TileMapLayer { Name = "MountainLayer", RenderingQuadrantSize = 128 };
		AddChild(_mountainLayer);

		_farmLayer = new TileMapLayer { Name = "FarmLayer", RenderingQuadrantSize = 128 };
		AddChild(_farmLayer);

		_farmBlueprintLayer = new TileMapLayer
		{
			Name = "FarmBlueprintLayer",
			Modulate = new Color(1f, 1f, 1f, 0.5f),
			RenderingQuadrantSize = 128
		};
		AddChild(_farmBlueprintLayer);

		// Оверлей влажности: один TextureRect 512×512 поверх земли,
		// текстуру готовит HumidityOverlayRenderer. Скрыт по умолчанию.
		_humidityOverlay = new HumidityOverlayRenderer { Name = "HumidityOverlay" };
		AddChild(_humidityOverlay);
		_humidityOverlay.Visible = false;

		// Оверлей плодородия: тот же приём (один TextureRect поверх земли).
		// Режимы взаимоисключающие — виден только один слой за раз.
		_fertilityOverlay = new FertilityOverlayRenderer { Name = "FertilityOverlay" };
		AddChild(_fertilityOverlay);
		_fertilityOverlay.Visible = false;

		_stockpileLayer = new TileMapLayer
		{
			Name = "StockpileLayer",
			Modulate = new Color(0.65f, 0.15f, 0.9f, 0.45f),
			RenderingQuadrantSize = 128
		};
		AddChild(_stockpileLayer);

		// Руда: child до ObjectLayer — рисуется над землёй/фермой/складом и под
		// деревьями/камнями/агентами. TileSet наполняется в ApplyMap.
		_oreLayer = new TileMapLayer { Name = "OreLayer", RenderingQuadrantSize = 128 };
		AddChild(_oreLayer);

		// task-6: оверлей РЕЖИМОВ КАРТЫ (месторождения/высоты/склад/работы).
		// Одна RGBA-текстура на размер карты (NEAREST), прозрачная вне данных.
		// Создаётся ПОСЛЕ складского/рудного слоёв (перекрывает их цветом режима),
		// но ДО деревьев/камней/зданий/агентов — мир остаётся читаемым.
		_mapModeOverlay = new MapModeOverlayRenderer { Name = "MapModeOverlay" };
		AddChild(_mapModeOverlay);
		_mapModeOverlay.Visible = false;
		// Клетки складских зон берём из этого слоя (у StockpileManager нет списка зон).
		_mapModeOverlay.BindStockpileLayer(_stockpileLayer);

		_objectLayer = new TileMapLayer { Name = "ObjectLayer", RenderingQuadrantSize = 128 };
		AddChild(_objectLayer);

		// Слой полов (дерево, камень, бетон): над землей, под мебелью, станками и стенами
		_floorLayer = new TileMapLayer { Name = "FloorLayer", RenderingQuadrantSize = 128 };
		AddChild(_floorLayer);

		_buildingLayer = new TileMapLayer { Name = "BuildingLayer", RenderingQuadrantSize = 128 };
		AddChild(_buildingLayer);

		_blueprintLayer = new TileMapLayer
		{
			Name = "BlueprintLayer",
			Modulate = new Color(1f, 1f, 1f, 0.5f),
			RenderingQuadrantSize = 128
		};
		AddChild(_blueprintLayer);

		// Чертежи стен: отдельный полупрозрачный terrain-слой на том же
		// сценовом наборе — движок сам считает состыковку чертёж↔чертёж
		// через SetCellsTerrainConnect. Мебель/грядки остаются в _blueprintLayer.
		_wallBlueprintLayer = new TileMapLayer
		{
			Name = "WallBlueprintLayer",
			Modulate = new Color(1f, 1f, 1f, 0.5f),
			RenderingQuadrantSize = 128
		};
		AddChild(_wallBlueprintLayer);

		_wallLayer = new TileMapLayer { Name = "WoodWallLayer", RenderingQuadrantSize = 128 };
		AddChild(_wallLayer);

		// Слой непрозрачных крыш жилых зданий: ZIndex = 8 (перекрывает стены, интерьер, кровати и спящих жителей)
		_roofLayer = new TileMapLayer { Name = "RoofLayer", RenderingQuadrantSize = 128, ZIndex = 8 };
		AddChild(_roofLayer);

		_ghostLayer = new TileMapLayer
		{
			Name = "WallGhostLayer",
			Modulate = new Color(1f, 1f, 1f, 0.4f),
			RenderingQuadrantSize = 128
		};
		AddChild(_ghostLayer);

		_designationRenderer = new DesignationRenderer { Name = "DesignationRenderer" };
		AddChild(_designationRenderer);

		// Слой прогресса работ (ProcessOfWork 3×2): поверх стен/чертежей,
		// полупрозрачный. TileSet — кодовый (сцену не трогаем).
		_workProgressLayer = new TileMapLayer
		{
			Name = "WorkProgressLayer",
			Modulate = new Color(1f, 1f, 1f, 0.85f),
			RenderingQuadrantSize = 128
		};
		AddChild(_workProgressLayer);

		ShadowRenderer = new ShadowCasterRenderer { Name = "ShadowCasterRenderer" };
		AddChild(ShadowRenderer);

		// Иконки содержимого склада (StockpileManager.SnapshotQueue).
		// Canonical-точка для предметных рендереров — scenes/main/Main.cs (_Ready, рядом
		// с GroundItemRenderer), но scenes/ по правилу src-only не трогаем — поэтому
		// StockpileItemRenderer добавляется здесь, ребёнком MapRenderer (ZIndex 6 внутри
		// всё равно выше StockpileLayer с Z по умолчанию).
		var stockpileItemRenderer = new StockpileItemRenderer { Name = "StockpileItemRenderer" };
		AddChild(stockpileItemRenderer);
		StockpileItems = stockpileItemRenderer;
		// R-01: канал подсветки склада при наведении. Отдельный Node2D, а не Modulate
		// слоя: склад — это набор клеток, подсвечивать надо только клетку под курсором.
		var stockpileHover = new StockpileHoverRenderer { Name = "StockpileHoverRenderer" };
		AddChild(stockpileHover);
		// task-18: контур/заливка/подпись РАДИУСНЫХ зон-отраслей (§119 центр+радиус).
		// Отдельный Node2D (ZIndex 8: выше земли, ниже агентов) — как FarmZoneRenderer,
		// но геометрия — окружность по радиусу, а не клетки. Добавляем здесь, а не в
		// Main.cs: scenes/ по правилу src-only не трогаем (тот же приём, что у
		// StockpileItemRenderer выше).
		var industryZoneRenderer = new IndustryZoneRenderer { Name = "IndustryZoneRenderer" };
		AddChild(industryZoneRenderer);
		var serviceRadiusOverlay = new ServiceRadiusOverlayRenderer { Name = "ServiceRadiusOverlay" };
		AddChild(serviceRadiusOverlay);
		TreeJobManager.Instance.OnTreeChopped += OnTreeChopped;
		// Подписка строго один раз: ApplyMap() вызывается при каждой загрузке карты,
		// и подписка внутри него накапливала N обработчиков (каждый добытый камень
		// обрабатывался N раз) и удерживала освобождённый MapRenderer в invocation list.
		StoneJobManager.Instance.OnStoneMined += OnStoneMined;
		// task-4: запас/состояние жилы изменились или она выработана — перерисовать
		// клетку и 8 соседей (маска зависима от соседей). События уже отложенные.
		OreJobManager.Instance.OnOreChanged += OnOreCellChanged;
		OreJobManager.Instance.OnOreDepleted += OnOreCellChanged;

		_onZoneTileAdded = pos => EnqueuePending(_stockpileLayer, new Vector2I(pos.X, pos.Y), SourceGrass, AtlasOrigin, false);
		_onZoneTileRemoved = pos => EnqueuePending(_stockpileLayer, new Vector2I(pos.X, pos.Y), 0, AtlasOrigin, true);
		_onZoneTilesBatchAdded = arr => { foreach (var pos in arr) EnqueuePending(_stockpileLayer, new Vector2I(pos.X, pos.Y), SourceGrass, AtlasOrigin, false); };
		_onZoneTilesBatchRemoved = arr => { foreach (var pos in arr) EnqueuePending(_stockpileLayer, new Vector2I(pos.X, pos.Y), 0, AtlasOrigin, true); };
		StockpileManager.Instance.OnZoneTileAdded += _onZoneTileAdded;
		StockpileManager.Instance.OnZoneTileRemoved += _onZoneTileRemoved;
		StockpileManager.Instance.OnZoneTilesBatchAdded += _onZoneTilesBatchAdded;
		StockpileManager.Instance.OnZoneTilesBatchRemoved += _onZoneTilesBatchRemoved;

		_onSitesBatchAdded = sites =>
		{
			foreach (var s in sites)
			{
				if (s.Definition.IsWall)
					_wallBlueprints?.MarkDirty(new Vector2I(s.X, s.Y));
				else
					EnqueuePending(_blueprintLayer, new Vector2I(s.X, s.Y), SourceForBuilding(s.Definition.Type), AtlasOrigin, false);
			}
		};
		_onSiteAdded = s =>
		{
			if (s.Definition.IsWall)
				_wallBlueprints?.MarkDirty(new Vector2I(s.X, s.Y));
			else
				EnqueuePending(_blueprintLayer, new Vector2I(s.X, s.Y), SourceForBuilding(s.Definition.Type), AtlasOrigin, false);
		};
		_onSiteRemoved = pos => MarkWallBlueprintRemoved(pos.X, pos.Y);
		_onSitesBatchRemoved = list =>
		{
			foreach (var pos in list)
				MarkWallBlueprintRemoved(pos.X, pos.Y);
		};
		_onSiteCompleted = s =>
        {
            if (s.Definition.IsWall)
            {
                _wallBlueprints?.MarkDirty(new Vector2I(s.X, s.Y));
            }
            else
            {
                // task-16: у не-стен (мебель/станки) чертёж рисовался в
                // _blueprintLayer той же текстурой. После постройки стираем его,
                // иначе 50%-призрак чертежа навсегда лежал поверх готового
                // станка (building-слой ниже blueprint-слоя). Для мультиклеточного
                // тайла EraseCell по origin убирает весь тайл.
                EnqueuePending(_blueprintLayer, new Vector2I(s.X, s.Y), 0, AtlasOrigin, true);
            }
        };

		// FIX (bughunt 14-3): подписки держим в полях и снимаем в _ExitTree. Раньше
		// здесь были анонимные лямбды: снять их было нечем, поэтому каждый заход в игру
		// навсегда оставлял живой MapRenderer (и 512 КБ terrain-буферов) в делегатах
		// ConstructionManager, а его _pendingCells рос без дренажа.
		ConstructionManager.Instance.OnSitesBatchAdded += _onSitesBatchAdded;
		ConstructionManager.Instance.OnSiteAdded += _onSiteAdded;
		ConstructionManager.Instance.OnSiteRemoved += _onSiteRemoved;
		ConstructionManager.Instance.OnSitesBatchRemoved += _onSitesBatchRemoved;
		ConstructionManager.Instance.OnSiteCompleted += _onSiteCompleted;

		_onBuildingPlaced = (pos, type) =>
		{
			int sourceId = SourceForBuilding(type);
			EnqueuePending(_buildingLayer, new Vector2I(pos.X, pos.Y), sourceId, AtlasOrigin, false);
			ShadowRenderer?.AddCaster(pos.X, pos.Y);
		};
		_onBuildingRemoved = pos =>
		{
			EnqueuePending(_buildingLayer, new Vector2I(pos.X, pos.Y), 0, AtlasOrigin, true);
			ShadowRenderer?.RemoveCaster(pos.X, pos.Y);
		};
		BuildingManager.Instance.OnBuildingPlaced += _onBuildingPlaced;
		BuildingManager.Instance.OnBuildingRemoved += _onBuildingRemoved;

		_onStructureAdded = inst =>
		{
			if (inst?.Definition == null) return;
			var pos = new Vector2I(inst.X, inst.Y);
			var def = inst.Definition;
			if (def.IsFloor)
			{
				int sourceId = SourceForBuilding(def.Type);
				EnqueuePending(_floorLayer, pos, sourceId, AtlasOrigin, false);
			}
			else if (def.IsRoof)
			{
				EnqueuePending(_roofLayer, pos, SourceRoofResidential, AtlasOrigin, false);
				ShadowRenderer?.AddCaster(inst.X, inst.Y);
			}
			else if (!def.IsWall)
			{
				int sourceId = SourceForBuilding(def.Type);
				EnqueuePending(_buildingLayer, pos, sourceId, AtlasOrigin, false);
				if (def.CastsShadow)
					ShadowRenderer?.AddCaster(inst.X, inst.Y);
			}
		};

		_onStructureRemoved = (pos, def) =>
		{
			var vpos = new Vector2I(pos.X, pos.Y);
			if (def != null && def.IsFloor)
			{
				EnqueuePending(_floorLayer, vpos, 0, AtlasOrigin, true);
			}
			else if (def != null && def.IsRoof)
			{
				EnqueuePending(_roofLayer, vpos, 0, AtlasOrigin, true);
				ShadowRenderer?.RemoveCaster(pos.X, pos.Y);
			}
			else if (def != null && !def.IsWall)
			{
				EnqueuePending(_buildingLayer, vpos, 0, AtlasOrigin, true);
				ShadowRenderer?.RemoveCaster(pos.X, pos.Y);
			}
		};
		StructureManager.Instance.OnStructureAdded += _onStructureAdded;
		StructureManager.Instance.OnStructureRemoved += _onStructureRemoved;

		_onPlotMarked = pos => _farmBlueprintTerrain?.MarkDirty(new Vector2I(pos.X, pos.Y));
		_onPlotsBatchMarked = list =>
		{
			foreach (var pos in list)
				_farmBlueprintTerrain?.MarkDirty(new Vector2I(pos.X, pos.Y));
		};
		_onPlotUnmarked = pos =>
		{
			_farmBlueprintTerrain?.MarkDirty(new Vector2I(pos.X, pos.Y));
			_farmTerrain?.MarkDirty(new Vector2I(pos.X, pos.Y));
		};
		_onPlotsBatchUnmarked = list =>
		{
			foreach (var pos in list)
			{
				_farmBlueprintTerrain?.MarkDirty(new Vector2I(pos.X, pos.Y));
				_farmTerrain?.MarkDirty(new Vector2I(pos.X, pos.Y));
			}
		};
		_onPlotCompleted = pos =>
		{
			_farmBlueprintTerrain?.MarkDirty(new Vector2I(pos.X, pos.Y));
			_farmTerrain?.MarkDirty(new Vector2I(pos.X, pos.Y));
		};

		FarmJobManager.Instance.OnPlotMarked += _onPlotMarked;
		FarmJobManager.Instance.OnPlotsBatchMarked += _onPlotsBatchMarked;
		FarmJobManager.Instance.OnPlotUnmarked += _onPlotUnmarked;
		FarmJobManager.Instance.OnPlotsBatchUnmarked += _onPlotsBatchUnmarked;
		FarmJobManager.Instance.OnPlotCompleted += _onPlotCompleted;

		// Если карта уже подана извне или отложена автогенерация — пропускаем
		if (_pendingMapData != null || _hasExternalMap || DeferAutoGeneration) return;

		Task.Run(() =>
		{
			uint seed = SeedOverride != 0
				? unchecked((uint)SeedOverride)
				: unchecked((uint)Random.Shared.Next(int.MinValue, int.MaxValue));
			CurrentMapSeed = seed;

			// Единая генерация через UnifiedWorldGenerator
			_pendingMapData = UnifiedWorldGenerator.GenerateColonyWindow(seed, UnifiedWorldGenerator.DefaultGeneratorVersion, 0, 0, MapWidth, MapHeight);

			int grass = 0, water = 0, mountains = 0, trees = 0, stones = 0;
			for (int x = 0; x < MapWidth; x++)
				for (int y = 0; y < MapHeight; y++)
				{
					if (_pendingMapData.Ground[x, y] == TileType.Grass) grass++;
					else if (_pendingMapData.Ground[x, y] == TileType.Mountain) mountains++;
					else water++;
					if (_pendingMapData.TreeOnGrass[x, y]) trees++;
					if (_pendingMapData.StoneOnGrass[x, y]) stones++;
				}
			GD.Print($"MapRenderer: карта сгенерирована, seed={seed} (SeedOverride={SeedOverride}), суша={grass}, вода={water}, горы={mountains}, деревья={trees}, камни={stones}, озёр={_pendingMapData.LakeCount}[{string.Join(",", _pendingMapData.LakeSizes)}], река={_pendingMapData.MainRiverLength}+{_pendingMapData.RiverBranchCount}пр, связность={_pendingMapData.LandConnectivity:P1}");

			Callable.From(ApplyMap).CallDeferred();
		});
	}

	public override void _Process(double delta)
	{
		if (!_choppedTreesQueue.IsEmpty && _objectLayer != null)
		{
			int budget = MaxCellsPerFrame;
			while (budget-- > 0 && _choppedTreesQueue.TryDequeue(out var cell))
			{
				// C-08: клетка прошлого мира не должна стирать тайл нового.
				if (cell.Generation != Volatile.Read(ref _mapGeneration))
					continue;
				_objectLayer.EraseCell(cell.Pos);
			}
		}
		// R-02: дренаж очереди клеток — отдельным методом с бюджетом ПО ВРЕМЕНИ.
		// Раньше здесь стоял фиксированный счётчик: при массовой разметке (склад/ферма
		// до 250k клеток) каждый кадр гарантированно съедал 16384 SetCell, а SetCell
		// инвалидирует квадрант TileMapLayer и перестраивает его меш ⇒ спайк 2–10 мс
		// ровно на клике назначения работы.
		FlushPendingCells();
		// Дебаунс 200мс: при массовой стройке (1000 чертежей батчем) полный
		// Clear+SetCell всех чертежей каждый кадр давал десятки мс фриза.
		// Флаг лишь коалесцирует события, гейт — по wall-clock.
		if (_blueprintRefreshQueued
			&& (System.Environment.TickCount64 - _lastBlueprintRefreshMs) >= 200)
		{
			_blueprintRefreshQueued = false;
			_lastBlueprintRefreshMs = System.Environment.TickCount64;
			RefreshAllBlueprints();
		}
		// Оверлей влажности обновляется сам по таймеру (троттлинг внутри Refresh):
		// тик влажности раз в 30с игрового, текстуру чаще дёргать незачем.
		if (CurrentMapMode == MapMode.Humidity && _humidityOverlay != null)
			_humidityOverlay.RefreshThrottled(Humidity, delta);
		else if (CurrentMapMode == MapMode.Fertility && _fertilityOverlay != null)
			_fertilityOverlay.RefreshThrottled(Fertility, delta);
		// task-6: руды/высоты/склад/работы — оверлей обновляет себя по таймеру и
		// событиям данных (не в кадре: реально перестраивается не чаще 0.35–1с).
		else if (_mapModeOverlay != null && MapModeOverlayRenderer.Handles(CurrentMapMode))
			_mapModeOverlay.RefreshThrottled(delta);
		// Стандарт: terrain-слои флашатся через TerrainTileLayer.
		// WallBuildManager копит NotifyChanged дебаунсом 200мс — сливаем хвост
		// здесь же (главный поток, _Process), иначе пачка стен висела бы в
		// _pending до следующей одиночной стены.
		_wallBuildManager?.FlushPending();
		// Terrain-флаш обоих слоёв стен: построенные — по IsWallAt, чертежи —
		// только WoodWall-чертежи (мебель в этот слой не попадает).
		_builtWalls?.Flush(c => StructureManager.Instance.IsWallAt(c.X, c.Y));
		_wallBlueprints?.Flush(c => ConstructionManager.Instance.IsWallSiteAt(c.X, c.Y));
		_farmTerrain?.Flush(c => FarmJobManager.Instance.IsGardenBed(c.X, c.Y));
		_farmBlueprintTerrain?.Flush(c => FarmJobManager.Instance.IsPlotMarked(c.X, c.Y));
		// R-05: слой призрака создавался видимым, и Visible больше никто не ставил —
		// при незачищенных клетках (инструмент отменили/отпустили) призрак продолжал
		// светиться. Прячем слой, когда в нём нет клеток. У TileMapLayer нет счётчика
		// клеток без аллокации (GetUsedCells создаёт движковый массив), поэтому
		// проверяем раз в 4 кадра (~66 мс): появление призрака запаздывает незаметно,
		// зато в _Process нет аллокации на каждый кадр.
		if (_ghostLayer != null && (++_ghostVisibilityFrame % GhostVisibilityCheckFrames) == 0)
		{
			bool hasGhost = _ghostLayer.GetUsedCells().Count > 0;
			if (_ghostLayer.Visible != hasGhost)
				_ghostLayer.Visible = hasGhost;
		}
		// Macro-LOD: при сильном отдалении (Zoom < 0.35x) скрываем микро-слои и чертежи (ST-17: минус до 400 Draw Calls)
		var cam = GetViewport()?.GetCamera2D();
		float zoom = cam?.Zoom.X ?? 1f;
		bool showDetail = zoom >= 0.35f;

		if (_workProgressLayer != null && _workProgressLayer.Visible != showDetail)
			_workProgressLayer.Visible = showDetail;

		if (_farmBlueprintLayer != null && _farmBlueprintLayer.Visible != showDetail)
			_farmBlueprintLayer.Visible = showDetail;

		if (_wallBlueprintLayer != null && _wallBlueprintLayer.Visible != showDetail)
			_wallBlueprintLayer.Visible = showDetail;

		if (_blueprintLayer != null && _blueprintLayer.Visible != showDetail)
			_blueprintLayer.Visible = showDetail;

		if (ShadowRenderer != null && ShadowRenderer.Visible != showDetail)
			ShadowRenderer.Visible = showDetail;

		FlushWorkProgress(delta);

		if (_terrainMaterial != null)
		{
			float speed = TimeManager.Instance != null ? TimeManager.Instance.SpeedMultiplier : 1f;
			float rate = speed <= 0f ? 0f : Mathf.Pow(speed, 0.45f);
			_waterAccumulatedTime += (float)delta * rate;
			_terrainMaterial.SetShaderParameter("water_time", _waterAccumulatedTime);

			if (_groundSurface != null && cam != null)
			{
				long camTileX = (long)MathF.Floor(cam.Position.X / TileSizePx);
				long camTileY = (long)MathF.Floor(cam.Position.Y / TileSizePx);
				// Сдвиг окна с квантованием 64 клетки (1 чанк стриминга = 4096 px).
				// Центрируем окно СВОИМ размером TerrainWindowTiles (§117.2): рост острова
				// MapWidth 512→1024 поведению follow-окна ничего не меняет.
				long targetOriginX = WorldCoordinates.FloorDiv(camTileX - TerrainWindowTiles / 2, TerrainWindowShift) * TerrainWindowShift;
				long targetOriginY = WorldCoordinates.FloorDiv(camTileY - TerrainWindowTiles / 2, TerrainWindowShift) * TerrainWindowShift;

				// Применяем готовый фоновый буфер, если фоновый таск завершился.
				// Устаревший буфер (камера уже уехала / была инвалидация) НЕ применяем:
				// раньше он применялся — окно прыгало назад и пересчитывалось целиком.
				if (_pendingReadyBuffer != null)
				{
					byte[] readyBuf = _pendingReadyBuffer;
					_pendingReadyBuffer = null;
					if (_pendingReadyGen == _terrainWindowGen)
					{
						ApplyTerrainBuffer(_pendingReadyOriginX, _pendingReadyOriginY, readyBuf);
						_terrainWindowValidGen = _pendingReadyGen;
					}
				}

				if (targetOriginX != _currentTerrainOriginX || targetOriginY != _currentTerrainOriginY || _terrainTextureNeedsUpdate)
				{
					RequestTerrainWindowUpdate(targetOriginX, targetOriginY);
				}
			}
		}
	}

	/// <summary>
	/// R-02: применение очереди клеток порцией по ВРЕМЕНИ (PendingFlushBudgetMs) вместо
	/// фиксированных 16384 SetCell за кадр. Остаток остаётся в ConcurrentQueue (инвариант
	/// «пишут события, читает главный поток» сохранён) и доезжает следующими кадрами:
	/// картинка та же, пика «клик назначения работы» нет.
	/// </summary>
	private void FlushPendingCells()
	{
		// Пустая очередь — быстрый выход: не читаем часы и не запускаем цикл каждый кадр.
		if (_pendingCells.IsEmpty) return;

		// Бюджет в тиках Stopwatch считается один раз на кадр — деления в горячем пути нет.
		long budgetTicks = (long)(PendingFlushBudgetMs * System.Diagnostics.Stopwatch.Frequency / 1000);
		long start = System.Diagnostics.Stopwatch.GetTimestamp();
		int applied = 0;

		while (_pendingCells.TryDequeue(out var pc))
		{
			if (pc.Layer != null)
			{
				if (pc.Erase) pc.Layer.EraseCell(pc.Pos);
				else pc.Layer.SetCell(pc.Pos, pc.Source, pc.Atlas);
			}

			// Клетка применена ДО проверки бюджета: иначе при кадре короче бюджета
			// очередь не рассосалась бы никогда. Потолок — страховка от прыгнувших часов.
			if (++applied >= PendingFlushHardCapCells)
				break;

			// Часы читаем раз в пачку клеток (маска), а не на каждую клетку.
			if ((applied & PendingFlushClockMask) == 0
				&& System.Diagnostics.Stopwatch.GetTimestamp() - start >= budgetTicks)
				break;
		}
	}

	public void InvalidateTerrainTexture()
	{
		_terrainTextureNeedsUpdate = true;
		// Новое поколение контента окна: клетки текущего окна больше нельзя
		// переиспользовать инкрементально (сид/WorldOrigin/карта изменились) —
		// следующий запрос пересчитает окно целиком (forceFull).
		_terrainWindowGen++;
	}

	// ================= F2 (§117.1): follow-окно террейна =================
	// Землю всего мира (остров + бесконечный стриминг) рисует _groundSurface
	// (ColorRect + terrain_blend.gdshader): окно TerrainWindowTiles² следует за
	// камерой с квантованием 64 клетки. Байты окна (0=Grass, 1=Sand, 2=Water)
	// заполняются тремя путями:
	//  1) окно ЦЕЛИКОМ внутри острова → синхронно из mapData (~0.3 мс);
	//  2) внешние клетки → сетка макро-климата шагом 16 (как GenerateChunk:
	//     5×5 узлов на чанк 64², 33×33 на окно 512²) + билинейный Lerp по u,v
	//     внутри 16×16 подоблока + UnifiedWorldGenerator.SampleCell(overrideClimate):
	//     полный WorldClimateGenerator.SampleBlendedCell (тектоника/Voronoi) зовётся
	//     ТОЛЬКО в узлах сетки — 1089 раз вместо 262144 (сотни раз дешевле, F2);
	//     решётка сетки совпадает с чанковой (origin окна кратен 64, WorldOrigin
	//     кратен 512) → 0 швов со стриминговыми чанками;
	//  3) сдвиг окна на 64 клетки → инкрементально: переиспользуем уже посчитанные
	//     клетки (сдвиг буфера front→back), пересчитываем только новую полосу 64×512.

	private volatile bool _terrainUpdateInProgress = false;
	private byte[] _backTerrainBuffer;
	private volatile byte[] _pendingReadyBuffer;
	private long _pendingReadyOriginX;
	private long _pendingReadyOriginY;
	private long _requestedOriginX;
	private long _requestedOriginY;
	private int _pendingReadyGen;
	/// <summary>Поколение контента окна: +1 на каждую инвалидацию (сид/WorldOrigin/карта) — клетки разных поколений инкрементально не переиспользуются.</summary>
	private int _terrainWindowGen;
	/// <summary>Поколение, которому соответствует _terrainBuffer (front-зеркало применённого окна).</summary>
	private int _terrainWindowValidGen = -1;
	/// <summary>Переиспользуемый Image окна: аллокация один раз, дальше SetData (анти-GC, lead F-3).</summary>
	private Image _terrainImage;

	/// <summary>
	/// Кап параллелизма заливки окна (lead): Parallel.For по умолчанию берёт все ядра
	/// и конкурирует с воркерами WorldChunkStreamer и симуляцией → ThreadPool
	/// голодание → хитчи главного потока.
	/// </summary>
	private static readonly int TerrainFillMaxDop = Math.Max(2, System.Environment.ProcessorCount / 4);
	private static readonly ParallelOptions TerrainFillParallelOptions = new ParallelOptions
	{
		MaxDegreeOfParallelism = TerrainFillMaxDop
	};

	private void RequestTerrainWindowUpdate(long originX, long originY)
	{
		_requestedOriginX = originX;
		_requestedOriginY = originY;
		bool forceFull = _terrainTextureNeedsUpdate;
		_terrainTextureNeedsUpdate = false;
		int gen = _terrainWindowGen;

		MapData mapData = _pendingMapData ?? _currentMapData;

		// Быстрый путь: окно ЦЕЛИКОМ внутри острова колонии — заполняем мгновенно
		// синхронно из mapData (~0.3 мс) без запуска фонового потока. Обобщение
		// прежнего условия origin==(0,0): окно может быть МЕНЬШЕ острова (MapWidth→1024,
		// §117.2) и лежать с любым origin внутри [0..mapData.Width-окно] — границы
		// острова берём из фактического mapData, не из констант.
		if (mapData != null
			&& originX >= 0 && originY >= 0
			&& originX + TerrainWindowTiles <= mapData.Width
			&& originY + TerrainWindowTiles <= mapData.Height)
		{
			_terrainBuffer ??= new byte[TerrainWindowTiles * TerrainWindowTiles];
			FillIslandWindowFromMapData(_terrainBuffer, mapData, originX, originY);
			ApplyTerrainBuffer(originX, originY, _terrainBuffer);
			_terrainWindowValidGen = gen;
			return;
		}

		if (_terrainUpdateInProgress)
		{
			// Фоновый таск ещё считает прошлое окно — запрос не теряем: возвращаем
			// флаг, _Process следующим кадром перезапросит уже актуальный origin.
			if (forceFull)
				_terrainTextureNeedsUpdate = true;
			return;
		}
		_terrainUpdateInProgress = true;

		_backTerrainBuffer ??= new byte[TerrainWindowTiles * TerrainWindowTiles];

		// Инкрементальность (F2): при сдвиге окна переиспользуем уже посчитанные
		// клетки — сдвиг буфера front→back ВЫПОЛНЯЕТСЯ НА ГЛАВНОМ ПОТОКЕ (фоновый
		// таск фронт не трогает — гонок нет), пересчёт только новой полосы.
		int compX0 = 0, compX1 = 0, compY0 = 0, compY1 = 0;
		bool canReuse = !forceFull
			&& _terrainBuffer != null
			&& _terrainWindowValidGen == gen
			&& _currentTerrainOriginX != long.MinValue
			&& Math.Abs(originX - _currentTerrainOriginX) < TerrainWindowTiles
			&& Math.Abs(originY - _currentTerrainOriginY) < TerrainWindowTiles;
		if (canReuse)
		{
			int dx = (int)(originX - _currentTerrainOriginX);
			int dy = (int)(originY - _currentTerrainOriginY);
			// Вычисляемые полосы = клетки вне пересечения старого и нового окон.
			if (dx > 0) { compX0 = TerrainWindowTiles - dx; compX1 = TerrainWindowTiles; }
			else if (dx < 0) { compX0 = 0; compX1 = -dx; }
			if (dy > 0) { compY0 = TerrainWindowTiles - dy; compY1 = TerrainWindowTiles; }
			else if (dy < 0) { compY0 = 0; compY1 = -dy; }

			// Сдвиг буфера: пересечение окон front → back (BlockCopy по строкам,
			// ~0.1 мс на 512² — дешевле пересчёта в ~1000 раз).
			int xRe0 = Math.Max(0, -dx);
			int xRe1 = Math.Min(TerrainWindowTiles, TerrainWindowTiles - dx);
			int yRe0 = Math.Max(0, -dy);
			int yRe1 = Math.Min(TerrainWindowTiles, TerrainWindowTiles - dy);
			for (int y = yRe0; y < yRe1; y++)
			{
				Buffer.BlockCopy(_terrainBuffer, (y + dy) * TerrainWindowTiles + (xRe0 + dx),
					_backTerrainBuffer, y * TerrainWindowTiles + xRe0, xRe1 - xRe0);
			}
		}
		else
		{
			// Полное заполнение: первое окно / инвалидация (сид/WorldOrigin/карта) /
			// прыжок камеры дальше размера окна.
			compX0 = 0;
			compX1 = TerrainWindowTiles;
		}

		long reqX = originX;
		long reqY = originY;
		// ДВЕРЬ (RENDER_SYNC_PLAN 1.1/1.3): сид и версия окна — из WorldModel, а не из
		// статусов генератора. Регрессия 2026-10-24 «за кромкой океан вместо суши»:
		// заливка брала DefaultGeneratorVersion, а игра — WorldGeneratorVersion из Main;
		// когда они разъезжаются, окно рисует ДРУГОЙ мир. WorldModel не сконфигурирован —
		// прозрачно падает в ActiveWorldSeed/DefaultGeneratorVersion (прежнее поведение).
		ulong seed = WorldModel.Seed;
		uint ver = WorldModel.Version;
		long worldOriginX = UnifiedWorldGenerator.WorldOriginX;
		long worldOriginY = UnifiedWorldGenerator.WorldOriginY;
		int cx0 = compX0, cx1 = compX1, cy0 = compY0, cy1 = compY1;
		MapData md = mapData;

		Task.Run(() =>
		{
			try
			{
				FillTerrainWindowExternal(_backTerrainBuffer, TerrainWindowTiles,
					seed, ver, reqX, reqY, worldOriginX, worldOriginY,
					cx0, cx1, cy0, cy1, md);

				_pendingReadyGen = gen;
				_pendingReadyOriginX = reqX;
				_pendingReadyOriginY = reqY;
				_pendingReadyBuffer = _backTerrainBuffer;
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[MapRenderer] Ошибка фоновой генерации террейна: {ex.Message}");
			}
			finally
			{
				_terrainUpdateInProgress = false;
			}
		});
	}

	/// <summary>Быстрый путь: окно целиком внутри острова — байты напрямую из mapData.</summary>
	private static void FillIslandWindowFromMapData(byte[] buffer, MapData mapData, long originX, long originY)
	{
		for (int y = 0; y < TerrainWindowTiles; y++)
		{
			int rowOffset = y * TerrainWindowTiles;
			int my = (int)(originY + y);
			for (int x = 0; x < TerrainWindowTiles; x++)
			{
				buffer[rowOffset + x] = TerrainByteFromMapData(mapData, (int)(originX + x), my);
			}
		}
	}

	// ------------------------------------------------------------------------------------
	// Коды поверхности terrain_map (RENDER_SYNC_PLAN, Фаза 2: «один рендер земли»)
	// ------------------------------------------------------------------------------------

	/// <summary>
	/// Коды поверхности для R8-текстуры terrain_map (читает terrain_blend.gdshader):
	/// 0 трава, 1 песок, 2 вода, 3 степь, 4 ледник, 5 гора. ЕДИНАЯ таблица острова
	/// и стриминга: раньше кодов было три (трава/песок/вода), степь/ледник/горы
	/// сваливались в траву — шейдер знал меньше типов, чем тайлы, и земля за кромкой
	/// нельзя было рисовать им одним (отсюда тайлы стриминга и шов на 1024).
	/// </summary>
	public const byte SurfaceGrass = 0;
	public const byte SurfaceSand = 1;
	public const byte SurfaceWater = 2;
	public const byte SurfaceSteppe = 3;
	public const byte SurfaceGlacier = 4;
	public const byte SurfaceMountain = 5;

	/// <summary>
	/// Код поверхности клетки: (BaseTerrainKind, Biome) → байт terrain_map.
	/// Контракт (tests/TestTerrainSurfaceCode.cs): вода == 2, гора == 5, степь == 3,
	/// ледник == 4, песок == 1, трава == 0 — шесть РАЗЛИЧИМЫХ кодов, никакого
	/// схлопывания в траву. Заполнение окна шейдера — только этими кодами.
	/// </summary>
	public static byte SurfaceCode(BaseTerrainKind terrain, byte biome)
	{
		if (terrain == BaseTerrainKind.Water)
			return SurfaceWater;
		if (terrain == BaseTerrainKind.Mountain)
			return SurfaceMountain;
		switch ((BiomeType)biome)
		{
			case BiomeType.Desert: return SurfaceSand;
			case BiomeType.Steppe: return SurfaceSteppe;
			case BiomeType.Glacier: return SurfaceGlacier;
			default: return SurfaceGrass;
		}
	}

	/// <summary>
	/// Байт типа поверхности из острова — та же таблица SurfaceCode (6 кодов), что и
	/// для внешних клеток: TileType → BaseTerrainKind, биом — из mapData. Раньше гора
	/// писалась как трава (3-кодовая таблица) — за кромкой горы рисовались водой.
	/// </summary>
	private static byte TerrainByteFromMapData(MapData mapData, int x, int y)
	{
		BaseTerrainKind terrain = mapData.Ground[x, y] switch
		{
			TileType.Water => BaseTerrainKind.Water,
			TileType.Mountain => BaseTerrainKind.Mountain,
			_ => BaseTerrainKind.Grass
		};
		return SurfaceCode(terrain, (byte)mapData.GetBiome(x, y));
	}

	/// <summary>
	/// Ядро F2-фикса: заполнение вычисляемой области follow-окна террейна.
	/// Внешние клетки (вне острова) — сетка макро-климата шагом TerrainClimateStep
	/// с билинейной интерполяцией Lerp по u,v внутри 16×16 подоблока +
	/// UnifiedWorldGenerator.SampleCell(overrideClimate) — в точности схема
	/// GenerateChunk (строки 173-199) / GenerateColonyWindow: полный
	/// WorldClimateGenerator.SampleBlendedCell зовётся только в узлах сетки
	/// (33×33 на полное окно против 262144 полных семплов у старого пути),
	/// а совпадение решётки с чанковой гарантирует 0 швов со стримингом.
	/// Клетки острова — быстрый путь из mapData. buffer уже содержит
	/// переиспользуемые (сдвинутые) клетки: заполняются только полосы
	/// x∈[compX0..compX1) и y∈[compY0..compY1); обе пустые = no-op
	/// (окно уже посчитано и просто сдвинуто). internal — для headless-бенчмарка
	/// tests/terrain_window_bench.cs (та же сборка).
	/// </summary>
	internal static void FillTerrainWindowExternal(
		byte[] buffer, int windowSize,
		ulong worldSeed, uint generatorVersion,
		long originLocalX, long originLocalY,
		long worldOriginX, long worldOriginY,
		int compX0, int compX1, int compY0, int compY1,
		MapData mapData)
	{
		bool hasX = compX1 > compX0;
		bool hasY = compY1 > compY0;
		if (!hasX && !hasY)
			return; // сдвиг (0,0): всё окно уже посчитано (сдвиг буфера)

		int mapW = mapData?.Width ?? 0;
		int mapH = mapData?.Height ?? 0;

		// ДВЕРЬ (RENDER_SYNC_PLAN 1.1): окно террейна больше не зовёт генератор
		// напрямую (арх-гард bypass_generator_ui) и НЕ держит побитовую копию
		// LerpClimate — двойная арифметика уже разъезжалась (в генераторе вес стал
		// smoothstep, здесь оставался линейный — дверь и окно спорили на переходных
		// клетках берега). Сэмплер решётки 16×16 побитово равен GenerateColonyWindow
		// и чанкам; worldOrigin — ЯВНЫЙ снапшот вызывающего (как у WorldModel).
		// Замечание о стоимости: сетка узлов и раньше считалась на полное окно
		// (горизонтальная полоса нуждается во всей ширине сетки и наоборот), так что
		// сэмплер не добавляет узлов относительно прежнего кода.
		var sampler = WorldModel.BeginBulkSample(
			worldSeed, generatorVersion, originLocalX, originLocalY, windowSize, windowSize,
			worldOriginX, worldOriginY, gridOptions: null);

		Parallel.For(0, windowSize, TerrainFillParallelOptions, y =>
		{
			// Строка полосы compY считает всю ширину; остальные строки — только столбцы compX.
			bool fullRow = (uint)(y - compY0) < (uint)(compY1 - compY0);
			int xStart = fullRow ? 0 : compX0;
			int xEnd = fullRow ? windowSize : compX1;
			if (xEnd <= xStart)
				return;

			int rowOffset = y * windowSize;
			long cy = originLocalY + y;

			for (int x = xStart; x < xEnd; x++)
			{
				long cx = originLocalX + x;
				byte type;
				if (mapData != null && (uint)cx < (uint)mapW && (uint)cy < (uint)mapH)
				{
					// Клетка острова: быстрый путь из mapData (тот же SurfaceCode).
					type = TerrainByteFromMapData(mapData, (int)cx, (int)cy);
				}
				else
				{
					// Внешняя клетка: решётка двери — побитово та же форма, что у
					// GenerateChunk/GenerateColonyWindow, без второй арифметики.
					GeneratedCell cell = sampler.CellAtLocal(x, y);
					type = SurfaceCode(cell.Terrain, cell.Biome);
				}
				buffer[rowOffset + x] = type;
			}
		});
	}

	private void ApplyTerrainBuffer(long originX, long originY, byte[] buffer)
	{
		_currentTerrainOriginX = originX;
		_currentTerrainOriginY = originY;

		// Front-зеркало: _terrainBuffer всегда содержит применённое окно — источник
		// клеток для инкрементального сдвига при следующем запросе. Заполняется и
		// быстрым путём (он пишет прямо в _terrainBuffer), и фоновым (копия из back).
		if (!ReferenceEquals(buffer, _terrainBuffer))
		{
			_terrainBuffer ??= new byte[TerrainWindowTiles * TerrainWindowTiles];
			Buffer.BlockCopy(buffer, 0, _terrainBuffer, 0, TerrainWindowTiles * TerrainWindowTiles);
		}

		// Image переиспользуется (анти-GC, lead F-3): аллокация только на первом
		// применении окна, дальше SetData в тот же объект + Update текстуры (R8).
		if (_terrainImage == null)
		{
			_terrainImage = Image.CreateFromData(TerrainWindowTiles, TerrainWindowTiles, false, Image.Format.R8, _terrainBuffer);
			_terrainTypeTexture = ImageTexture.CreateFromImage(_terrainImage);
			_terrainMaterial?.SetShaderParameter("terrain_map", _terrainTypeTexture);
		}
		else
		{
			_terrainImage.SetData(TerrainWindowTiles, TerrainWindowTiles, false, Image.Format.R8, _terrainBuffer);
			_terrainTypeTexture.Update(_terrainImage);
		}

		if (_groundSurface != null)
		{
			// Позиция согласована с Size окна (TerrainWindowTiles — фиксируется в _Ready
			// той же константой): ColorRect покрывает [origin, origin+окно) тайлов.
			_groundSurface.Position = new Vector2(originX * TileSizePx, originY * TileSizePx);
		}
		_terrainMaterial?.SetShaderParameter("map_origin", new Vector2(originX, originY));
		_terrainMaterial?.SetShaderParameter("map_size", new Vector2(TerrainWindowTiles, TerrainWindowTiles));
	}

	/// <summary>
	/// Слой прогресса работ: DrainDirty трекера → SetCell/EraseCell бюджетом
	/// 256/кадр + SweepStale раз в 2с (протухшие без Clear стираем).
	/// Стадия -1 = стереть спрайт (работа завершена/отменена).
	/// </summary>
	private void FlushWorkProgress(double delta)
	{
		if (_workProgressLayer?.TileSet == null)
			return;
		WorkProgressTracker.Instance.DrainDirty(_progressDrain, 256);
		if (!_workProgressLayer.Visible)
		{
			// При отдалении камеры (LOD) слой скрыт — стираем только завершённые, не рисуем новые
			foreach (var (x, y, stage) in _progressDrain)
			{
				if (stage < 0)
					_workProgressLayer.EraseCell(new Vector2I(x, y));
			}
			return;
		}
		foreach (var (x, y, stage) in _progressDrain)
		{
			var pos = new Vector2I(x, y);
			if (stage < 0)
			{
				_workProgressLayer.EraseCell(pos);
				continue;
			}
			var (ax, ay) = WorkProgressTracker.AtlasForStage(stage);
			_workProgressLayer.SetCell(pos, SourceWorkProgress, new Vector2I(ax, ay));
		}
		_progressSweepTimer += (float)delta;
		if (_progressSweepTimer >= 2.0f)
		{
			_progressSweepTimer = 0f;
			WorkProgressTracker.Instance.SweepStale(_progressStale, System.TimeSpan.FromSeconds(10));
			foreach (var (x, y) in _progressStale)
				_workProgressLayer.EraseCell(new Vector2I(x, y));
		}
	}

	/// <summary>
	/// Кодовый TileSet прогресса из uid://qqhtvxqngv4a (атлас 3×2, тайл 64).
	/// Одиночный Source без terrain — стадии выбираем атлас-координатой.
	/// Null — текстура не загрузилась (слой молча пуст, игра идёт).
	/// </summary>
	private TileSet CreateWorkProgressTileSet()
	{
		var tileSet = new TileSet { TileSize = new Vector2I(TileSizePx, TileSizePx) };
		var tex = LoadTexture(TextureProcessOfWork, "process_of_work");
		if (tex == null)
		{
			GD.PrintErr("MapRenderer: текстура прогресса (uid://qqhtvxqngv4a) не найдена!");
			return tileSet;
		}
		var src = new TileSetAtlasSource { Texture = tex, TextureRegionSize = new Vector2I(TileSizePx, TileSizePx) };
		for (int ty = 0; ty < 2; ty++)
			for (int tx = 0; tx < 3; tx++)
				src.CreateTile(new Vector2I(tx, ty));
		tileSet.AddSource(src, SourceWorkProgress);
		return tileSet;
	}

	private void QueueBlueprintRefresh() => _blueprintRefreshQueued = true;

	/// <summary>
	/// Переключить режим карты. Humidity — синий слой влажности, Fertility —
	/// зелёный слой плодородия, Normal — спрятать всё. Режимы взаимоисключающие.
	/// Слой обновляется раз в кадр троттлингом внутри.
	/// </summary>
	public void SetMapMode(MapMode mode)
	{
		CurrentMapMode = mode;
		if (_humidityOverlay != null)
		{
			// Сначала Visible=true, потом Refresh: невидимому Refresh — no-op.
			_humidityOverlay.Visible = mode == MapMode.Humidity;
			if (mode == MapMode.Humidity)
				_humidityOverlay.Refresh(Humidity);
		}
		if (_fertilityOverlay != null)
		{
			_fertilityOverlay.Visible = mode == MapMode.Fertility;
			if (mode == MapMode.Fertility)
				_fertilityOverlay.Refresh(Fertility);
		}
		if (_mapModeOverlay != null)
		{
			bool handled = MapModeOverlayRenderer.Handles(mode);
			// Видимость ставим ДО SetMode: SetMode сразу строит текстуру, если видим
			// (повторный RefreshNow здесь не нужен — иначе двойная сборка на переключении).
			_mapModeOverlay.Visible = handled;
			_mapModeOverlay.SetMode(mode);
		}
		OnMapModeChanged?.Invoke(mode);
	}

	/// <summary>Дёрнуть обновление оверлея (после тика влажности раз в 30с).</summary>
	public void RefreshHumidityOverlay()
	{
		if (CurrentMapMode == MapMode.Humidity && _humidityOverlay != null)
			_humidityOverlay.Refresh(Humidity);
	}

	/// <summary>Дёрнуть обновление оверлея (после тика плодородия раз в 60с).</summary>
	public void RefreshFertilityOverlay()
	{
		if (CurrentMapMode == MapMode.Fertility && _fertilityOverlay != null)
			_fertilityOverlay.Refresh(Fertility);
	}

	/// <summary>
	/// task-6: перестроить оверлей режимов карты немедленно (руды/высоты/склад/работы).
	/// Нужно после массовых изменений данных (например, отработка жилы), если ждать
	/// таймер обновления не хочется.
	/// </summary>
	public void RefreshMapModeOverlay()
	{
		if (_mapModeOverlay != null && MapModeOverlayRenderer.Handles(CurrentMapMode))
			_mapModeOverlay.RefreshNow();
	}

	public void ClearAllLayers()
	{
		_groundLayer?.Clear();
		_objectLayer?.Clear();
		_mountainLayer?.Clear();
		_oreLayer?.Clear();
		_wallLayer?.Clear();
		_floorLayer?.Clear();
		_buildingLayer?.Clear();
		_roofLayer?.Clear();
		_farmLayer?.Clear();
		_farmBlueprintLayer?.Clear();
		_farmTerrain?.Clear();
		_farmBlueprintTerrain?.Clear();
		_stockpileLayer?.Clear();
		_blueprintLayer?.Clear();
		_wallBlueprintLayer?.Clear();
		_ghostLayer?.Clear();
		_workProgressLayer?.Clear();
	}

	private void ApplyMap()
	{
		try
		{
			MapData mapData = _pendingMapData;
			if (mapData == null) return;
			_currentMapData = mapData;
			_pendingMapData = null;

			ClearAllLayers();

			TileSet groundTileSet = CreateGroundTileSet();
			_groundLayer.TileSet = groundTileSet;
			_stockpileLayer.TileSet = groundTileSet;

			if (_terrainMaterial != null)
			{
				var grassTex = LoadTexture(TextureGrass, "grass");
				var sandTex = LoadTexture(TextureSand, "sand");
				var waterTex = LoadTexture(TextureWater, "water");
				if (grassTex != null) _terrainMaterial.SetShaderParameter("tex_grass", grassTex);
				if (sandTex != null) _terrainMaterial.SetShaderParameter("tex_sand", sandTex);
				if (waterTex != null) _terrainMaterial.SetShaderParameter("tex_water", waterTex);

				// Новая карта = новое поколение контента окна (могли смениться сид и
				// WorldOrigin): без этого инкрементальный сдвиг переиспользовал бы
				// байты прошлой карты (InvalidateTerrainTexture → forceFull).
				InvalidateTerrainTexture();
				RequestTerrainWindowUpdate(_currentTerrainOriginX == long.MinValue ? 0 : _currentTerrainOriginX,
									_currentTerrainOriginY == long.MinValue ? 0 : _currentTerrainOriginY);
				if (_groundSurface != null)
				{
					_groundSurface.Material = _terrainMaterial;
					_groundSurface.Visible = true;
				}
				_groundLayer.Visible = false;
			}

			TileSet objectTileSet = CreateObjectTileSet();
			_objectLayer.TileSet = objectTileSet;

			// Руда (task-3): кодовый TileSet, композиты строятся лениво при первой
			// клетке пары (ore_type, state) — 0–5 жил ⇒ сотни файлов, не 3243.
			_oreTiles = new OreTileSetBuilder();
			_oreLayer.TileSet = _oreTiles.TileSet;

			

			// Горы — terrain-слой (стандарт): TileSet с разметкой из сцены
			// (mountains_tile в Main.tscn), атлас подбирает движок.
			TileSet mountainTerrainTileSet = GetMountainTerrainTileSet() ?? CreateMountainTileSet();
			_mountainLayer.TileSet = mountainTerrainTileSet;
			_mountains = new TerrainTileLayer(_mountainLayer, MountainTerrainSetId, MountainTerrainId);
			var mountainCells = new List<Vector2I>();

			bool populateGround = _groundLayer != null && _groundLayer.Visible;
			Vector2I[] grassVariants = populateGround ? GetAtlasTileCoords(groundTileSet, SourceGrass) : null;
			Vector2I[] waterVariants = populateGround ? GetAtlasTileCoords(groundTileSet, SourceWater) : null;
			Vector2I[] sandVariants = populateGround ? GetAtlasTileCoords(groundTileSet, SourceSand) : null;
			bool hasSand = populateGround && groundTileSet.HasSource(SourceSand);

			for (int x = 0; x < MapWidth; x++)
			{
				for (int y = 0; y < MapHeight; y++)
				{
					var pos = new Vector2I(x, y);
					int variant = populateGround ? mapData.GetGroundVariant(x, y) : 0;

					if (mapData.Ground[x, y] == TileType.Mountain)
					{
						if (populateGround)
						{
							Vector2I coords = grassVariants[variant % grassVariants.Length];
							_groundLayer.SetCell(pos, SourceGrass, coords);
						}
						mountainCells.Add(pos);
					}
					else if (mapData.Ground[x, y] == TileType.Grass)
					{
						if (populateGround)
						{
							var biome = mapData.GetBiome(x, y);
							int sourceId;
							Vector2I coords;
							if (biome == BiomeType.Desert && hasSand)
							{
								sourceId = SourceSand;
								coords = sandVariants[variant % sandVariants.Length];
							}
							else
							{
								sourceId = SourceGrass;
								coords = grassVariants[variant % grassVariants.Length];
							}
							_groundLayer.SetCell(pos, sourceId, coords);
						}

						if (mapData.TreeOnGrass[x, y])
						{
							int treeVariant = mapData.GetTreeVariant(x, y) & 3;
							_objectLayer.SetCell(pos, SourceObjectComposite, new Vector2I(treeVariant, 0));
						}
						else if (mapData.StoneOnGrass[x, y])
						{
							int stoneVariant = UnifiedWorldGenerator.StoneVariantFor(x, y, mapData.Seed) & 3;
							_objectLayer.SetCell(pos, SourceObjectComposite, new Vector2I(4 + stoneVariant, 0));
						}
					}
					else
					{
						if (populateGround)
						{
							Vector2I coords = waterVariants[variant % waterVariants.Length];
							_groundLayer.SetCell(pos, SourceWater, coords);
						}
					}
				}
			}

			_mountains.RebuildAll(mountainCells);

			// Руда: заливаем клетки с OreType != 0 (детерминированные месторождения
			// заглушки геологии). Ошибки арта не валят слой — клетки пропускаются.
			FillOreLayer(mapData);

			// Слои стен/чертежей/госта используют TileSet с terrain-разметкой
			// из сцены (wood_wall_tile в Main.tscn) — автотайлинг везде считает
			// сам Godot через SetCellsTerrainConnect. Кодовый CreateBuildingTileSet
			// больше не используется для стен (там нет terrain-битов).
			TileSet wallTerrainTileSet = GetWallTerrainTileSet() ?? CreateBuildingTileSet();
			TileSet buildingTileSet = CreateBuildingTileSet();
			// Слой прогресса: TileSet кодом из ProcessOfWork (атлас 3×2).
			_workProgressLayer.TileSet = CreateWorkProgressTileSet();
			// Ghost — ДУБЛЬ сценового terrain-набора + одиночные Source травы/
			// грядки/мебели (Ghost*Source). Исходный сценовый набор содержит
			// ТОЛЬКО стены: рисовать зону его SourceGrass/SourceGardenBed (=0)
			// НЕЛЬЗЯ — там стены, призрак заливался стенами (баг склада).
			// Дубликат никому не мешает: wall/blueprint-слои сидят на оригинале.
			TileSet ghostTileSet = CloneTileSetWithGhostSources(wallTerrainTileSet);
			_ghostLayer.TileSet = ghostTileSet ?? wallTerrainTileSet;
			_wallLayer.TileSet = wallTerrainTileSet;
			// Чертежи стен — сценовый terrain-набор (состыковка считает движок),
			// мебель/грядки — кодовый building-набор (одиночные спрайты).
			_wallBlueprintLayer.TileSet = wallTerrainTileSet;
			_blueprintLayer.TileSet = buildingTileSet;
			_buildingLayer.TileSet = buildingTileSet;

			TileSet floorTileSet = CreateFloorTileSet();
			_floorLayer.TileSet = floorTileSet;

			TileSet roofTileSet = CreateRoofTileSet();
			_roofLayer.TileSet = roofTileSet;

			TileSet farmTerrainTileSet = GetGardenBedTerrainTileSet() ?? buildingTileSet;
			_farmLayer.TileSet = farmTerrainTileSet;
			_farmBlueprintLayer.TileSet = farmTerrainTileSet;
			_farmTerrain = new TerrainTileLayer(_farmLayer, 0, 0);
			_farmBlueprintTerrain = new TerrainTileLayer(_farmBlueprintLayer, 0, 0);

		// Стандарт: построенные стены и чертежи стен — terrain-слои
		// (автотайлинг считает движок). Мебель — одиночные тайлы в building-наборе.
		_builtWalls = new TerrainTileLayer(_wallLayer, WallTerrainSetId, WallTerrainId);
		_wallBlueprints = new TerrainTileLayer(_wallBlueprintLayer, WallTerrainSetId, WallTerrainId);

			if (_wallBuildManager != null)
			{
				_wallBuildManager.OnTilesUpdated -= OnWallTilesUpdated;
				_wallBuildManager.Dispose();
				_wallBuildManager = null;
			}

			_wallBuildManager = new WallBuildManager();
			_wallBuildManager.OnTilesUpdated += OnWallTilesUpdated;

			// Начальные тени статики: деревья из MapData + пустые стены/здания (их докинет Rebuild при стройке).
			ShadowRenderer?.RebuildStatic(
				mapData,
				_wallBuildManager.GetAllWalls(),
				BuildingManager.Instance.GetAllBuildings());

			// task-6: оверлей режимов карты получает новую карту (размер/данные руды).
			_mapModeOverlay?.SetMap(mapData);

			OnMapApplied?.Invoke();
		}
		catch (Exception ex)
		{
			GD.PrintErr($"MapRenderer: ошибка ApplyMap: {ex.Message}");
		}
	}

	/// <summary>
	/// Снос чертежа стены: помечаем грязной саму клетку И 4 ортогональных соседа —
	/// симметрично OnBlueprintCompleted. Без соседей у оставшихся чертежей сохранялась
	/// старая состыковка (движок перерисовывал только саму клетку), и снятая стена
	/// оставляла визуальный «призрак» стыка.
	/// </summary>
	private void MarkWallBlueprintRemoved(int x, int y)
	{
		_wallBlueprints?.MarkDirty(new Vector2I(x, y));
		_wallBlueprints?.MarkDirty(new Vector2I(x + 1, y));
		_wallBlueprints?.MarkDirty(new Vector2I(x - 1, y));
		_wallBlueprints?.MarkDirty(new Vector2I(x, y + 1));
		_wallBlueprints?.MarkDirty(new Vector2I(x, y - 1));
		EnqueuePending(_blueprintLayer, new Vector2I(x, y), 0, AtlasOrigin, true);
	}

	private void OnBlueprintChanged((int X, int Y) pos, BuildingType type)
	{
		if (type == BuildingType.WoodWall)
			_wallBlueprints?.MarkDirty(new Vector2I(pos.X, pos.Y));
		else
			EnqueuePending(_blueprintLayer, new Vector2I(pos.X, pos.Y), SourceForBuilding(type), AtlasOrigin, false);
	}

	private void RefreshAllBlueprints()
	{
		if (_blueprintLayer == null) return;

		var blueprints = BlueprintManager.Instance.GetAllBlueprints();
		var farmPlots = FarmJobManager.Instance.GetAllMarkedPlots();

		_blueprintLayer.Clear();

		// Мебель — одиночные SetCell в building-слое. Стены идут только
		// через terrain-слой чертежей (_wallBlueprints.RebuildAll ниже):
		// движок сам считает состыковку чертёж↔чертёж.
		var wallCells = new List<Vector2I>();
		foreach (var (cell, bType) in blueprints)
		{
			Vector2I mapPos = new Vector2I(cell.X, cell.Y);
			if (bType == BuildingType.WoodWall)
				wallCells.Add(mapPos);
			else
				_blueprintLayer.SetCell(mapPos, SourceForBuilding(bType), AtlasOrigin);
		}
		_wallBlueprints?.RebuildAll(wallCells);

		foreach (var plot in farmPlots)
		{
			_blueprintLayer.SetCell(new Vector2I(plot.X, plot.Y), SourceGardenBed, AtlasOrigin);
		}
	}

	private void OnBlueprintCompleted((int X, int Y) pos, BuildingType type)
	{
		if (type == BuildingType.WoodWall)
		{
			// Чертёж убран из terrain-слоя (соседние чертежи пересчитаются
			// сами через exists-флаш), построенная стена — в _builtWalls.
			_wallBlueprints?.MarkDirty(new Vector2I(pos.X, pos.Y));
			_wallBlueprints?.MarkDirty(new Vector2I(pos.X + 1, pos.Y));
			_wallBlueprints?.MarkDirty(new Vector2I(pos.X - 1, pos.Y));
			_wallBlueprints?.MarkDirty(new Vector2I(pos.X, pos.Y + 1));
			_wallBlueprints?.MarkDirty(new Vector2I(pos.X, pos.Y - 1));
			_wallBuildManager.AddWall(pos.X, pos.Y);
			_builtWalls?.MarkDirty(new Vector2I(pos.X + 1, pos.Y - 1));
			_builtWalls?.MarkDirty(new Vector2I(pos.X - 1, pos.Y - 1));
			_builtWalls?.MarkDirty(new Vector2I(pos.X + 1, pos.Y + 1));
			_builtWalls?.MarkDirty(new Vector2I(pos.X - 1, pos.Y + 1));
		}
		else
		{
			// Мебель и верстак: стираем одиночное превью + один спрайт в building-слой.
			EnqueuePending(_blueprintLayer, new Vector2I(pos.X, pos.Y), 0, AtlasOrigin, true);
			BuildingManager.Instance.AddBuilding(pos.X, pos.Y, type);
		}
	}

	private void OnTreeChopped((int X, int Y) pos)
	{
		// §118 фаза 4 (аудит MapDataWriter §1, строки 1345/1359): записи в _pendingMapData
		// здесь БЫЛИ МЁРТВЫМИ — ApplyMap обнуляет _pendingMapData, поэтому `?.` их гасил.
		// Авторитетную мутацию делает сим-хендлер (TreeChoppingJobHandler пишет прямо в
		// алиас-массив MapData.TreeOnGrass), а на мир её зеркалит единый канал
		// (IWorldMutationSink/IslandMutationMirror). Рендеру остаётся только погасить
		// спрайт и тень.
		ShadowRenderer?.RemoveCaster(pos.X, pos.Y);
		_choppedTreesQueue.Enqueue(new ChoppedCell(new Vector2I(pos.X, pos.Y), Volatile.Read(ref _mapGeneration))); // C-08: штамп поколения
	}

	private void OnStoneMined((int X, int Y) pos)
	{
		// См. комментарий в OnTreeChopped: запись в _pendingMapData была мёртвой.
		// Россыпь гасит спрайт; симуляционный флаг снимает MiningJobHandler, зеркалит фасад.
		ShadowRenderer?.RemoveCaster(pos.X, pos.Y);
		_choppedTreesQueue.Enqueue(new ChoppedCell(new Vector2I(pos.X, pos.Y), Volatile.Read(ref _mapGeneration))); // C-08: штамп поколения
	}

	// ================= Руда (task-3) =================

	/// <summary>
	/// Залить слой руды из MapData: маска соседей → ячейка композитного атласа.
	/// Только главный поток (ApplyMap уже в нём).
	/// </summary>
	private void FillOreLayer(MapData mapData)
	{
		if (_oreLayer == null || mapData?.OreType == null)
			return;
		_oreLayer.Clear();
		int width = mapData.Width;
		int height = mapData.Height;
		byte[] oreType = mapData.OreType;
		for (int y = 0; y < height; y++)
		{
			int row = y * width;
			for (int x = 0; x < width; x++)
			{
				if (oreType[row + x] != OreConfig.None)
					RefreshOreCellInternal(mapData, x, y);
			}
		}
	}

	/// <summary>
	/// Перерисовать тайл руды клетки по текущему запасу/состоянию и маске соседей.
	/// Клетка без руды — EraseCell. ВЫЗЫВАТЬ ТОЛЬКО ИЗ ГЛАВНОГО ПОТОКА:
	/// из sim-потока — через <c>Callable.From(() => RefreshOreCell(x,y)).CallDeferred()</c>.
	/// </summary>
	public void RefreshOreCell(int x, int y)
	{
		MapData mapData = _currentMapData ?? _pendingMapData;
		if (mapData == null)
			return;
		RefreshOreCellInternal(mapData, x, y);
	}

	private void RefreshOreCellInternal(MapData mapData, int x, int y)
	{
		if (_oreLayer == null || mapData?.OreType == null)
			return;
		if ((uint)x >= (uint)mapData.Width || (uint)y >= (uint)mapData.Height)
			return;

		var pos = new Vector2I(x, y);
		int idx = mapData.OreIndex(x, y);
		if (mapData.OreType[idx] == OreConfig.None)
		{
			_oreLayer.EraseCell(pos);
			return;
		}
		if (_oreTiles == null)
			return;

		byte mask = ComputeIslandOreMask(mapData, x, y, mapData.OreType[idx]);
		int maskIndex = OreMaskHelper.IndexOf(mask);
		if (maskIndex < 0)
		{
			// Недопустимая маска (битые данные) — не рисуем мусор.
			_oreLayer.EraseCell(pos);
			return;
		}

		if (!_oreTiles.TryGetTile(mapData.OreType[idx], mapData.OreState[idx], maskIndex,
				out int sourceId, out Vector2I atlasCoords))
		{
			// Арта нет (warning один раз в билдере) — клетку пропускаем.
			return;
		}
		_oreLayer.SetCell(pos, sourceId, atlasCoords);
	}

	/// <summary>
	/// Перерисовать клетку и 8 соседей: снятие/выработка жилы меняет маску соседей
	/// (углы и стороны). Главный поток, из sim — CallDeferred.
	/// </summary>
	public void RefreshOreArea(int x, int y)
	{
		for (int oy = -1; oy <= 1; oy++)
			for (int ox = -1; ox <= 1; ox++)
				RefreshOreCell(x + ox, y + oy);
	}

	/// <summary>
	/// Событие OreJobManager (уже доставлено в главный поток через CallDeferred):
	/// запас сменил состояние rich→normal→poor или жила выработана — обновляем
	/// спрайт клетки и соседей (их маска тоже изменилась).
	/// </summary>
	private void OnOreCellChanged((int X, int Y) pos) => RefreshOreArea(pos.X, pos.Y);

	// ================= task-8: персистентность руды на острове =================

	/// <summary>
	/// Наложить мутации руды мира (<see cref="OreWorldState"/>) на массивы острова
	/// после генерации карты: клетки, добытые в прошлой сессии, не «возвращаются».
	/// </summary>
	public void ApplyWorldOreStateToIsland()
	{
		MapData mapData = _currentMapData;
		if (mapData?.OreType == null)
			return;
		int width = mapData.Width;
		int height = mapData.Height;
		foreach (var (localX, localY, reserve) in OreWorldState.Enumerate())
		{
			if ((uint)localX >= (uint)width || (uint)localY >= (uint)height)
				continue;
			ApplyWorldOreToIslandCellInternal(mapData, localX, localY, reserve);
		}
	}

	/// <summary>Применить одну запись руды из чанк-стора к острову (загрузка дельты).</summary>
	public void ApplyWorldOreToIslandCell(int localX, int localY)
	{
		MapData mapData = _currentMapData ?? _pendingMapData;
		if (mapData?.OreType == null)
			return;
		if (!OreWorldState.TryGet(localX, localY, out ushort reserve))
			return;
		ApplyWorldOreToIslandCellInternal(mapData, localX, localY, reserve);
	}

	/// <summary>
	/// Маска соседей руды для клетки острова (task-9). Сосед за кромкой окна
	/// спрашивается у канонического поля мира (<see cref="OreWorldField"/>), а не
	/// считается «нет руды»: иначе на кромке 1024 у тела, пересекающего границу,
	/// пропадали бы биты E/N/S/W и появлялся видимый шов.
	/// Координаты MapData локальны окну; окно MapRenderer всегда в начале холста
	/// (origin 0), поэтому local == canvas-координата для OreWorldField.
	/// </summary>
	private static byte ComputeIslandOreMask(MapData mapData, int x, int y, byte type)
	{
		byte mask = 0;
		bool n = SameOre(mapData, x, y - 1, type);
		bool e = SameOre(mapData, x + 1, y, type);
		bool s = SameOre(mapData, x, y + 1, type);
		bool w = SameOre(mapData, x - 1, y, type);
		if (n) mask |= OreMaskHelper.N;
		if (e) mask |= OreMaskHelper.E;
		if (s) mask |= OreMaskHelper.S;
		if (w) mask |= OreMaskHelper.W;
		if (n && e && SameOre(mapData, x + 1, y - 1, type)) mask |= OreMaskHelper.NE;
		if (e && s && SameOre(mapData, x + 1, y + 1, type)) mask |= OreMaskHelper.SE;
		if (s && w && SameOre(mapData, x - 1, y + 1, type)) mask |= OreMaskHelper.SW;
		if (w && n && SameOre(mapData, x - 1, y - 1, type)) mask |= OreMaskHelper.NW;
		return mask;
	}

	private static bool SameOre(MapData mapData, int x, int y, byte type)
	{
		if ((uint)x < (uint)mapData.Width && (uint)y < (uint)mapData.Height)
		{
			int idx = y * mapData.Width + x;
			return mapData.OreType[idx] == type;
		}
		// За кромкой окна — каноническое поле мира (единый источник истины).
		ulong seed = Game.Core.World.WorldModel.Seed;
		uint ver = Game.Core.World.WorldModel.Version;
		if (seed == 0)
			return false;
		return OreWorldField.GetEffectiveTypeLocal(x, y, seed, ver) == type;
	}

	private void ApplyWorldOreToIslandCellInternal(MapData mapData, int localX, int localY, ushort reserve)
	{
		if ((uint)localX >= (uint)mapData.Width || (uint)localY >= (uint)mapData.Height)
			return;
		int idx = mapData.OreIndex(localX, localY);
		if (reserve == 0)
		{
			if (mapData.OreType[idx] == OreConfig.None)
				return;
			mapData.OreType[idx] = OreConfig.None;
			mapData.OreReserve[idx] = 0;
			mapData.OreState[idx] = (byte)OreState.Poor;
		}
		else
		{
			if (mapData.OreType[idx] == OreConfig.None)
				return; // базовой руды нет — мутация не про эту карту
			mapData.OreReserve[idx] = reserve;
			mapData.OreState[idx] = (byte)OreConfig.StateFor(reserve, mapData.OreReserveInitial[idx]);
		}
		RefreshOreArea(localX, localY);
	}

	private TileSet CreateGroundTileSet()
	{
		var tileSet = new TileSet { TileSize = new Vector2I(TileSizePx, TileSizePx) };
		AddGroundAtlasSource(tileSet, TextureGrass, "grass", SourceGrass);
		AddGroundAtlasSource(tileSet, TextureWater, "water", SourceWater);
		AddGroundAtlasSource(tileSet, TextureSand, "sand", SourceSand);
		return tileSet;
	}

	private void AddGroundAtlasSource(TileSet tileSet, string uid, string name, int sourceId)
	{
		var tex = LoadTexture(uid, name);
		if (tex == null) return;

		var src = new TileSetAtlasSource { Texture = tex, TextureRegionSize = new Vector2I(TileSizePx, TileSizePx) };
		int cols = Math.Max(1, tex.GetWidth() / TileSizePx);
		int rows = Math.Max(1, tex.GetHeight() / TileSizePx);
		for (int r = 0; r < rows; r++)
		{
			for (int c = 0; c < cols; c++)
			{
				src.CreateTile(new Vector2I(c, r));
			}
		}
		tileSet.AddSource(src, sourceId);
	}

	private static Vector2I[] GetAtlasTileCoords(TileSet tileSet, int sourceId)
	{
		if (tileSet == null || !tileSet.HasSource(sourceId))
			return new[] { Vector2I.Zero };
		var src = tileSet.GetSource(sourceId) as TileSetAtlasSource;
		if (src == null || src.GetTilesCount() == 0)
			return new[] { Vector2I.Zero };
		int count = src.GetTilesCount();
		var coords = new Vector2I[count];
		for (int i = 0; i < count; i++)
			coords[i] = src.GetTileId(i);
		return coords;
	}

	private TileSet CreateObjectTileSet()
	{
		var tileSet = new TileSet { TileSize = new Vector2I(TileSizePx, TileSizePx) };

		// 1. Композитный объединенный атлас 512x64 (PLAN.md §36):
		// [0..3]: 4 варианта деревьев (tree, tree_1, tree_2, tree_3)
		// [4..7]: 4 варианта камней (4 квадранта 2x2 из stone.png)
		// Все 70 000 объектов слоя делят ОДНУ текстуру, исключая разрывы батчинга.
		var atlasImg = Image.CreateEmpty(TileSizePx * 8, TileSizePx, false, Image.Format.Rgba8);

		Texture2D[] treeTextures = {
			LoadTexture(TextureTree0, "tree"),
			LoadTexture(TextureTree1, "tree_1"),
			LoadTexture(TextureTree2, "tree_2"),
			LoadTexture(TextureTree3, "tree_3")
		};

		for (int v = 0; v < 4; v++)
		{
			var img = treeTextures[v]?.GetImage();
			if (img != null)
			{
				atlasImg.BlitRect(img, new Rect2I(0, 0, TileSizePx, TileSizePx), new Vector2I(v * TileSizePx, 0));
			}
		}

		var stoneTex = LoadTexture(TextureStone, "stone");
		var stoneImg = stoneTex?.GetImage();
		if (stoneImg != null)
		{
			for (int v = 0; v < 4; v++)
			{
				var (qx, qy) = UnifiedWorldGenerator.StoneAtlasFor(v);
				atlasImg.BlitRect(stoneImg, new Rect2I(qx * TileSizePx, qy * TileSizePx, TileSizePx, TileSizePx), new Vector2I((4 + v) * TileSizePx, 0));
			}
		}

		var compositeSource = new TileSetAtlasSource
		{
			Texture = ImageTexture.CreateFromImage(atlasImg),
			TextureRegionSize = new Vector2I(TileSizePx, TileSizePx)
		};
		for (int i = 0; i < 8; i++)
		{
			compositeSource.CreateTile(new Vector2I(i, 0));
		}
		tileSet.AddSource(compositeSource, SourceObjectComposite);

		// Обратная совместимость: регистрация отдельных SourceTree/SourceStone для внешних вызовов
		int[] treeSources = { SourceTree0, SourceTree1, SourceTree2, SourceTree3 };
		for (int v = 0; v < 4; v++)
		{
			if (treeTextures[v] != null)
			{
				var s = new TileSetAtlasSource { Texture = treeTextures[v], TextureRegionSize = new Vector2I(TileSizePx, TileSizePx) };
				s.CreateTile(Vector2I.Zero);
				tileSet.AddSource(s, treeSources[v]);
			}
		}

		if (stoneTex != null)
		{
			int[] stoneSources = { SourceStone0, SourceStone1, SourceStone2, SourceStone3 };
			for (int v = 0; v < 4; v++)
			{
				var (qx, qy) = UnifiedWorldGenerator.StoneAtlasFor(v);
				var quadrant = new AtlasTexture
				{
					Atlas = stoneTex,
					Region = new Rect2(qx * TileSizePx, qy * TileSizePx, TileSizePx, TileSizePx)
				};
				var s = new TileSetAtlasSource
				{
					Texture = quadrant,
					TextureRegionSize = new Vector2I(TileSizePx, TileSizePx)
				};
				s.CreateTile(Vector2I.Zero);
				tileSet.AddSource(s, stoneSources[v]);
			}
		}

		return tileSet;
	}

	/// <summary>
	/// SourceId спрайта дерева по варианту 0..3. Неизвестный вариант -> SourceTree0.
	/// </summary>
	public static int GetTreeSourceId(int variant)
	{
		return variant switch
		{
			1 => SourceTree1,
			2 => SourceTree2,
			3 => SourceTree3,
			_ => SourceTree0,
		};
	}

	/// <summary>
	/// SourceId спрайта камня по варианту 0..3 (квадранты stone.png 2x2).
	/// </summary>
	public static int GetStoneSourceId(int variant)
	{
		return (variant & 3) switch
		{
			1 => SourceStone1,
			2 => SourceStone2,
			3 => SourceStone3,
			_ => SourceStone0,
		};
	}

	/// <summary>
	/// SourceId слоя зданий по типу постройки. Стены идут terrain-слоем
	/// (этот Source здесь не используется — для единообразия маппинга).
	/// </summary>
	public static int SourceForBuilding(BuildingType type)
	{
		return type switch
		{
			BuildingType.WorkTable => SourceWorkTable,
			BuildingType.Bed => SourceBed,
			BuildingType.Bench => SourceBench,
			BuildingType.Nightstand => SourceNightstand,
			BuildingType.CentralBlock => SourceCentralBlock,
			// task-16: станки отраслей (мультиклеточные Source из building/*.png).
			BuildingType.Sawmill => SourceSawmill,
			BuildingType.Smelter => SourceSmelter,
			BuildingType.Anvil => SourceAnvil,
			BuildingType.Workbench => SourceWorkbench,
			BuildingType.SpinningWheel => SourceSpinningWheel,
			BuildingType.Loom => SourceLoom,
			BuildingType.Mill => SourceMill,
			BuildingType.AssemblyLine => SourceAssemblyLine,
			// Полы и крыша
			BuildingType.WoodFloor => SourceFloorWood,
			BuildingType.StoneFloor => SourceFloorStone,
			BuildingType.ConcreteFloor => SourceFloorConcrete,
			BuildingType.RoofResidential => SourceRoofResidential,
			BuildingType.RoofIndustrial => SourceRoofIndustrial,
			_ => SourceWall,
		};
	}

	private TileSet CreateMountainTileSet()
	{
		var tileSet = new TileSet { TileSize = new Vector2I(TileSizePx, TileSizePx) };
		var mountainTex = LoadTexture(TextureMountain, "mountains");
		if (mountainTex == null)
		{
			GD.PrintErr("MapRenderer: текстура гор (uid://de7mit41ukuoq) не найдена!");
			return tileSet;
		}
		var src = new TileSetAtlasSource { Texture = mountainTex, TextureRegionSize = new Vector2I(TileSizePx, TileSizePx) };
		// Атлас 5×5: используются только 4×4 (последний столбец x=4 и строка y=4 — пустые).
		for (int ty = 0; ty < 4; ty++)
			for (int tx = 0; tx < 4; tx++)
				src.CreateTile(new Vector2I(tx, ty));
		tileSet.AddSource(src, SourceMountain);
		return tileSet;
	}

	private TileSet CreateBuildingTileSet()
	{
		var tileSet = new TileSet { TileSize = new Vector2I(TileSizePx, TileSizePx) };

		var wallTexture = LoadTexture(TextureWoodWall, "wooden_wall");
		if (wallTexture != null)
		{
			var wallAtlas = new TileSetAtlasSource { Texture = wallTexture, TextureRegionSize = new Vector2I(WallAtlasTilePx, WallAtlasTilePx) };
			// Атлас стен 7×7 (448×448). Клетки без terrain-разметки в TileSet не создаём —
			// SetCell на несуществующий тайл молча не отрисуется, а WallTileHelper их не возвращает.
			for (int ty = 0; ty < WallAtlasRows; ty++)
				for (int tx = 0; tx < WallAtlasColumns; tx++)
				{
					if (!WallTileHelper.IsAtlasTileUsed(tx, ty)) continue;
					wallAtlas.CreateTile(new Vector2I(tx, ty));
				}

			tileSet.AddSource(wallAtlas, SourceWall);
		}

		var tableTexture = LoadTexture(TextureWorkTable, "work_table");
		if (tableTexture != null)
		{
			var tableSource = new TileSetAtlasSource { Texture = tableTexture, TextureRegionSize = new Vector2I(TileSizePx, TileSizePx) };
			tableSource.CreateTile(Vector2I.Zero);
			tileSet.AddSource(tableSource, SourceWorkTable);
		}

		// Мебель 64x64 одиночными спрайтами (кровать/скамья/тумбочка).
		AddSingleTile(tileSet, TextureBed, "bed", SourceBed);
		AddSingleTile(tileSet, TextureBench, "bench", SourceBench);
		AddSingleTile(tileSet, TextureNightstand, "nightstand", SourceNightstand);
		// §119 P5: центральный блок — тем же одиночным спрайтом.
		AddSingleTile(tileSet, TextureCentralBlock, "central_block", SourceCentralBlock);

		// task-16: СТАНКИ отраслей — мультиклеточные тайлы (габарит из арта).
		AddMultiTile(tileSet, TextureSawmill, "sawmill", SourceSawmill, BuildingType.Sawmill);
		AddMultiTile(tileSet, TextureSmelter, "smelter", SourceSmelter, BuildingType.Smelter);
		AddMultiTile(tileSet, TextureAnvil, "anvil", SourceAnvil, BuildingType.Anvil);
		AddMultiTile(tileSet, TextureWorkbench, "workbench", SourceWorkbench, BuildingType.Workbench);
		AddMultiTile(tileSet, TextureSpinningWheel, "spinning_wheel", SourceSpinningWheel, BuildingType.SpinningWheel);
		AddMultiTile(tileSet, TextureLoom, "loom", SourceLoom, BuildingType.Loom);
		AddMultiTile(tileSet, TextureMill, "mill", SourceMill, BuildingType.Mill);
		AddMultiTile(tileSet, TextureAssemblyLine, "assembly_line", SourceAssemblyLine, BuildingType.AssemblyLine);

		var gardenBedTexture = LoadTexture(TextureGardenBed, "garden_beds");
		if (gardenBedTexture != null)
		{
			var bedSource = new TileSetAtlasSource { Texture = gardenBedTexture, TextureRegionSize = new Vector2I(TileSizePx, TileSizePx) };
			bedSource.CreateTile(Vector2I.Zero);
			tileSet.AddSource(bedSource, SourceGardenBed);
		}

		return tileSet;
	}

	/// <summary>
	/// Один спрайт 64x64 отдельным Source (мебель). Молча пропускает,
	/// если текстура не загрузилась — слой просто не отрисует этот тип.
	/// </summary>
	private void AddSingleTile(TileSet tileSet, string textureUid, string name, int sourceId)
	{
		var tex = LoadTexture(textureUid, name);
		if (tex == null)
		{
			GD.PrintErr($"MapRenderer: текстура '{name}' ({textureUid}) не найдена!");
			return;
		}
		var src = new TileSetAtlasSource { Texture = tex, TextureRegionSize = new Vector2I(TileSizePx, TileSizePx) };
		src.CreateTile(Vector2I.Zero);
		tileSet.AddSource(src, sourceId);
	}

	/// <summary>
	/// Мультиклеточный спрайт станка (task-16): один тайл размером w×h клеток,
	/// чтобы картинка и габариты постройки совпадали. Арт обязан покрывать
	/// заявленный габарит — иначе тайл урезается по фактическому размеру файла.
	/// </summary>
	private void AddMultiTile(TileSet tileSet, string textureRef, string name, int sourceId, BuildingType type)
	{
		var tex = LoadTexture(textureRef, name);
		if (tex == null)
		{
			GD.PrintErr($"MapRenderer: текстура станка '{name}' ({textureRef}) не найдена!");
			return;
		}
		int w = BuildingFootprint.WidthOf(type);
		int h = BuildingFootprint.HeightOf(type);
		int texW = Math.Max(1, tex.GetWidth() / TileSizePx);
		int texH = Math.Max(1, tex.GetHeight() / TileSizePx);
		if (texW < w || texH < h)
		{
			GD.PrintErr($"MapRenderer: '{name}' арт {texW}×{texH} меньше габарита {w}×{h} — тайл урезан.");
			w = texW;
			h = texH;
		}
		var src = new TileSetAtlasSource
		{
			Texture = tex,
			TextureRegionSize = new Vector2I(TileSizePx, TileSizePx)
		};
		src.CreateTile(Vector2I.Zero, new Vector2I(w, h));
		// Godot рисует тайл ЦЕНТРОМ на клетке (texture_origin=0): сверхкрупный
		// тайл W×H симметрично вылезает за габарит на (W-1)/2 и (H-1)/2 клетки
		// влево-вверх — соседние постройки «наезжали» друг на друга. Сдвигаем
		// origin, чтобы текстура ложилась ровно в клетки (X..X+W-1, Y..Y+H-1).
		TileData td = src.GetTileData(Vector2I.Zero, 0);
		td.TextureOrigin = new Vector2I((w - 1) * TileSizePx / 2, (h - 1) * TileSizePx / 2);
		tileSet.AddSource(src, sourceId);
	}

	/// <summary>
	/// Дубликат сценового wall-TileSet + одиночные ghost-Source (трава/грядка/
	/// мебель) под константами Ghost*Source. Клон нужен, т.к. исходный набор
	/// из сцены AddSource не принимает (чужой ресурс), а номера 0/6 в нём
	/// уже заняты стенами. Null — клонировать нечего (нет сценового набора).
	/// </summary>
	private TileSet CloneTileSetWithGhostSources(TileSet wallTerrainTileSet)
	{
		if (wallTerrainTileSet == null)
			return null;
		var ghost = wallTerrainTileSet.Duplicate() as TileSet;
		if (ghost == null)
			return null;
		ghost.TileSize = new Vector2I(TileSizePx, TileSizePx);
		AddGhostSingleTile(ghost, TextureGrass, "ghost_grass", GhostGrassSource);
		AddGhostSingleTile(ghost, TextureGardenBed, "ghost_garden", GhostGardenBedSource);
		AddGhostStockpileTile(ghost, GhostStockpileSource);
		AddGhostSingleTile(ghost, TextureWorkTable, "ghost_table", GhostWorkTableSource);
		AddGhostSingleTile(ghost, TextureBed, "ghost_bed", GhostBedSource);
		AddGhostSingleTile(ghost, TextureBench, "ghost_bench", GhostBenchSource);
		AddGhostSingleTile(ghost, TextureNightstand, "ghost_nightstand", GhostNightstandSource);
		AddGhostSingleTile(ghost, TextureCentralBlock, "ghost_central_block", GhostCentralBlockSource);
		// task-16: ghost станков — те же мультиклеточные габариты, что у готового тайла.
		AddGhostMultiTile(ghost, TextureSawmill, "ghost_sawmill", GhostSawmillSource, BuildingType.Sawmill);
		AddGhostMultiTile(ghost, TextureSmelter, "ghost_smelter", GhostSmelterSource, BuildingType.Smelter);
		AddGhostMultiTile(ghost, TextureAnvil, "ghost_anvil", GhostAnvilSource, BuildingType.Anvil);
		AddGhostMultiTile(ghost, TextureWorkbench, "ghost_workbench", GhostWorkbenchSource, BuildingType.Workbench);
		AddGhostMultiTile(ghost, TextureSpinningWheel, "ghost_spinning_wheel", GhostSpinningWheelSource, BuildingType.SpinningWheel);
		AddGhostMultiTile(ghost, TextureLoom, "ghost_loom", GhostLoomSource, BuildingType.Loom);
		AddGhostMultiTile(ghost, TextureMill, "ghost_mill", GhostMillSource, BuildingType.Mill);
		AddGhostMultiTile(ghost, TextureAssemblyLine, "ghost_assembly_line", GhostAssemblyLineSource, BuildingType.AssemblyLine);
		// Ghost полов и крыши
		AddGhostCustomTile(ghost, FloorRoofTextureHelper.GetWoodFloorTexture(), GhostFloorWoodSource);
		AddGhostCustomTile(ghost, FloorRoofTextureHelper.GetStoneFloorTexture(), GhostFloorStoneSource);
		AddGhostCustomTile(ghost, FloorRoofTextureHelper.GetConcreteFloorTexture(), GhostFloorConcreteSource);
		AddGhostCustomTile(ghost, FloorRoofTextureHelper.GetRoofResidentialTexture(), GhostRoofResidentialSource);
		AddGhostCustomTile(ghost, FloorRoofTextureHelper.GetRoofIndustrialTexture(), GhostRoofIndustrialSource);
		return ghost;
	}

	private TileSet CreateFloorTileSet()
	{
		var tileSet = new TileSet { TileSize = new Vector2I(TileSizePx, TileSizePx) };
		AddCustomAtlasSource(tileSet, FloorRoofTextureHelper.GetWoodFloorTexture(), SourceFloorWood);
		AddCustomAtlasSource(tileSet, FloorRoofTextureHelper.GetStoneFloorTexture(), SourceFloorStone);
		AddCustomAtlasSource(tileSet, FloorRoofTextureHelper.GetConcreteFloorTexture(), SourceFloorConcrete);
		return tileSet;
	}

	private TileSet CreateRoofTileSet()
	{
		var tileSet = new TileSet { TileSize = new Vector2I(TileSizePx, TileSizePx) };
		AddCustomAtlasSource(tileSet, FloorRoofTextureHelper.GetRoofResidentialTexture(), SourceRoofResidential);
		AddCustomAtlasSource(tileSet, FloorRoofTextureHelper.GetRoofIndustrialTexture(), SourceRoofIndustrial);
		return tileSet;
	}

	private static void AddCustomAtlasSource(TileSet tileSet, Texture2D tex, int sourceId)
	{
		if (tileSet == null || tex == null || tileSet.HasSource(sourceId)) return;
		var src = new TileSetAtlasSource { Texture = tex, TextureRegionSize = new Vector2I(TileSizePx, TileSizePx) };
		src.CreateTile(Vector2I.Zero);
		tileSet.AddSource(src, sourceId);
	}

	private static void AddGhostCustomTile(TileSet tileSet, Texture2D tex, int sourceId)
	{
		if (tileSet == null || tex == null || tileSet.HasSource(sourceId)) return;
		var src = new TileSetAtlasSource { Texture = tex, TextureRegionSize = new Vector2I(TileSizePx, TileSizePx) };
		src.CreateTile(Vector2I.Zero);
		tileSet.AddSource(src, sourceId);
	}

	private void AddGhostStockpileTile(TileSet tileSet, int sourceId)
	{
		if (tileSet.HasSource(sourceId))
			return;
		var img = Image.CreateEmpty(TileSizePx, TileSizePx, false, Image.Format.Rgba8);
		var fillColor = new Color(0.65f, 0.15f, 0.9f, 0.55f);
		var borderColor = new Color(0.85f, 0.35f, 1f, 0.95f);
		for (int y = 0; y < TileSizePx; y++)
		{
			for (int x = 0; x < TileSizePx; x++)
			{
				bool isBorder = x <= 1 || x >= TileSizePx - 2 || y <= 1 || y >= TileSizePx - 2;
				img.SetPixel(x, y, isBorder ? borderColor : fillColor);
			}
		}
		var tex = ImageTexture.CreateFromImage(img);
		var src = new TileSetAtlasSource { Texture = tex, TextureRegionSize = new Vector2I(TileSizePx, TileSizePx) };
		src.CreateTile(Vector2I.Zero);
		tileSet.AddSource(src, sourceId);
	}

	private void AddGhostSingleTile(TileSet tileSet, string textureUid, string name, int sourceId)
	{
		if (tileSet.HasSource(sourceId))
			return;
		var tex = LoadTexture(textureUid, name);
		if (tex == null)
			return;
		var src = new TileSetAtlasSource { Texture = tex, TextureRegionSize = new Vector2I(TileSizePx, TileSizePx) };
		src.CreateTile(Vector2I.Zero);
		tileSet.AddSource(src, sourceId);
	}

	/// <summary>task-16: ghost станка мультиклеточным тайлом (габарит = арт).</summary>
	private void AddGhostMultiTile(TileSet tileSet, string textureRef, string name, int sourceId, BuildingType type)
	{
		if (tileSet.HasSource(sourceId))
			return;
		var tex = LoadTexture(textureRef, name);
		if (tex == null)
			return;
		int w = BuildingFootprint.WidthOf(type);
		int h = BuildingFootprint.HeightOf(type);
		int texW = Math.Max(1, tex.GetWidth() / TileSizePx);
		int texH = Math.Max(1, tex.GetHeight() / TileSizePx);
		if (texW < w || texH < h)
		{
			w = texW;
			h = texH;
		}
		var src = new TileSetAtlasSource { Texture = tex, TextureRegionSize = new Vector2I(TileSizePx, TileSizePx) };
		src.CreateTile(Vector2I.Zero, new Vector2I(w, h));
		// Та же правка центра, что в AddMultiTile: ghost обязан совпадать с
		// будущим габаритом постройки, иначе призрак стоит со сдвигом.
		TileData ghostTd = src.GetTileData(Vector2I.Zero, 0);
		ghostTd.TextureOrigin = new Vector2I((w - 1) * TileSizePx / 2, (h - 1) * TileSizePx / 2);
		tileSet.AddSource(src, sourceId);
	}

	/// <summary>
	/// SourceId ghost-призрака для зоны ("warehouse" — фиолетовая складская зона, остальное — грядка).
	/// </summary>
	public static int GhostSourceForZone(string zoneKind) =>
		zoneKind == "warehouse" ? GhostStockpileSource : GhostGardenBedSource;

	/// <summary>
	/// SourceId ghost-призрака для постройки (стены идут terrain'ом и здесь
	/// не нужны — для них PaintWallGhost; мебель — одиночные Ghost*Source).
	/// -1 — рисовать нечем (стены/неизвестное).
	/// </summary>
	public static int GhostSourceForBuilding(BuildingType type)
	{
		return type switch
		{
			BuildingType.WorkTable => GhostWorkTableSource,
			BuildingType.Bed => GhostBedSource,
			BuildingType.Bench => GhostBenchSource,
			BuildingType.Nightstand => GhostNightstandSource,
			BuildingType.CentralBlock => GhostCentralBlockSource,
			// task-16: призраки станков (мультиклеточные, как готовые тайлы).
			BuildingType.Sawmill => GhostSawmillSource,
			BuildingType.Smelter => GhostSmelterSource,
			BuildingType.Anvil => GhostAnvilSource,
			BuildingType.Workbench => GhostWorkbenchSource,
			BuildingType.SpinningWheel => GhostSpinningWheelSource,
			BuildingType.Loom => GhostLoomSource,
			BuildingType.Mill => GhostMillSource,
			BuildingType.AssemblyLine => GhostAssemblyLineSource,
			// Полы и крыша
			BuildingType.WoodFloor => GhostFloorWoodSource,
			BuildingType.StoneFloor => GhostFloorStoneSource,
			BuildingType.ConcreteFloor => GhostFloorConcreteSource,
			BuildingType.RoofResidential => GhostRoofResidentialSource,
			BuildingType.RoofIndustrial => GhostRoofIndustrialSource,
			_ => -1,
		};
	}

	/// <summary>
	/// TileSet с terrain-разметкой стен из сцены (нода wood_wall_tile в Main.tscn).
	/// Нужен ghost-слою: BuildTool рисует превью движковым SetCellsTerrainConnect.
	/// Возвращает null, если нода не найдена (например, MapRenderer создан кодом в тесте).
	/// </summary>
	private static TileSet _cachedWallTerrainTileSet;
	private static TileSet _cachedMountainTerrainTileSet;
	private static TileSet _cachedGardenBedTerrainTileSet;

	public static void ClearTileSetCaches()
	{
		_cachedWallTerrainTileSet = null;
		_cachedMountainTerrainTileSet = null;
		_cachedGardenBedTerrainTileSet = null;
	}

	public static TileSet GetWallTerrainTileSet()
	{
		if (_cachedWallTerrainTileSet != null && GodotObject.IsInstanceValid(_cachedWallTerrainTileSet))
			return _cachedWallTerrainTileSet;
		var tree = Engine.GetMainLoop() as SceneTree;
		var layer = (tree?.CurrentScene ?? tree?.Root)?.FindChild("wood_wall_tile", true, false) as TileMapLayer;
		_cachedWallTerrainTileSet = layer?.TileSet;
		return _cachedWallTerrainTileSet;
	}

	/// <summary>
	/// TileSet с terrain-разметкой гор из сцены (нода mountains_tile в Main.tscn,
	/// terrain_set "mountain0"). Стандарт: генерация гор в MapData, отрисовку
	/// атласа считает движок. Null — нода не найдена, fallback на кодовый TileSet.
	/// </summary>
	public static TileSet GetMountainTerrainTileSet()
	{
		if (_cachedMountainTerrainTileSet != null && GodotObject.IsInstanceValid(_cachedMountainTerrainTileSet))
			return _cachedMountainTerrainTileSet;
		var tree = Engine.GetMainLoop() as SceneTree;
		var layer = (tree?.CurrentScene ?? tree?.Root)?.FindChild("mountains_tile", true, false) as TileMapLayer;
		_cachedMountainTerrainTileSet = layer?.TileSet;
		return _cachedMountainTerrainTileSet;
	}

	/// <summary>
	/// TileSet с terrain-разметкой грядок из сцены (нода GardenBeds в Main.tscn).
	/// Автотайлинг грядок считает сам Godot через SetCellsTerrainConnect.
	/// </summary>
	public static TileSet GetGardenBedTerrainTileSet()
	{
		if (_cachedGardenBedTerrainTileSet != null && GodotObject.IsInstanceValid(_cachedGardenBedTerrainTileSet))
			return _cachedGardenBedTerrainTileSet;
		var tree = Engine.GetMainLoop() as SceneTree;
		var layer = (tree?.CurrentScene ?? tree?.Root)?.FindChild("GardenBeds", true, false) as TileMapLayer;
		_cachedGardenBedTerrainTileSet = layer?.TileSet;
		return _cachedGardenBedTerrainTileSet;
	}

	private void OnWallTilesUpdated(List<(int X, int Y)> changedTiles)
	{
		if (_wallLayer?.TileSet == null) return;

		// Построенные стены — через terrain-слой (стандарт). Тени сразу (дешёвый dirty-флаг).
		// exists=IsWallAt: соседние пустые клетки движок использует только как контекст,
		// SetCellsTerrainConnect сам решит что стереть/оставить — EraseCell здесь НЕ зовём.
		foreach (var (x, y) in changedTiles)
		{
			_builtWalls?.MarkDirty(new Vector2I(x, y));
			if (_wallBuildManager.IsWallAt(x, y))
				ShadowRenderer?.AddCaster(x, y);
			else
				ShadowRenderer?.RemoveCaster(x, y);
		}
	}

	private Texture2D LoadTexture(string pathOrUid, string name)
	{
		try
		{
			if (ResourceLoader.Exists(pathOrUid))
				return ResourceLoader.Load<Texture2D>(pathOrUid);
			string globalPath = ProjectSettings.GlobalizePath(pathOrUid);
			if (System.IO.File.Exists(globalPath))
			{
				var img = Image.LoadFromFile(globalPath);
				if (img != null)
					return ImageTexture.CreateFromImage(img);
			}
			return ResourceLoader.Load<Texture2D>(pathOrUid);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"MapRenderer: ошибка загрузки '{name}': {ex.Message}");
			return null;
		}
	}

	public override void _ExitTree()
	{
		TreeJobManager.Instance.OnTreeChopped -= OnTreeChopped;
		StoneJobManager.Instance.OnStoneMined -= OnStoneMined;
		OreJobManager.Instance.OnOreChanged -= OnOreCellChanged;
		OreJobManager.Instance.OnOreDepleted -= OnOreCellChanged;
		StockpileManager.Instance.OnZoneTileAdded -= _onZoneTileAdded;
		StockpileManager.Instance.OnZoneTileRemoved -= _onZoneTileRemoved;
		StockpileManager.Instance.OnZoneTilesBatchAdded -= _onZoneTilesBatchAdded;
		StockpileManager.Instance.OnZoneTilesBatchRemoved -= _onZoneTilesBatchRemoved;

		// FIX (bughunt 14-3): снимаем подписки ConstructionManager (были анонимными).
		ConstructionManager.Instance.OnSitesBatchAdded -= _onSitesBatchAdded;
		ConstructionManager.Instance.OnSiteAdded -= _onSiteAdded;
		ConstructionManager.Instance.OnSiteRemoved -= _onSiteRemoved;
		ConstructionManager.Instance.OnSitesBatchRemoved -= _onSitesBatchRemoved;
		ConstructionManager.Instance.OnSiteCompleted -= _onSiteCompleted;

		// FIX (bughunt 14-9): статический Instance не должен переживать свой узел —
		// иначе между сменами сцены он указывает на освобождённый MapRenderer.
		if (ReferenceEquals(Instance, this))
			Instance = null;

		BuildingManager.Instance.OnBuildingPlaced -= _onBuildingPlaced;
		BuildingManager.Instance.OnBuildingRemoved -= _onBuildingRemoved;
		StructureManager.Instance.OnStructureAdded -= _onStructureAdded;
		StructureManager.Instance.OnStructureRemoved -= _onStructureRemoved;

		FarmJobManager.Instance.OnPlotMarked -= _onPlotMarked;
		FarmJobManager.Instance.OnPlotsBatchMarked -= _onPlotsBatchMarked;
		FarmJobManager.Instance.OnPlotUnmarked -= _onPlotUnmarked;
		FarmJobManager.Instance.OnPlotsBatchUnmarked -= _onPlotsBatchUnmarked;
		FarmJobManager.Instance.OnPlotCompleted -= _onPlotCompleted;

		if (_wallBuildManager != null)
		{
			_wallBuildManager.OnTilesUpdated -= OnWallTilesUpdated;
			_wallBuildManager.Dispose();
			_wallBuildManager = null;
		}

		base._ExitTree();
	}
}
