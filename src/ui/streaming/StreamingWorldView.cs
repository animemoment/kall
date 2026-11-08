using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Game.Core;
using Game.Core.World;
using Game.Core.WorldStreaming;
using Game.Core.WorldStreaming.Integration;
using Game.UI;
using Godot;

namespace Game.UI.Streaming;

/// <summary>
/// Главнопоточный Node2D-вид бесконечного мира. ЗЕМЛЮ во всём мире (остров и за
/// кромкой) рисует шейдер terrain_blend.gdshader в MapRenderer («один рендер
/// земли», RENDER_SYNC_PLAN Фаза 2); этот вид кладёт ТОЛЬКО прозрачный ДЕКОР
/// (деревья/камни, атлас 8 тайлов) и метки игрока.
/// Дополнительно ведёт персистентные клеточные метки-«блоки» нескольких видов (стены, здания,
/// добытое, вырубленное, склады): изменения кодируются через WorldChunkDeltaCodec и сохраняются
/// при выгрузке чанка в WorldChunkStore, при повторном входе восстанавливаются из snapshot'а.
/// Никакого обращения к legacy MapGenerator/MapData/MapRenderer.
/// Реализует IWallWorld (контракт для StreamingWorldManager) и IBlockWorld
/// (мульти-kind доступ для менеджеров главной игры).
/// </summary>
public partial class StreamingWorldView : Node2D, IWallWorld, IBlockWorld, IWorldCellSource
{
	/// <summary>Kind записей delta для пользовательских постоянных меток (стен).</summary>
	public const byte WallKind = WorldDeltaKind.Wall;

	/// <summary>Пикселей текстуры на одну игровую клетку (сейчас — тайл 64×64 на клетку).</summary>
	// Рендер — настоящими тайлами игры: 1 клетка = один тайл 64×64 (TilePx).
	private const int TilePx = 64;

	[Export] public ulong WorldSeed = 0x0123456789ABCDEFUL;
	[Export] public uint GeneratorVersion = 3;
	[Export] public int MaxConcurrency = 4;
	// Hard per-frame budgets prevent a large batch of completed jobs / camera teleports
	// from turning into a single long main-thread frame.
	// §117/B3: 8→4 — применение готового чанка (декод snapshot + словари) вдвое
	// дешевле на кадр; дренирование теперь ТОЛЬКО из _Process (см. Update).
	[Export(PropertyHint.Range, "1,32,1")] public int MaxReadyChunksPerFrame = 4;
	[Export(PropertyHint.Range, "1,16,1")] public int MaxUnloadSchedulesPerFrame = 2;
	[Export] public string SaveDirectory = ""; // "" => user://infinite_world_lab_cache
	[Export] public bool WipeStoreOnStart = false; // true — стенд стартует с чистого хранилища
	// Окно стриминга подбирается под видимый прямоугольник (в чанках): меньше окно —
	// меньше resident-клеток, памяти и работы по тайлам.
	[Export] public int MinWindowChunks = 3;
	[Export] public int MaxWindowChunks = 16;
	// Each operation is a bounded slice, not a whole 64×64 chunk.
	[Export(PropertyHint.Range, "1,16,1")] public int MaxChunkOpsPerFrame = 4;
	[Export(PropertyHint.Range, "128,4096,128")] public int MaxCellsPerPaintSlice = 4096;
	[Export(PropertyHint.Range, "128,4096,128")] public int MaxCellsErasedPerFrame = 2048;
	public Func<ChunkKey, bool> IsChunkPinned { get; set; }

	private ChunkWindowPlanner _planner;
	private WorldChunkStore _store;
	private WorldChunkStreamer _streamer;
	private TileSet _tileSet;
	/// <summary>
	/// Слой ДЕКОРА (деревья/камни, прозрачный атлас): земля во всём мире рисует
	/// terrain_blend.gdshader (MapRenderer). ZIndex=2 при виде на ZIndex=-1 ⇒
	/// эффективный z=+1 — НАД шейдерной землёй MapRenderer (z=0) и над тайлами
	/// острова (пространственно не пересекается: внутри острова гейт PaintCell).
	/// </summary>
	private TileMapLayer _decorLayer;
	private TileMapLayer _markLayer;

	// task-8: руда мира. Базовое поле — детерминированное OreWorldField (чистая
	// функция абсолютных координат), поверх — OreWorldState (мутации: добыто).
	// Чанковые массивы считаются в фоне, рисуются по готовности.
	private TileMapLayer _oreLayer;
	private OreTileSetBuilder _oreTiles;
	private readonly Dictionary<ChunkKey, OreWorldField.ChunkOre> _oreArrays = new();
	private readonly Dictionary<ChunkKey, Task<OreArrays>> _oreJobs = new();
	private readonly HashSet<ChunkKey> _oreReady = new();
	private readonly HashSet<ChunkKey> _orePainted = new();

	private sealed class OreArrays
	{
		public ChunkKey Key;
		public OreWorldField.ChunkOre Center;
		public OreWorldField.ChunkOre N;
		public OreWorldField.ChunkOre S;
		public OreWorldField.ChunkOre E;
		public OreWorldField.ChunkOre W;
	}

	/// <summary>Слой чертежей (§29): план рисуется полупрозрачно, готовое — обычными слоями.</summary>
	private TileMapLayer _planLayer;
	// Горы, стены и грядки — свои слои с TileSet из сцен (terrain-автотайлинг, как на острове).
	private TileMapLayer _mountainLayer;
	private TileMapLayer _wallLayer;
	private TileMapLayer _wallPlanLayer;
	private TileMapLayer _farmLayer;
	private TileMapLayer _farmPlanLayer;
	private TerrainTileLayer _mountainTerrain;
	private TerrainTileLayer _wallTerrain;
	private TerrainTileLayer _wallPlanTerrain;
	private TerrainTileLayer _farmTerrain;
	private TerrainTileLayer _farmPlanTerrain;
	private readonly Dictionary<ChunkKey, WorldChunk> _chunkData = new();

	/// <summary>Загруженные персистентные метки: чанк → kind → множество локальных индексов.</summary>
	private readonly Dictionary<ChunkKey, Dictionary<byte, HashSet<ushort>>> _blocks = new();
	private readonly HashSet<ChunkKey> _textureDirty = new();
	// Reused instead of allocating a new array each time a batch of chunks arrives.
	private readonly List<ChunkKey> _dirtyBuffer = new();
	// A just-loaded chunk has no overlay tiles to clear; skip redundant EraseCell calls.
	private readonly HashSet<ChunkKey> _freshPaint = new();
	// §117/B3: чанки, которые когда-либо клали тайлы в наши слои (чанковая нарезка или
	// одиночная перекраска). Erase-очередь на выгрузке гоняется ТОЛЬКО для них: свежие
	// и околониевые (IsOccludedByColony) чанки тайлов не клали, а полный проход
	// 4096 EraseCell по каждому сжигал MaxCellsErasedPerFrame вхолостую.
	private readonly HashSet<ChunkKey> _paintedChunks = new();
	// §117/F4: кэш сетки плодородия 5×5 на чанк (шаг 16, как в GenerateChunk/
	// GenerateColonyWindow). GeneratedCell плодородия не хранит, а семплы
	// SampleBlendedCell нужны только 25 на чанк — строится лениво, при первом декоре.
	private readonly ushort[] _fertilityGrid = new ushort[FertilityGridSide * FertilityGridSide];
	private ChunkKey _fertilityChunk;
	private bool _fertilityValid;
	private readonly Queue<ChunkKey> _eraseQueue = new();
	private readonly HashSet<ChunkKey> _eraseScheduled = new();
	private readonly Dictionary<ChunkKey, ChunkEraseJob> _eraseJobs = new();
	private readonly Queue<ChunkKey> _paintQueue = new();
	private readonly HashSet<ChunkKey> _paintQueued = new();
	private readonly Dictionary<ChunkKey, ChunkPaintJob> _paintJobs = new();
	private long _focusCellX;
	private long _focusCellY;
	private readonly Queue<ChunkKey> _exitQueue = new();
	private readonly HashSet<ChunkKey> _exitScheduled = new();
	private readonly List<(ChunkKey Key, Task Task)> _unloadTasks = new();
	private readonly ChunkStreamResult[] _readyBuffer = new ChunkStreamResult[64];
	private readonly ChunkStreamFailure[] _failureBuffer = new ChunkStreamFailure[8];
	private ChunkWindowPlan _lastPlan;
	private bool _initialized;

	public ulong ActiveSeed => WorldSeed;
	public uint ActiveGeneratorVersion => GeneratorVersion;
	public int LoadedChunks => _chunkData.Count;
	public int WallCount { get; private set; }

	public override void _Ready()
	{
		string dir = SaveDirectory;
		if (string.IsNullOrWhiteSpace(dir))
		{
			string userDir = ProjectSettings.GlobalizePath("user://infinite_world_lab_cache");
			dir = System.IO.Path.Combine(userDir, $"world_{WorldSeed:x16}");
		}
		if (WipeStoreOnStart && System.IO.Directory.Exists(dir))
		{
			try
			{
				System.IO.Directory.Delete(dir, recursive: true);
				GD.Print($"[World] wiped store: {dir}");
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[World] store wipe failed: {ex.Message}");
			}
		}
		_planner = new ChunkWindowPlanner();
		_store = new WorldChunkStore(dir);
		// Выделяем до (ядер - 2) воркеров (от 2 до 8) для высокой пропускной способности без конкуренции за CPU
		int workerLimit = Math.Clamp(System.Environment.ProcessorCount - 2, 2, 8);
		int workerCount = Math.Clamp(MaxConcurrency, 1, workerLimit);
		_streamer = new WorldChunkStreamer(_planner, WorldSeed, GeneratorVersion, _store, workerCount);
		BuildTileLayers();
		OreWorldState.CellChanged += OnOreStateChanged;
		_initialized = true;
	}

	/// <summary>
	/// Сбросить кэш чанков, очереди и слои при смене сида мира или выборе нового региона.
	/// Гарантирует немедленную очистку устаревших чанков и бесшовный стриминг новой зоны.
	/// </summary>
	public void ResetWorld(ulong newSeed = 0)
	{
		// P0 (bughunt 05-1, регрессия R-053): ДО очистки кэша сохраняем несохранённые
		// правки resident-чанков. Раньше _chunkData/_blocks чистились, а
		// FlushResidentDeltas вызывался только из _ExitTree → стены и постройки,
		// поставленные в текущем окне, исчезали при «Карта мира → тот же регион → назад».
		// Флашим ДО смены WorldSeed: сохраняем в каталог того мира, где строили.
		try
		{
			int flushed = FlushResidentDeltas();
			if (flushed > 0)
				GD.Print($"[World] ResetWorld: сохранено {flushed} чанков перед сменой мира");
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[World] ResetWorld: не удалось сохранить чанки — {ex.Message}");
		}

		if (newSeed != 0)
			WorldSeed = newSeed;

		_eraseQueue.Clear();
		_eraseScheduled.Clear();
		_eraseJobs.Clear();
		_paintQueue.Clear();
		_paintQueued.Clear();
		_paintJobs.Clear();
		_freshPaint.Clear();
		_textureDirty.Clear();
		_exitQueue.Clear();
		_exitScheduled.Clear();
		_dirtyBuffer.Clear();
		_paintedChunks.Clear();
		_fertilityValid = false;

		_decorLayer?.Clear();
		_markLayer?.Clear();
		_oreLayer?.Clear();
		_oreArrays.Clear();
		_oreReady.Clear();
		_orePainted.Clear();
		_oreJobs.Clear();
		_decorRepairQueue.Clear();
		_decorRepairQueued.Clear();
		_decorRepairCursor.Clear();
		// Смена мира: мутации руды прошлого мира уже сохранены FlushResidentDeltas.
		OreWorldState.Clear();
		// Кэш поля адресуется локальными чанками — при смене origin он невалиден.
		OreWorldField.ClearCache();
		_planLayer?.Clear();
		_mountainLayer?.Clear();
		_wallLayer?.Clear();
		_wallPlanLayer?.Clear();
		_farmLayer?.Clear();
		_farmPlanLayer?.Clear();
		_mountainTerrain?.Clear();
		_wallTerrain?.Clear();
		_wallPlanTerrain?.Clear();
		_farmTerrain?.Clear();
		_farmPlanTerrain?.Clear();

		_chunkData.Clear();
		_blocks.Clear();
		WallCount = 0;
		_lastPlan = default;

		if (_streamer != null)
		{
			_streamer.CancelPending();
			try
			{
				_streamer.DisposeAsync().AsTask().Wait(2000);
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[StreamingWorldView] Ошибка ожидания закрытия streamer: {ex.Message}");
			}
			_streamer = null;
		}

		string dir = SaveDirectory;
		if (string.IsNullOrWhiteSpace(dir))
		{
			string userDir = ProjectSettings.GlobalizePath("user://infinite_world_lab_cache");
			dir = System.IO.Path.Combine(userDir, $"world_{WorldSeed:x16}");
		}
		_planner = new ChunkWindowPlanner();
		_store = new WorldChunkStore(dir);
		int workerLimit = Math.Clamp(System.Environment.ProcessorCount - 2, 2, 8);
		int workerCount = Math.Clamp(MaxConcurrency, 1, workerLimit);
		_streamer = new WorldChunkStreamer(_planner, WorldSeed, GeneratorVersion, _store, workerCount);
	}

	public override void _Process(double delta)
	{
		if (!_initialized)
			return;

		// Непрерывно каждый кадр: дренируем завершившиеся результаты из воркеров
		int readyBudget = Math.Clamp(MaxReadyChunksPerFrame, 1, _readyBuffer.Length);
		int readyCount = _streamer.DrainReady(_readyBuffer.AsSpan(0, readyBudget), readyBudget);
		for (int i = 0; i < readyCount; i++)
			ApplyReady(_readyBuffer[i]);

		int failureCount = _streamer.DrainFailures(_failureBuffer.AsSpan(0, _failureBuffer.Length), _failureBuffer.Length);
		for (int i = 0; i < failureCount; i++)
			GD.PrintErr($"[World] chunk failure {_failureBuffer[i].Key}: {_failureBuffer[i].Message}");

		ProcessExits();
		RepaintDirty();

		// water_time больше не нужен: streaming_ground.gdshader уволен (Фаза 2:
		// «один рендер земли»), воду за кромкой анимирует terrain_blend.gdshader
		// в MapRenderer — его water_time ведёт сам MapRenderer.
	}

