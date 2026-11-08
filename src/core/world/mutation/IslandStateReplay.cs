using System;
using System.Collections.Generic;
using Godot;
using Game.Core;
using Game.Core.WorldStreaming.Integration;
// L-03: нарушение слоёв (core → simulation/ui) — правило и список см. docs/ARCHITECTURE.md §7.1
using Game.Simulation;

namespace Game.Core.WorldStreaming.Mutation;

// ============================================================================
// §119 P7б (task-17 п.4): STATE-REPLAY островных дельт — восстановление
// СОСТОЯНИЯ (не только занятости) в менеджерах домена при загрузке колонии.
//
// ПОРЯДОК ЗАГРУЗКИ (почему этот класс существует):
//   1. IslandDeltaApplicator.Apply (в Task.Run) — восстанавливает занятость в
//      менеджерах домена (фаза 5 IslandDomainReplay).
//   2. Main.ResetColonyState — чистит ВСЕ менеджеры (в т.ч. то, что сделал шаг 1:
//      прте-существующий порядок затирал реплей!).
//   3. ЭТОТ Replay — идемпотентно ПОВТОРЯЕТ доменные метки (занятость) и
//      разворачивает payload'ы (стадия культур, содержимое склада, доставка
//      стройки, прогресс монтажа/рубки, кучки на земле).
//   → вызывается из Main ПОСЛЕ ResetColonyState и ДО старта сима (ctx == null).
//
// Гарантии: идемпотентность (вторая прокрутка не дублирует), исключения домена
// ловятся, недоступное состояние честно логируется, занятость всегда восстановлена.
// ============================================================================

/// <summary>Результат state-replay (для лога Main и тестов).</summary>
public readonly struct IslandStateReplayResult
{
    public readonly int DomainApplied;
    public readonly int DomainSkipped;
    public readonly int CropsRestored;
    public readonly int StorageSlotsRestored;
    public readonly int GroundPilesRestored;
    public readonly int SitesStateRestored;
    public readonly int ProgressCellsRestored;

    public IslandStateReplayResult(int domainApplied, int domainSkipped, int cropsRestored,
        int storageSlotsRestored, int groundPilesRestored, int sitesStateRestored, int progressCellsRestored)
    {
        DomainApplied = domainApplied;
        DomainSkipped = domainSkipped;
        CropsRestored = cropsRestored;
        StorageSlotsRestored = storageSlotsRestored;
        GroundPilesRestored = groundPilesRestored;
        SitesStateRestored = sitesStateRestored;
        ProgressCellsRestored = progressCellsRestored;
    }

    public override string ToString()
        => $"domain={DomainApplied}/skip={DomainSkipped} crops={CropsRestored} storage={StorageSlotsRestored} " +
           $"ground={GroundPilesRestored} siteState={SitesStateRestored} progress={ProgressCellsRestored}";
}

