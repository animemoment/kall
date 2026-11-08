using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Game.Core;
using Game.Simulation;
using Game.Simulation.Jobs;

namespace Game.Tests;

/// <summary>
/// 06-01 (+06-04, +08-01/06-09): счётчики клеймов JobSpatialPyramid и удаление работы.
///
/// 06-01. Было: RemoveJob решал «работа была unclaimed?» обычным чтением
/// `_assignedWorkers &lt; _maxWorkers`, а клейм — независимым CAS на том же поле без лока.
/// Клейм, попавший в окно между чтением и декрементом удаления, декрементил
/// `_unclaimedCount` ВТОРОЙ раз (счётчик уходил в минус) и возвращал клейм на уже
/// удалённую работу (агент уходил в OnStart по id, которого нет в _posMap).
/// Стало: удаление «отравляет» счётчик до max одним Interlocked.Exchange и забирает
/// прежнее значение — переход unclaimed→claimed принадлежит ровно одному участнику;
/// клейм-путь после CAS перепроверяет _active и клейм на удалённую работу не отдаёт.
///
/// 06-04. Первый id, который выдаёт пирамида, — 0 (free-list). Сентинел `id > 0` теряет
/// такую работу; тест фиксирует контракт: id 0 регистрируется, находится, клеймится и
/// удаляется, а «нет работы» отличается по found, а не по `id > 0`.
///
/// 08-01 / 06-09. Индекс пирамиды обязан покрывать весь остров 1024x1024
/// (ChunkDim = 64, 4096 чанков): 32x32 = 512 тайлов схлопывал восток и юг в крайние
/// чанки. Тест-страж: (900,900) и (500,500) обязаны попадать в РАЗНЫЕ чанки.
///
/// Гонку нельзя поймать по времени детерминированно, поэтому основной сценарий 06-01
/// использует хук в хендлере (CanAgentExecute вызывается в клейм-пути ПОСЛЕ чтения
/// _active и ДО CAS): поток-клейм останавливается в окне, главный поток в этом окне
/// удаляет работу и только потом отпускает клейм. Это воспроизводит гонку на 100%
/// (на старом коде клейм возвращается, а счётчик уходит в -1). Дополнительно — чурн
/// remove+claim в 4 потока с проверкой инварианта: после слива счётчики сходятся в 0.
///
/// Запуск: godot --headless --path . --script res://tests/run_job_pyramid_claims.gd
/// </summary>
public partial class TestJobPyramidClaims : RefCounted
{
    private const int VictimX = 8;
    private const int VictimY = 8;   // чанк (0,0): 8 >> 4 == 0
    private const int MapSize = 64;
    private const int StormMs = 900;
    private const int StormClaimers = 3;

    private static readonly ManualResetEventSlim ClaimEntered = new ManualResetEventSlim(false);
    private static readonly ManualResetEventSlim AllowClaim = new ManualResetEventSlim(false);

    /// <summary>Хук: останавливать клейм работы (VictimX, VictimY) внутри окна гонки.</summary>
    private static volatile bool _blockVictim;

    private bool _ok = true;

    /// <summary>
    /// Хендлер-проба. CanAgentExecute зовётся из TryClaimInChunk между чтением _active
    /// кандидата и CAS на _assignedWorkers — идеальная точка, чтобы детерминированно
    /// вклинить удаление работы в окно гонки 06-01.
    /// </summary>
    private sealed class ProbeHandler : IJobHandler
    {
        public JobTypeId TypeId => JobTypeId.TreeChopping;
        public JobExecutionType ExecutionType => JobExecutionType.Stationary;
        public JobPriorityTier DefaultPriority => JobPriorityTier.TreeChopping;
        public ToolRequirement RequiredTool => ToolRequirement.None;

        public bool CanAgentExecute(int agentIndex, in JobData job, AgentDataPool pool, SimulationContext ctx)
        {
            if (_blockVictim && job.TargetX == VictimX && job.TargetY == VictimY)
            {
                ClaimEntered.Set();
                AllowClaim.Wait(5000);   // ждём, пока главный поток удалит работу
            }
            return true;
        }

        public void OnStart(int agentIndex, in JobData job, AgentDataPool pool, SimulationContext ctx) { }
        public void ExecuteParallel(int agentIndex, float deltaTime, AgentDataPool pool, SimulationContext ctx) { }
        public void Commit(int agentIndex, float deltaTime, AgentDataPool pool, SimulationContext ctx) { }
        public void OnCancel(int agentIndex, AgentDataPool pool, SimulationContext ctx) { }
    }

    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print("[CLAIMS] 06-01/06-04/08-01: клейм, удаление и счётчики пирамиды");
        GD.Print("==================================================");

