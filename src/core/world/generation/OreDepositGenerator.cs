using System;
using Game.Core.WorldStreaming;

namespace Game.Core;

/// <summary>
/// Тонкая обёртка над <see cref="OreWorldField"/> (task-9: один источник истины).
///
/// Раньше остров генерировал руду СВОИМ оконным проходом (MapData.Ground,
/// провинции по локальному рельефу окна, отдельные стартовые залежи) — и на кромке
/// 1024 тип/запас/форма могли не совпасть с миром за кромкой (класс багов «шов»,
/// ARCHITECTURE.md §7). Теперь остров и мир читают ОДНО детерминированное поле по
/// абсолютным координатам: <see cref="OreWorldField.FillMapData"/> просто копирует
/// клетки поля в плоские массивы MapData. Стартовые Fe/Cu — часть поля
/// (<c>StartAnchor</c>), а не отдельный проход.
///
/// Сигнатура сохранена ради существующих вызовов
/// (<c>UnifiedWorldGenerator.GenerateColonyWindow</c>, <c>MapDataShim.CopyInto</c>).
/// Параметры <paramref name="climateGrid"/>/<paramref name="climateStep"/> больше не
/// нужны: пасс сам семплит канонический климат мира — оставлены для совместимости.
/// </summary>
public static class OreDepositGenerator
{
    /// <summary>
    /// Заполнить массивы руды <paramref name="data"/> из канонического поля мира.
    /// <paramref name="originAbsX/Y"/> — АБСОЛЮТНЫЙ мировой угол окна
    /// (<c>WorldOrigin + canvas</c>), как и раньше у вызовов.
    /// Возвращает число чанков с рудой (диагностика).
    /// </summary>
    public static int Generate(MapData data, ulong worldSeed, uint generatorVersion,
        long originAbsX, long originAbsY,
        WorldClimateParams[,] climateGrid = null,
        int climateStep = OreClimateLattice.DefaultStep)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));
        OreGeologyConfig.Validate();
        _ = climateGrid; // оставлено для совместимости: пасс сам берёт климат мира
        _ = climateStep;

        long canvasOriginX = originAbsX - UnifiedWorldGenerator.WorldOriginX;
        long canvasOriginY = originAbsY - UnifiedWorldGenerator.WorldOriginY;
        return OreWorldField.FillMapData(data, worldSeed, generatorVersion,
            canvasOriginX, canvasOriginY);
    }
}