	/// <summary>Применить новое видимое окно, обновить план окна.</summary>
	public void Update(WorldRect visibleTileBounds)
	{
		if (!_initialized)
			return;

		long currentFocusX = (visibleTileBounds.MinX + visibleTileBounds.MaxX) / 2;
		long currentFocusY = (visibleTileBounds.MinY + visibleTileBounds.MaxY) / 2;

		_focusCellX = currentFocusX;
		_focusCellY = currentFocusY;
		ApplyAdaptiveWindow(visibleTileBounds);
		// S-08: даже с корректным клампом планировщик может отвергнуть прямоугольник
		// (его контракт — «видимая область с запасом помещается в целое число
		// выровненных чанков»). Раньше ArgumentException из недр Update летел прямо
		// в игровой цикл; теперь пропускаем кадр и логируем один раз.
		try
		{
			_lastPlan = _streamer.Update(ClampVisibleToWindow(visibleTileBounds));
		}
		catch (ArgumentException ex)
		{
			LogStreamingWindowErrorOnce(ex);
			return;
		}

		foreach (ChunkKey key in _lastPlan.Exited)
		{
			if (_streamer.IsResident(key) && !_streamer.IsDesired(key) && !_exitScheduled.Contains(key))
			{
				_exitQueue.Enqueue(key);
				_exitScheduled.Add(key);
			}
		}

		// §117/B3: дренирование готовых чанков, выгрузки и покраска — ТОЛЬКО в _Process.
		// Main (родитель) вызывает Update раньше нашего _Process в том же кадре, поэтому
		// отложенная обработка попадает на экран в том же кадре, зато все кадровые бюджеты
		// (MaxReadyChunksPerFrame, MaxCellsErasedPerFrame, 4 мс нарезки) стали действительно
		// ПОКАДРОВЫМИ: раньше в кадрах с Update порции дренировались и RepaintDirty
		// запускался дважды (Update + _Process), удваивая работу главного потока.
	}

	/// <summary>Добавить персистентную метку-блок в клетку; чанк должен быть загружен.</summary>
	public bool AddBlock(byte kind, long cellX, long cellY)
	{
		if (!_initialized)
			return false;
		ChunkKey key = WorldCoordinates.ChunkForTile(cellX, cellY);
		if (!_chunkData.ContainsKey(key))
			return false;
		HashSet<ushort> set = GetOrCreateSet(key, kind);
		LocalCell local = WorldCoordinates.LocalForTile(cellX, cellY);
		if (!set.Add((ushort)(local.Y * WorldCoordinates.ChunkSize + local.X)))
			return false;
		if (kind == WallKind)
		{
			WallCount++;
			Game.Core.World.WorldCostCache.MarkBlocked(cellX, cellY);
			OnWallAdded?.Invoke(cellX, cellY);
		}
		RepaintCell(cellX, cellY);
		OnBlockAdded?.Invoke(kind, cellX, cellY);
		return true;
	}

	/// <summary>Добавить постоянную стену (удобная обёртка над AddBlock).</summary>
	public bool AddWall(long cellX, long cellY) => AddBlock(WallKind, cellX, cellY);

	public bool HasBlock(byte kind, long cellX, long cellY)
	{
		ChunkKey key = WorldCoordinates.ChunkForTile(cellX, cellY);
		HashSet<ushort> set = KindSet(key, kind);
		if (set == null)
			return false;
		LocalCell local = WorldCoordinates.LocalForTile(cellX, cellY);
		return set.Contains((ushort)(local.Y * WorldCoordinates.ChunkSize + local.X));
	}

	public bool HasWall(long cellX, long cellY) => HasBlock(WallKind, cellX, cellY);

	/// <summary>Снять персистентную метку-блок; false, если её нет или чанк не загружен.</summary>
	public bool RemoveBlock(byte kind, long cellX, long cellY)
	{
		if (!_initialized)
			return false;
		ChunkKey key = WorldCoordinates.ChunkForTile(cellX, cellY);
		HashSet<ushort> set = KindSet(key, kind);
		if (set == null)
			return false;
		LocalCell local = WorldCoordinates.LocalForTile(cellX, cellY);
		ushort index = (ushort)(local.Y * WorldCoordinates.ChunkSize + local.X);
		if (set.Remove(index))
		{
			if (kind == WallKind)
			{
				WallCount--;
				if (WallCount < 0)
					WallCount = 0;
				Game.Core.World.WorldCostCache.MarkUnblocked(cellX, cellY);
				OnWallRemoved?.Invoke(cellX, cellY);
			}
			RepaintCell(cellX, cellY);
			OnBlockRemoved?.Invoke(kind, cellX, cellY);
			return true;
		}
		return false;
	}

	/// <summary>Снять постоянную стену (удобная обёртка над RemoveBlock).</summary>
	public bool RemoveWall(long cellX, long cellY) => RemoveBlock(WallKind, cellX, cellY);

	/// <summary>
	/// Собрать чертежи, по которым ещё нет готового (§29): очередь задач для людей мира.
	/// Только загруженные чанки; ближайшие к фокусу — первыми; не больше <paramref name="max"/> клеток.
	/// </summary>
	public int CollectPendingPlans(List<WorldPlan> into, long focusX, long focusY, int max)
	{
		into.Clear();
		if (max <= 0)
			return 0;
		_planFocusX = focusX;
		_planFocusY = focusY;
		_planComparer ??= new PlanFocusComparer(this);

		// Keep only the closest `max` candidates while scanning. The previous implementation
		// materialized and sorted every pending plan (O(N log N) time and O(N) memory), even
		// when callers needed only a handful. The max-heap costs O(N log max) and O(max) memory.
		foreach (KeyValuePair<ChunkKey, Dictionary<byte, HashSet<ushort>>> pair in _blocks)
		{
			if (!_chunkData.ContainsKey(pair.Key))
				continue;
			Dictionary<byte, HashSet<ushort>> map = pair.Value;
			foreach (byte planKind in PlanKinds)
			{
				if (!map.TryGetValue(planKind, out HashSet<ushort> set) || set.Count == 0)
					continue;
				map.TryGetValue(WorldDeltaKind.DoneFor(planKind), out HashSet<ushort> done);
				foreach (ushort index in set)
				{
					if (done != null && done.Contains(index))
						continue;
					int localX = index % WorldCoordinates.ChunkSize;
					int localY = index / WorldCoordinates.ChunkSize;
					AddPlanCandidate(into, new WorldPlan(planKind,
						pair.Key.X * (long)WorldCoordinates.ChunkSize + localX,
						pair.Key.Y * (long)WorldCoordinates.ChunkSize + localY), max);
				}
			}
		}
		if (into.Count > 1)
			into.Sort(_planComparer);
		return into.Count;
	}

	private static readonly byte[] PlanKinds =
	{
		WorldDeltaKind.WallPlan,
		WorldDeltaKind.BuildingPlan,
		WorldDeltaKind.CropPlan,
		WorldDeltaKind.StockpilePlan,
		WorldDeltaKind.CutTreePlan,
		WorldDeltaKind.MinedStonePlan
	};

	private long _planFocusX;
	private long _planFocusY;
	private PlanFocusComparer _planComparer;

	/// <summary>Сортировка чертежей по близости к фокусу (камера/игрок).</summary>
	private sealed class PlanFocusComparer : IComparer<WorldPlan>
	{
		private readonly StreamingWorldView _view;

		public PlanFocusComparer(StreamingWorldView view) => _view = view;

		public int Compare(WorldPlan a, WorldPlan b)
			=> Distance(a, _view._planFocusX, _view._planFocusY)
				.CompareTo(Distance(b, _view._planFocusX, _view._planFocusY));

		private static double Distance(WorldPlan plan, long focusX, long focusY)
		{
			// Convert before subtracting: signed long subtraction/squaring can overflow
			// for valid world coordinates far from the current focus.
			double dx = (double)plan.CellX - focusX;
			double dy = (double)plan.CellY - focusY;
			return dx * dx + dy * dy;
		}
	}

	private void AddPlanCandidate(List<WorldPlan> heap, WorldPlan candidate, int max)
	{
		if (heap.Count < max)
		{
			heap.Add(candidate);
			int i = heap.Count - 1;
			while (i > 0)
			{
				int parent = (i - 1) / 2;
				if (_planComparer.Compare(heap[i], heap[parent]) <= 0)
					break;
				(heap[i], heap[parent]) = (heap[parent], heap[i]);
				i = parent;
			}
			return;
		}

		// The root is the farthest retained candidate.
		if (_planComparer.Compare(candidate, heap[0]) >= 0)
			return;
		heap[0] = candidate;
		int root = 0;
		while (true)
		{
			int left = root * 2 + 1;
			if (left >= heap.Count)
				break;
			int right = left + 1;
			int farther = right < heap.Count
				&& _planComparer.Compare(heap[right], heap[left]) > 0 ? right : left;
			if (_planComparer.Compare(heap[farther], heap[root]) <= 0)
				break;
			(heap[root], heap[farther]) = (heap[farther], heap[root]);
			root = farther;
		}
	}

	/// <summary>Число загруженных в память меток данного вида.</summary>
	public int BlockCount(byte kind)
	{
		int count = 0;
		foreach (KeyValuePair<ChunkKey, Dictionary<byte, HashSet<ushort>>> pair in _blocks)
		{
			if (pair.Value.TryGetValue(kind, out HashSet<ushort> set))
				count += set.Count;
		}
		return count;
	}

	/// <summary>Есть ли данные чанка в памяти; его покраска может продолжаться по кадрам.</summary>
	public bool IsLoaded(ChunkKey key) => _chunkData.ContainsKey(key);

	/// <summary>Чанк клетки загружен (контракт единого доступа к миру, §26.1).</summary>
	public bool CoversChunk(long cellX, long cellY)
		=> _chunkData.ContainsKey(WorldCoordinates.ChunkForTile(cellX, cellY));

	/// <summary>Исходные поля клетки из resident-чанка; false — чанк не загружен.</summary>
	public bool TryCellInfo(long cellX, long cellY, out GeneratedCell cell)
	{
		ChunkKey key = WorldCoordinates.ChunkForTile(cellX, cellY);
		if (!_chunkData.TryGetValue(key, out WorldChunk chunk))
		{
			cell = default;
			return false;
		}
		LocalCell local = WorldCoordinates.LocalForTile(cellX, cellY);
		cell = chunk.Cells.Span[local.Y * WorldCoordinates.ChunkSize + local.X];
		return true;
	}

	/// <summary>Существует ли сохранённый delta-файл чанка в хранилище.</summary>
	public bool IsChunkStored(ChunkKey key) => _store.ContainsChunk(key.X, key.Y);

	/// <summary>FNV-1a 64 по всем детерминированным полям клеток загруженного чанка (0, если не загружен).</summary>
	public ulong ChunkHash(ChunkKey key)
	{
		if (!_chunkData.TryGetValue(key, out WorldChunk chunk))
			return 0;
		ulong hash = 0xcbf29ce484222325ul;
		ReadOnlySpan<GeneratedCell> cells = chunk.Cells.Span;
		foreach (ref readonly GeneratedCell cell in cells)
		{
			hash = FoldByte(hash, (byte)cell.Terrain);
			hash = FoldByte(hash, (byte)(cell.ElevationQ16 & 0xFF));
			hash = FoldByte(hash, (byte)(cell.ElevationQ16 >> 8));
			hash = FoldByte(hash, (byte)(cell.MoistureQ16 & 0xFF));
			hash = FoldByte(hash, (byte)(cell.MoistureQ16 >> 8));
			hash = FoldByte(hash, (byte)(cell.ForestQ16 & 0xFF));
			hash = FoldByte(hash, (byte)(cell.ForestQ16 >> 8));
			hash = FoldByte(hash, (byte)(cell.StoneQ16 & 0xFF));
			hash = FoldByte(hash, (byte)(cell.StoneQ16 >> 8));
		}
		return hash;
	}

	private static ulong FoldByte(ulong hash, byte value)
	{
		hash ^= value;
		return hash * 0x100000001b3ul;
	}

	/// <summary>Сводка по типам поверхности всех загруженных чанков (проверка воды/гор/травы).</summary>
	public string TerrainSummary()
	{
		long water = 0, grass = 0, mountain = 0;
		foreach (KeyValuePair<ChunkKey, WorldChunk> pair in _chunkData)
		{
			ReadOnlySpan<GeneratedCell> cells = pair.Value.Cells.Span;
			foreach (ref readonly GeneratedCell cell in cells)
			{
				switch (cell.Terrain)
				{
					case BaseTerrainKind.Water: water++; break;
					case BaseTerrainKind.Mountain: mountain++; break;
					default: grass++; break;
				}
			}
		}
		long total = water + grass + mountain;
		if (total == 0)
			return "water=0 grass=0 mountain=0";
		return $"water={100.0 * water / total:F1}% grass={100.0 * grass / total:F1}% mountain={100.0 * mountain / total:F1}%";
	}

	public override string ToString() => $"seed={WorldSeed:X16} ver={GeneratorVersion} loaded={LoadedChunks} walls={WallCount}";

	/// <summary>Блокирующий останов стримера (workers не трогают Godot API).</summary>
	public void Shutdown()
	{
		if (!_initialized)
			return;
		_streamer.DisposeAsync().AsTask().GetAwaiter().GetResult();
		_initialized = false;
	}

	// --- §118 фаза 2: события изменений (аудит WorldDeltaApi §6.7 — в мировом слое их не было) ---

	/// <summary>Метка добавлена в загруженную клетку: (kind, cellX, cellY).</summary>
	public event Action<byte, long, long> OnBlockAdded;

	/// <summary>Метка снята: (kind, cellX, cellY).</summary>
	public event Action<byte, long, long> OnBlockRemoved;

	/// <summary>Стена поставлена: (cellX, cellY) — частный случай OnBlockAdded(Wall).</summary>
	public event Action<long, long> OnWallAdded;

	/// <summary>Стена снята: (cellX, cellY) — частный случай OnBlockRemoved(Wall).</summary>
	public event Action<long, long> OnWallRemoved;

