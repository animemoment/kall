using System;
using System.Collections.Generic;

namespace Game.Core.WorldStreaming;

// ============================================================================
// §118.1 фаза 3: реестр зон поселений — «одна зона на поселение» (фундамент
// мульти-поселений). Просто словарь в памяти, БЕЗ Godot API (можно из фоновых
// потоков). Зона поселения = мировой прямоугольник ActiveArea.Size²;
// ПЕРЕСЕЧЕНИЕ прямоугольников двух поселений запрещено (Register → false:
// клетки одной зоны не могут принадлежать двум поселениям сразу — иначе
// расходятся дельты/мутации). Касание краями — НЕ пересечение.
// ============================================================================

/// <summary>
/// Реестр активных зон поселений. Зона материализуется лениво при первом
/// обращении (TryGetZone/SetActiveZone) — ПУСТОЙ ActiveArea с origin;
/// данные появляются после zone.Fill(seed, version) (зовёт потребитель,
/// например интеграция Lead после выбора спавна).
/// </summary>
public sealed class ActiveAreaRegistry
{
    private sealed class ZoneRecord
    {
        public string Id = "";
        public long OriginX;
        public long OriginY;
        // FIX (bughunt 04-8): порядковый номер регистрации — по нему вытесняем
        // САМУЮ СТАРУЮ неактивную зону, когда реестр упёрся в MaxZones.
        public long Sequence;
        public ActiveArea Area; // ленивая материализация
    }

    /// <summary>
    /// FIX (bughunt 04-8): предел числа зарегистрированных зон.
    ///
    /// Зачем: у <see cref="UnregisterZone"/> в проде НЕ БЫЛО ни одного вызова —
    /// при каждом переключении региона (мировая карта / reroll сида) реестр
    /// получал новый id и рос без границ. Материализованная <see cref="ActiveArea"/>
    /// — это ~15 МБ (6 плоских массивов Size² + Humidity/Fertility); зона, в
    /// которой уже не играют, оставалась в словаре вместе со своей 15-мегабайтной
    /// областью, т.е. память утекала на каждый переход между поселениями.
    ///
    /// Предел сознательно НЕБОЛЬШОЙ: играть одновременно можно в одном поселении,
    /// а остальные записи нужны лишь как лёгкий «реестр координат» (две long на
    /// зону). Materialize (15 МБ) происходит только по запросу TryGetZone, поэтому
    /// 8 записей = максимум записей, а не 8×15 МБ.
    /// </summary>
    public const int MaxZones = 8;

    private readonly Dictionary<string, ZoneRecord> _zones = new();

    // FIX (bughunt 04-8): монотонный счётчик порядка регистраций (для вытеснения
    // «самых старых неактивных»). Доступ — только из потоков, зовущих Register*.
    private long _sequence;

    /// <summary>Активная зона (за которой сейчас играют). null — не выбрана.</summary>
    public ActiveArea ActiveZone { get; private set; }

    /// <summary>Сколько зон зарегистрировано.</summary>
    public int Count => _zones.Count;

    /// <summary>
    /// Зарегистрировать зону поселения [originX, originX+Size) ×
    /// [originY, originY+Size). Отрицательные origin допустимы (мир бесконечен,
    /// §118.1). false — пустой id, ДУБЛЬ id или прямоугольник ПЕРЕСЕКАЕТСЯ
    /// с уже зарегистрированной зоной (строгое перекрытие; касание краями — ок),
    /// либо в реестре MaxZones зон и освободить место нечем (все — активные).
    /// FIX (bughunt 04-8): при переполнении сначала вытесняется самая старая
    /// НЕактивная зона — реестр больше не растёт без границ.
    /// </summary>
    public bool RegisterZone(string id, long originX, long originY)
    {
        if (string.IsNullOrEmpty(id))
            return false;
        if (_zones.ContainsKey(id))
            return false;

        foreach (ZoneRecord z in _zones.Values)
        {
            if (originX < z.OriginX + ActiveArea.Size && z.OriginX < originX + ActiveArea.Size
                && originY < z.OriginY + ActiveArea.Size && z.OriginY < originY + ActiveArea.Size)
                return false;
        }

        // FIX (bughunt 04-8): перед вставкой освобождаем место под MaxZones.
        if (_zones.Count >= MaxZones && !MakeRoomLocked(id))
            return false;

        _zones[id] = new ZoneRecord
        {
            Id = id,
            OriginX = originX,
            OriginY = originY,
            Sequence = ++_sequence
        };
        return true;
    }

