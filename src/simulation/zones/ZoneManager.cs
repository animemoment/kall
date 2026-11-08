using System;
using System.Collections.Generic;
using System.Threading;
using Godot;
using Game.Core;

namespace Game.Simulation;

/// <summary>
/// Унифицированный менеджер зон: Farm-грядки (тайлы) и Work-зоны-бригадиры
/// (§119 P0: центр + РАДИУС 64..512, клетки НЕ материализуются) в одном
/// владельце. Farm-тайлы по-прежнему «один тайл — одна Farm-зона»; Work-зоны
/// НЕ пишут тайлы в _tileToZoneId — попадание проверяется формулой радиуса,
/// пересечения Work-зон разрешает партиция P1 (ScheduleRepartition).
/// Заменяет FarmZoneManager + WorkZoneManager (те оставлены тонкими фасадами).
/// </summary>
public sealed class ZoneManager
{
	public static ZoneManager Instance { get; } = new();

	private const int ChunkDim = 64; // 64x64 чанка = 1024x1024 тайлов (остров)
	private const int WorkersPerChunkBudget = 48;
	private const int MaxAssignPerChunk = 8;
	private const int AgentReservedMarker = -2;

	private readonly object _lock = new();
	private readonly Dictionary<int, Zone> _zones = new(256);
	private readonly Dictionary<(int X, int Y), int> _tileToZoneId = new(4096);
	private int _nextZoneId = 1;

	/// <summary>
	/// Санитарный предел координаты центра при ЗАГРУЗКЕ зоны из сейва (AUDIT task-17):
	/// защищает от мусора в файле, но не зависит от ещё не установленных границ карты.
	/// </summary>
	private const int MaxRestoreCoordinate = 1 << 20;

	// Границы карты для клиппа дисков Work-зон (B1: volatile — пишет sim, читает UI).
	private static volatile int _mapW = 512;
	private static volatile int _mapH = 512;

	/// <summary>§119 P1: ширина карты для покрытий (ZoneCoverageMap).</summary>
	public static int MapWidth => _mapW;

	/// <summary>§119 P1: высота карты для покрытий (ZoneCoverageMap).</summary>
	public static int MapHeight => _mapH;

	// Round-robin кап зон-бригадиров на тик.
	private const int MaxZonesPerPass = 4;
	private int _zoneScanIndex;

	private readonly ThreadLocal<int[]> _zoneBuffer = new(() => new int[WorkersPerChunkBudget]);

	public Zone HoveredZone { get; private set; }
	public Zone SelectedZone { get; private set; }

	public event Action OnZonesUpdated;
	public event Action<Zone> OnZoneHovered;
	public event Action<Zone> OnZoneSelected;
	public event Action OnZoneDeselected;
	public event Action<Zone> OnAutoPlantChanged;

	public static void SetMapBounds(int w, int h)
	{
		if (w > 0) _mapW = w;
		if (h > 0) _mapH = h;
	}

	/// <summary>
	/// §119 P0: индексы чанков (16×16, сетка ChunkDim×ChunkDim), пересекающих
	/// ДИСК зоны, — аналитически, БЕЗ материализации клеток. Чанк пересекает
	/// диск, если ближайшая к центру точка прямоугольника чанка в радиусе r
	/// (clamp центра в прямоугольник чанка). Тот же индекс-пространство, что
	/// у CalcChunkIndices, — диспатч (JobIndex.GetChunkJobCount) не меняется.
	/// </summary>
	private static List<int> CalcChunkIndicesForDisc(int cx, int cy, int r)
	{
		var set = new HashSet<int>();
		int minCx = Math.Clamp((cx - r) >> 4, 0, ChunkDim - 1);
		int maxCx = Math.Clamp((cx + r) >> 4, 0, ChunkDim - 1);
		int minCy = Math.Clamp((cy - r) >> 4, 0, ChunkDim - 1);
		int maxCy = Math.Clamp((cy + r) >> 4, 0, ChunkDim - 1);
		long rr = (long)r * r;
		for (int chy = minCy; chy <= maxCy; chy++)
		{
			int y0 = chy << 4;
			// Ближайшая к центру Y-координата прямоугольника чанка.
			int ny = Math.Clamp(cy, y0, y0 + 15);
			long dy = ny - cy;
			for (int chx = minCx; chx <= maxCx; chx++)
			{
				int x0 = chx << 4;
				int nx = Math.Clamp(cx, x0, x0 + 15);
				long dx = nx - cx;
				if (dx * dx + dy * dy <= rr)
					set.Add(chy * ChunkDim + chx);
			}
		}
		return new List<int>(set);
	}

	// ---------------- Farm API (mirror FarmZoneManager) ----------------

	/// <summary>Создать Farm-зоны из тайлов (BFS-сплит на связные компоненты).</summary>
	public List<Zone> CreateFarmZones(List<(int X, int Y)> tiles, string namePrefix = "Ферма")
	{
		var result = new List<Zone>();
		if (tiles == null || tiles.Count == 0) return result;
		var components = SplitIntoConnectedComponents(new HashSet<(int X, int Y)>(tiles));
		lock (_lock)
		{
			foreach (var comp in components)
			{
				// Пропускаем компоненты, пересекающие чужие зоны.
				bool clash = false;
				foreach (var pos in comp)
				{
					if (_tileToZoneId.ContainsKey(pos)) { clash = true; break; }
				}
				if (clash) continue;
				int id = _nextZoneId++;
				var zone = new Zone(id, $"{namePrefix} #{id}", ZoneKind.Farm, comp);
				_zones[id] = zone;
				foreach (var pos in comp)
					_tileToZoneId[pos] = id;
				result.Add(zone);
				BumpZonesVersion_NoLock();
			}
		}
		if (result.Count > 0)
			Callable.From(() => OnZonesUpdated?.Invoke()).CallDeferred();
		return result;
	}

