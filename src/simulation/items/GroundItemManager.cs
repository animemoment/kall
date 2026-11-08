using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Numerics;
using System.Threading;
using Game.Core;

namespace Game.Simulation;

public class GroundItemManager
{
    public static GroundItemManager Instance { get; } = new();

    private const int ChunkShift = 4;
    private const int ChunkSize = 16;
    // J-22: сетка бакетов предметов была 32×32 = 512×512 тайлов, а остров 1024×1024.
    // GetChunkCoord клампил координаты, поэтому ВСЁ восточнее/южнее 511 попадало в
    // один бакет (31,31): резервирование и сканы под глобальным локом перебирали
    // огромный HashSet вместо маленького. 64×64 = 1024×1024 и кламп больше не нужен.
    private const int ChunkDim = 64;
    private const float ChunkSizePx = ChunkSize * 64f;
    private const int MaxChunkRadius = 32;

    // task-4: типов предметов стало 91 (3 legacy + 87 сырья T0 + руда 23..45).
    // Размеры всех массивов-счётчиков берутся из ItemConfig — раньше здесь было
    // жёсткое 8, и сырьё с id ≥ 8 молча не попадало в тоталы/снапшоты.
    private const int MaxItemTypes = ItemConfig.MaxItemTypes;
    private const int MaxSnapshotItemsPerType = 16384;
    /// <summary>SIM-12: Максимальный размер стака предметов на одной клетке земли.</summary>
    public const int MaxItemsPerCell = ItemConfig.RawMaxStack;

    private readonly object _lock = new();
    // Finding 4 (вариант 2, безопасный компромисс): вес предметов кэшируется
    // в массив float (индекс = (int)ItemId) — ItemRegistry.Get внутри скана
    // под lock делал dictionary-lookup на КАЖДОГО кандидата. Веса статичны
    // (Log 0.85 / Grain 0.15 / Stone 1.5), кэш заполняется лениво один раз.
    private readonly float[] _weightCache = new float[MaxItemTypes];
    private int _weightCacheReady;
    private readonly Dictionary<(int X, int Y), (ItemId Item, int Count, int Reserved)> _groundItems = new(2048);
    private readonly HashSet<(int X, int Y)>[,] _itemChunks = new HashSet<(int X, int Y)>[ChunkDim, ChunkDim];

    private readonly int[] _totalAvailableByType = new int[MaxItemTypes];
    // task-4: суммарный «есть ли что переносить» по ВСЕМ типам — без скана 91
    // счётчика на каждый CanAgentExecute haul-работы. Держится в тех же точках,
    // что и _totalAvailableByType (Interlocked).
    private int _totalAvailableAny;
    public bool HasAvailableLogs => Volatile.Read(ref _totalAvailableByType[(int)ItemId.Log]) > 0;
    /// <summary>Есть ли на земле ЛЮБОЙ доступный предмет (Log/Grain/камень/руда/сырьё).</summary>
    public bool HasAnyAvailableItems => Volatile.Read(ref _totalAvailableAny) > 0;
    public int TotalGroundPilesCount { get { lock (_lock) return _groundItems.Count; } }
    public bool HasAvailableItemsOfType(ItemId id)
    {
        int idx = (int)id;
        if (idx < 0 || idx >= MaxItemTypes) return false;
        return Volatile.Read(ref _totalAvailableByType[idx]) > 0;
    }

    /// <summary>
    /// Сколько единиц типа доступно на земле (без чужих резервов). task-20:
    /// нужно производству, чтобы понять «хватит ли 2 руды на рецепт», не
    /// обходя клетки.
    /// </summary>
    public int GetAvailableCount(ItemId id)
    {
        int idx = (int)id;
        return idx >= 0 && idx < MaxItemTypes ? Volatile.Read(ref _totalAvailableByType[idx]) : 0;
    }

    private bool _isDirty = true;

    // Мост A (PLAN.md §112.2): ссылка на данные карты инъекцируется снаружи
    // (Main.OnMapApplied) — TrySpillItemsAdjacent больше не читает
    // Game.UI.MapRenderer напрямую; сам тип MapData живёт в Game.Core.
    private MapData _mapData;

    /// <summary>
    /// Инъекция данных карты (вызывает Main.OnMapApplied на каждое применение
    /// карты). Null допустим — проверка воды/гор при спилле просто пропускается
    /// (раньше null-MapRenderer означал то же самое).
    /// </summary>
    public void SetMapData(MapData mapData) => _mapData = mapData;

