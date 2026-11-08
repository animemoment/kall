using System;
using System.Runtime.CompilerServices;
using Game.Core;

namespace Game.Core.Soil;

/// <summary>
/// Домен дикой почвы (Static Base + Event / Season Trigger).
/// 
/// Архитектура:
/// 1. В нормальном режиме дикая почва НЕ тикает: 0% CPU, абсолютный покой.
///    Значения отдаются из статичного кэша/шума мгновенно.
/// 2. При глобальных событиях (смена сезона, засуха, сезон дождей, новые каналы)
///    запускается кольцевой Time-Sliced пересчет: ровно 1 чанк (32x32 = 1024 клетки)
///    за физический шаг (~0.01 мс).
/// 3. За 256 шагов (при карте 512x512) весь мир плавно обновляется без просадок FPS.
/// </summary>
public sealed class WildSoilDomain
{
    public const int ChunkSize = 32;
    public const int MaxMoisture = 200;
    public const int WaterMoisture = 200;
    public const int MinMoisture = 10;
    public const int MaxFertility = 200;
    public const int WaterSpreadRadius = 10;

    public int Width { get; }
    public int Height { get; }
    public int ChunksX { get; }
    public int ChunksY { get; }
    public int TotalChunks { get; }

    // Слои данных дикой почвы (256 КБ на слой при 512x512)
    private readonly byte[] _humidityCur;
    private readonly byte[] _fertilityCur;
    private readonly byte[] _humidityBase;
    private readonly byte[] _fertilityBase;
    private readonly byte[] _waterFeed;
    private readonly SoilCellFlags[] _flags;

    // Кольцевой планировщик (Rolling Time-Sliced Chunks)
    private int _rollingChunkIndex = 0;
    private int _remainingRollingSteps = 0;
    private bool _isRollingActive = false;

    // Параметры текущего события
    private float _currentSeason = 0f;
    private int _eventEvapModifier = 0;
    private int _eventRainModifier = 0;

    public bool IsRollingActive => _isRollingActive;
    public int RemainingSteps => _remainingRollingSteps;
    public int CurrentRollingChunk => _rollingChunkIndex;

    public WildSoilDomain(int width, int height)
    {
        Width = width;
        Height = height;
        int size = width * height;

        ChunksX = (width + ChunkSize - 1) / ChunkSize;
        ChunksY = (height + ChunkSize - 1) / ChunkSize;
        TotalChunks = ChunksX * ChunksY;

        _humidityCur = new byte[size];
        _fertilityCur = new byte[size];
        _humidityBase = new byte[size];
        _fertilityBase = new byte[size];
        _waterFeed = new byte[size];
        _flags = new SoilCellFlags[size];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte GetHumidity(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return MinMoisture;
        return _humidityCur[y * Width + x];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte GetFertility(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return 0;
        return _fertilityCur[y * Width + x];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SoilCellFlags GetFlags(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return SoilCellFlags.None;
        return _flags[y * Width + x];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetFlag(int x, int y, SoilCellFlags flag, bool enable)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return;
        int idx = y * Width + x;
        if (enable) _flags[idx] |= flag;
        else _flags[idx] &= ~flag;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetHumidity(int x, int y, byte value)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return;
        _humidityCur[y * Width + x] = (byte)Math.Clamp((int)value, MinMoisture, 255);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetFertility(int x, int y, byte value)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return;
        _fertilityCur[y * Width + x] = (byte)Math.Clamp((int)value, 0, MaxFertility);
    }

    /// <summary>
    /// Инициализация базового ландшафта и прекомпьют статической подпитки водой.
    /// </summary>
    public void Initialize(TileType[,] ground, float[,] moistureNoise, float[,] fertileNoise)
    {
        int w = Math.Min(Width, ground.GetLength(0));
        int h = Math.Min(Height, ground.GetLength(1));
        int nw = moistureNoise.GetLength(0);
        int nh = moistureNoise.GetLength(1);

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = y * Width + x;
                TileType t = ground[x, y];
                SoilCellFlags flags = SoilCellFlags.None;

                if (t == TileType.Water)
                {
                    flags |= SoilCellFlags.Water;
                    _humidityCur[idx] = WaterMoisture;
                    _fertilityCur[idx] = 0;
                }
                else
                {
                    if (t == TileType.Grass) flags |= SoilCellFlags.Grass;
                    else if (t == TileType.Mountain) flags |= SoilCellFlags.Mountain;

                    float mn = (x < nw && y < nh) ? moistureNoise[x, y] : 0.5f;
                    _humidityCur[idx] = (byte)(MinMoisture + (int)(mn * 100f));

                    if (t == TileType.Grass && fertileNoise != null)
                    {
                        float fn = fertileNoise[x, y];
                        int fv = Math.Clamp((int)(fn * (MaxFertility + 1)), 0, MaxFertility);
                        _fertilityCur[idx] = (byte)fv;
                    }
                    else
                    {
                        _fertilityCur[idx] = 0;
                    }
                }

                _flags[idx] = flags;
            }
        }

        RebuildWaterFeed();

        // Первичный градиент влаги от водоёмов
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = y * Width + x;
                if ((_flags[idx] & SoilCellFlags.Water) != 0) continue;

                int feed = _waterFeed[idx];
                if (feed > 0)
                {
                    int hum = _humidityCur[idx] + (int)(60f * MathF.Sqrt(feed / (float)WaterSpreadRadius));
                    _humidityCur[idx] = (byte)Math.Clamp(hum, MinMoisture, 255);

                    if ((_flags[idx] & SoilCellFlags.Grass) != 0)
                    {
                        int fert = _fertilityCur[idx] + 40;
                        _fertilityCur[idx] = (byte)Math.Clamp(fert, 0, MaxFertility);
                    }
                }
            }
        }

