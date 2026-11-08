using System;
using System.Collections.Generic;

namespace Game.Simulation;

/// <summary>
/// Занятость клеток габаритами построек (task-22, аудит C-3). Один станок 3×4
/// занимает 12 клеток: ставить поверх них другой объект/зону нельзя, а снос
/// освобождает ВСЕ клетки.
///
/// Реестр общий для стройплощадок (ConstructionManager) и готовых структур
/// (StructureManager) — это позволяет переходить «сайт → готовое» без окна, когда
/// клетки на миг свободны. Ключ клетки → origin (левая-верхняя клетка постройки);
/// размер по origin запоминается, чтобы ReleaseOrigin снимал ровно свой габарит.
///
/// Потокобезопасно (lock); проверка прямоугольника — до 12 итераций, без аллокаций.
/// </summary>
public static class FootprintRegistry
{
    private static readonly object _lock = new();
    private static readonly Dictionary<(int X, int Y), (int X, int Y)> _cellToOrigin = new(4096);
    private static readonly Dictionary<(int X, int Y), (int W, int H)> _sizeByOrigin = new(1024);

    /// <summary>Занята ли клетка ЛЮБЫМ габаритом (сайт или готовое).</summary>
    public static bool IsOccupied(int x, int y)
    {
        lock (_lock)
            return _cellToOrigin.ContainsKey((x, y));
    }

    /// <summary>Origin постройки, покрывающей клетку.</summary>
    public static bool TryGetOrigin(int x, int y, out (int X, int Y) origin)
    {
        lock (_lock)
            return _cellToOrigin.TryGetValue((x, y), out origin);
    }

    /// <summary>
    /// Занять прямоугольник W×H. Атомарно: если хоть одна клетка уже занята —
    /// false и ничего не меняется (постройка не «проглотит» половину).
    /// </summary>
    public static bool TryOccupy(int originX, int originY, int width, int height)
    {
        int w = Math.Max(1, width);
        int h = Math.Max(1, height);
        lock (_lock)
        {
            for (int y = originY; y < originY + h; y++)
                for (int x = originX; x < originX + w; x++)
                    if (_cellToOrigin.ContainsKey((x, y)))
                        return false;

            for (int y = originY; y < originY + h; y++)
                for (int x = originX; x < originX + w; x++)
                    _cellToOrigin[(x, y)] = (originX, originY);
            _sizeByOrigin[(originX, originY)] = (w, h);
            return true;
        }
    }

    /// <summary>Освободить все клетки постройки с известным origin.</summary>
    public static void ReleaseOrigin(int originX, int originY)
    {
        lock (_lock)
        {
            if (!_sizeByOrigin.Remove((originX, originY), out var size))
                return;
            var origin = (originX, originY);
            for (int y = originY; y < originY + size.H; y++)
                for (int x = originX; x < originX + size.W; x++)
                {
                    if (_cellToOrigin.TryGetValue((x, y), out var cellOrigin) && cellOrigin == origin)
                        _cellToOrigin.Remove((x, y));
                }
        }
    }

    /// <summary>
    /// Освободить постройку, зная любую её клетку (origin резолвится по реестру).
    /// Возвращает origin, если клетка была занята.
    /// </summary>
    public static bool ReleaseAnyCell(int x, int y)
    {
        (int X, int Y) origin;
        lock (_lock)
        {
            if (!_cellToOrigin.TryGetValue((x, y), out origin))
                return false;
        }
        ReleaseOrigin(origin.X, origin.Y);
        return true;
    }

    /// <summary>Размер занятой постройки по origin (0,0 — не занят).</summary>
    public static (int W, int H) SizeOf(int originX, int originY)
    {
        lock (_lock)
            return _sizeByOrigin.TryGetValue((originX, originY), out var size) ? size : (0, 0);
    }

    /// <summary>Полная очистка (смена региона/карты).</summary>
    public static void Clear()
    {
        lock (_lock)
        {
            _cellToOrigin.Clear();
            _sizeByOrigin.Clear();
        }
    }
}