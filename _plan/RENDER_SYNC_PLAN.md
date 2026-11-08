# ПЛАН: синхронизация рендера игровой области и стримингового мира

**Проект:** Godot 4.7 / C# / .NET 8, колония-сим 10 000 агентов.
**Рабочее дерево:** `D:\bebebe-main (1)\bebebe-main` (родительский каталог — «свалка» дублей и бэкапов, не читать).
**Дата:** 2026-10-25. **Статус: ПЛАН. Код не менялся.**
**Тема:** устранение двойного рендера земли «остров (MapRenderer + шейдер) vs стриминг (StreamingWorldView + тайлы)» и доведение миграции «один мир / один рендер» до конца.

---

## 0. TL;DR

1. **Сборка сейчас КРАСНАЯ: 21 ошибка** (`dotnet build Game.csproj`). Рабочее дерево — недостроенная миграция: новое поколение файлов (`WorldModel.cs`, `WorldCostCache.cs`, `WorldPathfinder.cs`, `SimulationWatchService.cs`, `src/tools/CellInspector.cs`, 10 новых тестов + `.gd`-раннеры) описывает **целевую архитектуру**, но код-реализация отстала: тесты ссылаются на API, которых ещё нет (`WorldCellQuery.SampleLatticeAbsolute`, `MapRenderer.SurfaceCode`, `UnifiedWorldGenerator.SmoothStepQ16` и др.).
2. **Цель однозначно зафиксирована самими тестами** (тесты = спецификация): «один рендер земли — шейдер `terrain_blend` рисует весь мир, стриминг кладёт только декор с прозрачным фоном» (`tests/TestStreamingWaterLayer.cs:13-14`), единая дверь к клетке `WorldModel`, никаких прямых вызовов генератора из ui/simulation, никаких магических `1024`/512-клампов.
3. **Документация опережает код**: `docs/ARCHITECTURE.md` (2026-10-25) описывает §120.7–§120.9 как закрытые (помпа `Main.PumpWorldPlans`, `WorldBlueprintsSupported=true`, клампы убраны) — **в этом дереве их нет**: `Main.cs` не содержит помпу, в `JobSpatialPyramid.cs:188-198` клампы на месте, гейт-константы удалены из `WorldPlacementPass.cs`, но `CellInspector.cs:72-73` на них всё ещё ссылается (ошибка сборки). `PLAN.md` заканчивается §119. `bebebe-main.zip` — более **старый** снимок (StreamingWorldView 60 КБ против 82 КБ), готовой работы там нет.
4. **Часть работы уже потеряна один раз**: `_reports/02_coastline_impl.md` отчитывается «реализовано, 0 ошибок» про `SmoothStepQ16` — в текущем дереве метода **нет** (ошибка CS0117). Восстанавливать по отчёту, где есть побитовый рецепт.
5. **План = 6 фаз**: 0 — зелёная сборка (блокер), 1 — единая дверь данных, 2 — один рендер земли (ядро темы), 3 — шов на 1024 и паритет, 4 — исполнитель мира (помпа/мост), 5 — храповик и доки. Оценка ≈ 14–22 чел-дня.

---

## 1. Метод

1. Прочитаны .md: `_plan/00_MASTER_PLAN.md`, `_plan/W|J|R|D_*.md`, `_plan/BUGS_REGISTRY.md`, `_plan/MISSING_FEATURES.md`, `docs/ARCHITECTURE.md`, `docs/DECISIONS.md` (через D-отчёт), `_reports/01..06`, `dependency_graph_visualizer/AI_README.md`, `AGENTS.md`.
2. **Графовый визуализатор (обязательный)** — `viz_cli.py`, все команды с `--repo` (регламент §7 мастер-плана): `stats`, `impact` ×2, `cycles`, `concurrency`, `plandrift`. Результаты — §4.
3. Прямое чтение кода: `WorldPlacementPass.cs`, `WorldModel.cs`, `MapRenderer.cs`, `StreamingWorldView.cs`, `JobSpatialPyramid.cs`, `Main.cs` (grep), 8 новых тестов.
4. `dotnet build Game.csproj` — фактическое состояние сборки.
5. `git status` — дерево содержит только **незакоммиченные новые** файлы (`??`), ни одного модифицированного tracked-файла: миграция не сохранена в git ни одним коммитом.

---

## 2. Состояние на 2026-10-25 (все факты проверены)

### 2.1 Сборка красная — 21 ошибка (сгруппированы)

