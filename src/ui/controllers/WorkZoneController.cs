using Godot;
using Game.Core;
using Game.Simulation;
using System;
using System.Collections.Generic;

namespace Game.UI;

public partial class WorkZoneController : Control, IZoneWindow, IFarmJobDisplayHost
{
    public static WorkZoneController Instance { get; private set; }

    private Control _rootPanel;
    private Label _titleLabel;
    private Label _infoLabel;
    private BaseButton _infoButton;
    private BaseButton _escapeButton;

    private HSlider _tilesSlider;
    private Label _tilesNumber;
    private HSlider _workerSlider;
    private Label _workerNumber;
    private HSlider _prioritySlider;
    private Label _priorityNumber;
    private BaseButton _farmBackButton;
    private BaseButton _enabledToggle;
    private BaseButton _removeZoneButton;

    /// <summary>
    /// §119/P6: тумблер «вкл/выкл» зоны. Поле Enabled в WorkZone появится в P0 —
    /// до слияния флаг держим локально: «выкл» запрещает этому окну толкать
    /// изменения слайдеров в зону (и так показывает состояние в Information).
    /// </summary>
    private bool _zoneEnabled = true;

    /// <summary>
    /// Id фермы, с которой перешли в JobDisplay. −1 = пришли не с фермы,
    /// кнопка ButtonForFarm скрыта (§20.6).
    /// </summary>
    public static int CameFromFarmZoneId = -1;

    /// <summary>Открыт ли JobDisplay сейчас для фермы (а не для Work-зоны).</summary>
    private bool _farmMode;

    private bool _updating;
    private long _lastTilesMs;
    private int _pendingTiles = -1;
    /// <summary>FIX (bughunt 15-8): id зоны для отложенного радиуса — к моменту
    /// слива выбор уже может быть снят (крестик), а значение терять нельзя.</summary>
    private int _pendingTilesZoneId = -1;
    private string _lastInfo = "";
    private double _infoTimer;

    private const long TilesThrottleMs = 150;

    // UI-14: двухэтапное подтверждение сноса зоны
    private bool _awaitingRemoveConfirmation;
    private long _removeConfirmExpireMs;
    private const long RemoveConfirmWindowMs = 3500;

    public event Action OnOpened;
    public event Action OnClosed;

    public bool IsOpen => _rootPanel != null && _rootPanel.Visible;

    public override void _Ready()
    {
        Instance = this;
        ZoneWindowRegistry.WorkWindow = this;
        ZoneWindowRegistry.FarmJobDisplay = this;
        ZIndex = 20;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        LoadScene();

        WorkZoneManager.Instance.OnZoneSelected += OnZoneSelectedFacade;
        WorkZoneManager.Instance.OnZoneDeselected += Close;
        WorkZoneManager.Instance.OnZonesUpdated += RefreshInfo;
    }

    // Единый ZoneManager шлёт WorkZone через фасад; фильтр Kind — страховка:
    // фасад уже отдаёт только Work, но если прилетит Farm/null — игнорим,
    // окно открывает только FarmController.
    private void OnZoneSelectedFacade(WorkZone zone)
    {
        if (zone == null) return;
        OnZoneSelected(zone);
    }

