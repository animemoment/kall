using Godot;
using Game.Core;
using Game.Simulation;
using System;
using System.Collections.Generic;

namespace Game.UI;

/// <summary>
/// Окно фермы (Farm.tscn). Открывается при клике по клетке фермы.
/// Показывает название («Ферма #5»), число клеток, сетку выбора культуры
/// (18 культур: 14 «Еда» + 4 «Волокно»), галочку автопосадки.
/// Выбранная культура пишется в Zone.Crop текущей зоны (одна на зону) и
/// применяется ко всем новым посадкам. Кнопка ButtonForJobDisplay ведёт в
/// JobDisplay (§20.6), кнопка Infoormation — тумблер подсказки про клетку (§20.5).
/// Замена старому GardenController — Garden.tscn удалён.
/// </summary>
public partial class FarmController : Control, IZoneWindow
{
    public static FarmController Instance { get; private set; }

    private Control _rootPanel;
    private Label _titleLabel;
    private Label _tilesLabel;
    private Label _resourcesLabel;
    private BaseButton _escapeButton;
    private BaseButton _checkMarkButton;
    private BaseButton _infoButton;
    private BaseButton _jobDisplayButton;

    // --- Сетка выбора культуры (task-11) ---
    // Каждая клетка — Button 88x96: иконка 64x64 (NEAREST) + подпись 12px.
    // Кнопки создаются ОДИН раз из CropRegistry при загрузке сцены.
    private const int CropCellWidth = 90;
    private const int CropCellHeight = 96;
    private const int CropIconSize = 64;

    private GridContainer _foodGrid;
    private GridContainer _fiberGrid;
    private Label _foodHeader;
    private Label _fiberHeader;
    private Label _selectedCropName;
    private Label _selectedCropDetails;
    private readonly Dictionary<CropType, Button> _cropButtons = new();
    private bool _cropGridBuilt;
    private StyleBoxFlat _cellNormal;
    private StyleBoxFlat _cellHover;
    private StyleBoxFlat _cellPressed;
    private StyleBoxFlat _cellSelected;
    private StyleBoxFlat _cellSelectedHover;
    private StyleBoxFlat _cellDisabled;

    /// <summary>Алиасы файлов иконок: id культуры → имя файла арта (без _s4.png).</summary>
    private static readonly Dictionary<string, string> CropIconAliases = new(StringComparer.Ordinal)
    {
        // В арте пенька лежит как hemp_s4.png, а id культуры — hemp_fiber.
        ["hemp_fiber"] = "hemp",
    };

    // Всплывашка про клетку (§20.5) и тумблер кнопки Infoormation (шаг 4).
    private Label _cellTooltip;
    private bool _infoMode;
    private int _lastTipX = int.MinValue;
    private int _lastTipY = int.MinValue;
    private string _lastTipText = "";

    // --- FIX (bughunt 15-5): кэш адаптированной Farm-зоны ---
    // FarmZoneManager.SelectedZone на КАЖДОЕ чтение создаёт новый FarmZone, а его
    // конструктор копирует ВСЕ клетки зоны в новый HashSet. В InfoMode это
    // происходило каждый кадр — на большой ферме микрофризы и GC. Держим один
    // адаптированный объект и пересобираем только при смене зоны/числа клеток.
    private FarmZone _cachedFarmZone;
    private int _cachedFarmZoneId = -1;
    private bool _farmZoneDirty = true;

