# FixPlan 04 — стриминг, генерация, перф почвы (`_plan/fixplans/04_streaming.md`)

> Волна 2026-11-08 (снимок дерева 22:30 МСК). Формат — `_plan/fixplans/README.md`. Код **read-only**, git/Godot/сборка не запускались; сборка на входе зелёная (0/0 — данные Лида).
> **Дерево движется параллельно (у 6 файлов зоны mtime 6–8 ноября).** Все строки ниже сверены по снимку; в задаче искать по цитате-фрагменту, а не только по номеру.
> Источники: `_reports/hunt311/worldgen.md` (W2-01…W2-05), `_plan/BUGS_REGISTRY.md` §13–§18 (G-01/N2-01, G-02, C-10…C-15).

## 0. Что изменилось в дереве с момента карточек (важно для планирования)

| Карточка реестра | Статус на снимке | Доказательство |
|---|---|---|
| **C-10** (64-битный `% 100000` в `FertilityMap`) | **УЖЕ ЗАКРЫТ (правка применена)** | `src/core/world/FertilityMap.cs:235-237` — `(((x * 83492791) ^ (int)((uint)(y * 83492791) >>> 16)) & 7) == 0`: литерала `2971215073` и `% 100000` больше нет; вокруг появились `stepMultiplier`/`diffShift` (catch-up) |
| **G-02** (гидрология планеты vs локальные реки) | **Похоже, ОБНУЛЁН сменой генератора** | `PlanetaryHydrology.SimulateGlobalHydrology` (`src/core/world/climate/PlanetaryHydrology.cs:39`) **не имеет ни одного вызова** в `src/` и `scenes/`; и карта (`WorldMapOverlay.cs:833,853` → `PureWorldMapGenerator.GeneratePreview/GenerateFullMap`), и мир (`UnifiedWorldGenerator.cs:103` → `PureWorldGenerator.GenerateWorldChunk`) идут в **один** `PureWorldGenerator`, где реки — `DomainRiverMeander` (`PureWorldGenerator.cs:28,109,181-200`) |
| **C-11, C-12** (233 472 деления/60 000 шумов на чанк) | **Неактуальны как написаны**: путь чанка заменён | `UnifiedWorldGenerator.cs:103` — `return PureWorldGenerator.GenerateWorldChunk(...)`; `:171` — `return PureWorldGenerator.SampleCell(...)`. Старые оценки относились к прежнему телу `GenerateChunk` |
| **G-01 / N2-01** (спавн без проверки обитаемости) | **Частично закрыт**: гейт в оверлее уже есть и валидирует тем же генератором | `src/ui/streaming/WorldMapOverlay.cs:381-417` — `isOcean` из `_tileData` (`:382-386`) **+** `PureWorldMapGenerator.SampleRegion(_seed, cellX, cellY).IsLand` (`:389-403`) + сообщение и `HUDController.ShowFloatingWarning` (`:408-414`), до `OnRegionSelected?.Invoke` (`:423`). Остаток — см. карточку G-01 ниже |
| **R2-01** (z-слои мира) | **Закрыт в дереве**, в план не входит | `src/ui/streaming/StreamingWorldView.cs:1494-1505` — `ZAsRelative = false; ZIndex = zIndex` |
| **W2-01, W2-02, W2-03, W2-04, W2-05, C-13, C-14** | **Действуют** (якоря перепроверены по снимку) | см. карточки |

⇒ **Волна «C-10 первым» отменяется: C-10 уже в коде.** Первой идёт связка W2-01 + W2-02 в `Main.cs` (одна правка = один файл).

---

### W2-01 · P1 · Сид меняется до `ResetWorld` ⇒ flush дельт старого мира пишется с метаданными нового

