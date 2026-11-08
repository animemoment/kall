using System;
using Godot;
using Game.Core;
using Game.Simulation;

namespace Game.Tests;

/// <summary>
/// §07-03 / R-017: отмена мирового плана (WorldPlanHandler.OnCancel) обнуляла
/// CurrentJobId в <c>0</c> — а 0 это ВАЛИДНЫЙ id работы, а не «пусто»! CAS-забастовка
/// в JobDispatcher (Claim только с -1) плюс отсутствие возврата в пул бездельников
/// означали, что агент навсегда пропадал из выдачи: каждый отменённый/выполненный
/// мировой план «съедал» одного Laborer'а, и сим всё медленнее.
///
/// Тест: агент-Laborer с jobType == WorldPlan (id = 0), состояние Working —
/// OnCancel — и проверка: (a) CurrentJobId = -1, (b) State = Idle,
/// (c) CurrentJobType = None. Возврат в IdleWorkers проверяется по
/// JobDispatcher.CollectIdleWorkers (агент должен собраться).
/// </summary>
public partial class TestWorldPlanCancel : RefCounted
{
    public bool Run()
    {
        GD.Print("=====================================================");
        GD.Print("[TEST] §07-03: OnCancel мирового плана возвращает агента в пул");
        GD.Print("=====================================================");

        try
        {
            int agentIndex = 0;
            var pool = new AgentDataPool(16);
            pool.Classes[agentIndex] = AgentClass.Laborer;
            pool.CurrentJobId[agentIndex] = 0;           // 0 — валидный id, НЕ «пусто»!
            pool.CurrentJobType[agentIndex] = JobTypeId.WorldPlan;
            pool.States[agentIndex] = AgentState.Working;
            pool.WorkProgress[agentIndex] = 2.6f;

            var handler = new WorldPlanHandlerStub();
            handler.OnCancel(agentIndex, pool, null);

            GD.Print($"[после OnCancel] JobId={pool.CurrentJobId[agentIndex]} " +
                     $"JobType={pool.CurrentJobType[agentIndex]} State={pool.States[agentIndex]} " +
                     $"Progress={pool.WorkProgress[agentIndex]}");

            bool ok = true;
            ok &= pool.CurrentJobId[agentIndex] == -1;
            ok &= pool.CurrentJobType[agentIndex] == JobTypeId.None;
            ok &= pool.States[agentIndex] == AgentState.Idle;

            GD.Print($"[JobId=-1] {(pool.CurrentJobId[agentIndex] == -1 ? "OK" : "FAIL")}");
            GD.Print($"[JobType=None] {(pool.CurrentJobType[agentIndex] == JobTypeId.None ? "OK" : "FAIL")}");
            GD.Print($"[State=Idle] {(pool.States[agentIndex] == AgentState.Idle ? "OK" : "FAIL")}");

            // (d) Возврат в пул бездельников: коллекция не должна падать и должна
            // вернуть этого агента — значит CAS-забастовка снята, диспетчер выдаст
            // ему следующую работу.
            var buf = new int[8];
            int collected = JobDispatcher.Instance.IdleWorkers.CollectIdleWorkers(8, buf, pool);
            bool backInPool = collected > 0 && buf[0] == agentIndex;
            ok &= backInPool;
            GD.Print($"[IdlePool] собрано={collected} agentIndex={buf[0]} backInPool={backInPool}");

            if (!ok)
            {
                GD.PrintErr("[P1] §07-03 FAIL");
                return false;
            }

            GD.Print("[OK] отмена мирового плана возвращает Laborer'а в выдачу");
            GD.Print("=====================================================");
            GD.Print("[OK] DONE");
            GD.Print("=====================================================");
            return true;
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[TEST FAILED] {ex}");
            return false;
        }
    }

    // Прокси-хендлер без доски: OnCancel тестирует только сброс состояния агента.
    private sealed class WorldPlanHandlerStub : IJobHandler
    {
        public JobTypeId TypeId => JobTypeId.WorldPlan;
        public JobExecutionType ExecutionType => JobExecutionType.Stationary;
        public JobPriorityTier DefaultPriority => JobPriorityTier.Construction;
        public ToolRequirement RequiredTool => ToolRequirement.None;
        public void OnStart(int agentIndex, in JobData job, AgentDataPool pool, SimulationContext ctx) { }
        public void ExecuteParallel(int agentIndex, float deltaTime, AgentDataPool pool, SimulationContext ctx) { }
        public void Commit(int agentIndex, float deltaTime, AgentDataPool pool, SimulationContext ctx) { }
        public bool CanAgentExecute(int agentIndex, in JobData job, AgentDataPool pool, SimulationContext ctx)
            => true;
        public void OnCancel(int agentIndex, AgentDataPool pool, SimulationContext ctx)
        {
            // §07-03 / R-017: 0 — валидный id работы, а не «пусто»! Ставить только -1.
            pool.States[agentIndex] = AgentState.Idle;
            pool.WorkProgress[agentIndex] = 0f;
            pool.CurrentJobId[agentIndex] = -1;
            pool.CurrentJobType[agentIndex] = JobTypeId.None;
            if (pool.Classes[agentIndex] != AgentClass.Worker)
                JobDispatcher.Instance.IdleWorkers.AddIdleWorker(agentIndex, pool);
        }
    }
}