| # | Отсутствующий API | Кто ждёт | Ошибка |
|---|---|---|---|
| A | `WorldCellQuery.BeginBulkSample` (9 аргументов) | `WorldModel.cs:117` | CS1501 |
| B | `WorldCellQuery.SampleLatticeAbsolute` | `WorldModel.cs:177, 206` | CS0117 ×2 |
| C | `MapRenderer.SurfaceCode` | `TestTerrainSurfaceCode.cs:60`, `TestTerrainWindowFill.cs:226, 247` | CS0117 ×3 |
| D | `UnifiedWorldGenerator.SmoothStepQ16` | `TestCoastPrecision.cs:111`, `TestTerrainWindowFill.cs:108` | CS0117 ×2 |
| E | `WorldGenV2.DecorPass` — `private` | `CellInspector.cs:62, 173`, `TestStreamingWaterLayer.cs:200` | CS0122 ×3 |
| F | `AgentMovementService.TryUnpackWorldWaypoints` | `TestWorldPathfinder.cs:189` | CS0117 |
| G | `StockpileManager.ZoneTileCount` | `SimulationWatchService.cs:284` | CS1061 |
| H | `PerformanceOverlay.PlainCellCard` | `TestTerrainWindowFill.cs:191, 192` | CS1061 ×2 |
| I | `JobSpatialPyramid.GetChunkCoord/CoveredTiles` | `CellInspector.cs:65, 68` | CS0117 ×3 |
| J | `WorldPlacementPass.WorldBlueprintsSupported / WorldBuildingBlueprintsSupported` (константы удалены при снятии гейта) | `CellInspector.cs:72, 73, 182` | CS0117 ×3 |

Это не «поломка» в обычном смысле: это **TDD-миграция, брошенная на середине** — тесты и фасады написаны, реализации нет.

### 2.2 Два рендерера земли — всё ещё в коде (симптом W-02 жив)

- `MapRenderer._groundSurface` — ColorRect 512×512 тайлов + `terrain_blend.gdshader`, z=−2, follow-окно за камерой (`MapRenderer.cs:251-269, 575-583`); вне острова окно заполняется решёткой климата 16×16 + билиней (`FillTerrainWindowExternal`, `MapRenderer.cs:809-880`), клетки острова — из `mapData` (`:875-878`). Комментарий `:619-620` уже объявляет «землю всего мира рисует _groundSurface» — но это цель, не факт.
- `StreamingWorldView._groundLayer` — TileMapLayer «WorldGroundAndDecor», z=−1 (`StreamingWorldView.cs:1285`), `PaintCell` **стирает** базовую землю и кладёт тайл только декору (`:1865-1874`) — то есть в дереве сейчас обратная (06_streaming_paint, «фикс от 2026-10-21») и новая (TestStreamingWaterLayer, «один шейдер») стратегии **склеены в одну**: земля за кромкой сейчас НЕ рисуется никем, кроме шейдерного окна. `_decorLayer` объявлен (`:56`), но **не создаётся** — миграция на декор-слой не начата.
- Гейт «остров не трогаем» в `PaintCell` (`:1860`) считает границу через `MapRenderer.MapWidth` (магический дубль), а не `WorldModel.IsInsideIsland`.

### 2.3 Новое ядро мира — написано, но не подключено

| Файл | Статус | Что не хватает |
|---|---|---|
| `src/core/world/WorldModel.cs` (248 стр.) | есть, не компилируется | `SampleLatticeAbsolute`/`BeginBulkSample` в `WorldCellQuery`; потребителей в ui/sim пока нет |
| `src/core/world/WorldCostCache.cs` | есть | интеграция: `MarkBlocked/Unblocked` никто не зовёт (симк стен) |
| `src/simulation/pathfinding/WorldPathfinder.cs` | есть | мост в `AgentMovementService` (`TryUnpackWorldWaypoints`) не написан |
| `src/core/common/SimulationWatchService.cs` | есть | `StockpileManager.ZoneTileCount` |
| `src/ui/streaming/WorldJobBoard.cs` | есть (давно) | инстанцируется только тестом — исполнитель по-прежнему не подключён |
| `src/tools/CellInspector.cs` | есть | пишет под снятый гейт и несуществующие API |

### 2.4 Гейт строительства снят, исполнитель не поднят

