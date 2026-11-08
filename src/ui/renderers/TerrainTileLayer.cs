using Godot;
using System;
using System.Collections.Generic;

namespace Game.UI;

/// <summary>
/// Стандарт отрисовки тайловых слоёв: движковый автотайлинг через terrain.
/// Вместо ручного подбора атлас-координат в C# (SetCell с вычисленным atlas —
/// так ломались стены: код рассинхронизировался с разметкой) слой копит грязные
/// клетки, а во Flush одним вызовом SetCellsTerrainConnect отдаёт движку ТОЛЬКО
/// клетки, где тайл реально есть. Соседей для контекста движок подтягивает сам;
/// заливать зоной 3×3 нельзя — одна клетка расползалась в квадрат 3×3.
/// TileSet обязан содержать terrain-разметку (из сцены, как wood_wall_tile).
/// Главный поток: MarkDirty — откуда угодно, Flush — только из _Process.
/// </summary>
public sealed class TerrainTileLayer
{
    private readonly TileMapLayer _layer;
    private readonly int _terrainSetId;
    private readonly int _terrainId;
    private readonly HashSet<Vector2I> _dirty = new();

    /// <summary>
    /// C-09: бюджет клеток, пересчитываемых за один кадр. Массовая разметка (500×500
    /// фермы, рельеф после смены региона) ставила в очередь десятки тысяч клеток, и
    /// Flush обрабатывал их все за кадр одним SetCellsTerrainConnect — заметный спайк.
    /// Остаток остаётся в _dirty и доедет следующими кадрами.
    /// </summary>
    private const int MaxCellsPerFlush = 8192;

    /// <summary>C-09: переиспользуемый массив для SetCellsTerrainConnect (был new на каждый Flush).</summary>
    private readonly Godot.Collections.Array<Vector2I> _flushArray = new();

    /// <summary>C-09: буфер пачки, когда грязных клеток больше бюджета.</summary>
    private readonly List<Vector2I> _flushBatch = new(MaxCellsPerFlush);

    public TerrainTileLayer(TileMapLayer layer, int terrainSetId = 0, int terrainId = 0)
    {
        _layer = layer;
        _terrainSetId = terrainSetId;
        _terrainId = terrainId;
    }

    public int DirtyCount => _dirty.Count;

    public void MarkDirty(Vector2I cell) => _dirty.Add(cell);

    public void MarkDirty(IEnumerable<Vector2I> cells)
    {
        foreach (var c in cells)
            _dirty.Add(c);
    }

    /// <summary>
    /// Пересчитать грязные клетки движком. exists(cell) — есть ли тайл сейчас
    /// (нет — EraseCell, иначе — в набор для SetCellsTerrainConnect).
    /// ВАЖНО: соседние пустые клетки передавать в connect НЕЛЬЗЯ — движок в режиме
    /// update (последний arg=true) стирает тайлы, которых нет в наборе, даже если
    /// они помечены грязными. Поэтому стираем по exists, а в набор кладём только
    /// существующие. Контекст соседей движок подтягивает сам из слоя.
    /// </summary>
    public void Flush(Func<Vector2I, bool> exists)
    {
        if (_dirty.Count == 0) return;
        if (_layer?.TileSet == null) { _dirty.Clear(); return; }

        // C-09: один проход вместо двух и один вызов exists() на клетку.
        // Раньше было два прохода (стереть отсутствующие, затем собрать существующие),
        // то есть exists(c) вызывался ДВАЖДЫ на каждую грязную клетку, плюс каждый
        // Flush аллоцировал новый Godot-массив.
        _flushArray.Clear();

        if (_dirty.Count <= MaxCellsPerFlush)
        {
            foreach (var c in _dirty)
            {
                if (exists(c))
                    _flushArray.Add(c);
                else
                    _layer.EraseCell(c);
            }
            _dirty.Clear();
        }
        else
        {
            // C-09: пачка по бюджету; остаток остаётся грязным до следующего кадра.
            _flushBatch.Clear();
            foreach (var c in _dirty)
            {
                _flushBatch.Add(c);
                if (_flushBatch.Count >= MaxCellsPerFlush) break;
            }
            for (int i = 0; i < _flushBatch.Count; i++)
            {
                var c = _flushBatch[i];
                _dirty.Remove(c);
                if (exists(c))
                    _flushArray.Add(c);
                else
                    _layer.EraseCell(c);
            }
        }

        if (_flushArray.Count > 0)
            _layer.SetCellsTerrainConnect(_flushArray, _terrainSetId, _terrainId, true);
    }

    /// <summary>Полная перестройка слоя: стереть всё и залить terrain'ом набор клеток.</summary>
    public void RebuildAll(IEnumerable<Vector2I> cells)
    {
        if (_layer?.TileSet == null) return;
        _layer.Clear();
        _dirty.Clear();
        var arr = new Godot.Collections.Array<Vector2I>();
        foreach (var c in cells)
        {
            arr.Add(c);
            _dirty.Add(c);
        }
        if (arr.Count > 0)
            _layer.SetCellsTerrainConnect(arr, _terrainSetId, _terrainId, true);
        _dirty.Clear();
    }

    public void Clear()
    {
        _dirty.Clear();
        _layer?.Clear();
    }
}
