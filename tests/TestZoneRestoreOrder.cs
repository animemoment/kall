using System;
using System.IO;
using System.Text.RegularExpressions;
using Godot;
using Game.Core;
using Game.Core.WorldStreaming.Mutation;
using Game.Simulation;

namespace Game.Tests;

/// <summary>
/// 10-02 / 15-04: зоны колонии, загруженные из .zones.json, не должны стираться
/// при переключении региона.
///
/// Что проверяем:
///  (а) сам слой персистентности: сохранить → ClearAll → загрузить = зоны вернулись
///      (если это сломано, никакой порядок вызовов не спасёт);
///  (б) ПОРЯДОК в Main.cs: восстановление зон обязано идти ПОСЛЕ ResetColonyState(),
///      иначе загрузка попадает под ClearAll и зоны исчезают (баг и был в этом).
///      Проверка по тексту сцены — единственный способ поймать это headless.
///
/// Запуск: godot --headless --path . --script res://tests/run_zone_restore_order.gd
/// </summary>
public partial class TestZoneRestoreOrder : RefCounted
{
    private const string ZonesFile = "user://colonies/zone_order_guard.zones.json";
    private const ulong Seed = 0x2011_1002UL;

    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print("[ZONE-ORDER] Зоны переживают смену региона (10-02/15-04)");
        GD.Print("==================================================");

        bool ok = true;
        ok &= RoundTripSurvivesClear();
        ok &= MainRestoresZonesAfterReset();
        return ok;
    }

    /// <summary>(а) Сохранил → очистил → загрузил: зона на месте.</summary>
    private bool RoundTripSurvivesClear()
    {
        ZoneManager zm = ZoneManager.Instance;
        zm.ClearAll();
        try
        {
            if (!zm.CreateWorkZoneAt(512, 512, 1UL << 7, 8, 2, out Zone created, out string fail))
            {
                GD.PrintErr($"[ZONE-ORDER] не удалось создать зону: {fail}");
                return false;
            }
            int id = created.Id;

            if (!ZonePersistence.SaveZones("zone_order_guard", Seed, out string saveError))
            {
                GD.PrintErr($"[ZONE-ORDER] SaveZones: {saveError}");
                return false;
            }

            zm.ClearAll();
            if (zm.TryGetZoneById(id, out _))
            {
                GD.PrintErr("[ZONE-ORDER] ClearAll не очистил зоны — тест невалиден");
                return false;
            }

            if (!ZonePersistence.TryLoadZones("zone_order_guard", Seed, out string loadError))
            {
                GD.PrintErr($"[ZONE-ORDER] TryLoadZones: {loadError}");
                return false;
            }

            if (!zm.TryGetZoneById(id, out Zone restored))
            {
                GD.PrintErr($"[ZONE-ORDER] (а) FAIL: после загрузки зоны нет (id={id})");
                return false;
            }
            if (restored.CenterX != 512 || restored.CenterY != 512)
            {
                GD.PrintErr($"[ZONE-ORDER] (а) FAIL: центр зоны {restored.CenterX},{restored.CenterY} != 512,512");
                return false;
            }

            GD.Print($"[ZONE-ORDER] (а) OK: зона id={id} пережила ClearAll через .zones.json");
            return true;
        }
        finally
        {
            zm.ClearAll();
            try
            {
                string path = ProjectSettings.GlobalizePath(ZonesFile);
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch { }
        }
    }

    /// <summary>(б) В Main.cs восстановление зон идёт после сброса состояния колонии.</summary>
    private bool MainRestoresZonesAfterReset()
    {
        const string path = "res://scenes/main/Main.cs";
        if (!Godot.FileAccess.FileExists(path))
        {
            GD.PrintErr($"[ZONE-ORDER] нет {path}");
            return false;
        }

        string text = Godot.FileAccess.GetFileAsString(path);
        Match reset = Regex.Match(text, @"\bResetColonyState\(\)\s*;");
        // Ищем именно ВЫЗОВ, а не объявление метода: аргументы (zoneId, seed)
        // есть только у вызова, у объявления — (string zoneId, ulong seed).
        Match restore = Regex.Match(text, @"\bRestoreColonyZones\(\s*zoneId\s*,\s*seed\s*\)");
        if (!reset.Success)
        {
            GD.PrintErr("[ZONE-ORDER] (б) не найден вызов ResetColonyState() в Main.cs");
            return false;
        }
        if (!restore.Success)
        {
            GD.PrintErr("[ZONE-ORDER] (б) не найден вызов RestoreColonyZones(zoneId, seed) в Main.cs");
            return false;
        }
        if (restore.Index < reset.Index)
        {
            GD.PrintErr(
                $"[ZONE-ORDER] (б) FAIL: RestoreColonyZones на позиции {restore.Index} раньше " +
                $"ResetColonyState на {reset.Index} — зоны будут стёрты сразу после загрузки");
            return false;
        }

        GD.Print("[ZONE-ORDER] (б) OK: Main.cs восстанавливает зоны после сброса состояния");
        return true;
    }
}