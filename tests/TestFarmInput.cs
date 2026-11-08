using System;
using System.Text.RegularExpressions;
using Godot;
using Game.Core;
using Game.UI;

namespace Game.Tests;

/// <summary>
/// 24-08 / 23-03 / 23-01: ввод окна фермы.
///
/// 24-08 — корень Farm.tscn на весь экран был обычным Control с дефолтным
///   MouseFilter=Stop: пока окно открыто, он съедал клики по карте, а клик мимо
///   панели его не закрывал. Теперь корню ставится Ignore, панелям — Stop,
///   кнопки не трогаем (D-08).
/// 23-03 — подсказка «i» проверяла границы ОСТРОВНЫХ массивов раньше, чем «своя ли
///   клетка», поэтому ферма за кромкой (R-021) оставалась без подсказки. Теперь
///   сначала клетка своей фермы, потом сэмпл: остров — массивы, кромка — дверь мира.
/// 23-01 — крестик JobDisplay в режиме фермы зовёт Work-фасад, а тот по guard'у
///   Kind==Work ничего не делает. Окно фермы само вешает реакцию на ту же кнопку.
///
/// Запуск: godot --headless --path . --script res://tests/run_farm_input.gd
/// </summary>
public partial class TestFarmInput : RefCounted
{
    private const string ControllerPath = "res://src/ui/controllers/FarmController.cs";

    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print("[FARM-INPUT] Ввод окна фермы: 24-08 / 23-03 / 23-01");
        GD.Print("==================================================");

