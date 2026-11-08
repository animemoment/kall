using System;
using Godot;
using Game.Core;
using Game.Simulation;
using Game.UI.Tools;

namespace Game.UI;

public partial class PlayerInteractionManager : Node2D
{
    public static PlayerInteractionManager Instance { get; private set; }

    [Export] public CameraController Camera { get; set; }
    [Export] public SelectionBox Selection { get; set; }
    [Export] public TileMapLayer WallLayer { get; set; }

    private ITool _currentTool;
    private SelectTool _defaultTool;
    private ICellProbeOverlay _cellProbe;
    public ITool DefaultTool => _defaultTool;

    /// <summary>
    /// Прокинуть источник подсказок клетки в дефолтный инструмент. Принимает узкий интерфейс:
    /// PlayerInteractionManager не должен ссылаться на HUDController, иначе цикл
    /// SelectTool → HUDController → PIM → SelectTool снова сомкнётся.
    /// Вызов до создания инструмента безопасен — проба применится в _Ready/Initialize.
    /// </summary>
    public void SetCellProbe(ICellProbeOverlay probe)
    {
        _cellProbe = probe;
        _defaultTool?.BindCellProbe(probe);
    }
    private bool _isMouseDown = false;
    // C-04: какая кнопка начала драг. Один _isMouseDown на ЛКМ и ПКМ давал инверсию:
    // OnRelease получал isLeftClick ОТПУЩЕННОЙ кнопки, поэтому отпускание ПКМ посреди
    // ЛКМ-драга разметки уходило в инструмент как «снятие» (Unmark/Remove инвертировали
    // уже нарисованный прямоугольник), а отпускание ЛКМ после ПКМ-драга — наоборот.
    private bool _dragButtonIsLeft = true;
    private Vector2I _startTile;

    public event Action OnToolReset;

    public override void _Ready()
    {
        Instance = this;
        _defaultTool = new SelectTool(Selection);
        _defaultTool.BindCellProbe(_cellProbe);
        _currentTool ??= _defaultTool;
    }

    public void Initialize(TileMapLayer wallLayer, SelectionBox selection, CameraController camera)
    {
        Instance = this;
        WallLayer = wallLayer;
        Selection = selection;
        Camera = camera;

        _defaultTool = new SelectTool(Selection);
        _defaultTool.BindCellProbe(_cellProbe);
        SetTool(_defaultTool);
    }

    public void SetTool(ITool tool)
    {
        // C-04: смена инструмента (и Esc → ResetToDefault) не должны оставлять «зависший»
        // драг от прошлого инструмента — иначе первый клик после возврата в игру
        // завершал бы старый драг от старой _startTile.
        _isMouseDown = false;
        _dragButtonIsLeft = true;
        // F69: момент «нажали инструмент» — видно на графике.
        var next = tool ?? _defaultTool;
        var toolName = next?.GetType().Name ?? "None";
        Game.Core.SimEvents.Mark("TOOL", toolName);
        Game.Core.SimEvents.Count("tool." + toolName);
        _currentTool?.Cancel();
        _currentTool = next;
    }

    public void ResetToDefault()
    {
        SetTool(_defaultTool);
        OnToolReset?.Invoke();
    }

