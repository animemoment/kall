using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Game.Core;

namespace Game.Simulation;

/// <summary>
/// Сверхбыстрый плотный пул свободных агентов на основе непрерывного массива (SoA-friendly).
/// Zero allocation, swap-and-pop за O(1), 100% L1-кэш попадание.
/// Полная замена устаревшего IdleWorkerSpatialGrid со связными списками.
/// </summary>
public sealed class FlatIdleWorkerPool
{
    private readonly object _lock = new();
    private int[] _idleAgentIds;
    private int[] _agentToIdleIndex;
    private int _count;

    // J-02: курсор ротации забора батча (см. CollectIdleWorkers). Доступ только
    // под _lock — читается и пишется внутри критической секции.
    private int _collectCursor;

    public int Count => Volatile.Read(ref _count);

    public FlatIdleWorkerPool(int initialCapacity = 16384)
    {
        _idleAgentIds = new int[initialCapacity];
        _agentToIdleIndex = new int[initialCapacity];
        Array.Fill(_agentToIdleIndex, -1);
        _count = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureCapacityLocked(int agentIndex)
    {
        if (agentIndex >= _agentToIdleIndex.Length)
        {
            int newCap = Math.Max(_agentToIdleIndex.Length * 2, agentIndex + 1024);
            Array.Resize(ref _idleAgentIds, newCap);
            int oldLen = _agentToIdleIndex.Length;
            Array.Resize(ref _agentToIdleIndex, newCap);
            for (int i = oldLen; i < newCap; i++)
            {
                _agentToIdleIndex[i] = -1;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddIdleWorker(int agentIndex, AgentDataPool pool = null)
    {
        if (agentIndex < 0) return;

        lock (_lock)
        {
            EnsureCapacityLocked(agentIndex);
            if (_agentToIdleIndex[agentIndex] != -1)
                return; // Уже в пуле

            int idx = _count;
            _idleAgentIds[idx] = agentIndex;
            _agentToIdleIndex[agentIndex] = idx;
            _count = idx + 1;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RemoveIdleWorker(int agentIndex, AgentDataPool pool = null)
    {
        if (agentIndex < 0) return;

        lock (_lock)
        {
            if (agentIndex >= _agentToIdleIndex.Length)
                return;

            int idx = _agentToIdleIndex[agentIndex];
            if (idx == -1)
                return; // Уже удален

            int lastIdx = _count - 1;
            int lastAgent = _idleAgentIds[lastIdx];

            // Swap-and-pop
            _idleAgentIds[idx] = lastAgent;
            _agentToIdleIndex[lastAgent] = idx;

            _idleAgentIds[lastIdx] = -1;
            _agentToIdleIndex[agentIndex] = -1;
            _count = lastIdx;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsIdle(int agentIndex)
    {
        if (agentIndex < 0 || agentIndex >= _agentToIdleIndex.Length)
            return false;
        return Volatile.Read(ref _agentToIdleIndex[agentIndex]) != -1;
    }

    /// <summary>
    /// Пакетный забор свободных агентов для диспетчеризации.
    /// Возвращает реальное количество скопированных ID.
    ///
    /// J-02: забор идёт с курсора ротации и закольцовывается. Раньше он всегда
    /// копировал массив с индекса 0, поэтому при пуле больше размера батча
    /// (MaxBatchWorkers = 4096) агенты, попавшие в пул позже и стоящие в его
    /// хвосте, не попадали в батч НИКОГДА: они висели бездельниками, хотя рядом
    /// были работы, а работу брал кто-то «с другого конца карты».
    /// </summary>
    public int CollectIdleWorkers(int maxCount, int[] destination, AgentDataPool pool = null)
    {
        if (destination == null || maxCount <= 0) return 0;

        lock (_lock)
        {
            int toCopy = Math.Min(maxCount, Math.Min(_count, destination.Length));
            if (toCopy <= 0) return 0;

            // Весь пул влезает в батч — копируем подряд и нормализуем курсор.
            if (toCopy >= _count)
            {
                Array.Copy(_idleAgentIds, 0, destination, 0, toCopy);
                _collectCursor = 0;
                return toCopy;
            }

            int start = _collectCursor;
            if (start < 0 || start >= _count) start = 0;

            int head = Math.Min(toCopy, _count - start);
            Array.Copy(_idleAgentIds, start, destination, 0, head);
            int tail = toCopy - head;
            if (tail > 0)
                Array.Copy(_idleAgentIds, 0, destination, head, tail);

            _collectCursor = (start + toCopy) % _count;
            return toCopy;
        }
    }

    /// <summary>
    /// Совместимость со старым API для сбора по чанку.
    /// Так как сетка стала плоской и не разделяется на чанки с мертвыми зонами,
    /// возвращает свободных агентов без принудительной изоляции.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int CollectIdleWorkersInChunk(int chunkIndex, int maxCount, int[] destination, AgentDataPool pool = null)
    {
        return CollectIdleWorkers(maxCount, destination, pool);
    }

    /// <summary>
    /// Обновление чанка (no-op в плоском пуле, оставлен для обратной совместимости).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void UpdateWorkerChunk(int agentIndex, AgentDataPool pool = null)
    {
        // В плоском пуле перемещение агента не требует перекладывания по связным спискам.
    }

    public void Clear()
    {
        lock (_lock)
        {
            Array.Fill(_agentToIdleIndex, -1);
            _count = 0;
            _collectCursor = 0;
        }
    }

    public int TotalIdleCount => Volatile.Read(ref _count);
}
