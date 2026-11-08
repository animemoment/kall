using System;
using Godot;
using Game.UI;
using Game.UI.Tools;

namespace Game.Tests;

/// <summary>
/// 20-01 / R-005: драг обязан заканчиваться, даже если событие отпускания не дошло.
///
/// Что проверяем:
///  (а) нормальный поток: нажали ЛКМ → отпустили ЛКМ → инструмент получил ровно
///      один OnClick и один OnRelease с isLeft=true;
///  (б) отпускание ПКМ во время ЛКМ-драга НЕ завершает драг (это уже было, R-005);
///  (в) ГЛАВНОЕ: если событие отпускания потерялось (курсор ушёл за окно, клик
///      перехватил HUD), менеджер обязан сам завершить драг по реальному состоянию
///      кнопки. Иначе рамка выделения остаётся на экране, а следующий клик
///      игнорируется («залипло»), пока игрок не нажмёт и не отпустит ещё раз.
///
/// Запуск: godot --headless --path . --script res://tests/run_mouse_button_stuck.gd
/// </summary>
public partial class TestMouseButtonStuck : RefCounted
{
    /// <summary>Инструмент-шпион: считает вызовы студии «клик/драг/отпускание».</summary>
    private sealed class SpyTool : ITool
    {
        public int Clicks;
        public int Releases;
        public int Drags;
        public bool LastReleaseWasLeft;
        public bool LastReleaseHasEnd;

        public void OnHover(Vector2I tilePos, Vector2 worldPos) { }
        public void OnClick(Vector2I tilePos, Vector2 worldPos, bool isLeftClick) => Clicks++;
        public void OnDrag(Vector2I startTile, Vector2I currentTile, Vector2 currentWorldPos) => Drags++;
        public void OnRelease(Vector2I startTile, Vector2I endTile, Vector2 worldPos, bool isLeftClick)
        {
            Releases++;
            LastReleaseWasLeft = isLeftClick;
            LastReleaseHasEnd = endTile != startTile || endTile == startTile; // всегда true; важно, что вызвано
        }
        public void Cancel() { }
    }

    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print("[MOUSE] Драг завершается, даже если отпускание потерялось (20-01)");
        GD.Print("==================================================");

        bool ok = true;
        try
        {
            ok &= NormalClickRelease();
            ok &= RightReleaseDoesNotEndLeftDrag();
            ok &= LostReleaseIsRecovered();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[MOUSE] исключение: {ex}");
            return false;
        }
        return ok;
    }

    private static (PlayerInteractionManager, SpyTool) MakeManager()
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        var manager = new PlayerInteractionManager
        {
            Name = "MouseStuckTest",
            Selection = new SelectionBox { Name = "TestSelection" }
        };
        tree.Root.AddChild(manager);
        manager.WallLayer = new TileMapLayer { Name = "TestWallLayer" };
        tree.Root.AddChild(manager.WallLayer);

        var spy = new SpyTool();
        manager.SetTool(spy);
        return (manager, spy);
    }

    private static void Drop(PlayerInteractionManager manager, SpyTool spy, TileMapLayer layer)
    {
        layer?.QueueFree();
        manager?.QueueFree();
    }

    private static InputEventMouseButton Button(MouseButton index, bool pressed, Vector2 at)
        => new() { ButtonIndex = index, Pressed = pressed, Position = at };

    /// <summary>(а) Обычный клик: OnClick и OnRelease по одному разу.</summary>
    private bool NormalClickRelease()
    {
        var (manager, spy) = MakeManager();
        try
        {
            var at = new Vector2(100, 100);
            manager._UnhandledInput(Button(MouseButton.Left, true, at));
            manager._UnhandledInput(Button(MouseButton.Left, false, at));

            if (spy.Clicks != 1 || spy.Releases != 1)
            {
                GD.PrintErr($"[MOUSE] (а) FAIL: clicks={spy.Clicks}, releases={spy.Releases} (ожидалось 1/1)");
                return false;
            }
            if (!spy.LastReleaseWasLeft)
            {
                GD.PrintErr("[MOUSE] (а) FAIL: отпускание ЛКМ пришло как isLeft=false");
                return false;
            }
            GD.Print("[MOUSE] (а) OK: обычный клик — 1 OnClick + 1 OnRelease(isLeft=true)");
            return true;
        }
        finally
        {
            Drop(manager, spy, manager.WallLayer);
        }
    }

    /// <summary>(б) Отпускание ПКМ не завершает ЛКМ-драг.</summary>
    private bool RightReleaseDoesNotEndLeftDrag()
    {
        var (manager, spy) = MakeManager();
        try
        {
            var at = new Vector2(100, 100);
            manager._UnhandledInput(Button(MouseButton.Left, true, at));
            manager._UnhandledInput(Button(MouseButton.Right, false, at));

            if (spy.Releases != 0)
            {
                GD.PrintErr($"[MOUSE] (б) FAIL: отпускание ПКМ завершило ЛКМ-драг (releases={spy.Releases})");
                return false;
            }
            GD.Print("[MOUSE] (б) OK: ПКМ не завершает ЛКМ-драг");
            return true;
        }
        finally
        {
            Drop(manager, spy, manager.WallLayer);
        }
    }

    /// <summary>(в) Потерянное отпускание: менеджер сам завершает драг по состоянию кнопок.</summary>
    private bool LostReleaseIsRecovered()
    {
        var (manager, spy) = MakeManager();
        try
        {
            var at = new Vector2(100, 100);
            manager._UnhandledInput(Button(MouseButton.Left, true, at));
            if (spy.Clicks != 1)
            {
                GD.PrintErr($"[MOUSE] (в) нажатие не дошло до инструмента (clicks={spy.Clicks})");
                return false;
            }

            // Отпускание не пришло (курсор ушёл за окно / клик съел HUD).
            // Кнопка физически НЕ нажата → следующий кадр обязан завершить драг.
            manager._Process(0.016);
            if (spy.Releases != 1)
            {
                GD.PrintErr($"[MOUSE] (в) FAIL: драг не завершён кадром (releases={spy.Releases})");
                return false;
            }
            if (!spy.LastReleaseWasLeft)
            {
                GD.PrintErr("[MOUSE] (в) FAIL: восстановленное отпускание потеряло кнопку");
                return false;
            }

            // Главный симптом: следующий клик НЕ должен игнорироваться.
            manager._UnhandledInput(Button(MouseButton.Left, true, at));
            if (spy.Clicks != 2)
            {
                GD.PrintErr($"[MOUSE] (в) FAIL: следующий клик проигнорирован (clicks={spy.Clicks}, ожидалось 2)");
                return false;
            }
            manager._UnhandledInput(Button(MouseButton.Left, false, at));

            // Второй кадр после нормального отпускания ничего не должен трогать.
            int releasesAfterNormal = spy.Releases;
            manager._Process(0.016);
            if (spy.Releases != releasesAfterNormal)
            {
                GD.PrintErr("[MOUSE] (в) FAIL: кадр завершил уже завершённый драг (двойной OnRelease)");
                return false;
            }

            GD.Print("[MOUSE] (в) OK: потерянное отпускание восстанавливается, повторных OnRelease нет");
            return true;
        }
        finally
        {
            Drop(manager, spy, manager.WallLayer);
        }
    }
}