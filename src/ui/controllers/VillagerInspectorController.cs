using System;
using Godot;
using Game.Core;
using Game.Simulation;

namespace Game.UI;

/// <summary>
/// Контроллер инспектора жителей колонии (scenes/ui/villager.tscn).
/// Отображает имя, профессию, статус, инвентарь и шкалы потребностей выбранного агента.
/// </summary>
public partial class VillagerInspectorController : Control
{
    public static VillagerInspectorController Instance { get; private set; }

    private Control _rootControl;
    private Label _nameLabel;
    private Label _classLabel;
    private Label _stateLabel;
    private Label _carriedItemLabel;

    private ProgressBar _healthBar;
    private Label _healthValueLabel;

    private ProgressBar _hungerBar;
    private Label _hungerValueLabel;

    private ProgressBar _sleepBar;
    private Label _sleepValueLabel;

    private ProgressBar _fatigueBar;
    private Label _fatigueValueLabel;

    private ProgressBar _satisfactionBar;
    private Label _satisfactionValueLabel;

    private Button _escButton;

    private int _selectedAgentIndex = -1;
    public bool IsOpen => _rootControl != null && _rootControl.Visible;
    public int SelectedAgentIndex => _selectedAgentIndex;

    public override void _Ready()
    {
        Instance = this;
        Initialize(this);
    }

    public void Initialize(Control root)
    {
        _rootControl = root;

        _nameLabel = root.FindChild("Label", true, false) as Label;
        _escButton = root.FindChild("esc", true, false) as Button;

        _classLabel = root.FindChild("ClassLabel", true, false) as Label;
        _stateLabel = root.FindChild("StateLabel", true, false) as Label;
        _carriedItemLabel = root.FindChild("CarriedItemLabel", true, false) as Label;

        _healthBar = root.FindChild("HealthBar", true, false) as ProgressBar;
        _healthValueLabel = root.FindChild("HealthValue", true, false) as Label;

        _hungerBar = root.FindChild("HungerBar", true, false) as ProgressBar;
        _hungerValueLabel = root.FindChild("HungerValue", true, false) as Label;

        _sleepBar = root.FindChild("SleepBar", true, false) as ProgressBar;
        _sleepValueLabel = root.FindChild("SleepValue", true, false) as Label;

        _fatigueBar = root.FindChild("FatigueBar", true, false) as ProgressBar;
        _fatigueValueLabel = root.FindChild("FatigueValue", true, false) as Label;

        _satisfactionBar = root.FindChild("SatisfactionBar", true, false) as ProgressBar;
        _satisfactionValueLabel = root.FindChild("SatisfactionValue", true, false) as Label;

        var fatigueTitle = root.FindChild("FatigueBox", true, false)?.FindChild("Title", true, false) as Label;
        if (fatigueTitle != null) fatigueTitle.Text = "Энергия";

        var satTitle = root.FindChild("SatBox", true, false)?.FindChild("Title", true, false) as Label;
        if (satTitle != null) satTitle.Text = "Настроение";

        if (_escButton != null)
        {
            _escButton.Pressed -= Close;
            _escButton.Pressed += Close;
        }

        _rootControl.Visible = false;
    }

    public void Open(int agentIndex)
    {
        _selectedAgentIndex = agentIndex;
        if (_rootControl != null)
        {
            _rootControl.Visible = true;
            _rootControl.MoveToFront();
            RefreshDisplay();
        }
    }

    private static readonly string[] ColonistNames =
    {
        "Иван", "Алексей", "Дмитрий", "Сергей", "Михаил", "Андрей", "Николай", "Пётр", "Евгений", "Владимир",
        "Анна", "Елена", "Ольга", "Татьяна", "Мария", "Екатерина", "Наталья", "Светлана", "Ирина", "Юлия",
        "Артём", "Максим", "Денис", "Павел", "Роман", "Виктор", "Константин", "Илья", "Григорий", "Василий",
        "Алина", "Дарья", "Полина", "Ксения", "Виктория", "Валерия", "Алиса", "Маргарита", "Вероника", "Любовь"
    };

    public static string GetColonistName(int agentIndex)
    {
        int nameIdx = (int)(((uint)agentIndex * 2654435761u) % (uint)ColonistNames.Length);
        return $"{ColonistNames[nameIdx]} (#{agentIndex + 1})";
    }

    private static string GetJobTitle(JobTypeId job) => job switch
    {
        JobTypeId.TreeChopping => "Лесоруб",
        JobTypeId.Mining => "Каменотёс",
        JobTypeId.OreMining => "Шахтёр",
        JobTypeId.Farming => "Фермер",
        JobTypeId.Harvesting => "Сборщик урожая",
        JobTypeId.Construction => "Строитель",
        JobTypeId.BlueprintDelivery => "Подвоз материалов",
        JobTypeId.StockpileHauling => "Переноска на склад",
        JobTypeId.Crafting => "Ремесленник",
        JobTypeId.Production => "Повар",
        JobTypeId.None => "Без назначения",
        _ => job.ToString()
    };