	/// <summary>
	/// §118 фаза 2 (аудит §6.4): принудительно сохранить дельты ВСЕХ resident-чанков,
	/// включая desired (UnloadChunkAsync их не принимает — поэтому штатный путь при
	/// выходе терял правки текущего окна). Пишет напрямую в стор вида той же атомарной
	/// заменой, что и штатная выгрузка. Идемпотентен; ошибки пишутся в лог и не
	/// прерывают цепочку выхода. Вызывать ДО Shutdown().
	/// Пустая память + файла на диске нет → диск не трогаем (аудит §6.9: пустой
	/// payload удаляет файл — нельзя стереть чужие валидные дельты).
	/// </summary>
	/// <returns>Число чанков с непустыми записанными дельтами.</returns>
	public int FlushResidentDeltas(double gameTimeCheckpoint = 0.0)
	{
		if (!_initialized || _store == null)
			return 0;
		var metadata = new WorldSaveMetadata(
			WorldChunkCodec.CurrentFormatVersion, GeneratorVersion,
			UnifiedWorldGenerator.FeatureSchemaVersion, WorldSeed,
			GameTimeCheckpoint: gameTimeCheckpoint);
		int saved = 0;
		foreach (KeyValuePair<ChunkKey, WorldChunk> pair in _chunkData)
		{
			try
			{
				byte[] payload = EncodeBlocks(pair.Key);
				if (payload.Length == 0 && !_store.ContainsChunk(pair.Key.X, pair.Key.Y))
					continue;
				if (_store.SaveChunk(metadata, pair.Key.X, pair.Key.Y, payload))
					saved++;
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[World] FlushResidentDeltas {pair.Key}: {ex.Message}");
			}
		}
		if (saved > 0)
			GD.Print($"[World] FlushResidentDeltas: {saved} чанков сохранено (gameTime={gameTimeCheckpoint:0.0}s)");
		return saved;
	}

	/// <summary>
	/// §118 фаза 2 (аудит §6.2): перечислить метки resident-чанков в прямоугольнике
	/// (глобальные координаты). kind == 0 → все виды. Дисковое (нерезидентное)
	/// перечисление появится вместе с материализатором фазы 3.
	/// </summary>
	public int EnumerateDeltas(WorldRect area, byte kind, List<(byte Kind, long CellX, long CellY)> into)
	{
		if (into == null)
			return 0;
		into.Clear();
		if (_blocks.Count == 0)
			return 0;
		ChunkKey min = WorldCoordinates.ChunkForTile(area.MinX, area.MinY);
		ChunkKey max = WorldCoordinates.ChunkForTile(area.MaxX, area.MaxY);
		for (long cy = min.Y; cy <= max.Y; cy++)
		{
			for (long cx = min.X; cx <= max.X; cx++)
			{
				if (!_blocks.TryGetValue(new ChunkKey(cx, cy), out Dictionary<byte, HashSet<ushort>> map))
					continue;
				long originX = cx * (long)WorldCoordinates.ChunkSize;
				long originY = cy * (long)WorldCoordinates.ChunkSize;
				foreach (KeyValuePair<byte, HashSet<ushort>> kindSet in map)
				{
					if (kind != 0 && kindSet.Key != kind)
						continue;
					foreach (ushort index in kindSet.Value)
					{
						long cellX = originX + index % WorldCoordinates.ChunkSize;
						long cellY = originY + index / WorldCoordinates.ChunkSize;
						if (cellX < area.MinX || cellX > area.MaxX || cellY < area.MinY || cellY > area.MaxY)
							continue;
						into.Add((kindSet.Key, cellX, cellY));
					}
				}
			}
		}
		return into.Count;
	}

	private void ApplyReady(ChunkStreamResult result)
	{
		ChunkKey key = result.Key;
		bool wasLoaded = _chunkData.ContainsKey(key);
		// Снимок delta, если чанк сохранялся ранее (постоянные изменения).
		if (result.Snapshot != null)
		{
			if (WorldChunkDeltaCodec.TryDecode(result.Snapshot.Payload, out WorldChunkDelta delta, out string error))
			{
				var map = new Dictionary<byte, HashSet<ushort>>();
				foreach (WorldChunkDeltaRecord record in delta.Records)
				{
					if (record.Kind == WorldDeltaKind.OreMined)
					{
						// task-8: остаток запаса руды (0 = выработано) переживает выход.
						if (OreWorldState.TryDecodePayload(record.Data, out ushort oreReserve))
							ApplyOreRecord(key, record.LocalIndex, oreReserve);
						continue;
					}
					if (!map.TryGetValue(record.Kind, out HashSet<ushort> set))
					{
						set = new HashSet<ushort>();
						map[record.Kind] = set;
					}
					set.Add(record.LocalIndex);
				}

				// A re-delivered snapshot replaces, rather than double-counts, its old walls.
				if (_blocks.TryGetValue(key, out Dictionary<byte, HashSet<ushort>> previous)
					&& previous.TryGetValue(WallKind, out HashSet<ushort> oldWalls))
					WallCount = Math.Max(0, WallCount - oldWalls.Count);
				_blocks.Remove(key);
				if (map.Count > 0)
				{
					_blocks[key] = map;
					if (map.TryGetValue(WallKind, out HashSet<ushort> walls))
						WallCount += walls.Count;
				}
			}
			else
			{
				GD.PrintErr($"[World] delta decode failed for {key}: {error}");
			}
		}

		_paintJobs.Remove(key); // any queued token will pick up the replacement job
		_chunkData[key] = result.Chunk;
		// Фикс гонки: ore-массивы могли быть готовы РАНЬШЕ данных чанка —
		// PaintOreChunk тогда молча вышел, а Scheduling по _oreReady уже запрещён.
		EnsureOrePainted(key);
		if (wasLoaded)
			_freshPaint.Remove(key);
		else
			_freshPaint.Add(key);

		// Тайлы ставит RepaintDirty (главный поток) — здесь только помечаем чанк.
		_textureDirty.Add(key);
	}

	private void ProcessExits()
	{
		int scheduleBudget = Math.Clamp(MaxUnloadSchedulesPerFrame, 1, 16);
		while (_exitQueue.Count > 0 && scheduleBudget > 0)
		{
			ChunkKey key = _exitQueue.Peek();
			if (!_streamer.IsResident(key) || _streamer.IsDesired(key))
			{
				_exitQueue.Dequeue();
				_exitScheduled.Remove(key);
				continue;
			}

			if (IsChunkPinned != null && IsChunkPinned(key))
			{
				_exitQueue.Dequeue();
				_exitScheduled.Remove(key);
				continue;
			}

			Task task;
			try
			{
				task = _streamer.UnloadChunkAsync(key, EncodeBlocks(key), MakeMetadata());
			}
			catch (InvalidOperationException)
			{
				// Очередь сохранений переполнена — попробуем в следующем кадре.
				break;
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[World] unload schedule failed {key}: {ex.Message}");
				_exitQueue.Dequeue();
				_exitScheduled.Remove(key);
				continue;
			}

			_unloadTasks.Add((key, task));
			_exitQueue.Dequeue();
			_exitScheduled.Remove(key);
			scheduleBudget--;
		}

		for (int i = _unloadTasks.Count - 1; i >= 0; i--)
		{
			var (k, t) = _unloadTasks[i];
			if (!t.IsCompleted)
				continue;
			if (t.IsFaulted)
			{
				GD.PrintErr($"[World] unload task failed for {k}: {t.Exception?.GetBaseException().Message}");
				if (!_exitScheduled.Contains(k))
				{
					_exitQueue.Enqueue(k);
					_exitScheduled.Add(k);
				}
			}
			else
			{
				// FIX (bughunt 15-3): unload завершился, но чанк мог УЖЕ вернуться в окно и
				// быть перерисован (стример после unload успевает перегенерить и отдать его
				// заново). Безусловный RemoveChunkTiles стирал свежие _chunkData/_blocks/
				// _textureDirty/_paintJobs → пустой чанк до выхода из окна; далее EncodeBlocks
				// от пустого чанка давал SaveChunk(empty), а тот УДАЛЯЕТ файл дельты
				// (WorldChunkStore.cs) — безвозвратная потеря стен и построек.
				if (!_chunkData.ContainsKey(k))
					RemoveChunkTiles(k);
			}
			_unloadTasks.RemoveAt(i);
		}
	}

	private void RemoveChunkTiles(ChunkKey key)
	{
		MarkTerrainForUnload(key);

		long originX = key.X * (long)WorldCoordinates.ChunkSize;
		long originY = key.Y * (long)WorldCoordinates.ChunkSize;
		// §117/B3: erase нужен только чанкам, которые реально клали тайлы (колониальная
		// окклюзия и выгрузка до первой нарезки тайлов не клала). Холостой полный проход
		// 4096 EraseCell на каждый такой чанк выжигал бюджет MaxCellsErasedPerFrame.
		if (FitsTileMap(originX, originY) && _paintedChunks.Contains(key))
		{
			_blocks.TryGetValue(key, out Dictionary<byte, HashSet<ushort>> oldMap);
			ScheduleEraseChunk(key,
				CollectOverlayIndices(oldMap, plans: false),
				CollectOverlayIndices(oldMap, plans: true));
		}

		_paintedChunks.Remove(key);
		_chunkData.Remove(key);
		if (_blocks.Remove(key, out Dictionary<byte, HashSet<ushort>> map)
			&& map.TryGetValue(WallKind, out HashSet<ushort> walls))
			WallCount = Math.Max(0, WallCount - walls.Count);
		_textureDirty.Remove(key);
		_freshPaint.Remove(key);
		_paintJobs.Remove(key);
		// task-8: руда выгружаемого чанка (массивы в кэше поля остаются).
		_orePainted.Remove(key);
		_oreReady.Remove(key);
		_oreJobs.Remove(key);
		_oreArrays.Remove(key);
	}

	/// <summary>
	/// Подобрать сторону окна под видимый прямоугольник с запасом планировщика.
	/// Раньше окно было жёстко 16×16 чанков = 1024×1024 клетки ≈ 1M тайлов даже тогда,
	/// когда видно ~60×34 клетки.
	/// </summary>
	private void ApplyAdaptiveWindow(WorldRect visible)
	{
		long margin = _planner.MarginTiles;
		long needW = ChunkSpan(visible.MinX - margin, visible.MaxX + margin);
		long needH = ChunkSpan(visible.MinY - margin, visible.MaxY + margin);
		long need = Math.Max(needW, needH);

		// §117/B7: zoom-исключение «окно 24 при zoom<0.25» УБРАНО (выбран вариант «проще
		// и надёжнее» вместо гистерезиса 0.20/0.30). Обоснование: окно MaxWindowChunks=16
		// чанков = 1024×1024 клетки покрывает весь экран уже на минимальном зуме 0.12
		// (1920/0.12/64 ≈ 250 видимых тайлов ширины, 4K ≈ 500 — запас ×2), а порог без
		// гистерезиса сам был источником дребезга: у zoom≈0.25 сторона прыгала 16↔24,
		// SetWindowSide при смене инвалидирует якорь плана → полный rebuild окна на каждое
		// переключение. Теперь сторона зависит только от видимого прямоугольника (при зуме
		// меняется редко и монотонно), и из горячего пути Update ушёл лишний поиск камеры.
		int side = (int)Math.Clamp(need, MinWindowChunks, MaxWindowChunks);
		_planner.SetWindowSide(side);
	}

	/// <summary>Сколько чанков покрывает полуинтервал клеток [minCell, maxCellExclusive).</summary>
	private static long ChunkSpan(long minCell, long maxCellExclusive)
	{
		long first = WorldCoordinates.FloorDiv(minCell, WorldCoordinates.ChunkSize);
		long last = WorldCoordinates.FloorDiv(maxCellExclusive - 1, WorldCoordinates.ChunkSize);
		return last - first + 1;
	}

	/// <summary>
	/// Планировщик требует, чтобы видимая область с запасом помещалась в целое число выровненных
	/// чанков. Если она больше максимального окна — сужаем прямоугольник вокруг центра (дальние
	/// края дозаполнятся при движении камеры), а не падаем исключением из планировщика.
	/// </summary>
	/// <summary>
	/// S-08: ошибка контракта окна логируется ОДИН раз, а не каждый кадр — иначе
	/// сломанный прямоугольник утопил бы консоль (это путь из игрового цикла).
	/// </summary>
	private bool _windowErrorLogged;
	private void LogStreamingWindowErrorOnce(ArgumentException ex)
	{
		if (_windowErrorLogged) return;
		_windowErrorLogged = true;
		Godot.GD.PrintErr("[StreamingWorldView] видимое окно отвергнуто планировщиком: " + ex.Message);
	}
	private WorldRect ClampVisibleToWindow(WorldRect visible)
	{
		long side = _planner.WindowChunks;
		long available = Math.Max(WorldCoordinates.ChunkSize,
			(side - 1) * WorldCoordinates.ChunkSize - 2 * _planner.MarginTiles);
		long width = visible.MaxX - visible.MinX;
		long height = visible.MaxY - visible.MinY;
		long centerX = (visible.MinX + visible.MaxX) / 2;
		long centerY = (visible.MinY + visible.MaxY) / 2;
		long halfW = Math.Min(width, available) / 2;
		long halfH = Math.Min(height, available) / 2;
		// S-08 (бывший W-08 AUDIT-A): страховка от выравнивания не имеет права
		// «дожать» полуширину до нуля/минуса. Раньше шаг −32 при условии `halfW > 1`
		// из любого halfW ∈ (1, 33] приводил к 0 или отрицательному значению, а
		// `new WorldRect(center-halfW, ..., center+halfW, ...)` становился вырожденным —
		// и ChunkWindowPlanner.ValidateVisibleBounds бросал ArgumentException прямо из
		// игрового цикла. Теперь нижняя граница — ровно один чанк (кламп никогда не
		// инвертирует прямоугольник), а не «страховка, которая падает».
		const long MinHalfExtent = WorldCoordinates.ChunkSize / 2;
		halfW = Math.Max(MinHalfExtent, halfW);
		halfH = Math.Max(MinHalfExtent, halfH);

		while (halfW > MinHalfExtent
			&& ChunkSpan(centerX - halfW - _planner.MarginTiles, centerX + halfW + _planner.MarginTiles) > side)
			halfW = Math.Max(MinHalfExtent, halfW - WorldCoordinates.ChunkSize / 2);
		while (halfH > MinHalfExtent
			&& ChunkSpan(centerY - halfH - _planner.MarginTiles, centerY + halfH + _planner.MarginTiles) > side)
			halfH = Math.Max(MinHalfExtent, halfH - WorldCoordinates.ChunkSize / 2);

		// Инвариант планера: Max exclusive и строго больше Min.
		long minX = centerX - halfW;
		long maxX = centerX + halfW;
		long minY = centerY - halfH;
		long maxY = centerY + halfH;
		if (maxX <= minX) maxX = minX + 1;
		if (maxY <= minY) maxY = minY + 1;
		return new WorldRect(minX, minY, maxX, maxY);
	}

