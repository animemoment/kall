# D-track: инвентаризация .md и дрейф документации

Роль: `docs-auditor` (@researcher/@architect read-only). Режим — **только чтение**:
под `src/`, `scenes/`, `tests/`, `*.cfg`, `project.godot` ничего не менялось.
Единственные созданные файлы: этот отчёт + кэш выводов viz в `_plan/_viz/`.

Корень: `D:\bebebe-main (1)\bebebe-main` (в родителе `D:\bebebe-main (1)` лежат дубликаты — см. §1.2).

---

## 0. Метод и инструменты

### 0.1. Графовый визуализатор (обязательный по правилу №1)

Все команды запускались из `D:\bebebe-main (1)\bebebe-main` с обязательным `--repo`.

| Команда | Результат | Замечание |
|---|---|---|
| `viz_cli.py stats --repo ...` | ✅ `files=301` (`.cs 238`, `.gd 52`, `.tscn 11`), `symbols=2136`, `edges=752`, `methods=2295`, `scenes=11`, `errors=0` | top_fan_in: `DynamicWorkScheduler.cs` 26 |
| `viz_cli.py plandrift --repo ...` | ⚠️ **первый запуск упал**: `{"error": "UnicodeEncodeError: 'charmap' codec can't encode character '\\u2194' in position 12376: character maps to <undefined>"}` | лечится `PYTHONUTF8=1`; **это дефект инструмента**, см. FEAT-D03 |
| `viz_cli.py plandrift` (с `PYTHONUTF8=1`) | ✅ `refs_total=273`, **`drift=82`** (30 %), все 82 — `missing_file` | ключевое доказательство дрейфа PLAN.md |
| `viz_cli.py deadcode` | ✅ `unreferenced_files=51` (core 26 / sim 17 / ui 8), `orphan_types=44`, `scenes_not_referenced=9` | 44 «сироты» — это в основном `tests/*.gd` SceneTree-раннеры (ложные срабатывания) |
| `viz_cli.py dangling` | ✅ **`missing_resource_refs: []`, `connections_without_method: []`** | висячих ресурсов сцен нет |
| `viz_cli.py coverage` | ✅ `src_files=207`, `covered=139`, **`67.1 %`**, `untested_hot_files=11` | в hot-списке `scenes/main/Main.cs`, `PlanetaryHydrology.cs`, `SimdNeedsBatch.cs` |
| `viz_cli.py layers` | ✅ `violations=21` (`core -> simulation` 20, `core -> ui` 1) | все 20 — из `src/core/world/mutation/*` |
| `viz_cli.py cycles --scope src --repo ...` | ✅ **`cycles count: 0`** | циклов нет; это важно для оценки `CYCLE_BREAK_DESIGN.md` |
| `python -m unittest test_vizcore -v` | ❌ **`Ran 15 tests … FAILED (failures=10)`**: `AssertionError: нет репозитория: D:\bebebe-main (1)\bebebe-main\bebebe-main` | `test_vizcore.py:19` и `viz_cli.py:25` по умолчанию берут `../bebebe-main`; когда визуализатор лежит ВНУТРИ проекта — путь неверный |
| `python -c "import viz_mcp; len(viz_mcp.TOOLS)"` | ✅ `tools: 15` | подтверждает D-10 |

Сырые выводы сохранены: `_plan/_viz/{plandrift,deadcode,dangling,coverage,layers,cycles}.json`.

**Чего инструмент НЕ умеет (важно для интерпретации доказательств):**
- `plandrift` проверяет **только существование файла** из бэктиков. Переименование «файл есть, но переехал» он ловит как `missing_file`, а семантический дрейф (раздел объявляет «Шаг 7 — снять фасад», а фасад жив) — **не ловит вообще**.
- `deadcode` считает «мёртвым» всё, у чего нет входящих рёбер: enum-файлы, DTO-структуры, интерфейсы, SceneTree-раннеры тестов, сцены, загружаемые по строковому пути (`GD.Load<PackedScene>("res://…")`), и `main_scene` из `project.godot`. Прямое следствие — **опасный вывод в `orphans.md`** (§5, P0-1).
- `coverage` считает покрытием *наличие* теста, ссылающегося на файл, а не исполнение.

### 0.2. Прочие проверки

`grep`/`read` по `src/`, `scenes/`, `project.godot`, `tests/`; `Get-FileHash SHA256` для поиска дубликатов .md; выборочное чтение `PLAN.md` (7218 строк — читались только заголовки + §0/§117–§119 + зоны, куда указал `plandrift`).

---

## 1. Реестр .md

### 1.1. Итоговые числа

