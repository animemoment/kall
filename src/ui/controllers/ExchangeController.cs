using Godot;
using System;
using System.Collections.Generic;
using Game.UI.Resources;

namespace Game.UI;

/// <summary>
/// Window "Биржи" (Exchange.tscn): two independent pages switched by the buttons in the header.
///
/// Commodity page — the whole raw-resource atlas (90 entries, see _plan/RESOURCE_ATLAS_LAYOUT.md),
/// grouped by category, each row with icon/name/price and buy/sell buttons.
/// Stock page — a skeleton: table header plus a placeholder text; company shares need
/// enterprises with profit, which the game does not have yet.
///
/// Stage 1 (this file) is interface only: a deal moves the cash counter and writes one line to
/// the deal log. The stockpile and the world are NOT touched — real trading arrives in stage 2.
/// </summary>
public partial class ExchangeController : Control
{
    public static ExchangeController Instance { get; private set; }

    /// <summary>Cash the colony starts with.</summary>
    [Export] public int StartingMoney = 500;

    /// <summary>How many units one click trades.</summary>
    [Export] public int LotSize = 10;

    private Control _rootPanel;
    private Label _titleLabel;
    private Label _moneyLabel;
    private Label _dealLogLabel;

    private Button _tabCommodity;
    private Button _tabStock;
    private Control _commodityPage;
    private Control _stockPage;

    private VBoxContainer _commodityList;
    private HBoxContainer _templateRow;
    private VBoxContainer _stockRows;

    private BaseButton _escButton;
    private BaseButton _checkMarkButton;

    private int _money;
    public int Money => _money;

    /// <summary>Which page is visible now: true = commodity, false = stock.</summary>
    public bool IsCommodityVisible => _commodityPage != null && _commodityPage.Visible;

    /// <summary>Rows built for the commodity page (0 until the window is first opened).</summary>
    public int CommodityRowCount => _builtRows.Count;

    public event Action OnOpened;
    public event Action OnClosed;

    public bool IsOpen => _rootPanel != null && _rootPanel.Visible;

    private readonly List<Control> _builtRows = new();

    public override void _Ready()
    {
        Instance = this;
        ZIndex = 20;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _money = StartingMoney;
        LoadScene();
    }

    private void LoadScene()
    {
        var paths = new[]
        {
            "res://scenes/ui/Exchange.tscn",
            "res://ui/Exchange.tscn",
            "res://Exchange.tscn"
        };

        foreach (string path in paths)
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
            _moneyLabel = _rootPanel.FindChild("MoneyAmount", true, false) as Label;
            _dealLogLabel = _rootPanel.FindChild("DealLog", true, false) as Label;

            _tabCommodity = _rootPanel.FindChild("TabCommodity", true, false) as Button;
            _tabStock = _rootPanel.FindChild("TabStock", true, false) as Button;
            _commodityPage = _rootPanel.FindChild("CommodityPage", true, false) as Control;
            _stockPage = _rootPanel.FindChild("StockPage", true, false) as Control;

            _commodityList = _rootPanel.FindChild("VBoxContainer", true, false) as VBoxContainer;
            _templateRow = _rootPanel.FindChild("HBoxContainer2", true, false) as HBoxContainer;
            _stockRows = _rootPanel.FindChild("StockRows", true, false) as VBoxContainer;

            _escButton = _rootPanel.FindChild("esc", true, false) as BaseButton;
            _checkMarkButton = _rootPanel.FindChild("CheckMark", true, false) as BaseButton;

            if (_escButton != null)
            {
                _escButton.MouseFilter = MouseFilterEnum.Stop;
                _escButton.Pressed += Close;
            }
            if (_checkMarkButton != null)
            {
                _checkMarkButton.MouseFilter = MouseFilterEnum.Stop;
                _checkMarkButton.Pressed += ConfirmAndClose;
            }
            if (_tabCommodity != null)
            {
                _tabCommodity.MouseFilter = MouseFilterEnum.Stop;
                _tabCommodity.Pressed += ShowCommodityPage;
            }
            if (_tabStock != null)
            {
                _tabStock.MouseFilter = MouseFilterEnum.Stop;
                _tabStock.Pressed += ShowStockPage;
            }

            UpdateMoneyLabel();
            return;
        }