- **Файлы:** `scenes/main/Main.cs:393-394` (присваивание), `:399-400` (ранний `return`), `:478` (`ResetWorld(newSeed)`); `src/ui/streaming/StreamingWorldView.cs:186` (`FlushResidentDeltas`), `:195` (`if (newSeed != 0) WorldSeed = newSeed`), `:642-666` (flush), `:1001-1003` (`MakeMetadata` берёт `WorldSeed`), `:257-267` (новый `_store` по новому сиду).
- **Минимальная правка:** единственный владелец смены сида — `ResetWorld`.
  ```csharp
  // scenes/main/Main.cs:393-394 — УДАЛИТЬ:
  -           if (_worldView != null)
  -               _worldView.WorldSeed = newSeed;

  // scenes/main/Main.cs:399-400 — ранний выход обязан сменить мир и у стримера:
  -           if (_mapOverlay != null && _mapOverlay.IsSpawnSelectionMode)
  -               return;
  +           if (_mapOverlay != null && _mapOverlay.IsSpawnSelectionMode)
  +           {
  +               // Иначе identity расщеплена: стример держит readonly _worldSeed старого
  +               // мира, а вид уже считает себя новым (flush пишет метаданные нового сида).
  +               _worldView?.ResetWorld(newSeed);
  +               return;
  +           }
  ```
  Между `:394` и `:478` поле `_worldView.WorldSeed` не читает никто (там только `UnifiedWorldGenerator.ActiveWorldSeed`, `WorldModel.Configure`, `FindHabitableSpawn(WorldSeed,…)` и локальный `seed` в лямбде).
- **Что НЕ трогаем:** `PrepareIslandStore`/островные дельты (`Main.cs:124-159` — там порядок правильный), `WorldChunkStreamer` (политика «файл не перезаписывать» верна), формат метаданных.
- **Риск:** низкий. Побочно `ResetWorld` в режиме выбора спавна пересоздаёт `WorldChunkStore` (flush + новый объект, без чтения диска) — на клик reroll допустимо. Откат — вернуть 2 строки.
- **Проверка:** `tests/TestRegionSwitchKeep.cs` (reroll → заголовок `world_{old}/chunk_*.bin` содержит СТАРЫЙ seed); вручную: стена за кромкой → reroll → дельта старого мира читается без `[delta] delta несовместима`.
- **Объём:** **S** (≤10 строк, один файл). **Перестройка:** нет.

### W2-02 · P1 · Зум не пересчитывает окно чанков (+ страховка от `Math.Abs(long.MinValue)`)

- **Файлы:** `scenes/main/Main.cs:789-810` (гейт), `:65-66` (поля `_lastStreamerTile*`), `:475`, `:566` (сбросы); `src/ui/CameraController.cs:224-228` (зум меняется независимо от позиции); `src/ui/streaming/StreamingWorldView.cs:878-894` (окно адаптивное от видимого прямоугольника).
- **Минимальная правка:**
  ```csharp
  // scenes/main/Main.cs:65-66 — новое поле рядом с _lastStreamerTileX/Y:
  +   private float _lastStreamerZoom = -1f;

  // scenes/main/Main.cs:789-810:
  +           float camZoom = _camera.Zoom.X;
  +           bool zoomChanged = Mathf.Abs(camZoom - _lastStreamerZoom) >= 0.01f;
  +           // long.MinValue ⇒ первый апдейт обязателен; Math.Abs(long.MinValue) кидает OverflowException
  +           bool camMoved = _lastStreamerTileX == long.MinValue
  +               || Math.Abs(camTileX - _lastStreamerTileX) >= 8
  +               || Math.Abs(camTileY - _lastStreamerTileY) >= 8;
  -           if ((Math.Abs(camTileX - _lastStreamerTileX) >= 8 || Math.Abs(camTileY - _lastStreamerTileY) >= 8)
  -               && nowMsec - _lastStreamerUpdateMsec >= StreamingUpdateMinIntervalMs)
  +           if ((zoomChanged || camMoved)
  +               && nowMsec - _lastStreamerUpdateMsec >= StreamingUpdateMinIntervalMs)
            {
                _lastStreamerUpdateMsec = nowMsec;
  +             _lastStreamerZoom = camZoom;
  ```
  Плюс `_lastStreamerZoom = -1f;` в двух точках сброса (`Main.cs:475`, `:566`, рядом с `_lastStreamerTileX = long.MinValue`).
