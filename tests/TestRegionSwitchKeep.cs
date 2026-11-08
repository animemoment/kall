using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using Godot;
using Game.Core.WorldStreaming;
using Game.Core.WorldStreaming.Integration;
using Game.Core.WorldStreaming.Mutation;
using Game.UI.Streaming;

namespace Game.Tests;

/// <summary>
/// 12-01 regression (P1): switching the region/seed in game used to drop resident
/// deltas without writing them, so walls/buildings placed in the current window
/// disappeared after "map → other region → map → back".
///
/// Checks:
///  1) edits made before the switch are flushed to the save and come back when the
///     player returns to the region (StreamingWorldView.ResetWorld flush path);
///  2) a save written before the switch (island delta format v1, written as an old
///     build would) is still readable by the current loader;
///  3) with no unsaved deltas the switch behaves exactly as before (nothing is
///     written, the window is cleared and streaming recovers).
/// The island store round-trip mirrors Main.PersistCurrentColony + PrepareIslandStore:
/// the outgoing zone is saved first, then the store is rebound/loaded for the next zone.
/// </summary>
public partial class TestRegionSwitchKeep : RefCounted
{
	private const uint GenVersion = 3;
	private const ulong Seed = 0x5EED1201UL;

	public bool Run()
	{
		GD.Print("==================================================");
		GD.Print("[REGION-SWITCH] 12-01: resident deltas survive a region/seed switch");
		GD.Print("==================================================");
		try
		{
			UnifiedWorldGenerator.SetWorldOrigin(0, 0);

			TestResidentDeltasSurviveRegionSwitch();
			TestIslandDeltasSurviveZoneSwitch();
			TestLegacyIslandSaveLoads();
			TestCleanSwitchKeepsPreviousBehaviour();

			GD.Print("==================================================");
			GD.Print("[REGION-SWITCH OK] ALL TESTS PASSED! (Code 0)");
			GD.Print("==================================================");
			return true;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[REGION-SWITCH FAILED] {ex.Message}\n{ex.StackTrace}");
			return false;
		}
	}

	// ---------------------------------------------------------------- helpers

	private static string NewTempDir(string tag)
		=> Path.Combine(OS.GetUserDataDir(), $"region_switch_{tag}_{Guid.NewGuid():N}");

	private static void CleanupDir(string dir)
	{
		try
		{
			if (Directory.Exists(dir))
				Directory.Delete(dir, recursive: true);
		}
		catch
		{
			// Soft cleanup: I/O workers of a destroyed view must not fail the test.
		}
	}

	private static StreamingWorldView CreateView(ulong seed, string dir, bool wipe)
	{
		var view = new StreamingWorldView
		{
			Name = "RegionSwitchKeepView",
			SaveDirectory = dir,
			WipeStoreOnStart = wipe,
			WorldSeed = seed,
			GeneratorVersion = GenVersion,
			MaxConcurrency = 4,
			MinWindowChunks = 3,
			MaxWindowChunks = 6,
			MaxReadyChunksPerFrame = 16,
			MaxUnloadSchedulesPerFrame = 16,
		};
		view._Ready();
		return view;
	}

	private static bool Pump(StreamingWorldView view, WorldRect rect, Func<bool> done, int timeoutMs, string what)
	{
		var sw = Stopwatch.StartNew();
		while (!done())
		{
			view.Update(rect);
			view._Process(0.016);
			if (sw.ElapsedMilliseconds > timeoutMs)
			{
				GD.PrintErr($"[REGION-SWITCH] Pump timeout ({timeoutMs} ms): {what}");
				return false;
			}
			Thread.Sleep(15);
		}
		return true;
	}

	private static void DestroyView(StreamingWorldView view)
	{
		try
		{
			view.Shutdown();
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[REGION-SWITCH] Shutdown exception: {ex.Message}");
		}
		view.Free();
	}

