# 01 · Сторы, сохранность, выход — планы минимальных правок (волна 2026-11-07)

> **Задача:** для каждого назначенного бага дать правку, готовую к передаче исполнителю. Код read-only; git/Godot/`dotnet build` в этой волне не запускались.
> **Доказательность:** все цитаты и номера строк сняты с текущего рабочего дерева в этой волне. Сборка на входе зелёная — это не говорит о рантайме.
> **Не в скоупе:** z-слои (`R2-01`) в этой волне уже закрыты в дереве другим исполнителем (`StreamingWorldView.MakeLayer` ставит `ZAsRelative=false; ZIndex=zIndex`), устаревшие §13-цитаты по ним здесь не используются. Формат `WorldChunkCodec`/`WorldChunkDeltaCodec` — не трогаем (кроме чтения).
> **Источники карточек:** `_plan/BUGS_REGISTRY.md` §13.1 (`N-01…N-06`), §16 (`C2-*`), §17 (`W2-06`, `W2-07`).

## Назначенные баги

| ID | P | Правка | Объём | Перестройка |
|---|---|---|---|---|
| **N-01** | P1 | `IslandOverrideStore.Save`: fsync + `File.Move(overwrite)`, подхват `.tmp` в `Load` | S | нет |
| **N-02** | P1 | карантин битого `.island.delta` при неудачной загрузке | S | нет |
| **N-06** | P3 | `Load`: проверка короткого `ReadBytes`, хвоста файла и заявленного `count` | S | нет |
| **C2-02** | P1 | **дубль N-01/N-02** — отдельной правки нет (закрывается вместе с ними) | — | нет |
| **C2-01** | P1 | `SaveZones` перед сменой колонии (не только в `_ExitTree`) | M | нет |
| **C2-03** | P1 | снапшот куч снимается ДО удаления старых меток `GroundItems` | S | нет |
| **C2-11** | P1 | каталог мирового стора = сид **+ origin региона** (3 точки + 1 хелпер) | M (2 файла) | **ТРЕБУЕТ РЕШЕНИЯ ВЛАДЕЛЬЦА** |
| **C2-12** | P2 | `WorldManifest.StartTileX/Y` = origin региона вместо `(0,0)` | S | нет |
| **C2-13** | P2 | уборка осиротевших `*.tmp` и томбстонов `*.deleted` | M | нет |
| **W2-06** | P1 | flush не УДАЛЯЕТ дельту, которую стример не прочитал | S | нет |
| **W2-07** | P2 | `_agentThread.Stop()` до снятия состояния колонии в `_ExitTree` | S | нет |

**Итого:** S — 7, M — 3, L — 0, дублей — 1 (`C2-02`).
Оговорка по объёму: `C2-11` — «M» по трудоёмкости (~22 строки), хотя формально это 2 файла; `C2-13` — «M» из-за try/catch-обвязки уборки, логики в ней нет.

---

### N-01 · P1 · Сохранение островных дельт: fsync + атомарная замена + подхват `.tmp`

- **Файлы:** `src/core/world/mutation/IslandOverrideStore.cs:290-312` (`Save`), `:319-322` (`Load`, вход в файл).
  Образец стандарта проекта: `src/core/world/streaming/storage/WorldChunkStore.cs:76-104` (`stream.Flush(flushToDisk: true);` + `File.Move(tempPath, targetPath, overwrite: true);`) и `WorldManifestStore.cs:43-64`.
- **Минимальная правка (было → стало):**
  ```csharp
  // IslandOverrideStore.cs:290-312 — БЫЛО
  string temp = path + TempSuffix;
  using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
  using (var writer = new BinaryWriter(stream))
  { …writer.Write(…); }                       // fsync НЕ вызывается
  if (File.Exists(path))
      File.Delete(path);                      // ← окно: целевой файл отсутствует
  File.Move(temp, path);                      // ← крэш до этой строки = потеря и старого, и нового
  ```
  ```csharp
  // СТАЛО — 3 правки, тело записи не меняется
  using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
  using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
  {                                            // leaveOpen: BinaryWriter.Dispose ниже не закроет поток
      …writer.Write(…);                        // ← те же строки :294-307
      writer.Flush();
      stream.Flush(flushToDisk: true);         // ← fsync до замены (стандарт WorldChunkStore.cs:93)
  }
  File.Move(temp, path, overwrite: true);      // ← атомарная замена; строки File.Delete больше нет
  ```
  Плюс подхват `.tmp` в `Load` (сбой мог случиться до `File.Move` — тогда самый свежий снимок лежит в `.tmp`, а целевой цел или отсутствует):
  ```csharp
  // :321 — БЫЛО
  if (string.IsNullOrEmpty(path) || !File.Exists(path))
      return false;
  // СТАЛО
  if (string.IsNullOrEmpty(path))
      return false;
  if (!File.Exists(path) && File.Exists(path + TempSuffix))
      path += TempSuffix;                     // читаем только если целевогo НЕТ (иначе он валиден)
  if (!File.Exists(path))
      return false;
  ```
- **Что НЕ трогаем:** формат (`Magic`/`FormatVersion`/порядок полей), `WorldChunkStore`/`WorldChunkDeltaCodec`, `ZonePersistence` (у него **та же** неатомарная схема `Delete+Move` в `src/simulation/zones/ZonePersistence.cs:124-128` — отдельная карточка-кандидат, здесь не правим), `Main.cs` (кроме N-02).
- **Риск:** `File.Move(..., overwrite: true)` на Windows требует, чтобы целевой файл не был открыт на запись другим хэндлом — в игре пишет только этот стор, читает `Load` через `FileShare.Read`, окно чтения и записи не пересекаются. Если на площадке политика запрещает overwrite-move — откат в `File.Replace(temp, path, null)` с тем же fsync. Ничего не теряется: до `Move` старый файл цел, `.tmp` остаётся для диагностики.
- **Проверка:**
  - статически: `grep -n "Flush(flushToDisk" src/core/world/mutation/IslandOverrideStore.cs` (было 0, стало 1), `grep -n "File.Delete(path)"` в `Save` — 0;
  - стенд: существующий `tests/TestIslandMirror.cs` (round-trip `Save`+`Load`, строки ~159-225) должен остаться зелёным; добавить кейс «целевого файла нет, `.tmp` есть» → `Load` возвращает `true` и метки на месте;
  - рантайм (человеком): убить процесс во время сохранения на выходе, затем запустить — в логе не должно быть «островные дельты не загружены», либо `.tmp` подхвачен.