    private void LoadScene()
    {
        var paths = new[]
        {
            "res://scenes/ui/JobDisplay.tscn",
            "res://JobDisplay.tscn",
            "res://ui/JobDisplay.tscn"
        };

        foreach (var path in paths)
        {
            if (ResourceLoader.Exists(path))
            {
                var packed = ResourceLoader.Load<PackedScene>(path);
                if (packed != null)
                {
                    _rootPanel = packed.Instantiate<Control>();
                    AddChild(_rootPanel);
                    _rootPanel.Visible = false;


                    ConfigureInputBlocking(_rootPanel);

                    _titleLabel = _rootPanel.FindChild("NameWork", true, false) as Label;
                    _infoLabel = _rootPanel.FindChild("Information", true, false) as Label
                              ?? _rootPanel.FindChild("Infoormation", true, false) as Label;
                    _infoButton = _rootPanel.FindChild("Information", true, false) as BaseButton
                               ?? _rootPanel.FindChild("Infoormation", true, false) as BaseButton;

                    var tilesRow = _rootPanel.FindChild("Tiles", true, false) as Control;
                    var workerRow = _rootPanel.FindChild("Worker", true, false) as Control;
                    var priorityRow = _rootPanel.FindChild("Prioritet", true, false) as Control;

                    if (tilesRow != null)
                    {
                        _tilesSlider = tilesRow.FindChild("HSlider", true, false) as HSlider;
                        _tilesNumber = tilesRow.FindChild("Number", true, false) as Label;
                        if (_tilesSlider != null)
                        {
                            // §119/P6: Tiles — это РАДИУС зоны (64..512), не площадь.
                            _tilesSlider.MinValue = 64;
                            _tilesSlider.MaxValue = 512;
                            _tilesSlider.Step = 1;
                            _tilesSlider.ValueChanged += OnTilesSliderChanged;
                        }
                    }
                    if (workerRow != null)
                    {
                        _workerSlider = workerRow.FindChild("HSlider", true, false) as HSlider;
                        _workerNumber = workerRow.FindChild("Number", true, false) as Label;
                        if (_workerSlider != null)
                        {
                            // §119/P6: Worker — желаемое число рабочих 0..999 (потолка нет).
                            _workerSlider.MinValue = 0;
                            _workerSlider.MaxValue = 999;
                            _workerSlider.Step = 1;
                            _workerSlider.ValueChanged += OnWorkerSliderChanged;
                        }
                    }
                    if (priorityRow != null)
                    {
                        _prioritySlider = priorityRow.FindChild("HSlider", true, false) as HSlider;
                        _priorityNumber = priorityRow.FindChild("Number", true, false) as Label;
                        if (_prioritySlider != null)
                        {
                            _prioritySlider.MinValue = 1;
                            _prioritySlider.MaxValue = 10;
                            _prioritySlider.Step = 1;
                            _prioritySlider.ValueChanged += OnPrioritySliderChanged;
                        }
                    }

                    // В JobDisplay.tscn крестик окна называется "esc" (как в Farm.tscn) —
                    // без этой ветки FindChild не находил кнопку и окно Work-зоны
                    // нельзя было закрыть её крестиком.
                    _escapeButton = _rootPanel.FindChild("esc", true, false) as BaseButton
                                    ?? _rootPanel.FindChild("Escape", true, false) as BaseButton
                                    ?? _rootPanel.FindChild("Close", true, false) as BaseButton
                                    ?? _rootPanel.FindChild("Exit", true, false) as BaseButton
                                    ?? _rootPanel.FindChild("Back", true, false) as BaseButton;

                    if (_escapeButton != null)
                    {
                        _escapeButton.Pressed += OnCloseButtonPressed;
                    }

                    _farmBackButton = _rootPanel.FindChild("ButtonForFarm", true, false) as BaseButton;
                    if (_farmBackButton != null)
                    {
                        _farmBackButton.Visible = false;
                        _farmBackButton.Pressed += OnFarmBackPressed;
                    }

                    // §119/P6: тумблер «вкл/выкл» зоны + кнопка «Снести зону».
                    // Поле Enabled у зоны появится в P0 — тумблер пока локальный.
                    _enabledToggle = _rootPanel.FindChild("EnabledToggle", true, false) as BaseButton;
                    if (_enabledToggle != null)
                        _enabledToggle.Toggled += OnEnabledToggled;

                    _removeZoneButton = _rootPanel.FindChild("RemoveZone", true, false) as BaseButton;
                    if (_removeZoneButton != null)
                        _removeZoneButton.Pressed += OnRemoveZonePressed;
                    break;
                }
            }
        }
    }

    private static void ConfigureInputBlocking(Control node)
    {
        if (node == null) return;

        if (node is Panel || node is PanelContainer || node is ScrollContainer || node is ItemList)
        {
            node.MouseFilter = MouseFilterEnum.Stop;
        }

        foreach (Node child in node.GetChildren())
        {
            if (child is Control childCtrl)
            {
                ConfigureInputBlocking(childCtrl);
            }
        }
    }

