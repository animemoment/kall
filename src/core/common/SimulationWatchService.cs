using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Game.Simulation;

namespace Game.Core;

/// <summary>
/// «СЛЕЖКА ЗА СИМУЛЯЦИЕЙ» (Simulation Watch) — отдельная секция профайлера.
///
/// Назначение: раз в секунду писать в кольцевой буфер СТРУКТУРИРОВАННЫЙ снимок
/// того, что реально происходит в мире и в симуляции, чтобы потом (по кнопке
/// «Copy AI») отдать ИИ ровно те 60 записей, которые соответствуют последней
/// минуте игры:
///   • время мира, скорость, пауза, FPS;
///   • камера игрока (позиция/зум) — что видно на экране;
///   • работа: сколько всего, взято, не взято, сколько агентов простаивает;
///   • стройка: площадки по стадиям (Clearance → Supplying → Assembling),
///     сколько с препятствиями, сколько без полного комплекта, средний прогресс
///     и до 5 конкретных «битых» площадок с координатами;
///   • предметы на земле: кучек/штук и разбивка по типу;
///   • склад: клеток/штук и разбивка по типу;
///   • зоны: Work / Farm / площадь склада;
///   • топ-скриптов симуляции за эту секунду (какие файлы реально работали).
///
/// Почему отдельный сервис: существующий ProfilerLogService пишет МЕТРИКИ
/// (мс по методам), но ничего не знает про состояние мира. Именно поэтому по
/// его отчёту нельзя было понять «почему чертёж исчез» или «почему агенты
/// стоят». Здесь — посекундный снимок СОСТОЯНИЯ, а не только таймингов.
///
/// Потокобезопасность: Capture() зовётся только с главного потока
/// (PerformanceOverlay._Process), все менеджеры читаются через их публичные
/// lock-free/снапшот-API. Чтение обёрнуто в try/catch: диагностика НИКОГДА не
/// должна ронять игру.
/// </summary>
public sealed class SimulationWatchService
{
    public static SimulationWatchService Instance { get; } = new();

    /// <summary>Сколько секунд держим в кольце (10 минут).</summary>
    public const int DefaultCapacity = 600;

    /// <summary>Сколько секунд попадает в AI-отчёт по умолчанию.</summary>
    public const int DefaultSectionSeconds = 60;

    /// <summary>Сколько «битых» площадок перечислять в записи.</summary>
    private const int BrokenSitesPerRecord = 5;

    /// <summary>Одна посекундная запись состояния.</summary>
    public sealed class Record
    {
        public int Index;               // номер секунды в кольце (1,2,3...)
        public double SessionSec;       // время сессии, с
        public int Day, Hour, Minute;
        public double Speed;
        public bool Paused;
        public int Fps;
        public string Camera = "—";

        public int JobsTotal, JobsUnclaimed, JobsClaimed;
        public int AgentsIdle;

        public int SitesTotal, SitesClearance, SitesSupplying, SitesAssembling;
        public int SitesBlocked, SitesUnsupplied;
        public double SitesAvgProgressMs;
        public string SitesBroken = "";

        public int GroundPiles, GroundCount;
        public string GroundByItem = "";

        public int StockpileTiles, StockpileCount;
        public string StockpileByItem = "";

        public int ZonesWork, ZonesFarm, ZonesTotal;

        public string TopSimMethods = "";
        public string Flags = "";
    }

    private readonly object _lock = new();
    private readonly List<Record> _ring = new(DefaultCapacity + 8);

    private double _accum;
    private int _totalSeconds;

    /// <summary>Включение/выключение записи (по умолчанию включена).</summary>
    public bool Enabled { get; set; } = true;

    public int Capacity { get; set; } = DefaultCapacity;

    public int RecordCount { get { lock (_lock) return _ring.Count; } }

    public int TotalSeconds { get { lock (_lock) return _totalSeconds; } }

