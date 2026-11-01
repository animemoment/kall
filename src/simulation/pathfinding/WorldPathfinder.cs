using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Game.Core.World;

namespace Game.Simulation.Pathfinding;

/// <summary>
/// Патфайндер по МИРУ (стримингу), а не по острову.
///
/// Как работает:
///   1. Стоимости клеток берём из <see cref="WorldCostCache"/> — он считает генератор
///      на любой координате и НЕ зависит от того, загружен ли чанк.
///   2. Отдаём сетку существующему HPA (<see cref="HierarchicalPathfinder.InitializeCost"/>):
///      его внутренности работают в локальных координатах окна и переписывать их не нужно.
///   3. Окна кэшируются по прямоугольнику: агенты в одной местности переиспользуют граф.
///
/// Дальние маршруты (HPA по стримингу):
///   одно окно физически не может быть больше <see cref="MaxWindow"/> (640 клеток), поэтому
///   длинный маршрут режется на сегменты вдоль прямой старт→цель. Каждый сегмент считается
///   СВОИМ окном HPA (≤ 640), а точки стыкуются в один путь. Стыковочные точки — общие
///   (последняя точка сегмента N ровно равна первой точке сегмента N+1), поэтому между
///   окнами нет «прыжков»: каждый участок пути проверен на видимость внутри своего окна.
///   Виртуальное окно (охват всего маршрута) нужно ТОЛЬКО для упаковки точек в int —
///   память под него не выделяется.
///
/// Координаты: на вход и на выход — МИРОВЫЕ. Внутри окна — локальные, поэтому
/// вызывающий получает origin окна и прибавляет его к пути.
/// </summary>
public sealed class WorldPathfinder
{
    public static WorldPathfinder Instance { get; } = new();

    /// <summary>Размер окна: степень двойки, чтобы выравнивание было дешёвым.</summary>
    private const int MinWindow = 128;
    private const int MaxWindow = 640;
    private const int WindowShift = 5;      // выравнивание 32 клетки
    private const int Margin = 16;          // запас вокруг старта и цели
    // Сшивка длинного маршрута требует несколько окон сразу (по одному на сегмент),
    // иначе окна вытесняют друг друга ещё до конца поиска.
    private const int MaxCachedWindows = 12;

    // ---- Сшивка длинных маршрутов ----
    /// <summary>Шаг сегмента вдоль прямой: гарантированно влезает в MaxWindow с запасом.</summary>
    private const int SegmentSpan = 512;
    /// <summary>Предел числа сегментов (SegmentSpan * MaxSegments ≈ 49 000 клеток).</summary>
    private const int MaxSegments = 96;
    /// <summary>Предел точек сшитого пути (упаковка в int обязана остаться в диапазоне).</summary>
    private const int MaxStitchedPoints = 4096;
    /// <summary>Предел охвата маршрута: дальше int-упаковка рискует переполниться.</summary>
    private const int MaxRouteSpan = 24_000;
    /// <summary>Радиус поиска проходимой точки-якоря рядом с идеальной.</summary>
    private const int AnchorSearchRadius = 8;

    private sealed class Window
    {
        public HierarchicalPathfinder Finder = null!;
        public long OriginX;
        public long OriginY;
        public int Width;
        public int Height;
        public long LastUsed;
    }

    private readonly object _lock = new();
    private readonly Dictionary<(long X, long Y, int W, int H), Window> _windows = new();
    /// <summary>Окна, которые уже строятся в фоне (защита от дублирующих сборок).</summary>
    private readonly HashSet<(long X, long Y, int W, int H)> _building = new();
    /// <summary>Сколько сборок идёт прямо сейчас: не более <see cref="MaxConcurrentBuilds"/>.</summary>
    private int _activeBuilds;
    private const int MaxConcurrentBuilds = 2;
    /// <summary>
    /// Поколение кэша. Растёт при каждом сбросе окон. Фоновая сборка, начатая ДО
    /// сброса, не имеет права публиковать результат: её сетка уже устарела.
    /// </summary>
    private int _generation;
    private long _tick;
    private long _lastInvalidateMs;
    private volatile bool _invalidateQueued;

    /// <summary>Буфер сшитого пути: свой на поток, валиден до следующего вызова на нём.</summary>
    [ThreadStatic] private static int[] _stitchBuffer;

    private WorldPathfinder()
    {
        // Окна держат КОПИЮ стоимости. Стена за кромкой построена/снесена — окна
        // протухли. Раньше этого не было: StructureManager инвалидировал только
        // островной HierarchicalPathfinder, поэтому мировой HPA ходил по старой сетке.
        WorldCostCache.CellChanged += OnWorldCostChanged;
    }

