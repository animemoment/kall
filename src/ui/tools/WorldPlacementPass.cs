using System;
using Game.Core.World;

namespace Game.UI.Tools;

/// <summary>
/// Общий прогон выделения инструмента по БЕСКОНЕЧНОМУ МИРУ (PLAN §28): клетки вне острова
/// отдаются в <see cref="IToolWorldPlacement"/>, островные пропускаются — их обрабатывает
/// обычная (островная) логика инструмента со своими клампами.
/// Лимиты защищают от гигантского драга: применяем не больше <see cref="MaxAppliedCells"/>,
/// просматриваем не больше <see cref="MaxScannedCells"/> клеток за одно выделение.
/// </summary>
public static class WorldPlacementPass
{
    public const int MaxAppliedCells = 250_000;
    public const int MaxScannedCells = 250_000;

    /// <summary>Последнее выделение было обрезано лимитом просмотра (для HUD/диагностики).</summary>
    public static bool LastTruncated { get; private set; }

    /// <summary>
    /// Применить выделение (start → end включительно) к миру. Возвращает число успешных
    /// применений. Островные клетки (и всё, что не обслуживается миром) пропускаются.
    /// </summary>
    public static int Run(
        long startX,
        long startY,
        long endX,
        long endY,
        IToolWorldPlacement placement,
        Func<long, long, bool> apply,
        Func<long, long, bool> isIslandCell = null)
    {
        if (placement == null || apply == null)
            return 0;

        long minX = Math.Min(startX, endX);
        long maxX = Math.Max(startX, endX);
        long minY = Math.Min(startY, endY);
        long maxY = Math.Max(startY, endY);

        // Гигантский драг обрезается ЦЕНТРИРОВАННО: раньше прогон съедал лимит просмотра на
        // верхние строки и низ выделения молча не обрабатывался (а SelectionBox показывал всё).
        long width = maxX - minX + 1;
        long height = maxY - minY + 1;
        LastTruncated = false;
        if (width <= 0 || height <= 0)
            return 0;
        if (width > MaxScannedCells / height)
        {
            LastTruncated = true;
            // Сохраняем пропорции, не превращая длинное тонкое выделение в квадрат.
            long clippedWidth = Math.Min(width, MaxScannedCells);
            long clippedHeight = Math.Min(height, Math.Max(1, MaxScannedCells / clippedWidth));
            long centerX = minX + width / 2;
            long centerY = minY + height / 2;
            minX = Math.Max(minX, centerX - clippedWidth / 2);
            minY = Math.Max(minY, centerY - clippedHeight / 2);
            maxX = Math.Min(maxX, minX + clippedWidth - 1);
            maxY = Math.Min(maxY, minY + clippedHeight - 1);
        }

        int applied = 0;
        long scanned = 0;
        for (long y = minY; y <= maxY; y++)
        {
            for (long x = minX; x <= maxX; x++)
            {
                if (++scanned > MaxScannedCells)
                {
                    LastTruncated = true;
                    return applied;
                }
                // Границу острова спрашиваем у адаптера: он единственный знает размеры,
        // с которыми его создали (в игре это WorldModel.IslandTiles).
        Func<long, long, bool> islandBoundary = isIslandCell ?? placement.IsIslandCell;
        if (islandBoundary(x, y))
                    continue;
                if (applied >= MaxAppliedCells)
                    return applied;
                if (!placement.IsWorldCell(x, y))
                    continue;
                if (apply(x, y))
                    applied++;
            }
        }
        return applied;
    }

    /// <summary>
    /// Клетка острова колонии. ЕДИНСТВЕННЫЙ ответ проекта — <see cref="WorldModel.IsInsideIsland"/>
    /// (R-008: свои формулы границы запрещены, их ловит run_architecture_guards).
    /// </summary>
    public static bool IsOnIsland(long cellX, long cellY)
        => WorldModel.IsInsideIsland(cellX, cellY);

    /// <summary>
    /// Диагностика расхождения размеров (SeamInspector): «влезла бы клетка в остров
    /// указанного размера». НЕ рабочая формула границы — в игре размер только один,
    /// <see cref="WorldModel.IslandTiles"/>.
    /// </summary>
    public static bool IsOnIsland(long cellX, long cellY, long width, long height)
        => width == WorldModel.IslandTiles && height == WorldModel.IslandTiles
            ? WorldModel.IsInsideIsland(cellX, cellY)
            : width > 0 && height > 0 && cellX >= 0 && cellY >= 0 && cellX < width && cellY < height;

    /// <summary>Клетка острова с учётом адаптера инструмента (null → остров проекта).</summary>
    public static bool IsOnIsland(long cellX, long cellY, IToolWorldPlacement placement)
        => placement != null ? placement.IsIslandCell(cellX, cellY) : IsOnIsland(cellX, cellY);
}