	/// <summary>
	/// Закодировать все персистентные метки чанка в delta-полезную нагрузку
	/// (канонический порядок: LocalIndex, затем Kind). Пусто — пустой payload
	/// (стример воспринимает как «без изменений»).
	/// </summary>
	private byte[] EncodeBlocks(ChunkKey key)
	{
		var records = new List<WorldChunkDeltaRecord>();
		if (_blocks.TryGetValue(key, out Dictionary<byte, HashSet<ushort>> map) && map.Count > 0)
		{
			foreach (KeyValuePair<byte, HashSet<ushort>> kindSet in map)
			{
				foreach (ushort index in kindSet.Value)
					records.Add(new WorldChunkDeltaRecord(index, kindSet.Key, Array.Empty<byte>()));
			}
		}

		// task-8: мутации руды этого чанка (остаток запаса; 0 = выработано).
		// Это же покрывает остров: его клетки — те же чанки локального холста,
		// поэтому добыча на острове тоже переживает выход/вход.
		List<(int LocalX, int LocalY, ushort Reserve)> ore = OreWorldState.EnumerateChunk(key.X, key.Y);
		for (int i = 0; i < ore.Count; i++)
		{
			var (localX, localY, reserve) = ore[i];
			LocalCell local = WorldCoordinates.LocalForTile(localX, localY);
			ushort index = (ushort)(local.Y * WorldCoordinates.ChunkSize + local.X);
			records.Add(new WorldChunkDeltaRecord(index, WorldDeltaKind.OreMined,
				OreWorldState.EncodePayload(reserve)));
		}

		if (records.Count == 0)
			return Array.Empty<byte>();
		records.Sort(static (left, right) =>
		{
			int indexOrder = left.LocalIndex.CompareTo(right.LocalIndex);
			return indexOrder != 0 ? indexOrder : left.Kind.CompareTo(right.Kind);
		});
		return WorldChunkDeltaCodec.Encode(new WorldChunkDelta(records));
	}
	private WorldSaveMetadata MakeMetadata()
		=> new(WorldChunkCodec.CurrentFormatVersion, GeneratorVersion,
			UnifiedWorldGenerator.FeatureSchemaVersion, WorldSeed, GameTimeCheckpoint: 0.0);

	/// <summary>
	/// Поставить тайлы изменившимся чанкам и стереть выгруженные — с бюджетом на кадр
	/// (ближние к камере чанки первыми), чтобы телепорт/pan не давал шторм SetCell/EraseCell.
	/// </summary>
	private void RepaintDirty()
	{
		int operationBudget = Mathf.Max(1, MaxChunkOpsPerFrame);

		ProcessEraseWork(Math.Max(1, MaxCellsErasedPerFrame));

		if (_textureDirty.Count > 0)
		{
			SelectNearestDirtyChunks(operationBudget);
			foreach (ChunkKey key in _dirtyBuffer)
			{
				if (!_textureDirty.Contains(key))
					continue;
				if (!_chunkData.ContainsKey(key))
				{
					_textureDirty.Remove(key); // stale dirty entry must not be reconsidered forever
					continue;
				}
				if (_eraseJobs.ContainsKey(key))
					continue; // never let an old erase job erase a newly-painted chunk
				_textureDirty.Remove(key);
				bool clearEmptyLayers = !_freshPaint.Remove(key);
				ScheduleChunkPaint(key, clearEmptyLayers);
			}
		}

		ProcessPaintSlices(operationBudget);
		ProcessOreJobs();
		// N-5: после готовности ore-массивов докрашиваем отложенный декор.
		ProcessDecorRepair(DecorRepairCellsPerFrame);
		_mountainTerrain?.Flush(IsMountainCell);
		_wallTerrain?.Flush(IsWallCell);
		_wallPlanTerrain?.Flush(IsWallPlanCell);
		_farmTerrain?.Flush(IsCropCell);
		_farmPlanTerrain?.Flush(IsCropPlanCell);
	}

	private sealed class ChunkPaintJob
	{
		public readonly ChunkKey Key;
		public readonly WorldChunk Chunk;
		public readonly BlockLookup Lookup;
		public readonly long OriginX;
		public readonly long OriginY;
		public readonly bool ClearEmptyLayers;
		public int Cursor;

		public ChunkPaintJob(ChunkKey key, WorldChunk chunk, BlockLookup lookup,
			long originX, long originY, bool clearEmptyLayers)
		{
			Key = key; Chunk = chunk; Lookup = lookup;
			OriginX = originX; OriginY = originY; ClearEmptyLayers = clearEmptyLayers;
		}
	}

	private sealed class ChunkEraseJob
	{
		public readonly ChunkKey Key;
		public readonly ushort[] MarkIndices;
		public readonly ushort[] PlanIndices;
		public int Cursor;
		public int MarkCursor;
		public int PlanCursor;

		public ChunkEraseJob(ChunkKey key, ushort[] markIndices, ushort[] planIndices)
		{
			Key = key;
			MarkIndices = markIndices ?? Array.Empty<ushort>();
			PlanIndices = planIndices ?? Array.Empty<ushort>();
		}
	}

	private void ScheduleChunkPaint(ChunkKey key, bool clearEmptyLayers)
	{
		if (!_chunkData.TryGetValue(key, out WorldChunk chunk))
			return;
		long originX = key.X * (long)WorldCoordinates.ChunkSize;
		long originY = key.Y * (long)WorldCoordinates.ChunkSize;
		if (!FitsTileMap(originX, originY))
			return;
		if (IsOccludedByColony(originX, originY))
			return;
		_blocks.TryGetValue(key, out Dictionary<byte, HashSet<ushort>> map);
		_paintedChunks.Add(key); // §117/B3: будет класть тайлы — выгрузке понадобится erase
		_paintJobs[key] = new ChunkPaintJob(key, chunk, new BlockLookup(map),
			originX, originY, clearEmptyLayers);
		if (_paintQueued.Add(key))
			_paintQueue.Enqueue(key);
	}

	private static bool IsOccludedByColony(long originX, long originY)
	{
		return originX >= 0 && originX + WorldCoordinates.ChunkSize <= MapRenderer.MapWidth
			&& originY >= 0 && originY + WorldCoordinates.ChunkSize <= MapRenderer.MapHeight;
	}

	private void ProcessPaintSlices(int maxSlices)
	{
		int slices = Math.Max(1, maxSlices);
		int maxCells = Math.Clamp(MaxCellsPerPaintSlice, 128, WorldCoordinates.ChunkSize * WorldCoordinates.ChunkSize);
		var sw = System.Diagnostics.Stopwatch.StartNew();
		while (slices > 0 && _paintQueue.Count > 0)
		{
			if (sw.ElapsedMilliseconds >= 4)
				break;

			ChunkKey key = _paintQueue.Dequeue();
			_paintQueued.Remove(key);
			if (!_paintJobs.TryGetValue(key, out ChunkPaintJob job))
				continue; // stale queue token after unload
			if (!_chunkData.ContainsKey(key))
			{
				_paintJobs.Remove(key);
				continue;
			}

			ReadOnlySpan<GeneratedCell> cells = job.Chunk.Cells.Span;
			int total = cells.Length;
			if (job.Cursor >= total)
			{
				_paintJobs.Remove(key);
				MarkTerrainRing(job.OriginX, job.OriginY);
				ScheduleOreChunk(key);
				continue;
			}

			int end = Math.Min(total, job.Cursor + maxCells);
			for (int index = job.Cursor; index < end; index++)
			{
				int x = index % WorldCoordinates.ChunkSize;
				int y = index / WorldCoordinates.ChunkSize;
				PaintCell(job.OriginX + x, job.OriginY + y, index, cells[index],
					job.Lookup, job.ClearEmptyLayers);
			}
			job.Cursor = end;
			slices--;

			if (job.Cursor >= total)
			{
				_paintJobs.Remove(key);
				MarkTerrainRing(job.OriginX, job.OriginY);
				ScheduleOreChunk(key);
			}
			else if (_paintQueued.Add(key))
			{
				_paintQueue.Enqueue(key);
			}
		}
	}

	private void ScheduleEraseChunk(ChunkKey key, ushort[] markIndices, ushort[] planIndices)
	{
		// Replace an existing job for the same key but retain its queue token.
		_eraseJobs[key] = new ChunkEraseJob(key, markIndices, planIndices);
		if (_eraseScheduled.Add(key))
			_eraseQueue.Enqueue(key);
	}

	private ushort[] CollectOverlayIndices(Dictionary<byte, HashSet<ushort>> map, bool plans)
	{
		if (map == null || map.Count == 0)
			return Array.Empty<ushort>();
		var indices = new HashSet<ushort>();
		if (plans)
		{
			AddIndices(indices, map, WorldDeltaKind.WallPlan);
			AddIndices(indices, map, WorldDeltaKind.BuildingPlan);
			AddIndices(indices, map, WorldDeltaKind.CropPlan);
			AddIndices(indices, map, WorldDeltaKind.StockpilePlan);
			AddIndices(indices, map, WorldDeltaKind.CutTreePlan);
			AddIndices(indices, map, WorldDeltaKind.MinedStonePlan);
		}
		else
		{
			if (_wallLayer == null)
				AddIndices(indices, map, WallKind);
			AddIndices(indices, map, WorldDeltaKind.Crop);
			AddIndices(indices, map, WorldDeltaKind.Stockpile);
			AddIndices(indices, map, WorldDeltaKind.Building);
		}
		if (indices.Count == 0)
			return Array.Empty<ushort>();
		var result = new ushort[indices.Count];
		indices.CopyTo(result);
		Array.Sort(result);
		return result;
	}

	private static void AddIndices(HashSet<ushort> into,
		Dictionary<byte, HashSet<ushort>> map, byte kind)
	{
		if (!map.TryGetValue(kind, out HashSet<ushort> set))
			return;
		foreach (ushort index in set)
			if (index < WorldCoordinates.ChunkSize * WorldCoordinates.ChunkSize)
				into.Add(index);
	}

	private void ProcessEraseWork(int maxCells)
	{
		int budget = Math.Max(1, maxCells);
		int chunkCellCount = WorldCoordinates.ChunkSize * WorldCoordinates.ChunkSize;
		while (budget > 0 && _eraseQueue.Count > 0)
		{
			ChunkKey key = _eraseQueue.Peek();
			if (!_eraseJobs.TryGetValue(key, out ChunkEraseJob job))
			{
				_eraseQueue.Dequeue();
				_eraseScheduled.Remove(key);
				continue;
			}

			long originX = key.X * (long)WorldCoordinates.ChunkSize;
			long originY = key.Y * (long)WorldCoordinates.ChunkSize;
			while (budget > 0 && job.Cursor < chunkCellCount)
			{
				int index = job.Cursor;
				var pos = new Vector2I((int)(originX + index % WorldCoordinates.ChunkSize),
					(int)(originY + index / WorldCoordinates.ChunkSize));
				_decorLayer?.EraseCell(pos);
				// Руду стираем только у ВЫГРУЖЕННОГО чанка: если чанк вернулся в окно,
				// пока шёл erase, его руда должна остаться (иначе «мигание»).
				if (!_chunkData.ContainsKey(key))
					_oreLayer?.EraseCell(pos);

				if (job.MarkCursor < job.MarkIndices.Length && job.MarkIndices[job.MarkCursor] == index)
				{
					_markLayer?.EraseCell(pos);
					job.MarkCursor++;
				}
				if (job.PlanCursor < job.PlanIndices.Length && job.PlanIndices[job.PlanCursor] == index)
				{
					_planLayer?.EraseCell(pos);
					job.PlanCursor++;
				}
				job.Cursor++;
				budget--;
			}

			if (job.Cursor >= chunkCellCount)
			{
				_eraseQueue.Dequeue();
				_eraseJobs.Remove(key);
				_eraseScheduled.Remove(key);
				// Чанк вернулся, пока шёл erase: часть клеток руды могла быть
				// стёрта. После снятия erase-джобы перерисовываем чанк руды.
				if (_chunkData.ContainsKey(key))
					EnsureOrePainted(key, force: true);
			}
			else
			{
				break; // continue the head chunk next frame; don't produce a long frame
			}
		}
	}

	// Select only the closest `budget` dirty chunks. Sorting the entire dirty set every
	// frame was wasteful during teleports, when arrivals can outpace the paint budget.
	private void SelectNearestDirtyChunks(int limit)
	{
		_dirtyBuffer.Clear();
		foreach (ChunkKey candidate in _textureDirty)
		{
			if (_eraseJobs.ContainsKey(candidate))
				continue;
			if (_dirtyBuffer.Count < limit)
			{
				_dirtyBuffer.Add(candidate);
				int i = _dirtyBuffer.Count - 1;
				while (i > 0)
				{
					int parent = (i - 1) / 2;
					if (CompareByDistanceToFocus(_dirtyBuffer[i], _dirtyBuffer[parent]) <= 0)
						break;
					(_dirtyBuffer[i], _dirtyBuffer[parent]) = (_dirtyBuffer[parent], _dirtyBuffer[i]);
					i = parent;
				}
				continue;
			}

			// Max-heap root is the farthest currently selected chunk.
			if (CompareByDistanceToFocus(candidate, _dirtyBuffer[0]) >= 0)
				continue;
			_dirtyBuffer[0] = candidate;
			int root = 0;
			while (true)
			{
				int left = root * 2 + 1;
				if (left >= _dirtyBuffer.Count)
					break;
				int right = left + 1;
				int farther = right < _dirtyBuffer.Count
					&& CompareByDistanceToFocus(_dirtyBuffer[right], _dirtyBuffer[left]) > 0 ? right : left;
				if (CompareByDistanceToFocus(_dirtyBuffer[farther], _dirtyBuffer[root]) <= 0)
					break;
				(_dirtyBuffer[root], _dirtyBuffer[farther]) = (_dirtyBuffer[farther], _dirtyBuffer[root]);
				root = farther;
			}
		}
		_dirtyBuffer.Sort(CompareByDistanceToFocus);
	}