    /// <summary>Выбранная ферма из кэша (пересборка — только при реальном изменении).</summary>
    private FarmZone GetSelectedFarmZone()
    {
        var live = ZoneManager.Instance.SelectedZone;
        if (live == null || live.Kind != ZoneKind.Farm)
        {
            _cachedFarmZone = null;
            _cachedFarmZoneId = -1;
            return null;
        }

        bool stale = _farmZoneDirty
                     || _cachedFarmZone == null
                     || _cachedFarmZoneId != live.Id
                     || _cachedFarmZone.TotalTiles != live.TotalTiles;
        if (!stale)
            return _cachedFarmZone;

        if (!FarmZoneManager.Instance.TryGetZoneById(live.Id, out var adapted) || adapted == null)
        {
            _cachedFarmZone = null;
            _cachedFarmZoneId = -1;
            return null;
        }

        _cachedFarmZone = adapted;
        _cachedFarmZoneId = adapted.Id;
        _farmZoneDirty = false;
        return _cachedFarmZone;
    }

    public event Action OnOpened;
    public event Action OnClosed;

    public bool IsOpen => _rootPanel != null && _rootPanel.Visible;

    /// <summary>Включён ли режим подсказок про клетки (кнопка Infoormation).</summary>
    public bool InfoMode => _infoMode;

    public override void _Ready()
    {
        Instance = this;
        ZoneWindowRegistry.FarmWindow = this;
        ZIndex = 20;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        LoadScene();

        FarmZoneManager.Instance.OnZoneSelected += OnZoneSelected;
        FarmZoneManager.Instance.OnZoneDeselected += Close;
        // FIX (bughunt 15-5): состав зоны мог измениться (тайлы добавили/убрали) —
        // помечаем кэш адаптированной зоны невалидным, чтобы не держать старые клетки.
        FarmZoneManager.Instance.OnZonesUpdated += OnFarmZonesUpdated;
    }

    /// <summary>FIX (bughunt 15-5): зоны изменились — кэш FarmZone невалиден.</summary>
    private void OnFarmZonesUpdated()
    {
        _farmZoneDirty = true;
    }

    private void LoadScene()
    {
        var paths = new[]
        {
            "res://scenes/ui/Farm.tscn",
            "res://Farm.tscn",
            "res://ui/Farm.tscn",
        };

        foreach (var path in paths)
        {
            if (!ResourceLoader.Exists(path))
                continue;
            var packed = ResourceLoader.Load<PackedScene>(path);
            if (packed == null)
                continue;
            _rootPanel = packed.Instantiate<Control>();
            AddChild(_rootPanel);
            _rootPanel.Visible = false;


            ConfigureInputBlocking(_rootPanel);
            BindNodes(_rootPanel);

            if (_escapeButton != null)
                _escapeButton.Pressed += () => FarmZoneManager.Instance.DeselectZone();
            if (_checkMarkButton != null)
                _checkMarkButton.Pressed += OnCheckMarkPressed;
            if (_infoButton != null)
                _infoButton.Pressed += OnInfoButtonPressed;
            if (_jobDisplayButton != null)
                _jobDisplayButton.Pressed += OnJobDisplayPressed;

            BuildCellTooltip();
            BuildCropGrid();
            break;
        }

        if (_rootPanel == null)
            GD.PushWarning("[Farm] Farm.tscn не найден ни по одному пути.");
    }

