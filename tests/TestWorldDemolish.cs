using System;
using System.Diagnostics;
using System.Threading;
using Godot;
using Game.Core;
using Game.Core.World;
using Game.Core.WorldStreaming;
using Game.Core.WorldStreaming.Integration;
using Game.UI.Streaming;
using Game.UI.Tools;

namespace Game.Tests;

/// <summary>
/// 11-03 / R-029: готовый объект за кромкой обязан сноситься инструментом.
///
/// Что доказываем: инструменты сноса снимали ТОЛЬКО `*Plan`-виды. Игра ставит план,
/// работу доводит `WorldPlanHandler`, а `WorldJobBoard.Complete` меняет план на готовый
/// вид (`WallPlan→Wall`) через `TryReplaceBlock`. После этого плана в клетке нет, и
/// старый снос молча возвращал false: объект оставался навсегда, а клетка (готовый вид
/// входит в `WorldDeltaKind.BlockingKinds`) становилась непригодной для строительства.
///
/// Проверяем реальный путь инструмента: `StreamingToolPlacement` + `StreamingWorldManager`
/// на настоящем `StreamingWorldView`, за кромкой острова (1024).
///
/// Запуск: godot --headless --path . --script res://tests/run_world_demolish.gd
/// </summary>
public partial class TestWorldDemolish : RefCounted
{
    private const ulong Seed = 0xDE570001UL;
    private const uint GenVersion = 3;

    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print("[DEMOLISH] Готовый объект за кромкой сносится (11-03)");
        GD.Print("==================================================");

        WorldModel.Configure(Seed, GenVersion);
        WorldModel.BindResident(null);
        UnifiedWorldGenerator.SetWorldOrigin(0, 0);
        WorldCostCache.Clear();

        string dir = System.IO.Path.Combine(OS.GetUserDataDir(), $"demolish_{Guid.NewGuid():N}");
        var view = new StreamingWorldView
        {
            Name = "DemolishTestView",
            SaveDirectory = dir,
            WipeStoreOnStart = true,
            WorldSeed = Seed,
            GeneratorVersion = GenVersion,
            MaxConcurrency = 4,
            MinWindowChunks = 3,
            MaxWindowChunks = 6,
            MaxReadyChunksPerFrame = 16,
            MaxUnloadSchedulesPerFrame = 16
        };

