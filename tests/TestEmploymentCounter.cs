using System;
using System.Text.RegularExpressions;
using Godot;
using Game.Core;
using Game.Simulation;

namespace Game.Tests;

/// <summary>
/// 28-01 (P0): HUD показывал «занято» как «население − бездельники».
///
/// Проверяем две вещи по отдельности:
///  (а) арифметику занятости: занят = взял задачу и идёт/делает/несёт; Idle и Evacuating
///      работой НЕ считаются (по старой формуле они попадали в «занятые»);
///  (б) что HUD действительно берёт числа из симуляции, а старый расчёт остался только
///      аварийной ветвью для сцен без потока. Это ловится по тексту исходников — headless
///      иначе не увидеть, а именно это и было сломано.
///
/// На старом коде тест не собирается (нет AgentStats) и не находит SetSimulation — то есть
/// краснеет по обеим причинам.
///
/// Запуск: godot --headless --path . --script res://tests/run_employment_counter.gd
/// </summary>
public partial class TestEmploymentCounter : RefCounted
{
    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print("[EMPLOY] Честный счётчик занятости (28-01)");
        GD.Print("==================================================");

        bool ok = true;
        ok &= CountsOnlyRealWork();
        ok &= CountEmploymentSplitsEvenly();
        ok &= SurvivesBadInput();
        ok &= HudUsesSimulationSnapshot();
        ok &= SimPublishesSnapshot();
        return ok;
    }

    /// <summary>(а) Idle и Evacuating — не работа; остальные три состояния — работа.</summary>
    private bool CountsOnlyRealWork()
    {
        if (AgentStats.IsEmployed(AgentState.Idle))
        {
            GD.PrintErr("[EMPLOY] (а) FAIL: Idle считается занятым");
            return false;
        }
        if (AgentStats.IsEmployed(AgentState.Evacuating))
        {
            GD.PrintErr("[EMPLOY] (а) FAIL: Evacuating считается занятым — это агент, снявшийся с работы");
            return false;
        }
        if (!AgentStats.IsEmployed(AgentState.MovingToSource)
            || !AgentStats.IsEmployed(AgentState.Working)
            || !AgentStats.IsEmployed(AgentState.MovingToTarget))
        {
            GD.PrintErr("[EMPLOY] (а) FAIL: одно из рабочих состояний не считается занятым");
            return false;
        }

        // 10 агентов: 4 бездельника, 2 идут к цели, 1 работает, 3 несут, + эвакуация
        AgentState[] states =
        {
            AgentState.Idle, AgentState.Idle, AgentState.Idle, AgentState.Idle,
            AgentState.MovingToSource, AgentState.MovingToSource,
            AgentState.Working,
            AgentState.MovingToTarget, AgentState.MovingToTarget, AgentState.MovingToTarget,
            AgentState.Evacuating,
        };
        int employed = AgentStats.CountEmployed(states, states.Length);
        if (employed != 6)
        {
            GD.PrintErr($"[EMPLOY] (а) FAIL: занятых {employed}, ожидалось 6 из 11");
            return false;
        }

        GD.Print("[EMPLOY] (а) OK: 6 занятых из 11, Idle и Evacuating не в счёте");
        return true;
    }

    /// <summary>(б) занято + без работы = все агенты, без отрицательных значений.</summary>
    private bool CountEmploymentSplitsEvenly()
    {
        AgentState[] states = new AgentState[8];
        for (int i = 0; i < states.Length; i++) states[i] = AgentState.Idle;
        states[0] = AgentState.Working;
        states[1] = AgentState.MovingToSource;
        states[2] = AgentState.MovingToTarget;

        AgentStats.CountEmployment(states, states.Length, out int employed, out int unemployed);
        if (employed != 3 || unemployed != 5)
        {
            GD.PrintErr($"[EMPLOY] (б) FAIL: занято {employed}, без работы {unemployed}, ожидалось 3/5");
            return false;
        }

        GD.Print("[EMPLOY] (б) OK: 3/5 из 8, сумма сходится");
        return true;
    }

    /// <summary>(в) мусорный вход не роняет главный поток HUD.</summary>
    private bool SurvivesBadInput()
    {
        if (AgentStats.CountEmployed(null, 10) != 0)
        {
            GD.PrintErr("[EMPLOY] (в) FAIL: null-массив дал не ноль");
            return false;
        }
        AgentState[] states = { AgentState.Working };
        if (AgentStats.CountEmployed(states, 999) != 1)
        {
            GD.PrintErr("[EMPLOY] (в) FAIL: count больше длины массива не клампится");
            return false;
        }
        if (AgentStats.CountEmployed(states, -5) != 0)
        {
            GD.PrintErr("[EMPLOY] (в) FAIL: отрицательный count дал не ноль");
            return false;
        }

        GD.Print("[EMPLOY] (в) OK: null / count>length / count<0 не роняют счёт");
        return true;
    }

    /// <summary>(г) HUD читает снимок симуляции, а старый расчёт — только аварийная ветвь.</summary>
    private bool HudUsesSimulationSnapshot()
    {
        const string hudPath = "res://src/ui/controllers/HUDController.cs";
        if (!Godot.FileAccess.FileExists(hudPath))
        {
            GD.PrintErr($"[EMPLOY] (г) нет {hudPath}");
            return false;
        }
        string hud = Godot.FileAccess.GetFileAsString(hudPath);

        int simUse = hud.IndexOf("_simulation.EmployedCount", StringComparison.Ordinal);
        int poolFallback = hud.IndexOf("IdleWorkers.TotalIdleCount", StringComparison.Ordinal);
        if (simUse < 0)
        {
            GD.PrintErr("[EMPLOY] (г) FAIL: HUD не использует EmployedCount из симуляции");
            return false;
        }
        if (poolFallback >= 0 && poolFallback < simUse)
        {
            GD.PrintErr("[EMPLOY] (г) FAIL: старый расчёт из пула бездельников идёт раньше снимка симуляции");
            return false;
        }

        const string mainPath = "res://scenes/main/Main.cs";
        string main = Godot.FileAccess.GetFileAsString(mainPath);
        if (!Regex.IsMatch(main, @"hud\.SetSimulation\(\s*_agentThread\s*\)"))
        {
            GD.PrintErr("[EMPLOY] (г) FAIL: Main.cs не подключает поток симуляции к HUD");
            return false;
        }

        GD.Print("[EMPLOY] (г) OK: HUD берёт занятость из снимка симуляции, пул — только фолбэк");
        return true;
    }

    /// <summary>(д) поток публикует снимок через Volatile, а не каждый тик.</summary>
    private bool SimPublishesSnapshot()
    {
        const string simPath = "res://src/simulation/AgentSimulationThread.cs";
        string sim = Godot.FileAccess.GetFileAsString(simPath);
        if (!Regex.IsMatch(sim, @"Volatile\.Write\(ref _statEmployed"))
        {
            GD.PrintErr("[EMPLOY] (д) FAIL: поток не публикует _statEmployed");
            return false;
        }
        if (!Regex.IsMatch(sim, @"StatsPeriodTicks"))
        {
            GD.PrintErr("[EMPLOY] (д) FAIL: публикация не ограничена по тикам (была бы работа каждый тик)");
            return false;
        }
        if (!Regex.IsMatch(sim, @"AgentStats\.CountEmployed\(\s*_pool\.States"))
        {
            GD.PrintErr("[EMPLOY] (д) FAIL: счёт идёт не через AgentStats.CountEmployed");
            return false;
        }

        GD.Print("[EMPLOY] (д) OK: снимок публикуется раз в StatsPeriodTicks тиков");
        return true;
    }
}