	/// <summary>
	/// AUDIT task-17 (M-4): восстановить Farm-зону из .zones.json С СОХРАНЁННЫМ id.
	///
	/// Раньше загрузка шла через <see cref="CreateFarmZones"/> и выдавала зоне НОВЫЙ
	/// id: все внешние ссылки на зону (CropData.ZoneId в островных дельтах, будущие
	/// привязки/отчёты по зонам) после перезапуска указывали в пустоту, а id зон
	/// «плыли» от сессии к сессии. Теперь id сохраняется.
	///
	/// Защита от мусора в файле: клетки, уже занятые другой зоной, в новую не
	/// попадают; если сохранённые клетки распались на несколько компонент
	/// (в норме такого быть не может — ферма всегда создаётся связной), основная
	/// компонента (содержащая первую клетку записи) получает сохранённый id,
	/// остальные — новые id (иначе потеряли бы грядки).
	///
	/// Возвращает false, если данных нет/зона с таким id уже существует.
	/// </summary>
	public bool RestoreFarmZoneForPersistence(int id, string name,
		List<(int X, int Y)> tiles, bool autoPlant, Game.Core.CropType crop, out Zone zone)
	{
		zone = null;
		if (id < 1 || tiles == null || tiles.Count == 0)
			return false;

		var components = SplitIntoConnectedComponents(new HashSet<(int X, int Y)>(tiles));
		if (components.Count == 0)
			return false;

		lock (_lock)
		{
			if (_zones.ContainsKey(id))
				return false; // дубликат записи в файле — вторую не ставим

			// Основная компонента — та, что содержит ПЕРВУЮ сохранённую клетку
			// (по ней же ZonePersistence делает проверку идемпотентности).
			int primary = 0;
			for (int i = 0; i < components.Count; i++)
			{
				if (components[i].Contains(tiles[0]))
				{
					primary = i;
					break;
				}
			}

			for (int i = 0; i < components.Count; i++)
			{
				var comp = components[i];
				var free = new HashSet<(int X, int Y)>(comp.Count);
				foreach (var pos in comp)
				{
					if (!_tileToZoneId.ContainsKey(pos))
						free.Add(pos);
				}
				if (free.Count == 0)
					continue;

				int zoneId;
				if (i == primary)
				{
					zoneId = id;
					if (zoneId >= _nextZoneId)
						_nextZoneId = zoneId + 1;
				}
				else
				{
					zoneId = _nextZoneId++;
				}

				string zoneName = string.IsNullOrEmpty(name) ? $"Ферма #{zoneId}" : name;
				if (i != primary)
					zoneName = $"{zoneName} #{zoneId}";

				var created = new Zone(zoneId, zoneName, ZoneKind.Farm, free)
				{
					Crop = crop,
					AutoPlantEnabled = autoPlant,
					// Семя = урожай культуры зоны (как в PlantingJobHandler.SeedItemFor):
					// поле легаси, но пусть не врёт, если кто-то его прочитает.
					RequiredSeedItem = CropRegistry.Get(crop).HarvestItem
				};
				_zones[zoneId] = created;
				foreach (var pos in free)
					_tileToZoneId[pos] = zoneId;
				BumpZonesVersion_NoLock();
				if (i == primary)
					zone = created;
			}
		}

		if (zone == null)
			return false;
		Callable.From(() => OnZonesUpdated?.Invoke()).CallDeferred();
		return true;
	}

	public void SetAutoPlant(int zoneId, bool enabled)
	{
		Zone zone = null;
		lock (_lock)
		{
			if (_zones.TryGetValue(zoneId, out zone) && zone.IsFarmZone)
				zone.AutoPlantEnabled = enabled;
			else
				zone = null;
		}
		if (zone != null)
		{
			if (enabled)
			{
				foreach (var (x, y) in zone.Tiles)
				{
					if (FarmJobManager.Instance.IsGardenBed(x, y) && !CropGrowthManager.Instance.HasCrop(x, y))
						JobBroker.Instance.RegisterPlanting(x, y, zone.Id);
				}
			}
			else
			{
				foreach (var (x, y) in zone.Tiles)
					JobBroker.Instance.UnregisterPlanting(x, y);
			}
			OnAutoPlantChanged?.Invoke(zone);
		}
	}

