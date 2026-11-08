using System;
using System.Collections.Generic;
using Godot;
using Game.Core;
using Game.Simulation;

namespace Game.UI.Tools;

/// <summary>
/// Инструмент первичного основания колонии: игрок выбирает сухое безопасное место
/// на локальной карте, где возводится ратуша/лагерь, выгружаются стартовые ресурсы
/// и высаживаются 100 поселенцев.
/// </summary>
public sealed class ColonyFoundingTool : ITool
{
    private readonly MapData _mapData;
    private readonly TileMapLayer _ghostLayer;
    private readonly Action<int, int> _onColonyFound;
    private readonly List<Vector2I> _previewCells = new(9);
    private readonly Godot.Collections.Array<Vector2I> _ghostArray = new();
    private Vector2I _lastHover = new(-1, -1);

    public ColonyFoundingTool(MapData mapData, TileMapLayer ghostLayer, Action<int, int> onColonyFound)
    {
        _mapData = mapData;
        _ghostLayer = ghostLayer;
        _onColonyFound = onColonyFound;
    }

    public void OnHover(Vector2I tilePos, Vector2 worldPos)
    {
        if (_ghostLayer == null || _mapData == null) return;
        if (_lastHover == tilePos) return;
        _lastHover = tilePos;
        ClearGhost();

        int x = tilePos.X;
        int y = tilePos.Y;
        bool valid = IsValidPlacementSpot(x, y);

        _previewCells.Clear();
        // 3x3 площадка ратуши и лагеря высадки
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                _previewCells.Add(new Vector2I(x + dx, y + dy));
            }
        }

        _ghostArray.Clear();
        foreach (var c in _previewCells)
            _ghostArray.Add(c);

        if (valid)
        {
            _ghostLayer.Modulate = new Color(0.2f, 1.0f, 0.4f, 0.75f);
            _ghostLayer.SetCellsTerrainConnect(_ghostArray, 0, 0, true);
        }
        else
        {
            _ghostLayer.Modulate = new Color(1.0f, 0.25f, 0.25f, 0.75f);
            _ghostLayer.SetCellsTerrainConnect(_ghostArray, 0, 0, true);
        }
    }

    public void OnClick(Vector2I tilePos, Vector2 worldPos, bool isLeftClick)
    {
        if (!isLeftClick) return;
        int x = tilePos.X;
        int y = tilePos.Y;
        if (!IsValidPlacementSpot(x, y))
        {
            HUDController.ShowFloatingWarning("Нельзя основать колонию здесь: выберите ровную сушу без воды и препятствий!");
            return;
        }

        ClearGhost();
        _onColonyFound?.Invoke(x, y);
    }

    public void OnDrag(Vector2I startTile, Vector2I currentTile, Vector2 currentWorldPos)
    {
        OnHover(currentTile, currentWorldPos);
    }

    public void OnRelease(Vector2I startTile, Vector2I endTile, Vector2 worldPos, bool isLeftClick)
    {
    }

    public void Cancel()
    {
        ClearGhost();
    }

    private void ClearGhost()
    {
        if (_ghostLayer != null && _previewCells.Count > 0)
        {
            foreach (var c in _previewCells)
                _ghostLayer.EraseCell(c);
            _previewCells.Clear();
            _ghostLayer.Modulate = new Color(1f, 1f, 1f, 0.4f);
        }
    }

    public bool IsValidPlacementSpot(int cx, int cy)
    {
        if (_mapData == null || _mapData.Ground == null) return false;
        if (cx < 4 || cy < 4 || cx >= _mapData.Width - 4 || cy >= _mapData.Height - 4) return false;

        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int x = cx + dx;
                int y = cy + dy;
                if (_mapData.Ground[x, y] != TileType.Grass) return false;
                if (_mapData.TreeOnGrass != null && _mapData.TreeOnGrass[x, y]) return false;
                if (_mapData.StoneOnGrass != null && _mapData.StoneOnGrass[x, y]) return false;
                if (_mapData.HasOre(x, y)) return false;
                if (StructureManager.Instance.HasStructureAt(x, y)) return false;
            }
        }
        return true;
    }
}