    // Finding 6: ring из 3 снапшот-буферов вместо fresh-аллокации каждые 33 мс
    // (~256 КБ × 3 менеджера × 30 Гц ≈ 23 МБ/с мусора). Рендерер drain'ит очередь
    // сразу (TryDequeue до последнего), cap очереди = 2, ротация ring = 3 —
    // продюсер не перезаписывает буфер, который читает рендерер. _isDirty-гейт
    // уже есть: без изменений снапшот не строится вообще. Заполнение — только
    // counts-префикс, остаток массива — stale, рендерер его не читает.
    private readonly System.Numerics.Vector2[][][] _snapRing = new System.Numerics.Vector2[3][][];
    private readonly int[][] _snapCountsRing = new int[3][];
    private int _snapCursor;
    // P0-1: гейт секундных рядов ресурсов (Series = lock, снапшоты 30/с).
    private long _resSeriesWallTicks;

    public ConcurrentQueue<(Vector2[][] PositionsByItem, int[] Counts)> SnapshotQueue { get; } = new();

    public GroundItemManager()
    {
        for (int cx = 0; cx < ChunkDim; cx++)
        {
            for (int cy = 0; cy < ChunkDim; cy++)
            {
                _itemChunks[cx, cy] = new HashSet<(int X, int Y)>();
            }
        }
    }

    private float GetWeightCached(ItemId id)
    {
        int idx = (int)id;
        if ((uint)idx >= (uint)MaxItemTypes)
            return 1f;
        if (Volatile.Read(ref _weightCacheReady) == 0)
        {
            lock (_lock)
            {
                if (_weightCacheReady == 0)
                {
                    for (int t = 0; t < MaxItemTypes; t++)
                    {
                        try { _weightCache[t] = ItemRegistry.Get((ItemId)t).Weight; }
                        catch { _weightCache[t] = 1f; }
                    }
                    Volatile.Write(ref _weightCacheReady, 1);
                }
            }
        }
        float w = Volatile.Read(ref _weightCache[idx]);
        return w > 0f ? w : 1f;
    }

    private static (int CX, int CY) GetChunkCoord(int x, int y)
    {
        // J-22: без Clamp — координаты валидны (остров 1024×1024 → 0..63).
        // Кламп схлопывал восточную/южную половину карты в один бакет.
        int cx = x >> ChunkShift;
        int cy = y >> ChunkShift;
        if (cx < 0) cx = 0; else if (cx >= ChunkDim) cx = ChunkDim - 1;
        if (cy < 0) cy = 0; else if (cy >= ChunkDim) cy = ChunkDim - 1;
        return (cx, cy);
    }

    /// <summary>
    /// J-22: счётчик куч в бакете. Читается БЕЗ глобального лока (префильтр для
    /// HasItemsAt и скана резервирования), пишется только под _lock и только так,
    /// чтобы читатель не увидел «0» при живых предметах: инкремент идёт ДО вставки
    /// в HashSet, декремент — ПОСЛЕ удаления. Тогда нулевой счётчик означает
    /// «в бакете действительно пусто» (возможен лишь обратный, безопасный случай —
    /// счётчик > 0 при пустом бакете, и его разберёт честная проверка под локом).
    /// </summary>
    private readonly int[] _chunkPileCounts = new int[ChunkDim * ChunkDim];

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static int ChunkSlot(int cx, int cy) => cy * ChunkDim + cx;

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private int ChunkPileCount(int x, int y)
    {
        var (cx, cy) = GetChunkCoord(x, y);
        return Volatile.Read(ref _chunkPileCounts[ChunkSlot(cx, cy)]);
    }

    /// <summary>Добавить клетку в бакет (под _lock). Счётчик публикуется ДО вставки.</summary>
    private void ChunkAdd(int cx, int cy, (int X, int Y) key)
    {
        Interlocked.Increment(ref _chunkPileCounts[ChunkSlot(cx, cy)]);
        if (!_itemChunks[cx, cy].Add(key))
            Interlocked.Decrement(ref _chunkPileCounts[ChunkSlot(cx, cy)]); // уже был — откат
    }

    /// <summary>Убрать клетку из бакета (под _lock). Счётчик уменьшается ПОСЛЕ удаления.</summary>
    private void ChunkRemove(int cx, int cy, (int X, int Y) key)
    {
        if (_itemChunks[cx, cy].Remove(key))
            Interlocked.Decrement(ref _chunkPileCounts[ChunkSlot(cx, cy)]);
    }