	/// <summary>Write the island delta file exactly as format v1 defines it (old save).</summary>
	private static void WriteLegacyIslandSave(string path, uint seed,
		(long X, long Y, byte Kind, byte[] Payload)[] cells)
	{
		string dir = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(dir))
			Directory.CreateDirectory(dir);
		using var stream = new FileStream(path, FileMode.Create, System.IO.FileAccess.Write, FileShare.None);
		using var writer = new BinaryWriter(stream);
		writer.Write(Encoding.ASCII.GetBytes("ISM1"));
		writer.Write(1u);      // FormatVersion
		writer.Write(seed);
		writer.Write(cells.Length);
		foreach ((long x, long y, byte kind, byte[] payload) in cells)
		{
			writer.Write(x);
			writer.Write(y);
			writer.Write(kind);
			int len = payload?.Length ?? -1;
			writer.Write(len);
			if (len > 0)
				writer.Write(payload, 0, len);
		}
	}

	// ---------------------------------------------------------------- tests

	/// <summary>
	/// "Built inside the current window → switched region → came back": the wall and
	/// building must be written by ResetWorld and reappear after returning.
	/// Fails on the pre-12-01 code (ResetWorld cleared _chunkData without flushing).
	/// </summary>
	private void TestResidentDeltasSurviveRegionSwitch()
	{
		string dir = NewTempDir("resident");
		var rect = new WorldRect(0, 0, 192, 192);
		const long wallX = 5, wallY = 5;
		const long buildingX = 70, buildingY = 70; // chunk (1,1)
		byte[] payload = { 0x2A, 0x01 };

		try
		{
			var view = CreateView(Seed, dir, wipe: true);
			try
			{
				if (!Pump(view, rect,
					() => view.IsLoaded(new ChunkKey(0, 0))
						&& view.IsLoaded(new ChunkKey(1, 1)),
					30000, "region A window load"))
					throw new Exception("region A window did not load");

				if (!view.AddBlock(WorldDeltaKind.Wall, wallX, wallY))
					throw new Exception("AddBlock(Wall) returned false");
				if (!view.AddBlock(WorldDeltaKind.Building, buildingX, buildingY, payload))
					throw new Exception("AddBlock(Building) returned false");

				// Exactly what Main.OnRegionSelected/OnSeedRegenerated do: same view,
				// new region (origin changes, the window is reset).
				view.ResetWorld(Seed);

				if (view.LoadedChunks != 0 || view.WallCount != 0 || view.HasWall(wallX, wallY))
					throw new Exception("ResetWorld did not drop the resident window");
			}
			finally
			{
				DestroyView(view);
			}

			// The edits must be on disk BEFORE the view is recreated (not only in RAM).
			var store = new WorldChunkStore(dir);
			if (!store.ContainsChunk(0, 0) || !store.ContainsChunk(1, 1))
				throw new Exception("12-01: resident deltas were not flushed to the save before the region switch");

			// "Map → original region": the same store path is opened again.
			var back = CreateView(Seed, dir, wipe: false);
			try
			{
				if (!Pump(back, rect, () => back.IsLoaded(new ChunkKey(1, 1)), 30000, "return window load"))
					throw new Exception("window did not load after returning to the region");
				if (!back.HasWall(wallX, wallY))
					throw new Exception("12-01: wall built before the switch is missing after returning");
				if (!back.HasBlock(WorldDeltaKind.Building, buildingX, buildingY))
					throw new Exception("12-01: building built before the switch is missing after returning");
				if (!back.TryGetBlockData(WorldDeltaKind.Building, buildingX, buildingY, out byte[] restored)
					|| restored == null || restored.Length != payload.Length
					|| restored[0] != payload[0] || restored[1] != payload[1])
					throw new Exception("12-01: building payload did not survive the region switch");
			}
			finally
			{
				DestroyView(back);
			}

			GD.Print("[REGION-SWITCH] resident deltas: flushed on switch, restored on return - OK");
		}
		finally
		{
			CleanupDir(dir);
		}
	}

	/// <summary>
	/// Island layer: Main.PersistCurrentColony writes the outgoing zone's delta file,
	/// then PrepareIslandStore() rebinds the store and loads the next zone. Returning
	/// to the first zone must restore its deltas from the save.
	/// </summary>
	private void TestIslandDeltasSurviveZoneSwitch()
	{
		string dir = NewTempDir("island");
		const uint seedKey = 0xABCDEF01u;
		string zoneA = Path.Combine(dir, "colony_0_0.island.delta");
		string zoneB = Path.Combine(dir, "colony_512_0.island.delta");
		try
		{
			var store = new IslandOverrideStore(seedKey);
			if (!store.TryApply(WorldDeltaKind.Wall, 100, 100))
				throw new Exception("TryApply(Wall) returned false");
			if (!store.TryApply(WorldDeltaKind.Building, 101, 100, new byte[] { 7, 1 }))
				throw new Exception("TryApply(Building) returned false");

			// Step 1 of Main.PersistCurrentColony: outgoing zone is saved first.
			if (!store.TrySave(zoneA, out string saveError))
				throw new Exception($"zone A save failed: {saveError}");

			// Step 2 of PrepareIslandStore: same world seed → RebindSeed is a no-op;
			// zone B has no file yet.
			if (store.RebindSeed(seedKey))
				throw new Exception("RebindSeed reported a change for the same seed key");
			if (store.Load(zoneB))
				throw new Exception("unexpected zone B save");

			// "Return to region A": its save must still be there and loadable.
			var back = new IslandOverrideStore();
			if (!back.Load(zoneA))
				throw new Exception("12-01: zone A save disappeared after the switch");
			if (back.Count != 2 || back.Seed != seedKey)
				throw new Exception($"zone A restored Count={back.Count} Seed=0x{back.Seed:X8}");
			if (!back.Contains(WorldDeltaKind.Wall, 100, 100))
				throw new Exception("12-01: zone A wall missing after the switch");
			if (!back.TryGet(WorldDeltaKind.Building, 101, 100, out byte[] building)
				|| building == null || building.Length != 2 || building[0] != 7)
				throw new Exception("12-01: zone A building payload missing after the switch");

			GD.Print("[REGION-SWITCH] island deltas: saved before the switch, restored on return - OK");
		}
		finally
		{
			CleanupDir(dir);
		}
	}

	/// <summary>Old save: a v1 file written by a previous build must load unchanged.</summary>
	private void TestLegacyIslandSaveLoads()
	{
		string dir = NewTempDir("legacy");
		try
		{
			string path = Path.Combine(dir, "colony_0_0.island.delta");
			WriteLegacyIslandSave(path, 0x12345678u, new (long, long, byte, byte[])[]
			{
				(10, 20, WorldDeltaKind.Wall, Array.Empty<byte>()),
				(11, 20, WorldDeltaKind.Crop, new byte[] { 1, 2, 3, 4 }),
			});

			var store = new IslandOverrideStore();
			if (!store.Load(path))
				throw new Exception("12-01: legacy island save was not loaded");
			if (store.Seed != 0x12345678u || store.Count != 2)
				throw new Exception($"legacy save restored Seed=0x{store.Seed:X8} Count={store.Count}");
			if (!store.Contains(WorldDeltaKind.Wall, 10, 20))
				throw new Exception("legacy save: wall cell missing");
			if (!store.TryGet(WorldDeltaKind.Crop, 11, 20, out byte[] crop)
				|| crop == null || crop.Length != 4 || crop[3] != 4)
				throw new Exception("legacy save: crop payload missing");

			GD.Print("[REGION-SWITCH] legacy island save (format v1) loads - OK");
		}
		finally
		{
			CleanupDir(dir);
		}
	}

	/// <summary>
	/// No unsaved deltas: the switch must not write anything and must clear/reload
	/// the window exactly as before 12-01.
	/// </summary>
	private void TestCleanSwitchKeepsPreviousBehaviour()
	{
		string dir = NewTempDir("clean");
		var rect = new WorldRect(0, 0, 128, 128);
		try
		{
			var view = CreateView(Seed, dir, wipe: true);
			try
			{
				if (!Pump(view, rect, () => view.IsLoaded(new ChunkKey(0, 0)), 30000, "clean window load"))
					throw new Exception("clean window did not load");

				// Nothing was edited: the flush has nothing to write.
				if (view.FlushResidentDeltas(7.0) != 0)
					throw new Exception("a clean window reported dirty chunks to flush");

				view.ResetWorld(Seed);
				if (view.LoadedChunks != 0 || view.WallCount != 0)
					throw new Exception("ResetWorld without dirty deltas changed the window state");
			}
			finally
			{
				DestroyView(view);
			}

			int files = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.bin").Length : 0;
			if (files != 0)
				throw new Exception($"a clean switch created {files} delta files");

			var back = CreateView(Seed, dir, wipe: false);
			try
			{
				if (!Pump(back, rect, () => back.LoadedChunks >= 2, 30000, "clean reload"))
					throw new Exception("streaming did not recover after a clean switch");
				if (back.WallCount != 0)
					throw new Exception($"clean reload WallCount={back.WallCount}");
			}
			finally
			{
				DestroyView(back);
			}

			GD.Print("[REGION-SWITCH] clean switch: no writes, window reset, reload works - OK");
		}
		finally
		{
			CleanupDir(dir);
		}
	}
}