        bool ok = false;
        try
        {
            view._Ready();
            WorldModel.BindResident(view);

            var coordinator = new WorldSaveCoordinator(dir, Seed, GenVersion, UnifiedWorldGenerator.FeatureSchemaVersion);
            var manager = new StreamingWorldManager(view, coordinator);
            manager.Open(0, 0);
            var rules = new WorldGameRules(view, Seed, GenVersion);
            var placement = new StreamingToolPlacement(manager, rules, 1024, 1024);

            var rect = new WorldRect(1024, 1024, 1408, 1408);
            if (!Pump(view, rect, () => view.LoadedChunks >= 16, 30000, "загрузка окна мира"))
            {
                GD.PrintErr("[DEMOLISH] окно мира не загрузилось");
                return false;
            }

            if (!FindGrassCell(view, rect, out long x, out long y))
            {
                GD.PrintErr("[DEMOLISH] не найдена трава за кромкой");
                return false;
            }
            GD.Print($"[DEMOLISH] клетка за кромкой: ({x},{y})");

            // ---------- стена ----------
            if (!placement.TryPlaceWall(x, y))
            {
                GD.PrintErr("[DEMOLISH] TryPlaceWall не поставил план — тест невалиден");
                return false;
            }
            // Работа выполнена: WorldJobBoard.Complete делает ровно это.
            if (!manager.TryReplaceBlock(WorldDeltaKind.WallPlan, WorldDeltaKind.Wall, x, y))
            {
                GD.PrintErr("[DEMOLISH] TryReplaceBlock(WallPlan→Wall) не сработал — тест невалиден");
                return false;
            }
            if (!view.HasBlock(WorldDeltaKind.Wall, x, y))
            {
                GD.PrintErr("[DEMOLISH] готовой стены нет в клетке — тест невалиден");
                return false;
            }

            if (!placement.TryRemoveWall(x, y))
            {
                GD.PrintErr("[DEMOLISH] FAIL: готовую стену нельзя снести (TryRemoveWall=false)");
                return false;
            }
            if (view.HasBlock(WorldDeltaKind.Wall, x, y))
            {
                GD.PrintErr("[DEMOLISH] FAIL: стена осталась в клетке после сноса");
                return false;
            }
            if (WorldCostCache.CostAt(x, y) == 0)
            {
                GD.PrintErr("[DEMOLISH] FAIL: клетка осталась непроходимой после сноса стены");
                return false;
            }
            GD.Print("[DEMOLISH] OK: готовая стена снесена, клетка снова проходима");

            // ---------- здание (с payload типа) ----------
            if (!placement.TryPlaceBuilding(x, y, BuildingType.Bed))
            {
                GD.PrintErr("[DEMOLISH] TryPlaceBuilding(Bed) не поставил план");
                return false;
            }
            if (!manager.TryReplaceBlock(WorldDeltaKind.BuildingPlan, WorldDeltaKind.Building, x, y))
            {
                GD.PrintErr("[DEMOLISH] TryReplaceBlock(BuildingPlan→Building) не сработал");
                return false;
            }
            if (!placement.TryRemoveBuilding(x, y))
            {
                GD.PrintErr("[DEMOLISH] FAIL: готовое здание нельзя снести (TryRemoveBuilding=false)");
                return false;
            }
            if (view.HasBlock(WorldDeltaKind.Building, x, y))
            {
                GD.PrintErr("[DEMOLISH] FAIL: здание осталось после сноса");
                return false;
            }
            GD.Print("[DEMOLISH] OK: готовое здание снесено");

            // ---------- склад ----------
            if (!FindGrassCell(view, rect, out long sx, out long sy, x, y))
            {
                GD.PrintErr("[DEMOLISH] нет второй травяной клетки для склада");
                return false;
            }
            if (!placement.TryPlaceStockpile(sx, sy)
                || !manager.TryReplaceBlock(WorldDeltaKind.StockpilePlan, WorldDeltaKind.Stockpile, sx, sy)
                || !placement.TryRemoveStockpile(sx, sy)
                || view.HasBlock(WorldDeltaKind.Stockpile, sx, sy))
            {
                GD.PrintErr("[DEMOLISH] FAIL: готовый склад не сносится");
                return false;
            }
            GD.Print("[DEMOLISH] OK: готовый склад снесён");

            // ---------- грядка ----------
            // Готовый вид ставим напрямую: гейт посадки требует зоны фермы, которой в
            // этом тесте нет (это отдельная механика). Проверяем именно СНОС.
            if (!view.AddBlock(WorldDeltaKind.Crop, sx, sy))
            {
                GD.PrintErr("[DEMOLISH] не удалось поставить готовую грядку — тест невалиден");
                return false;
            }
            if (!placement.TryRemoveFarmPlot(sx, sy) || view.HasBlock(WorldDeltaKind.Crop, sx, sy))
            {
                GD.PrintErr("[DEMOLISH] FAIL: готовая грядка не сносится");
                return false;
            }
            GD.Print("[DEMOLISH] OK: готовая грядка снесена");

            // ---------- план по-прежнему снимается ----------
            if (!placement.TryPlaceWall(x, y) || !placement.TryRemoveWall(x, y)
                || view.HasBlock(WorldDeltaKind.WallPlan, x, y))
            {
                GD.PrintErr("[DEMOLISH] FAIL: чертёж перестал сниматься (регрессия старого пути)");
                return false;
            }
            GD.Print("[DEMOLISH] OK: чертёж снимается как раньше");

            ok = true;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[DEMOLISH] исключение: {ex}");
        }
        finally
        {
            view.QueueFree();
            Thread.Sleep(100);
            try { System.IO.Directory.Delete(dir, recursive: true); } catch { }
            WorldCostCache.Clear();
            WorldModel.BindResident(null);
            WorldModel.Reset();
        }

        GD.Print("==================================================");
        GD.Print(ok ? "[DEMOLISH] PASS" : "[DEMOLISH] FAIL");
        GD.Print("==================================================");
        return ok;
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
                GD.PrintErr($"[DEMOLISH] таймаут ожидания: {what}");
                return false;
            }
            Thread.Sleep(15);
        }
        return true;
    }

    private static bool FindGrassCell(StreamingWorldView view, WorldRect rect, out long x, out long y,
        long skipX = long.MinValue, long skipY = long.MinValue)
    {
        for (long cy = rect.MinY; cy <= rect.MaxY; cy += 2)
        {
            for (long cx = rect.MinX; cx <= rect.MaxX; cx += 2)
            {
                if (cx < 1024 || cy < 1024)
                    continue;
                if (cx == skipX && cy == skipY)
                    continue;
                if (!view.TryCellInfo(cx, cy, out GeneratedCell cell) || cell.Terrain != BaseTerrainKind.Grass)
                    continue;
                if (view.HasBlock(WorldDeltaKind.Wall, cx, cy) || view.HasBlock(WorldDeltaKind.Building, cx, cy)
                    || view.HasBlock(WorldDeltaKind.Stockpile, cx, cy) || view.HasBlock(WorldDeltaKind.Crop, cx, cy))
                    continue;
                x = cx;
                y = cy;
                return true;
            }
        }
        x = y = 0;
        return false;
    }
}