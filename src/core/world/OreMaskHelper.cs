using System;

namespace Game.Core;

/// <summary>
/// Нормализованная 8-битная маска соседей руды (автотайл 47 значений).
///
/// Биты: N=1, E=2, S=4, W=8 (стороны), NE=16, SE=32, SW=64, NW=128 (углы).
/// Угловой бит учитывается ТОЛЬКО если обе смежные стороны того же типа руды:
/// сосед по диагонали сам по себе ребра не образует, его перекрывают стороны.
/// Поэтому допустимых значений ровно 47 (см. <see cref="ValidMasks"/>), а не 256.
///
/// Арт: <c>ui/assets/textures/world/ore47/&lt;ore_id&gt;/&lt;state&gt;/t&lt;mask&gt;.png</c>,
/// где &lt;mask&gt; — десятичное значение нормализованной маски из этого класса.
/// Порядок <see cref="ValidMasks"/> — канон нумерации ячеек композитного атласа
/// рендера (индекс маски в массиве = номер тайла), поэтому файлы ищутся по
/// значению, а не по порядку.
/// </summary>
public static class OreMaskHelper
{
    public const byte N = 1;
    public const byte E = 2;
    public const byte S = 4;
    public const byte W = 8;
    public const byte NE = 16;
    public const byte SE = 32;
    public const byte SW = 64;
    public const byte NW = 128;

    /// <summary>Все 47 допустимых (нормализованных) значений маски, по возрастанию.</summary>
    public static readonly byte[] ValidMasks = BuildValidMasks();

    public const int ValidMaskCount = 47;

    // Обратная таблица: значение маски (0..255) → индекс в ValidMasks (-1 = нет).
    private static readonly sbyte[] IndexByMask = BuildIndex();

    /// <summary>
    /// Посчитать нормализованную маску для клетки (x,y) с типом руды <paramref name="oreType"/>.
    /// Вне карты сосед = «нет руды». Возвращает 0, если в клетке руды нет.
    /// </summary>
    public static byte Compute(byte[] oreType, int width, int height, int x, int y)
    {
        if (oreType == null || width <= 0 || height <= 0)
            return 0;
        if ((uint)x >= (uint)width || (uint)y >= (uint)height)
            return 0;

        byte type = oreType[y * width + x];
        if (type == OreConfig.None)
            return 0;

        bool n = NeighborIs(oreType, width, height, x, y - 1, type);
        bool e = NeighborIs(oreType, width, height, x + 1, y, type);
        bool s = NeighborIs(oreType, width, height, x, y + 1, type);
        bool w = NeighborIs(oreType, width, height, x - 1, y, type);

        byte mask = 0;
        if (n) mask |= N;
        if (e) mask |= E;
        if (s) mask |= S;
        if (w) mask |= W;
        // Угол — только при обеих смежных сторонах того же типа.
        if (n && e && NeighborIs(oreType, width, height, x + 1, y - 1, type)) mask |= NE;
        if (e && s && NeighborIs(oreType, width, height, x + 1, y + 1, type)) mask |= SE;
        if (s && w && NeighborIs(oreType, width, height, x - 1, y + 1, type)) mask |= SW;
        if (w && n && NeighborIs(oreType, width, height, x - 1, y - 1, type)) mask |= NW;
        return mask;
    }

    /// <summary>Индекс маски в <see cref="ValidMasks"/> (0..46) или -1 для недопустимого значения.</summary>
    public static int IndexOf(byte mask) => IndexByMask[mask];

    /// <summary>Значение маски валидно (нормализовано)?</summary>
    public static bool IsValid(byte mask) => IndexOf(mask) >= 0;

    private static bool NeighborIs(byte[] oreType, int width, int height, int x, int y, byte type)
    {
        if ((uint)x >= (uint)width || (uint)y >= (uint)height)
            return false;
        return oreType[y * width + x] == type;
    }

    private static byte[] BuildValidMasks()
    {
        var list = new System.Collections.Generic.List<byte>(ValidMaskCount);
        for (int sides = 0; sides < 16; sides++)
        {
            bool n = (sides & N) != 0;
            bool e = (sides & E) != 0;
            bool s = (sides & S) != 0;
            bool w = (sides & W) != 0;
            // Свободные углы — только между двумя зажжёнными сторонами.
            var freeCorners = new byte[4];
            int count = 0;
            if (n && e) freeCorners[count++] = NE;
            if (e && s) freeCorners[count++] = SE;
            if (s && w) freeCorners[count++] = SW;
            if (w && n) freeCorners[count++] = NW;

            int combos = 1 << count;
            for (int c = 0; c < combos; c++)
            {
                int mask = sides;
                for (int b = 0; b < count; b++)
                    if ((c & (1 << b)) != 0)
                        mask |= freeCorners[b];
                if (!list.Contains((byte)mask))
                    list.Add((byte)mask);
            }
        }
        list.Sort();
        if (list.Count != ValidMaskCount)
            throw new InvalidOperationException(
                $"OreMaskHelper: ожидалось {ValidMaskCount} масок, получено {list.Count}");
        return list.ToArray();
    }

    private static sbyte[] BuildIndex()
    {
        var table = new sbyte[256];
        for (int i = 0; i < table.Length; i++)
            table[i] = -1;
        for (int i = 0; i < ValidMasks.Length; i++)
            table[ValidMasks[i]] = (sbyte)i;
        return table;
    }
}