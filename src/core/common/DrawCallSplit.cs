using System;
using System.Collections.Concurrent;
using System.Threading;
using Game.Core;

namespace Game.Core;

/// <summary>
/// L-07: разбивка «одного числа» RenderTotalDrawCallsInFrame по группам.
///
/// Причина: PerformanceOverlay печатал ОДНО число draw calls на кадр. По нему
/// нельзя ни подтвердить «400», ни адресно уменьшить вклад — непонятно, кто
/// именно рисует (мир/агенты/тени/оверлей). Здесь — только УЧЁТ: подсистема
/// сообщает, сколько draw-батчей она отправила в кадре, счётчики суммируются
/// и отдаются наружу с долей от размеченного итога.
///
/// Контракт (важно, чтобы цифры не начали «врать», как это было с AvgMs):
///   • Report(group, n) — АБСОЛЮТНОЕ значение за текущий кадр (идемпотентно
///     в пределах кадра): повторный вызов перезаписывает, а не прибавляет;
///   • Reset() — вызывается рендер-потоком в начале кадра;
///   • Snapshot() — читает последний кадр и НЕ двигает состояние.
/// Неразмеченный остаток = Performance.Monitor.RenderTotalDrawCallsInFrame −
/// сумма по группам; показывается отдельной строкой, чтобы «немые» участки
/// были видны, а не прятались в общей цифре.
/// </summary>
public static class DrawCallSplit
{
    public struct SplitEntry
    {
        public string Group;
        /// <summary>Draw-батчей группы в последнем кадре.</summary>
        public int DrawCalls;
        /// <summary>Доля от СУММЫ размеченных групп, %.</summary>
        public double Percent;
    }

    private sealed class Entry
    {
        public int Value;
    }

    private static readonly ConcurrentDictionary<string, Entry> _groups = new(StringComparer.Ordinal);
    private static SplitEntry[] _cache = Array.Empty<SplitEntry>();
    private static int _reportedTotal;

    /// <summary>L-07: сообщить число draw-батчей группы за текущий кадр (перезапись).</summary>
    public static void Report(string group, int drawCalls)
    {
        if (string.IsNullOrEmpty(group) || drawCalls < 0)
            return;
        var e = _groups.GetOrAdd(group, static _ => new Entry());
        Interlocked.Exchange(ref e.Value, drawCalls);
    }

    /// <summary>L-07: начало кадра — держим окно в один кадр, чтобы разбивка не копилась.</summary>
    public static void Reset()
    {
        foreach (var kv in _groups)
            Interlocked.Exchange(ref kv.Value.Value, 0);
        Interlocked.Exchange(ref _reportedTotal, 0);
        _cache = Array.Empty<SplitEntry>();
    }

    /// <summary>
    /// L-07: снапшот разбивки по последнему кадру (убывание по draw calls).
    /// Возвращает только непустые группы.
    /// </summary>
    public static void Snapshot(out SplitEntry[] results)
    {
        var list = new SplitEntry[_groups.Count];
        int idx = 0;
        int total = 0;
        foreach (var kv in _groups)
        {
            int v = Volatile.Read(ref kv.Value.Value);
            if (v <= 0)
                continue;
            list[idx++] = new SplitEntry { Group = kv.Key, DrawCalls = v };
            total += v;
        }

        if (idx != list.Length)
            Array.Resize(ref list, idx);

        if (total > 0)
        {
            for (int i = 0; i < list.Length; i++)
                list[i].Percent = (list[i].DrawCalls / (double)total) * 100.0;
        }

        Array.Sort(list, static (a, b) => b.DrawCalls.CompareTo(a.DrawCalls));
        Interlocked.Exchange(ref _reportedTotal, total);
        _cache = list;
        results = _cache;
    }

    /// <summary>L-07: сумма размеченных групп в последнем снапшоте.</summary>
    public static int TotalReported => Volatile.Read(ref _reportedTotal);

    /// <summary>
    /// L-07: остаток «немых» draw calls (монитор движка минус размеченное).
    /// 0, если монитор меньше суммы (бывает на переходных кадрах) — не врём минусом.
    /// </summary>
    public static int Unaccounted(long engineTotalDrawCalls)
    {
        long rest = engineTotalDrawCalls - TotalReported;
        if (rest <= 0)
            return 0;
        return rest > int.MaxValue ? int.MaxValue : (int)rest;
    }
}