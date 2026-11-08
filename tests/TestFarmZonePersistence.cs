using System;
using System.Collections.Generic;
using Godot;
using Game.Core;
using Game.Core.WorldStreaming.Mutation;
using Game.Simulation;

// ============================================================================
// 10-01 / 15-06 / 17-02: Farm-зоны (тайлы + AutoPlant) переживают сохранение и
// загрузку колонии (.zones.json). До фикса SaveZones писал ТОЛЬКО Work-зоны:
// после загрузки грядки были (Crop/CropPlan островной дельты), а Zone не было —
// клик по ферме не открывал окно и авто-посадка не возобновлялась.
//
// ВАЖНО: менеджеры-синглтоны общие с другими тестами процесса, поэтому сетапы
// идемпотентны (ClearAll перед наполнением), а тестовый файл зон удаляется.
// По образцу tests/TestIslandZonePersistence.cs.
// Запуск: godot --headless --path . --script res://tests/run_farm_zone_persistence.gd
// ============================================================================

public partial class TestFarmZonePersistence : RefCounted
{
	private static int _failures;

	/// <summary>Уникальный zoneId теста: не задеть .zones.json реальной колонии.</summary>
	private const string TestZoneId = "colony_farmzone_selftest";

	private const ulong Seed = 0x0FA4_2026UL;

	private static void Check(bool cond, string name)
	{
		if (cond)
		{
			GD.Print($"[TEST 10-01] {name} OK");
		}
		else
		{
			_failures++;
			GD.PrintErr($"[TEST 10-01] {name} FAILED");
		}
	}

	public int Run()
	{
		GD.Print("==================================================");
		GD.Print("[TEST 10-01] Farm-зоны: сохранение/загрузка (.zones.json)");
		GD.Print("==================================================");

		string path = ZonePersistence.ZonesPath(TestZoneId);
		DeleteFile(path);
		DeleteFile(path + ".tmp");

		try
		{
			RoundTripFarmZone();
			LegacySnapshotWithoutFarmTail(path);
		}
		finally
		{
			DeleteFile(path);
			DeleteFile(path + ".tmp");
			FarmJobManager.Instance.ClearAll();
			CropGrowthManager.Instance.ClearAll();
			JobDispatcher.Instance.JobIndex.Clear();
			ZoneManager.Instance.ClearAll();
		}

		GD.Print($"[TEST 10-01] {(_failures == 0 ? "ALL PASSED" : "FAILURES: " + _failures)}");
		return _failures == 0 ? 0 : 1;
	}

	/// <summary>
	/// Основной сценарий: создать Work+Farm через публичный API, сохранить снапшот,
	/// сбросить состояние колонии, загрузить снапшот и сверить id/тип/клетки/AutoPlant.
	/// </summary>
	private static void RoundTripFarmZone()
	{
		ZoneManager.Instance.ClearAll();

		bool workOk = ZoneManager.Instance.CreateWorkZoneAt(
			200, 180, 1UL << (int)JobTypeId.TreeChopping, 128, 5, out Zone workZone, out string workFail);
		Check(workOk, $"Work-зона создана (err={workFail ?? "null"})");
		int workId = workZone?.Id ?? -1;

		var farmTiles = new List<(int X, int Y)> { (300, 300), (301, 300), (302, 300), (302, 301) };
		List<Zone> created = ZoneManager.Instance.CreateFarmZones(farmTiles);
		Check(created.Count == 1, $"Farm-зона создана через публичный API (факт {created.Count})");
		if (created.Count != 1)
			return; // дальше проверять нечего: без зоны сейв пустой

		Zone farmCreated = created[0];
		int farmId = farmCreated.Id;
		ZoneManager.Instance.SetAutoPlant(farmId, true);
		Check(farmCreated.AutoPlantEnabled, "AutoPlant включён до сейва");

		Check(ZonePersistence.SaveZones(TestZoneId, Seed, out string saveError),
			$"SaveZones (Work+Farm) OK (err={saveError ?? "null"})");

		// ---------- сброс состояния колонии (как Main.ResetColonyState) ----------
		ZoneManager.Instance.ClearAll();
		Check(ZoneManager.Instance.GetAllZones().Count == 0
			&& !ZoneManager.Instance.TryGetZoneAt(300, 300, out _),
			"состояние колонии сброшено: зон и грядок в ZoneManager нет");

		// ---------- загрузка снапшота ----------
		Check(ZonePersistence.TryLoadZones(TestZoneId, Seed, out string loadError),
			$"TryLoadZones OK (err={loadError ?? "null"})");

		List<Zone> all = ZoneManager.Instance.GetAllZones();
		Check(all.Count == 2, $"восстановлено ровно 2 зоны (факт {all.Count})");

		Zone farm = null;
		Zone work = null;
		foreach (Zone z in all)
		{
			if (z.Kind == ZoneKind.Farm)
				farm = z;
			else if (z.Kind == ZoneKind.Work)
				work = z;
		}

		Check(farm != null && farm.Kind == ZoneKind.Farm && farm.Id == farmId,
			$"Farm-зона на месте с тем же id={farmId} и типом Farm");
		Check(farm != null && TilesEqual(farm.Tiles, farmTiles),
			$"клетки Farm-зоны совпадают ({farmTiles.Count} шт.)");
		Check(farm != null && !farm.AutoPlantEnabled,
			"до реплея состояния автопосадка ещё выключена (порядок загрузки)");
		Check(farm != null && farm.RequiredSeedItem == ItemId.Grain, "тип семян Farm-зоны сохранён");
		Check(farm != null && ZoneManager.Instance.TryGetZoneAt(301, 300, out Zone hit)
			&& hit != null && hit.Id == farmId && hit.Kind == ZoneKind.Farm,
			"клик по клетке фермы находит восстановленную Farm-зону");

		Check(work != null && work.Id == workId && work.CenterX == 200 && work.CenterY == 180
			&& work.RadiusTiles == 128 && work.DesiredWorkers == 5,
			"Work-зона рядом не пострадала (id/центр/радиус/заявка)");

		// ---------- 15-06: реплей грядок/культур, затем отложенная автопосадка ----------
		// Пустая грядка должна получить работу посева, а грядка с уже
		// восстановленной культурой — нет (иначе культура начнёт расти заново).
		FarmJobManager.Instance.CompletePlot(300, 300);
		FarmJobManager.Instance.CompletePlot(301, 300);
		CropGrowthManager.Instance.RestoreCrop(301, 300, farmId, 2, 5f);

		Check(ZoneManager.Instance.ApplyPendingAutoPlantRestores() == 1,
			"автопосадка восстановленных ферм включена отложенным проходом");
		Check(farm != null && farm.AutoPlantEnabled, "AutoPlant пережил загрузку");

		bool emptyBedFound;
		int emptyBedJob = JobDispatcher.Instance.JobIndex.TryGetJobIdByPos(
			300, 300, JobTypeId.Planting, out emptyBedFound);
		Check(emptyBedFound && emptyBedJob >= 0, "пустая грядка получила работу посева");

		bool cropBedFound;
		int cropBedJob = JobDispatcher.Instance.JobIndex.TryGetJobIdByPos(
			301, 300, JobTypeId.Planting, out cropBedFound);
		Check(!cropBedFound || cropBedJob < 0, "грядку с восстановленной культурой НЕ пересаживают");
		Check(CropGrowthManager.Instance.TryGetCrop(301, 300, out CropData crop) && crop.Stage == 2,
			"стадия восстановленной культуры не сброшена на 1");

		// ---------- id продолжаются, а не переиспользуются ----------
		bool nextOk = ZoneManager.Instance.CreateWorkZoneAt(
			400, 300, 1UL << (int)JobTypeId.TreeChopping, 64, 1, out Zone next, out string nextFail);
		Check(nextOk && next != null && next.Id > Math.Max(farmId, workId),
			$"новые зоны получают id больше восстановленных (id={next?.Id}, err={nextFail ?? "null"})");
	}

