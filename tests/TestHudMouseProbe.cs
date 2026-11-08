using System;
using System.Reflection;
using System.Text.RegularExpressions;
using Godot;
using Game.UI;

namespace Game.Tests;

/// <summary>
/// 28-09 / 28-02 / 28-06: мышь HUD, проба клетки и две независимые подсказки.
///
/// 28-09 — SetMouseFilterRecursive ставил Stop ЛЮБОЙ Panel/PanelContainer, и панель
///   ресурсов AmountOfResources (1646..1920 × 454..1080) переставала пропускать клики
///   в мир, хотя в hud.tscn у неё mouse_filter = Ignore. Теперь правило такое:
///   интерактив (кнопки/списки/скроллы/поля) — Stop; панель — Stop только если внутри
///   неё есть интерактив; панель, которая рисует лишь текст и иконки, — Ignore.
/// 28-02 — SetCellProbe(this) вызывался только из HUD._Ready, а PlayerInteractionManager
///   создаётся позже (Main._Ready) → Instance == null, проба не перевязывалась. Теперь
///   привязка идемпотентная и зовётся из трёх мест: _Ready, Setup (Main зовёт его, когда
///   менеджер уже есть) и повтор из _Process по таймеру.
/// 28-06 — ShowFertilityAt безусловно гасил общий тултип, поэтому в режиме влажности
///   SelectTool.OnHover (ShowHumidityAt, затем ShowFertilityAt) гасил показанную цифру
///   в том же кадре. Теперь у режимов два независимых слоя со своими Label.
///
/// Живую сцену с настоящим MapRenderer в headless не поднять (шейдеры/текстуры),
/// поэтому 28-06 проверяется на настоящих Label-нодах через чистое правило слоя
/// (HUDController.CellTooltipLayer) + сверкой исходника: Show*/легенды трогают только
/// свой слой. Так же сделаны 23-03/24-08 в tests/TestFarmInput.cs.
///
/// На старом коде тест краснеет: правило даёт Stop для AmountOfResources, в _Ready нет
/// повторной попытки, а ShowFertilityAt гасит чужой Label.
///
/// Запуск: godot --headless --path . --script res://tests/run_hud_mouse_probe.gd
/// </summary>
public partial class TestHudMouseProbe : RefCounted
{
    private const string HudPath = "res://src/ui/controllers/HUDController.cs";
    private const string HudScenePath = "res://scenes/ui/hud.tscn";

    public bool Run()
    {
        GD.Print("==================================================");
        GD.Print("[HUD-MOUSE] 28-09 фильтры мыши / 28-02 проба клетки / 28-06 две подсказки");
        GD.Print("==================================================");

        bool ok = true;
        ok &= FilterRuleTable();
        ok &= FilterRuleOnRealTree();
        ok &= FilterRuleOnLiveScene();
        ok &= TooltipLayersIndependent();
        ok &= TooltipSourceWiring();
        ok &= ProbeBindsAfterManagerCreated();
        ok &= ProbeSourceWiring();
        return ok;
    }

    // --- 28-09: правило фильтра мыши ---

