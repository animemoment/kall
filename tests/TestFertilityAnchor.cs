using System;
using Godot;
using Game.Core;
using Game.Core.World;
using Game.Core.WorldStreaming;
using Game.Core.WorldStreaming.Integration;

namespace Game.Tests;

/// <summary>
/// 14-03: якорь восстановления плодородия (`FertilityMap._base`) обязан переживать
/// мост ActiveArea → MapData.
///
/// Что проверяем:
///  (а) производственный путь острова (`GenerateColonyWindow`) якорь ставит — это
///      контроль, что баг НЕ в живом пути колонии (проверка карточки ревью);
///  (б) мост `MapDataShim.ToMapData` якорь ТЕРЯЛ: `BaseAt` возвращал 0, и «пар»
///      (восстановление после сбора урожая) не работал никогда — это и есть 14-03,
///      латентно (путь зоны пока не подключён к симуляции);
///  (в) поведение, которое видит игрок: истощённая уборкой клетка обязана
///      восстановиться к стартовому значению, а не остаться навсегда ×0.8.
///
/// Запуск: godot --headless --path . --script res://tests/run_fertility_anchor.gd
/// </summary>
public partial class TestFertilityAnchor : RefCounted
{
    private const ulong Seed = 0xFE120314UL;
    private const uint GenVersion = 3;

    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print("[FERT-ANCHOR] Якорь плодородия переживает мост (14-03)");
        GD.Print("==================================================");

        bool ok = true;
        ok &= ColonyWindowHasAnchor();
        ok &= ShimKeepsAnchor();
        ok &= FallowRecoveryWorks();
        return ok;
    }

    /// <summary>(а) Окно колонии: якорь есть и равен стартовому плодородию.</summary>
    private bool ColonyWindowHasAnchor()
    {
        MapData map = UnifiedWorldGenerator.GenerateColonyWindow(Seed, GenVersion, 0, 0, 128, 128);

        int checkedCells = 0;
        for (int y = 0; y < map.Height; y += 3)
        {
            for (int x = 0; x < map.Width; x += 3)
            {
                if (map.Ground[x, y] != TileType.Grass)
                    continue;
                int cur = map.Fertility.Get(x, y);
                int baseValue = map.Fertility.BaseAt(x, y);
                if (baseValue <= 0 || baseValue != cur)
                {
                    GD.PrintErr($"[FERT-ANCHOR] (а) FAIL @({x},{y}): base={baseValue}, cur={cur}");
                    return false;
                }
                checkedCells++;
            }
        }

        if (checkedCells == 0)
        {
            GD.PrintErr("[FERT-ANCHOR] (а) нет травы в окне — тест невалиден");
            return false;
        }

        GD.Print($"[FERT-ANCHOR] (а) OK: GenerateColonyWindow ставит якорь ({checkedCells} клеток)");
        return true;
    }

    /// <summary>(б) Мост зоны: якорь обязан переноситься, а не молча теряться.</summary>
    private bool ShimKeepsAnchor()
    {
        var zone = new ActiveArea(0, 0);
        zone.Fill(Seed, GenVersion, 0, 0);
        if (!zone.IsFilled)
        {
            GD.PrintErr("[FERT-ANCHOR] (б) зона не заполнилась");
            return false;
        }

        MapData map = MapDataShim.ToMapData(zone);

        int grass = 0;
        int lost = 0;
        for (int y = 0; y < map.Height; y += 3)
        {
            for (int x = 0; x < map.Width; x += 3)
            {
                if (map.Ground[x, y] != TileType.Grass)
                    continue;
                grass++;
                int baseValue = map.Fertility.BaseAt(x, y);
                int zoneBase = zone.Fertility.BaseAt(x, y);
                if (baseValue != zoneBase || baseValue <= 0)
                    lost++;
            }
        }

        if (grass == 0)
        {
            GD.PrintErr("[FERT-ANCHOR] (б) нет травы в зоне — тест невалиден");
            return false;
        }
        if (lost > 0)
        {
            GD.PrintErr($"[FERT-ANCHOR] (б) FAIL: якорь потерян на {lost} из {grass} клеток после ToMapData");
            return false;
        }

        GD.Print($"[FERT-ANCHOR] (б) OK: якорь переносится мостом ({grass} клеток)");
        return true;
    }

    /// <summary>(в) Игровое поведение: истощение уборкой и восстановление под паром.</summary>
    private bool FallowRecoveryWorks()
    {
        MapData map = UnifiedWorldGenerator.GenerateColonyWindow(Seed, GenVersion, 0, 0, 64, 64);

        // Берём травяную клетку с запасом плодородия.
        int cx = -1, cy = -1;
        for (int y = 4; y < 60 && cx < 0; y++)
        {
            for (int x = 4; x < 60; x++)
            {
                if (map.Ground[x, y] == TileType.Grass && map.Fertility.Get(x, y) >= 60)
                {
                    cx = x;
                    cy = y;
                    break;
                }
            }
        }
        if (cx < 0)
        {
            GD.PrintErr("[FERT-ANCHOR] (в) нет плодородной травы — тест невалиден");
            return false;
        }

        int start = map.Fertility.Get(cx, cy);
        for (int i = 0; i < 3; i++)
            map.Fertility.DrainAfterHarvest(cx, cy);

        int drained = map.Fertility.Get(cx, cy);
        if (drained >= start)
        {
            GD.PrintErr($"[FERT-ANCHOR] (в) истощение не сработало: {start} → {drained}");
            return false;
        }

        // Пар: 60 тиков без огорода (farmMask пустой) — клетка должна подрасти.
        var noFarm = new bool[64 * 64];
        for (int i = 0; i < 60; i++)
            map.Fertility.Tick(map.Ground, null, noFarm);

        int after = map.Fertility.Get(cx, cy);
        GD.Print($"[FERT-ANCHOR] (в) плодородие: старт {start} → после 3 уборок {drained} → под паром {after}");
        if (after <= drained)
        {
            GD.PrintErr("[FERT-ANCHOR] (в) FAIL: под паром клетка не восстанавливается (якорь не работает)");
            return false;
        }

        GD.Print("[FERT-ANCHOR] (в) OK: истощение и восстановление под паром работают");
        return true;
    }
}