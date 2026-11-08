using System;
using System.Runtime.CompilerServices;

namespace Game.Core.WorldStreaming;

/// <summary>
/// Быстрый целочисленный генератор шумов и хэшей (детерминирован, Zero-GC, без float).
/// Все расчёты выполняются в фиксированной точке Q16 (0..65535).
/// </summary>
public static class PureFastNoise
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Hash2D(ulong seed, long x, long y, uint domain)
    {
        unchecked
        {
            ulong h = seed ^ (ulong)domain * 0x9E3779B97F4A7C15UL;
            h ^= (ulong)x * 0xBF58476D1CE4E5B9UL;
            h ^= (ulong)y * 0x94D049BB133111EBUL;
            h = (h ^ (h >> 30)) * 0xBF58476D1CE4E5B9UL;
            h = (h ^ (h >> 27)) * 0x94D049BB133111EBUL;
            return (uint)(h ^ (h >> 31));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort ValueNoise2DQ16(ulong seed, long x, long y, int scale, uint domain)
    {
        if (scale <= 1) scale = 1;
        long gx = (x >= 0) ? (x / scale) : ((x - scale + 1) / scale);
        long gy = (y >= 0) ? (y / scale) : ((y - scale + 1) / scale);

        int fx = (int)(x - gx * scale);
        int fy = (int)(y - gy * scale);

        uint n00 = Hash2D(seed, gx, gy, domain) & 0xFFFF;
        uint n10 = Hash2D(seed, gx + 1, gy, domain) & 0xFFFF;
        uint n01 = Hash2D(seed, gx, gy + 1, domain) & 0xFFFF;
        uint n11 = Hash2D(seed, gx + 1, gy + 1, domain) & 0xFFFF;

        uint u = SmoothStepQ16((uint)(fx * 65535L / scale));
        uint v = SmoothStepQ16((uint)(fy * 65535L / scale));

        uint top = LerpQ16(n00, n10, u);
        uint bot = LerpQ16(n01, n11, u);
        return (ushort)LerpQ16(top, bot, v);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint SmoothStepQ16(uint t)
    {
        const ulong One = 65535ul;
        const ulong OneSq = One * One;
        ulong x = Math.Min(t, (uint)One);
        ulong shaped = (x * x) * (3ul * One - 2ul * x) + OneSq / 2;
        return (uint)Math.Min(One, Math.BigMul(shaped, 4295101697UL, out _));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint LerpQ16(uint a, uint b, uint t)
    {
        int diff = (int)b - (int)a;
        return (uint)((int)a + (int)(((long)diff * (long)Math.Min(t, 65535u) + 32768) >> 16));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort Lerp16(ushort a, ushort b, uint t)
    {
        int diff = (int)b - (int)a;
        return (ushort)((int)a + (int)(((long)diff * (long)t + 32768) >> 16));
    }
}