- **Что НЕ трогаем:** `ChunkWindowPlanner`/`ApplyAdaptiveWindow` (клампы 3…16 чанков верны), интервал 120 мс (троттлинг оставляем).
- **Риск:** низкий; окно пересобирается при пересечении порога зума 1 % (редко), сброс `-1f` даёт гарантированный апдейт после смены региона. Откат — вернуть условие.
- **Проверка:** стенд гейта: одинаковый `Position`, `Zoom` 0.5 → 0.12 ⇒ `Update` вызван, `LoadedChunks` вырос; в игре — отдалиться, стоя на месте (пояс декора/гор/руд появляется без панорамирования).
- **Объём:** **S** (≤12 строк, один файл). **Перестройка:** нет.

### W2-03 · P1 · `_oreArrays` растёт без вытеснения (5 записей на чанк, удаляется только центр)

- **Файлы:** `src/ui/streaming/StreamingWorldView.cs:2103-2107` (пять вставок: центр + `N/S/E/W`), `:872` (`RemoveChunkTiles` — удаляет только `key`), `:215` + `:2426` (`_oreArrays.Clear()` только в `ResetWorld`/`Shutdown`); цена записи — `src/core/world/generation/OreWorldField.cs:99-105` (12 288 Б на `ChunkOre`).
- **Минимальная правка:** очередь вставок + потолок, вытеснять только нерезидентные ключи.
  ```csharp
  // поля рядом с _oreArrays (StreamingWorldView.cs:73):
  +   private const int OreArrayCap = 1536;            // 1536 × 12 КБ ≈ 18 МБ
  +   private readonly Queue<ChunkKey> _oreArrayOrder = new();

  // ProcessOreJobs, сразу после пяти вставок (после :2107):
  +   _oreArrayOrder.Enqueue(key);
  +   PruneOreArrays();

  +   private void PruneOreArrays()
  +   {
  +       int guard = _oreArrayOrder.Count;            // дубликаты (re-load) не зацикливают
  +       while (_oreArrays.Count > OreArrayCap && _oreArrayOrder.Count > 0 && guard-- > 0)
  +       {
  +           ChunkKey old = _oreArrayOrder.Dequeue();
  +           if (_chunkData.ContainsKey(old)) continue;   // резидентный нужен соседям
  +           _oreArrays.Remove(old);
  +       }
  +   }

  // ResetWorld (:215) и Shutdown рядом с _oreArrays.Clear():
  +   _oreArrayOrder.Clear();
  ```
  Потолок 1536 выбран так, что при максимальном окне (`MaxWindowChunks = 16` ⇒ 256 чанков ⇒ ≤1280 нужных записей «центр + 4 соседа») вытесняются только ключи вне окна.
- **Что НЕ трогаем:** `OreWorldField.Cache` (эвикция — W2-04), `PaintOreEdge`/`EnsureOrePainted`, `OreWorldState`.
- **Риск:** средний-низкий: вытесненный массив соседа нужен резиденту только при *последующей* перекраске края (добыча руды) — один тайл может получить маску без соседа до перезахода чанка в окно; с потолком 1536 это возможно лишь при окне у максимума. Если неприемлемо — вариант Б (счётчики ссылок на 4 соседа), это уже M+.
- **Проверка:** новый стенд `tests/TestStreamingOreCache.cs`: 200 итераций «сдвиг окна на 1 чанк + дренаж покраски» ⇒ `_oreArrays.Count ≤ 1536` и `≈ 5·_chunkData.Count` (сейчас растёт линейно с пройденным путём).
- **Объём:** **M** (~25 строк, один файл). **Перестройка:** нет.

### W2-04 · P2 · Эвикция ore-кэша: полный `Cache.Clear()` залпом, счётчик не синхронизирован с `Cache.Count`