	/// <summary>
	/// Совместимость: снапшот СТАРОГО формата (без новых полей "kind"/"tiles") грузится
	/// без исключения и БЕЗ Farm-зон, а Work-зона читается как раньше.
	/// </summary>
	private static void LegacySnapshotWithoutFarmTail(string path)
	{
		ZoneManager.Instance.ClearAll();
		System.IO.File.WriteAllText(path, LegacyZonesJson(1UL << (int)JobTypeId.TreeChopping));

		bool loaded = false;
		string legacyError = null;
		try
		{
			loaded = ZonePersistence.TryLoadZones(TestZoneId, Seed, out legacyError);
		}
		catch (Exception ex)
		{
			Check(false, $"старый снапшот без нового хвоста не бросает исключение ({ex.Message})");
			return;
		}

		Check(loaded && legacyError == null,
			$"старый снапшот (без kind/tiles) загружен (err={legacyError ?? "null"})");

		List<Zone> zones = ZoneManager.Instance.GetAllZones();
		int farmCount = 0;
		Zone legacyWork = null;
		foreach (Zone z in zones)
		{
			if (z.Kind == ZoneKind.Farm)
				farmCount++;
			else if (z.Kind == ZoneKind.Work)
				legacyWork = z;
		}

		Check(farmCount == 0, $"в старом снапшоте Farm-зон нет (факт {farmCount})");
		Check(legacyWork != null && legacyWork.Id == 1 && legacyWork.CenterX == 200
			&& legacyWork.CenterY == 180 && legacyWork.RadiusTiles == 128
			&& legacyWork.JobMask == (1UL << (int)JobTypeId.TreeChopping),
			"Work-зона старого снапшота восстановлена как раньше");
	}

	/// <summary>
	/// JSON формата v1 ДО 10-01: только Work-поля, без "kind" и "tiles".
	/// Именно такие файлы лежат у игроков — они обязаны грузиться без исключения.
	/// </summary>
	private static string LegacyZonesJson(ulong jobMask)
	{
		var ci = System.Globalization.CultureInfo.InvariantCulture;
		return "{ \"version\": 1, \"seed\": \"" + Seed.ToString(ci) + "\", \"nextId\": 2, \"zones\": ["
			+ " { \"id\": 1, \"name\": \"Зона #1\", \"center\": [200, 180], \"radius\": 128,"
			+ " \"jobMask\": \"" + jobMask.ToString(ci) + "\", \"desiredWorkers\": 5,"
			+ " \"priorityOverride\": -1, \"enabled\": true, \"anchorBlockId\": -1 } ] }";
	}

	private static bool TilesEqual(HashSet<(int X, int Y)> actual, List<(int X, int Y)> expected)
	{
		if (actual == null || expected == null || actual.Count != expected.Count)
			return false;
		foreach (var tile in expected)
		{
			if (!actual.Contains(tile))
				return false;
		}
		return true;
	}

	private static void DeleteFile(string path)
	{
		try { System.IO.File.Delete(path); } catch { /* лучшее усилие */ }
	}
}