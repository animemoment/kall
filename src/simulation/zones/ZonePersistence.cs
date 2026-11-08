using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Game.Simulation;

namespace Game.Core.WorldStreaming.Mutation;

// ============================================================================
// §119 P7б (task-17 п.3): персистентность Work-зон колонии —
// user://colonies/{zoneId}.zones.json.
//
// Формат (v1):
//   { "version": 1, "seed": <ulong как строка>, "nextId": int,
//     "zones": [ { "id", "name", "center": [cx, cy], "radius": 64..512,
//                  "jobMask": "<ulong как строка>", "desiredWorkers",
//                  "priorityOverride": 1..10 | -1, "enabled": bool,
//                  "anchorBlockId": int } ] }
//
// Правила:
//  * Атомарная запись: .tmp рядом → File.Move (паттерн IslandOverrideStore.Save).
//  * Очереди работ НЕ сериализуются (v1): после загрузки их соберёт репуш P1 (≤30с).
//  * Seed-ключ: файл другого сида — мусор, чистится ФАЙЛ и зоны менеджера
//    («RebindSeed чистит и зоны»).
//  * Farm-зоны в v1 НЕ сохраняются (их тайлы — отдельная задача; после загрузки
//    их не будет, только Work-зоны).
//  * Сейв читает снапшот GetAllZones ПОД Lock менеджера; чужих локов нет.
// ============================================================================

public static class ZonePersistence
{
    private const int FormatVersion = 1;

    /// <summary>Путь файла зон колонии (user://).</summary>
    public static string ZonesPath(string zoneId)
        => ProjectSettings.GlobalizePath($"user://colonies/{zoneId}.zones.json");