    /// <summary>Очистить бакет (под _lock): и содержимое, и счётчик.</summary>
    private void ChunkClear(int cx, int cy)
    {
        // P0 (bughunt 08-1/12-1): здесь была самовызывающаяся рекурсия —
        // ClearAll() убивал процесс StackOverflowException'ом на «Играть».
        _itemChunks[cx, cy].Clear();
        Volatile.Write(ref _chunkPileCounts[ChunkSlot(cx, cy)], 0);
    }

    public void SpawnItems(int x, int y, ItemId id, int count)
    {
        // Нулевой/отрицательный спавн не создаёт «нулевых стаков»:
        // инвариант _groundItems — Count > 0 всегда (см. проверки ниже).
        if (count <= 0 || id == ItemId.None)
            return;
        int totalSpawned = count;
        int placedX = x, placedY = y;

        lock (_lock)
        {
            // SIM-12: Если в целевой клетке уже лежит стак того же типа и размер меньше MaxItemsPerCell,
            // объединяем количество в существующий стак вместо создания новой записи на земле.
            if (_groundItems.TryGetValue((x, y), out var entry) && entry.Item == id)
            {
                if (entry.Count <= 0)
                {
                    // Битый нулевой стак (не должен существовать) — чиним на месте.
                    int toAdd = Math.Min(count, MaxItemsPerCell);
                    _groundItems[(x, y)] = (id, toAdd, 0);
                    var (rcx, rcy) = GetChunkCoord(x, y);
                    ChunkAdd(rcx, rcy, (x, y));
                    count -= toAdd;
                }
                else if (entry.Count < MaxItemsPerCell)
                {
                    int space = MaxItemsPerCell - entry.Count;
                    int toAdd = Math.Min(count, space);
                    _groundItems[(x, y)] = (id, entry.Count + toAdd, entry.Reserved);
                    count -= toAdd;
                }

                if (count <= 0)
                {
                    placedX = x;
                    placedY = y;
                }
            }
            else if (!_groundItems.ContainsKey((x, y)))
            {
                int toAdd = Math.Min(count, MaxItemsPerCell);
                _groundItems[(x, y)] = (id, toAdd, 0);
                var (cx, cy) = GetChunkCoord(x, y);
                ChunkAdd(cx, cy, (x, y));
                count -= toAdd;
                placedX = x;
                placedY = y;
            }

            // Если остался излишек (переполнение целевой клетки) или в (x, y) лежит чужой предмет —
            // распределяем по соседям, объединяя с однотипными стаками до MaxItemsPerCell.
            if (count > 0)
            {
                bool placed = false;
                for (int oy = -2; oy <= 2 && !placed; oy++)
                {
                    for (int ox = -2; ox <= 2 && !placed; ox++)
                    {
                        if (ox == 0 && oy == 0) continue;
                        var key = (x + ox, y + oy);
                        if (_groundItems.TryGetValue(key, out var n))
                        {
                            if (n.Item != id || n.Count >= MaxItemsPerCell)
                                continue;
                            if (n.Count <= 0)
                            {
                                int toAdd = Math.Min(count, MaxItemsPerCell);
                                _groundItems[key] = (id, toAdd, 0);
                                var (ccx0, ccy0) = GetChunkCoord(key.Item1, key.Item2);
                                ChunkAdd(ccx0, ccy0, key);
                                count -= toAdd;
                                placedX = key.Item1;
                                placedY = key.Item2;
                                if (count <= 0) placed = true;
                                continue;
                            }
                            int space = MaxItemsPerCell - n.Count;
                            int toAddNeighbor = Math.Min(count, space);
                            _groundItems[key] = (id, n.Count + toAddNeighbor, n.Reserved);
                            count -= toAddNeighbor;
                            placedX = key.Item1;
                            placedY = key.Item2;
                            if (count <= 0) placed = true;
                        }
                        else
                        {
                            int toAdd = Math.Min(count, MaxItemsPerCell);
                            _groundItems[key] = (id, toAdd, 0);
                            var (ccx, ccy) = GetChunkCoord(key.Item1, key.Item2);
                            ChunkAdd(ccx, ccy, key);
                            count -= toAdd;
                            placedX = key.Item1;
                            placedY = key.Item2;
                            if (count <= 0) placed = true;
                        }
                    }
                }

                if (count > 0)
                {
                    // ST-08 / GD-05: защита от уничтожения лута — если 24 соседа заняты,
                    // объединяем остаток со стаком на (x, y), гарантируя 100% сохранность ресурсов.
                    if (_groundItems.TryGetValue((x, y), out var current))
                    {
                        _groundItems[(x, y)] = (current.Item, current.Count + count, current.Reserved);
                    }
                    else
                    {
                        _groundItems[(x, y)] = (id, count, 0);
                        var (cx, cy) = GetChunkCoord(x, y);
                        ChunkAdd(cx, cy, (x, y));
                    }
                    placedX = x;
                    placedY = y;
                }
            }

            int itemIdx = (int)id;
            if (itemIdx > 0 && itemIdx < MaxItemTypes)
            {
                Interlocked.Add(ref _totalAvailableByType[itemIdx], totalSpawned);
                Interlocked.Add(ref _totalAvailableAny, totalSpawned);
            }

            _isDirty = true;
        }

        // HasFreeSpace читаем ВНЕ Ground-lock: он и сам лока не берёт (два Volatile.Read
        // по счётчикам склада), поэтому порядок G->S нигде не удерживается.
        // Работу регистрируем по ФАКТИЧЕСКОЙ клетке размещения (placedX/Y).
        bool hasFreeStockpileSpace = StockpileManager.Instance.HasFreeSpace;

        // 1. Реактивное Push-уведомление через ResourceBroker: если ресурса ждут чертежи/потребители
        bool matchedDemand = ResourceBroker.Instance.NotifyResourceAvailable(id, placedX, placedY, totalSpawned);

        // 2. Если заказ не забрал этот ресурс — регистрируем переноску на склад.
        // FIX (bughunt 12-7): складская клетка больше НЕ считается «уже учтённой складом».
        // Куча лежит в _groundItems, а не в _storage: в тоталах/HUD склада её нет,
        // TryReserveStockpileItem/TryTakeStockpileFood её не видят, а sweep такие клетки
        // раньше пропускал — брёвна под нарисованным складом оставались на полу навсегда.
        // Регистрируем haul и на складской клетке: путь StockpileHaulingJobHandler при
        // цели-складской клетке кладёт груз ТОЛЬКО в _storage (DepositItems) и не спавнит
        // второй стак на полу, поэтому инвариант «предмет в одном месте» держится —
        // куча либо в _groundItems, либо в _storage, но не в обоих.
        if (!matchedDemand && hasFreeStockpileSpace)
        {
            JobBroker.Instance.RegisterStockpileHaul(placedX, placedY);
        }
    }