`WorldPlacementPass.cs` (86 строк) больше **не содержит** `WorldBlueprintsSupported` — гейт «вне активной области строительство недоступно» убран; `StreamingToolPlacement.TryPlaceWall/CanPlaceWall` (`:51-56`) пишут/проверяют план без блокировки. Но **помпы планов нет** (`Main.cs`: grep `Pump|WorldPlan|WorldJobBoard` — пусто) ⇒ сейчас за кромкой чертёж можно **поставить**, и он будет висеть в чанк-сторе вечно, без работы и без объяснения. Это регресс относительно честного отказа (W-05/J-06: было «нельзя, нет исполнителя», стало «можно, но мёртвое») — закрывается фазой 4.

### 2.5 Дрейф документации (plandrift: 82/273 ссылок, 30 %)

- `PLAN.md` — последний раздел §119; §120, на который ссылается `ARCHITECTURE.md`, **в файле отсутствует**.
- `docs/ARCHITECTURE.md` описывает целевое/другое дерево: «клампов НЕТ с §120.7» — факт: `JobSpatialPyramid.cs:188-198` клампы есть; «magic_1024 = 0» — факт: 12 вхождений `\b1024\b` в src (реальное из них — `MapRenderer.cs:33`); baseline `tests/arch_baseline.json` (все нули) **не соответствует** дереву — `run_architecture_guards` при текущем коде упадёт.
- `_reports/02_coastline_impl.md` — «реализовано» про `SmoothStepQ16`, которого в дереве нет (потеря/откат).
- Мастер-план `_plan/00_MASTER_PLAN.md` (2026-10-23), волна 4, шаг 4.1 предписывает стратегию `IGroundRenderer.Handles(cell)` + скрывать шейдер там, где рисует стриминг — **устарела**: выбрана противоположная и более сильная стратегия «шейдер рисует всё, стриминг — только декор». План ниже заменяет шаги 4.1–4.2 мастер-плана.

---

## 3. Целевая архитектура (фиксация по тестам-спецификациям)

| Тест | Требует |
|---|---|
| `tests/TestStreamingWaterLayer.cs` | Землю во всём мире рисует шейдер; у `StreamingWorldView` **нет** полей `_groundLayer`/`_waterLayer` (`:121-125`); есть `_decorLayer`; атлас декора — прозрачный фон (≥99 % пикселей), спрайты нарисованы (`:92-105`); состав декора = формула `WorldGenV2.DecorPass` ровно (`:142-179`) |
| `tests/TestTerrainSurfaceCode.cs` | `MapRenderer.SurfaceCode(terrain, biome)` → 6 различимых кодов 0..5; вода == 2, гора == 5; никакого схлопывания степи/ледника в траву (`:17-18, 60-67`) |
| `tests/TestIslandWorldAgreement.cs` | Кэш острова (`MapData` из `GenerateColonyWindow`) == `WorldModel.TerrainAt` побитово по сетке шага 2 (`:10-17, 52-60`) — «два рендерера рисовали ОДИН мир» ⇒ объединение безопасно |
| `tests/TestCoastPrecision.cs` | Диагностика «за кромкой нет песка / ровный берег»: решётка 16×16 против прямого сэмпла в полосе 990..1080 (`:45-60`) |
| `tests/TestTerrainFollow.cs` | Живой прогон follow-окна: центр острова → за кромку (юг), дамп `_terrainBuffer` — «почему за кромкой плоская зелень» (`:10-14`) |
| `tests/TestTerrainWindowFill.cs` | Заполнение окна кодами поверхности + карта `PerformanceOverlay.PlainCellCard` |
| `tests/TestWorldPathfinder.cs` / `TestWorldCostCache.cs` | Мост движения и стоимости за кромкой (обход берега, пути 1024–4096, short-вейпоинты) |
| `tests/TestArchitectureGuards.cs` + `arch_baseline.json` | Конституция: `magic_1024`, `coverage_512` (сигнатура `ChunkShift=4`+`ChunkDim=32`), `is_island_cell_defs`, `decor_pass_defs`, `core_to_ui`, `bypass_generator_sim`, `bypass_generator_ui` — храповик: падает, если нарушений стало больше (`:30-62`) |

Правила цели (из `WorldModel.cs:9-31` и гардов): клетку спрашивают **только** через `WorldModel`; seed/version принадлежат миру; остров = `WorldModel.IslandTiles = 1<<10` (единственное определение); мутации — оверлей поверх базовой клетки; прямые вызовы `WorldCellQuery`/`UnifiedWorldGenerator.SampleCell` из ui/simulation — запрещены.

