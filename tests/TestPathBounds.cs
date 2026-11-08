using System;
using Godot;
using Game.Simulation;

namespace Game.Tests;

/// <summary>
/// 05-02 / 07-02: запрос пути из клетки за границей карты.
///
/// Мир бесконечный, агент может оказаться на клетке с отрицательной координатой
/// (остров 1024 внутри мира). Раньше `RegionOfCell` считал регион чистой
/// арифметикой: для (-1, 5) получался отрицательный id, и
/// `GetRegionPathCached` индексировал им массив — IndexOutOfRange в потоке
/// симуляции. В игре это выглядело как «агент пропал», а в консоли — простыня
/// исключений; на горячем пути (TryFindPathScratch) падал весь тик.
///
/// Тест зовёт оба входа: обычный и горячий (zero-alloc). Оба обязаны вернуть
/// false без исключения.
///
/// Запуск: godot --headless --path . --script res://tests/run_path_bounds.gd
/// </summary>
public partial class TestPathBounds : RefCounted
{
    // 1024 — размер острова: меньше нельзя, оверлей регионов на маленькой карте
    // не строится, и тогда отрицательные координаты отсекаются более ранним
    // возвратом false (тест был бы зелёным и на сломанном коде).
    private const int MapSize = 1024;

    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print("[PATH-BOUNDS] Клетки за границей карты (05-02/07-02)");
        GD.Print("==================================================");

        HierarchicalPathfinder pf = HierarchicalPathfinder.Instance;
        try
        {
            byte[] cost = new byte[MapSize * MapSize];
            for (int i = 0; i < cost.Length; i++)
                cost[i] = HierarchicalPathfinder.CostGrass;
            pf.InitializeCost(cost, MapSize, MapSize);

            bool ok = true;
            long before = pf.FailOutOfBounds;

            ok &= ExpectFalse(pf, "scratch: x<0", -1, 5, 20, 20, true);
            ok &= ExpectFalse(pf, "scratch: y<0", 5, -1, 20, 20, true);
            ok &= ExpectFalse(pf, "scratch: x>=w", 20, 20, MapSize, 5, true);
            ok &= ExpectFalse(pf, "scratch: y>=h", 20, 20, 5, MapSize, true);
            ok &= ExpectFalse(pf, "обычный: x<0", -1, -1, 10, 10, false);
            ok &= ExpectFalse(pf, "обычный: цель за краем", 10, 10, MapSize + 5, 10, false);

            // Гард обязан быть именно тем, что отсек запросы: если бы отказ случился
            // раньше (пустой оверлей), счётчик остался бы на нуле и тест покраснел бы.
            long delta = pf.FailOutOfBounds - before;
            if (delta != 6)
            {
                GD.PrintErr($"[PATH-BOUNDS] FAIL: счётчик границ вырос на {delta}, ожидалось 6 " +
                            "— значит запросы отсекает что-то ДРУГОЕ, а гард не работает");
                ok = false;
            }
            else
            {
                GD.Print($"[PATH-BOUNDS] OK: гард границ отсек ровно {delta} запросов");
            }

            return ok;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[PATH-BOUNDS] FAIL: исключение вместо false: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
        finally
        {
            pf.Reset();
        }
    }

    private bool ExpectFalse(HierarchicalPathfinder pf, string label,
        int sx, int sy, int tx, int ty, bool scratch)
    {
        bool found = scratch
            ? pf.TryFindPathScratch(sx, sy, tx, ty, out _, out _)
            : pf.TryFindPath(sx, sy, tx, ty, out _, out _);

        if (found)
        {
            GD.PrintErr($"[PATH-BOUNDS] FAIL ({label}): " +
                        $"({sx},{sy})->({tx},{ty}) вернул true, ожидался false");
            return false;
        }

        GD.Print($"[PATH-BOUNDS] OK ({label}): ({sx},{sy})->({tx},{ty}) = false, без исключения");
        return true;
    }

    /// <summary>
    /// Нормальный путь внутри карты не должен пострадать от гарда. Берём прямую
    /// линию по траве в одном столбце: её закрывает проверка видимости, до регионов
    /// дело не доходит — то есть проверяем ровно то, что гард не съел валидный запрос.
    /// </summary>
    private bool InsideStillWorks(HierarchicalPathfinder pf)
    {
        bool found = pf.TryFindPathScratch(1, 1, 1, MapSize - 2, out _, out int count);
        if (!found)
        {
            GD.PrintErr("[PATH-BOUNDS] FAIL: прямой путь внутри карты перестал находиться");
            return false;
        }

        GD.Print($"[PATH-BOUNDS] OK: путь внутри карты найден (точек {count})");
        return true;
    }
}