- **Файлы:** `src/core/world/generation/OreWorldField.cs:232-256` (`EnsureChunk`), `:39-43` (`CacheCap = 2048`, `_cached`), `:54-89` (`CacheWorldGate`).
- **Минимальная правка:**
  ```csharp
  // OreWorldField.cs:246-252 — было:
  -   if (System.Threading.Interlocked.Increment(ref _cached) > CacheCap)
  -   {
  -       Cache.Clear();
  -       System.Threading.Interlocked.Exchange(ref _cached, 0);
  -   }
  // стало:
  +   if (System.Threading.Interlocked.Increment(ref _cached) > CacheCap)
  +   {
  +       lock (CacheWorldGate)                 // один очищающий, без «залпа» из 8 воркеров
  +       {
  +           if (Cache.Count > CacheCap)        // повторная проверка под локом
  +           {
  +               Cache.Clear();
  +               System.Threading.Interlocked.Exchange(ref _cached, 0);
  +           }
  +       }
  +   }
  ```
- **Что НЕ трогаем:** `EnsureCacheWorld`/`ClearCache`, `KnownChunks`/`KnownOreBits` (`KnownCap` — другая семантика), `ComputeChunk`.
- **Риск:** низкий: `Cache.Count` под локом — O(n), но только на пороге (>2048); дедлока нет (`CacheWorldGate` не вложен). Cliff «полный сброс» остаётся; выборочный LRU — отдельная L-задача [рантайм].
- **Проверка:** `[рантайм]`: 2049 `EnsureChunk` разными ключами на 8 потоках ⇒ число фактических `Clear()` = 1 (дебаг-счётчик), `Cache.Count ≤ 2048`; пик кадра при панорамировании до/после.
- **Объём:** **S** (≤8 строк). **Перестройка:** нет.

### W2-05 · P2 · `RememberKnown` публикует «чанк известен» до записи битсета руды

- **Файлы:** `src/core/world/generation/OreWorldField.cs:161-193` (писатель), `:200-220` (`TryPeekKnownOreLocal` — читатель гейтов).
- **Минимальная правка:** сначала данные, потом флаг.
  ```csharp
  // OreWorldField.cs:161-193 — порядок меняется:
  +   if (!KnownOreBits.ContainsKey(chunkKey))
  +   {
  +       bool any = false;
  +       var bits = new ulong[ChunkCells / 64];
  +       for (int i = 0; i < ChunkCells; i++)
  +       {
  +           if (chunk.Types[i] == OreConfig.None) continue;
  +           bits[i >> 6] |= 1UL << (i & 63);
  +           any = true;
  +       }
  +       if (any) KnownOreBits.TryAdd(chunkKey, bits);   // 1) данные
  +   }
  +   if (KnownChunks.ContainsKey(chunkKey)) return;       // уже опубликован
  +   KnownChunks[chunkKey] = 1;                           // 2) флаг «известен»
  +   if (System.Threading.Interlocked.Increment(ref _knownCount) > KnownCap)
  +   { /* прежний блок кап-очистки, затем KnownChunks[chunkKey]=1; _knownCount=1; */ }
  ```
  Читатель (`if (!KnownChunks.ContainsKey) return false;` → `KnownOreBits.TryGetValue`) при таком порядке всегда видит битсет, если видит флаг.
- **Что НЕ трогаем:** `TryPeekCachedOreLocal` (эфемерный кэш), `KnownCap`-политику, `OreWorldState`.
- **Риск:** низкий; для «руды нет» поведение не меняется (`any == false` ⇒ битсета нет). Откат — вернуть порядок.
- **Проверка:** новый стенд `tests/TestOreKnownRace.cs`: поток A — `EnsureChunk` по чанкам с рудой, поток B — `TryPeekKnownOreLocal` по жилам; пары `(known == true, ore == false)` недопустимы.
- **Объём:** **S** (~12 строк). **Перестройка:** нет.

### G-01 / N2-01 · P1 (было P0) · Клик по карте мира проверяет ОДИН регион, а колония — 1024² (2×2 региона)

