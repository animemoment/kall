using System;
using System.Diagnostics;
using System.Threading;
using Godot;
using Game.Core;
using Game.Core.World;
using Game.Core.WorldStreaming;
using Game.Core.WorldStreaming.Integration;
using Game.Simulation;
using Game.UI.Streaming;

namespace Game.Tests;

/// <summary>
/// §123-диагностика (@destroyer): воспроизводит РЕАЛЬНЫЙ флоу юзера:
///   инструмент → StreamingToolPlacement.TryPlaceBuilding (гейты WorldGameRules)
///   → план в _blocks → мост → НАСТОЯЩИЙ DispatchPendingJobs (claim агентом)
///   → движение → Commit → Complete → Building.
/// На каждом шаге печатает вердикт — где именно рвётся конвейер.
/// </summary>
public partial class test_world_plan_flow_diag : RefCounted
{
    private const uint GenVersion = 3;

    public bool Run()
    {
        GD.Print("=====================================================");
        GD.Print("[DIAG] §123: полный флоу мирового плана (как у юзера)");
        GD.Print("=====================================================");
        try
        {
            Diagnose();
            GD.Print("=====================================================");
            GD.Print("[DIAG] DONE (Code 0)");
            GD.Print("=====================================================");
            return true;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[DIAG FAILED] {ex}");
            return false;
        }
    }

