using System;
using System.Threading;

namespace Game.Simulation;

/// <summary>
/// Lock-free ThreadLocal RNG для параллельных фаз симуляции.
/// Random.Shared держит внутренний lock (contention на 16 ядрах),
/// ctx.Random вообще не thread-safe (data race в Parallel).
/// XorShift: ~2нс на NextDouble, состояние — 64 бита на поток, зерно —
/// смесь TickCount + ManagedThreadId (детерминизм Parallel и так условен).
/// </summary>
public static class ParallelRng
{
    [ThreadStatic]
    private static ulong _state;

    /// <summary>
    /// B-09: базовое зерно прогона. -1 = не задано (историческое поведение:
    /// зерно из TickCount, детерминизма нет и быть не может).
    /// Задаётся один раз на прогон/тест через <see cref="SetBaseSeed"/>, после чего
    /// параллельные фазы обязаны вызвать <see cref="Fork"/> со стабильным номером
    /// потока/диапазона — тогда последовательность значений воспроизводима.
    /// </summary>
    private static long _baseSeed = -1;

    /// <summary>true, если задано базовое зерно (значит Fork даёт воспроизводимость).</summary>
    public static bool Deterministic => Volatile.Read(ref _baseSeed) >= 0;

    /// <summary>
    /// B-09: зафиксировать зерно прогона. Сбрасывает ThreadStatic-состояние
    /// вызывающего потока, иначе между прогонами тянулся бы старый xorshift-хвост.
    /// </summary>
    public static void SetBaseSeed(ulong seed)
    {
        Interlocked.Exchange(ref _baseSeed, (long)(seed == 0 ? 0x9E3779B97F4A7C15UL : seed));
        _state = 0;
    }

    /// <summary>B-09: вернуть недетерминированный фолбэк (поведение по умолчанию).</summary>
    public static void ClearBaseSeed()
    {
        Interlocked.Exchange(ref _baseSeed, -1);
        _state = 0;
    }

    /// <summary>
    /// B-09: сбросить состояние потока. Нужно на границе фаз/прогонов: [ThreadStatic]
    /// живёт вместе с потоком и без сброса переносил хвост предыдущего прогона.
    /// </summary>
    public static void ResetThreadState() => _state = 0;

    /// <summary>
    /// B-09: создать детерминированный поток значений для конкретного воркера.
    /// <paramref name="streamId"/> — стабильный номер (индекс воркера или начало
    /// диапазона), а не ManagedThreadId: пул потоков переиспользует потоки между
    /// запусками, поэтому привязка к потоку ломала воспроизводимость.
    /// Без заданного базового зерна метод ничего не гарантирует (как и раньше).
    /// </summary>
    public static void Fork(int streamId)
    {
        long baseSeed = Volatile.Read(ref _baseSeed);
        if (baseSeed < 0) return; // недетерминированный режим — не трогаем состояние
        ulong s = Mix((ulong)baseSeed, (ulong)(uint)streamId);
        _state = s == 0 ? 0x9E3779B97F4A7C15UL : s;
    }

    private static ulong Mix(ulong a, ulong b)
    {
        ulong s = a ^ (b * 0x9E3779B97F4A7C15UL);
        s ^= s >> 30; s *= 0xBF58476D1CE4E5B9UL;
        s ^= s >> 27; s *= 0x94D049BB133111EBUL;
        s ^= s >> 31;
        return s;
    }

    private static ulong Seed()
    {
        long baseSeed = Volatile.Read(ref _baseSeed);
        if (baseSeed >= 0)
            return Mix((ulong)baseSeed, (ulong)(uint)Thread.CurrentThread.ManagedThreadId);

        ulong s = (ulong)(Environment.TickCount ^ (Thread.CurrentThread.ManagedThreadId * 7919));
        if (s == 0) s = 0x9E3779B97F4A7C15UL;
        // Прогрев: первые значения сырого LCG коррелируют между потоками.
        s ^= s >> 29; s *= 0xBF58476D1CE4E5B9UL;
        s ^= s >> 32;
        return s;
    }

    private static ulong Next()
    {
        ulong s = _state;
        if (s == 0)
        {
            s = Seed();
            _state = s;
        }
        // xorshift64*: один mul + три xor — без lock.
        s ^= s >> 12;
        s ^= s << 25;
        s ^= s >> 27;
        _state = s;
        return s * 0x2545F4914F6CDD1DUL;
    }

    /// <summary>[0, 1) без lock и без аллокаций.</summary>
    public static double NextDouble() => (Next() >> 11) * (1.0 / 9007199254740992.0);

    /// <summary>[minInclusive, maxExclusive) без lock. Равномерно через
    /// rejection sampling (#9: наивный Next()%range даёт modulo-смещение —
    /// для больших range старшие значения выпадают реже; здесь отсев
    /// переполняющего хвоста 2^64 mod range, распределение равномерное).</summary>
    public static int Next(int minInclusive, int maxExclusive)
    {
        if (minInclusive >= maxExclusive) return minInclusive;
        uint range = (uint)(maxExclusive - minInclusive);
        // Граница отсева: значения r >= limit отбрасываем (иначе modulo-смещение).
        ulong limit = (0xFFFFFFFFFFFFFFFFUL / range) * range;
        while (true)
        {
            ulong r = Next();
            if (r < limit)
                return minInclusive + (int)(r % range);
        }
    }
}
