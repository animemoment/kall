# R-track: рендер, draw calls, подсветка, профиль

**Статус документа:** статический анализ кода, БЕЗ рантайм-замеров.
**Дата:** 2026-10-23. **Автор:** субагент `render-perf`.
**Важно:** все числа вида «draw calls», «мс/кадр» — **оценки из кода** (помечены явно), кроме тех, что пользователь дал как факт из сессии.
Единственный созданный файл — этот отчёт. `src/`, `scenes/`, `resources/`, `project.godot`, `*.tscn`, `*.cs` не изменялись.

---

## 0. Метод и инструменты

### 0.1 Что выполнено

Обязательный графовый визуализатор запускался из корня проекта `D:\bebebe-main (1)\bebebe-main` с `--repo`:

| Команда | Результат (сжато) |
|---|---|
| `stats` | 301 файл (`238 .cs`, `52 .gd`, `11 .tscn`), 2136 символов, 752 ребра, 2295 методов, `errors: 0`. `top_fan_out`: `AgentSimulationThread.cs` = 32, `Main.cs` = 26. `top_fan_in`: `DynamicWorkScheduler.cs` = 26 |
| `impact --file src/ui/renderers/MapRenderer.cs` | `files_count: 21`, прямые потребители: `Main.cs`, `HUDController.cs`, `MainMenuController.cs`, `StreamingWorldView.cs`, `BuildTool.cs`, `ZoneDraftTool.cs` + 4 теста; `in_cycle: false` |
| `impact --file src/ui/renderers/AgentRenderer.cs` | `files_count: 8`, прямые: `scenes/main/Main.cs`, `tests/TestRenderLodBenchmark.cs` |
| `impact --file src/core/world/HumidityMap.cs` | `in_cycle: true`, **`scc_size: 62`**, `files_count: 49` (см. §5) |
| `impact --file src/ui/PerformanceOverlay.cs`, `.../FarmZoneRenderer.cs`, `.../tools/WarehouseTool.cs` | `direct: []`, `files_count: 0` — **слепые пятна графа**, см. §5.2 |
| `hotpath` | 715 «горячих» методов; entry_points включают `Main._Process:569`, `MapRenderer` (через рантайм), `PerformanceOverlay._Process:403`, `CameraController._Process:87`, `WorldChunkStreamer.Update:131`, `HumidityMap.Tick:233` и `:344`, `FertilityMap.Tick:170` |
| `concurrency` | Максимальный риск = **4** у `src/ui/renderers/MapRenderer.cs`: `static_singleton: 1`, `interlocked: 1`, `concurrent_collection: 2`, `thread: 2`, `volatile: 3`, `jobsystem: 3`, **`shared_field_writes: 31`**. Второй по риску = `JobDispatcher.cs` (risk 4) |
| `layers` | Нарушения слоёв: `core → simulation` (`IslandDomainReplay.cs` → `FarmJobManager/StockpileManager/...`, `SimEvents.cs` → `ZoneJobRouter.cs`), см. §5.1 |
| `coverage` | `src_files: 207`, `covered: 139`, **`percent: 67.1`**. Не покрыты тестами: `scenes/main/Main.cs`, `src/simulation/simd/SimdNeedsBatch.cs`, `src/core/world/climate/PlanetaryHydrology.cs` |
| `deadcode` | Неиспользуемые файлы: `CpuTopology.cs`, `ToolRequirement.cs`, `CityState.cs`, `ResourceTypes.cs`, `ItemDefinition.cs`, `ItemRegistry.cs`, `JobCategory.cs`, `JobData.cs`, ... |
| `callers --symbol SetHoveredTile` | **ровно 1 call_site:** `src/ui/tools/SelectTool.cs:31`; определения `FarmZoneManagerFacade.cs:127`, `ZoneManager.cs:606` |
| `callers --symbol OnHover` | **ровно 1 call_site:** `src/ui/PlayerInteractionManager.cs:109`; определения: `BuildTool.cs:64`, `FarmingTool.cs:28`, `SelectTool.cs:29`, `StoneMiningTool.cs:31`, `TreeFellingTool.cs:26`, **`WarehouseTool.cs:21`**, `ZoneDraftTool.cs:74` |

### 0.2 Границы достоверности

- **Замеров нет.** Рантайм в этой задаче запрещён; игра не запускалась для получения чисел. Всё, что помечено «оценка из кода», выведено из: количества узлов-рендереров, `rendering_quadrant_size`, количества уникальных текстур/материалов и известного факта пользователя «≈400 draw calls».
- `project.godot` не изменялся (`git status` не содержит его; секция `[autoload]` — предсуществующая, строки `McpInteractionServer`, `AgentTools`, `PerfProbe`, `LiveBridge`).
- Там, где вывод нельзя сделать из кода, стоит метка **`[требует рантайм-проверки]`** и указан конкретный замер.

---

## 1. Инвентарь draw call'ов

### 1.1 Модель оценки

Godot 4 `TileMapLayer` разрезает слой на **квадранты** размером `rendering_quadrant_size` клеток; каждый непустой квадрант — отдельная единица отрисовки (свой батч). Отсюда базовая формула оценки:

```
draw_calls_слоя ≈ число НЕПУСТЫХ квадрантов, попавших в кадр
```

- Все слои `MapRenderer` созданы с `RenderingQuadrantSize = 128` (`MapRenderer.cs:271, 297, 300, 303-307, 323-327, 331, 334, 337-341, 348-352, 356, 359-363, 372-376`) → при карте 1024² (`MapRenderer.cs:33-34`) это **8×8 = до 64 квадрантов на слой**.
- Все слои `StreamingWorldView` — `RenderingQuadrantSize = 64` (`StreamingWorldView.cs:1372`) в пределах `MakeLayer` (`:1367-1376`).
- `MultiMeshInstance2D` = ровно **1** draw call на узел (инстансы батчатся движком).
- `Node2D._Draw()` = 1 canvas item = **1** draw call, независимо от числа примитивов внутри.

### 1.2 Таблица: источник → оценка → что делать → ожидаемый выигрыш

Все `file:line` — точки создания/включения. «Оценка» — по формуле §1.1.