    private static void OnWorldCostChanged() => Instance.Invalidate();

    /// <summary>Сколько окон сейчас в кэше (для тестов и профиля).</summary>
    public int CachedWindows { get { lock (_lock) return _windows.Count; } }

    public void Clear()
    {
        lock (_lock)
        {
            _windows.Clear();
            _generation++;
        }
    }

    /// <summary>
    /// Инвалидация кэша окон: мир изменился (стена за кромкой построена/снесена —
    /// §29). Дебаунс 200 мс по образцу HierarchicalPathfinder.Invalidate: рамка
    /// стены из десятков клеток не должна сбрасывать окна десятки раз за тик
    /// (каждое окно = FillRect до 640×640 + сборка порталов).
    /// ХВОСТ (trailing): вызовы внутри дебаунс-окна ставят _invalidateQueued, и
    /// следующий запрос пути досбрасывает окна — одинокая стена не протухает
    /// навсегда, как при «leading-only» дебаунсе.
    /// </summary>
    public void Invalidate()
    {
        long now = System.Environment.TickCount64;
        lock (_lock)
        {
            if (now - _lastInvalidateMs < 200)
            {
                _invalidateQueued = true;
                return;
            }
            _lastInvalidateMs = now;
            _generation++;
            _windows.Clear();
        }
    }

    private void FlushQueuedInvalidation_NoLock()
    {
        if (!_invalidateQueued)
            return;
        long now = System.Environment.TickCount64;
        if (now - _lastInvalidateMs < 200)
            return;
        _lastInvalidateMs = now;
        _invalidateQueued = false;
        _generation++;
        _windows.Clear();
    }

    /// <summary>
    /// Найти путь в МИРОВЫХ координатах.
    /// <paramref name="path"/> — буфер, валидный до следующего вызова на этом потоке
    /// (внутренний scratch HPA или буфер сшивки). Ячейки упакованы как
    /// y * windowWidth + x в координатах окна: мировые координаты =
    /// originX + (packed % windowWidth), originY + (packed / windowWidth).
    ///
    /// Дальние пары (больше одного окна) считаются сшивкой сегментов; предел —
    /// <see cref="MaxRouteSpan"/>. Дальше — честный false, а не выдуманный путь.
    /// </summary>
    public bool TryFindPath(
        long fromX, long fromY, long toX, long toY,
        out int[] path, out int count, out long originX, out long originY, out int windowWidth)
        => TryFindPathCore(fromX, fromY, toX, toY, blockOnBuild: false,
            out path, out count, out originX, out originY, out windowWidth);

    /// <summary>
    /// То же, но недостающие окна строит СИНХРОННО (сотни миллисекунд).
    /// Только тесты и предварительный прогрев: в потоке симуляции это фриз.
    /// </summary>
    public bool TryFindPathBlocking(
        long fromX, long fromY, long toX, long toY,
        out int[] path, out int count, out long originX, out long originY, out int windowWidth)
        => TryFindPathCore(fromX, fromY, toX, toY, blockOnBuild: true,
            out path, out count, out originX, out originY, out windowWidth);

    private bool TryFindPathCore(
        long fromX, long fromY, long toX, long toY, bool blockOnBuild,
        out int[] path, out int count, out long originX, out long originY, out int windowWidth)
    {
        if (fromX == toX && fromY == toY)
        {
            path = Array.Empty<int>();
            count = 0;
            originX = fromX;
            originY = fromY;
            windowWidth = 1;
            return true;
        }

        long dx = Math.Abs(toX - fromX);
        long dy = Math.Abs(toY - fromY);

        // Быстрый путь: маршрут влезает в одно окно HPA.
        if (dx + 2L * Margin <= MaxWindow && dy + 2L * Margin <= MaxWindow)
            return TryFindSingleWindow(fromX, fromY, toX, toY, blockOnBuild,
                out path, out count, out originX, out originY, out windowWidth);

        // Длинный маршрут: HPA по стримингу через сшивку сегментов.
        return TryFindStitched(fromX, fromY, toX, toY, blockOnBuild,
            out path, out count, out originX, out originY, out windowWidth);
    }