public static class IslandStateReplay
{
    /// <summary>
    /// Восстановить состояние колонии из дельт стора в менеджерах домена.
    /// originX/originY — мировой origin зоны (UnifiedWorldGenerator.WorldOriginX/Y).
    /// <paramref name="writeLog"/> = false — для headless-стендов без запущенного
    /// движка (GD.Print/GD.PushWarning вне движка роняют процесс); на результат
    /// реплея не влияет.
    /// </summary>
    public static IslandStateReplayResult Replay(IslandOverrideStore store, long originX, long originY,
        bool writeLog = true)
    {
        if (store == null)
            return new IslandStateReplayResult(0, 0, 0, 0, 0, 0, 0);

        var cells = new List<(byte Kind, long CellX, long CellY)>(store.Count);
        // ST-37: перечисляем все дельты стора без отсечения по ActiveArea.Size (охват стриминга и объектов за кромкой 1024)
        store.Enumerate(long.MinValue, long.MinValue, long.MaxValue, long.MaxValue, 0, cells);

        // --- 1. Доменные метки (занятость) — идемпотентный повтор фазы 5. ---
        // P0 (bughunt 04-1): доменные менеджеры (стены/здания/склады/грядки/планы)
        // работают в ЛОКАЛЬНЫХ координатах острова, а стор хранит МИРОВЫЕ клетки.
        var domainCells = new List<(byte Kind, long X, long Y)>();
        foreach ((byte kind, long x, long y) in cells)
        {
            if (kind == WorldDeltaKind.GroundItems)
                continue; // «массивный» вид — не домен
            if (!IslandDomainReplay.IsDomainKind(kind))
                continue;
            long lx = x - originX;
            long ly = y - originY;
            // ST-37: не отсекаем дельты за пределами ActiveArea.Size
            domainCells.Add((kind, lx, ly));
        }
        (int domainApplied, int domainSkipped) = IslandDomainReplay.ReplayAll(domainCells, null);

        // --- 2. Состояние по kind'ам. ---
        int crops = 0, storageSlots = 0, ground = 0, siteState = 0, progress = 0;

        foreach ((byte kind, long wx, long wy) in cells)
        {
            int x = checked((int)(wx - originX));
            int y = checked((int)(wy - originY));
            // ST-37: не отсекаем дельты и предметы за пределами ActiveArea.Size
            store.TryGet(kind, wx, wy, out byte[] payload);
            try
            {
                switch (kind)
                {
                    case WorldDeltaKind.Stockpile:
                    {
                        if (!IslandPayload.TryReadStockpile(payload, out ItemId item, out int count))
                            break; // пустой слот/битый payload — занятость уже восстановлена
                        // L-03: нарушение слоёв (core → simulation/ui) — правило и список см. docs/ARCHITECTURE.md §7.1
                        StockpileManager.Instance.RestoreSlotRaw(x, y, item, count);
                        storageSlots++;
                        break;
                    }
                    case WorldDeltaKind.Crop:
                    {
                        if (!IslandPayload.TryReadCrop(payload, out int stage, out float timer, out int zoneId, out CropType cropType))
                            break; // готовая грядка без культуры — метка уже реплеена
                        // Старые дельты (schema v1) и payload с type=0 не знают культуру —
                        // берём её у farm-зоны (клетка → зона, затем сохранённый zoneId),
                        // иначе пшеница. Без исключений.
                        if (cropType == CropType.None)
                            cropType = ResolveCropForZone(x, y, zoneId);
                        CropGrowthManager.Instance.RestoreCrop(x, y, zoneId, stage, timer, cropType);
                        crops++;
                        break;
                    }
                    case WorldDeltaKind.WallPlan:
                    case WorldDeltaKind.BuildingPlan:
                    {
                        if (!IslandPayload.TryReadBuildPlan(payload, out var stage, out var delivered, out float wp))
                            break; // null payload = свежий план (дефолт фазы 5 уже применён)
                        // L-03: нарушение слоёв (core → simulation/ui) — правило и список см. docs/ARCHITECTURE.md §7.1
                        ConstructionSite site = ConstructionManager.Instance.TryGetSite(x, y, out var s) ? s : null;
                        if (site == null)
                            break; // сайт сорвался (занятость важнее) — не критично
                        if (stage == ConstructionStage.Completed)
                            stage = ConstructionStage.Assembling; // завершение = зеркало снимет метку
                        foreach (var d in delivered)
                            site.AddDelivered(d.Item, d.Count);
                        site.Stage = stage;
                        site.WorkProgress = Math.Clamp(wp, 0f, 1f);
                        site.WorkProgressStage = -1; // визуальная стадия пересчитается
                        // Перезапуск jobs-трубки стадии (jobs стёрты ResetColonyState):
                        if (stage == ConstructionStage.Assembling)
                        {
                            JobBroker.Instance.RegisterConstructionAt(x, y);
                        }
                        else if (stage == ConstructionStage.Supplying || stage == ConstructionStage.WaitingAll)
                        {
                            JobBroker.Instance.RegisterBlueprint(x, y, site.Definition.Type, site.Definition.TotalRequiredItems);
                        }
                        siteState++;
                        break;
                    }
                    case WorldDeltaKind.CutTreePlan:
                    {
                        if (IslandPayload.TryReadProgressFraction(payload, out float f) && f > 0f)
                        {
                            // L-03: нарушение слоёв (core → simulation/ui) — правило и список см. docs/ARCHITECTURE.md §7.1
                            TreeJobManager.Instance.ChopProgress.SaveFraction(x, y, f);
                            progress++;
                        }
                        break;
                    }
                    case WorldDeltaKind.MinedStonePlan:
                    {
                        if (IslandPayload.TryReadProgressFraction(payload, out float f) && f > 0f)
                        {
                            StoneJobManager.Instance.MineProgress.SaveFraction(x, y, f);
                            progress++;
                        }
                        break;
                    }
                    case WorldDeltaKind.GroundItems:
                    {
                        if (!IslandPayload.TryReadGroundItems(payload, out ItemId item, out int count))
                            break;
                        // L-03: нарушение слоёв (core → simulation/ui) — правило и список см. docs/ARCHITECTURE.md §7.1
                        GroundItemManager.Instance.SpawnItems(x, y, item, count);
                        ground++;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                // Дельта не убивает загрузку: лог + честный учёт.
                if (writeLog)
                    GD.PushWarning($"[IslandStateReplay] kind=0x{kind:X2} ({x},{y}): {ex.Message}");
            }
        }

        var result = new IslandStateReplayResult(domainApplied, domainSkipped, crops, storageSlots, ground, siteState, progress);
        if (writeLog)
            GD.Print($"[World] state-replay острова: {result}");
        return result;
    }

    /// <summary>
    /// Культура для клетки, у которой в payload НЕТ типа (старая схема v1 или
    /// type=0): сначала смотрим, какой farm-зоне принадлежит САМА КЛЕТКА (устойчиво
    /// к перенумерации зон при загрузке), затем — зону по сохранённому zoneId.
    /// Если зон ещё нет (RestoreZonesFromDisk идёт ПОСЛЕ state-replay) или это не
    /// Farm-зона — честный дефолт Wheat. Никаких исключений наружу.
    /// </summary>
    private static CropType ResolveCropForZone(int x, int y, int zoneId)
    {
        try
        {
            if (FarmZoneManager.Instance.TryGetZoneAt(x, y, out var byCell) && byCell != null)
                return byCell.Crop;
            if (zoneId >= 0
                && FarmZoneManager.Instance.TryGetZoneById(zoneId, out var byId) && byId != null)
                return byId.Crop;
        }
        catch (Exception)
        {
            // Дельта не должна убивать загрузку из-за отказа фасада зон.
        }
        return CropType.Wheat;
    }
}
