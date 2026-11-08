using Godot;
using Game.Core;
using Game.Simulation;
using System.Collections.Generic;

namespace Game.UI.Tools;

public class TreeFellingTool : ITool
{
    private readonly SelectionBox _selectionBox;
    private readonly MapData _mapData;
    private bool _isDragging = false;
    private bool _isLeftClick = true;

    private static readonly Color BlackZoneFill = new Color(0f, 0f, 0f, 0.4f);
    private static readonly Color BlackZoneBorder = new Color(0.1f, 0.1f, 0.1f, 0.85f);

    private readonly List<(int X, int Y)> _cellBuffer = new(2048);

    public TreeFellingTool(SelectionBox selectionBox, MapData mapData)
    {
        _selectionBox = selectionBox;
        _mapData = mapData;
    }

    public void OnHover(Vector2I tilePos, Vector2 worldPos) { }

    public void OnClick(Vector2I tilePos, Vector2 worldPos, bool isLeftClick)
    {
        _isDragging = true;
        _isLeftClick = isLeftClick;

        if (_selectionBox != null)
        {
            _selectionBox.SetStyle(BlackZoneFill, BlackZoneBorder);
            _selectionBox.StartSelection(worldPos);
        }
    }

    public void OnDrag(Vector2I startTile, Vector2I currentTile, Vector2 currentWorldPos)
    {
        if (_isDragging && _selectionBox != null)
        {
            _selectionBox.UpdateSelection(currentWorldPos);
        }
    }

    public IToolWorldPlacement WorldPlacement { get; set; }

    public void OnRelease(Vector2I startTile, Vector2I endTile, Vector2 worldPos, bool isLeftClick)
    {
        if (!_isDragging) return;
        _isDragging = false;

        GD.Print($"[TREE-TOOL] release start=({startTile.X},{startTile.Y}) end=({endTile.X},{endTile.Y}) left={isLeftClick} worldPlacement={WorldPlacement != null} mapData={_mapData?.Width ?? -1}x{_mapData?.Height ?? -1}");

        _selectionBox?.EndSelection();
        _selectionBox?.ResetDefaultStyle();

        int minX = Mathf.Min(startTile.X, endTile.X);
        int maxX = Mathf.Max(startTile.X, endTile.X);
        int minY = Mathf.Min(startTile.Y, endTile.Y);
        int maxY = Mathf.Max(startTile.Y, endTile.Y);

        long cellCount = (long)(maxX - minX + 1) * (maxY - minY + 1);
        if (cellCount > 250_000)
        {
            HUDController.ShowFloatingWarning("Выделенная область слишком велика");
            return;
        }

        _cellBuffer.Clear();

        if (isLeftClick)
        {
            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    if (_mapData != null && (uint)x < (uint)_mapData.Width && (uint)y < (uint)_mapData.Height && _mapData.TreeOnGrass[x, y])
                    {
                        _cellBuffer.Add((x, y));
                    }
                    else if (WorldPlacement != null && !WorldPlacementPass.IsIslandCell(x, y))
                    {
                        WorldPlacement.TryCutTree(x, y);
                    }
                }
            }

            if (_cellBuffer.Count > 0)
            {
                TreeJobManager.Instance.MarkTreesBatch(_cellBuffer);
            }
            else
            {
                // UI-15: оповещение при попытке рубки там, где нет деревьев или на препятствиях
                string warning = "Нельзя рубить: здесь нет деревьев";
                if (_mapData != null && (uint)startTile.X < (uint)_mapData.Width && (uint)startTile.Y < (uint)_mapData.Height)
                {
                    TileType ground = _mapData.Ground[startTile.X, startTile.Y];
                    if (ground == TileType.Water)
                        warning = "Нельзя рубить: вода";
                    else if (ground == TileType.Mountain)
                        warning = "Нельзя рубить: скала / горы";
                    else if (_mapData.HasStone(startTile.X, startTile.Y))
                        warning = "Нельзя рубить: препятствие (валун / камень)";
                    else if (_mapData.HasOre(startTile.X, startTile.Y))
                        warning = "Нельзя рубить: месторождение руды";
                }
                HUDController.ShowFloatingWarning(warning);
            }
        }
        else
        {
            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    if (WorldPlacementPass.IsIslandCell(x, y))
                    {
                        _cellBuffer.Add((x, y));
                    }
                    else if (WorldPlacement != null)
                    {
                        WorldPlacement.TryCancelCutTree(x, y);
                    }
                }
            }

            if (_cellBuffer.Count > 0)
            {
                TreeJobManager.Instance.UnmarkTreesBatch(_cellBuffer);
            }
        }
    }

    public void Cancel()
    {
        _isDragging = false;
        _selectionBox?.CancelSelection();
        _selectionBox?.ResetDefaultStyle();
    }
}
