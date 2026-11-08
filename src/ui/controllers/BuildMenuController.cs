using Godot;
using System;
using System.Collections.Generic;
using Game.Core;
using Game.Simulation;
using Game.UI.Resources;
using Game.UI.Tools;

namespace Game.UI;

/// <summary>
/// Универсальное окно строительства (Build.tscn).
/// Открывается при выборе зоны в HUD (ZoneContainer: отрасли из
/// ZoneIndustryRegistry).
///
/// task-12 (замысел владельца «зона = отрасль»): окно показывает ТОЛЬКО объекты
/// выбранной зоны плюс группу «Общее» (стены/дверь/центральный блок) — чтобы
/// игрок не путался в чужих станках. При смене зоны (HUD или селектор «Отрасль»
/// внутри окна) список перестраивается БЕЗ пересоздания окна.
///
/// Верхний HBox — режимы: DoorButton (тумблер проёма), WallMaterialButton (попап
/// материалов), ZoneButton (черновик зоны+стен в мире, окно остаётся открытым).
/// IndustryBar/IndustryOption — текущая отрасль (источник: ZoneIndustryRegistry).
/// ItemsRoot — программное содержимое по зонам (секции + ряды по 5 кнопок).
/// CheckMark — коммит черновика (зона по типу + стены чертежами), окно закрыть.
/// </summary>
public partial class BuildMenuController : Control
{
    public static BuildMenuController Instance { get; private set; }

    private IDraftCoordinator _draft;

    /// <summary>
    /// Координатор черновика (HUDController). Узкий интерфейс вместо HUDController.Instance —
    /// иначе цикл HUDController ↔ BuildMenuController (ветка (b) цикла #4).
    /// </summary>
    public void BindDraftCoordinator(IDraftCoordinator draft) => _draft = draft;

    /// <summary>Сколько кнопок максимум в одном HBox-ряду нижнего списка.</summary>
    public const int MaxButtonsPerRow = 5;

    /// <summary>Отступ между кнопками в нижнем списке (как в сцене у HBoxContainer2).</summary>
    public const int RowSeparation = 63;

    /// <summary>Попап материалов: ряды по 5 кнопок 50x50, отступ 10.</summary>
    public const int MaterialPerRow = 5;

    public const int MaterialSeparation = 10;
    public const int MaterialButtonSize = 50;

    private Control _rootPanel;
    private VBoxContainer _vbox;
    private Label _titleLabel;
    private BaseButton _escButton;
    private BaseButton _checkMarkButton;

    private Button _doorButton;
    private Button _wallMaterialButton;
    private Button _zoneButton;
    /// <summary>§119 P5: кнопка «Центральный блок» (Build.tscn, верхний ряд).</summary>
    private Button _centralBlockButton;

    // --- task-12: отрасль зоны и её каталог объектов ---
    private OptionButton _industryOption;
    private VBoxContainer _itemsRoot;

    /// <summary>Отрасль, для которой сейчас открыто окно (фильтр списка).</summary>
    private ZoneKind _zoneKind = ZoneKind.Warehouse;

    /// <summary>Шаблонный ряд из сцены (HBoxContainer2 с 5 заглушками). Не удаляем, прячем при динамике.</summary>
    private HBoxContainer _templateRow;

    /// <summary>Динамически созданные узлы списка (секции/ряды) — удаляются при Rebuild.</summary>
    private readonly List<Node> _dynamicNodes = new();

    // task-21: цена постройки по ТИПАМ материалов. Каждая подпись-количество и
    // тултип помнят требуемое — цвет обновляется, когда меняется склад.
    private sealed class CostEntry
    {
        public ItemId Item;
        public int Required;
        public Label Label;
        public bool Enough;
    }

    private readonly List<CostEntry> _costEntries = new();
    private readonly List<(Button Button, BuildingType Type, string Title)> _costTooltips = new();
    private float _costRefreshTimer;

    private static readonly Color CostColorEnough = new(1f, 0.93f, 0.75f);
    private static readonly Color CostColorMissing = new(1f, 0.45f, 0.4f);

    // Попап материалов: панель поверх окна, тумблер кнопкой, клик мимо закрывает.
    private PanelContainer _materialPopup;
    private VBoxContainer _materialRows;
    private string _selectedMaterialId = "wood";
    private readonly Dictionary<string, Button> _materialButtons = new();

    private string _currentZoneId = "";
    public string CurrentZoneId => _currentZoneId;

    /// <summary>task-12: отрасль выбранной зоны (контекст фильтра).</summary>
    public ZoneKind CurrentIndustry => _zoneKind;