    /// <summary>
    /// P0 (bughunt 04-2): зарегистрировать зону, ВЫТЕСНИВ пересекающиеся.
    ///
    /// Зачем: зона поселения — квадрат <see cref="ActiveArea.Size"/> = 1024 клетки,
    /// а шаг выбора региона в мировой карте — 512 клеток. Поэтому любой соседний
    /// регион ГАРАНТИРОВАННО пересекается с уже зарегистрированной зоной, и
    /// строгий <see cref="RegisterZone"/> всегда возвращал false → вызывающий
    /// получал null из TryGetZone и падал в NullReferenceException.
    ///
    /// Играть одновременно можно только в одной колонии, поэтому прежние
    /// (неактивные) регистрации вытесняются: их дельты лежат на диске отдельными
    /// файлами и не теряются. Активная зона тоже снимается — новый выбор региона
    /// и есть смена активной зоны.
    /// </summary>
    public bool RegisterOrReplaceZone(string id, long originX, long originY)
    {
        if (string.IsNullOrEmpty(id))
            return false;

        var remove = new List<string>();
        foreach (ZoneRecord z in _zones.Values)
        {
            if (z.Id == id)
                continue;
            bool overlaps =
                originX < z.OriginX + ActiveArea.Size && z.OriginX < originX + ActiveArea.Size &&
                originY < z.OriginY + ActiveArea.Size && z.OriginY < originY + ActiveArea.Size;
            if (!overlaps)
                continue;
            if (z.Area != null && ReferenceEquals(ActiveZone, z.Area))
                ActiveZone = null;
            remove.Add(z.Id);
        }
        for (int i = 0; i < remove.Count; i++)
            _zones.Remove(remove[i]);

        // FIX (bughunt 04-8): старые НЕактивные зоны вытесняются и тогда, когда
        // новая зона ни с кем не пересекается (переход на другой остров/регион).
        // Раньше удалялись только пересекающиеся, а смена региона на далёкий
        // оставляла в реестре прежнюю 15-мегабайтную зону навсегда.
        MakeRoomLocked(id);

        _zones[id] = new ZoneRecord
        {
            Id = id,
            OriginX = originX,
            OriginY = originY,
            Sequence = ++_sequence
        };
        return true;
    }

    /// <summary>
    /// FIX (bughunt 04-8): явный путь освобождения реестра — снять все зоны,
    /// кроме АКТИВНОЙ (та, где сейчас играют). Возвращает число снятых зон.
    ///
    /// Зачем отдельно: продовый код зовёт только RegisterOrReplaceZone (P0 04-2)
    /// и никогда — UnregisterZone, поэтому без такого метода «закрыть мир»
    /// означало бы утечку всех материализованных зон до конца процесса. Вызывать
    /// при выходе в меню/смене режима: дельты зон уже лежат на диске отдельными
    /// файлами (IslandOverrideStore), так что снятие регистрации данные не теряет.
    /// </summary>
    public int ClearInactive()
    {
        var remove = new List<string>();
        foreach (ZoneRecord z in _zones.Values)
        {
            // Активную зону не трогаем: на неё смотрят потребители (MapDataShim,
            // стример) и её ActiveArea — единственная, которую нельзя освободить.
            if (z.Area != null && ReferenceEquals(ActiveZone, z.Area))
                continue;
            remove.Add(z.Id);
        }

        for (int i = 0; i < remove.Count; i++)
            _zones.Remove(remove[i]);
        return remove.Count;
    }

    /// <summary>
    /// FIX (bughunt 04-8): вытеснить самые старые НЕактивные зоны, пока не
    /// появится место под вставку (MaxZones). Активную зону и зону keepId не
    /// трогаем. Возвращает true, если после вытеснения влезаем в предел.
    ///
    /// Выбор политики: вытесняем именно НЕактивные — рабочую зону нельзя отнять
    /// у игрока; порядок — по Sequence (FIFO), то есть «та, в которой давно не
    /// играли». Материализованная ActiveArea вытесненной записи освобождается
    /// сборщиком мусора: ссылок на неё реестр больше не держит.
    /// </summary>
    private bool MakeRoomLocked(string keepId)
    {
        while (_zones.Count >= MaxZones)
        {
            ZoneRecord oldest = null;
            foreach (ZoneRecord z in _zones.Values)
            {
                if (z.Id == keepId)
                    continue;
                if (z.Area != null && ReferenceEquals(ActiveZone, z.Area))
                    continue; // активную зону не вытесняем никогда
                if (oldest == null || z.Sequence < oldest.Sequence)
                    oldest = z;
            }

            // Все оставшиеся записи — активная зона: вытеснять некого. Предел
            // мягкий (активная зона одна, так что на практике сюда не попадаем).
            if (oldest == null)
                return false;

            _zones.Remove(oldest.Id);
        }
        return true;
    }

    /// <summary>
    /// Снять зону с регистрации. true — зона была. Если снималась АКТИВНАЯ
    /// зона — ActiveZone сбрасывается в null.
    /// </summary>
    public bool UnregisterZone(string id)
    {
        if (string.IsNullOrEmpty(id) || !_zones.Remove(id, out ZoneRecord removed))
            return false;
        if (ReferenceEquals(ActiveZone, removed.Area))
            ActiveZone = null;
        return true;
    }

    /// <summary>
    /// Зона по id. true — зона зарегистрирована; материализуется при первом
    /// обращении и кэшируется (одна и та же инстанция на id). ПУСТАЯ:
    /// IsFilled=false до zone.Fill(seed, version).
    /// </summary>
    public bool TryGetZone(string id, out ActiveArea zone)
    {
        zone = null;
        if (string.IsNullOrEmpty(id) || !_zones.TryGetValue(id, out ZoneRecord rec))
            return false;
        rec.Area ??= new ActiveArea(rec.OriginX, rec.OriginY);
        zone = rec.Area;
        return true;
    }

    /// <summary>
    /// Назначить АКТИВНУЮ зону (где сейчас играет игрок). false — id не
    /// зарегистрирован (ActiveZone не меняется). Зона материализуется как в
    /// TryGetZone.
    /// </summary>
    public bool SetActiveZone(string id)
    {
        if (!TryGetZone(id, out ActiveArea zone))
            return false;
        ActiveZone = zone;
        return true;
    }
}