	/// <summary>Удалить тайлы (фермерский инструмент, правая кнопка).</summary>
	public void RemoveTiles(List<(int X, int Y)> tilesToRemove)
	{
		if (tilesToRemove == null || tilesToRemove.Count == 0) return;
		// Двухфазно: под _lock только структуры зон (тайлы/индексы/BFS-сплит),
		// симуляционные вызовы (RemoveCrop/Unregister/Register — чужие локи)
		// СТРОГО вне _lock. Раньше всё было под одним hold: вложенные локи +
		// долгий BFS = contention sim-потока.
		var affectedZones = new HashSet<int>();
		List<(int ZoneId, List<HashSet<(int X, int Y)>> Comps, bool WasAuto)> splits = null;
		List<int> removedZoneIds = null;
		lock (_lock)
		{
			var zoneTilesToRemove = new Dictionary<int, List<(int X, int Y)>>();
			foreach (var pos in tilesToRemove)
			{
				if (_tileToZoneId.TryGetValue(pos, out int zoneId))
				{
					if (_zones.TryGetValue(zoneId, out var zone))
					{
						// Рабочие зоны неделимы: удаление тайлов из них запрещено.
						if (zone.IsWorkZone) continue;
						if (!zoneTilesToRemove.TryGetValue(zoneId, out var list))
						{
							list = new List<(int X, int Y)>();
							zoneTilesToRemove[zoneId] = list;
						}
						list.Add(pos);
						affectedZones.Add(zoneId);
					}
					_tileToZoneId.Remove(pos);
				}
			}
			foreach (var (zid, toRemoveList) in zoneTilesToRemove)
			{
				if (_zones.TryGetValue(zid, out var z))
				{
					var newTiles = new HashSet<(int X, int Y)>(z.Tiles);
					foreach (var p in toRemoveList)
						newTiles.Remove(p);
					z.Tiles = newTiles;
				}
			}
			foreach (int zoneId in affectedZones)
			{
				if (!_zones.TryGetValue(zoneId, out var zone)) continue;
				if (zone.Tiles.Count == 0)
				{
					_zones.Remove(zoneId);
					BumpZonesVersion_NoLock();
					if (SelectedZone?.Id == zoneId) DeselectZoneInternal();
					if (HoveredZone?.Id == zoneId) HoveredZone = null;
					(removedZoneIds ??= new List<int>()).Add(zoneId);
					continue;
				}
				var subComponents = SplitIntoConnectedComponents(zone.Tiles);
				if (subComponents.Count > 1)
				{
					bool wasAuto = zone.AutoPlantEnabled;
					_zones.Remove(zoneId);
					BumpZonesVersion_NoLock();
					if (SelectedZone?.Id == zoneId) DeselectZoneInternal();
					foreach (var comp in subComponents)
					{
						int newId = _nextZoneId++;
						var newZone = new Zone(newId, $"Ферма #{newId}", ZoneKind.Farm, comp)
						{
							AutoPlantEnabled = wasAuto
						};
						_zones[newId] = newZone;
						BumpZonesVersion_NoLock();
						foreach (var pos in comp)
							_tileToZoneId[pos] = newId;
					}
					(splits ??= new List<(int, List<HashSet<(int, int)>>, bool)>()).Add((zoneId, subComponents, wasAuto));
				}
			}
		}
		// Вне лока: симуляция.
		FarmJobManager.Instance.UnmarkPlotsBatch(tilesToRemove);
		for (int i = 0; i < tilesToRemove.Count; i++)
		{
			var pos = tilesToRemove[i];
			if (CropGrowthManager.Instance.TryGetCrop(pos.X, pos.Y, out var crop) && crop.Stage == 4)
			{
				CropGrowthManager.Instance.HarvestCrop(pos.X, pos.Y, null);
			}
			CropGrowthManager.Instance.RemoveCrop(pos.X, pos.Y);
			JobBroker.Instance.UnregisterPlanting(pos.X, pos.Y);
			JobBroker.Instance.UnregisterHarvest(pos.X, pos.Y);
		}
		if (splits != null)
		{
			for (int s = 0; s < splits.Count; s++)
			{
				var (_, comps, wasAuto) = splits[s];
				if (!wasAuto) continue;
				for (int c = 0; c < comps.Count; c++)
				{
					foreach (var (x, y) in comps[c])
					{
						if (FarmJobManager.Instance.IsGardenBed(x, y) && !CropGrowthManager.Instance.HasCrop(x, y))
						{
							// zoneId новой зоны неизвестен здесь без лока — ищем по тайлу.
							if (TryGetZoneAt(x, y, out var nz))
								JobBroker.Instance.RegisterPlanting(x, y, nz.Id);
						}
					}
				}
			}
		}
		Callable.From(() => OnZonesUpdated?.Invoke()).CallDeferred();
	}

	// ---------------- Work API ----------------

	/// <summary>
	/// §119 P0: создать рабочую зону-бригадир = ЦЕНТР + РАДИУС (64..512, кламп),
	/// ровно ОДИН бит JobMask (одна зона = один тип работ), DesiredWorkers —
	/// заявка без потолка (0..999). Клетки НЕ материализуются; пересечения
	/// зон не режутся (партиция — P1). Параметр радиусный: UI-слайдер до P2
	/// шлёт легаси-значение, оно клампится в 64..512.
	///
	/// ЛЕГАСИ-путь (старый WorkZoneController): тип зоны не передан — отрасль
	/// восстанавливаем по маске работ (ZoneIndustryRegistry.FromLegacyJobMask),
	/// имя зоны = название отрасли. Зона с Kind == Work больше не создаётся.
	/// </summary>
	public bool CreateWorkZoneAt(int cx, int cy, ulong jobMask, int radiusTiles, int desiredWorkers, out Zone zone, out string failReason)
	{
		ZoneKind kind = ZoneIndustryRegistry.FromLegacyJobMask(jobMask);
		return CreateWorkZoneAt(kind, cx, cy, radiusTiles, desiredWorkers, jobMask, out zone, out failReason);
	}

	/// <summary>
	/// task-12: создать рабочую зону ОТРАСЛИ. Маска берётся из профиля отрасли
	/// (<see cref="ZoneIndustryProfile.PrimaryJob"/> — ровно один бит), имя зоны —
	/// название отрасли («Станкостроение #7»), чтобы в окне зоны был виден её тип.
	/// </summary>
	public bool CreateWorkZoneAt(ZoneKind kind, int cx, int cy, int radiusTiles,
		int desiredWorkers, out Zone zone, out string failReason)
	{
		var profile = ZoneIndustryRegistry.Get(kind);
		return CreateWorkZoneAt(profile.Kind, cx, cy, radiusTiles, desiredWorkers,
			ZoneIndustryRegistry.PrimaryJobMask(profile.Kind), out zone, out failReason);
	}

