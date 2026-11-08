using System;
using System.Text.RegularExpressions;
using Godot;
using Game.Core.WorldStreaming;

namespace Game.Tests;

/// <summary>
/// 14-01: picking a region next to an existing colony used to throw a NullReferenceException and
/// leave the player without a colony. A colony zone is 1024x1024 (<see cref="ActiveArea.Size"/>)
/// while a world region is 512x512, so any neighbouring region overlaps an existing zone:
/// RegisterZone correctly refuses, TryGetZone then returns false with a null area, and the old
/// code called area.Fill() anyway.
///
/// The test proves both halves:
///  1) the registry really refuses overlapping zones and yields a null area for them (this is
///     the exact condition that produced the NRE) — real code, not a copy;
///  2) both colony-generation sites in Main.cs check the result before touching the area and
///     report the conflict to the player instead of dereferencing null (source check: the
///     gameplay path needs a full scene and async generation, which headless cannot drive).
///
/// Run: godot --headless --path . --script res://tests/run_region_zone_conflict.gd
/// </summary>
public partial class TestRegionZoneConflict : RefCounted
{
    private const string MainPath = "res://scenes/main/Main.cs";
    private const string Tag = "[REGION-CONFLICT]";

    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print($"{Tag} соседний регион не роняет колонию (14-01)");
        GD.Print("==================================================");

        bool ok = true;

        // 1. Registry behaviour: the 1024 zone vs 512 region mismatch is real.
        var registry = new ActiveAreaRegistry();
        long size = ActiveArea.Size;
        ok &= Check("зона колонии больше региона", size == 1024);

        ok &= Check("первая зона регистрируется", registry.RegisterZone("colony_0_0", 0, 0));
        ok &= Check("соседний регион перекрывается и получает отказ",
            !registry.RegisterZone("colony_1_0", 512, 0));
        ok &= Check("через один регион — ок", registry.RegisterZone("colony_4_0", 2048, 0));
        ok &= Check("дубль id — отказ", !registry.RegisterZone("colony_0_0", 4096, 0));

        // 2. The exact crash precondition: refused zone → TryGetZone false + null area.
        bool got = registry.TryGetZone("colony_1_0", out ActiveArea refused);
        ok &= Check("отказанная зона не материализуется", !got && refused == null);
        ok &= Check("зарегистрированная зона материализуется",
            registry.TryGetZone("colony_0_0", out ActiveArea first) && first != null);

        // 3. Main.cs must check the result before Fill, at BOTH sites.
        string src = Godot.FileAccess.GetFileAsString(MainPath);
        if (string.IsNullOrEmpty(src))
        {
            GD.PrintErr($"{Tag} FAIL: не прочитал {MainPath}");
            return false;
        }

        int registerCalls = Regex.Matches(src, @"registry\.RegisterZone\(").Count;
        int guardedRegister = Regex.Matches(src, @"if \(!registry\.RegisterZone\(").Count;
        int guardedGet = Regex.Matches(src,
            @"if \(!registry\.TryGetZone\([^)]*out ActiveArea (\w+)\) \|\| \1 == null\)").Count;
        int nullBranches = Regex.Matches(src, @"if \(mapData == null\)").Count;
        int notices = Regex.Matches(src, @"ShowNotice\(").Count;

        ok &= Check("оба места генерации на месте", registerCalls == 2);
        ok &= Check("оба проверяют RegisterZone", guardedRegister == 2);
        ok &= Check("оба проверяют null после TryGetZone", guardedGet == 2);
        ok &= Check("оба обрабатывают mapData == null", nullBranches == 2);
        ok &= Check("игроку показывают причину, а не молчание", notices == 2);

        // Regressions guard: the crash shape (Fill right after TryGetZone without a check).
        bool oldShape = Regex.IsMatch(src,
            @"registry\.TryGetZone\(zoneId, out ActiveArea \w+\);\s*\n\s*\w+\.Fill\(");
        ok &= Check("старого вида «TryGetZone → Fill без проверки» нет", !oldShape);

        GD.Print(ok ? $"{Tag} PASS" : $"{Tag} FAIL");
        return ok;
    }

    private bool Check(string what, bool condition)
    {
        if (condition)
        {
            GD.Print($"{Tag} OK: {what}");
            return true;
        }
        GD.PrintErr($"{Tag} FAIL: {what}");
        return false;
    }
}