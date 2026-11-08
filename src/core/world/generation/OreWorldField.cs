using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Game.Core.WorldClimate;
using Game.Core.WorldStreaming;

namespace Game.Core;

/// <summary>
/// Руда СТРИМИНГОВОГО МИРА (task-8): детерминированное поле по АБСОЛЮТНЫМ мировым
/// координатам. Остров — 0.0000002% планеты, практически вся руда живёт за кромкой,
/// поэтому мир получает тот же геологический пасс, что и остров:
/// те же <see cref="OreGeologyConfig"/> и <see cref="OreSuitability"/>, та же форма
/// тел и спутники. Отличается только источник данных клетки: канонический сэмплер
/// <c>WorldGenV2.SampleCell + WorldClimateGenerator.SampleBlendedCell</c>, а не
/// <c>MapData.Ground</c> своего окна — поэтому стык чанков не рвётся.
///
/// Единица кэша — чанк 64×64. Кандидаты перебираются на сетке 24 с запасом
/// <see cref="Margin"/> вокруг чанка: центры соседних тел/спутников, чьи клетки
/// попадают в чанк, обрабатываются одинаково в любом чанке (бесшовность).
///
/// Координаты: публичные методы принимают ЛОКАЛЬНЫЙ ХОЛСТ (как задания/рендер),
/// внутри переводятся в абсолютные: <c>abs = WorldOrigin + local</c> — одна дверь
/// (<see cref="UnifiedWorldGenerator.WorldOriginX"/>).
///
/// Мутации (добытый запас/выработка) НЕ входят в это поле — они живут в
/// <see cref="OreWorldState"/> и накладываются поверх базового поля.
/// Чистый C#, без Godot API.
/// </summary>
public static class OreWorldField
{
    public const int ChunkSide = WorldCoordinates.ChunkSize;
    public const int ChunkCells = ChunkSide * ChunkSide;

    /// <summary>Запас вокруг чанка: покрывает тело + спутники соседних центров.</summary>
    private const int Margin = 96;

    /// <summary>Кэш чанков (16 КБ на чанк): ~2 000 чанков = 32 МБ максимум.</summary>
    private const int CacheCap = 2048;

    private static readonly ConcurrentDictionary<long, ChunkOre> Cache = new();
    private static int _cached;

    // AUDIT task-17 (blocker): кэш адресуется ЛОКАЛЬНЫМИ чанками, а содержимое
    // чанка зависит от (seed, ver, WorldOrigin) — ComputeChunk читает
    // UnifiedWorldGenerator.WorldOriginX/Y напрямую. Раньше единственной защитой
    // был явный ClearCache(), который живёт в StreamingWorldView.ResetWorld и
    // вызывается ПОСЛЕ генерации новой карты (Main.OnRegionSelected: Fill идёт
    // в Task.Run до ResetWorld) → смена региона/сида отдавала руду ПРЕДЫДУЩЕГО
    // мира для всех уже закэшированных чанков (остров 1024² = 256 чанков, они
    // почти всегда в кэше). Теперь кэш сам замечает смену мира: ключ чанка
    // дополнен идентичностью мира, при несовпадении кэш сбрасывается.
    private static readonly object CacheWorldGate = new();
    private static ulong _cacheWorldSeed;
    private static uint _cacheWorldVer;
    private static long _cacheWorldOriginX = long.MinValue;
    private static long _cacheWorldOriginY = long.MinValue;

    /// <summary>
    /// Сбросить кэш, если мир (seed/ver/WorldOrigin) сменился. FillMapData зовёт
    /// EnsureChunk из Parallel.For, поэтому быстрый путь — только чтение полей;
    /// реальная очистка происходит один раз на смену мира, под локом.
    /// </summary>
    private static void EnsureCacheWorld(ulong seed, uint ver)
    {
        long originX = UnifiedWorldGenerator.WorldOriginX;
        long originY = UnifiedWorldGenerator.WorldOriginY;
        if (_cacheWorldSeed == seed && _cacheWorldVer == ver
            && _cacheWorldOriginX == originX && _cacheWorldOriginY == originY)
            return;

        lock (CacheWorldGate)
        {
            if (_cacheWorldSeed == seed && _cacheWorldVer == ver
                && _cacheWorldOriginX == originX && _cacheWorldOriginY == originY)
                return;
            Cache.Clear();
            System.Threading.Interlocked.Exchange(ref _cached, 0);
            // AUDIT task-17 (P4-1): знание «где руда» тоже привязано к миру.
            KnownChunks.Clear();
            KnownOreBits.Clear();
            System.Threading.Interlocked.Exchange(ref _knownCount, 0);
            _cacheWorldSeed = seed;
            _cacheWorldVer = ver;
            _cacheWorldOriginX = originX;
            _cacheWorldOriginY = originY;
        }
    }

    private const int Stride = OreGeologyConfig.CandidateStride;
    private const int One = Q16.One;

    // 8 направлений (0=E,1=SE,2=S,3=SW,4=W,5=NW,6=N,7=NE).
    private static readonly int[] DirX = { 1, 1, 0, -1, -1, -1, 0, 1 };
    private static readonly int[] DirY = { 0, 1, 1, 1, 0, -1, -1, -1 };

    /// <summary>Базовое (немутированное) поле руды одного чанка.</summary>
    public sealed class ChunkOre
    {
        /// <summary>Тип руды по локальному индексу y*64+x (0 = нет).</summary>
        public readonly byte[] Types = new byte[ChunkCells];
        /// <summary>Запас при генерации (initial) для того же индекса.</summary>
        public readonly ushort[] Reserves = new ushort[ChunkCells];
    }