        bool ok = true;
        ok &= ResolverRules();
        ok &= JournalAppliesAndRestores();
        ok &= JournalKeepsOriginalsOnRepeatApply();
        ok &= JournalToleratesFreedNode();
        ok &= LiveSceneFilters();
        ok &= TooltipCellsBeyondEdge();
        ok &= TooltipTextFormat();
        ok &= TooltipSourceUsesDoor();
        ok &= JobDisplayCloseWired();
        return ok;
    }

    // --- 24-08: правило фильтров ---

    /// <summary>Корень — Ignore, панели — Stop; кнопки и прочее в «панели» не попадают.</summary>
    private bool ResolverRules()
    {
        bool ok = true;
        ok &= Expect(FarmController.ResolveFor(false) == Control.MouseFilterEnum.Ignore,
            "корень окна фермы — Ignore (клик мимо панели уходит в мир)");
        ok &= Expect(FarmController.ResolveFor(true) == Control.MouseFilterEnum.Stop,
            "панель окна фермы — Stop (окно не проваливается под кликом)");

        var panel = new Panel();
        var panelContainer = new PanelContainer();
        var scroll = new ScrollContainer();
        var list = new ItemList();
        var plain = new Control();
        var button = new Button();
        var label = new Label();
        try
        {
            ok &= Expect(FarmController.IsInputBlockingNode(panel)
                         && FarmController.IsInputBlockingNode(panelContainer)
                         && FarmController.IsInputBlockingNode(scroll)
                         && FarmController.IsInputBlockingNode(list),
                "панели/списки ловят мышь: Panel, PanelContainer, ScrollContainer, ItemList");
            ok &= Expect(!FarmController.IsInputBlockingNode(plain)
                         && !FarmController.IsInputBlockingNode(button)
                         && !FarmController.IsInputBlockingNode(label),
                "обычный Control, кнопка и Label фильтр не получают — кнопки работают как раньше (D-08)");
        }
        finally
        {
            Drop(panel, panelContainer, scroll, list, plain, button, label);
        }
        return ok;
    }

    /// <summary>
    /// Журнал: применить «корень Ignore / панели Stop» и вернуть ровно то, что стояло
    /// в сцене до правки. Именно этого не хватало: правка фильтров была необратимой.
    /// </summary>
    private bool JournalAppliesAndRestores()
    {
        var root = new Control { Name = "Root" };
        root.MouseFilter = Control.MouseFilterEnum.Stop; // как в Farm.tscn: mouse_filter не задан
        var panel = new PanelContainer { Name = "Panel" };
        panel.MouseFilter = Control.MouseFilterEnum.Pass;
        var nested = new ScrollContainer { Name = "Scroll" };
        nested.MouseFilter = Control.MouseFilterEnum.Ignore;
        var button = new Button { Name = "Btn" }; // дефолт Stop, трогать нельзя
        root.AddChild(panel);
        panel.AddChild(nested);
        root.AddChild(button);

        var journal = new FarmController.MouseFilterJournal();
        try
        {
            journal.Apply(root);

            bool ok = true;
            ok &= Expect(root.MouseFilter == Control.MouseFilterEnum.Ignore,
                "журнал ставит корню Ignore — клики по карте больше не перехватываются (24-08)");
            ok &= Expect(panel.MouseFilter == Control.MouseFilterEnum.Stop,
                "журнал ставит вложенной панели Stop — по окну кликают");
            ok &= Expect(nested.MouseFilter == Control.MouseFilterEnum.Stop,
                "вложенный ScrollContainer тоже Stop");
            ok &= Expect(button.MouseFilter == Control.MouseFilterEnum.Stop,
                "кнопку журнал не трогает: у неё свой фильтр (D-08)");
            ok &= Expect(journal.Count == 3,
                $"под наблюдением ровно три узла (корень + 2 панели), а не {journal.Count}");
            ok &= Expect(journal.Tracks(root) && journal.Tracks(panel) && !journal.Tracks(button),
                "журнал помнит корень и панели, но не кнопку");

            journal.RestoreAll();

            ok &= Expect(root.MouseFilter == Control.MouseFilterEnum.Stop,
                "закрытие вернуло корню фильтр сцены (Stop), а не оставило Ignore (24-08)");
            ok &= Expect(panel.MouseFilter == Control.MouseFilterEnum.Pass,
                "закрытие вернуло панели её собственный Pass");
            ok &= Expect(nested.MouseFilter == Control.MouseFilterEnum.Ignore,
                "закрытие вернуло вложенному ScrollContainer его Ignore");
            ok &= Expect(button.MouseFilter == Control.MouseFilterEnum.Stop,
                "кнопка после закрытия не изменилась");
            ok &= Expect(journal.Count == 0, "журнал очищен после восстановления");
            return ok;
        }
        finally
        {
            Drop(root);
        }
    }

    /// <summary>Повторный Apply (окно открыли дважды) не затирает исходные значения.</summary>
    private bool JournalKeepsOriginalsOnRepeatApply()
    {
        var root = new Control { Name = "Root" };
        root.MouseFilter = Control.MouseFilterEnum.Pass;
        var panel = new Panel { Name = "Panel" };
        panel.MouseFilter = Control.MouseFilterEnum.Pass;
        root.AddChild(panel);

        var journal = new FarmController.MouseFilterJournal();
        try
        {
            journal.Apply(root);
            journal.Apply(root); // OnZoneSelected может прийти повторно
            bool ok = true;
            ok &= Expect(journal.Count == 2, $"повторный Apply не дублирует узлы (Count={journal.Count})");
            ok &= Expect(root.MouseFilter == Control.MouseFilterEnum.Ignore
                         && panel.MouseFilter == Control.MouseFilterEnum.Stop,
                "повторный Apply держит корень Ignore и панель Stop");
            journal.RestoreAll();
            ok &= Expect(root.MouseFilter == Control.MouseFilterEnum.Pass
                         && panel.MouseFilter == Control.MouseFilterEnum.Pass,
                "после повторного Apply восстановился исходный Pass, а не Ignore");
            return ok;
        }
        finally
        {
            Drop(root);
        }
    }

    /// <summary>Освобождённый узел в журнале не должен ронять восстановление.</summary>
    private bool JournalToleratesFreedNode()
    {
        var root = new Control { Name = "Root" };
        root.MouseFilter = Control.MouseFilterEnum.Stop;
        var panel = new PanelContainer { Name = "Panel" };
        root.AddChild(panel);

        var journal = new FarmController.MouseFilterJournal();
        try
        {
            journal.Apply(root);
            panel.Free(); // окно выгрузили вместе с деревом UI
            bool threw = false;
            try
            {
                journal.RestoreAll();
            }
            catch (Exception ex)
            {
                threw = true;
                GD.PrintErr($"[FARM-INPUT] RestoreAll упал на освобождённой ноде: {ex.GetType().Name}");
            }
            bool ok = Expect(!threw, "RestoreAll переживает освобождённую ноду");
            ok &= Expect(root.MouseFilter == Control.MouseFilterEnum.Stop,
                "корень всё равно восстановлен после освобождения панели");
            return ok;
        }
        finally
        {
            Drop(root);
        }
    }

    /// <summary>
    /// Живая сцена (headless): Farm.tscn поднимается внутри FarmController и получает
    /// Ignore на корне, Stop на панелях и не тронутые кнопки; Close() возвращает фильтр сцены.
    /// </summary>
    private bool LiveSceneFilters()
    {
        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree == null)
        {
            GD.Print("[FARM-INPUT] SKIP: нет SceneTree — живую сцену не поднять, проверяю источник");
            return SourceWiringChecks();
        }

        FarmController farm = null;
        try
        {
            farm = new FarmController { Name = "TestFarmInputFarm" };
            tree.Root.AddChild(farm);
            // Headless-скрипт не проходит ни одного кадра, поэтому _Ready добавленной
            // ноде сам не приходит. Зовём явно — так же делают TestWorldDemolish,
            // TestSeamParity и другие тесты со сценовыми нодами.
            if (!farm.IsNodeReady())
                farm._Ready();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[FARM-INPUT] SKIP: FarmController не поднялся в headless ({ex.GetType().Name}: {ex.Message}); " +
                        "ниже — проверка источника");
            Drop(farm);
            return SourceWiringChecks();
        }

        try
        {
            var rootPanel = farm.FindChild("Control", false, false) as Control;
            if (rootPanel == null)
            {
                GD.PrintErr("[FARM-INPUT] FAIL: Farm.tscn не инстанцировался внутри FarmController");
                return false;
            }

            bool ok = true;
            ok &= Expect(rootPanel.MouseFilter == Control.MouseFilterEnum.Ignore,
                "живая сцена: корень Farm.tscn получил Ignore (24-08)");

            var scroll = rootPanel.FindChild("ScrollContainer", true, false) as Control;
            ok &= Expect(scroll != null && scroll.MouseFilter == Control.MouseFilterEnum.Stop,
                "живая сцена: ScrollContainer окна — Stop");

            var panel = rootPanel.FindChild("PanelContainer", true, false) as Control;
            ok &= Expect(panel != null && panel.MouseFilter == Control.MouseFilterEnum.Stop,
                "живая сцена: PanelContainer окна — Stop");

            var escape = rootPanel.FindChild("esc", true, false) as BaseButton;
            ok &= Expect(escape != null && escape.MouseFilter == Control.MouseFilterEnum.Stop,
                "живая сцена: крестик окна остался Stop — кнопки не урезаны (D-08)");

            var jobButton = rootPanel.FindChild("ButtonForJobDisplay", true, false) as BaseButton;
            ok &= Expect(jobButton != null && jobButton.MouseFilter == Control.MouseFilterEnum.Stop,
                "живая сцена: кнопка JobDisplay осталась Stop (D-08)");

            farm.Close();
            ok &= Expect(rootPanel.MouseFilter == Control.MouseFilterEnum.Stop,
                "живая сцена: Close() вернул корню фильтр сцены, правка не осталась навсегда (24-08)");

            return ok;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[FARM-INPUT] FAIL: живая сцена бросила {ex.GetType().Name}: {ex.Message}");
            return false;
        }
        finally
        {
            Drop(farm);
        }
    }

    /// <summary>Если живую сцену поднять нельзя — проверяем, что контроллер вообще зовёт настройку.</summary>
    private bool SourceWiringChecks()
    {
        string src = Godot.FileAccess.GetFileAsString(ControllerPath);
        if (string.IsNullOrEmpty(src))
        {
            GD.PrintErr($"[FARM-INPUT] FAIL: не прочитать {ControllerPath}");
            return false;
        }

        bool ok = true;
        Match load = Regex.Match(src, @"private void LoadScene\(\)[\s\S]*?\n    \}");
        ok &= Expect(load.Success && load.Value.Contains("ConfigureInputBlocking(_rootPanel)"),
            "источник: LoadScene настраивает перехват мыши");

        Match open = Regex.Match(src, @"private void OnZoneSelected\(FarmZone zone\)[\s\S]*?\n    \}");
        ok &= Expect(open.Success && open.Value.Contains("ConfigureInputBlocking(_rootPanel)"),
            "источник: OnZoneSelected возвращает перехват перед показом окна");

        Match close = Regex.Match(src, @"public void Close\(\)[\s\S]*?\n    \}");
        ok &= Expect(close.Success && close.Value.Contains("_mouseJournal.RestoreAll()"),
            "источник: Close() возвращает прежние фильтры мыши");
        return ok;
    }

    // --- 23-03: подсказка за кромкой ---

    /// <summary>Клетки за кромкой не индексируются в островных массивах и не роняют сэмпл.</summary>
    private bool TooltipCellsBeyondEdge()
    {
        var humidity = new HumidityMap(4, 4);
        humidity.Set(1, 1, 20);
        var fertility = new FertilityMap(4, 4);
        fertility.Set(1, 1, 50);

        bool ok = true;
        ok &= Expect(FarmController.IsInMapBounds(0, 0, 4, 4)
                     && FarmController.IsInMapBounds(1023, 1023, 1024, 1024),
            "IsInMapBounds: клетки внутри карты — островные");
        ok &= Expect(!FarmController.IsInMapBounds(4, 4, 4, 4)
                     && !FarmController.IsInMapBounds(1024, 1024, 1024, 1024)
                     && !FarmController.IsInMapBounds(-1, -1, 1024, 1024),
            "IsInMapBounds: кромка, 1024 и отрицательные — уже не остров");

        ok &= Expect(FarmController.TrySampleIslandCell(1, 1, 4, 4, humidity, fertility, out var inside)
                     && inside.Moisture == 20 && inside.Fertility == 50 && !inside.FromWorld,
            "островная клетка читается из массивов карты, FromWorld=false");

        bool threw = false;
        try
        {
            // Раньше первый же if по границам гасил подсказку за кромкой; теперь
            // клетка честно уходит в дверь мира, а без карты сэмпл просто пустой.
            ok &= Expect(!FarmController.TrySampleIslandCell(1024, 1024, 1024, 1024,
                    humidity, fertility, out _),
                "клетка 1024,1024 — не островная: сэмпл уходит в дверь мира (23-03)");
            ok &= Expect(!FarmController.TrySampleIslandCell(-5, -5, 1024, 1024,
                    humidity, fertility, out _),
                "отрицательные координаты не индексируют массивы");
            _ = FarmController.TrySampleCell(2048, 2048, out _);       // карты нет — false, без исключения
            _ = FarmController.TrySampleCell(-7, 3, out _);
            _ = FarmController.TrySampleWorldCell(1024, 1024, out _);  // дверь мира не роняет UI
        }
        catch (Exception ex)
        {
            threw = true;
            GD.PrintErr($"[FARM-INPUT] сэмпл подсказки за кромкой бросил {ex.GetType().Name}: {ex.Message}");
        }
        ok &= Expect(!threw, "подсказка за кромкой не падает (23-03)");
        return ok;
    }

    /// <summary>Формат строки подсказки: слова влажности/плодородия и текст про культуру.</summary>
    private bool TooltipTextFormat()
    {
        var dry = new FarmController.CellSample(20, 50, false, 0, false);
        string dryText = FarmController.ComposeTooltipText(in dry);
        bool ok = Expect(dryText.Contains("Влага: 20% сухо")
                         && dryText.Contains("Плодородие: 50 бедно")
                         && dryText.Contains("Пусто"),
            "подсказка сухой бедной клетки: «20% сухо», «бедно», «Пусто»");

        // Клетка за кромкой приходит из мира (FromWorld=true) — текст обязан собраться так же.
        var world = new FarmController.CellSample(200, 200, true, 3, true);
        string worldText = FarmController.ComposeTooltipText(in world);
        ok &= Expect(worldText.Contains("мокро") && worldText.Contains("богато")
                     && worldText.Contains("Пшеница (фаза 3/4)"),
            "подсказка клетки из мира собирается: «мокро», «богато», «фаза 3/4»");
        return ok;
    }

    /// <summary>Источник: порядок «своя клетка → сэмпл» и дверь мира вместо границ острова.</summary>
    private bool TooltipSourceUsesDoor()
    {
        string src = Godot.FileAccess.GetFileAsString(ControllerPath);
        if (string.IsNullOrEmpty(src))
        {
            GD.PrintErr($"[FARM-INPUT] FAIL: не прочитать {ControllerPath}");
            return false;
        }

        bool ok = true;
        Match tick = Regex.Match(src, @"private void TickCellTooltip\(\)[\s\S]*?\n    \}");
        if (!tick.Success)
            return Expect(false, "источник: TickCellTooltip не найден");

        string body = tick.Value;
        int zoneCheck = body.IndexOf("zone.Tiles.Contains", StringComparison.Ordinal);
        int sample = body.IndexOf("TrySampleCell", StringComparison.Ordinal);
        ok &= Expect(zoneCheck >= 0 && sample > zoneCheck,
            "источник: сначала «своя ли клетка», потом сэмпл (23-03 — раньше границы острова гасили подсказку)");
        ok &= Expect(body.IndexOf("ground.GetLength", StringComparison.Ordinal) < 0,
            "источник: в подсказке больше нет раннего выхода по границам островных массивов");

        Match world = Regex.Match(src, @"public static bool TrySampleWorldCell\([\s\S]*?\n    \}");
        ok &= Expect(world.Success
                     && world.Value.Contains("Game.Core.World.WorldModel.CellAtLocal")
                     && world.Value.Contains("catch"),
            "источник: за кромкой подсказка идёт через дверь WorldModel и не бросает наружу");
        return ok;
    }

    // --- 23-01: крестик JobDisplay ---

    /// <summary>
    /// Крестик JobDisplay в режиме фермы: окно фермы само вешает реакцию на ту же кнопку
    /// и снимает выбор фермы (Work-фасад это делает только для Kind==Work).
    /// </summary>
    private bool JobDisplayCloseWired()
    {
        string src = Godot.FileAccess.GetFileAsString(ControllerPath);
        if (string.IsNullOrEmpty(src))
        {
            GD.PrintErr($"[FARM-INPUT] FAIL: не прочитать {ControllerPath}");
            return false;
        }

        bool ok = true;
        Match open = Regex.Match(src, @"private void OnJobDisplayPressed\(\)[\s\S]*?\n    \}");
        ok &= Expect(open.Success && open.Value.Contains("HookJobDisplayEscape()"),
            "источник: при открытии JobDisplay из фермы подключается крестик");

        Match hook = Regex.Match(src, @"private void HookJobDisplayEscape\(\)[\s\S]*?\n    \}");
        ok &= Expect(hook.Success
                     && hook.Value.Contains("FindChild(\"esc\"")
                     && hook.Value.Contains("Pressed += OnFarmJobDisplayEscapePressed;"),
            "источник: крестик «esc» ищется внутри хоста JobDisplay и подписывается один раз");

        Match handler = Regex.Match(src, @"private void OnFarmJobDisplayEscapePressed\(\)[\s\S]*?\n    \}");
        ok &= Expect(handler.Success && handler.Value.Contains("FarmZoneManager.Instance.DeselectZone()"),
            "источник: нажатие крестика снимает выбор фермы — JobDisplay закроется по своему событию (23-01)");

        Match exit = Regex.Match(src, @"public override void _ExitTree\(\)[\s\S]*?\n    \}");
        ok &= Expect(exit.Success && exit.Value.Contains("Pressed -= OnFarmJobDisplayEscapePressed;"),
            "источник: подписка на чужой крестик снимается в _ExitTree");
        return ok;
    }

    // --- утилиты ---

    private static bool Expect(bool condition, string message)
    {
        if (condition)
        {
            GD.Print($"[FARM-INPUT] OK: {message}");
            return true;
        }
        GD.PrintErr($"[FARM-INPUT] FAIL: {message}");
        return false;
    }

    private static void Drop(params Node[] nodes)
    {
        foreach (Node node in nodes)
        {
            if (node == null) continue;
            try
            {
                if (GodotObject.IsInstanceValid(node))
                    node.Free();
            }
            catch (ObjectDisposedException)
            {
                // Уже освобождена (например, вместе с родителем) — ничего не делаем.
            }
        }
    }
}