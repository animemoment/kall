using System.Text.RegularExpressions;
using Godot;

namespace Game.Tests;

/// <summary>
/// §03-05: счётчик занятых/желаемых рабочих на ползунке зоны не обновлялся, пока
/// окно открыто. Причина: _infoTimer в Tick обновлял зону, но не _workerNumber —
/// строка писались в отдельных местах, а RefreshInfo() не перечитывала текст.
///
/// Починка: RefreshInfo() теперь пишет _workerNumber.Text = $"{assigned}/{v}" —
/// и каждый тик, прошедший 1с, ползунок показывает актуальное соотношение.
///
/// Тест: (a) RefreshInfo() содержит запись в _workerNumber (источник),
/// (b) _infoTimer сбрасывается и RefreshInfo() вызывается каждые ~1с.
/// </summary>
public partial class TestJobDisplayWorkerCounter : RefCounted
{
    public bool Run()
    {
        GD.Print("=====================================================");
        GD.Print("[TEST] §03-05: счётчик зоны обновляется каждый тик 1с");
        GD.Print("=====================================================");

        try
        {
            bool ok = true;

            // (a) Источниковая проверка: RefreshInfo() пишет в _workerNumber.
            string src = Godot.FileAccess.GetFileAsString(
                "res://src/ui/controllers/WorkZoneController.cs");
            GD.Print("[source] WorkZoneController.cs найдено");

            Match refresh = Regex.Match(
                src, @"private void RefreshInfo\(\)[\s\S]*?\n    \}");
            ok &= Expect(refresh.Success, "RefreshInfo() найдена в источнике");
            if (refresh.Success)
            {
                bool setsNumber = refresh.Value.Contains("_workerNumber.Text");
                ok &= Expect(setsNumber,
                    "RefreshInfo() записывает _workerNumber.Text = $\"{assigned}/...\" " +
                    "(03-05: ползунок обновляется, пока окно открыто)");
            }

            // (b) Проверка: _infoTimer сбрасывается и RefreshInfo() вызывается каждые ~1с.
            Match tick = Regex.Match(
                src, @"public override void _Process\(double delta\)[\s\S]*?\n    \}");
            bool hasProcess = tick.Success;
            ok &= Expect(hasProcess, "_Process(delta) существует");
            if (hasProcess)
            {
                bool timerReset = tick.Value.Contains("_infoTimer = 0.0")
                                  && tick.Value.Contains("RefreshInfo()");
                ok &= Expect(timerReset,
                    "_infoTimer сбрасывается и RefreshInfo() вызывается каждые ~1с");
            }

            if (!ok)
            {
                GD.PrintErr("[P1] §03-05 FAIL (источник)");
                return false;
            }

            GD.Print("[OK] RefreshInfo() обновляет _workerNumber и вызывается каждые ~1с");
            GD.Print("=====================================================");
            GD.Print("[OK] DONE");
            GD.Print("=====================================================");
            return true;
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[TEST FAILED] {ex}");
            return false;
        }
    }

    private static bool Expect(bool cond, string msg)
    {
        GD.Print($"[expect] {msg}: {(cond ? "OK" : "FAIL")}");
        return cond;
    }
}
