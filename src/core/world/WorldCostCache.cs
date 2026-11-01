using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Game.Core.WorldStreaming;

namespace Game.Core.World;

/// <summary>
/// Стоимость прохода по клетке — кэш по чанкам, посчитанный из <see cref="WorldModel"/>.
///
/// Зачем отдельный слой: путь и проверка «можно ли тут пройти» вызываются на КАЖДОМ
/// шаге агента. Если каждый раз спрашивать генератор — это шум Перлина на клетку,
/// и агент начинает стоить дороже, чем весь остальной мир. Поэтому стоимость клетки
/// считается один раз на чанк 64×64 (4 КБ) и живёт в памяти.
///
/// ВАЖНО про резидентность: этот кэш НЕ связан с кэшем чанков мира и НЕ спрашивает
/// «загружен ли чанк». Он считается генератором на любой координате, включая
/// недостижимые для рендера. Именно это и делает мир единым: путь можно проложить
/// там, где ничего не нарисовано.
///
/// Стоимость (совместима с HierarchicalPathfinder):
///   0 — непроходимо (вода, горы), 1 — трава (в т.ч. камень: россыпь не блокирует).
/// </summary>
public static class WorldCostCache
{
    public const byte CostBlocked = 0;
    public const byte CostGrass = 1;

    private const int ChunkShift = 6;              // 64 клетки в чанке
    private const int ChunkSize = 1 << ChunkShift; // 64
    private const int ChunkMask = ChunkSize - 1;

    /// <summary>Потолок кэша: 8192 чанка × 4 КБ = 32 МБ. Дальше просто чистим целиком.</summary>
    private const int MaxChunks = 8192;

    private static readonly ConcurrentDictionary<long, byte[]> Chunks = new();

    /// <summary>
    /// Блокировки МИРА (§29, 2026-10-25): клетки, непроходимые поверх генератора —
    /// стены, построенные за кромкой (и островные стены при синке). Ключ = клетка
    /// в АБСОЛЮТНЫХ мировых координатах. Read-merge: CostAt проверяет оверлей ПОСЛЕ
    /// кэша чанков — не нужны инвалидации чанков и нет гонок сборки (словарь
    /// потокобезопасен, запись видна сразу). Память: ~28 байт на клетку-стену.
    /// НЕ переживает перезапуск процесса — восстановление из чанк-стора делает
    /// помпа Main.SyncWorldWallCosts (каждый тик, идемпотентно).
    /// </summary>
    private static readonly ConcurrentDictionary<long, byte> BlockedCells = new();

    /// <summary>Сколько клеток сейчас заблокировано поверх генератора (диагностика/тесты).</summary>
    public static int BlockedCellsCount => BlockedCells.Count;

    /// <summary>
    /// Проходимость клетки изменилась поверх генератора (стена построена/снесена).
    /// Зачем: окна мирового HPA (<see cref="Game.Simulation.Pathfinding.WorldPathfinder"/>)
    /// держат СКОПИРОВАННУЮ стоимость. Без этого уведомления они держали старую
    /// сетку и агенты шли сквозь свежепостроенную стену (или обходили снесённую).
    /// Событие живёт в core, подписчик — в simulation: обратной зависимости нет.
    /// </summary>
    public static event Action CellChanged;

    /// <summary>
    /// Сделать клетку непроходимой для мирового поиска пути (стена построена).
    /// Идемпотентен; true — состояние РЕАЛЬНО изменилось (гвард от шторма
    /// инвалидаций: помпа-синк перерегистрирует стены каждый тик).
    /// </summary>
    public static bool MarkBlocked(long x, long y)
    {
        if (!BlockedCells.TryAdd(PackCell(x, y), 1))
            return false;
        CellChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Снять блокировку клетки (стена снесена). true — блок была.
    /// Кэш чанков трогать не нужно: оверлей читается поверх, генератор отвечает сам.
    /// </summary>
    public static bool MarkUnblocked(long x, long y)
    {
        if (!BlockedCells.TryRemove(PackCell(x, y), out _))
            return false;
        CellChanged?.Invoke();
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long PackCell(long x, long y) => ((long)(uint)x << 32) | (uint)y;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte CostAt(long x, long y)
    {
        if (!BlockedCells.IsEmpty && BlockedCells.ContainsKey(PackCell(x, y)))
            return CostBlocked;

        // Каноническое знаковое деление (WorldCoordinates.FloorDiv), не каст long→int:
        // (int)(x >> 6) усекал координаты дальше 2^31 и СВЕЛ два разных чанка в один
        // ключ (стоимость неправильного куска карты). Дельта x>>6 влезает в int всегда.
        long cxLong = WorldCoordinates.FloorDiv(x, ChunkSize);
        long cyLong = WorldCoordinates.FloorDiv(y, ChunkSize);
        int cx = (int)cxLong;
        int cy = (int)cyLong;
        long key = PackChunk(cx, cy);

        byte[] chunk = Chunks.TryGetValue(key, out byte[] found) ? found : BuildChunk(cx, cy, key);

        int lx = WorldCoordinates.FloorMod(x, ChunkSize);
        int ly = WorldCoordinates.FloorMod(y, ChunkSize);
        return chunk[(ly << ChunkShift) | lx];
    }

    /// <summary>Проходима ли клетка.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsWalkable(long x, long y) => CostAt(x, y) != CostBlocked;

    /// <summary>
    /// Заполнить прямоугольник стоимостей (для окна патфайндера).
    /// originX/originY — мировая координата левого верхнего угла.
    /// </summary>
    public static void FillRect(long originX, long originY, int width, int height, byte[] destination)
    {
        if (destination == null) throw new ArgumentNullException(nameof(destination));
        if (destination.Length < width * height)
            throw new ArgumentException("буфер стоимостей меньше запрошенного прямоугольника", nameof(destination));

        int index = 0;
        for (int y = 0; y < height; y++)
        {
            long wy = originY + y;
            for (int x = 0; x < width; x++)
                destination[index++] = CostAt(originX + x, wy);
        }
    }

    /// <summary>Сколько чанков сейчас в памяти (для тестов и профиля).</summary>
    public static int CachedChunks => Chunks.Count;

    /// <summary>
    /// Полный сброс: кэш чанков + блокировки мира. Чанки восстановимы из
    /// генератора; блокировки — из чанк-стора стен (помпа Main.SyncWorldWallCosts
    /// переустановит их в ближайшие 500 мс). В проде Clear не зовётся — только
    /// тесты и смена мира (ResetWorld).
    /// </summary>
    public static void Clear()
    {
        Chunks.Clear();
        BlockedCells.Clear();
    }

    private static byte[] BuildChunk(int cx, int cy, long key)
    {
        // Простая защита от неограниченного роста: чистим целиком (кэш восстановим).
        if (Chunks.Count >= MaxChunks) Chunks.Clear();

        var data = new byte[ChunkSize * ChunkSize];
        long baseX = (long)cx << ChunkShift;
        long baseY = (long)cy << ChunkShift;

        for (int ly = 0; ly < ChunkSize; ly++)
        {
            long wy = baseY + ly;
            int row = ly << ChunkShift;
            for (int lx = 0; lx < ChunkSize; lx++)
            {
                BaseTerrainKind terrain = WorldModel.TerrainAt(baseX + lx, wy);
                data[row + lx] = terrain == BaseTerrainKind.Grass ? CostGrass : CostBlocked;
            }
        }

        return Chunks.GetOrAdd(key, data);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long PackChunk(int cx, int cy) => ((long)(uint)cx << 32) | (uint)cy;
}