    /// <summary>
    /// Крестик/esc JobDisplay. FIX (bughunt 15-2): окно одно и для Work-, и для
    /// Farm-зон, а Work-фасад снимает выбор только у Kind==Work. В режиме фермы
    /// выбрана Farm-зона — гасим её через FarmZoneManager, иначе крестик был
    /// no-op и окно не закрывалось (OnZoneDeselected не выстреливал).
    /// </summary>
    private void OnCloseButtonPressed()
    {
        if (_farmMode)
            FarmZoneManager.Instance.DeselectZone();
        else
            WorkZoneManager.Instance.DeselectZone();
    }

    /// <summary>
    /// §119/P6: провести радиус в зону. P0 слился — фасад отдаёт SetRadius
    /// (ZoneManager.SetWorkRadius, кламп 64..512 внутри). ПЛОЩАДЬ больше не
    /// проводим: слайдер теперь радиус.
    /// </summary>
    private void ApplyRadiusToZone(int zoneId, int radiusTiles)
    {
        WorkZoneManager.Instance.SetRadius(zoneId, radiusTiles);
    }

    private void OnTilesSliderChanged(double value)
    {
        if (_updating || !_zoneEnabled) return;
        var zone = WorkZoneManager.Instance.SelectedZone;
        if (zone == null) return;
        int v = (int)value;
        if (_tilesNumber != null) _tilesNumber.Text = v.ToString();
        long now = System.Environment.TickCount64;
        if (now - _lastTilesMs < TilesThrottleMs)
        {
            _pendingTiles = v;
            // FIX (bughunt 15-8): запоминаем зону — к моменту слива выбор может быть снят.
            _pendingTilesZoneId = zone.Id;
            return;
        }
        _lastTilesMs = now;
        _pendingTiles = -1;
        _pendingTilesZoneId = -1;
        ApplyRadiusToZone(zone.Id, v);
    }

    /// <summary>
    /// FIX (bughunt 15-8): применить отложенный троттлингом радиус, не дожидаясь
    /// следующего _Process. Вызывается при закрытии окна/смене зоны — иначе
    /// последний сдвиг слайдера (попавший в 150-мс окно) терялся.
    /// </summary>
    private void FlushPendingTiles()
    {
        if (_pendingTiles < 0)
            return;
        int value = _pendingTiles;
        int zoneId = _pendingTilesZoneId;
        _pendingTiles = -1;
        _pendingTilesZoneId = -1;
        // Выключенная тумблером зона изменения слайдера не принимает (см. OnEnabledToggled).
        if (!_zoneEnabled || _farmMode)
            return;
        if (zoneId < 0)
        {
            var zone = WorkZoneManager.Instance.SelectedZone;
            if (zone == null)
                return;
            zoneId = zone.Id;
        }
        _lastTilesMs = System.Environment.TickCount64;
        ApplyRadiusToZone(zoneId, value);
    }

    private void OnWorkerSliderChanged(double value)
    {
        if (_updating || !_zoneEnabled) return;
        var zone = WorkZoneManager.Instance.SelectedZone;
        if (zone == null) return;
        int v = (int)value;
        // §119/P6: подпись Worker = «назначено/желаемое».
        int assigned = System.Threading.Volatile.Read(ref zone.AssignedCount);
        if (_workerNumber != null) _workerNumber.Text = $"{assigned}/{v}";
        WorkZoneManager.Instance.SetDesiredWorkers(zone.Id, v);
    }

    private void OnPrioritySliderChanged(double value)
    {
        if (_updating) return;
        int v = (int)value;
        if (_priorityNumber != null) _priorityNumber.Text = v.ToString();
        if (_farmMode)
        {
            // Режим фермы: слайдера Work-зоны нет — пишем прямо в Farming.
            JobPriorityManager.Instance.SetPriority(JobCategory.Farming, v);
            return;
        }
        var zone = WorkZoneManager.Instance.SelectedZone;
        if (zone == null) return;
        WorkZoneManager.Instance.SetPriorityOverride(zone.Id, v);
    }