    /// <summary>Маршрут, который целиком помещается в одно окно HPA.</summary>
    private bool TryFindSingleWindow(
        long fromX, long fromY, long toX, long toY, bool blockOnBuild,
        out int[] path, out int count, out long originX, out long originY, out int windowWidth)
    {
        path = null!;
        count = 0;
        originX = 0;
        originY = 0;
        windowWidth = 0;

        long dx = Math.Abs(toX - fromX);
        long dy = Math.Abs(toY - fromY);

        int width = (int)Math.Min(MaxWindow, Math.Max(MinWindow, AlignUp((int)Math.Min(dx + 2 * Margin, dx > MaxWindow ? MaxWindow : dx + 2 * Margin))));
        int height = (int)Math.Min(MaxWindow, Math.Max(MinWindow, AlignUp((int)Math.Min(dy + 2 * Margin, dy > MaxWindow ? MaxWindow : dy + 2 * Margin))));

        if (dx + 2L * Margin > MaxWindow || dy + 2L * Margin > MaxWindow)
            return false;

        // Центр окна — середина между стартом и целью; выравниваем по сетке.
        long centerX = (fromX + toX) / 2;
        long centerY = (fromY + toY) / 2;
        long ox = AlignDown(centerX - width / 2);
        long oy = AlignDown(centerY - height / 2);

        if (!AcquireWindow(ox, oy, width, height, blockOnBuild, out Window window))
            return false;

        int lx0 = (int)(fromX - window.OriginX);
        int ly0 = (int)(fromY - window.OriginY);
        int lx1 = (int)(toX - window.OriginX);
        int ly1 = (int)(toY - window.OriginY);

        if (!InWindow(lx0, ly0, window) || !InWindow(lx1, ly1, window))
            return false;

        bool ok = window.Finder.TryFindPathScratch(lx0, ly0, lx1, ly1, out path, out count);
        originX = window.OriginX;
        originY = window.OriginY;
        windowWidth = window.Width;
        return ok;
    }

    /// <summary>
    /// Длинный маршрут: режем прямую старт→цель на сегменты, каждый считаем своим
    /// окном HPA, точки складываем в один путь (виртуальное окно — только для упаковки).
    /// </summary>
    private bool TryFindStitched(
        long fromX, long fromY, long toX, long toY, bool blockOnBuild,
        out int[] path, out int count, out long originX, out long originY, out int windowWidth)
    {
        path = null!;
        count = 0;
        originX = 0;
        originY = 0;
        windowWidth = 0;

        long dx = Math.Abs(toX - fromX);
        long dy = Math.Abs(toY - fromY);
        if (dx > MaxRouteSpan || dy > MaxRouteSpan)
            return false;

        // Виртуальное окно: охват маршрута + запас. Нужно ТОЛЬКО как система
        // координат для упаковки точек в int; массив такого размера не выделяется.
        long minX = Math.Min(fromX, toX) - Margin;
        long minY = Math.Min(fromY, toY) - Margin;
        long oxV = AlignDown(minX);
        long oyV = AlignDown(minY);
        int vw = AlignUp((int)(Math.Max(fromX, toX) - oxV) + Margin + 1);
        int vh = AlignUp((int)(Math.Max(fromY, toY) - oyV) + Margin + 1);
        if (vw <= 0 || vh <= 0)
            return false;

        long span = Math.Max(dx, dy);
        int segments = (int)((span + SegmentSpan - 1) / SegmentSpan);
        if (segments < 1) segments = 1;
        if (segments > MaxSegments)
            return false;

        int[] buf = _stitchBuffer ??= new int[MaxStitchedPoints];
        int used = 0;

        long curX = fromX;
        long curY = fromY;

        for (int s = 0; s < segments; s++)
        {
            long nextX, nextY;
            if (s == segments - 1)
            {
                nextX = toX;
                nextY = toY;
            }
            else
            {
                long idealX = fromX + (toX - fromX) * (s + 1) / segments;
                long idealY = fromY + (toY - fromY) * (s + 1) / segments;
                if (!TryFindWalkableAnchor(idealX, idealY, out nextX, out nextY))
                    return false;
            }

            if (!AppendSegment(curX, curY, nextX, nextY, buf, ref used, oxV, oyV, vw, blockOnBuild))
                return false;

            curX = nextX;
            curY = nextY;
        }

        if (used == 0)
            return false;

        path = buf;
        count = used;
        originX = oxV;
        originY = oyV;
        windowWidth = vw;
        return true;
    }

