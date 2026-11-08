using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Game.UI.Renderers.Culling;

/// <summary>
/// Иерархическая пространственная сетка агентов для отсечения невидимой геометрии (Frustum Culling).
/// Разбивает мир на чанки 32x32 тайла (2048x2048 px).
/// Позволяет отсекать тысячи агентов вне экрана за несколько микросекунд
/// через быструю проверку AABB чанков вместо 10 000 - 50 000 поштучных тестов.
/// Полностью Zero-GC: использует плоские предвыделенные массивы связанных списков.
/// </summary>
public sealed class AgentRenderSpatialGrid
{
    public const float DefaultTileSize = 64f;
    public const int DefaultChunkSizeTiles = 32;
    public const float DefaultChunkSizePx = DefaultTileSize * DefaultChunkSizeTiles; // 2048 px

    public readonly int GridWidthChunks;
    public readonly int GridHeightChunks;
    public readonly int TotalChunks;
    public readonly float ChunkSizePx;
    public readonly float InvChunkSizePx;
    public readonly int MaxAgents;

    private readonly int[] _chunkHead;
    private readonly int[] _agentNext;
    private readonly int[] _chunkCount;
    private readonly int[] _agentChunk;
    private readonly Rect2D[] _chunkBounds;

    public AgentRenderSpatialGrid(int gridWidthChunks, int gridHeightChunks, int maxAgents, float chunkSizePx = DefaultChunkSizePx)
    {
        GridWidthChunks = gridWidthChunks;
        GridHeightChunks = gridHeightChunks;
        TotalChunks = gridWidthChunks * gridHeightChunks;
        ChunkSizePx = chunkSizePx;
        InvChunkSizePx = 1.0f / chunkSizePx;
        MaxAgents = maxAgents;

        _chunkHead = new int[TotalChunks];
        _agentNext = new int[maxAgents];
        _chunkCount = new int[TotalChunks];
        _agentChunk = new int[maxAgents];
        _chunkBounds = new Rect2D[TotalChunks];

        Array.Fill(_chunkHead, -1);
        Array.Fill(_agentNext, -1);
        Array.Fill(_agentChunk, -1);

        for (int cy = 0; cy < GridHeightChunks; cy++)
        {
            float minY = cy * ChunkSizePx;
            float maxY = minY + ChunkSizePx;
            for (int cx = 0; cx < GridWidthChunks; cx++)
            {
                float minX = cx * ChunkSizePx;
                float maxX = minX + ChunkSizePx;
                int idx = cy * GridWidthChunks + cx;
                _chunkBounds[idx] = new Rect2D(minX, minY, maxX, maxY);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetChunkIndexClamped(Vector2 pos)
    {
        int cx = Math.Clamp((int)(pos.X * InvChunkSizePx), 0, GridWidthChunks - 1);
        int cy = Math.Clamp((int)(pos.Y * InvChunkSizePx), 0, GridHeightChunks - 1);
        return cy * GridWidthChunks + cx;
    }

    /// <summary>
    /// Полный сброс и заполнение сетки позициями агентов за один проход O(N). Zero-GC.
    /// </summary>
    public void Rebuild(ReadOnlySpan<Vector2> positions)
    {
        Array.Fill(_chunkHead, -1);
        Array.Fill(_chunkCount, 0);

        int count = positions.Length;
        for (int i = 0; i < count; i++)
        {
            int chunkIdx = GetChunkIndexClamped(positions[i]);
            _agentChunk[i] = chunkIdx;
            _agentNext[i] = _chunkHead[chunkIdx];
            _chunkHead[chunkIdx] = i;
            _chunkCount[chunkIdx]++;
        }
    }

    /// <summary>
    /// Иерархический Frustum Culling агентов против прямоугольника видимости камеры.
    /// 1. Вычисляет диапазон видимых чанков [minCX..maxCX, minCY..maxCY] за O(1).
    /// 2. Для чанков целиком внутри экрана: сбрасывает всех агентов без проверок координат!
    /// 3. Для пограничных чанков: проверяет координаты только агентов пограничного чанка.
    /// 4. Чанки вне экрана пропускаются мгновенно целыми пачками.
    /// Zero-GC: результат пишется в переданный Span outVisibleIndices.
    /// </summary>
    public void QueryVisibleAgents(
        in Rect2D viewRect,
        ReadOnlySpan<Vector2> positions,
        Span<int> outVisibleIndices,
        out int visibleCount)
    {
        visibleCount = 0;

        int minCx = Math.Clamp((int)(viewRect.MinX * InvChunkSizePx), 0, GridWidthChunks - 1);
        int maxCx = Math.Clamp((int)(viewRect.MaxX * InvChunkSizePx), 0, GridWidthChunks - 1);
        int minCy = Math.Clamp((int)(viewRect.MinY * InvChunkSizePx), 0, GridHeightChunks - 1);
        int maxCy = Math.Clamp((int)(viewRect.MaxY * InvChunkSizePx), 0, GridHeightChunks - 1);

        for (int cy = minCy; cy <= maxCy; cy++)
        {
            int rowOffset = cy * GridWidthChunks;
            for (int cx = minCx; cx <= maxCx; cx++)
            {
                int chunkIdx = rowOffset + cx;
                int countInChunk = _chunkCount[chunkIdx];
                if (countInChunk == 0)
                    continue;

                ref readonly Rect2D chunkBox = ref _chunkBounds[chunkIdx];

                // Случай 1: Чанк полностью внутри области видимости камеры
                if (viewRect.Contains(in chunkBox))
                {
                    int curr = _chunkHead[chunkIdx];
                    while (curr != -1)
                    {
                        if (visibleCount < outVisibleIndices.Length)
                            outVisibleIndices[visibleCount++] = curr;
                        curr = _agentNext[curr];
                    }
                }
                // Случай 2: Чанк пересекает границу вьюпорта — проверяем поштучно только его агентов
                else
                {
                    int curr = _chunkHead[chunkIdx];
                    while (curr != -1)
                    {
                        if (viewRect.Contains(positions[curr]))
                        {
                            if (visibleCount < outVisibleIndices.Length)
                                outVisibleIndices[visibleCount++] = curr;
                        }
                        curr = _agentNext[curr];
                    }
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref readonly Rect2D GetChunkBounds(int chunkIdx) => ref _chunkBounds[chunkIdx];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetChunkAgentCount(int chunkIdx) => _chunkCount[chunkIdx];
}
