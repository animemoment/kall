using Game.Core.WorldClimate;

namespace Game.Core;

/// <summary>Поля провинции в точке (0..65535) — из сетки 16×16 (§R1).</summary>
public readonly struct OreProvinceSample
{
    public readonly int Craton;
    public readonly int Magmatic;
    public readonly int Felsic;
    public readonly int Basin;
    public readonly int Laterite;
    public readonly int Placer;
    public readonly int Lowland;
    public readonly int Coast;
    public readonly int Lineament;
    /// <summary>Локальный рельеф (0..65535). Планетарный ElevationQ16 на острове
    /// почти константа, поэтому провинции считаются по локальному рельефу:
    /// горы/вода из MapData.Ground + hill/ridge-шум генератора.</summary>
    public readonly int Relief;
    /// <summary>Локальная скальность (аналог RockExposure на масштабе острова).</summary>
    public readonly int Rock;

    public OreProvinceSample(int craton, int magmatic, int felsic, int basin, int laterite,
        int placer, int lowland, int coast, int lineament, int relief, int rock)
    {
        Craton = craton;
        Magmatic = magmatic;
        Felsic = felsic;
        Basin = basin;
        Laterite = laterite;
        Placer = placer;
        Lowland = lowland;
        Coast = coast;
        Lineament = lineament;
        Relief = relief;
        Rock = rock;
    }

    public bool IsEmpty => Craton == 0 && Magmatic == 0 && Basin == 0 && Laterite == 0 && Placer == 0;
}

/// <summary>Локальные условия точки для таблицы пригодностей (§2.2).</summary>
public readonly struct OreTypeEnv
{
    public readonly int Elevation;
    public readonly int Tectonic;
    public readonly int RockExposure;
    public readonly int Aridity;
    public readonly int Moisture;
    public readonly int Precipitation;
    public readonly int Temperature;
    public readonly int OceanInfluence;
    public readonly int RiverDensity;
    public readonly int Fertility;
    public readonly int Glaciation;
    public readonly int Stone;
    /// <summary>1 — точка на берегу видимой реки (bank), 0 — нет.</summary>
    public readonly int Bank;

    public OreTypeEnv(int elevation, int tectonic, int rockExposure, int aridity, int moisture,
        int precipitation, int temperature, int oceanInfluence, int riverDensity, int fertility,
        int glaciation, int stone, int bank)
    {
        Elevation = elevation;
        Tectonic = tectonic;
        RockExposure = rockExposure;
        Aridity = aridity;
        Moisture = moisture;
        Precipitation = precipitation;
        Temperature = temperature;
        OceanInfluence = oceanInfluence;
        RiverDensity = riverDensity;
        Fertility = fertility;
        Glaciation = glaciation;
        Stone = stone;
        Bank = bank;
    }
}

/// <summary>
/// Пригодность места для каждого из 23 типов руды (§R3): произведение 2–4 «полок»
/// (а НЕ сумма — сумма размазывает провинции, произведение даёт резкие пояса).
/// Все значения Q16 0..65535; 65535 = 1.0.
/// </summary>
public static class OreSuitability
{
    private const int One = Q16.One;