| # | Источник | file:line | Оценка calls | Что можно сделать (без отрезания функций, D-08) | Ожидаемый выигрыш |
|---|---|---|---|---|---|
| 1 | `Main.tscn`: `wood_wall_tile`, `mountains_tile`, `ProcessOfWork`, `GardenBeds` (4 × TileMapLayer) | `scenes/main/Main.tscn:742,745,748,751` | **0 cells → 0 calls**, но 4 живых CanvasItem | Эти узлы нужны только как **доноры TileSet** (`MapRenderer.GetWallTerrainTileSet()` и т.п., `StreamingWorldView.cs:1316,1323,1340`). После извлечения TileSet — `Visible=false` (или перенести доноры в Resource) | 0 calls, −4 CanvasItem в обходе каждый кадр |
| 2 | `_groundSurface` ColorRect + `terrain_blend.gdshader` (follow-окно 512²) | `MapRenderer.cs:251-269` | **1** | Уже оптимально (1 квад на весь террейн — замена 1M cells в TileMapLayer) | — |
| 3 | `_groundLayer` TileMapLayer | `MapRenderer.cs:271-295` | **0** (принудительно `Visible=false`, когда шейдер загрузился) | Оставить как fallback | — |
| 4 | `_mountainLayer` | `MapRenderer.cs:297-298` | 1…64 | Слить с `_objectLayer` в один TileSet+слой (горы и объекты не пересекаются по клетке) | до −63 |
| 5 | `_farmLayer` (грядки) | `MapRenderer.cs:300-301` | 1…64 | Грядки — научеренный тайлсет с terrain-разметкой; слить с `_farmBlueprintLayer` нельзя (разный `Modulate`), но **можно** заменить `Modulate` на per-source оттенок в атласе → слияние | до −63 |
| 6 | `_farmBlueprintLayer` (α=0.5) | `MapRenderer.cs:303-309` | 1…64 | Все «планы» (`_farmBlueprintLayer`, `_blueprintLayer`, `_wallBlueprintLayer`) — один визуальный класс «полупрозрачный чертёж». Свести к **одному** TileSet-атласу и **одному** слою | до −128 |
| 7 | `_stockpileLayer` (Modulate фиолетовый) | `MapRenderer.cs:323-329` | 1…64 | Модуляцию — в цвет тайла атласа, затем слить с зонно-плановым слоем | до −63 |
| 8 | `_objectLayer` (деревья + камни, композитный атлас) | `MapRenderer.cs:331-332`, `SourceObjectComposite=20` (`:75`) | 1…64 | Уже сведён в 1 текстуру. Дальше — только квадранты (§1.3) | — |
| 9 | `_buildingLayer` (мебель/верстак) | `MapRenderer.cs:334-335` | 1…64 | Мебель — в тот же colony-атлас, что `_objectLayer`; z-порядок совместим (мебель над деревом не встречается в одной клетке) | до −63 |
| 10 | `_blueprintLayer` (чертежи мебели, α=0.5) | `MapRenderer.cs:337-343` | 1…64 | См. #6 | (входит в −128) |
| 11 | `_wallBlueprintLayer` (α=0.5) | `MapRenderer.cs:348-354` | 1…64 | См. #6 | (входит в −128) |
| 12 | `_wallLayer` (построенные стены) | `MapRenderer.cs:356-357` | 1…64 | Terrain-автотайлинг обязателен (состыковка), но квадрант можно увеличить | §1.3 |
| 13 | `_ghostLayer` (призрак инструмента, α=0.4) | `MapRenderer.cs:359-365` | 0…64 | **Дефект R-05:** слой не скрывается, когда призрака нет. Скрывать `Visible=false` при `_painted.Count==0` | 0…−64 |
| 14 | `_workProgressLayer` (стадии работ) | `MapRenderer.cs:372-378` | 1…64 | Уже есть LOD-скрытие при zoom<0.22 (`:563-568`). Порог поднять/привязать к оценке dc | — |
| 15 | `_humidityOverlay` / `_fertilityOverlay` TextureRect | `MapRenderer.cs:312-320` | 0 или 1 | Уже `Visible=false` вне режима (`:315`, `:1062`) | — |
| 16 | `_designationRenderer` MultiMesh | `MapRenderer.cs:366-368` | 1 | Уже MultiMesh | — |
| 17 | `ShadowRenderer` (`ShadowCasterRenderer`) MultiMesh | `MapRenderer.cs:380-381` | 0…1 | Уже MultiMesh; LOD-скрытие при zoom<0.22 (`:569-572`) | — |
| 18 | `StockpileItemRenderer` — **по 1 MultiMesh на ItemId** | `StockpileItemRenderer.cs:27-73`; `ItemId` = `{None, Log, Grain, Stone}` (`ItemDefinition.cs`) | **3** | Свести иконки предметов в **один атлас**, выбор региона через `INSTANCE_CUSTOM` → 1 MultiMesh | **−2** |
| 19 | `StreamingWorldView`: `_groundLayer`, `_markLayer`, `_mountainLayer`, `_wallLayer`, `_wallPlanLayer`, `_farmLayer`, `_farmPlanLayer`, `_planLayer` — **8 слоёв** | `StreamingWorldView.cs:1310-1356` | 8…8·N | `_wallLayer`+`_wallPlanLayer` и `_farmLayer`+`_farmPlanLayer` различаются **только `Modulate`** (`:1335`, `:1348`, `:1355`) → прозрачность в атлас → **−3 слоя**. `_planLayer` (`:1354`) — тоже α=0.5 → **−1**. Плюс `_textureDirty`/`RepaintDirty` уже батчат | **−4 слоя**, у каждого до −N квадрантов |
| 20 | `AgentRenderer`: `_multiMeshInstance` + `_shadowInstance` (общий MultiMesh) | `AgentRenderer.cs:199-210`, `236-246` | **2** | Уже оптимум по числу узлов. Экономия — в байтах/кадр (§2.1, R-07) | 0 calls |
| 21 | `CropRenderer`: **4** MultiMesh (по фазе роста) | `CropRenderer.cs:34-36, 54-85` | **4** | Фазы роста уже в отдельных текстурах (`phase1..4`). Положить 4 фазы в **один атлас** (4 колонки) → 1 MultiMesh | **−3** |
| 22 | `GroundItemRenderer`: **3** MultiMesh (Log/Grain/Stone) | `GroundItemRenderer.cs:38-68` | **3** | Тот же атлас предметов, что и #18 → 1 MultiMesh | **−2** |
| 23 | `ItemShadowRenderer` | `ItemShadowRenderer.cs:67-77` | 1 | — | — |
| 24 | `HUD` — `scenes/ui/hud.tscn`: 47 узлов, **23 Button**, 9 HBox, 6 Label, 3 PanelContainer, 2 TextureRect; **19 уникальных PNG-иконок** | `scenes/ui/hud.tscn` (ext_resource-пути), `HUDController.cs:96-194` | **~35–45** | **19 отдельных PNG → 1 атлас иконок**; `AtlasTexture` выбирает регион. Кнопки с одним StyleBox уже батчатся | **−18** |
| 25 | Окна-сцены: `Build.tscn` (23 узла), `Farm.tscn` (19), `JobDisplay.tscn` (21), `Prioritet.tscn` (32), `villager.tscn` (6) | `scenes/ui/*.tscn` | 0 (скрыты) … ~15–30 (открыто окно) | Отдельный атлас иконок окон (тот же, что #24) | −5…−15 при открытом окне |
| 26 | `SelectionBox._Draw` | `SelectionBox.cs:85-103` | 0…1 | — | — |
| 27 | `FarmZoneRenderer._Draw` | `FarmZoneRenderer.cs:54-108` | **1** (все зоны в одном CanvasItem) | Уже 1 call. Проблема не в calls, а в **числе примитивов** (`DrawRect` на каждый тайл: `:79`, `:86`) — см. R-06 | 0 calls |

### 1.3 Откуда берутся ~400 (ведущая гипотеза, **оценка из кода**)

Слоёв-кандидатов с содержимым: **11** в `MapRenderer` (#4–#14) + **8** в `StreamingWorldView` (#19) = **19 слоёв**. При `rendering_quadrant_size = 128` и карте 1024² верхняя граница = `19 × 64 = 1216`. Реалистично заполнены далеко не все квадранты; при 5–7 «содержательных» слоях с большим покрытием получаем `≈ 6 × 64 ≈ 384` — **это и есть порядок 400**.

Формально:

```
≈400 ≈ (слои с большим покрытием: _objectLayer, _farmLayer, _stockpileLayer,
        _wallLayer, _mountainLayer, _buildingLayer) × 64 квадранта
```

**Проверка гипотезы (единственный надёжный способ) — `[требует рантайм-проверки]`:**

1. `Performance.Monitor.RenderTotalDrawCallsInFrame` уже читается оверлеем (`PerformanceOverlay.cs:483` в mini-режиме, `:508` в AI-отчёте) — **новый инструмент не нужен**, нужен только вывод в покое при фиксированной камере.
2. Бисекция `rendering_quadrant_size ∈ {16, 32, 64, 128, 256}` **при неизменном видимом окне** (одна и та же позиция+зум камеры). Если dc падает ~линейно по `1/quadrant²` — гипотеза верна.
3. Поочерёдное `Visible=false` для 19 слоёв (по одному) с чтением того же монитора → получаем **таблицу «слой → его calls»**. Это и есть тот «инвентарь», который в §1.2 сейчас оценён.
4. Отдельный прогон: `HUD` скрыть целиком (`CanvasLayer/visible=false`) → разница даёт вклад UI.

**Оценка экономии при выполнении всех мер §1.2:** `−4 слоя` стриминга, `−128` на плановых слоях, `−63`×3 на слиянии гор/мебели/склада, `−18` на атласе HUD-иконок, `−7` на MultiMesh предметов/культур. **Итого ожидаемое снижение (оценка из кода): −35…−45 % draw calls**, при этом ни одна функция не удаляется — только унификация текстур и слияние слоёв.

---

## 2. Главный поток: что дорого в кадре

### 2.1 `AgentRenderer._Process` — `Render: Agent MultiMesh`

`AgentRenderer.cs:410-620`, скоуп объявлен на `:415`.

Дорогое (в порядке вклада):

| Место | Работа | Оценка |
|---|---|---|
| `AgentRenderer.cs:528` | `_multiMesh.Buffer = _gpuInstanceBuffer` — маршалинг `float[]` → `PackedFloat32Array` + upload в RenderingServer | **основной вклад**; 10 000 × **16 float × 4 Б = 640 КБ** на кадр-снапшот. Комментарий `:21-30` фиксирует, что до исправления двойной upload давал ~28 мс пика; после дедупликации `_shadowSharesBodyMultiMesh` (`:217-221`) осталось 7.5 мс пика — совпадает с фактом пользователя |
| `AgentLerpBatch.cs:145-167` (`FillGpuInstanceBuffer`) | CPU-заливка 16 float/инстанс | **6 из 16 float — константы**: `[0..7]` = единичная матрица с нулевой трансляцией (`:148-155`), `[10] = 0`, `[11] = 1`. 37.5 % буфера — мусор, который каждый раз маршалится |
| `AgentRenderer.cs:453-456` | `AgentLerpBatch.Interpolate` по 10k (SIMD) | ~0.02–0.05 мс |
| `AgentRenderer.cs:457` | `Array.Copy` 10k × 8 Б = 80 КБ | ~0.02 мс |
| `AgentRenderer.cs:458-466` | цикл 10k с `MathF.Atan2` для движущихся | ~0.1–0.3 мс при 3k движущихся |
| `AgentRenderer.cs:507-512` | 2–3 × `SetShaderParameter` за кадр | P/Invoke, ~0.01 мс |
| `AgentRenderer.cs:415` | `GameProfiler.Scope` → `GetCachedName` (`GameProfiler.cs:254-263`) + `_metrics.GetOrAdd` + 2 × `Interlocked` + CAS-цикл (`:265-281`) | ~1–3 мкс, незначимо |

**Вывод по R3 (AgentRenderer):** стоимость на 90 % — это **байты на кадр-снапшот**, а не вычисления. Пользователь видит `Σ 19–46 ms/s` при снапшотах 20–30 Гц (`GetMinSnapInterval`, `AgentSimulationThread.cs:155-163`: `1/30` с, `1/20` на 25x+) → `25 × 1.8 мс ≈ 45 мс/с` среднего и `7.5 мс` пик. Совпадает.

**Что делать (D-08-совместимо, без потери качества):**
- **(a) Упаковка 16 → 12 float.** Поворот уже лежит в COLOR (`cos,sin,0,1`): `AgentInterpolation.gdshader:12-14` читает `COLOR.r/.g`. Матрица `Transform2D` — это ровно матрица поворота, её базисные 4 float `buffer[0],[1],[4],[5]` сейчас константны. Переносим `cos,sin` туда → `UseColors = false` (`AgentRenderer.cs:193`) → **480 КБ вместо 640 КБ (−25 %)**, 0 потери визуала.
- **(b) Трансляцию матрицы отдать под `target`** (`buffer[3], buffer[7]` — сейчас всегда 0): `INSTANCE_CUSTOM` оставить только `prev` (`buffer[12..15]`). Итог тот же — 12 float, но без правки шейдера по COLOR.
- **(c) Снизить частоту upload без потери плавности:** сейчас `bodiesNeedUpdate` = «есть снапшот» (`:480-484`). На скорости < 25x шейдер и так интерполирует `lerp_factor` — можно отправлять новый `Buffer=` только когда фактическое смещение > half-pixel (порог по `Vector2`-дельте, считать бесплатно в уже идущем цикле `:458-466`). Ожидаемо: −30…−50 % upload-кадров в простое.
- **(d) Побочно:** `UseColors/UseCustomData` в CPU-fallback (`:228-229`) уже `false` → 8 float; GPU-ветку надо привести к тому же минимуму.

**Ожидаемый эффект (оценка из кода):** `Σ 19–46 ms/s` → `8–20 ms/s`, пик 7.5 мс → **≤ 4 мс**.

### 2.2 `MapRenderer._Process` — `MapRenderer.cs:513-615`

| Место | Работа за кадр | Оценка мс/кадр |
|---|---|---|
| `:515-522` | до `MaxCellsPerFrame = 2048` (`:194`) `EraseCell` по срубленным деревьям | 0.1–0.6 |
| **`:525-531`** | **адáптивный бюджет: `_pendingCells.Count > 4096 ? 16384 : 2048` → до 16 384 `SetCell`/`EraseCell` за кадр** | **2–10** — **главный источник спайков, коррелирующих с кликом назначения работы** (массовая разметка зоны склада/фермы кладёт десятки тысяч клеток в `_pendingCells`, `:397-400`) |
| `:535-541` → `RefreshAllBlueprints` `:1304-1331` | `_blueprintLayer.Clear()` + `SetCell` на **каждый** чертёж + каждую размеченную грядку, с дебаунсом 200 мс | **3–15** на кадре срабатывания; `Clear()` пересобирает весь слой |
| `:552` | `_wallBuildManager.FlushPending()` | 0–1 |
| **`:555-558`** | 4 × `TerrainTileLayer.Flush` → внутри `exists(c)` вызывается **ДВАЖДЫ** на клетку (`TerrainTileLayer.cs:56` и `:62`), затем один `SetCellsTerrainConnect` | 0.2–3; **двойной вызов `exists` — чистая потеря 50 %** → R-08 |
| `:561-572` | смена `Visible` у `_workProgressLayer`/`ShadowRenderer` при пересечении zoom 0.22 | **Priming-спайк**: флип `Visible` у TileMapLayer = перестройка квадрантов; приходится ровно на **зум колесом** — совпадает с наблюдением пользователя |
| `:574` → `FlushWorkProgress` `:991-1020` | `DrainDirty(_progressDrain, 256)` + до 256 `SetCell` + раз в 2 с `SweepStale` + `EraseCell` по каждому протухшему | 0.1–1.2 |
| `:576-581` | `Mathf.Pow(speed, 0.45)` + `SetShaderParameter("water_time")` — **каждый кадр** | ~0.01 (P/Invoke), но лишний; можно при `speed` без изменений и `delta` тот же — параметр всё равно меняется, оставить |
| `:583-613` | при сдвиге окна на 64 клетки → `RequestTerrainWindowUpdate` (фон) → `ApplyTerrainBuffer` на главном потоке: заливка `512×512` RGBA = **1 МБ** в ImageTexture | **4–12** на кадре применения; приходится на **пан/зум** — второй источник спайков от ввода |

**Итог:** в покое (камера стоит, очередей нет) `_Process` ≈ **0.3–0.6 мс**. Спайки до **10–15 мс** дают ровно те три сценария, которые пользователь назвал: клик разметки (`_pendingCells` при `:525`), зум (флип `Visible` при `:563-572`), пан (upload террейна при `:604`).

### 2.3 `StreamingWorldView._Process` — `StreamingWorldView.cs:214-243`

- `:220-223` — `DrainReady` + `ApplyReady` до `MaxReadyChunksPerFrame = 4` (`:37`) чанков (64×64 клеток): покраска в TileMapLayer → 0.5–4 мс при загрузке.
- `:229` `ProcessExits()` → `ProcessEraseWork(MaxCellsErasedPerFrame = 2048)` (`:48`, `:891-895`) → 0.3–2 мс.
- `:230` `RepaintDirty()` — только при `_textureDirty.Count > 0` (`:897`).
- `:236-242` — `Mathf.Pow` + `SetShaderParameter("water_time")` каждый кадр (дубль расчёта из `MapRenderer.cs:578-581`; расчёт идентичен намеренно, см. комментарий `:232-235`).
- `:214-223` **всегда**, даже если ничего не готово (дешёвые `TryDequeue`).

**Оценка:** 0.05–0.3 мс в покое, 1–5 мс при стриминге.

### 2.4 Прочие `_Process` на главном потоке (полный список найденного)

| Файл:строка | Что делает каждый кадр | Оценка |
|---|---|---|
| `scenes/main/Main.cs:569` | синк стримера, гейт 120 мс (`:31`) | 0–1 |
| `src/ui/CameraController.cs:87` | ввод + `ClampPosition` | ~0.01 |
| `src/ui/controllers/HUDController.cs:742` | `FollowHumidityTooltip` (`:703`) + `TickSoilTooltip` (`:714`, гейт 0.25 с) + `TickZoneHint`; `RefreshStats` 1/с, `UpdateWorldTimeDisplay` 1/2с | 0.02–0.1 |
| `src/ui/PerformanceOverlay.cs:403` | таймеры; тяжёлое — раз в 0.1 с | см. §2.5 |
| `src/ui/renderers/GroundItemRenderer.cs:76` | `TryDequeue` снапшота, иначе `return` | ~0 |
| `src/ui/renderers/StockpileItemRenderer.cs:81` | то же | ~0 |
| `src/ui/renderers/DesignationRenderer.cs:106` | только при `_isDirty` | ~0 |
| `src/ui/renderers/ItemShadowRenderer.cs:152` | `UpdateAndRender()` **безусловно каждый кадр** (`:157-229`): обход всех `_sourceSpots` + перезаливка буфера при `visibleCount>0` | 0.05–0.4 (растёт с числом предметов) |
| `src/ui/renderers/FarmZoneRenderer.cs:39` | гейт 0.1 с (`:25-47`) — ок | ~0 |
| `src/ui/renderers/CropRenderer`, `ShadowCasterRenderer` | по снапшоту/`Tick` | 0–0.3 |
| `autoload/perf_probe.gd:257`, `autoload/mcp_interaction_server.gd:43`, `autoload/mcp_interaction_server.gd:4002` (`_draw`), `addons/live_bridge/live_bridge.gd:45` | **dev-автолоады** (см. `project.godot [autoload]`) | `[требует рантайм-проверки]` — в релизной сборке их быть не должно; `_draw` в 4000-строчном GDScript-файле — подозрительный источник и draw calls, и CPU |

### 2.5 `PerformanceOverlay._Process` — корректность и аллокации (пункт 6 задания)

**Что считается корректно:**
- Окно усреднения честное: `_sinceSnapshot`/`_framesSinceSnapshot` (`:414-425`), `GameProfiler.SnapshotMetrics(out _metrics, windowSec, frames)` (`:427`) — `GameProfiler.cs:286-346` берёт **дельту** `Interlocked.Exchange(TotalTicks, 0)` и нормирует на кадр. Комментарий `AgentSimulationThread.cs:394-399` объясняет, почему суммы не сбрасываются на тик — это правильно.
- Draw calls уже читаются из движка: `:483` (`RenderTotalDrawCallsInFrame`), `:508-511` (+ objects/primitives/VRAM). **Инструмент для замера R1 уже существует** — не хватает только разбивки по источникам (FEAT-R01).
- Тренды: 1 точка/с (`:452-474`), бюджет кадра — `1000/FPS` (`:469`).

**Дефекты оверлея (аллокации/строки):**

| Место | Проблема | Оценка |
|---|---|---|
| `:492-499` | `_label.Text = Build*Text()` — большая строка заново 10 раз/с. Это **не** per-frame, но 10 Гц × несколько КБ → Gen0 churn | 10–60 КБ/с |
| `:486` | `_copyStatus.Text = $"FPS: ... RAM: ... DC: ..."` в mini-режиме — 10 раз/с; при `_isMiniMode` **ранний return на `:487` не мешает** строке строиться каждый раз | 10 строк/с |
| `:460-464` | `camTag`-строка **каждую секунду даже при `!_isVisible`** (проверка `_isVisible` стоит на `:476`, ниже) | 1 строка/с + `SimulationWatchService.CaptureNow` |
| `:381-400` | `_Input` логирует **каждое** движение? Нет — только `InputEventMouseButton` (`:386-394`) и не-Echo клавиши (`:397-400`). Приемлемо, но `ProfilerLogService.RecordInput` со строковой интерполяцией на **каждый клик** | ~1 строка/клик |
| `GameProfiler.cs:265-281` | `RecordElapsedTicks` — **2 × `Interlocked` + CAS-цикл + `ConcurrentDictionary.GetOrAdd` на КАЖДЫЙ scope**. `AgentSimulationThread` вызывает его на фазу (дёшево), но каждый `Scope()` в горячем цикле был бы катастрофой. Сейчас корректно: `:677-679` прямо запрещает замеры в `UpdateSingleAgent` | — |
| `:430-450` | автопауза по спайку: `foreach` по `_metrics` 10 раз/с — дёшево, но **меняет состояние игры** (`TimeManager.SetSpeed(Paused)`) из отчёта. Опасно для воспроизводимости замеров | — |

**Вердикт:** оверлей считает **корректно**; собственных спайков не создаёт (тяжёлая часть — 10 Гц, не кадр). Основная претензия — **строки на 10 Гц** и отсутствие разбивки draw calls по слоям (FEAT-R01).

---

## 3. Дефекты

Приоритеты: **P0** — ломает требование пользователя, **P1** — заметная стоимость/регресс, **P2** — гигиена.

### R-01. P0 — Зона склада НЕ подсвечивается при наведении (симптом R2)

**Механизм (полная цепочка, доказано по коду):**

1. Единственный источник наведения в мире — `PlayerInteractionManager._UnhandledInput` → `_currentTool.OnHover(...)` (`PlayerInteractionManager.cs:104-110`). Других call_site'ов `OnHover` в проекте нет (`viz_cli callers --symbol OnHover` → только `PlayerInteractionManager.cs:109`).
2. `SelectTool.OnHover` (`SelectTool.cs:29-41`) — единственное место, которое **что-то** делает с наведением: `FarmZoneManager.Instance.SetHoveredTile(x, y)` (`:31`) → фасад (`FarmZoneManagerFacade.cs:127`) → `ZoneManager.SetHoveredTile` (`ZoneManager.cs:606-616`) → `FindZoneAt` → `HoveredZone` + `OnZoneHovered`.
3. Рисует подсветку `FarmZoneRenderer._Draw` (`FarmZoneRenderer.cs:54-108`): фиолетовая/белая заливка тайлов `:75-88` и утолщённая рамка `:91-106` — **только** для объектов из `ZoneManager.GetAllZones()` (`:57-59`).
4. **Склад в `ZoneManager` не регистрируется.** Владелец — `StockpileManager`; это прямо зафиксировано в `SelectTool.cs:87-89`: «Склады в ZoneManager НЕ регистрируются (их владелец — StockpileManager)».
5. В `StockpileManager` **нет ни состояния наведения, ни события** — есть только `SelectedTile`/`SelectTile`/`DeselectTile` (по `SelectTool.cs:124`, `StockpileManager.cs:403-433`).
6. Слой склада рисуется статически: `_stockpileLayer` создан с постоянным `Modulate = new Color(0.65f, 0.15f, 0.9f, 0.45f)` (`MapRenderer.cs:326`) и **нигде** этот `Modulate` не меняется (grep по `_stockpileLayer`: только создание `:323-329`, `Clear` `:1100`, `TileSet` `:1120`, и `AddChild`).
7. `WarehouseTool` — **мёртвый код**: `new WarehouseTool(...)` не встречается ни в `src/`, ни в `scenes/` (grep). Его `OnHover` пуст (`WarehouseTool.cs:21`). Реальный инструмент склада — `ZoneDraftTool` с `_zoneKind == "warehouse"` (`HUDController.cs:1217-1218`, вызов из `HUDController.OnWarehouseAreaPressed` `:1170-1174` → `BuildMenuController.Open("warehouse", "Склад")` `:1173`), и его `OnHover` тоже пуст (`ZoneDraftTool.cs:74`).

**Итог:** «ферма/рубка подсвечиваются» — потому что ферма/Work-зона живут в `ZoneManager` и их красит `FarmZoneRenderer`. Рубка/добыча на самом деле **не** подсвечиваются (их `OnHover` пусты: `TreeFellingTool.cs:26`, `StoneMiningTool.cs:31`), а подсветка, которую пользователь видит на «других зонах», — это `FarmZoneRenderer`. У склада нет ни владельца наведения, ни визуального канала — **точка обрыва: `StockpileManager` (нет `HoveredTile`) + `MapRenderer._stockpileLayer` (статичный `Modulate`) + `SelectTool.cs:31` (наведение уходит только в `FarmZoneManager`)**.

**Где чинить (минимально, 3 точки):**
- `StockpileManager`: добавить `HoveredTile` (`int _hoveredX/_hoveredY`, `int.MinValue`), `SetHoveredTile(x,y)` + `event Action<int,int> OnHoveredTileChanged` (по образцу `ZoneManager.cs:606-616`, только без поиска зоны — склад клеточный).
- `SelectTool.OnHover` (`:29-41`): рядом с `FarmZoneManager.SetHoveredTile` вызвать `StockpileManager.Instance.SetHoveredTile(x, y)` (или через новый узкий интерфейс — чтобы не сомкнуть цикл, см. §5).
- Визуал: в `FarmZoneRenderer._Draw` **не** тащить склад (он не Zona); правильнее — отдельный лёгкий `Node2D`-оверлей (`StockpileHoverRenderer`) на **1** draw call, рисующий заливку+рамку только для клетки/клеток склада под курсором, с `ZIndex` выше `StockpileLayer` (сейчас `ZIndex` слоя — по умолчанию, `StockpileItemRenderer` = 6, `FarmZoneRenderer` = 8). Тот же визуальный язык, что у фермы: `HoverFillColor = (1,1,1,0.12)` + `HoverBorderColor = (1,1,1,0.95)`, толщина 3.2 (`FarmZoneRenderer.cs:14,17,11`), но в фиолетовой гамме склада (`WarehouseTool.cs:13-14` уже содержит нужные цвета).
- **Не** дублировать `ZoneManager.SetHoveredTile` для склада: склад не должен попадать в `GetAllZones()` — иначе сломается `ZoneDraftTool`/`WorkZoneController` (у склада нет `Tiles`/`RadiusTiles`).

**Как проверено:** статически — `viz_cli callers --symbol SetHoveredTile` (1 call_site), `viz_cli callers --symbol OnHover` (1 call_site), grep `WarehouseTool` (0 инстанцирований), grep `_stockpileLayer` (0 изменений `Modulate`). **`[требует рантайм-проверки]`**: клик по складу открывает окно (`SelectTool.cs:124-131` — путь есть), а подсветки нет; проверить скриншотом «до/после».

### R-02. P1 — до 16 384 `SetCell` за один кадр

`MapRenderer.cs:525-531`: `int cellsBudget = _pendingCells.Count > 4096 ? 16384 : MaxCellsPerFrame;`. При массовой разметке (`WarehouseTool.OnRelease`/`ZoneDraftTool` кладут до 250 000 клеток, `WarehouseTool.cs:58-59`) очередь не пустеет много кадров, и каждый кадр — 16k `SetCell`. `SetCell` у `TileMapLayer` инвалидирует квадрант и перестраивает его меш → 2–10 мс/кадр. Это ровно «спайк при клике назначения работы» из профиля пользователя.
**Направление:** адáптивная порция по **времени**, а не по числу клеток (бюджет 1.5 мс через `Stopwatch.GetTimestamp`), плюс начинать с клеток, видимых камерой. Функциональность не меняется — только размазывание (это и есть разрешённая D-08 амортизация).

### R-03. P1 — `HumidityMap.Tick` — полный скалярный обход 1 048 576 клеток

- Размер карты: `Humidity = new HumidityMap(width, height)` (`MapData.cs:62`), `width/height` = параметры `MapData` = **1024×1024** (`MapRenderer.MapWidth/MapHeight = 1024`, `MapRenderer.cs:33-34`; `Main.ColonySizeTiles = 1024`, `Main.cs:27`). **Факт из сессии пользователя «512×512» устарел** — карта выросла, стоимость выросла в **4×**.
- `HumidityMap.Tick` (`HumidityMap.cs:233-340`) — `Parallel.For` по строкам, на клетку: 6 чтений `int[]` + `ground[x,y]` (**многомерный массив = умножение + 2 bounds-check**), 2 чтения `bool[]`-масок, до 4 чтений соседних `forestMask`, и **два дешёвых на вид, но дорогих хеша**:
  - `HumidityMap.cs:304`: `((x*73856093) ^ (y*19349663)) % 4` — % 4 компилятор сведёт к AND;
  - **`HumidityMap.cs:306`: `((x*83492791) ^ (y*2971215073 % 100000)) & 1`** — литерал `2971215073` > `int.MaxValue` → выражение **`long`**, `% 100000` = **64-битное деление на клетку** (~20–40 тактов × 1.05M клеток ≈ 20–40 млн тактов ≈ **7–13 мс** только на этом);
  - `HumidityMap.cs:318`: `% 8` → AND, ок.
- Плюс `AgentSimulationThread.cs:451-477`: при `_environmentalMasksDirty` — второй полный проход 1M клеток (`_reusableForestMask`) + `FarmJobManager.FillGardenBedFlatMask` (`:475`). В обычном тике маски закешированы (`:455`), т.е. дефект — только на тиках после вырубки/посадки.
- Наблюдаемый пик пользователя: **39–70 мс**. Это согласуется: 1M клеток × (6 загрузок + 64-битное деление + ветвления) в `Parallel.For` с плохим балансом по 16 потокам.
- Условие вызова: `GetScaledIntervalHi(250.0f, speed)` (`AgentSimulationThread.cs:440`, константа `TickIntervalGameSec = 250.0f`, `HumidityMap.cs:26`) → часто, но не каждый кадр.

**Что делать (D-08: только алгоритмика и амортизация):**
1. **Убрать 64-битное деление** (`HumidityMap.cs:306`): `2971215073 % 100000 = 1215073 % 100000`… точнее — заменить на 32-битный хеш (`(y * 0x45d9f3b) >>> 16`) и `& 1`. Это **бесплатная** правка, ожидаемо **−7…−13 мс** пика.
2. **SoA вместо `TileType[,]`**: `_ctx.Ground` — 2D-массив (`MapData.Ground`), читается на каждой клетке. Добавить `byte[]`/`bool[]`-маску «вода» (одна из масок `EnsureEnvironmentalMasks` уже строится — `_staticMountainMask`, `:483`; нужна парная `_waterMask`) → убирает умножение+2 bounds-check.
3. **Амортизация по полосам**: `HumidityMap.Tick` вызывать не целиком, а 1/8 карты за тик с 8-фазным курсором (64 полосы по 16 строк) — визуально неотличимо (влага диффундирует за часы), пик **39–70 мс → 5–9 мс**. Это ровно предписанный D-08 приём «−0,5 каждые 50 кадров вместо −0,01 каждый кадр».
4. **Убрать `Parallel.For` на маленькой работе**: после (1)+(2) тик станет ~5 мс; для 1M клеток `Parallel.For` даёт ~3–4× на 16 потоках, но имеет overhead старта/джойна ~0.05–0.2 мс — оставить.
5. GPU-путь **не предлагаю**: `GpuHumiditySimulator.cs` существует (267 строк), но `HumidityMap.cs:252-255` фиксирует, что GPU-путь **отключён намеренно** из-за таймаута `CallOnRenderThread` (200 мс) и порчи буферов. Возврат к нему — отдельный риск, не в этом плане.

### R-04. P1 — `Phase2_ParallelUpdate` не укладывается в кадр: дерево стоимости

`Simulation.TotalStepCycle` Σ 1000–1150 мс/с, пики 370 мс — **симуляция не успевает за реальным временем**: при 100x (`GetSimStepDelta` = 0.5 с, `AgentSimulationThread.cs:667-673`) тик требует `accumulator ≥ 0.5`, а `steps` капнут 1–2 (`:355-357`), т.е. заложено отставание. Это by design.

Дерево стоимости `Phase2_ParallelUpdate` (`AgentSimulationThread.cs:713-794`):

```
Phase2_ParallelUpdate(dt)                                    :713
└─ GameProfiler.Scope()                                      :715  (~2 мкс/вызов, один на фазу)
└─ DynamicWorkScheduler.Shared.ForEachSplittable(count=10000):746  WorkKind.Fast,
   │                                                               "Simulation.Phase2_Balance" → это и есть Σ 620–880 мс/с
   └─ на КАЖДОГО агента: q.ShouldYieldCached(ref cached, ref sinceRefresh, 32)   :766
      └─ UpdateSingleAgent → UpdateSingleAgentInner          :675-711
         ├─ Idle (большинство 10k):  только 1/4 агентов за тик (i & 3 == tickBucket) :688
         │  └─ WanderJobSystem.ExecuteParallel               :690
         │     └─ 4 чтения pool-массивов + sqrt только если distSq>16  WanderJobSystem.cs:49-79
         │     └─ ctx.Movement.IsTileBlocked(nextX, nextY, ctx)        WanderJobSystem.cs:68
         │        └─ HierarchicalPathfinder.Instance.Cost  ← STATIC SINGLETON, 2 разыменования + volatile  AgentMovementService.cs:425-426
         │        └─ + 4 соседа с bounds-check (скольжение по кромке клетки)             AgentMovementService.cs:436-439
         │        └─ вне карты: WorldCellQuery.TerrainAt → ПОЛНЫЙ семпл генератора БЕЗ кэша   AgentMovementService.cs:409-421 → см. R-10
         ├─ Evacuating: NeedsJobSystem.ExecuteParallel или Wander         :695-702
         └─ Job != None: JobRegistry.GetHandler(jobType).ExecuteParallel  :705-710
            └─ ЛЮБОЙ хендлер = по сути ctx.Movement.MoveTowards(...)
               (напр. StockpileHaulingJobHandler.cs:116-120 — один вызов MoveTowards)
               └─ MoveTowards                                     AgentMovementService.cs:25-…
                  ├─ waypoints: MoveToPoint → IsTileBlocked (1–5 вызовов)
                  ├─ WaypointCount==0 && далеко: RequestHierarchicalPath
                  │  └─ HierarchicalPathfinder.TryFindPathScratch   HierarchicalPathfinder.cs:486
                  │     ├─ EnsureOverlay() при _dirty               :499-501  ← lock(_buildLock) + rebuild
                  │     ├─ LineOfSightWithWater (Брезенхем)          :381-383
                  │     ├─ GetRegionPathCached (ConcurrentDictionary) :513
                  │     ├─ PickBestPortal × (regionCount-1)          :414-422
                  │     ├─ GetSegmentCached → ComputeLocalSegment (A* на сегменте) :552, :1208
                  │     ├─ IsDetourAbsurd + SmoothPath               :570-579
                  │     └─ **TryReserveSearch() НЕ ВЫЗЫВАЕТСЯ**      ← см. R-09
                  └─ иначе: FlowFieldManager.CalculateLocalDetourDirection
                        FlowFieldManager.cs:102-208 — локальный BFS 33×33, до 100 шагов,
                        + _targetFlowFieldCache (ConcurrentDictionary) :62
```

**Что дороже и почему (ответ на вопрос задания):**

| Компонент | Дорого? | Почему |
|---|---|---|
| `ExecuteParallel` хендлеров | **Нет** | все хендлеры — тонкая обёртка над `MoveTowards` (`StockpileHaulingJobHandler.cs:116-120`); виртуального диспатча на агента — один `JobRegistry.GetHandler` (`AgentSimulationThread.cs:708`) |
| `MoveTowards` (базовый шаг) | **Да, это база** | на каждого не-idle агента каждый суб-степ: ≥1 `IsTileBlocked`, а тот лезет в `HierarchicalPathfinder.Instance.Cost` — **статическое свойство через `Volatile.Read(ref _state)`** (`HierarchicalPathfinder.cs:71-72`) → 2 разыменования + volatile-барьер на **каждый** вызов; плюс 4 соседа `ctx.SolidWalls/TreeOnGrass/Ground` (2D-массивы). При 10k × ~2 000 суб-степов/с это десятки миллионов вызовов |
| **`HierarchicalPathfinder.TryFindPathScratch`** | **ДА — самый дорогой единичный вызов** | `ComputeLocalSegment` — A* с `Open`-массивом (`HierarchicalPathfinder.cs:211-215`, `LocalCapacity = (3*RegionSize)²`), `GetRegionPathCached`/`GetSegmentCached` — `ConcurrentDictionary` (`:203-204`), `EnsureOverlay` под `lock(_buildLock)` (`:591-697`) при каждом `Invalidate()` (стройка = `_dirty`, `:343`). На 100x с ~1000 запросов/с это доминирует в пиках |
| `FlowFieldManager.CalculateLocalDetourDirection` | Средне | BFS 33×33 = 1089 клеток, `maxSearchSteps = 100` (`FlowFieldManager.cs:135`), `ThreadLocal` буферы (`:35`) — без аллокаций, но 100 шагов × 4 соседа × `IsWalkableCell` (которое **снова** читает `HierarchicalPathfinder.Instance.Cost`, `:215`) |
| **`EnsureOverlay()` при `_dirty`** | **ДА, пиками** | `lock(_buildLock)` (`:593`) — сериализует **все 16 потоков** Phase2; дебаунс есть (`:325-343`), но один тяжёлый rebuild = спайк на всю фазу |

**Уже внесённые оптимизации — проверено по коду (не только «в плане»):**
- ThreadLocal scratch вместо `new int[]` на запрос — `HierarchicalPathfinder.cs:397-400` ✅
- `ConcurrentDictionary` вместо `Dictionary+lock` (был сериализатором 16 потоков) — `:199-204` ✅
- версионирование вместо `Array.Fill(1089)` — `FlowFieldManager.cs:27-33, 112-119` ✅
- хендлеры/`UpdateSingleAgent` — без замеров в горячем пути — `AgentSimulationThread.cs:675-679` ✅
- `ShouldYieldCached(..., 32)` — таймер раз в 32 агента — `:766` ✅
- Idle тайм-слайсинг 25 % на тик — `:687-691` ✅ — **но именно из-за него idle-агенты «стоят», а стоимость ядра приходится на работающих**
- `_state` как immutable-снимок + `Volatile.Read` — `HierarchicalPathfinder.cs:71-74` ✅

**Чего НЕТ (дефект R-09):** бюджет поиска пути объявлен и **не используется** — см. R-09.

### R-05. P1 — `_ghostLayer` никогда не скрывается

`MapRenderer.cs:359-365`: слой создаётся видимым и с `Visible=true` по умолчанию. Призрак рисуется только когда активен инструмент (`BuildTool.PaintGhostPreview` `:52-59`, `ZoneDraftTool.RepaintGhost`), но сам слой всё время в дереве. Клеток в нём обычно 0 → draw calls 0, **но** при отпускании инструмента с незачищенными клетками (баг-предпосылка уже описана в `ZoneDraftTool.cs:45-48`) слой продолжит светиться. Дёшево: `_ghostLayer.Visible = _ghostLayer.GetUsedCells().Count > 0` раз в 10 кадров, либо ставить `Visible=false` в `Cancel()` инструментов.

### R-06. P2 — `FarmZoneRenderer._Draw` рисует прямоугольник на КАЖДЫЙ тайл зоны

`FarmZoneRenderer.cs:75-88`: при `isSelected`/`isHovered` — `DrawRect` на каждую клетку; `:100-106` — `DrawLine` на каждую граничную клетку (4 проверки `HashSet.Contains` на клетку, `:143-153`). Для фермы 500×500 = 250 000 `DrawRect` в одном CanvasItem. Это **1 draw call**, но 250k примитивов на CPU-построение команд + fill-rate. При `RedrawThrottleSec = 0.1f` (`:25`) это происходит 10 раз/с, пока зона выбрана.
**Направление:** заливку зоны рисовать **одной** текстурой `Rect2` на bounding-box зоны с прозрачностью (маска не нужна — зона на экране всё равно прямоугольная для Farm; для Work-диска — оставить, но радиус мал), рамку — по контуру из уже собранного `_borderBuffer`. Убирает O(N) примитивов.

### R-07. P1 — 37.5 % буфера агентов — константы (подробно в §2.1)

`AgentLerpBatch.cs:145-167`, `AgentRenderer.cs:193-194, 528`. Выигрыш: −25 % байт/кадр, 0 изменений визуала.

### R-08. P2 — `TerrainTileLayer.Flush` вызывает `exists()` дважды на клетку

`TerrainTileLayer.cs:54-64`: первый цикл `if (!exists(c)) EraseCell`, второй `if (exists(c)) cells.Add(c)`. Четыре таких `Flush` в `MapRenderer._Process:555-558` → до 8 полных вызовов `IsWallAt`/`IsWallSiteAt`/`IsGardenBed`/`IsPlotMarked` на клетку вместо 4. Эти методы лезут в менеджеры (`StructureManager`, `ConstructionManager`, `FarmJobManager`). **Выигрыш 50 %** от этой работы — замена на один цикл с локальной проверкой.

### R-09. P2 — бюджет поиска пути мёртв

`HierarchicalPathfinder.cs:154` (`SearchesPerFrame = 17`), `:166-177` (`ConfigureBudget`), `:179-196` (`TryReserveSearch`, `_budgetDeferred`). Grep по всему `src/`, `scenes/`, `tests/` → **0 вызовов вне самого класса**. `TryFindPathScratch` (`:486`) и `TryFindPath` (`:356`) бюджет не проверяют. Следствие: при массовой постановке дальних работ (`Main.cs` десятки тысяч агентов) путь запрашивают все сразу — прямые кандидаты в пики 370 мс. **D-08-совместимое исправление:** вызывать `TryReserveSearch()` в начале `TryFindPathScratch`, при отказе — вернуть `false` (агент повторит на следующем суб-степе, кулдаун `PathRequestCooldown` уже есть, `AgentMovementService.cs:28-32`). Функциональность не урезается — только рассрочка.

### R-10. P1 — `IsTileBlocked` вне карты вызывает ПОЛНЫЙ семпл генератора мира, без кэша

`AgentMovementService.cs:402-423`. Если клетка агента вне окна колонии (`(uint)tx >= ctx.MapWidth || (uint)ty >= ctx.MapHeight`, строка `:409`):

```
:411  ulong seed = UnifiedWorldGenerator.ActiveWorldSeed;      // static-чтение (Volatile?)
:414  long wx = WorldOriginX + tx;  long wy = WorldOriginY + ty;
:416  WorldCellQuery.TerrainAt(seed, DefaultGeneratorVersion, wx, wy);
```

а `WorldCellQuery.TerrainAt` (`WorldCellQuery.cs:20-21`) = `Sample(seed, ver, x, y).Terrain`, где `Sample` (`:45-46`) = `UnifiedWorldGenerator.SampleCell(seed, ver, WorldOriginX + x, WorldOriginY + y)` — **без override-климата**, т.е. полный путь `WorldClimateGenerator.SampleBlendedCell` (тектоника/Voronoi/18 климатических полей) на **каждую** проверку. **Кэша нет** (`WorldCellQuery` — чистый `static`-фасад, никаких `Dictionary`).

**Почему это дорого именно сейчас:** агенты **ходят за пределы острова** — это штатная функция (§26/§28: зоны и склады за кромкой, `ZoneDraftTool` с `WorldPlacement`, `pendingWarehouseZone`, `StockpileManager.TryReserveStockpileSlot` ищет клетки в радиусе). Каждый суб-степ для такого агента: `MoveTowards` → 1–5 × `IsTileBlocked` (`:228, 245, 276, 294, 305, 306, 385`) → **1–5 полных семплов генератора**. При даже 300 агентах за кромкой и 2 000 суб-степов/с это `300 × 3 × 2000 = 1.8 млн` семплов/с — при ~1–3 мкс на семпл это **целые секунды CPU в секунду**, распределённые по 16 потокам Phase2.

Именно это правдоподобно объясняет, почему `Phase2_ParallelUpdate` «съедает 620–880 мс/с» и почему пик 370 мс не сводится к A*: **A\* ограничен кэшами (`_regionPathCache`, `_segmentCache`), а семпл генератора — нет.**

**Как проверить (статика):** запрос `viz_cli callers --symbol TerrainAt` и `grep WorldCellQuery.TerrainAt`. **`[требует рантайм-проверки]`**: временно заменить тело ветки `:416` на `return true;` (или залогировать счётчик попаданий в ветку за секунду) и сравнить `Phase2_ParallelUpdate` Σ — это один замер, который отделит вклад pathfinding'а от вклада worldgen-семплов.

**Что делать (D-08: без урезания дальности ходьбы за кромку):**
1. **Кэш уровня клетки** на `StreamingWorldView`: у него уже резидентны чанки и есть `HasBlock(kind, x, y)` (`StreamingWorldView.cs:300-310`) и `_chunkData` (`:282`). Террейн за кромкой **неизменяем** (генератор детерминирован) → достаточно `ConcurrentDictionary<long, byte>` «packed клетка → TerrainKind» с ограничением размера (например 64k записей, `Clear()` по достижению — как уже сделано в `FlowFieldManager.cs:86-87`). Ожидаемо: −99 % семплов.
2. Дополнительно — **раскладка по чанку**: для агента в чанке `C` посчитать один `BulkCellSampler` (`WorldCellQuery.BeginBulkSample`, `:79-80`) на весь чанк 64×64 = 4096 клеток и читать из него. Это уже существующий в проекте API ровно для этого случая.
3. Не менять логику «куда можно ходить» — только источник ответа.

### R-11. P2 — `TickSoilTooltip` считает время как `+= 1.0f/60.0f`

`HUDController.cs:716`: `_soilTipTimer += 1.0f / 60.0f;` вместо `delta`. При 211 FPS подсказка обновляется в 3.5× реже задуманного; при просадке ниже 60 — чаще. Косметика, но ломает «раз в 0.25 с» и делает поведение невоспроизводимым при замерах.

---

## 4. Отсутствующие функции/инструменты (FEAT-Rxx)

| ID | Что | Зачем | Где | Оценка |
|---|---|---|---|---|
| **FEAT-R01** | **Разбивка draw calls по источникам в `PerformanceOverlay`** — новая вкладка: DC всего (уже есть, `:483`), DC по группам (Map / Streaming / Agents / Items / HUD / Overlay), с переключателем «скрывать группу → показать дельту». Поскольку движок не даёт per-CanvasItem счётчик, реализовать через **поочерёдное скрытие** по кнопке («Remove group / measure delta»), а не каждый кадр | Закрывает R1: «откуда 400» перестаёт быть гипотезой | `PerformanceOverlay.cs` (новая вкладка `TabNames`), чтение `Performance.Monitor.RenderTotalDrawCallsInFrame` уже есть | 3–4 ч |
| **FEAT-R02** | **Счётчик draw calls на экране + бюджет кадра с алертом.** mini-режим уже показывает `DC` (`:486`) — добавить порог (например 250) и красную подсветку + запись в `ProfilerLogService.AddMarker` | Регресс по dc ловится мгновенно | `PerformanceOverlay.cs:479-488` | 1 ч |
| **FEAT-R03** | **Оверлей фазовых таймингов** (`TotalStepCycle / Phase2 / Phase3a / Phase3b / Snapshots / Humidity`) поверх игры одной строкой, а не в прокручиваемой панели. `GameProfiler.SnapshotBalance` уже отдаёт `BalanceSnapshot[]` (`GameProfiler.cs:193-201`) — данные готовы | Профиль пользователя перестанет быть «копипастой из консоли» | новый `Node2D`-оверлей 1 draw call | 2 ч |
| **FEAT-R04** | **Тест «нет аллокаций в `_Process`»**: в тесте вызвать кадр и сравнить `GC.GetAllocatedBytesForCurrentThread()` до/после для `MapRenderer`, `AgentRenderer`, `ItemShadowRenderer`, `FarmZoneRenderer`, `StreamingWorldView`. Порог — 0 байт | Ловит скрытый GC-churn, который сейчас не виден | `tests/TestZeroAllocFrame.cs` (новый файл, `tests/` не запрещён) | 3 ч |
| **FEAT-R05** | **Публичный канал наведения для склада**: `StockpileManager.HoveredTile` + `event Action<int,int> OnHoveredTileChanged` (симметрично `ZoneManager.cs:606-616`) | Без этого R-01 не чинится сколько-нибудь чисто | `StockpileManager.cs` | 1 ч |
| **FEAT-R06** | **`IRenderStats`**: единый интерфейс `int EstimatedDrawCalls { get; }` у каждого рендерера (по числу видимых слоёв/квадрантов/инстансов). Оверлей суммирует и сравнивает с `RenderTotalDrawCallsInFrame` — расхождение = невидимый источник | Делает dc наблюдаемой величиной «из кода», без скрытия групп | `src/ui/renderers/IRenderStats.cs` + реализации | 4 ч |
| **FEAT-R07** | **Скриншот-тест подсветки**: кадр «курсор над зоной склада» vs «курсор вне» — ассерт по разнице пикселей в bounding-box | Регресс R2 ловится автоматически | `tests/` (через существующий `godot_run_headless`/CI) | 3 ч |
| **FEAT-R08** | **Включить бюджет поиска пути** (R-09) + метрика `path.deferred` в `GameProfiler` | Пики Phase2 от массовых path-запросов | `HierarchicalPathfinder.cs:486`, `AgentSimulationThread.cs:713` | 2 ч |
| **FEAT-R09** | **Атлас иконок UI** (19 PNG из `hud.tscn` → 1) и **атлас предметов/культур** (Log/Grain/Stone/4 фазы) — генератор атласа в `tools/` | R1: −18 и −7 draw calls | `tools/`, `scenes/ui/hud.tscn`, `CropRenderer.cs`, `*ItemRenderer.cs` | 6 ч |

---

## 5. Граф: риски

### 5.1 Нарушения слоёв и циклы

- **`core → simulation`** (`viz_cli layers`): `src/core/world/mutation/IslandDomainReplay.cs` → `FarmJobManager/StockpileManager/StructureManager/ConstructionManager/StoneJobManager/TreeJobManager` (4/3/2/2/2/2 ссылки); `src/core/world/mutation/IslandStateReplay.cs` → `JobBroker.cs`; **`src/core/common/SimEvents.cs` → `src/simulation/zones/ZoneJobRouter.cs`**. Последнее особенно неприятно: **профайлер (`SimEvents`) знает про зоны**, поэтому любая правка `ZoneJobRouter` дергает ядро профайлинга — и наоборот, `ZoneJobRouter` не может быть протестирован без `SimEvents`.
- **SCC из 62 файлов** вокруг `src/core/world/HumidityMap.cs` (`impact` → `"in_cycle": true, "scc_size": 62`, note: *«файл входит в цикл сильной связности: транзитивный охват завышен»*). Практическое следствие: правка `HumidityMap` транзитивно «задевает» 62 файла, `impact`-анализ не даёт точного радиуса — **для R-02/R-03 нельзя полагаться на `impact`, только на grep конкретных вызовов**.
- `MapRenderer.cs` — `in_cycle: false`, но **`shared_field_writes: 31`, `risk: 4`** (`viz_cli concurrency`), единственный `static_singleton` + 3 `volatile` + 3 `jobsystem`-маркера. Потоки пишут в `_pendingCells`/`_choppedTreesQueue` (`ConcurrentQueue`, `:176, :193`), главный поток читает в `_Process`. **Инвариант держится только на «пишут из событий, читает главный поток» — ничем не проверяется.** Любая правка R-02 должна сохранить `ConcurrentQueue` (не заменять на `List` + `lock`).

### 5.2 Слепые пятна графа (важно для планирования)

`viz_cli impact` вернул **`direct: []`, `files_count: 0`** для:
- `src/ui/PerformanceOverlay.cs` — создаётся `new PerformanceOverlay` в `Main.cs:259`;
- `src/ui/tools/WarehouseTool.cs` — **вообще не создаётся** (мёртвый код);
- `src/ui/renderers/FarmZoneRenderer.cs` — создаётся `new FarmZoneRenderer` в `Main.cs:239`.

Т.е. граф не видит рёбер через `new T {}` для этих классов. **Вывод для процессов:** `impact` нельзя использовать как единственное доказательство «кто вызывает» — обязателен второй проход grep'ом. `callers` в этом смысле надёжнее (нашёл `SelectTool.cs:31` и `PlayerInteractionManager.cs:109`).

### 5.3 Прочие риски для работ R-track

| Риск | Детали |
|---|---|
| **Слияние TileMapLayer ломает z-order** | `_stockpileLayer` ZIndex по умолчанию, `StockpileItemRenderer` Z=6, `FarmZoneRenderer` Z=8 (`FarmZoneRenderer.cs:35`), `DesignationRenderer` Z=15, `SelectionBox` Z=100, `ShadowCasterRenderer` Z=4, `ItemShadowRenderer` Z=3, `AgentShadowInstance` Z=-1. Слияние слоёв (**R1**) обязано сохранить этот порядок, иначе «предметы под фиолетовой заливкой» (комментарий `StockpileItemRenderer.cs:11-13`) — регресс |
| **`DisableMasks`/`Visible=false` как инструмент замера** | Для FEAT-R01 «скрыть группу и померить дельту» нужно быть уверенным, что `Visible=false` на TileMapLayer действительно снимает его calls, а не только пропускает отрисовку — **`[требует рантайм-проверки]`** |
| **Правка `AgentRenderer` затрагивает 2 шейдера** | `AgentInterpolation.gdshader` читает `COLOR`; `AgentShadowInterpolated.gdshader` — общий MultiMesh (`_shadowSharesBodyMultiMesh`, `:219-221`). Любая смена формата буфера (R-07) обязана править **оба** шейдера синхронно, иначе тени разъедутся с телами (это уже случалось: комментарий `:21-30`) |
| **Тестовое покрытие** | `coverage: 67.1 %`; **не покрыты** `scenes/main/Main.cs` и `src/simulation/simd/SimdNeedsBatch.cs`. Рендереры тестируются только `tests/TestRenderLodBenchmark.cs` (единственный тест, трогающий `AgentRenderer`) — регресс по dc/подсветке автоматикой **не поймается** без FEAT-R07 |

---

## 6. Оценка работ и порядок

Порядок: сначала «дёшево и без риска», затем «дорого, но с большим выигрышем». Все меры D-08-совместимы — функциональность не урезается.

| # | Работа | Дефект/FEAT | Часы | Ожидаемый эффект (оценка из кода) | Риск |
|---|---|---|---|---|---|
| 1 | **Подсветка склада**: `StockpileManager.HoveredTile` + событие + `SelectTool` hook + `StockpileHoverRenderer` (1 draw call) | R-01, FEAT-R05 | **3** | R2 закрыт; +1 draw call | низкий |
| 2 | `WarehouseTool` — удалить или пометить `[Obsolete]` (мёртвый код вводит в заблуждение при отладке R2) | R-01 | 0.5 | — | низкий |
| 3 | Убрать 64-битное деление в `HumidityMap.Tick:306` | R-03 (1) | **1** | −7…−13 мс пика `Simulation.Humidity` | минимальный |
| 4 | `TerrainTileLayer.Flush` — один проход по `_dirty` вместо двух | R-08 | **1** | −50 % работы 4 flush'ей в `MapRenderer._Process` | минимальный |
| 5 | `MakeLayer`/`TileSet`-атлас: слияние плановых слоёв (`_farmBlueprintLayer`+`_blueprintLayer`+`_wallBlueprintLayer` → 1) | R1 | 8 | **−128** draw calls (2 из 3 слоёв) | средний (z-order) |
| 6 | `StreamingWorldView`: прозрачность в атлас → `_wallPlanLayer`+`_farmPlanLayer`+`_planLayer` в свои базовые слои | R1 | 6 | **−3 слоя** | средний |
| 7 | Атлас иконок HUD (19 PNG → 1) + атлас предметов (Log/Grain/Stone) + атлас фаз культур | R1, FEAT-R09 | **6** | **−18** (HUD) **−7** (MultiMesh) | низкий |
| 8 | `AgentRenderer`: 16 → 12 float (поворот в базис матрицы) + порог «смещение > 0.5 px» для upload | R-07 | **5** | Σ 19–46 → 8–20 мс/с; пик 7.5 → ≤4 мс | средний (2 шейдера) |
| 9 | `MapRenderer._Process`: бюджет по **времени** вместо `16384` клеток; приоритет видимым клеткам | R-02 | **4** | убирает спайк 2–10 мс при клике разметки | низкий |
| 10 | `HierarchicalPathfinder`: включить `TryReserveSearch` | R-09, FEAT-R08 | **2** | сглаживает пики Phase2 | средний (поведение поиска) |
| 11 | `HumidityMap.Tick` — амортизация по полосам + маска воды (`bool[]`) вместо `TileType[,]` | R-03 (2,3) | **5** | 39–70 мс → 5–9 мс | низкий |
| 12 | `RenderingQuadrantSize` — бисекция и фикс значения | R1 | 2 (после замера) | **`[требует рантайм-проверки]`**; ожидаемо −15…−25 % | средний |
| 13 | `FarmZoneRenderer`: заливка одним Rect2 по bbox вместо `DrawRect` на тайл | R-06 | 2 | убирает 250k примитивов при выбранной большой зоне | низкий |
| 14 | `_ghostLayer.Visible` по факту содержимого | R-05 | 0.5 | — | низкий |
| 15 | **FEAT-R01**: вкладка «Draw calls по группам» с ручным скрытием группы | R1 | 4 | делает R1 измеримым | низкий |
| 16 | **FEAT-R04**: тест «0 аллокаций в `_Process`» | профиль | 3 | ловит churn | низкий |
| 17 | **FEAT-R07**: скриншот-тест подсветки | R2 | 3 | регресс-защита | средний |
| 18 | `PerformanceOverlay`: убрать построение `camTag`/строк при `!_isVisible` (перенести `:476` выше `:452`) | — | 0.5 | −1…−10 КБ/с | минимальный |
| 20 | **Кэш террейна вне карты** для `IsTileBlocked`: `ConcurrentDictionary<long,byte>` или `BulkCellSampler` на чанк | R-10 | **4** | убирает миллионы полных семплов генератора/с из Phase2 — главный кандидат на «съедает 620–880 мс/с» | низкий |
| 21 | `HUDController.TickSoilTooltip`: `delta` вместо `1/60` | R-11 | 0.2 | корректность | минимальный |

**Итого:** ~**59 ч**. Критический путь до «R1 понятен и снижен, R2 закрыт»: пункты **1, 2, 3, 4, 5, 7, 15** = **~20 ч**. Отдельно, **вне зависимости от draw calls**: пункт **20** (кэш террейна вне карты, R-10) — самый дешёвый способ снять основную часть `Phase2_ParallelUpdate` и первый кандидат на проверку гипотезы «620–880 мс/с — это worldgen-семплы, а не A*».

**Что обязательно перед пунктами 5–7 и 12 — один замер `[требует рантайм-проверки]`:**
> В `PerformanceOverlay` уже есть `Performance.Monitor.RenderTotalDrawCallsInFrame` (`:483`). Нужен прогон: камера в фиксированной точке, зум 1.0, поочерёдное `Visible=false` для 19 слоёв + `CanvasLayer/hud_tscn` — таблица «источник → draw calls». Без этого таблица §1.2 остаётся оценкой, а эффект пунктов 5–7, 12 — прогнозом.

### 6.1 Сводка: какие рантайм-замеры нужны (и ни один не сделан в этом отчёте)

Все замеры ниже — **не выполнены**; они перечислены как план. Инструмент для большинства уже есть: `Performance.Monitor.*` уже читается в `PerformanceOverlay.cs:483, 508-511`.

| # | Замер | Что даёт | Приоритет |
|---|---|---|---|
| M1 | `Performance.Monitor.RenderTotalDrawCallsInFrame` (уже читается, `:483`) при **фиксированной камере** (позиция+зум 1.0), затем поочерёдное `Visible=false` по 19 слоям + `CanvasLayer/hud_tscn` | превращает таблицу §1.2 из оценки в факт: «слой → его calls» | **P0** для R1 |
| M2 | То же при `rendering_quadrant_size ∈ {16,32,64,128,256}`, всё остальное неизменно | подтверждает/опровергает гипотезу §1.3 | **P0** для R1 |
| M3 | `RenderTotalDrawCallsInFrame` при зуме 0.2 / 0.5 / 1.0 / 2.0 | показывает, растут ли calls от зума (квадранты) или нет | P1 |
| M4 | `Simulation.Phase2_ParallelUpdate` Σ с временно нейтрализованной веткой `AgentMovementService.cs:416` (`return true`) | отделяет вклад worldgen-семплов (R-10) от A* | **P0** для R3 |
| M5 | `Simulation.Humidity` после замены `% 100000` на 32-битный хеш (`HumidityMap.cs:306`) | подтверждает оценку −7…−13 мс | P0 для R3 |
| M6 | `Simulation.Humidity` после полосовой амортизации (1/8 карты за тик) | 39–70 мс → ~5–9 мс | P1 |
| M7 | `render/AgentRenderer.bodies_gpu_upload` (уже пишется в `SimEvents`, `AgentRenderer.cs:533`) до/после перехода 16→12 float | подтверждает −25 % и пик ≤4 мс | P1 |
| M8 | Счётчик `TryReserveSearch`-отказов (`_budgetDeferred`, `HierarchicalPathfinder.cs:164`) после включения бюджета (R-09) | сколько path-запросов реально откладывается | P2 |
| M9 | `GC.GetAllocatedBytesForCurrentThread()` вокруг одного кадра (FEAT-R04) | 0 аллокаций в `_Process` или список нарушителей | P1 |
| M10 | `RenderTotalDrawCallsInFrame` при скрытом `CanvasLayer/hud_tscn` | вклад HUD (~35–45 по оценке §1.2) | P1 |
| M11 | Скриншот «курсор над клеткой склада» vs «вне» (R-01) | доказательство дефекта и приёмка фикса | P0 для R2 |

**Чего в этом плане сознательно НЕТ:**
- GPU-влажность — отключена намеренно и с обоснованием (`HumidityMap.cs:252-255`).
- GPU-шаг агентов — отключён из-за двойного движения (`AgentSimulationThread.cs:724-726`).
- Урезание числа агентов, дальности путей, числа слоёв «до нуля» — запрещено D-08 (`docs/DECISIONS.md`, раздел D-08: *«Сокращать дальности, количество агентов или возможности — запрещено»*).