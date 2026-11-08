using System;
using System.Collections.Generic;
using Godot;
using Game.Core;

namespace Game.Simulation;

/// <summary>
/// Результат снятия порции руды из клетки-хранилища (task-4).
/// </summary>
public readonly struct OreExtractResult
{
    /// <summary>Предмет-дроп (None, если ничего не снято).</summary>
    public readonly ItemId Item;
    /// <summary>Сколько единиц реально снято (может быть меньше запрошенного).</summary>
    public readonly int Extracted;
    /// <summary>Жила выработана: клетка очищена (OreType=0), метка снята.</summary>
    public readonly bool Depleted;
    /// <summary>Состояние rich/normal/poor сменилось — тайл надо перерисовать.</summary>
    public readonly bool StateChanged;

    public OreExtractResult(ItemId item, int extracted, bool depleted, bool stateChanged)
    {
        Item = item;
        Extracted = extracted;
        Depleted = depleted;
        StateChanged = stateChanged;
    }
}

/// <summary>
/// Метки рудных жил под добычу (близнец <see cref="StoneJobManager"/>, но руда —
/// ХРАНИЛИЩЕ с запасом, а не «есть/нет»).
///
/// Работа = ОДИН цикл добычи (4 игровых часа, как камень): агент приходит, работает,
/// за цикл снимает порцию 8–15 ед., на землю падает дроп соответствующего ItemId.
/// Если запас остался — на клетку сразу регистрируется следующий цикл (метка живёт
/// до выработки). Выработалось — клетка очищается (OreType=0), метка снимается,
/// событие <see cref="OnOreDepleted"/> убирает тайл и маркер.
///
/// Потокобезопасность: метки — под lock; мутация запаса/состояния — под тем же
/// lock (Commit идёт из параллельной фазы). События — только main-поток через
/// <c>CallDeferred</c>; рендер сам решает, что перерисовать.
/// </summary>
public sealed class OreJobManager
{
    public static OreJobManager Instance { get; } = new();

    private readonly object _lock = new();
    private readonly HashSet<(int X, int Y)> _markedOre = new(1024);
    // task-8: метки руды ЗА КРОМКОЙ острова (координаты — локальный холст).
    private readonly HashSet<(int X, int Y)> _worldMarkedOre = new(256);

    /// <summary>Per-cell прогресс цикла 0..1 — переживает прерывание агента.</summary>
    public CellProgressStore MineProgress { get; } = new();

    public event Action<(int X, int Y)> OnOreMarked;
    public event Action<List<(int X, int Y)>> OnOreBatchMarked;
    public event Action<(int X, int Y)> OnOreUnmarked;
    public event Action<List<(int X, int Y)>> OnOreBatchUnmarked;
    /// <summary>Запас/состояние клетки изменились — перерисовать тайл и соседей.</summary>
    public event Action<(int X, int Y)> OnOreChanged;
    /// <summary>Жила выработана: клетка очищена, тайл/маркер убрать.</summary>
    public event Action<(int X, int Y)> OnOreDepleted;
    /// <summary>Жила за кромкой выработана.</summary>
    public event Action<(int X, int Y)> OnWorldOreDepleted;

    // ------------------------------------------------------------------ //
    //  Метки                                                              //
    // ------------------------------------------------------------------ //

    /// <summary>Пометить клетку с рудой под добычу. Вне карты/без руды — no-op.</summary>
    public void MarkOre(int x, int y, SimulationContext ctx)
    {
        if (!IsOreCell(x, y, ctx))
            return;

        bool added = false;
        lock (_lock)
        {
            added = _markedOre.Add((x, y));
            if (added)
                MineProgress.Clear(x, y);
        }
        if (!added)
            return;

        JobBroker.Instance.RegisterOreMine(x, y);
        Callable.From(() => OnOreMarked?.Invoke((x, y))).CallDeferred();
    }

    /// <summary>Пометить пачку клеток (инструмент добычи руды).</summary>
    public void MarkOreBatch(List<(int X, int Y)> cells, SimulationContext ctx = null)
    {
        if (cells == null || cells.Count == 0)
            return;

        var addedList = new List<(int X, int Y)>(cells.Count);
        lock (_lock)
        {
            foreach (var pos in cells)
            {
                if (ctx != null && !IsOreCellLocked(pos.X, pos.Y, ctx))
                    continue;
                if (_markedOre.Add(pos))
                {
                    MineProgress.Clear(pos.X, pos.Y);
                    addedList.Add(pos);
                }
            }
        }

        if (addedList.Count == 0)
            return;
        JobBroker.Instance.RegisterOreMineBatch(addedList, ctx);
        Callable.From(() => OnOreBatchMarked?.Invoke(addedList)).CallDeferred();
    }

    public void UnmarkOre(int x, int y)
    {
        lock (_lock)
        {
            if (!_markedOre.Remove((x, y)))
                return;
            MineProgress.Clear(x, y);
        }
        JobBroker.Instance.UnregisterOreMine(x, y);
        Callable.From(() => OnOreUnmarked?.Invoke((x, y))).CallDeferred();
    }

