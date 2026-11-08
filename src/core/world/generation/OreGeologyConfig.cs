using System;

namespace Game.Core;

/// <summary>Форма рудного тела (генезис) — влияет на рост блоба.</summary>
public enum OreFormKind : byte
{
    /// <summary>Округлый блоб/плащ (порфиры, латериты, магматические).</summary>
    Blob = 0,
    /// <summary>Пласт/полоса с вытянутостью 4:1…8:1 (BIF, SEDEX, эвапориты).</summary>
    Stripe = 1,
    /// <summary>Жила вдоль разлома, ширина 1–3, длина 5–20 (W, Sn, Au, Hg, Ag).</summary>
    Vein = 2,
    /// <summary>Лента вдоль реки/берега (россыпи).</summary>
    Ribbon = 3,
    /// <summary>Стручок/линза, вытянутая цепочкой (хромит, платина).</summary>
    Pod = 4
}

/// <summary>
/// Таблицы рудной геологии (task-5, источник — _plan/ORES_GEOLOGY.md).
/// Индексация — OreType 1..23 (0 = None), порядок = порядок атласа
/// resources_raw_atlas.json (категория «руда», индексы 20..42).
///
/// Единое место тюнинга: редкость/размер/форма/запас на клетку.
/// Все значения — целочисленные (детерминизм, никаких float в выборе).
/// </summary>
public static class OreGeologyConfig
{
    public const int TypeCount = OreConfig.TypeCount; // 23

    // ---- Плотность кандидатов (R2) ----
    /// <summary>Шаг сетки кандидатов в клетках: 1024² → ~1 800 кандидатов.</summary>
    public const int CandidateStride = 24;

    /// <summary>Вероятность приёма кандидата = S² · OreDensity (Q16, 0.5).</summary>
    public const int OreDensityQ16 = 62258; // 0.95: калибровка под фактические S² острова (док: 0.5 при E[S²]=0.34)

    /// <summary>Минимальное расстояние между центрами залежей (клеток).</summary>
    public const int MinGap = 10;

    /// <summary>Запас кандидатов за пределами окна (центр снаружи, тело внутри) — для бесшовности.</summary>
    public const int CandidateMargin = 48;

    // ---- Площади (R4.1): 15 квантилей лог-нормального распределения ----
    /// <summary>Квантили площади 1..14 (q=15 — тяжёлый хвост, см. SizeForHash).</summary>
    public static readonly ushort[] AreaQuantiles =
        { 1, 1, 1, 2, 2, 3, 4, 5, 6, 8, 10, 13, 17, 22, 30 };

    /// <summary>Абсолютный потолок площади залежа (после SizeScale).</summary>
    public static readonly int[] MaxArea =
    {
        0,
        900, // 1 hematite (BIF — пласты)
        700, // 2 magnetite
        300, // 3 manganese
        80,  // 4 chromite
        80,  // 5 tungsten
        80,  // 6 molybdenum
        80,  // 7 vanadium
        300, // 8 nickel
        60,  // 9 cobalt
        300, // 10 ilmenite
        900, // 11 copper chalcopyrite (порфир)
        300, // 12 copper oxide
        300, // 13 lead galena
        300, // 14 zinc sphalerite
        80,  // 15 tin
        900, // 16 bauxite
        40,  // 17 mercury
        300, // 18 pyrite
        40,  // 19 gold
        40,  // 20 silver
        30,  // 21 platinum
        100, // 22 uranium
        40,  // 23 monazite
    };

    /// <summary>Множитель площади по типу, % (жилы/спутники мельче, BIF/боксит крупнее).</summary>
    public static readonly int[] SizeScalePercent =
    {
        0,
        200, 150, 100, 60, 60, 55, 55, 100, 50, 100, // 1..10
        100, 100, 100, 100, 60, 180, 40, 40, 50, 45, // 11..20
        40, 65, 45                                    // 21..23
    };

    /// <summary>
    /// Условная доля среди залежей, в десятых процента (сумма = 1000 = 100 %).
    /// Источник — §2.2 «Частота, %»: 15;9;4;3;2;2;1,5;4;1,5;3;9;3;6;5;2;6;0,8;17;2;1,5;0,7;1;1.
    /// </summary>
    public static readonly int[] RarityTenths =
    {
        0,
        150, 90, 40, 30, 20, 20, 15, 40, 15, 30,      // 1..10
        90, 30, 60, 50, 20, 60, 8, 170, 20, 15,       // 11..20
        7, 10, 10                                     // 21..23
    };

