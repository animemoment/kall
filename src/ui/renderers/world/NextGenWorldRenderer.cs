#nullable enable
using Godot;
using System;
using System.Collections.Generic;
using Game.UI.Renderers.Culling;

namespace Game.UI.Renderers.World;

/// <summary>
/// Next-Gen Ultra-Performance World Renderer для Godot 4 (.NET 8 C#).
/// Спроектирован под карты 512x512 и 1024x1024 с максимальным FPS и Zero-GC.
/// Ключевые принципы:
/// 1. Чанковая архитектура 32x32 тайла (2048 x 2048 px).
/// 2. Единый процедурный ArrayMesh на каждый чанк, объединяющий тайлы в 1 Draw Call.
/// 3. Меш чанка пересобирается ИСКЛЮЧИТЕЛЬНО при IsDirty == true (копание, постройка).
/// 4. Быстрое AABB отсечение чанков вьюпортом камеры.
/// </summary>
public partial class NextGenWorldRenderer : Node2D
{
    private WorldGrid? _grid;
    private MeshInstance2D[] _chunkNodes = Array.Empty<MeshInstance2D>();
    private ArrayMesh[] _chunkMeshes = Array.Empty<ArrayMesh>();
    private ShaderMaterial? _material;

    private int[] _visibleChunkIndices = Array.Empty<int>();
    private bool[] _currentlyVisible = Array.Empty<bool>();

    public WorldGrid? Grid => _grid;

    public void Initialize(
        int mapWidthTiles,
        int mapHeightTiles,
        Texture2D? atlasTexture = null,
        Shader? customShader = null)
    {
        _grid = new WorldGrid(mapWidthTiles, mapHeightTiles);
        int totalChunks = _grid.TotalChunks;

        _chunkNodes = new MeshInstance2D[totalChunks];
        _chunkMeshes = new ArrayMesh[totalChunks];
        _visibleChunkIndices = new int[totalChunks];
        _currentlyVisible = new bool[totalChunks];

        if (customShader != null)
        {
            _material = new ShaderMaterial { Shader = customShader };
            if (atlasTexture != null)
            {
                _material.SetShaderParameter("world_atlas", atlasTexture);
            }
        }

        for (int i = 0; i < totalChunks; i++)
        {
            var chunk = _grid.Chunks[i];
            var mesh = new ArrayMesh();
            _chunkMeshes[i] = mesh;

            var node = new MeshInstance2D
            {
                Name = $"Chunk_{chunk.ChunkX}_{chunk.ChunkY}",
                Mesh = mesh,
                Material = _material,
                Visible = false
            };

            _chunkNodes[i] = node;
            AddChild(node);
        }
    }

    /// <summary>
    /// Цикл обновления рендеринга мира кадра. Zero-GC.
    /// Отсекает невидимые чанки и пересобирает меши только «грязных» видимых чанков.
    /// </summary>
    public void RenderFrame(Rect2D cameraViewRect)
    {
        if (_grid == null)
            return;

        WorldFrustumCuller.CullVisibleChunks(
            cameraViewRect,
            _grid,
            _visibleChunkIndices.AsSpan(),
            out int visibleCount);

        Array.Clear(_currentlyVisible, 0, _grid.TotalChunks);

        for (int i = 0; i < visibleCount; i++)
        {
            int chunkIdx = _visibleChunkIndices[i];
            _currentlyVisible[chunkIdx] = true;

            var chunk = _grid.Chunks[chunkIdx];
            var node = _chunkNodes[chunkIdx];
            var mesh = _chunkMeshes[chunkIdx];

            if (chunk.IsDirty)
            {
                chunk.BuildMeshBuffers();
                UpdateGodotArrayMesh(mesh, chunk);
            }

            if (!node.Visible)
            {
                node.Visible = true;
            }
        }

        for (int i = 0; i < _grid.TotalChunks; i++)
        {
            if (!_currentlyVisible[i] && _chunkNodes[i].Visible)
            {
                _chunkNodes[i].Visible = false;
            }
        }
    }

    private static void UpdateGodotArrayMesh(ArrayMesh mesh, WorldChunk chunk)
    {
        mesh.ClearSurfaces();

        if (chunk.VertexCount == 0)
            return;

        var verts = chunk.GetVertices();
        var uvs = chunk.GetUVs();
        var colors = chunk.GetColors();
        var indices = chunk.GetIndices();

        var godotVerts = new Godot.Vector2[chunk.VertexCount];
        var godotUvs = new Godot.Vector2[chunk.VertexCount];
        var godotColors = new Godot.Color[chunk.VertexCount];
        var godotIndices = new int[chunk.IndexCount];

        for (int i = 0; i < chunk.VertexCount; i++)
        {
            godotVerts[i] = new Godot.Vector2(verts[i].X, verts[i].Y);
            godotUvs[i] = new Godot.Vector2(uvs[i].X, uvs[i].Y);
            godotColors[i] = new Godot.Color(colors[i].X, colors[i].Y, colors[i].Z, colors[i].W);
        }

        indices.CopyTo(godotIndices);

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = godotVerts;
        arrays[(int)Mesh.ArrayType.TexUV] = godotUvs;
        arrays[(int)Mesh.ArrayType.Color] = godotColors;
        arrays[(int)Mesh.ArrayType.Index] = godotIndices;

        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
    }
}