    /// <summary>Очистить кольцо (например, вместе с Reset профайлера).</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _ring.Clear();
            _totalSeconds = 0;
            _accum = 0.0;
        }
    }

    /// <summary>
    /// Тикать каждый кадр с главного потока. Внутри раз в секунду снимает запись.
    /// </summary>
    /// <param name="delta">Кадровая дельта, с.</param>
    /// <param name="camera">Строка вида "pos=(x, y) zoom=1.00x".</param>
    /// <param name="metrics">Последние метрики GameProfiler (для топа скриптов).</param>
    public void Tick(double delta, string camera, GameProfiler.MetricSnapshot[] metrics)
    {
        if (!Enabled)
            return;
        if (!double.IsFinite(delta) || delta <= 0.0)
            return;

        _accum += delta;
        if (_accum < 1.0)
            return;
        _accum -= 1.0;
        if (_accum < 0.0) _accum = 0.0;

        CaptureNow(camera, metrics);
    }

    /// <summary>
    /// Немедленно снять одну запись (вызывается из 1-герцового таймера оверлея,
    /// чтобы не строить строку камеры каждый кадр).
    /// </summary>
    public void CaptureNow(string camera, GameProfiler.MetricSnapshot[] metrics)
    {
        if (!Enabled)
            return;
        Record rec = Capture(camera, metrics);
        lock (_lock)
        {
            _totalSeconds++;
            rec.Index = _totalSeconds;
            _ring.Add(rec);
            int cap = Capacity > 0 ? Capacity : DefaultCapacity;
            while (_ring.Count > cap)
                _ring.RemoveAt(0);
        }
    }

    private Record Capture(string camera, GameProfiler.MetricSnapshot[] metrics)
    {
        var r = new Record { Camera = camera ?? "—", Fps = (int)Godot.Engine.GetFramesPerSecond() };
        try
        {
            CaptureTime(r);
            CaptureJobs(r);
            CaptureSites(r);
            CaptureGroundItems(r);
            CaptureStockpile(r);
            CaptureZones(r);
            CaptureMetrics(r, metrics);
            r.Flags = BuildFlags(r);
        }
        catch (Exception ex)
        {
            r.Flags = "watch_error:" + ex.GetType().Name;
        }
        return r;
    }

    private static void CaptureTime(Record r)
    {
        var tm = TimeManager.Instance;
        if (tm == null)
            return;
        r.Day = tm.DayNumber;
        float t = tm.TimeOfDaySeconds;
        if (t < 0f) t = 0f;
        // ВАЖНО: в этом проекте час — не 3600 секунд, а WorldTime.SecondsPerHour
        // (сутки = 12000 с). С делением на 3600 отчёт показывал «00:58» вместо
        // «07:00» — ИИ получал неверное игровое время.
        float hourFraction = t / WorldTime.SecondsPerHour;
        r.Hour = (int)hourFraction;
        r.Minute = (int)((hourFraction - r.Hour) * 60f);
        r.Speed = tm.SpeedMultiplier;
        r.Paused = tm.IsPaused;
        if (r.Hour > 23) r.Hour = 23;
        if (r.Minute > 59) r.Minute = 59;
    }

    private static void CaptureJobs(Record r)
    {
        var jd = JobDispatcher.Instance;
        if (jd == null)
            return;
        var idx = jd.JobIndex;
        if (idx == null)
            return;
        r.JobsTotal = idx.TotalCount;
        r.JobsUnclaimed = idx.UnclaimedCount;
        r.JobsClaimed = Math.Max(0, r.JobsTotal - r.JobsUnclaimed);
        r.AgentsIdle = jd.IdleWorkers?.TotalIdleCount ?? 0;
    }

    private static void CaptureSites(Record r)
    {
        var cm = ConstructionManager.Instance;
        if (cm == null)
            return;
        var sites = cm.GetAllSites();
        if (sites == null || sites.Count == 0)
            return;

        r.SitesTotal = sites.Count;
        double progressSum = 0.0;
        int anomalies = 0;
        var broken = new StringBuilder(160);

        foreach (var kv in sites)
        {
            var s = kv.Value;
            if (s == null)
                continue;

            switch (s.Stage)
            {
                case ConstructionStage.Clearance: r.SitesClearance++; break;
                case ConstructionStage.Supplying: r.SitesSupplying++; break;
                case ConstructionStage.Assembling: r.SitesAssembling++; break;
            }

            if (s.HasObstacles) r.SitesBlocked++;
            if (!s.IsFullySupplied) r.SitesUnsupplied++;
            progressSum += s.WorkProgress;

            // Аномалия строгой иерархии: монтаж идёт, хотя этап 1 и/или 2 не закрыт.
            bool bad = s.Stage == ConstructionStage.Assembling && (s.HasObstacles || !s.IsFullySupplied);
            if (bad)
            {
                anomalies++;
                if (anomalies <= BrokenSitesPerRecord)
                {
                    broken.Append('(').Append(s.X).Append(',').Append(s.Y).Append(") ")
                          .Append("sup=").Append(s.TotalDelivered).Append('/').Append(s.TotalRequired)
                          .Append(" obs=").Append(s.HasObstacles ? 'Y' : 'N')
                          .Append(" prog=").Append(s.WorkProgress.ToString("F0", CultureInfo.InvariantCulture))
                          .Append("; ");
                }
            }
        }

        r.SitesAvgProgressMs = progressSum / r.SitesTotal;
        if (anomalies > 0)
            r.SitesBroken = anomalies + " шт. в Assembling без полного комплекта/с препятствиями: " + broken;
    }

    private static void CaptureGroundItems(Record r)
    {
        var gm = GroundItemManager.Instance;
        if (gm == null)
            return;
        r.GroundPiles = gm.TotalGroundPilesCount;
        var items = gm.GetItemsSnapshot();
        if (items == null)
            return;

        int total = 0;
        Span<int> byType = stackalloc int[256];
        for (int i = 0; i < items.Count; i++)
        {
            int id = (int)items[i].Item;
            if ((uint)id < 256u)
                byType[id] += items[i].Count;
            total += items[i].Count;
        }
        r.GroundCount = total;
        r.GroundByItem = BuildByItem(byType);
    }

    private static void CaptureStockpile(Record r)
    {
        var sm = StockpileManager.Instance;
        if (sm == null)
            return;
        r.StockpileTiles = sm.ZoneTileCount;

        Span<int> byType = stackalloc int[256];
        int total = 0;
        foreach (ItemId id in Enum.GetValues<ItemId>())
        {
            if (id == ItemId.None)
                continue;
            int n = sm.GetTotalItemCount(id);
            if (n <= 0)
                continue;
            int i = (int)id;
            if ((uint)i < 256u)
                byType[i] = n;
            total += n;
        }
        r.StockpileCount = total;
        r.StockpileByItem = BuildByItem(byType);
    }

    private static void CaptureZones(Record r)
    {
        var zm = ZoneManager.Instance;
        if (zm == null)
            return;
        var zones = zm.GetAllZones();
        if (zones == null)
            return;
        r.ZonesTotal = zones.Count;
        for (int i = 0; i < zones.Count; i++)
        {
            var z = zones[i];
            if (z == null)
                continue;
            if (z.Kind == ZoneKind.Work) r.ZonesWork++;
            else if (z.Kind == ZoneKind.Farm) r.ZonesFarm++;
        }
    }

    private static void CaptureMetrics(Record r, GameProfiler.MetricSnapshot[] metrics)
    {
        if (metrics == null || metrics.Length == 0)
            return;
        // Берём 6 самых дорогих методов окна — это и есть «какие скрипты работали».
        var sb = new StringBuilder(192);
        int taken = 0;
        for (int i = 0; i < metrics.Length && taken < 6; i++)
        {
            var m = metrics[i];
            if (m.TotalMs <= 0.0 && m.Calls == 0)
                continue;
            if (taken > 0) sb.Append(", ");
            sb.Append(m.Name).Append(' ').Append(m.TotalMs.ToString("F2", CultureInfo.InvariantCulture)).Append("ms");
            if (m.Calls > 0)
                sb.Append('/').Append(m.Calls).Append("c");
            taken++;
        }
        r.TopSimMethods = sb.ToString();
    }

    private static string BuildFlags(Record r)
    {
        var sb = new StringBuilder(96);
        if (r.SitesBlocked > 0)
            sb.Append("стройка_на_грязной_клетке=").Append(r.SitesBlocked).Append("; ");
        if (r.SitesUnsupplied > 0 && r.SitesAssembling > 0)
            sb.Append("монтаж_без_комплекта=").Append(r.SitesUnsupplied).Append("; ");
        if (r.SitesBroken.Length > 0)
            sb.Append("НАРУШЕНА_ИЕРАРХИЯ; ");
        if (r.JobsUnclaimed > 500)
            sb.Append("много_невзятых_работ=").Append(r.JobsUnclaimed).Append("; ");
        if (r.AgentsIdle > 0 && r.JobsUnclaimed > 0)
            sb.Append("агенты_бездельничают_при_работе; ");
        if (r.StockpileTiles == 0 && r.GroundCount > 0)
            sb.Append("нет_склада_предметы_на_земле; ");
        return sb.ToString();
    }

    private static string BuildByItem(Span<int> byType)
    {
        var sb = new StringBuilder(64);
        foreach (ItemId id in Enum.GetValues<ItemId>())
        {
            int i = (int)id;
            if ((uint)i >= 256u)
                continue;
            int n = byType[i];
            if (n <= 0)
                continue;
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(ItemName(id)).Append('=').Append(n);
        }
        return sb.ToString();
    }

    private static string ItemName(ItemId id) => id switch
    {
        ItemId.Log => "Log",
        ItemId.Grain => "Grain",
        ItemId.Stone => "Stone",
        _ => id.ToString()
    };

    private Record[] TakeLast(int seconds)
    {
        lock (_lock)
        {
            int want = seconds > 0 ? Math.Min(seconds, _ring.Count) : _ring.Count;
            if (want <= 0)
                return Array.Empty<Record>();
            var arr = new Record[want];
            _ring.CopyTo(_ring.Count - want, arr, 0, want);
            return arr;
        }
    }

    /// <summary>
    /// Секция для AI-отчёта: Markdown, посекундно (по умолчанию последняя минута).
    /// </summary>
    public string BuildMarkdown(int seconds = DefaultSectionSeconds)
    {
        var recs = TakeLast(seconds <= 0 ? DefaultSectionSeconds : seconds);
        var sb = new StringBuilder(8192);
        sb.AppendLine("### 🎞️ СЛЕЖКА ЗА СИМУЛЯЦИЕЙ (посекундно)");
        if (recs.Length == 0)
        {
            sb.AppendLine("- записей пока нет (слежка включается вместе с оверлеем F3)");
            sb.AppendLine();
            return sb.ToString();
        }

        sb.AppendLine($"- записей в окне: **{recs.Length}** (по 1 в секунду) | всего записано: {TotalSeconds} с");
        sb.AppendLine($"- окно: t={recs[0].Index}…{recs[recs.Length - 1].Index} с");
        sb.AppendLine();

        // Компактная таблица-обзор: одна строка = одна секунда.
        sb.AppendLine("| t,с | день/время | скор | FPS | работа (взято/не взято/всего) | простой | стройка C/S/A | грязн | некомпл | земля | склад | зоны W/F |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|");
        for (int i = 0; i < recs.Length; i++)
        {
            var r = recs[i];
            sb.Append("| ").Append(r.Index)
              .Append(" | ").Append(r.Day).Append(' ').Append(r.Hour.ToString("D2")).Append(':').Append(r.Minute.ToString("D2"))
              .Append(" | ").Append(r.Speed.ToString("F0", CultureInfo.InvariantCulture)).Append("x")
              .Append(r.Paused ? "⏸" : "")
              .Append(" | ").Append(r.Fps)
              .Append(" | ").Append(r.JobsClaimed).Append('/').Append(r.JobsUnclaimed).Append('/').Append(r.JobsTotal)
              .Append(" | ").Append(r.AgentsIdle)
              .Append(" | ").Append(r.SitesClearance).Append('/').Append(r.SitesSupplying).Append('/').Append(r.SitesAssembling)
              .Append(" | ").Append(r.SitesBlocked)
              .Append(" | ").Append(r.SitesUnsupplied)
              .Append(" | ").Append(r.GroundPiles).Append("к/").Append(r.GroundCount).Append("шт")
              .Append(" | ").Append(r.StockpileTiles).Append("к/").Append(r.StockpileCount).Append("шт")
              .Append(" | ").Append(r.ZonesWork).Append('/').Append(r.ZonesFarm)
              .AppendLine(" |");
        }
        sb.AppendLine();

        // Детализация только по «интересным» секундам (аномалии/смена состояния) —
        // иначе на 60 записей отчёт раздувается и ИИ теряет суть.
        sb.AppendLine("**Детализация значимых секунд** (аномалии, появление/исчезновение построек, скачки работ):");
        int shown = 0;
        Record prev = null;
        for (int i = 0; i < recs.Length; i++)
        {
            var r = recs[i];
            bool interesting = prev == null
                || r.Flags.Length > 0
                || r.SitesTotal != prev.SitesTotal
                || r.ZonesTotal != prev.ZonesTotal
                || r.StockpileTiles != prev.StockpileTiles
                || Math.Abs(r.JobsTotal - prev.JobsTotal) > 0
                || (r.SitesAssembling > 0 && prev.SitesAssembling == 0);
            if (interesting && shown < 25)
            {
                AppendDetail(sb, r);
                shown++;
            }
            prev = r;
        }
        if (shown == 0)
            sb.AppendLine("  (за окно состояние не менялось)");
        sb.AppendLine();
        return sb.ToString();
    }

    private static void AppendDetail(StringBuilder sb, Record r)
    {
        sb.Append("- ").Append('`').Append("t=").Append(r.Index).Append("с` ")
          .Append('`').Append("День ").Append(r.Day).Append(' ')
          .Append(r.Hour.ToString("D2")).Append(':').Append(r.Minute.ToString("D2")).Append("` ")
          .Append("| камера ").Append(r.Camera)
          .Append(" | стройка ").Append(r.SitesTotal).Append(" (C=").Append(r.SitesClearance)
          .Append(",S=").Append(r.SitesSupplying).Append(",A=").Append(r.SitesAssembling).Append(')')
          .Append(" | прогресс ").Append(r.SitesAvgProgressMs.ToString("F0", CultureInfo.InvariantCulture)).Append("мс")
          .Append(" | земля ").Append(r.GroundByItem.Length > 0 ? r.GroundByItem : "0")
          .Append(" | склад ").Append(r.StockpileByItem.Length > 0 ? r.StockpileByItem : "0")
          .AppendLine();
        if (r.SitesBroken.Length > 0)
            sb.Append("    - ⚠️ ").AppendLine(r.SitesBroken);
        if (r.Flags.Length > 0)
            sb.Append("    - 🚩 ").AppendLine(r.Flags);
        if (r.TopSimMethods.Length > 0)
            sb.Append("    - скрипты: ").AppendLine(r.TopSimMethods);
    }

    /// <summary>Короткая сводка для строки статуса оверлея.</summary>
    public string BuildShortLine()
    {
        lock (_lock)
        {
            if (_ring.Count == 0)
                return "watch: 0с";
            var r = _ring[_ring.Count - 1];
            return $"watch: {_ring.Count}с | стройка C{r.SitesClearance}/S{r.SitesSupplying}/A{r.SitesAssembling} | земля {r.GroundCount} | склад {r.StockpileCount}";
        }
    }
}