- Всего `.md`-файлов в обеих ветках дерева: **45**.
- Уникальных документов: **30** (15 файлов продублированы байт-в-байт в `D:\bebebe-main (1)\` и `D:\bebebe-main (1)\bebebe-main\`).
- Объём: `PLAN.md` = 825 197 Б (7218 строк) — **79 % всего текста документации** (суммарно ≈1,05 МБ .md).
- Реально «живых» (актуальных коду на 2026-10-23): **6** документов (§1.3).

### 1.2. Дубликаты (SHA256 совпал байт-в-байт)

| Документ | Размер | Родитель | Ребёнок |
|---|---|---|---|
| `GLOBAL_AUDIT_REPORT.md` | 10 867 | ✅ | ✅ |
| `PLAN_GLOBAL_AUDIT_GPU.md` | 14 000 | ✅ | ✅ |
| `godot-csharp-strategy-sysprompt.md` | 8 555 | ✅ | ✅ |
| `_audit/AUDIT-A-world.md` | 66 250 | ✅ | ✅ |
| `_audit/AUDIT-B-sim.md` | 58 299 | ✅ | ✅ |
| `_audit/AUDIT-C-ui.md` | 56 396 | ✅ | ✅ |
| `_audit/CYCLE_BREAK_DESIGN.md` | 34 202 | ✅ | ✅ |
| `_audit/FIXES_APPLIED.md` | 16 451 | ✅ | ✅ |
| `_audit/PLAN_APPENDIX_115.md` | 7 462 | ✅ | ✅ |
| `_audit/PLAN_APPENDIX_116.md` | 6 014 | ✅ | ✅ |
| `_audit/cycles_after/all_deps.md` | 196 831 | ✅ | ✅ |
| `_audit/cycles_after/orphans.md` | 1 761 | ✅ | ✅ |
| `dependency_graph_visualizer/README.md` | 16 809 | ✅ | ✅ |
| `dependency_graph_visualizer/AI_README.md` | 7 831 | ✅ | ✅ |
| `dependency_graph_visualizer/VIZ_API.md` | 4 188 | ✅ | ✅ |

Вывод: **15 лишних копий** (≈400 КБ). Ни одна копия не отличается — правки в родителе и ребёнке разъедутся при следующем прогоне, поэтому каталог `D:\bebebe-main (1)\{_audit,GLOBAL_AUDIT_REPORT.md,…}` надо объявить «свалкой» и не читать.

Уникальные только в родителе: **`audit-cubemeter.md`** (инородный, см. §5 P0-6), `docs/lab_screenshots/*.png` (картинки, .md нет).
Уникальные только в ребёнке: `PLAN.md`, `AGENTS.md`, `AI_PROJECT_MAP.md`, 7 × `_reports/*.md`, 4 × `docs/*.md`.

### 1.3. Реестр уникальных документов

Легенда вердикта: **А** — актуален; **А* ** — актуален с оговорками/неполон; **У** — устарел (историческая ценность); **П** — противоречит коду (вводит в заблуждение); **И** — инородный.

| № | Путь | Размер | Назначение | Дата / актуальность | Вердикт |
|---|---|---|---|---|---|
| 1 | `PLAN.md` | 825 197 | склейка 3 разных PLAN-документов + дорожная карта §0–§119; единственный де-факто источник задач | 2026-10-20 (последний §119) | **А\*** — дрейф 82/273 ссылки, дубли номеров §52/§100, внутри §0 путь `C:\s\bebebe-main` |
| 2 | `AI_PROJECT_MAP.md` | 25 024 | «карта местности» для агента: константы, слои, цепочки | 2026-10-08 | **П** — 15+ расхождений с кодом (§3) |
| 3 | `AGENTS.md` | 2 975 | ролевая модель 4 субагентов | 2026-10-08 | **А\*** — модель верна, но процесс ушёл вперёд (§4.0) |
| 4 | `docs/DECISIONS.md` | 12 933 | ADR D-01…D-10 | 2026-10-21 | **А** — все 10 решений реализованы в коде; 1 нарушено, 1 частично (§4) |
| 5 | `docs/LIVE_BRIDGE.md` | 4 606 | живой мост `live.py`/`live_bridge.gd` | 2026-10-21 | **П** — `:58` «предполагает клетку 32 px», реально 64 px (§5 P0-2) |
| 6 | `docs/MapDataWriterAudit.md` | 46 787 | аудит писателей `MapData` и легаси-менеджеров | 2026-10-20 | **А\*** — снимок, ссылки `файл:строка` валидны выборочно |
| 7 | `docs/WorldDeltaApiAudit.md` | 58 010 | аудит дельта-API мира | 2026-10-19 | **П** — `:14` «payload-байты `Data` сейчас всегда пустые», но схема v1 реализована (`IslandPayload.cs:38-44`) |
| 8 | `_reports/01_streaming_decor.md` | 27 152 | диагностика «пропадает декор/чанки» | 2026-10-21 | **А** |
| 9 | `_reports/02_coastline_impl.md` | 21 888 | smoothstep вместо линейного климата | 2026-10-21 | **А** |
| 10 | `_reports/03_blueprint_impl.md` | 19 162 | чертёж за кромкой 1024×1024 | 2026-10-21 | **А** |
| 11 | `_reports/04_construction_hierarchy.md` | 53 402 | баг #5: иерархия стройки | 2026-10-21 | **А** — `WaitForAllSiteResources=false` подтверждается |
| 12 | `_reports/05_zone_click_impl.md` | 20 612 | клик по зонам | 2026-10-21 | **А** |
| 13 | `_reports/06_streaming_paint.md` | 4 805 | «мир за кромкой» | 2026-10-21 | **А** |
| 14 | `_reports/deps_all.md` | 190 656 | дамп графа (`app.exe --cli`) | 2026-10-21 | **А\*** — 301 файл совпал с `viz stats`, но 1983 ребра ≠ 752 (другая версия/определение) |
| 15 | `_audit/AUDIT-A-world.md` | 66 250 | состязательный аудит world/streaming | 2026-10-18 | **А\*** — снимок; 9 находок, часть закрыта `FIXES_APPLIED.md` |
| 16 | `_audit/AUDIT-B-sim.md` | 58 299 | аудит симуляции (10 находок B-01…B-10) | 2026-10-18 | **А\*** — B-01/B-02 закрыты §116 |
| 17 | `_audit/AUDIT-C-ui.md` | 56 396 | аудит UI/render (C-01…C-05+) | 2026-10-18 | **А\*** — C-01/C-02/C-03/C-04/C-05: см. §6.4 |
| 18 | `_audit/CYCLE_BREAK_DESIGN.md` | 34 202 | верификация 4 «циклов» | 2026-10-18 | **П** — циклы 2/3 ссылаются на удалённые файлы; сейчас `cycles=0` (§5 P1-3) |
| 19 | `_audit/FIXES_APPLIED.md` | 16 451 | реестр применённых фиксов (W-01…) | 2026-10-18 | **У** — исторический реестр, ссылки на файлы валидны |
| 20 | `_audit/PLAN_APPENDIX_115.md` | 7 462 | разрыв циклов R7 | 2026-10-18 | **У** — дублирует PLAN.md §115 (файл `WorldAgentRenderer.cs` удалён) |
| 21 | `_audit/PLAN_APPENDIX_116.md` | 6 014 | раунд 2 P1 (`JobSpatialPyramid`) | 2026-10-18 | **А\*** — код пирамиды совпадает с описанием |
| 22 | `_audit/cycles_after/all_deps.md` | 196 831 | дамп графа до/после | 2026-10-18 | **П** — «Всего файлов: 413», сейчас 301; ссылки на `WorldAgentRenderer.cs`, `Test10kBenchmark.cs` |
| 23 | `_audit/cycles_after/orphans.md` | 1 761 | «26 абсолютно мёртвых файлов — можно удалять» | 2026-10-18 | **П (опасный)** — предлагает удалить используемые файлы (§5 P0-1) |
| 24 | `GLOBAL_AUDIT_REPORT.md` | 10 867 | глобальный аудит стриминга + CPU/GPU | 2026-10-05 | **П** — 0 из 6 тестов и 5 ключевых файлов не существуют (§5 P0-4) |
| 25 | `PLAN_GLOBAL_AUDIT_GPU.md` | 14 000 | план GPU-миграции по тому же аудиту | 2026-10-05 | **П** — опирается на `CoordinateHash.cs`, `NoiseGenerator.cs`, `WorldFeatureGenerator.cs` (все отсутствуют) |
| 26 | `godot-csharp-strategy-sysprompt.md` | 8 555 | системный промпт «senior C# Godot 4.7» | 2026-10-09 | **А\*** — не документ проекта; полезен как памятка, но не описывает этот проект |
| 27 | `dependency_graph_visualizer/README.md` | 16 809 | GUI-описание визуализатора | 2026-10-18 | **А** |
| 28 | `dependency_graph_visualizer/AI_README.md` | 7 831 | CLI-инструкция для агентов | 2026-10-18 | **А\*** — не упоминает `viz_cli.py`/`--repo`/UTF-8 |
| 29 | `dependency_graph_visualizer/VIZ_API.md` | 4 188 | API vizcore/viz_cli/viz_mcp | 2026-10-21 | **А\*** — 15 инструментов ✅, но `--repo` по умолчанию неверен для этой раскладки (§5 P2-2) |
| 30 | `audit-cubemeter.md` (родитель) | 21 392 | аудит **чужого** проекта `game.1t.ru` (JS-бандл, «Кубометр») | 2026-10-21 | **И** — вынести из репозитория (§5 P0-6) |

### 1.4. Сводка вердиктов

| Вердикт | Кол-во | Документы |
|---|---|---|
| **А** актуален | 8 | DECISIONS, `_reports/01…06`, viz README |
| **А\*** актуален с оговорками | 11 | PLAN, AGENTS, MapDataWriterAudit, AUDIT-A/B/C, PLAN_APPENDIX_116, deps_all, sysprompt, AI_README, VIZ_API |
| **У** устарел (исторический) | 2 | FIXES_APPLIED, PLAN_APPENDIX_115 |
| **П** противоречит коду | 8 | AI_PROJECT_MAP, LIVE_BRIDGE, WorldDeltaApiAudit, CYCLE_BREAK_DESIGN, cycles_after/all_deps, cycles_after/orphans, GLOBAL_AUDIT_REPORT, PLAN_GLOBAL_AUDIT_GPU |
| **И** инородный | 1 | audit-cubemeter |
| **Итого** | **30** | |

---

## 2. PLAN.md: карта разделов и статус

### 2.1. Что это физически

`PLAN.md` — **не один документ, а три, склеенные в один файл append-only**:

| Строки | Заголовок H1 | Внутренняя нумерация |
|---|---|---|
| 1–72 | `# PLAN.md — Унификация рендера ландшафта…` | §99, §100 (дубль номера) |
| 73–1411 | `# PLAN.md — Динамическая балансировка нагрузки между ядрами…` | §0…§20 |
| 1412–7218 | `# 21. Ghost склада…` (и далее 40 H1-разделов) | §21…§119 |

Технические дефекты структуры (доказательства):
- **Два разных раздела под одним номером 100**: строка 47 (`## 99.`/`## 100.`) и строка 5449 (`## 100. Архитектурный план исправления критических багов…`).
- **Два разных §52**: строка 4185 («Устранение GC-пауз») и 4224 («Физическая планетарная генерация»).
- **Пропущены номера §39 и §64**; §13 идёт после §14 (459 → 558).
- **§0 (строка 75) описывает чужой корень**: «что найдено в корне `C:\s\bebebe-main`» — отчёт об аудите другой копии проекта.
- 238 заголовков H1/H2, 44 строки с маркерами «ВЫПОЛНЕНО/РЕАЛИЗОВАНО», 24 из них — в заголовках.

### 2.2. Дрейф по данным `plandrift` (ключевое доказательство)

`refs_total=273`, `drift=82` (30 %), все — `missing_file`. Разбивка по разделам (топ):

| Раздел PLAN.md | Битых ссылок | Что именно протухло |
|---|---|---|
| `100.9. Итоги реализации и верификация` | **9** | тесты `tests/Test10kBenchmark.cs`, `run_test.gd`, `TestAdversarialGpuAndSimd.cs` — удалены |
| `112.0.3. Реальные God-Objects` | 6 | дизайн-документ ссылается на уже расщеплённые файлы |
| `112.6.2. Юнит-тесты по зонам`, `111.1`, `102.7` | по 4 | `tests/run_*_test.gd`, `src/ui/streaming/WorldAgent*` |
| `46.3.4`, `47.4`, `48.3/48.4`, `49.3/49.4` | 2–3 каждый | `src/shaders/terrain_blend.gd` — **шейдер переименован в `.gdshader`, PLAN этого не знает** |
| `38.5`, `115.2`, `118.3`, `§119` | 1–2 | `AgentGpuComputeService.cs`, `WorldJobTicker.cs`, `src/core/agents/AgentDataPool.cs` |

47 уникальных несуществующих путей, в т.ч. самые «ядовитые»:

```
src/core/world/MapGenerator.cs            → реально src/core/world/generation/UnifiedWorldGenerator.cs + WorldGenV2.cs
src/simulation/gpu/AgentGpuComputeService.cs → реально gpu/GpuFlowFieldSimulator.cs, GpuAgentTransformSimulator.cs, GpuHumiditySimulator.cs
src/ui/controllers/GardenController.cs    → реально ui/controllers/FarmController.cs
src/core/agents/AgentDataPool.cs          → реально src/simulation/agents/AgentDataPool.cs  (дрейф ВНУТРИ §119, самой свежей секции!)
src/ui/streaming/{WorldAgent,WorldJobTicker,IWorldAgentSource}.cs → удалены
src/core/world/{WorldRegions,climate/WorldClimateParams,streaming/generation/WorldFeatures}.cs → удалены/переехали
tests/{run_test,run_benchmark,run_render_test,run_shadow_terrain_test}.gd → удалены
src/GlobalUsings.cs, src/simulation/IColonyResettable.cs, src/ui/contracts/ITool.cs → удалены
```

### 2.3. Карта разделов (верхний уровень)

Статусы: **✅** реализовано и подтверждено кодом · **⚠️** реализовано с semantic drift · **🗄** исторический отчёт о выполненном (задач больше нет) · **❌** устарело/вводит в заблуждение · **🧊** план без реализации.

| § | Строки | Тема | Статус | Доказательство |
|---|---|---|---|---|
| 99–100 (дубль) | 8–72 | рендер ландшафта, фризы камеры, «очистка сущностей» | 🗄 | перекрыто §23.24/§23.25/§31 |
| 0 | 75–116 | аудит «корня `C:\s\bebebe-main`» | ❌ | чужой путь; §0.1 ссылается на `MapGenerator.cs` |
| 1–3 | 117–181 | синтез researcher: PLINQ/TPL, цели, архитектура | 🗄 | теория, реализовано в §4 |
| 4–7 | 182–373 | интерфейсы `IDynamicWorkScheduler`, политика миграции, потоки, zero-alloc | ✅ | 7 файлов `src/simulation/scheduling/*` на месте, `DynamicWorkScheduler.cs` — top_fan_in #1 |
| 8 | 374–424 | точки интеграции, шаг 1 — фасад `DynamicWorkBalancer` | ⚠️ | фасад есть (`DynamicWorkBalancer.cs:7-11`), но **[Obsolete] и «Шаг 7 — снять фасад» не выполнены**: `AgentSimulationThread.cs:332,954,1366` и `GpuAgentTransformSimulator.cs:261,268` всё ещё зовут фасад |
| 9–12 | 425–458 | тесты, порядок внедрения, приёмка, риски | ✅ | `Bench*`, `Test*` бенчи на месте |
| 13–14 | 558–568 | горы `mountains.png`, инструкции @developer | ✅ | `MountainTileHelper.cs`, `SourceMountain=9` (`MapRenderer.cs:60`) |
| 15 | 569–703 | GPU compute/vertex-shader/CPU-SIMD | ⚠️ | §15.0 ссылается на `AgentGpuComputeService.cs` (удалён); реально 3 GPU-симулятора |
| 16 | 704–756 | режим карты «Влажность» | ✅ | `HumidityMap.cs`, `HumidityOverlayRenderer.cs` |
| 18 | 757–834 | независимые слои шума + `FertilityMap` 0..200 | ✅ | `FertilityMap.cs`, `FertilityOverlayRenderer.cs` |
| 19 | 835–974 | реалистичная генерация (критика + что берём) | ✅ | `ContinentShaper.cs`, `WorldClimateProvider.cs` |
| 20 | 975–1411 | окно фермы `Farm.tscn`, урожай от почвы | ⚠️ | `Farm.tscn`+`FarmController.cs` есть; `GardenController.cs` (упомянут в §20.2) удалён |
| 21 | 1412–1506 | ghost склада, жёсткий пайплайн стройки, 6 стадий | ✅ | `ConstructionPipeline.cs`, `WorkProgressTracker.cs`, `ConstructionStage` в `WorldDeltaKind.cs:44` |
| 22 | 1489–1506 | видимость мировой карты | ✅ | `WorldMapOverlay.cs` |
| 23 (27 подразделов) | 1507–2529 | иерархическая генерация: 18 параметров → чанки → локальная карта | ✅ | §23.17 «снос устаревших генераторов» исполнен: `UnifiedWorldGenerator.cs` единственный |
| 24 | 2530–2564 | главное меню `menu.tscn`, выбор спавна | ✅ | `MainMenuController.cs`, `run/main_scene="res://scenes/ui/menu.tscn"` (`project.godot:14`) |
| 25–28 | 2565–2762 | макро/локальная суша, бухты, морфология островов | 🗄 | реализовано в `UnifiedWorldGenerator`/`ContinentShaper` |
| 29–38 | 2763–3488 | L1/L2/L3 кэш, пики CPU 100x, LOD, тест-инфра, FPS 1000, 100k GPU | ✅/🗄 | §32 «ВЫПОЛНЕНО»; `run/max_fps=1000` (`project.godot:16`); §38.5 ссылается на удалённые тесты |
| 40–44 | 3489–3659 | стресс 100k, GPU flow fields, GPU transform, интеграция | ✅ | `GpuFlowFieldSimulator.cs`, `GpuAgentTransformSimulator.cs`, `project.godot:44 use_gpu_agent_interpolation=true` |
| 45–52 (два §52) | 3660–4308 | спайки рендера/мира, draw calls ≤10, тени, pixel seams, вода, геоморфология, профайлер, GC-паузы, планетарная генерация | ✅ | `ShadowCasterRenderer.cs`, `DayNightCycle.SampleSun`, `PerformanceOverlay.cs`, `ProfilerLogService.cs` |
| 53–59 | 4309–4977 | UI стройки/зон, HUD-clamping, перезагрузка стройки, зоны складов, реактивные ресурсы, загрузка 15с→1–2с | ✅ | `BuildMenuController.cs`, `WorkZoneController.cs`, `DemandRegistry.cs`, `ResourceBroker.cs` |
| 60–63 | 4978–5359 | HSWM-матчер на 1M, Phase 3a/2, комплекс багов (ПКМ, шов, стены, голодание задач, обход воды) | ⚠️ | `JobSpatialPyramid.cs` есть, но его размеры — 512×512 (см. FEAT-D10) |
| 65 | 5360–5448 | унификация локальной карты 512×512 и стриминга | ⚠️ | §117.2 поднял остров до **1024**; константы пирамиды остались 512 |
| 100 (дубль) | 5449–5575 | критические баги геймплея/рендера | 🗄 | 9 битых ссылок на удалённые тесты |
| 101–106 | 5576–5792 | UI/диспетчер/навигация, legacy-vs-new менеджеры, фазы стабилизации 0–1 | ✅ | `EventAggregator`/`SimEvents.cs`, `JobValidator.cs` |
| 107–111 | 5793–6075 | фазы 2–3, блокировки, Round 7, разрыв циклов 1–3 | ✅/🗄 | `cycles --scope src` = **0** |
| 112 | 6076–6390 | мастер-план рефакторинга по графу (DIP + Signal Bus, декомпозиция god-objects) | ⚠️ | журнал R0–R2 «выполнено» (`PLAN.md:6363-6376`); §112.0.3/§112.6.2 — 10 битых путей |
| 113–114 | 6391–6537 | живой багхантинг через godot-bridge, полный UI-тест через MCP | ✅ | `docs/LIVE_BRIDGE.md`, `addons/live_bridge/live_bridge.gd` |
| 115–116 | 6538–6724 | разрыв циклов R7/R9 | 🗄 | дублируется в `_audit/PLAN_APPENDIX_115/116.md` |
| 117 | 6725–6810 | «мир без фризов и обрывков», 8 субагентов | ✅ | 8 ролей, границы файлов |
| 118 | 6811–7050 | снос «двуголового генератора»: один мир, одна активная зона | ✅ | `ActiveArea.cs`, `ActiveAreaRegistry.cs`, `MapDataShim.cs` |
| 119 | 7051–7218 | **зоны-бригадиры**: Рабочий/Разнорабочий, радиус, покрытие, очередь, якорь | ✅ (P0–P2) | `WorkZone.cs:19-33`, `ZoneCoverageMap.cs`, `ZoneJobRouter.cs`, `WorkerAssignmentSystem.cs`, `WorkerZoneJobSystem.cs` |

> **Пример протухшего «СТАТУС»-лога внутри PLAN.md** (аргумент за split, §2.4): `PLAN.md:7125-7127` утверждает «FarmZoneRenderer по-прежнему рисует Work-зоны из `zone.Tiles` — после P0 Work-зоны не отрисовываются (визуальный пробел до P2, легаси-рендер оставлен нетронутым)». Код это опровергает: `FarmZoneRenderer.cs:66-70` давно материализует диск — «Work-зоны хранят не список тайлов… поэтому раньше work-зона не рисовалась вообще», `tiles = isWork ? WorkZoneTiles(zone) : zone.Tiles`. То есть запись «известный визуальный баг» в PLAN.md врёт в сторону «хуже, чем есть», а §119 P0/P1/P2 при этом помечены «выполнено» — читатель не может отличить живой статус от исторического.

### 2.4. Предложение по реструктуризации PLAN.md

Сейчас: 825 КБ, 79 % всей документации, 3 документа в одном, дубли номеров, 30 % ссылок мертвы, последний раздел (§119) — единственный, который реально читают исполнители.

Предлагаю (не делал, только план):

1. **`PLAN.md` оставить только «активной дорожкой»**: §112.9–§119 (строки 6359–7218, ≈ 45 КБ) + краткий «живой» индекс. Всё, что до §112 — в архив.
2. **`docs/archive/PLAN_2026-10.md`** — строки 1–6358 как есть (история решений; полезна для «почему так», но не как ТЗ).
3. **`PLAN.md` → `PLAN_INDEX.md`**: 40 H1 + 198 H2 списком, со статусом и ссылкой на строку. Генератор уже есть — `tools/extract_plan_headers.py` + `tools/plan_headers.txt` (45 473 Б); подключить его к `viz_cli.py` (FEAT-D08).
4. **Разрезать по темам** (2-й уровень): `docs/plan/world_gen.md` (§23, §25–28, §52-2, §118), `docs/plan/simulation.md` (§29–§44, §60–62, §119), `docs/plan/render_ui.md` (§31, §33, §45–49, §53–54), `docs/plan/process.md` (§112–117).
5. **Каждому разделу — front-matter** `status: done|active|dropped`, `verified: 2026-10-23`, `files: [...]`. Тогда `plandrift` можно расширить проверкой «все files существуют» без бэктиков в тексте.
6. **Не переносить** в новый PLAN: §0 (чужой путь `C:\s\bebebe-main`), §38.5/§45.5/§100.9 (мертвые тесты) — сразу в архив с пометкой.

---

## 3. AI_PROJECT_MAP.md: расхождения с кодом

Документ от 2026-10-08 — «первое, что читает агент». Проверено 23 утверждения: **16 расходятся**, 7 подтверждены.

### 3.1. Расхождения (file:line)

| # | AI_PROJECT_MAP.md | Код | Тяжесть |
|---|---|---|---|
| 1 | `:3` `main_scene=uid://di6gcaep1knur (scenes/main/Main.tscn)` | `project.godot:14` `run/main_scene="res://scenes/ui/menu.tscn"` → стартует меню, не Main. Плюс `window/stretch` теперь `canvas_items`/`keep_width` | **P0** |
| 2 | `:6` «Карта: 512x512, Tile 64px => 32768px» | `MapRenderer.cs:33-34` `MapWidth=1024; MapHeight=1024` (комментарий `:27` «§117.2: 512→1024»), `Main.cs:27` `ColonySizeTiles = 1024` | **P0** |
| 3 | `:7`,`:29` генерация `MapGenerator` + `Noise` + `River` | файла нет (plandrift `PLAN.md:89`); реально `src/core/world/generation/UnifiedWorldGenerator.cs`, `WorldGenV2.cs`, `climate/ContinentShaper.cs` | **P0** |
| 4 | `:24` `Garden.tscn->GardenController.cs` | `GardenController.cs` отсутствует; `scenes/ui/Garden.tscn` — **34-байтная заглушка** (`[gd_scene load_steps=1 format=3]` и всё); `FarmController.cs:12`: «Замена старому GardenController — Garden.tscn удалён» | **P0** |
| 5 | `:34` `gpu: AgentGpuComputeService (не в прод-цикле)` | файла нет; есть `GpuFlowFieldSimulator.cs`, `GpuAgentTransformSimulator.cs`, `GpuHumiditySimulator.cs`; GPU **в цикле**: `project.godot:44 rendering/use_gpu_agent_interpolation=true` | **P0** |
| 6 | `:27` `ItemId (None/Log/Grain)` | `ItemDefinition.cs:3-9`: `None, Log, Grain, **Stone**` | P1 |
| 7 | `:9` «maxSteps 8 (6 на 100x)» | `AgentSimulationThread.cs:355-357`: `maxAllowedSteps = speed>=100 ? (acc > dt*6 ? 2 : 1) : 10` (амортизация §32) | P1 |
| 8 | `:40` «8 слоёв TileMapLayer» | `MapRenderer.cs:156-169`: **10** (добавлены `_mountainLayer`, `_farmBlueprintLayer`) | P1 |
| 9 | `:44` «JobRegistry.Register (8 хендлеров)» с составом «…Wander + NeedsJobSystem» | `AgentSimulationThread.cs:265-272`: 8 хендлеров, среди них **`MiningJobHandler`**, а `WanderJobSystem`/`NeedsJobSystem` **не** регистрируются в `JobRegistry` (живут отдельно) | P1 |
| 10 | `:18` Z-порядок 5/6/8/10/15/100/120 | база верна (5/6/8/10/15/100), но список неполон: `ShadowCasterRenderer.cs:41` Z=4, `ItemShadowRenderer.cs:67` Z=3, `MapRenderer.cs:267` Z=−2, `WorldMapOverlay.cs:102,113` Z=1000/999. «Overlay 120» в коде не найден | P1 |
| 11 | `:17` «Чанки 32x32 по 16x16 (`ChunkShift=4`), регионы pathfinder 16x16» | `ChunkShift=4` ✅; **`ChunkDim=32` = 512 клеток** (`JobSpatialPyramid.cs:20-21`), а мир 1024 → см. FEAT-D10 | P1 |
| 12 | `:76` «waypoints 40 / Stuck 1.5с» | `AgentDataPool.cs:224` `MaxWaypoints = 96`; `StuckTimer >= 1.5f` (`AgentMovementService.cs:96`) ✅ | P2 |
| 13 | `:34` `farming: … ZoneManager` | в `farming/` только `FarmZoneManagerFacade.cs`; `ZoneManager.cs` живёт в `src/simulation/zones/` | P2 |
| 14 | `:33` состав handlers | добавились `MiningJobHandler`, `ResourceBroker`, `DemandRegistry`; `NeedsJobSystem` переписан под §119 (60с-холд) | P2 |
| 15 | `:11-12` перф-блок (радиусы еды 3 чанка и т.п.) | не «нейтральные оптимизации», а **урезание дальностей** → прямое нарушение D-08 (см. §4) | P1 |
| 16 | `:13` «ReachDist 48» | во **всех** хендлерах `ReachDist = 20.0f` (`BuildConfig.cs:54`, `TreeChoppingJobHandler.cs:10`, `FarmingJobHandler.cs:10`, `NeedsJobSystem.cs:16`, `WorkerZoneJobSystem.cs:31`) | P1 |

### 3.2. Что подтвердилось (можно доверять)

| Утверждение | Подтверждение |
|---|---|
| `:13` `Main.AgentCount=10000` | `Main.cs:73` `[Export] public int AgentCount = 10000;` |
| `:8` сутки 12000 с / час 500 с | `WorldTime.cs:15,21` |
| `:9` скорости 0/1/5/25/100, шаг 0.05 с | `GameSpeed.cs:5-9`, `AgentSimulationThread.cs:154` `BaseFixedDeltaTime = 0.05f` |
| `:13` MoveSpeed 120, вода ×0.45 | `AgentMovementService.cs:10-12` (+`TileShift=6`) |
| `:14` пороги нужд 70/80/80/Env 40, Mood-веса | `AgentDataPool.cs:250-253,276-277`, `NeedsJobSystem.cs:34-41` |
| `:15` длительности 3000/125/2000/10/5/60 | `BuildConfig.cs:36-39`, `FarmingJobHandler.cs:9`, `HarvestJobHandler.cs:8`, `PlantingJobHandler.cs:9`, `CropGrowthManager.cs:13` |
| `:28` JobCategory 4, JobPriorityTier 7, JobExecutionType 3, NeedBehavior 4; `AgentState` 5; `JobRegistry` массив[32] | `JobCategory.cs:9` (`Count=4`), `JobPriorityTier.cs:8-14`, `JobExecutionType.cs:8-10`, `NeedBehavior.cs:11-17`, `AgentState.cs:8-12`, `JobRegistry.cs:12` |
| `:44` 8 хендлеров | `AgentSimulationThread.cs:265-272` |

**Вердикт: `AI_PROJECT_MAP.md` нельзя использовать как карту** — заголовки §0 (константы) устарели в 5 ключевых точках, §1 (структура) ссылается на удалённые файлы. Нужен перевыпуск либо явная шапка «версия от 2026-10-08, доверять только §4–§7».

### 3.3. Чего в AI_PROJECT_MAP.md нет вообще (а код есть)

Целые подсистемы, появившиеся после 2026-10-08 и не упомянутые ни строкой:
`src/core/world/activearea/*` (`ActiveArea`, `ActiveAreaRegistry`, `MapDataShim`), `src/core/world/mutation/*` (11 файлов: дельты `ISM1`, replay, mirror), `src/core/world/streaming/**` (25 файлов), `src/simulation/structures/*` (7), `src/simulation/zones/*` (8), `src/simulation/scheduling/*` (7), `src/simulation/simd/*` (2), `src/simulation/offline/*` (4), `src/simulation/stones/*`, `WorkerAssignmentSystem`, `WorkerZoneJobSystem`, `FlatIdleWorkerPool`, `JobSpatialPyramid`, `JobValidator`, `DemandRegistry`, `ResourceBroker`, `ResourceRef`, `SimEvents`, `SimulationWatchService`, `CpuTopology`, `ParallelRng`, `HumidityMap`, `FertilityMap`, `BiomeMapper`, `MountainTileHelper`, `StreamingWorldView`, `WorldJobBoard`, `WorldMapOverlay`, `TerrainTileLayer`, `StockpileItemRenderer`, `ItemShadowRenderer`, `Fertility/HumidityOverlayRenderer`, `AgentLerpBatch`, `ZoneDraftTool`, `WorkZoneController`, `BuildMenuController`, `MainMenuController`, `FarmController`, `WallMaterialRegistry`.

Это 60+ файлов из 207 в `src/` (**≈30 % кода**), которых в «карте» нет.

---

## 4. DECISIONS.md: соблюдение D-01…D-10

| ID | Вердикт | Доказательство |
|---|---|---|
| **D-01** зоны = центр+радиус | **Соблюдается** | `WorkZone.cs:19-20` `MinRadiusTiles=64/MaxRadiusTiles=512`, `:30-33` кламп радиуса; `ZoneCoverageMap.cs:10-11` «`ushort[]` на тип работ (1024²=2 МБ)», `:143-164` покраска диска по формуле, без HashSet |
| **D-02** пуш работы в зону, репуш ~30 с | **Соблюдается с отклонением** | Пуш ✅ `ZoneJobRouter.cs:15-20` (ConcurrentQueue на зону, владелец клетки — O(1) `Coverage.TryGetOwner`); репуш ✅ `:95` `RepushAfterMs = 30_000`. **Но**: (а) появился 60-секундный холд, которого в D-02 нет — `ZoneJobRouter.cs:102` `WorkerHoldMs = 60_000`, применяется в `JobSpatialPyramid.cs:1120` → **квалифицированная работа в зоне невидима разнорабочему 60 с** (это осознанное решение §119 P2 — `PLAN.md:7171-7180`, но в ADR не внесено: следующий агент узнает о нём только из 825-КБ файла); (б) D-02 обещает «для срочных работ — отдельный путь немедленного пуша» — такого пути в коде **нет**, и `PLAN.md:7074` прямо фиксирует обратное: «мгновенного прохода при создании зоны НЕТ». **Это первый кандидат в причину «работы ждут десятки секунд»** |
| **D-03** партиция пересечения зон | **Соблюдается** | `ZoneCoverageMap.cs:15` «`DesiredWorkers == 0` → зона НЕ владеет клетками», `:105-109` сортировка `AssignedCount desc`, `:128` `c != 0 ? c : a.Id.CompareTo(b.Id)` — tie → меньший id |
| **D-04** Laborer/Worker, желаемое число без потолка | **Соблюдается** | `WorkZone.cs:22` `MaxDesiredWorkers = 999`, `:50-54` легаси-алиас; `WorkerAssignmentSystem.cs:8-26` (найм N ближайших `RecruitRadius`, разжатие лишних); `AgentDataPool` `AgentClass`/`AssignedZoneId` (`PLAN.md:7168-7170`) |
| **D-05** прерывание сохраняет прогресс | **Соблюдается частично** | `CellProgressStore.cs:14-35` (0..1 по клетке, `SaveFraction`/`GetFraction`); сейв: `WorldDeltaKind.cs:47-48` `CutTreePlan/MinedStonePlan → [1]ver,[4]fraction`, `BuildingPlan` → `[4] workProgress`; `MinedStonePlan (0x16)` зеркалит `CellProgressStore` (`:48`). **Дыра:** прогресс **вскопки грядки** — `FarmJobManager.cs:20` `TillProgress = new CellProgressStore()` — **не имеет своего `WorldDeltaKind`** (в `WorldDeltaKind.cs:10-101` нет Farm/Till-kind), т.е. «переживает сейв» для фармы не выполняется |
| **D-06** иерархический отрицательный ответ | **Соблюдается** | `JobSpatialPyramid.cs:961-976`: «P8в: O(1) глобальный отрицательный ответ», `_globalCdCount >= _unclaimedCount && now < _globalCdEarliest` + перепроверка счётчиков против гонки |
| **D-07** HPA*, регионы 16×16, ≤17 поисков/кадр | **Соблюдается** | `HierarchicalPathfinder.cs:33-34` `RegionShift=4` → 16×16; `:154` `SearchesPerFrame = 17`; `:152` обоснование «17/кадр ≈ 1000 поисков/с». Часть «≤1,5 мс» кодом не гарантируется — только бюджет по числу поисков (`:166-190`) |
| **D-08** перф без отрезания функциональности | **НАРУШЕН (2 места, задокументированы в самом коде)** | ① `NeedsJobSystem.cs:128-134`: «P0: радиус еды 40 тайлов = 3 чанка (было 32 = скан до 1024 чанков под lock)` → `maxChunkRadius: 3`; ② `StockpileHaulingJobHandler.cs:73-75`: «P0.3: лимит радиуса 4 чанка вместо полного 32» → `TryReserveGroundItems(..., 4, ...)`; ③ тот же класс — `JobSpatialPyramid.cs:41` `MaxClaimScanPerChunk = 64` (скан чанка урезан). Комментарии честно называют причину — перф, ровно то, что D-08 запрещает. См. §5 P1-1 и FEAT-D11 |
| **D-09** дельта на остров с переигрыванием | **Соблюдается** | `IslandOverrideStore.cs:64-65` `FormatVersion=1` + `Magic = {'I','S','M','1'}`; `:265` `TempSuffix=".tmp"`; реплей — `IslandDomainReplay.cs:76-89`, `IslandStateReplay.cs`, `IslandDeltaApplicator.cs:91-120`; регресс-тест есть (`tests/TestIslandMirror.cs`) |
| **D-10** анализатор отвечает агенту | **Соблюдается, инструмент дефектный** | `viz_mcp.py` — ровно **15** инструментов ✅ (совпадает с D-10 и `VIZ_API.md:52-54`); 13 команд `viz_cli` отработали. Дефекты: `plandrift` падает на cp1251-консоли (FEAT-D03), `--repo` по умолчанию неверен (FEAT-D04), self-test 10/15 red (FEAT-D05) |

**Сводно:** 7 — соблюдается, 2 — частично (D-02, D-05), 1 — нарушено (D-08).

---

## 5. Вводящие в заблуждение документы (приоритет на исправление)

Критерий: агент-исполнитель, прочитав документ, **сделает неверное действие** (удалит нужное, пойдёт по неверному пути, будет искать несуществующие тесты).

### P0 — немедленно (риск порчи кода/неверного действия)

**P0-1. `_audit/cycles_after/orphans.md` — предлагает удалить живой код.**
Строка 5: «💀 АБСОЛЮТНО МЕРТВЫЕ ФАЙЛЫ (0 связей в коде и 0 в сценах — **можно удалять**)», далее 26 файлов. Среди них:
- `src/core/jobs/JobTypeId.cs` — используется везде: `JobRegistry.cs:17,22`, `JobSpatialPyramid`, все хендлеры;
- `src/simulation/agents/AgentState.cs` — `AgentState.Working` в `ConstructionJobHandler.cs:89,109,118`, `FarmingJobHandler.cs:61,81,89`, `AgentMovementService`;
- `src/core/world/TileType.cs`, `NeedBehavior.cs`, `JobCategory.cs`, `JobPriorityTier.cs`, `BuildingType.cs`, `StructureCategory.cs` — все используются;
- `autoload/mcp_interaction_server.gd` — **автолоад**, прописан в `project.godot:21` (движок грузит его без «рёбер»);
- `src/GlobalUsings.cs`, `src/core/world/streaming/generation/WorldFeatures.cs` — **файлов не существует** (уже удалены).
Причина: `deadcode` не видит рёбер у enum/DTO и автолоадов (это признаёт `VIZ_API.md:66-67`, но в `orphans.md` оговорки нет).
→ Действие: пометить файл «не для исполнения, ложные срабатывания», и не раздавать как задачу.

**P0-2. `docs/LIVE_BRIDGE.md:58` + `addons/live_bridge/live_bridge.gd:454` — неверная сетка тайлов.**
Док: «Сетка `tile_to_screen` предполагает клетку 32 px — при смене размера тайлов править здесь».
Код: `live_bridge.gd:454` `var world := Vector2(tx * 32.0 + 16.0, ty * 32.0 + 16.0)  # сетка 32 px`.
Реальность: `MapRenderer.cs:35` `TileSizePx = 64`.
→ Любой клик по «клетке» через `live.py tile` попадает **вдвое ближе к нулю**, т.е. в другую клетку. Для агента, тестирующего UI кликами, это систематическая ошибка.

**P0-3. `AI_PROJECT_MAP.md` — «карта», по которой агент выбирает файлы.** См. §3: `main_scene` неверен, мир 512 vs 1024, `MapGenerator`/`AgentGpuComputeService`/`GardenController` отсутствуют. Действие: перевыпустить или поставить шапку-предупреждение.

**P0-4. `GLOBAL_AUDIT_REPORT.md` + `PLAN_GLOBAL_AUDIT_GPU.md` (2026-10-05).** Документы целиком построены на архитектуре, которой больше нет:
- `GLOBAL_AUDIT_REPORT.md:3` «**Дата**: 2025» (в проекте 2026 год);
- `:24-30` таблица тестов: `WorldGenerationTests`, `ExtremeCoordinateTests`, `StreamerChurnTests`, `WorldChunkCodecTests`, `CorruptChunkTests`, `DeltaCodecStressTests` — **0 из 6 существуют** (в `tests/` 30 `.cs`: `TestWorldGen.cs`, `TestWorldGenV2.cs`, `TestDestroyerStreaming.cs`, …);
- `PLAN_GLOBAL_AUDIT_GPU.md:32-40` «Ключевые файлы»: `CoordinateHash.cs`, `NoiseGenerator.cs`, `WorldFeatureGenerator.cs`, `WorldChunkGenerator.cs`, `RegionTraitProvider.cs` — **5 из 5 отсутствуют** (есть только `RegionTraits.cs`).
→ Действие: перенести в `docs/archive/` с шапкой «аудит старой архитектуры, файлы удалены».

**P0-5. `docs/WorldDeltaApiAudit.md:14`** — «payload-байты `Data` сейчас всегда пустые (см. §6.8)». Реально: `WorldDeltaKind.cs:36-50` описывает полную схему v1, `IslandPayload.cs:38-108` её пишет/читает (`SchemaVersion=1`), `IslandStateSnapshot.RefreshPayloads` заполняет перед сейвом. Документ (2026-10-19) устарел на следующий день — §119 P7б.

**P0-6. `audit-cubemeter.md` (в родителе, 21 КБ)** — аудит **другого проекта**: `game.1t.ru`, сим `colorMatrixSum`, JS-бандл `CubemeterScene-srnYJorp.js` (1.96 МБ). К этой игре не имеет отношения. → убрать из дерева (или в `_drafts/`), иначе агент потратит контекст на «нейросеть в задании ученика».

### P1 — на этой неделе

**P1-1. `AI_PROJECT_MAP.md:11-12` выдаёт нарушения D-08 за «оптимизации».** Формулировки «радиус еды: 3 чанка вместо 32, ~100x меньше работы под lock» и «лимит радиуса 4/4/3 чанка вместо 32» — это буквально «сокращать дальности запрещено» из D-08. Пока документ это подаёт как достижение, следующий перф-агент повторит приём.

**P1-2. `PLAN.md` §0 (строка 75)** описывает аудит корня `C:\s\bebebe-main` — другой копии проекта; §0.1 ссылается на `src/core/world/MapGenerator.cs` (нет). Читатель начала файла получает неверную картину.

**P1-3. `_audit/CYCLE_BREAK_DESIGN.md`** — «все 4 «цикла» проверены по фактическому коду» (строка 5), TL;DR-таблица (строки 11–16) объявляет циклы #2 и #3 реальными. Сейчас это неверно **по разным причинам**:
- цикл **#2** (`WorldAgentRenderer.cs:27,29` ↔ `WorldJobTicker.cs:115`) — **объекты удалены** (`WorldAgentRenderer.cs`, `WorldJobTicker.cs`, `WorldAgent.cs`, `IWorldAgentSource.cs` отсутствуют; plandrift их фиксирует);
- цикл **#3** (`FarmController.cs:222,233` ↔ `WorkZoneController.cs:237,313`) — **разорван посредником**: на указанных строках теперь `ZoneWindowRegistry.FarmJobDisplay` / `.WorkWindow` (`FarmController.cs:223,234`) и `ZoneWindowRegistry.FarmWindow` (`WorkZoneController.cs:320`), прямых перекрёстных вызовов контроллеров нет;
- `viz_cli cycles --scope src` = **0**.
Из 34 КБ полезна только §1 (доказательство, что «цикл #1» — артефакт склейки имён) и находка про `_ExitTree` (строка 20: «не обнуляют статический `Instance`»).

**P1-4. `_audit/cycles_after/all_deps.md`** — «Всего файлов: **413**» (строка 2) против фактических **301** (`viz stats`); в тексте `src/ui/streaming/WorldAgentRenderer.cs`, `WorldAgent.cs`, `WorldJobTicker.cs`, `tests/Test10kBenchmark.cs`… — удалены.

**P1-5. `_audit/PLAN_APPENDIX_115.md` / `_116.md`** — 115 дублирует `PLAN.md:6538+` и ссылается на `WorldAgentRenderer`/`IWorldAgentSource` (удалены: plandrift `PLAN.md:7172…` → `src/ui/streaming/WorldAgent.cs` missing). 116 актуален по существу (пирамида).

**P1-6. `PLAN.md` 82 мёртвые ссылки** — в т.ч. **внутри §119** (`PLAN.md:7168` `src/core/agents/AgentDataPool.cs`, реально `src/simulation/agents/`). Самый свежий раздел уже врёт путём.

### P2 — «гигиена»

**P2-1. `AGENTS.md`** — см. §7 ниже: модель верна, но процесс ушёл вперёд (`_audit/`, `_reports/`, `DECISIONS.md`, MCP, viz — ничего не описано).

**P2-2. `dependency_graph_visualizer/VIZ_API.md`** — 15 инструментов ✅, но:
- `:42` «`--repo PATH` (по умолчанию `../bebebe-main`)» — при текущей раскладке (визуализатор **внутри** проекта) дефолт резолвится в `…\bebebe-main\bebebe-main` и **не работает**; фактически `--repo` обязателен;
- `:73` «15 проверок против реального репозитория» — сейчас `FAILED (failures=10)`;
- не сказано про `PYTHONUTF8=1` для `plandrift` (падает на кириллице/`↔` в cp1251-консоли).

**P2-3. `_audit/AUDIT-{A,B,C}`** — качественные read-only аудиты от 2026-10-18 с `file:line`. **Статус находок нигде не проставлен** — читатель не знает, что уже исправлено. Проверено выборочно:

| Находка | Статус на 2026-10-23 | Доказательство |
|---|---|---|
| C-01 `OnStoneMined` подписан на каждую генерацию карты | **закрыта** | `MapRenderer.cs:392-395`: комментарий «Подписка строго один раз: `ApplyMap()` вызывается при каждой загрузке карты, и подписка внутри него накапливала N обработчиков», подписка вне `ApplyMap`, отписка в `_ExitTree` (`:1799`) |
| C-02 крестик `esc` в окне Work-зоны не привязан | **закрыта** | `WorkZoneController.cs:163-172`: поиск по `esc`/`Escape`/`Close`/`Exit`/`Back` + `Pressed += …` (`:174`) |
| «Отдельная находка» из `CYCLE_BREAK_DESIGN.md:20-23`: `_ExitTree` не обнуляет `Instance` | **закрыта** | `FarmController.cs:412-423` и `WorkZoneController.cs:699-713` — отписки + сброс статики, комментарий «Симметрично BuildMenuController/HUDController» |
| C-03 `AgentRenderer._hasInitialSnapshot` не сбрасывается при `Initialize` | **не проверялось** — нужен исполнитель | — |
| C-04 один флаг `_isMouseDown` на ЛКМ+ПКМ | **не проверялось** — нужен исполнитель | — |
| C-05 `ZoneDraftTool`: `IsSelecting`/`_isRightDragging` залипают в door-режиме | **не проверялось** — нужен исполнитель | — |

→ Действие: дописать в оба файла колонку «статус» (или шапку «часть находок закрыта, сверяйся с кодом»).

**P2-4. `_reports/deps_all.md`** — 301 файл ✅, но 1983 ребра против 752 у `vizcore` (другое определение рёбер: `app.exe` считает по имени, `vizcore` — только по однозначным символам). Как «карта» дублирует `viz_cli impact`; ценность — только историческая.

---

## 6. Тесты: покрытие, дыры, висячие ссылки сцен

### 6.1. Что есть

`tests/`: **71 файл** — 30 `.cs` + 41 `.gd` (+ 71 `.uid`).
Паттерн: `.gd` — `@tool extends SceneTree`, `_init()` грузит C#-класс и зовёт `Run()`; `.cs` — сам прогон (`Test*`, `Bench*`, `PhaseNStressTestRunner`).
Запуск — **только** `godot_run_headless --script tests/run_*.gd` (зафиксировано в `PLAN.md:7114-7118`: «pwsh/bash — signal 11, запрещено»).

### 6.2. Покрытие (`viz_cli coverage`)

`src_files=207`, `covered=139` → **67,1 %**. Не покрыто **68** файлов.

`untested_hot_files` (11) — из них реально горячие и непокрытые:
- `scenes/main/Main.cs` — bootstrap/`_Process`-насос (проверяется только «глазами» через LiveBridge);
- `src/core/world/climate/PlanetaryHydrology.cs`;
- `src/simulation/simd/SimdNeedsBatch.cs`;
- `autoload/mcp_interaction_server.gd`, `autoload/agent_tools.gd` (инфраструктура, ожидаемо);
- остальные 6 — сами `tests/Test*.cs` (метрика считает их «горячими», потому что на них ссылаются `.gd`-раннеры — **шум метрики**).

Крупные непокрытые зоны (по `untested`): весь `src/core/jobs/*` (enum-ы — шум), `src/core/world/streaming/storage/*` (кодеки чанков!), `src/simulation/offline/*` (4 файла — оффлайн-тикер), `src/simulation/pathfinding/NavGridState.cs`, `src/simulation/scheduling/{IDynamicWorkScheduler,WorkItem,WorkQuantum}.cs`, `src/ui/tools/*` (Build/Farming/Select/StoneMining/TreeFelling/Warehouse — 6 инструментов без тестов), `src/ui/controllers/{ZoneWindowRegistry,WallMaterialRegistry}.cs`.

**Вывод:** алгоритмическое ядро (пирамида, диспетчер, пасфайндер, зоны, GPU) покрыто; **слой UI/инструментов и storage-кодеки — нет**, а именно там живут «клик не работает» и «сейв потерялся».

### 6.3. Висячие ссылки и мёртвые сцены

- `dangling`: **`missing_resource_refs: []`, `connections_without_method: []`** → `.tscn` целы, сигналы не висят. Это единственная «зелёная» метрика.
- `deadcode.scenes_not_referenced` = 9, но **истинных сирот только 3**:

| Сцена | Реальность |
|---|---|
| `scenes/ui/Garden.tscn` | **заглушка 34 байта**, `FarmController.cs:12` утверждает «удалён» — файл остался |
| `scenes/ui/villager.tscn` | ни одной ссылки в коде/сценах — сирота |
| `scenes/infinite_world/infinite_world_game_demo.tscn` | демо — сирота |
| `scenes/ui/Build.tscn` | ❌ ложное: `BuildMenuController.cs:91` `GD.Load("res://scenes/ui/Build.tscn")` |
| `scenes/ui/Farm.tscn` | ❌ ложное: `FarmController.cs:60` |
| `scenes/ui/JobDisplay.tscn` | ❌ ложное: `WorkZoneController.cs:96` |
| `scenes/ui/Prioritet.tscn` | ❌ ложное: `HUDController.cs:609` |
| `scenes/ui/WorldMap.tscn` | ❌ ложное: `Main.cs:266` |
| `scenes/ui/menu.tscn` | ❌ ложное: это `run/main_scene` (`project.godot:14`) |

→ **Не давать этот список исполнителю как задачу на удаление** (иначе удалят рабочие окна). Нужен FEAT-D06.

### 6.4. Известные красные/сомнительные тесты

- `PLAN.md:7215-7218`: `run_job_race_stress` (task-20) **красный** по инварианту Cd-агрегатов: `131127 vs 131135`. Зафиксировано как «владельцу task-20», P2 этот путь не трогает.
- `tests/smoke_build_menu.gd` — смоук `Build.tscn`; `PLAN.md:7123-7125` фиксирует, что `test_streamingview_jobdisplay_smoke.cs:64` был несовместим с API и правился кросс-скопом.
- `dotnet build` — по `_reports/02,03,05` «0 ошибок, 6 предупреждений CS0219 в `tests/TestJobRaceStress.cs`».

### 6.5. Нарушения слоёв (не документированы нигде)

`viz_cli layers`: **21** нарушение, все — `core → simulation` (20) и `core → ui` (1):
- `src/core/world/mutation/IslandDomainReplay.cs` → `simulation/farming/FarmJobManager` (4), `items/StockpileManager` (3), `structures/StructureManager` (2), `structures/ConstructionManager` (2), `stones/StoneJobManager` (2), `trees/TreeJobManager` (2);
- `src/core/world/mutation/IslandStateReplay.cs` → `jobs/JobBroker` (2) + ещё 6 файлов симуляции по 1 (`ConstructionSite`, `CropGrowthManager`, `StockpileManager`, `CellProgressStore`, `GroundItemManager`, `ConstructionManager`);
- `src/core/world/mutation/IslandStateSnapshot.cs` → `CropGrowthManager`, `CellProgressStore`, `ConstructionManager` (по 1);
- `src/core/common/{SimEvents,SimulationWatchService}.cs`, `src/core/world/FertilityMap.cs`, `src/core/world/mutation/IslandOverrideStore.cs` → по 1 ссылке в симуляцию;
- `src/core/world/streaming/integration/IWallWorld.cs` → `src/ui/streaming/StreamingWorldView.cs` (единственный **core → ui**).

Правило слоёв (`core` не зависит от `simulation`/`ui`) **нигде не записано** — ни в `DECISIONS.md`, ни в `AGENTS.md`. `PLAN.md §112`/`§115` говорит о циклах, но не о направлении зависимостей. → FEAT-D09.

---

## 7. Отсутствующие функции/инструменты (FEAT-Dxx)

| ID | Что | Зачем (доказательство) | Оценка |
|---|---|---|---|
| **FEAT-D01** | `docs/INDEX.md` — реестр документации с владельцем и датой | 30 уникальных документов, 7 противоречат коду; новому агенту неоткуда узнать, чему верить | S |
| **FEAT-D02** | `docs/` lint: «документ, помеченный как актуальный, обязан ссылаться на существующие файлы» | `plandrift` ловит только PLAN.md; `AI_PROJECT_MAP`, `LIVE_BRIDGE`, `WorldDeltaApiAudit`, `GLOBAL_AUDIT_REPORT` не проверяются никем | M |
| **FEAT-D03** | `viz_cli plandrift`: принудительный UTF-8 stdout (`sys.stdout.reconfigure(encoding="utf-8")`) | воспроизведено: `{"error": "UnicodeEncodeError: 'charmap' … '\\u2194'"}` | XS |
| **FEAT-D04** | `viz_cli`: дефолт `--repo` = искать вверх `project.godot`; сейчас `../bebebe-main` даёт несуществующий путь | `viz_cli.py:25`, `VIZ_API.md:42` | XS |
| **FEAT-D05** | `test_vizcore.py`: тот же авто-детект репозитория (+`VIZ_REPO`) | `Ran 15 tests … FAILED (failures=10)` | XS |
| **FEAT-D06** | `deadcode`: классы-исключения — enum/DTO/interface «leaf», `tests/*.gd` SceneTree-раннеры, сцены, загружаемые строкой, автолоады | `orphans.md` предлагает удалить `JobTypeId.cs`; `scenes_not_referenced` даёт 6 ложных из 9 | M |
| **FEAT-D07** | «doc-claims» тест: проверять числа из `AI_PROJECT_MAP.md` против кода (`AgentCount==10000`, `MapWidth==1024`, `TileSizePx==64`, `MaxWaypoints==96`, `SecondsPerDay==12000`) | 16 расхождений в «карте» (§3) — ровно те, что проверяются grep’ом | S |
| **FEAT-D08** | `viz_cli planindex` — карта разделов PLAN.md (номер → строка → тема → статус) на базе уже существующего `tools/extract_plan_headers.py` | 238 заголовков, 2 дубля номеров, 2 пропуска, 82 битые ссылки | S |
| **FEAT-D09** | Зафиксировать правило слоёв в `docs/DECISIONS.md` (D-11) и в CI-гейте `viz_cli layers` | 21 нарушение, все `core → simulation/ui`, нигде не описано | S |
| **FEAT-D10** | **`JobSpatialPyramid` рассчитан на 512×512, мир — 1024×1024** | `JobSpatialPyramid.cs:14,21,25-27`: L1 «1024 сектора на 512x512», `ChunkDim=32`; L2 `MacroDim=8 × 64 = 512`; `GetChunkIndexStatic` (`:186-190`) **клампит** координаты в 0..31 → все работы при `x≥512` или `y≥512` попадают в один краевой чанк/макрос-бит. Вместе с `MaxClaimScanPerChunk=64` это механизм задержки выдачи работ на дальней половине острова. Документация (AI_PROJECT_MAP `:17`, PLAN §65) утверждает 512 — код мира уже 1024 | **L/XL (P0-кандидат)** |
| **FEAT-D11** | Привести D-08 и код к согласию: либо отменить лимиты радиусов (`NeedsJobSystem.cs:134`, `StockpileHaulingJobHandler.cs:75`), либо оформить их как исключение в DECISIONS | D-08 «сокращать дальности запрещено» нарушено в 2 местах, оба задокументированы в коде как P0-оптимизации | M |
| **FEAT-D12** | Дописать в `DECISIONS.md`: D-11 (60-секундный холд зоны — сейчас только в `PLAN.md:7171-7180`), D-12 (прогресс вскопки не сохраняется) | `WorkerHoldMs=60_000` влияет на «работы ждут», но в ADR отсутствует; `FarmJobManager.TillProgress` без `WorldDeltaKind` | S |
| **FEAT-D13** | Проверить C-03/C-04/C-05 из `AUDIT-C-ui.md` и проставить статусы в самом аудите (C-01, C-02 и `_ExitTree` уже закрыты — см. §5 P2-3) | остались 3 непроверенные находки UI-ввода; без колонки «статус» 180 КБ аудитов нечитаемы | M |
| **FEAT-D14** | Починить `live_bridge.gd:454` (32 → 64 px) или читать `TileSize` из `MapRenderer` | агент кликает мимо клетки (§5 P0-2) | XS |
| **FEAT-D15** | Удалить `scenes/ui/Garden.tscn` (34-байтная заглушка) и `villager.tscn` либо завести на них ссылки | `dangling` чист, но мусор вводит в заблуждение | XS |

---

## 8. Рекомендации: архивировать / объединить / дописать

### 8.1. Архивировать (в `docs/archive/`, шапка «историческое, файлы удалены»)

| Файл | Причина |
|---|---|
| `GLOBAL_AUDIT_REPORT.md` | 0/6 тестов и 5/5 ключевых файлов не существуют; дата «2025» |
| `PLAN_GLOBAL_AUDIT_GPU.md` | то же + GPU уже реализован иначе (§41/§42) |
| `_audit/cycles_after/all_deps.md` (196 КБ) | 413 файлов против 301; ссылки на удалённые файлы |
| `_audit/cycles_after/orphans.md` | опасные ложные срабатывания — **либо архив, либо удалить** |
| `_audit/CYCLE_BREAK_DESIGN.md` | 2 из 4 циклов ссылаются на удалённые файлы; циклов в графе 0 |
| `_audit/PLAN_APPENDIX_115.md` | дублирует PLAN §115, ссылается на удалённое |
| `_reports/deps_all.md` (186 КБ) | дублирует `viz_cli` с другим определением рёбер |
| строки 1–6358 `PLAN.md` | исторические §0–§112 (759 КБ → в архив, активная часть ≈ 45 КБ) |
| `audit-cubemeter.md` | **инородный проект** — вынести из дерева (не в архив проекта) |

### 8.2. Объединить

1. `_audit/PLAN_APPENDIX_116.md` → влить в `PLAN.md §116`, файл удалить (дубль).
2. `_audit/AUDIT-A/B/C` → один `docs/AUDIT_2026-10-18.md` с колонкой «статус находки» (закрыта/жива/не проверялась), иначе читатель не знает, что делать с 180 КБ.
3. `_reports/01…06` → один `_reports/FIXES_2026-10-21.md` (6 отчётов одного дня, один и тот же шаблон «диагноз → правка → build 0 ошибок»).
4. `PLAN_APPENDIX_115/116` + `AUDIT-B` B-01/B-02 → один раздел «разрыв циклов: что сделано» (сейчас это 4 пересекающихся текста).

### 8.3. Дописать / переписать

| Файл | Что дописать |
|---|---|
| `AI_PROJECT_MAP.md` | перевыпуск по состоянию на 2026-10-23: `main_scene=menu.tscn`, мир **1024×1024**, `UnifiedWorldGenerator` вместо `MapGenerator`, 3 GPU-симулятора вместо `AgentGpuComputeService`, `FarmController` вместо `GardenController`, `Stone` в `ItemId`, 10 TileMapLayer, полный Z-список (включая 3/4/−2/999/1000), §1 — 60 недокументированных файлов. До перевыпуска — шапка «⚠️ версия 2026-10-08, §0/§1 устарели» |
| `AGENTS.md` | §7 ниже |
| `docs/DECISIONS.md` | + D-11 (слой-правило), + D-12 (60с-холд зоны), + D-13 (прогресс вскопки не персистится), + правка D-02 («срочный немедленный пуш» — не реализован), + правка D-08 (перечислить исключения или отменить их) |
| `docs/LIVE_BRIDGE.md` | `:58` 32 px → 64 px; добавить «`tile`/`tile_to_screen` сейчас смещены — до фикса использовать `canvas`-координаты» |
| `docs/WorldDeltaApiAudit.md` | `:14` — обратное: payload’ы схемы v1 реализованы (`IslandPayload.cs`), §6.8 закрыт §119 P7б |
| `dependency_graph_visualizer/VIZ_API.md` | `--repo` обязателен при визуализаторе внутри проекта; `PYTHONUTF8=1` для `plandrift`; `test_vizcore` требует `VIZ_REPO` |
| `PLAN.md` §119 | `src/core/agents/AgentDataPool.cs` → `src/simulation/agents/AgentDataPool.cs` (`PLAN.md:7168`) — и прогнать `plandrift` до 0 |

### 8.4. AGENTS.md — соответствие реальному процессу

Что написано: 4 роли — `@architect` (пишет/обновляет `PLAN.md`), `@researcher` (websearch, код не трогает), `@developer` (только `src/`), `@destroyer` (запрещено менять `src/` и `scenes/`, пишет mock-данные и `bad_data.json`).

Что фактически происходит в репозитории:
- **роли исполнялись иначе**: `_audit/AUDIT-{A,B,C}` — «@destroyer / teammate `audit-world`», read-only, но результат — не `bad_data.json`, а 180 КБ отчётов с `file:line`; в `_reports/` артефакты `@developer` содержат «dotnet build 0 ошибок» и правки **вне** `src/` (`FarmController`, `WorkZoneController`);
- **`@researcher`** — следов нет: web-синтез живёт внутри `PLAN.md` §1/§19/§38/§48/§50 («Исследование `@researcher`»), отдельного артефакта нет;
- **`PLAN.md` пишут все** (40 H1-разделов от «@architect», §117/§119 — от исполнителей), правило «@architect пишет PLAN до кода» не соблюдается: §118/§119 содержат «СТАТУС P0/P1/P2 … выполнено» — т.е. план и отчёт об исполнении в одном файле;
- **появилось 5-е пространство** — `docs/` (`DECISIONS.md`, `LIVE_BRIDGE.md`, 2 аудита) и `_plan/` (выводы viz); в AGENTS.md о них ни слова, как и о самих инструментах: `godot-bridge`/LiveBridge, `viz_cli`, `godot_run_headless` вместо pwsh (жёсткое правило из `PLAN.md:7114-7118`!);
- **нет правила «не исполнять `orphans.md`»** и нет правила «проверять `AI_PROJECT_MAP` по коду» — а именно это привело к §5 P0-1/P0-3.

Что дописать (предложение):
1. §0 «Источник истины»: `src/` > `DECISIONS.md` > активная часть `PLAN.md` > всё остальное (`AI_PROJECT_MAP`, `_audit`, `_reports` — снимки, не истина).
2. §5 «Артефакты и где им место»: `PLAN.md` — только план (без «СТАТУС выполнено»), `_audit/` — read-only аудиты с колонкой статуса, `_reports/` — отчёты об исполнении, `docs/DECISIONS.md` — «почему», `_plan/` — машинные выводы.
3. §6 «Инструменты»: `viz_cli` (обязательный `--repo`), `godot_run_headless` (никаких pwsh/bash для Godot — signal 11), LiveBridge (`tile_to_screen` смещён до фикса).
4. §7 «Запреты»: не удалять файлы по `deadcode`/`orphans` без проверки `viz_cli callers`; не доверять числам из `AI_PROJECT_MAP` без grep; не менять `scenes/*.tscn` имена нод (HUD `FindChild` с опечатками — `AI_PROJECT_MAP:100`).
5. §8 «Definition of Done»: `dotnet build` 0 ошибок + профильный headless-тест + обновлённый `PLAN.md`-раздел + запись в `DECISIONS.md`, если решение архитектурное.

---

## Приложение A. Куда смотреть дальше (для лида)

1. **FEAT-D10** (`JobSpatialPyramid` 512 vs мир 1024) — единственная находка, которая объясняет поведение, а не только текст; стоит проверить в живой игре: поставить задачу на клетке `(700, 700)` и замерить задержку до выдачи.
2. **D-02 + 60с-холд + `MaxClaimScanPerChunk=64`** — три независимых механизма, каждый даёт «работы ждут десятки секунд»; все три — в выдаче работ (`ZoneJobRouter.cs:95,102`, `JobSpatialPyramid.cs:41,1120`).
3. **D-08 нарушен** — если перф-агент снова «урежет дальности», формально он будет прав по `AI_PROJECT_MAP:11-12` и неправ по `DECISIONS.md`. Нужно решить, что главнее, и записать.