- **Файлы:** `src/ui/streaming/WorldMapOverlay.cs:381-417` (существующий гейт: `isOcean` из `_tileData` + `PureWorldMapGenerator.SampleRegion(_seed, cellX, cellY).IsLand` + сообщение + `ShowFloatingWarning`), `:419-426` (invoke + `CloseMap`); `scenes/main/Main.cs:497` (`SetWorldOrigin(region.X * WorldRegionCells, …)`), `:27` (`ColonySizeTiles = 1024`), `:22` (`WorldRegionCells = 512`); `src/core/world/activearea/ActiveArea.cs:51` (`StartClearRadius = 14`), `:164-172` (`ApplyStartClearance`).
- **Механизм остатка:** гейт валидирует **центр одного региона** — клетку `(cellX, cellY)` карты. Колония же занимает 2×2 региона (origin = `region * 512`, размер 1024), и её центр — `origin + 512`, т.е. центр соседнего региона `(rx+1, ry+1)`. Регион с сухим центром, но водой в соседнем квадранте, проходит гейт; `ApplyStartClearance` затем выжигает круг 14 клеток в `Grass` — «пятачок в море» (симптом G-01) остаётся достижимым. `Main.OnRegionSelected` по-прежнему не валидирует ничего (`:489-497`).
- **Минимальная правка (дешевле всего — расширить существующий гейт, не строить новый):**
  ```csharp
  // WorldMapOverlay.cs:405-417 — перед показом сообщения расширить проверку:
  +   if (!isOcean && _hasSeed && !IsColonyWindowViable(_seed, cellX, cellY))
  +       isOcean = true;   // переиспользуем существующую ветку отказа (сообщение + warning + return)
  ...
  +   /// <summary>Окно колонии 1024² = 2×2 региона: все 4 центра — суша И центр окна — суша.</summary>
  +   private static bool IsColonyWindowViable(ulong seed, long rx, long ry)
  +   {
  +       for (int dy = 0; dy <= 1; dy++)
  +       for (int dx = 0; dx <= 1; dx++)
  +           if (!PureWorldMapGenerator.SampleRegion(seed, rx + dx, ry + dy).IsLand) return false;
  +       // центр окна = (rx*512+512, ry*512+512) — уже центр региона (rx+1, ry+1), проверен выше
  +       return true;
  +   }
  // текст в :410 заменить на «Здесь океан/озеро в окне колонии 1024×1024 — выберите другой регион».
  ```
  Плюс **страховка** (дешёвая, defense-in-depth) в `Main.OnRegionSelected` перед `SetWorldOrigin` (`:497`): та же проверка на игровом сэмплере `WorldCellQuery.SampleLatticeAbsolute` по сетке 5×5 окна — и при отказе только `GD.PrintErr` + `return` (карта к этому моменту уже закрыта оверлеем, поэтому UI-сообщение обязан дать оверлей; «тихий отказ» недопустим — см. GD-класс).
- **Что НЕ трогаем:** `FindHabitableSpawn` реролла (`Main.cs:402` — он ищет по всей планете и уже идёт в `PureWorldMapGenerator.FindBestSpawn`), `ApplyStartClearance` (круг 14 нужен и после проверки — убирает деревья/камни у спавна), генератор.
- **Риск:** средний: порог «все 4 региона — суша» может отклонить пригодный архипелаг; откат — убрать 1 вызов. Второй риск: отказ в оверлее оставляет карту открытой (правильное поведение — существующее), а отказ в `Main` — без UI, поэтому страховка должна молчать только в логах и быть недостижимой при работающем оверлее.
- **Проверка:** `tests/TestWorldMapClick.cs` (клик по региону с водным соседом ⇒ origin не меняется, карта не закрывается); сценарий в игре: клик у воды → отказ с текстом; клик по «хорошему» региону → в логе `[SPAWN]`, пятна 14 тайлов в воде нет.
- **Объём:** **S/M** (≤20 строк в оверлее + опционально ≤25 строк страховки в `Main`). **Дешевле UI-гейта, чем предикат в `Main`:** да — оверлей уже имеет готовую ветку отказа с сообщением, а `Main`-предикат требует UI-механики (карта закрывается сразу после `Invoke`, `:423-424`).
- **Перестройка:** нет.

