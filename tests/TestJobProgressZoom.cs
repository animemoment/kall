using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Godot;
using Game.Simulation;
using Game.UI;

namespace Game.Tests;

/// <summary>
/// 02-02 (P2): work-progress sprites disappear after zooming the camera out and never
/// come back after zooming in.
///
/// Root cause: `FlushWorkProgress` called `WorkProgressTracker.DrainDirty` every frame,
/// before checking `_workProgressLayer.Visible`. `DrainDirty` is destructive (it dequeues
/// cells), `Report` queues a cell only on a STAGE CHANGE, and the old hidden branch
/// applied only stage &lt; 0 (erase) and returned, dropping positive stages for good.
/// Zoom-in had nothing left to repaint, so the indicator stayed missing until the work
/// advanced to the next stage or a new job started.
///
/// Fix: the drain is gated by the pure `MapRenderer.ShouldDrainWorkProgress(visible)`
/// rule; while the macro LOD hides the layer the updates stay in the tracker queue and
/// are replayed on the first visible frame (budgeted by the existing
/// `PendingFlushBudgetMs`). Visibility is recomputed from live zoom every frame, so
/// there is no one-way "hidden" flag/cache.
///
/// Checks:
///  1) numeric model over the real `WorkProgressTracker` queue + pure render decisions:
///     after "zoom out -> zoom in" the displayed atlas of every cell equals the latest
///     reported stage. The same sequence modelled with the OLD hidden-drain behaviour
///     must lose those updates (otherwise the test catches nothing).
///  2) source text: visibility comes from live zoom, the destructive drain sits behind
///     the pure gate, the old hidden-drop branch is absent, no persistent progress/LOD
///     bool flag exists, and the drain stays inside `PendingFlushBudgetMs`.
///
/// Run: godot --headless --path . --script res://tests/run_job_progress_zoom.gd
/// </summary>
public partial class TestJobProgressZoom : RefCounted
{
    private const string SourcePath = "res://src/ui/renderers/MapRenderer.cs";

    /// <summary>Chunk used by DrainWorkProgressBudgeted (WorkProgressDrainChunk = 64).</summary>
    private const int DrainChunk = 64;

    /// <summary>Below the 0.22 macro-LOD threshold: the progress layer is hidden.</summary>
    private const float ZoomedOut = 0.2f;

    /// <summary>Above the threshold: the progress layer is visible again.</summary>
    private const float ZoomedIn = 0.5f;

    /// <summary>Cells that already show progress before the camera zooms out.</summary>
    private static readonly (int X, int Y, int Stage)[] InitialStages =
    {
        (10, 10, 0),
        (11, 12, 1),
        (14, 10, 2),
        (15, 12, 3),
    };

    /// <summary>Stage changes that arrive while the camera is zoomed out.</summary>
    private static readonly (int X, int Y, int Stage)[] HiddenStageChanges =
    {
        (10, 10, 2),
        (11, 12, 3),
        (14, 10, 4),
        (15, 12, 5),
    };

    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print("[JOB-PROGRESS-ZOOM] 02-02: progress survives zoom-out -> zoom-in");
        GD.Print("==================================================");

        string src = Godot.FileAccess.GetFileAsString(SourcePath);
        if (string.IsNullOrEmpty(src))
        {
            GD.PrintErr($"[JOB-PROGRESS-ZOOM] FAIL: cannot read {SourcePath}");
            return false;
        }

        int failed = 0;
        if (!CheckRoundTripKeepsProgress()) failed++;
        if (!CheckNoOneWayHiddenFlag(src)) failed++;
        if (!CheckBudgetedDrain(src)) failed++;

        if (failed != 0)
        {
            GD.PrintErr($"[JOB-PROGRESS-ZOOM] FAIL: {failed} check(s) failed");
            return false;
        }