    /// <summary>§119/P6: тумблер «вкл/выкл». Поле Enabled у зоны есть (P0), но
    /// сеттера через фасад ещё нет (SetWorkEnabled появится позже) — пока «выкл»
    /// блокирует pushes слайдеров из этого окна; TODO: push Enabled через фасад.</summary>
    private void OnEnabledToggled(bool pressed)
    {
        // FIX (bughunt 15-8): отложенный радиус применяем ДО смены режима, а не
        // выбрасываем — иначе последнее значение слайдера пропадало.
        FlushPendingTiles();
        ResetRemoveConfirmation();
        _zoneEnabled = pressed;
        GD.Print($"[JobDisplay] зона {(pressed ? "включена" : "выключена")} (push Enabled ждёт SetEnabled в фасаде).");
        RefreshInfo();
    }

    /// <summary>§119/P6: кнопка «Снести зону» → WorkZoneManagerFacade.RemoveZone(id).</summary>
    private void ResetRemoveConfirmation()
    {
        _awaitingRemoveConfirmation = false;
        _removeConfirmExpireMs = 0;
        if (_removeZoneButton is Button btn)
        {
            btn.Text = "Снести зону";
            btn.Modulate = Colors.White;
        }
    }

    private void OnRemoveZonePressed()
    {
        var zone = WorkZoneManager.Instance.SelectedZone;
        if (zone == null) return;
        long now = System.Environment.TickCount64;
        if (!_awaitingRemoveConfirmation || now > _removeConfirmExpireMs)
        {
            _awaitingRemoveConfirmation = true;
            _removeConfirmExpireMs = now + RemoveConfirmWindowMs;
            if (_removeZoneButton is Button btn)
            {
                btn.Text = "Подтвердите снос!";
                btn.Modulate = new Color(1.0f, 0.4f, 0.4f);
            }
            return;
        }

        ResetRemoveConfirmation();

        int id = zone.Id;
        bool removed = WorkZoneManager.Instance.RemoveZone(id);
        GD.Print($"[JobDisplay] Снести зону {id}: {(removed ? "удалена" : "менеджер отказал")}");
        // DeselectZone идемпотентен: если RemoveZone уже сняла выбор — ничего не сделает.
        WorkZoneManager.Instance.DeselectZone();
    }

    /// <summary>
    /// Открыть JobDisplay для Farm-зоны (§20.6): название + число клеток
    /// (только чтение), рабочих «∞», приоритет Farming — рабочий.
    /// Кнопка ButtonForFarm видна (назад — в ферму).
    /// </summary>
    public void ShowForFarmZone(int farmZoneId)
    {
        if (_rootPanel == null) return;
        if (!FarmZoneManager.Instance.TryGetZoneById(farmZoneId, out var farm) || farm == null)
            return;

        // Закрываем окно фермы — окна взаимоисключающие (как Farm/Work).
        var farmCtrl = ZoneWindowRegistry.FarmWindow;
        if (farmCtrl != null && farmCtrl.IsOpen)
            farmCtrl.Close();

        // FIX (bughunt 15-8): сменили режим — отложенный радиус прошлой Work-зоны
        // досылаем сейчас, пока _farmMode ещё false (иначе он бы потерялся).
        FlushPendingTiles();
        ResetRemoveConfirmation();

        CameFromFarmZoneId = farmZoneId;
        _farmMode = true;

        _updating = true;
        try
        {
            if (_titleLabel != null)
                _titleLabel.Text = farm.Name;
            // Тайлы фермы менять через работы нельзя — только чтение.
            if (_tilesSlider != null) _tilesSlider.Editable = false;
            if (_tilesNumber != null) _tilesNumber.Text = $"{farm.TotalTiles} кл.";
            // Лимита рабочих у фермы нет — «∞».
            if (_workerSlider != null) _workerSlider.Editable = false;
            if (_workerNumber != null) _workerNumber.Text = "∞";
            int prio = JobPriorityManager.Instance.GetPriority(JobCategory.Farming);
            if (_prioritySlider != null) _prioritySlider.Value = prio;
            if (_priorityNumber != null) _priorityNumber.Text = prio.ToString();
            _pendingTiles = -1;
            _pendingTilesZoneId = -1;
            _lastInfo = "";
        }
        finally
        {
            _updating = false;
        }

        if (_farmBackButton != null)
            _farmBackButton.Visible = true;
        // §119/P6: в режиме фермы тумблер/снос Work-зоны не по делу — прячем.
        if (_enabledToggle != null) _enabledToggle.Visible = false;
        if (_removeZoneButton != null) _removeZoneButton.Visible = false;

        _rootPanel.Visible = true;
        OnOpened?.Invoke();
        RefreshInfo();
    }

