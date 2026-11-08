using Godot;
using Game.Core;
using Game.Simulation;
using System;
using System.Collections.Generic;

namespace Game.UI;

public partial class GroundItemRenderer : Node2D
{
    private const int MaxRenderedInstances = 16384;
    private const float DefaultItemSize = 44f;

    private readonly Dictionary<ItemId, (MultiMesh Mesh, MultiMeshInstance2D Instance, float[] Buffer)> _renderers = new();

    /// <summary>Тени мелочи (Syx ShadowBatch): общий рендерер ставит Main, тикает солнцем.</summary>
    public ItemShadowRenderer ItemShadows { get; set; }

    public override void _Ready()
    {
        ZIndex = 5;

        // Базовые типы создаём сразу (горячие: рубка/ферма/камень). Остальные —
        // лениво по первому появлению на земле (task-4: руда и 87 сырьевых id):
        // жадный MultiMesh на каждый из 91 типов — 16384 инстанса × буфер каждому.
        EnsureRenderer(ItemId.Log);
        EnsureRenderer(ItemId.Grain);
        EnsureRenderer(ItemId.Stone);
    }

    /// <summary>
    /// Создать MultiMesh-слой одного предмета. Текстура: камень режется из атласа
    /// 2×2, остальное — готовый спрайт дропа (<see cref="ItemTextureLoader"/>).
    /// Нет арта — слой не создаём (warning один раз внутри загрузчика).
    /// </summary>
    private void EnsureRenderer(ItemId id)
    {
        if (_renderers.ContainsKey(id))
            return;
        if (!ItemRegistry.TryGet(id, out ItemDefinition def))
            return;

        var quadMesh = new QuadMesh { Size = new Vector2(DefaultItemSize, DefaultItemSize) };
        // R2-02: AABB охватывает мир с запасом (-262144..+262144 px = ±4096 тайлов), чтобы предметы за кромкой 1024 не отсекались.
        var mapAabb = new Aabb(new Godot.Vector3(-262144f, -262144f, -1000f), new Godot.Vector3(524288f, 524288f, 2000f));

        Texture2D texture;
        if (id == ItemId.Stone)
        {
            // Камень лежит на земле первым квадрантом атласа (верх-лево).
            // Вариант россыпи уже не важен: это сыпучий ресурс, не декор.
            var atlas = ResourceLoader.Load<Texture2D>(def.TextureUid);
            texture = atlas == null ? null : new AtlasTexture
            {
                Atlas = atlas,
                Region = new Rect2(0, 0, MapRenderer.TileSizePx, MapRenderer.TileSizePx)
            };
        }
        else
        {
            texture = ItemTextureLoader.Load(def);
        }
        if (texture == null)
            return;

        var multiMesh = new MultiMesh
        {
            Mesh = quadMesh,
            TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,
            UseColors = false,
            UseCustomData = false,
            InstanceCount = MaxRenderedInstances,
            VisibleInstanceCount = 0,
            CustomAabb = mapAabb
        };

        var instance = new MultiMeshInstance2D
        {
            Name = $"GroundItems_{def.Name}",
            Multimesh = multiMesh,
            Texture = texture
        };
        AddChild(instance);

        _renderers[id] = (multiMesh, instance, new float[MaxRenderedInstances * 8]);
    }

    public override void _Process(double delta)
    {
        if (!GroundItemManager.Instance.SnapshotQueue.TryDequeue(out var latestSnapshot))
            return;

        // E62: сколько снапшотов выброшено — «рендер не успевает».
        int dropped = 0;
        while (GroundItemManager.Instance.SnapshotQueue.TryDequeue(out var newerSnapshot))
        {
            latestSnapshot = newerSnapshot;
            dropped++;
        }
        if (latestSnapshot.PositionsByItem == null || latestSnapshot.Counts == null)
            return;

        // task-4: новые типы (руда/сырьё) получают свой слой при первом появлении.
        for (int t = 1; t < latestSnapshot.Counts.Length; t++)
        {
            if (latestSnapshot.Counts[t] > 0 && !_renderers.ContainsKey((ItemId)t))
                EnsureRenderer((ItemId)t);
        }

        // C-06: Stopwatch — ссылочный тип, StartNew() аллоцировал объект в _Process
        // каждый кадр. Замеряем по timestamp (struct-long), без аллокации; и только
        // после null-гейта снапшота.
        long swStart = System.Diagnostics.Stopwatch.GetTimestamp();

        ItemShadows?.ClearSourceSpots(ItemShadowRenderer.ShadowSource.Ground);
        foreach (var (id, (mesh, _, buffer)) in _renderers)
        {
            int itemIdx = (int)id;
            int count = (itemIdx < latestSnapshot.Counts.Length)
                ? Math.Min(latestSnapshot.Counts[itemIdx], MaxRenderedInstances)
                : 0;

            mesh.VisibleInstanceCount = count;

            if (count > 0)
            {
                var positions = latestSnapshot.PositionsByItem[itemIdx];
                for (int i = 0; i < count; i++)
                {
                    var pos = positions[i];
                    int idx = i * 8;

                    buffer[idx + 0] = 1.0f;
                    buffer[idx + 1] = 0.0f;
                    buffer[idx + 2] = 0.0f;
                    buffer[idx + 3] = pos.X;

                    buffer[idx + 4] = 0.0f;
                    buffer[idx + 5] = 1.0f;
                    buffer[idx + 6] = 0.0f;
                    buffer[idx + 7] = pos.Y;
                }

                mesh.Buffer = buffer;

                ItemShadows?.AddSourceSpots(ItemShadowRenderer.ShadowSource.Ground, positions, count, DefaultItemSize);
            }
        }
        SimEvents.Record("render", "GroundItemRenderer",
            (float)System.Diagnostics.Stopwatch.GetElapsedTime(swStart).TotalMilliseconds);
    }
}