using System;
using System.Collections.Generic;
using System.Text;

namespace Game.Core;

/// <summary>
/// 87 новых сырьевых ресурсов T0 (task-4). Вместе с тремя legacy-предметами
/// (Log/Grain/Stone) покрывают все 90 позиций атласа
/// <c>ui/assets/textures/resources_raw/resources_raw_atlas.json</c>:
/// wheat→Grain, stone→Stone, wood_softwood→Log уже существуют, остальные 87
/// добавлены в <see cref="ItemId"/> плотным диапазоном 4..90 в порядке index.
///
/// Вес/стак заданы по КАТЕГОРИИ атласа (enum собран в порядке атласа, поэтому
/// категория — непрерывный диапазон enum): еда 0.2 кг, руда 2.5 кг, энергия 1.0,
/// химия 1.2, камень 2.0, заполнитель 1.5, лес 1.5, волокно 0.5, утилита 1.0.
/// Стак — <see cref="ItemConfig.RawMaxStack"/>.
///
/// Идентификатор ресурса для арта и рецептов выводится из имени члена enum
/// (PascalCase → snake_case), а не дублируется строкой: <c>IronHematite</c> →
/// <c>iron_hematite</c> → <c>res://ui/assets/textures/drop/iron_hematite.png</c>.
/// Арт дропа опционален: рендер подставляет заглушку, если файла нет.
/// </summary>
public static class RawItemDefinitions
{
    private static readonly ItemDefinition[] Definitions = Build();

    /// <summary>Все определения новых сырьевых ресурсов (87 штук).</summary>
    public static IReadOnlyList<ItemDefinition> All => Definitions;

    /// <summary>Сколько новых ItemId добавлено (90 ресурсов минус 3 legacy).</summary>
    public static int Count => Definitions.Length;

    /// <summary>Идентификатор ресурса в атласе/арте: PascalCase → snake_case.</summary>
    public static string ResourceId(ItemId id) => ToSnakeCase(id.ToString());

    /// <summary>Путь к спрайту дропа ресурса (файл может отсутствовать до арта).</summary>
    public static string DropTexturePath(string resourceId) => ItemConfig.DropTextureRoot + resourceId + ".png";

    private static ItemDefinition[] Build()
    {
        var list = new List<ItemDefinition>(ItemConfig.LastRawItemId - ItemConfig.FirstRawItemId + 1);
        // Диапазоны enum = категории атласа (порядок index). Меняется только
        // ПЕРВЫЙ/ПОСЛЕДНИЙ id и вес — состав задаётся enum'ом.
        AddRange(list, ItemId.Rye, ItemId.FishSea, 0.2f);             // еда (19)
        AddRange(list, ItemId.IronHematite, ItemId.Monazite, 2.5f);   // руда (23)
        AddRange(list, ItemId.BituminousCoal, ItemId.NaturalGasDry, 1.0f); // энергия (6)
        AddRange(list, ItemId.Sulfur, ItemId.Borax, 1.2f);            // химия (12)
        AddRange(list, ItemId.Granite, ItemId.Slate, 2.0f);           // камень (5)
        AddRange(list, ItemId.Sand, ItemId.Marl, 1.5f);               // заполнитель (5)
        AddRange(list, ItemId.WoodHardwood, ItemId.GuttaPercha, 1.5f); // лес (5)
        AddRange(list, ItemId.Cotton, ItemId.Tallow, 0.5f);           // волокно (10)
        AddRange(list, ItemId.Water, ItemId.Ice, 1.0f);               // утилита (2)
        return list.ToArray();
    }

    private static void AddRange(List<ItemDefinition> list, ItemId first, ItemId last, float weight)
    {
        for (int value = (int)first; value <= (int)last; value++)
        {
            var id = (ItemId)value;
            string resourceId = ToSnakeCase(id.ToString());
            list.Add(new ItemDefinition(
                id,
                resourceId,
                weight,
                ItemConfig.RawMaxStack,
                DropTexturePath(resourceId)));
        }
    }

    /// <summary>PascalCase → snake_case без аллокации промежуточных строк-массивов.</summary>
    private static string ToSnakeCase(string pascal)
    {
        if (string.IsNullOrEmpty(pascal))
            return pascal ?? "";
        var sb = new StringBuilder(pascal.Length + 8);
        for (int i = 0; i < pascal.Length; i++)
        {
            char c = pascal[i];
            if (char.IsUpper(c))
            {
                if (i > 0 && (char.IsLower(pascal[i - 1]) || (i + 1 < pascal.Length && char.IsLower(pascal[i + 1]))))
                    sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}