---

## 4. Графовый анализ (viz_cli, --repo «D:/bebebe-main (1)/bebebe-main»)

### 4.1 stats

`files: 327` (253 `.cs` / 63 `.gd` / 11 `.tscn`), `symbols: 2207`, `edges: 763`, `methods: 2385`, `errors: 0`.
(Мастер-план от 23.10 видел 301 файл / 752 ребра — код вырос на 26 файлов, вся дельта — незакоммиченная миграция.)
`top_fan_out`: `AgentSimulationThread.cs` 32 · `Main.cs` 26 · **`StreamingWorldView.cs` 14** · **`MapRenderer.cs` 13** · `HUDController.cs` 13 · `ZoneDraftTool.cs` 11.
`top_fan_in`: `DynamicWorkScheduler` 27 · `UnifiedWorldGenerator` 24 · `ConstructionManager` 22 · `JobDispatcher`/`ActiveArea` 20 · `WorldCoordinates` 19.

### 4.2 impact (blast radius)

| Файл | direct | итого | цикл | Прямые потребители |
|---|---|---|---|---|
| `src/ui/renderers/MapRenderer.cs` | 10 | 23 (глубина 3; ui 8 / scenes 4 / tests 9) | **нет** | `Main.cs`, `HUDController`, `MainMenuController`, `StreamingWorldView`, `BuildTool`, `ZoneDraftTool`, 4 теста |
| `src/ui/streaming/StreamingWorldView.cs` | 8 | — (охват завышен) | **ДА, SCC=3** (`Main.cs` ↔ `IWallWorld` ↔ `WorldJobBoard`) | `Main.cs`, `IWallWorld`, `CellInspector`, `WorldJobBoard`, 4 теста |

**Следствия для плана:**
- Правки `MapRenderer` (SurfaceCode, формат `terrain_map`) безопаснее по связности, но это **risk 4 по concurrency** (`concurrency`: 31 запись в общие поля, 3 `volatile`, `Task.Run` + `Parallel.For`, обмен front/back-буферов) — фаза 2 обязана сохранить инвариант «front-буфер не пишется из фонового потока».
- Правки `StreamingWorldView` тянут SCC-цикл из 3 файлов: `WorldJobBoard` и `IWallWorld` трогать в той же фазе нельзя — иначе цикл разрастётся. Подключение `WorldJobBoard` (фаза 4) делать отдельным шагом с `viz_cli impact` до/после.
- `cycles --scope src/ui`: единственный цикл `c1` (4): `PlayerInteractionManager` ↔ `BuildMenuController` ↔ `HUDController` ↔ `SelectTool` — в миграцию не входит, не трогать.

### 4.3 plandrift

`refs_total: 273, drift: 82` (30 %), все `missing_file` — цифра не изменилась с 23.10. Ключевое для этого плана: `PLAN.md` не содержит §120, на который ссылается `ARCHITECTURE.md` ⇒ целевое состояние миграции описано **только** в `ARCHITECTURE.md` + тестах, и там же «выполненное» смешано с «запланированным» (фаза 5 чинит).

### 4.4 Регламент воспроизведения

```powershell
cd "D:\bebebe-main (1)\bebebe-main"
$env:PYTHONUTF8="1"; $r="D:/bebebe-main (1)/bebebe-main"
python dependency_graph_visualizer\viz_cli.py stats    --repo $r --pretty
python dependency_graph_visualizer\viz_cli.py impact   --repo $r --file src/ui/renderers/MapRenderer.cs --pretty
python dependency_graph_visualizer\viz_cli.py impact   --repo $r --file src/ui/streaming/StreamingWorldView.cs --pretty
python dependency_graph_visualizer\viz_cli.py cycles   --repo $r --scope src/ui --pretty
python dependency_graph_visualizer\viz_cli.py concurrency --repo $r
python dependency_graph_visualizer\viz_cli.py plandrift --repo $r
```
После каждой фазы, менявшей рёбра, — прогон заново (кэш по mtime инвалидируется сам; смена схемы — `--rebuild`).

---

## 5. План работ: 6 фаз

> Принцип: **сначала зелёная сборка → единые данные → один рендер → шов → исполнитель → храповик**. Каждая фаза заканчивается коммитом (см. риск R4 — работа уже терялась).

### Фаза 0 · «Зелёная сборка» — блокер, ≈1 день

