using System;
using Game.Core;

namespace Game.Core.WorldStreaming;

// ============================================================================
// §118 фаза 3: Fill-мост «плоская ActiveArea → существующий MapData».
// Цель: 23 потребителя MapData продолжают работать БЕЗ правок (снос ядра —
// не разом, §118.0). Одно копирование при создании зоны; подключение к
// прод-коду (Main/MapRenderer) — Lead после смоука фазы 3.
//
// ПЕРЕНОСИТСЯ: Ground/TreeOnGrass/TreeVariant/StoneOnGrass/Biome/Variant
// (плоский row-major → двумерный [x,y]), ТЕКУЩЕЕ состояние Humidity/Fertility
// (по клетке через Set), водная подпитка влажности (RebuildWaterNear по
// перенесённому Ground — тот же BFS, что делает Initialize зоны), Climate, Seed.
//
// ЗАФИКСИРОВАННОЕ ОГРАНИЧЕНИЕ (§118): якорь восстановления плодородия
// FertilityMap._base переносится через публичный SeedBaseFromCurrent()
// (14-03, 2026-10-31): у зоны якорь уже стоит с Initialize, поэтому у цели он
// равен перенесённому стартовому состоянию. Живая миграция «потикавшей» зоны
// (когда _cur уже истощён) по-прежнему требует CopyFrom-API у FertilityMap/
// HumidityMap — это фаза 4.
//
// Мета-статистики LakeCount/LakeSizes/RiverBranchCount/MainRiverLength/
// LandConnectivity НЕ переносятся: GenerateColonyWindow их тоже НЕ заполняет
// (остаются дефолтами) — потребители разницы не видят.
// ============================================================================

/// <summary>
/// Мост ActiveArea → MapData. Чистый C#, без Godot API — можно звать из
/// фоновых потоков (Fill-мост при генерации зоны).
/// </summary>
public static class MapDataShim
{
    /// <summary>
    /// Новый MapData Size×Size с копией данных ЗАПОЛНЕННОЙ зоны.
    /// </summary>
    public static MapData ToMapData(ActiveArea source)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));
        if (!source.IsFilled)
            throw new InvalidOperationException("ActiveArea не заполнена (Fill) — мостить нечего.");

        var target = new MapData(ActiveArea.Size, ActiveArea.Size);
        CopyInto(target, source);
        return target;
    }

    /// <summary>
    /// Перезаписать СУЩЕСТВУЮЩИЙ MapData данными зоны. Размеры цели обязаны
    /// совпадать с зоной (Size×Size); все шесть массивов перезаписываются
    /// поэлементно (присвоить сами массивы нельзя — readonly-свойства).
    /// </summary>
    public static void FillMapData(MapData target, ActiveArea source)
    {
        if (target == null)
            throw new ArgumentNullException(nameof(target));
        if (source == null)
            throw new ArgumentNullException(nameof(source));
        if (!source.IsFilled)
            throw new InvalidOperationException("ActiveArea не заполнена (Fill) — мостить нечего.");
        if (target.Width != ActiveArea.Size || target.Height != ActiveArea.Size)
            throw new ArgumentException(
                $"MapData {target.Width}×{target.Height} не совпадает с зоной {ActiveArea.Size}×{ActiveArea.Size}.");

        CopyInto(target, source);
    }

    private static void CopyInto(MapData target, ActiveArea source)
    {
        int size = ActiveArea.Size;
        TileType[] ground = source.Ground;
        bool[] trees = source.TreeOnGrass;
        byte[] treeVariants = source.TreeVariant;
        bool[] stones = source.StoneOnGrass;
        byte[] biome = source.Biome;
        byte[] variant = source.Variant;

        // Плоский row-major (индекс = ly*size + lx) → двумерный [lx, ly] MapData.
        for (int ly = 0; ly < size; ly++)
        {
            int row = ly * size;
            for (int lx = 0; lx < size; lx++)
            {
                int i = row + lx;
                target.Ground[lx, ly] = ground[i];
                target.TreeOnGrass[lx, ly] = trees[i];
                target.TreeVariant[lx, ly] = treeVariants[i];
                target.StoneOnGrass[lx, ly] = stones[i];
                target.Biome[lx, ly] = biome[i];
                target.Variant[lx, ly] = variant[i];
            }
        }

        // Динамические слои: переносим ТЕКУЩЕЕ состояние зоны. Клампы Set
        // (влага: пол 10; плодородие: 0..200) значений после Initialize не
        // меняют — перенос бит-в-бит. Водная подпитка (BFS от воды) —
        // пересобирается по перенесённому Ground, идентично зоне.
        // Про _base — см. фиксацию в шапке файла.
        for (int ly = 0; ly < size; ly++)
        {
            int row = ly * size;
            for (int lx = 0; lx < size; lx++)
            {
                int i = row + lx;
                target.Humidity.Set(lx, ly, source.Humidity.Get(lx, ly));
                target.Fertility.Set(lx, ly, source.Fertility.Get(lx, ly));
            }
        }
        target.Humidity.RebuildWaterNear(target.Ground);

        // 14-03: якорь восстановления плодородия (_base) переносится ОТДЕЛЬНО —
        // Set пишет только _cur. Без этого BaseAt нового MapData возвращал 0 и
        // «пар» (восстановление после уборки) не работал никогда: клетка навсегда
        // оставалась истощённой ×0.8 за каждый урожай.
        target.Fertility.SeedBaseFromCurrent();

        target.Climate = source.Climate;
        target.Seed = source.Seed;
    }
}