    public string SelectedMaterialId => _selectedMaterialId;

    private bool _doorMode;
    public bool DoorMode => _doorMode;

    public event Action OnOpened;
    public event Action OnClosed;

    public bool IsOpen => _rootPanel != null && _rootPanel.Visible;

    public override void _Ready()
    {
        Instance = this;
        ZIndex = 20;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        LoadScene();
    }

    private void LoadScene()
    {
        var paths = new[]
        {
            "res://scenes/ui/Build.tscn",
            "res://ui/Build.tscn",
            "res://Build.tscn"
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
            _rootPanel.MouseFilter = MouseFilterEnum.Ignore;

            ConfigureInputBlocking(_rootPanel);

            _titleLabel = _rootPanel.FindChild("Name", true, false) as Label;
            _vbox = _rootPanel.FindChild("VBoxContainer", true, false) as VBoxContainer;
            _templateRow = _rootPanel.FindChild("HBoxContainer2", true, false) as HBoxContainer;

            // task-12: селектор отрасли и корень списка объектов.
            _industryOption = _rootPanel.FindChild("IndustryOption", true, false) as OptionButton;
            _itemsRoot = _rootPanel.FindChild("ItemsRoot", true, false) as VBoxContainer;

            _doorButton = _rootPanel.FindChild("DoorButton", true, false) as Button;
            _wallMaterialButton = _rootPanel.FindChild("WallMaterialButton", true, false) as Button;
            _zoneButton = _rootPanel.FindChild("ZoneButton", true, false) as Button;
            _centralBlockButton = _rootPanel.FindChild("CentralBlockButton", true, false) as Button;

            if (_doorButton != null)
            {
                _doorButton.MouseFilter = MouseFilterEnum.Stop;
                _doorButton.Pressed += OnDoorPressed;
            }
            if (_wallMaterialButton != null)
            {
                _wallMaterialButton.MouseFilter = MouseFilterEnum.Stop;
                _wallMaterialButton.Pressed += ToggleMaterialPopup;
            }
            if (_zoneButton != null)
            {
                _zoneButton.MouseFilter = MouseFilterEnum.Stop;
                _zoneButton.Pressed += OnZoneBuildPressed;
            }
            if (_centralBlockButton != null)
            {
                _centralBlockButton.MouseFilter = MouseFilterEnum.Stop;
                _centralBlockButton.Pressed += OnCentralBlockPressed;
            }

            BuildIndustryOption();

            _escButton = _rootPanel.FindChild("esc", true, false) as BaseButton
                         ?? _rootPanel.FindChild("Escape", true, false) as BaseButton
                         ?? _rootPanel.FindChild("Close", true, false) as BaseButton;
            if (_escButton != null)
                _escButton.Pressed += OnDiscardPressed;

            _checkMarkButton = _rootPanel.FindChild("CheckMark", true, false) as BaseButton
                               ?? _rootPanel.FindChild("Checkmark", true, false) as BaseButton;
            if (_checkMarkButton != null)
                _checkMarkButton.Pressed += OnCheckMarkPressed;

            BuildMaterialPopup();
            break;
        }

        if (_rootPanel == null)
            GD.PushWarning("[BuildMenu] Build.tscn не найден ни по одному пути.");
    }

    private static void ConfigureInputBlocking(Control node)
    {
        if (node == null)
            return;
        // §113.5/B1: ScrollContainer НЕ ставим в Stop — в сцене он растянут до
        // низа экрана (1007px при контенте 174px) и пустой частью накрывал
        // кнопки левой колонки HUD (Farming/WarehouseArea и др.), съедая клики.
        // Реальную панель держит PanelContainer ниже по рекурсии.
        if (node is Panel || node is PanelContainer || node is ItemList)
            node.MouseFilter = MouseFilterEnum.Stop;
        if (node is ScrollContainer)
            node.MouseFilter = MouseFilterEnum.Ignore;
        foreach (Node child in node.GetChildren())
        {
            if (child is Control childCtrl)
                ConfigureInputBlocking(childCtrl);
        }
    }

    /// <summary>
    /// task-12: список отраслей строится ИЗ РЕЕСТРА (ZoneIndustryRegistry.All),
    /// а не хардкодом: добавили отрасль в профиль — она появилась в окне.
    /// </summary>
    private void BuildIndustryOption()
    {
        if (_industryOption == null)
            return;
        _industryOption.Clear();
        for (int i = 0; i < ZoneIndustryRegistry.All.Count; i++)
            _industryOption.AddItem(ZoneIndustryRegistry.All[i].Title);
        _industryOption.MouseFilter = MouseFilterEnum.Stop;
        _industryOption.ItemSelected += OnIndustrySelected;
    }