	/// <summary>Пометить горы/стены выгружаемого чанка к стиранию (данные ещё в памяти).</summary>
	private void MarkTerrainForUnload(ChunkKey key)
	{
		if (_mountainTerrain == null && _wallTerrain == null)
			return;
		long originX = key.X * (long)WorldCoordinates.ChunkSize;
		long originY = key.Y * (long)WorldCoordinates.ChunkSize;
		if (!FitsTileMap(originX, originY))
			return;
		if (_mountainTerrain != null && _chunkData.TryGetValue(key, out WorldChunk chunk))
		{
			ReadOnlySpan<GeneratedCell> cells = chunk.Cells.Span;
			for (int i = 0; i < cells.Length; i++)
			{
				if (cells[i].Terrain != BaseTerrainKind.Mountain)
					continue;
				int cx = i % WorldCoordinates.ChunkSize;
				int cy = i / WorldCoordinates.ChunkSize;
				_mountainTerrain.MarkDirty(new Vector2I((int)(originX + cx), (int)(originY + cy)));
			}
		}
		if (_wallTerrain != null
			&& _blocks.TryGetValue(key, out Dictionary<byte, HashSet<ushort>> map)
			&& map.TryGetValue(WallKind, out HashSet<ushort> walls))
		{
			foreach (ushort index in walls)
			{
				int cx = index % WorldCoordinates.ChunkSize;
				int cy = index / WorldCoordinates.ChunkSize;
				_wallTerrain.MarkDirty(new Vector2I((int)(originX + cx), (int)(originY + cy)));
			}
		}
		// The neighbour-side terrain tiles must reconnect after this chunk is removed.
		MarkTerrainRing(originX, originY);
	}

	/// <summary>Сортировка чанков по удалённости от центра видимого окна (клетки).</summary>
	private int CompareByDistanceToFocus(ChunkKey a, ChunkKey b)
	{
		double half = WorldCoordinates.ChunkSize / 2.0;
		double ax = (double)a.X * WorldCoordinates.ChunkSize + half - _focusCellX;
		double ay = (double)a.Y * WorldCoordinates.ChunkSize + half - _focusCellY;
		double bx = (double)b.X * WorldCoordinates.ChunkSize + half - _focusCellX;
		double by = (double)b.Y * WorldCoordinates.ChunkSize + half - _focusCellY;
		double da = ax * ax + ay * ay;
		double db = bx * bx + by * by;
		return da.CompareTo(db);
	}

	private HashSet<ushort> KindSet(ChunkKey key, byte kind)
		=> _blocks.TryGetValue(key, out Dictionary<byte, HashSet<ushort>> map)
			&& map.TryGetValue(kind, out HashSet<ushort> set) ? set : null;

	private HashSet<ushort> GetOrCreateSet(ChunkKey key, byte kind)
	{
		if (!_blocks.TryGetValue(key, out Dictionary<byte, HashSet<ushort>> map))
		{
			map = new Dictionary<byte, HashSet<ushort>>();
			_blocks[key] = map;
		}
		if (!map.TryGetValue(kind, out HashSet<ushort> set))
		{
			set = new HashSet<ushort>();
			map[kind] = set;
		}
		return set;
	}

	// ---------- Рендер: настоящие тайлы игры (1 клетка = 1 тайл 64×64) ----------

	/// <summary>Прозрачность слоя чертежей (§29): план отличим от готовой постройки.</summary>
	private const float PlanAlpha = 0.55f;

	private const int SrcCrop = 8;       // food/corn.png
	private const int SrcStockpile = 9;  // icons/logistic_zone.png
	private const int SrcWork = 10;      // sprites/ProcessOfWork.png
	private const int SrcWall = 11;      // construction items/wooden_wall.png (7×7)

	// Terrain-разметка сцен (mountains_tile, wood_wall_tile) — как в MapRenderer.
	private const int MountainTerrainSetId = 0;
	private const int MountainTerrainId = 0;
	private const int WallTerrainSetId = 0;
	private const int WallTerrainId = 0;

	/// <summary>Деревья/камни — пороги декора (общий источник истины с доступом к миру).</summary>
	private const ushort ForestDecorMin = WorldDecor.ForestMinQ16;
	private const ushort StoneDecorMin = WorldDecor.StoneMinQ16;

	// §117/F4: шаг сетки климата плодородия и сторона сетки 5×5 на чанк 64×64 —
	// ровно как в UnifiedWorldGenerator.GenerateChunk/GenerateColonyWindow,
	// чтобы SoilFertilityQ16 (порог рощ) совпадал с островом клетка в клетку.
	private const int FertilityGridStep = 16;
	private const int FertilityGridSide = WorldCoordinates.ChunkSize / FertilityGridStep + 1;

	private static readonly string[] TexTrees =
	{
		"res://ui/assets/textures/objects/tree.png",
		"res://ui/assets/textures/objects/tree_1.png",
		"res://ui/assets/textures/objects/tree_2.png",
        "res://ui/assets/textures/objects/tree_3.png"
	};

	/// <summary>
	/// Слои мира: декор (лес/камни, прозрачный фон) и метки игрока.
	/// Земли здесь НЕТ: её во всём мире рисует terrain_blend.gdshader (MapRenderer).
	/// TileSet — из стандартных текстур игры, размер тайла 64 px: клетка = тайл.
	/// </summary>
	private void BuildTileLayers()
	{
		_tileSet = new TileSet { TileSize = new Vector2I(TilePx, TilePx) };
		AddSingleTile("res://ui/assets/textures/food/corn.png", SrcCrop);
		AddStockpileZoneTile(SrcStockpile);
		AddSingleTile("res://ui/assets/textures/sprites/ProcessOfWork.png", SrcWork);
		AddAtlasTileSet("res://ui/assets/textures/construction items/wooden_wall.png", SrcWall);
		BuildDecorSource();

		// R2-01: Декор и слои мира получают абсолютный ZIndex=2 (или 3 для меток) при ZAsRelative=false,
		// чтобы гарантированно не уходить под шейдерную землю ColorRect _groundSurface (z=0) при ZIndex=-1 корня мира.
		_decorLayer = MakeLayer("WorldDecor", _tileSet, 2);
		_markLayer = MakeLayer("WorldMark", _tileSet, 3);
		AddChild(_decorLayer);
		AddChild(_markLayer);

		// task-8: слой руды мира (те же тайлы ore47, что у острова).
		_oreTiles = new OreTileSetBuilder();
		_oreLayer = MakeLayer("WorldOre", _oreTiles.TileSet, 2);
		AddChild(_oreLayer);

		// Горы, стены и грядки выглядят как в игре: TileSet берём из сцен (там terrain-разметка),
		// и соединения подбирает движок, а не мы вручную атласом.
		TileSet mountainTiles = MapRenderer.GetMountainTerrainTileSet();
		if (mountainTiles != null)
		{
			_mountainLayer = MakeLayer("WorldMountain", mountainTiles);
			AddChild(_mountainLayer);
			_mountainTerrain = new TerrainTileLayer(_mountainLayer, MountainTerrainSetId, MountainTerrainId);
		}
		TileSet wallTiles = MapRenderer.GetWallTerrainTileSet();
		if (wallTiles != null)
		{
			// Коллизии стен считаются геймплеем по меткам-блокам (WorldGameRules /
			// _blocks / MapData), НЕ через физику тайлов: в проекте нет ни одного
			// CharacterBody/RigidBody/Area2D/запроса пересечений — поэтому у ВСЕХ
			// слоёв вида tile-физика/навигация/окклюзия отключены (§117, researcher-аудит).
			_wallLayer = MakeLayer("WorldWall", wallTiles);
			AddChild(_wallLayer);
			_wallTerrain = new TerrainTileLayer(_wallLayer, WallTerrainSetId, WallTerrainId);

			_wallPlanLayer = MakeLayer("WorldWallPlan", wallTiles);
			_wallPlanLayer.Modulate = new Color(1f, 1f, 1f, PlanAlpha);
			AddChild(_wallPlanLayer);
			_wallPlanTerrain = new TerrainTileLayer(_wallPlanLayer, WallTerrainSetId, WallTerrainId);
		}

		TileSet farmTiles = MapRenderer.GetGardenBedTerrainTileSet();
		if (farmTiles != null)
		{
			_farmLayer = MakeLayer("WorldFarm", farmTiles);
			AddChild(_farmLayer);
			_farmTerrain = new TerrainTileLayer(_farmLayer, 0, 0);

			_farmPlanLayer = MakeLayer("WorldFarmPlan", farmTiles);
			_farmPlanLayer.Modulate = new Color(1f, 1f, 1f, PlanAlpha);
			AddChild(_farmPlanLayer);
			_farmPlanTerrain = new TerrainTileLayer(_farmPlanLayer, 0, 0);
		}

		// §29: чертежи (планы) — своим слоем и полупрозрачно, чтобы отличать от готового.
		_planLayer = MakeLayer("WorldPlan", _tileSet);
		_planLayer.Modulate = new Color(1f, 1f, 1f, PlanAlpha);
		AddChild(_planLayer);
	}

	/// <summary>
	/// §117 (researcher-аудит): у TileMapLayer дефолты collision/navigation/occlusion = true —
	/// каждая партия SetCell/EraseCell прогоняла клетки через эти подсистемы даром.
	/// Геймплей их не использует (коллизии — свои, по MapData/меткам-блокам; в проекте нет
	/// ни одного физического тела или запроса пересечений), поэтому отключаем всё.
	/// RenderingQuadrantSize=64 = размеру чанка (рекомендация мейнтейнера TileMapLayer),
	/// Y-sort остаётся выключенным.
	/// R2-01: ZAsRelative = false, ZIndex = zIndex (по умолчанию 2) гарантирует, что слои мира
	/// не уходят под шейдерную землю ColorRect _groundSurface (z=0) при ZIndex=-1 корня мира.
	/// </summary>
	private static TileMapLayer MakeLayer(string name, TileSet tileSet, int zIndex = 2)
		=> new()
		{
			Name = name,
			TileSet = tileSet,
			RenderingQuadrantSize = 64,
			CollisionEnabled = false,
			NavigationEnabled = false,
			OcclusionEnabled = false,
			ZAsRelative = false,
			ZIndex = zIndex
		};

	/// <summary>Процедурный фиолетовый тайл складской зоны (64×64) с четкой границей вместо иконки.</summary>
	private void AddStockpileZoneTile(int sourceId)
	{
		var img = Image.CreateEmpty(TilePx, TilePx, false, Image.Format.Rgba8);
		var fillColor = new Color(0.65f, 0.15f, 0.9f, 0.5f);
		var borderColor = new Color(0.85f, 0.35f, 1f, 0.9f);
		for (int y = 0; y < TilePx; y++)
		{
			for (int x = 0; x < TilePx; x++)
			{
				bool isBorder = x <= 1 || x >= TilePx - 2 || y <= 1 || y >= TilePx - 2;
				img.SetPixel(x, y, isBorder ? borderColor : fillColor);
			}
		}
		var tex = ImageTexture.CreateFromImage(img);
		var source = new TileSetAtlasSource
		{
			Texture = tex,
			TextureRegionSize = new Vector2I(TilePx, TilePx)
		};
		source.CreateTile(Vector2I.Zero);
		_tileSet.AddSource(source, sourceId);
	}

	/// <summary>Одиночный тайл 64×64 (трава, вода, дерево, посев, склад, стройка).</summary>
	private void AddSingleTile(string path, int sourceId)
	{
		Texture2D tex = GD.Load<Texture2D>(path);
		if (tex == null)
		{
			GD.PrintErr($"[World] нет текстуры {path}");
			return;
		}
		var source = new TileSetAtlasSource
		{
			Texture = tex,
			TextureRegionSize = new Vector2I(TilePx, TilePx)
		};
		source.CreateTile(Vector2I.Zero);
		_tileSet.AddSource(source, sourceId);
	}

	/// <summary>Атлас N×M тайлов 64×64 (горы 7×7, камень 2×2, стены 7×7).</summary>
	private void AddAtlasTileSet(string path, int sourceId)
	{
		Texture2D tex = GD.Load<Texture2D>(path);
		if (tex == null)
		{
			GD.PrintErr($"[World] нет текстуры {path}");
			return;
		}
		var source = new TileSetAtlasSource
		{
			Texture = tex,
			TextureRegionSize = new Vector2I(TilePx, TilePx)
		};
		int cols = Mathf.Max(1, tex.GetWidth() / TilePx);
		int rows = Mathf.Max(1, tex.GetHeight() / TilePx);
		for (int y = 0; y < rows; y++)
		{
			for (int x = 0; x < cols; x++)
				source.CreateTile(new Vector2I(x, y));
		}
		_tileSet.AddSource(source, sourceId);
	}

	/// <summary>Id источника декора в _tileSet (историческое имя SrcCompositeGround — источник 12).</summary>
	private const int SrcDecor = 12;
	/// <summary>Атлас декора: 8 тайлов 64×64 в ряд — 0..3 деревья, 4..7 камни. Фон ПРОЗРАЧНЫЙ.</summary>
	private const int DecorAtlasTileCount = 8;
	private const int DecorTree0 = 0;
	private const int DecorStone0 = 4;

	private static Image LoadTileImage(string path, int atlasX = 0, int atlasY = 0, Color fallback = default)
	{
		Texture2D texture = GD.Load<Texture2D>(path);
		Image image = texture?.GetImage();
		int x = atlasX * TilePx;
		int y = atlasY * TilePx;
		if (image == null || image.GetWidth() < x + TilePx || image.GetHeight() < y + TilePx)
		{
			var missing = Image.CreateEmpty(TilePx, TilePx, false, Image.Format.Rgba8);
			missing.Fill(fallback.A == 0f ? new Color(1f, 0f, 1f, 1f) : fallback);
			GD.PrintErr($"[World] composite source tile unavailable: {path} [{atlasX},{atlasY}]");
			return missing;
		}
		Image tile = image.GetRegion(new Rect2I(x, y, TilePx, TilePx));
		if (tile.GetFormat() != Image.Format.Rgba8)
			tile.Convert(Image.Format.Rgba8);
		return tile;
	}