    /// <summary>
    /// Посчитать ОДИН сегмент маршрута своим окном HPA и дописать его точки в буфер
    /// сшивки. Стыковочная точка общая: конец сегмента = начало следующего, поэтому
    /// дубликаты отбрасываются и между окнами не появляется разрыв.
    /// </summary>
    private bool AppendSegment(
        long ax, long ay, long bx, long by,
        int[] buf, ref int used,
        long oxV, long oyV, int vw, bool blockOnBuild)
    {
        if (ax == bx && ay == by)
            return true;

        long ox = AlignDown(Math.Min(ax, bx) - Margin);
        long oy = AlignDown(Math.Min(ay, by) - Margin);
        long needW = Math.Max(ax, bx) - ox + Margin + 1;
        long needH = Math.Max(ay, by) - oy + Margin + 1;
        if (needW > MaxWindow || needH > MaxWindow)
            return false;

        int w = Math.Max(MinWindow, AlignUp((int)needW));
        int h = Math.Max(MinWindow, AlignUp((int)needH));
        if (w > MaxWindow || h > MaxWindow)
            return false;

        if (!AcquireWindow(ox, oy, w, h, blockOnBuild, out Window window))
            return false;

        int lx0 = (int)(ax - window.OriginX);
        int ly0 = (int)(ay - window.OriginY);
        int lx1 = (int)(bx - window.OriginX);
        int ly1 = (int)(by - window.OriginY);
        if (!InWindow(lx0, ly0, window) || !InWindow(lx1, ly1, window))
            return false;

        if (!window.Finder.TryFindPathScratch(lx0, ly0, lx1, ly1, out int[] segment, out int segmentCount))
            return false;

        // HPA возвращает count == 0, когда сегмент проходим по прямой (промежуточные
        // точки не нужны). Для сшивки это НОРМАЛЬНЫЙ случай: добавляем конец сегмента.
        if (segmentCount == 0)
            return AppendPoint(bx, by, buf, ref used, oxV, oyV, vw);

        for (int i = 0; i < segmentCount; i++)
        {
            int packed = segment[i];
            long wx = window.OriginX + packed % window.Width;
            long wy = window.OriginY + packed / window.Width;

            if (!AppendPoint(wx, wy, buf, ref used, oxV, oyV, vw))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Дописать точку в буфер сшивки (в координатах виртуального окна), пропустив
    /// дубликат предыдущей. false — точка вне int-упаковки или буфер переполнен.
    /// </summary>
    private static bool AppendPoint(
        long wx, long wy,
        int[] buf, ref int used,
        long oxV, long oyV, int vw)
    {
        if (used > 0)
        {
            int prev = buf[used - 1];
            long prevX = oxV + prev % vw;
            long prevY = oyV + prev / vw;
            if (prevX == wx && prevY == wy)
                return true;
        }

        long packedV = (wy - oyV) * vw + (wx - oxV);
        if (packedV < 0 || packedV > int.MaxValue)
            return false;

        if (used >= buf.Length)
            return false;

        buf[used++] = (int)packedV;
        return true;
    }

    /// <summary>
    /// Проходимая точка-якорь рядом с идеальной (на идеальной может быть вода).
    /// Ищем по периметру расширяющихся квадратов — без аллокаций.
    /// </summary>
    private static bool TryFindWalkableAnchor(long x, long y, out long ax, out long ay)
    {
        if (WorldCostCache.IsWalkable(x, y))
        {
            ax = x;
            ay = y;
            return true;
        }

        for (int r = 1; r <= AnchorSearchRadius; r++)
        {
            for (int dy = -r; dy <= r; dy++)
            {
                int ady = Math.Abs(dy);
                for (int dx = -r; dx <= r; dx++)
                {
                    // Только периметр квадрата: внутренность уже проверена прошлым r.
                    if (ady != r && Math.Abs(dx) != r)
                        continue;
                    long nx = x + dx;
                    long ny = y + dy;
                    if (WorldCostCache.IsWalkable(nx, ny))
                    {
                        ax = nx;
                        ay = ny;
                        return true;
                    }
                }
            }
        }

        ax = x;
        ay = y;
        return false;
    }

    private static bool InWindow(int x, int y, Window w) =>
        x >= 0 && y >= 0 && x < w.Width && y < w.Height;

    /// <summary>
    /// Взять готовое окно. Если его нет:
    ///   • blockOnBuild == false — поставить сборку в фон и вернуть false СРАЗУ
    ///     (поток симуляции не должен ждать: сборка окна ≈ сотни миллисекунд);
    ///   • blockOnBuild == true — построить синхронно (тесты, прогрев).
    /// </summary>
    private bool AcquireWindow(long ox, long oy, int width, int height, bool blockOnBuild, out Window window)
    {
        var key = (ox, oy, width, height);

        lock (_lock)
        {
            // Хвост дебаунса Invalidate: пропущенный (внутри 200 мс) сброс окон
            // доезжает ДО выдачи окна — одинокая стена не протухает навсегда.
            FlushQueuedInvalidation_NoLock();
            if (_windows.TryGetValue(key, out window))
            {
                window.LastUsed = ++_tick;
                return true;
            }
        }

        if (!blockOnBuild)
        {
            RequestBuild(ox, oy, width, height);
            window = null!;
            return false;
        }

        window = BuildAndStore(ox, oy, width, height);
        return window != null;
    }

    /// <summary>
    /// Собрать окно синхронно и положить в кэш (либо вернуть уже готовое).
    /// Сборка — ВНЕ lock: она тяжёлая, держать мьютекс нельзя.
    /// </summary>
    private Window BuildAndStore(long ox, long oy, int width, int height)
    {
        var key = (ox, oy, width, height);
        Window built = BuildWindow(ox, oy, width, height);

        lock (_lock)
        {
            built.LastUsed = ++_tick;
            if (_windows.TryGetValue(key, out Window existing))
            {
                existing.LastUsed = built.LastUsed;
                return existing;
            }

            if (_windows.Count >= MaxCachedWindows)
                EvictOldest_NoLock();

            _windows[key] = built;
            return built;
        }
    }

    /// <summary>Тяжёлая часть: стоимости из генератора + построение графа HPA.</summary>
    private static Window BuildWindow(long ox, long oy, int width, int height)
    {
        var finder = new HierarchicalPathfinder();
        var cost = new byte[width * height];
        WorldCostCache.FillRect(ox, oy, width, height, cost);
        finder.InitializeCost(cost, width, height);

        return new Window
        {
            Finder = finder,
            OriginX = ox,
            OriginY = oy,
            Width = width,
            Height = height,
        };
    }

    /// <summary>
    /// Поставить сборку окна в фон. Не более <see cref="MaxConcurrentBuilds"/> разом,
    /// без очереди ожидания: агент получит отказ и повторит запрос по кулдауну.
    /// Сборка, начатая до сброса кэша, НЕ публикуется (поколение не совпало).
    /// </summary>
    private void RequestBuild(long ox, long oy, int width, int height)
    {
        var key = (ox, oy, width, height);

        int generation;
        lock (_lock)
        {
            if (_windows.ContainsKey(key) || _building.Contains(key))
                return;
            if (_activeBuilds >= MaxConcurrentBuilds)
                return;
            _building.Add(key);
            _activeBuilds++;
            generation = _generation;
        }

        ThreadPool.UnsafeQueueUserWorkItem(
            static state =>
            {
                var (self, reqKey, gen) = ((WorldPathfinder, (long X, long Y, int W, int H), int))state!;
                try
                {
                    Window built = BuildWindow(reqKey.X, reqKey.Y, reqKey.W, reqKey.H);
                    lock (self._lock)
                    {
                        // Поколение сменилось (стена построена/снесена, кэш сброшен):
                        // сетка окна устарела ещё до публикации — выбрасываем.
                        if (gen != self._generation)
                            return;

                        built.LastUsed = ++self._tick;
                        if (!self._windows.ContainsKey(reqKey))
                        {
                            if (self._windows.Count >= MaxCachedWindows)
                                self.EvictOldest_NoLock();
                            self._windows[reqKey] = built;
                        }
                    }
                }
                catch (Exception)
                {
                    // Сборка не удалась (гонка с Clear/переполнение) — повторят позже.
                }
                finally
                {
                    lock (self._lock)
                    {
                        self._building.Remove(reqKey);
                        self._activeBuilds--;
                    }
                }
            },
            (this, key, generation));
    }

    private void EvictOldest_NoLock()
    {
        (long X, long Y, int W, int H) oldestKey = default;
        long oldest = long.MaxValue;
        bool found = false;

        foreach (KeyValuePair<(long X, long Y, int W, int H), Window> kv in _windows)
        {
            if (kv.Value.LastUsed < oldest)
            {
                oldest = kv.Value.LastUsed;
                oldestKey = kv.Key;
                found = true;
            }
        }

        if (found) _windows.Remove(oldestKey);
    }

    private static long AlignDown(long v) => v & ~((1L << WindowShift) - 1);

    private static int AlignUp(int v)
    {
        int mask = (1 << WindowShift) - 1;
        return (v + mask) & ~mask;
    }
}