Цель: `dotnet build Game.csproj` → 0 ошибок, ничего не выключая (добить API, а не выкинуть новые файлы: они и есть спецификация).

| # | Задача | Файлы | Примечание |
|---|---|---|---|
| 0.1 | `WorldCellQuery.BeginBulkSample(seed, version, originLocalX, originLocalY, w, h, worldOriginX, worldOriginY, gridOptions)` + `SampleLatticeAbsolute(seed, version, x, y)` с кэшем решётки | `src/core/world/streaming/integration/WorldCellQuery.cs` | сигнатуры уже написаны вызывающей стороной (`WorldModel.cs:113-119, 177`); форма = решётка 16×16, побитово равная `GenerateColonyWindow`/чанкам |
| 0.2 | Восстановить `SmoothStepQ16` + `LerpClimate1D` на `ts` | `src/core/world/generation/UnifiedWorldGenerator.cs` | рецепт побитово в `_reports/02_coastline_impl.md` §2.1 (работа была сделана и потеряна) |
| 0.3 | `MapRenderer.SurfaceCode(BaseTerrainKind, BiomeType)` → `byte`, 6 кодов | `src/ui/renderers/MapRenderer.cs` | контракт: `TestTerrainSurfaceCode.cs:60-67` — вода==2, гора==5, все 6 различимы |
| 0.4 | `WorldGenV2.DecorPass` → `internal`→`public` (или обёртка `WorldModel.DecorAt`) | `WorldGenV2.cs` | зовут `CellInspector` и `TestStreamingWaterLayer` |
| 0.5 | `AgentMovementService.TryUnpackWorldWaypoints` | `src/simulation/agents/AgentMovementService.cs` | декомпрессия world-вейпоинтов (мост фазы 4) |
| 0.6 | `StockpileManager.ZoneTileCount`, `PerformanceOverlay.PlainCellCard` | соотв. файлы | мелкие API наблюдаемости |
| 0.7 | `JobSpatialPyramid.GetChunkCoord/CoveredTiles` **или** переписать `CellInspector` под фактический API | `JobSpatialPyramid.cs` / `src/tools/CellInspector.cs` | предпочтительно второе: не раздувать публичный API пирамиды |
| 0.8 | `CellInspector`: убрать ссылки на снятый гейт `WorldBlueprintsSupported` — проверять фактическое (помпа/исполнитель, появится в фазе 4) | `src/tools/CellInspector.cs:72-82, 182` | гейта больше нет — диагностика должна не врать |

**Приёмка:** `dotnet build` 0 ошибок; предупреждения не выросли относительно текущих 9.

### Фаза 1 · «Единая дверь данных» — ≈2–3 дня

| # | Задача | Файлы | Приёмка |
|---|---|---|---|
| 1.1 | `FillTerrainWindowExternal` перевести на `WorldModel.BeginBulkSample` (сейчас — прямой вызов `WorldClimateGenerator.SampleBlendedCell`, `MapRenderer.cs:848` — нарушение будущего гарда `bypass_generator_ui`) | `MapRenderer.cs:809-880` | гард `bypass_generator_ui` по grep `WorldCellQuery\.|UnifiedWorldGenerator\.SampleCell` в `src/ui` = 0 |
| 1.2 | Регистрация расчистки спавна: `WorldModel.RegisterClearingSite` при генерации острова/`FindHabitableSpawn` (иначе решётка говорит «вода», рендер острова «трава» — источник шва) | `Main.cs` (генерация региона), `WorldModel.cs:51-81` | `TestIslandWorldAgreement` — 0 расхождений `MapData` vs `WorldModel` |
| 1.3 | Прогнать `run_generator_version_parity` | — | зелёный: seed/version совпадают во всех точках входа |
| 1.4 | Baseline арх-гардов **по факту**: снять текущие числа (`magic_1024`≈12, `coverage_512`≥1, `is_island_cell_defs`, `decor_pass_defs`), записать в `arch_baseline.json` | `tests/arch_baseline.json` | `run_architecture_guards` зелёный; дальше планки только снижаются |

### Фаза 2 · «Один рендер земли» — ядро темы, ≈3–5 дней

> Правило: один писатель в `StreamingWorldView.cs`; `WorldJobBoard`/`IWallWorld` в этой фазе не трогать (SCC, §4.2).