        try
        {
            JobRegistry.Register(new ProbeHandler());

            SequentialInvariants();
            ChunkCoverage1024();
            RemovedJobClaimRace();
            ConcurrentChurn();

            if (_ok)
            {
                GD.Print("==================================================");
                GD.Print("[CLAIMS OK] инварианты клеймов и удаления соблюдены");
                GD.Print("==================================================");
            }
            else
            {
                GD.PrintErr("[CLAIMS FAIL] см. строки FAIL выше");
            }
            return _ok;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CLAIMS] ИСКЛЮЧЕНИЕ: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            return false;
        }
    }

    // ---------------- 06-04 + последовательные инварианты счётчиков ---------------- //

    private void SequentialInvariants()
    {
        var p = new JobSpatialPyramid();

        // 06-04: первая же работа получает id 0 — сентинел `id > 0` её теряет.
        int id = p.RegisterJob(MakeJob(VictimX, VictimY));
        Check("06-04: первая работа получает id 0", id == 0);
        Check("06-04: TotalCount == 1", p.TotalCount == 1);
        Check("06-04: UnclaimedCount == 1", p.UnclaimedCount == 1);
        Check("счётчики сходятся (1)", Aggregates(p, 1));

        int byPos = p.TryGetJobIdByPos(VictimX, VictimY, JobTypeId.TreeChopping, out bool found);
        // Контракт: 0 — валидный id, «нет работы» различается по found.
        Check("06-04: TryGetJobIdByPos отдаёт id 0 с found == true", found && byPos == 0);
        Check("06-04: TryGetJob(0) находит работу",
            p.TryGetJob(0, out JobData jd0) && jd0.Id == 0 && jd0.IsActive);

        Check("06-04: клейм работы с id 0", p.TryClaimJobById(0, out _));
        Check("после клейма UnclaimedCount == 0", p.UnclaimedCount == 0);
        Check("счётчики сходятся (0, после клейма)", Aggregates(p, 0));
        Check("повторный клейм насыщенной работы невозможен", !p.TryClaimJobById(0, out _));
        Check("отказ не увёл счётчик", p.UnclaimedCount == 0);

        p.ReleaseWorkerClaim(0);
        Check("после ReleaseWorkerClaim UnclaimedCount == 1", p.UnclaimedCount == 1);
        Check("счётчики сходятся (1, после release)", Aggregates(p, 1));

        Check("удаление unclaimed-работы", p.RemoveJob(0, out JobData removed) && removed.Id == 0);
        Check("после удаления TotalCount == 0", p.TotalCount == 0);
        Check("после удаления UnclaimedCount == 0 (не минус)", p.UnclaimedCount == 0);
        Check("счётчики сходятся (0, после удаления)", Aggregates(p, 0));
        Check("клейм удалённой работы невозможен", !p.TryClaimJobById(0, out _));
        Check("повторное удаление возвращает false", !p.RemoveJob(0, out _));
        Check("отказы не увели счётчик в минус", p.UnclaimedCount == 0);

        // Удаление ЗАНЯТОЙ работы не должно трогать unclaimed, а поздний release
        // (агент держал клейм удалённой работы) — не должен уводить его в минус.
        int busy = p.RegisterJob(MakeJob(VictimX + 1, VictimY + 1));
        Check("занятая работа заклеймлена", p.TryClaimJobById(busy, out _) && p.UnclaimedCount == 0);
        Check("удаление занятой работы", p.RemoveJob(busy, out _));
        Check("удаление занятой работы не трогает unclaimed", p.UnclaimedCount == 0);
        p.ReleaseWorkerClaim(busy);
        Check("поздний release по удалённой работе не ушёл в минус", p.UnclaimedCount == 0);
        Check("счётчики сходятся (0, занятая удалена)", Aggregates(p, 0));

        // Переиспользование слота free-list: единица считается на новую работу.
        int reused = p.RegisterJob(MakeJob(VictimX + 1, VictimY + 1));
        Check("слот переиспользован", reused == busy);
        Check("переиспользованный слот: UnclaimedCount == 1", p.UnclaimedCount == 1);
        Check("переиспользованный слот удаляется чисто",
            p.RemoveJob(reused, out _) && p.UnclaimedCount == 0 && Aggregates(p, 0));
    }

    // ---------------- 08-01 / 06-09: покрытие индекса 1024x1024 ---------------- //

    private void ChunkCoverage1024()
    {
        Check("08-01: ChunkDim == 64 (1024 тайла)", JobSpatialPyramid.ChunkDim == 64);
        Check("08-01: ChunkCount == 4096", JobSpatialPyramid.ChunkCount == 4096);
        Check("08-01: (900,900) → чанк (56,56)",
            JobSpatialPyramid.GetChunkIndexStatic(900, 900) == 56 * 64 + 56);
        Check("08-01: (900,900) и (500,500) — разные чанки",
            JobSpatialPyramid.GetChunkIndexStatic(900, 900) != JobSpatialPyramid.GetChunkIndexStatic(500, 500));
        Check("08-01: (512,512) → чанк (32,32), не крайний",
            JobSpatialPyramid.GetChunkIndexStatic(512, 512) == 32 * 64 + 32);
        Check("08-01: (1023,1023) → чанк 4095",
            JobSpatialPyramid.GetChunkIndexStatic(1023, 1023) == 4095);
        Check("08-01: отрицательные клетки → чанк 0 (индекс валиден)",
            JobSpatialPyramid.GetChunkIndexStatic(-1, -7) == 0);
        Check("08-01: клетки за кромкой → крайний чанк",
            JobSpatialPyramid.GetChunkIndexStatic(5000, 5000) == 4095);
    }

    // ---------------- 06-01: детерминированное окно «клейм ↔ удаление» ---------------- //

    private void RemovedJobClaimRace()
    {
        var p = new JobSpatialPyramid();
        int victimId = p.RegisterJob(MakeJob(VictimX, VictimY));
        Check("06-01: работа зарегистрирована (id 0)", victimId == 0 && p.UnclaimedCount == 1);

        var pool = NewPool();
        var ctx = NewContext();

        bool claimResult = false;
        JobData claimJob = default;
        Exception claimError = null;

        _blockVictim = true;
        ClaimEntered.Reset();
        AllowClaim.Reset();

        Task claimTask = Task.Run(() =>
        {
            try
            {
                claimResult = p.TryClaimJobForAgent(VictimX, VictimY, ToolRequirement.None,
                    pool, 0, ctx, false, false, out claimJob);
            }
            catch (Exception ex)
            {
                claimError = ex;
            }
        });

        // Ждём, пока клейм встанет в окно между чтением _active и CAS на _assignedWorkers.
        bool entered = ClaimEntered.Wait(5000);
        Check("06-01: клейм дошёл до окна гонки (CanAgentExecute)", entered);

        if (entered)
        {
            // Пока клейм «висит» в окне — удаляем работу (в игре: снесли цель/отменили).
            Check("06-01: удаление работы в окне клейма", p.RemoveJob(victimId, out _));
            Check("06-01: удаление сняло ровно одну единицу unclaimed", p.UnclaimedCount == 0);
            Check("06-01: счётчики сходятся после удаления", Aggregates(p, 0));
        }

        AllowClaim.Set();
        _blockVictim = false;
        claimTask.Wait(5000);

        if (claimError != null)
            GD.PrintErr($"[CLAIMS] клейм в потоке упал: {claimError.GetType().Name}: {claimError.Message}");
        Check("06-01: клейм в потоке без исключения", claimError == null);

        // Главные инварианты карточки.
        Check("06-01: клейм удалённой работы НЕ выдан", !claimResult);
        Check("06-01: UnclaimedCount не ушёл в минус", p.UnclaimedCount == 0);
        Check("06-01: счётчики сходятся (0, после гонки)", Aggregates(p, 0));
        Check("06-01: TotalCount == 0 после гонки", p.TotalCount == 0);
        if (claimResult)
            Check("06-01: выданный клейм указывает на живую работу", p.TryGetJob(claimJob.Id, out _));
    }

    // ---------------- 06-01: чурн remove+claim, инвариант счётчиков ---------------- //

    private void ConcurrentChurn()
    {
        // Точное межпоточное окно гонки ловится выше детерминированно; здесь проверяем
        // ИНВАРИАНТ на серии remove+claim: после слива счётчики обязаны сойтись в 0 и не
        // уйти в минус. На старом коде двойной декремент оставляет UnclaimedCount
        // отрицательным навсегда, поэтому финальные проверки красные.
        var p = new JobSpatialPyramid();
        var positions = new List<(int X, int Y)>();
        JobData victim = MakeJob(VictimX, VictimY);

        int victimId = p.RegisterJob(victim);
        positions.Add((VictimX, VictimY));

        // 24 приманки в том же чанке: скан чанка не вырожденный (окно скана длиннее).
        for (int i = 0; i < 24; i++)
        {
            int x = 1 + (i % 15);
            int y = 1 + (i / 15);
            p.RegisterJob(MakeJob(x, y));
            positions.Add((x, y));
        }

        Check("чурн: стартовых unclaimed == 25", p.UnclaimedCount == 25);

        var pool = NewPool();
        var ctx = NewContext();
        for (int a = 0; a < StormClaimers; a++)
        {
            pool.CurrentCellX[a] = VictimX;
            pool.CurrentCellY[a] = VictimY;
        }

        var errors = new ConcurrentQueue<string>();
        long claims = 0;
        long deadClaims = 0;
        int minUnclaimed = int.MaxValue;
        int stop = 0;

        Task[] tasks = new Task[StormClaimers + 1];

        // Роль 1: удаляет и тут же перерегистрирует работу (снос цели в игре).
        tasks[0] = Task.Run(() =>
        {
            try
            {
                int vid = victimId;
                while (Volatile.Read(ref stop) == 0)
                {
                    if (p.RemoveJob(vid, out _))
                        vid = p.RegisterJob(victim);
                }
            }
            catch (Exception ex)
            {
                errors.Enqueue("remover: " + ex.GetType().Name + ": " + ex.Message);
            }
        });

        // Роли 2..N: клейм + освобождение.
        for (int a = 0; a < StormClaimers; a++)
        {
            int agent = a;
            tasks[a + 1] = Task.Run(() =>
            {
                try
                {
                    while (Volatile.Read(ref stop) == 0)
                    {
                        if (!p.TryClaimJobForAgent(VictimX, VictimY, ToolRequirement.None,
                                pool, agent, ctx, false, false, out JobData job))
                            continue;

                        Interlocked.Increment(ref claims);
                        if (!p.TryGetJob(job.Id, out _))
                            Interlocked.Increment(ref deadClaims);   // диагностика, не ассерт

                        int u = p.UnclaimedCount;
                        if (u < minUnclaimed)
                            minUnclaimed = u;

                        p.ReleaseWorkerClaim(job.Id);
                    }
                }
                catch (Exception ex)
                {
                    errors.Enqueue("claimer: " + ex.GetType().Name + ": " + ex.Message);
                }
            });
        }

        Thread.Sleep(StormMs);
        Volatile.Write(ref stop, 1);
        Task.WaitAll(tasks);

        GD.Print($"[CLAIMS] чурн: claims={claims} dead_claim_ids={deadClaims} " +
                 $"min_unclaimed={minUnclaimed}");

        // Слив: снимаем работы и возможные «висящие» клеймы по диапазону id.
        p.RemoveBatchByPositions(positions, JobTypeId.TreeChopping);
        for (int id = 0; id < 256; id++)
            p.ReleaseWorkerClaim(id);

        foreach (string e in errors)
            GD.PrintErr($"[CLAIMS] поток: {e}");
        Check("чурн: исключений в потоках нет", errors.IsEmpty);
        Check("чурн: TotalCount == 0 после слива", p.TotalCount == 0);
        Check($"чурн: UnclaimedCount == 0 (не минус; есть {p.UnclaimedCount}, total={p.TotalCount})",
            p.UnclaimedCount == 0);
        Check("чурн: счётчики сошлись", Aggregates(p, 0));
        Check("чурн: клеймы отработали (иначе сценарий пустой)", claims > 0);
    }

    // ---------------- helpers ---------------- //

    private void Check(string what, bool condition)
    {
        if (condition)
        {
            GD.Print($"[CLAIMS] OK: {what}");
        }
        else
        {
            GD.PrintErr($"[CLAIMS] FAIL: {what}");
            _ok = false;
        }
    }

    /// <summary>Σ чанковых == глобальный, обе Cd-суммы нулевые (в этих сценариях кулдаунов нет).</summary>
    private static bool Aggregates(JobSpatialPyramid p, int expectedUnclaimed)
    {
        p.GetCdAggregatesForTest(out int gCd, out long chunkCdSum, out long chunkUnclaimedSum, out int gUncl);
        return gUncl == expectedUnclaimed
            && gUncl == chunkUnclaimedSum
            && gCd == 0
            && chunkCdSum == 0;
    }

    private static JobData MakeJob(int x, int y)
    {
        return new JobData
        {
            TypeId = JobTypeId.TreeChopping,
            ExecutionType = JobExecutionType.Stationary,
            PriorityTier = JobPriorityTier.TreeChopping,
            RequiredTool = ToolRequirement.None,
            TargetX = x,
            TargetY = y,
            StandX = x,
            StandY = y,
            MaxWorkers = 1,
            WorkDuration = 1f
        };
    }

    private static AgentDataPool NewPool() => new AgentDataPool(8);

    private static SimulationContext NewContext()
    {
        var ground = new TileType[MapSize, MapSize];
        var trees = new bool[MapSize, MapSize];
        var solid = new bool[MapSize, MapSize];
        var walkable = new List<(int X, int Y)>(MapSize * MapSize);
        for (int x = 0; x < MapSize; x++)
            for (int y = 0; y < MapSize; y++)
                walkable.Add((x, y));

        return new SimulationContext(ground, null, trees, solid, walkable,
            new AgentSpatialGrid(MapSize, MapSize), new AgentMovementService(), new Random(1234), 64);
    }
}