### C-13 · P1 (перф) · `BuildFertilityGrid` на главном потоке внутри покраски декора

- **Файлы:** `src/ui/streaming/StreamingWorldView.cs:1688-1705` (`BuildFertilityGrid` — 25 × `WorldClimateGenerator.SampleBlendedCell`), `:1671-1686` (`CellSoilFertilityQ16` → вызов), `:1744` (использование из `GetDecorIndex`); фоновый конвейер — `ScheduleOreChunk` (`:2066-2081`), `ProcessOreJobs` (`:2084-2120`), отложенный докрас — `EnqueueDecorRepair`/`ProcessDecorRepair` (`:2123-2186`).
- **Минимальная правка:** считать сетку 5×5 в **существующем** фоновом задании чанка и читать из словаря; `BuildFertilityGrid` оставить фолбэком.
  ```csharp
  // OreArrays (StreamingWorldView.cs:78-86):
  +       public ushort[] Fertility;                    // 25 значений, шаг FertilityGridStep
  // ScheduleOreChunk (:2072-2080) — в том же Task.Run:
  +           Fertility = SampleFertilityGrid(key.X, key.Y, seed, ver),
  // ProcessOreJobs (после :2103):
  +       _fertilityByChunk[key] = bundle.Fertility;
  // CellSoilFertilityQ16 (:1671-1686) — сначала словарь:
  +       if (_fertilityByChunk.TryGetValue(key, out ushort[] grid)) { /* тот же билинейный Lerp по grid[25] */ }
  -       if (!_fertilityValid || _fertilityChunk != key) BuildFertilityGrid(key);   // остаётся фолбэком
  // RemoveChunkTiles (:872-876) и ResetWorld (:215)/Shutdown (:2426):
  +       _fertilityByChunk.Remove(key);   /   _fertilityByChunk.Clear();
  ```
  Работает потому, что декор **уже** отложен до готовности ore-массивов (`OreBaseAtCellCached(…) == null` ⇒ `GetDecorIndex` → −1, и `ProcessDecorRepair` докрашивает позже), т.е. фоновые данные приходят раньше первой покраски декора. Адресация `origin + 16·g` переносится из `BuildFertilityGrid` без изменений.
- **Что НЕ трогаем:** формулу `DecorPass`/пороги `WorldDecor` (паритет остров↔мир), `WorldClimateGenerator`, ор-слой, бюджеты `ProcessPaintSlices`.
- **Риск:** средний: +50 Б/чанк (25 `ushort`, чистится выгрузкой); если для чанка не было ore-джобы (`_oreLayer == null` на стенде) — работает фолбэк (медленно, но корректно); `test_streamingview_decor_seam.cs` обязан остаться зелёным — значения те же, меняется только поток.
- **Проверка:** `tests/TestStreamingPaintBudget.cs` + `tests/test_streamingview_decor_seam.cs`; `[рантайм]` — F3-профиль при покраске 4 лесных чанков (спайк от 25 синхронных семплов на чанк), плюс проверить, что `SampleBlendedCell` не стал дешёвым форвардером (тогда эффект меньше — замер).
- **Объём:** **M** (~35 строк, один файл). **Перестройка:** нет.

### C-14 · P2 (перф, настройка) · Кап догоняющих тиков почвы

- **Файлы:** `src/simulation/AgentSimulationThread.cs:285` (`private const int MaxSoilTickRepeatsPerPass = 8`); использование — `:643` (humidity) и `:708` (fertility) по фрагменту `repeats < MaxSoilTickRepeatsPerPass`.
- **Минимальная правка:** `8 → 4` — **только после замера**, это запас на ускорение времени, а не дефект. Вокруг правки уже появились `stepMultiplier`/`diffShift` (`FertilityMap.cs:224-237`), которые компенсируют шаг — при их наличии кап можно снижать агрессивнее.
  ```csharp
  -   private const int MaxSoilTickRepeatsPerPass = 8;
  +   private const int MaxSoilTickRepeatsPerPass = 4;
  ```