    public bool HasItemsAt(int x, int y)
    {
        // J-22: этот метод зовётся на горячем пути выдачи работ (валидатор, хендлеры,
        // разметка фермы) и раньше КАЖДЫЙ вызов брал глобальный лок всех предметов
        // земли — конкуренция с тиком симуляции. Теперь пустой бакет отвечает без
        // лока (в подавляющем большинстве запросов), а лок берётся только там, где
        // в бакете действительно что-то есть.
        if (ChunkPileCount(x, y) == 0)
            return false;

        lock (_lock)
        {
            return _groundItems.TryGetValue((x, y), out var entry) && entry.Count > 0;
        }
    }

    /// <summary>
    /// Сдвинуть предметы из клетки (x, y) на соседнюю свободную проходимую клетку (чтобы освободить стройку/грядку).
    /// </summary>
    public bool TrySpillItemsAdjacent(int x, int y, int mapWidth, int mapHeight, bool[,] solidWalls = null)
    {
        bool spilled = false;
        lock (_lock)
        {
            if (!_groundItems.TryGetValue((x, y), out var entry) || entry.Count <= 0)
                return false;

            for (int oy = -1; oy <= 1; oy++)
            {
                for (int ox = -1; ox <= 1; ox++)
                {
                    if (ox == 0 && oy == 0) continue;
                    int nx = x + ox, ny = y + oy;
                    if (nx >= 0 && ny >= 0 && nx < mapWidth && ny < mapHeight)
                    {
                        if (solidWalls != null && solidWalls[nx, ny]) continue;
                        if (_mapData != null)
                        {
                            var g = _mapData.Ground[nx, ny];
                            if (g == TileType.Water || g == TileType.Mountain) continue;
                        }
                        var nkey = (nx, ny);
                        if (!_groundItems.ContainsKey(nkey) || _groundItems[nkey].Item == entry.Item)
                        {
                            _groundItems.Remove((x, y));
                            var (ocx, ocy) = GetChunkCoord(x, y);
                            ChunkRemove(ocx, ocy, (x, y));

                            if (_groundItems.TryGetValue(nkey, out var nentry))
                            {
                                _groundItems[nkey] = (entry.Item, nentry.Count + entry.Count, nentry.Reserved + entry.Reserved);
                            }
                            else
                            {
                                _groundItems[nkey] = (entry.Item, entry.Count, entry.Reserved);
                                var (ncx, ncy) = GetChunkCoord(nx, ny);
                                ChunkAdd(ncx, ncy, nkey);
                            }
                            _isDirty = true;
                            spilled = true;
                            break;
                        }
                    }
                }
                if (spilled) break;
            }
        }

        if (spilled)
        {
            FarmJobManager.Instance.NotifyItemCleared(x, y);
            ConstructionManager.Instance.NotifyHaulCleared(x, y);
            return true;
        }
        return false;
    }