- **Объём:** S (~8 строк).
- **Перестройка:** нет.

---

### N-02 · P1 · Битый `.island.delta` не затирается молча: карантин в `<файл>.corrupt-<utc>`

- **Файлы:** `scenes/main/Main.cs:166-186` — ветка `Load` в `PrepareIslandStore` (catch на `:182-185`).
- **Минимальная правка (было → стало):**
  ```csharp
  // :182-185 — БЫЛО: причина только в лог, файл остаётся на месте и будет перезаписан при выходе
  catch (Exception ex)
  {
      GD.PrintErr($"[World] {zoneId}: островные дельты не загружены — {ex.Message}");
  }
  ```
  ```csharp
  // СТАЛО: откладываем файл, чтобы следующее сохранение не уничтожило улику и данные
  catch (Exception ex)
  {
      string quarantine = path + $".corrupt-{DateTime.UtcNow:yyyyMMdd_HHmmss}";
      try
      {
          System.IO.File.Move(path, quarantine, overwrite: true);
          GD.PrintErr($"[World] {zoneId}: островные дельты не загружены — {ex.Message}; " +
                      $"файл отложен в {quarantine}");
      }
      catch (Exception moveEx)
      {
          GD.PrintErr($"[World] {zoneId}: островные дельты не загружены — {ex.Message}; " +
                      $"карантин не удался ({moveEx.Message})");
      }
  }
  ```
  Примечание: в `Main.cs` путь к файлам уже пишется полностью квалифицированно (`System.IO.Path` на `:346`), поэтому `System.IO.File` — в том же стиле (не полагаемся на `using System.IO`).
- **Что НЕ трогаем:** диагностику «дельты другого мира проигнорированы» (`:171-176`) — там **не** исключение, а проверка `Seed`: файл валиден для другого мира, карантин ему не нужен. `IslandOverrideStore.Load` (бросает исключение намеренно — «тихо терять нельзя»), `_ExitTree` — не трогаем: N-02 закрывает единственный путь, на котором битый файл доживал до перезаписи. NB: если карантин сам не удался (внутренний `catch`), поведение остаётся прежним (лог об ошибке загрузки), т.е. хуже не становится.
- **Риск:** `File.Move` на битом файле не требует чтения — сработает; при `UnauthorizedAccess` попадём во внутренний catch и продолжим как раньше (регресса нет). Диск: один лишний файл на инцидент, до десятков МБ. Уборку `.corrupt-*` **не** автоматизируем (в C2-13 сознательно не включаем — это улики).
- **Проверка:** стенд — обрезать `<zoneId>.island.delta` на середине, запустить загрузку колонии: в логе «файл отложен в …corrupt-…», в `user://colonies/` появился `*.island.delta.corrupt-<utc>`, а новый файл создаётся только следующим сохранением. Рантайм — тот же сценарий в игре после смены региона.
- **Объём:** S (~10 строк).
- **Перестройка:** нет.

---

### N-06 · P3 · `Load` принимает усечённый файл: проверки короткого чтения, хвоста и заявленного `count`

- **Файлы:** `src/core/world/mutation/IslandOverrideStore.cs:335-352` (шапка + цикл записей).
- **Минимальная правка (было → стало):**
  ```csharp
  // :335-337 + :347-352 — БЫЛО
  int count = reader.ReadInt32();
  if (count < 0) throw new InvalidDataException($"Отрицательное число записей ({count}): {path}");
  …
  if (len > 0)
      payload = reader.ReadBytes(len);        // на EOF молча возвращает МЕНЬШЕ байт, без исключения
  …
  }                                           // конец for: ни сверки count, ни проверки хвоста
  ```
  ```csharp
  // СТАЛО — три независимые проверки
  int count = reader.ReadInt32();
  if (count < 0) throw new InvalidDataException($"Отрицательное число записей ({count}): {path}");
  // (1) count не может превышать остаток файла: min-запись = 8+8+1+4 = 21 байт.
  //     Заодно отсекает OOM от «count = int.MaxValue» через new Dictionary(count).
  const int MinRecordBytes = 21;
  long remaining = stream.Length - stream.Position;
  if (count > remaining / MinRecordBytes)
      throw new InvalidDataException($"Заявлено {count} записей, в файле осталось {remaining} байт: {path}");
  …
  if (len > 0)
  {
      payload = reader.ReadBytes(len);
      if (payload.Length != len)               // (2) короткое чтение payload на EOF
          throw new InvalidDataException($"Усечённый payload ({payload.Length}/{len}) в {path}");
  }
  …
  }                                           // конец for
  if (stream.Position != stream.Length)        // (3) мусор после последней записи
      throw new InvalidDataException($"Хвост {stream.Length - stream.Position} байт после {count} записей: {path}");
  ```
- **Что НЕ трогаем:** формат/версию, поведение «нет файла → `false`» (`:321`), N-01-правку `Load` (подхват `.tmp` — она в начале функции, конфликта нет), `TrySave`.
- **Риск:** строгие проверки отвергают файлы, которые раньше «загружались с мусором» — это цель. Ложных срабатываний на валидных файлах нет: `count` пишется в шапку, каждая запись — ровно 21+len байт, хвоста писатель не оставляет. После N-02 такой файл уходит в карантин, а не затирается.
- **Проверка:** стенд в `tests/TestIslandMirror.cs` (есть сценарии битого/junk файла, ~:196-225): добавить 3 кейса — (а) файл обрезан на середине payload → `InvalidDataException`; (б) `count` увеличен на 1 → `InvalidDataException`; (в) в конец дописано 4 байта → `InvalidDataException`. Все три должны попасть в карантин по N-02 (интеграционный кейс — в `Main`).
- **Объём:** S (~11 строк, все — проверки без новой логики).
- **Перестройка:** нет.

---

### C2-02 · P1 · `IslandOverrideStore.Save` не durable — **дубль N-01/N-02, отдельной правки нет**

