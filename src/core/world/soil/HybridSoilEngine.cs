using System;
using System.Runtime.CompilerServices;
using Game.Core;

namespace Game.Core.Soil;

/// <summary>
/// Hybrid Sparse & Event-Driven Soil Engine.
/// 
/// Объединяет:
/// 1. Sparse Farm Domain — точечный контур ферм колонии: O(N_farms), < 0.005 мс на все грядки.
/// 2. Wild Soil Domain — статичная дикая природа (0% CPU) + фоновый кольцевой пересчет
///    (Rolling Time-Sliced Chunks, 1 чанк 32x32 за физический шаг, ~0.01 мс) при глобальных событиях.
/// </summary>
public sealed class HybridSoilEngine
{
    public SparseFarmDomain Farms { get; }
    public WildSoilDomain WildSoil { get; }

    public int Width => WildSoil.Width;
    public int Height => WildSoil.Height;

    public HybridSoilEngine(int width, int height, int farmCapacity = 2048)
    {
        WildSoil = new WildSoilDomain(width, height);
        Farms = new SparseFarmDomain(width, height, farmCapacity);
    }

    public void Initialize(TileType[,] ground, float[,] moistureNoise, float[,] fertileNoise)
    {
        WildSoil.Initialize(ground, moistureNoise, fertileNoise);
    }

    /// <summary>
    /// Получить влажность в точке (x, y).
    /// Если клетка является фермой — мгновенно возвращает влажность грядки,
    /// иначе — статическую/дикую влажность ландшафта.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte GetHumidity(int x, int y)
    {
        int farmIdx = Farms.GetFarmIndex(x, y);
        if (farmIdx >= 0)
        {
            return Farms.GetFarmByIndex(farmIdx).Moisture;
        }
        return WildSoil.GetHumidity(x, y);
    }

    /// <summary>
    /// Получить плодородие в точке (x, y).
    /// Если ферма — возвращает плодородие фермы, иначе — дикое плодородие.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte GetFertility(int x, int y)
    {
        int farmIdx = Farms.GetFarmIndex(x, y);
        if (farmIdx >= 0)
        {
            return Farms.GetFarmByIndex(farmIdx).Fertility;
        }
        return WildSoil.GetFertility(x, y);
    }

    /// <summary>
    /// Регистрация новой грядки.
    /// </summary>
    public int RegisterFarm(int x, int y, byte soilQuality = 100, CropType initialCrop = CropType.None)
    {
        byte initialM = WildSoil.GetHumidity(x, y);
        byte initialF = WildSoil.GetFertility(x, y);
        int farmId = Farms.RegisterFarm(x, y, soilQuality, initialM, initialF);

        WildSoil.SetFlag(x, y, SoilCellFlags.Farm, true);

        if (initialCrop != CropType.None)
        {
            Farms.PlantCrop(farmId, initialCrop);
        }

        return farmId;
    }

    /// <summary>
    /// Удаление грядки.
    /// </summary>
    public bool UnregisterFarm(int x, int y)
    {
        WildSoil.SetFlag(x, y, SoilCellFlags.Farm, false);
        return Farms.UnregisterFarm(x, y);
    }

    /// <summary>
    /// Полив клетки (ручной или ведрами).
    /// </summary>
    public void Water(int x, int y, byte amount)
    {
        if (!Farms.WaterAt(x, y, amount))
        {
            byte cur = WildSoil.GetHumidity(x, y);
            WildSoil.SetHumidity(x, y, (byte)Math.Min(255, cur + amount));
        }
    }

    /// <summary>
    /// Истощение / обогащение клетки после сбора урожая с поддержкой севооборота.
    /// </summary>
    public void DrainAfterHarvest(int x, int y, CropType cropHint = CropType.None)
    {
        Farms.DrainAfterHarvestAt(x, y, cropHint);
    }

    /// <summary>
    /// Внесение минеральных удобрений.
    /// </summary>
    public bool ApplyFertilizer(int x, int y, byte amount)
    {
        int farmIdx = Farms.GetFarmIndex(x, y);
        if (farmIdx < 0) return false;
        return Farms.ApplyFertilizer(Farms.GetFarmByIndex(farmIdx).Id, amount);
    }

    /// <summary>
    /// Внесение органического компоста.
    /// </summary>
    public bool ApplyCompost(int x, int y, byte amount)
    {
        int farmIdx = Farms.GetFarmIndex(x, y);
        if (farmIdx < 0) return false;
        return Farms.ApplyCompost(Farms.GetFarmByIndex(farmIdx).Id, amount);
    }

    /// <summary>
    /// Прополка сорняков.
    /// </summary>
    public bool RemoveWeeds(int x, int y)
    {
        int farmIdx = Farms.GetFarmIndex(x, y);
        if (farmIdx < 0) return false;
        return Farms.RemoveWeeds(Farms.GetFarmByIndex(farmIdx).Id);
    }

    /// <summary>
    /// Посадка культуры на грядку (с севооборотом).
    /// </summary>
    public bool PlantCrop(int x, int y, CropType crop)
    {
        int farmIdx = Farms.GetFarmIndex(x, y);
        if (farmIdx < 0)
        {
            // Автоматически регистрируем грядку, если еще не была зарегистрирована
            byte baseQual = (byte)Math.Clamp((int)WildSoil.GetFertility(x, y), 50, 200);
            RegisterFarm(x, y, baseQual, crop);
            return true;
        }
        return Farms.PlantCrop(Farms.GetFarmByIndex(farmIdx).Id, crop);
    }

    /// <summary>
    /// Создание оросительного канала (копка канала).
    /// </summary>
    public void DigCanal(int x, int y, int radius = 5)
    {
        WildSoil.DigCanal(x, y, radius);

        // Обновляем дистанцию для всех близлежащих ферм
        int r = radius;
        for (int dy = -r; dy <= r; dy++)
        {
            int cy = y + dy;
            if ((uint)cy >= (uint)Height) continue;
            for (int dx = -r; dx <= r; dx++)
            {
                int cx = x + dx;
                if ((uint)cx >= (uint)Width) continue;

                int dist = (int)MathF.Sqrt(dx * dx + dy * dy);
                if (dist <= r)
                {
                    Farms.SetCanalProximity(cx, cy, (byte)Math.Max(1, dist));
                }
            }
        }
    }

    /// <summary>
    /// Запуск событий природы.
    /// </summary>
    public void TriggerSeasonChange(float season) => WildSoil.TriggerSeasonChange(season);
    public void TriggerDrought(byte severity = 2) => WildSoil.TriggerDrought(severity);
    public void TriggerRainSeason(byte intensity = 4) => WildSoil.TriggerRainSeason(intensity);

    /// <summary>
    /// Главный физический шаг движка почвы:
    /// 1. Sparse Farm Domain — точечный обсчет всех активных грядок (< 0.005 мс).
    /// 2. Wild Soil Domain — 0% CPU в покое, либо ровно 1 чанк 32x32 при событиях (~0.01 мс).
    /// Суммарное время тика: 0.013 – 0.035 мс. Zero-GC.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Tick(float season = 0f, bool isRaining = false, int stepMultiplier = 1)
    {
        // 1. Точечный обсчет грядок колонии
        Farms.Tick(season, isRaining, stepMultiplier);

        // 2. Фоновый размазанный пересчет дикой природы (если активно событие)
        WildSoil.StepRollingChunk(stepMultiplier);
    }
}
