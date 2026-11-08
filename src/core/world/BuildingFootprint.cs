namespace Game.Core;

/// <summary>
/// Габариты постройки в клетках. 1 клетка = 1 м = 64 px (решение владельца,
/// 2026-11-06); спрайт обязан быть ровно W*64 × H*64 и совпадать с занимаемой
/// площадью (§133.6, _reports/art_scale.md):
/// anvil 1×1, workbench 2×1, spinning 2×1, sawmill 3×2, smelter 2×2,
/// mill 2×2, loom 2×2, assembly 3×2. Спрайт — W*64 × H*64 png
/// в res://ui/assets/textures/workplaces/.
///
/// Единый источник истины для: StructureDefinition.Width/Height (площадка,
/// логика), TileSet-тайла в MapRenderer (визуал) и призрака BuildTool —
/// чтобы «сколько занимает станок» не расходилось между логикой и картинкой.
/// </summary>
public static class BuildingFootprint
{
    /// <summary>Ширина постройки в клетках.</summary>
    public static int WidthOf(BuildingType type) => type switch
    {
        BuildingType.AssemblyLine => 3,
        BuildingType.Sawmill => 3,
        BuildingType.Mill => 2,
        BuildingType.SpinningWheel => 2,
        BuildingType.Loom => 2,
        BuildingType.Smelter => 2,
        BuildingType.Workbench => 2,
        BuildingType.Anvil => 1,
        _ => 1,
    };

    /// <summary>Высота постройки в клетках.</summary>
    public static int HeightOf(BuildingType type) => type switch
    {
        BuildingType.AssemblyLine => 2,
        BuildingType.Sawmill => 2,
        BuildingType.Mill => 2,
        BuildingType.SpinningWheel => 1,
        BuildingType.Loom => 2,
        BuildingType.Smelter => 2,
        BuildingType.Workbench => 1,
        BuildingType.Anvil => 1,
        _ => 1,
    };

    public static (int Width, int Height) Of(BuildingType type) =>
        (WidthOf(type), HeightOf(type));

    /// <summary>Многоклеточная постройка (спрайт больше одной клетки).</summary>
    public static bool IsMultiTile(BuildingType type) => WidthOf(type) > 1 || HeightOf(type) > 1;
}