    public void UnmarkOreBatch(List<(int X, int Y)> cells)
    {
        if (cells == null || cells.Count == 0)
            return;

        var removed = new List<(int X, int Y)>(cells.Count);
        lock (_lock)
        {
            foreach (var pos in cells)
            {
                if (_markedOre.Remove(pos))
                {
                    MineProgress.Clear(pos.X, pos.Y);
                    removed.Add(pos);
                }
            }
        }
        if (removed.Count == 0)
            return;
        JobBroker.Instance.UnregisterOreMineBatch(removed);
        Callable.From(() => OnOreBatchUnmarked?.Invoke(removed)).CallDeferred();
    }

    public bool IsOreMarked(int x, int y)
    {
        lock (_lock)
            return _markedOre.Contains((x, y));
    }

    /// <summary>Копия меток (JobValidator: аудит потерянных задач).</summary>
    public HashSet<(int X, int Y)> GetAllMarkedOre()
    {
        lock (_lock)
            return new HashSet<(int X, int Y)>(_markedOre);
    }

    /// <summary>Очистка при смене региона/перезапуске карты (метки + прогресс).</summary>
    public void ClearAll()
    {
        List<(int X, int Y)> old;
        lock (_lock)
        {
            old = new List<(int X, int Y)>(_markedOre);
            _markedOre.Clear();
            _worldMarkedOre.Clear();
            MineProgress.ClearAll();
        }
        if (old.Count == 0)
            return;
        JobBroker.Instance.UnregisterOreMineBatch(old);
        Callable.From(() => OnOreBatchUnmarked?.Invoke(old)).CallDeferred();
    }

    // ------------------------------------------------------------------ //
    //  Добыча (вызывается из sim-потока, в т.ч. из Parallel-commit)        //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Снять до <paramref name="wanted"/> единиц из клетки. Уменьшает OreReserve,
    /// пересчитывает OreState, при нуле очищает клетку и снимает метку/задачу.
    /// Возвращает, что снято и что случилось; события — отложенно в main-поток.
    ///
    /// N-4 (task-23): если OreType ещё не обнулён, а запас УЖЕ 0 — клетка считается
    /// выработанной: очищаем тип/state/запас, снимаем метки ОБОИХ миров и задачу,
    /// шлём OnOreDepleted. Иначе take=0 → работа снималась, метка висела навсегда.
    /// </summary>
    public OreExtractResult TryExtract(int x, int y, int wanted, SimulationContext ctx)
    {
        if (ctx?.OreType == null || wanted <= 0)
            return default;
        if ((uint)x >= (uint)ctx.MapWidth || (uint)y >= (uint)ctx.MapHeight)
            return default;

        ItemId item = ItemId.None;
        int extracted = 0;
        bool depleted = false;
        bool stateChanged = false;
        bool hadStaleMark = false;

        lock (_lock)
        {
            int idx = y * ctx.MapWidth + x;
            byte type = ctx.OreType[idx];
            if (type == OreConfig.None)
            {
                // Метка/работа протухли (клетку выработали иначе) — забываем ОБА мира.
                hadStaleMark = ForgetCellLocked(x, y);
            }
            else
            {
                int reserve = ctx.OreReserve[idx];
                if (reserve <= 0)
                {
                    // N-4: запас 0, но тип ещё есть — фактическая выработка.
                    item = ItemRegistry.ForOreType(type);
                    ClearDepletedCellLocked(ctx, idx, x, y);
                    // Персистентность мира: клетка выработана (0 в OreWorldState).
                    OreWorldState.SetReserve(x, y, 0);
                    depleted = true;
                    hadStaleMark = true;
                }
                else
                {
                    int take = Math.Min(wanted, reserve);
                    if (take > 0)
                    {
                        int left = reserve - take;
                        ctx.OreReserve[idx] = (ushort)left;

                        var oldState = (OreState)ctx.OreState[idx];
                        var newState = OreConfig.StateFor(left, ctx.OreReserveInitial[idx]);
                        stateChanged = newState != oldState;
                        if (stateChanged)
                            ctx.OreState[idx] = (byte)newState;

                        item = ItemRegistry.ForOreType(type);
                        extracted = take;
                        depleted = left <= 0;

                        // task-8: мутация — и в мир-состояние: островные клетки лежат
                        // в тех же чанках и сохраняются чанк-стором.
                        OreWorldState.SetReserve(x, y, (ushort)left);

                        if (depleted)
                            ClearDepletedCellLocked(ctx, idx, x, y);
                    }
                }
            }
        }

        // Работа снимается ВНЕ lock (у JobBroker свои блокировки).
        if (depleted)
        {
            JobBroker.Instance.UnregisterOreMine(x, y);
            Callable.From(() => OnOreDepleted?.Invoke((x, y))).CallDeferred();
        }
        else if (hadStaleMark)
        {
            JobBroker.Instance.UnregisterOreMine(x, y);
        }
        else if (extracted > 0 && stateChanged)
        {
            Callable.From(() => OnOreChanged?.Invoke((x, y))).CallDeferred();
        }

        return new OreExtractResult(item, extracted, depleted, stateChanged);
    }

