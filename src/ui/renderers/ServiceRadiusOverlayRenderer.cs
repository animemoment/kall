using Godot;
using Game.Simulation;
using System;

namespace Game.UI;

/// <summary>
/// UI-16: Оверлей подсветки рабочего радиуса обслуживания зоны или здания (склад, ратуша).
/// Отображает окружность и полупрозрачную заливку зоны действия при выборе или размещении объекта.
/// </summary>
public partial class ServiceRadiusOverlayRenderer : Node2D
{
    public static ServiceRadiusOverlayRenderer Instance { get; private set; }

    private const float TileSize = MapRenderer.TileSizePx;
    private const float BorderScreenPx = 2.5f;

    private static readonly Color StockpileBorderColor = new(0.75f, 0.45f, 0.95f, 0.85f);
    private static readonly Color StockpileFillColor = new(0.65f, 0.25f, 0.9f, 0.08f);

    private bool _hasCustomRadius;
    private Vector2 _customCenter;
    private float _customRadius;
    private Color _customBorderColor;
    private Color _customFillColor;
    private string _customLabel;

    public override void _Ready()
    {
        Instance = this;
        ZIndex = 8;
        StockpileManager.Instance.OnSelectedTileChanged += OnStockpileSelectedChanged;
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
        StockpileManager.Instance.OnSelectedTileChanged -= OnStockpileSelectedChanged;
        base._ExitTree();
    }

    private void OnStockpileSelectedChanged((int X, int Y)? tile)
    {
        QueueRedraw();
    }

    public void SetCustomRadius(Vector2 centerPx, float radiusPx, Color color, string label = null)
    {
        _hasCustomRadius = true;
        _customCenter = centerPx;
        _customRadius = radiusPx;
        _customBorderColor = color;
        _customFillColor = new Color(color.R, color.G, color.B, 0.08f);
        _customLabel = label;
        QueueRedraw();
    }

    public void ClearCustomRadius()
    {
        if (_hasCustomRadius)
        {
            _hasCustomRadius = false;
            QueueRedraw();
        }
    }

    public void ClearAll()
    {
        _hasCustomRadius = false;
        StockpileManager.Instance.DeselectTile();
        QueueRedraw();
    }

    public override void _Draw()
    {
        float zoom = 1f;
        Transform2D canvasXf = GetCanvasTransform();
        if (Mathf.Abs(canvasXf.Scale.X) > 0.0001f)
            zoom = Mathf.Abs(canvasXf.Scale.X);
        float borderWidth = Mathf.Clamp(BorderScreenPx / zoom, 1.5f, 32f);

        // 1. Подсветка выбранного склада
        if (StockpileManager.Instance.SelectedTile.HasValue)
        {
            var tile = StockpileManager.Instance.SelectedTile.Value;
            Vector2 center = new Vector2((tile.X + 0.5f) * TileSize, (tile.Y + 0.5f) * TileSize);
            float radius = StockpileManager.DefaultServiceRadiusTiles * TileSize;
            DrawCircle(center, radius, StockpileFillColor);
            DrawArc(center, radius, 0f, Mathf.Tau, 96, StockpileBorderColor, borderWidth, true);
        }

        // 2. Пользовательский радиус (ратуша / размещение)
        if (_hasCustomRadius && _customRadius > 0f)
        {
            DrawCircle(_customCenter, _customRadius, _customFillColor);
            DrawArc(_customCenter, _customRadius, 0f, Mathf.Tau, 96, _customBorderColor, borderWidth, true);
        }
    }
}