    /// <summary>
    /// Таблица правила: «панель только с текстом» → Ignore, «панель с кнопкой» → Stop,
    /// кнопки и прочий интерактив — Stop при любом содержимом (D-08).
    /// </summary>
    private bool FilterRuleTable()
    {
        var panel = new Panel();
        var panelContainer = new PanelContainer();
        var label = new Label();
        var row = new HBoxContainer();
        var button = new Button();
        var plain = new Control();
        var progress = new ProgressBar();
        var scroll = new ScrollContainer();
        var list = new ItemList();
        var tree = new Tree();
        var edit = new LineEdit();
        var slider = new HSlider();
        try
        {
            bool ok = true;

            ok &= Expect(HUDController.ResolveMouseFilter(panelContainer, false) == Control.MouseFilterEnum.Ignore,
                "28-09: PanelContainer без интерактива — Ignore (клик уходит в мир)");
            ok &= Expect(HUDController.ResolveMouseFilter(panelContainer, true) == Control.MouseFilterEnum.Stop,
                "28-09: PanelContainer с кнопкой внутри — Stop");
            ok &= Expect(HUDController.ResolveMouseFilter(panel, false) == Control.MouseFilterEnum.Ignore
                         && HUDController.ResolveMouseFilter(panel, true) == Control.MouseFilterEnum.Stop,
                "28-09: то же правило для Panel");

            ok &= Expect(HUDController.ResolveMouseFilter(button, false) == Control.MouseFilterEnum.Stop,
                "кнопка — Stop всегда (D-08: кнопки не урезаны)");
            ok &= Expect(HUDController.IsInteractiveNode(button)
                         && HUDController.IsInteractiveNode(scroll)
                         && HUDController.IsInteractiveNode(list)
                         && HUDController.IsInteractiveNode(tree)
                         && HUDController.IsInteractiveNode(edit)
                         && HUDController.IsInteractiveNode(slider),
                "интерактив: кнопка, скролл, список, дерево, поле ввода, слайдер");
            ok &= Expect(!HUDController.IsInteractiveNode(label)
                         && !HUDController.IsInteractiveNode(row)
                         && !HUDController.IsInteractiveNode(panel)
                         && !HUDController.IsInteractiveNode(plain)
                         && !HUDController.IsInteractiveNode(progress),
                "не интерактив: Label, HBoxContainer, Panel, обычный Control, ProgressBar");

            ok &= Expect(HUDController.ResolveMouseFilter(row, false) == Control.MouseFilterEnum.Ignore,
                "контейнер кнопок сам фильтр не меняет — площадь HUD не выросла");
            ok &= Expect(HUDController.ResolveMouseFilter(null, true) == Control.MouseFilterEnum.Ignore,
                "null-узел не роняет правило");
            return ok;
        }
        finally
        {
            Drop(panel, panelContainer, label, row, button, plain, progress, scroll, list, tree, edit, slider);
        }
    }

    /// <summary>
    /// Дерево как в hud.tscn: панель ресурсов с текстом/иконками, ряд кнопок, панель с
    /// кнопкой. После обхода: ресурсы прозрачны, кнопки ловят мышь, корень прозрачен
    /// (он на весь экран — иначе HUD съел бы весь ввод).
    /// </summary>
    private bool FilterRuleOnRealTree()
    {
        var root = new Control { Name = "HudRoot" };
        var resources = new PanelContainer { Name = "AmountOfResources" };
        var box = new VBoxContainer { Name = "VBoxContainer" };
        var caption = new Label { Name = "Label" };
        var row = new HBoxContainer { Name = "HBoxContainer" };
        var chip = new PanelContainer { Name = "BreadContainer" };
        var chipLabel = new Label { Name = "BreadAmount" };
        var buttonsRow = new HBoxContainer { Name = "BuildContainer" };
        var construct = new Button { Name = "ButtonConstruct" };
        var window = new PanelContainer { Name = "Window" };
        var deep = new Button { Name = "ZoneButton" };

        root.AddChild(resources);
        resources.AddChild(box);
        box.AddChild(caption);
        box.AddChild(row);
        row.AddChild(chip);
        chip.AddChild(chipLabel);
        root.AddChild(buttonsRow);
        buttonsRow.AddChild(construct);
        root.AddChild(window);
        window.AddChild(deep);

        // как в hud.tscn: панель ресурсов объявлена кликопрозрачной
        resources.MouseFilter = Control.MouseFilterEnum.Ignore;

        try
        {
            bool hasInteractive = HUDController.ApplyMouseFilters(root);

            bool ok = true;
            ok &= Expect(hasInteractive, "обход нашёл интерактив в поддереве");
            ok &= Expect(resources.MouseFilter == Control.MouseFilterEnum.Ignore,
                "28-09: AmountOfResources осталась Ignore — её 274×626 px больше не съедают клик");
            ok &= Expect(chip.MouseFilter == Control.MouseFilterEnum.Ignore
                         && caption.MouseFilter == Control.MouseFilterEnum.Ignore
                         && chipLabel.MouseFilter == Control.MouseFilterEnum.Ignore,
                "28-09: чип и подписи внутри панели ресурсов тоже прозрачны");
            ok &= Expect(root.MouseFilter == Control.MouseFilterEnum.Ignore,
                "корень HUD на весь экран остаётся Ignore");
            ok &= Expect(construct.MouseFilter == Control.MouseFilterEnum.Stop
                         && deep.MouseFilter == Control.MouseFilterEnum.Stop,
                "кнопки ловят мышь (D-08)");
            ok &= Expect(window.MouseFilter == Control.MouseFilterEnum.Stop,
                "28-09: панель с кнопкой внутри — Stop");
            ok &= Expect(box.MouseFilter == Control.MouseFilterEnum.Ignore
                         && row.MouseFilter == Control.MouseFilterEnum.Ignore,
                "обычные контейнеры остались прозрачными — площадь HUD не выросла");
            return ok;
        }
        finally
        {
            Drop(root);
        }
    }