- **Что НЕ трогаем:** `stepMultiplier`/`diffShift`, коэффициенты диффузии, `SpeedMultiplier`.
- **Риск:** на 25× почва/влага отстают от игрового времени иначе (кадры плавнее, динамика другая). Комментарий в коде обосновывает 8 — без замера **не применять**.
- **Проверка:** `[рантайм]`: 25× + панорамирование 30 с — пик кадра (было до 80–180 мс по C-14) и отклонение влажности от эталона при 1×.
- **Объём:** **S** (1 константа). **Перестройка:** нет.

---

## Вне этой волны

| ID | Причина | Что нужно |
|---|---|---|
| **C-10** | **Уже исправлен** в дереве (`FertilityMap.cs:236`) | только регресс-тест `tests/TestFertilityAnchor.cs` |
| **G-02** | **Обнулён**: `PlanetaryHydrology` без вызовов; карта и мир идут в один `PureWorldGenerator` (реки — `DomainRiverMeander`) | при желании — карточка «удалить мёртвый `PlanetaryHydrology`» (S, чистка) |
| **C-11, C-12** | Оценки относились к прежнему телу `GenerateChunk`; путь заменён на `PureWorldGenerator` (`UnifiedWorldGenerator.cs:103,171`) | заново профилировать **новый** путь (M/L), отдельная задача |
| **C-15** | Не эта зона: `ZoneCoverageMap`/`ZoneJobRouter` (`07_econ_code.md`, там же C2-06) | передать в econ-план |
| **W2-06** (удаление отвергнутых дельт на выходе) | Персистентность | `01_stores.md` |
| **W2-08** (руда: origin из статики) | Мир за кромкой/экономика | `07_econ_code.md` / `02_worldedge.md` |
| **W2-09** (float32 в `terrain_blend`) | Шейдеры | `03_render.md` |
| **R2-01** | Закрыт в дереве (`StreamingWorldView.cs:1494-1505`) | — |

---

## Волны применения

**Волна 1 — `scenes/main/Main.cs` (один файл, один исполнитель; правки рядом по строкам):**
1. **W2-02** — гейт зума + защита от `Math.Abs(long.MinValue)` (`:789-810`, поля `:65-66`, сбросы `:475/:566`).
2. **W2-01** — убрать `_worldView.WorldSeed` (`:393-394`), закрыть ранний `return` (`:399-400`).
Проверка после пары: `dotnet build` 0/0; `tests/TestRegionSwitchKeep.cs`.

**Волна 2 — `src/core/world/generation/OreWorldField.cs` (один пакет, независимые правки):**
1. **W2-05** (порядок `RememberKnown`) — S.
2. **W2-04** (повторная проверка под `CacheWorldGate`) — S.
Проверка: `[рантайм]`-стенды из карточек; сборка.

**Волна 3 — `src/ui/streaming/StreamingWorldView.cs` (один пакет):**
1. **C-13** (сетка плодородия в фоне) — M; первой: меняет путь данных декора.
2. **W2-03** (потолок `_oreArrays`) — M; после зелёного `tests/test_streamingview_decor_seam.cs` и `tests/TestStreamingPaintBudget.cs`.

**Волна 4 — `G-01` (после волны 1, т.к. тоже `Main.cs`):**
1. Расширить гейт в `WorldMapOverlay` на окно 2×2 (S) — закрывает игровой симптом.
2. Страховка в `Main.OnRegionSelected` (S) — defense-in-depth.
Проверка: `tests/TestWorldMapClick.cs` + ручной сценарий «клик у воды».

**Волна 5 — `C-14`, только после `[рантайм]`-замера** 25× (иначе не начинать).

**Не начинать:** C-11/C-12 (нужен профиль нового `PureWorldGenerator`), G-02 (закрыт сменой генератора; при желании — чистка `PlanetaryHydrology`).