        Array.Copy(_humidityCur, _humidityBase, _humidityCur.Length);
        Array.Copy(_fertilityCur, _fertilityBase, _fertilityCur.Length);

        // По умолчанию режим покоя: 0% CPU!
        _isRollingActive = false;
        _remainingRollingSteps = 0;
        _rollingChunkIndex = 0;
    }

    /// <summary>
    /// Пересчет подпитки от водоёмов по алгоритму BFS.
    /// </summary>
    public void RebuildWaterFeed()
    {
        int w = Width;
        int h = Height;
        const int R = WaterSpreadRadius;
        Array.Clear(_waterFeed, 0, _waterFeed.Length);

        int[] queue = new int[w * h];
        int head = 0, tail = 0;

        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                int idx = row + x;
                if ((_flags[idx] & SoilCellFlags.Water) == 0) continue;

                for (int oy = -1; oy <= 1; oy++)
                {
                    int ny = y + oy;
                    if ((uint)ny >= (uint)h) continue;
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        if (ox == 0 && oy == 0) continue;
                        int nx = x + ox;
                        if ((uint)nx >= (uint)w) continue;
                        int nidx = ny * w + nx;
                        if ((_flags[nidx] & SoilCellFlags.Water) != 0) continue;
                        if (_waterFeed[nidx] != 0) continue;
                        _waterFeed[nidx] = (byte)R;
                        queue[tail++] = (ny * w + nx);
                    }
                }
            }
        }

        while (head < tail)
        {
            int code = queue[head++];
            int cx = code % w, cy = code / w;
            int cidx = cy * w + cx;
            int strength = _waterFeed[cidx];
            if (strength <= 1) continue;

            byte nextStrength = (byte)(strength - 1);
            for (int oy = -1; oy <= 1; oy++)
            {
                int ny = cy + oy;
                if ((uint)ny >= (uint)h) continue;
                for (int ox = -1; ox <= 1; ox++)
                {
                    if (ox == 0 && oy == 0) continue;
                    int nx = cx + ox;
                    if ((uint)nx >= (uint)w) continue;
                    int nidx = ny * w + nx;
                    if ((_flags[nidx] & SoilCellFlags.Water) != 0) continue;
                    if (_waterFeed[nidx] != 0) continue;
                    _waterFeed[nidx] = nextStrength;
                    queue[tail++] = (ny * w + nx);
                }
            }
        }
    }

    /// <summary>
    /// Копка канала: создает воду и обновляет подпитку вокруг точки.
    /// </summary>
    public void DigCanal(int canalX, int canalY, int radius = 5)
    {
        if ((uint)canalX >= (uint)Width || (uint)canalY >= (uint)Height) return;

        int idx = canalY * Width + canalX;
        _flags[idx] |= SoilCellFlags.Water;
        _humidityCur[idx] = WaterMoisture;
        _fertilityCur[idx] = 0;

        for (int dy = -radius; dy <= radius; dy++)
        {
            int y = canalY + dy;
            if ((uint)y >= (uint)Height) continue;
            for (int dx = -radius; dx <= radius; dx++)
            {
                int x = canalX + dx;
                if ((uint)x >= (uint)Width) continue;

                int dist = (int)MathF.Sqrt(dx * dx + dy * dy);
                if (dist <= radius)
                {
                    int cidx = y * Width + x;
                    int feedVal = Math.Max(0, WaterSpreadRadius - dist);
                    if (feedVal > _waterFeed[cidx])
                        _waterFeed[cidx] = (byte)feedVal;
                }
            }
        }

        TriggerRollingCycle(passes: 1);
    }

    /// <summary>
    /// Запуск кольцевого фонового обновления (Rolling Time-Sliced Chunks).
    /// </summary>
    public void TriggerRollingCycle(int passes = 1, float season = 0f, int evapMod = 0, int rainMod = 0)
    {
        _currentSeason = season;
        _eventEvapModifier = evapMod;
        _eventRainModifier = rainMod;
        _remainingRollingSteps = TotalChunks * passes;
        _isRollingActive = true;
    }

    public void TriggerSeasonChange(float season)
    {
        TriggerRollingCycle(passes: 1, season: season);
    }

    public void TriggerDrought(byte severity = 2)
    {
        TriggerRollingCycle(passes: 1, season: 0.5f, evapMod: severity, rainMod: -2);
    }

    public void TriggerRainSeason(byte intensity = 4)
    {
        TriggerRollingCycle(passes: 1, season: 0.0f, evapMod: -1, rainMod: intensity);
    }

    /// <summary>
    /// Шаг одного физического кадра:
    /// - Если режим покоя: 0% CPU (мгновенный возврат).
    /// - Если активно событие: пересчитывает РОВНО 1 ЧАНК 32x32 (~0.01 мс) без влияния на FPS.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe bool StepRollingChunk(int stepMultiplier = 1)
    {
        if (!_isRollingActive || _remainingRollingSteps <= 0)
        {
            _isRollingActive = false;
            return false;
        }

        int chunkIdx = _rollingChunkIndex;
        _rollingChunkIndex = (_rollingChunkIndex + 1) % TotalChunks;
        _remainingRollingSteps--;

        int cx = chunkIdx % ChunksX;
        int cy = chunkIdx / ChunksX;

        ProcessSingleChunk(cx, cy, stepMultiplier);

        if (_remainingRollingSteps <= 0)
        {
            _isRollingActive = false;
        }

        return true;
    }

    /// <summary>
    /// Пересчет ровно одного чанка 32x32 клеток (1024 клетки = 1 КБ).
    /// Выполняется in-place (Gauss-Seidel) за один проход по L1 кэшу.
    /// Время выполнения: ~0.008 - 0.012 мс. Zero-GC.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.AggressiveInlining)]
    private unsafe void ProcessSingleChunk(int cx, int cy, int stepMultiplier)
    {
        int w = Width;
        int h = Height;
        int chunkXStart = cx * ChunkSize;
        int chunkYStart = cy * ChunkSize;
        int chunkXEnd = Math.Min(chunkXStart + ChunkSize, w);
        int chunkYEnd = Math.Min(chunkYStart + ChunkSize, h);

        int seasonDelta = (_currentSeason > 0.33f ? 1 : _currentSeason < -0.33f ? -1 : 0) * stepMultiplier;
        int rainDelta = _eventRainModifier * stepMultiplier;
        int evapExtra = _eventEvapModifier * stepMultiplier;
        int diffShift = Math.Max(2, 5 - (stepMultiplier > 1 ? 1 : 0));

        fixed (byte* hCur = _humidityCur, feed = _waterFeed, fCur = _fertilityCur, fBase = _fertilityBase)
        fixed (SoilCellFlags* flags = _flags)
        {
            for (int y = chunkYStart; y < chunkYEnd; y++)
            {
                int row = y * w;
                int rowHash = (y * 19349663) ^ 0x5bd1e995;

                byte* phCur = hCur + row + chunkXStart;
                byte* pFeed = feed + row + chunkXStart;
                byte* pfCur = fCur + row + chunkXStart;
                byte* pfBase = fBase + row + chunkXStart;
                SoilCellFlags* pFlags = flags + row + chunkXStart;

                int xHash = chunkXStart * 73856093;

                for (int x = chunkXStart; x < chunkXEnd; x++, phCur++, pFeed++, pfCur++, pfBase++, pFlags++, xHash += 73856093)
                {
                    SoilCellFlags fl = *pFlags;

                    if ((fl & SoilCellFlags.Water) != 0)
                    {
                        *phCur = WaterMoisture;
                        *pfCur = 0;
                        continue;
                    }

                    int hMe = *phCur;
                    int hLeft = x > 0 ? *(phCur - 1) : hMe;
                    int hRight = x + 1 < w ? *(phCur + 1) : hMe;
                    int hUp = y > 0 ? *(phCur - w) : hMe;
                    int hDown = y + 1 < h ? *(phCur + w) : hMe;

                    int hv;
                    if ((fl & SoilCellFlags.Downhill) != 0)
                    {
                        int avg = (hLeft + hRight + (hDown << 1) + (hUp >> 1) + hMe) / 5;
                        hv = hMe + (((avg - hMe) * stepMultiplier) >> 4);
                    }
                    else
                    {
                        int avg = (hLeft + hRight + hUp + hDown) >> 2;
                        hv = hMe + (((avg - hMe) * stepMultiplier) >> diffShift);
                    }

                    int f = *pFeed;
                    if (f > 7) hv += 3 * stepMultiplier;
                    else if (f > 0) hv += 1 * stepMultiplier;

                    int cellHash = xHash ^ rowHash;
                    int evap = 0;
                    if ((cellHash & 3) == 0) evap = 1;
                    if ((fl & SoilCellFlags.High) != 0 && ((cellHash & 1) == 0)) evap += 1;
                    if ((fl & SoilCellFlags.Forest) != 0) evap = 0;

                    hv -= (evap + evapExtra) * stepMultiplier;
                    hv += seasonDelta + rainDelta;

                    if ((fl & SoilCellFlags.High) != 0)
                        hv = hMe + ((hv - hMe) >> 2);

                    if (hv < MinMoisture) hv = MinMoisture;
                    else if (hv > 255) hv = 255;
                    *phCur = (byte)hv;

                    if ((fl & SoilCellFlags.Grass) == 0)
                    {
                        *pfCur = 0;
                    }
                    else
                    {
                        int fMe = *pfCur;
                        int fLeft = x > 0 ? *(pfCur - 1) : fMe;
                        int fRight = x + 1 < w ? *(pfCur + 1) : fMe;
                        int fUp = y > 0 ? *(pfCur - w) : fMe;
                        int fDown = y + 1 < h ? *(pfCur + w) : fMe;
                        int fAvg = (fLeft + fRight + fUp + fDown) >> 2;

                        int fv = fMe + (((fAvg - fMe) * stepMultiplier) >> 4);

                        if ((fl & SoilCellFlags.Forest) != 0 && ((cellHash & 7) == 0))
                            fv += 1 * stepMultiplier;

                        if (fMe < *pfBase && (((cellHash >> 4) & 7) == 0))
                        {
                            fv += 1 * stepMultiplier;
                            if (fv > *pfBase) fv = *pfBase;
                        }

                        if (fv < 0) fv = 0;
                        else if (fv > MaxFertility) fv = MaxFertility;
                        *pfCur = (byte)fv;
                    }
                }
            }
        }
    }
}
