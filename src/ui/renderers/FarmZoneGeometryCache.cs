using Godot;
using Game.Simulation;
using System.Collections.Generic;

namespace Game.UI;

/// <summary>
/// R-06: готовая геометрия одной зоны — заливка прямоугольниками (склейка прогонов)
/// и контур склеенными отрезками. Строится один раз на набор клеток, дальше
/// FarmZoneRenderer._Draw только читает списки (никакого обхода клеток в кадре).
/// </summary>
public sealed class ZoneGeometry
{
    public readonly List<Rect2> FillRects;
    public readonly List<(Vector2 From, Vector2 To)> BorderSegments;

    public ZoneGeometry(List<Rect2> fillRects, List<(Vector2 From, Vector2 To)> borderSegments)
    {
        FillRects = fillRects;
        BorderSegments = borderSegments;
    }
}

/// <summary>
/// R-06: кэш геометрии зон по id. ZoneManager отдаёт зоны снапшотом, а набор клеток
/// меняет copy-on-write — поэтому валидность проверяется по ССЫЛКЕ на HashSet и по
/// числу клеток: новый набор или правка на месте = пересборка, иначе отдаём готовое.
/// </summary>
public sealed class FarmZoneGeometryCache
{
    private sealed class Entry
    {
        public HashSet<(int X, int Y)> Tiles;
        public int Count;
        public ZoneGeometry Geometry;
    }

    private readonly Dictionary<int, Entry> _entries = new();

    /// <summary>Сколько зон держат готовую геометрию (наблюдаемость/тесты).</summary>
    public int Count => _entries.Count;

    public ZoneGeometry Get(int zoneId, HashSet<(int X, int Y)> tiles)
    {
        int count = tiles?.Count ?? 0;
        if (_entries.TryGetValue(zoneId, out var entry)
            && ReferenceEquals(entry.Tiles, tiles)
            && entry.Count == count)
            return entry.Geometry;

        var geometry = FarmZoneRenderer.BuildZoneGeometry(tiles);
        _entries[zoneId] = new Entry { Tiles = tiles, Count = count, Geometry = geometry };
        return geometry;
    }

    /// <summary>Выбросить геометрию снесённых зон (иначе кэш течёт при пересоздании зон).</summary>
    public void Prune(List<Zone> liveZones)
    {
        if (_entries.Count == 0) return;

        if (liveZones == null || liveZones.Count == 0)
        {
            _entries.Clear();
            return;
        }

        var live = new HashSet<int>();
        for (int i = 0; i < liveZones.Count; i++)
            live.Add(liveZones[i].Id);

        if (live.Count == _entries.Count) return;

        var dead = new List<int>();
        foreach (var pair in _entries)
            if (!live.Contains(pair.Key))
                dead.Add(pair.Key);

        for (int i = 0; i < dead.Count; i++)
            _entries.Remove(dead[i]);
    }
}