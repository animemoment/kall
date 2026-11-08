using Godot;
using System;
using System.Collections.Generic;
using Game.Core;
using Game.Simulation;

namespace Game.UI;

/// <summary>
/// Реестр материалов стен для попапа WallMaterialButton.
/// Пока 1 материал: дерево (WoodWall). Новые — добавлением одной строки.
/// </summary>
public static class WallMaterialRegistry
{
    public readonly struct Entry
    {
        public readonly string Id;
        public readonly string Title;
        public readonly BuildingType Type;
        public readonly int SourceId;
        public readonly BuildingType FloorType;

        public Entry(string id, string title, BuildingType type, int sourceId, BuildingType floorType)
        {
            Id = id;
            Title = title;
            Type = type;
            SourceId = sourceId;
            FloorType = floorType;
        }
    }

    public static readonly IReadOnlyList<Entry> All = new List<Entry>
    {
        new("wood", "Дерево", BuildingType.WoodWall, MapRenderer.SourceWall, BuildingType.WoodFloor),
        new("stone", "Камень", BuildingType.WoodWall, MapRenderer.SourceWall, BuildingType.StoneFloor),
        new("concrete", "Бетон", BuildingType.WoodWall, MapRenderer.SourceWall, BuildingType.ConcreteFloor),
    };

    public static Entry ById(string id)
    {
        foreach (var e in All)
            if (string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase))
                return e;
        return All[0];
    }

    /// <summary>
    /// Автоматический выбор типа пола по материалу здания:
    /// Дерево -> WoodFloor, Камень -> StoneFloor, Бетон -> ConcreteFloor.
    /// </summary>
    public static BuildingType GetFloorForMaterial(string materialId) => ById(materialId).FloorType;

    /// <summary>
    /// Автоматический выбор типа крыши по назначению здания:
    /// Жилье -> непрозрачная RoofResidential (скрывает интерьер).
    /// Промпредприятия/склады/цеха -> полупрозрачная RoofIndustrial (затемняет цех, но всё видно).
    /// </summary>
    public static BuildingType GetRoofForZone(ZoneKind kind)
    {
        return kind == ZoneKind.Housing ? BuildingType.RoofResidential : BuildingType.RoofIndustrial;
    }
}
