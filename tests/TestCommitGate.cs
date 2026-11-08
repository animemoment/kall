using System;
using System.Text.RegularExpressions;
using Godot;

namespace Game.Tests;

/// <summary>
/// 04-01: агент навсегда застревал, если его работу сняли из мира (снесли цель,
/// цель стала недостижимой) — он оставался в MovingToSource, а Commit не вызывался.
///
/// Причина: Phase3b (commit) гейтилась условием `totalJobs > 0 && busyWorkers > 0`.
/// Когда работу убрали, `totalJobs` становился 0, и коммит пропускался, хотя агенты
/// работу ещё держат (`busyWorkers > 0`). В игре это выглядело так: колония замирает,
/// часть агентов стоит с задачей, время идёт, ничего не происходит — и так до перезапуска.
///
/// Проверяем по тексту симуляции: гейт коммита не должен смотреть на количество работ
/// в индексе. Это тот случай, когда «лишний» фаст-путь и был багом.
///
/// Запуск: godot --headless --path . --script res://tests/run_commit_gate.gd
/// </summary>
public partial class TestCommitGate : RefCounted
{
    private const string SimPath = "res://src/simulation/AgentSimulationThread.cs";

    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print("[COMMIT-GATE] Коммит не зависит от числа работ (04-01)");
        GD.Print("==================================================");

        if (!Godot.FileAccess.FileExists(SimPath))
        {
            GD.PrintErr($"[COMMIT-GATE] нет {SimPath}");
            return false;
        }

        string src = Godot.FileAccess.GetFileAsString(SimPath);
        bool ok = true;
        ok &= GateHasNoTotalJobs(src);
        ok &= CommitStillRuns(src);
        return ok;
    }

    /// <summary>Гейт обязан быть `if (busyWorkers > 0)` — без totalJobs.</summary>
    private bool GateHasNoTotalJobs(string src)
    {
        Match gate = Regex.Match(src, @"if\s*\(\s*busyWorkers\s*>\s*0\s*\)");
        if (!gate.Success)
        {
            GD.PrintErr("[COMMIT-GATE] FAIL: не найден гейт `if (busyWorkers > 0)` в Phase3b");
            return false;
        }

        // Ловим возврат старого условия в любом виде: totalJobs рядом с busyWorkers в if.
        Match bad = Regex.Match(src, @"if\s*\([^)]*totalJobs[^)]*busyWorkers[^)]*\)"
                                    + @"|if\s*\([^)]*busyWorkers[^)]*totalJobs[^)]*\)");
        if (bad.Success)
        {
            GD.PrintErr($"[COMMIT-GATE] FAIL: гейт снова смотрит на totalJobs: {bad.Value}");
            return false;
        }

        // totalJobs вообще не должен больше фигурировать как условие коммита.
        if (Regex.IsMatch(src, @"totalJobs\s*>\s*0"))
        {
            GD.PrintErr("[COMMIT-GATE] FAIL: в файле осталось условие `totalJobs > 0`");
            return false;
        }

        GD.Print("[COMMIT-GATE] OK: коммит гейтится только busyWorkers > 0");
        return true;
    }

    /// <summary>Сам коммит на месте: гейт не должен был «починиться» удалением работы.</summary>
    private bool CommitStillRuns(string src)
    {
        if (!Regex.IsMatch(src, @"Simulation\.Phase3b_Commit"))
        {
            GD.PrintErr("[COMMIT-GATE] FAIL: из Phase3b пропал GameProfiler-скоуп коммита");
            return false;
        }
        if (!Regex.IsMatch(src, @"CommitSingleAgent\("))
        {
            GD.PrintErr("[COMMIT-GATE] FAIL: CommitSingleAgent больше не вызывается");
            return false;
        }
        GD.Print("[COMMIT-GATE] OK: коммит по-прежнему вызывается для всех агентов");
        return true;
    }
}