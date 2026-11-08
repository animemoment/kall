using System;
using System.Diagnostics;
using Godot;
using Game.Core.World;
using Game.Core.WorldStreaming;
using Game.Simulation.Pathfinding;

namespace Game.Tests;

/// <summary>
/// Патфайндер по миру. Что доказываем:
///   1. HPA строит настоящий обходной путь (а не «прямо видно») — на паре, где прямая перекрыта.
///   2. Путь прокладывается ДАЛЕКО за старым островом (origin+2048), где раньше мира не было.
///   3. Контракт соблюдён: каждая точка проходима, между соседними — прямая видимость,
///      последняя точка — цель.
///   4. Слишком далёкая пара честно получает отказ, а не выдуманный путь.
///
/// Запуск: godot --headless --path . --script res://tests/run_world_pathfinder.gd
/// </summary>
public partial class TestWorldPathfinder : RefCounted
{
    private const ulong Seed = 0xC0FFEE1234ABCDEFul;
    private const uint Version = 3;

    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print("[WPATH] Патфайндер по миру (оконный HPA)");
        GD.Print("==================================================");

        WorldModel.Reset();
        WorldModel.Configure(Seed, Version);
        WorldCostCache.Clear();
        WorldPathfinder.Instance.Clear();

        RegionKey spawn = UnifiedWorldGenerator.FindHabitableSpawn(Seed, Version);
        long cx = spawn.X * 512;
        long cy = spawn.Y * 512;
        UnifiedWorldGenerator.SetWorldOrigin(cx, cy);

        GD.Print($"[WPATH] спавн региона: ({cx},{cy}); старый остров: [{cx}..{cx + 1024}]");

        bool ok = true;
        ok &= DetourPath(cx, cy);
        ok &= PathFarOutsideIsland(cx, cy);
        ok &= RejectFarX();
        ok &= MovementServiceBridge(cx, cy);

        WorldPathfinder.Instance.Clear();
        WorldCostCache.Clear();
        WorldModel.Reset();