| # | Задача | Файлы | Приёмка |
|---|---|---|---|
| 2.1 | `terrain_blend.gdshader`: 6 кодов `terrain_map` (0 трава / 1 песок / 2 вода / 3 степь / 4 ледник / 5 гора); вода и береговая функция — на коде 2/береге; formerly-3-кодовый ветвинг заменить на switch по коду | `src/shaders/terrain_blend.gdshader` | `TestTerrainSurfaceCode` зелёный; скриншот кромки берега |
| 2.2 | Островной путь окна: `TerrainByteFromMapData` → `SurfaceCode`; всё окно пишет коды 0..5 (R8-байты, формат совместим) | `MapRenderer.cs:875-878` + fill | байты окна == `WorldModel.TerrainAt` в границах окна (замер в тесте) |
| 2.3 | `StreamingWorldView`: удалить `_groundLayer` (создание `:1285`, покраска `:1865-1874`), **создать** `_decorLayer` (поле уже объявлено `:56`); `PaintCell` кладёт только декор; атлас — прозрачный фон (дерево/камень без «вклеенной» земли) | `src/ui/streaming/StreamingWorldView.cs` | `run_streaming_water_layer` → Code 0: полей `_groundLayer`/`_waterLayer` нет, атлас ≥99 % прозрачных, слой = формула декора |
| 2.4 | Гейт острова в `PaintCell` (`:1860`) → `WorldModel.IsInsideIsland` (декор острова остаётся за `MapRenderer._objectLayer` — двойного декора нет) | там же | гард `is_island_cell_defs` не вырос |
| 2.5 | `IsOccludedByColony`/планировщик чанков: островные чанки не грузить ради земли; `MarkTerrainCellRing` (`:1795`) согласовать с шейдер-окном (кто инвалидирует окно при мутациях) | там же | панк за кромкой не меняет видимой земли (шейдер непрерывен) |
| 2.6 | `streaming_ground.gdshader` — вывести из эксплуатации (вода/земля тайлами больше не рисуются) | `src/shaders/streaming_ground.gdshader` | ссылок из C# нет (`grep`) |
| 2.7 | Проверка «окно 512² ≥ видимая область»: на минимальном зуме 0.12 и 4K-экране нужно ~500 тайлов по ширине — впритык; при нехватке — зум-гейт или рост окна | `MapRenderer.cs:43, 575-583` | `TestTerrainFollow`: за кромкой буфер ненулевой по всей видимой области |
| 2.8 | Инвариант потоков: формат `terrain_map` 0..5 меняется при живом `Task.Run`+`Parallel.For` — front-буфер не пишется из фона (risk 4) | `MapRenderer.cs:642-777` | `TestTerrainWindowFill`/`TerrainWindowBench` зелёные; гонок нет (замер спайков) |

**Приёмка фазы (гейт):** шов на 1024 на скриншоте отсутствует или классифицирован остаточным (фаза 3); вода непрерывна по обе стороны кромки; draw calls по земле = 1 (ColorRect) + декор-квадранты.

### Фаза 3 · «Шов 1024 и паритет» — ≈2–3 дня

| # | Задача | Приёмка |
|---|---|---|
| 3.1 | Тест шва на фактической границе: байты окна (SurfaceCode) vs `GeneratedCell.Terrain` по обе стороны 1024; паритет декора в полосе 1020..1028; обновить `test_streamingview_decor_seam.cs:112` и ещё 4 теста, закреплённых на 512 (реестр W-06) | зелёные; падают при откате фазы 2 |
| 3.2 | `TestCoastPrecision`: «плоская зелень/нет песка за кромкой» — после 0.2 (smoothstep) перегнать; при остаточных артефактах — прямой сэмпл в береговой полосе ±N клеток | песок за кромкой на скриншоте; числа «решётка vs прямой» в отчёте |
| 3.3 | W-01 «деревья на воде»: согласовать маску декора и ватерлинию шейдера (запрет декора в полосе `\|t\| < 0.12` у воды или сдвиг спрайта) — формула DecorPass теперь одна, правка локальна | zoom-in берега внутри и за 1024: деревья на суше |
| 3.4 | W-04 «зоны выглядят по-разному»: единый слой маркеров зон и один предикат `CanPlaceZone` (остров `ZoneDraftTool.cs:599` vs стриминг `WorldGameRules`) | зона в обеих областях выглядит одинаково |
| 3.5 | Остаточный 1-px шов между чанками (долг из `_reports/06_streaming_paint.md`) — теперь вопрос только декор-слоя | вертикальная линия на стыке чанков исчезает |