        GD.PushError("[Exchange] Exchange.tscn not found — the exchange window cannot open");
    }

    /// <summary>
    /// Mouse rule, same as the build window (§113.5/B1): panels and lists catch the mouse,
    /// the scroll container takes the wheel but still lets clicks through.
    ///
    /// The scroll must NOT be Ignore: an Ignore control never sees the wheel, so it travelled
    /// into the world and zoomed the camera instead of scrolling the list (R-074). Pass keeps
    /// clicks transparent and keeps the wheel.
    /// </summary>
    private static void ConfigureInputBlocking(Node node)
    {
        if (node == null)
            return;
        if (node is Panel || node is PanelContainer || node is ItemList)
            node.Set("mouse_filter", (int)Control.MouseFilterEnum.Stop);
        if (node is ScrollContainer)
            node.Set("mouse_filter", (int)Control.MouseFilterEnum.Pass);
        foreach (Node child in node.GetChildren())
        {
            if (child is Control childCtrl)
                ConfigureInputBlocking(childCtrl);
        }
    }

    public void Open()
    {
        if (_rootPanel == null)
            return;
        BuildCommodityRows();
        ShowCommodityPage();
        _rootPanel.Visible = true;
        UpdateMoneyLabel();
        OnOpened?.Invoke();
    }

    public void Close()
    {
        if (_rootPanel != null && _rootPanel.Visible)
        {
            _rootPanel.Visible = false;
            OnClosed?.Invoke();
        }
    }

    /// <summary>CheckMark: stage 1 has nothing to commit, so it closes the window like esc.</summary>
    private void ConfirmAndClose()
    {
        Close();
    }

    public void ShowCommodityPage()
    {
        SetPage(commodity: true);
    }

    public void ShowStockPage()
    {
        SetPage(commodity: false);
    }

    private void SetPage(bool commodity)
    {
        if (_commodityPage != null)
            _commodityPage.Visible = commodity;
        if (_stockPage != null)
            _stockPage.Visible = !commodity;
        if (_tabCommodity != null)
            _tabCommodity.ButtonPressed = commodity;
        if (_tabStock != null)
            _tabStock.ButtonPressed = !commodity;
    }

    /// <summary>
    /// One Escape closes the window. Handled before PlayerInteractionManager's stage, the same
    /// way BuildMenuController does it (R-066): with a tool armed, PIM would otherwise consume
    /// the key first.
    /// </summary>
    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (!IsOpen || @event is not InputEventKey key)
            return;
        if (!key.Pressed || key.Echo || key.Keycode != Key.Escape)
            return;
        Close();
        GetViewport().SetInputAsHandled();
    }

    /// <summary>Fallback when the earlier stage was skipped.</summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (!IsOpen || @event is not InputEventKey key)
            return;
        if (!key.Pressed || key.Echo || key.Keycode != Key.Escape)
            return;
        Close();
        GetViewport().SetInputAsHandled();
    }

    // --- commodity page ---

    /// <summary>
    /// Builds one row per atlas entry, grouped by category. Rows live until the node is freed;
    /// reopening the window reuses them.
    /// </summary>
    private void BuildCommodityRows()
    {
        if (_commodityList == null || _builtRows.Count > 0)
            return;

        if (_templateRow != null)
            _templateRow.Visible = false;

        string currentCategory = null;
        foreach (ResourceAtlasEntry entry in ResourceAtlasCatalog.Entries)
        {
            if (!string.Equals(entry.Category, currentCategory, StringComparison.Ordinal))
            {
                currentCategory = entry.Category;
                AddCategoryCaption(currentCategory);
            }
            AddResourceRow(entry);
        }

        if (_builtRows.Count == 0)
        {
            GD.PushWarning(
                $"[Exchange] commodity list is empty: {ResourceAtlasCatalog.LastError}");
        }
        else
        {
            // Rows are added after the scene was laid out once, so the containers still hold the
            // old minimum (the window rendered as a 20 px strip). Re-sorting the chain from the
            // list up to the scroll tells the scroll the real content height.
            RefreshCommodityLayout();
        }
    }

    /// <summary>
    /// Forces a relayout of the commodity list after rows were appended. Godot laid the scene
    /// out before the rows existed, so the content panel kept a 20 px box and the list stayed
    /// 0×0 (the window looked like a thin strip). QueueSort alone does not fix it here: the
    /// scroll re-uses the cached minimum, so the panel gets its real height explicitly and the
    /// inner containers are re-sorted afterwards.
    /// </summary>
    private void RefreshCommodityLayout()
    {
        if (_commodityList == null)
            return;

        var panel = _commodityList.GetParent() as Control;
        if (panel != null)
        {
            float height = Math.Max(panel.GetCombinedMinimumSize().Y, 1f);
            if (Math.Abs(panel.Size.Y - height) > 0.5f)
                panel.Size = new Vector2(panel.Size.X, height);
        }

        Node node = _commodityList;
        while (node != null && node != _rootPanel)
        {
            if (node is Container container)
                container.QueueSort();
            if (node is ScrollContainer)
                break;
            node = node.GetParent();
        }

        // The rows were created this frame, so their own minimum is not known yet: sort them
        // again after the layout pass, otherwise every row keeps the width it guessed first.
        CallDeferred(nameof(RefreshCommodityLayoutDeferred));
    }

    /// <summary>Second layout pass, run after Godot applied the sizes of the fresh rows.</summary>
    private void RefreshCommodityLayoutDeferred()
    {
        if (_commodityList == null || !IsInstanceValid(_commodityList))
            return;
        Node node = _commodityList;
        while (node != null && node != _rootPanel)
        {
            if (node is Container container)
                container.QueueSort();
            if (node is ScrollContainer)
                break;
            node = node.GetParent();
        }
    }

    private void AddCategoryCaption(string category)
    {
        var caption = new Label
        {
            Text = string.IsNullOrEmpty(category) ? "БЕЗ КАТЕГОРИИ" : category.ToUpperInvariant(),
            MouseFilter = MouseFilterEnum.Ignore
        };
        caption.AddThemeColorOverride("font_color", new Color(0.545f, 0.545f, 0.545f, 1f));
        caption.AddThemeFontSizeOverride("font_size", 18);
        _commodityList.AddChild(caption);
        _builtRows.Add(caption);
    }

    private void AddResourceRow(ResourceAtlasEntry entry)
    {
        var plate = new PanelContainer
        {
            MouseFilter = MouseFilterEnum.Stop,
            // Stretch to the list width: without it every row shrinks to its own labels.
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        plate.AddThemeStyleboxOverride("panel", ResourceRowStyle());

        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 10);
        plate.AddChild(row);

        var icon = new TextureRect
        {
            CustomMinimumSize = new Vector2(32, 32),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Texture = ResourceAtlasCatalog.TextureFor(entry),
            MouseFilter = MouseFilterEnum.Ignore
        };
        row.AddChild(icon);

        var title = new Label
        {
            Text = entry.NameRu,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore
        };
        title.AddThemeFontSizeOverride("font_size", 18);
        row.AddChild(title);

        var price = new Label
        {
            Text = PriceFor(entry).ToString(),
            CustomMinimumSize = new Vector2(60, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore
        };
        price.AddThemeFontSizeOverride("font_size", 18);
        row.AddChild(price);

        Button buy = MakeDealButton("КУПИТЬ");
        buy.Pressed += () => Trade(entry, buy: true);
        row.AddChild(buy);

        Button sell = MakeDealButton("ПРОДАТЬ");
        sell.Pressed += () => Trade(entry, buy: false);
        row.AddChild(sell);

        _commodityList.AddChild(plate);
        _builtRows.Add(plate);
    }

    private static Button MakeDealButton(string text)
    {
        var button = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(96, 32),
            MouseFilter = MouseFilterEnum.Stop
        };
        return button;
    }

    /// <summary>
    /// Stage 1 price: a constant per atlas category. Stage 2 replaces this body with a real
    /// supply/demand read of the stockpile — callers only ever touch this method.
    /// </summary>
    public int PriceFor(ResourceAtlasEntry entry)
    {
        return entry.Category switch
        {
            "еда" => 1,
            "руда" => 3,
            "энергия" => 4,
            "химия" => 4,
            "камень" => 2,
            "заполнитель" => 1,
            "лес" => 2,
            "волокно" => 2,
            "утилита" => 1,
            _ => 1
        };
    }

    /// <summary>
    /// Stage 1 deal: cash moves, the log gets one line. The stockpile and the world are not
    /// touched on purpose — real trading is stage 2.
    /// </summary>
    public void Trade(ResourceAtlasEntry entry, bool buy)
    {
        int cost = PriceFor(entry) * LotSize;
        if (buy && cost > _money)
        {
            SetDealLog($"Не хватает денег: {entry.NameRu} стоит {cost}.");
            return;
        }

        _money += buy ? -cost : cost;
        UpdateMoneyLabel();
        SetDealLog(buy
            ? $"Куплено {entry.NameRu}: {LotSize} шт. за {cost}."
            : $"Продано {entry.NameRu}: {LotSize} шт. за {cost}.");
    }

    private void UpdateMoneyLabel()
    {
        if (_moneyLabel != null)
            _moneyLabel.Text = _money.ToString();
    }

    private void SetDealLog(string text)
    {
        if (_dealLogLabel != null)
            _dealLogLabel.Text = text;
    }

    private static StyleBoxFlat ResourceRowStyle() => new()
    {
        BgColor = new Color(0.043137256f, 0.043137256f, 0.043137256f, 1f),
        ContentMarginLeft = 8f,
        ContentMarginTop = 3f,
        ContentMarginRight = 8f,
        ContentMarginBottom = 3f,
        BorderWidthLeft = 1,
        BorderWidthTop = 1,
        BorderWidthRight = 1,
        BorderWidthBottom = 1,
        BorderColor = new Color(0.09803922f, 0.09803922f, 0.09803922f, 1f)
    };
}