    private void OnIndustrySelected(long index)
    {
        var all = ZoneIndustryRegistry.All;
        if (index < 0 || index >= all.Count)
            return;
        var profile = all[(int)index];
        if (profile.Kind == _zoneKind)
            return;
        // Смена отрасли = смена контекста зоны: список перестраивается мгновенно,
        // окно НЕ пересоздаётся. Старый черновик другой зоны гасим.
        SetZoneContext(profile.Kind, profile.Title, startDraft: true);
    }

    private void SyncIndustryOption()
    {
        if (_industryOption == null)
            return;
        var all = ZoneIndustryRegistry.All;
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i].Kind != _zoneKind)
                continue;
            if (_industryOption.Selected != i)
                _industryOption.Select(i);
            return;
        }
    }

    /// <summary>
    /// Открыть окно для зоны. zoneId: "farm"/"farming", "warehouse",
    /// "machine_building", "oil_refining", … (id отрасли из ZoneIndustryRegistry).
    /// Повторный вызов с тем же zoneId — обновить заголовок, окно остаётся открытым.
    /// Для фермы прячем материал стен и дверь (стен у фермы нет).
    /// </summary>
    public void Open(string zoneId, string zoneTitle)
    {
        if (_rootPanel == null)
            return;
        _currentZoneId = zoneId ?? "";
        _zoneKind = ZoneIndustryRegistry.ResolveZoneKind(_currentZoneId);
        var profile = ZoneIndustryRegistry.Get(_zoneKind);
        if (_titleLabel != null)
            _titleLabel.Text = string.IsNullOrEmpty(zoneTitle) ? profile.Title : zoneTitle;

        ApplyZoneVisibility();
        _rootPanel.Visible = true;
        ResetDoorMode();
        _draft?.SetDraftDoorMode(false);
        // Черновик зоны: строка id уходит в ZoneDraftTool, который сам
        // сопоставляет её отрасли (task-13). Для legacy "farming"/"warehouse"
        // поведение прежнее. Жилью зона-бригадир не нужна (нет основной работы).
        if (ZoneIndustryRegistry.PrimaryJobMask(_zoneKind) != 0)
            _draft?.StartZoneDraft(_currentZoneId);
        SyncIndustryOption();
        RebuildCatalog();
        UpdateModeButtons();
        OnOpened?.Invoke();
    }

    /// <summary>
    /// task-12/13: переключить контекст зоны (отрасль) без пересоздания окна.
    /// Вызывается селектором «Отрасль» и HUD'ом при выборе зоны в мире.
    /// startDraft — сразу вооружить черновик зоны этой отрасли.
    /// </summary>
    public void SetZoneContext(ZoneKind kind, string zoneTitle = null, bool startDraft = false)
    {
        if (_rootPanel == null)
            return;
        ZoneKind normalized = kind.NormalizeLegacy();
        bool changed = normalized != _zoneKind;
        _zoneKind = normalized;
        _currentZoneId = ZoneIndustryRegistry.IdFor(normalized);

        var profile = ZoneIndustryRegistry.Get(normalized);
        if (_titleLabel != null)
            _titleLabel.Text = string.IsNullOrEmpty(zoneTitle) ? profile.Title : zoneTitle;

        ApplyZoneVisibility();
        SyncIndustryOption();
        RebuildCatalog();
        UpdateModeButtons();

        if (startDraft && changed && ZoneIndustryRegistry.PrimaryJobMask(_zoneKind) != 0)
        {
            CancelDraft();
            _draft?.StartZoneDraft(_currentZoneId);
        }
    }

    /// <summary>
    /// У фермы нет стен — прячем кнопки материала и двери (только для фермы,
    /// остальные отрасли — как склад: стены и дверь доступны). Сцену не трогаем.
    /// </summary>
    private void ApplyZoneVisibility()
    {
        bool isFarm = _zoneKind.IsFarmZone();
        if (_wallMaterialButton != null)
            _wallMaterialButton.Visible = !isFarm;
        if (_doorButton != null)
            _doorButton.Visible = !isFarm;
        // Отрасль без основной работы (жильё) не создаёт зону-бригадира из черновика.
        if (_zoneButton != null)
            _zoneButton.Disabled = ZoneIndustryRegistry.PrimaryJobMask(_zoneKind) == 0;
        if (isFarm)
            ResetDoorMode();
    }

    public void Close()
    {
        CloseMaterialPopup();
        if (_rootPanel != null && _rootPanel.Visible)
        {
            _rootPanel.Visible = false;
            _currentZoneId = "";
            OnClosed?.Invoke();
        }
    }

    /// <summary>Закрыть окно без OnClosed (страховка пути Discard: HUD уже закрыл окно сам).
    /// Esc идёт через Close(), иначе HUD не вернёт нижние контейнеры (bughunt 15-9).</summary>
    private void CloseSilent()
    {
        CloseMaterialPopup();
        if (_rootPanel != null)
            _rootPanel.Visible = false;
        _currentZoneId = "";
    }

    /// <summary>esc: отмена черновика (ничего в мир не ставили) + закрыть окно.</summary>
    private void OnEscPressed()
    {
        CancelDraft();
        // FIX (bughunt 15-9): Esc идёт тем же путём, что крестик (Close → OnClosed),
        // иначе HUD не возвращает нижние контейнеры после закрытия клавишей.
        Close();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb && mb.Pressed && _materialPopup != null && _materialPopup.Visible)
        {
            // Клик мимо попапа — закрыть (попап поверх окна, Stop-фильтр).
            Vector2 mp = _materialPopup.GetGlobalMousePosition();
            if (!_materialPopup.GetGlobalRect().HasPoint(mp))
                CloseMaterialPopup();
        }
        if (@event is InputEventKey key && key.Pressed && key.Keycode == Key.Escape && IsOpen)
        {
            // FIX (bughunt 15-9): Esc закрывает окно тем же путём, что крестик —
            // через Close() с событием OnClosed. CloseSilent() не поднимал нижние
            // контейнеры HUD (обработчик OnClosed делает SetMainHUDVisible(true)).
            CancelDraft();
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    // --- Режимы (только ОДИН активен: зона-режим или дверь-режим) ---

    /// <summary>
    /// ZoneButton: включить черновик зоны+стен, окно остаётся открытым.
    /// Повторный нажим — выключить (тумблер). Включение зоны гасит дверь-режим.
    /// </summary>
    private void OnZoneBuildPressed()
    {
        var hud = _draft;
        bool nowDrafting = hud != null && hud.IsDrafting;
        if (nowDrafting)
        {
            // Был черновик — выключаем (инструмент в обычный, призрак гаснет).
            hud.CancelDraft();
            ResetDoorMode();
            hud.SetDraftDoorMode(false);
            UpdateModeButtons();
            return;
        }
        // Включаем зону, дверь-режим гасим (два режима сразу нельзя).
        ResetDoorMode();
        hud?.SetDraftDoorMode(false);
        hud?.StartZoneDraft(_currentZoneId);
        UpdateModeButtons();
    }

    /// <summary>
    /// DoorButton: тумблер режима проёма (клик по ghost-стене снимает её).
    /// Включение двери гасит зону-режим черчения (два режима сразу нельзя).
    /// </summary>
    private void OnDoorPressed()
    {
        _doorMode = !_doorMode;
        var hud = _draft;
        if (_doorMode)
        {
            // Дверь включилась — зону-черчение останавливаем (черновик живёт).
            hud?.StopDraftDrawing();
        }
        hud?.SetDraftDoorMode(_doorMode);
        UpdateModeButtons();
    }

    /// <summary>Подсветка: активный режим — красным, неактивный — белым.</summary>
    private void UpdateModeButtons()
    {
        var hud = _draft;
        bool drafting = hud != null && hud.IsDrafting && !_doorMode;
        if (_zoneButton != null)
            _zoneButton.Modulate = drafting ? new Color(0.6f, 1f, 0.6f, 1f) : Color.Color8(255, 255, 255, 255);
        if (_doorButton != null)
            _doorButton.Modulate = _doorMode ? new Color(1f, 0.6f, 0.6f, 1f) : Color.Color8(255, 255, 255, 255);
    }

    internal void ResetDoorMode()
    {
        _doorMode = false;
        if (_doorButton != null)
            _doorButton.Modulate = Color.Color8(255, 255, 255, 255);
    }

    /// <summary>CheckMark: коммит черновика, окно закрыть, инструмент сбросить.</summary>
    private void OnCheckMarkPressed()
    {
        var hud = _draft;
        hud?.ConfirmDraft();
    }

    /// <summary>
    /// §119 P5: «Центральный блок» — предмет стройки 10 дерева (как мебель,
    /// путь BuildTool: остров через ConstructionManager, мир за кромкой через
    /// WorldPlacement). Идёт тем же tool-каналом, что кровать/скамья/тумбочка.
    /// </summary>
    private void OnCentralBlockPressed()
    {
        var interaction = PlayerInteractionManager.Instance;
        var mapRenderer = MapRenderer.Instance;
        if (interaction == null || mapRenderer?.WallBuildManager == null || mapRenderer?.MapData == null)
            return;

        var tool = new BuildTool(
            mapRenderer.WallBuildManager,
            mapRenderer.GhostLayer,
            mapRenderer.MapData,
            BuildingType.CentralBlock,
            MapRenderer.SourceCentralBlock
        );
        interaction.SetTool(tool);
        tool.WorldPlacement = HUDController.Instance?.WorldPlacement;
    }

    private void CancelDraft()
    {
        var hud = _draft;
        hud?.CancelDraft();
        ResetDoorMode();
    }

    /// <summary>Крестик окна (esc-кнопка сцены): удалить поставленные чертежи+зону.</summary>
    private void OnDiscardPressed()
    {
        var hud = _draft;
        hud?.DiscardDraft();
        ResetDoorMode();
        CloseSilent();
    }

    // --- task-12: список объектов выбранной отрасли ---

    /// <summary>
    /// Пересобрать список объектов под текущую отрасль: секция «Станки: <отрасль>»
    /// (только объекты этой зоны) + секция «Общее» (стены/дверь/центральный блок).
    /// Окно не пересоздаётся — меняется содержимое ItemsRoot.
    /// </summary>
    private void RebuildCatalog()
    {
        if (_vbox == null)
            return;
        ClearDynamicNodes();
        if (_templateRow != null)
            _templateRow.Visible = false;

        var root = _itemsRoot ?? _vbox;
        var profile = ZoneIndustryRegistry.Get(_zoneKind);
        bool isFarm = _zoneKind.IsFarmZone();

        AddSectionLabel(root, isFarm ? "Объекты фермы" : $"Станки: {profile.Title}");
        if (profile.Buildings.Count == 0)
            AddHintLabel(root, "У этой отрасли пока нет своих станков — доступны только общие объекты.");
        else
            AddEntryRows(root, profile.Buildings, isCommon: false);

        AddSectionLabel(root, "Общее");
        var common = new List<BuildingType>(ZoneBuildCatalog.Common.Count);
        for (int i = 0; i < ZoneBuildCatalog.Common.Count; i++)
        {
            var type = ZoneBuildCatalog.Common[i];
            // У фермы нет стен — общий объект стены ей не показываем.
            if (isFarm && type == BuildingType.WoodWall)
                continue;
            common.Add(type);
        }
        AddEntryRows(root, common, isCommon: true);
        if (!isFarm)
            AddDoorRow(root);
        RefreshCostAvailability(); // task-21: цвета цены по текущему складу
        _costRefreshTimer = 0f;
    }

    private void AddSectionLabel(Node parent, string text)
    {
        var label = new Label
        {
            Name = "Section",
            Text = text,
            MouseFilter = MouseFilterEnum.Ignore
        };
        label.AddThemeColorOverride("font_color", new Color(0.75f, 0.75f, 0.78f));
        label.AddThemeFontSizeOverride("font_size", 18);
        parent.AddChild(label);
        _dynamicNodes.Add(label);
    }

    private void AddHintLabel(Node parent, string text)
    {
        var label = new Label
        {
            Name = "Hint",
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore
        };
        label.AddThemeColorOverride("font_color", new Color(0.55f, 0.55f, 0.58f));
        label.AddThemeFontSizeOverride("font_size", 14);
        parent.AddChild(label);
        _dynamicNodes.Add(label);
    }

    private void AddEntryRows(Node parent, IReadOnlyList<BuildingType> types, bool isCommon)
    {
        if (types == null || types.Count == 0)
            return;
        HBoxContainer row = null;
        int inRow = 0;
        for (int i = 0; i < types.Count; i++)
        {
            if (row == null || inRow >= MaxButtonsPerRow)
            {
                row = NewCatalogRow(parent);
                inRow = 0;
            }
            row.AddChild(CreateCatalogItem(types[i], isCommon));
            inRow++;
        }
    }

    private HBoxContainer NewCatalogRow(Node parent)
    {
        var row = new HBoxContainer { Name = "CatalogRow" };
        row.AddThemeConstantOverride("separation", RowSeparation);
        row.MouseFilter = MouseFilterEnum.Ignore;
        parent.AddChild(row);
        _dynamicNodes.Add(row);
        return row;
    }

    /// <summary>
    /// task-21: кнопка объекта + ПОДПИСЬ ПОЛНОЙ ЦЕНЫ ПО ТИПАМ («12 брёвен · 25 камня»)
    /// с иконками материалов. Не хватает на складе — подпись красноватая.
    /// Отдельная VBox, чтобы цена была видна без наведения: раньше в окне не было
    /// цены вообще, хотя станки стоят брёвна + камень (task-19).
    /// </summary>
    private Control CreateCatalogItem(BuildingType type, bool isCommon)
    {
        string title = ZoneBuildCatalog.TitleFor(type);
        var btn = new Button
        {
            Name = "Item_" + type,
            CustomMinimumSize = new Vector2(50, 50),
            MouseFilter = MouseFilterEnum.Stop,
            TooltipText = BuildCatalogTooltip(title, type),
            ExpandIcon = true
        };
        var icon = LoadIcon(ZoneBuildCatalog.IconPathFor(type));
        if (icon != null)
            btn.Icon = icon;
        else
            btn.Text = title;

        switch (type)
        {
            case BuildingType.WoodWall:
                // Стена — это режим материала стены (попап), как верхний ряд окна.
                if (!isCommon)
                    btn.Pressed += () => GetViewport()?.SetInputAsHandled();
                else
                    btn.Pressed += ToggleMaterialPopup;
                break;
            case BuildingType.CentralBlock:
                btn.Pressed += OnCentralBlockPressed;
                break;
            default:
                // task-16: станок/мебель ставится в мир тем же tool-каналом, что
                // мебель из HUD: BuildTool → призрак под курсором → ЛКМ → чертёж
                // → доставка материалов → постройка.
                btn.Pressed += () => StartBuildingTool(type);
                break;
        }

        _costTooltips.Add((btn, type, title));

        var wrapper = new VBoxContainer
        {
            Name = "ItemBox_" + type,
            MouseFilter = MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(50, 66)
        };
        wrapper.AddThemeConstantOverride("separation", 1);
        wrapper.AddChild(btn);
        wrapper.AddChild(BuildCostRow(type));
        return wrapper;
    }

    /// <summary>Компактная строка цены: [icon 12][count] · [icon 12][count].</summary>
    private Control BuildCostRow(BuildingType type)
    {
        var row = new HBoxContainer
        {
            Name = "CostRow",
            MouseFilter = MouseFilterEnum.Ignore,
            Alignment = BoxContainer.AlignmentMode.Center
        };
        row.AddThemeConstantOverride("separation", 1);

        IReadOnlyList<(ItemId Item, int Count)> materials = StructureRegistry.Get(type).RequiredMaterials;
        for (int i = 0; i < materials.Count; i++)
        {
            if (i > 0)
            {
                var dot = new Label { Text = "·", MouseFilter = MouseFilterEnum.Ignore };
                dot.AddThemeFontSizeOverride("font_size", 11);
                dot.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.65f));
                row.AddChild(dot);
            }

            (ItemId item, int count) = materials[i];
            var entry = new CostEntry { Item = item, Required = count };

            Texture2D materialIcon = LoadDropIcon(item);
            if (materialIcon != null)
            {
                var rect = new TextureRect
                {
                    CustomMinimumSize = new Vector2(12, 12),
                    Texture = materialIcon,
                    TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    MouseFilter = MouseFilterEnum.Ignore
                };
                row.AddChild(rect);
            }

            var label = new Label
            {
                Text = count.ToString(),
                MouseFilter = MouseFilterEnum.Ignore
            };
            label.AddThemeFontSizeOverride("font_size", 11);
            bool enoughNow = StructureCost.HasEnough(item, count);
            entry.Enough = enoughNow;
            label.AddThemeColorOverride("font_color", enoughNow ? CostColorEnough : CostColorMissing);
            entry.Label = label;
            row.AddChild(label);
            _costEntries.Add(entry);
        }
        return row;
    }

    /// <summary>Тултип объекта: полная цена по типам + (task-20) что производит станок.</summary>
    private static string BuildCatalogTooltip(string title, BuildingType type)
    {
        string tooltip = StructureCost.Tooltip(title, type);
        var recipe = RecipeRegistry.GetForStation(type);
        if (recipe != null)
            tooltip += $"\nПроизводство: {recipe.Summary} · {RecipeRegistry.DurationLabel(recipe)}";
        return tooltip;
    }

    /// <summary>Обновить цвета цен (раз в секунду, только при открытом окне).</summary>
    private void RefreshCostAvailability()
    {
        for (int i = 0; i < _costEntries.Count; i++)
        {
            CostEntry e = _costEntries[i];
            if (e.Label == null || !GodotObject.IsInstanceValid(e.Label))
                continue;
            bool enough = StructureCost.HasEnough(e.Item, e.Required);
            if (enough == e.Enough)
                continue;
            e.Enough = enough;
            e.Label.AddThemeColorOverride("font_color", enough ? CostColorEnough : CostColorMissing);
        }
        for (int i = 0; i < _costTooltips.Count; i++)
        {
            (Button button, BuildingType type, string title) = _costTooltips[i];
            if (button == null || !GodotObject.IsInstanceValid(button))
                continue;
            string tooltip = BuildCatalogTooltip(title, type);
            if (!StructureCost.IsFullyAvailable(type))
                tooltip += "\nНе хватает материалов на складе";
            if (button.TooltipText != tooltip)
                button.TooltipText = tooltip;
        }
    }

    /// <summary>
    /// task-16: вооружить BuildTool конкретным объектом каталога (станок/мебель).
    /// Источник тайла — MapRenderer.SourceForBuilding (мультиклеточный для станков),
    /// призрак — GhostSourceForBuilding внутри BuildTool.
    /// </summary>
    private void StartBuildingTool(BuildingType type)
    {
        var interaction = PlayerInteractionManager.Instance;
        var mapRenderer = MapRenderer.Instance;
        if (interaction == null || mapRenderer?.WallBuildManager == null || mapRenderer?.MapData == null)
            return;

        var tool = new BuildTool(
            mapRenderer.WallBuildManager,
            mapRenderer.GhostLayer,
            mapRenderer.MapData,
            type,
            MapRenderer.SourceForBuilding(type)
        );
        interaction.SetTool(tool);
        tool.WorldPlacement = HUDController.Instance?.WorldPlacement;
    }

    /// <summary>«Дверь» — общий режим (как DoorButton верхнего ряда), иконка из сцены.</summary>
    private void AddDoorRow(Node parent)
    {
        var row = NewCatalogRow(parent);
        var btn = new Button
        {
            Name = "Item_Door",
            CustomMinimumSize = new Vector2(50, 50),
            MouseFilter = MouseFilterEnum.Stop,
            TooltipText = "Дверь (проём в стене)",
            ExpandIcon = true
        };
        var icon = LoadIcon("res://ui/assets/textures/icons/BuildIcon/door.png");
        if (icon != null)
            btn.Icon = icon;
        else
            btn.Text = "Дверь";
        btn.Pressed += OnDoorPressed;
        row.AddChild(btn);
    }

    private static Texture2D LoadIcon(string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;
        try
        {
            return ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Иконка материала по ItemId для строки цены: StructureCost отдаёт только
    /// короткий id ("logs"/"stone"/"wheat"/сырьё), а лежит арт в drop/.
    /// </summary>
    private static Texture2D LoadDropIcon(ItemId item)
    {
        string id = StructureCost.IconResourceId(item);
        if (string.IsNullOrEmpty(id))
            return null;
        return LoadIcon(ItemConfig.DropTextureRoot + id + ".png");
    }

    private void ClearDynamicNodes()
    {
        foreach (Node node in _dynamicNodes)
        {
            if (!GodotObject.IsInstanceValid(node))
                continue;
            // Сразу отцепляем от дерева (QueueFree по умолчанию отложен до конца
            // кадра — старые и новые ряды успевали отрисоваться одновременно).
            node.GetParent()?.RemoveChild(node);
            node.QueueFree();
        }
        _dynamicNodes.Clear();
    }

    /// <summary>
    /// Программная пересборка нижнего списка: по максимум MaxButtonsPerRow кнопок в HBox,
    /// каждый следующий ряд — новый HBoxContainer с separation RowSeparation.
    /// Пустой список — вернуть шаблон сцены (5 заглушек) как есть.
    /// </summary>
    public void RebuildButtons(IReadOnlyList<BuildMenuItem> items)
    {
        if (_vbox == null && _itemsRoot == null)
            return;
        ClearDynamicNodes();

        if (items == null || items.Count == 0)
        {
            if (_templateRow != null)
                _templateRow.Visible = true;
            return;
        }

        // Есть динамика — шаблон прячем, строим ряды по 5.
        if (_templateRow != null)
            _templateRow.Visible = false;

        var root = _itemsRoot ?? _vbox;
        HBoxContainer currentRow = null;
        int inRow = 0;
        foreach (var item in items)
        {
            if (currentRow == null || inRow >= MaxButtonsPerRow)
            {
                currentRow = NewCatalogRow(root);
                inRow = 0;
            }
            var btn = new Button
            {
                Text = item.Title,
                CustomMinimumSize = new Vector2(50, 50),
                MouseFilter = MouseFilterEnum.Stop,
                TooltipText = item.Title,
                ExpandIcon = true
            };
            var icon = LoadIcon(item.IconPath);
            if (icon != null)
            {
                btn.Icon = icon;
                btn.Text = string.Empty;
            }
            btn.Pressed += () => GetViewport()?.SetInputAsHandled();
            currentRow.AddChild(btn);
            inRow++;
        }
    }

    // --- Попап материалов ---

    private void BuildMaterialPopup()
    {
        if (_rootPanel == null)
            return;
        _materialPopup = new PanelContainer { Name = "MaterialPopup", Visible = false };
        _materialPopup.MouseFilter = MouseFilterEnum.Stop;
        _materialPopup.ZIndex = 50;
        _materialRows = new VBoxContainer();
        _materialRows.AddThemeConstantOverride("separation", MaterialSeparation);
        _materialPopup.AddChild(_materialRows);
        _rootPanel.AddChild(_materialPopup);
        RebuildMaterialPopup();
        UpdateMaterialSelection();
    }

    private void RebuildMaterialPopup()
    {
        if (_materialRows == null)
            return;
        foreach (Node c in _materialRows.GetChildren())
            c.QueueFree();
        _materialButtons.Clear();

        HBoxContainer row = null;
        int inRow = 0;
        foreach (var mat in WallMaterialRegistry.All)
        {
            if (row == null || inRow >= MaterialPerRow)
            {
                row = new HBoxContainer();
                row.AddThemeConstantOverride("separation", MaterialSeparation);
                row.MouseFilter = MouseFilterEnum.Stop;
                _materialRows.AddChild(row);
                inRow = 0;
            }
            var btn = new Button
            {
                Text = mat.Title,
                CustomMinimumSize = new Vector2(MaterialButtonSize, MaterialButtonSize),
                MouseFilter = MouseFilterEnum.Stop,
                TooltipText = mat.Title
            };
            string id = mat.Id;
            btn.Pressed += () => SelectMaterial(id);
            row.AddChild(btn);
            _materialButtons[id] = btn;
            inRow++;
        }
    }

    private void ToggleMaterialPopup()
    {
        if (_materialPopup == null)
            return;
        if (_materialPopup.Visible)
        {
            CloseMaterialPopup();
            return;
        }
        RebuildMaterialPopup();
        UpdateMaterialSelection();
        // Позиция: под кнопкой материала, поверх всего.
        Vector2 at = _wallMaterialButton != null
            ? _wallMaterialButton.GetGlobalRect().Position + new Vector2(0, _wallMaterialButton.Size.Y + 8)
            : new Vector2(120, 200);
        _materialPopup.Position = at;
        _materialPopup.Visible = true;
    }

    private void CloseMaterialPopup()
    {
        if (_materialPopup != null)
            _materialPopup.Visible = false;
    }

    private void SelectMaterial(string id)
    {
        _selectedMaterialId = string.IsNullOrEmpty(id) ? "wood" : id;
        UpdateMaterialSelection();
        var hud = _draft;
        hud?.SetDraftMaterial(_selectedMaterialId);
        CloseMaterialPopup();
    }

    private void UpdateMaterialSelection()
    {
        foreach (var kv in _materialButtons)
        {
            bool sel = kv.Key == _selectedMaterialId;
            kv.Value.Modulate = sel ? new Color(0.6f, 1f, 0.6f, 1f) : Color.Color8(255, 255, 255, 255);
        }
    }

    public override void _Process(double delta)
    {
        // task-21: пока окно открыто — раз в секунду перекрашивать цену под склад.
        if (_rootPanel == null || !_rootPanel.Visible || _costEntries.Count == 0)
            return;
        if (!double.IsFinite(delta) || delta < 0d)
            delta = 0d;
        _costRefreshTimer += (float)delta;
        if (_costRefreshTimer < 1.0f)
            return;
        _costRefreshTimer = 0f;
        RefreshCostAvailability();
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
        base._ExitTree();
    }
}

/// <summary>Одна кнопка стройобъекта в универсальном окне.
/// task-12: несёт иконку и тип постройки (каталог зоны), чтобы окно рисовало
/// предметы отрасли спрайтами, а не текстом.</summary>
public readonly struct BuildMenuItem
{
    public readonly string Id;
    public readonly string Title;
    public readonly string IconPath;
    public readonly BuildingType? Type;

    public BuildMenuItem(string id, string title)
        : this(id, title, null, null)
    {
    }

    public BuildMenuItem(string id, string title, string iconPath, BuildingType? type = null)
    {
        Id = id;
        Title = title;
        IconPath = iconPath;
        Type = type;
    }
}