    /// <summary>S_i: пригодность 0..1 (Q16) для типа <paramref name="type"/> (1..23).</summary>
    public static ushort Score(int type, in OreProvinceSample p, in OreTypeEnv e)
    {
        // КАЛИБРОВКА ПОД РЕАЛЬНЫЕ ДАННЫЕ ОСТРОВА: планетарные T/R на окне 1024²
        // почти константны (T=0, R=3000 замерено), поэтому условия по T/R из дока
        // заменены на локальную скальность e.RockExposure и локальный рельеф
        // e.Elevation (провинции R1). Пороговые диапазоны пересчитаны под 0..65535
        // локальной скальности (0..~45000), а не планетарного R.
        switch (type)
        {
            case 1: // hematite / BIF: кратон + плоские низкие бассейны, не арид
                return Mul(p.Craton,
                    Mul(Plateau(e.Elevation, 22000, 24000, 38000, 44000),
                        OneMinus(Ramp(e.Aridity, 36000, 50000))));

            case 2: // magnetite: у подножия гор, скальность
                return Mul(Plateau(e.Elevation, 26000, 29000, 44000, 48500),
                           Ramp(e.RockExposure, 18000, 40000));

            case 3: // manganese: мелководный шельф или латерит
            {
                ushort shelf = Mul(Plateau(e.Elevation, 22000, 23500, 30000, 34000),
                    Mul(Ramp(e.OceanInfluence, 32768, 52000), Ramp(e.Moisture, 40000, 50000)));
                ushort lateriteMn = Mul(p.Laterite, Plateau(e.Elevation, 23000, 25000, 33000, 38000));
                return Max(shelf, lateriteMn);
            }

            case 4: // chromite: офиолиты/расслоенные интрузии (скальные пояса)
                return Mul(Ramp(e.RockExposure, 20000, 42000),
                           Plateau(e.Elevation, 24000, 26000, 40000, 45000));

            case 5: // tungsten: скарны у подножия гор
                return Mul(Mul(Weighted(Ramp(e.Tectonic, 40000, 65000), 40,
                                        (ushort)Clamp01(e.RockExposure), 60),
                               Ramp(e.RockExposure, 22000, 42000)),
                    Mul(Plateau(e.Elevation, 30000, 32000, 44000, 48000),
                        Plateau(e.Temperature, 30000, 32000, 52000, 55000)));

            case 6: // molybdenum: порфировый спутник меди
                return Mul(Mul(Weighted(Ramp(e.Tectonic, 40000, 65000), 50,
                                        Mono(e.RockExposure, 38000), 50),
                               Ramp(e.RockExposure, 18000, 38000)),
                    Plateau(e.Elevation, 25000, 28000, 42000, 46500));

            case 7: // vanadium: титаномагнетит, спутник магнетита
                return Mul(Ramp(e.RockExposure, 20000, 40000),
                           Plateau(e.Elevation, 24000, 26000, 42000, 46000));

            case 8: // nickel: латерит (жарко+влажно) или магматический сульфид
            {
                ushort lateritic = Mul(p.Laterite,
                    Mul(Ramp(e.RockExposure, 16000, 34000),
                        OneMinus(Ramp(e.Elevation, 36000, 41000))));
                ushort magmatic = Mul(Weighted(Ramp(e.Tectonic, 50000, 70000), 40, Mono(0, 32768), 60),
                                      Ramp(e.RockExposure, 25000, 45000));
                return Max(lateritic, magmatic);
            }

            case 9: // cobalt: спутник никеля (жарко/влажно, низко)
                return Mul(Ramp(e.Temperature, 45000, 55000),
                    Mul(Ramp(e.Moisture, 35000, 45000), OneMinus(Ramp(e.Elevation, 36000, 40000))));

            case 10: // ilmenite: тяжёлые пески (россыпь)
                return Mul(p.Placer, Plateau(e.Elevation, 22000, 23500, 29000, 33000));

            case 11: // copper chalcopyrite: порфир дуги (магматика+фельзика)
                return Mul(Weighted((ushort)Clamp01(p.Magmatic), 60, (ushort)Clamp01(p.Felsic), 40),
                    Mul(Plateau(e.Elevation, 24000, 27000, 43000, 46500),
                        Weighted(Ramp(e.Precipitation, 18000, 38000), 80, 32768, 20)));

            case 12: // copper oxide: зона окисления в аридном климате
                return Mul(Ramp(e.Aridity, 24000, 38000),
                    Mul(Plateau(e.Elevation, 24000, 26000, 38000, 42000),
                        OneMinus(Ramp(e.Moisture, 36000, 45000))));

            case 13: // lead galena: осадочные бассейны / MVT
                return Mul(p.Basin,
                    Weighted(Ramp(e.Tectonic, 25000, 50000), 50,
                             OneMinus(Ramp(e.Aridity, 40000, 55000)), 50));

            case 14: // zinc sphalerite: SEDEX с галенитом
                return Mul(p.Basin,
                    Weighted(Ramp(e.Tectonic, 30000, 55000), 50, Ramp(e.Moisture, 30000, 45000), 50));

            case 15: // tin cassiterite: грейзен/скарн + россыпь
            {
                ushort greisen = Mul(Mul(Weighted(Ramp(e.Tectonic, 40000, 65000), 40,
                                                  (ushort)Clamp01(e.RockExposure), 60),
                                         Ramp(e.RockExposure, 22000, 42000)),
                    Mul(Plateau(e.Elevation, 30000, 32000, 44000, 48000),
                        Plateau(e.Temperature, 32000, 34000, 50000, 53000)));
                ushort placerSn = Mul(p.Placer, Plateau(e.Elevation, 22000, 23500, 29000, 34000));
                return Max(greisen, placerSn);
            }

            case 16: // bauxite: латеритное выветривание тропиков
                return Mul(p.Laterite, Plateau(e.Elevation, 22000, 24000, 34000, 38000));

            case 17: // mercury cinnabar: эпитермальная, разломы
                return Mul(Weighted(Ramp(e.Tectonic, 40000, 65000), 40, 32768, 60),
                    Mul(Ramp(e.Precipitation, 35000, 48000),
                        Mul(Ramp(e.RockExposure, 18000, 36000),
                            Plateau(e.Elevation, 24000, 26000, 36000, 41000))));

            case 18: // pyrite: чёрные сланцы бассейнов (самостоятельные залежи)
                return Mul(p.Basin, Ramp(e.Moisture, 45000, 55000));

            case 19: // gold: орогенные жилы разломов + россыпь
            {
                ushort orogenic = Mul(Mul(Weighted(Ramp(e.Tectonic, 40000, 65000), 40,
                                                    (ushort)Clamp01(e.RockExposure), 60),
                                          Ramp(e.RockExposure, 20000, 40000)),
                    Plateau(e.Elevation, 28000, 30000, 43000, 46000));
                ushort placerAu = Mul(p.Placer, Mul(Ramp(e.Stone, 20000, 45000),
                                                    (ushort)(e.Bank != 0 ? One : 0)));
                return Max(orogenic, placerAu);
            }

            case 20: // silver: эпитермальное / спутник галенита
                return Mul(Weighted(Ramp(e.Tectonic, 40000, 65000), 40, 32768, 60),
                    Mul(Ramp(e.Precipitation, 35000, 45000), Ramp(e.RockExposure, 18000, 36000)));

            case 21: // platinum: расслоенные интрузии/офиолиты
                return Mul(Mul(Weighted(Ramp(e.Tectonic, 40000, 65000), 40, 32768, 60),
                               Ramp(e.RockExposure, 24000, 44000)),
                           Plateau(e.Elevation, 24000, 26000, 38000, 43000));

            case 22: // uranium: щит × бассейн, не сильно аридный
                return Mul(p.Craton,
                    Mul(Plateau(e.Elevation, 23000, 24000, 34000, 38000),
                        OneMinus(Ramp(e.Aridity, 25000, 32000))));

            case 23: // monazite: тяжёлые пески (россыпь)
                return Mul(p.Placer, Plateau(e.Elevation, 22000, 23200, 27000, 31000));

            default:
                return 0;
        }
    }
    // ---- Целочисленные Q16-операции (единый стиль для читаемости таблицы) ----

    private static ushort Ramp(int value, int lo, int hi) => Q16.InverseLerp(lo, hi, value);

    private static ushort Plateau(int value, int a, int b, int c, int d)
    {
        ushort up = Q16.InverseLerp(a, b, value);
        ushort down = (ushort)(One - Q16.InverseLerp(c, d, value));
        return up < down ? up : down;
    }

    private static ushort OneMinus(ushort value) => (ushort)(One - value);

    /// <summary>Фиксированный Q16-уровень (для взвешивания «нет сигнала»).</summary>
    private static ushort Mono(int value, int level) => (ushort)Clamp01(level);

    private static ushort Mul(ushort a, ushort b) => Q16.Mul(a, b);

    private static ushort Mul(int a, ushort b) => Q16.Mul((ushort)Clamp01(a), b);

    private static ushort Weighted(ushort a, int wa, ushort b, int wb) => Q16.Weighted(a, wa, b, wb);

    private static ushort Max(ushort a, ushort b) => a > b ? a : b;

    private static int Clamp01(int v) => v < 0 ? 0 : (v > One ? One : v);
}