    /// <summary>Кнопка ButtonForFarm — назад в окно фермы (§20.6).</summary>
    private void OnFarmBackPressed()
    {
        int farmId = CameFromFarmZoneId;
        CameFromFarmZoneId = -1;
        _farmMode = false;
        if (_farmBackButton != null)
            _farmBackButton.Visible = false;
        // Разблокировать слайдеры для обычных Work-зон.
        if (_tilesSlider != null) _tilesSlider.Editable = true;
        if (_workerSlider != null) _workerSlider.Editable = true;
        // §119/P6: вернуть тумблер/снос для Work-зон (прятались в режиме фермы).
        if (_enabledToggle != null) _enabledToggle.Visible = true;
        if (_removeZoneButton != null) _removeZoneButton.Visible = true;
        Close();
        if (farmId < 0)
            return;
        // Переоткрыть окно фермы: найти любую клетку зоны и выбрать её.
        if (FarmZoneManager.Instance.TryGetZoneById(farmId, out var farm) && farm != null)
        {
            foreach (var (x, y) in farm.Tiles)
            {
                ZoneManager.Instance.SelectZoneAt(x, y);
                break;
            }
        }
    }

    private void OnZoneSelected(WorkZone zone)
    {
        if (_rootPanel == null || zone == null) return;

        // FIX (bughunt 15-8): не теряем отложенный радиус ПРЕДЫДУЩЕЙ зоны при
        // переключении выбора — досылаем его старому id до сброса состояния.
        FlushPendingTiles();
        ResetRemoveConfirmation();

        // Обычный выбор Work-зоны — режим фермы сбрасываем, кнопка назад скрыта.
        _farmMode = false;
        CameFromFarmZoneId = -1;
        if (_farmBackButton != null)
            _farmBackButton.Visible = false;
        if (_tilesSlider != null) _tilesSlider.Editable = true;
        if (_workerSlider != null) _workerSlider.Editable = true;
        // §119/P6: Work-зона — тумблер/снос видимы, тумблер синхронизируем с зоной
        // (Enabled пришёл с P0; push его менять пока не может — сеттера в фасаде нет).
        if (_enabledToggle != null)
        {
            _enabledToggle.Visible = true;
            _updating = true;
            _enabledToggle.SetPressedNoSignal(zone.Enabled);
            _updating = false;
        }
        if (_removeZoneButton != null) _removeZoneButton.Visible = true;
        _zoneEnabled = zone.Enabled;

        // Единый выбор уже в ZoneManager: чужой выбор (Farm) сюда не приходит
        // (фасад отдаёт только Work). Закрываем окно фермы если открыто.
        var garden = ZoneWindowRegistry.FarmWindow;
        if (garden != null && garden.IsOpen)
            garden.Close();

        _updating = true;
        try
        {
            if (_titleLabel != null)
                _titleLabel.Text = zone.Name;
            // §119/P6: слайдер Tiles = РАДИУС (TilesTarget — легаси-алиас радиуса с P0).
            if (_tilesSlider != null) _tilesSlider.Value = zone.RadiusTiles;
            if (_tilesNumber != null) _tilesNumber.Text = zone.RadiusTiles.ToString();
            // §119/P6: подпись Worker = «назначено/желаемое» (MaxWorkers — легаси DesiredWorkers).
            int assignedNow = System.Threading.Volatile.Read(ref zone.AssignedCount);
            if (_workerSlider != null) _workerSlider.Value = zone.DesiredWorkers;
            if (_workerNumber != null) _workerNumber.Text = $"{assignedNow}/{zone.DesiredWorkers}";
            // T4: override ?? живой глобальный (мин по покрытым категориям).
            int prio = zone.PriorityOverride ?? GetLiveMinPriority(zone);
            if (_prioritySlider != null) _prioritySlider.Value = prio;
            if (_priorityNumber != null) _priorityNumber.Text = prio.ToString();
            _pendingTiles = -1;
            _pendingTilesZoneId = -1;
            _lastTilesMs = System.Environment.TickCount64;
            _lastInfo = "";
        }
        finally
        {
            _updating = false;
        }

        _rootPanel.Visible = true;
        OnOpened?.Invoke();
        RefreshInfo();
    }

