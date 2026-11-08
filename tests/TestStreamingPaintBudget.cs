using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Godot;
using Game.Core.World;
using Game.Core.WorldStreaming;
using Game.UI.Streaming;

namespace Game.Tests;

/// <summary>
/// 01-01: the StreamingWorldView paint budget must be counted in CELLS, not chunks.
/// Old code had `MaxChunkOpsPerFrame × MaxCellsPerPaintSlice` = 4 × 4096 cells per
/// frame, and every slice was exactly one 64×64 chunk (`job.Cursor + maxCells` with
/// maxCells = 4096). A teleport or a burst of arrivals therefore put four whole chunks
/// (16 384 SetCell/EraseCell calls) into one frame — the dropped frame on a large drag.
/// New code takes a per-frame cell budget (`MaxCellsPerPaintFrame`) and the pure
/// `CellsToPaintInSlice` decides the slice size; the rest of the queue waits a frame.
///
/// 01-02: the streaming world layer owns only cells OUTSIDE the colony island. PaintCell
/// already had the gate, but MarkTerrainAt/IsMountainAt did not. The ring of the world
/// chunk just behind the edge marks the 1023|1024 seam, and IsMountainAt answered
/// "mountain" for island cell 1023 — so MapRenderer's island mountain and the world
/// layer's mountain were both drawn there ("a mountain behind the edge appears inside
/// the island"). The single predicate `IsWorldLayerCell` asks WorldModel.IsInsideIsland.
///
/// WHY SOURCE CHECKS FOR THE BUDGET: the real price of SetCell is a TileMapLayer
/// quadrant invalidation + mesh rebuild in the render server, which is empty in
/// --headless; the same reason as tests/TestMapRendererBudget.cs. So the test pins the
/// root decision numerically and its wiring by source. For the island gate there is a
/// real behavioural check: a mountain chunk is injected into the private chunk cache
/// and IsMountainAt is called for the seam and negative cells (old code returns true
/// for 1023 / 500,1023 / etc., new code returns false).
///
/// Run: godot --headless --path . --script res://tests/run_streaming_paint_budget.gd
/// </summary>
public partial class TestStreamingPaintBudget : RefCounted
{
	private const string SourcePath = "res://src/ui/streaming/StreamingWorldView.cs";
	private static readonly int ChunkCells =
		WorldCoordinates.ChunkSize * WorldCoordinates.ChunkSize;

	public bool Run()
	{
		GD.Print("==================================================");
		GD.Print("[PAINT-BUDGET] StreamingWorldView: per-frame cell budget + island gate");
		GD.Print("==================================================");
		try
		{
			string source = Godot.FileAccess.GetFileAsString(SourcePath);
			if (string.IsNullOrEmpty(source))
			{
				GD.PrintErr($"[PAINT-BUDGET] FAIL: cannot read {SourcePath}");
				return false;
			}

			bool ok = true;
			ok &= TestFrameBudgetNumeric(source);
			ok &= TestFrameBudgetWiring(source);
			ok &= TestIslandGateSource(source);
			ok &= TestIslandGateBehavior();
			ok &= TestSeamSymmetry();

			GD.Print(ok ? "[PAINT-BUDGET] PASS" : "[PAINT-BUDGET] FAIL");
			return ok;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[PAINT-BUDGET] FAIL: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
			return false;
		}
	}

	// ---------------------------------------------------------------- 01-01 numeric

