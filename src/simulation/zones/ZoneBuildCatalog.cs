using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.Simulation;

/// <summary>Строка каталога объектов стройки: что рисуем кнопкой в окне стройки.</summary>
public sealed class ZoneBuildEntry
{
    public BuildingType Type { get; }
    public string Title { get; }
    public string IconPath { get; }
    /// <summary>Общий объект: доступен в ЛЮБОЙ зоне (стены/центральный блок).</summary>
    public bool IsCommon { get; }

    public ZoneBuildEntry(BuildingType type, string title, string iconPath, bool isCommon)
    {
        Type = type;
        Title = title;
        IconPath = iconPath;
        IsCommon = isCommon;
    }
}

/// <summary>
/// Каталог объектов стройки: ОТРАСЛЬ → её объекты (станки/мебель). Окно стройки
/// показывает ТОЛЬКО объекты выбранной зоны, чтобы игрок не путался (замысел
/// владельца), плюс группу «Общее» — она доступна всегда.
///
/// Данные берутся из <see cref="ZoneIndustryRegistry"/>: чтобы добавить станок
/// отрасли, достаточно дописать BuildingType в профиль — UI подхватит.
/// </summary>
public static class ZoneBuildCatalog
{
    /// <summary>
    /// Общие объекты: доступны в любой зоне. Стены/двери/полы — это режимы
    /// верхнего ряда окна (WallMaterialButton/DoorButton), а здесь — объекты,
    /// которые ставятся как постройка. CentralBlock — якорь центра зоны.
    /// </summary>
    public static readonly IReadOnlyList<BuildingType> Common = new[]
    {
        BuildingType.WoodWall,
        BuildingType.CentralBlock
    };

    private static readonly Dictionary<BuildingType, string> IconByType = new()
    {
        // Станки: res://ui/assets/textures/workplaces/<id>.png (task-12).
        [BuildingType.Sawmill] = "res://ui/assets/textures/workplaces/sawmill.png",
        [BuildingType.Smelter] = "res://ui/assets/textures/workplaces/smelter.png",
        [BuildingType.Anvil] = "res://ui/assets/textures/workplaces/anvil.png",
        [BuildingType.Workbench] = "res://ui/assets/textures/workplaces/workbench.png",
        [BuildingType.SpinningWheel] = "res://ui/assets/textures/workplaces/spinning.png",
        [BuildingType.Loom] = "res://ui/assets/textures/workplaces/loom.png",
        [BuildingType.Mill] = "res://ui/assets/textures/workplaces/mill.png",
        [BuildingType.AssemblyLine] = "res://ui/assets/textures/workplaces/assembly.png",
        // Существующие объекты: те же спрайты, что в мире.
        [BuildingType.WorkTable] = "res://ui/assets/textures/icons/work_table.png",
        [BuildingType.Bed] = "res://ui/assets/textures/objects/bed.png",
        [BuildingType.Bench] = "res://ui/assets/textures/objects/bench.png",
        [BuildingType.Nightstand] = "res://ui/assets/textures/objects/nightstand.png",
        [BuildingType.WoodWall] = "res://ui/assets/textures/icons/WallIcon.png",
        [BuildingType.CentralBlock] = "res://ui/assets/textures/objects/central.png"
    };

    /// <summary>Объекты отрасли (без общих). Пустой список — отрасль без строек.</summary>
    public static IReadOnlyList<BuildingType> BuildingsFor(ZoneKind kind) =>
        ZoneIndustryRegistry.Get(kind).Buildings;

    /// <summary>Объекты отрасли + общие объекты — ровно то, что видно в окне стройки.</summary>
    public static IReadOnlyList<ZoneBuildEntry> CatalogFor(ZoneKind kind)
    {
        var own = BuildingsFor(kind);
        var list = new List<ZoneBuildEntry>(own.Count + Common.Count);
        for (int i = 0; i < own.Count; i++)
        {
            var t = own[i];
            list.Add(new ZoneBuildEntry(t, TitleFor(t), IconPathFor(t), false));
        }
        for (int i = 0; i < Common.Count; i++)
        {
            var t = Common[i];
            list.Add(new ZoneBuildEntry(t, TitleFor(t), IconPathFor(t), true));
        }
        return list;
    }

    /// <summary>Является ли объект общим (доступен всегда).</summary>
    public static bool IsCommon(BuildingType type)
    {
        for (int i = 0; i < Common.Count; i++)
        {
            if (Common[i] == type)
                return true;
        }
        return false;
    }

    /// <summary>Иконка объекта для кнопки окна стройки (null — иконки нет).</summary>
    public static string IconPathFor(BuildingType type) =>
        IconByType.TryGetValue(type, out var path) ? path : null;

    /// <summary>Подпись объекта — из StructureRegistry (единый источник имён).</summary>
    public static string TitleFor(BuildingType type) => StructureRegistry.Get(type).DisplayName;
}