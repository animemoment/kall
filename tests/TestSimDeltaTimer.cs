using System;
using Godot;
using Game.Simulation;

namespace Game.Tests;

/// <summary>
/// 04-02 / 04-03: таймер должен кормиться той же дельтой, по которой считается время.
///
/// Как было в AgentSimulationThread.SimulationLoop:
///   • `_jobAuditRealTimer += realDelta` стоял ВНУТРИ `for (step &lt; steps)`, а
///     `realDelta` — реальное время всего прохода. Таймер «раз в 30 с» получал
///     steps × realDelta за проход: на 1x (~0.3 в секунду) аудит шёл раз в 90–250 с,
///     на 25x (~10 в секунду) — раз в 3 с.
///   • `TickEventThrottle(currentStepDt)` стоял ПОСЛЕ цикла (раз за проход), но
///     получал дельту одного суб-степа, тогда как проход продвигал мир на
///     steps × currentStepDt. Порог «200 мс» превращался в 0.2/шаги игросекунды.
///
/// Корень один: у времени и у дельты таймера были разные источники. Лечится не
/// множителем, а тем, что план прохода считается одной чистой функцией
/// (SimStepPlanner.Advance), и каждый таймер берёт дельту своего счёта:
/// реальный — plan.Elapsed (один раз за проход), игровой — plan.GameDelta.
///
/// Тест — численная модель этого плана + проверка по тексту, что петля его
/// действительно использует.
///
/// Запуск: godot --headless --path . --script res://tests/run_sim_delta_timer.gd
/// </summary>
public partial class TestSimDeltaTimer : RefCounted
{
    /// <summary>16.667 мс — шаг из постановки задачи (фиксированный шаг 1/60).</summary>
    private const float Step60 = 1f / 60f;

    /// <summary>Штатный шаг 1x в этом проекте: BaseFixedDeltaTime = 0.05 (20 Гц).</summary>
    private const float Step1x = SimStepPlanner.BaseStepSec;

    private const float Eps = 1e-5f;

    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print("[SIM-DELTA] Таймер кормится той же дельтой, по которой идёт время (04-02/04-03)");
        GD.Print("==================================================");

        int failed = 0;
        if (!CheckRealClockIsNotQuantized()) failed++;
        if (!CheckConvergesAt33Ms()) failed++;
        if (!CheckVariableStepSums()) failed++;
        if (!CheckSpeedProfile()) failed++;
        if (!CheckCallerWiring()) failed++;

        if (failed != 0)
        {
            GD.PrintErr($"[SIM-DELTA] FAIL: провалено проверок — {failed}");
            return false;
        }