- **Файлы:** те же — `src/core/world/mutation/IslandOverrideStore.cs:290-312` (Save), `scenes/main/Main.cs:182-185` (catch).
- **Что делать:** **ничего дополнительно.** Карточка §16 фиксирует только новое доказательство: островной стор — регресс относительно уже принятого в проекте стандарта (`WorldChunkStore.cs:93` fsync, `:99` move-overwrite; `WorldManifestStore.cs:55` fsync). Эта правка **закрывается N-01 (fsync+атомарность) и N-02 (карантин)**; дублировать код не нужно.
- **Что НЕ трогаем:** см. N-01/N-02; комментарий `:267-270` в `IslandOverrideStore.cs` («Сохранить стор атомарной заменой файла (как WorldChunkStore)») после N-01 становится правдой — его можно оставить как есть.
- **Риск:** отсутствует (правки нет).
- **Проверка:** после применения N-01 проверить, что `grep -c "File.Delete(path)" src/core/world/mutation/IslandOverrideStore.cs` = 0 и в `Save` есть `flushToDisk: true`; после N-02 — что карантин срабатывает (см. N-02).
- **Объём:** — (0 строк).
- **Перестройка:** нет.

---

### C2-01 · P1 · Смена региона теряет `.zones.json`: сохранять зоны в той же точке, где сохраняются дельты

- **Файлы:** `scenes/main/Main.cs:124-153` (ветка смены колонии в `PrepareIslandStore`), `:155-156` (привязка зоны), `:862` (`SaveZones` в `_ExitTree`), `:56-63` (поля стора); `src/simulation/zones/ZonePersistence.cs:35-49,143-179` (путь/формат/seed-ключ).
- **Механизм (доказательство):** единственная запись зон — `_ExitTree` (`Main.cs:859-866`). Штатная смена колонии идёт через `PrepareIslandStore` (`OnRegionSelected:505`, `OnSeedRegenerated:412`), где сохраняются **только дельты** (`:139-149`), после чего `ResetColonyState()` (`:446`, `:539`) делает `ZoneManager.ClearAll()` (`:783`), а возврат читает `RestoreZonesFromDisk` (`:197-226`) → «файл зон отсутствует (первая сессия колонии)».
- **Минимальная правка (было → стало):**
  ```csharp
  // 1) поле (рядом с :60) — сид, к которому привязана текущая колония; WorldSeed на reroll уже НОВЫЙ
  private string _islandStoreZoneId;
  private ulong _islandStoreSeed;               // ← добавить
  ```
  ```csharp
  // 2) в ветке смены колонии, ПОСЛЕ сохранения дельт (:139-149) и ДО Clear() (:150)
  int dropped = _islandDeltas.Clear();
  // --- вставить перед этой строкой ---
  try
  {
      if (ZonePersistence.SaveZones(_islandStoreZoneId, _islandStoreSeed, out string switchZonesError))
          GD.Print($"[World] {_islandStoreZoneId}: зоны сохранены перед сменой колонии");
      else if (!string.IsNullOrEmpty(switchZonesError))
          GD.PrintErr($"[World] {_islandStoreZoneId}: зоны НЕ сохранены при смене — {switchZonesError}");
  }
  catch (Exception ex)
  {
      GD.PrintErr($"[World] {_islandStoreZoneId}: ошибка сохранения зон — {ex.Message}");
  }
  ```
  ```csharp
  // 3) привязка (:155-156) — запомнить сид колонии
  _islandZoneId = zoneId;
  _islandStoreZoneId = zoneId;
  _islandStoreSeed = seed;                      // ← добавить
  ```
  ```csharp
  // 4) _ExitTree:862 — писать тот же сид, которым колония была связана
  if (ZonePersistence.SaveZones(_islandZoneId, WorldSeed, out string zonesError))     // БЫЛО
  if (ZonePersistence.SaveZones(_islandZoneId, _islandStoreSeed, out string zonesError)) // СТАЛО
  ```
  Почему поле, а не `WorldSeed`: на reroll `WorldSeed` присваивается **до** `PrepareIslandStore` (`:385` → `:412`), и зоны старого мира уехали бы в файл с новым сидом; `TryLoadZones` при возврате увидит несовпадение seed и **удалит** файл (`ZonePersistence.cs:171-179`). Нормальная смена региона (без reroll) этим полем не затрагивается: `_islandStoreSeed == WorldSeed`.
- **Что НЕ трогаем:** `ZonePersistence`/формат/`TryLoadZones`/`RestoreZonesFromDisk`; порядок «загрузка зон после `ResetColonyState`» (`:545`, `:454` — комментарий P0 bughunt 04-3); `_ExitTree`-структуру.
- **Риск:** `SaveZones` вызывается, пока менеджеры ещё живы (до `ResetColonyState`) — это условие корректности, порядок уже такой и не меняется. Если `SaveZones` бросит — исключение проглочено, смена колонии продолжится (деградация как раньше, без новых падений). **Отдельно:** `ZonePersistence.SaveZones` сам неатомарен (`:124-128` `File.WriteAllText` → `Delete` → `Move`, без fsync и без уникального `.tmp`) — это отдельный дефект, в реестре не заведён; C2-01 его не лечит, но и не усугубляет (частота записи растёт с 1 до N за сессию — при сменах региона).
- **Проверка:** сценарий в игре: поставить 2 зоны → уйти на карту мира в другой регион → вернуться: зоны на месте, файл `user://colonies/<zoneId>.zones.json` существует и содержит `seed` прежней колонии. Стенд: `tests/TestIslandZonePersistence.cs` (round-trip зон) + новый кейс «две зоны → SaveZones по смене → Clear → TryLoadZones».
- **Объём:** M (~14 строк: 1 поле + 9 строк try/catch + 2 присваивания + 1 замена).
- **Перестройка:** нет.

---

### C2-03 · P1 · `IslandStateSnapshot`: сначала читаем кучи, потом снимаем метки

- **Файлы:** `src/core/world/mutation/IslandStateSnapshot.cs:130-171` (блок «GroundItems: синхронизация набора меток»).
- **Механизм (доказательство):**
  ```csharp
  // :137-143 — снимаем ВСЕ старые метки
  var rm = new WorldMutation[existing.Count]; …
  groundRemoved = store.RemoveBatch(rm);
  // :145 — только ПОСЛЕ этого читаем состояние (глобальный лок GroundItemManager)
  var piles = GroundItemManager.Instance.GetItemsSnapshot();
  // :163 — и только потом добавляем; всё в одном try с catch-warning (:167-171) — откатa нет
  groundApplied = store.ApplyBatch(add);
  ```
  Если `GetItemsSnapshot`/`ApplyBatch` бросят, в сторе остаётся **ноль** меток `GroundItems`, и `Save` запишет именно это (а `_agentThread.Stop()` в `Main.cs:874` вызывается позже — сим-поток ещё жив).
