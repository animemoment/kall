using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Godot;
using Game.Simulation;

namespace Game.Tests;

/// <summary>
/// 03-01 / 10-04: тумблер «вкл/выкл» рабочей зоны.
///
/// Было две дырки:
///  1) `WorkZoneController.OnEnabledToggled` менял только локальный флаг окна и писал
///     в лог «ждёт SetEnabled в фасаде» — зона оставалась включённой;
///  2) даже когда флаг зоны выставлялся, `ZoneManager.DispatchZones` его не смотрел,
///     поэтому агенты продолжали получать работы выключенной зоны.
///
/// В игре это выглядело так: игрок выключает зону, а агенты всё равно идут туда работать.
///
/// Запуск: godot --headless --path . --script res://tests/run_zone_enabled.gd
/// </summary>
public partial class TestZoneEnabled : RefCounted
{
    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print("[ZONE-ENABLED] Тумблер зоны доходит до зоны (03-01/10-04)");
        GD.Print("==================================================");

        bool ok = true;
        ok &= ToggleReachesZone();
        ok &= DispatchSkipsDisabled();
        ok &= ControllerWired();
        return ok;
    }

    /// <summary>SetWorkEnabled меняет флаг зоны и возвращает true.</summary>
    private bool ToggleReachesZone()
    {
        ZoneManager zm = ZoneManager.Instance;
        zm.ClearAll();
        try
        {
            if (!zm.CreateWorkZoneAt(512, 512, 1UL << 7, 6, 2, out Zone zone, out string fail))
            {
                GD.PrintErr($"[ZONE-ENABLED] FAIL: не удалось создать зону: {fail}");
                return false;
            }

            if (!zm.SetWorkEnabled(zone.Id, false))
            {
                GD.PrintErr("[ZONE-ENABLED] FAIL: SetWorkEnabled(false) вернул false");
                return false;
            }
            if (zone.Enabled)
            {
                GD.PrintErr("[ZONE-ENABLED] FAIL: после выключения зона осталась Enabled");
                return false;
            }
            if (!zm.SetWorkEnabled(zone.Id, true) || !zone.Enabled)
            {
                GD.PrintErr("[ZONE-ENABLED] FAIL: зона не включается обратно");
                return false;
            }

            GD.Print("[ZONE-ENABLED] OK: выключение и включение зоны доходят до самого объекта зоны");
            return true;
        }
        finally
        {
            zm.ClearAll();
        }
    }

    /// <summary>Диспатч обязан пропускать выключенные зоны.</summary>
    private bool DispatchSkipsDisabled()
    {
        const string path = "res://src/simulation/zones/ZoneManager.cs";
        string src = Godot.FileAccess.GetFileAsString(path);
        if (!Regex.IsMatch(src, @"if\s*\(\s*!zone\.Enabled\s*\)\s*continue;"))
        {
            GD.PrintErr("[ZONE-ENABLED] FAIL: DispatchZones не пропускает выключенные зоны — " +
                        "агенты снова начнут уходить на работу в выключенную зону");
            return false;
        }

        GD.Print("[ZONE-ENABLED] OK: DispatchZones пропускает выключенные зоны");
        return true;
    }

    /// <summary>Тумблер в окне зоны обязан звать фасад, а не только менять свой флаг.</summary>
    private bool ControllerWired()
    {
        const string path = "res://src/ui/controllers/WorkZoneController.cs";
        string src = Godot.FileAccess.GetFileAsString(path);

        Match handler = Regex.Match(src,
            @"private void OnEnabledToggled\(bool pressed\)[\s\S]{0,600}?\n    \}");
        if (!handler.Success)
        {
            GD.PrintErr("[ZONE-ENABLED] FAIL: не найден обработчик тумблера");
            return false;
        }
        if (!Regex.IsMatch(handler.Value, @"SetEnabled\(\s*zone\.Id\s*,\s*pressed\s*\)"))
        {
            GD.PrintErr("[ZONE-ENABLED] FAIL: тумблер не зовёт SetEnabled — выключение снова " +
                        "останется только в окне");
            return false;
        }

        GD.Print("[ZONE-ENABLED] OK: тумблер окна вызывает SetEnabled(zone.Id, pressed)");
        return true;
    }
}