	private bool CreateWorkZoneAt(ZoneKind kind, int cx, int cy, int radiusTiles,
		int desiredWorkers, ulong jobMask, out Zone zone, out string failReason)
	{
		zone = null;
		failReason = null;
		// Farm-зона — тайловый тип (грядки), радиусной моделью не создаётся.
		if (kind.IsFarmZone())
		{
			failReason = "Farm-зона создаётся тайлами, а не радиусом";
			return false;
		}
		// §119: ровно один тип работ — ровно один бит; бит 0 (JobTypeId.None) запрещён.
		if (jobMask == 0 || (jobMask & (jobMask - 1)) != 0 || (jobMask & 1UL) != 0)
		{
			failReason = "маска должна содержать ровно один бит типа работ (не 0)";
			return false;
		}
		int w = _mapW, h = _mapH;
		if (cx < 0 || cy < 0 || cx >= w || cy >= h)
		{
			failReason = "центр вне карты";
			return false;
		}
		int r = Math.Clamp(radiusTiles, WorkZone.MinRadiusTiles, WorkZone.MaxRadiusTiles);
		desiredWorkers = Math.Clamp(desiredWorkers, 0, WorkZone.MaxDesiredWorkers);
		var industry = ZoneIndustryRegistry.Get(kind);
		lock (_lock)
		{
			int id = _nextZoneId++;
			zone = new Zone(id, $"{industry.Title} #{id}", industry.Kind, cx, cy, r,
				desiredWorkers, jobMask, CalcChunkIndicesForDisc(cx, cy, r));
			_zones[id] = zone;
			BumpZonesVersion_NoLock();
		}
		ScheduleRepartition();
		Callable.From(() => OnZonesUpdated?.Invoke()).CallDeferred();
		return true;
	}

	// ---------------- Общие операции ----------------

	/// <summary>
	/// §119 P7б (task-17): восстановить Work-зону с СОХРАНЁННЫМ id и полями
	/// (обратная сторона снапшота GetAllZones для ZonePersistence). Вызывается
	/// ТОЛЬКО из пути загрузки колонии (до старта сима/диспатча). Соседние id
	/// не переиспользуются: _nextZoneId всегда растёт (max(restored)+1).
	/// </summary>
	public bool RestoreWorkZoneForPersistence(
		int id, string name, int cx, int cy, int radiusTiles, int desiredWorkers,
		ulong jobMask, int? priorityOverride, bool enabled, int anchorBlockId,
		ZoneKind kind = ZoneKind.Work)
	{
		if (id < 1 || jobMask == 0 || (jobMask & (jobMask - 1)) != 0 || (jobMask & 1UL) != 0)
			return false;
		// AUDIT task-17 (blocker, совместимость сейвов): центр НЕ проверяем по
		// _mapW/_mapH. Загрузка .zones.json идёт до старта симуляции, а границы
		// карты выставляет только DispatchZones (SetMapBounds из ctx.MapWidth/Height):
		// на ПЕРВОЙ загрузке после запуска игры они равны значениям по умолчанию
		// 512×512, поэтому любая зона с центром в правой/нижней половине острова
		// 1024² молча не восстанавливалась (зона пропадала из сейва без ошибки,
		// а следующий SaveZones перезаписывал файл уже без неё).
		// Для ЧТЕНИЯ сейва границы не авторитет: зона была проверена при создании
		// (CreateWorkZoneAt), а ContainsTile/покрытие клипаются по живым границам.
		if (cx < 0 || cy < 0 || cx > MaxRestoreCoordinate || cy > MaxRestoreCoordinate)
			return false;
		// СОВМЕСТИМОСТЬ СЕЙВОВ: старый .zones.json не хранит отрасль (kind="work") —
		// восстанавливаем её по маске работ. Отрасль из файла главнее маски.
		ZoneKind restoredKind = kind == ZoneKind.Work || kind.IsFarmZone()
			? ZoneIndustryRegistry.FromLegacyJobMask(jobMask)
			: kind.NormalizeLegacy();
		if (restoredKind.IsFarmZone())
			return false; // farm-зоны идут другой веткой загрузки (тайлы)
		var industry = ZoneIndustryRegistry.Get(restoredKind);
		// task-20: маска зоны обязана совпадать с ОСНОВНОЙ работой отрасли. Легаси-
		// сейвы хранили Crafting/Construction и т.п. — без нормализации зона после
		// загрузки не получила бы ни одной работы («зона-декорация»).
		ulong primaryMask = ZoneIndustryRegistry.PrimaryJobMask(industry.Kind);
		if (primaryMask != 0)
			jobMask = primaryMask;
		// Старые сейвы хранят авто-имя «Зона #N» (у зоны не было отрасли). Даём
		// зоне человеческое имя отрасли, чтобы окно зоны показывало тип; имя,
		// заданное игроком вручную, НЕ трогаем.
		string restoredName = name;
		if (string.IsNullOrEmpty(restoredName) || IsLegacyAutoZoneName(restoredName))
			restoredName = $"{industry.Title} #{id}";
		int r = Math.Clamp(radiusTiles, WorkZone.MinRadiusTiles, WorkZone.MaxRadiusTiles);
		lock (_lock)
		{
			if (_zones.ContainsKey(id))
				return false; // дубликат в файле — вторую молча не ставим
			if (id >= _nextZoneId)
				_nextZoneId = id + 1;
			var zone = new Zone(id, restoredName,
				industry.Kind, cx, cy, r, Math.Clamp(desiredWorkers, 0, WorkZone.MaxDesiredWorkers),
				jobMask, CalcChunkIndicesForDisc(cx, cy, r))
			{
				PriorityOverride = priorityOverride is >= 0 and <= 10 ? priorityOverride : null,
				Enabled = enabled,
				AnchorBlockId = anchorBlockId
			};
			_zones[id] = zone;
			BumpZonesVersion_NoLock();
		}
		ScheduleRepartition();
		Callable.From(() => OnZonesUpdated?.Invoke()).CallDeferred();
		return true;
	}