    /// <summary>
    /// Живой hud.tscn (headless): после _Ready панель ресурсов сохраняет Ignore, кнопки
    /// остаются Stop, а у влажности и плодородия — две разные всплывашки (28-06).
    /// </summary>
    private bool FilterRuleOnLiveScene()
    {
        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree == null)
            return Skip("нет SceneTree — живой hud.tscn не поднять, правило проверено выше");

        HUDController hud = null;
        try
        {
            var packed = GD.Load<PackedScene>(HudScenePath);
            if (packed == null)
                return Skip($"{HudScenePath} не загрузился");

            hud = packed.Instantiate<HUDController>();
            if (hud == null)
                return Skip("корень hud.tscn — не HUDController");
        }
        catch (Exception ex)
        {
            return Skip($"hud.tscn не инстанцировался ({ex.GetType().Name}: {ex.Message})");
        }

        try
        {
            tree.Root.AddChild(hud);
            // Headless-скрипт не проходит кадров: если _Ready сам не пришёл, зовём явно
            // (так же делают TestFarmInput, TestWorldDemolish и другие).
            if (!hud.IsNodeReady())
                hud._Ready();
        }
        catch (Exception ex)
        {
            // Упавший _Ready живой сцены — это поломка, а не «нельзя поднять»: так и говорим.
            GD.PrintErr($"[HUD-MOUSE] FAIL: hud.tscn._Ready бросил {ex.GetType().Name}: {ex.Message}");
            Drop(hud);
            return false;
        }