- **Минимальная правка (было → стало): перестановка блоков, новой логики нет**
  ```csharp
  // :130-165 — СТАЛО (порядок: собрать всё → удалить → добавить)
  try
  {
      var existing = new List<(long X, long Y)>();
      foreach ((byte kind, long wx, long wy) in cells)
          if (kind == WorldDeltaKind.GroundItems)
              existing.Add((wx, wy));

      // (перенесено ВВЕРХ из :145-164) сначала потенциально бросающее чтение
      var piles = GroundItemManager.Instance.GetItemsSnapshot();
      var add = new WorldMutation[piles.Count];
      int n = 0;
      foreach (var p in piles)
      {
          long wx = originX + p.X, wy = originY + p.Y;
          if (p.X < 0 || p.Y < 0 || wx > maxX || wy > maxY)
              continue;
          add[n++] = new WorldMutation(WorldDeltaKind.GroundItems, wx, wy,
              IslandPayload.WriteGroundItems(p.Item3, p.Item4));
      }
      if (n != add.Length)
          Array.Resize(ref add, n);

      // (ниже — уже существующие блоки удаления и добавления, без изменений)
      if (existing.Count > 0) { … store.RemoveBatch(rm) … }
      if (n > 0)             { … store.ApplyBatch(add) … }
  }
  ```
- **Что НЕ трогаем:** верхнюю часть `RefreshPayloads` (снапшоты `Stockpile`/`Crop`/`WallPlan`/`BuildingPlan`/`CutTreePlan`/`MinedStonePlan`, `:51-125`), `IslandPayload`, формат payload, вызовы в `Main.cs:838`/`:132`.
- **Риск:** перестановка меняет порядок событий стора (сначала чтение, затем `RemoveBatch` → `ApplyBatch`) — внешние подписчики (`IslandMutationMirror`) увидят то же «удалить старые, добавить новые», только позже на несколько микросекунд; окно, в котором метки отсутствуют, сокращается. Остаточный риск: если `ApplyBatch` бросит **после** удаления, метки всё равно потеряются — полное закрытие требует откатa (сохранить старые payload’ы через `TryGet` в `List<WorldMutation>` и вернуть их в `catch`); это +10 строк, вынесено в отдельный необязательный шаг, не в минимум.
- **Проверка:** стенд (без движка? `IslandStateSnapshot` использует `Godot` GD.Print при `writeLog`, передавать `writeLog: false`): смонтировать `GroundItemManager` с 3 кучами, подложить исключение в `GetItemsSnapshot` (обёртка/шаблон теста), вызвать `RefreshPayloads(store, …, writeLog:false)` → `store.Count` по `GroundItems` должен остаться прежним (3), а не 0. Плюс инвариант «после успешного вызова число меток `GroundItems` == числу куч в зоне».
- **Объём:** S (перестановка ~15 строк, новой логики — 0).
- **Перестройка:** нет.

---

### C2-11 · P1 · Мировой чанк-стор: каталог = сид **+ origin региона**

- **Файлы:** `scenes/main/Main.cs:344-350`; `src/ui/streaming/StreamingWorldView.cs:144-149` и `:257-262`; хелпер — `src/core/world/streaming/storage/WorldChunkStore.cs` (новый `public static`, рядом с `GetChunkFilePath`, `:208-218`).
- **Механизм (доказательство):** каталог — только сид: `Main.cs:346` `$"world_{seed:x16}"`; то же в виде: `StreamingWorldView.cs:148` и `:261` (у вида **свой** `_store`, он не берёт каталог у координатора). Формат `WorldSaveMetadata` origin'а не содержит (`src/core/world/streaming/storage/WorldSaveMetadata.cs:7-12`), `WorldSaveCoordinator.EnsureCompatible` (`:194-221`) сверяет только seed/gen/schema, дельты адресуются **локальными** индексами чанков (`GetChunkFilePath`, `:209-218`). При этом `SetWorldOrigin` меняется при выборе региона (`Main.cs:496`), т.е. `chunk_0_0.bin` одного каталога описывает разные клетки мира. Островной стор, наоборот, per-zone (`Main.cs:107`) — асимметрия.
- **Минимальная правка (было → стало):**
  ```csharp
  // 1) хелпер в WorldChunkStore.cs (после GetChunkFilePath, :218) — 6 строк
  /// <summary>Каталог мира: СИД + origin региона (иначе дельты колоний одного сида смешиваются).</summary>
  public static string DefaultSaveDirectory(string userRoot, ulong seed, long originX, long originY)
      => Path.Combine(userRoot, string.Concat(
          "world_", seed.ToString("x16"),
          "_x", originX.ToString(CultureInfo.InvariantCulture),
          "_y", originY.ToString(CultureInfo.InvariantCulture)));
  ```
  ```csharp
  // 2) Main.cs:345-346 — БЫЛО
  string baseCacheDir = ProjectSettings.GlobalizePath("user://infinite_world_lab_cache");
  string worldSaveDir = System.IO.Path.Combine(baseCacheDir, $"world_{seed:x16}");
  // СТАЛО
  string baseCacheDir = ProjectSettings.GlobalizePath("user://infinite_world_lab_cache");
  string worldSaveDir = WorldChunkStore.DefaultSaveDirectory(baseCacheDir, seed,
      UnifiedWorldGenerator.WorldOriginX, UnifiedWorldGenerator.WorldOriginY);
  ```
  ```csharp
  // 3) StreamingWorldView.cs:144-149 (и точно так же :257-262) — БЫЛО
  string userDir = ProjectSettings.GlobalizePath("user://infinite_world_lab_cache");
  dir = System.IO.Path.Combine(userDir, $"world_{WorldSeed:x16}");
  // СТАЛО
  string userDir = ProjectSettings.GlobalizePath("user://infinite_world_lab_cache");
  dir = WorldChunkStore.DefaultSaveDirectory(userDir, WorldSeed,
      UnifiedWorldGenerator.WorldOriginX, UnifiedWorldGenerator.WorldOriginY);
  ```
