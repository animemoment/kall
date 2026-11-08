using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Game.UI.Renderers.Culling;

/// <summary>
/// Быстрый 2D AABB прямоугольник для Frustum Culling без динамических аллокаций памяти.
/// </summary>
public readonly struct Rect2D
{
    public readonly float MinX;
    public readonly float MinY;
    public readonly float MaxX;
    public readonly float MaxY;

    public float Width => MaxX - MinX;
    public float Height => MaxY - MinY;

    public Rect2D(float minX, float minY, float maxX, float maxY)
    {
        MinX = minX;
        MinY = minY;
        MaxX = maxX;
        MaxY = maxY;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Rect2D FromCenterSize(Vector2 center, Vector2 size)
    {
        float hw = size.X * 0.5f;
        float hh = size.Y * 0.5f;
        return new Rect2D(center.X - hw, center.Y - hh, center.X + hw, center.Y + hh);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(Vector2 point) =>
        point.X >= MinX && point.X <= MaxX && point.Y >= MinY && point.Y <= MaxY;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(in Rect2D other) =>
        MinX <= other.MinX && MaxX >= other.MaxX && MinY <= other.MinY && MaxY >= other.MaxY;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Intersects(in Rect2D other) =>
        MinX <= other.MaxX && MaxX >= other.MinX && MinY <= other.MaxY && MaxY >= other.MinY;
}