	private void BuildDecorSource()
	{
		// Атлас ДЕКОРА (8 тайлов 64×64 в ряд): 0..3 деревья, 4..7 камни.
		// Фон ПРОЗРАЧНЫЙ (BlendRect на пустой альфе): землю под декором рисует
		// terrain_blend.gdshader — тайл, вклеивший землю, давал бы непрозрачные
		// квадраты 64×64 поверх шейдерной земли (шов «два рендера земли»).
		var atlas = Image.CreateEmpty(TilePx * DecorAtlasTileCount, TilePx, false, Image.Format.Rgba8);

		for (int v = 0; v < TexTrees.Length; v++)
		{
			Image tree = LoadTileImage(TexTrees[v], fallback: new Color(0.1f, 0.35f, 0.1f, 1f));
			atlas.BlendRect(tree, new Rect2I(0, 0, TilePx, TilePx),
				new Vector2I((DecorTree0 + v) * TilePx, 0));
		}

		Image stone = LoadTileImage("res://ui/assets/textures/objects/stone.png", 0, 0,
			new Color(0.45f, 0.45f, 0.45f, 1f));
		for (int variant = 0; variant < 4; variant++)
		{
			Image stoneTile = variant == 0 ? stone : LoadTileImage(
				"res://ui/assets/textures/objects/stone.png", variant & 1, (variant >> 1) & 1,
				new Color(0.45f, 0.45f, 0.45f, 1f));
			atlas.BlendRect(stoneTile, new Rect2I(0, 0, TilePx, TilePx),
				new Vector2I((DecorStone0 + variant) * TilePx, 0));
		}

		var source = new TileSetAtlasSource
		{
			Texture = ImageTexture.CreateFromImage(atlas),
			TextureRegionSize = new Vector2I(TilePx, TilePx)
		};
		for (int i = 0; i < DecorAtlasTileCount; i++)
			source.CreateTile(new Vector2I(i, 0));
		_tileSet.AddSource(source, SrcDecor);
	}

	// ---------- §117/F4: паритет прореживания декора с островом ----------

	/// <summary>
	/// Формула декора ОДНА на проект: Game.Core.WorldStreaming.WorldGenV2.DecorPass
	/// (дословный перенос GenerateColonyWindow, включая евклидов Mod100 — деревья
	/// есть и в отрицательных квадрантах). Прежняя локальная копия IslandDecorPass
	/// удалена (арх-гард decor_pass_defs): вторая копия формулы — это второй мир
	/// в зародыше. Правила игры (WorldGameRules/ChunkWorldAccess) и тесты зовут ту же.
	/// </summary>

	/// <summary>
	/// Плодородие почвы клетки (SoilFertilityQ16) — та же 16×16-билинейная сетка, которой
	/// его видит GenerateColonyWindow/GenerateChunk: якоря кратны 16 от начала чанка в
	/// МИРОВЫХ координатах, интерполяция — LerpQ16 (копия генераторной). GeneratedCell
	/// плодородия не хранит. Одноразовый вариант (4 семпла) — для тестов/правил;
	/// горячий путь рендера идёт через кэш CellSoilFertilityQ16 (25 семплов на чанк).
	/// </summary>
	public static ushort SoilFertilityAtCell(ulong worldSeed, uint generatorVersion, long cellX, long cellY)
	{
		ChunkKey key = WorldCoordinates.ChunkForTile(cellX, cellY);
		LocalCell local = WorldCoordinates.LocalForTile(cellX, cellY);
		long baseX = UnifiedWorldGenerator.WorldOriginX + key.X * (long)WorldCoordinates.ChunkSize;
		long baseY = UnifiedWorldGenerator.WorldOriginY + key.Y * (long)WorldCoordinates.ChunkSize;
		long ax = baseX + (local.X / FertilityGridStep) * FertilityGridStep;
		long ay = baseY + (local.Y / FertilityGridStep) * FertilityGridStep;
		uint u = (uint)((local.X % FertilityGridStep) * 65535 / FertilityGridStep);
		uint v = (uint)((local.Y % FertilityGridStep) * 65535 / FertilityGridStep);

		ushort nw = WorldClimateGenerator.SampleBlendedCell(worldSeed, generatorVersion, ax, ay).SoilFertilityQ16;
		ushort ne = WorldClimateGenerator.SampleBlendedCell(worldSeed, generatorVersion, ax + FertilityGridStep, ay).SoilFertilityQ16;
		ushort sw = WorldClimateGenerator.SampleBlendedCell(worldSeed, generatorVersion, ax, ay + FertilityGridStep).SoilFertilityQ16;
		ushort se = WorldClimateGenerator.SampleBlendedCell(worldSeed, generatorVersion, ax + FertilityGridStep, ay + FertilityGridStep).SoilFertilityQ16;
		return LerpQ16(LerpQ16(nw, ne, u), LerpQ16(sw, se, u), v);
	}

	/// <summary>Кэшированный для рендера аналог SoilFertilityAtCell (сетка 5×5 на чанк).</summary>
	private ushort CellSoilFertilityQ16(long cellX, long cellY)
	{
		ChunkKey key = WorldCoordinates.ChunkForTile(cellX, cellY);
		if (!_fertilityValid || _fertilityChunk != key)
			BuildFertilityGrid(key);
		LocalCell local = WorldCoordinates.LocalForTile(cellX, cellY);
		int row = (local.Y / FertilityGridStep) * FertilityGridSide;
		int col = local.X / FertilityGridStep;
		uint u = (uint)((local.X % FertilityGridStep) * 65535 / FertilityGridStep);
		uint v = (uint)((local.Y % FertilityGridStep) * 65535 / FertilityGridStep);
		ushort top = LerpQ16(_fertilityGrid[row + col], _fertilityGrid[row + col + 1], u);
		ushort bottom = LerpQ16(_fertilityGrid[row + FertilityGridSide + col],
			_fertilityGrid[row + FertilityGridSide + col + 1], u);
		return LerpQ16(top, bottom, v);
	}

	/// <summary>Якори сетки — ровно как в GenerateChunk: WorldOrigin + начало чанка + 16·g.</summary>
	private void BuildFertilityGrid(ChunkKey key)
	{
		long originX = UnifiedWorldGenerator.WorldOriginX + key.X * (long)WorldCoordinates.ChunkSize;
		long originY = UnifiedWorldGenerator.WorldOriginY + key.Y * (long)WorldCoordinates.ChunkSize;
		for (int gy = 0; gy < FertilityGridSide; gy++)
		{
			int row = gy * FertilityGridSide;
			long wy = originY + gy * FertilityGridStep;
			for (int gx = 0; gx < FertilityGridSide; gx++)
			{
				_fertilityGrid[row + gx] = WorldClimateGenerator.SampleBlendedCell(
					WorldSeed, GeneratorVersion, originX + gx * FertilityGridStep, wy).SoilFertilityQ16;
			}
		}
		_fertilityChunk = key;
		_fertilityValid = true;
	}

	/// <summary>LerpQ16 — побитовая копия интерполяции генератора (умножение со сдвигом вместо IDIV).</summary>
	private static ushort LerpQ16(ushort a, ushort b, uint t)
	{
		uint clampedT = Math.Min(t, 65535);
		ulong value = (ulong)((long)a * (65535 - clampedT) + (long)b * clampedT + 32768);
		return (ushort)((value * 0x80008001UL) >> 47);
	}

	/// <summary>
	/// Индекс тайла ДЕКОРА (атлас 8: 0..3 деревья, 4..7 камни) или -1, если декора нет.
	/// Формула — WorldGenV2.DecorPass (единая): тот же порог рощ/осыпей, что у окна
	/// острова, — плотность леса/камней через границу 1024 совпадает клетка в клетку.
	/// </summary>
	private int GetDecorIndex(long cellX, long cellY, int localIndex, GeneratedCell cell, BlockLookup kinds)
	{
		if (cell.Terrain != BaseTerrainKind.Grass)
			return -1;
		// task-8/23: на клетке руды декор гасим (как остров: «дерево в жиле»).
		// N-5: если поле руды чанка ЕЩЁ НЕ посчитано — декор НЕ ставим вовсе
		// (консервативно), иначе «дерево в жиле» проявилось бы после выработки.
		// После готовности ore-массивов чанк докрашивается ProcessDecorRepair.
		bool? oreBase = OreBaseAtCellCached(cellX, cellY);
		if (!oreBase.HasValue)
			return -1;
		if (oreBase.Value)
			return -1;
		if ((BiomeType)cell.Biome == BiomeType.DeepWater)
			return -1;

		bool isCut = BlockLookup.Contains(kinds.CutTree, localIndex);
		bool isMined = BlockLookup.Contains(kinds.MinedStone, localIndex);

		// §117/F4: мировые координаты клетки (у острова wx = WorldOrigin+lx — тот же
		// номер клетки, что и здесь) и прореживание декора ЕГО формулой.
		long wx = UnifiedWorldGenerator.WorldOriginX + cellX;
		long wy = UnifiedWorldGenerator.WorldOriginY + cellY;
		// Плодородие (порог рощ) нужно только клеткам зоны леса; остальным — 0.
		bool needsFertility = cell.ForestQ16 >= ForestDecorMin;
		ushort fertility = needsFertility ? CellSoilFertilityQ16(cellX, cellY) : (ushort)0;
		(bool hasTree, bool hasStone) = WorldGenV2.DecorPass(wx, wy, in cell, fertility);

		// Варианты спрайтов — как у острова (MapRenderer): дерево по МИРОВЫМ
		// координатам (TreeVariantFor), камень — по клеточным (StoneVariantFor).
		uint seed32 = (uint)WorldSeed;
		if (!isCut && hasTree)
			return DecorTree0 + (UnifiedWorldGenerator.TreeVariantFor((int)wx, (int)wy, seed32) & 3);
		if (!isMined && hasStone)
			return DecorStone0 + (UnifiedWorldGenerator.StoneVariantFor((int)cellX, (int)cellY, seed32) & 3);
		return -1;
	}

	/// <summary>Поставить тайлы всем клеткам чанка.</summary>
	private void PaintChunk(ChunkKey key, bool clearEmptyLayers)
		=> ScheduleChunkPaint(key, clearEmptyLayers);

	/// <summary>
	/// Пометить только внешнюю кромку чанка и соседние клетки. Внутренние клетки уже
	/// отмечены во время PaintCell; обход полного 66×66 прямоугольника был лишним.
	/// </summary>
	private void MarkTerrainRing(long originX, long originY)
	{
		if (_mountainTerrain == null && _wallTerrain == null)
			return;
		if (!FitsTileMap(originX, originY, 1))
			return;
		long maxX = originX + WorldCoordinates.ChunkSize;
		long maxY = originY + WorldCoordinates.ChunkSize;

		// Top and bottom rows, including the two outside corners.
		for (long x = originX - 1; x <= maxX; x++)
		{
			MarkTerrainAt(x, originY - 1);
			MarkTerrainAt(x, maxY);
		}
		// Left and right columns, excluding corners already handled above.
		for (long y = originY; y < maxY; y++)
		{
			MarkTerrainAt(originX - 1, y);
			MarkTerrainAt(maxX, y);
		}
	}

	private void MarkTerrainAt(long x, long y)
	{
		// Mark even cells whose feature was just removed. The terrain predicate passed
		// to Flush decides whether to keep or erase the tile. Checking IsWallCell here
		// would miss a wall that has already been removed from _blocks.
		if (!FitsTileMapCell(x, y))
			return;
		var pos = new Vector2I((int)x, (int)y);
		_mountainTerrain?.MarkDirty(pos);
		_wallTerrain?.MarkDirty(pos);
		_wallPlanTerrain?.MarkDirty(pos);
		_farmTerrain?.MarkDirty(pos);
		_farmPlanTerrain?.MarkDirty(pos);
	}

	/// <summary>Кольцо 3×3 вокруг одной клетки — для одиночных меток (стена/её снос).</summary>
	private void MarkTerrainCellRing(long cellX, long cellY)
	{
		if (_mountainTerrain == null && _wallTerrain == null && _wallPlanTerrain == null && _farmTerrain == null && _farmPlanTerrain == null)
			return;
		for (long y = cellY - 1; y <= cellY + 1; y++)
		{
			for (long x = cellX - 1; x <= cellX + 1; x++)
			{
				if (!FitsTileMapCell(x, y))
					continue;
				MarkTerrainAt(x, y);
			}
		}
	}

	// --- Диагностика для SeamInspector (src/tools): только чтение, без мутаций ---

	/// <summary>Есть ли в клетке мира хоть один блок (план/готовое) — диагностика.</summary>
	public bool HasAnyBlockAt(long cellX, long cellY)
	{
		ChunkKey key = WorldCoordinates.ChunkForTile(cellX, cellY);
		if (!_blocks.TryGetValue(key, out Dictionary<byte, HashSet<ushort>> map) || map.Count == 0)
			return false;
		LocalCell local = WorldCoordinates.LocalForTile(cellX, cellY);
		ushort idx = (ushort)(local.Y * WorldCoordinates.ChunkSize + local.X);
		foreach (HashSet<ushort> set in map.Values)
		{
			if (set.Contains(idx))
				return true;
		}
		return false;
	}

	/// <summary>Нарисован ли в клетке мира декор (диагностика шва).</summary>
	public bool HasAnyDrawnTileAt(long cellX, long cellY)
	{
		ChunkKey key = WorldCoordinates.ChunkForTile(cellX, cellY);
		if (!_chunkData.TryGetValue(key, out WorldChunk chunk))
			return false;
		LocalCell local = WorldCoordinates.LocalForTile(cellX, cellY);
		GeneratedCell cell = chunk.Cells.Span[local.Y * WorldCoordinates.ChunkSize + local.X];
		return cell.Biome != 0 || cell.Variant != 0 || cell.ForestQ16 != 0 || cell.StoneQ16 != 0;
	}