	/// <summary>
	/// Старое авто-имя зоны («Зона #7»), которое генерировал CreateWorkZoneAt до
	/// task-12. Такие имена при загрузке заменяем на «&lt;Отрасль&gt; #id»;
	/// имена, заданные игроком, не трогаем.
	/// </summary>
	private static bool IsLegacyAutoZoneName(string name)
	{
		if (string.IsNullOrEmpty(name))
			return true;
		if (!name.StartsWith("Зона #", StringComparison.Ordinal))
			return false;
		return int.TryParse(name.AsSpan(6), out _);
	}

	public bool RemoveZone(int id)
	{
		bool wasSelected;
		lock (_lock)
		{
			if (!_zones.TryGetValue(id, out var zone))
				return false;
			foreach (var t in zone.Tiles)
				_tileToZoneId.Remove(t);
			_zones.Remove(id);
			BumpZonesVersion_NoLock();
			wasSelected = SelectedZone?.Id == id;
			if (wasSelected) DeselectZoneInternal();
			if (HoveredZone?.Id == id) HoveredZone = null;
		}
		if (wasSelected) Callable.From(() => OnZoneDeselected?.Invoke()).CallDeferred();
		// §119 P1: очередь удалённой зоны падает в непокрытые (спека: снос зоны —
		// работы падают в непокрытые; репуш подберёт ≤30с).
		ZoneJobRouter.Instance.OnZoneRemoved(id);
		ScheduleRepartition();
		Callable.From(() => OnZonesUpdated?.Invoke()).CallDeferred();
		return true;
	}

	/// <summary>
	/// §119 P0/P5: удалить Work-зону с ЦЕНТРОМ в указанной клетке (путь для
	/// сноса центрального блока-якоря). Farm-зоны не имеют центра — не матчатся.
	/// Работы обрезанной зоны по контракту §119 ОСТАЮТСЯ у зоны до её смерти:
	/// здесь зона исчезает целиком, а непокрытые работы подберёт репуш (P1).
	/// </summary>
	public bool RemoveZoneAtCenter(int cx, int cy)
	{
		int targetId = -1;
		lock (_lock)
		{
			foreach (var kvp in _zones)
			{
				var z = kvp.Value;
				if (z.IsWorkZone && z.CenterX == cx && z.CenterY == cy)
				{
					targetId = kvp.Key;
					break;
				}
			}
		}
		return targetId != -1 && RemoveZone(targetId);
	}

	// ---------------- §119 P1: партиция покрытий (заглушка P0 → событие P1) ----------------

	private volatile bool _repartitionDirty;

	/// <summary>
	/// §119: заявить пересчёт покрытий зон. P0 — пометка dirty; P1 — дополнительно
	/// будит ZoneJobRouter (битмапы покрытия пересчитаются не чаще 1р/2с —
	/// throttle внутри ZoneCoverageMap).
	/// </summary>
	public void ScheduleRepartition()
	{
		_repartitionDirty = true;
		ZoneJobRouter.Instance.Coverage.RequestRebuild();
	}

	/// <summary>Для P1/тестов: есть ли незавершённая заявка на пересчёт.</summary>
	public bool IsRepartitionPending => _repartitionDirty;

	/// <summary>§119 P1: снять заявку после фактического пересчёта (зовёт ZoneJobRouter.Sweep).</summary>
	public void ClearRepartitionPending() => _repartitionDirty = false;

	/// <summary>§119 P1: версия состава зон (для покрытий/диагностики).</summary>
	public int ZonesVersion => Volatile.Read(ref _zonesVersion);

	/// <summary>
	/// §119 P0: поиск зоны по клетке. Farm — точная битмапа _tileToZoneId;
	/// Work — формула радиуса по списку зон (зон немного; битмапа покрытия
	/// клетка→зона на каждый тип работ — P1). При перекрытии Work-зон —
	/// ближайший центр (детерминизм; партиция P1 заменит на AssignedCount).
	/// </summary>
	public bool TryGetZoneAt(int x, int y, out Zone zone)
	{
		zone = FindZoneAt(x, y);
		return zone != null;
	}

	private Zone FindZoneAt(int x, int y)
	{
		lock (_lock)
		{
			if (_tileToZoneId.TryGetValue((x, y), out int zoneId) && _zones.TryGetValue(zoneId, out var farm))
				return farm;
			Zone best = null;
			long bestDist = long.MaxValue;
			foreach (var z in _zones.Values)
			{
				if (!z.IsWorkZone || !z.ContainsTile(x, y))
					continue;
				long dx = x - z.CenterX;
				long dy = y - z.CenterY;
				long d = dx * dx + dy * dy;
				if (d < bestDist || (d == bestDist && best != null && z.Id > best.Id))
				{
					best = z;
					bestDist = d;
				}
			}
			return best;
		}
	}