- **Что НЕ трогаем:** формат чанка/манифеста (`WorldChunkCodec`, `WorldSaveMetadata`, `WorldManifestStore` — вариант «origin в метаданные» отвергнут в пользу пути, см. вопрос владельцу), `EnsureCompatible`, `WorldChunkStreamer` (`:496-527`), `ResetWorld`-логику выбора каталога кроме двух строк выше, `FlushResidentDeltas` (это W2-06).
- **Риск:** (а) уже существующие каталоги `world_{seed}` станут недоступны — новая колония начнётся «с чистого мира»; старые файлы не удаляются, но и не читаются (см. вопрос владельцу). (б) `StreamingWorldView._Ready` выполняется ДО выбора спавна: `WorldOriginX/Y` там ещё могут быть `(0,0)` → создастся каталог `…_x0_y0`; он переоткрывается в `ResetWorld` после `SetWorldOrigin` (`Main.cs:568` → `:569`), поэтому «пустой» каталог может остаться осиротевшим (в C2-13 он не убирается — уборщик чистит только файлы, не каталоги); (в) при смене региона в рамках одного сида `ResetWorld` пересоздаёт `_store` уже с новым origin — flush при этом идёт ДО (`:186`), в каталог старого origin, как и надо.
- **Проверка:** две колонии одного сида в разных регионах: поставить стену в первой, выбрать второй регион — во втором стены нет; на диске два каталога `world_<seed>_x<ox>_y<oy>`. Стенд: `tests/TestRegionSwitchKeep.cs` (создаёт `WorldChunkStore`/`StreamingWorldView` на заданном каталоге) — добавить кейс: два вида на `DefaultSaveDirectory` с разными origin, файлы не пересекаются.
- **Объём:** M (~22 строки в 2 файлах + 1 хелпер).
- **Перестройка:** **ТРЕБУЕТ РЕШЕНИЯ ВЛАДЕЛЬЦА.**
  **Вопрос:** что делать со старыми каталогами `world_<seed>` (в них уже лежат дельты **разных** регионов одного сида — это и есть баг)?
  - **Вариант А (рекомендую для dev):** оставить осиротевшими — новая раскладка, старые файлы не читаются и не удаляются. Плюс: 0 риска смешивания, минимум кода. Минус: «миры» из прошлых сессий не открываются (для разработки приемлемо).
  - **Вариант Б:** при первом открытии `Directory.Move("world_<seed>", "world_<seed>_x<first origin>")` — сохраняет старые данные, но выдаёт одной колонии смешанные дельты прошлых регионов (переносит баг в другой вид). Плюс: данные не теряются формально. Минус: воспроизводит исходный дефект, нужен флаг «миграция выполнена».
  - **Вариант В:** путь не менять, а origin класть в `WorldSaveMetadata` + `EnsureCompatible` и отбраковывать несовпадающие дельты. Плюс: старые каталоги остаются валидными, смешивание ловится на чтении. Минус: смена формата чанка/манифеста (L), миграция, риск для уже сохранённых миров, и всё равно нужен per-origin адрес файла, чтобы колонии не затирали друг друга при записи.

---

### C2-12 · P2 · `WorldManifest.StartTileX/Y`: писать origin региона, а не `(0,0)`

- **Файлы:** `scenes/main/Main.cs:340-351` (`InitStreamingWorld`); `src/core/world/streaming/integration/StreamingWorldManager.cs:32-53` (`Open`/`Checkpoint`/`StartTileX`); `src/core/world/streaming/storage/WorldManifestStore.cs:9-15,142-164`.
- **Механизм (доказательство):** `Main.cs:350` — `_worldManager.Open(0, 0);` — литеральные нули; `StreamingWorldManager.Open` кладёт их в поля (`:37-38`) и потом пишет в манифест (`Checkpoint` → `PersistCheckpoint(gameTime, _startTileX, _startTileY)`, `:44-49`). Больше `StartTileX/Y` нигде не читаются (`grep` по `src/`: только объявления/сериализация в `WorldManifestStore`), т.е. документированный «стартовый глобальный tile» всегда `(0,0)`.
- **Минимальная правка (было → стало):**
  ```csharp
  // Main.cs:350 — БЫЛО
  _worldManager.Open(0, 0);
  // СТАЛО: origin региона уже установлен вызывающим (OnRegionSelected:496 / OnSeedRegenerated:402)
  long originX = UnifiedWorldGenerator.WorldOriginX;
  long originY = UnifiedWorldGenerator.WorldOriginY;
  ResumeState resume = _worldManager.Open(originX, originY);
  // Миграция старых манифестов (у них StartTile = 0,0): переписать один раз,
  // иначе для региона (0,0) значение «легально нулевое» и мы не отличим его от старого.
  if (!resume.IsNewWorld && resume.StartTileX == 0 && resume.StartTileY == 0
      && (originX != 0 || originY != 0))
  {
      _worldManager.Coordinator.PersistCheckpoint(resume.GameTime, originX, originY);
  }
  ```
- **Что НЕ трогаем:** формат манифеста (`HeaderLength = 50`, CRC — `WorldManifestStore.cs:22-29`), `WorldSaveCoordinator.OpenOrCreate`/`EnsureCompatible` (C2-11 — отдельно), `StreamingWorldManager` (API уже готов: `Open(long,long)`), `Checkpoint` в `_ExitTree`.
- **Риск:** `PersistCheckpoint` требует `_isOpened` (`WorldSaveCoordinator.cs:141-142`) — вызов идёт сразу после `Open`, флаг уже `true`. `resume.GameTime` провалидирован ≥0 при чтении (`WorldManifestStore.cs:195-196`). Если данные `StartTile` где-то всё-таки используются в будущем — значение станет осмысленным; сейчас чтений нет, регресса нет.
- **Проверка:** `grep -n "Open(0, 0)" src/ scenes/` → 0 (в игре; в `tests/TestWorldStoreExit.cs:143` `manager.Open(0, 0)` — тест, не трогаем). Стенд: `new WorldSaveCoordinator(dir, seed, ver, schema)` → `OpenOrCreate(1024, 512)` → файл `world.manifest` → `TryLoad` → `StartTileX == 1024`, `StartTileY == 512`. После правки в игре: выбрать регион ≠ (0,0), выйти, `WorldManifestStore.TryLoad` показывает его origin.
- **Объём:** S (~7 строк; без миграционной ветки — 2 строки).
- **Перестройка:** нет.