	private static bool TestFrameBudgetNumeric(string source)
	{
		GD.Print("[PAINT-BUDGET] 1) one frame paints at most N cells even for a fully dirty chunk");

		MethodInfo decide = typeof(StreamingWorldView).GetMethod(
			"CellsToPaintInSlice", BindingFlags.Public | BindingFlags.Static);
		if (decide == null)
		{
			GD.PrintErr("[PAINT-BUDGET] FAIL: StreamingWorldView has no public static " +
				"CellsToPaintInSlice(...). The per-frame decision is not extracted, so the " +
				"budget is still chunk-shaped (01-01).");
			return false;
		}
		if (decide.ReturnType != typeof(int) || !HasFiveIntParameters(decide))
		{
			GD.PrintErr("[PAINT-BUDGET] FAIL: CellsToPaintInSlice signature changed; expected " +
				"static int (int frameCellBudget, int paintedThisFrame, int cursor, " +
				"int chunkCellCount, int maxSliceCells).");
			return false;
		}

		int configured;
		if (!TryReadFrameBudgetDefault(out configured))
		{
			GD.PrintErr("[PAINT-BUDGET] FAIL: StreamingWorldView has no MaxCellsPerPaintFrame " +
				"export — there is no per-frame cell budget (01-01).");
			return false;
		}
		int cap = ConstInt(source, "MaxPaintCellsPerFrameCap");
		if (configured <= 0)
		{
			GD.PrintErr("[PAINT-BUDGET] FAIL: MaxCellsPerPaintFrame is missing or not a " +
				$"positive integer (got {configured}) — there is no cell budget per frame.");
			return false;
		}
		if (cap <= 0)
		{
			GD.PrintErr($"[PAINT-BUDGET] FAIL: MaxPaintCellsPerFrameCap is missing (got {cap}).");
			return false;
		}
		int budget = Math.Min(configured, cap);
		if (budget >= ChunkCells)
		{
			GD.PrintErr($"[PAINT-BUDGET] FAIL: effective budget {budget} >= one chunk " +
				$"({ChunkCells}) — a fully dirty chunk can still be painted in one frame.");
			return false;
		}

		(int Budget, int Painted, int Cursor, int Total, int Slice, int Expected)[] cases =
		{
			(budget, 0, 0, ChunkCells, ChunkCells, budget),
			(budget, 0, 0, ChunkCells, 256, Math.Min(budget, 256)),
			(budget, budget - 24, budget - 24, ChunkCells, ChunkCells, 24),
			(budget, budget, budget, ChunkCells, ChunkCells, 0),
			(budget, 0, ChunkCells - 6, ChunkCells, ChunkCells, 6),
			(budget, 0, ChunkCells, ChunkCells, ChunkCells, 0),
			(0, 0, 0, ChunkCells, ChunkCells, 0),
			(-1, 0, 0, ChunkCells, ChunkCells, 0),
			(budget, 0, -1, ChunkCells, ChunkCells, 0),
			(budget, 0, 0, ChunkCells, 0, 0),
			(ChunkCells, 0, 0, ChunkCells, ChunkCells, ChunkCells),
		};

		bool ok = true;
		foreach ((int caseBudget, int painted, int cursor, int total, int slice, int expected) in cases)
		{
			int got = (int)decide.Invoke(null, new object[] { caseBudget, painted, cursor, total, slice });
			if (got == expected)
				continue;
			GD.PrintErr($"[PAINT-BUDGET] FAIL: CellsToPaintInSlice(budget={caseBudget}, " +
				$"painted={painted}, cursor={cursor}, total={total}, slice={slice}) = {got}, " +
				$"expected {expected}.");
			ok = false;
		}

		// One frame of a fully dirty chunk must be exactly the budget, not the chunk.
		int firstSlice = (int)decide.Invoke(null,
			new object[] { budget, 0, 0, ChunkCells, ChunkCells });
		if (firstSlice != budget)
		{
			GD.PrintErr($"[PAINT-BUDGET] FAIL: a fully dirty chunk takes {firstSlice} cells in " +
				$"one frame, expected the budget {budget}.");
			ok = false;
		}

		// Simulate frames until the 4096-cell chunk is done: every cell exactly once,
		// no frame over budget, and the chunk takes several frames (that is the fix).
		int frame = 0, cursorAfterFrames = 0, totalPainted = 0;
		while (cursorAfterFrames < ChunkCells && frame <= ChunkCells)
		{
			int paintedThisFrame = 0;
			while (paintedThisFrame < budget && cursorAfterFrames < ChunkCells)
			{
				int take = (int)decide.Invoke(null, new object[]
				{
					budget, paintedThisFrame, cursorAfterFrames, ChunkCells, ChunkCells
				});
				if (take <= 0)
					break;
				paintedThisFrame += take;
				cursorAfterFrames += take;
			}
			if (paintedThisFrame <= 0)
			{
				GD.PrintErr("[PAINT-BUDGET] FAIL: the paint loop stalled — the budget function " +
					"returned 0 with unspent budget, the chunk would never finish.");
				return false;
			}
			if (paintedThisFrame > budget)
			{
				GD.PrintErr($"[PAINT-BUDGET] FAIL: a frame painted {paintedThisFrame} cells, " +
					$"over the budget {budget}.");
				return false;
			}
			totalPainted += paintedThisFrame;
			frame++;
		}

		int expectedFrames = (ChunkCells + budget - 1) / budget;
		if (cursorAfterFrames != ChunkCells || totalPainted != ChunkCells)
		{
			GD.PrintErr($"[PAINT-BUDGET] FAIL: simulated {ChunkCells}-cell chunk painted " +
				$"{totalPainted} cells (cursor {cursorAfterFrames}) — cells are skipped or doubled.");
			ok = false;
		}
		if (frame != expectedFrames)
		{
			GD.PrintErr($"[PAINT-BUDGET] FAIL: fully dirty chunk took {frame} frames, expected " +
				$"{expectedFrames} with budget {budget}.");
			ok = false;
		}

		if (ok)
			GD.Print($"[PAINT-BUDGET] OK: budget {budget} cells/frame; one 64×64 chunk needs " +
				$"{expectedFrames} frames, every cell exactly once.");
		return ok;
	}

