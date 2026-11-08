using Godot;
using Game.Core;
using Game.Core.World;
using Game.Simulation;
using System.Collections.Generic;

namespace Game.UI.Tools;

/// <summary>
/// Инструмент добычи руды (task-4, близнец <see cref="StoneMiningTool"/>).
/// ЛКМ — рамкой отметить клетки с жилой (MapData.OreType != 0) под добычу,
/// ПКМ — снять метки в рамке. Метки/работы ведёт <see cref="OreJobManager"/>.
///
/// Руда — ХРАНИЛИЩЕ: одна метка живёт, пока жила не выработана; агент снимает
/// порции по циклу. Инструмент только размечает — добычу делает
/// OreMiningJobHandler. Мир за кромкой острова руду пока не поддерживает
/// (геология — отдельный док), поэтому WorldPlacement здесь не используется.
/// </summary>
public class OreMiningTool : ITool
{
    private readonly SelectionBox _selectionBox;
    private readonly MapData _mapData;
    private bool _isDragging;

    // Рудная разметка — охристо-коричневая (не путать с чёрной рубкой и
    // серо-белым камнем).
    private static readonly Color OreZoneFill = new Color(0.85f, 0.55f, 0.15f, 0.40f);
    private static readonly Color OreZoneBorder = new Color(1.0f, 0.75f, 0.20f, 0.90f);

    private const long MaxSelectionCells = 250_000;

    private readonly List<(int X, int Y)> _cellBuffer = new(2048);
    private readonly List<(int X, int Y)> _worldBuffer = new(512);

    public OreMiningTool(SelectionBox selectionBox, MapData mapData)
    {
        _selectionBox = selectionBox;
        _mapData = mapData;
    }

    /// <summary>Мир за кромкой (контракт интерфейса StoneMiningTool); руде не нужен.</summary>
    public IToolWorldPlacement WorldPlacement { get; set; }

    public void OnHover(Vector2I tilePos, Vector2 worldPos) { }

    public void OnClick(Vector2I tilePos, Vector2 worldPos, bool isLeftClick)
    {
        _isDragging = true;
        if (_selectionBox != null)
        {
            _selectionBox.SetStyle(OreZoneFill, OreZoneBorder);
            _selectionBox.StartSelection(worldPos);
        }
    }

    public void OnDrag(Vector2I startTile, Vector2I currentTile, Vector2 currentWorldPos)
    {
        if (_isDragging && _selectionBox != null)
            _selectionBox.UpdateSelection(currentWorldPos);
    }

    public void OnRelease(Vector2I startTile, Vector2I endTile, Vector2 worldPos, bool isLeftClick)
    {
        if (!_isDragging)
            return;
        _isDragging = false;

        _selectionBox?.EndSelection();
        _selectionBox?.ResetDefaultStyle();

        int minX = Mathf.Min(startTile.X, endTile.X);
        int maxX = Mathf.Max(startTile.X, endTile.X);
        int minY = Mathf.Min(startTile.Y, endTile.Y);
        int maxY = Mathf.Max(startTile.Y, endTile.Y);

        long cellCount = (long)(maxX - minX + 1) * (maxY - minY + 1);
        if (cellCount > MaxSelectionCells)
        {
            HUDController.ShowFloatingWarning("Выделенная область слишком велика");
            return;
        }

        _cellBuffer.Clear();

        if (isLeftClick)
        {
            // task-8: остров — по массивам MapData, мир за кромкой — по
            // детерминированному полю OreWorldField (координаты — локальный холст).
            ulong seed = WorldModel.Seed;
            uint ver = WorldModel.Version;
            _worldBuffer.Clear();
            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    if (_mapData != null && _mapData.HasOre(x, y))
                    {
                        _cellBuffer.Add((x, y));
                    }
                    else if (seed != 0 && !WorldModel.IsInsideIsland(x, y) &&
                             OreWorldField.GetEffectiveTypeLocal(x, y, seed, ver) != OreConfig.None)
                    {
                        _worldBuffer.Add((x, y));
                    }
                }
            }

            if (_cellBuffer.Count > 0)
                OreJobManager.Instance.MarkOreBatch(_cellBuffer);
            for (int i = 0; i < _worldBuffer.Count; i++)
                OreJobManager.Instance.MarkOreWorld(_worldBuffer[i].X, _worldBuffer[i].Y);
        }
        else
        {
            _worldBuffer.Clear();
            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    if (WorldModel.IsInsideIsland(x, y))
                        _cellBuffer.Add((x, y));
                    else
                        _worldBuffer.Add((x, y));
                }
            }

            if (_cellBuffer.Count > 0)
                OreJobManager.Instance.UnmarkOreBatch(_cellBuffer);
            for (int i = 0; i < _worldBuffer.Count; i++)
                OreJobManager.Instance.UnmarkOreWorld(_worldBuffer[i].X, _worldBuffer[i].Y);
        }
    }

    public void Cancel()
    {
        _isDragging = false;
        _selectionBox?.CancelSelection();
        _selectionBox?.ResetDefaultStyle();
    }
}