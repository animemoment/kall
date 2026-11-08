using System.Collections.Generic;
using Game.Core;

namespace Game.Simulation;

/// <summary>
/// Состав материалов постройки (task-19): бревно + камень (если в цене есть).
/// Доставка типизирована (<c>JobData.TargetItemId</c>), поэтому RequiredMaterials
/// станков может содержать камень — площадка не «зависнет» в Supplying: она
/// копит материалы потипно и переходит в Building только когда довезено ВСЁ.
/// </summary>
public static class StructureMaterials
{
    /// <summary>
    /// Материалы типа: LogsFor + StonesFor (нулевые позиции не включаются).
    /// Порядок стабилен: бревно, затем камень — от него зависит последовательность
    /// доставки (ConstructionSite.TryGetNextNeededMaterial).
    /// </summary>
    public static (ItemId Item, int Count)[] Build(BuildingType type)
    {
        int logs = BuildConfig.LogsFor(type);
        int stones = BuildConfig.StonesFor(type);
        int count = (logs > 0 ? 1 : 0) + (stones > 0 ? 1 : 0);
        if (count == 0)
            return System.Array.Empty<(ItemId, int)>();

        var materials = new (ItemId, int)[count];
        int i = 0;
        if (logs > 0)
            materials[i++] = (ItemId.Log, logs);
        if (stones > 0)
            materials[i] = (ItemId.Stone, stones);
        return materials;
    }
}