    /// <summary>Тип предмета в клетке без резерва (для корректного CarriedItemId).</summary>
    public ItemId PeekItemAt(int x, int y)
    {
        lock (_lock)
        {
            return _groundItems.TryGetValue((x, y), out var entry) && entry.Count > 0
                ? entry.Item : ItemId.None;
        }
    }

    /// <summary>
    /// P1: атомарно забирает до count предметов И возвращает их тип.
    /// Сливает PeekItemAt+TakeItems (два прохода под двумя lock) в один захват
    /// _lock: Phase3b-commit делал Peek+Take на каждого прибывшего носильщика,
    /// два lock/acquire на одного агента = конвой при тысячах коммитов/тик.
    /// Возвращает (тип, забрано): тип None + 0 — куча пуста/отсутствует.
    /// </summary>
    public (ItemId Item, int Taken) TakeItemsWithType(int x, int y, int count)
    {
        ItemId item;
        int toTake;
        lock (_lock)
        {
            if (!_groundItems.TryGetValue((x, y), out var entry) || entry.Count <= 0)
                return (ItemId.None, 0);
            item = entry.Item;
            toTake = Math.Min(entry.Count, count);
            int newCount = entry.Count - toTake;
            int newReserved = Math.Max(0, entry.Reserved - toTake);

            if (newCount <= 0)
            {
                _groundItems.Remove((x, y));
                var (cx, cy) = GetChunkCoord(x, y);
                ChunkRemove(cx, cy, (x, y));
            }
            else
            {
                _groundItems[(x, y)] = (entry.Item, newCount, newReserved);
            }

            _isDirty = true;
        }

        if (toTake > 0 && !HasItemsAt(x, y))
        {
            FarmJobManager.Instance.NotifyItemCleared(x, y);
            ConstructionManager.Instance.NotifyHaulCleared(x, y);
        }

        return (item, toTake);
    }

    /// <summary>
    /// §119 P7б (task-17 п.3-5): снимок куч на земле для сейва (payload'ы
    /// GroundItems-меток). Копия под коротким lock; Reserved НЕ пишем.
    /// </summary>
    public List<(int X, int Y, ItemId Item, int Count)> GetItemsSnapshot()
    {
        var result = new List<(int, int, ItemId, int)>(64);
        lock (_lock)
        {
            foreach (var kvp in _groundItems)
            {
                var entry = kvp.Value;
                if (entry.Item != ItemId.None && entry.Count > 0)
                    result.Add((kvp.Key.X, kvp.Key.Y, entry.Item, entry.Count));
            }
        }
        return result;
    }

    /// <summary>Очистить все предметы на земле при смене региона / перезапуске карты.</summary>
    public void ClearAll()
    {
        lock (_lock)
        {
            _groundItems.Clear();
            for (int cy = 0; cy < ChunkDim; cy++)
                for (int cx = 0; cx < ChunkDim; cx++)
                    ChunkClear(cx, cy);
            Array.Clear(_totalAvailableByType, 0, _totalAvailableByType.Length);
            Volatile.Write(ref _totalAvailableAny, 0);
            _isDirty = true;
        }
    }