	public bool TryGetZoneById(int zoneId, out Zone zone)
	{
		lock (_lock)
		{
			return _zones.TryGetValue(zoneId, out zone);
		}
	}

	// P0-3: кэшированный снимок зон для DispatchZones. GetAllZones делал
	// new List + lock + Sort на КАЖДЫЙ диспатч (сотни раз/с). Снимок валиден,
	// пока Version не изменился (инкремент в Create/Delete/SetWork*).
	private List<Zone> _dispatchSnap;
	private int _dispatchSnapVersion = -1;
	private int _zonesVersion;

	// FIX (bughunt 13-10): отдельный кэш снимка ТОЛЬКО Work-зон. Диспатчу нужны
	// именно они (Farm-зоны не раздаются), и round-robin должен считать их
	// количество, а не общее число зон. Кэш валиден по той же версии состава.
	private List<Zone> _dispatchWorkSnap;
	private int _dispatchWorkSnapVersion = -1;

	public List<Zone> GetAllZones()
	{
		lock (_lock)
		{
			return new List<Zone>(_zones.Values);
		}
	}

	/// <summary>
	/// Снимок зон для диспатч-прохода: без аллокации, если зоны не менялись.
	/// Возвращаемый список НЕ модифицировать (кэш). Только для sim-потока.
	/// </summary>
	public List<Zone> GetDispatchSnapshot()
	{
		int v = Volatile.Read(ref _zonesVersion);
		var snap = Volatile.Read(ref _dispatchSnap);
		if (snap != null && v == Volatile.Read(ref _dispatchSnapVersion))
			return snap;
		lock (_lock)
		{
			v = _zonesVersion;
			if (_dispatchSnap != null && v == _dispatchSnapVersion)
				return _dispatchSnap;
			var fresh = new List<Zone>(_zones.Values);
			fresh.Sort(static (a, b) => a.Id.CompareTo(b.Id));
			_dispatchSnap = fresh;
			Volatile.Write(ref _dispatchSnapVersion, v);
			return fresh;
		}
	}

	private void BumpZonesVersion()
	{
		Interlocked.Increment(ref _zonesVersion);
	}

	/// <summary>
	/// FIX (bughunt 13-10): снимок ТОЛЬКО Work-зон для диспатч-прохода.
	/// Farm-зоны в раздаче не участвуют, но раньше попадали в totalZones —
	/// из-за этого Work-зона обслуживалась раз в N/4 проходов.
	/// Возвращаемый список НЕ модифицировать (кэш). Только для sim-потока.
	/// </summary>
	public List<Zone> GetDispatchWorkSnapshot()
	{
		int v = Volatile.Read(ref _zonesVersion);
		var snap = Volatile.Read(ref _dispatchWorkSnap);
		if (snap != null && v == Volatile.Read(ref _dispatchWorkSnapVersion))
			return snap;
		lock (_lock)
		{
			v = _zonesVersion;
			if (_dispatchWorkSnap != null && v == _dispatchWorkSnapVersion)
				return _dispatchWorkSnap;
			var fresh = new List<Zone>();
			foreach (Zone z in _zones.Values)
			{
				if (z.IsWorkZone)
					fresh.Add(z);
			}
			fresh.Sort(static (a, b) => a.Id.CompareTo(b.Id));
			_dispatchWorkSnap = fresh;
			Volatile.Write(ref _dispatchWorkSnapVersion, v);
			return fresh;
		}
	}

	/// <summary>То же, но под уже взятым _lock (инкремент int атомарен и без Interlocked).</summary>
	private void BumpZonesVersion_NoLock()
	{
		_zonesVersion++;
	}

	/// <summary>
	/// §119 P0: задать РАДИУС зоны (64..512, кламп). Тайлы не перестраиваются —
	/// пересчитываются только аналитические ChunkIndices. Пересечения не режем
	/// (партиция спорных клеток — P1, ScheduleRepartition).
	/// </summary>
	public bool SetWorkRadius(int id, int radius)
	{
		int clamped = Math.Clamp(radius, WorkZone.MinRadiusTiles, WorkZone.MaxRadiusTiles);
		lock (_lock)
		{
			if (!_zones.TryGetValue(id, out var zone) || !zone.IsWorkZone)
				return false;
			zone.RadiusTiles = clamped;
			zone.TilesTarget = clamped; // легаси-поле: до P2 хранит радиус
			zone.ChunkIndices = CalcChunkIndicesForDisc(zone.CenterX, zone.CenterY, clamped);
			zone.ResetAssigned();
			BumpZonesVersion_NoLock();
		}
		ScheduleRepartition();
		Callable.From(() => OnZonesUpdated?.Invoke()).CallDeferred();
		return true;
	}

	/// <summary>§119: задать ЗАЯВКУ на число рабочих (0..999, без потолка).</summary>
	public bool SetWorkDesiredWorkers(int id, int v)
	{
		bool ok;
		lock (_lock)
		{
			ok = _zones.TryGetValue(id, out var zone) && zone.IsWorkZone;
			if (ok)
			{
				zone.DesiredWorkers = Math.Clamp(v, 0, WorkZone.MaxDesiredWorkers);
				BumpZonesVersion_NoLock();
			}
		}
		// §119 P1: DesiredWorkers == 0 снимает владение клетками — покрытию нужен пересчёт.
		if (ok)
			ScheduleRepartition();
		return ok;
	}