    /// <summary>
    /// Привязка нод Farm.tscn. Внимание: имя «Label» встречается дважды
    /// (заголовок — прямой ребёнок корня, «N клеток» — внутри ScrollContainer),
    /// поэтому заголовок и кнопки берём среди ПРЯМЫХ детей корня, а счётчик
    /// клеток — внутри ScrollContainer. Рекурсивный FindChild здесь врёт.
    /// </summary>
    private void BindNodes(Control root)
    {
        // Прямые дети корня: заголовок + кнопки.
        foreach (Node child in root.GetChildren())
        {
            if (child is not Control ctrl)
                continue;
            switch (ctrl.Name)
            {
                case "Label" when _titleLabel == null && child.GetParent() == root:
                    _titleLabel = ctrl as Label;
                    break;
                case "CheckMark" when _checkMarkButton == null && child.GetParent() == root:
                    _checkMarkButton = ctrl as BaseButton;
                    break;
                case "esc" when _escapeButton == null:
                    _escapeButton = ctrl as BaseButton;
                    break;
                case "Infoormation" when _infoButton == null:
                    // В сцене опечатка с двумя «o» — ищем именно так.
                    _infoButton = ctrl as BaseButton;
                    break;
                case "ButtonForJobDisplay" when _jobDisplayButton == null:
                    _jobDisplayButton = ctrl as BaseButton;
                    break;
            }
        }

        // Запасные варианты (если структура сцены другая, напр. старая Garden).
        _titleLabel ??= root.FindChild("Label", true, false) as Label;
        _escapeButton ??= root.FindChild("Escape", true, false) as BaseButton
            ?? root.FindChild("Close", true, false) as BaseButton
            ?? root.FindChild("Exit", true, false) as BaseButton
            ?? root.FindChild("Back", true, false) as BaseButton;
        _checkMarkButton ??= root.FindChild("CheckMark", true, false) as BaseButton
            ?? root.FindChild("Checkmark", true, false) as BaseButton
            ?? root.FindChild("CheckBox", true, false) as BaseButton;

        // Счётчик клеток — внутри ScrollContainer (не путать с заголовком).
        var scroll = root.FindChild("ScrollContainer", true, false) as Control;
        if (scroll != null)
            _tilesLabel = scroll.FindChild("Label", true, false) as Label;
        _tilesLabel ??= root.FindChild("NumFarm", true, false) as Label;

        _resourcesLabel = root.FindChild("AmountOfResources", true, false) as Label;

        // Внутренний CheckMark старой разметки (список культур) — прячем, работает
        // корневой. В новом Farm.tscn его нет, проверка оставлена ради совместимости.
        if (scroll != null)
        {
            var innerCheck = scroll.FindChild("CheckMark", true, false) as Control;
            if (innerCheck != null && innerCheck != _checkMarkButton)
                innerCheck.Visible = false;
        }

        // InfoWindow из сцены лежит за краем окна (offset_left = −236) и не виден.
        // Сцены не трогаем — вместо него свой тултип (BuildCellTooltip).
        var sceneInfo = root.FindChild("InfoWindow", true, false) as Control;
        if (sceneInfo != null)
            sceneInfo.Visible = false;

        // --- task-11: узлы сетки выбора культуры ---
        _foodGrid = root.FindChild("FoodGrid", true, false) as GridContainer;
        _fiberGrid = root.FindChild("FiberGrid", true, false) as GridContainer;
        _foodHeader = root.FindChild("FoodHeader", true, false) as Label;
        _fiberHeader = root.FindChild("FiberHeader", true, false) as Label;
        _selectedCropName = root.FindChild("SelectedCropName", true, false) as Label;
        _selectedCropDetails = root.FindChild("SelectedCropDetails", true, false) as Label;
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

    private void OnCheckMarkPressed()
    {
        // FIX (bughunt 15-5): берём ЖИВУЮ зону (не кэш) — AutoPlantEnabled меняется
        // этим же нажатием, иначе повторный клик не включил бы автопосадку обратно.
        var zone = ZoneManager.Instance.SelectedZone;
        if (zone == null || zone.Kind != ZoneKind.Farm) return;

        bool newState = !zone.AutoPlantEnabled;
        FarmZoneManager.Instance.SetAutoPlant(zone.Id, newState);
        _farmZoneDirty = true; // данные зоны изменились — кэш невалиден
        UpdateCheckMarkState(newState);
    }

    private void UpdateCheckMarkState(bool isEnabled)
    {
        if (_checkMarkButton != null)
        {
            if (_checkMarkButton is Button btn)
            {
                btn.Modulate = isEnabled ? new Color(0.4f, 1.0f, 0.4f, 1.0f) : new Color(1f, 1f, 1f, 0.5f);
            }
            else if (_checkMarkButton is CheckBox chk)
            {
                chk.ButtonPressed = isEnabled;
            }
        }
    }

    /// <summary>Тумблер подсказки про клетку (кнопка Infoormation, §20.5).</summary>
    private void OnInfoButtonPressed()
    {
        _infoMode = !_infoMode;
        if (_infoButton is Button btn)
            btn.Modulate = _infoMode ? new Color(0.6f, 1f, 1f, 1f) : new Color(1f, 1f, 1f, 1f);
        if (!_infoMode && _cellTooltip != null)
            _cellTooltip.Visible = false;
    }

    /// <summary>Кнопка ButtonForJobDisplay — открыть JobDisplay для этой фермы (§20.6).</summary>
    private void OnJobDisplayPressed()
    {
        var zone = GetSelectedFarmZone();
        if (zone == null) return;
        var host = ZoneWindowRegistry.FarmJobDisplay;
        if (host == null) return;
        Close();
        host.ShowForFarmZone(zone.Id);
    }

    private void OnZoneSelected(FarmZone zone)
    {
        if (_rootPanel == null || zone == null) return;

        // FIX (bughunt 15-5): выбор сменился — кэш адаптированной зоны невалиден.
        _farmZoneDirty = true;

        // Единый выбор уже в ZoneManager: закрываем окно Work-зоны если открыто.
        var work = ZoneWindowRegistry.WorkWindow;
        if (work != null && work.IsOpen)
            work.Close();

        if (_titleLabel != null)
            _titleLabel.Text = zone.Name;

        if (_tilesLabel != null)
            _tilesLabel.Text = $"{zone.TotalTiles} кл.";

        if (_resourcesLabel != null)
            _resourcesLabel.Text = $"Семена: {zone.TotalTiles} · растёт 0/{zone.TotalTiles}";

        UpdateCheckMarkState(zone.AutoPlantEnabled);
        ResetCellTooltip();

        // task-11: культура зоны — в ЖИВОЙ Zone (FarmZone из события — адаптер-копия,
        // его Crop может быть дефолтным), поэтому читаем ZoneManager напрямую.
        var live = ZoneManager.Instance.SelectedZone;
        CropType crop = live != null && live.Kind == ZoneKind.Farm ? live.Crop : CropType.Wheat;
        SetCropGridEnabled(true);
        _lastPlantedText = "";
        UpdateCropSelection(crop);

        _rootPanel.Visible = true;
        OnOpened?.Invoke();
    }

    public void Close()
    {
        if (_cellTooltip != null)
            _cellTooltip.Visible = false;
        if (_rootPanel != null && _rootPanel.Visible)
        {
            _rootPanel.Visible = false;
            SetCropGridEnabled(false);
            OnClosed?.Invoke();
        }
    }

    public override void _Process(double delta)
    {
        if (_rootPanel == null || !_rootPanel.Visible)
            return;
        TickCellTooltip();
        TickPlantedCount(delta);
    }

    // --- Подсказка про клетку (§20.5, шаг 4) ---

    private double _plantedTimer;
    private string _lastPlantedText = "";

    /// <summary>Строка «растёт K/N» — обновляется раз в секунду, Text только при изменении.</summary>
    private void TickPlantedCount(double delta)
    {
        // FIX (bughunt 15-10): копим РЕАЛЬНОЕ время кадра, а не «кадры по 16.7 мс».
        // Раньше на 144 Гц счётчик срабатывал в 2.4 раза чаще, на 30 fps — вдвое реже.
        if (!double.IsFinite(delta) || delta < 0.0) delta = 0.0;
        _plantedTimer += delta;
        if (_plantedTimer < 1.0)
            return;
        _plantedTimer = 0.0;
        // FIX (bughunt 15-5): зона — из кэша, без копирования всех клеток каждый кадр.
        var zone = GetSelectedFarmZone();
        if (zone == null || _resourcesLabel == null)
            return;
        int growing = 0;
        foreach (var (x, y) in zone.Tiles)
        {
            if (CropGrowthManager.Instance.HasCrop(x, y))
                growing++;
        }
        string text = $"Семена: {zone.TotalTiles} · растёт {growing}/{zone.TotalTiles}";
        if (text != _lastPlantedText)
        {
            _lastPlantedText = text;
            _resourcesLabel.Text = text;
        }
    }

    /// <summary>Свой тултип вместо сценичного InfoWindow (тот за краем окна).</summary>
    private void BuildCellTooltip()
    {
        _cellTooltip = new Label
        {
            Name = "FarmCellTooltip",
            Text = "",
            Visible = false,
            MouseFilter = MouseFilterEnum.Ignore,
            ZIndex = 200,
            TopLevel = true
        };
        _cellTooltip.AddThemeColorOverride("font_color", new Color(0.8f, 1f, 0.8f));
        _cellTooltip.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.9f));
        _cellTooltip.AddThemeConstantOverride("shadow_offset_x", 1);
        _cellTooltip.AddThemeConstantOverride("shadow_offset_y", 1);
        _cellTooltip.AddThemeFontSizeOverride("font_size", 20);
        AddChild(_cellTooltip);
    }

    private void ResetCellTooltip()
    {
        _lastTipX = int.MinValue;
        _lastTipY = int.MinValue;
        _lastTipText = "";
        if (_cellTooltip != null)
            _cellTooltip.Visible = false;
    }

    /// <summary>
    /// Подсказка рядом с мышкой: влажность, плодородие, что растёт.
    /// Видна только при включённом InfoMode и мышке над клеткой своей фермы.
    /// </summary>
    private void TickCellTooltip()
    {
        if (_cellTooltip == null || !_infoMode)
        {
            if (_cellTooltip != null && _cellTooltip.Visible)
                _cellTooltip.Visible = false;
            return;
        }
        // FIX (bughunt 15-5): в InfoMode метод зовётся каждый кадр — берём кэш,
        // а не FarmZoneManager.SelectedZone (тот копирует все клетки в новый HashSet).
        var zone = GetSelectedFarmZone();
        var map = MapRenderer.Instance;
        if (zone == null || map?.MapData?.Ground == null)
        {
            _cellTooltip.Visible = false;
            return;
        }
        var camera = GetViewport()?.GetCamera2D();
        Vector2 world;
        if (camera != null)
        {
            world = camera.GetScreenCenterPosition()
                + (GetViewport().GetMousePosition() - GetViewport().GetVisibleRect().Size * 0.5f)
                    / (camera.Zoom.X > 0f ? camera.Zoom.X : 1f);
        }
        else
        {
            world = GetGlobalMousePosition();
        }
        int tx = (int)MathF.Floor(world.X / MapRenderer.TileSizePx);
        int ty = (int)MathF.Floor(world.Y / MapRenderer.TileSizePx);
        var ground = map.MapData.Ground;
        if ((uint)tx >= (uint)ground.GetLength(0) || (uint)ty >= (uint)ground.GetLength(1))
        {
            _cellTooltip.Visible = false;
            return;
        }
        // Только своя ферма — чужие клетки и мимо зон прячут тултип.
        if (!zone.Tiles.Contains((tx, ty)))
        {
            _cellTooltip.Visible = false;
            _lastTipX = int.MinValue;
            return;
        }
        // Текст — только при смене клетки, позиция — каждый кадр.
        if (tx != _lastTipX || ty != _lastTipY)
        {
            _lastTipX = tx;
            _lastTipY = ty;
            string text = BuildCellText(tx, ty, map);
            if (text != _lastTipText)
            {
                _lastTipText = text;
                _cellTooltip.Text = text;
            }
        }
        if (string.IsNullOrEmpty(_lastTipText))
        {
            _cellTooltip.Visible = false;
            return;
        }
        _cellTooltip.GlobalPosition = GetViewport().GetMousePosition() + new Vector2(16, 16);
        _cellTooltip.Visible = true;
    }

    private static string BuildCellText(int x, int y, MapRenderer map)
    {
        int m = map.Humidity != null ? map.Humidity.Get(x, y) : 100;
        int f = map.Fertility != null ? map.Fertility.Get(x, y) : 100;
        string mWord = m < 40 ? "сухо" : m <= 130 ? "норма" : "мокро";
        string fWord = f < 60 ? "бедно" : f <= 140 ? "норма" : "богато";
        string crop;
        if (CropGrowthManager.Instance.TryGetCrop(x, y, out var c))
            crop = $"{CropRegistry.Get(c.Type).DisplayName} (фаза {c.Stage}/4)";
        else
            crop = "Пусто";
        return $"Влага: {m}% {mWord}\nПлодородие: {f} {fWord}\n{crop}";
    }

    // ------------------------------------------------------------------ //
    //  Сетка выбора культуры (task-11)                                    //
    //  Данные — из CropRegistry (один источник правды): 14 «Еда» + 4      //
    //  «Волокно». Пишем в ЖИВУЮ Zone.Crop (адаптер FarmZone — копия).     //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Строит сетку культур из <see cref="CropRegistry"/> ОДИН раз при загрузке
    /// сцены: заголовки групп + GridContainer по 5 клеток в ряд. Клетка — кнопка
    /// 90×96: иконка 64×64 (зрелая стадия, фильтр NEAREST) и подпись 12px.
    /// </summary>
    private void BuildCropGrid()
    {
        if (_cropGridBuilt)
            return;
        _cropGridBuilt = true;

        MakeCellStyles();
        BuildCropGroup(CropCategory.Food, _foodGrid, _foodHeader);
        BuildCropGroup(CropCategory.Fiber, _fiberGrid, _fiberHeader);
        SetCropGridEnabled(false);
    }

    private void BuildCropGroup(CropCategory category, GridContainer grid, Label header)
    {
        var crops = CropRegistry.ByCategory(category);
        if (header != null)
            header.Text = $"{CategoryTitle(category)} · {crops.Count}";
        if (grid == null)
        {
            GD.PushWarning($"[Farm] В Farm.tscn нет GridContainer для группы «{CategoryTitle(category)}».");
            return;
        }
        for (int i = 0; i < crops.Count; i++)
            grid.AddChild(CreateCropCell(crops[i]));
    }

    private Button CreateCropCell(CropDefinition def)
    {
        var button = new Button
        {
            Name = $"Crop_{def.Id}",
            CustomMinimumSize = new Vector2(CropCellWidth, CropCellHeight),
            TooltipText = BuildCropTooltip(def),
            FocusMode = FocusModeEnum.None,
            MouseDefaultCursorShape = CursorShape.PointingHand,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
        };
        button.AddThemeStyleboxOverride("normal", _cellNormal);
        button.AddThemeStyleboxOverride("hover", _cellHover);
        button.AddThemeStyleboxOverride("pressed", _cellPressed);
        button.AddThemeStyleboxOverride("disabled", _cellDisabled);

        // Button — не контейнер, поэтому вложенный VBox растягиваем по кнопке руками.
        var layout = new VBoxContainer
        {
            Name = "Layout",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        layout.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        layout.OffsetLeft = 4f;
        layout.OffsetTop = 4f;
        layout.OffsetRight = -4f;
        layout.OffsetBottom = -3f;
        layout.AddThemeConstantOverride("separation", 2);
        button.AddChild(layout);

        var icon = new TextureRect
        {
            Name = "Icon",
            Texture = LoadCropIcon(def.Id),
            CustomMinimumSize = new Vector2(CropIconSize, CropIconSize),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            // NEAREST иначе иконка 64→64 «мылится» на масштабе окна.
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
        };
        layout.AddChild(icon);

        var caption = new Label
        {
            Name = "Caption",
            Text = def.DisplayName,
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            ClipText = true,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        caption.AddThemeFontSizeOverride("font_size", 12);
        caption.AddThemeColorOverride("font_color", new Color(0.82f, 0.82f, 0.85f, 1f));
        layout.AddChild(caption);

        CropType type = def.Type; // локальная копия для замыкания
        button.Pressed += () => OnCropSelected(type);
        _cropButtons[def.Type] = button;
        return button;
    }

    /// <summary>Клик по культуре: пишем в Zone.Crop текущей выбранной зоны.</summary>
    private void OnCropSelected(CropType crop)
    {
        var zone = ZoneManager.Instance.SelectedZone;
        if (zone == null || zone.Kind != ZoneKind.Farm)
            return;

        if (zone.Crop != crop)
        {
            // Истинный владелец культуры — Zone в ZoneManager (FarmZone — копия).
            // CropType : byte, запись атомарна; посадка читает её через
            // FarmZoneManager.TryGetZoneAt → Adapted.Crop → PlantingJobHandler.
            zone.Crop = crop;
            _farmZoneDirty = true;   // кэш адаптированной FarmZone невалиден
            _lastPlantedText = "";   // заставит TickPlantedCount переписать подпись
        }
        UpdateCropSelection(crop);
    }

    /// <summary>Подсветка выбранной клетки + подписи «что за культура / что собираем / рост».</summary>
    private void UpdateCropSelection(CropType crop)
    {
        if (!_cropGridBuilt)
            return;

        foreach (var pair in _cropButtons)
        {
            var button = pair.Value;
            if (button == null || !GodotObject.IsInstanceValid(button))
                continue;
            bool selected = pair.Key == crop;
            button.AddThemeStyleboxOverride("normal", selected ? _cellSelected : _cellNormal);
            button.AddThemeStyleboxOverride("hover", selected ? _cellSelectedHover : _cellHover);
        }
        UpdateCropDetails(crop);
    }

    private void UpdateCropDetails(CropType crop)
    {
        CropDefinition def = CropRegistry.Get(crop);
        string harvest = HarvestName(def);
        if (_selectedCropName != null)
            _selectedCropName.Text = $"Культура зоны: {def.DisplayName}";
        if (_selectedCropDetails != null)
            _selectedCropDetails.Text = $"Собирается: {harvest} · рост {FormatGrowth(def.GrowthSeconds)}";
    }

    /// <summary>
    /// Доступность сетки: без выбранной Farm-зоны клики не должны менять культуру
    /// (и подписи показывают «зона не выбрана»).
    /// </summary>
    private void SetCropGridEnabled(bool enabled)
    {
        if (!_cropGridBuilt)
            return;

        if (!enabled)
        {
            if (_selectedCropName != null)
                _selectedCropName.Text = "Культура не выбрана";
            if (_selectedCropDetails != null)
                _selectedCropDetails.Text = "Выберите зону фермы";
        }

        Color tint = enabled ? new Color(1f, 1f, 1f, 1f) : new Color(1f, 1f, 1f, 0.45f);
        foreach (var pair in _cropButtons)
        {
            var button = pair.Value;
            if (button == null || !GodotObject.IsInstanceValid(button))
                continue;
            button.Disabled = !enabled;
            button.Modulate = tint;
        }
    }

    private void MakeCellStyles()
    {
        _cellNormal = MakeCellStyle(
            new Color(0.043f, 0.043f, 0.047f, 1f), new Color(0.212f, 0.212f, 0.224f, 1f), 1);
        _cellHover = MakeCellStyle(
            new Color(0.102f, 0.102f, 0.114f, 1f), new Color(0.502f, 0.502f, 0.533f, 1f), 1);
        _cellPressed = MakeCellStyle(
            new Color(0.012f, 0.012f, 0.016f, 1f), new Color(0.910f, 0.910f, 0.910f, 1f), 1);
        // Выбранная — светлее фон + БЕЛАЯ рамка 2px (как SB_button_pressed, но заметнее).
        _cellSelected = MakeCellStyle(
            new Color(0.078f, 0.078f, 0.090f, 1f), new Color(0.910f, 0.910f, 0.910f, 1f), 2);
        _cellSelectedHover = MakeCellStyle(
            new Color(0.125f, 0.125f, 0.141f, 1f), new Color(1f, 1f, 1f, 1f), 2);
        _cellDisabled = MakeCellStyle(
            new Color(0.031f, 0.031f, 0.035f, 1f), new Color(0.118f, 0.118f, 0.129f, 1f), 1);
    }

    private static StyleBoxFlat MakeCellStyle(Color bg, Color border, int borderWidth)
    {
        var box = new StyleBoxFlat { BgColor = bg, BorderColor = border };
        box.BorderWidthLeft = borderWidth;
        box.BorderWidthTop = borderWidth;
        box.BorderWidthRight = borderWidth;
        box.BorderWidthBottom = borderWidth;
        box.ContentMarginLeft = 4f;
        box.ContentMarginTop = 4f;
        box.ContentMarginRight = 4f;
        box.ContentMarginBottom = 4f;
        return box;
    }

    /// <summary>
    /// Иконка культуры — зрелая стадия арта: res://ui/assets/textures/crops/&lt;id&gt;_s4.png.
    /// Для пеньки файл называется hemp_s4.png (id культуры — hemp_fiber), поэтому есть алиас.
    /// </summary>
    private static Texture2D LoadCropIcon(string cropId)
    {
        string file = CropIconAliases.TryGetValue(cropId, out var alias) ? alias : cropId;
        string path = $"res://ui/assets/textures/crops/{file}_s4.png";
        if (!ResourceLoader.Exists(path))
        {
            path = $"res://ui/assets/textures/crops/{cropId}_s4.png";
            if (!ResourceLoader.Exists(path))
            {
                GD.PushWarning($"[Farm] Нет иконки культуры «{cropId}»: {path}");
                return null;
            }
        }
        var texture = GD.Load<Texture2D>(path);
        if (texture == null)
            GD.PushWarning($"[Farm] Не удалось загрузить иконку культуры: {path}");
        return texture;
    }

    /// <summary>Что падает при сборе (человекочитаемое имя предмета).</summary>
    private static string HarvestName(CropDefinition def)
    {
        if (ItemRegistry.TryGet(def.HarvestItem, out var item) && !string.IsNullOrEmpty(item.Name))
            return item.Name;
        return def.HarvestItem.ToString();
    }

    private static string BuildCropTooltip(CropDefinition def)
        => $"{def.DisplayName}\nСобирается: {HarvestName(def)}\nРост: {FormatGrowth(def.GrowthSeconds)}";

    private static string FormatGrowth(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds <= 0f)
            return "—";
        int total = (int)MathF.Round(seconds);
        return total >= 60 ? $"{total / 60} мин" : $"{total} с";
    }

    private static string CategoryTitle(CropCategory category)
        => category == CropCategory.Fiber ? "Волокно" : "Еда";

    public override void _ExitTree()
    {
        FarmZoneManager.Instance.OnZoneSelected -= OnZoneSelected;
        FarmZoneManager.Instance.OnZoneDeselected -= Close;
        FarmZoneManager.Instance.OnZonesUpdated -= OnFarmZonesUpdated;
        // Симметрично BuildMenuController/HUDController: статика не должна удерживать
        // освобождённый Godot-объект, иначе Instance отдаёт disposed-ноду после выгрузки UI.
        if (ReferenceEquals(ZoneWindowRegistry.FarmWindow, this))
            ZoneWindowRegistry.FarmWindow = null;
        if (ReferenceEquals(Instance, this))
            Instance = null;
        base._ExitTree();
    }
}