	/// <summary>Что стоит в клетке мира по мнению рендерера — диагностика шва.</summary>
	public string DescribeCellVisual(long cellX, long cellY)
	{
		ChunkKey key = WorldCoordinates.ChunkForTile(cellX, cellY);
		LocalCell local = WorldCoordinates.LocalForTile(cellX, cellY);
		bool resident = _chunkData.ContainsKey(key);
		var sb = new System.Text.StringBuilder(256);
		sb.Append("StreamingWorldView: cell=(").Append(cellX).Append(',').Append(cellY)
		  .Append(") chunk=(").Append(key.X).Append(',').Append(key.Y)
		  .Append(") local=(").Append(local.X).Append(',').Append(local.Y)
		  .Append(") resident=").Append(resident ? "y" : "n");
		if (resident && _chunkData.TryGetValue(key, out WorldChunk chunk))
		{
			GeneratedCell cell = chunk.Cells.Span[local.Y * WorldCoordinates.ChunkSize + local.X];
			sb.Append(" terrain=").Append(cell.Terrain)
			  .Append(" biome=").Append(cell.Biome)
			  .Append(" variant=").Append(cell.Variant)
			  .Append(" forest=").Append(cell.ForestQ16)
			  .Append(" stone=").Append(cell.StoneQ16);
		}
		if (_blocks.TryGetValue(key, out Dictionary<byte, HashSet<ushort>> map) && map.Count > 0)
		{
			ushort idx = (ushort)(local.Y * WorldCoordinates.ChunkSize + local.X);
			sb.Append(" types=");
			bool first = true;
			foreach (KeyValuePair<byte, HashSet<ushort>> pair in map)
			{
				if (!pair.Value.Contains(idx)) continue;
				if (!first) sb.Append(',');
				sb.Append(pair.Key);
				first = false;
			}
			if (first) sb.Append('-');
		}
		return sb.Append('\n').ToString();
	}

	/// <summary>Компактная строка диагностики клетки мира (для полосы/сводки).</summary>
	public string CompactCellVisual(long cellX, long cellY)
	{
		ChunkKey key = WorldCoordinates.ChunkForTile(cellX, cellY);
		bool resident = _chunkData.ContainsKey(key);
		bool block = HasAnyBlockAt(cellX, cellY);
		return resident || block ? $"world:{(resident ? "res" : "-")}{(block ? "+blk" : "")}" : "world:-";
	}

	/// <summary>Есть ли гора в мировой клетке (по resident-чанку).</summary>
	private bool IsMountainAt(long cellX, long cellY)
	{
		ChunkKey key = WorldCoordinates.ChunkForTile(cellX, cellY);
		if (!_chunkData.TryGetValue(key, out WorldChunk chunk))
			return false;
		if (HasBlock(WorldDeltaKind.MinedStone, cellX, cellY))
			return false;
		LocalCell local = WorldCoordinates.LocalForTile(cellX, cellY);
		return chunk.Cells.Span[local.Y * WorldCoordinates.ChunkSize + local.X].Terrain
			== BaseTerrainKind.Mountain;
	}

	private bool IsMountainCell(Vector2I cell) => IsMountainAt(cell.X, cell.Y);

	private bool IsWallCell(Vector2I cell) => HasBlock(WallKind, cell.X, cell.Y);

	private bool IsWallPlanCell(Vector2I cell) => HasBlock(WorldDeltaKind.WallPlan, cell.X, cell.Y);

	private bool IsCropCell(Vector2I cell) => HasBlock(WorldDeltaKind.Crop, cell.X, cell.Y);

	private bool IsCropPlanCell(Vector2I cell) => HasBlock(WorldDeltaKind.CropPlan, cell.X, cell.Y);

	/// <summary>Перерисовать одну клетку (после AddBlock/RemoveBlock).</summary>
	private void RepaintCell(long cellX, long cellY)
	{
		ChunkKey key = WorldCoordinates.ChunkForTile(cellX, cellY);
		if (!_chunkData.TryGetValue(key, out WorldChunk chunk))
			return;
		long originX = key.X * (long)WorldCoordinates.ChunkSize;
		long originY = key.Y * (long)WorldCoordinates.ChunkSize;
		if (!FitsTileMap(originX, originY) || !FitsTileMapCell(cellX, cellY))
			return;
		if (_paintJobs.ContainsKey(key))
		{
			_textureDirty.Add(key);
			return;
		}
		LocalCell local = WorldCoordinates.LocalForTile(cellX, cellY);
		int localIndex = local.Y * WorldCoordinates.ChunkSize + local.X;
		_blocks.TryGetValue(key, out Dictionary<byte, HashSet<ushort>> kinds);
		_paintedChunks.Add(key); // §117/B3: одиночная перекраска тоже кладёт тайлы
		PaintCell(cellX, cellY, localIndex, chunk.Cells.Span[localIndex], new BlockLookup(kinds), clearEmptyLayers: true);
		MarkTerrainCellRing(cellX, cellY);
	}

	/// <summary>Один тайл на клетку: ТОЛЬКО ДЕКОР (лес/камни) + метки игрока. Земли нет — она у шейдера.</summary>
	private void PaintCell(long cellX, long cellY, int localIndex, GeneratedCell cell,
		BlockLookup kinds, bool clearEmptyLayers)
	{
		// Остров колонии: декор острова (деревья/камни) рисует MapRenderer своими
		// слоями. Ответ «внутри острова?» — у двери (WorldModel.IsInsideIsland):
		// единственное место, где записано, где остров кончается (гард magic_1024).
		if (WorldModel.IsInsideIsland(cellX, cellY))
			return;

		var pos = new Vector2I((int)cellX, (int)cellY);

		// ЗЕМЛЮ во всём мире (остров + за кромкой) рисует terrain_blend.gdshader
		// (MapRenderer): здесь кладём только прозрачный декор поверх неё.
		// Горы рисует _mountainLayer (terrain-автотайлинг, тайлы из сцен игры).
		// W-03: сглаживание кромки воды — ОДНА функция (terrain_blend.gdshader,
		// evaluate_biome + shore_inset) для острова и для мира за 1024. Этот слой
		// не должен рисовать ни земли, ни воды: тайл декора собран BlendRect на
		// пустой альфе, поэтому подложки-квадрата 64×64 поверх шейдера нет и
		// «лесенок» 64 px за кромкой он не вносит (см. BuildDecorSource).
		int decorIndex = GetDecorIndex(cellX, cellY, localIndex, cell, kinds);
		if (decorIndex < 0)
			_decorLayer.EraseCell(pos);
		else
			_decorLayer.SetCell(pos, SrcDecor, new Vector2I(decorIndex, 0));

		if (_mountainLayer != null && cell.Terrain == BaseTerrainKind.Mountain)
			_mountainTerrain.MarkDirty(pos);

		if (clearEmptyLayers)
		{
			_markLayer.EraseCell(pos);
			_planLayer?.EraseCell(pos);
			_wallPlanLayer?.EraseCell(pos);
			_farmPlanLayer?.EraseCell(pos);
		}
		if (BlockLookup.Contains(kinds.Wall, localIndex))
		{
			if (_wallLayer != null)
				_wallTerrain.MarkDirty(pos);
			else
				_markLayer.SetCell(pos, SrcWall, Vector2I.Zero);
		}
		else if (BlockLookup.Contains(kinds.Crop, localIndex))
		{
			if (_farmLayer != null)
				_farmTerrain.MarkDirty(pos);
			else
				_markLayer.SetCell(pos, SrcCrop, Vector2I.Zero);
		}
		else if (BlockLookup.Contains(kinds.Stockpile, localIndex))
			_markLayer.SetCell(pos, SrcStockpile, Vector2I.Zero);
		else if (BlockLookup.Contains(kinds.Building, localIndex))
			_markLayer.SetCell(pos, SrcWork, Vector2I.Zero);

		if (BlockLookup.Contains(kinds.WallPlan, localIndex))
		{
			if (_wallPlanLayer != null)
				_wallPlanTerrain.MarkDirty(pos);
			else
				_planLayer?.SetCell(pos, SrcWall, Vector2I.Zero);
		}
		else if (BlockLookup.Contains(kinds.CropPlan, localIndex))
		{
			if (_farmPlanLayer != null)
				_farmPlanTerrain.MarkDirty(pos);
			else
				_planLayer?.SetCell(pos, SrcCrop, Vector2I.Zero);
		}
		else if (BlockLookup.Contains(kinds.StockpilePlan, localIndex))
			_planLayer?.SetCell(pos, SrcStockpile, Vector2I.Zero);
		else if (BlockLookup.Contains(kinds.BuildingPlan, localIndex)
			|| BlockLookup.Contains(kinds.CutTreePlan, localIndex)
			|| BlockLookup.Contains(kinds.MinedStonePlan, localIndex))
			_planLayer?.SetCell(pos, SrcWork, Vector2I.Zero);
	}

	private readonly struct BlockLookup
	{
		public readonly HashSet<ushort> CutTree;
		public readonly HashSet<ushort> MinedStone;
		public readonly HashSet<ushort> Wall;
		public readonly HashSet<ushort> Crop;
		public readonly HashSet<ushort> Stockpile;
		public readonly HashSet<ushort> Building;
		public readonly HashSet<ushort> WallPlan;
		public readonly HashSet<ushort> CropPlan;
		public readonly HashSet<ushort> StockpilePlan;
		public readonly HashSet<ushort> BuildingPlan;
		public readonly HashSet<ushort> CutTreePlan;
		public readonly HashSet<ushort> MinedStonePlan;

		public BlockLookup(Dictionary<byte, HashSet<ushort>> map)
		{
			CutTree = Get(map, WorldDeltaKind.CutTree);
			MinedStone = Get(map, WorldDeltaKind.MinedStone);
			Wall = Get(map, WallKind);
			Crop = Get(map, WorldDeltaKind.Crop);
			Stockpile = Get(map, WorldDeltaKind.Stockpile);
			Building = Get(map, WorldDeltaKind.Building);
			WallPlan = Get(map, WorldDeltaKind.WallPlan);
			CropPlan = Get(map, WorldDeltaKind.CropPlan);
			StockpilePlan = Get(map, WorldDeltaKind.StockpilePlan);
			BuildingPlan = Get(map, WorldDeltaKind.BuildingPlan);
			CutTreePlan = Get(map, WorldDeltaKind.CutTreePlan);
			MinedStonePlan = Get(map, WorldDeltaKind.MinedStonePlan);
		}

		private static HashSet<ushort> Get(Dictionary<byte, HashSet<ushort>> map, byte kind)
			=> map != null && map.TryGetValue(kind, out HashSet<ushort> set) ? set : null;

		public static bool Contains(HashSet<ushort> set, int localIndex)
			=> set != null && set.Contains((ushort)localIndex);
	}

	/// <summary>
	/// Клетки чанка представимы в int32: TileMapLayer адресует клетки int32, и за этим
	/// диапазоном рисование/стирание «завернулось» бы и стёрло чужие тайлы. Такой чанк
	/// просто не рисуем (мир дальше ~±2.1 млрд клеток не отображается — предел TileMapLayer).
	/// </summary>
	// ================= task-8: руда стримингового мира =================

	/// <summary>
	/// Запланировать фоновый расчёт базового поля руды чанка и 4 соседей (маски
	/// на границе нужны с обеих сторон). Тяжёлые сэмплы мира — НЕ на главном потоке.
	/// </summary>
	private void ScheduleOreChunk(ChunkKey key)
	{
		if (_oreLayer == null || _oreJobs.ContainsKey(key) || _oreReady.Contains(key))
			return;
		ulong seed = WorldSeed;
		uint ver = GeneratorVersion;
		_oreJobs[key] = Task.Run(() => new OreArrays
		{
			Key = key,
			Center = OreWorldField.EnsureChunk(key.X, key.Y, seed, ver),
			N = OreWorldField.EnsureChunk(key.X, key.Y - 1, seed, ver),
			S = OreWorldField.EnsureChunk(key.X, key.Y + 1, seed, ver),
			E = OreWorldField.EnsureChunk(key.X + 1, key.Y, seed, ver),
			W = OreWorldField.EnsureChunk(key.X - 1, key.Y, seed, ver),
		});
	}

	/// <summary>Дренаж готовых фоновых полей и покраска чанков руды (главный поток).</summary>
	private void ProcessOreJobs()
	{
		if (_oreJobs.Count == 0)
			return;
		_oreJobBuffer.Clear();
		foreach (var kvp in _oreJobs)
			if (kvp.Value.IsCompleted)
				_oreJobBuffer.Add(kvp.Key);
		for (int i = 0; i < _oreJobBuffer.Count; i++)
		{
			ChunkKey key = _oreJobBuffer[i];
			Task<OreArrays> task = _oreJobs[key];
			_oreJobs.Remove(key);
			if (task.IsFaulted)
			{
				GD.PrintErr($"[World] ore chunk failed {key}: {task.Exception?.GetBaseException().Message}");
				continue;
			}
			OreArrays bundle = task.Result;
			_oreArrays[key] = bundle.Center;
			_oreArrays[new ChunkKey(key.X, key.Y - 1)] = bundle.N;
			_oreArrays[new ChunkKey(key.X, key.Y + 1)] = bundle.S;
			_oreArrays[new ChunkKey(key.X + 1, key.Y)] = bundle.E;
			_oreArrays[new ChunkKey(key.X - 1, key.Y)] = bundle.W;
			_oreReady.Add(key);
			// N-5 (task-23): декор этого чанка мог быть отложен — докрашиваем.
			EnqueueDecorRepair(key);
			EnsureOrePainted(key);
			// Кромка соседей: их маски на стыке теперь знают про этот чанк.
			PaintOreEdge(new ChunkKey(key.X, key.Y - 1), 1);
			PaintOreEdge(new ChunkKey(key.X, key.Y + 1), 0);
			PaintOreEdge(new ChunkKey(key.X + 1, key.Y), 2);
			PaintOreEdge(new ChunkKey(key.X - 1, key.Y), 3);
		}
		if (_oreJobBuffer.Count > 0)
			_oreJobBuffer.Clear();
	}