    private static int GetLiveMinPriority(WorkZone zone)
    {
        int min = int.MaxValue;
        for (int t = 1; t <= (int)JobTypeId.Production; t++)
        {
            if (!zone.Matches((JobTypeId)t)) continue;
            int p = JobPriorityManager.Instance.GetPriorityForJobType((JobTypeId)t);
            if (p < min) min = p;
        }
        return min == int.MaxValue ? 1 : min; // маска пуста → 1
    }

    private static Dictionary<JobCategory, int> CollectCategoryGlobals(WorkZone zone)
    {
        var dict = new Dictionary<JobCategory, int>();
        for (int t = 1; t <= (int)JobTypeId.Production; t++)
        {
            var typeId = (JobTypeId)t;
            if (!zone.Matches(typeId)) continue;
            var cat = JobPriorityManager.Instance.GetCategory(typeId);
            if (!dict.ContainsKey(cat))
                dict[cat] = JobPriorityManager.Instance.GetPriority(cat);
        }
        return dict;
    }

    private void RefreshInfo()
    {
        if (_infoLabel == null && _infoButton == null) return;
        // Режим фермы: показываем данные фермы (клетки + приоритет Farming).
        if (_farmMode && CameFromFarmZoneId >= 0)
        {
            if (!FarmZoneManager.Instance.TryGetZoneById(CameFromFarmZoneId, out var farm) || farm == null)
                return;
            int prio = JobPriorityManager.Instance.GetPriority(JobCategory.Farming);
            string farmText = $"Ферма: {farm.TotalTiles} кл. · приоритет Farming={prio}";
            if (farmText != _lastInfo)
            {
                _lastInfo = farmText;
                if (_infoLabel != null) _infoLabel.Text = farmText;
                if (_infoButton != null) _infoButton.TooltipText = farmText;
            }
            return;
        }
        var zone = WorkZoneManager.Instance.SelectedZone;
        if (zone == null) return;
        // §119/P6: состав Information — очередь зоны, покрытие, назначено/желаемо,
        // работы «вне зон». Очередь и «вне зон» через фасад ещё не отдаются —
        // прочерки с пометкой; очередь показываем прокси «работы в чанках зоны».
        int total = 0;
        var chunks = zone.ChunkIndices;
        if (chunks != null)
        {
            foreach (int c in chunks)
                total += JobDispatcher.Instance.JobIndex.GetChunkJobCount(c);
        }
        // T8: "Назначено {perPass} · Работ в чанках {total} · лимит {N/∞}".
        int perPass = System.Threading.Volatile.Read(ref zone.AssignedCount);
        int desired = zone.DesiredWorkers;
        // FIX (bughunt 15-4): подпись Worker «назначено/желаемое» и строка Information
        // берутся из ОДНОГО расчёта. Раньше _workerNumber писался только при движении
        // слайдера, и в окне одновременно жили «0/12» и «Назначено 12/12».
        if (_workerNumber != null)
        {
            string workerText = $"{perPass}/{desired}";
            if (_workerNumber.Text != workerText)
                _workerNumber.Text = workerText;
        }
        // §119 P0: покрытие зоны — вычисляемая площадь πr² (клетки не материализуются).
        long coverage = zone.CellCount;
        // T4/T5: строка конфликта — покрытые категории + их текущие глобальные значения.
        var globals = CollectCategoryGlobals(zone);
        var parts = new List<string>();
        foreach (var kv in globals)
            parts.Add($"{kv.Key}={kv.Value}");
        string conflict = parts.Count > 0 ? string.Join(", ", parts) : "—";
        string state = _zoneEnabled ? "вкл" : "выкл";
        // task-13: в окне зоны видно ОТРАСЛЬ и её объекты (зона = отрасль).
        var industryBuilds = ZoneIndustryRegistry.Get(zone.Kind).Buildings;
        var objectNames = new List<string>(industryBuilds.Count);
        for (int bi = 0; bi < industryBuilds.Count; bi++)
            objectNames.Add(StructureRegistry.Get(industryBuilds[bi]).DisplayName);
        string objectList = objectNames.Count > 0 ? string.Join(", ", objectNames) : "—";
        // task-20: что производит станок этой отрасли (рецепт = «2× руда → 1× слиток»).
        var productionParts = new List<string>();
        for (int bi = 0; bi < industryBuilds.Count; bi++)
        {
            var recipe = RecipeRegistry.GetForStation(industryBuilds[bi]);
            if (recipe == null) continue;
            productionParts.Add($"{StructureRegistry.Get(industryBuilds[bi]).DisplayName}: {recipe.Summary}");
        }
        string productionLine = productionParts.Count > 0
            ? "\nПроизводство: " + string.Join(" · ", productionParts)
            : string.Empty;
        string text =
            $"Отрасль: {zone.IndustryTitle} · Объекты: {objectList}\n" +
            $"Очередь зоны (чанки): {total} · Покрытие: {coverage} кл. (r={zone.RadiusTiles}) · Назначено {perPass}/{desired} · " +
            $"Вне зон: — (нет данных, ждёт P1) · Состояние: {state}\ngлобально: {conflict}" + productionLine;
        if (text != _lastInfo)
        {
            _lastInfo = text;
            if (_infoLabel != null) _infoLabel.Text = text;
            if (_infoButton != null) _infoButton.TooltipText = text;
        }
    }