    private void Diagnose()
    {
        ulong seed = 0xDE570001UL;
        WorldModel.Configure(seed, GenVersion);
        WorldModel.BindResident(null);
        UnifiedWorldGenerator.SetWorldOrigin(0, 0);

        string dir = System.IO.Path.Combine(OS.GetUserDataDir(), $"diag_{Guid.NewGuid():N}");
        var view = new StreamingWorldView
        {
            Name = "DiagView",
            SaveDirectory = dir,
            WipeStoreOnStart = true,
            WorldSeed = seed,
            GeneratorVersion = GenVersion,
            MaxConcurrency = 4,
            MinWindowChunks = 3,
            MaxWindowChunks = 6,
            MaxReadyChunksPerFrame = 16,
            MaxUnloadSchedulesPerFrame = 16
        };
        view._Ready();
        WorldModel.BindResident(view);

        var coordinator = new WorldSaveCoordinator(dir, seed, GenVersion, UnifiedWorldGenerator.FeatureSchemaVersion);
        var manager = new StreamingWorldManager(view, coordinator);
        manager.Open(0, 0);

        try
        {
            // ---- ШАГ 1: загрузить окно за кромкой ----
            var rect = new WorldRect(1024, 1024, 1216, 1216);
            if (!Pump(view, rect, () => view.LoadedChunks >= 4, 30000, "load"))
                throw new Exception("[P1] окно не загрузилось");
            GD.Print("[ШАГ 1] чанки за кромкой загружены: OK");

            // ---- ШАГ 2: найти траву за кромкой ----
            if (!FindGrass(view, rect, out long px, out long py))
                throw new Exception("нет травы за кромкой");
            GD.Print($"[ШАГ 2] трава: ({px},{py})");

            // ---- ШАГ 3: РЕАЛЬНЫЙ путь инструмента (как Main) ----
            var rules = new WorldGameRules(view, seed, GenVersion);
            var placement = new StreamingToolPlacement(manager, rules, 1024, 1024);

            bool canPlace = placement.CanPlaceBuilding(px, py);
            GD.Print($"[ШАГ 3] CanPlaceBuilding({px},{py}) = {canPlace}");
            if (!canPlace)
                throw new Exception("[P3] гейт CanBuildAt не пустил — вот почему план не ставится!");

            bool placed = placement.TryPlaceBuilding(px, py, Game.Core.BuildingType.Bench);
            GD.Print($"[ШАГ 3] TryPlaceBuilding = {placed}, HasBlock(BuildingPlan) = {view.HasBlock(WorldDeltaKind.BuildingPlan, px, py)}");
            if (!placed)
                throw new Exception("[P3] TryPlaceBuilding вернул false");

            // ---- ШАГ 4: визуал — тайл на план-слое + z-порядок над terrain ----
            var pos = new Vector2I((int)px, (int)py);
            var planLayer = view.GetNodeOrNull<TileMapLayer>("WorldPlan");
            int src = planLayer != null ? planLayer.GetCellSourceId(pos) : -999;
            GD.Print($"[ШАГ 4] WorldPlan.GetCellSourceId({pos}) = {src} (-1 = НЕ НАРИСОВАН → фантом!)");
            if (src == -1)
                GD.PrintErr("[ШАГ 4][!!!] План в данных ЕСТЬ, но тайла НЕТ — вот исчезновение!");
            else if (src == -999)
                GD.PrintErr("[ШАГ 4][!!!] Слой WorldPlan вообще не найден!");
            else
                GD.Print("[ШАГ 4] тайл плана нарисован: OK");

            // §124: effective z = view(-1) + слой(2) = +1 — НАД terrain MapRenderer (z=0).
            // Раньше слой был z=0 → effective -1 → ПОД непрозрачным terrain → невидим.
            if (planLayer != null)
            {
                int effectiveZ = view.ZIndex + planLayer.ZIndex;
                GD.Print($"[ШАГ 4] z: view={view.ZIndex}, слой={planLayer.ZIndex}, effective={effectiveZ} (>0 = над terrain: OK)");
                if (effectiveZ <= 0)
                    throw new Exception($"[P4] План-слой под terrain (effective z={effectiveZ} ≤ 0) — невидим!");
            }

            // ---- ШАГ 5: повторная постановка (фантом?) ----
            bool canAgain = placement.CanPlaceBuilding(px, py);
            GD.Print($"[ШАГ 5] повторное CanPlaceBuilding = {canAgain} (false = фантом, ожидаемо после успешной постановки)");

            // ---- ШАГ 6: мост (как Main) ----
            var board = new WorldJobBoard(view, manager);
            var bridge = new WorldJobBridge(board, JobDispatcher.Instance);
            JobRegistry.Register(new WorldPlanHandler(board));
            bridge.Tick(px, py);
            GD.Print($"[ШАГ 6] bridge.RegisteredCount = {bridge.RegisteredCount}, board.PendingCount = {board.PendingCount}, HasFreeWork = {board.HasFreeWork}");
            if (bridge.RegisteredCount == 0)
                throw new Exception("[P6] мост не зарегистрировал задачу!");
            if (!JobDispatcher.Instance.JobIndex.HasJobAt((int)px, (int)py, JobTypeId.WorldPlan))
                throw new Exception("[P6] задачи нет в индексе!");

            // ---- ШАГ 7: НАСТОЯЩИЙ диспатчер (как AgentSimulationThread) ----
            var pool = new AgentDataPool(1);
            var ground = new TileType[2, 2];
            var trees = new bool[2, 2];
            var walls = new bool[2, 2];
            var walkable = new System.Collections.Generic.List<(int X, int Y)> { (0, 0), (1, 0), (0, 1), (1, 1) };
            var movement = new AgentMovementService();
            var ctx = new SimulationContext(ground, null, trees, walls, walkable,
                new AgentSpatialGrid(2, 2), movement, new Random(42), 64);

            // Агент стоит прямо на цели (движение уже проверено прошлым тестом).
            pool.PositionX[0] = px * 64f + 32f;
            pool.PositionY[0] = py * 64f + 32f;
            pool.States[0] = AgentState.Idle;
            pool.CurrentJobId[0] = -1;
            pool.CurrentCellX[0] = (int)px;
            pool.CurrentCellY[0] = (int)py;

            // Пул бездельников — как сим-поток делает для idle-агентов.
            JobDispatcher.Instance.IdleWorkers.AddIdleWorker(0, pool);

            // Прогнать диспатч (как AgentSimulationThread каждый тик).
            JobDispatcher.Instance.DispatchPendingJobs(pool, ctx);

            bool claimed = pool.CurrentJobId[0] > 0 || pool.CurrentJobId[0] == 0
                ? pool.CurrentJobType[0] == JobTypeId.WorldPlan
                : false;
            GD.Print($"[ШАГ 7] диспатчер: CurrentJobId={pool.CurrentJobId[0]}, CurrentJobType={pool.CurrentJobType[0]}, state={pool.States[0]}");
            if (pool.CurrentJobType[0] != JobTypeId.WorldPlan)
            {
                GD.PrintErr("[ШАГ 7][!!!] Диспетчер НЕ выдал мировую задачу агенту — вот почему никто не идёт!");
                throw new Exception("[P7] claim не произошёл");
            }
            GD.Print("[ШАГ 7] агент получил задачу: OK");

            // ---- ШАГ 8: работа → завершение (§124: через очередь + тик моста) ----
            if (!JobRegistry.TryGetHandler(JobTypeId.WorldPlan, out var handler))
                throw new Exception("[P8] хендлер не найден");

            float total = 0f;
            for (int step = 0; step < 1000; step++)
            {
                handler.ExecuteParallel(0, 0.016f, pool, ctx);
                handler.Commit(0, 0.016f, pool, ctx);
                total += 0.016f;
                if (pool.States[0] == AgentState.Idle)
                    break;
            }
            // §124: sim-поток положил координаты в очередь — дренит мост (main thread).
            bridge.Tick(px, py);
            bool buildingPlaced = view.HasBlock(WorldDeltaKind.Building, px, py);
            bool planGone = !view.HasBlock(WorldDeltaKind.BuildingPlan, px, py);
            int srcAfter = planLayer != null ? planLayer.GetCellSourceId(pos) : -999;
            GD.Print($"[ШАГ 8] работа {total:F1}с: Building={buildingPlaced}, план снят={planGone}, тайл после = {srcAfter}");
            if (!buildingPlaced)
                throw new Exception("[P8] Building не поставлен после завершения");

            GD.Print("[DIAG] ВСЁ ПРОШЛО — конвейер цел. Значит, у юзера рвётся в другом месте (поток?).");
        }
        finally
        {
            view.QueueFree();
            Thread.Sleep(100);
            try { System.IO.Directory.Delete(dir, recursive: true); } catch { }
        }
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
                GD.PrintErr($"[DIAG] Pump timeout: {what}");
                return false;
            }
            Thread.Sleep(15);
        }
        return true;
    }

    private static bool FindGrass(StreamingWorldView view, WorldRect rect, out long x, out long y)
    {
        for (long cy = rect.MinY; cy <= rect.MaxY; cy += 4)
            for (long cx = rect.MinX; cx <= rect.MaxX; cx += 4)
            {
                if (cx < 1024 || cy < 1024)
                    continue;
                if (!view.TryCellInfo(cx, cy, out GeneratedCell cell) || cell.Terrain != BaseTerrainKind.Grass)
                    continue;
                x = cx;
                y = cy;
                return true;
            }
        x = y = 0;
        return false;
    }
}