    /// <summary>
    /// Сохранить Work-зоны менеджера. seed — «чистый» сид мира (без примеси
    /// версии генератора: зоны не зависят от формул генерации).
    /// </summary>
    public static bool SaveZones(string zoneId, ulong seed, out string error)
    {
        error = null;
        if (string.IsNullOrEmpty(zoneId))
        {
            error = "пустой zoneId";
            return false;
        }

        List<Zone> snapshot = ZoneManager.Instance.GetAllZones();
        var zonesArray = new Godot.Collections.Array();
        int nextId = 1;
        foreach (Zone z in snapshot)
        {
            if (z == null || !z.IsWorkZone)
                continue;
            var d = new Godot.Collections.Dictionary
            {
                // FIX (bughunt 13-3): тип зоны теперь пишется явно. Отсутствие поля
                // в старых файлах трактуется как Work — обратная совместимость.
                ["kind"] = "work",
                // task-12: ОТРАСЛЬ зоны. Поле добавлено БЕЗ смены version формата:
                // старые читатели его игнорируют, новый читатель без поля
                // восстанавливает отрасль по маске работ (см. TryLoadZones).
                ["industry"] = ZoneIndustryRegistry.IdFor(z.Kind),
                ["id"] = z.Id,
                ["name"] = z.Name ?? string.Empty,
                ["center"] = new Godot.Collections.Array { z.CenterX, z.CenterY },
                ["radius"] = z.RadiusTiles,
                ["jobMask"] = z.JobMask.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["desiredWorkers"] = z.DesiredWorkers,
                ["priorityOverride"] = z.PriorityOverride ?? -1,
                ["enabled"] = z.Enabled,
                ["anchorBlockId"] = z.AnchorBlockId
            };
            zonesArray.Add(d);
            if (z.Id >= nextId)
                nextId = z.Id + 1;
        }

        // FIX (bughunt 13-3): Farm-зоны раньше не писались вообще — после перезапуска
        // грядки и культуры были (они едут островными дельтами), а зон не было:
        // окно фермы не открывалось, AutoPlant не возобновлялся.
        List<FarmZone> farmSnapshot = FarmZoneManager.Instance.GetAllZones();
        foreach (FarmZone fz in farmSnapshot)
        {
            if (fz == null || fz.Tiles == null || fz.Tiles.Count == 0)
                continue;
            var tilesArr = new Godot.Collections.Array();
            foreach ((int x, int y) in fz.Tiles)
                tilesArr.Add(new Godot.Collections.Array { x, y });
            zonesArray.Add(new Godot.Collections.Dictionary
            {
                ["kind"] = "farm",
                ["id"] = fz.Id,
                ["name"] = fz.Name ?? string.Empty,
                ["tiles"] = tilesArr,
                ["autoPlant"] = fz.AutoPlantEnabled,
                // task-13 (запрос Lead): культура зоны-фермы. Раньше писался только
                // autoPlant — после загрузки все грядки становились пшеницей, хотя
                // игрок выбрал лён. Crop — истинное поле Zone.Crop (адаптер FarmZone
                // читает его же). Чистое число = стабильно к переименованиям.
                ["crop"] = (int)fz.Crop
            });
        }

        var root = new Godot.Collections.Dictionary
        {
            ["version"] = FormatVersion,
            ["seed"] = seed.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["nextId"] = nextId,
            ["zones"] = zonesArray
        };

        try
        {
            string json = Json.Stringify(root, "  ");
            string path = ZonesPath(zoneId);
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            string temp = path + ".tmp";
            File.WriteAllText(temp, json);
            if (File.Exists(path))
                File.Delete(path);
            File.Move(temp, path);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Загрузить Work-зоны. false — файла нет (не ошибка) или seed не совпал
    /// (файл и зоны менеджера ЧИСТЯТСЯ — «RebindSeed чистит и зоны»). Битый
    /// JSON — исключение наружу (тихо терять зоны нельзя).
    /// </summary>
    public static bool TryLoadZones(string zoneId, ulong seed, out string error)
    {
        error = null;
        if (string.IsNullOrEmpty(zoneId))
        {
            error = "пустой zoneId";
            return false;
        }

        string path = ZonesPath(zoneId);
        if (!File.Exists(path))
            return false; // первая сессия колонии

        string json = File.ReadAllText(path);
        Variant parsed = Json.ParseString(json);
        if (parsed.VariantType == Variant.Type.Nil)
        {
            error = "битый JSON зон";
            return false;
        }

        Godot.Collections.Dictionary root = parsed.AsGodotDictionary();
        if (root == null || (int)root["version"].AsInt64() != FormatVersion)
        {
            error = "неизвестная версия формата .zones.json";
            return false;
        }

        // Seed-ключ: несовпадение = зоны прошлого мира → чистим файл и менеджер.
        string storedSeed = root["seed"].AsString();
        if (storedSeed != seed.ToString(System.Globalization.CultureInfo.InvariantCulture))
        {
            ZoneManager.Instance.ClearAll();
            try { File.Delete(path); } catch { /* лучшее усилие */ }
            error = null;
            return false;
        }

        // Раз zones НЕ Farm — ClearAll перед восстановлением обязателен
        // (диспатч ещё не стартовал: гонок нет).
        ZoneManager.Instance.ClearAll();

        Godot.Collections.Array zones = root["zones"].AsGodotArray();
        if (zones != null)
        {
            foreach (Variant zv in zones)
            {
                Godot.Collections.Dictionary z = zv.AsGodotDictionary();
                if (z == null)
                    continue;
                // FIX (bughunt 13-3): Farm-зоны восстанавливаем отдельной веткой.
                // В старых файлах поля "kind" нет — это Work (обратная совместимость).
                string kind = z.ContainsKey("kind") ? z["kind"].AsString() : "work";
                if (kind == "farm")
                {
                    RestoreFarmZone(z);
                    continue;
                }
                Godot.Collections.Array center = z["center"].AsGodotArray();
                if (center == null || center.Count < 2)
                    continue;
                ulong jobMask;
                if (!ulong.TryParse(z["jobMask"].AsString(),
                        System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out jobMask)
                    || jobMask == 0)
                    continue; // битая маска — зона пропускается, остальные живут

                int? priority = null;
                int pRaw = (int)z["priorityOverride"].AsInt64();
                if (pRaw >= 1 && pRaw <= 10)
                    priority = pRaw;

                // task-12: отрасль из файла. Нет поля (старый сейв) или мусор —
                // отрасль восстанавливаем по маске работ (та же миграция, что
                // раньше делал ZoneManager, только здесь она нужна ДО выбора маски).
                ZoneKind industry;
                if (z.ContainsKey("industry")
                    && ZoneIndustryRegistry.TryGet(z["industry"].AsString(), out var profile))
                {
                    industry = profile.Kind;
                }
                else
                {
                    industry = ZoneIndustryRegistry.FromLegacyJobMask(jobMask);
                }

                // AUDIT task-17 (M-3): СТАРЫЕ .zones.json могли хранить МНОГОБИТОВУЮ
                // маску («безымянная Work-зона» с несколькими разрешёнными работами).
                // §119 P0 требует ровно один бит, поэтому
                // RestoreWorkZoneForPersistence такие записи отвергал — и зона МОЛЧА
                // исчезала при загрузке (а следующий SaveZones затирал файл уже без
                // неё). Многобитовая модель всё равно нерабочая: ZoneCoverageMap
                // отбрасывает зоны с ≠1 битом, диспатч берёт один тип. Поэтому не
                // теряем зону, а мигрируем маску в ОСНОВНУЮ работу отрасли.
                ulong restoreMask = jobMask;
                if (!IsSingleWorkBit(jobMask))
                {
                    restoreMask = ZoneIndustryRegistry.PrimaryJobMask(industry);
                    if (restoreMask == 0)
                        continue; // отрасль без основной работы — бригадира не создаём
                    GD.Print($"[Zones] {zoneId}: маска 0x{jobMask:X} не однобитовая — " +
                             $"зона мигрирована в отрасль «{ZoneIndustryRegistry.TitleFor(industry)}» " +
                             $"(работа 0x{restoreMask:X})");
                }

                ZoneManager.Instance.RestoreWorkZoneForPersistence(
                    (int)z["id"].AsInt64(),
                    z["name"].AsString(),
                    (int)center[0].AsInt64(),
                    (int)center[1].AsInt64(),
                    (int)z["radius"].AsInt64(),
                    (int)z["desiredWorkers"].AsInt64(),
                    restoreMask,
                    priority,
                    z["enabled"].AsBool(),
                    (int)z["anchorBlockId"].AsInt64(),
                    industry);
            }
        }

        return true;
    }

    /// <summary>
    /// Маска содержит ровно ОДИН бит работы (не 0 и не легаси-бит None).
    /// AUDIT task-17 (M-3): критерий совпадает с валидацией
    /// <see cref="ZoneManager.RestoreWorkZoneForPersistence"/> и
    /// <see cref="ZoneCoverageMap"/>, но проверяется ДО вызова, чтобы успеть
    /// мигрировать старую многобитовую маску вместо потери зоны.
    /// </summary>
    private static bool IsSingleWorkBit(ulong mask)
        => mask != 0 && (mask & 1UL) == 0 && (mask & (mask - 1UL)) == 0;

    /// <summary>
    /// FIX (bughunt 13-3): восстановить Farm-зону из записи файла.
    ///
    /// Идемпотентно: если в первой клетке зона уже есть, запись пропускается —
    /// у FarmZoneManager нет публичного ClearAll, поэтому повторная загрузка
    /// без этой проверки плодила бы дубликаты. Пользуемся только публичным API.
    /// </summary>
    private static void RestoreFarmZone(Godot.Collections.Dictionary z)
    {
        Godot.Collections.Array raw = z["tiles"].AsGodotArray();
        if (raw == null || raw.Count == 0)
            return;

        var tiles = new List<(int X, int Y)>(raw.Count);
        foreach (Variant tv in raw)
        {
            Godot.Collections.Array pair = tv.AsGodotArray();
            if (pair == null || pair.Count < 2)
                continue;
            tiles.Add(((int)pair[0].AsInt64(), (int)pair[1].AsInt64()));
        }
        if (tiles.Count == 0)
            return;

        if (FarmZoneManager.Instance.TryGetZoneAt(tiles[0].X, tiles[0].Y, out _))
            return; // уже восстановлена в этой сессии (дубль записи/повторный вызов)

        // task-13: культура фермы. Старый файл без "crop" → пшеница (прежнее поведение).
        var crop = CropType.Wheat;
        if (z.ContainsKey("crop"))
        {
            int cropRaw = (int)z["crop"].AsInt64();
            var parsed = (CropType)cropRaw;
            if (Enum.IsDefined(parsed))
                crop = parsed;
        }
        bool autoPlant = z.ContainsKey("autoPlant") && z["autoPlant"].AsBool();
        int savedId = z.ContainsKey("id") ? (int)z["id"].AsInt64() : 0;
        string savedName = z.ContainsKey("name") ? z["name"].AsString() : null;

        // AUDIT task-17 (M-4): восстанавливаем СОХРАНЁННЫЙ id — иначе CropData.ZoneId
        // из островных дельт и любые внешние привязки после перезапуска указывали бы
        // в пустоту. Fallback (битый id/дубль) — старый путь с новым id: лучше зона
        // без стабильного номера, чем потерянная ферма.
        if (FarmZoneManager.Instance.RestoreZone(savedId, savedName, tiles, autoPlant, crop))
            return;

        FarmZoneManager.Instance.CreateZone(tiles);

        if (!FarmZoneManager.Instance.TryGetZoneAt(tiles[0].X, tiles[0].Y, out FarmZone created)
            || created == null)
            return;

        if (crop != CropType.Wheat)
            FarmZoneManager.Instance.SetCrop(created.Id, crop);

        if (autoPlant)
            FarmZoneManager.Instance.SetAutoPlant(created.Id, true);
    }
}
