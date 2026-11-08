using Godot;
using Game.Core;
using Game.Core.WorldStreaming;
using Game.Simulation;
using Game.UI.Tools;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Game.UI;

public partial class HUDController : Control, ICellProbeOverlay, IDraftCoordinator
{
    public static HUDController Instance { get; private set; }

    private Button _buttonConstruct;

    private Control _selectionContainer;
    private Control _buildContainer;
    private Control _orderContainer;
    private Control _wallContainer;
    private Control _zoneContainer;
    private Control _industrialContainer;

    private Button _buildVariantButton;
    private Button _orderVariantButton;

    private Button _wallVariantButton;
    private Button _zonesVariantButton;
    private Button _industrialItemsVariantButton;

    private Button _otherButton;
    private Control _additionallyBox;
    private Button _humidityButton;
    private Label _humidityLegendLabel;
    private Button _fertilityButton;
    private Label _fertilityLegendLabel;
    // task-6: панель режимов карты + легенда месторождений (создаются кодом).
    private PanelContainer _mapModePanel;
    private Button _mapLayersButton;
    private Label _mapModeCaption;
    private readonly Dictionary<MapMode, Button> _mapModeButtons = new();
    private PanelContainer _oreLegendPanel;
    private RichTextLabel _oreLegend;
    private float _oreLegendTimer;

    private Button _woodWallButton;
    private Button _warehouseAreaButton;
    private Button _farmingButton;

    // task-13: отрасль зоны — кнопки строятся из каталога ZoneIndustryRegistry.
    private GridContainer _industryGrid;
    private readonly Dictionary<ZoneKind, Button> _industryButtons = new();
    private Button _workTableButton;
    private Button _bedButton;
    private Button _benchButton;
    private Button _nightstandButton;
    private Button _stoneMiningButton;
    private Button _oreMiningButton;
    private Label _oreTooltipLabel;
    private int _oreTipX = int.MinValue;
    private int _oreTipY = int.MinValue;
    private string _oreTipText = string.Empty;
    private Button _treeFellingButton;
    private Button _prioritetButton;

    private Label _populationLabel;
    private Label _employmentLabel;
    private Label _woodAmountLabel;
    private Label _oreAmountLabel;
    private Label _worldTimeLabel;

    // task-20: счётчики товаров производства (слитки/доски/мука) в панели ресурсов.
    private Label _ingotAmountLabel;
    private Label _plankAmountLabel;
    private Label _flourAmountLabel;
    private string _lastIngotText = "";
    private string _lastPlankText = "";
    private string _lastFlourText = "";
    private Control _amountOfResourcesPanel;

    private Control _speedButtonsContainer;
    private Button _stopButton;
    private Button _speed1xButton;
    private Button _speed5xButton;
    private Button _speed25xButton;
    private Button _speed100xButton;

    private float _statsSyncTimer;
    private float _timeSyncTimer;
    private string _lastPopText = "";
    private string _lastWoodText = "";
    private string _lastOreText = "";
    private string _lastTimeText = "";
    private int _totalPopulation = 550;

    private Button _activeToolButton;
    private Panel _activeHighlightPanel;
    private PriorityBoardController _priorityBoard;
    private FarmController _gardenController;
    private BuildMenuController _buildMenuController;
    private PlayerInteractionManager _interactionManager;

    // UI-20: система критических алертов колонии (голод, отсутствие еды).
    private PanelContainer _criticalAlertBanner;
    private Label _criticalAlertLabel;
    private string _lastCriticalAlert = "";

    public override void _Ready()
    {
        Instance = this;
        _buttonConstruct = GetNodeOrNull<Button>("ButtonConstruct") ?? FindChild("ButtonConstruct", true, false) as Button;

        _selectionContainer  = FindChild("SelectionContainer", true, false) as Control;
        _buildContainer      = FindChild("BuildContainer", true, false) as Control;
        _orderContainer      = FindChild("OrderContainer", true, false) as Control;
        _wallContainer       = FindChild("WallContainer", true, false) as Control;
        _zoneContainer       = FindChild("ZoneModal", true, false) as Control;
        _industrialContainer = FindChild("IndustrialContainer", true, false) as Control;

        _buildVariantButton = FindChild("BuildVariant", true, false) as Button;
        _orderVariantButton = (FindChild("OrderVariant", true, false) as Button) ?? (FindChild("OrderVarinat", true, false) as Button);

        _wallVariantButton            = FindChild("WallVariant", true, false) as Button;
        _zonesVariantButton           = FindChild("ZonesVariant", true, false) as Button;
        _industrialItemsVariantButton = FindChild("IndustrialItemsVariant", true, false) as Button;

        _woodWallButton      = FindChild("WoodWallButton", true, false) as Button;
        _warehouseAreaButton = FindChild("WarehouseArea", true, false) as Button;
        _farmingButton       = FindChild("Farming", true, false) as Button;
        _workTableButton     = FindChild("WorkTableButton", true, false) as Button;
        _treeFellingButton   = (FindChild("TreeFellingButton", true, false) as Button) ?? (FindChild("TreeFelingButton", true, false) as Button);
        _prioritetButton     = FindChild("PrioritetButton", true, false) as Button ?? FindChild("PriorityButton", true, false) as Button;

        // Режим карты: кнопка other (шторка >/<) + additionally-бокс с влажностью.
        _otherButton = FindChild("other", true, false) as Button;
        
        // Исправленный поиск: поднимаемся от кнопки humidity до родителя ноды "additionally"
        var humidityBtn = FindChild("humidity", true, false) as Control;
        _additionallyBox = humidityBtn?.GetParent()?.GetParent() as Control;
        if (_additionallyBox == null)
            GD.PrintErr("[HUD] additionallyBox не найден (humidity->parent->parent)");
        
        _humidityButton = FindChild("humidity", true, false) as Button;
        _fertilityButton = FindChild("fertility", true, false) as Button;
        SetupMapModeControls();
        // Слои карты: панель и кнопки — в hud.tscn (сцена = источник правды),
        // код только находит их по имени и переключает видимость.
        SetupMapLayersPanel();
        // Легенда создаётся ПОСЛЕ SetMouseFilterRecursive (он ставит Ignore всем
        // не-кнопкам): легенде нужен Stop? Нет — Ignore, но создавать надо после,
        // иначе рекурсия её уже обработала. Порядок: рекурсия ниже, легенда внутри
        // SetupMapModeControls — ок, но тултип-лейбл создаём отдельно после.
        SetupHumidityTooltip();

        _populationLabel = (FindChild("Population", true, false) as Label) ?? GetNodeOrNull<Label>("Population");
        _employmentLabel = (FindChild("Employment", true, false) as Label) ?? GetNodeOrNull<Label>("Employment");
        _worldTimeLabel = FindChild("WorldTimeLabel", true, false) as Label;
        _amountOfResourcesPanel = FindChild("AmountOfResources", true, false) as Control;

        _stopButton      = FindChild("stop", true, false) as Button ?? FindChild("StopTime", true, false) as Button;
        _speed1xButton   = FindChild("1x", true, false) as Button ?? FindChild("1xTime", true, false) as Button;
        _speed5xButton   = FindChild("5x", true, false) as Button ?? FindChild("5xTime", true, false) as Button;
        _speed25xButton  = FindChild("25x", true, false) as Button ?? FindChild("25xTime", true, false) as Button;
        _speed100xButton = FindChild("100x", true, false) as Button ?? FindChild("100xTime", true, false) as Button;

        SetupSpeedControlsLayer();
        SetupHighlightPanel();
        SetupPriorityBoard();
        SetupGardenController();
        // Новые кнопки стройки/добычи создаются кодом (сцену hud.tscn не трогаем):
        // мебель — в IndustrialContainer рядом с верстаком, камень — в OrderContainer
        // рядом с рубкой. Иконки = те же спрайты, что в мире (100x100, expand_icon).
        SetupFurnitureButtons();
        // task-13: кнопки ОТРАСЛЕЙ в ZoneContainer — из каталога, без хардкода.
        SetupIndustryButtons();
        SetupStoneMiningButton();
        // task-4: инструмент и счётчик руды (кнопка в OrderContainer, строка в
        // панели ресурсов). Сцену не трогаем — создаём кодом.
        SetupOreMiningButton();
        // task-4/task-20: панель ресурсов — ВСЕ ресурсы и товары по категориям,
        // 3 счётчика в ряд. Строки строятся из реестров (атлас + ItemRegistry),
        // отрисовка — в контейнерах, которые лежат в hud.tscn.
        SetupResourcePanel();
        // task-7: панель склада (90 ресурсов по категориям) + тултип клетки с рудой.
        SetupSpecialWindows();
        SetupVillagerInspector();
        SetupOreTooltip();
        SetupCriticalAlertBanner();
        SetMouseFilterRecursive(this);

        if (_buttonConstruct != null)
        {
            _buttonConstruct.MouseFilter = MouseFilterEnum.Stop;
            _buttonConstruct.Pressed += OnButtonConstructPressed;
        }

        if (_buildVariantButton != null)
        {
            _buildVariantButton.MouseFilter = MouseFilterEnum.Stop;
            _buildVariantButton.Pressed += OnBuildVariantPressed;
        }

        if (_orderVariantButton != null)
        {
            _orderVariantButton.MouseFilter = MouseFilterEnum.Stop;
            _orderVariantButton.Pressed += OnOrderVariantPressed;
        }

        if (_wallVariantButton != null)
        {
            _wallVariantButton.MouseFilter = MouseFilterEnum.Stop;
            _wallVariantButton.Pressed += () => ToggleSubContainer(_wallContainer);
        }

        if (_zonesVariantButton != null)
        {
            _zonesVariantButton.MouseFilter = MouseFilterEnum.Stop;
            // «Зоны» → окно выбора отрасли (как в других окнах игры), а не плашка в HUD.
            _zonesVariantButton.Pressed += ToggleZoneModal;
        }

        if (_industrialItemsVariantButton != null)
        {
            _industrialItemsVariantButton.MouseFilter = MouseFilterEnum.Stop;
            _industrialItemsVariantButton.Pressed += () => ToggleSubContainer(_industrialContainer);
        }

        if (_woodWallButton != null)
        {
            _woodWallButton.MouseFilter = MouseFilterEnum.Stop;
            _woodWallButton.Pressed += OnWoodWallPressed;
        }

        if (_warehouseAreaButton != null)
        {
            _warehouseAreaButton.MouseFilter = MouseFilterEnum.Stop;
            _warehouseAreaButton.Pressed += OnWarehouseAreaPressed;
        }

        if (_farmingButton != null)
        {
            _farmingButton.MouseFilter = MouseFilterEnum.Stop;
            _farmingButton.Pressed += OnFarmingPressed;
        }

        if (_workTableButton != null)
        {
            _workTableButton.MouseFilter = MouseFilterEnum.Stop;
            _workTableButton.Pressed += OnWorkTablePressed;
        }

        if (_treeFellingButton != null)
        {
            _treeFellingButton.MouseFilter = MouseFilterEnum.Stop;
            _treeFellingButton.Pressed += OnTreeFellingPressed;
        }

        if (_prioritetButton != null)
        {
            _prioritetButton.MouseFilter = MouseFilterEnum.Stop;
            _prioritetButton.Pressed += OnPrioritetButtonPressed;
        }

        BindTimeButton(_stopButton, GameSpeed.Paused);
        BindTimeButton(_speed1xButton, GameSpeed.Normal);
        BindTimeButton(_speed5xButton, GameSpeed.Fast5);
        BindTimeButton(_speed25xButton, GameSpeed.Fast25);
        BindTimeButton(_speed100xButton, GameSpeed.Fast100);

        StockpileManager.Instance.OnItemCountChanged += OnStockpileItemCountChanged;
        UpdateWoodDisplay(StockpileManager.Instance.GetTotalItemCount(ItemId.Log));

        if (PlayerInteractionManager.Instance != null && _interactionManager == null)
        {
            _interactionManager = PlayerInteractionManager.Instance;
            // Узкий контракт ICellProbeOverlay вместо обратной ссылки SelectTool → HUDController.
            _interactionManager.SetCellProbe(this);
            _interactionManager.OnToolReset += ClearActiveToolButton;
            // §113.5/B1: ESC-десинк — PlayerInteractionManager съедает Escape при
            // активном инструменте (SetInputAsHandled), BuildMenuController его не
            // видит и _draftTool оставался stale: повторное Open той же зоны
            // упиралось в ранний return и инструмент не переактивировался.
            _interactionManager.OnToolReset += ClearStaleDraftTool;
        }

        CloseAllMenus();
    }

    /// <summary>Внешний сброс инструмента (ESC/кнопка): черновик больше не активен.</summary>
    private void ClearStaleDraftTool()
    {
        _draftTool = null;
    }