    /// <summary>
    /// AUDIT task-17 (P4-1): БЕСПЛАТНАЯ проверка «есть ли руда» по УЖЕ посчитанному чанку —
    /// для гейтов постройки/зон в мире (без тяжёлого синхронного семпла поля).
    /// Возвращает false, если поле чанка ещё не считалось (вызывающий решает:
    /// для стен/станков — консервативно запретить, для зон — разрешить).
    ///
    /// ВНИМАНИЕ: читает эфемерный <see cref="Cache"/>. Для геймплейных гейтов используйте
    /// <see cref="TryPeekKnownOreLocal"/> — он переживает эвикцию кэша.
    /// </summary>
    public static bool TryPeekCachedOreLocal(int localX, int localY, out bool ore)
    {
        ore = false;
        long cx = WorldCoordinates.FloorDiv(localX, ChunkSide);
        long cy = WorldCoordinates.FloorDiv(localY, ChunkSide);
        long key = PackChunk(cx, cy);
        if (!Cache.TryGetValue(key, out ChunkOre chunk))
            return false; // поле ещё не посчитано — «неизвестно»

        int index = WorldCoordinates.FloorMod(localY, ChunkSide) * ChunkSide
                  + WorldCoordinates.FloorMod(localX, ChunkSide);
        byte type = chunk.Types[index];
        if (type == OreConfig.None)
            return true;
        // Выработанная клетка рудой не считается (мутации — OreWorldState).
        if (OreWorldState.TryGet(localX, localY, out ushort reserve) && reserve == 0)
            return true;
        ore = true;
        return true;
    }

    // ------------------------------------------------------------------ //
    //  AUDIT task-17 (P4-1): «ИЗВЕСТНАЯ» РУДА ДЛЯ ГЕЙМПЛЕЙН-ГЕЙТОВ        //
    //                                                                      //
    //  Cache выше — эфемерный: при превышении CacheCap вызывается Clear(), //
    //  при смене мира — ClearCache(). Гейты мира (WorldGameRules/          //
    //  ConstructionManager/BuildTool/JobValidator) раньше спрашивали ИМЕННО //
    //  Cache и при «неизвестно» ЗАПРЕЩАЛИ строить: после эвикции/неудачи    //
    //  ore-джобы в уже отрисованных клетках игрок не мог поставить стену    //
    //  «без причины» — геймплей зависел от расписания рендера.             //
    //                                                                      //
    //  Здесь — компактный набор «поле чанка считалось» + битсет «в каких    //
    //  клетках была руда», который эвикцией Cache НЕ сбрасывается.         //
    //  Семантика гейтов: неизвестно → НЕ блокируем; известно, что руда есть //
    //  (и не выработана) → блокируем; известно, что руды нет → пропускаем. //
    // ------------------------------------------------------------------ //

    /// <summary>Потолок «известных» чанков (страховка от роста: 512 Б битсета на чанк).</summary>
    private const int KnownCap = 16384;

    private static readonly ConcurrentDictionary<long, byte> KnownChunks = new();
    private static readonly ConcurrentDictionary<long, ulong[]> KnownOreBits = new();
    private static int _knownCount;

    /// <summary>Запомнить «поле чанка посчитано» и битсет клеток с рудой (идемпотентно).</summary>
    private static void RememberKnown(long chunkKey, ChunkOre chunk)
    {
        if (chunk == null)
            return;
        if (!KnownChunks.ContainsKey(chunkKey))
        {
            KnownChunks[chunkKey] = 1;
            if (System.Threading.Interlocked.Increment(ref _knownCount) > KnownCap)
            {
                // Потолок достигнут: знание теряем целиком (гейты станут «разрешать»
                // неизвестное — безопаснее, чем навсегда запретить видимую клетку).
                KnownChunks.Clear();
                KnownOreBits.Clear();
                System.Threading.Interlocked.Exchange(ref _knownCount, 0);
                KnownChunks[chunkKey] = 1;
                System.Threading.Interlocked.Exchange(ref _knownCount, 1);
            }
        }
        if (KnownOreBits.ContainsKey(chunkKey))
            return;

        bool any = false;
        var bits = new ulong[ChunkCells / 64];
        for (int i = 0; i < ChunkCells; i++)
        {
            if (chunk.Types[i] == OreConfig.None)
                continue;
            bits[i >> 6] |= 1UL << (i & 63);
            any = true;
        }
        if (any)
            KnownOreBits[chunkKey] = bits;
    }

    /// <summary>
    /// AUDIT task-17 (P4-1): есть ли руда в клетке по «известному» набору, который
    /// НЕ зависит от эвикции <see cref="Cache"/>. false — поле чанка не считалось
    /// («неизвестно»; гейты это трактуют как «не блокировать»).
    /// </summary>
    public static bool TryPeekKnownOreLocal(int localX, int localY, out bool ore)
    {
        ore = false;
        long cx = WorldCoordinates.FloorDiv(localX, ChunkSide);
        long cy = WorldCoordinates.FloorDiv(localY, ChunkSide);
        long key = PackChunk(cx, cy);
        if (!KnownChunks.ContainsKey(key))
            return false; // чанк не считался — неизвестно
        if (KnownOreBits.TryGetValue(key, out ulong[] bits))
        {
            int index = WorldCoordinates.FloorMod(localY, ChunkSide) * ChunkSide
                      + WorldCoordinates.FloorMod(localX, ChunkSide);
            if ((bits[index >> 6] & (1UL << (index & 63))) != 0)
            {
                // Выработанную клетку рудой не считаем (мутации — OreWorldState).
                if (!(OreWorldState.TryGet(localX, localY, out ushort reserve) && reserve == 0))
                    ore = true;
            }
        }
        return true; // чанк известен; конкретика — в ore
    }

    /// <summary>Известно ли поле руды чанка клетки (диагностика/тесты).</summary>
    public static bool IsChunkKnownLocal(int localX, int localY)
        => KnownChunks.ContainsKey(PackChunk(
            WorldCoordinates.FloorDiv(localX, ChunkSide),
            WorldCoordinates.FloorDiv(localY, ChunkSide)));

    /// <summary>
    /// Гарантировать, что базовое поле чанка посчитано (кэш). Чанковые координаты —
    /// ЛОКАЛЬНОГО ХОЛСТА (то же, что <c>WorldCoordinates.ChunkForTile</c> от local).
    /// </summary>
    public static ChunkOre EnsureChunk(long chunkX, long chunkY, ulong seed, uint ver)
    {
        // AUDIT task-17: кэш валиден только внутри одного мира (seed/ver/origin).
        EnsureCacheWorld(seed, ver);
        long key = PackChunk(chunkX, chunkY);
        if (Cache.TryGetValue(key, out ChunkOre cached))
        {
            // AUDIT task-17 (P4-1): «известную руду» помним отдельно от Cache —
            // эвикция кэша не должна стирать знание для гейтов.
            RememberKnown(key, cached);
            return cached;
        }
        ChunkOre computed = ComputeChunk(chunkX, chunkY, seed, ver);
        ChunkOre race = Cache.GetOrAdd(key, computed);
        if (ReferenceEquals(race, computed))
        {
            if (System.Threading.Interlocked.Increment(ref _cached) > CacheCap)
            {
                Cache.Clear();
                System.Threading.Interlocked.Exchange(ref _cached, 0);
            }
        }
        RememberKnown(key, race);
        return race;
    }

