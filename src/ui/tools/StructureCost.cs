using System;
using System.Collections.Generic;
using System.Text;
using Game.Core;
using Game.Simulation;

namespace Game.UI.Tools;

/// <summary>
/// Полная цена постройки по ТИПАМ материалов (task-21): «12 брёвен · 25 камня».
/// Источник — <see cref="StructureRegistry"/> (RequiredMaterials: Log + Stone…),
/// наличие — кэш склада <see cref="StockpileManager.GetTotalItemCount"/> (O(1)).
///
/// Раньше окно стройки показывало только брёвна (LogsFor), хотя станки (task-19)
/// стоят брёвна + камень — игрок не понимал, чего ждёт площадка.
/// Класс без Godot API: иконки подтягивает вызывающий (UI), текст/цвет — здесь.
/// </summary>
public static class StructureCost
{
    /// <summary>
    /// «12 брёвен · 25 камня» (пусто, если у постройки нет материалов).
    /// </summary>
    public static string Text(BuildingType type)
    {
        StructureDefinition def = StructureRegistry.Get(type);
        IReadOnlyList<(ItemId Item, int Count)> materials = def.RequiredMaterials;
        if (materials.Count == 0)
            return string.Empty;

        var sb = new StringBuilder(48);
        for (int i = 0; i < materials.Count; i++)
        {
            if (i > 0)
                sb.Append(" · ");
            (ItemId item, int count) = materials[i];
            sb.Append(count).Append(' ').Append(Word(item));
        }
        return sb.ToString();
    }

    /// <summary>Тултип объекта: название + полная цена (то же, что в окне).</summary>
    public static string Tooltip(string title, BuildingType type)
    {
        string cost = Text(type);
        return string.IsNullOrEmpty(cost) ? title : title + "\n" + cost;
    }

    /// <summary>Хватает ли материала на складе для постройки.</summary>
    public static bool HasEnough(ItemId item, int required)
        => required <= 0 || StockpileManager.Instance.GetTotalItemCount(item) >= required;

    /// <summary>Все материалы постройки есть на складе.</summary>
    public static bool IsFullyAvailable(BuildingType type)
    {
        IReadOnlyList<(ItemId Item, int Count)> materials = StructureRegistry.Get(type).RequiredMaterials;
        for (int i = 0; i < materials.Count; i++)
        {
            if (!HasEnough(materials[i].Item, materials[i].Count))
                return false;
        }
        return true;
    }

    /// <summary>Русское слово материала для подписи цены («брёвен», «камня»…).</summary>
    public static string Word(ItemId item) => item switch
    {
        ItemId.Log => "брёвен",
        ItemId.Stone => "камня",
        ItemId.Grain => "зерна",
        _ => LowerName(item)
    };

    /// <summary>id ресурса для иконки дропа (ResourceIconLoader: drop/&lt;id&gt;.png).</summary>
    public static string IconResourceId(ItemId item)
    {
        if (item == ItemId.Log)
            return "logs";
        if (item == ItemId.Stone)
            return "stone";
        if (item == ItemId.Grain)
            return "wheat";
        if ((int)item >= ItemConfig.FirstRawItemId && (int)item <= ItemConfig.LastRawItemId)
            return RawItemDefinitions.ResourceId(item);
        return null;
    }

    private static string LowerName(ItemId item)
    {
        if (!ItemRegistry.TryGet(item, out ItemDefinition def) || string.IsNullOrEmpty(def.Name))
            return "ед.";
        string name = def.Name;
        return char.ToLowerInvariant(name[0]) + name.Substring(1);
    }
}