using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Game.UI.Renderers.Culling;

namespace Game.UI.Renderers.World;

/// <summary>
/// Чанк игрового мира размером 32x32 тайла (2048 x 2048 пикселей).
/// Хранит данные 1024 тайлов и генерирует единый процедурный меш со сшитыми вершинами.
/// Меш пересобирается ИСКЛЮЧИТЕЛЬНО при IsDirty == true.
/// Zero-GC: использует общие предвыделенные scratch-буферы для построения геометрии.
/// </summary>
public sealed class WorldChunk
{
    public const int ChunkSizeTiles = 32;
    public const int TilesPerChunk = ChunkSizeTiles * ChunkSizeTiles; // 1024
    public const float TileSizePx = 64f;
    public const float ChunkSizePx = ChunkSizeTiles * TileSizePx; // 2048f

    public readonly int ChunkX;
    public readonly int ChunkY;
    public readonly Rect2D Bounds;

    public readonly TileRenderData[] Tiles;
    public bool IsDirty { get; set; } = true;

    public int VertexCount { get; private set; }
    public int IndexCount { get; private set; }

    private static readonly Vector2[] ScratchVertices = new Vector2[TilesPerChunk * 4];
    private static readonly Vector2[] ScratchUVs = new Vector2[TilesPerChunk * 4];
    private static readonly Vector4[] ScratchColors = new Vector4[TilesPerChunk * 4];
    private static readonly int[] ScratchIndices = new int[TilesPerChunk * 6];

    public WorldChunk(int chunkX, int chunkY)
    {
        ChunkX = chunkX;
        ChunkY = chunkY;
        Tiles = new TileRenderData[TilesPerChunk];

        float minX = chunkX * ChunkSizePx;
        float minY = chunkY * ChunkSizePx;
        Bounds = new Rect2D(minX, minY, minX + ChunkSizePx, minY + ChunkSizePx);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref TileRenderData GetTile(int localX, int localY)
    {
        return ref Tiles[localY * ChunkSizeTiles + localX];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetTile(int localX, int localY, in TileRenderData tile)
    {
        Tiles[localY * ChunkSizeTiles + localX] = tile;
        IsDirty = true;
    }

    public void BuildMeshBuffers(int atlasCols = 16, int atlasRows = 16)
    {
        if (!IsDirty)
            return;

        int vertIdx = 0;
        int indIdx = 0;

        float invAtlasW = 1.0f / atlasCols;
        float invAtlasH = 1.0f / atlasRows;

        float originX = Bounds.MinX;
        float originY = Bounds.MinY;

        for (int ty = 0; ty < ChunkSizeTiles; ty++)
        {
            float py = originY + ty * TileSizePx;
            int rowOffset = ty * ChunkSizeTiles;

            for (int tx = 0; tx < ChunkSizeTiles; tx++)
            {
                ref readonly TileRenderData tile = ref Tiles[rowOffset + tx];

                if (tile.FloorId == 0 && tile.WallId == 0)
                    continue;

                float px = originX + tx * TileSizePx;
                ushort spriteId = tile.WallId > 0 ? tile.WallId : tile.FloorId;

                int tileCol = spriteId % atlasCols;
                int tileRow = spriteId / atlasCols;

                float u0 = tileCol * invAtlasW;
                float v0 = tileRow * invAtlasH;
                float u1 = u0 + invAtlasW;
                float v1 = v0 + invAtlasH;

                float light = tile.LightLevel * (1.0f / 255.0f);
                Vector4 color = new Vector4(light, light, light, 1.0f);

                int baseVert = vertIdx;

                ScratchVertices[vertIdx] = new Vector2(px, py);
                ScratchUVs[vertIdx] = new Vector2(u0, v0);
                ScratchColors[vertIdx] = color;
                vertIdx++;

                ScratchVertices[vertIdx] = new Vector2(px + TileSizePx, py);
                ScratchUVs[vertIdx] = new Vector2(u1, v0);
                ScratchColors[vertIdx] = color;
                vertIdx++;

                ScratchVertices[vertIdx] = new Vector2(px + TileSizePx, py + TileSizePx);
                ScratchUVs[vertIdx] = new Vector2(u1, v1);
                ScratchColors[vertIdx] = color;
                vertIdx++;

                ScratchVertices[vertIdx] = new Vector2(px, py + TileSizePx);
                ScratchUVs[vertIdx] = new Vector2(u0, v1);
                ScratchColors[vertIdx] = color;
                vertIdx++;

                ScratchIndices[indIdx++] = baseVert + 0;
                ScratchIndices[indIdx++] = baseVert + 1;
                ScratchIndices[indIdx++] = baseVert + 2;

                ScratchIndices[indIdx++] = baseVert + 0;
                ScratchIndices[indIdx++] = baseVert + 2;
                ScratchIndices[indIdx++] = baseVert + 3;
            }
        }

        VertexCount = vertIdx;
        IndexCount = indIdx;
        IsDirty = false;
    }

    public ReadOnlySpan<Vector2> GetVertices() => new ReadOnlySpan<Vector2>(ScratchVertices, 0, VertexCount);
    public ReadOnlySpan<Vector2> GetUVs() => new ReadOnlySpan<Vector2>(ScratchUVs, 0, VertexCount);
    public ReadOnlySpan<Vector4> GetColors() => new ReadOnlySpan<Vector4>(ScratchColors, 0, VertexCount);
    public ReadOnlySpan<int> GetIndices() => new ReadOnlySpan<int>(ScratchIndices, 0, IndexCount);
}