        try
        {
            var resources = hud.FindChild("AmountOfResources", true, false) as Control;
            if (resources == null)
                return Skip("в hud.tscn нет AmountOfResources — сцена изменилась, проверено правило выше");

            bool ok = true;
            ok &= Expect(resources.MouseFilter == Control.MouseFilterEnum.Ignore,
                "живая сцена: AmountOfResources после _Ready осталась Ignore (28-09)");

            var chip = hud.FindChild("BreadContainer", true, false) as Control;
            ok &= Expect(chip != null && chip.MouseFilter == Control.MouseFilterEnum.Ignore,
                "живая сцена: чип хлеба внутри панели ресурсов — Ignore");

            var construct = hud.FindChild("ButtonConstruct", true, false) as BaseButton;
            ok &= Expect(construct != null && construct.MouseFilter == Control.MouseFilterEnum.Stop,
                "живая сцена: кнопка стройки — Stop (не урезана, D-08)");
            var prioritet = hud.FindChild("PrioritetButton", true, false) as BaseButton;
            ok &= Expect(prioritet != null && prioritet.MouseFilter == Control.MouseFilterEnum.Stop,
                "живая сцена: кнопка приоритетов — Stop");
            var humidity = hud.FindChild("humidity", true, false) as BaseButton;
            ok &= Expect(humidity != null && humidity.MouseFilter == Control.MouseFilterEnum.Stop,
                "живая сцена: кнопка влажности — Stop");

            var population = hud.FindChild("Population", true, false) as Control;
            ok &= Expect(population != null && population.MouseFilter == Control.MouseFilterEnum.Ignore,
                "живая сцена: подпись населения мышь не ловит");

            var humidityTip = hud.FindChild("HumidityTooltip", true, false) as Control;
            var fertilityTip = hud.FindChild("FertilityTooltip", true, false) as Control;
            ok &= Expect(humidityTip != null && fertilityTip != null
                         && !ReferenceEquals(humidityTip, fertilityTip),
                "живая сцена: у влажности и плодородия РАЗНЫЕ всплывашки (28-06)");
            ok &= Expect(humidityTip != null && humidityTip.MouseFilter == Control.MouseFilterEnum.Ignore
                         && fertilityTip != null && fertilityTip.MouseFilter == Control.MouseFilterEnum.Ignore,
                "живая сцена: всплывашки мышь не ловят");
            return ok;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[HUD-MOUSE] FAIL: проверка живой сцены бросила {ex.GetType().Name}: {ex.Message}");
            return false;
        }
        finally
        {
            Drop(hud);
        }
    }

    // --- 28-06: два независимых слоя подсказки ---

    /// <summary>
    /// SelectTool.OnHover зовёт ShowHumidityAt, затем ShowFertilityAt — в одном кадре.
    /// Проверяем на настоящих Label: показ/гашение одного слоя не трогает другой.
    /// </summary>
    private bool TooltipLayersIndependent()
    {
        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree == null)
            return Skip("нет SceneTree — Label'ы подсказок не поднять");

        var holder = new Control { Name = "TooltipHolder" };
        var humidityLabel = new Label { Name = "HumidityTooltip", Visible = false };
        var fertilityLabel = new Label { Name = "FertilityTooltip", Visible = false };
        holder.AddChild(humidityLabel);
        holder.AddChild(fertilityLabel);
        // GlobalPosition выставляется только внутри дерева — кладём как в бою.
        tree.Root.AddChild(holder);

        try
        {
            var humidity = new HUDController.CellTooltipLayer(humidityLabel, MapMode.Humidity);
            var fertility = new HUDController.CellTooltipLayer(fertilityLabel, MapMode.Fertility);
            var mouse = new Vector2(100, 100);
            bool ok = true;

            ok &= Expect(humidity.Present(MapMode.Humidity, "42% норма", mouse),
                "влажность показывает цифру в своём режиме");
            ok &= Expect(humidity.Visible && humidity.Text == "42% норма",
                "подсказка влажности видима и текст верный");
            ok &= Expect(!fertility.Visible, "чужая подсказка не показана");

            // Ровно то, что делает SelectTool.OnHover вторым вызовом.
            ok &= Expect(!fertility.Present(MapMode.Humidity, "100 богато", mouse),
                "плодородие в режиме влажности не показывает свою цифру");
            ok &= Expect(!fertility.Visible, "слой плодородия погасил сам себя");
            ok &= Expect(humidity.Visible && humidity.Text == "42% норма",
                "28-06: влажность НЕ погашена вторым вызовом (было мигание подсказки)");

            // И обратный порядок режимов.
            ok &= Expect(fertility.Present(MapMode.Fertility, "100 богато", mouse),
                "плодородие показывает цифру в своём режиме");
            ok &= Expect(!humidity.Present(MapMode.Fertility, "42% норма", mouse),
                "влажность в режиме плодородия не показывает свою цифру");
            ok &= Expect(!humidity.Visible, "слой влажности погасил сам себя");
            ok &= Expect(fertility.Visible && fertility.Text == "100 богато",
                "28-06: плодородие НЕ погашено вторым вызовом");

            // Два слоя — два независимых состояния.
            humidity.Present(MapMode.Humidity, "7% сухо", mouse);
            ok &= Expect(humidity.Visible && fertility.Visible,
                "показ одного слоя не меняет видимость другого");
            fertility.Hide();
            ok &= Expect(humidity.Visible && !fertility.Visible, "Hide() гасит только свой слой");
            humidity.Hide();
            ok &= Expect(!humidity.Visible && !fertility.Visible, "Hide() второго слоя гасит второй");
            ok &= Expect(humidity.Mode == MapMode.Humidity && fertility.Mode == MapMode.Fertility,
                "у слоёв разные режимы карты");
            return ok;
        }
        finally
        {
            Drop(holder);
        }
    }

    /// <summary>
    /// Исходник: ShowFertilityAt/ShowHumidityAt и легенды трогают только свой слой, а
    /// общего тултипа (одно состояние на два режима) больше нет.
    /// </summary>
    private bool TooltipSourceWiring()
    {
        string src = Godot.FileAccess.GetFileAsString(HudPath);
        if (string.IsNullOrEmpty(src))
        {
            GD.PrintErr($"[HUD-MOUSE] FAIL: не прочитать {HudPath}");
            return false;
        }

        bool ok = true;
        ok &= Expect(!src.Contains("_humidityTooltip"),
            "28-06: общего тултипа больше нет (было два состояния в одном Label)");
        ok &= Expect(src.Contains("HumidityTooltip") && src.Contains("FertilityTooltip"),
            "28-06: у режимов две разные ноды подсказок");

        string fertBody = MethodBody(src, @"public void ShowFertilityAt\(int x, int y\)");
        ok &= Expect(fertBody.Contains("_fertilityTip"),
            "28-06: ShowFertilityAt работает со своим слоем");
        ok &= Expect(!fertBody.Contains("_humidityTip"),
            "28-06: ShowFertilityAt НЕ гасит слой влажности");

        string humBody = MethodBody(src, @"public void ShowHumidityAt\(int x, int y\)");
        ok &= Expect(humBody.Contains("_humidityTip"),
            "28-06: ShowHumidityAt работает со своим слоем");
        ok &= Expect(!humBody.Contains("_fertilityTip"),
            "28-06: ShowHumidityAt НЕ гасит слой плодородия");

        string humLegend = MethodBody(src, @"private void UpdateHumidityLegend\(bool visible\)");
        ok &= Expect(humLegend.Contains("_humidityTip") && !humLegend.Contains("_fertilityTip"),
            "28-06: легенда влажности гасит только влажность");
        string fertLegend = MethodBody(src, @"private void UpdateFertilityLegend\(bool visible\)");
        ok &= Expect(fertLegend.Contains("_fertilityTip") && !fertLegend.Contains("_humidityTip"),
            "28-06: легенда плодородия гасит только плодородие");
        return ok;
    }

    // --- 28-02: проба клетки ---

    /// <summary>
    /// Порядок как в бою: HUD создан и уже крутит _Process, менеджера ещё нет. Затем
    /// менеджер появляется (Main._Ready) — повторная попытка привязывает пробу сама.
    /// Дальше проверяем идемпотентность и Setup.
    /// </summary>
    private bool ProbeBindsAfterManagerCreated()
    {
        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree == null)
            return Skip("нет SceneTree — PlayerInteractionManager.Instance не создать");

        var hud = new HUDController { Name = "TestHudProbe" };
        PlayerInteractionManager pim = null;
        try
        {
            bool ok = true;

            // Менеджера ещё нет: привязываться не к чему, и это не ошибка.
            hud._Process(0.1);
            ok &= Expect(!ProbeBound(hud), "до создания менеджера проба не привязана");

            // Менеджер появляется позже HUD — ровно как в main.tscn.
            pim = new PlayerInteractionManager { Name = "TestHudProbePim" };
            tree.Root.AddChild(pim);
            if (!pim.IsNodeReady())
                pim._Ready();
            ok &= Expect(ReferenceEquals(PlayerInteractionManager.Instance, pim),
                "менеджер зарегистрировался в Instance");

            // Повторная попытка из _Process: первая же пауза связывает пробу.
            hud._Process(0.3);
            ok &= Expect(ProbeBound(hud), "28-02: повторная попытка из _Process привязала пробу");
            ok &= Expect(ReferenceEquals(ProbeField(pim), hud), "28-02: менеджер получил пробу HUD");
            ok &= Expect(ReferenceEquals(ToolProbeField(pim), hud),
                "28-02: проба уже в текущем дефолтном инструменте (SelectTool), а не только в поле");

            int handlersAfterBind = HandlerCount(pim);
            ok &= Expect(handlersAfterBind > 0, "менеджер подписан на сброс инструмента");
            ok &= Expect(hud.TryBindCellProbe(), "TryBindCellProbe() при существующем менеджере — true");
            ok &= Expect(hud.TryBindCellProbe(), "повторный вызов — снова true (идемпотентно)");
            ok &= Expect(HandlerCount(pim) == handlersAfterBind,
                $"28-02: повторная привязка не плодит подписки (было {handlersAfterBind}, стало {HandlerCount(pim)})");
            ok &= Expect(ReferenceEquals(ToolProbeField(pim), hud), "проба после повторов та же");

            // Setup — штатная точка подключения: Main зовёт его, когда менеджер уже есть.
            hud.Setup(pim, null, 100);
            ok &= Expect(ProbeBound(hud) && ReferenceEquals(ProbeField(pim), hud),
                "28-02: Setup отдаёт пробу менеджеру");
            ok &= Expect(HandlerCount(pim) == handlersAfterBind,
                $"Setup не дублирует подписки (обработчиков {HandlerCount(pim)})");
            return ok;
        }
        finally
        {
            Drop(hud);
            Drop(pim);
        }
    }

    /// <summary>Исходник: привязка не зависит от порядка — есть повтор и нет логов в кадре.</summary>
    private bool ProbeSourceWiring()
    {
        string src = Godot.FileAccess.GetFileAsString(HudPath);
        if (string.IsNullOrEmpty(src))
        {
            GD.PrintErr($"[HUD-MOUSE] FAIL: не прочитать {HudPath}");
            return false;
        }

        bool ok = true;

        string ready = MethodBody(src, @"public override void _Ready\(\)");
        ok &= Expect(ready.Contains("TryBindCellProbe()"),
            "28-02: _Ready пробует привязать пробу, а не только проверяет Instance");

        string setup = MethodBody(src, @"public void Setup\(PlayerInteractionManager");
        ok &= Expect(setup.Contains("AttachCellProbe"),
            "28-02: Setup отдаёт пробу (Main зовёт его после создания менеджера)");

        string process = MethodBody(src, @"public override void _Process\(double delta\)");
        ok &= Expect(process.Contains("TickCellProbeBinding"),
            "28-02: в _Process есть повторная попытка привязки");

        string tick = MethodBody(src, @"private void TickCellProbeBinding\(float delta\)");
        ok &= Expect(tick.Contains("TryBindCellProbe()"),
            "28-02: повторная попытка зовёт идемпотентную привязку");
        ok &= Expect(tick.Contains("ProbeRetrySeconds"),
            "28-02: повтор идёт по таймеру, а не каждый кадр");
        ok &= Expect(!tick.Contains("GD.Print"),
            "28-02: в повторной попытке нет GD.Print (горячий путь)");

        // Привязка разложена на два метода: TryBindCellProbe решает «кто менеджер»
        // (ReferenceEquals — не переподписываться на того же), AttachCellProbe отдаёт
        // саму ссылку (SetCellProbe). Поэтому SetCellProbe ищется в AttachCellProbe,
        // а не в TryBindCellProbe — раньше проверка требовала его не в том методе
        // и краснела на исправном коде.
        string bind = MethodBody(src, @"public bool TryBindCellProbe\(\)");
        ok &= Expect(bind.Contains("ReferenceEquals") && bind.Contains("AttachCellProbe"),
            "28-02: привязка идемпотентна (сравнение с уже привязанным менеджером)");

        string attach = MethodBody(src, @"private void AttachCellProbe\(PlayerInteractionManager");
        ok &= Expect(attach.Contains("SetCellProbe(this)"),
            "28-02: проба действительно отдаётся менеджеру — SetCellProbe(this)");
        ok &= Expect(attach.Contains("_cellProbeBound") && attach.Contains("ReferenceEquals"),
            "28-02: повторная привязка к тому же менеджеру — no-op, готовая связка не сбрасывается");
        ok &= Expect(!attach.Contains("OnToolReset"),
            "28-02: AttachCellProbe не трогает подписки — дублей обработчиков быть не может");
        return ok;
    }

    // --- утилиты ---

    /// <summary>Тело метода: от сигнатуры до первой строки «    }» на уровне класса.</summary>
    private static string MethodBody(string source, string signaturePattern)
    {
        Match match = Regex.Match(source, signaturePattern + @"[\s\S]*?\n    \}");
        return match.Success ? match.Value : string.Empty;
    }

    private static object PrivateField(object target, string name)
    {
        if (target == null) return null;
        FieldInfo field = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
        return field?.GetValue(target);
    }

    private static bool ProbeBound(HUDController hud)
        => PrivateField(hud, "_cellProbeBound") is bool bound && bound;

    private static object ProbeField(PlayerInteractionManager manager)
        => PrivateField(manager, "_cellProbe");

    private static object ToolProbeField(PlayerInteractionManager manager)
    {
        var tool = manager?.DefaultTool;
        return tool == null ? null : PrivateField(tool, "_probe");
    }

    /// <summary>Сколько обработчиков на OnToolReset (проверка «подписки не плодятся»).</summary>
    private static int HandlerCount(PlayerInteractionManager manager)
        => PrivateField(manager, "OnToolReset") is Delegate handlers
            ? handlers.GetInvocationList().Length
            : 0;

    private static bool Expect(bool condition, string message)
    {
        if (condition)
        {
            GD.Print($"[HUD-MOUSE] OK: {message}");
            return true;
        }
        GD.PrintErr($"[HUD-MOUSE] FAIL: {message}");
        return false;
    }

    private static bool Skip(string message)
    {
        GD.Print($"[HUD-MOUSE] SKIP: {message}");
        return true;
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