    /// <summary>Снять метки/прогресс обоих миров под _lock; true — что-то сняли.</summary>
    private bool ForgetCellLocked(int x, int y)
    {
        bool removed = _markedOre.Remove((x, y));
        removed |= _worldMarkedOre.Remove((x, y));
        MineProgress.Clear(x, y);
        return removed;
    }

    /// <summary>Очистить выработанную клетку острова (тип/запас/state/метки).</summary>
    private void ClearDepletedCellLocked(SimulationContext ctx, int idx, int x, int y)
    {
        ctx.OreType[idx] = OreConfig.None;
        ctx.OreReserve[idx] = 0;
        ctx.OreState[idx] = 0;
        ForgetCellLocked(x, y);
    }
    /// <summary>
    /// Зарегистрировать СЛЕДУЮЩИЙ цикл добычи на клетке (метка жива, запас остался).
    /// Вызывается из commit завершившегося цикла; дедуп по клетке — HasJobAt.
    /// </summary>
    public void RequeueCycle(int x, int y)
    {
        lock (_lock)
        {
            if (!_markedOre.Contains((x, y)))
                return;
        }
        if (JobDispatcher.Instance.JobIndex.HasJobAt(x, y, JobTypeId.OreMining))
            return;
        JobBroker.Instance.RegisterOreMine(x, y);
    }

    // ------------------------------------------------------------------ //
    //  Проверки                                                           //
    // ------------------------------------------------------------------ //

    private static bool IsOreCell(int x, int y, SimulationContext ctx)
    {
        if (ctx?.OreType == null)
            return false;
        if ((uint)x >= (uint)ctx.MapWidth || (uint)y >= (uint)ctx.MapHeight)
            return false;
        return ctx.OreType[y * ctx.MapWidth + x] != OreConfig.None;
    }

    private static bool IsOreCellLocked(int x, int y, SimulationContext ctx) => IsOreCell(x, y, ctx);

    // ------------------------------------------------------------------ //
    //  Мир за кромкой (task-8)                                            //
    // ------------------------------------------------------------------ //

    /// <summary>Пометить руду мира под добычу (координаты — локальный холст).</summary>
    public void MarkOreWorld(int x, int y)
    {
        bool added;
        lock (_lock)
        {
            added = _worldMarkedOre.Add((x, y));
            if (added)
                MineProgress.Clear(x, y);
        }
        if (!added)
            return;
        JobBroker.Instance.RegisterOreMine(x, y);
        Callable.From(() => OnOreMarked?.Invoke((x, y))).CallDeferred();
    }

    public void UnmarkOreWorld(int x, int y)
    {
        lock (_lock)
        {
            if (!_worldMarkedOre.Remove((x, y)))
                return;
            MineProgress.Clear(x, y);
        }
        JobBroker.Instance.UnregisterOreMine(x, y);
        Callable.From(() => OnOreUnmarked?.Invoke((x, y))).CallDeferred();
    }

    public bool IsWorldOreMarked(int x, int y)
    {
        lock (_lock)
            return _worldMarkedOre.Contains((x, y));
    }

    public HashSet<(int X, int Y)> GetAllWorldMarkedOre()
    {
        lock (_lock)
            return new HashSet<(int X, int Y)>(_worldMarkedOre);
    }

    /// <summary>
    /// Снять порцию руды в мире: остаток пишется в <see cref="OreWorldState"/>
    /// (переживает выход через чанк-стор), 0 = клетка выработана.
    /// </summary>
    public OreExtractResult TryExtractWorld(int x, int y, int wanted, ulong seed, uint ver)
    {
        byte type = OreWorldField.GetOreTypeLocal(x, y, seed, ver);
        if (type == OreConfig.None)
        {
            lock (_lock)
            {
                if (_worldMarkedOre.Remove((x, y)))
                    MineProgress.Clear(x, y);
            }
            return default;
        }

        int take = OreWorldState.Extract(x, y, wanted, seed, ver, out ushort remaining);
        if (take <= 0)
            return default;

        ushort initial = OreWorldField.GetBaseReserveLocal(x, y, seed, ver);
        var oldState = OreConfig.StateFor(remaining + take, initial);
        var newState = OreConfig.StateFor(remaining, initial);
        bool stateChanged = oldState != newState;
        bool depleted = remaining == 0;

        if (depleted)
        {
            lock (_lock)
            {
                _worldMarkedOre.Remove((x, y));
                MineProgress.Clear(x, y);
            }
            JobBroker.Instance.UnregisterOreMine(x, y);
            Callable.From(() =>
            {
                OnOreDepleted?.Invoke((x, y));
                OnWorldOreDepleted?.Invoke((x, y));
            }).CallDeferred();
        }
        else if (stateChanged)
        {
            Callable.From(() => OnOreChanged?.Invoke((x, y))).CallDeferred();
        }

        return new OreExtractResult(ItemRegistry.ForOreType(type), take, depleted, stateChanged);
    }}