    public void Close()
    {
        _selectedAgentIndex = -1;
        if (_rootControl != null)
        {
            _rootControl.Visible = false;
        }
    }

    public override void _Process(double delta)
    {
        if (IsOpen && _selectedAgentIndex >= 0)
        {
            RefreshDisplay();
        }
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (IsOpen && @event is InputEventKey key && key.Pressed && key.Keycode == Key.Escape)
        {
            Close();
            GetViewport()?.SetInputAsHandled();
        }
    }

    private void RefreshDisplay()
    {
        var sim = AgentSimulationThread.Instance;
        if (sim == null || sim.Pool == null || _selectedAgentIndex < 0 || _selectedAgentIndex >= sim.Pool.Capacity)
        {
            Close();
            return;
        }

        var pool = sim.Pool;
        int i = _selectedAgentIndex;

        if (_nameLabel != null)
            _nameLabel.Text = GetColonistName(i);

        JobTypeId job = pool.CurrentJobType != null ? pool.CurrentJobType[i] : JobTypeId.None;

        if (_classLabel != null)
        {
            string cls = pool.Classes != null ? pool.Classes[i].ToString() : "Laborer";
            if (cls == "Worker")
            {
                int zoneId = pool.AssignedZoneId != null ? pool.AssignedZoneId[i] : -1;
                string jobStr = job != JobTypeId.None ? $" ({GetJobTitle(job)})" : "";
                _classLabel.Text = $"Профессия: Специалист зоны #{zoneId}{jobStr}";
            }
            else
            {
                string jobStr = job != JobTypeId.None ? $" ({GetJobTitle(job)})" : " (свободен)";
                _classLabel.Text = $"Профессия: Разнорабочий{jobStr}";
            }
        }

        if (_stateLabel != null)
        {
            AgentState st = pool.States[i];
            float progress = pool.WorkProgress != null ? pool.WorkProgress[i] : 0f;

            string actionText = st switch
            {
                AgentState.Idle => "Отдыхает (ожидает задачу)",
                AgentState.MovingToSource => "Идёт за ресурсом",
                AgentState.MovingToTarget => $"Идёт к цели ({GetJobTitle(job)})",
                AgentState.Working => $"Работает: {GetJobTitle(job)} ({(int)(progress * 100f)}%)",
                AgentState.Evacuating => "Уступает дорогу",
                _ => st.ToString()
            };
            _stateLabel.Text = $"Действие: {actionText}";
        }

        if (_carriedItemLabel != null)
        {
            ItemId item = pool.CarriedItemId != null ? pool.CarriedItemId[i] : ItemId.None;
            int count = pool.CarriedItemCount != null ? pool.CarriedItemCount[i] : 0;
            if (item != ItemId.None && count > 0)
            {
                ItemRegistry.TryGet(item, out var def);
                string name = def?.Name ?? item.ToString();
                _carriedItemLabel.Text = $"Груз: {count}x {name}";
            }
            else
            {
                _carriedItemLabel.Text = "Груз: (нет)";
            }
        }

        // Потребности и энергия/настроение:
        float hunger = pool.Hunger != null ? pool.Hunger[i] : 0f;
        float sleep = pool.Sleep != null ? pool.Sleep[i] : 0f;
        float fatigue = pool.Fatigue != null ? pool.Fatigue[i] : 0f;
        float satisfaction = pool.EnvironmentSatisfaction != null ? pool.EnvironmentSatisfaction[i] : 100f;
        float mood = pool.Mood != null ? pool.Mood[i] : Math.Clamp(100f - hunger * 0.4f - fatigue * 0.3f, 0f, 100f);
        float energy = Math.Clamp(100f - fatigue, 0f, 100f);

        if (_healthBar != null)
        {
            _healthBar.Value = hunger >= 100f ? 20 : 100;
        }
        if (_healthValueLabel != null)
        {
            _healthValueLabel.Text = hunger >= 100f ? "20% (Истощение!)" : "100%";
        }

        if (_hungerBar != null) _hungerBar.Value = hunger;
        if (_hungerValueLabel != null) _hungerValueLabel.Text = $"{(int)hunger}%";

        if (_sleepBar != null) _sleepBar.Value = sleep;
        if (_sleepValueLabel != null) _sleepValueLabel.Text = $"{(int)sleep}%";

        if (_fatigueBar != null) _fatigueBar.Value = energy;
        if (_fatigueValueLabel != null) _fatigueValueLabel.Text = $"{(int)energy}%";

        if (_satisfactionBar != null) _satisfactionBar.Value = mood;
        if (_satisfactionValueLabel != null) _satisfactionValueLabel.Text = $"{(int)mood}%";
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }
}
