namespace Game.Core;

/// <summary>
/// Состояние рудной жилы в клетке. Считается по доле остатка от начального
/// запаса (<see cref="OreConfig.StateFor"/>), а не хранится как независимая
/// сущность: <see cref="MapData.OreState"/> — производный кэш для рендера.
/// </summary>
public enum OreState : byte
{
    Rich = 0,
    Normal = 1,
    Poor = 2
}

/// <summary>
/// Константы руды (task-3). Руда в клетке — ХРАНИЛИЩЕ с запасом (как камень,
/// но с числом), поэтому её состояние зависит от остатка, а не от наличия.
///
/// Идентификаторы <see cref="TypeCount"/>=23 — это 23 записи категории «руда»
/// в <c>ui/assets/textures/resources_raw/resources_raw_atlas.json</c>. Сами
/// строковые id в C# НЕ дублируются (канон списка — JSON): рендер берёт их
/// через <c>ResourceAtlasCatalog</c> в порядке index, и номер там = OreType-1.
/// </summary>
public static class OreConfig
{
    /// <summary>Нет руды в клетке.</summary>
    public const byte None = 0;

    /// <summary>Количество типов руды (категория «руда» атласа ресурсов).</summary>
    public const int TypeCount = 23;

    /// <summary>Сколько месторождений максимум ставит заглушка генератора.</summary>
    public const int MaxDeposits = 5;

    /// <summary>Минимальный запас одной клетки месторождения (ед.).</summary>
    public const int ReserveMin = 80;

    /// <summary>Верхняя граница запаса клетки (не включая, как Random.Next).</summary>
    public const int ReserveMaxExclusive = 121;

    /// <summary>Минимальная порция, снимаемая агентом за один цикл добычи (ед.).</summary>
    public const int PortionMin = 8;

    /// <summary>Верхняя граница порции (не включая): за цикл снимается 8..15.</summary>
    public const int PortionMaxExclusive = 16;

    /// <summary>Порог rich: доля остатка СТРОГО больше (66 %).</summary>
    public const float RichThreshold = 0.66f;

    /// <summary>Порог normal: доля остатка больше либо равна (33 %).</summary>
    public const float NormalThreshold = 0.33f;

    /// <summary>
    /// Пересчитать состояние по остатку: rich &gt;66 %, normal 33..66 %, poor &lt;33 %.
    /// <paramref name="initial"/> == 0 (битые данные) трактуется как rich.
    /// </summary>
    public static OreState StateFor(int reserve, int initial)
    {
        if (initial <= 0 || reserve <= 0)
            return OreState.Poor;
        if (reserve > initial)
            reserve = initial;
        float ratio = (float)reserve / initial;
        if (ratio > RichThreshold)
            return OreState.Rich;
        if (ratio >= NormalThreshold)
            return OreState.Normal;
        return OreState.Poor;
    }

    /// <summary>Имя состояния для пути к арту: rich | normal | poor.</summary>
    public static string StateName(OreState state) => state switch
    {
        OreState.Rich => "rich",
        OreState.Normal => "normal",
        OreState.Poor => "poor",
        _ => "rich",
    };
}