    public override void _Process(double delta)
    {
        if (_rootPanel == null || !_rootPanel.Visible) return;
        if (_awaitingRemoveConfirmation && System.Environment.TickCount64 > _removeConfirmExpireMs)
            ResetRemoveConfirmation();
        if (_farmMode)
        {
            // Режим фермы: слайдеры заблокированы, тикает только инфо-строка.
            _infoTimer += delta;
            if (_infoTimer >= 1.0)
            {
                _infoTimer = 0.0;
                RefreshInfo();
            }
            return;
        }
        var zone = WorkZoneManager.Instance.SelectedZone;
        if (zone == null) return;
        if (_zoneEnabled && _pendingTiles >= 0 && System.Environment.TickCount64 - _lastTilesMs >= TilesThrottleMs)
        {
            // FIX (bughunt 15-8): слив через общий помощник (ту же логику зовёт Close).
            FlushPendingTiles();
        ResetRemoveConfirmation();
        }
        _infoTimer += delta;
        if (_infoTimer >= 1.0)
        {
            _infoTimer = 0.0;
            RefreshInfo();
        }
    }

    public void Close()
    {
        // FIX (bughunt 15-8): досылаем отложенный троттлингом радиус ДО скрытия —
        // после закрытия _Process ранний выход, и последний сдвиг слайдера терялся.
        FlushPendingTiles();
        ResetRemoveConfirmation();
        if (_rootPanel != null && _rootPanel.Visible)
        {
            _rootPanel.Visible = false;
            OnClosed?.Invoke();
        }
    }

    public override void _ExitTree()
    {
        WorkZoneManager.Instance.OnZoneSelected -= OnZoneSelectedFacade;
        WorkZoneManager.Instance.OnZoneDeselected -= Close;
        WorkZoneManager.Instance.OnZonesUpdated -= RefreshInfo;
        // Симметрично BuildMenuController/HUDController: статика не должна удерживать
        // освобождённый Godot-объект, иначе Instance отдаёт disposed-ноду после выгрузки UI.
        if (ReferenceEquals(ZoneWindowRegistry.WorkWindow, this))
            ZoneWindowRegistry.WorkWindow = null;
        if (ReferenceEquals(ZoneWindowRegistry.FarmJobDisplay, this))
            ZoneWindowRegistry.FarmJobDisplay = null;
        if (ReferenceEquals(Instance, this))
            Instance = null;
        base._ExitTree();
    }
}