	/// <summary>N-5: поставить чанк в очередь докраса декора (ore-массивы готовы).</summary>
	private void EnqueueDecorRepair(ChunkKey key)
	{
		if (_decorLayer == null || !_chunkData.ContainsKey(key) || !_oreReady.Contains(key))
			return;
		if (!_decorRepairQueued.Add(key))
			return;
		_decorRepairCursor[key] = 0;
		_decorRepairQueue.Add(key);
	}

	/// <summary>
	/// N-5: докрас декора чанка, у которого на момент первой покраски поле руды ещё
	/// не было посчитано. Бюджет на кадр, чтобы не давать хитч на главном потоке.
	/// </summary>
	private void ProcessDecorRepair(int cellBudget)
	{
		if (_decorRepairQueue.Count == 0 || _decorLayer == null)
			return;
		int budget = Math.Max(64, cellBudget);
		while (budget > 0 && _decorRepairQueue.Count > 0)
		{
			ChunkKey key = _decorRepairQueue[0];
			if (!_chunkData.TryGetValue(key, out WorldChunk chunk) || !_oreReady.Contains(key))
			{
				_decorRepairQueue.RemoveAt(0);
				_decorRepairQueued.Remove(key);
				_decorRepairCursor.Remove(key);
				continue;
			}

			_blocks.TryGetValue(key, out Dictionary<byte, HashSet<ushort>> map);
			var lookup = new BlockLookup(map);
			int cursor = _decorRepairCursor.TryGetValue(key, out int saved) ? saved : 0;
			int end = Math.Min(WorldChunk.CellCount, cursor + budget);
			ReadOnlySpan<GeneratedCell> cells = chunk.Cells.Span;
			long originX = key.X * (long)WorldCoordinates.ChunkSize;
			long originY = key.Y * (long)WorldCoordinates.ChunkSize;
			for (int index = cursor; index < end; index++)
			{
				long cellX = originX + index % WorldCoordinates.ChunkSize;
				long cellY = originY + index / WorldCoordinates.ChunkSize;
				if (WorldModel.IsInsideIsland(cellX, cellY) || !FitsTileMapCell(cellX, cellY))
					continue;
				var pos = new Vector2I((int)cellX, (int)cellY);
				int decorIndex = GetDecorIndex(cellX, cellY, index, cells[index], lookup);
				if (decorIndex < 0)
					_decorLayer.EraseCell(pos);
				else
					_decorLayer.SetCell(pos, SrcDecor, new Vector2I(decorIndex, 0));
				budget--;
			}

			if (end >= WorldChunk.CellCount)
			{
				_decorRepairQueue.RemoveAt(0);
				_decorRepairQueued.Remove(key);
				_decorRepairCursor.Remove(key);
			}
			else
			{
				_decorRepairCursor[key] = end;
			}
		}
	}

	private readonly List<ChunkKey> _oreJobBuffer = new();

	// N-5 (task-23): покадровый докрас ДЕКОРА после готовности ore-массивов чанка
	// (при первой покраске декор на таких чанках консервативно отложен).
	private readonly Dictionary<ChunkKey, int> _decorRepairCursor = new();
	private readonly List<ChunkKey> _decorRepairQueue = new();
	private readonly HashSet<ChunkKey> _decorRepairQueued = new();
	private const int DecorRepairCellsPerFrame = 2048;

	private void RepaintOreNeighbor(ChunkKey key)
	{
		if (_oreReady.Contains(key) && _oreArrays.ContainsKey(key))
			PaintOreChunk(key);
	}

	/// <summary>Перекрасить только кромку чанка (маска на стыке изменилась).
	/// Полная перекраска соседа на каждый готовый чанк — 5×4096 клеток, лишний хитч.</summary>
	private void PaintOreEdge(ChunkKey key, int side)
	{
		if (_oreLayer == null || !_oreReady.Contains(key) || !_chunkData.ContainsKey(key))
			return;
		long originX = key.X * (long)WorldCoordinates.ChunkSize;
		long originY = key.Y * (long)WorldCoordinates.ChunkSize;
		int last = WorldCoordinates.ChunkSize - 1;
		for (int i = 0; i <= last; i++)
		{
			switch (side)
			{
				case 0: PaintOreCell(originX + i, originY); break;          // север
				case 1: PaintOreCell(originX + i, originY + last); break;   // юг
				case 2: PaintOreCell(originX, originY + i); break;          // запад
				default: PaintOreCell(originX + last, originY + i); break;  // восток
			}
		}
	}

	/// <summary>
	/// Инвариант покраски: чанк ДОЛЖЕН быть покрашен, если его данные на месте,
	/// ore-массивы готовы, а покраски ещё не было. Вызывается из ApplyReady (данные
	/// могли прийти позже поля), из ProcessOreJobs и после erase. force — перекрасить
	/// даже если чанк считался покрашенным (часть клеток могла быть стёрта erase).
	/// </summary>
	private void EnsureOrePainted(ChunkKey key, bool force = false)
	{
		if (!_chunkData.ContainsKey(key))
			return;
		if (!_oreReady.Contains(key))
		{
			ScheduleOreChunk(key);
			return;
		}
		if (!force && _orePainted.Contains(key))
			return;
		PaintOreChunk(key);
	}

	/// <summary>Полная покраска слоя руды одного чанка (главный поток, массивы уже в кэше).</summary>
	private void PaintOreChunk(ChunkKey key)
	{
		if (_oreLayer == null || !_chunkData.ContainsKey(key))
			return;
		long originX = key.X * (long)WorldCoordinates.ChunkSize;
		long originY = key.Y * (long)WorldCoordinates.ChunkSize;
		if (!FitsTileMap(originX, originY))
			return;
		for (int index = 0; index < WorldCoordinates.ChunkSize * WorldCoordinates.ChunkSize; index++)
		{
			int x = index % WorldCoordinates.ChunkSize;
			int y = index / WorldCoordinates.ChunkSize;
			PaintOreCell(originX + x, originY + y);
		}
		_orePainted.Add(key);
	}

	/// <summary>Одна клетка слоя руды: тип/состояние из поля+мутаций, маска по соседям.</summary>
	private void PaintOreCell(long cellX, long cellY)
	{
		if (_oreLayer == null || !FitsTileMapCell(cellX, cellY))
			return;
		// Остров рисует MapRenderer своим слоем — здесь гейт, как у декора.
		if (WorldModel.IsInsideIsland(cellX, cellY))
			return;

		var pos = new Vector2I((int)cellX, (int)cellY);
		byte type = EffectiveOreType(cellX, cellY);
		if (type == OreConfig.None)
		{
			if (_orePaintedChunkOf(cellX, cellY))
				_oreLayer.EraseCell(pos);
			return;
		}

		// N-5: руда выше декора по z, но старый тайл дерева под ней не оставляем —
		// после выработки он стал бы виден.
		_decorLayer?.EraseCell(pos);

		int mask = ComputeOreMask(cellX, cellY, type);
		int maskIndex = OreMaskHelper.IndexOf((byte)mask);
		if (maskIndex < 0)
		{
			_oreLayer.EraseCell(pos);
			return;
		}
		ushort reserve = EffectiveOreReserve(cellX, cellY);
		ushort initial = BaseOreReserve(cellX, cellY);
		byte state = (byte)OreConfig.StateFor(reserve, initial);
		if (!_oreTiles.TryGetTile(type, state, maskIndex, out int sourceId, out Vector2I atlas))
			return; // арта нет — warning уже отдан билдером
		_oreLayer.SetCell(pos, sourceId, atlas);
	}

	private bool _orePaintedChunkOf(long cellX, long cellY)
	{
		ChunkKey key = WorldCoordinates.ChunkForTile(cellX, cellY);
		return _orePainted.Contains(key);
	}

	/// <summary>
	/// N-5 (task-23): БАЗОВАЯ руда клетки по УЖЕ посчитанным массивам чанка.
	/// null — поле чанка ещё не посчитано (декор отложен); true — клетка жилы
	/// (декор запрещён и ПОСЛЕ выработки — как остров очищает декор на жиле);
	/// false — руды нет. Без тяжёлых синхронных расчётов.
	/// </summary>
	private bool? OreBaseAtCellCached(long cellX, long cellY)
	{
		ChunkKey key = WorldCoordinates.ChunkForTile(cellX, cellY);
		if (!_oreArrays.TryGetValue(key, out OreWorldField.ChunkOre chunk))
			return null;
		LocalCell local = WorldCoordinates.LocalForTile(cellX, cellY);
		int index = local.Y * WorldCoordinates.ChunkSize + local.X;
		return chunk.Types[index] != OreConfig.None;
	}

	/// <summary>Есть ли руда в клетке по УЖЕ посчитанным массивам (без семпла поля).</summary>
	private bool OreCellHasOreCached(long cellX, long cellY)
	{
		ChunkKey key = WorldCoordinates.ChunkForTile(cellX, cellY);
		if (!_oreArrays.TryGetValue(key, out OreWorldField.ChunkOre chunk))
			return false;
		LocalCell local = WorldCoordinates.LocalForTile(cellX, cellY);
		int index = local.Y * WorldCoordinates.ChunkSize + local.X;
		byte type = chunk.Types[index];
		if (type == OreConfig.None)
			return false;
		if (OreWorldState.TryGet((int)cellX, (int)cellY, out ushort mutated) && mutated == 0)
			return false;
		return true;
	}

	private byte EffectiveOreType(long cellX, long cellY)
	{
		ChunkKey key = WorldCoordinates.ChunkForTile(cellX, cellY);
		if (!_oreArrays.TryGetValue(key, out OreWorldField.ChunkOre chunk))
			return OreConfig.None;
		LocalCell local = WorldCoordinates.LocalForTile(cellX, cellY);
		byte type = chunk.Types[local.Y * WorldCoordinates.ChunkSize + local.X];
		if (type == OreConfig.None)
			return OreConfig.None;
		if (OreWorldState.TryGet((int)cellX, (int)cellY, out ushort mutated) && mutated == 0)
			return OreConfig.None; // выработано
		return type;
	}

	private ushort EffectiveOreReserve(long cellX, long cellY)
	{
		if (OreWorldState.TryGet((int)cellX, (int)cellY, out ushort mutated))
			return mutated;
		return BaseOreReserve(cellX, cellY);
	}

	private ushort BaseOreReserve(long cellX, long cellY)
	{
		ChunkKey key = WorldCoordinates.ChunkForTile(cellX, cellY);
		if (!_oreArrays.TryGetValue(key, out OreWorldField.ChunkOre chunk))
			return 0;
		LocalCell local = WorldCoordinates.LocalForTile(cellX, cellY);
		return chunk.Reserves[local.Y * WorldCoordinates.ChunkSize + local.X];
	}

	private int ComputeOreMask(long cellX, long cellY, byte type)
	{
		bool n = EffectiveOreType(cellX, cellY - 1) == type;
		bool e = EffectiveOreType(cellX + 1, cellY) == type;
		bool s = EffectiveOreType(cellX, cellY + 1) == type;
		bool w = EffectiveOreType(cellX - 1, cellY) == type;
		int mask = 0;
		if (n) mask |= OreMaskHelper.N;
		if (e) mask |= OreMaskHelper.E;
		if (s) mask |= OreMaskHelper.S;
		if (w) mask |= OreMaskHelper.W;
		if (n && e && EffectiveOreType(cellX + 1, cellY - 1) == type) mask |= OreMaskHelper.NE;
		if (e && s && EffectiveOreType(cellX + 1, cellY + 1) == type) mask |= OreMaskHelper.SE;
		if (s && w && EffectiveOreType(cellX - 1, cellY + 1) == type) mask |= OreMaskHelper.SW;
		if (w && n && EffectiveOreType(cellX - 1, cellY - 1) == type) mask |= OreMaskHelper.NW;
		return mask;
	}

	/// <summary>Перекрасить одну клетку руды (после добычи/смены состояния).</summary>
	public void RefreshOreCell(int localX, int localY)
	{
		if (WorldModel.IsInsideIsland(localX, localY))
			return; // остров — слоем MapRenderer
		ChunkKey key = WorldCoordinates.ChunkForTile(localX, localY);
		if (!_oreArrays.ContainsKey(key))
		{
			ScheduleOreChunk(key);
			return;
		}
		PaintOreCell(localX, localY);
		// Маска соседей могла измениться — обновим 8 клеток вокруг.
		PaintOreCell(localX + 1, localY);
		PaintOreCell(localX - 1, localY);
		PaintOreCell(localX, localY + 1);
		PaintOreCell(localX, localY - 1);
		PaintOreCell(localX + 1, localY + 1);
		PaintOreCell(localX - 1, localY + 1);
		PaintOreCell(localX + 1, localY - 1);
		PaintOreCell(localX - 1, localY - 1);
	}

	/// <summary>OreWorldState.CellChanged может прийти из sim-потока — маршалим.</summary>
	private void OnOreStateChanged(int localX, int localY)
		=> Callable.From(() => RefreshOreCell(localX, localY)).CallDeferred();

	/// <summary>Применить запись OreMined из чанк-стора (загрузка чанка).</summary>
	private static void ApplyOreRecord(ChunkKey key, ushort localIndex, ushort reserve)
	{
		int localX = (int)(key.X * WorldCoordinates.ChunkSize + localIndex % WorldCoordinates.ChunkSize);
		int localY = (int)(key.Y * WorldCoordinates.ChunkSize + localIndex / WorldCoordinates.ChunkSize);
		OreWorldState.ApplyLoadedReserve(localX, localY, reserve);
		if (WorldModel.IsInsideIsland(localX, localY))
			MapRenderer.Instance?.ApplyWorldOreToIslandCell(localX, localY);
	}

	public override void _ExitTree()
	{
		OreWorldState.CellChanged -= OnOreStateChanged;
		_oreJobs.Clear();
		_oreArrays.Clear();
		_oreReady.Clear();
		_orePainted.Clear();
		base._ExitTree();
	}
	private static bool FitsTileMap(long originX, long originY, long margin = 0)
		=> originX >= (long)int.MinValue + margin
			&& originY >= (long)int.MinValue + margin
			&& originX <= (long)int.MaxValue - WorldCoordinates.ChunkSize + 1 - margin
			&& originY <= (long)int.MaxValue - WorldCoordinates.ChunkSize + 1 - margin;

	private static bool FitsTileMapCell(long x, long y)
		=> x >= int.MinValue && x <= int.MaxValue
			&& y >= int.MinValue && y <= int.MaxValue;

}
