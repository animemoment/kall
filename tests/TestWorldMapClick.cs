using System;
using System.Text.RegularExpressions;
using Godot;
using Game.UI.Streaming;

namespace Game.Tests;

/// <summary>
/// 22-01 / 12-02: клик по мировой карте вне режима выбора спавна не должен выбирать регион.
///
/// Почему это важно: Main.OnRegionSelected не «переключает вид», а заново генерирует
/// мир, сбрасывает колонию и создаёт новую (`SetWorldOrigin`, `RegisterZone`, `Fill`).
/// Пока карту можно открыть в игре (кнопка видна и вне спавна), любой клик ЛКМ по ней
/// стирал текущую колонию — в игре это выглядело как «нажал на карту и всё пропало».
///
/// Проверяем чистую функцию, которую использует обработчик, и то, что обработчик
/// действительно зовёт её (а не сравнивает координаты сам — так проще откатить правку
/// случайно).
///
/// Запуск: godot --headless --path . --script res://tests/run_worldmap_click.gd
/// </summary>
public partial class TestWorldMapClick : RefCounted
{
    private const string OverlayPath = "res://src/ui/streaming/WorldMapOverlay.cs";

    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print("[WORLDMAP-CLICK] Клик вне спавна не выбирает регион (22-01/12-02)");
        GD.Print("==================================================");

        bool ok = true;
        ok &= GateSemantics();
        ok &= HandlerUsesGate();
        return ok;
    }

    private bool GateSemantics()
    {
        // В режиме спавна клик по клетке внутри сетки — это выбор региона.
        if (!WorldMapOverlay.ShouldSelectRegion(true, 5, 7, 64, 64))
        {
            GD.PrintErr("[WORLDMAP-CLICK] FAIL: в режиме спавна клик по карте не выбирает регион");
            return false;
        }

        // Вне спавна — не выбирает, даже по валидной клетке.
        if (WorldMapOverlay.ShouldSelectRegion(false, 5, 7, 64, 64))
        {
            GD.PrintErr("[WORLDMAP-CLICK] FAIL: вне режима спавна клик по карте выбирает регион " +
                        "(это перегенерирует мир и стирает колонию)");
            return false;
        }

        // За границей сетки — не выбирает никогда.
        if (WorldMapOverlay.ShouldSelectRegion(true, -1, 0, 64, 64)
            || WorldMapOverlay.ShouldSelectRegion(true, 0, -1, 64, 64)
            || WorldMapOverlay.ShouldSelectRegion(true, 64, 0, 64, 64)
            || WorldMapOverlay.ShouldSelectRegion(true, 0, 64, 64, 64))
        {
            GD.PrintErr("[WORLDMAP-CLICK] FAIL: клетка за границей сетки считается выбором");
            return false;
        }

        GD.Print("[WORLDMAP-CLICK] OK: выбирает только в режиме спавна и только внутри сетки");
        return true;
    }

    private bool HandlerUsesGate()
    {
        if (!Godot.FileAccess.FileExists(OverlayPath))
        {
            GD.PrintErr($"[WORLDMAP-CLICK] нет {OverlayPath}");
            return false;
        }

        string src = Godot.FileAccess.GetFileAsString(OverlayPath);
        if (!Regex.IsMatch(src, @"ShouldSelectRegion\(\s*IsSpawnSelectionMode\s*,"))
        {
            GD.PrintErr("[WORLDMAP-CLICK] FAIL: обработчик клика не зовёт ShouldSelectRegion — " +
                        "гейт можно случайно откатить");
            return false;
        }

        // Ловим возврат старой проверки «только по границам сетки» в ветке клика.
        if (Regex.IsMatch(src, @"cellX >= 0 && cellX < GridWidth && cellY >= 0 && cellY < GridHeight"))
        {
            GD.PrintErr("[WORLDMAP-CLICK] FAIL: в обработчике осталась старая проверка без режима спавна");
            return false;
        }

        GD.Print("[WORLDMAP-CLICK] OK: обработчик использует общий гейт");
        return true;
    }
}