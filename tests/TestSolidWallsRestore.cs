using System.Text.RegularExpressions;
using Godot;
using Game.Simulation;

namespace Game.Tests;

/// <summary>
/// 15-03 / 16-01: после загрузки стены должны снова блокировать агентов.
///
/// Как было: реплей дельт идёт ДО создания симуляции (`ctx == null`), поэтому
/// `StructureManager.AddStructure` не мог записать `ctx.SolidWalls`; поток симуляции
/// создавал массив проходимости всегда-ложным и ничем его не заливал. В игре: загружаешь
/// колонию — агенты ходят сквозь собственные стены, а стены за кромкой острова вообще
/// перестают существовать для навигации.
///
/// Теперь массив заливается из `StructureManager` перед созданием контекста.
///
/// Запуск: godot --headless --path . --script res://tests/run_solid_walls_restore.gd
/// </summary>
public partial class TestSolidWallsRestore : RefCounted
{
    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print("[SOLID-WALLS] Стены блокируют агентов после загрузки (15-03/16-01)");
        GD.Print("==================================================");

        bool ok = true;
        ok &= FillFromStructures();
        ok &= NonBlockingStaysPassable();
        ok &= SimulationThreadUsesIt();
        return ok;
    }

    private bool FillFromStructures()
    {
        StructureManager sm = StructureManager.Instance;
        sm.ClearAll();

        const int w = 64, h = 64;
        var solid = new bool[w, h];

        if (!sm.AddStructure(10, 10, StructureRegistry.WoodWall))
        {
            GD.PrintErr("[SOLID-WALLS] FAIL: не удалось добавить стену");
            return false;
        }

        int filled = sm.FillSolidWalls(solid, w, h);
        if (filled != 1 || !solid[10, 10])
        {
            GD.PrintErr($"[SOLID-WALLS] FAIL: залито {filled} клеток, ожидалась 1 (стена 10,10)");
            sm.ClearAll();
            return false;
        }

        // Идемпотентность: повторный залив не должен ничего ломать.
        if (sm.FillSolidWalls(solid, w, h) != 0)
        {
            GD.PrintErr("[SOLID-WALLS] FAIL: повторный залив снова что-то записал");
            sm.ClearAll();
            return false;
        }

        // Клетка за пределами массива не должна ронять заливку.
        if (!sm.AddStructure(w + 5, h + 5, StructureRegistry.WoodWall))
        {
            GD.PrintErr("[SOLID-WALLS] FAIL: не удалось добавить стену за границей массива");
            sm.ClearAll();
            return false;
        }
        sm.FillSolidWalls(solid, w, h);
        GD.Print("[SOLID-WALLS] OK: блокирующие постройки заливаются в массив, границы не текут");
        sm.ClearAll();
        return true;
    }

    private bool NonBlockingStaysPassable()
    {
        StructureManager sm = StructureManager.Instance;
        sm.ClearAll();

        const int w = 32, h = 32;
        var solid = new bool[w, h];
        StructureDefinition table = StructureRegistry.WorkTable;
        if (table.BlocksMovement)
        {
            GD.Print("[SOLID-WALLS] пропуск: верстак в этой сборке блокирует движение — проверка не применима");
            return true;
        }
        sm.AddStructure(5, 5, table);
        int filled = sm.FillSolidWalls(solid, w, h);
        if (filled != 0 || solid[5, 5])
        {
            GD.PrintErr($"[SOLID-WALLS] FAIL: неблокирующая постройка залита как стена (filled={filled})");
            sm.ClearAll();
            return false;
        }
        GD.Print("[SOLID-WALLS] OK: неблокирующая постройка проходима");
        sm.ClearAll();
        return true;
    }

    private bool SimulationThreadUsesIt()
    {
        string src = Godot.FileAccess.GetFileAsString("res://src/simulation/AgentSimulationThread.cs");
        if (!Regex.IsMatch(src, @"bool\[,\]\s+solidWalls\s*=\s*new\s+bool\[[^\]]*\]\s*;\s*[\s\S]{0,600}?FillSolidWalls\("))
        {
            GD.PrintErr("[SOLID-WALLS] FAIL: поток симуляции не заливает стены — после загрузки " +
                        "агенты снова пойдут сквозь стены");
            return false;
        }
        GD.Print("[SOLID-WALLS] OK: поток симуляции заполняет массив проходимости из StructureManager");
        return true;
    }
}