	private static bool HasFiveIntParameters(MethodInfo method)
	{
		ParameterInfo[] parameters = method.GetParameters();
		if (parameters.Length != 5)
			return false;
		foreach (ParameterInfo parameter in parameters)
			if (parameter.ParameterType != typeof(int))
				return false;
		return true;
	}

	// ---------------------------------------------------------------- 01-01 wiring

	private static bool TestFrameBudgetWiring(string source)
	{
		GD.Print("[PAINT-BUDGET] 1b) the cell budget is wired into ProcessPaintSlices");

		string process = ExtractMember(source, "private void ProcessPaintSlices(");
		if (process == null)
		{
			GD.PrintErr("[PAINT-BUDGET] FAIL: ProcessPaintSlices not found in StreamingWorldView.");
			return false;
		}

		bool ok = true;
		ok &= Require(process, "MaxCellsPerPaintFrame",
			"ProcessPaintSlices does not read the per-frame cell budget MaxCellsPerPaintFrame.");
		ok &= Require(process, "MaxPaintCellsPerFrameCap",
			"ProcessPaintSlices does not apply the hard ceiling MaxPaintCellsPerFrameCap.");
		ok &= Require(process, "CellsToPaintInSlice(",
			"ProcessPaintSlices does not call the pure CellsToPaintInSlice decision.");
		ok &= Require(process, "paintedThisFrame += take",
			"ProcessPaintSlices does not accumulate painted cells per frame.");
		ok &= Require(process, "paintedThisFrame < frameCellBudget",
			"the paint loop is not bounded by the per-frame cell budget.");

		if (process.Contains("job.Cursor + maxCells", StringComparison.Ordinal))
		{
			GD.PrintErr("[PAINT-BUDGET] FAIL: a paint slice still ends at `job.Cursor + maxCells` " +
				"(whole chunk per slice) — 01-01 is back.");
			ok = false;
		}

		string decision = ExtractMember(source, "public static int CellsToPaintInSlice(");
		if (decision == null)
		{
			GD.PrintErr("[PAINT-BUDGET] FAIL: CellsToPaintInSlice body not found.");
			return false;
		}
		if (Regex.IsMatch(decision, @"\bnew\s"))
		{
			GD.PrintErr("[PAINT-BUDGET] FAIL: CellsToPaintInSlice allocates (new) in the frame path.");
			ok = false;
		}
		if (ContainsAny(decision, ".Select(", ".Where(", ".ToArray(", ".ToList(", "Stopwatch"))
		{
			GD.PrintErr("[PAINT-BUDGET] FAIL: CellsToPaintInSlice uses LINQ/clock in the hot path; " +
				"the decision must be a pure arithmetic function.");
			ok = false;
		}
		if (!decision.Contains("Math.Min", StringComparison.Ordinal))
		{
			GD.PrintErr("[PAINT-BUDGET] FAIL: CellsToPaintInSlice does not clamp to the minimum of " +
				"the remaining budget, the slice cap and the chunk end.");
			ok = false;
		}

		if (ok)
			GD.Print("[PAINT-BUDGET] OK: ProcessPaintSlices spends at most MaxCellsPerPaintFrame " +
				"cells per frame through the pure decision.");
		return ok;
	}

	// ---------------------------------------------------------------- 01-02 source

