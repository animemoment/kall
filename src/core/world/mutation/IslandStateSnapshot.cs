using System;
using System.Collections.Generic;
using Godot;
using Game.Core.WorldStreaming.Integration;
using Game.Simulation;

namespace Game.Core.WorldStreaming.Mutation;

// ============================================================================
// §119 P7б (task-17 п.4-5): SAVE-TIME REFRESH — непосредственно перед записью
// островных дельт пристраивает АКТУАЛЬНОЕ СОСТОЯНИЕ доменов в payload'ы меток.
//
// Зеркало (IslandMutationMirror) держит НАБОРИ меток через события; payload'ы
// оно не пишет. Здесь, на выходе из колонии, каждой существующей метке
// пристраивается снапшот состояния:
//   Stockpile      → item/count слота (null = пустой слот),
//   Crop           → stage/growthTimer/zoneId + ТИП культуры (null = грядка без культуры),
//   WallPlan/BuildingPlan → stage + доставленные материалы + WorkProgress,
//   CutTreePlan/MinedStonePlan → fraction CellProgressStore,
//   GroundItems    → СИНХРОНИЗАЦИЯ НАБОРА: старые метки снимаются, свежий
//                    снапшот куч применяется заново (кучи меняются без событий).
//
// Правки только через TrySetPayload (метки не создаются/не снимаются, КРОМЕ
// синхронизации GroundItems). Вызывается ТОЛЬКО из пути выхода (Main._ExitTree).
// ============================================================================

public static class IslandStateSnapshot
{
    /// <summary>
    /// Обновить payload'ы всех меток зоны актуальным состоянием доменов.
    /// <paramref name="writeLog"/> = false — для headless-стендов без запущенного
    /// движка (GD.Print/GD.PushWarning вне движка роняют процесс); на результат
    /// и запись в стор не влияет.
    /// </summary>
    public static (int PayloadsSet, int GroundApplied, int GroundRemoved) RefreshPayloads(
        IslandOverrideStore store, long originX, long originY, bool writeLog = true)
    {
        if (store == null)
            return (0, 0, 0);

        var cells = new List<(byte Kind, long CellX, long CellY)>(store.Count);
        // ST-37 / S-09: перечисляем все дельты стора без обрезки по ActiveArea.Size
        store.Enumerate(long.MinValue, long.MinValue, long.MaxValue, long.MaxValue, 0, cells);

        int set = 0;
        int groundApplied = 0;
        int groundRemoved = 0;

        // --- Снапшоты доменов (один lock на менеджер). ---
        var storageSnap = new Dictionary<(int, int), (ItemId, int)>();
        foreach (var s in StockpileManager.Instance.GetStorageSnapshot())
            storageSnap[(s.X, s.Y)] = (s.Item, s.Count);

        var cropsSnap = new Dictionary<(int, int), (int Stage, float Timer, int ZoneId, Game.Core.CropType Type)>();
        foreach (CropData c in CropGrowthManager.Instance.GetCropsSnapshot())
            cropsSnap[(c.X, c.Y)] = (c.Stage, c.GrowthTimer, c.ZoneId, c.Type);

        foreach ((byte kind, long wx, long wy) in cells)
        {
            int x = checked((int)(wx - originX));
            int y = checked((int)(wy - originY));
            // ST-37: не отсекаем по ActiveArea.Size
            try
            {
                switch (kind)
                {
                    case WorldDeltaKind.Stockpile:
                    {
                        byte[] payload = null;
                        if (storageSnap.TryGetValue((x, y), out var e))
                            payload = IslandPayload.WriteStockpile(e.Item1, e.Item2);
                        if (store.TrySetPayload(kind, wx, wy, payload))
                            set++;
                        break;
                    }
                    case WorldDeltaKind.Crop:
                    {
                        byte[] payload = null;
                        if (cropsSnap.TryGetValue((x, y), out var c))
                            // Схема v2: тип культуры едет вместе со стадией/таймером.
                            payload = IslandPayload.WriteCrop(c.Stage, c.Timer, c.ZoneId, c.Type);
                        if (store.TrySetPayload(kind, wx, wy, payload))
                            set++;
                        break;
                    }
                    case WorldDeltaKind.WallPlan:
                    case WorldDeltaKind.BuildingPlan:
                    {
                        byte[] payload = null;
                        if (ConstructionManager.Instance.TryGetSite(x, y, out var site) && site != null)
                        {
                            var delivered = new List<(ItemId, int)>();
                            foreach (var d in site.GetAllDelivered())
                                delivered.Add((d.Key, d.Value));
                            payload = IslandPayload.WriteBuildPlan(site.Stage, delivered, site.WorkProgress);
                        }
                        if (store.TrySetPayload(kind, wx, wy, payload))
                            set++;
                        break;
                    }
                    case WorldDeltaKind.CutTreePlan:
                    {
                        float f = TreeJobManager.Instance.ChopProgress.GetFraction(x, y);
                        if (store.TrySetPayload(kind, wx, wy, IslandPayload.WriteProgressFraction(f)))
                            set++;
                        break;
                    }
                    case WorldDeltaKind.MinedStonePlan:
                    {
                        float f = StoneJobManager.Instance.MineProgress.GetFraction(x, y);
                        if (store.TrySetPayload(kind, wx, wy, IslandPayload.WriteProgressFraction(f)))
                            set++;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                if (writeLog)
                    GD.PushWarning($"[IslandStateSnapshot] kind=0x{kind:X2} ({x},{y}): {ex.Message}");
            }
        }

        // --- GroundItems: синхронизация набора меток со снапшотом куч. ---
        // Кучи появляются/исчезают БЕЗ событий (у менеджера их нет), поэтому
        // набор меток не может жить инкрементально — только полная сверка.
        try
        {
            var existing = new List<(long X, long Y)>();
            foreach ((byte kind, long wx, long wy) in cells)
                if (kind == WorldDeltaKind.GroundItems)
                    existing.Add((wx, wy));

            if (existing.Count > 0)
            {
                var rm = new WorldMutation[existing.Count];
                for (int i = 0; i < existing.Count; i++)
                    rm[i] = new WorldMutation(WorldDeltaKind.GroundItems, existing[i].X, existing[i].Y);
                groundRemoved = store.RemoveBatch(rm);
            }

            var piles = GroundItemManager.Instance.GetItemsSnapshot();
            if (piles.Count > 0)
            {
                var add = new WorldMutation[piles.Count];
                int n = 0;
                foreach (var p in piles)
                {
                    // ST-37 / S-09: пишем все кучи, включая клетки за пределами острова
                    long wx = originX + p.X, wy = originY + p.Y;
                    add[n++] = new WorldMutation(WorldDeltaKind.GroundItems, wx, wy,
                        IslandPayload.WriteGroundItems(p.Item3, p.Item4));
                }
                if (n > 0)
                {
                    if (n != add.Length)
                        Array.Resize(ref add, n);
                    groundApplied = store.ApplyBatch(add);
                }
            }
        }
        catch (Exception ex)
        {
            if (writeLog)
                GD.PushWarning($"[IslandStateSnapshot] ground items sync: {ex.Message}");
        }

        if (writeLog)
            GD.Print($"[World] state-snapshot острова: payload'ы={set} ground+={groundApplied} ground-={groundRemoved}");
        return (set, groundApplied, groundRemoved);
    }
}
