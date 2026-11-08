#nullable enable
using System;
using System.Runtime.CompilerServices;

namespace Game.UI.Renderers.World;

/// <summary>
/// Сетка чанков мира колонии.
/// Поддерживает гигантские карты: 512x512 (256 чанков) и 1024x1024 (1024 чанка).
/// Обеспечивает O(1) доступ к тайлам, точечные мутации с автоматической пометкой IsDirty,
/// и нулевые аллокации в рантайме.
/// </summary>
public sealed class WorldGrid
{
    public readonly int MapWidthTiles;
    public readonly int MapHeightTiles;
    public readonly int ChunksX;
    public readonly int ChunksY;
    public readonly int TotalChunks;

    public readonly WorldChunk[] Chunks;

    public WorldGrid(int mapWidthTiles = 512, int mapHeightTiles = 512)
    {
        MapWidthTiles = mapWidthTiles;
        MapHeightTiles = mapHeightTiles;

        ChunksX = (mapWidthTiles + WorldChunk.ChunkSizeTiles - 1) / WorldChunk.ChunkSizeTiles;
        ChunksY = (mapHeightTiles + WorldChunk.ChunkSizeTiles - 1) / WorldChunk.ChunkSizeTiles;
        TotalChunks = ChunksX * ChunksY;

        Chunks = new WorldChunk[TotalChunks];
        for (int cy = 0; cy < ChunksY; cy++)
        {
            for (int cx = 0; cx < ChunksX; cx++)
            {
                Chunks[cy * ChunksX + cx] = new WorldChunk(cx, cy);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public WorldChunk GetChunk(int cx, int cy)
    {
        return Chunks[cy * ChunksX + cx];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public WorldChunk? GetChunkForTile(int tileX, int tileY)
    {
        int cx = tileX / WorldChunk.ChunkSizeTiles;
        int cy = tileY / WorldChunk.ChunkSizeTiles;

        if ((uint)cx >= (uint)ChunksX || (uint)cy >= (uint)ChunksY)
            return null;

        return Chunks[cy * ChunksX + cx];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref TileRenderData GetTile(int tileX, int tileY)
    {
        int cx = tileX / WorldChunk.ChunkSizeTiles;
        int cy = tileY / WorldChunk.ChunkSizeTiles;
        int lx = tileX % WorldChunk.ChunkSizeTiles;
        int ly = tileY % WorldChunk.ChunkSizeTiles;

        return ref Chunks[cy * ChunksX + cx].GetTile(lx, ly);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetTile(int tileX, int tileY, in TileRenderData tile)
    {
        int cx = tileX / WorldChunk.ChunkSizeTiles;
        int cy = tileY / WorldChunk.ChunkSizeTiles;
        int lx = tileX % WorldChunk.ChunkSizeTiles;
        int ly = tileY % WorldChunk.ChunkSizeTiles;

        if ((uint)cx < (uint)ChunksX && (uint)cy < (uint)ChunksY)
        {
            Chunks[cy * ChunksX + cx].SetTile(lx, ly, in tile);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DigWall(int tileX, int tileY)
    {
        int cx = tileX / WorldChunk.ChunkSizeTiles;
        int cy = tileY / WorldChunk.ChunkSizeTiles;
        int lx = tileX % WorldChunk.ChunkSizeTiles;
        int ly = tileY % WorldChunk.ChunkSizeTiles;

        if ((uint)cx < (uint)ChunksX && (uint)cy < (uint)ChunksY)
        {
            ref var tile = ref Chunks[cy * ChunksX + cx].GetTile(lx, ly);
            tile.WallId = 0;
            Chunks[cy * ChunksX + cx].IsDirty = true;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void BuildFloor(int tileX, int tileY, ushort floorId)
    {
        int cx = tileX / WorldChunk.ChunkSizeTiles;
        int cy = tileY / WorldChunk.ChunkSizeTiles;
        int lx = tileX % WorldChunk.ChunkSizeTiles;
        int ly = tileY % WorldChunk.ChunkSizeTiles;

        if ((uint)cx < (uint)ChunksX && (uint)cy < (uint)ChunksY)
        {
            ref var tile = ref Chunks[cy * ChunksX + cx].GetTile(lx, ly);
            tile.FloorId = floorId;
            Chunks[cy * ChunksX + cx].IsDirty = true;
        }
    }
}