	private static bool TestIslandGateSource(string source)
	{
		GD.Print("[PAINT-BUDGET] 2) island cells never get a world-layer tile (single predicate)");

		string predicate = ExtractMember(source, "public static bool IsWorldLayerCell(");
		if (predicate == null)
		{
			GD.PrintErr("[PAINT-BUDGET] FAIL: StreamingWorldView has no public static " +
				"IsWorldLayerCell(long,long). The island gate has no single predicate, so a " +
				"world-layer mountain can be drawn inside the island (01-02).");
			return false;
		}
		bool ok = true;
		if (!predicate.Contains("WorldModel.IsInsideIsland(", StringComparison.Ordinal))
		{
			GD.PrintErr("[PAINT-BUDGET] FAIL: IsWorldLayerCell does not ask WorldModel.IsInsideIsland " +
				"— the island boundary must come from the project door.");
			ok = false;
		}
		if (predicate.Contains("IslandTiles", StringComparison.Ordinal)
			|| Regex.IsMatch(predicate, @"\b1024\b")
			|| predicate.Contains(">>", StringComparison.Ordinal)
			|| predicate.Contains("ChunkForTile", StringComparison.Ordinal))
		{
			GD.PrintErr("[PAINT-BUDGET] FAIL: IsWorldLayerCell contains its own boundary/chunk " +
				"formula instead of the WorldModel.IsInsideIsland door.");
			ok = false;
		}

		string[] gatedMethods =
		{
			"private void PaintCell(",
			"private void MarkTerrainAt(",
			"private bool IsMountainAt(",
		};
		foreach (string signature in gatedMethods)
		{
			string body = ExtractMember(source, signature);
			if (body == null)
			{
				GD.PrintErr($"[PAINT-BUDGET] FAIL: {signature} not found.");
				ok = false;
				continue;
			}
			if (!body.Contains("IsWorldLayerCell(", StringComparison.Ordinal))
			{
				GD.PrintErr($"[PAINT-BUDGET] FAIL: {signature} does not use IsWorldLayerCell — " +
					"a world-layer mountain/ground tile can be painted inside the island (01-02).");
				ok = false;
				continue;
			}
			if (signature.StartsWith("private void MarkTerrainAt(", StringComparison.Ordinal))
			{
				int gate = body.IndexOf("IsWorldLayerCell(", StringComparison.Ordinal);
				int mark = body.IndexOf("MarkDirty", StringComparison.Ordinal);
				if (mark >= 0 && gate > mark)
				{
					GD.PrintErr("[PAINT-BUDGET] FAIL: MarkTerrainAt marks a cell dirty before the " +
						"island gate — MapRenderer's cell can still be duplicated.");
					ok = false;
				}
			}
		}

		string mountain = ExtractMember(source, "private bool IsMountainAt(");
		if (mountain != null)
		{
			if (!mountain.Contains("WorldCoordinates.ChunkForTile(", StringComparison.Ordinal))
			{
				GD.PrintErr("[PAINT-BUDGET] FAIL: IsMountainAt does not use WorldCoordinates.ChunkForTile " +
					"(FloorDiv) — negative cells would land in the wrong chunk.");
				ok = false;
			}
			if (mountain.Contains(">>", StringComparison.Ordinal)
				|| mountain.Contains("/ WorldCoordinates.ChunkSize", StringComparison.Ordinal))
			{
				GD.PrintErr("[PAINT-BUDGET] FAIL: IsMountainAt divides chunks by hand instead of " +
					"WorldCoordinates.ChunkForTile (breaks on negatives).");
				ok = false;
			}
		}

		if (ok)
			GD.Print("[PAINT-BUDGET] OK: PaintCell/MarkTerrainAt/IsMountainAt all go through " +
				"IsWorldLayerCell → WorldModel.IsInsideIsland.");
		return ok;
	}

	// ---------------------------------------------------------------- 01-02 behaviour