    /// <summary>
    /// Сбросить кэш поля. ОБЯЗАТЕЛЬНО при смене WorldOrigin/сида мира: кэш
    /// адресуется локальными чанками, а смысл локальных координат меняет origin.
    /// </summary>
    public static void ClearCache()
    {
        Cache.Clear();
        System.Threading.Interlocked.Exchange(ref _cached, 0);
        // AUDIT task-17 (P4-1): «известная руда» тоже адресуется локальными чанками —
        // при смене мира знание невалидно (иначе старые жилы блокировали бы постройку
        // в новом мире).
        KnownChunks.Clear();
        KnownOreBits.Clear();
        System.Threading.Interlocked.Exchange(ref _knownCount, 0);
        lock (CacheWorldGate)
        {
            // AUDIT task-17: сброс идентичности мира — следующий EnsureChunk
            // переустановит её и (если мир уже другой) не оставит полупустой кэш
            // с чанками старого мира.
            _cacheWorldOriginX = long.MinValue;
            _cacheWorldOriginY = long.MinValue;
            _cacheWorldSeed = 0;
            _cacheWorldVer = 0;
        }
        lock (AnchorLock)
        {
            _anchors = Array.Empty<StartAnchor>();
            _anchorOriginX = long.MinValue;
            _anchorOriginY = long.MinValue;
        }
    }