    public bool IsDefaultTool => _currentTool == _defaultTool;

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_currentTool == null || WallLayer == null)
            return;

        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (key.Keycode == Key.Escape)
            {
                // UI-17: закрытие открытых окон UI / инспекторов перед сбросом инструмента
                if (HUDController.Instance != null && HUDController.Instance.TryCloseTopWindow())
                {
                    GetViewport().SetInputAsHandled();
                    return;
                }

                if (!IsDefaultTool)
                {
                    ResetToDefault();
                    GetViewport().SetInputAsHandled();
                    return;
                }
            }

            var code = key.Keycode != Key.None ? key.Keycode : key.PhysicalKeycode;

            // Хоткеи управления временем: Space (пауза/снятие с паузы), 1..5 (скорости)
            if (code == Key.Space && TimeManager.Instance != null)
            {
                TimeManager.Instance.TogglePause();
                GetViewport().SetInputAsHandled();
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
                GetViewport().SetInputAsHandled();
                return;
            }
        }

        // §118: позиция из события, а не из GetGlobalMousePosition(): текущее
        // положение мыши могло уже уехать (отстающие/синтетические клики летели мимо).
        Vector2 worldPos;
        if (@event is InputEventMouseMotion motionEv)
            worldPos = (Vector2)(GetViewport().GetCanvasTransform().AffineInverse() * motionEv.Position);
        else if (@event is InputEventMouseButton btnEv)
            worldPos = (Vector2)(GetViewport().GetCanvasTransform().AffineInverse() * btnEv.Position);
        else
            worldPos = GetGlobalMousePosition();

        Vector2I tilePos = WallLayer.LocalToMap(WallLayer.ToLocal(worldPos));

        if (@event is InputEventMouseMotion)
        {
            if (_isMouseDown)
                _currentTool.OnDrag(_startTile, tilePos, worldPos);
            else
                _currentTool.OnHover(tilePos, worldPos);
        }
        else if (@event is InputEventMouseButton mouseBtn)
        {
            if (mouseBtn.ButtonIndex == MouseButton.Middle)
                return;

            GD.Print($"[PIM] btn={mouseBtn.ButtonIndex} pressed={mouseBtn.Pressed} tile=({tilePos.X},{tilePos.Y}) world=({worldPos.X:F0},{worldPos.Y:F0}) tool={_currentTool?.GetType().Name ?? "null"}");

            bool isLeft = mouseBtn.ButtonIndex == MouseButton.Left;
            bool isRight = mouseBtn.ButtonIndex == MouseButton.Right;

            if (mouseBtn.Pressed && (isLeft || isRight))
            {
                // C-04: пока один драг активен, второе нажатие игнорируем — иначе
                // инструмент перезапускал состояние (новый _dragStart) и его завершала
                // не та кнопка, что начал.
                if (_isMouseDown)
                    return;
                _isMouseDown = true;
                _dragButtonIsLeft = isLeft;
                _startTile = tilePos;
                _currentTool.OnClick(tilePos, worldPos, isLeft);
                GetViewport().SetInputAsHandled();
            }
            else if (!mouseBtn.Pressed && _isMouseDown && (isLeft || isRight))
            {
                // C-04: драг завершает ТОЛЬКО кнопка-инициатор, и в OnRelease уходит
                // именно она (а не отпущенная сейчас) — иначе инструмент выполнял
                // обратную операцию по чужому прямоугольнику.
                if (isLeft != _dragButtonIsLeft)
                    return;
                _isMouseDown = false;
                _currentTool.OnRelease(_startTile, tilePos, worldPos, _dragButtonIsLeft);
                GetViewport().SetInputAsHandled();
            }
        }
    }

    public override void _Process(double delta)
    {
        // C-04 (второй путь отказа): отпускание кнопки может не дойти до _UnhandledInput
        // (alt-tab, клик перехватил HUD). Тогда _isMouseDown остаётся true, и первый клик
        // после возврата в игру воспринимался как завершение «зависшего» драга от старой
        // точки. Реальное состояние кнопки в Input — источник истины: если кнопка-инициатор
        // уже не нажата, драг завершаем сами (один OnRelease, как от настоящего события).
        if (!_isMouseDown || _currentTool == null)
            return;
        MouseButton button = _dragButtonIsLeft ? MouseButton.Left : MouseButton.Right;
        if (Input.IsMouseButtonPressed(button))
            return;

        _isMouseDown = false;
        Vector2 worldPos = GetGlobalMousePosition();
        Vector2I tilePos = WallLayer != null
            ? WallLayer.LocalToMap(WallLayer.ToLocal(worldPos))
            : _startTile;
        _currentTool.OnRelease(_startTile, tilePos, worldPos, _dragButtonIsLeft);
    }
}