    /// <summary>
    /// Собирает до <paramref name="maxItems"/> клеток с кучами на земле, которых ЕЩЁ НЕТ
    /// в складском хранилище (_storage), — для sweep-регистрации haul-работ. Вызывается
    /// из JobBroker.
    ///
    /// FIX (bughunt 12-7): клетки складской зоны больше не отбрасываются. Куча на
    /// складской клетке — это НЕ складской запас: она в _groundItems, а не в _storage,
    /// поэтому и её надо переносить. Прежний фильтр `IsZoneTile → continue` держал такой
    /// инвариант на ложном допущении «раз клетка склад, то предметы уже учтены» — из-за
    /// него куча под нарисованным складом (и остаток после частичной переноски) лежала
    /// на полу навсегда. Имя метода сохранено ради публичного API и существующего вызова
    /// из JobBroker; смысл теперь точный — «кучи, которые надо перенести в _storage».
    /// </summary>
    public void CollectItemTilesOutsideStockpiles(List<(int X, int Y)> destination, int maxItems)
    {
        destination.Clear();
        if (maxItems <= 0)
            return;
        // Пишем в буфер вызывающего под коротким Ground-lock: фильтра по складской зоне
        // больше нет, значит второго (складского) лока внутри секции не будет вообще —
        // прежний порядок G->S и промежуточная копия кандидатов тут больше не нужны.
        lock (_lock)
        {
            foreach (var kvp in _groundItems)
            {
                if (kvp.Value.Count <= 0)
                    continue; // битый нулевой стак — работу под него не создаём
                destination.Add(kvp.Key);
                if (destination.Count >= maxItems)
                    break;
            }
        }
    }

    public int GetItemCountAt(int x, int y)
    {
        lock (_lock)
        {
            return _groundItems.TryGetValue((x, y), out var entry) ? entry.Count : 0;
        }
    }

    public bool TryReserveGroundItems(Vector2 agentPos, float maxWeightCapacity, bool allowFromStockpile, out (int X, int Y) cell, out ItemId id, out int reservedCount)
    {
        return TryReserveGroundItems(agentPos, maxWeightCapacity, allowFromStockpile, ItemId.None, out cell, out id, out reservedCount);
    }

    public bool TryReserveGroundItems(Vector2 agentPos, float maxWeightCapacity, bool allowFromStockpile, ItemId preferredId, out (int X, int Y) cell, out ItemId id, out int reservedCount)
    {
        return TryReserveGroundItems(agentPos, maxWeightCapacity, allowFromStockpile, preferredId, MaxChunkRadius, out cell, out id, out reservedCount);
    }

    /// <summary>
    /// P0-фикс thundering herd (фриз 16:45): fast-fail по тоталам + лимит радиуса.
    /// Без еды на карте — возврат без взятия lock. Радиус ограничивает спиральный скан
    /// (еда: 3 чанка вместо 32 — в ~100 раз меньше работы под lock на агента).
    /// </summary>
    public bool TryReserveGroundItems(Vector2 agentPos, float maxWeightCapacity, bool allowFromStockpile, ItemId preferredId, int maxChunkRadius, out (int X, int Y) cell, out ItemId id, out int reservedCount)
    {
        cell = (-1, -1);
        id = ItemId.None;
        reservedCount = 0;

        // Fast-fail: запрашиваемого типа нет вообще — не берём lock, не сканируем карту.
        // Критично при 10k голодных и 0 зерна: иначе все молотят полный скан каждый тик.
        // P0-1: fast-fail без счётчиков (ToString+lock на каждый промах).
        if (preferredId != ItemId.None)
        {
            int pIdx = (int)preferredId;
            if (pIdx < 0 || pIdx >= MaxItemTypes || Volatile.Read(ref _totalAvailableByType[pIdx]) <= 0)
            {
                return false;
            }
        }
        else if (_groundItems.Count == 0)
        {
            return false;
        }

        int radius = Math.Clamp(maxChunkRadius, 1, MaxChunkRadius);
        // P0-1: без замера lockwait — Stopwatch+Count на КАЖДЫЙ резерв.
        lock (_lock)
        {
            if (_groundItems.Count == 0)
                return false;

            int startCx = Math.Clamp((int)(agentPos.X / ChunkSizePx), 0, ChunkDim - 1);
            int startCy = Math.Clamp((int)(agentPos.Y / ChunkSizePx), 0, ChunkDim - 1);

            int agentTileX = (int)(agentPos.X / 64f);
            int agentTileY = (int)(agentPos.Y / 64f);

            float bestDistSq = float.MaxValue;
            // Кандидаты собираем под lock БЕЗ вызовов Stockpile (второй lock) — иначе конвой G->S.
            (int X, int Y) bestCell = (-1, -1);
            ItemId bestId = ItemId.None;
            int bestReserve = 0;

            for (int r = 0; r < radius; r++)
            {
                int minCx = Math.Max(0, startCx - r);
                int maxCx = Math.Min(ChunkDim - 1, startCx + r);
                int minCy = Math.Max(0, startCy - r);
                int maxCy = Math.Min(ChunkDim - 1, startCy + r);

                for (int cx = minCx; cx <= maxCx; cx++)
                {
                    for (int cy = minCy; cy <= maxCy; cy++)
                    {
                        if (r > 0 && cx > minCx && cx < maxCx && cy > minCy && cy < maxCy)
                            continue;

                        var chunk = _itemChunks[cx, cy];
                        if (chunk.Count == 0) continue;

                        foreach (var pos in chunk)
                        {
                            if (!_groundItems.TryGetValue(pos, out var entry)) continue;

                            if (preferredId != ItemId.None && entry.Item != preferredId) continue;

                            int available = entry.Count - entry.Reserved;
                            if (available <= 0) continue;

                            // Finding 4: вес из кэша (без dictionary-lookup под lock).
                            float weight = GetWeightCached(entry.Item);
                            int maxCanTake = (int)(maxWeightCapacity / weight);
                            if (maxCanTake <= 0) continue;

                            if (!allowFromStockpile && StockpileManager.Instance.IsZoneTile(pos.X, pos.Y))
                                continue;

                            float dx = pos.X - agentTileX;
                            float dy = pos.Y - agentTileY;
                            float distSq = dx * dx + dy * dy;

                            if (distSq < bestDistSq)
                            {
                                bestDistSq = distSq;
                                bestCell = pos;
                                bestId = entry.Item;
                                bestReserve = Math.Min(available, maxCanTake);
                            }
                        }
                    }
                }

                if (bestCell.X != -1 && bestReserve > 0)
                    break;
            }

            if (bestCell.X == -1 || bestReserve <= 0)
                return false;

            if (!_groundItems.TryGetValue(bestCell, out var e2) || e2.Item != bestId)
                return false;

            int avail2 = e2.Count - e2.Reserved;
            if (avail2 <= 0) return false;

            int take = Math.Min(avail2, bestReserve);
            _groundItems[bestCell] = (e2.Item, e2.Count, e2.Reserved + take);

            cell = bestCell;
            id = bestId;
            reservedCount = take;

            int resIdx = (int)e2.Item;
            if (resIdx > 0 && resIdx < MaxItemTypes)
            {
                Interlocked.Add(ref _totalAvailableByType[resIdx], -take);
                Interlocked.Add(ref _totalAvailableAny, -take);
            }

            return true;
        }
    }