    /// <summary>
    /// Остров и мир используют ОДНО поле (task-9): заполнить плоские массивы
    /// MapData из OreWorldField по абсолютным координатам. Два источника истины
    /// (островной оконный проход и мировое поле) устранены — поэтому на кромке
    /// 1024 тип/запас/форма совпадают клетка в клетку.
    /// <paramref name="canvasOriginX/Y"/> — координаты левого-верхнего угла окна
    /// в ЛОКАЛЬНОМ ХОЛСТЕ (обычно 0; для окна со смещением — origin окна).
    /// Возвращает число чанков с рудой (диагностика).
    /// </summary>
    public static int FillMapData(MapData data, ulong seed, uint ver,
        long canvasOriginX, long canvasOriginY)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));
        Array.Clear(data.OreType, 0, data.OreType.Length);
        Array.Clear(data.OreState, 0, data.OreState.Length);
        Array.Clear(data.OreReserve, 0, data.OreReserve.Length);
        Array.Clear(data.OreReserveInitial, 0, data.OreReserveInitial.Length);
        int w = data.Width;
        int h = data.Height;
        if (w <= 0 || h <= 0)
            return 0;

        // Стартовые якоря — часть поля: гарантируем, что они посчитаны до
        // параллельной заливки (один расчёт на сид/origin).
        EnsureStartAnchors(seed, ver);

        long firstChunkX = WorldCoordinates.FloorDiv(canvasOriginX, ChunkSide);
        long lastChunkX = WorldCoordinates.FloorDiv(canvasOriginX + w - 1, ChunkSide);
        long firstChunkY = WorldCoordinates.FloorDiv(canvasOriginY, ChunkSide);
        long lastChunkY = WorldCoordinates.FloorDiv(canvasOriginY + h - 1, ChunkSide);
        int rows = (int)(lastChunkY - firstChunkY + 1);
        int oreChunks = 0;
        object countLock = new();

        // Параллельно по строкам чанков: чанк-семплы независимы и детерминированы
        // (кэш поля — ConcurrentDictionary). Окно 1024² = 256 чанков: ~1 c в один
        // поток, поэтому параллель обязательна для загрузки карты.
        Parallel.For(0, rows, row =>
        {
            long chunkY = firstChunkY + row;
            int oreChunksRow = 0;
            for (long chunkX = firstChunkX; chunkX <= lastChunkX; chunkX++)
            {
                ChunkOre chunk = EnsureChunk(chunkX, chunkY, seed, ver);
                long chunkCanvasX = chunkX * (long)ChunkSide;
                long chunkCanvasY = chunkY * (long)ChunkSide;
                bool any = false;
                for (int i = 0; i < ChunkCells; i++)
                {
                    byte type = chunk.Types[i];
                    if (type == OreConfig.None)
                        continue;
                    long canvasX = chunkCanvasX + i % ChunkSide;
                    long canvasY = chunkCanvasY + i / ChunkSide;
                    long dx = canvasX - canvasOriginX;
                    long dy = canvasY - canvasOriginY;
                    if ((ulong)dx >= (ulong)w || (ulong)dy >= (ulong)h)
                        continue;
                    int idx = (int)(dy * w + dx);
                    data.OreType[idx] = type;
                    data.OreReserve[idx] = chunk.Reserves[i];
                    data.OreReserveInitial[idx] = chunk.Reserves[i];
                    data.OreState[idx] = (byte)OreState.Rich;
                    any = true;
                }
                if (any)
                    oreChunksRow++;
            }
            if (oreChunksRow > 0)
            {
                lock (countLock)
                    oreChunks += oreChunksRow;
            }
        });

        // §7.5: на клетках руды гасим дерево/камень (как делал островной проход).
        // Скан 1M байт — дёшево (~1-2 мс) и только на загрузке карты.
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                if (data.OreType[row + x] == OreConfig.None)
                    continue;
                data.TreeOnGrass[x, y] = false;
                data.StoneOnGrass[x, y] = false;
            }
        }
        return oreChunks;
    }

    // ---------------- Стартовые гарантии как часть поля (task-9) ----------------

    /// <summary>Центр окна колонии 1024² в локальном холсте (как в MapRenderer).</summary>
    private const int StartCenterOffset = 512;

    private readonly struct StartAnchor
    {
        public readonly int Type;
        public readonly int Area;
        public readonly long AbsX;
        public readonly long AbsY;

        public StartAnchor(int type, int area, long absX, long absY)
        {
            Type = type;
            Area = area;
            AbsX = absX;
            AbsY = absY;
        }
    }

    private static readonly object AnchorLock = new();
    private static StartAnchor[] _anchors = Array.Empty<StartAnchor>();
    private static ulong _anchorSeed;
    private static uint _anchorVer;
    private static long _anchorOriginX = long.MinValue;
    private static long _anchorOriginY = long.MinValue;

    /// <summary>
    /// Стартовые залежи Fe/Cu (и, если есть пригодная провинция, Pb/Zn) в кольце
    /// 60..200 от центра окна колонии. Считаются ОДИН раз на (seed, version,
    /// WorldOrigin) и дальше входят в поле как обычные тела — поэтому они
    /// одинаковы для острова и мира и не являются «отдельным проходом».
    /// </summary>
    private static StartAnchor[] EnsureStartAnchors(ulong seed, uint ver)
    {
        long originX = UnifiedWorldGenerator.WorldOriginX;
        long originY = UnifiedWorldGenerator.WorldOriginY;
        lock (AnchorLock)
        {
            if (_anchorOriginX == originX && _anchorOriginY == originY &&
                _anchorSeed == seed && _anchorVer == ver)
                return _anchors;
            _anchors = ComputeStartAnchors(seed, ver, originX, originY);
            _anchorOriginX = originX;
            _anchorOriginY = originY;
            _anchorSeed = seed;
            _anchorVer = ver;
            return _anchors;
        }
    }

    private static StartAnchor[] ComputeStartAnchors(ulong seed, uint ver, long originX, long originY)
    {
        long spawnX = originX + StartCenterOffset;
        long spawnY = originY + StartCenterOffset;
        var grassCache = new Dictionary<long, bool>(512);
        int[] types = { 1, 11, 13, 14 }; // Fe, Cu, Pb (флюс), Zn
        var best = new ushort[types.Length];
        var bestX = new long[types.Length];
        var bestY = new long[types.Length];
        var have = new bool[types.Length];
        long firstX = 0, firstY = 0;
        long secondX = 0, secondY = 0;
        bool haveFirst = false, haveSecond = false;

        int ringMin = OreGeologyConfig.StartRingMin;
        int ringMax = OreGeologyConfig.StartRingMax;
        for (int dy = -ringMax; dy <= ringMax; dy += 8)
        {
            for (int dx = -ringMax; dx <= ringMax; dx += 8)
            {
                long r2 = (long)dx * dx + (long)dy * dy;
                if (r2 < (long)ringMin * ringMin || r2 > (long)ringMax * ringMax)
                    continue;
                long wx = spawnX + dx;
                long wy = spawnY + dy;
                if (!IsGrass(wx, wy, seed, ver, grassCache))
                    continue;
                if (!haveFirst)
                {
                    haveFirst = true;
                    firstX = wx;
                    firstY = wy;
                }
                else if (!haveSecond && (wx != firstX || wy != firstY))
                {
                    haveSecond = true;
                    secondX = wx;
                    secondY = wy;
                }

                OreProvinceSample prov = SampleProvince(wx, wy, seed, ver, out OreTypeEnv env);
                for (int i = 0; i < types.Length; i++)
                {
                    ushort s = OreSuitability.Score(types[i], in prov, in env);
                    if (!have[i] || s > best[i])
                    {
                        have[i] = true;
                        best[i] = s;
                        bestX[i] = wx;
                        bestY[i] = wy;
                    }
                }
            }
        }

        var result = new List<StartAnchor>(4);
        for (int i = 0; i < types.Length; i++)
        {
            bool required = i == 0 || i == 1; // Fe и Cu — всегда (гарантия играбельности)
            if (!required && (!have[i] || best[i] < OreGeologyConfig.CoverageSuitabilityMin))
                continue;

            long wx;
            long wy;
            if (have[i] && best[i] > 0)
            {
                wx = bestX[i];
                wy = bestY[i];
            }
            else if (i == 0)
            {
                wx = firstX;
                wy = firstY;
            }
            else
            {
                wx = secondX;
                wy = secondY;
            }
            if (wx == 0 && wy == 0)
                continue;

            // Разносим стартовые тела, чтобы они не «съели» друг друга.
            for (int k = 0; k < result.Count; k++)
            {
                long ddx = result[k].AbsX - wx;
                long ddy = result[k].AbsY - wy;
                if (ddx * ddx + ddy * ddy < (long)OreGeologyConfig.MinGap * OreGeologyConfig.MinGap)
                {
                    wx += 2 * OreGeologyConfig.MinGap;
                    wy += 0;
                }
            }

            ulong h = CoordinateHash.Hash(seed, ver, GenerationDomain.OreSatellite,
                wx, wy, lane: 300 + (ulong)i);
            int span = OreGeologyConfig.StartDepositMaxArea - OreGeologyConfig.StartDepositMinArea + 1;
            if (span < 1) span = 1;
            int area = OreGeologyConfig.StartDepositMinArea + (int)(h % (ulong)span);
            result.Add(new StartAnchor(types[i], area, wx, wy));
        }
        return result.ToArray();
    }

    /// <summary>Может ли тело стартового якоря попасть в этот чанк (запас + спутники)?</summary>
    private static bool AnchorTouchesChunk(long absX, long absY, long chunkAbsX, long chunkAbsY)
    {
        const long margin = OreGeologyConfig.MaxBlobRadius + 48;
        return absX >= chunkAbsX - margin && absX <= chunkAbsX + ChunkSide + margin
            && absY >= chunkAbsY - margin && absY <= chunkAbsY + ChunkSide + margin;
    }

    /// <summary>Тип руды в клетке локального холста (0 = нет).</summary>
    public static byte GetOreTypeLocal(int localX, int localY, ulong seed, uint ver)
    {
        if (!TryGetLocal(localX, localY, seed, ver, out ChunkOre chunk, out int index))
            return OreConfig.None;
        return chunk.Types[index];
    }

    /// <summary>Базовый запас (initial) клетки локального холста; 0 = руды нет.</summary>
    public static ushort GetBaseReserveLocal(int localX, int localY, ulong seed, uint ver)
    {
        if (!TryGetLocal(localX, localY, seed, ver, out ChunkOre chunk, out int index))
            return 0;
        return chunk.Reserves[index];
    }

    /// <summary>
    /// Фактический тип с учётом мутаций: выработанная клетка = 0.
    /// Рендер/гейты должны звать именно этот метод.
    /// </summary>
    public static byte GetEffectiveTypeLocal(int localX, int localY, ulong seed, uint ver)
    {
        byte type = GetOreTypeLocal(localX, localY, seed, ver);
        if (type == OreConfig.None)
            return 0;
        if (OreWorldState.TryGet(localX, localY, out ushort reserve) && reserve == 0)
            return 0; // выработка
        return type;
    }

    /// <summary>
    /// Нормализованная маска соседей того же типа руды (47 значений, task-3) для
    /// клетки локального холста. Учитывает мутации (выработанный сосед = нет руды).
    /// </summary>
    public static byte ComputeMaskLocal(int localX, int localY, ulong seed, uint ver)
    {
        byte type = GetEffectiveTypeLocal(localX, localY, seed, ver);
        if (type == OreConfig.None)
            return 0;

        bool n = NeighborIs(localX, localY - 1, type, seed, ver);
        bool e = NeighborIs(localX + 1, localY, type, seed, ver);
        bool s = NeighborIs(localX, localY + 1, type, seed, ver);
        bool w = NeighborIs(localX - 1, localY, type, seed, ver);
        byte mask = 0;
        if (n) mask |= OreMaskHelper.N;
        if (e) mask |= OreMaskHelper.E;
        if (s) mask |= OreMaskHelper.S;
        if (w) mask |= OreMaskHelper.W;
        if (n && e && NeighborIs(localX + 1, localY - 1, type, seed, ver)) mask |= OreMaskHelper.NE;
        if (e && s && NeighborIs(localX + 1, localY + 1, type, seed, ver)) mask |= OreMaskHelper.SE;
        if (s && w && NeighborIs(localX - 1, localY + 1, type, seed, ver)) mask |= OreMaskHelper.SW;
        if (w && n && NeighborIs(localX - 1, localY - 1, type, seed, ver)) mask |= OreMaskHelper.NW;
        return mask;
    }

    private static bool NeighborIs(int lx, int ly, byte type, ulong seed, uint ver)
        => GetEffectiveTypeLocal(lx, ly, seed, ver) == type;

    private static bool TryGetLocal(int localX, int localY, ulong seed, uint ver,
        out ChunkOre chunk, out int index)
    {
        long cx = WorldCoordinates.FloorDiv(localX, ChunkSide);
        long cy = WorldCoordinates.FloorDiv(localY, ChunkSide);
        chunk = EnsureChunk(cx, cy, seed, ver);
        index = WorldCoordinates.FloorMod(localY, ChunkSide) * ChunkSide
              + WorldCoordinates.FloorMod(localX, ChunkSide);
        return true;
    }

    // ================================================================== //
    //  Генерация чанка                                                    //
    // ================================================================== //

    private static ChunkOre ComputeChunk(long chunkX, long chunkY, ulong seed, uint ver)
    {
        var result = new ChunkOre();
        long originAbsX = UnifiedWorldGenerator.WorldOriginX + chunkX * (long)ChunkSide;
        long originAbsY = UnifiedWorldGenerator.WorldOriginY + chunkY * (long)ChunkSide;

        long startGx = WorldCoordinates.FloorDiv(originAbsX - Margin, Stride);
        long endGx = WorldCoordinates.FloorDiv(originAbsX + ChunkSide - 1 + Margin, Stride);
        long startGy = WorldCoordinates.FloorDiv(originAbsY - Margin, Stride);
        long endGy = WorldCoordinates.FloorDiv(originAbsY + ChunkSide - 1 + Margin, Stride);

        // Клетки, уже занятые в этом чанке (первый выигрывает, как на острове).
        // Террейн-сэмплы кэшируем на время прохода: проверка «только трава».
        Dictionary<long, bool> grassCache = new(256);
        List<(long Wx, long Wy, int Type)> centers = new(32);

        // task-9: стартовые гарантии — ЧАСТЬ ПОЛЯ (одни и те же от абсолютных
        // координат для острова и мира), а не отдельный островной проход.
        long spawnAbsX = UnifiedWorldGenerator.WorldOriginX + StartCenterOffset;
        long spawnAbsY = UnifiedWorldGenerator.WorldOriginY + StartCenterOffset;
        StartAnchor[] anchors = EnsureStartAnchors(seed, ver);
        for (int i = 0; i < anchors.Length; i++)
        {
            StartAnchor anchor = anchors[i];
            if (!AnchorTouchesChunk(anchor.AbsX, anchor.AbsY, originAbsX, originAbsY))
                continue;
            if (WriteDeposit(result, anchor.AbsX, anchor.AbsY, anchor.Type, anchor.Area,
                    seed, ver, originAbsX, originAbsY, grassCache,
                    spawnAbsX, spawnAbsY, OreGeologyConfig.StartAvoidRadius))
            {
                centers.Add((anchor.AbsX, anchor.AbsY, anchor.Type));
                WriteSatellites(result, anchor.AbsX, anchor.AbsY, anchor.Type,
                    seed, ver, originAbsX, originAbsY, grassCache, centers);
            }
        }

        for (long cgy = startGy; cgy <= endGy; cgy++)
        {
            for (long cgx = startGx; cgx <= endGx; cgx++)
            {
                ulong h = CoordinateHash.Hash(seed, ver, GenerationDomain.OreCandidate, cgx, cgy);
                long wx = cgx * Stride + (long)(h % (ulong)Stride);
                long wy = cgy * Stride + (long)((h >> 32) % (ulong)Stride);

                OreProvinceSample prov = SampleProvince(wx, wy, seed, ver, out OreTypeEnv env);
                if (prov.IsEmpty)
                    continue;

                int type = PickType(wx, wy, seed, ver, in prov, in env, out ushort suitability);
                if (type == 0)
                    continue;

                ulong sub = CoordinateHash.Hash(seed, ver, GenerationDomain.OreTypePick, wx, wy, lane: 11);
                if (type == 11 && env.Aridity >= 36000 && env.Elevation <= 32000 && (int)(sub % 100) < 35)
                    type = 12;

                long accept = (long)Q16.Mul(Q16.Mul(suitability, suitability),
                                            (ushort)OreGeologyConfig.OreDensityQ16);
                if (accept <= (long)(ushort)(h >> 48))
                    continue;
                if (TooClose(centers, wx, wy))
                    continue;

                ulong sizeHash = CoordinateHash.Hash(seed, ver, GenerationDomain.OreSize, wx, wy);
                int area = SizeForHash(sizeHash, type);
                if (WriteDeposit(result, wx, wy, type, area, seed, ver, originAbsX, originAbsY, grassCache,
                        spawnAbsX, spawnAbsY, OreGeologyConfig.StartAvoidRadius))
                {
                    centers.Add((wx, wy, type));
                    // Спутники/ореолы — детерминированно от хозяина (R5).
                    WriteSatellites(result, wx, wy, type, seed, ver, originAbsX, originAbsY, grassCache, centers,
                        spawnAbsX, spawnAbsY, OreGeologyConfig.StartAvoidRadius);
                }
            }
        }

        return result;
    }

    private static bool TooClose(List<(long Wx, long Wy, int Type)> centers, long wx, long wy)
    {
        long minSq = (long)OreGeologyConfig.MinGap * OreGeologyConfig.MinGap;
        for (int i = 0; i < centers.Count; i++)
        {
            long dx = centers[i].Wx - wx;
            long dy = centers[i].Wy - wy;
            if (dx * dx + dy * dy < minSq)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Локальная «провинция» мира в абсолютной точке — те же формулы, что у острова
    /// (§R1), но поверх канонического сэмплера мира и по абсолютным координатам.
    /// </summary>
    private static OreProvinceSample SampleProvince(long wx, long wy, ulong seed, uint ver,
        out OreTypeEnv env)
    {
        GeneratedCell cell = WorldGenV2.SampleCell(seed, ver, wx, wy);
        WorldClimateParams c = WorldClimateGenerator.SampleBlendedCell(seed, ver, wx, wy);

        // Рельеф: локальная высота клетки; вода/горы прижаты к своим полосам.
        int relief = cell.ElevationQ16;
        if (cell.Terrain == BaseTerrainKind.Water) relief = Math.Min(relief, 21999);
        else if (cell.Terrain == BaseTerrainKind.Mountain) relief = Math.Max(relief, 47000);

        ushort lineament = LineamentAt(wx, wy, seed, ver);
        ushort stoneNoise = CoordinateHash.ValueNoiseQ16(seed + 44444ul, ver,
            GenerationDomain.Stone, wx, wy, 24);
        ushort reliefRock = Q16.InverseLerp(34000, 46500, relief);
        ushort rockBase = Math.Max(c.RockExposureQ16, reliefRock);
        ushort rock = Q16.Weighted(rockBase, 65, Q16.InverseLerp(18000, 45000, stoneNoise), 35);

        ushort lowland = (ushort)(One - Q16.InverseLerp(26000, 40000, relief));
        ushort coast = Q16.Weighted(Q16.InverseLerp(30000, 38000, lowland), 60, lowland, 40);
        // «Плоскость» — медленный шум 64: дешёвая замена slope на масштабе чанка.
        ushort flatNoise = CoordinateHash.ValueNoiseQ16(seed + 707ul, ver,
            GenerationDomain.OreProvince, wx, wy, 64);
        ushort flat = (ushort)(One - Math.Abs((int)flatNoise - 32768) * 2);
        ushort basin = Q16.Mul(lowland, Q16.Weighted(
            flat, 60, Q16.InverseLerp(30000, 50000, c.SoilFertilityQ16), 40));
        ushort instability = Q16.InverseLerp(12000, 36000, c.TectonicPressureQ16);
        ushort craton = Q16.Mul((ushort)(One - instability),
            Q16.Weighted(Q16.InverseLerp(18000, 42000, rock), 70, flat, 30));
        ushort magmatic = Q16.Weighted(lineament, 70, instability, 30);
        ushort felsic = Q16.Mul(magmatic, Q16.Weighted(rock, 60, c.PrecipitationQ16, 40));
        ushort laterite = Q16.Mul(
            Q16.InverseLerp(47000, 56000, c.TemperatureQ16),
            Q16.Mul(Q16.InverseLerp(36000, 48000, c.MoistureQ16),
                    (ushort)(One - Q16.InverseLerp(22000, 36000, c.AridityQ16))));
        ushort placer = Q16.Mul(
            Q16.Weighted(Q16.Weighted(c.RiverDensityQ16, 60,
                                      (ushort)(One - c.GlaciationQ16), 20), 80, rock, 20),
            Q16.Weighted(coast, 60, lowland, 40));

        var prov = new OreProvinceSample(craton, magmatic, felsic, basin, laterite,
            placer, lowland, coast, lineament, relief, rock);
        env = new OreTypeEnv(relief, c.TectonicPressureQ16, rock, c.AridityQ16,
            c.MoistureQ16, c.PrecipitationQ16, c.TemperatureQ16, c.OceanInfluenceQ16,
            c.RiverDensityQ16, c.SoilFertilityQ16, c.GlaciationQ16, stoneNoise,
            RiverBank(wx, wy, seed, ver, in c) ? 1 : 0);
        return prov;
    }

    private static ushort LineamentAt(long wx, long wy, ulong seed, uint ver)
    {
        ushort r64 = CoordinateHash.ValueNoiseQ16(seed + 101ul, ver,
            GenerationDomain.OreStructure, wx, wy, 64);
        ushort r22 = CoordinateHash.ValueNoiseQ16(seed + 202ul, ver,
            GenerationDomain.OreStructure, wx, wy, 22);
        return Q16.Weighted(Spine(r64), 55, Spine(r22), 45);
    }

    private static ushort Spine(ushort noise)
    {
        int dist = Math.Abs((int)noise - 32768);
        return (ushort)Math.Clamp(Math.Max(0, 32768 - dist) * 2, 0, One);
    }

    /// <summary>Берег видимой реки (та же формула, что SampleCell/остров).</summary>
    private static bool RiverBank(long wx, long wy, ulong seed, uint ver, in WorldClimateParams c)
    {
        if (c.ElevationQ16 < WorldClimateGenerator.SeaLevelQ16 || c.RiverDensityQ16 <= 18000)
            return false;
        ushort rWarpX = CoordinateHash.ValueNoiseQ16(seed + 777ul, ver,
            GenerationDomain.RiverGeometry, wx, wy, 72);
        ushort rWarpY = CoordinateHash.ValueNoiseQ16(seed + 888ul, ver,
            GenerationDomain.RiverGeometry, wx + 31, wy, 72);
        long rwx = wx + ((int)rWarpX - 32768) * 20 / 32768;
        long rwy = wy + ((int)rWarpY - 32768) * 20 / 32768;
        ushort meander = CoordinateHash.ValueNoiseQ16(seed + 999ul, ver,
            GenerationDomain.RiverGeometry, rwx, rwy, 96);
        long transPos = (rwx * 3 - rwy * 4) / 5 + ((long)meander - 32768) * 64 / 32768;
        const int riverPeriod = 512;
        long local = ((transPos % riverPeriod) + riverPeriod) % riverPeriod;
        int dist = (int)Math.Abs(local - 256) * 65535 / 256;
        int width = 550 + (int)c.RiverDensityQ16 * 750 / 65535;
        return dist > width && dist < width * 3;
    }

    private static int PickType(long wx, long wy, ulong seed, uint ver,
        in OreProvinceSample prov, in OreTypeEnv env, out ushort chosenSuitability)
    {
        chosenSuitability = 0;
        long total = 0;
        Span<ushort> suit = stackalloc ushort[OreGeologyConfig.TypeCount + 1];
        Span<long> weight = stackalloc long[OreGeologyConfig.TypeCount + 1];
        for (int t = 1; t <= OreGeologyConfig.TypeCount; t++)
        {
            ushort s = OreSuitability.Score(t, in prov, in env);
            suit[t] = s;
            weight[t] = (long)OreGeologyConfig.RarityTenths[t] * Q16.Mul(s, s);
            total += weight[t];
        }
        if (total <= 0)
            return 0;

        ulong r = CoordinateHash.Hash(seed, ver, GenerationDomain.OreTypePick, wx, wy) % (ulong)total;
        for (int t = 1; t <= OreGeologyConfig.TypeCount; t++)
        {
            if (r < (ulong)weight[t])
            {
                if (suit[t] < OreGeologyConfig.MinSuitabilityPick)
                    return 0;
                chosenSuitability = suit[t];
                return t;
            }
            r -= (ulong)weight[t];
        }
        return 0;
    }

    private static int SizeForHash(ulong sizeHash, int type)
    {
        int area;
        int q = (int)((sizeHash >> 20) & 15);
        if (q < 15)
        {
            area = OreGeologyConfig.AreaQuantiles[q];
        }
        else
        {
            ulong t = sizeHash >> 40;
            if ((t & 3) != 3) area = 100 + (int)((sizeHash >> 8) % 151);
            else if ((t & 7) != 7) area = 251 + (int)((sizeHash >> 12) % 199);
            else area = 450 + (int)((sizeHash >> 16) % 451);
        }
        area = area * OreGeologyConfig.SizeScalePercent[type] / 100;
        if (area < 1) area = 1;
        int max = OreGeologyConfig.MaxArea[type];
        return area > max ? max : area;
    }

    /// <summary>
    /// Виртуальная форма тела от АБСОЛЮТНОГО центра (не зависит от чанка — только
    /// клетки, попадающие в текущий чанк, записываются). Начало/направление роста
    /// детерминированы хешем, поэтому чанк A и чанк B видят одну и ту же форму.
    /// </summary>
    private static bool WriteDeposit(ChunkOre result, long cx, long cy, int type, int area,
        ulong seed, uint ver, long originAbsX, long originAbsY, Dictionary<long, bool> grassCache,
        long avoidAbsX = long.MinValue, long avoidAbsY = long.MinValue, int avoidRadius = 0)
    {
        area = Math.Clamp(area, 1, OreGeologyConfig.MaxArea[type]);
        int strike = StrikeDirection(type, cx, cy, seed, ver);
        int count = BuildShape(type, cx, cy, area, strike, seed, ver, out int[] xs, out int[] ys);
        if (count <= 0)
            return false;

        int reserveMin = OreGeologyConfig.ReserveMin[type];
        int reserveMaxEx = OreGeologyConfig.ReserveMaxExclusive[type];
        int written = 0;
        for (int i = 0; i < count; i++)
        {
            long wx = cx + xs[i];
            long wy = cy + ys[i];
            long lxAbs = wx - originAbsX;
            long lyAbs = wy - originAbsY;
            if ((ulong)lxAbs >= (ulong)ChunkSide || (ulong)lyAbs >= (ulong)ChunkSide)
                continue; // клетка другого чанка — её запишет свой чанк

            int idx = (int)(lyAbs * ChunkSide + lxAbs);
            if (result.Types[idx] != OreConfig.None)
                continue;
            // Стартовая поляна колонии: один и тот же запрет для острова и мира,
            // иначе на стыке появился бы «вырез»/шов.
            if (avoidRadius > 0)
            {
                long adx = wx - avoidAbsX;
                long ady = wy - avoidAbsY;
                if (adx * adx + ady * ady <= (long)avoidRadius * avoidRadius)
                    continue;
            }
            if (!IsGrass(wx, wy, seed, ver, grassCache))
                continue;

            int reserve = reserveMin;
            int span = reserveMaxEx - reserveMin;
            if (span > 0)
                reserve += (int)(CoordinateHash.Hash(seed, ver, GenerationDomain.OreSize,
                    wx, wy, lane: 3) % (ulong)span);
            if (reserve < 1) reserve = 1;
            if (reserve > 65535) reserve = 65535;
            result.Types[idx] = (byte)type;
            result.Reserves[idx] = (ushort)reserve;
            written++;
        }
        return written > 0;
    }

    private static void WriteSatellites(ChunkOre result, long hostX, long hostY, int hostType,
        ulong seed, uint ver, long originAbsX, long originAbsY, Dictionary<long, bool> grassCache,
        List<(long Wx, long Wy, int Type)> centers,
        long avoidAbsX = long.MinValue, long avoidAbsY = long.MinValue, int avoidRadius = 0)
    {
        ulong h = CoordinateHash.Hash(seed, ver, GenerationDomain.OreSatellite, hostX, hostY,
            lane: (ulong)hostType);

        // Пиритовый ореол вокруг сульфидов (60 %).
        if (OreGeologyConfig.IsSulfide(hostType) && (int)(h % 100) < 60)
            TrySatellite(18, hostX, hostY, h >> 8, 3, 20);

        if (hostType == 13 && (int)((h >> 8) % 100) < 70) TrySatellite(20, hostX, hostY, h >> 16, 1, 10);
        if (hostType == 14 && (int)((h >> 8) % 100) < 30) TrySatellite(20, hostX, hostY, h >> 16, 1, 10);
        if (hostType == 11 && (int)((h >> 16) % 100) < 90) TrySatellite(6, hostX, hostY, h >> 24, 2, 18);
        if (hostType == 8 && (int)((h >> 24) % 100) < 80) TrySatellite(9, hostX, hostY, h >> 32, 2, 15);
        if (hostType == 11 && (int)((h >> 32) % 100) < 20) TrySatellite(9, hostX, hostY, h >> 40, 2, 15);
        if (hostType == 2 && (int)((h >> 40) % 100) < 70) TrySatellite(7, hostX, hostY, h >> 48, 2, 20);
        if (hostType == 1 && (int)((h >> 48) % 100) < 30) TrySatellite(7, hostX, hostY, h, 2, 20);
        if (hostType == 13 && (int)((h ^ 0x9E37u) % 100) < 50) TrySatellite(14, hostX, hostY, h >> 12, 10, 90);

        void TrySatellite(int satType, long hx, long hy, ulong sh, int minArea, int maxArea)
        {
            int radius = 4 + (int)(sh % 12);
            int dir = (int)((sh >> 8) & 7);
            long sx = hx + DirX[dir] * radius;
            long sy = hy + DirY[dir] * radius;
            int span = maxArea - minArea + 1;
            if (span < 1) span = 1;
            int area = minArea + (int)((sh >> 16) % (ulong)span);
            if (WriteDeposit(result, sx, sy, satType, area, seed, ver, originAbsX, originAbsY, grassCache,
                    avoidAbsX, avoidAbsY, avoidRadius))
                centers.Add((sx, sy, satType));
        }
    }

    private static bool IsGrass(long wx, long wy, ulong seed, uint ver, Dictionary<long, bool> cache)
    {
        long key = ((long)(int)(wx & 0xFFFFFFFF) << 32) ^ (uint)(wy & 0xFFFFFFFF);
        if (cache.TryGetValue(key, out bool grass))
            return grass;
        GeneratedCell cell = WorldGenV2.SampleCell(seed, ver, wx, wy);
        grass = cell.Terrain == BaseTerrainKind.Grass;
        cache[key] = grass;
        return grass;
    }

    private static int BuildShape(int type, long cx, long cy, int area, int strike,
        ulong seed, uint ver, out int[] xs, out int[] ys)
    {
        xs = new int[area];
        ys = new int[area];
        xs[0] = 0;
        ys[0] = 0;
        int count = 1;
        var used = new HashSet<long>(area) { 0L };
        OreFormKind form = OreGeologyConfig.Form[type];
        bool linear = form == OreFormKind.Stripe || form == OreFormKind.Vein || form == OreFormKind.Ribbon;
        int aniso = OreGeologyConfig.AnisotropyPercent[type];
        int maxRadius = OreGeologyConfig.MaxBlobRadius;

        while (count < area)
        {
            ulong h = CoordinateHash.Hash(seed, ver, GenerationDomain.OreShape, cx, cy, (ulong)count);
            int parent = linear ? count - 1 : (int)(h % (ulong)count);
            int px = xs[parent];
            int py = ys[parent];
            bool alongStrike = (int)((h >> 8) % 100) < aniso;
            int sign = ((h >> 16) & 1) == 0 ? 1 : -1;
            int dirBase = alongStrike ? (sign > 0 ? strike : (strike + 4) & 7) : (int)((h >> 24) & 7);
            bool placed = false;
            for (int attempt = 0; attempt < 6 && !placed; attempt++)
            {
                int d = (dirBase + (int)((h >> (20 + attempt * 4)) & 7)) & 7;
                int nx = px + DirX[d];
                int ny = py + DirY[d];
                if (Math.Abs(nx) > maxRadius || Math.Abs(ny) > maxRadius) continue;
                long key = ((long)nx << 32) ^ (uint)ny;
                if (!used.Add(key)) continue;
                xs[count] = nx;
                ys[count] = ny;
                count++;
                placed = true;
            }
            if (!placed) break;
        }
        return count;
    }

    private static int StrikeDirection(int type, long wx, long wy, ulong seed, uint ver)
    {
        switch (OreGeologyConfig.Form[type])
        {
            case OreFormKind.Stripe:
            {
                ushort px1 = CoordinateHash.ValueNoiseQ16(seed + 11ul, ver, GenerationDomain.OreProvince, wx + 8, wy, 96);
                ushort px0 = CoordinateHash.ValueNoiseQ16(seed + 11ul, ver, GenerationDomain.OreProvince, wx - 8, wy, 96);
                ushort py1 = CoordinateHash.ValueNoiseQ16(seed + 11ul, ver, GenerationDomain.OreProvince, wx, wy + 8, 96);
                ushort py0 = CoordinateHash.ValueNoiseQ16(seed + 11ul, ver, GenerationDomain.OreProvince, wx, wy - 8, 96);
                int dx = (int)px1 - px0;
                int dy = (int)py1 - py0;
                if (dx != 0 || dy != 0) return QuantizeDirection(dx, dy);
                break;
            }
            case OreFormKind.Vein:
            case OreFormKind.Pod:
            {
                ushort nx1 = CoordinateHash.ValueNoiseQ16(seed + 101ul, ver, GenerationDomain.OreStructure, wx + 8, wy, 64);
                ushort nx0 = CoordinateHash.ValueNoiseQ16(seed + 101ul, ver, GenerationDomain.OreStructure, wx - 8, wy, 64);
                ushort ny1 = CoordinateHash.ValueNoiseQ16(seed + 101ul, ver, GenerationDomain.OreStructure, wx, wy + 8, 64);
                ushort ny0 = CoordinateHash.ValueNoiseQ16(seed + 101ul, ver, GenerationDomain.OreStructure, wx, wy - 8, 64);
                int dx = (int)nx1 - nx0;
                int dy = (int)ny1 - ny0;
                if (dx != 0 || dy != 0) return QuantizeDirection(dx, dy);
                break;
            }
            case OreFormKind.Ribbon:
                return 1; // SE — вдоль рек (направление transPos)
        }
        ulong h = CoordinateHash.Hash(seed, ver, GenerationDomain.OreShape, wx, wy, lane: 999);
        return (int)(h & 7);
    }

    private static int QuantizeDirection(int dx, int dy)
    {
        int adx = Math.Abs(dx);
        int ady = Math.Abs(dy);
        if (adx > ady * 2) return dx > 0 ? 0 : 4;
        if (ady > adx * 2) return dy > 0 ? 2 : 6;
        if (dx > 0) return dy > 0 ? 1 : 7;
        return dy > 0 ? 3 : 5;
    }

    private static long PackChunk(long cx, long cy)
        => ((long)(int)(cx & 0xFFFFFFFF) << 32) ^ (uint)(cy & 0xFFFFFFFF);
}