	private static bool TestIslandGateBehavior()
	{
		GD.Print("[PAINT-BUDGET] 2b) IsMountainAt: mountain outside the island, nothing inside");

		MethodInfo isMountain = typeof(StreamingWorldView).GetMethod("IsMountainAt",
			BindingFlags.Instance | BindingFlags.NonPublic);
		if (isMountain == null)
		{
			GD.PrintErr("[PAINT-BUDGET] FAIL: private IsMountainAt not found — cannot verify the " +
				"mountain layer gate.");
			return false;
		}
		FieldInfo chunkDataField = typeof(StreamingWorldView).GetField("_chunkData",
			BindingFlags.Instance | BindingFlags.NonPublic);
		if (chunkDataField == null)
		{
			GD.PrintErr("[PAINT-BUDGET] FAIL: _chunkData not found — cannot inject a resident chunk.");
			return false;
		}

		var view = new StreamingWorldView();
		try
		{
			if (!(chunkDataField.GetValue(view) is Dictionary<ChunkKey, WorldChunk> chunks))
			{
				GD.PrintErr("[PAINT-BUDGET] FAIL: _chunkData is not a " +
					"Dictionary<ChunkKey, WorldChunk>.");
				return false;
			}

			(long X, long Y, bool Expected, string Why)[] cases =
			{
				(1023, 500, false, "last island column"),
				(500, 1023, false, "last island row"),
				(0, 500, false, "first island column"),
				(500, 0, false, "first island row"),
				(1023, 1023, false, "island corner"),
				(1024, 500, true, "first world column behind the edge"),
				(500, 1024, true, "first world row behind the edge"),
				(1024, 1024, true, "diagonal behind the island corner"),
				(-1, 500, true, "negative column"),
				(500, -1, true, "negative row"),
				(-1024, -1024, true, "far negative quadrant"),
			};

			bool ok = true;
			foreach ((long x, long y, bool expected, string why) in cases)
			{
				EnsureMountainChunk(chunks, x, y);
				EnsureMountainChunk(chunks, y, x);
				bool got = (bool)isMountain.Invoke(view, new object[] { x, y });
				if (got != expected)
				{
					GD.PrintErr($"[PAINT-BUDGET] FAIL: IsMountainAt({x},{y}) = {got}, " +
						$"expected {expected} ({why}). Inside the island MapRenderer owns the " +
						"mountain; outside the world layer must still see it.");
					ok = false;
				}

				bool swapped = (bool)isMountain.Invoke(view, new object[] { y, x });
				if (swapped != got)
				{
					GD.PrintErr($"[PAINT-BUDGET] FAIL: IsMountainAt is not symmetric: " +
						$"({x},{y})={got}, ({y},{x})={swapped}.");
					ok = false;
				}
			}

			if (ok)
				GD.Print("[PAINT-BUDGET] OK: 1023 → false, 1024 → true, negatives → true, both axes.");
			return ok;
		}
		finally
		{
			view.Free();
		}
	}

	private static void EnsureMountainChunk(Dictionary<ChunkKey, WorldChunk> chunks, long x, long y)
	{
		ChunkKey key = WorldCoordinates.ChunkForTile(x, y);
		if (chunks.ContainsKey(key))
			return;
		var cells = new GeneratedCell[ChunkCells];
		for (int i = 0; i < cells.Length; i++)
			cells[i] = new GeneratedCell(BaseTerrainKind.Mountain, 0, 0, 0, 0, 0);
		chunks[key] = new WorldChunk(key, cells);
	}

	// ---------------------------------------------------------------- 01-02 seam

	private static bool TestSeamSymmetry()
	{
		GD.Print("[PAINT-BUDGET] 3) seam 1020..1028 and negative cells: predicate matches the door");

		MethodInfo predicate = typeof(StreamingWorldView).GetMethod("IsWorldLayerCell",
			BindingFlags.Public | BindingFlags.Static);
		if (predicate == null)
		{
			GD.PrintErr("[PAINT-BUDGET] FAIL: IsWorldLayerCell not found.");
			return false;
		}
		if (predicate.ReturnType != typeof(bool) || predicate.GetParameters().Length != 2)
		{
			GD.PrintErr("[PAINT-BUDGET] FAIL: IsWorldLayerCell must be static bool (long,long).");
			return false;
		}

		long[] seam = { 1020, 1021, 1022, 1023, 1024, 1025, 1026, 1027, 1028 };
		long[] probes = { -1024, -65, -64, -1, 0, 500, 1023, 1024, 2048 };
		bool ok = true;
		foreach (long edge in seam)
		{
			foreach (long probe in probes)
			{
				ok &= CheckPredicate(predicate, edge, probe);
				ok &= CheckPredicate(predicate, probe, edge);
			}
		}
		foreach (long negative in new long[] { -1024, -65, -64, -1 })
		{
			foreach (long edge in seam)
				ok &= CheckPredicate(predicate, negative, edge);
		}

		// Chunk keys on negatives go through FloorDiv: -1 and -64 are in chunk -1,
		// -65 is in chunk -2. The gate must not change with the chunk (a shared seam).
		if (WorldCoordinates.ChunkForTile(-1, -1) != new ChunkKey(-1, -1)
			|| WorldCoordinates.ChunkForTile(-64, 0) != new ChunkKey(-1, 0)
			|| WorldCoordinates.ChunkForTile(-65, 5) != new ChunkKey(-2, 0))
		{
			GD.PrintErr("[PAINT-BUDGET] FAIL: WorldCoordinates.ChunkForTile does not use FloorDiv " +
				"on negative cells — the seam test cannot trust chunk routing.");
			ok = false;
		}

		if (ok)
			GD.Print("[PAINT-BUDGET] OK: 1020..1028 both axes, negatives, symmetric, matches " +
				"WorldModel.IsInsideIsland.");
		return ok;
	}