    public bool TryReserveSpecificCell(int x, int y, float maxWeightCapacity, out ItemId id, out int reservedCount)
    {
        lock (_lock)
        {
            id = ItemId.None;
            reservedCount = 0;

            if (_groundItems.TryGetValue((x, y), out var entry))
            {
                int available = entry.Count - entry.Reserved;
                if (available <= 0) return false;

                // Finding 4: вес из кэша (см. TryReserveGroundItems).
                int maxCanTake = (int)(maxWeightCapacity / GetWeightCached(entry.Item));
                if (maxCanTake <= 0) return false;

                id = entry.Item;
                reservedCount = Math.Min(available, maxCanTake);
                _groundItems[(x, y)] = (entry.Item, entry.Count, entry.Reserved + reservedCount);
                int resIdx = (int)entry.Item;
                if (resIdx > 0 && resIdx < MaxItemTypes)
                {
                    Interlocked.Add(ref _totalAvailableByType[resIdx], -reservedCount);
                    Interlocked.Add(ref _totalAvailableAny, -reservedCount);
                }
                return true;
            }

            return false;
        }
    }

    public int TakeItems(int x, int y, int count)
    {
        return TakeItemsWithType(x, y, count).Taken;
    }

    public void ReleaseReservation(int x, int y, int count)
    {
        if (count <= 0)
            return;
        // P0-1: без счётчика (горячий путь откатов резерва).
        ItemId relItem = ItemId.None;
        int released = 0;
        lock (_lock)
        {
            if (_groundItems.TryGetValue((x, y), out var entry))
            {
                // FIX (bughunt 12-3): начисляем только ФАКТИЧЕСКИ снятый резерв, а не
                // запрошенный count. Раньше при расхождении (куча частично разобрана
                // чужим TakeItems, резерв уже снят/пересоздан) в _totalAvailableByType
                // возвращался весь count — вечный фантом «есть бревно/зерно».
                released = Math.Min(count, entry.Reserved);
                if (released > 0)
                {
                    _groundItems[(x, y)] = (entry.Item, entry.Count, entry.Reserved - released);
                    relItem = entry.Item;
                }
            }
        }
        int relIdx = (int)relItem;
        if (relIdx > 0 && relIdx < MaxItemTypes)
        {
            Interlocked.Add(ref _totalAvailableByType[relIdx], released);
            Interlocked.Add(ref _totalAvailableAny, released);
        }
    }