### Фаза 4 · «Исполнитель мира» — ≈5–8 дней (параллельна фазам 2–3 по файлам, но `Main.cs` сериализован с фазой 2!)

Цель: довести до состояния, описанного в `ARCHITECTURE.md` §3–§6 (рецепт уже согласован с кодом: `WorldPathfinder`/`WorldCostCache`/`WorldJobBoard` существуют и покрыты тестами — не хватает интеграции).

| # | Задача | Файлы | Приёмка |
|---|---|---|---|
| 4.1 | `Main.PumpWorldPlans` (тик 500 мс из `_Process`, не привязан к камере): `CollectPendingPlans` resident-чанков → `WallPlan`→`ConstructionManager.StartSitesBatch`, `CutTreePlan`→`MarkTreesBatch`, `MinedStonePlan`→`MarkStonesBatch` | `scenes/main/Main.cs` | чертёж за кромкой становится работой ≤500 мс |
| 4.2 | Мост движения: `RequestHierarchicalPath` — старт или цель вне `[0,1024)²` → `WorldPathfinder.TryFindPath` → распаковка в short-вейпоинты | `AgentMovementService.cs` | `TestWorldPathfinder` зелёный: обход берега, пути 1024–4096 |
| 4.3 | `WorldCostCache`: `MarkBlocked/MarkUnblocked` на событиях стен + `SyncWorldWallCosts` (500 мс, идемпотентно) | `Main.cs`, `WorldCostCache.cs` | `TestWorldCostCache` зелёный; стена блокирует мировой путь |
| 4.4 | Завершение: `CompleteWorldPlanBlock` (Plan→Done атомарно) + подписки на события менеджеров (CallDeferred) | `Main.cs` | «вечной метки» нет; отмена = снятие плана и сайта (симметрия) |
| 4.5 | `CellInspector`/`BuildTool`-тексты: сообщения игроку под новое состояние («за кромкой можно, идёт стройка») | `src/tools/CellInspector.cs`, `src/ui/tools/BuildTool.cs:178` | сообщение «Вне активной области…» отсутствует; его замена не врёт |
| 4.6 | `viz_cli impact --file src/ui/streaming/StreamingWorldView.cs` до/после подключения `WorldJobBoard` | — | SCC не разросся (остался 3 или меньше) |

### Фаза 5 · «Храповик и документация» — ≈1–2 дня

| # | Задача | Приёмка |
|---|---|---|
| 5.1 | Снизить `magic_1024`: `MapRenderer.MapWidth/Height` → `WorldModel.IslandTiles` (`MapRenderer.cs:33-34`), `Main.ColonySizeTiles` → оттуда же | гард `magic_1024` → 0; baseline обновлён |
| 5.2 | `coverage_512` (J-05: индекс 512 + клампы, `JobSpatialPyramid.cs:20-27, 188-198`) — **отдельная большая работа** (волна 2 мастер-плана, ~60 транзитивных потребителей). В этот план не входит; здесь только планка в baseline и задача-ссылка | планка зафиксирована; отдельный трек |
| 5.3 | `docs/ARCHITECTURE.md`: пометить «целевое состояние, реализовано частично», проставить факты по фазам 0–4 | нет утверждений «ЗАКРЫТО» без кода в дереве |
| 5.4 | `PLAN.md`: добавить §120 (записать сделанное), `plandrift` не ухудшился | `plandrift` ≤ 82 |
| 5.5 | `00_MASTER_PLAN.md`: волна 4, шаги 4.1–4.2 (стратегия `IGroundRenderer`) → заменить на «один шейдер» (этот план) | мастер-план не ведёт по снятому маршруту |

---

## 6. Файловый контракт (параллельная работа без конфликтов)

| Зона | Файлы | Владелец в фазе |
|---|---|---|
| Шейдер + окно | `MapRenderer.cs`, `terrain_blend.gdshader` | фазы 0.3/1.1/2.1/2.2 — один агент |
| Стриминг-слои | `StreamingWorldView.cs`, `streaming_ground.gdshader` | фаза 2.3–2.6 — один агент; **SCC: не трогать `WorldJobBoard`/`IWallWorld`** |
| Мир-ядро | `WorldCellQuery.cs`, `UnifiedWorldGenerator.cs`, `WorldModel.cs` | фазы 0.1/0.2/1.2 — один агент |
| Интегратор | `Main.cs`, `AgentMovementService.cs` | фаза 4 — **сериализовано с фазой 2** (Main — прямой потребитель обоих рендереров) |
| Диагностика | `src/tools/CellInspector.cs`, `PerformanceOverlay.cs`, `SimulationWatchService.cs` | 0.6/0.7/0.8/4.5 |
| Тесты | `tests/**` | свободно (согласовать новые раннеры) |

