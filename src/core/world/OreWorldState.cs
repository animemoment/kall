using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Game.Core;

/// <summary>
/// Мутируемое состояние руды МИРА и острова (task-8): остаток запаса в клетке после
/// добычи. Базовое (немутированное) поле — детерминированное <see cref="OreWorldField"/>;
/// здесь хранятся только отличия, поэтому экономно (тысячи клеток, не 5.5e11).
///
/// Ключ — ЛОКАЛЬНЫЙ ХОЛСТ (int, как задания/менеджеры; абсолют = WorldOrigin+local).
/// Значение — остаток запаса; 0 = клетка выработана (тайл руды убирается).
/// Отсутствие записи = «руда нетронута» (рендер/гейты берут базовое поле).
///
/// Потокобезопасность: ConcurrentDictionary; пишут и sim-потоки (добыча), и главный
/// (загрузка дельт из чанк-стора). Событие <see cref="CellChanged"/> вызывается в
/// потоке писателя — подписчики с Godot обязаны маршалить через CallDeferred.
///
/// Персистентность: дельты кодируются в чанк-сторе как записи
/// <see cref="Game.Core.WorldStreaming.Integration.WorldDeltaKind.OreMined"/> с
/// payload [ver=1, reserveLo, reserveHi]; см. StreamingWorldView.
/// </summary>
public static class OreWorldState
{
    /// <summary>Версия схемы payload'а записи чанк-стора.</summary>
    public const byte PayloadVersion = 1;

    private static readonly ConcurrentDictionary<long, ushort> Reserve = new();

    // ------------------------------------------------------------------
    // AUDIT task-17 (blocker): мутации руды адресуются ЛОКАЛЬНЫМ холстом
    // (int, key = (x,y)), а смысл локальных координат задаёт мир — (seed,
    // WorldOrigin). Раньше единственная очистка жила в
    // StreamingWorldView.ResetWorld, который при смене региона/сида
    // вызывается ПОЗЖЕ генерации новой карты (Main.OnRegionSelected:
    // Fill → StartAsync/ApplyMap → ApplyWorldOreStateToIsland, и только потом
    // ResetWorld). Следствие: выработанные в прошлой колонии клетки
    // «пробивали дыры» в руде новой карты по тем же локальным координатам, а
    // при первом заходе после перезапуска процесса — и вовсе маскировали
    // свежие жилы. Теперь состояние само понимает, что мир сменился, и
    // сбрасывается ДО первого чтения/записи в новом мире.
    // ------------------------------------------------------------------
    private static readonly object WorldGate = new();
    private static ulong _boundSeed;
    private static long _boundOriginX = long.MinValue;
    private static long _boundOriginY = long.MinValue;

    private static void EnsureWorldBound()
    {
        ulong seed = Game.Core.World.WorldModel.Seed;
        long originX = Game.Core.WorldStreaming.UnifiedWorldGenerator.WorldOriginX;
        long originY = Game.Core.WorldStreaming.UnifiedWorldGenerator.WorldOriginY;
        if (_boundSeed == seed && _boundOriginX == originX && _boundOriginY == originY)
            return;
        lock (WorldGate)
        {
            if (_boundSeed == seed && _boundOriginX == originX && _boundOriginY == originY)
                return;
            Reserve.Clear();
            _boundSeed = seed;
            _boundOriginX = originX;
            _boundOriginY = originY;
        }
    }

    /// <summary>Сколько клеток имеют сохранённый остаток (диагностика).</summary>
    public static int Count
    {
        get
        {
            EnsureWorldBound();
            return Reserve.Count;
        }
    }

    /// <summary>Изменилась клетка (запас/выработка). Поток писателя!</summary>
    public static event Action<int, int> CellChanged;

    /// <summary>Есть ли сохранённый остаток для клетки (false = руда нетронута).</summary>
    public static bool TryGet(int localX, int localY, out ushort reserve)
    {
        EnsureWorldBound();
        return Reserve.TryGetValue(Key(localX, localY), out reserve);
    }

    /// <summary>Текущий остаток: мутация, иначе базовый запас поля (0 = руды нет).</summary>
    public static ushort GetReserve(int localX, int localY, ulong seed, uint ver)
    {
        EnsureWorldBound();
        if (Reserve.TryGetValue(Key(localX, localY), out ushort mutated))
            return mutated;
        return OreWorldField.GetBaseReserveLocal(localX, localY, seed, ver);
    }

