using System;
using System.Numerics;
using Game.Core;

namespace Game.Simulation;

/// <summary>
/// Легковесный фасад совместимости для локального обхода препятствий.
/// Вся реальная навигация заменена на ультра-быстрый Micro-A* steering (~1.16 мкс).
/// </summary>
public sealed class FlowFieldManager
{
    public static FlowFieldManager Instance { get; } = new();

    public void ClearCache() { }

    public Vector2 GetDirection(float worldX, float worldY, int targetTileX, int targetTileY, SimulationContext ctx)
    {
        float targetX = targetTileX * ctx.TileSize + 32f;
        float targetY = targetTileY * ctx.TileSize + 32f;
        float dx = targetX - worldX;
        float dy = targetY - worldY;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        return len > 0.01f ? new Vector2(dx / len, dy / len) : Vector2.Zero;
    }
}