	private static bool CheckPredicate(MethodInfo predicate, long x, long y)
	{
		bool got = (bool)predicate.Invoke(null, new object[] { x, y });
		bool expected = !WorldModel.IsInsideIsland(x, y);
		if (got == expected)
			return true;
		GD.PrintErr($"[PAINT-BUDGET] FAIL: IsWorldLayerCell({x},{y}) = {got}, expected " +
			$"{expected} (!WorldModel.IsInsideIsland). The world layer would paint a MapRenderer cell.");
		return false;
	}

	// ---------------------------------------------------------------- helpers

	private static bool TryReadFrameBudgetDefault(out int value)
	{
		value = -1;
		FieldInfo field = typeof(StreamingWorldView).GetField("MaxCellsPerPaintFrame",
			BindingFlags.Public | BindingFlags.Instance);
		if (field == null)
			return false;
		StreamingWorldView probe = null;
		try
		{
			probe = new StreamingWorldView();
			value = (int)field.GetValue(probe);
			return true;
		}
		finally
		{
			probe?.Free();
		}
	}

	private static int ConstInt(string source, string name)
	{
		Match match = Regex.Match(source, @"const\s+int\s+" + name + @"\s*=\s*([0-9]+)");
		return match.Success
			? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)
			: -1;
	}

	private static bool Require(string body, string needle, string message)
	{
		if (body.Contains(needle, StringComparison.Ordinal))
			return true;
		GD.PrintErr($"[PAINT-BUDGET] FAIL: {message}");
		return false;
	}

	private static bool ContainsAny(string body, params string[] needles)
	{
		foreach (string needle in needles)
			if (body.Contains(needle, StringComparison.Ordinal))
				return true;
		return false;
	}

	/// <summary>
	/// Member body by signature: braced method or expression-bodied member (`=> ...;`).
	/// Comments and string literals are skipped so braces inside them do not confuse the
	/// matcher (same technique as TestMapRendererBudget).
	/// </summary>
	private static string ExtractMember(string source, string signature)
	{
		int start = source.IndexOf(signature, StringComparison.Ordinal);
		if (start < 0)
			return null;

		int arrow = source.IndexOf("=>", start, StringComparison.Ordinal);
		int open = source.IndexOf('{', start);
		if (arrow >= 0 && (open < 0 || arrow < open))
		{
			int semicolon = source.IndexOf(';', arrow);
			return semicolon < 0 ? null : source.Substring(start, semicolon - start + 1);
		}
		if (open < 0)
			return null;

		int depth = 0;
		for (int i = open; i < source.Length; i++)
		{
			char c = source[i];
			if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
			{
				int newline = source.IndexOf('\n', i);
				if (newline < 0)
					return null;
				i = newline;
				continue;
			}
			if (c == '"' || c == '\'')
			{
				int end = SkipQuoted(source, i, c);
				if (end < 0)
					return null;
				i = end;
				continue;
			}
			if (c == '{')
			{
				depth++;
			}
			else if (c == '}')
			{
				depth--;
				if (depth == 0)
					return source.Substring(start, i - start + 1);
			}
		}
		return null;
	}

	private static int SkipQuoted(string source, int start, char quote)
	{
		for (int i = start + 1; i < source.Length; i++)
		{
			if (source[i] == '\\')
			{
				i++;
				continue;
			}
			if (source[i] == quote)
				return i;
			if (source[i] == '\n')
				return -1;
		}
		return -1;
	}
}