        GD.Print("[SIM-DELTA] OK: реальный таймер = сумма реальных дельт, игровой = шаги × шаг");
        GD.Print("==================================================");
        return true;
    }

    /// <summary>
    /// 10 тиков по 16 мс: реальный таймер накапливает 160 мс, а не 166.7 мс
    /// (166.7 — это 10 × 1/60, то есть если бы время брали из фиксированного шага).
    /// Плюс тот же прогон на штатном шаге 1x (50 мс): там видно, во сколько раз
    /// ошибалась прежняя схема «delta на каждый суб-степ».
    /// </summary>
    private static bool CheckRealClockIsNotQuantized()
    {
        // (а) модель из постановки: шаг 1/60.
        float acc = 0f;
        float sumElapsed = 0f;
        float sumGame = 0f;
        float oldStyle = 0f;
        int sumSteps = 0;
        const int passes = 10;
        const float tick = 0.016f;
        for (int i = 0; i < passes; i++)
        {
            SimStepPlan p = SimStepPlanner.Advance(acc, tick, 1f, Step60);
            acc = p.Accumulator;
            sumElapsed += p.Elapsed;
            sumGame += p.GameDelta;
            sumSteps += p.Steps;
            // «как было»: таймер получал дельту в каждом исполненном суб-степе.
            oldStyle += p.Steps * tick;
        }

        if (Math.Abs(sumElapsed - 0.160f) > Eps)
        {
            GD.PrintErr($"[SIM-DELTA] FAIL: 10×16 мс накоплено {sumElapsed * 1000f:F3} мс, ожидалось 160.000");
            return false;
        }
        if (sumElapsed > 0.1655f)
        {
            GD.PrintErr($"[SIM-DELTA] FAIL: время посчитано из фиксированного шага ({sumElapsed * 1000f:F3} мс ≈ 10×1/60)");
            return false;
        }
        if (sumSteps != 9 || Math.Abs(sumGame - 9f * Step60) > Eps)
        {
            GD.PrintErr($"[SIM-DELTA] FAIL: шагов {sumSteps}, игровое время {sumGame * 1000f:F3} мс, ожидалось 9 шагов = 150.000 мс");
            return false;
        }
        GD.Print($"[SIM-DELTA] 10×16 мс: реальное {sumElapsed * 1000f:F2} мс, игровое {sumGame * 1000f:F2} мс ({sumSteps} шагов)");

        // (б) штатный режим 1x: проход ~16 мс, шаг 50 мс — большинство проходов без
        // суб-шагов. Именно здесь старая схема отставала в ~3 раза (0.3 в секунду).
        acc = 0f;
        sumElapsed = 0f;
        oldStyle = 0f;
        for (int i = 0; i < passes; i++)
        {
            SimStepPlan p = SimStepPlanner.Advance(acc, tick, 1f, Step1x);
            acc = p.Accumulator;
            sumElapsed += p.Elapsed;
            oldStyle += p.Steps * tick;
        }

        float newRate = sumElapsed / (passes * tick);
        float oldRate = oldStyle / (passes * tick);
        if (Math.Abs(newRate - 1f) > 1e-3f)
        {
            GD.PrintErr($"[SIM-DELTA] FAIL: реальный таймер идёт со скоростью {newRate:F3}× вместо 1×");
            return false;
        }
        if (oldRate >= 0.9f)
        {
            GD.PrintErr($"[SIM-DELTA] FAIL: старая схема не отстаёт ({oldRate:F2}×) — модель ничего не ловит");
            return false;
        }
        GD.Print($"[SIM-DELTA] 1x/шаг 50 мс: было {oldRate:F2}× реального времени, стало {newRate:F2}×");
        return true;
    }

    /// <summary>
    /// Тик 33 мс (два шага по 1/60 не влезают): накопленное игровое время сходится
    /// к сумме реальных дельт — расхождение не больше одного шага, и симуляция
    /// никогда не «выдумывает» время вперёд реального.
    /// </summary>
    private static bool CheckConvergesAt33Ms()
    {
        float acc = 0f;
        float sumElapsed = 0f;
        float sumGame = 0f;
        const int passes = 30;
        const float tick = 0.033f;
        for (int i = 0; i < passes; i++)
        {
            SimStepPlan p = SimStepPlanner.Advance(acc, tick, 1f, Step60);
            acc = p.Accumulator;
            sumElapsed += p.Elapsed;
            sumGame += p.GameDelta;
        }

        if (Math.Abs(sumElapsed - passes * tick) > 1e-4f)
        {
            GD.PrintErr($"[SIM-DELTA] FAIL: сумма реальных дельт {sumElapsed:F4} вместо {passes * tick:F4}");
            return false;
        }
        if (sumGame > sumElapsed + 1e-6f)
        {
            GD.PrintErr($"[SIM-DELTA] FAIL: игровое время {sumGame:F4} обогнало реальное {sumElapsed:F4}");
            return false;
        }
        float drift = sumElapsed - sumGame;
        if (drift > Step60 + Eps)
        {
            GD.PrintErr($"[SIM-DELTA] FAIL: отставание {drift:F4} больше шага {Step60:F4}");
            return false;
        }
        GD.Print($"[SIM-DELTA] 30×33 мс: реальное {sumElapsed:F4}, игровое {sumGame:F4}, отставание {drift:F4} < шага");
        return true;
    }

    /// <summary>
    /// Переменный шаг: игровая дельта каждого прохода ровно равна Steps × StepDelta
    /// (то же, что получает GameTimeSeconds), сумма реальных дельт — сумме входных
    /// дельт, а остаток аккумулятора объясняет всю разницу. Никакого «подкрученного
    /// множителя»: числа сходятся тождеством, а не подгонкой.
    /// </summary>
    private static bool CheckVariableStepSums()
    {
        float[] deltas = { 0.008f, 0.016f, 0.021f, 0.033f, 0.012f, 0.004f, 0.048f, 0.017f };
        float acc = 0f;
        float sumElapsed = 0f;
        float sumGame = 0f;
        float sumInput = 0f;
        int passes = 0;
        for (int round = 0; round < 5; round++)
        {
            for (int i = 0; i < deltas.Length; i++)
            {
                float input = deltas[i];
                SimStepPlan p = SimStepPlanner.Advance(acc, input, 1f, Step1x);
                if (p.GameDelta != p.Steps * p.StepDelta)
                {
                    GD.PrintErr($"[SIM-DELTA] FAIL: GameDelta {p.GameDelta} != Steps×StepDelta {p.Steps * p.StepDelta}");
                    return false;
                }
                acc = p.Accumulator;
                sumElapsed += p.Elapsed;
                sumGame += p.GameDelta;
                sumInput += input;
                passes++;
            }
        }

        if (Math.Abs(sumElapsed - sumInput) > 1e-5f)
        {
            GD.PrintErr($"[SIM-DELTA] FAIL: реальная сумма {sumElapsed:F6} != сумме дельт {sumInput:F6}");
            return false;
        }
        // Остаток аккумулятора — единственная разница между счетами.
        float identity = sumElapsed - sumGame - acc;
        if (Math.Abs(identity) > 1e-5f)
        {
            GD.PrintErr($"[SIM-DELTA] FAIL: не сходится тождество: elapsed−game−acc = {identity:E2}");
            return false;
        }
        GD.Print($"[SIM-DELTA] {passes} проходов с переменным шагом: реальное {sumElapsed:F4}, игровое {sumGame:F4}, остаток {acc:F4}");
        return true;
    }

    /// <summary>
    /// Профиль шага по скоростям остался прежним (0.05 / 0.2 / 0.5), потолок
    /// реальной дельты 0.25 с работает, на 100x шагов не больше двух.
    /// </summary>
    private static bool CheckSpeedProfile()
    {
        (float speed, float expect)[] table =
        {
            (1f, 0.05f), (5f, 0.05f), (25f, 0.20f), (100f, 0.50f),
        };
        for (int i = 0; i < table.Length; i++)
        {
            float got = SimStepPlanner.StepDeltaFor(table[i].speed);
            if (Math.Abs(got - table[i].expect) > 1e-6f)
            {
                GD.PrintErr($"[SIM-DELTA] FAIL: шаг на {table[i].speed}x = {got}, ожидалось {table[i].expect}");
                return false;
            }
        }

        // Кламп реального времени: 1 с простоя не должна превращаться в 1 с игрового.
        SimStepPlan clamp = SimStepPlanner.Advance(0f, 1.0f, 1f);
        if (Math.Abs(clamp.Elapsed - SimStepPlanner.MaxElapsedSec) > 1e-6f)
        {
            GD.PrintErr($"[SIM-DELTA] FAIL: кламп реальной дельты не работает ({clamp.Elapsed})");
            return false;
        }

        // 100x: амортизация — не больше двух шагов за проход.
        SimStepPlan fast = SimStepPlanner.Advance(0f, 0.016f, 100f);
        if (fast.Steps > 2 || fast.StepDelta != 0.5f)
        {
            GD.PrintErr($"[SIM-DELTA] FAIL: на 100x шагов {fast.Steps} при шаге {fast.StepDelta}, ожидалось ≤2 по 0.5");
            return false;
        }

        // 25x: длинный проход упирается в потолок 10 шагов и не теряет больше backlog-капа.
        SimStepPlan coarse = SimStepPlanner.Advance(0f, 0.100f, 25f);
        if (coarse.Steps != 10)
        {
            GD.PrintErr($"[SIM-DELTA] FAIL: на 25x шагов {coarse.Steps}, ожидалось 10 (потолок)");
            return false;
        }
        float backlog = coarse.Accumulator;
        if (backlog < 0f || backlog > coarse.StepDelta * 10f + 1e-4f)
        {
            GD.PrintErr($"[SIM-DELTA] FAIL: backlog {backlog} вне [0, 10 шагов]");
            return false;
        }
        GD.Print($"[SIM-DELTA] профиль шага и кламп на месте (100x: {fast.Steps}×{fast.StepDelta}, 25x backlog {backlog:F3})");
        return true;
    }

    /// <summary>
    /// Проверка по тексту: петля обязана брать план из SimStepPlanner и кормить
    /// таймеры его дельтами. Без этого тест на модель зелёный, а баг живой.
    /// </summary>
    private static bool CheckCallerWiring()
    {
        string root = ProjectSettings.GlobalizePath("res://");
        string path = System.IO.Path.Combine(root, "src", "simulation", "AgentSimulationThread.cs");
        if (!System.IO.File.Exists(path))
        {
            GD.PrintErr($"[SIM-DELTA] FAIL: не найден {path}");
            return false;
        }
        string src = System.IO.File.ReadAllText(path);

        (string fragment, bool mustExist, string why)[] checks =
        {
            ("SimStepPlanner.Advance(", true, "петля должна считать план прохода чистой функцией"),
            ("_jobAuditRealTimer += plan.Elapsed", true, "реальный таймер аудита кормится реальным временем прохода"),
            ("_jobAuditRealTimer += realDelta", false, "старая схема: delta на каждый суб-степ"),
            ("TickEventThrottle(plan.Elapsed)", true, "троттлинг склада кормится реальным временем прохода"),
            ("TickEventThrottle(currentStepDt)", false, "старая схема: одна дельта суб-степа на весь проход"),
            ("GetSimStepDelta", false, "шаг выбирается только в SimStepPlanner"),
        };
        for (int i = 0; i < checks.Length; i++)
        {
            bool contains = src.Contains(checks[i].fragment, StringComparison.Ordinal);
            if (contains != checks[i].mustExist)
            {
                GD.PrintErr($"[SIM-DELTA] FAIL: в AgentSimulationThread.cs {(contains ? "есть" : "нет")} " +
                            $"«{checks[i].fragment}» — {checks[i].why}");
                return false;
            }
        }
        GD.Print("[SIM-DELTA] петля использует SimStepPlanner и кормит таймеры его дельтами");
        return true;
    }
}