        GD.Print("[JOB-PROGRESS-ZOOM] PASS");
        return true;
    }

    /// <summary>
    /// Numeric model: real tracker queue, real pure render decisions. Visible frames
    /// replay the deferred updates; the legacy model (drain while hidden, keep only
    /// erases) must NOT match, proving this sequence catches the old bug.
    /// </summary>
    private static bool CheckRoundTripKeepsProgress()
    {
        bool ok = true;

        if (!MapRenderer.IsWorkProgressVisibleAtZoom(1f))
        {
            GD.PrintErr("[JOB-PROGRESS-ZOOM] FAIL: layer must be visible at zoom 1.0");
            ok = false;
        }
        if (MapRenderer.IsWorkProgressVisibleAtZoom(ZoomedOut))
        {
            GD.PrintErr($"[JOB-PROGRESS-ZOOM] FAIL: layer must be hidden at zoom {ZoomedOut}");
            ok = false;
        }
        if (!MapRenderer.IsWorkProgressVisibleAtZoom(ZoomedIn))
        {
            GD.PrintErr($"[JOB-PROGRESS-ZOOM] FAIL: layer must be visible again at zoom {ZoomedIn}");
            ok = false;
        }
        if (MapRenderer.ShouldDrainWorkProgress(false))
        {
            GD.PrintErr("[JOB-PROGRESS-ZOOM] FAIL: hidden layer must NOT consume the destructive tracker queue");
            ok = false;
        }
        if (!MapRenderer.ShouldDrainWorkProgress(true))
        {
            GD.PrintErr("[JOB-PROGRESS-ZOOM] FAIL: visible layer must drain the tracker queue");
            ok = false;
        }

        var expected = new Dictionary<(int X, int Y), Vector2I>();
        foreach (var (x, y, stage) in HiddenStageChanges)
        {
            var (ax, ay) = WorkProgressTracker.AtlasForStage(stage);
            expected[(x, y)] = new Vector2I(ax, ay);
        }

        Dictionary<(int X, int Y), Vector2I> fixedModel = Simulate(legacyDropsWhileHidden: false);
        Dictionary<(int X, int Y), Vector2I> legacyModel = Simulate(legacyDropsWhileHidden: true);

        // 1) The fixed model: same cells as before zoom-out, atlas = latest stage.
        if (fixedModel.Count != InitialStages.Length)
        {
            GD.PrintErr($"[JOB-PROGRESS-ZOOM] FAIL: displayed cells {fixedModel.Count}, expected {InitialStages.Length}");
            ok = false;
        }
        foreach (var (x, y, _) in InitialStages)
        {
            var key = (x, y);
            if (!fixedModel.TryGetValue(key, out var atlas))
            {
                GD.PrintErr($"[JOB-PROGRESS-ZOOM] FAIL: cell {x},{y} lost its progress after zoom-in");
                ok = false;
                continue;
            }
            if (!atlas.Equals(expected[key]))
            {
                GD.PrintErr($"[JOB-PROGRESS-ZOOM] FAIL: cell {x},{y} shows {atlas}, expected {expected[key]}");
                ok = false;
            }
        }
        if (ok)
            GD.Print($"[JOB-PROGRESS-ZOOM] OK: after zoom-out -> zoom-in all {fixedModel.Count} cells show the latest stage");

        // 2) The legacy model must lose the hidden stage changes, otherwise this test
        // would pass on the old code too and would catch nothing.
        int stale = 0;
        foreach (var kv in expected)
        {
            if (!legacyModel.TryGetValue(kv.Key, out var atlas) || !atlas.Equals(kv.Value))
                stale++;
        }
        if (stale == 0)
        {
            GD.PrintErr("[JOB-PROGRESS-ZOOM] FAIL: legacy hidden-drain model keeps every update — model catches nothing");
            ok = false;
        }
        else
        {
            GD.Print($"[JOB-PROGRESS-ZOOM] OK: legacy hidden-drain model loses {stale}/{expected.Count} cells (old code was red here)");
        }

        return ok;
    }

    /// <summary>
    /// Replays the renderer's frame sequence against the real tracker queue. The only
    /// difference between models is what happens while the layer is hidden:
    ///  * fixed: `ShouldDrainWorkProgress(false)` is false -> queue is left intact;
    ///  * legacy: the queue is drained destructively and only erases are applied.
    /// </summary>
    private static Dictionary<(int X, int Y), Vector2I> Simulate(bool legacyDropsWhileHidden)
    {
        WorkProgressTracker.Instance.ClearAll();
        var displayed = new Dictionary<(int X, int Y), Vector2I>();
        var drain = new List<(int X, int Y, int Stage)>(DrainChunk);

        // Frame group 1: camera close, layer visible, jobs report their first stages.
        foreach (var (x, y, stage) in InitialStages)
            WorkProgressTracker.Instance.Report(x, y, stage);
        DrainAndApply(displayed, drain);

        // Frames 2..N: camera zoomed out, layer hidden, but jobs keep reporting.
        bool visible = MapRenderer.IsWorkProgressVisibleAtZoom(ZoomedOut);
        foreach (var (x, y, stage) in HiddenStageChanges)
            WorkProgressTracker.Instance.Report(x, y, stage);

        if (legacyDropsWhileHidden)
        {
            // Old FlushWorkProgress: drained before the visibility check, applied only
            // stage < 0 and dropped every positive stage.
            WorkProgressTracker.Instance.DrainDirty(drain, 256);
            foreach (var (x, y, stage) in drain)
            {
                if (stage < 0)
                    displayed.Remove((x, y));
            }
        }
        else if (MapRenderer.ShouldDrainWorkProgress(visible))
        {
            // Must not happen: the fixed renderer defers while hidden.
            DrainAndApply(displayed, drain);
        }

        // Frame group N+1: zoomed back in. Deferred updates are replayed.
        visible = MapRenderer.IsWorkProgressVisibleAtZoom(ZoomedIn);
        if (MapRenderer.ShouldDrainWorkProgress(visible))
            DrainAndApply(displayed, drain);

        WorkProgressTracker.Instance.ClearAll();
        return displayed;
    }

    /// <summary>Drain the tracker in the same chunks as production and apply the pure render mapping.</summary>
    private static void DrainAndApply(
        Dictionary<(int X, int Y), Vector2I> displayed,
        List<(int X, int Y, int Stage)> drain)
    {
        while (true)
        {
            WorkProgressTracker.Instance.DrainDirty(drain, DrainChunk);
            if (drain.Count == 0)
                break;
            foreach (var (x, y, stage) in drain)
            {
                if (MapRenderer.TryResolveWorkProgressTile(stage, out var atlas))
                    displayed[(x, y)] = atlas;
                else
                    displayed.Remove((x, y));
            }
        }
    }

    /// <summary>
    /// Textual check (2): there must be no one-way flag that is set on zoom-out and
    /// never cleared, and the destructive drain must be gated by the pure rule.
    /// </summary>
    private static bool CheckNoOneWayHiddenFlag(string src)
    {
        string process = ExtractMethod(src, "public override void _Process(double delta)");
        string flush = ExtractMethod(src, "private void FlushWorkProgress(double delta)");
        string drain = ExtractMethod(src, "private void DrainWorkProgressBudgeted()");
        if (process == null || flush == null || drain == null)
        {
            GD.PrintErr("[JOB-PROGRESS-ZOOM] FAIL: cannot extract _Process/FlushWorkProgress/DrainWorkProgressBudgeted " +
                        $"(process={process != null}, flush={flush != null}, drain={drain != null})");
            return false;
        }

        bool ok = true;

        ok &= Check(Regex.IsMatch(process, @"bool\s+showProgress\s*=\s*IsWorkProgressVisibleAtZoom\s*\(\s*zoom\s*\)\s*;"),
            "visibility is recomputed from live zoom every frame (no cached LOD flag)");
        ok &= Check(Regex.IsMatch(process, @"_workProgressLayer\.Visible\s*=\s*showProgress\s*;"),
            "layer visibility is driven by that rule in both directions");
        ok &= Check(!process.Contains("_workProgressLayer.Visible = false", StringComparison.Ordinal),
            "no one-way `_workProgressLayer.Visible = false` assignment");
        ok &= Check(!Regex.IsMatch(process, @"_(?:workProgress|progress)\w*\s*=\s*(?:true|false)\s*;"),
            "no persistent progress bool is toggled in _Process");
        ok &= Check(process.Contains("FlushWorkProgress(delta);", StringComparison.Ordinal),
            "flush runs every frame; visibility only gates the queue, not the replay");

        int gate = flush.IndexOf("ShouldDrainWorkProgress(", StringComparison.Ordinal);
        int drainCall = flush.IndexOf("DrainWorkProgressBudgeted(", StringComparison.Ordinal);
        ok &= Check(gate >= 0 && drainCall > gate,
            "destructive drain is gated by ShouldDrainWorkProgress, not by a cached flag");
        ok &= Check(!flush.Contains("if (!_workProgressLayer.Visible)", StringComparison.Ordinal),
            "old hidden branch that dropped positive stages is gone");
        ok &= Check(!flush.Contains("DrainDirty(", StringComparison.Ordinal),
            "FlushWorkProgress no longer consumes the tracker queue directly");
        ok &= Check(Regex.Matches(drain, @"DrainDirty\(").Count == 1,
            "exactly one tracker drain site");

        int sweep = gate >= 0 ? flush.IndexOf("_progressSweepTimer +=", gate, StringComparison.Ordinal) : -1;
        int earlyReturn = gate >= 0 ? flush.IndexOf("return;", gate, StringComparison.Ordinal) : -1;
        ok &= Check(sweep > gate && (earlyReturn < 0 || earlyReturn > sweep),
            "stale sweep is not skipped while the layer is hidden");

        ok &= Check(!Regex.IsMatch(src, @"\bbool\s+_(?:workProgress|progress)\w*\s*(?:=|;)"),
            "no persistent bool progress field (one-way cache) exists");

        string visibility = ExtractMethod(src, "public static bool IsWorkProgressVisibleAtZoom(float zoom)");
        string decision = ExtractMethod(src, "public static bool ShouldDrainWorkProgress(bool layerVisible)");
        ok &= Check(visibility != null && visibility.Contains("return zoom >= WorkProgressZoomThreshold;", StringComparison.Ordinal),
            "IsWorkProgressVisibleAtZoom is a pure rule around WorkProgressZoomThreshold");
        ok &= Check(decision != null && decision.Contains("return layerVisible;", StringComparison.Ordinal),
            "ShouldDrainWorkProgress only forwards visibility (symmetric hide/show)");

        return ok;
    }

    /// <summary>
    /// The replay must not add a second unbudgeted path: it uses the existing
    /// `PendingFlushBudgetMs` ticks/mask, drains in chunks, allocates nothing and never
    /// walks all jobs.
    /// </summary>
    private static bool CheckBudgetedDrain(string src)
    {
        string drain = ExtractMethod(src, "private void DrainWorkProgressBudgeted()");
        if (drain == null)
        {
            GD.PrintErr("[JOB-PROGRESS-ZOOM] FAIL: DrainWorkProgressBudgeted not found");
            return false;
        }

        bool ok = true;
        ok &= Check(drain.Contains("PendingFlushBudgetTicks", StringComparison.Ordinal),
            "drain uses the existing PendingFlushBudgetMs budget");
        ok &= Check(Regex.Matches(drain, @"Stopwatch\.GetTimestamp\(\)").Count == 2,
            "start and budget check: exactly two clock reads, amortized over the chunk");
        ok &= Check(drain.Contains("WorkProgressDrainChunk", StringComparison.Ordinal),
            "drain is chunked, so the budget covers the whole frame");
        ok &= Check(drain.Contains("_progressDrain", StringComparison.Ordinal)
                  && !Regex.IsMatch(drain, @"\bnew\s+(?:List|Dictionary|HashSet|ConcurrentQueue)\b"),
            "reuses the preallocated _progressDrain buffer, no per-frame collection allocation");
        ok &= Check(drain.Contains("SetCell(", StringComparison.Ordinal),
            "visible frames repaint the deferred stages (SetCell)");
        ok &= Check(!drain.Contains(".Select(", StringComparison.Ordinal)
                  && !drain.Contains(".Where(", StringComparison.Ordinal)
                  && !drain.Contains(".ToList(", StringComparison.Ordinal)
                  && !drain.Contains(".ToArray(", StringComparison.Ordinal),
            "no LINQ in the hot path");
        return ok;
    }

    private static bool Check(bool condition, string what)
    {
        if (condition)
        {
            GD.Print($"[JOB-PROGRESS-ZOOM] OK: {what}");
            return true;
        }
        GD.PrintErr($"[JOB-PROGRESS-ZOOM] FAIL: {what}");
        return false;
    }

    /// <summary>
    /// Method body by exact signature: from the first `{` to the matching `}` while
    /// skipping line comments and quoted strings (MapRenderer has braces inside
    /// interpolated strings).
    /// </summary>
    private static string ExtractMethod(string src, string signature)
    {
        int sig = src.IndexOf(signature, StringComparison.Ordinal);
        if (sig < 0) return null;

        int open = src.IndexOf('{', sig);
        if (open < 0) return null;

        int depth = 0;
        for (int i = open; i < src.Length; i++)
        {
            char c = src[i];
            if (c == '/' && i + 1 < src.Length && src[i + 1] == '/')
            {
                int nl = src.IndexOf('\n', i);
                if (nl < 0) return null;
                i = nl;
                continue;
            }
            if (c == '"' || c == '\'')
            {
                int end = SkipQuoted(src, i, c);
                if (end < 0) return null;
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
                    return src.Substring(open, i - open + 1);
            }
        }
        return null;
    }

    /// <summary>Index of the closing quote (escape-aware), -1 if it is not closed.</summary>
    private static int SkipQuoted(string src, int start, char quote)
    {
        for (int i = start + 1; i < src.Length; i++)
        {
            if (src[i] == '\\') { i++; continue; }
            if (src[i] == quote) return i;
            if (src[i] == '\n') return -1;
        }
        return -1;
    }
}