---

### C2-13 · P2 · Уборка осиротевших `*.tmp` и томбстонов `*.deleted` в каталоге чанк-стора

- **Файлы:** `src/core/world/streaming/storage/WorldChunkStore.cs:16-24` (поля/ctor), `:220-260` (`DeleteChunkFileAtomically` — создаёт `*.deleted`), `:76,99` (`*.tmp` от `SaveChunk`), `:262-281` (`TryDeleteFile`).
- **Механизм (доказательство):** томбстоны появляются на каждом удалении дельты: `:241` `$"{targetPath}.{Guid:N}.deleted"`, удаляются в `:258` best-effort. Temp — на каждой записи: `:76` `$"{targetPath}.{Guid:N}.tmp"`, удаляется в `finally` (`:102-105`). При сбое питания/`kill` в окне между `Move` и `TryDelete`, или при `IOException` в `TryDeleteFile` (`:269-280` глотает), файл остаётся **навсегда**: комментарий `:256-257` обещает «может быть удалён позднее обслуживающим кодом», но во всём `src/` нет ни одного `EnumerateFiles`/`GetFiles` по каталогу сохранений (`grep` пуст). Имена у обоих расширений уникальные (`Guid`), т.е. мусор не перезаписывается, а копится; чанк-стор их не сканирует (шаблон `chunk_X_Y.bin`).
- **Минимальная правка (было → стало):**
  ```csharp
  // WorldChunkStore.cs: добавить константу рядом с MaxStoredFileBytes (:14)
  private const int OrphanMinAgeMinutes = 60;

  // Новый public-метод (после DeleteChunk/GetChunkFilePath, ~:218)
  /// <summary>
  /// Убрать мусор прошлых сбоев: осиротевшие *.tmp и томбстоны *.deleted.
  /// Удаляются ТОЛЬКО файлы старше OrphanMinAgeMinutes: у view и координатора РАЗНЫЕ
  /// экземпляры стора на одном каталоге, и общий _ioGate их не сериализует — свежий
  /// temp живого писателя трогать нельзя.
  /// </summary>
  public int SweepOrphanFiles()
  {
      int removed = 0;
      lock (_ioGate)
      {
          DateTime cutoff = DateTime.UtcNow.AddMinutes(-OrphanMinAgeMinutes);
          foreach (string pattern in new[] { "*.deleted", "*.tmp" })
          {
              string[] files;
              try { files = Directory.GetFiles(_saveDirectory, pattern); }
              catch (IOException) { continue; }
              catch (UnauthorizedAccessException) { continue; }
              catch (DirectoryNotFoundException) { continue; }
              foreach (string file in files)
              {
                  try
                  {
                      if (File.GetLastWriteTimeUtc(file) >= cutoff) continue;
                      File.Delete(file);
                      removed++;
                  }
                  catch (IOException) { }
                  catch (UnauthorizedAccessException) { }
                  catch (SecurityException) { }
              }
          }
      }
      return removed;
  }

  // Ctor (:18-24) — вызвать один раз после присвоения _saveDirectory
  _saveDirectory = Path.GetFullPath(saveDirectory);
  SweepOrphanFiles();          // ← добавить; каталог может ещё не существовать — метод это переживает
  ```
- **Что НЕ трогаем:** `SaveChunk`/`TryLoadChunk`/`DeleteChunk`/`DeleteChunkFileAtomically`/`TryDeleteFile` (их поведение не меняется), `WorldManifestStore` (его `*.tmp` попадут под тот же шаблон и будут убраны — это желаемое), `manifest_backup_*.json` (не мусор сбоя, а бэкап `WorldSaveCoordinator.BackupManifest` — **не удаляем**), каталоги-сироты (см. риск C2-11), `.corrupt-*` из N-02 (улики).
- **Риск:** (а) гонка с живым писателем, если файл моложе порога — защищены возрастом; (б) часы системы уехали вперёд/назад → либо не убираем, либо уберём раньше времени: потеря временного файла безопасна (это не чанк), томбстон — уже удалённая дельта, повторная уборка безвредна; (в) `Directory.GetFiles` на каталоге с тысячами чанков — один проход, только по двум шаблонам, вызывается на создании стора (2 раза за смену региона); (г) `WorldChunkStore` документирован как «не обращается к Godot API» (`:9`) — поэтому метод ничего не логирует, возвращает счётчик (при желании вызывающий лог).
- **Проверка:** стенд: создать в temp-каталоге `chunk_0_0.bin.deadbeef.tmp` и `chunk_0_0.bin.deadbeef.deleted` с `LastWriteTimeUtc = UtcNow-2h`, плюс свежий `*.tmp`; `new WorldChunkStore(dir)` → после конструктора старых файлов нет, свежий на месте, `SweepOrphanFiles() == 0`. Рантайм/ручной: после серии аварийных выходов — `grep`-подобная проверка каталога `user://infinite_world_lab_cache/world_*`: файлов `*.tmp`/`*.deleted` старше часа нет.
- **Объём:** M (~35 строк, из них 20 — обвязка try/catch).
- **Перестройка:** нет.

---

### W2-06 · P1 · Выходной flush не должен УДАЛЯТЬ дельту, которую стример не читал

