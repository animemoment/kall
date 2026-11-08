using System;
using System.Diagnostics;
using System.Threading;
using Godot;
using Game.Core;
using Game.Core.World;
using Game.Core.WorldStreaming;
using Game.Core.WorldStreaming.Integration;
using Game.UI.Streaming;

namespace Game.Tests;

/// <summary>
/// 14-02 / R-019: стена, построенная за кромкой, обязана попадать в стоимость мира.
///
/// Что доказываем: `StreamingWorldView.AddBlock(Wall)` помечает клетку в
/// `WorldCostCache` (D-11 п.4), а `RemoveBlock` — снимает. Раньше
/// `MarkBlocked`/`MarkUnblocked` не вызывались ниоткуда: окна мирового HPA
/// держали стоимость без стен, и агент шёл сквозь свежепостроенную стену.
///
/// Запуск: godot --headless --path . --script res://tests/run_world_wall_cost.gd
/// </summary>
public partial class TestWorldWallCost : RefCounted
{
    private const ulong Seed = 0xDE570001UL;
    private const uint GenVersion = 3;

    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print("[WALL-COST] Стена за кромкой попадает в стоимость мира");
        GD.Print("==================================================");

        WorldModel.Configure(Seed, GenVersion);
        WorldModel.BindResident(null);
        UnifiedWorldGenerator.SetWorldOrigin(0, 0);
        WorldCostCache.Clear();

        string dir = System.IO.Path.Combine(OS.GetUserDataDir(), $"wallcost_{Guid.NewGuid():N}");
        var view = new StreamingWorldView
        {
            Name = "WallCostTestView",
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

            var rect = new WorldRect(1024, 1024, 1408, 1408);
            if (!Pump(view, rect, () => view.LoadedChunks >= 16, 30000, "загрузка окна мира"))
            {
                GD.PrintErr("[WALL-COST] окно мира не загрузилось");
                return false;
            }

            if (!FindGrassCell(view, rect, out long x, out long y))
            {
                GD.PrintErr("[WALL-COST] не найдена трава за кромкой");
                return false;
            }
            GD.Print($"[WALL-COST] клетка за кромкой: ({x},{y})");

            int before = WorldCostCache.BlockedCellsCount;
            if (WorldCostCache.CostAt(x, y) == 0)
            {
                GD.PrintErr("[WALL-COST] клетка уже непроходима до стены — тест невалиден");
                return false;
            }

            if (!view.AddBlock(WorldDeltaKind.Wall, x, y))
            {
                GD.PrintErr("[WALL-COST] AddBlock(Wall) вернул false");
                return false;
            }

            if (WorldCostCache.CostAt(x, y) != 0)
            {
                GD.PrintErr($"[WALL-COST] FAIL: после стены стоимость {WorldCostCache.CostAt(x, y)}, ожидалось 0");
                return false;
            }
            if (WorldCostCache.BlockedCellsCount != before + 1)
            {
                GD.PrintErr($"[WALL-COST] FAIL: BlockedCellsCount={WorldCostCache.BlockedCellsCount}, ожидалось {before + 1}");
                return false;
            }
            GD.Print("[WALL-COST] OK: стена заблокировала клетку в WorldCostCache");

            if (!view.RemoveBlock(WorldDeltaKind.Wall, x, y))
            {
                GD.PrintErr("[WALL-COST] RemoveBlock(Wall) вернул false");
                return false;
            }

            if (WorldCostCache.CostAt(x, y) == 0)
            {
                GD.PrintErr("[WALL-COST] FAIL: после сноса клетка всё ещё непроходима");
                return false;
            }
            if (WorldCostCache.BlockedCellsCount != before)
            {
                GD.PrintErr($"[WALL-COST] FAIL: после сноса BlockedCellsCount={WorldCostCache.BlockedCellsCount}, ожидалось {before}");
                return false;
            }
            GD.Print("[WALL-COST] OK: снос снимает блокировку");

            // План стены НЕ должен блокировать путь: он ещё не построен.
            if (!view.AddBlock(WorldDeltaKind.WallPlan, x, y))
            {
                GD.PrintErr("[WALL-COST] AddBlock(WallPlan) вернул false");
                return false;
            }
            if (WorldCostCache.CostAt(x, y) == 0)
            {
                GD.PrintErr("[WALL-COST] FAIL: чертёж стены заблокировал путь (должен блокировать только готовый)");
                return false;
            }
            GD.Print("[WALL-COST] OK: чертёж путь не блокирует");
            ok = true;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[WALL-COST] исключение: {ex}");
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
        GD.Print(ok ? "[WALL-COST] PASS" : "[WALL-COST] FAIL");
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
                GD.PrintErr($"[WALL-COST] таймаут ожидания: {what}");
                return false;
            }
            Thread.Sleep(15);
        }
        return true;
    }

    private static bool FindGrassCell(StreamingWorldView view, WorldRect rect, out long x, out long y)
    {
        for (long cy = rect.MinY; cy <= rect.MaxY; cy += 2)
        {
            for (long cx = rect.MinX; cx <= rect.MaxX; cx += 2)
            {
                if (cx < 1024 || cy < 1024)
                    continue;
                if (!view.TryCellInfo(cx, cy, out GeneratedCell cell) || cell.Terrain != BaseTerrainKind.Grass)
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