	/// <summary>§119: вкл/выкл зоны (выключенная не владеет клетками — покрытию нужен пересчёт).</summary>
	public bool SetWorkEnabled(int id, bool enabled)
	{
		bool ok;
		lock (_lock)
		{
			ok = _zones.TryGetValue(id, out var zone) && zone.IsWorkZone;
			if (ok)
			{
				zone.Enabled = enabled;
				BumpZonesVersion_NoLock();
			}
		}
		if (ok)
		{
			ScheduleRepartition();
			Callable.From(() => OnZonesUpdated?.Invoke()).CallDeferred();
		}
		return ok;
	}

	public bool SetWorkPriorityOverride(int id, int v)
	{
		int clamped = Math.Clamp(v, 0, 10);
		lock (_lock)
		{
			if (!_zones.TryGetValue(id, out var zone) || !zone.IsWorkZone)
				return false;
			zone.PriorityOverride = clamped;
		}
		Callable.From(() => OnZonesUpdated?.Invoke()).CallDeferred();
		return true;
	}

	public void SetHoveredTile(int x, int y)
	{
		Zone newHovered = FindZoneAt(x, y);
		if (HoveredZone != newHovered)
		{
			HoveredZone = newHovered;
			OnZoneHovered?.Invoke(HoveredZone);
			OnZonesUpdated?.Invoke();
		}
	}

	/// <summary>
	/// Выбрать зону по тайлу. Возвращает выбранную (null если промах — выбор снят).
	/// НЕ дёргает чужой Deselect: взаимоисключение окон — на слое контроллеров.
	/// </summary>
	public Zone SelectZoneAt(int x, int y)
	{
		Zone targetZone = FindZoneAt(x, y);
		if (targetZone != null)
		{
			SelectedZone = targetZone;
			OnZoneSelected?.Invoke(SelectedZone);
			OnZonesUpdated?.Invoke();
		}
		else
		{
			DeselectZone();
		}
		return targetZone;
	}

	public void DeselectZone()
	{
		if (SelectedZone != null)
		{
			DeselectZoneInternal();
			OnZoneDeselected?.Invoke();
			OnZonesUpdated?.Invoke();
		}
	}

	private void DeselectZoneInternal() => SelectedZone = null;

	// ---------------- Бригадир-диспатч (только Work-зоны) ----------------