- **Файлы:** `src/ui/streaming/StreamingWorldView.cs:640-667` (`FlushResidentDeltas`), `:709-752` (`ApplyReady`), `:56` (поля), `:238-241` (`ResetWorld`); контекст — `src/core/world/streaming/runtime/WorldChunkStreamer.cs:496-527` (файл не читает и **не перезаписывает** при несовпадении seed/gen/schema), `src/core/world/streaming/storage/WorldChunkStore.cs:63-70` (пустой payload = удаление чанка).
- **Механизм (доказательство):**
  ```csharp
  // StreamingWorldView.cs:653-657 — текущий flush
  byte[] payload = EncodeBlocks(pair.Key);
  if (payload.Length == 0 && !_store.ContainsChunk(pair.Key.X, pair.Key.Y))
      continue;                                    // (а) файла нет — пропуск, ок
  if (_store.SaveChunk(metadata, pair.Key.X, pair.Key.Y, payload))  // (б) файл ЕСТЬ, payload пуст
      saved++;                                     //     ⇒ SaveChunk удалит чанк (tombstone)
  ```
  Если стример отказался читать файл (`:504-526` — несовместимая/битая дельта), `_blocks` для чанка пуст ⇒ `EncodeBlocks` вернёт пусто ⇒ ветка (б) выполняется и **стирает** сохранённый мир. Достаточно поднять `GeneratorVersion` или `FeatureSchemaVersion` (`:506-508`) и зайти в посещённые чанки.
- **Минимальная правка (было → стало):**
  ```csharp
  // 1) поле рядом с _store (:56)
  private readonly HashSet<ChunkKey> _loadedFromStore = new();

  // 2) ApplyReady (:714-747) — отметить чанк, чья база ПРИШЛА С ДИСКА (после успешного TryDecode)
  if (WorldChunkDeltaCodec.TryDecode(result.Snapshot.Payload, out WorldChunkDelta delta, out string error))
  {
      …существующая сборка map/счётчиков…
      _loadedFromStore.Add(key);      // ← добавить в конце успешной ветки (перед else на :748)
  }

  // 3) ResetWorld (:239) — сброс вместе с блокировками
  _blocks.Clear();
  _loadedFromStore.Clear();           // ← добавить

  // 4) FlushResidentDeltas:653-657 — БЫЛО
  if (payload.Length == 0 && !_store.ContainsChunk(pair.Key.X, pair.Key.Y))
      continue;
  // СТАЛО
  if (payload.Length == 0)
  {
      if (!_store.ContainsChunk(pair.Key.X, pair.Key.Y))
          continue;                                   // файла нет — удалять нечего
      if (!_loadedFromStore.Contains(pair.Key))
      {
          GD.PrintErr($"[World] FlushResidentDeltas {pair.Key}: файл на диске не прочитан " +
                      $"(несовместим/битый, см. [delta] в логе) — оставлен как есть");
          continue;                                   // ← главное: не перезаписываем пустотой
      }
  }
  ```
- **Что НЕ трогаем:** `WorldChunkStore.SaveChunk`/`DeleteChunk` («пустой payload = удалить» — верное поведение для *прочитанного* чанка), `WorldChunkStreamer` (он намеренно не перезаписывает чужое — `:512-526`), `EncodeBlocks`, `ResetWorld`-очистку остальных структур, `StreamingWorldView.WorldSeed` (W2-01 — другая карточка, здесь не правим).
- **Риск:** (а) чанк, созданный в этой сессии без файла, а затем получивший файл от другой сессии за время игры, теперь не будет удалён — оставит устаревшую дельту; это безопаснее потери мира и лечится повторным заходом (файл будет прочитан и `_loadedFromStore` отметит ключ); (б) `_loadedFromStore` растёт только на resident-окно (≤256 чанков, `MaxBufferedChunks`) и чистится в `ResetWorld`; (в) `ApplyReady` при повторной доставке снапшота — ключ уже в множестве, повторное добавление идемпотентно.
- **Проверка:** стенд (по образцу `tests/TestWorldStoreExit.cs:123-176`): собрать вид на temp-каталоге, вручную положить `chunk_0_0.bin` с метаданными «GenVersion+1» через `new WorldChunkStore(dir).SaveChunk(...)`, загрузить окно, вызвать `FlushResidentDeltas(0)`; ожидание: файл **на диске** → `ContainsChunk(0,0) == true` и в логе «оставлен как есть». Контрольный кейс: чанк без файла (payload пуст) → файла нет; чанк, прочитанный с диска, затем очищенный в игре → файла нет (удаление работает).
- **Объём:** S (~11 строк в 1 файле).
- **Перестройка:** нет.

---

### W2-07 · P2 · `_ExitTree`: остановить сим-поток ДО снятия состояния колонии

- **Файлы:** `scenes/main/Main.cs:798-875` (порядок в `_ExitTree`): снапшот `:836-843`, сохранение дельт `:844-858`, зоны `:859-871`, `_agentThread?.Stop()` на `:874`.
- **Механизм (доказательство):** `IslandStateSnapshot.RefreshPayloads` (`:838`) и `ZonePersistence.SaveZones` (`:862`) читают менеджеры на главном потоке, пока сим-поток (`AgentSimulationThread`) в них пишет: остановка идёт последней строкой (`:874`). Перестановки/снапшоты, снятые в промежуток, могут быть рваными (половина домена «до», половина «после»), а сами менеджеры — под локом, но последовательность снимков разных доменов неатомарна.
- **Минимальная правка (было → стало): перенести одну строку выше чекпоинта**
  ```csharp
  // :808-812 — БЫЛО
  _mapOverlay?.QueueFree();
  float worldTimeOnExit = _timeManager?.GameTimeSeconds ?? 0f;    // дальше Checkpoint/Flush → снапшот → зоны → Stop
  // СТАЛО
  _mapOverlay?.QueueFree();
  // W2-07: сим-поток обязан встать ДО снятия состояния/зон и до exit-flush — иначе
  // менеджеры читаются, пока в них пишет симуляция (рваные payload'ы).
  _agentThread?.Stop();
  float worldTimeOnExit = _timeManager?.GameTimeSeconds ?? 0f;
  ```
  Строку `:874` (`_agentThread?.Stop();`) — удалить (иначе двойной `Stop`; повторный вызов безвреден, но лишний).
