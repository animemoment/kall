using System;
using Godot;
using TestWorldGen;

public partial class TestPureWorldGen : RefCounted
{
    public bool Run()
    {
        try
        {
            WorldGenTestSuite.RunAllTests();
            return true;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[WORLDGEN FAIL] {ex.Message}\n{ex.StackTrace}");
            return false;
        }
    }
}