    public void GenerateSnapshot()
    {
        // Снапшот читает словарь — короткая блокировка только на копирование.
        KeyValuePair<(int X, int Y), (ItemId Item, int Count, int Reserved)>[] itemsCopy;
        lock (_lock)
        {
            if (!_isDirty) return;
            _isDirty = false;

            // Копируем только ссылки на записи (без аллокации списка ключей в Update-пути —
            // здесь снимок идёт редко: при GenerateSnapshot из Phase2), затем отпускаем lock
            // и строим буферы вне критической секции, чтобы не держать потоки.
            itemsCopy = new KeyValuePair<(int X, int Y), (ItemId Item, int Count, int Reserved)>[_groundItems.Count];
            int ci = 0;
            foreach (var kvp in _groundItems)
                itemsCopy[ci++] = kvp;
        }

        // Finding 6: ring-буфер вместо fresh-аллокации. Ленивая инициализация
        // слота (первый проход) — дальше ноль аллокаций на снапшот.
        int slot = _snapCursor % 3;
        _snapCursor++;
        var currentSnap = _snapRing[slot];
        var currentCounts = _snapCountsRing[slot];
        if (currentSnap == null)
        {
            // task-4: позиционные буферы типов аллоцируются ЛЕНИВО (по первому
            // предмету типа) — при 91 типе жадная матрица 91×16384×8 байт ×3 слота
            // = ~36 МБ пустой памяти. Реальные типы на карте — единицы.
            currentSnap = new Vector2[MaxItemTypes][];
            currentCounts = new int[MaxItemTypes];
            _snapRing[slot] = currentSnap;
            _snapCountsRing[slot] = currentCounts;
        }
        else
        {
            Array.Clear(currentCounts, 0, currentCounts.Length);
        }

        for (int i = 0; i < itemsCopy.Length; i++)
        {
            var pos = itemsCopy[i].Key;
            var entry = itemsCopy[i].Value;
            if (entry.Count > 0)
            {
                int itemIdx = (int)entry.Item;
                if (itemIdx >= 0 && itemIdx < MaxItemTypes)
                {
                    int count = currentCounts[itemIdx];
                    if (count < MaxSnapshotItemsPerType)
                    {
                        currentSnap[itemIdx] ??= new Vector2[MaxSnapshotItemsPerType];
                        currentSnap[itemIdx][count] = new Vector2(pos.X * 64f + 32f, pos.Y * 64f + 32f);
                        currentCounts[itemIdx]++;
                    }
                }
            }
        }

        SnapshotQueue.Enqueue((currentSnap, currentCounts));

        while (SnapshotQueue.Count > 2)
            SnapshotQueue.TryDequeue(out _);
        // P0-1: ряды ресурсов — раз в секунду wall-clock, не на каждый снапшот
        // (ToString + lock на тип, 30 раз/с). Снапшот идёт из sim-потока.
        long nowResTicks = DateTime.UtcNow.Ticks;
        if (nowResTicks - _resSeriesWallTicks >= 10000000L)
        {
            _resSeriesWallTicks = nowResTicks;
            // task-4: ряды — только для legacy-типов (Log/Grain/Stone + резерв):
            // 91 ряд в SimEvents раз в секунду — лишний шум и аллокации строк.
            for (int t = 1; t < 8 && t < MaxItemTypes; t++)
                SimEvents.Series("res.available." + ((ItemId)t).ToString(), Volatile.Read(ref _totalAvailableByType[t]));
            SimEvents.Series("res.ground_piles", _groundItems.Count);
        }
    }

    // P0-1: гейт — ряды склада пишутся из снапшотов/коммитов, не чаще раза в секунду.
    private static long _stockSeriesWallTicks;

    /// <summary>D53: ряд свободных слотов склада — сюда же, рядом с ресурсами.</summary>
    public static void NoteStockpileSeries(int freeSlots, int zones)
    {
        long now = DateTime.UtcNow.Ticks;
        if (now - _stockSeriesWallTicks < 10000000L)
            return;
        _stockSeriesWallTicks = now;
        SimEvents.Series("res.stock_free", freeSlots);
        SimEvents.Series("res.stock_zones", zones);
    }
}