    /// <summary>Записать остаток (0 = выработка). Повтор с тем же значением — no-op.</summary>
    public static void SetReserve(int localX, int localY, ushort reserve)
    {
        EnsureWorldBound();
        long key = Key(localX, localY);
        if (Reserve.TryGetValue(key, out ushort old) && old == reserve)
            return;
        Reserve[key] = reserve;
        CellChanged?.Invoke(localX, localY);
    }

    /// <summary>
    /// Добыть порцию: возвращает сколько снято и остаток (0 = выработка).
    ///
    /// N-7 (task-23): АТОМАРНОЕ вычитание (CAS-повтор на ConcurrentDictionary —
    /// TryUpdate по прочитанному значению / TryAdd). Раньше Read→Modify→Write
    /// прикрывался клеймом работы, но контракт был хрупким: параллельные Extract
    /// (например, мир + остров по одной клетке) теряли обновления. Теперь потерь
    /// нет; горячий путь без аллокаций.
    /// </summary>
    public static int Extract(int localX, int localY, int wanted, ulong seed, uint ver,
        out ushort remaining)
    {
        if (wanted <= 0)
        {
            remaining = GetReserve(localX, localY, seed, ver);
            return 0;
        }

        EnsureWorldBound();
        long key = Key(localX, localY);
        // Крайне маловероятный предел: защита от «живого» горячего цикла при
        // патологической конкуренции; при исчерпании — решаем, что не повезло.
        for (int attempt = 0; attempt < 1024; attempt++)
        {
            ushort current = Reserve.TryGetValue(key, out ushort mutated)
                ? mutated
                : OreWorldField.GetBaseReserveLocal(localX, localY, seed, ver);
            if (current == 0)
            {
                remaining = 0;
                return 0;
            }

            int take = Math.Min(wanted, current);
            ushort next = (ushort)(current - take);

            bool won;
            if (Reserve.TryGetValue(key, out ushort observed))
                won = observed == current && Reserve.TryUpdate(key, next, observed);
            else
                won = Reserve.TryAdd(key, next);

            if (won)
            {
                remaining = next;
                CellChanged?.Invoke(localX, localY);
                return take;
            }
        }

        remaining = GetReserve(localX, localY, seed, ver);
        return 0;
    }

    /// <summary>Сброс (смена мира/сида).</summary>
    public static void Clear() => Reserve.Clear();

    /// <summary>Применить дельту из чанк-стора (загрузка чанка). Без события — рендер
    /// перерисует чанк целиком после ApplyReady.</summary>
    public static void ApplyLoadedReserve(int localX, int localY, ushort reserve)
    {
        EnsureWorldBound();
        Reserve[Key(localX, localY)] = reserve;
    }

    /// <summary>Все сохранённые клетки (для сейва острова/диагностики).</summary>
    public static IEnumerable<(int LocalX, int LocalY, ushort Reserve)> Enumerate()
    {
        EnsureWorldBound();
        foreach (var kvp in Reserve)
        {
            long key = kvp.Key;
            yield return ((int)(key >> 32), (int)(uint)key, kvp.Value);
        }
    }

    /// <summary>Сохранённые клетки заданного чанка (координаты чанка локального холста).</summary>
    public static List<(int LocalX, int LocalY, ushort Reserve)> EnumerateChunk(long chunkX, long chunkY)
    {
        EnsureWorldBound();
        var result = new List<(int, int, ushort)>(64);
        long baseX = chunkX * WorldStreaming.WorldCoordinates.ChunkSize;
        long baseY = chunkY * WorldStreaming.WorldCoordinates.ChunkSize;
        foreach (var kvp in Reserve)
        {
            long key = kvp.Key;
            int lx = (int)(key >> 32);
            int ly = (int)(uint)key;
            if (lx >= baseX && lx < baseX + WorldStreaming.WorldCoordinates.ChunkSize &&
                ly >= baseY && ly < baseY + WorldStreaming.WorldCoordinates.ChunkSize)
            {
                result.Add((lx, ly, kvp.Value));
            }
        }
        return result;
    }

    /// <summary>Payload записи чанк-стора: [ver=1, reserveLo, reserveHi].</summary>
    public static byte[] EncodePayload(ushort reserve)
        => new[] { PayloadVersion, (byte)(reserve & 0xFF), (byte)(reserve >> 8) };

    /// <summary>Разобрать payload записи; false — чужая/битая версия (запись игнорируется).</summary>
    public static bool TryDecodePayload(byte[] data, out ushort reserve)
    {
        reserve = 0;
        if (data == null || data.Length < 3 || data[0] != PayloadVersion)
            return false;
        reserve = (ushort)(data[1] | (data[2] << 8));
        return true;
    }

    private static long Key(int localX, int localY)
        => ((long)localX << 32) ^ (uint)localY;
}