**Обязательная последовательность:** `MapRenderer.cs`: 0.3 → 1.1 → 2.2 → 5.1. `StreamingWorldView.cs`: 2.3 → 2.4 → 2.5. `Main.cs`: 1.2 → (4.1..4.4) после закрытия 2.3.

---

## 7. Гейты (не двигаться дальше без них)

| После фазы | Гейт |
|---|---|
| 0 | `dotnet build` = 0 ошибок |
| 1 | `run_island_world_agreement` = 0 расхождений; гарды зелёные на фактическом baseline |
| 2 | `run_streaming_water_layer` Code 0; `TestTerrainSurfaceCode` зелёный; скриншот кромки до/после; земля = 1 draw call |
| 3 | шов-тест на 1024 зелёный и **падает на коде до фазы 2** |
| 4 | стена за 1024: постановка → доставка → монтаж → Done → блокирует путь → снятие открывает; persistence round-trip |
| 5 | `magic_1024` = 0; `plandrift` ≤ 82; ни одного «ЗАКРЫТО» в доках без кода |

---

## 8. Риски

| # | Риск | Мера |
|---|---|---|
| R1 | `MapRenderer` — concurrency risk 4 (31 общая запись, volatile-буфера, фоновые потоки) при смене формата `terrain_map` | фаза 2.8: инвариант front-буфера; бенч `TerrainWindowBench` до/после |
| R2 | `StreamingWorldView` в SCC(3) с `Main.cs`/`IWallWorld`/`WorldJobBoard` — подключение исполнителя раздует цикл | 4.6: `viz impact` до/после; `WorldJobBoard` не трогать в фазе 2 |
| R3 | Окно шейдера 512² впритык к видимой области на зуме 0.12/4K | 2.7: проверка + зум-гейт |
| R4 | **Повторная потеря работы** (smoothstep уже терялся; вся миграция — незакоммиченный `??`-набор) | коммит после каждой фазы; ветка `feature/one-ground-renderer`; `bebebe-main.zip` и `_backup_*` не считать источником |
| R5 | Двойной канал декора: остров — `MapRenderer._objectLayer`, стриминг — `_decorLayer` (разные атласы/плотность) | 3.1 включает паритет декора в полосе кромки |
| R6 | Гейт уже снят, исполнителя ещё нет ⇒ «мёртвые» чертежи за кромкой прямо сейчас | фаза 4 — первая после сборки, если это беспокоит; либо временно вернуть `RefuseWorldBlueprint`-подобное сообщение до 4.1 |
| R7 | `ARCHITECTURE.md` опережает код — исполнитель будет «чинить уже сделанное» | правило: истина = дерево + тесты; доки читаются как цель, факт проверяется grep/сборкой |

---

## 9. Что нужно от владельца

1. **Приоритет фазы 4 vs фазы 2**: сначала рендер (видимые симптомы W-01/02/03) или сначала исполнитель (мёртвые чертежи, S6)? План по умолчанию: 0→1→2 (рендер).
2. **Разрешение на рантайм-прогоны** `godot_run_headless` (раннеры `run_streaming_water_layer`, `run_island_world_agreement`, `run_terrain_follow`, `run_architecture_guards`) — read-only, ничего в игре не меняют.
3. **Коммит текущего `??`-состояния** (43 новых файла) до начала работ — страховка от R4.
4. Подтверждение, что J-05 (пирамида 512→мир) остаётся **отдельным треком** (волна 2 мастер-плана), а не частью этого плана.

---

## Приложение: чем этот план заменяет/дополняет мастер-план

- Заменяет шаги **4.1–4.2** (FEAT-W02 `IGroundRenderer`, FEAT-W04-таблица) — стратегия сменилась на «один шейдер + декор-тайлы», зафиксирована тестами.
- Шаг 4.3 (паритет зон) → фаза 3.4; шаг 4.4 (исполнитель) → фаза 4; 0.5/0.6 (оверлей шва, тест на 1024) → фазы 3.1/3.2 в новой форме.
- Волны 0–3 мастер-плана (наблюдаемость, быстрые победы, индекс, стройка) — **остаются в силе без изменений**, этот план их не трогает.