    /// <summary>
    /// DEBUG (§133.6): поставить постройку программно — для проверок арта/габаритов
    /// из LiveBridge без участия мыши. Вызывается только тест-бриджем; в обычной
    /// игре не используется. false — клетка занята/вне карты/тип не найден.
    /// </summary>
    public bool DebugPlaceStation(string typeName, int cellX, int cellY)
    {
        if (string.IsNullOrEmpty(typeName) || !Enum.TryParse(typeName, ignoreCase: true, out BuildingType type))
            return false;
        StructureDefinition def = StructureRegistry.Get(type);
        if (def == null)
            return false;
        var mapRenderer = MapRenderer.Instance;
        if (mapRenderer?.MapData == null)
            return false;
        return StructureManager.Instance.AddStructure(cellX, cellY, def, null, null);
    }

    /// <summary>
    /// Кнопка other тогглит ">" / "&lt;" и видимость additionally-бокса.
    /// Кнопка humidity тогглит режим карты Влажность (слой поверх тайлов).
    /// </summary>
    private void SetupMapModeControls()
    {
        if (_additionallyBox != null)
            _additionallyBox.Visible = false;

        if (_otherButton != null)
        {
            _otherButton.MouseFilter = MouseFilterEnum.Stop;
            _otherButton.Pressed += () =>
            {
                if (_additionallyBox == null) return;
                bool show = !_additionallyBox.Visible;
                _additionallyBox.Visible = show;
                _otherButton.Text = show ? "<" : ">";
            };
        }

        if (_humidityButton != null)
        {
            _humidityButton.MouseFilter = MouseFilterEnum.Stop;
            _humidityButton.Pressed += () =>
            {
                var map = MapRenderer.Instance;
                if (map == null) return;
                bool enable = map.CurrentMapMode != MapMode.Humidity;
                map.SetMapMode(enable ? MapMode.Humidity : MapMode.Normal);
                SyncMapModeUi(map.CurrentMapMode);
                // Подсветка активной кнопки: яркая когда вкл, тусклая когда выкл.
                // Режимы взаимоисключающие — вторую кнопку гасим + её легенду.
                _humidityButton.Modulate = enable ? new Color(0.5f, 1f, 1f, 1f) : Color.Color8(255, 255, 255, 255);
                if (_fertilityButton != null)
                    _fertilityButton.Modulate = Color.Color8(255, 255, 255, 255);
                UpdateHumidityLegend(enable);
                UpdateFertilityLegend(false);
            };
        }

        if (_fertilityButton != null)
        {
            _fertilityButton.MouseFilter = MouseFilterEnum.Stop;
            _fertilityButton.Pressed += () =>
            {
                var map = MapRenderer.Instance;
                if (map == null) return;
                bool enable = map.CurrentMapMode != MapMode.Fertility;
                map.SetMapMode(enable ? MapMode.Fertility : MapMode.Normal);
                SyncMapModeUi(map.CurrentMapMode);
                _fertilityButton.Modulate = enable ? new Color(0.6f, 1f, 0.6f, 1f) : Color.Color8(255, 255, 255, 255);
                if (_humidityButton != null)
                    _humidityButton.Modulate = Color.Color8(255, 255, 255, 255);
                UpdateFertilityLegend(enable);
                UpdateHumidityLegend(false);
            };
        }

        // Легенда-шкала влажности (текст, создаём кодом — сцену не трогаем).
        // Позиция — левый низ (600,1020 при 1920×1080), поверх мира, ZIndex выше.
        _humidityLegendLabel = new Label
        {
            Name = "HumidityLegend",
            Text = "Влажность: светлое — сухо, тёмное — мокро",
            Visible = false,
            MouseFilter = MouseFilterEnum.Ignore,
            ZIndex = 100
        };
        _humidityLegendLabel.SetAnchorsPreset(LayoutPreset.BottomLeft);
        _humidityLegendLabel.Position = new Vector2(20, -60);
        _humidityLegendLabel.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f));
        _humidityLegendLabel.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.9f));
        _humidityLegendLabel.AddThemeConstantOverride("shadow_offset_x", 1);
        _humidityLegendLabel.AddThemeConstantOverride("shadow_offset_y", 1);
        AddChild(_humidityLegendLabel);

        // Легенда-шкала плодородия (текст, создаём кодом — сцену не трогаем).
        _fertilityLegendLabel = new Label
        {
            Name = "FertilityLegend",
            Text = "Плодородие: бледное — бедно, тёмное — богато",
            Visible = false,
            MouseFilter = MouseFilterEnum.Ignore,
            ZIndex = 100
        };
        _fertilityLegendLabel.SetAnchorsPreset(LayoutPreset.BottomLeft);
        _fertilityLegendLabel.Position = new Vector2(20, -60);
        _fertilityLegendLabel.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f));
        _fertilityLegendLabel.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.9f));
        _fertilityLegendLabel.AddThemeConstantOverride("shadow_offset_x", 1);
        _fertilityLegendLabel.AddThemeConstantOverride("shadow_offset_y", 1);
        AddChild(_fertilityLegendLabel);
    }

    private Label _humidityTooltip;

    /// <summary>
    /// Всплывашка у курсора с % влажности. Создаётся после SetMouseFilterRecursive,
    /// иначе рекурсия выставит ей Ignore... нет, ей и нужен Ignore (не жрёт клики).
    /// TopLevel + ZIndex 200 — летает поверх всего за мышкой.
    /// </summary>
    private void SetupHumidityTooltip()
    {
        _humidityTooltip = new Label
        {
            Name = "HumidityTooltip",
            Text = "",
            Visible = false,
            MouseFilter = MouseFilterEnum.Ignore,
            ZIndex = 200,
            TopLevel = true
        };
        _humidityTooltip.AddThemeColorOverride("font_color", new Color(0.7f, 0.95f, 1f));
        _humidityTooltip.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.9f));
        _humidityTooltip.AddThemeConstantOverride("shadow_offset_x", 1);
        _humidityTooltip.AddThemeConstantOverride("shadow_offset_y", 1);
        _humidityTooltip.AddThemeFontSizeOverride("font_size", 20);
        AddChild(_humidityTooltip);
    }

    private void UpdateHumidityLegend(bool visible)
    {
        if (_humidityLegendLabel != null)
            _humidityLegendLabel.Visible = visible;
        if (!visible && _humidityTooltip != null && MapRenderer.Instance?.CurrentMapMode != MapMode.Fertility)
            _humidityTooltip.Visible = false;
    }

    private void UpdateFertilityLegend(bool visible)
    {
        if (_fertilityLegendLabel != null)
            _fertilityLegendLabel.Visible = visible;
        if (!visible && _humidityTooltip != null && MapRenderer.Instance?.CurrentMapMode != MapMode.Humidity)
            _humidityTooltip.Visible = false;
    }

    /// <summary>
    /// Показать плодородие клетки всплывашкой у курсора (тот же тултип, что
    /// влажность — режимы взаимоисключающие, конфликта нет).
    /// Только в режиме Fertility и только грунт.
    /// </summary>
    public void ShowFertilityAt(int x, int y)
    {
        if (_humidityTooltip == null) return;
        var map = MapRenderer.Instance;
        if (map == null || map.CurrentMapMode != MapMode.Fertility)
        {
            _humidityTooltip.Visible = false;
            return;
        }

        int v = 0;
        bool isGrass = false;
        var ground = map.MapData?.Ground;
        if (ground != null && (uint)x < (uint)ground.GetLength(0) && (uint)y < (uint)ground.GetLength(1))
        {
            isGrass = (ground[x, y] == TileType.Grass);
            if (isGrass && map.Fertility != null)
                v = map.Fertility.Get(x, y);
        }
        else
        {
            // ДВЕРЬ: клетка за картой — через WorldModel (сид/версия/форма мира —
            // единственные). Прежний прямой вызов WorldCellQuery с зашитой «3»
            // отвечал другим миром при смене версии (гард bypass_generator_ui).
            if (Game.Core.World.WorldModel.Seed != 0)
            {
                var cell = Game.Core.World.WorldModel.CellAtLocal(x, y);
                isGrass = (cell.Terrain == Game.Core.WorldStreaming.BaseTerrainKind.Grass);
                if (isGrass)
                {
                    // FIX (bughunt 15-6): ForestQ16 — это плотность ЛЕСА, а не плодородие:
                    // бедная почва без деревьев показывала «0 бедно», а залесённая бедная —
                    // «богато». Берём канонический почвенный сэмплер мира (та же 16×16-сетка
                    // климата, что у генератора/рендера) и переводим Q16 в шкалу 0..200.
                    int soilQ16 = Game.UI.Streaming.StreamingWorldView.SoilFertilityAtCell(
                        Game.Core.World.WorldModel.Seed, Game.Core.World.WorldModel.Version, x, y);
                    if (soilQ16 > 0)
                    {
                        v = (int)((long)soilQ16 * 200L / 65535L);
                    }
                    else
                    {
                        // Фолбэк, если почвенных данных нет: та же осторожная формула, что
                        // у FertilityOverlayRenderer для клеток за кромкой (органика леса
                        // поверх базовых 50), а не чистый ForestQ16 от нуля.
                        v = 50 + (int)((long)cell.ForestQ16 * 150L / 65535L);
                        if (v > 200) v = 200;
                    }
                }
            }
        }

        if (!isGrass)
        {
            _humidityTooltip.Visible = false;
            return;
        }

        string word = v < 60 ? "бедно" : v <= 140 ? "норма" : "богато";
        _humidityTooltip.Text = $"{v} {word}";
        _humidityTooltip.GlobalPosition = GetViewport().GetMousePosition() + new Vector2(16, 16);
        _humidityTooltip.Visible = true;
    }

    /// <summary>
    /// Показать % влажности клетки всплывашкой у курсора.
    /// Работает бесшовно как на острове колонии, так и в стриминговом мире.
    /// </summary>
    public void ShowHumidityAt(int x, int y)
    {
        if (_humidityTooltip == null) return;
        var map = MapRenderer.Instance;
        if (map == null || map.CurrentMapMode != MapMode.Humidity)
        {
            _humidityTooltip.Visible = false;
            return;
        }

        int m = 0;
        bool isGrass = false;
        var ground = map.MapData?.Ground;
        if (ground != null && (uint)x < (uint)ground.GetLength(0) && (uint)y < (uint)ground.GetLength(1))
        {
            isGrass = (ground[x, y] == TileType.Grass);
            if (isGrass && map.Humidity != null)
                m = map.Humidity.Get(x, y);
        }
        else
        {
            // ДВЕРЬ: как в ShowFertilityAt — WorldModel, а не прямой вызов генератора.
            if (Game.Core.World.WorldModel.Seed != 0)
            {
                var terrain = Game.Core.World.WorldModel.TerrainAtLocal(x, y);
                isGrass = (terrain == Game.Core.WorldStreaming.BaseTerrainKind.Grass);
                if (isGrass)
                {
                    ushort moistQ16 = Game.Core.World.WorldModel.MoistureQ16AtLocal(x, y);
                    m = (int)((long)moistQ16 * 200 / 65535);
                }
            }
        }

        if (!isGrass)
        {
            _humidityTooltip.Visible = false;
            return;
        }

        string word = m < 40 ? "сухо" : m <= 130 ? "норма" : "мокро";
        _humidityTooltip.Text = $"{m}% {word}";
        _humidityTooltip.GlobalPosition = GetViewport().GetMousePosition() + new Vector2(16, 16);
        _humidityTooltip.Visible = true;
    }

    private void SetupSpeedControlsLayer()
    {
        // Находим родительский контейнер кнопок времени или поднимаем сами кнопки на верхний слой (ZIndex = 50)
        Control parent = _stopButton?.GetParent() as Control;
        if (parent != null && parent != this)
        {
            _speedButtonsContainer = parent;
            _speedButtonsContainer.ZIndex = 50;
        }
        else
        {
            if (_stopButton != null) _stopButton.ZIndex = 50;
            if (_speed1xButton != null) _speed1xButton.ZIndex = 50;
            if (_speed5xButton != null) _speed5xButton.ZIndex = 50;
            if (_speed25xButton != null) _speed25xButton.ZIndex = 50;
            if (_speed100xButton != null) _speed100xButton.ZIndex = 50;
        }
    }

    private void SetupGardenController()
    {
        _gardenController = new FarmController { Name = "GardenController" };
        AddChild(_gardenController);

        _gardenController.OnOpened += () =>
        {
            CloseAllMenus();
            ClearActiveToolButton();
            SetMainHUDVisible(false);
        };

        _gardenController.OnClosed += () =>
        {
            SetMainHUDVisible(true);
        };

        // WorkZone: bootstrap 1-в-1 как Garden (B5: иначе WorkZoneController.Instance == null и весь UI зон мёртв).
        var workZoneController = new WorkZoneController { Name = "WorkZoneController" };
        AddChild(workZoneController);

        workZoneController.OnOpened += () =>
        {
            CloseAllMenus();
            ClearActiveToolButton();
            SetMainHUDVisible(false);
        };

        workZoneController.OnClosed += () =>
        {
            SetMainHUDVisible(true);
        };

        // Универсальное окно строительства (Build.tscn): открывается при выборе
        // варианта зоны в ZoneContainer. Bootstrap 1-в-1 как Garden/WorkZone.
        _buildMenuController = new BuildMenuController { Name = "BuildMenuController" };
        // Узкий контракт IDraftCoordinator вместо обратной ссылки BuildMenu → HUDController.
        _buildMenuController.BindDraftCoordinator(this);
        AddChild(_buildMenuController);

        bool selectionWasOpen = false;
        bool buildWasOpen = false;

        _buildMenuController.OnOpened += () =>
        {
            selectionWasOpen = _selectionContainer != null && _selectionContainer.Visible;
            buildWasOpen = _buildContainer != null && _buildContainer.Visible;
            CloseAllMenus();
            ClearActiveToolButton();
        };

        _buildMenuController.OnClosed += () =>
        {
            SetMainHUDVisible(true);
            // U2-01: меню стройки открывается только если было открыто до этого
            if (selectionWasOpen && _selectionContainer != null)
                ShowContainer(_selectionContainer);
            if (buildWasOpen && _buildContainer != null)
                ShowContainer(_buildContainer);
        };
    }

    public void SetMainHUDVisible(bool visible)
    {
        if (_buttonConstruct != null) _buttonConstruct.Visible = visible;
        if (_prioritetButton != null) _prioritetButton.Visible = visible;
        if (_populationLabel != null) _populationLabel.Visible = visible;
        if (_employmentLabel != null) _employmentLabel.Visible = visible;
        // task-6: панель «Слои карты» — только по кнопке, не принудительно.
        // При скрытии HUD (окна стройки/фермы) её прячем, но при показе не открываем.
        if (!visible && _mapModePanel != null) _mapModePanel.Visible = false;
        if (_criticalAlertBanner != null && !visible) _criticalAlertBanner.Visible = false;
        if (_oreLegendPanel != null) _oreLegendPanel.Visible = visible && MapRenderer.Instance?.CurrentMapMode == MapMode.Ore;

        if (_amountOfResourcesPanel != null)
        {
            bool shouldShowResources = visible && (_priorityBoard == null || !_priorityBoard.IsOpen);
            _amountOfResourcesPanel.Visible = shouldShowResources;
        }

        if (!visible)
        {
            CloseAllMenus();
        }
    }

    private void SetupHighlightPanel()
    {
        _activeHighlightPanel = new Panel
        {
            Name = "ActiveButtonHighlight",
            MouseFilter = MouseFilterEnum.Ignore
        };
        _activeHighlightPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var style = new StyleBoxFlat
        {
            BgColor = new Color(0f, 0f, 0f, 0.35f),
            DrawCenter = true,
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            BorderColor = new Color(0.1f, 0.1f, 0.1f, 0.95f),
            BorderBlend = false
        };
        _activeHighlightPanel.AddThemeStyleboxOverride("panel", style);
    }

    private void SetupPriorityBoard()
    {
        _priorityBoard = FindChild("Control", true, false) as PriorityBoardController
                         ?? FindChild("PriorityBoard", true, false) as PriorityBoardController;

        if (_priorityBoard == null)
        {
            var scenePaths = new[]
            {
                "res://ui/Prioritet.tscn",
                "res://scenes/ui/Prioritet.tscn",
                "res://Prioritet.tscn",
                "res://scenes/Prioritet.tscn"
            };

            foreach (var path in scenePaths)
            {
                if (ResourceLoader.Exists(path))
                {
                    var scene = ResourceLoader.Load<PackedScene>(path);
                    if (scene != null)
                    {
                        var inst = scene.Instantiate();
                        if (inst is Control ctrl)
                        {
                            AddChild(ctrl);
                            _priorityBoard = new PriorityBoardController();
                            AddChild(_priorityBoard);
                            _priorityBoard.Initialize(ctrl);
                            break;
                        }
                    }
                }
            }
        }
    }

    private void OnPrioritetButtonPressed()
    {
        if (_priorityBoard == null) return;

        if (_priorityBoard.IsOpen)
        {
            _priorityBoard.Close();
            ClearActiveToolButton();
            if (_amountOfResourcesPanel != null && (_gardenController == null || !_gardenController.IsOpen) && (WorkZoneController.Instance == null || !WorkZoneController.Instance.IsOpen))
                _amountOfResourcesPanel.Visible = true;
        }
        else
        {
            ToggleToolButton(_prioritetButton, () =>
            {
                ZoneManager.Instance.DeselectZone();
                if (_amountOfResourcesPanel != null)
                    _amountOfResourcesPanel.Visible = false;
                _priorityBoard.Open();
            });
        }
    }

    private void ToggleToolButton(Button button, Action activateAction)
    {
        if (_activeToolButton == button)
        {
            ClearActiveToolButton();
            if (_priorityBoard != null && _priorityBoard.IsOpen)
            {
                _priorityBoard.Close();
                if (_amountOfResourcesPanel != null && (_gardenController == null || !_gardenController.IsOpen) && (WorkZoneController.Instance == null || !WorkZoneController.Instance.IsOpen))
                    _amountOfResourcesPanel.Visible = true;
            }
            PlayerInteractionManager.Instance?.ResetToDefault();
            return;
        }

        ClearActiveToolButton();
        ZoneManager.Instance.DeselectZone();
        _activeToolButton = button;

        // FIX (bughunt 15-12): после ClearActiveToolButton панель освобождается
        // (QueueFree), поэтому для нового инструмента создаём её заново.
        if (_activeHighlightPanel == null || !GodotObject.IsInstanceValid(_activeHighlightPanel))
        {
            SetupHighlightPanel();
        }

        if (_activeToolButton != null && _activeHighlightPanel != null)
        {
            if (_activeHighlightPanel.GetParent() != null)
                _activeHighlightPanel.GetParent().RemoveChild(_activeHighlightPanel);

            _activeToolButton.AddChild(_activeHighlightPanel);
            _activeHighlightPanel.Visible = true;
        }

        activateAction?.Invoke();
    }

    public void ClearActiveToolButton()
    {
        // FIX (bughunt 15-12): после RemoveChild узел оставался без родителя и жил
        // до конца сессии (утечка ObjectDB). Освобождаем его; при следующем включении
        // инструмента ToggleToolButton создаст панель заново.
        if (_activeHighlightPanel != null && GodotObject.IsInstanceValid(_activeHighlightPanel))
        {
            if (_activeHighlightPanel.GetParent() != null)
                _activeHighlightPanel.GetParent().RemoveChild(_activeHighlightPanel);
            _activeHighlightPanel.Visible = false;
            _activeHighlightPanel.QueueFree();
        }
        _activeHighlightPanel = null;
        _activeToolButton = null;
    }

    // Всплывашка влажности/плодородия следует за курсором каждый кадр.
    // Text не трогаем здесь (только Position) — дёшево.
    private void FollowHumidityTooltip()
    {
        if (_humidityTooltip != null && _humidityTooltip.Visible)
            _humidityTooltip.GlobalPosition = GetViewport().GetMousePosition() + new Vector2(16, 16);
    }

    // §20.7: цифра под курсором протухает если мышь стоит, а камера едет (WASD).
    // SelectTool дёргает Show* только при смене клетки — перезапрашиваем сами
    // раз в 0.25с для клетки под курсором (дешёво, 4 раза в секунду).
    private float _soilTipTimer;

    private void TickSoilTooltip(double delta)
    {
        // R-11: время копим по РЕАЛЬНОЙ дельте кадра. Раньше стояло `+= 1.0f/60.0f`,
        // то есть таймер считал «кадры по 16.7 мс» независимо от настоящего FPS:
        // на 144 Гц обновление приходило в 2.4 раза чаще заявленных 0.25 с, на 30 Гц —
        // вдвое реже, а при просадках подсказка «протухала» рывками.
        if (!double.IsFinite(delta) || delta < 0d) delta = 0d;
        _soilTipTimer += (float)delta;
        if (_soilTipTimer < 0.25f)
            return;
        _soilTipTimer = 0f;
        var map = MapRenderer.Instance;
        if (map?.MapData?.Ground == null)
            return;
        // Мышь вьюпорта → мировые координаты через камеру.
        Vector2 world = GetViewport().GetCamera2D()?.GetScreenCenterPosition()
            + (GetViewport().GetMousePosition() - GetViewport().GetVisibleRect().Size * 0.5f)
                / (GetViewport().GetCamera2D()?.Zoom.X ?? 1f) ?? Vector2.Zero;
        int tx = (int)(world.X / MapRenderer.TileSizePx);
        int ty = (int)(world.Y / MapRenderer.TileSizePx);
        var ground = map.MapData.Ground;
        if ((uint)tx >= (uint)ground.GetLength(0) || (uint)ty >= (uint)ground.GetLength(1))
        {
            if (_humidityTooltip != null)
                _humidityTooltip.Visible = false;
            return;
        }
        if (map.CurrentMapMode == MapMode.Humidity)
            ShowHumidityAt(tx, ty);
        else if (map.CurrentMapMode == MapMode.Fertility)
            ShowFertilityAt(tx, ty);
    }

    public override void _Process(double delta)
    {
        FollowHumidityTooltip();
        TickSoilTooltip(delta);
        FollowOreTooltip();
        TickOreTooltip();
        RefreshOreLegendThrottled((float)delta);
        _statsSyncTimer += (float)delta;
        // П.8: некритичные показатели (население/занятость) — 1с вместо 250мс,
        // время мира — отдельно 0.5с (дёргать Label каждый кадр незачем).
        if (_statsSyncTimer >= 1.0f)
        {
            _statsSyncTimer = 0f;
            RefreshStats();
        }
        _timeSyncTimer += (float)delta;
        if (_timeSyncTimer >= 0.5f)
        {
            _timeSyncTimer = 0f;
            UpdateWorldTimeDisplay();
        }
    }

    private void RefreshStats()
    {
        // E67: замер HUD-обновлений — Label-строки сами могут тормозить.
        var swH = System.Diagnostics.Stopwatch.StartNew();

        // J-07: «занято» больше НЕ считается как `население − бездельники`.
        // В плановое население попадали еще не заспавненные агенты, а в «занятые» —
        // все, кого нет в пуле свободных: Worker'ы зон (они по дизайну не в пуле),
        // эвакуирующиеся, агенты с маршрутами. Игрок видел 200–300 «работа.т»
        // при отсутствии реальной работы. Теперь берём счётчики из sim-потока:
        // реальный размер пула и число агентов, которые действительно держат работу.
        var (popActual, working, idleStat, _) = Game.Simulation.SimStats.Snapshot();

        int total = popActual > 0 ? popActual : _totalPopulation;
        int employed = Math.Clamp(working, 0, total);
        int unemployed = Math.Max(0, total - employed);

        string pop = FormatAmount(total);
        if (pop != _lastPopText) { _lastPopText = pop; if (_populationLabel != null) _populationLabel.Text = pop; }
        string emp = FormatAmount(employed) + "/" + FormatAmount(unemployed);
        // GPU-трек удалён (GpuStatsReduce): строка employment — бит-в-бит как раньше.
        if (_employmentLabel != null && _employmentLabel.Text != emp) _employmentLabel.Text = emp;

        UpdateResourcePanel();
        UpdateCriticalAlerts();
        UpdateWorldTimeDisplayThrottled();
        swH.Stop();
        if (swH.Elapsed.TotalMilliseconds > 1.0)
            Game.Core.SimEvents.Record("render", "HUD.RefreshStats", (float)swH.Elapsed.TotalMilliseconds);
    }

    private void UpdateWorldTimeDisplayThrottled()
    {
        // Время обновляется своим таймером 0.5с — здесь только если таймер уже тикнул.
        if (_timeSyncTimer <= 0.001f) UpdateWorldTimeDisplay();
    }

    /// <summary>
    /// Обновляет «время в мире»: день и часы/минуты суток (сутки = 24 игровых часа).
    /// Работает при любой скорости: на 100x сутки идут ровно 2 реальные минуты.
    /// </summary>
    private void UpdateWorldTimeDisplay()
    {
        if (_worldTimeLabel == null || TimeManager.Instance == null)
            return;

        var tm = TimeManager.Instance;
        int day = tm.DayNumber;
        float tod = tm.TimeOfDaySeconds;
        int hours = (int)(tod / WorldTime.SecondsPerHour);
        int minutes = (int)((tod % WorldTime.SecondsPerHour) / WorldTime.SecondsPerHour * 60f);
        string icon = DayNightCycle.IsNight(tod) ? "☾" : "☀";

        string text = $"{icon} День {day}, {hours:00}:{minutes:00}";
        if (text == _lastTimeText) return;
        _lastTimeText = text;
        _worldTimeLabel.Text = text;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && key.Keycode == Key.Escape)
        {
            bool handled = false;

            // Окно выбора отрасли закрывается ESC в первую очередь.
            if (_zoneContainer != null && _zoneContainer.Visible)
            {
                CloseZoneModal();
                GetViewport().SetInputAsHandled();
                return;
            }

            if ((_gardenController != null && _gardenController.IsOpen) ||
                (WorkZoneController.Instance != null && WorkZoneController.Instance.IsOpen))
            {
                ZoneManager.Instance.DeselectZone();
                handled = true;
            }

            if ((_selectionContainer != null && _selectionContainer.Visible) || _activeToolButton != null || (_priorityBoard != null && _priorityBoard.IsOpen))
            {
                CloseAllMenus();
                if (_priorityBoard != null && _priorityBoard.IsOpen)
                {
                    _priorityBoard.Close();
                    if (_amountOfResourcesPanel != null && (_gardenController == null || !_gardenController.IsOpen) && (WorkZoneController.Instance == null || !WorkZoneController.Instance.IsOpen))
                        _amountOfResourcesPanel.Visible = true;
                }
                ClearActiveToolButton();
                PlayerInteractionManager.Instance?.ResetToDefault();
                handled = true;
            }

            if (handled)
            {
                GetViewport().SetInputAsHandled();
            }
        }
    }

    private void OnStockpileItemCountChanged(ItemId itemId, int totalCount)
    {
        // Любое изменение склада (сырьё, руда, товар) — обновляем панель ресурсов.
        // Считаем из O(1) кэша тоталов, строки сравниваются по тексту: лишних
        // перерисовок нет (тета 200 мс на тяжёлые панели не нужна — тут O(90)).
        UpdateResourcePanel();
    }

    private void UpdateWoodDisplay(int count)
    {
        // Дерево теперь показывается общей строкой панели ресурсов.
        UpdateResourcePanel();
    }

    private static string FormatAmount(int count)
    {
        if (count < 1000)
            return count.ToString();

        if (count < 1_000_000)
        {
            float kValue = count / 1000f;
            return kValue.ToString("0.##", CultureInfo.InvariantCulture) + "k";
        }

        float mValue = count / 1_000_000f;
        return mValue.ToString("0.##", CultureInfo.InvariantCulture) + "M";
    }

    private void OnButtonConstructPressed()
    {
        if (_selectionContainer != null && _selectionContainer.Visible)
        {
            CloseAllMenus();
            ClearActiveToolButton();
            PlayerInteractionManager.Instance?.ResetToDefault();
        }
        else
        {
            CloseAllMenus();
            ShowContainer(_selectionContainer);
        }
    }

    private void OnBuildVariantPressed()
    {
        if (_buildContainer != null && _buildContainer.Visible)
        {
            CloseTier2AndSub();
        }
        else
        {
            CloseTier2AndSub();
            HideContainer(_orderContainer);
            ShowContainer(_buildContainer);
        }
    }

    private void OnOrderVariantPressed()
    {
        if (_orderContainer != null && _orderContainer.Visible)
        {
            HideContainer(_orderContainer);
        }
        else
        {
            CloseTier2AndSub();
            ShowContainer(_orderContainer);
        }
    }

    private void ToggleSubContainer(Control targetSub)
    {
        if (targetSub != null && targetSub.Visible)
        {
            HideContainer(targetSub);
        }
        else
        {
            HideAllSubContainers();
            ShowContainer(targetSub);
        }
    }

    private static void ShowContainer(Control control)
    {
        if (control == null) return;
        control.Visible = true;
    }

    private static void HideContainer(Control control)
    {
        if (control == null) return;
        control.Visible = false;
    }

    private void CloseTier2AndSub()
    {
        HideContainer(_buildContainer);
        HideAllSubContainers();
    }

    private void HideAllSubContainers()
    {
        HideContainer(_wallContainer);
        HideContainer(_zoneContainer);
        HideContainer(_industrialContainer);
    }

    private void CloseAllMenus()
    {
        HideContainer(_selectionContainer);
        HideContainer(_buildContainer);
        HideContainer(_orderContainer);
        HideAllSubContainers();
    }

    /// <summary>Скрыть все контейнеры HUD (для открытия мировой карты).</summary>
    public void HideContainers()
    {
        CloseAllMenus();
        foreach (Control ctrl in GetChildren())
        {
            string n = ctrl.Name;
            if (n == "GardenController" || n == "WorkZoneController" || n == "BuildMenuController")
                ctrl.Visible = false;
        }
        HideContainer(FindChild("AmountOfResources", true, false) as Control);
        // task-6: вместе с HUD прячем и панель режимов карты, и легенду руд.
        if (_mapModePanel != null) _mapModePanel.Visible = false;
        if (_oreLegendPanel != null) _oreLegendPanel.Visible = false;
    }

    private void OnWoodWallPressed()
    {
        ToggleToolButton(_woodWallButton, () =>
        {
            var interaction = PlayerInteractionManager.Instance;
            var mapRenderer = MapRenderer.Instance;
            if (interaction == null || mapRenderer?.WallBuildManager == null || mapRenderer?.MapData == null) return;

            var buildTool = new BuildTool(
                mapRenderer.WallBuildManager,
                mapRenderer.GhostLayer,
                mapRenderer.MapData,
                BuildingType.WoodWall,
                MapRenderer.SourceWall
            );
            interaction.SetTool(buildTool);
            buildTool.WorldPlacement = WorldPlacement;
        });
    }

    private void OnWorkTablePressed()
    {
        ToggleToolButton(_workTableButton, () =>
        {
            var interaction = PlayerInteractionManager.Instance;
            var mapRenderer = MapRenderer.Instance;
            if (interaction == null || mapRenderer?.WallBuildManager == null || mapRenderer?.MapData == null) return;

            var tableTool = new BuildTool(
                mapRenderer.WallBuildManager,
                mapRenderer.GhostLayer,
                mapRenderer.MapData,
                BuildingType.WorkTable,
                MapRenderer.SourceWorkTable
            );
            interaction.SetTool(tableTool);
            tableTool.WorldPlacement = WorldPlacement;
        });
    }

    /// <summary>
    /// Кнопки мебели (кровать/скамья/тумбочка) в IndustrialContainer.
    /// Сцену не трогаем — создаём кодом 1-в-1 как WorkTableButton (100x100).
    /// Иконки грузятся по тем же UID, что кладутся в мир.
    /// </summary>
    private void SetupFurnitureButtons()
    {
        if (_industrialContainer == null) return;
        _bedButton = CreateFurnitureButton("BedButton", "uid://0uf8ok6dxhyi", OnBedPressed);
        _benchButton = CreateFurnitureButton("BenchButton", "uid://bi667xckuklur", OnBenchPressed);
        _nightstandButton = CreateFurnitureButton("NightstandButton", "uid://ir5jk74j01fg", OnNightstandPressed);
    }

    private Button CreateFurnitureButton(string nodeName, string textureUid, Action onPressed)
    {
        var btn = new Button { Name = nodeName, CustomMinimumSize = new Vector2(100, 100), ExpandIcon = true };
        try
        {
            var tex = ResourceLoader.Load<Texture2D>(textureUid);
            if (tex != null) btn.Icon = tex;
        }
        catch { /* без иконки кнопка всё равно работает (текст ниже) */ }
        if (btn.Icon == null) btn.Text = nodeName;
        btn.MouseFilter = MouseFilterEnum.Stop;
        btn.Pressed += () => onPressed();
        _industrialContainer.AddChild(btn);
        return btn;
    }

    // ================= task-13: ОТРАСЛИ зон в HUD =================

    /// <summary>
    /// Кнопки ОТРАСЛЕЙ в ZoneContainer. Список — из ZoneIndustryRegistry.All
    /// (единственный источник истины): добавили отрасль в профиль — кнопка
    /// появилась сама, HUD править не нужно (требование владельца/Lead).
    /// Стиль как в PatentBureau.tscn: тёмная панель, тонкая рамка, иконка + подпись.
    /// </summary>
    private void SetupIndustryButtons()
    {
        _industryGrid = FindChild("IndustryGrid", true, false) as GridContainer;
        if (_industryGrid == null)
            GD.PushWarning("[HUD] IndustryGrid не найден в hud.tscn — окно зон будет пустым.");
        else
            RebuildIndustryButtons();

        // Закрытие окна: кнопка «Закрыть» и клик по затемнению.
        if (FindChild("CloseButton", true, false) is Button close)
        {
            close.MouseFilter = MouseFilterEnum.Stop;
            close.Pressed += CloseZoneModal;
        }
        if (_zoneContainer?.FindChild("Dim", true, false) is Control dim)
        {
            dim.MouseFilter = MouseFilterEnum.Stop;
            dim.GuiInput += (InputEvent e) =>
            {
                if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
                    CloseZoneModal();
            };
        }
        if (_zoneContainer != null)
            _zoneContainer.Visible = false;
        ZoneManager.Instance.OnZoneSelected += OnWorldZoneSelected;
    }

    /// <summary>Открыть/закрыть окно выбора отрасли («Зоны» в меню стройки).</summary>
    private void ToggleZoneModal()
    {
        if (_zoneContainer == null) return;
        if (_zoneContainer.Visible)
        {
            CloseZoneModal();
            return;
        }
        RebuildIndustryButtons();
        _zoneContainer.Visible = true;
        _zoneContainer.MoveToFront();
    }

    /// <summary>Закрыть окно выбора отрасли.</summary>
    private void CloseZoneModal()
    {
        if (_zoneContainer != null)
            _zoneContainer.Visible = false;
    }

    /// <summary>Пересобрать кнопки отраслей (состав каталога мог измениться).</summary>
    public void RebuildIndustryButtons()
    {
        if (_industryGrid == null) return;
        foreach (var child in _industryGrid.GetChildren())
        {
            if (child is Node node) node.QueueFree();
        }
        _industryButtons.Clear();

        var all = ZoneIndustryRegistry.All;
        for (int i = 0; i < all.Count; i++)
        {
            var profile = all[i];
            var cell = new VBoxContainer { Name = "Industry_" + profile.Id };
            cell.AddThemeConstantOverride("separation", 2);

            var btn = new Button
            {
                Name = "IndustryButton_" + profile.Id,
                CustomMinimumSize = new Vector2(100, 100),
                ExpandIcon = true,
                MouseFilter = MouseFilterEnum.Stop,
                TooltipText = profile.Description.Length > 0
                    ? $"{profile.Title}\n{profile.Description}"
                    : profile.Title
            };
            var tex = LoadTextureOrNull(profile.IconPath);
            if (tex != null) btn.Icon = tex;
            else btn.Text = profile.Title;
            ApplyIndustryButtonStyle(btn);
            var captured = profile;
            btn.Pressed += () => OnIndustryPressed(captured);

            var label = new Label
            {
                Text = profile.Title,
                MouseFilter = MouseFilterEnum.Ignore,
                HorizontalAlignment = HorizontalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                CustomMinimumSize = new Vector2(100, 0)
            };
            label.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.52f));
            label.AddThemeFontSizeOverride("font_size", 13);

            cell.AddChild(btn);
            cell.AddChild(label);
            _industryGrid.AddChild(cell);
            _industryButtons[profile.Kind] = btn;
        }
    }

    private static Texture2D LoadTextureOrNull(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        try { return ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null; }
        catch { return null; }
    }

    /// <summary>Стиль кнопки отрасли — цвета PatentBureau.tscn (SB_button/hover/pressed).</summary>
    private static void ApplyIndustryButtonStyle(Button btn)
    {
        if (btn == null) return;
        btn.AddThemeStyleboxOverride("normal",
            MakeFlat(new Color(0.043f, 0.043f, 0.047f), new Color(0.211f, 0.211f, 0.223f)));
        btn.AddThemeStyleboxOverride("hover",
            MakeFlat(new Color(0.101f, 0.101f, 0.113f), new Color(0.5f, 0.5f, 0.533f)));
        btn.AddThemeStyleboxOverride("pressed",
            MakeFlat(new Color(0.012f, 0.012f, 0.016f), new Color(0.909f, 0.909f, 0.909f)));
        btn.AddThemeStyleboxOverride("focus",
            MakeFlat(new Color(0.043f, 0.043f, 0.047f), new Color(0.5f, 0.5f, 0.533f)));
    }

    private static StyleBoxFlat MakeFlat(Color bg, Color border)
    {
        var box = new StyleBoxFlat { BgColor = bg, BorderColor = border };
        box.SetBorderWidthAll(1);
        box.SetContentMarginAll(6f);
        return box;
    }

    /// <summary>Клик по отрасли: окно стройки фильтруется по этой зоне + черновик её зоны.</summary>
    private void OnIndustryPressed(ZoneIndustryProfile profile)
    {
        if (profile == null) return;
        // Выбрал отрасль — окно закрывается, дальше стройка + инструмент зоны.
        CloseZoneModal();
        _buildMenuController?.Open(profile.Id, profile.Title);
        // Клик по отрасли = «создать зону этой отрасли»: черновик с ZoneKind.
        // У жилья основной работы нет — рабочую зону не создаём, только окно стройки.
        if (ZoneIndustryRegistry.PrimaryJobMask(profile.Kind) != 0)
            StartZoneDraft(profile.Kind);
        HighlightIndustryButton(profile.Kind);
    }

    /// <summary>Подсветить выбранную отрасль (активная — зелёная, остальные — белые).</summary>
    private void HighlightIndustryButton(ZoneKind kind)
    {
        ZoneKind normalized = kind.NormalizeLegacy();
        foreach (var kv in _industryButtons)
        {
            bool active = kv.Key == normalized;
            kv.Value.Modulate = active ? new Color(0.6f, 1f, 0.6f, 1f) : Colors.White;
        }
    }

    /// <summary>
    /// Выбрали зону в мире — окно стройки (если открыто) моментально переключает
    /// фильтр на её отрасль: «выбрал зону А → в окне только предметы зоны А».
    /// </summary>
    private void OnWorldZoneSelected(Zone zone)
    {
        if (zone == null) return;
        HighlightIndustryButton(zone.Kind);
        if (_buildMenuController != null && _buildMenuController.IsOpen)
            _buildMenuController.SetZoneContext(zone.Kind, ZoneIndustryRegistry.TitleFor(zone.Kind));
    }

    /// <summary>
    /// Кнопка добычи камня в OrderContainer рядом с рубкой.
    /// Иконка — первый квадрант stone.png (тот же, что падает на землю).
    /// </summary>
    private void SetupStoneMiningButton()
    {
        if (_orderContainer == null) return;
        _stoneMiningButton = new Button { Name = "StoneMiningButton", CustomMinimumSize = new Vector2(100, 100), ExpandIcon = true };
        try
        {
            var atlas = ResourceLoader.Load<Texture2D>("uid://du6ur8mvtu3le");
            if (atlas != null)
                _stoneMiningButton.Icon = new AtlasTexture
                {
                    Atlas = atlas,
                    Region = new Rect2(0, 0, MapRenderer.TileSizePx, MapRenderer.TileSizePx)
                };
        }
        catch { }
        if (_stoneMiningButton.Icon == null) _stoneMiningButton.Text = "Камень";
        _stoneMiningButton.MouseFilter = MouseFilterEnum.Stop;
        _stoneMiningButton.Pressed += OnStoneMiningPressed;
        _orderContainer.AddChild(_stoneMiningButton);
    }

    private void StartFurnitureBuild(Button button, BuildingType type, int sourceId)
    {
        ToggleToolButton(button, () =>
        {
            var interaction = PlayerInteractionManager.Instance;
            var mapRenderer = MapRenderer.Instance;
            if (interaction == null || mapRenderer?.WallBuildManager == null || mapRenderer?.MapData == null) return;

            // Ghost мебели — через building-слой (источники 14–16 только там).
            var tool = new BuildTool(
                mapRenderer.WallBuildManager,
                mapRenderer.GhostLayer,
                mapRenderer.MapData,
                type,
                sourceId
            );
            interaction.SetTool(tool);
            tool.WorldPlacement = WorldPlacement;
        });
    }

    private void OnBedPressed() => StartFurnitureBuild(_bedButton, BuildingType.Bed, MapRenderer.SourceBed);
    private void OnBenchPressed() => StartFurnitureBuild(_benchButton, BuildingType.Bench, MapRenderer.SourceBench);
    private void OnNightstandPressed() => StartFurnitureBuild(_nightstandButton, BuildingType.Nightstand, MapRenderer.SourceNightstand);

    private void OnStoneMiningPressed()
    {
        ToggleToolButton(_stoneMiningButton, () =>
        {
            var interaction = PlayerInteractionManager.Instance;
            var mapRenderer = MapRenderer.Instance;
            if (interaction == null || mapRenderer?.MapData == null) return;

            var stoneTool = new StoneMiningTool(interaction.Selection, mapRenderer.MapData);
            interaction.SetTool(stoneTool);
            stoneTool.WorldPlacement = WorldPlacement;
        });
    }

    // ================= task-4: добыча руды =================

    /// <summary>
    /// Кнопка добычи руды в OrderContainer рядом с камнем. Иконка — спрайт дропа
    /// железной руды (res:// путь, без выдуманных uid); нет арта — текст «Руда».
    /// </summary>
    private void SetupOreMiningButton()
    {
        if (_orderContainer == null) return;
        _oreMiningButton = new Button
        {
            Name = "OreMiningButton",
            CustomMinimumSize = new Vector2(100, 100),
            ExpandIcon = true
        };
        try
        {
            var tex = ResourceLoader.Load<Texture2D>("res://ui/assets/textures/drop/iron_hematite.png");
            if (tex != null)
                _oreMiningButton.Icon = tex;
        }
        catch { }
        if (_oreMiningButton.Icon == null) _oreMiningButton.Text = "Руда";
        _oreMiningButton.MouseFilter = MouseFilterEnum.Stop;
        _oreMiningButton.Pressed += OnOreMiningPressed;
        _orderContainer.AddChild(_oreMiningButton);
    }

    private void OnOreMiningPressed()
    {
        ToggleToolButton(_oreMiningButton, () =>
        {
            var interaction = PlayerInteractionManager.Instance;
            var mapRenderer = MapRenderer.Instance;
            if (interaction == null || mapRenderer?.MapData == null) return;

            var oreTool = new OreMiningTool(interaction.Selection, mapRenderer.MapData);
            interaction.SetTool(oreTool);
            oreTool.WorldPlacement = WorldPlacement;
        });
    }

    // ================= панель ресурсов: ВСЕ ресурсы и товары =================

    /// <summary>Один счётчик в панели ресурсов: число + последний текст (анти-дребезг).</summary>
    private sealed class ResourceCell
    {
        public ItemId Item;
        public Label Amount;
        public string Last = "";
    }

    private readonly List<ResourceCell> _resourceCells = new(ItemConfig.MaxItemTypes);
    private VBoxContainer _resourceCategories;

    /// <summary>
    /// Панель «Ресурсы»: все предметы, по категориям, по 3 счётчика в ряд.
    /// Контейнеры (ScrollContainer + VBox «Categories») лежат в hud.tscn — код
    /// только наполняет их строками. Число = суммарное количество на ВСЕХ складах.
    ///
    /// Источники данных: сырьё — атлас ресурсов (единственный источник названий и
    /// категорий), товары — ItemRegistry (диапазон FirstGoodsItemId..MaxItemTypes-1).
    /// </summary>
    private void SetupResourcePanel()
    {
        _resourceCategories = _amountOfResourcesPanel?.FindChild("Categories", true, false) as VBoxContainer;
        if (_resourceCategories == null)
        {
            GD.PushWarning("[HUD] Categories не найден в hud.tscn — панель ресурсов пустая.");
            return;
        }

        // id ресурса в атласе → ItemId (алиасы legacy: wheat→Grain, stone→Stone,
        // wood_softwood→Log — в атласе они названы по-своему).
        var byResourceId = new Dictionary<string, ItemId>(StringComparer.Ordinal);
        for (int v = ItemConfig.FirstRawItemId; v <= ItemConfig.LastRawItemId; v++)
            byResourceId[RawItemDefinitions.ResourceId((ItemId)v)] = (ItemId)v;
        byResourceId["wheat"] = ItemId.Grain;
        byResourceId["stone"] = ItemId.Stone;
        byResourceId["wood_softwood"] = ItemId.Log;

        var categories = new List<(string Title, List<ItemId> Items)>(12);
        List<ItemId> current = null;
        string currentTitle = null;
        IReadOnlyList<Game.UI.Resources.ResourceAtlasEntry> entries =
            Game.UI.Resources.ResourceAtlasCatalog.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            Game.UI.Resources.ResourceAtlasEntry e = entries[i];
            if (!byResourceId.TryGetValue(e.Id, out ItemId item) || item == ItemId.None)
                continue;
            if (current == null || !string.Equals(currentTitle, e.Category, StringComparison.Ordinal))
            {
                current = new List<ItemId>(12);
                currentTitle = e.Category;
                categories.Add((string.IsNullOrEmpty(e.Category) ? "Прочее" : e.Category, current));
            }
            current.Add(item);
        }

        // Товары производства — отдельной группой в конце.
        var goods = new List<ItemId>(ItemConfig.MaxItemTypes - ItemConfig.FirstGoodsItemId);
        for (int v = ItemConfig.FirstGoodsItemId; v < ItemConfig.MaxItemTypes; v++)
        {
            if (ItemRegistry.TryGet((ItemId)v, out _))
                goods.Add((ItemId)v);
        }
        if (goods.Count > 0)
            categories.Add(("Товары", goods));

        // Строки: заголовок категории + GridContainer по 3 счётчика в ряд.
        for (int c = 0; c < categories.Count; c++)
        {
            var header = new Label { Name = "Cat_" + c, Text = categories[c].Title };
            header.AddThemeFontSizeOverride("font_size", 17);
            header.AddThemeColorOverride("font_color", new Color(0.72f, 0.72f, 0.75f, 1f));
            _resourceCategories.AddChild(header);

            var grid = new GridContainer { Name = "CatGrid_" + c, Columns = 3 };
            grid.AddThemeConstantOverride("h_separation", 4);
            grid.AddThemeConstantOverride("v_separation", 4);
            _resourceCategories.AddChild(grid);

            var items = categories[c].Items;
            for (int i = 0; i < items.Count; i++)
                grid.AddChild(CreateResourceCell(items[i]));
        }

        UpdateResourcePanel();
    }

    /// <summary>Счётчик одного предмета: иконка 24×24 + число; полное имя — в тултипе.</summary>
    private Control CreateResourceCell(ItemId item)
    {
        ItemRegistry.TryGet(item, out ItemDefinition def);
        string title = def?.Name ?? item.ToString();

        var cell = new HBoxContainer { Name = "Res_" + (int)item };
        cell.CustomMinimumSize = new Vector2(82, 30);
        cell.AddThemeConstantOverride("separation", 3);
        cell.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        cell.TooltipText = title;

        var icon = new TextureRect
        {
            CustomMinimumSize = new Vector2(24, 24),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        try
        {
            string resourceId = RawItemDefinitions.ResourceId(item);
            icon.Texture = ItemRegistry.IsOreItem(item) || item == ItemId.Log || item == ItemId.Grain ||
                           item == ItemId.Stone
                ? Game.UI.Resources.ResourceAtlasCatalog.TextureForId(resourceId)
                : LoadTextureOrNull(def?.TextureUid);
        }
        catch { /* без иконки счётчик остаётся читаемым */ }
        cell.AddChild(icon);

        var amount = new Label
        {
            Text = "0",
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        amount.AddThemeFontSizeOverride("font_size", 15);
        amount.AddThemeColorOverride("font_color", new Color(0.949f, 0.949f, 0.949f, 1f));
        cell.AddChild(amount);

        _resourceCells.Add(new ResourceCell { Item = item, Amount = amount });
        return cell;
    }

    /// <summary>Обновить все счётчики из O(1) кэша тоталов склада (сумма по всем складам).</summary>
    private void UpdateResourcePanel()
    {
        for (int i = 0; i < _resourceCells.Count; i++)
        {
            ResourceCell cell = _resourceCells[i];
            if (cell.Amount == null)
                continue;
            string text = FormatAmount(StockpileManager.Instance.GetTotalItemCount(cell.Item));
            if (text == cell.Last)
                continue;
            cell.Last = text;
            cell.Amount.Text = text;
        }
    }

    // ================= окна «Биржа» и «Патентное бюро» =================

    private Button _exchangeButton;
    private Button _patentButton;
    private Control _exchangeWindow;
    private Control _patentWindow;

    /// <summary>
    /// Кнопки открытия Биржи и Патентного бюро. Сцены грузятся лениво при первом
    /// нажатии: на старте игры они не нужны и не должны стоить памяти.
    /// </summary>
    private void SetupSpecialWindows()
    {
        // Кнопки Биржи и Патентного бюро — узлы самой сцены hud.tscn.
        // Программное создание оставлено только как аварийный фолбэк.
        _exchangeButton = FindChild("ExchangeButton", true, false) as Button;
        if (_exchangeButton == null)
        {
            _exchangeButton = MakeSpecialButton(
                "ExchangeButton",
                "res://ui/assets/textures/icons/exchange.png",
                "Биржа");
            AddChild(_exchangeButton);
        }
        _exchangeButton.Pressed += OnExchangePressed;

        _patentButton = FindChild("PatentButton", true, false) as Button;
        if (_patentButton == null)
        {
            _patentButton = MakeSpecialButton(
                "PatentButton",
                "res://ui/assets/textures/icons/PatentтOffice.png",
                "Патентное бюро");
            AddChild(_patentButton);
        }
        _patentButton.Pressed += OnPatentPressed;
    }

    private static Button MakeSpecialButton(string name, string iconPath, string tooltip)
    {
        var button = new Button
        {
            Name = name,
            TooltipText = tooltip,
            CustomMinimumSize = new Vector2(75, 75),
            ExpandIcon = true,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        if (ResourceLoader.Exists(iconPath))
            button.Icon = ResourceLoader.Load<Texture2D>(iconPath);
        else
            button.Text = tooltip; // иконки нет — хотя бы подпись, кнопка остаётся живой
        return button;
    }

    private void OnExchangePressed()
    {
        if (_exchangeWindow == null)
        {
            var packed = ResourceLoader.Load<PackedScene>("res://scenes/ui/Exchange.tscn");
            if (packed == null)
            {
                GD.PushWarning("[HUD] Exchange.tscn не найден.");
                return;
            }
            _exchangeWindow = packed.Instantiate<Control>();
            AddChild(_exchangeWindow);
        }

        if (_exchangeWindow is ExchangeController exchange)
        {
            if (exchange.IsOpen) exchange.Close();
            else exchange.Open();
        }
        else
        {
            _exchangeWindow.Visible = !_exchangeWindow.Visible;
        }
    }

    private void SetupVillagerInspector()
    {
        string[] paths = {
            "res://scenes/ui/villager.tscn",
            "res://ui/villager.tscn",
            "res://villager.tscn"
        };
        foreach (var p in paths)
        {
            if (ResourceLoader.Exists(p))
            {
                var scene = ResourceLoader.Load<PackedScene>(p);
                if (scene != null)
                {
                    var inst = scene.Instantiate<Control>();
                    if (inst != null)
                    {
                        AddChild(inst);
                        var ctrl = new VillagerInspectorController();
                        AddChild(ctrl);
                        ctrl.Initialize(inst);
                        break;
                    }
                }
            }
        }
    }

    public static void ShowFloatingWarning(string text, Vector2? screenPos = null, Color? color = null)
    {
        if (Instance == null) return;
        var label = new Label
        {
            Text = text,
            TopLevel = true,
            ZIndex = 500,
            MouseFilter = MouseFilterEnum.Ignore
        };
        label.AddThemeColorOverride("font_color", color ?? new Color(1f, 0.25f, 0.25f, 1f));
        label.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.9f));
        label.AddThemeConstantOverride("shadow_offset_x", 1);
        label.AddThemeConstantOverride("shadow_offset_y", 1);
        label.AddThemeFontSizeOverride("font_size", 18);

        Vector2 pos = screenPos ?? Instance.GetViewport().GetMousePosition();
        label.Position = pos + new Vector2(16, -16);
        Instance.AddChild(label);

        var tween = Instance.CreateTween();
        tween.TweenProperty(label, "position:y", label.Position.Y - 32f, 1.2f);
        tween.Parallel().TweenProperty(label, "modulate:a", 0f, 1.2f).SetDelay(0.4f);
        tween.TweenCallback(Callable.From(label.QueueFree));
    }

    /// <summary>
    /// UI-20: Создание панели критических алертов колонии в верхней части HUD.
    /// </summary>
    private void SetupCriticalAlertBanner()
    {
        _criticalAlertBanner = new PanelContainer
        {
            Name = "CriticalAlertBanner",
            ZIndex = 120,
            Visible = false,
            MouseFilter = MouseFilterEnum.Stop
        };
        _criticalAlertBanner.SetAnchorsPreset(LayoutPreset.CenterTop);
        _criticalAlertBanner.AnchorLeft = 0.5f;
        _criticalAlertBanner.AnchorRight = 0.5f;
        _criticalAlertBanner.AnchorTop = 0f;
        _criticalAlertBanner.AnchorBottom = 0f;
        _criticalAlertBanner.OffsetLeft = -280f;
        _criticalAlertBanner.OffsetRight = 280f;
        _criticalAlertBanner.OffsetTop = 50f;
        _criticalAlertBanner.OffsetBottom = 86f;

        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.55f, 0.08f, 0.08f, 0.92f),
            BorderColor = new Color(1.0f, 0.35f, 0.35f, 1.0f),
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 4
        };
        style.SetBorderWidthAll(2);
        style.SetContentMarginAll(6f);
        _criticalAlertBanner.AddThemeStyleboxOverride("panel", style);

        _criticalAlertLabel = new Label
        {
            Name = "CriticalAlertLabel",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore
        };
        _criticalAlertLabel.AddThemeColorOverride("font_color", new Color(1f, 0.95f, 0.95f, 1f));
        _criticalAlertLabel.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.9f));
        _criticalAlertLabel.AddThemeConstantOverride("shadow_offset_x", 1);
        _criticalAlertLabel.AddThemeConstantOverride("shadow_offset_y", 1);
        _criticalAlertLabel.AddThemeFontSizeOverride("font_size", 16);

        _criticalAlertBanner.AddChild(_criticalAlertLabel);

        _criticalAlertBanner.GuiInput += (InputEvent @event) =>
        {
            if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            {
                FocusOnCriticalProblem();
            }
        };

        AddChild(_criticalAlertBanner);
    }

    /// <summary>
    /// UI-20: Проверка и обновление состояния критических алертов колонии.
    /// </summary>
    private void UpdateCriticalAlerts()
    {
        if (_criticalAlertBanner == null || _criticalAlertLabel == null)
            return;

        int foodInStockpile = StockpileManager.Instance.GetTotalItemCount(ItemId.Bread) +
                              StockpileManager.Instance.GetTotalItemCount(ItemId.Grain);
        bool hasGroundFood = GroundItemManager.Instance != null &&
                             (GroundItemManager.Instance.HasAvailableItemsOfType(ItemId.Bread) ||
                              GroundItemManager.Instance.HasAvailableItemsOfType(ItemId.Grain));
        int starving = SimStats.Starving;

        string alertText = null;
        if (foodInStockpile == 0)
        {
            if (starving > 0)
            {
                alertText = $"⚠️ КРИТИЧЕСКИЙ АЛЕРТ: Запасы еды — 0! Голодают: {starving} чел.";
            }
            else if (!hasGroundFood)
            {
                alertText = "⚠️ ВНИМАНИЕ: Все запасы еды в колонии исчерпаны (0 ед.)!";
            }
            else if (StockpileManager.Instance.ZoneTileCount > 0)
            {
                alertText = "⚠️ ВНИМАНИЕ: На складе нет запасов еды (0 ед.)!";
            }
        }
        else if (starving > 0)
        {
            alertText = $"⚠️ ВНИМАНИЕ: В колонии голодают жители ({starving} чел.)!";
        }

        if (!string.IsNullOrEmpty(alertText))
        {
            _criticalAlertBanner.Visible = true;
            _criticalAlertLabel.Text = alertText;

            if (alertText != _lastCriticalAlert)
            {
                _lastCriticalAlert = alertText;
                Vector2 bannerCenter = new Vector2(GetViewport().GetVisibleRect().Size.X * 0.5f - 180f, 95f);
                ShowFloatingWarning(alertText, bannerCenter, new Color(1f, 0.3f, 0.3f, 1f));
            }
        }
        else
        {
            _criticalAlertBanner.Visible = false;
            _lastCriticalAlert = "";
        }
    }

    /// <summary>
    /// UI-20: Центрирование камеры на источнике проблемы при клике по баннеру алерта.
    /// </summary>
    private void FocusOnCriticalProblem()
    {
        var camera = GetViewport()?.GetCamera2D();
        if (camera == null) return;

        var pool = AgentSimulationThread.Instance?.Pool;
        if (pool != null)
        {
            for (int i = 0; i < pool.Capacity; i++)
            {
                if (pool.Hunger[i] >= 80f)
                {
                    camera.Position = new Vector2(pool.PositionX[i], pool.PositionY[i]);
                    VillagerInspectorController.Instance?.Open(i);
                    ShowFloatingWarning("Переход к голодающему жителю", null, new Color(1f, 0.8f, 0.3f, 1f));
                    return;
                }
            }
        }

        foreach (var zone in ZoneManager.Instance.GetAllZones())
        {
            if (zone.Kind == ZoneKind.Farm)
            {
                camera.Position = new Vector2(zone.CenterX * MapRenderer.TileSizePx, zone.CenterY * MapRenderer.TileSizePx);
                ZoneManager.Instance.SelectZoneAt(zone.CenterX, zone.CenterY);
                ShowFloatingWarning("Переход к ферме", null, new Color(0.4f, 1f, 0.4f, 1f));
                return;
            }
        }

        foreach (var zone in ZoneManager.Instance.GetAllZones())
        {
            if (zone.Kind == ZoneKind.Warehouse)
            {
                camera.Position = new Vector2(zone.CenterX * MapRenderer.TileSizePx, zone.CenterY * MapRenderer.TileSizePx);
                ZoneManager.Instance.SelectZoneAt(zone.CenterX, zone.CenterY);
                ShowFloatingWarning("Переход к складу", null, new Color(0.4f, 1f, 0.4f, 1f));
                return;
            }
        }
    }

    private void OnPatentPressed()
    {
        if (_patentWindow == null)
        {
            var packed = ResourceLoader.Load<PackedScene>("res://scenes/ui/PatentBureau.tscn");
            if (packed == null)
            {
                GD.PushWarning("[HUD] PatentBureau.tscn не найден.");
                return;
            }
            _patentWindow = packed.Instantiate<Control>();
            AddChild(_patentWindow);
            _patentWindow.Visible = false;
        }
        _patentWindow.Visible = !_patentWindow.Visible;
    }

    /// <summary>
    /// Всплывашка клетки с рудой (TopLevel, за мышью; стиль как у тултипов
    /// влажности/почвы). Показывается только в обычном режиме карты — оверлеи
    /// влажности/плодородия имеют свои подсказки.
    /// </summary>
    private void SetupOreTooltip()
    {
        _oreTooltipLabel = new Label
        {
            Name = "OreTooltip",
            Text = "",
            Visible = false,
            MouseFilter = MouseFilterEnum.Ignore,
            ZIndex = 200,
            TopLevel = true
        };
        _oreTooltipLabel.AddThemeColorOverride("font_color", new Color(1f, 0.88f, 0.62f));
        _oreTooltipLabel.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.9f));
        _oreTooltipLabel.AddThemeConstantOverride("shadow_offset_x", 1);
        _oreTooltipLabel.AddThemeConstantOverride("shadow_offset_y", 1);
        _oreTooltipLabel.AddThemeFontSizeOverride("font_size", 20);
        AddChild(_oreTooltipLabel);
    }

    private void FollowOreTooltip()
    {
        if (_oreTooltipLabel != null && _oreTooltipLabel.Visible)
            _oreTooltipLabel.GlobalPosition = GetViewport().GetMousePosition() + new Vector2(16, 16);
    }

    /// <summary>
    /// Тултип обновляется ТОЛЬКО при смене клетки под курсором (риск из
    /// UI_INTEGRATION_PLAN §5: не читать массивы каждый кадр). Позиция обновляется
    /// каждый кадр (дешёво), текст — по смене клетки.
    /// </summary>
    private void TickOreTooltip()
    {
        if (_oreTooltipLabel == null)
            return;

        var map = MapRenderer.Instance;
        // В режимах влажности/плодородия работает тултип почвы — не перекрываем.
        if (map == null || map.CurrentMapMode != MapMode.Normal ||
            map.MapData == null || !TryGetHoveredTile(out int tx, out int ty))
        {
            InvalidateOreTooltip();
            return;
        }

        // Тултип почвы фермы (InfoMode) рисуется там же — уступаем ему.
        FarmController farm = FarmController.Instance;
        if (farm != null && farm.IsOpen && farm.InfoMode)
        {
            InvalidateOreTooltip();
            return;
        }

        bool cellChanged = tx != _oreTipX || ty != _oreTipY;
        if (!cellChanged)
            return; // текст/видимость не трогаем: массивы читаем только по смене клетки
        _oreTipX = tx;
        _oreTipY = ty;

        MapData mapData = map.MapData;
        if ((uint)tx >= (uint)mapData.Width || (uint)ty >= (uint)mapData.Height)
        {
            HideOreTooltip();
            return;
        }

        int idx = mapData.OreIndex(tx, ty);
        byte oreType = mapData.OreType[idx];
        if (oreType == OreConfig.None)
        {
            HideOreTooltip();
            return;
        }

        string text = BuildOreTooltipText(oreType, mapData.OreReserve[idx],
            mapData.OreReserveInitial[idx], mapData.GetOreState(tx, ty));
        if (text != _oreTipText)
        {
            _oreTipText = text;
            _oreTooltipLabel.Text = text;
        }
        _oreTooltipLabel.GlobalPosition = GetViewport().GetMousePosition() + new Vector2(16, 16);
        _oreTooltipLabel.Visible = true;
    }

    private void HideOreTooltip()
    {
        if (_oreTooltipLabel != null && _oreTooltipLabel.Visible)
            _oreTooltipLabel.Visible = false;
    }

    /// <summary>Сбросить кэш клетки (сменился режим карты / контекст) — при возврате
    /// тултип пересчитается, даже если мышь не двигалась.</summary>
    private void InvalidateOreTooltip()
    {
        _oreTipX = int.MinValue;
        _oreTipY = int.MinValue;
        HideOreTooltip();
    }

    /// <summary>Клетка мира под курсором (общая формула с тултипом почвы).</summary>
    private bool TryGetHoveredTile(out int tx, out int ty)
    {
        tx = 0;
        ty = 0;
        var camera = GetViewport().GetCamera2D();
        if (camera == null)
            return false;
        Vector2 visibleSize = GetViewport().GetVisibleRect().Size;
        Vector2 world = camera.GetScreenCenterPosition()
            + (GetViewport().GetMousePosition() - visibleSize * 0.5f) / camera.Zoom.X;
        tx = (int)MathF.Floor(world.X / MapRenderer.TileSizePx);
        ty = (int)MathF.Floor(world.Y / MapRenderer.TileSizePx);
        return true;
    }

    /// <summary>
    /// Текст тултипа: «Железная руда (гематит) / Запас: 8 400 / 10 000 /
    /// Состояние: обычная». Состояние — выработанность (task-4), не сорт.
    /// </summary>
    private static string BuildOreTooltipText(byte oreType, int reserve, int initial, OreState state)
    {
        string baseName = oreType >= 1 && oreType <= OreDisplayNames.Length
            ? OreDisplayNames[oreType]
            : "Руда";
        string atlasName = OreNameRu(oreType);
        string title = string.IsNullOrEmpty(atlasName) ||
                       string.Equals(atlasName, baseName, StringComparison.OrdinalIgnoreCase) ||
                       baseName.IndexOf('(') >= 0
            ? baseName
            : baseName + " (" + atlasName + ")";

        string stateWord = state switch
        {
            OreState.Rich => "богатая",
            OreState.Normal => "обычная",
            _ => "бедная"
        };
        return title
            + "\nЗапас: " + FormatSpaced(reserve) + " / " + FormatSpaced(initial)
            + "\nСостояние: " + stateWord;
    }

    /// <summary>Русское название руды из атласа (по порядку категории «руда»).</summary>
    private static string OreNameRu(byte oreType)
    {
        IReadOnlyList<Game.UI.Resources.ResourceAtlasEntry> entries =
            Game.UI.Resources.ResourceAtlasCatalog.Entries;
        int seen = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            if (!string.Equals(entries[i].Category, "руда", StringComparison.Ordinal))
                continue;
            seen++;
            if (seen == oreType)
                return entries[i].NameRu;
        }
        return "";
    }

    /// <summary>Число с пробелами-разрядами: 8400 → «8 400».</summary>
    private static string FormatSpaced(int value)
        => value.ToString("N0", CultureInfo.InvariantCulture).Replace(',', ' ');

    // Русские названия 23 руд (OreType 1..23 = порядок категории «руда» в атласе).
    private static readonly string[] OreDisplayNames =
    {
        "", "Железная руда (гематит)", "Железная руда (магнетит)", "Марганцевая руда",
        "Хромитовая руда", "Вольфрамовая руда", "Молибденовая руда", "Ванадиевая руда",
        "Никелевая руда", "Кобальтовая руда", "Титановая руда (ильменит)",
        "Медная руда (колчедан)", "Медная руда (окисленная)", "Свинцовая руда",
        "Цинковая руда", "Оловянная руда", "Боксит", "Ртутная руда (киноварь)",
        "Пирит", "Золотая руда", "Серебряная руда", "Платиновая руда",
        "Урановая руда", "Монацит"
    };

    private void OnWarehouseAreaPressed()
    {
        // Выбор варианта зоны "Склад": открываем универсальное окно строительства.
        _buildMenuController?.Open("warehouse", "Склад");
    }

    private void OnFarmingPressed()
    {
        // Выбор варианта зоны "Ферма": открываем универсальное окно строительства.
        _buildMenuController?.Open("farming", "Ферма");
    }

    private ZoneDraftTool _draftTool;

    /// <summary>
    /// Мир за кромкой острова (§26 шаг 2, §28): задаётся из Main.cs после включения
    /// стримингового мира. null — инструменты работают только по острову, как раньше.
    /// </summary>
    public Game.UI.Tools.IToolWorldPlacement WorldPlacement { get; set; }

    /// <summary>Идёт ли черчение зоны (черновик активен как инструмент).</summary>
    public bool IsDrafting => _draftTool != null
        && PlayerInteractionManager.Instance != null
        && !PlayerInteractionManager.Instance.IsDefaultTool;

    /// <summary>
    /// ZoneButton окна: включить черновик зоны+стен. Окно остаётся открытым.
    /// Повторный нажим — ничего не сносит (черновик живёт дальше).
    /// </summary>
    public void StartZoneDraft(string zoneId)
    {
        if (string.IsNullOrEmpty(zoneId))
            zoneId = _buildMenuController?.CurrentZoneId ?? "";
        // task-13: строковый id → отрасль (легаси "farming"/"warehouse" тоже понимаем).
        StartZoneDraft(ZoneIndustryRegistry.ResolveZoneKind(zoneId));
    }

    /// <summary>
    /// task-13: черновик зоны сразу с ОТРАСЛЬЮ. Клик по отрасли в HUD зовёт этот
    /// путь — инструмент получает ZoneKind, а не строку.
    /// </summary>
    public void StartZoneDraft(ZoneKind kind)
    {
        var interaction = PlayerInteractionManager.Instance;
        var mapRenderer = MapRenderer.Instance;
        if (interaction == null || mapRenderer?.MapData == null || mapRenderer?.GhostLayer == null)
            return;
        ZoneKind normalized = kind.NormalizeLegacy();
        // Повторный нажим той же зоны — не пересоздаём инструмент:
        // иначе черновик и призрак сносились (баг пропажи зон).
        if (_draftTool != null && _draftTool.Kind == normalized)
            return;
        // Смена типа зоны — старый черновик аккуратно гасим.
        if (_draftTool != null)
        {
            _draftTool.Cancel();
            _draftTool = null;
        }
        _draftTool = new ZoneDraftTool(
            interaction.Selection, mapRenderer.MapData, mapRenderer.GhostLayer, normalized);
        if (_buildMenuController != null)
        {
            _draftTool.SetWallMaterial(_buildMenuController.SelectedMaterialId);
            _draftTool.SetDoorMode(_buildMenuController.DoorMode);
        }
        interaction.SetTool(_draftTool);
        _draftTool.WorldPlacement = WorldPlacement;
    }

    /// <summary>DoorButton окна: тумблер режима проёма в активном черновике.</summary>
    public void SetDraftDoorMode(bool enabled)
    {
        _draftTool?.SetDoorMode(enabled);
    }

    /// <summary>Попап материалов: сменить материал в активном черновике.</summary>
    public void SetDraftMaterial(string materialId)
    {
        _draftTool?.SetWallMaterial(materialId);
    }

    /// <summary>CheckMark окна: коммит черновика, окно закрыть, инструмент сбросить.</summary>
    public void ConfirmDraft()
    {
        bool committed = _draftTool != null && _draftTool.Commit();
        _draftTool = null;
        _buildMenuController?.ResetDoorMode();
        _buildMenuController?.Close();
        // UI-03: сброс инструмента безусловно, чтобы исключить зависание зомби-инструмента
        PlayerInteractionManager.Instance?.ResetToDefault();
    }

    /// <summary>Дверь включилась: остановить черчение, но черновик не сносить.</summary>
    public void StopDraftDrawing()
    {
        _draftTool?.StopDrawing();
    }

    /// <summary>esc окна: отмена черновика БЕЗ удаления (ничего не ставили), инструмент сбросить.</summary>
    public void CancelDraft()
    {
        if (_draftTool != null)
        {
            _draftTool.Cancel();
            _draftTool = null;
        }
        PlayerInteractionManager.Instance?.ResetToDefault();
    }

    /// <summary>Крестик окна: черновик уже поставлен в мир — удалить чертежи+зону.</summary>
    public void DiscardDraft()
    {
        if (_draftTool != null)
        {
            _draftTool.Discard();
            _draftTool = null;
        }
        _buildMenuController?.ResetDoorMode();
        _buildMenuController?.Close();
        PlayerInteractionManager.Instance?.ResetToDefault();
    }

    private void OnTreeFellingPressed()
    {
        ToggleToolButton(_treeFellingButton, () =>
        {
            var interaction = PlayerInteractionManager.Instance;
            var mapRenderer = MapRenderer.Instance;
            if (interaction == null || mapRenderer?.MapData == null) return;

            var treeTool = new TreeFellingTool(interaction.Selection, mapRenderer.MapData);
            interaction.SetTool(treeTool);
            treeTool.WorldPlacement = WorldPlacement;
        });
    }

    private void BindTimeButton(Button button, GameSpeed speed)
    {
        if (button != null)
        {
            button.MouseFilter = MouseFilterEnum.Stop;
            button.Pressed += () => TimeManager.Instance?.SetSpeed(speed);
        }
    }

    public void Setup(PlayerInteractionManager interactionManager, MapRenderer mapRenderer, int totalPopulation = 550)
    {
        _totalPopulation = totalPopulation;
        if (_interactionManager != null)
        {
            _interactionManager.OnToolReset -= ClearActiveToolButton;
            _interactionManager.OnToolReset -= ClearStaleDraftTool;
        }
        _interactionManager = interactionManager;
        if (_interactionManager != null)
        {
            _interactionManager.OnToolReset += ClearActiveToolButton;
            // §113.5/B1: см. комментарий в _Ready — чистка stale-черновика.
            _interactionManager.OnToolReset += ClearStaleDraftTool;
        }
        RefreshStats();
    }

    private Button _buttonMap; // Сохраняем ссылку для защиты от дубликатов
    private Action _worldMapCallback;

    /// <summary>Привязать кнопку ButtonMap (глобальная карта §23/§24) к переключению оверлея.</summary>
    public void BindWorldMapButton(Action onPressed)
    {
        if (onPressed == null)
            return;

        _worldMapCallback = onPressed;

        if (_buttonMap != null && GodotObject.IsInstanceValid(_buttonMap))
            return;

        var button = GetNodeOrNull<Button>("BtnOpenWorldMap")
                    ?? FindChild("BtnOpenWorldMap", true, false) as Button;

        if (button == null)
        {
            GD.PrintErr("[HUD] ButtonMap NOT found — checking scene tree:");
            foreach (var child in GetChildren())
                GD.Print($"  [HUD] direct child: '{child.Name}' ({child.GetType().Name})");
            return;
        }

        _buttonMap = button;
        _buttonMap.MouseFilter = Control.MouseFilterEnum.Stop;
        _buttonMap.FocusMode = Control.FocusModeEnum.None;
        _buttonMap.Visible = true;
        _buttonMap.SetProcessInput(true);
        _buttonMap.ZIndex = 50;

        // Полноценная стилизация с четким откликом при наведении и клике
        var styleNormal = new StyleBoxFlat
        {
            BgColor = new Color(0.10f, 0.10f, 0.12f, 0.90f),
            CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 4,
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            BorderColor = new Color(0.35f, 0.35f, 0.40f, 0.85f)
        };
        var styleHover = new StyleBoxFlat
        {
            BgColor = new Color(0.20f, 0.24f, 0.32f, 0.95f),
            CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 4,
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            BorderColor = new Color(0.45f, 0.70f, 0.95f, 1.0f)
        };
        var stylePressed = new StyleBoxFlat
        {
            BgColor = new Color(0.06f, 0.08f, 0.12f, 1.0f),
            CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 4,
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            BorderColor = new Color(0.95f, 0.80f, 0.25f, 1.0f)
        };
        _buttonMap.AddThemeStyleboxOverride("normal", styleNormal);
        _buttonMap.AddThemeStyleboxOverride("hover", styleHover);
        _buttonMap.AddThemeStyleboxOverride("pressed", stylePressed);

        _buttonMap.MoveToFront();

        _buttonMap.Pressed += () =>
        {
            GD.Print("[HUD BUTTONMAP] CLICKED → invoking ToggleWorldMap");
            _worldMapCallback?.Invoke();
        };

        GD.Print($"[HUD] ButtonMap bound OK (pos={_buttonMap.Position}, size={_buttonMap.Size}, icon={(_buttonMap.Icon != null ? _buttonMap.Icon.ResourcePath : "none")})");
    }

    private static void SetMouseFilterRecursive(Control control)
    {
        if (control is FarmController ||
            control is BuildMenuController ||
            control is WorkZoneController ||
            control is PriorityBoardController ||
            control is VillagerInspectorController)
        {
            return;
        }

        if (control is BaseButton)
        {
            control.MouseFilter = MouseFilterEnum.Stop;
        }
        else if (control is Panel or PanelContainer or ColorRect)
        {
            // UI-04: затемнение ColorRect (Dim) обязано блокировать клики сквозь модалки
            control.MouseFilter = MouseFilterEnum.Stop;
        }
        else
        {
            control.MouseFilter = MouseFilterEnum.Ignore;
        }

        foreach (Node child in control.GetChildren())
        {
            if (child is Control childControl)
                SetMouseFilterRecursive(childControl);
        }
    }

    public void UpdateUI(CityState state)
    {
        if (_populationLabel != null)
            _populationLabel.Text = state.Population.ToString();
        if (_employmentLabel != null)
            _employmentLabel.Text = $"{state.Employed}/{state.Unemployed}";
    }


    // ================= task-6: РЕЖИМЫ КАРТЫ =================

    /// <summary>
    /// «Слои карты»: кнопка и панель лежат в hud.tscn (сцена — источник правды).
    /// Код находит узлы по имени, вешает обработчики и синхронизирует подсветку.
    /// Клавиши F1..F8 обрабатываются в _UnhandledInput как и раньше.
    /// </summary>
    private void SetupMapLayersPanel()
    {
        _mapLayersButton = FindChild("MapLayersButton", true, false) as Button;
        _mapModePanel = FindChild("MapLayersPanel", true, false) as PanelContainer;
        _mapModeCaption = FindChild("MapModeCaption", true, false) as Label;
        _mapModeButtons.Clear();

        if (_mapModePanel != null)
            _mapModePanel.Visible = false;

        if (_mapLayersButton != null)
        {
            _mapLayersButton.MouseFilter = MouseFilterEnum.Stop;
            _mapLayersButton.Pressed += () =>
            {
                if (_mapModePanel == null) return;
                _mapModePanel.Visible = !_mapModePanel.Visible;
                if (_mapModePanel.Visible)
                    SyncMapModeUi(MapRenderer.Instance?.CurrentMapMode ?? MapMode.Normal);
            };
        }

        BindMapModeButton("MapMode_Normal", MapMode.Normal);
        BindMapModeButton("MapMode_Ore", MapMode.Ore);
        BindMapModeButton("MapMode_Humidity", MapMode.Humidity);
        BindMapModeButton("MapMode_Fertility", MapMode.Fertility);
        BindMapModeButton("MapMode_Elevation", MapMode.Elevation);
        BindMapModeButton("MapMode_Stockpile", MapMode.Stockpile);
        BindMapModeButton("MapMode_Jobs", MapMode.Jobs);

        SyncMapModeUi(MapRenderer.Instance?.CurrentMapMode ?? MapMode.Normal);
    }

    /// <summary>Привязать кнопку слоя из сцены к режиму карты.</summary>
    private void BindMapModeButton(string nodeName, MapMode mode)
    {
        if (FindChild(nodeName, true, false) is not Button btn)
            return;
        btn.MouseFilter = MouseFilterEnum.Stop;
        btn.Pressed += () => ApplyMapMode(mode);
        _mapModeButtons[mode] = btn;
    }

    /// <summary>
    /// Переключить режим карты (единая точка для панели, старых кнопок влажности/
    /// плодородия и горячих клавиш). Повторный выбор активного режима — Normal.
    /// </summary>
    public void ApplyMapMode(MapMode mode, bool toggle = true)
    {
        var map = MapRenderer.Instance;
        if (map == null)
            return;
        MapMode target = mode;
        if (toggle && map.CurrentMapMode == mode)
            target = MapMode.Normal;
        map.SetMapMode(target);
        SyncMapModeUi(target);
    }

    private void SyncMapModeUi(MapMode mode)
    {
        if (_mapModeCaption != null)
            _mapModeCaption.Text = "Режим: " + MapModeTitle(mode);
        foreach (var kv in _mapModeButtons)
            kv.Value.Modulate = kv.Key == mode ? new Color(0.6f, 1f, 0.6f, 1f) : Color.Color8(255, 255, 255, 255);
        if (_humidityButton != null)
            _humidityButton.Modulate = mode == MapMode.Humidity ? new Color(0.5f, 1f, 1f, 1f) : Color.Color8(255, 255, 255, 255);
        if (_fertilityButton != null)
            _fertilityButton.Modulate = mode == MapMode.Fertility ? new Color(0.6f, 1f, 0.6f, 1f) : Color.Color8(255, 255, 255, 255);

        UpdateHumidityLegend(mode == MapMode.Humidity);
        UpdateFertilityLegend(mode == MapMode.Fertility);
        SyncOreLegend(mode == MapMode.Ore);
    }

    private static string MapModeTitle(MapMode mode) => mode switch
    {
        MapMode.Ore => "Месторождения",
        MapMode.Humidity => "Влажность",
        MapMode.Fertility => "Плодородие",
        MapMode.Elevation => "Высоты",
        MapMode.Stockpile => "Склад",
        MapMode.Jobs => "Работы",
        _ => "Обычный"
    };

    // ---------- легенда месторождений ----------

    private void SyncOreLegend(bool visible)
    {
        EnsureOreLegend();
        if (_oreLegendPanel == null)
            return;
        _oreLegendPanel.Visible = visible;
        if (visible)
            RebuildOreLegend();
    }

    private void EnsureOreLegend()
    {
        if (_oreLegendPanel != null)
            return;
        _oreLegendPanel = new PanelContainer
        {
            Name = "OreLegendPanel",
            ZIndex = 60,
            Visible = false,
            MouseFilter = MouseFilterEnum.Ignore
        };
        _oreLegendPanel.SetAnchorsPreset(LayoutPreset.TopLeft);
        _oreLegendPanel.Position = new Vector2(340f, 172f);
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.0157f, 0.0157f, 0.0196f, 0.92f),
            BorderColor = new Color(0.196f, 0.196f, 0.212f)
        };
        style.SetBorderWidthAll(1);
        style.SetContentMarginAll(8f);
        _oreLegendPanel.AddThemeStyleboxOverride("panel", style);

        _oreLegend = new RichTextLabel
        {
            Name = "OreLegend",
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            CustomMinimumSize = new Vector2(258, 0),
            MouseFilter = MouseFilterEnum.Ignore
        };
        _oreLegend.AddThemeFontSizeOverride("normal_font_size", 13);
        _oreLegendPanel.AddChild(_oreLegend);
        AddChild(_oreLegendPanel);
    }

    private void RefreshOreLegendThrottled(float delta)
    {
        if (_oreLegendPanel == null || !_oreLegendPanel.Visible)
            return;
        _oreLegendTimer += delta;
        if (_oreLegendTimer < 2f)
            return;
        _oreLegendTimer = 0f;
        RebuildOreLegend();
    }

    /// <summary>
    /// Легенда: только те руды, что реально есть на острове (v1 — все известны).
    /// Цвет тот же, что на оверлее (MapModeOverlayRenderer.OreColor).
    /// </summary>
    private void RebuildOreLegend()
    {
        if (_oreLegend == null)
            return;
        MapData data = MapRenderer.Instance?.MapData;
        byte[] ore = data?.OreType;
        if (ore == null)
        {
            _oreLegend.Text = "Месторождения: нет данных карты";
            return;
        }

        Span<int> counts = stackalloc int[24];
        for (int i = 0; i < ore.Length; i++)
        {
            byte type = ore[i];
            if (type > 0 && type < 24)
                counts[type]++;
        }

        var sb = new System.Text.StringBuilder(512);
        sb.Append("[b]Месторождения[/b]\nцвет = тип руды, яркость = запас\n");
        bool any = false;
        for (int type = 1; type <= 23; type++)
        {
            if (counts[type] == 0)
                continue;
            any = true;
            Color c = MapModeOverlayRenderer.OreColor((byte)type);
            string name;
            ItemId item = ItemRegistry.ForOreType(type);
            if (ItemRegistry.TryGet(item, out ItemDefinition def) && !string.IsNullOrEmpty(def.Name))
                name = def.Name;
            else
                name = "руда #" + type;
            sb.Append("[color=#").Append(c.ToHtml(false)).Append("]\u25A0[/color] ")
              .Append(name).Append(" — ").Append(counts[type]).Append(" кл.\n");
        }
        if (!any)
            sb.Append("На острове пока нет разведанных залежей.");
        _oreLegend.Text = sb.ToString();
    }

    /// <summary>
    /// UI-17: закрыть верхнее открытое окно / модалку / инспектор.
    /// Возвращает true, если было закрыто окно или меню.
    /// </summary>
    public bool TryCloseTopWindow()
    {
        // 1. Модальное окно выбора отрасли зон
        if (_zoneContainer != null && _zoneContainer.Visible)
        {
            CloseZoneModal();
            return true;
        }

        // 2. Окно биржи
        if (_exchangeWindow != null && _exchangeWindow.Visible)
        {
            if (_exchangeWindow is IZoneWindow ex && ex.IsOpen)
                ex.Close();
            else
                _exchangeWindow.Visible = false;
            return true;
        }

        // 3. Бюро патентов
        if (_patentWindow != null && _patentWindow.Visible)
        {
            _patentWindow.Visible = false;
            return true;
        }

        // 4. Панель слоёв карты
        if (_mapModePanel != null && _mapModePanel.Visible)
        {
            _mapModePanel.Visible = false;
            return true;
        }

        // 5. Окно приоритетов (доска работ)
        if (_priorityBoard != null && _priorityBoard.IsOpen)
        {
            _priorityBoard.Close();
            ClearActiveToolButton();
            if (_amountOfResourcesPanel != null)
                _amountOfResourcesPanel.Visible = true;
            return true;
        }

        // 6. Инспектор поселенца
        if (VillagerInspectorController.Instance != null && VillagerInspectorController.Instance.IsOpen)
        {
            VillagerInspectorController.Instance.Close();
            return true;
        }

        // 7. Окно универсального меню стройки
        if (_buildMenuController != null && _buildMenuController.IsOpen)
        {
            _buildMenuController.Close();
            return true;
        }

        // 8. Окно фермы
        if (_gardenController != null && _gardenController.IsOpen)
        {
            _gardenController.Close();
            FarmZoneManager.Instance?.DeselectZone();
            return true;
        }

        // 9. Окно рабочей зоны
        if (WorkZoneController.Instance != null && WorkZoneController.Instance.IsOpen)
        {
            WorkZoneController.Instance.Close();
            WorkZoneManager.Instance?.DeselectZone();
            return true;
        }

        // 10. Открытые подконтейнеры меню стройки
        if (_wallContainer != null && _wallContainer.Visible)
        {
            HideContainer(_wallContainer);
            return true;
        }
        if (_industrialContainer != null && _industrialContainer.Visible)
        {
            HideContainer(_industrialContainer);
            return true;
        }
        if (_orderContainer != null && _orderContainer.Visible)
        {
            HideContainer(_orderContainer);
            return true;
        }
        if (_buildContainer != null && _buildContainer.Visible)
        {
            HideContainer(_buildContainer);
            return true;
        }
        if (_selectionContainer != null && _selectionContainer.Visible)
        {
            HideContainer(_selectionContainer);
            return true;
        }

        return false;
    }

    /// <summary>Горячие клавиши режимов карты и симулятора (Space, 1..5, Escape, F1..F8).</summary>
    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo)
            return;

        var code = key.Keycode != Key.None ? key.Keycode : key.PhysicalKeycode;

        // UI-17: Escape закрывает открытые окна UI перед сбросом инструмента
        if (code == Key.Escape)
        {
            if (TryCloseTopWindow())
            {
                GetViewport()?.SetInputAsHandled();
                return;
            }
        }

        // Хоткеи управления временем: Space (пауза/снятие с паузы), 1..5 (скорости)
        if (code == Key.Space && TimeManager.Instance != null)
        {
            TimeManager.Instance.TogglePause();
            GetViewport()?.SetInputAsHandled();
            return;
        }

        if (TimeManager.Instance != null && code is >= Key.Key1 and <= Key.Key5)
        {
            GameSpeed spd = code switch
            {
                Key.Key1 => GameSpeed.Normal,
                Key.Key2 => GameSpeed.Fast2,
                Key.Key3 => GameSpeed.Fast5,
                Key.Key4 => GameSpeed.Fast25,
                Key.Key5 => GameSpeed.Fast100,
                _ => GameSpeed.Normal
            };
            TimeManager.Instance.SetSpeed(spd);
            GetViewport()?.SetInputAsHandled();
            return;
        }

        MapMode? mode = code switch
        {
            Key.F1 => MapMode.Ore,
            Key.F2 => MapMode.Fertility,
            Key.F4 => MapMode.Humidity,
            Key.F5 => MapMode.Elevation,
            Key.F6 => MapMode.Stockpile,
            Key.F7 => MapMode.Jobs,
            Key.F8 => MapMode.Normal,
            _ => null
        };
        if (mode == null)
            return;
        ApplyMapMode(mode.Value, toggle: mode.Value != MapMode.Normal);
        GetViewport()?.SetInputAsHandled();
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
        StockpileManager.Instance.OnItemCountChanged -= OnStockpileItemCountChanged;
        // task-13: снимаем подписку на выбор зоны (статический менеджер не должен
        // держать освобождённый HUD).
        ZoneManager.Instance.OnZoneSelected -= OnWorldZoneSelected;
        if (_interactionManager != null)
        {
            _interactionManager.OnToolReset -= ClearActiveToolButton;
            _interactionManager.OnToolReset -= ClearStaleDraftTool;
            _interactionManager = null;
        }
        else if (PlayerInteractionManager.Instance != null)
        {
            PlayerInteractionManager.Instance.OnToolReset -= ClearActiveToolButton;
            PlayerInteractionManager.Instance.OnToolReset -= ClearStaleDraftTool;
        }

        // FIX (bughunt 15-12): панель подсветки могла так и остаться без родителя
        // (создана в _Ready и ни разу не подключена к кнопке) — освобождаем,
        // иначе это узел-сирота вне освобождаемого поддерева HUD.
        if (_activeHighlightPanel != null
            && GodotObject.IsInstanceValid(_activeHighlightPanel)
            && _activeHighlightPanel.GetParent() == null)
        {
            _activeHighlightPanel.QueueFree();
        }
        _activeHighlightPanel = null;
        _criticalAlertBanner = null;
        _criticalAlertLabel = null;

        base._ExitTree();
    }
}