- **Что НЕ трогаем:** `_islandMirror?.Detach()` (`:872`) и `_worldView?.Shutdown()` (`:873`) остаются на своих местах — `Shutdown` обязан идти ПОСЛЕ `FlushResidentDeltas` (`:823`), а `Detach` безвреден в конце; `_worldManager?.Checkpoint` (`:815`) и exit-flush — только выигрывают от того, что сим-поток уже стоит; порядок «островные дельты → зоны» внутри блока `:831-871` не меняем.
- **Риск:** `Stop()` делался и раньше в этом же методе — блокирующий вызов с таймаутом (`_agentThread.Stop(3000)` в других местах), поэтому выход не станет дольше, только раньше. Если `Stop` вернёт `false` (поток не встал за таймаут), мы продолжим сохранение как сегодня — регресса нет; для чистоты можно логировать результат (`if (!_agentThread.Stop(3000)) GD.PrintErr(...)`) — это уже строчка сверх минимума.
- **Проверка:** статически — `_agentThread?.Stop()` находится **выше** `:831` (`if (_islandDeltas != null …)`), и в методе не осталось второго `Stop`. Стенд: `tests/TestWorldStoreExit.cs` (exit-путь) + ручной сценарий: выход во время работы агентов (стройка/рубка) — в сохранённом `.island.delta` payload'ы не противоречат друг другу (нет `CompletedCount > TargetCount`, нет `null` у активных чертежей), в логе нет warning'ов из `IslandStateSnapshot`.
- **Объём:** S (перенос 1 строки).
- **Перестройка:** нет.

---

## Волны применения

**Правило нулевого шага:** перед каждой волной — `dotnet build Game.csproj` (эталон: 0 ошибок / 0 предупреждений). Тесты `tests/` исключены из сборки (`Game.csproj:25`) — стенды гоняются отдельным headless-раннером; если он недоступен, волна закрывается статической проверкой + сборкой, а стенд отмечается как «долг».

### Волна 1 — независимые файлы, можно параллельно (4 исполнителя, конфликтов записи нет)

| Поток | Карточки | Файл | Зависимости |
|---|---|---|---|
| **1A** | **N-01 + N-06** (закрывают **C2-02**) | `src/core/world/mutation/IslandOverrideStore.cs` | нет. Делать одним исполнителем: обе правки в одном файле, `Load` затрагивают обе. Порядок внутри потока: N-01 (Save + подхват `.tmp`) → N-06 (проверки чтения) |
| **1B** | **N-02 + W2-07** | `scenes/main/Main.cs` | нет. Оба — точечные правки в разных методах (`PrepareIslandStore` catch и `_ExitTree`); один владелец файла |
| **1C** | **C2-03** | `src/core/world/mutation/IslandStateSnapshot.cs` | нет (перестановка внутри одного метода) |
| **1D** | **C2-13** | `src/core/world/streaming/storage/WorldChunkStore.cs` | нет (новый метод + вызов в ctor). **Не пересекается** с C2-11: тот добавит туда только `public static`-хелпер — если обе идут параллельно, хелпер и `SweepOrphanFiles` пишутся в разные места файла, но лучше разнести по волнам (см. ниже) |

**Почему N-01 первым:** он снимает главный риск потери прогрессии; N-02/N-06 без него полезны, но дают неполный эффект (карантин без атомарности, строгая проверка без карантина).

### Волна 2 — `Main.cs` последовательно после 1B (тот же файл, другой владелец не нужен)

| Поток | Карточки | Файл | Зависимости |
|---|---|---|---|
| **2A** | **W2-06** | `src/ui/streaming/StreamingWorldView.cs` | независим от Main; можно параллельно с 2B |
| **2B** | **C2-12** → **C2-01** | `scenes/main/Main.cs` | после 1B (файл). Порядок: C2-12 (2 строки в `InitStreamingWorld`) → C2-01 (поле + блок в `PrepareIslandStore` + `_ExitTree`) |

### Волна 3 — C2-11 (последней, после решения владельца)

| Поток | Карточки | Файлы | Зависимости |
|---|---|---|---|
| **3A** | **C2-11** | `scenes/main/Main.cs:344-350`, `src/ui/streaming/StreamingWorldView.cs:144-149,257-262`, `WorldChunkStore.cs` (хелпер) | (а) **решение владельца** по старым каталогам (вопрос в карточке); (б) после C2-12 и C2-01 в `Main.cs`; (в) после W2-06 в `StreamingWorldView.cs` (одни и те же строки вокруг `_store`); (г) если параллельно применяется **W2-01** (другая карточка §17: `WorldSeed` во flush) — сначала W2-01, иначе каталог и метаданные разъедутся в двух местах |

### Дерево зависимостей (кратко)

```text
N-01 ──► C2-02 ◄── N-02          (C2-02 = сумма двух)
N-01 (подхват .tmp в Load) ──► N-06 (проверки в том же Load)   [один исполнитель, порядок внутри]
N-02 ──► (карантин для битых файлов, которые ловит N-06)       [логическая, не кодовая]
C2-03  независимо
C2-13  независимо  ──► C2-11 (меняется каталог мира; уборщик от этого не зависит)
W2-07  независимо  ──► (уменьшает вероятность рваных payload'ов для C2-03)
W2-06  независимо  ──► C2-11 (общий файл StreamingWorldView)
C2-01  независимо  ──► C2-11 (общий Main.cs)
C2-12  независимо  ──► C2-11 (общий Main.cs + тот же origin)
```

**Можно параллельно:** {N-01+N-06} ∥ {N-02+W2-07} ∥ {C2-03} ∥ {C2-13} ∥ {W2-06} — пять непересекающихся файлов (проверено: `IslandOverrideStore.cs`, `Main.cs`, `IslandStateSnapshot.cs`, `WorldChunkStore.cs`, `StreamingWorldView.cs`).
**Нельзя параллельно:** всё, что пишет `Main.cs` (потоки 1B → 2B → 3A) и всё, что пишет `StreamingWorldView.cs` (2A → 3A).

### Что даёт волна целиком

- **Потеря островной прогрессии при сбое** (N-01/N-02/C2-02/N-06 + W2-07): атомарная запись с fsync, карантин битого файла, строгая проверка чтения, состояние снимается на остановленной симуляции — прогрессия острова перестаёт зависеть от момента выхода и от целостности одного файла.
- **Потеря зон при смене региона** (C2-01): `.zones.json` пишется в той же точке, что и дельты.
- **Потеря предметов на земле** (C2-03): исключение в середине снапшота больше не обнуляет метки `GroundItems`.
- **Стирание мира при подъёме версии генератора** (W2-06): несовместимая дельта остаётся на диске, а не удаляется пустым payload'ом.
- **Смешивание колоний одного сида** (C2-11) и **origin региона в манифесте** (C2-12).
- **Мусор на диске** (C2-13): томбстоны/temp убираются, каталог не растёт от аварийных выходов.