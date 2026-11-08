using Godot;
using Game.Simulation;

namespace Game.UI;

/// <summary>
/// R-01 (P0, симптом R2): подсветка клетки склада под курсором.
///
/// Точка обрыва по реестру: у <see cref="StockpileManager"/> нет ни состояния
/// наведения, ни события (только SelectedTile), а <c>_stockpileLayer</c> в
/// MapRenderer создан с константным Modulate и нигде не меняется — поэтому зона
/// склада не подсвечивалась вообще.
///
/// Решение в границах renderers/: подсветку рисует собственный лёгкий Node2D.
/// Источник истины — публичный <see cref="StockpileManager.IsZoneTile(int,int)"/>
/// (read-only чтение битмаски, O(1)), поэтому НЕ требуется ни новое событие в
/// StockpileManager (FEAT-R05), ни правка SelectTool.OnHover. Канал чисто
/// визуальный: 1 draw call на клетку под курсором, ничего не пишет в менеджеры.
///
/// ZIndex 7 — выше StockpileLayer (0) и StockpileItemRenderer (6), ниже
/// FarmZoneRenderer (8): приоритет у зон, склад — фон.
/// </summary>
public partial class StockpileHoverRenderer : Node2D
{
	private const float TileSize = MapRenderer.TileSizePx;

	/// <summary>Троттлинг опроса мыши: IsZoneTile дешёв, но QueueRedraw каждый кадр не нужен.</summary>
	private const float HoverThrottleSec = 0.05f;

	// Тот же визуальный язык, что у FarmZoneRenderer (заливка + толстая рамка),
	// но в фиолетовой гамме склада (_stockpileLayer.Modulate = 0.65,0.15,0.9,0.45).
	private static readonly Color HoverFillColor = new(0.65f, 0.15f, 0.9f, 0.30f);
	private static readonly Color HoverBorderColor = new(0.85f, 0.55f, 1f, 0.95f);
	private const float HoverBorderThickness = 3.2f;

	private float _timer;
	private int _hoverX = int.MinValue;
	private int _hoverY = int.MinValue;

	public override void _Ready()
	{
		ZIndex = 7;
	}

	public override void _Process(double delta)
	{
		_timer += (float)delta;
		if (_timer < HoverThrottleSec)
			return;
		_timer = 0f;

		if (!TryGetHoveredTile(out int x, out int y) || !StockpileManager.Instance.IsZoneTile(x, y))
			SetHovered(int.MinValue, int.MinValue);
		else
			SetHovered(x, y);
	}

	private bool TryGetHoveredTile(out int x, out int y)
	{
		x = 0;
		y = 0;
		var vp = GetViewport();
		// Курсор над панелью HUD/меню — склад под ней не подсвечиваем (иначе мигает сквозь UI).
		if (vp == null || vp.GuiGetHoveredControl() != null)
			return false;

		Vector2 mouse = GetGlobalMousePosition();
		x = Mathf.FloorToInt(mouse.X / TileSize);
		y = Mathf.FloorToInt(mouse.Y / TileSize);
		return true;
	}

	private void SetHovered(int x, int y)
	{
		if (x == _hoverX && y == _hoverY)
			return;
		_hoverX = x;
		_hoverY = y;
		QueueRedraw();
	}

	public override void _Draw()
	{
		if (_hoverX == int.MinValue)
			return;

		var rect = new Rect2(_hoverX * TileSize, _hoverY * TileSize, TileSize, TileSize);
		DrawRect(rect, HoverFillColor);
		DrawRect(rect, HoverBorderColor, false, HoverBorderThickness);
	}
}