	public int DispatchZones(AgentDataPool pool, SimulationContext ctx)
	{
		if (ctx != null)
			SetMapBounds(ctx.MapWidth, ctx.MapHeight);
		// P0-3: кэшированный снимок вместо new List + Sort на каждый диспатч.
		// FIX (bughunt 13-10): планировать round-robin по WORK-зонам, а не по всем:
		// Farm-зоны в раздаче не участвуют, но раньше попадали в totalZones, и
		// Work-зона обслуживалась раз в N/4 проходов вместо 4 зон за проход.
		var zones = GetDispatchWorkSnapshot();
		if (zones.Count == 0) return 0;
		if (JobDispatcher.Instance.JobIndex.UnclaimedCount <= 0) return 0;
		if (JobDispatcher.Instance.IdleWorkers.TotalIdleCount <= 0) return 0;
		int totalZones = zones.Count;
		int toProcess = Math.Min(MaxZonesPerPass, totalZones);
		int oldScan = Interlocked.Add(ref _zoneScanIndex, toProcess) - toProcess;
		int baseIdx = ((oldScan % totalZones) + totalZones) % totalZones;
		int total = 0;
		for (int zi = 0; zi < toProcess; zi++)
		{
			var zone = zones[(baseIdx + zi) % totalZones];
			// FIX (bughunt 13-1): выключенная зона не диспатчит и НЕ владеет клетками
			// (контракт ZoneCoverageMap.cs:15 и фильтр WorkerAssignmentSystem) — проверка
			// ДО ResetAssigned, чтобы выключенная зона не «теряла» счётчик просто так.
			if (!zone.Enabled) continue;
			// FIX (bughunt 13-2): DesiredWorkers == 0 — это «зона не просит рабочих»
			// (XML-doc Zone/WorkZone), а не «без потолка»: зона не получает никого.
			if (zone.DesiredWorkers <= 0) continue;
			zone.ResetAssigned();
			int zoneAssigned = 0;
			var chunks = zone.ChunkIndices;
			var tiles = zone.Tiles;
			if (chunks == null || tiles == null) continue;
			foreach (int chunkIdx in chunks)
			{
				// FIX (bughunt 13-2): потолок заявки действует всегда (0 отсечён выше),
				// раньше при 0 условие было ложным и потолка не было вовсе.
				if (zoneAssigned >= zone.DesiredWorkers)
					break;
				if (JobDispatcher.Instance.JobIndex.GetChunkJobCount(chunkIdx) == 0)
					continue;
				int[] buf = _zoneBuffer.Value;
				int workerCount = JobDispatcher.Instance.IdleWorkers.CollectIdleWorkersInChunk(chunkIdx, WorkersPerChunkBudget, buf, pool);
				if (workerCount == 0) continue;
				int assigned = 0;
				for (int wi = 0; wi < workerCount; wi++)
				{
					if (assigned >= MaxAssignPerChunk)
						break;
					if (zoneAssigned >= zone.DesiredWorkers)
						break;
					int agentIndex = buf[wi];
					if (agentIndex < 0 || agentIndex >= pool.Capacity)
						continue;
					if (pool.States[agentIndex] != AgentState.Idle)
						continue;
					if (Interlocked.CompareExchange(ref pool.CurrentJobId[agentIndex], AgentReservedMarker, -1) != -1)
						continue;
					try
					{
						int workerTx = pool.CurrentCellX[agentIndex];
						int workerTy = pool.CurrentCellY[agentIndex];
						if (JobDispatcher.Instance.JobIndex.TryClaimForWorkerInChunk(
							chunkIdx, workerTx, workerTy,
							pool.EquippedTools[agentIndex],
							pool, agentIndex, ctx,
							out var activeJob,
							zone.JobMask))
						{
							// J-18: принадлежность клетки зоне проверяем ДО OnStart.
							// Раньше работа чужой зоны (чанк может пересекать несколько
							// зон) уже запускалась через OnStart и тут же отменялась
							// через OnCancel — двойной churn на каждую такую выдачу.
							// Теперь такие работы просто не берём: клейм снимается,
							// агент остаётся свободным и ищет дальше.
							if (!zone.Matches(activeJob.TypeId) || !zone.ContainsTile(activeJob.TargetX, activeJob.TargetY))
							{
								JobDispatcher.Instance.JobIndex.ReleaseWorkerClaim(activeJob.Id);
								pool.CurrentJobId[agentIndex] = -1;
								pool.CurrentJobType[agentIndex] = JobTypeId.None;
								continue;
							}

							JobDispatcher.Instance.IdleWorkers.RemoveIdleWorker(agentIndex, pool);
							pool.CurrentJobId[agentIndex] = activeJob.Id;
							pool.CurrentJobType[agentIndex] = activeJob.TypeId;
							if (JobRegistry.TryGetHandler(activeJob.TypeId, out var handler))
							{
								try
								{
									handler.OnStart(agentIndex, activeJob, pool, ctx);
									if (pool.States[agentIndex] == AgentState.Idle)
									{
										int rollbackId = pool.CurrentJobId[agentIndex];
										if (rollbackId != -1)
										{
											JobDispatcher.Instance.JobIndex.ReleaseWorkerClaim(rollbackId);
											pool.CurrentJobId[agentIndex] = -1;
											pool.CurrentJobType[agentIndex] = JobTypeId.None;
										}
										JobDispatcher.Instance.IdleWorkers.AddIdleWorker(agentIndex, pool);
									}
									else
									{
										// Принадлежность зоне уже проверена до OnStart (J-18),
										// поэтому здесь только фиксация выдачи.
										pool.LastJobCategory[agentIndex] = (int)JobPriorityManager.Instance.GetCategory(activeJob.TypeId);
										zone.RegisterAssigned();
										assigned++;
										zoneAssigned++;
									}
								}
								catch (Exception ex)
								{
									GD.PrintErr($"[ZoneManager] OnStart error {activeJob.TypeId} (agent #{agentIndex}): {ex.Message}\n{ex.StackTrace}");
									JobDispatcher.Instance.JobIndex.ReleaseWorkerClaim(activeJob.Id);
									pool.CurrentJobId[agentIndex] = -1;
									pool.CurrentJobType[agentIndex] = JobTypeId.None;
									pool.States[agentIndex] = AgentState.Idle;
									JobDispatcher.Instance.IdleWorkers.AddIdleWorker(agentIndex, pool);
								}
							}
							else
							{
								JobDispatcher.Instance.JobIndex.ReleaseWorkerClaim(activeJob.Id);
								pool.CurrentJobId[agentIndex] = -1;
								pool.CurrentJobType[agentIndex] = JobTypeId.None;
								JobDispatcher.Instance.IdleWorkers.AddIdleWorker(agentIndex, pool);
							}
						}
						else
						{
							pool.CurrentJobId[agentIndex] = -1;
						}
					}
					catch
					{
						pool.CurrentJobId[agentIndex] = -1;
						throw;
					}
				}
				total += assigned;
			}
		}
		return total;
	}

	private static List<HashSet<(int X, int Y)>> SplitIntoConnectedComponents(HashSet<(int X, int Y)> tiles)
	{
		var result = new List<HashSet<(int X, int Y)>>();
		var unvisited = new HashSet<(int X, int Y)>(tiles);
		var queue = new Queue<(int X, int Y)>();
		var dirs = new (int X, int Y)[] { (0, -1), (0, 1), (-1, 0), (1, 0) };
		while (unvisited.Count > 0)
		{
			using var enumerator = unvisited.GetEnumerator();
			enumerator.MoveNext();
			var start = enumerator.Current;
			var comp = new HashSet<(int X, int Y)>();
			queue.Enqueue(start);
			unvisited.Remove(start);
			while (queue.Count > 0)
			{
				var curr = queue.Dequeue();
				comp.Add(curr);
				foreach (var dir in dirs)
				{
					var neighbor = (curr.X + dir.X, curr.Y + dir.Y);
					if (unvisited.Remove(neighbor))
						queue.Enqueue(neighbor);
				}
			}
			result.Add(comp);
		}
		return result;
	}

	public void ClearAll()
	{
		lock (_lock)
		{
			_zones.Clear();
			_tileToZoneId.Clear();
			_nextZoneId = 1;
			SelectedZone = null;
			HoveredZone = null;
			_repartitionDirty = false;
			BumpZonesVersion_NoLock();
		}
		// §119 P1: смена мира — роутер тоже под ноль (очереди/покрытие старого мира невалидны).
		ZoneJobRouter.Instance.Reset();
		Callable.From(() => OnZonesUpdated?.Invoke()).CallDeferred();
	}
}
