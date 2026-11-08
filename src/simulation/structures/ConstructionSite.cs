using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.Simulation;

public enum ConstructionStage : byte
{
    Clearance = 0,
    Supplying = 1,
    WaitingAll = 2,
    Assembling = 3,
    Completed = 4
}

public sealed class ConstructionSite
{
    public int SiteId { get; set; }
    public int X { get; }
    public int Y { get; }
    public StructureDefinition Definition { get; }
    public ConstructionStage Stage { get; set; } = ConstructionStage.Clearance;

    // Препятствия
    public bool NeedTree { get; set; }
    public bool NeedStone { get; set; }
    public bool NeedHaul { get; set; }

    // Доставка материалов
    // Состояние материалов читается/пишется из нескольких sim-потоков
    // (Commit доставки параллелен добавлению другого носильщика) — короткий lock
    // вокруг только словарей; Stage/WorkProgress — как раньше, без него.
    private readonly object _matLock = new();
    private readonly Dictionary<ItemId, int> _delivered = new();
    private readonly Dictionary<ItemId, int> _required = new();

    // Прогресс монтажа
    public float WorkProgress { get; set; }
    public int WorkProgressStage { get; set; } = -1;

    public ConstructionSite(int x, int y, StructureDefinition definition)
    {
        X = x;
        Y = y;
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));

        foreach (var req in definition.RequiredMaterials)
        {
            _required[req.Item] = req.Count;
            _delivered[req.Item] = 0;
        }
    }

    public int GetDelivered(ItemId item)
    {
        lock (_matLock)
            return _delivered.TryGetValue(item, out int c) ? c : 0;
    }

    public int GetRequired(ItemId item)
    {
        lock (_matLock)
            return _required.TryGetValue(item, out int c) ? c : 0;
    }

    public int TotalDelivered
    {
        get
        {
            lock (_matLock)
            {
                int sum = 0;
                foreach (var kvp in _delivered) sum += kvp.Value;
                return sum;
            }
        }
    }

    public int TotalRequired
    {
        get
        {
            lock (_matLock)
            {
                int sum = 0;
                foreach (var kvp in _required) sum += kvp.Value;
                return sum;
            }
        }
    }

    public bool IsFullySupplied
    {
        get
        {
            lock (_matLock)
            {
                if (_required.Count == 0)
                    return true;
                foreach (var (item, reqCount) in _required)
                {
                    if (!_delivered.TryGetValue(item, out int delCount) || delCount < reqCount)
                        return false;
                }
                return true;
            }
        }
    }

    /// <summary>
    /// Следующий материал, который ещё не довезён (порядок = порядок
    /// RequiredMaterials; детерминированно). false — сайт снабжён ПОЛНОСТЬЮ.
    /// Один активный delivery-джоб на клетку, поэтому материалы возятся
    /// последовательно: этот метод выбирает, что везти следующим.
    /// </summary>
    public bool TryGetNextNeededMaterial(out ItemId item, out int remaining)
    {
        IReadOnlyList<(ItemId Item, int Count)> mats = Definition.RequiredMaterials;
        lock (_matLock)
        {
            for (int i = 0; i < mats.Count; i++)
            {
                (ItemId reqItem, int reqCount) = mats[i];
                int delivered = _delivered.TryGetValue(reqItem, out int d) ? d : 0;
                if (delivered < reqCount)
                {
                    item = reqItem;
                    remaining = reqCount - delivered;
                    return true;
                }
            }
        }
        item = ItemId.None;
        remaining = 0;
        return false;
    }

    public int AddDelivered(ItemId item, int count)
    {
        if (count <= 0) return 0;
        lock (_matLock)
        {
            int req = _required.TryGetValue(item, out int r) ? r : 0;
            int cur = _delivered.TryGetValue(item, out int d) ? d : 0;
            int canAccept = Math.Max(0, req - cur);
            int accepted = Math.Min(canAccept, count);
            if (accepted > 0)
                _delivered[item] = cur + accepted;
            return accepted;
        }
    }

    public bool HasObstacles => NeedTree || NeedStone || NeedHaul;

    public Dictionary<ItemId, int> GetAllDelivered()
    {
        lock (_matLock)
            return new Dictionary<ItemId, int>(_delivered);
    }
}