    /// <summary>Форма залежа по типу.</summary>
    public static readonly OreFormKind[] Form =
    {
        OreFormKind.Blob,
        OreFormKind.Stripe, // 1 hematite
        OreFormKind.Blob,   // 2 magnetite
        OreFormKind.Stripe, // 3 manganese
        OreFormKind.Pod,    // 4 chromite
        OreFormKind.Vein,   // 5 tungsten
        OreFormKind.Blob,   // 6 molybdenum
        OreFormKind.Pod,    // 7 vanadium
        OreFormKind.Blob,   // 8 nickel
        OreFormKind.Blob,   // 9 cobalt
        OreFormKind.Ribbon, // 10 ilmenite
        OreFormKind.Blob,   // 11 copper chalcopyrite
        OreFormKind.Blob,   // 12 copper oxide
        OreFormKind.Stripe, // 13 lead galena (линза 2:1)
        OreFormKind.Stripe, // 14 zinc sphalerite
        OreFormKind.Vein,   // 15 tin
        OreFormKind.Blob,   // 16 bauxite
        OreFormKind.Vein,   // 17 mercury
        OreFormKind.Blob,   // 18 pyrite
        OreFormKind.Vein,   // 19 gold
        OreFormKind.Vein,   // 20 silver
        OreFormKind.Pod,    // 21 platinum
        OreFormKind.Stripe, // 22 uranium (линза/полумесяц)
        OreFormKind.Ribbon, // 23 monazite
    };

    /// <summary>Анизотропия роста, %: 0 = круглый блоб, 95 = вытянутая жила/пласт.</summary>
    public static readonly int[] AnisotropyPercent =
    {
        0,
        80, 20, 45, 55, 95, 30, 40, 30, 30, 90,       // 1..10
        20, 20, 55, 55, 90, 10, 90, 50, 95, 90,       // 11..20
        50, 50, 90                                    // 21..23
    };

    /// <summary>Минимальный запас клетки по типу (богатые жилы плотнее бедных BIF).</summary>
    public static readonly int[] ReserveMin =
    {
        0,
        50, 50, 80, 80, 120, 80, 80, 80, 120, 80,     // 1..10
        80, 80, 80, 80, 120, 50, 120, 50, 120, 120,   // 11..20
        120, 120, 120                                 // 21..23
    };

    /// <summary>Верхняя граница запаса клетки (не включая).</summary>
    public static readonly int[] ReserveMaxExclusive =
    {
        0,
        81, 81, 121, 121, 161, 121, 121, 121, 161, 121, // 1..10
        121, 121, 121, 121, 161, 81, 161, 81, 161, 161, // 11..20
        161, 161, 161                                    // 21..23
    };

    // ---- Играбельность (R5.4) ----
    /// <summary>Радиус стартовой поляны, которую руда не занимает (клеток).</summary>
    public const int StartAvoidRadius = 16;

    /// <summary>Стартовые залежи Fe/Cu ищутся в кольце [Min..Max] от центра окна.</summary>
    public const int StartRingMin = 60;
    public const int StartRingMax = 200;

    /// <summary>Размер стартового залежа Fe/Cu (клеток).</summary>
    public const int StartDepositMinArea = 30;
    public const int StartDepositMaxArea = 120;

    /// <summary>Порог «провинция существует» для гарантии покрытия типов (Q16).</summary>
    public const int CoverageSuitabilityMin = 29491; // ~0.45

    /// <summary>Порог, ниже которого руда не сеется (S_i почти ноль — §R3).</summary>
    public const int MinSuitabilityPick = 8000;

    /// <summary>Tyп руды в стартовых залежах: 1 = железо, 11 = медь.</summary>
    public const int StartOreIronType = 1;
    public const int StartOreCopperType = 11;

    /// <summary>Руды, которым гарантируется минимум залежей на карте (Fe, Cu, Pb, Zn).</summary>
    public const int GuaranteedMinDeposits = 2;

    /// <summary>Максимальный радиус виртуального блоба: рост за его пределы не пишем.</summary>
    public const int MaxBlobRadius = 48;

    // ---- Сульфиды/спутники (R5) ----
    public static bool IsSulfide(int type) =>
        type == 11 || type == 13 || type == 14 || type == 19 || type == 20 || type == 17;

    /// <summary>Ассоциация «спутник → хозяин»: возвращает тип-хозяина или 0.</summary>
    public static int SatelliteHost(int type) => type switch
    {
        20 => 13, // серебро ← галенит
        6 => 11,  // молибден ← медь
        9 => 8,   // кобальт ← никель
        7 => 2,   // ванадий ← магнетит
        14 => 13, // сфалерит ← галенит
        18 => 11, // пирит ← медь (ореол)
        _ => 0,
    };

    public static void Validate()
    {
        if (RarityTenths.Length != TypeCount + 1 || Form.Length != TypeCount + 1 ||
            AnisotropyPercent.Length != TypeCount + 1 || MaxArea.Length != TypeCount + 1 ||
            SizeScalePercent.Length != TypeCount + 1 || ReserveMin.Length != TypeCount + 1 ||
            ReserveMaxExclusive.Length != TypeCount + 1)
        {
            throw new InvalidOperationException("OreGeologyConfig: таблицы должны быть длиной TypeCount+1 (24).");
        }
        int sum = 0;
        for (int i = 1; i <= TypeCount; i++)
            sum += RarityTenths[i];
        if (sum != 1000)
            throw new InvalidOperationException($"OreGeologyConfig: сумма редкостей {sum} != 1000");
    }
}