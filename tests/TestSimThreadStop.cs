using System;
using System.Reflection;
using System.Text.RegularExpressions;
using Godot;
using Game.Simulation;

namespace Game.Tests;

/// <summary>
/// 04-06: stopping the simulation thread left the runtime state behind.
///
/// Three faults, one node:
///  1) a clean <c>Stop</c> dropped neither the position queue nor <c>_pool</c>/<c>_ctx</c>, so a
///     stale snapshot could be delivered to the next colony and whole SoA arrays stayed alive;
///  2) on a TIMEOUT the old loop kept running while <c>Start</c> reassigned <c>_pool</c>/<c>_ctx</c>
///     (new pool + old context => IndexOutOfRange/NRE every 20 ms until the generation changed);
///  3) <c>Start</c> bumped the generation only after it had already replaced both fields.
///
/// Checks 1-2 run the real <see cref="AgentSimulationThread"/> (never started, so no thread is
/// created). Checks 3-4 are structural: the ordering guarantee cannot be produced without
/// actually leaving a loop hanging for the timeout, which headless cannot reproduce reliably.
///
/// Run: godot --headless --path . --script res://tests/run_sim_thread_stop.gd
/// </summary>
public partial class TestSimThreadStop : RefCounted
{
    private const string SourcePath = "res://src/simulation/AgentSimulationThread.cs";
    private const string Tag = "[SIM-STOP]";

    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print($"{Tag} остановка потока отпускает состояние (04-06)");
        GD.Print("==================================================");

        bool ok = true;
        var thread = new AgentSimulationThread();

        // 1. A queue snapshot from the previous colony must not survive a clean stop.
        thread.PositionQueue.Enqueue(new[] { new System.Numerics.Vector2(1, 2), new System.Numerics.Vector2(3, 4) });
        thread.PositionQueue.Enqueue(new[] { new System.Numerics.Vector2(5, 6) });
        ok &= Check("очередь позиций наполнена до остановки", thread.PositionQueue.Count == 2);

        bool clean = thread.Stop(50);
        ok &= Check("остановка не запущенного потока — чистая", clean);
        ok &= Check("очередь позиций пуста после остановки", thread.PositionQueue.IsEmpty);
        ok &= Check("пул отпущен", GetPrivate(thread, "_pool") == null);
        ok &= Check("контекст отпущен", GetPrivate(thread, "_ctx") == null);
        ok &= Check("задача отпущена", GetPrivate(thread, "_simulationTask") == null);

        // 2. Repeated stop must stay harmless (Dispose calls Stop again).
        ok &= Check("повторная остановка не падает", thread.Stop(50));

        // 3. Ordering: generation invalidated before the fields are replaced.
        string src = Godot.FileAccess.GetFileAsString(SourcePath);
        if (string.IsNullOrEmpty(src))
        {
            GD.PrintErr($"{Tag} FAIL: не прочитал {SourcePath}");
            return false;
        }

        int startIndex = src.IndexOf("public void Start(", StringComparison.Ordinal);
        int poolAssign = src.IndexOf("_pool = new AgentDataPool", Math.Max(startIndex, 0), StringComparison.Ordinal);
        int genBump = src.IndexOf("int generation = ++_loopGeneration;", Math.Max(startIndex, 0), StringComparison.Ordinal);
        ok &= Check("Start найден", startIndex >= 0);
        ok &= Check("поколение поднимается ДО создания пула", genBump >= 0 && poolAssign > genBump);
        ok &= Check("поколение поднимается ровно один раз на Start",
            Regex.Matches(src, @"\+\+_loopGeneration").Count == 2); // Start + Stop(timeout path)

        // 4. Stop must invalidate on the timeout path and reset on the clean one.
        int stopIndex = src.IndexOf("public bool Stop(", StringComparison.Ordinal);
        string stopBody = stopIndex >= 0 ? src.Substring(stopIndex) : string.Empty;
        ok &= Check("ветка таймаута поднимает поколение",
            Regex.IsMatch(stopBody, @"if \(!cleanStop\)\s*\n\s*_loopGeneration\+\+;"));
        ok &= Check("чистая остановка чистит очередь и поля",
            Regex.IsMatch(stopBody, @"while \(PositionQueue\.TryDequeue\(out _\)\)") &&
            Regex.IsMatch(stopBody, @"_pool = null;") &&
            Regex.IsMatch(stopBody, @"_ctx = null;"));

        GD.Print(ok ? $"{Tag} PASS" : $"{Tag} FAIL");
        return ok;
    }

    private static object GetPrivate(AgentSimulationThread thread, string field) =>
        typeof(AgentSimulationThread)
            .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(thread);

    private bool Check(string what, bool condition)
    {
        if (condition)
        {
            GD.Print($"{Tag} OK: {what}");
            return true;
        }
        GD.PrintErr($"{Tag} FAIL: {what}");
        return false;
    }
}