        GD.Print("==================================================");
        GD.Print(ok ? "[WPATH] ALL WORLD PATHFINDER TESTS PASSED! (Code 0)" : "[WPATH] FAIL");
        GD.Print("==================================================");
        return ok;
    }

    /// <summary>Пара с перекрытой прямой видимостью: HPA обязан выдать обход из точек.</summary>
    private static bool DetourPath(long cx, long cy)
    {
        int scanned = 0, waterSeen = 0;

        // Ищем берег: вода в середине, суша по обе стороны за пределами прямой видимости.
        for (long y = cy; y < cy + 400; y += 2)
        {
            for (long x = cx; x < cx + 400; x += 2)
            {
                scanned++;
                if (WorldCostCache.IsWalkable(x, y)) continue;
                waterSeen++;

                foreach (int d in new[] { 12, 20, 28, 36 })
                {
                    long lx = x - d, rx = x + d;
                    if (!WorldCostCache.IsWalkable(lx, y) || !WorldCostCache.IsWalkable(rx, y)) continue;
                    if (HasLineOfSight(lx, y, rx, y)) continue;

                    var sw = Stopwatch.StartNew();
                    bool found = WorldPathfinder.Instance.TryFindPath(
                        lx, y, rx, y, out int[] path, out int count,
                        out long originX, out long originY, out int width);
                    sw.Stop();

                    if (!found || count == 0) continue;
                    if (!Validate(path, count, originX, originY, width, lx, y, rx, y))
                        return false;

                    GD.Print($"[WPATH] обход берега ({lx},{y})→({rx},{y}): {count} точек за {sw.ElapsedMilliseconds} мс — связно, цель достигнута");
                    return true;
                }
            }
        }

        GD.PrintErr($"[WPATH] FAIL: обход не найден (просмотрено клеток {scanned}, воды {waterSeen}).");
        return false;
    }

    /// <summary>Путь далеко за старым островом — там, где раньше «мира не было».</summary>
    private static bool PathFarOutsideIsland(long cx, long cy)
    {
        // Ищем землю кольцами: спавн может быть окружён океаном на сотни клеток.
        int[] distances = { 1024, 1536, 2048, 3072, 4096 };
        (int dx, int dy)[] dirs =
        {
            (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, -1), (1, -1), (-1, 1),
        };

        foreach (int dist in distances)
        {
            foreach ((int dx, int dy) in dirs)
            {
                long baseX = cx + dx * dist;
                long baseY = cy + dy * dist;

                for (long oy = baseY; oy < baseY + 200; oy += 4)
                {
                    for (long ox = baseX; ox < baseX + 200; ox += 4)
                    {
                        if (!WorldCostCache.IsWalkable(ox, oy)) continue;

                        foreach (int d in new[] { 25, 40, 55 })
                        {
                            long tx = ox + d;
                            long ty = oy + d / 2;
                            if (!WorldCostCache.IsWalkable(tx, ty)) continue;

                            bool found = WorldPathfinder.Instance.TryFindPath(
                                ox, oy, tx, ty, out int[] path, out int count,
                                out long originX, out long originY, out int width);

                            if (!found) continue;
                            if (count > 0 && !Validate(path, count, originX, originY, width, ox, oy, tx, ty))
                                return false;

                            GD.Print($"[WPATH] ЗА ОСТРОВОМ ({ox},{oy})→({tx},{ty}) [дистанция {dist}]: {count} точек — мир там есть");
                            return true;
                        }
                    }
                }
            }
        }

        GD.PrintErr("[WPATH] FAIL: за островом не нашёл проходимых клеток ни в одном направлении.");
        return false;
    }

    /// <summary>Честная граница: слишком далёкая пара получает отказ, а не выдуманный путь.</summary>
    private static bool RejectFarX()
    {
        bool found = WorldPathfinder.Instance.TryFindPath(
            0, 0, 5000, 5000, out _, out _, out _, out _, out _);
        if (found)
        {
            GD.PrintErr("[WPATH] FAIL: путь через 5000 клеток не должен строиться (окно ограничено).");
            return false;
        }
        GD.Print("[WPATH] далёкая пара (0,0)→(5000,5000): отказ — как задокументировано");
        return true;
    }

    /// <summary>
    /// Мост AgentMovementService: путь мира распаковывается в ЛОКАЛЬНЫЕ short
    /// вейпоинты (как островные) — и каждый вейпоинт обязан быть проходимым и
    /// видимым из предыдущего. Это ровно то, что агент кладёт в свой пул.
    /// </summary>
    private static bool MovementServiceBridge(long cx, long cy)
    {
        // Пара за кромкой: цель ЗА границей старого острова (local >= 1024).
        for (long oy = cy + 1100; oy < cy + 1600; oy += 4)
        {
            for (long ox = cx; ox < cx + 400; ox += 4)
            {
                if (!WorldCostCache.IsWalkable(ox, oy)) continue;

                foreach (int d in new[] { 25, 40, 55 })
                {
                    long tx = ox + d, ty = oy + d / 2;
                    if (!WorldCostCache.IsWalkable(tx, ty)) continue;

                    bool found = WorldPathfinder.Instance.TryFindPath(
                        ox, oy, tx, ty, out int[] path, out int count,
                        out long winOx, out long winOy, out int width);
                    if (!found || count == 0) continue;

                    var wx = new short[count];
                    var wy = new short[count];
                    var sw = Stopwatch.StartNew();
                    bool unpacked = Game.Simulation.AgentMovementService.TryUnpackWorldWaypoints(
                        path, count, winOx, winOy, width, cx, cy, wx, wy, 0);
                    sw.Stop();
                    if (!unpacked)
                    {
                        GD.PrintErr("[WPATH] FAIL: мост не распаковал вейпоинты (за short-диапазоном?).");
                        return false;
                    }

                    // Контракт моста: вейпоинт (lx,ly) ⇒ мировая (cx+lx, cy+ly)
                    // проходима; соседние видны; последний — цель.
                    long prevX = ox, prevY = oy;
                    for (int i = 0; i < count; i++)
                    {
                        long ax = cx + wx[i], ay = cy + wy[i];
                        if (!WorldCostCache.IsWalkable(ax, ay))
                        {
                            GD.PrintErr($"[WPATH] FAIL: вейпоинт ({ax},{ay}) непроходим.");
                            return false;
                        }
                        if (!HasLineOfSight(prevX, prevY, ax, ay))
                        {
                            GD.PrintErr($"[WPATH] FAIL: нет видимости ({prevX},{prevY})→({ax},{ay}).");
                            return false;
                        }
                        prevX = ax; prevY = ay;
                    }
                    if (prevX != tx || prevY != ty)
                    {
                        GD.PrintErr($"[WPATH] FAIL: мост закончил путь в ({prevX},{prevY}), цель ({tx},{ty}).");
                        return false;
                    }

                    GD.Print($"[WPATH] МОСТ: за кромкой ({ox - cx},{oy - cy})→({tx - cx},{ty - cy}) (local): " +
                             $"{count} short-вейпоинтов за {sw.Elapsed.TotalMicroseconds / 1000.0:F3} мс — проходимы, связны");
                    return true;
                }
            }
        }

        GD.PrintErr("[WPATH] FAIL: мост — не нашёл пару за кромкой (1100..1600 от origin).");
        return false;
    }

    /// <summary>
    /// Контракт HPA: путь — ПУТЕВЫЕ ТОЧКИ. Между соседними агент идёт по прямой,
    /// поэтому проверяем видимость, а не шаг в одну клетку.
    /// </summary>
    private static bool Validate(int[] path, int count, long originX, long originY, int width,
        long fromX, long fromY, long toX, long toY)
    {
        int prevX = (int)(fromX - originX);
        int prevY = (int)(fromY - originY);

        for (int i = 0; i < count; i++)
        {
            int packed = path[i];
            int lx = packed % width;
            int ly = packed / width;

            if (!WorldCostCache.IsWalkable(originX + lx, originY + ly))
            {
                GD.PrintErr($"[WPATH] FAIL: точка пути ({originX + lx},{originY + ly}) непроходима.");
                return false;
            }

            if (!HasLineOfSight(originX + prevX, originY + prevY, originX + lx, originY + ly))
            {
                GD.PrintErr($"[WPATH] FAIL: нет прямой видимости между ({originX + prevX},{originY + prevY}) и ({originX + lx},{originY + ly}).");
                return false;
            }

            prevX = lx;
            prevY = ly;
        }

        int lastX = (int)(toX - originX);
        int lastY = (int)(toY - originY);
        if (prevX != lastX || prevY != lastY)
        {
            GD.PrintErr($"[WPATH] FAIL: путь закончился в ({prevX},{prevY}), а цель ({lastX},{lastY}).");
            return false;
        }

        return true;
    }

    /// <summary>Брезенхем по МИРОВЫМ координатам.</summary>
    private static bool HasLineOfSight(long x0, long y0, long x1, long y1)
    {
        long dx = Math.Abs(x1 - x0);
        long dy = Math.Abs(y1 - y0);
        long sx = x0 < x1 ? 1 : -1;
        long sy = y0 < y1 ? 1 : -1;
        long err = dx - dy;
        long x = x0, y = y0;

        while (true)
        {
            if (!WorldCostCache.IsWalkable(x, y))
                return false;
            if (x == x1 && y == y1)
                return true;

            long e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x += sx; }
            if (e2 < dx) { err += dx; y += sy; }
        }
    }
}