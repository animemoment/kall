using Godot;
using Game.Core;
using Game.Simulation;
using Game.UI;

namespace Game.UI.Tools;

public class SelectTool : ITool
{
	private readonly SelectionBox _selectionBox;
	private Vector2 _startMousePos;
	private bool _hasDragged;

	public SelectTool(SelectionBox selectionBox)
	{
		_selectionBox = selectionBox;
	}

	private ICellProbeOverlay _probe;

	/// <summary>
	/// Источник подсказок клетки (влажность/плодородие). Узкий интерфейс вместо
	/// HUDController.Instance — иначе цикл SelectTool → HUDController → PIM → SelectTool.
	/// </summary>
	public void BindCellProbe(ICellProbeOverlay probe) => _probe = probe;

	private int _lastHumidityX = int.MinValue;
	private int _lastHumidityY = int.MinValue;

	public void OnHover(Vector2I tilePos, Vector2 worldPos)
	{
		FarmZoneManager.Instance.SetHoveredTile(tilePos.X, tilePos.Y);
		// В режиме влажности — показывать % клетки под курсором.
		// Дёргаем HUD только при смене клетки: MouseMotion идёт каждый кадр,
		// а Get+Text при стоящем курсоре — зря жечь CPU.
		if (tilePos.X == _lastHumidityX && tilePos.Y == _lastHumidityY)
			return;
		_lastHumidityX = tilePos.X;
		_lastHumidityY = tilePos.Y;
		_probe?.ShowHumidityAt(tilePos.X, tilePos.Y);
		_probe?.ShowFertilityAt(tilePos.X, tilePos.Y);
	}

	// §113.5/B2: ПКМ в дефолтном инструменте ничего не делает — удаление
	// чертежей живёт только в окнах стройки (BuildTool/ZoneDraftTool и т.п.).

	public void OnClick(Vector2I tilePos, Vector2 worldPos, bool isLeftClick)
	{
		if (!isLeftClick)
			return;

		_startMousePos = worldPos;
		_hasDragged = false;
		_selectionBox?.ResetDefaultStyle();
		_selectionBox?.StartSelection(worldPos);
	}

	public void OnDrag(Vector2I startTile, Vector2I currentTile, Vector2 currentWorldPos)
	{
		if (_selectionBox != null && _selectionBox.IsSelecting)
		{
			if ((currentWorldPos - _startMousePos).LengthSquared() > 40.0f)
			{
				_hasDragged = true;
			}
			_selectionBox.UpdateSelection(currentWorldPos);
		}
	}

	public void OnRelease(Vector2I startTile, Vector2I endTile, Vector2 worldPos, bool isLeftClick)
	{
		// §113.5/B2: ПКМ-рамка дефолтного инструмента больше ничего не сносит
		// (раньше удаляла чертежи И построенное через WorldPlacement.TryRemove*).
		if (!isLeftClick)
			return;

		if (_selectionBox != null && _selectionBox.IsSelecting)
		{
			_selectionBox.EndSelection();
			_selectionBox.ResetDefaultStyle();
		}

		if (!_hasDragged)
		{
			// UI-18: инспектор жителя при клике на агента
			int agentIdx = Game.Simulation.AgentSimulationThread.Instance?.FindAgentNearWorld(worldPos.X, worldPos.Y, 54f)
						   ?? Game.Simulation.AgentSimulationThread.Instance?.FindAgentNearTile(endTile.X, endTile.Y, 1.2f)
						   ?? -1;
			if (agentIdx < 0)
				agentIdx = Game.Simulation.AgentSimulationThread.Instance?.FindAgentNearTile(endTile.X, endTile.Y, 1.5f) ?? -1;
			if (agentIdx >= 0)
			{
				ZoneManager.Instance.DeselectZone();
				StockpileManager.Instance.DeselectTile();
				ServiceRadiusOverlayRenderer.Instance?.ClearCustomRadius();
				VillagerInspectorController.Instance?.Open(agentIdx);
			}
			else
			{
				VillagerInspectorController.Instance?.Close();

				// UI-19: проверка клика на стройплощадку или ячейку склада
				if (ConstructionManager.Instance.TryGetSiteOrigin(endTile.X, endTile.Y, out int ox, out int oy) &&
					ConstructionManager.Instance.TryGetSite(ox, oy, out var site))
				{
					ZoneManager.Instance.DeselectZone();
					StockpileManager.Instance.DeselectTile();
					ServiceRadiusOverlayRenderer.Instance?.ClearCustomRadius();
					string name = site.Definition?.DisplayName ?? "Стройплощадка";
					string msg;
					if (site.Stage == ConstructionStage.Clearance)
						msg = $"{name}: расчистка площадки";
					else if (site.IsFullySupplied || site.Stage == ConstructionStage.Assembling)
						msg = $"{name}: сборка {(int)(site.WorkProgress * 100)}%";
					else
						msg = $"{name}: подвезено {site.TotalDelivered}/{site.TotalRequired}";

					HUDController.ShowFloatingWarning(msg, null, new Color(0.4f, 0.9f, 1f, 1f));
				}
				else if (StockpileManager.Instance.TryGetStockpileItem(endTile.X, endTile.Y, out ItemId item, out int count))
				{
					ZoneManager.Instance.DeselectZone();
					ServiceRadiusOverlayRenderer.Instance?.ClearCustomRadius();
					StockpileManager.Instance.SelectTile(endTile.X, endTile.Y);
					string msg;
					if (item != ItemId.None && count > 0)
					{
						string itemName = ItemRegistry.TryGet(item, out var def) && !string.IsNullOrEmpty(def.Name)
							? def.Name
							: item.ToString();
						msg = $"Склад: {itemName} x{count} (радиус 64 кл.)";
					}
					else
					{
						msg = "Склад: пусто (радиус 64 кл.)";
					}
					HUDController.ShowFloatingWarning(msg, null, new Color(0.5f, 1f, 0.5f, 1f));
				}
				else if (StructureManager.Instance.TryGetStructure(endTile.X, endTile.Y, out var str) && str.Definition?.Type == BuildingType.CentralBlock)
				{
					StockpileManager.Instance.DeselectTile();
					int radius = 64;
					if (ZoneManager.Instance.TryGetZoneAt(endTile.X, endTile.Y, out var zone))
					{
						radius = zone.RadiusTiles;
						ZoneManager.Instance.SelectZoneAt(endTile.X, endTile.Y);
					}
					Vector2 center = new Vector2((str.X + 0.5f) * MapRenderer.TileSizePx, (str.Y + 0.5f) * MapRenderer.TileSizePx);
					ServiceRadiusOverlayRenderer.Instance?.SetCustomRadius(center, radius * MapRenderer.TileSizePx, new Color(0.95f, 0.75f, 0.25f, 0.85f), "Ратуша");
					HUDController.ShowFloatingWarning($"Ратуша: радиус обслуживания {radius} кл.", null, new Color(1f, 0.85f, 0.3f, 1f));
				}
				else
				{
					StockpileManager.Instance.DeselectTile();
					ServiceRadiusOverlayRenderer.Instance?.ClearCustomRadius();
					// Единый ZoneManager: один выбор на все виды зон.
					// SelectZoneAt сам снимает выбор при промахе; контроллеры
					// открывают окно по OnZoneSelected (Garden — Farm, WorkZone — Work).
					ZoneManager.Instance.SelectZoneAt(endTile.X, endTile.Y);
				}
			}
		}
	}

	public void Cancel()
	{
		_selectionBox?.CancelSelection();
		_selectionBox?.ResetDefaultStyle();
		ZoneManager.Instance.SetHoveredTile(-1, -1);
		ZoneManager.Instance.DeselectZone();
		StockpileManager.Instance.DeselectTile();
		ServiceRadiusOverlayRenderer.Instance?.ClearAll();
		VillagerInspectorController.Instance?.Close();
	}
}
