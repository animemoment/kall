# J-track: работы, диспетчеризация, агенты

**Статус документа:** статический анализ кода, **БЕЗ рантайм-замеров** (игра не запускалась).
**Дата:** 2026-10-23. **Автор:** субагент `jobs-agents`.
**Область:** `src/simulation/jobs/**`, `src/simulation/zones/**`, `src/simulation/agents/**`, `src/simulation/structures/ConstructionManager.cs`, `src/simulation/farming/FarmJobManager.cs`, `src/simulation/items/GroundItemManager.cs`, `src/ui/tools/*` (только точки входа), `src/core/world/activearea/ActiveArea.cs`.
**Единственный созданный файл — этот отчёт.** `src/`, `scenes/`, `resources/`, `project.godot`, `*.tscn`, `*.cs`, `*.cfg` не изменялись (правило READ-ONLY).

**Соглашение об обозначениях симптомов игрока:**
| ID | Жалоба пользователя (дословно) |
|---|---|
| **S1** | «Рабочие не идут за область 1024 для выполнения работ, даже если работа в 10 тайлах от них» |
| **S2** | Работа выдаётся очень медленно: 2 работы ждут десятки секунд, рядом ходит ~1000 человек |
| **S3** | «Назначил работу, агенты её выполнили, но потом вижу условно 200–300 агентов „работа.т“, хотя её нет» |
| **S4** | Иногда жители не строят, даже когда всё расчищено |
| **S5** | Фризы при массовом назначении: ферма ~70 000 клеток, менеджер пытался выдать 2000 работ сразу |
| **S6** | Убрать гейт «вне активной области строительство недоступно» |

Дефекты ниже нумеруются **J-01 … J-23** (это ID дефектов, не симптомы).

---

## 0. Метод и инструменты

### 0.1 Обязательный графовый визуализатор

Все прогоны — из корня проекта `D:\bebebe-main (1)\bebebe-main` с `--repo "D:/bebebe-main (1)/bebebe-main"`.

| Команда | Результат (сжато) |
|---|---|
| `stats` | 301 файл (238 `.cs`), **2136 символов, 752 ребра, 2295 методов, 71 тестовый файл, errors: 0**. `top_fan_out`: `AgentSimulationThread.cs` = 32, `Main.cs` = 26. `top_fan_in`: `DynamicWorkScheduler.cs` = 26, `ConstructionManager.cs` = 22, `ParallelRng.cs` = 21, **`JobDispatcher.cs` = 20**, `ActiveArea.cs` = 20, **`JobBroker.cs` = 19**, `AgentMovementService.cs` = 19, **`GroundItemManager.cs` = 18**, **`FarmJobManager.cs` = 17** |
| `impact --file src/simulation/jobs/JobDispatcher.cs` | 20 прямых потребителей, транзитивно — вся `Main.cs`, все хендлеры, все менеджеры работ и зон |
| `impact` (пакетно) | `JobSpatialPyramid.cs` → direct **7** / transitive **69**; `JobBroker.cs` → **19 / 87**; `AgentDataPool.cs` → **16 / 16**; `ZoneJobRouter.cs` → **10 / 83**; `ConstructionManager.cs` → **22 / 84**; `GroundItemManager.cs` → **18 / 89**; `JobValidator.cs` → **1 / 11**; `WorkerAssignmentSystem.cs` → **1 / 9** |
| `cycles --scope src/simulation` | **один цикл `c1` из 39 файлов** — вся подсистема (см. §4.1) |
| `layers` | Нарушения: `core → simulation`. `IslandDomainReplay.cs` → `FarmJobManager` (4 ссылки), `StockpileManager` (3), `StructureManager` (2), `ConstructionManager` (2), `StoneJobManager` (2), `TreeJobManager` (2); `IslandStateReplay.cs` → `JobBroker` (2) — см. §4.3 |
| `deadcode` | `unreferenced_files` содержит `src/simulation/agents/IdleWorkerSpatialGrid.cs`, `src/simulation/jobs/GenericJobSpatialIndex.cs`, плюс ложные срабатывания (`IJobHandler.cs`, `AgentState.cs`, `src/core/jobs/*`); **`unreferenced_symbols: 0`** — символьного анализа де-факто нет |
| `coverage` | `src_files: 207`, `covered: 139`, **`67.1 %`**. Не покрыты (релевантные): `src/simulation/agents/WorkerAssignmentSystem.cs`, `src/core/jobs/*`, `src/core/world/GridHelper.cs`, `src/simulation/agents/AgentState.cs`, `CropData.cs`, `FarmZone.cs` |
| `mutators --state States` / `--state JobId` | вернул **только объявления полей** (`AgentDataPool.cs:147`, `ZoneJobRouter.cs:50/68/87`) — для переписи мест записи бесполезен (см. §0.3) |
| `hotpath` | мой прогон печатал `entry_points` (в основном `.gd`-мосты `live_bridge/mcp_interaction_server/perf_probe`, затем `Main._Ready:173`, `Main._Process:569`) и обрывался на обрезке пайплайна `Select-Object`; для этой подсистемы почти ничего не даёт, т.к. все точки входа — из sim-потока, а не из `_Process` |

### 0.2 Как проверялись утверждения

1. Каждый факт — чтение исходника (`read`) с цитированием `файл:строка`. Где строка не указана — утверждение помечено как гипотеза.
2. Поиск вхождений — только инструментом `grep` (не `Select-String` по glob: в этой оболочке `src\**\*.cs` **не раскрывает подкаталоги**, что уже приводило к ложным «пусто» — см. §0.3).
3. Профильные метрики пользователя сопоставлены с `GameProfiler.*` вызовами (`CallerMemberName`-скоупы `GameProfiler.Scope()` дают имя вида `Класс.Метод`, поэтому `AgentSimulationThread.Phase2_ParallelUpdate` — это ровно `AgentSimulationThread.cs:715`).
4. Всё, что нельзя доказать кодом, помечено **`[требует рантайм-проверки]`** с указанием конкретного замера.

### 0.3 Ограничения инструментов (важно для доверия к §4)

- **`mutators` бесполезен для SoA-кода**: визуализатор работает на синтаксическом уровне и для `--state States`/`JobId` вернул объявления полей, а не места записи (`pool.States[i] = ...`). Перепись записи сделана `grep`-ом.
- **`deadcode` даёт ложные срабатывания** (`IJobHandler`, `AgentState`, `JobData`, весь `src/core/jobs/*` используются). Поэтому «мёртвость» `GenericJobSpatialIndex.cs`/`IdleWorkerSpatialGrid.cs`/`WorldJobBoard.cs` подтверждена **независимо — `grep` по всему репозиторию**.
- **`hotpath` неприменим** к подсистеме: горячий путь идёт из `Task.Run`-петли `AgentSimulationThread`, а не из `_Process`/`_Ready` Godot.
- Часть `.cs` читалась инструментом `read` (UTF-8), а не `Get-Content`: в консоли часть русских комментариев выглядит «кракозябрами» из-за ANSI-декодирования оболочки. **Это артефакт чтения, а не дефект файлов** — в отчёт как дефект не включено.

---

## 1. Карта подсистемы и тайминги

### 1.1 Поток данных (кто в кого пишет)

```
UI / main thread                                  sim thread (Task.Run, ~20 Гц, 4–5 суб-степов)
──────────────                                    ───────────────────────────────────────────
FarmingTool.cs:92 ────────┐
ZoneDraftTool.cs:771 ─────┤ MarkPlotsBatch          FarmJobManager.cs:68-131
                          │  (per-cell: GroundItemManager.HasItemsAt  ← lock(_lock))
                          │  (per-cell: TrySpillItemsAdjacent / MarkTreesBatch / MarkStonesBatch)
                          ▼
                    RegisterFarmPlotBatch ──┐
BuildTool.cs:199 → ConstructionManager.StartSitesBatch:114 ──► JobBroker.RegisterBlueprintBatch
TreeFellingTool  → TreeJobManager.MarkTreesBatch ───────────► JobBroker.RegisterTreeChopBatch
                                                             │
                                                             ▼
                              JobSpatialPyramid.RegisterJob/RegisterBatch   (lock _registerLock)
                              ├─ _posMap[(x,y,type)] дедуп, free-list, _chunkJobs[L0]
                              └─ macro-mask 64 бита (8×8 по 64 тайла)
                                                             │
                              ZoneJobRouter.Push  ── coverage.TryGetOwner(x,y,type)
                                 ├─ крытая клетка → очередь зоны (_zoneQueues[zoneId])
                                 └─ ничья      → _uncovered (репуш через 30 с)
                                                             │
        ┌────────────────────────────────────────────────────┴───────────────────────┐
        ▼ каждые dispatchInterval = 0.25 игросек  (AgentSimulationThread.cs:506-526)  │
  ZoneManager.DispatchZones:651-786   (бездельники ОБЩЕГО пула внутри чанков Work-зон, 4 зоны/проход)    │
  ZoneJobRouter.Sweep:298-372         (дренаж _uncovered + чистка очередей)  ← БЕЗ скоупа
  JobDispatcher.DispatchPendingJobs   (только AgentClass.Laborer, idle-пул ≤4096)      │
        ▼                                                                             │
  TryClaimJobForAgent → режим «свой чанк → кольцо 1..3 → макро-скан 64×16»            │
        ▼                                                                             │
  handler.OnStart → States = MovingToSource / Working                                  │
        ▼                                                                             │
  Phase2 (Parallel, DynamicWorkScheduler) → ExecuteParallel → MoveTowards → (Phase3) A*
  Phase3a (Parallel) → BookkeepSingleAgent* (нужды, поиск работы, Worker-зоны)
  Phase3b (Sequential) → Commit → Complete → RemoveJob + States=Idle + IdleWorkers.Add
```

### 1.2 Тик: где живут метрики пользователя

| Что | Скоуп профиля | `file:line` | Периодичность |
|---|---|---|---|
| Весь цикл шагов | `Simulation.TotalStepCycle` | `AgentSimulationThread.cs:400` | каждый цикл |
| Phase2 (движение, работа, нужды) | `GameProfiler.Scope()` → **`AgentSimulationThread.Phase2_ParallelUpdate`** | `:715` (вызов `:576`) | каждый суб-степ |
| Phase3a (бухгалтерия) | `Simulation.Phase3a_Fused` | `:952` (вызов `:577`) | каждый суб-степ |
| Phase3b rebuild / commit | `Simulation.Phase3b_Rebuild` / `Simulation.Phase3b_Commit` | `:1342` / `:1357` (вызов `:578`) | каждый суб-степ |
| Диспетчеризация работ | **`Simulation.JobDispatch`** | `JobDispatcher.cs:56` | `_dispatchTimer >= 0.25` игросек (`:506`) |
| Диспетчеризация зон | `Simulation.ZoneDispatch` | `:515` | там же |
| **Роутер зон (Sweep)** | **скоупа НЕТ** | вызов `:522`, тело `ZoneJobRouter.cs:298-372` | там же |
| Складской sweep | `Simulation.Sweep` | `:536` (гейт 2 с wall внутри `JobBroker.cs:280-296`) | `_stockpileSweepTimer >= 1.0` |
| Сверка строек | `Simulation.PipelineReconcile` | `:543` (гейт 5 с wall внутри `ConstructionManager.cs:647-650`) | там же |
| Аудит работ | `Simulation.JobAudit` | `:555` | 30 с **реального** времени (`:551-552`) |
| Влажность | `Simulation.Humidity` | `:444` | 30 игросек, масштабируется |
| Рендер агентов | `Render: Agent MultiMesh` | `src/ui/renderers/AgentRenderer.cs:415` | кадр |

**Следствие для чтения профиля пользователя:** рост `Simulation.JobDispatch` (16→85 мс/с) — это только `CollectIdleWorkers` + цикл клейма. **Стоимость `ZoneJobRouter.Sweep` не видна ни в одной метрике** и падает в остаток `Simulation.TotalStepCycle`. Это отдельный дефект наблюдаемости (см. FEAT-J05).

### 1.3 Числа (все — из кода)

| Константа | Значение | Где |
|---|---|---|
| `DispatchIntervalBase` | 0.25 игросек | `AgentSimulationThread.cs:174` |
| `IntervalScaleSpeed` / `Hi` | 16 / 64 (квадратичное замедление выше 64×) | `:181-182` |
| `dispatchScale` | `ceil(speed/16)`, **кап 2** — и **не используется** (см. J-20) | `:513-514`, `:523` |
| `ParallelThreshold` | 128 | `:183` |
| `MaxDispatchBudgetMs` | **0.8 мс** | `JobDispatcher.cs:38` |
| Проверка бюджета | **раз в 64 агента** (`(n & 63) == 0 && n > 0`) | `JobDispatcher.cs:89` |
| `MaxBatchWorkers` | 4096 (и `toCollect = min(idleCount, 4096)`) | `:28`, `:64` |
| `MaxAssignmentsPerTick` | 4096 — **объявлена, не используется** | `:22` |
| `MaxClaimScanPerChunk` | **64** | `JobSpatialPyramid.cs:41` |
| Индекс L0 | `ChunkShift=4`, `ChunkDim=32` ⇒ **512×512 тайлов** | `:20-27` |
| Индекс L1 | `MacroShift=6`, `MacroDim=8` ⇒ 8×8 макро по 64 тайла = 512 | `:20-27` |
| Кольцо локального скана | r = 1..3 (48 чанков) | `:987-1013` |
| Глобальный макро-скан | 64 макро × 16 чанков | `:1015-1043` |
| `WorkerHoldMs` | **60 000 мс** (работа ждёт Worker'а зоны) | `ZoneJobRouter.cs:102` |
| `RepushAfterMs` | 30 000 мс (непокрытые) | `:95` |
| Watchdog: порог | **8 игросекунд** без прогресса | `AgentSimulationThread.cs:1224` |
| Watchdog: кулдаун | **15 000 мс** | `:1317` |
| Окно watchdog'а | каждый 4-й суб-степ, O(N) | `:1254` |
| Кулдаун «застрял» у хендлеров | 5 с (`ReleaseJobWorkerForStuck` дефолт `:244` / `MaxStuckDuration`) | `StockpileHaulingJobHandler.cs:11`, `HarvestJobHandler.cs:68` |
| Кулдаун аудита | 30 000 мс | `JobValidator.cs:172` |
| Аудит: период / порция / probes | 30 с / 2048 работ / 8 агентов | `JobValidator.cs:20, 23, 76` |
| Зоны: `WorkersPerChunkBudget` / `MaxAssignPerChunk` / `MaxZonesPerPass` | 48 / 8 / 4 | `ZoneManager.cs` (см. §2 J-18) |
| Стройка: `WaitForAllSiteResources` | **true** | `BuildConfig.cs:33` |
| Стройка: `BuildDurationSec` | `SecondsPerHour*4` = 2000 игросек | `BuildConfig.cs` |
| Стройка: гейт сверки | 5 с wall | `ConstructionManager.cs:647-650` |
| Складской sweep: стоп-кран | **`if (UnclaimedCount > 2048) return;`** | `JobBroker.cs:301` |
| Складской sweep: гейт | 2 с wall | `JobBroker.cs:280-296` |
| Активная область | 1024×1024 | `ActiveArea.cs` (`Size = 1024`) |
| `MapData` = размер области | 1024×1024 | `MapDataShim.cs:45, 63` |

### 1.4 Пропускная способность выдачи (расчёт из кода, не замер)

**Сколько агентов реально осматривается за один вызов диспетчера.**

```
бюджет = 0.8 мс, но проверяется на n = 64, 128, 192 …
⇒ между проверками осматривается до 64 агентов
⇒ потолок одного вызова = 64 × (стоимость промаха)
```

Стоимость промаха = полный проход `TryClaimJobForAgent`: свой чанк (≤64 кандидата) → кольцо 48 чанков → 64 макро × 16 чанков, причём на «проходных» кандидатах вызывается `handler.CanAgentExecute` (внутри — `lock(_lock)` у `ConstructionManager`/`GroundItemManager`) и `JobPriorityManager` (2 выборки на кандидата). Комментарий самого кода (`JobSpatialPyramid.cs:97`) оценивает худший случай в **≈5.7 мс на агента**.

| Сценарий | Стоимость промаха | Потолок вызова | Агентов за вызов при 0.8 мс |
|---|---|---|---|
| Работ нет в радиусе, чанки пусты | ~2–5 мкс | ~1 мс | 160–400 |
| Работы есть, но все на кулдауне/заняты | 20–100 мкс | 1.3–6.4 мс | 64–128 |
| Работы есть, `CanAgentExecute` с локами | 0.5–2 мс | 32–128 мс | 64 (ровно один шаг проверки) |
| Худший случай кода (макро-скан 1024 чанка) | **5.7 мс** | **≈365 мс** | 64 |

**Наблюдаемые пользователем спайки 60–370 мс попадают ровно в эту вилку** — это совпадение порядка, не доказательство (см. `[требует рантайм-проверки]` в J-01).

Частота: `dispatchInterval = 0.25` игросек; при 4–5 суб-степах по 0.05 игросек это ~5 суб-степов между диспатчами; профиль показывает 13–29 циклов шагов в секунду ⇒ **2.6–5.8 вызовов диспетчера в реальную секунду на 1×**. Значит 1000 бездельников «осматриваются» со скоростью 170–3700 агентов/с в лучшем случае и 160–640 агентов/с в типичном — то есть **полный обход пула занимает от 0.3 до 6 секунд, а при дорогих промахах — десятки секунд**. Это и есть механизм S2 на верхнем уровне.

**Латентность конкретной работы.** Внутри чанка с `N` работами скан идёт окном 64 с ротацией по глобальной эпохе диспатча (`JobSpatialPyramid.cs:1080-1087`):

```
ждать ≤ (N − 63) диспатчей × 0.25 игросек
```

| Заполнение чанка `N` | Максимальное ожидание (игросек) | ≈ реальных секунд на 1× |
|---|---|---|
| ≤ 64 | 0 (скан полный) | 0 |
| 128 | 65 → 16.3 | ~16 |
| 242 (70 000 клеток на ~17×17 чанков) | 179 → 44.8 | **~45** |
| 512 | 449 → 112 | ~112 |

---

## 2. Дефекты

Сводная таблица, далее — разбор каждого.

| ID | P | Симптом | Кратко | Файл:строка |
|---|---|---|---|---|
| J-01 | **P0** | S2, S5 | Бюджет диспатча проверяется раз в 64 агента ⇒ потолок вызова ~365 мс вместо 0.8 мс | `JobDispatcher.cs:38, 89-94` |
| J-02 | **P0** | S2 | Пул бездельников собирается всегда с индекса 0 и жёстко ≤4096 ⇒ хвост пула не осматривается никогда | `JobDispatcher.cs:28, 64-65, 80-82` |
| J-03 | **P0** | S2, S5 | Окно скана чанка 64 + глобальная эпоха ⇒ «хвост» работы ждёт (N−63) диспатчей | `JobSpatialPyramid.cs:41, 1080-1087` |
| J-04 | **P0** | S5, S2 | `ZoneJobRouter.Sweep` дренирует **всю** очередь `_uncovered` на каждый диспатч, без троттла, вне профайлера | `ZoneJobRouter.cs:298-372`, `AgentSimulationThread.cs:522` |
| J-05 | **P0** | S1, S2 | Индекс работ покрывает 512×512 при острове 1024×1024; всё восточнее/южнее 511 схлопывается в один чанк | `JobSpatialPyramid.cs:20-27` |
| J-06 | **P0** | S1, S6 | За кромкой 1024: нет A*, работа удаляется аудитом, watchdog сбрасывает агента через 8 с + 15 с кулдауна ⇒ вечный цикл «взял—бросил» | `JobValidator.cs:320-327`, `HierarchicalPathfinder.cs:303, 775`, `AgentSimulationThread.cs:1224-1317` |
| J-07 | **P0** | S3 | HUD «занято» = население − бездельники ⇒ любой не-Idle агент (в т.ч. Worker и Evacuating) числится работающим | `HUDController.cs:811-823` |
| J-08 | **P0** | S3 | Watchdog игнорирует агентов с waypoints ⇒ вечный `Working` без прогресса | `AgentSimulationThread.cs:1275` |
| J-09 | **P1** | S3 | `Evacuating` без watchdog прогресса (только `StuckTimer`, а он про нулевую скорость) | `AgentSimulationThread.cs:1187-1193`, `AgentMovementService.cs:496` |
| J-10 | **P1** | S3, S4 | Завершение работ безусловно добавляет агента в общий idle-пул, включая `AgentClass.Worker` ⇒ Worker уходит из своей зоны | `TreeChoppingJobHandler.cs:141`, `ConstructionJobHandler.cs:166`, и ещё 6 мест |
| J-11 | **P1** | S3 | Откат в диспетчере не вызывает `OnCancel` ⇒ резервы (склад/предмет/материал) утекают | `JobDispatcher.cs:132-143` |
| J-12 | **P1** | S2, S4 | Мемоизация `CanAgentExecute` по `jobId`+эпоха, **без agentIndex** ⇒ ответ первого агента применяется ко всем | `JobSpatialPyramid.cs:276-299, 1127-1135` |
| J-13 | **P1** | S2 | Приоритет категории 0 тихо блокирует все работы категории (без сообщения) | `JobPriorityManager.cs:56-63`, `JobSpatialPyramid.cs:1141-1144` |
| J-14 | **P0** | S4 | Строительство: тройной групповой гейт (`Clearance`→`Supplying`→`WaitingAll`) + предусловия на клетку + голод haul-работ при >2048 непокрытых | `ConstructionManager.cs:212-221, 312-345, 386-425`, `JobBroker.cs:301`, `BlueprintDeliveryJobHandler.cs:17-51` |
| J-15 | **P0** | S5 | Массовая разметка идёт на **UI-потоке**, по клетке: `lock(_lock)` + пересчёт покрытия (аллокация 2 МБ на тип) | `FarmingTool.cs:92`, `ZoneDraftTool.cs:771`, `FarmJobManager.cs:68-131`, `ZoneCoverageMap.cs:123-132` |
| J-16 | **P1** | S5 | `DequeueNearest` дренирует **всю** очередь зоны в `Dictionary` на каждый вызов (на каждого Worker'а каждые 2 с) | `ZoneJobRouter.cs:201-247` |
| J-17 | **P1** | S2, S4 | Аудит: 8 «слепых» проб ⇒ живая работа получает 30-секундный кулдаун | `JobValidator.cs:76, 157-174` |
| J-18 | **P1** | S2, S4 | `ZoneManager.DispatchZones` не чтит 60-с холд и берёт работы чужого типа с немедленной отменой (churn `OnStart`/`OnCancel`) | `ZoneManager.cs:651-786` |
| J-19 | **P2** | — | Документация (`AI_PROJECT_MAP.md`) описывает архитектуру 2–3 поколения назад | §2.19 |
| J-20 | **P2** | — | Мёртвый код и мёртвые параметры (~1650 строк + 4 шт.) | §2.20 |
| J-21 | **P2** | S2 | Приоритет/выдача: `jid > 0` вместо `>= 0` ⇒ работа с `jobId == 0` не пушится в очередь зоны | `JobBroker.cs:162` |
| J-22 | **P2** | S5 | Один глобальный `lock(_lock)` на все предметы земли — в том числе на пути выдачи работ | `GroundItemManager.cs:23, 224-232` |
| J-23 | **P2** | — | Диагностика врёт: `[DISP]`-отчёт считает «бездельников в чанке» по плоскому пулу | `AgentSimulationThread.cs:851-880` |

---

### J-01 · P0 · Бюджет диспатча не работает как бюджет

**Файл:** `src/simulation/jobs/JobDispatcher.cs:38, 44-48, 86-94`.

**Механизм.** Комментарий к методу обещает «распределяет весь доступный пул в пределах кванта (< 0.8 мс)» (`:44-48`). Фактически:

```csharp
for (int n = 0; n < collected; n++) {
    if ((n & 63) == 0 && n > 0) {          // :89 — проверка ТОЛЬКО на кратных 64
        if (elapsedMs >= MaxDispatchBudgetMs) break;   // :92
    }
    ...
    TryClaimJobForAgent(...)                // :114 — стоимость не ограничена
```

Между двумя проверками выполняется до 64 полных попыток клейма. Стоимость одной попытки не ограничена ничем: при промахе `TryClaimJobForAgent` обходит свой чанк (≤64 кандидата), кольцо из 48 чанков, затем до 64 макро-блоков по 16 чанков (`JobSpatialPyramid.cs:978-1043`), и на каждом «проходном» кандидате вызывает `handler.CanAgentExecute`, внутри которого — `lock(_lock)` менеджеров (`ConstructionJobHandler.cs:19-25`, `GroundItemManager.HasItemsAt:224-232`).

**Проверено:** статически. Потолок: `64 × 5.7 мс ≈ 365 мс` — верхняя граница, которую даёт сам код в комментарии `JobSpatialPyramid.cs:97`.

**Рантайм:** добавить в конец `DispatchPendingJobs` публикацию `(examined, assigned, elapsedMs)` в `SimEvents.Series`. Сейчас `examined` (`:85, 96`) используется только для курсора (`:182`), а `assigned` (`:84, 146`) **не читается вообще** — то есть «сколько выдано за вызов» не знает никто, включая профилировщик. Замер: гистограмма `elapsedMs` и `examined` на вызов. Ожидание: медиана ~0.8 мс, хвост до сотен мс.

---

### J-02 · P0 · Хвост пула бездельников не осматривается никогда

**Файл:** `src/simulation/jobs/JobDispatcher.cs:28, 64-65, 80-82`; `src/simulation/agents/FlatIdleWorkerPool.cs` (`CollectIdleWorkers` копирует с индекса 0).

**Механизм.** `toCollect = Math.Min(idleCount, MaxBatchWorkers)` (4096). `CollectIdleWorkers` **всегда** копирует с нулевого индекса плоского массива; ротация `_dispatchScanCursor % collected` двигает только стартовую точку **внутри** собранных 4096. Если в колонии 10 000 бездельников (а пользователь говорит про «~1000 рядом» и население 10 000), агенты с индексами ≥ 4096 не попадают в батч до тех пор, пока голова пула не опустеет. `RemoveIdleWorker` — swap-and-pop, поэтому «голова» — это произвольные агенты, а не ближайшие.

Второе следствие: порядок в пуле — это порядок добавления/свопа, а не геометрия. Значит «1000 человек рядом с работой» и «работу взял кто-то с другого конца карты» — это не аномалия, а прямое следствие: клейм достаётся первому осмотренному, а расстояние участвует только в ранжировании **внутри** одного чанка (`JobSpatialPyramid.cs:1147-1156`).

**Проверено:** статически.

**Рантайм:** логировать `idleCount`, `collected`, `scanStart`, `examined` на вызов; проверить, что при `idleCount > 4096` агенты с индексом ≥ 4096 не назначаются (счётчик «назначений по индексам»).

---

### J-03 · P0 · «Хвост» чанка ждёт (N−63) диспатчей

**Файл:** `src/simulation/jobs/JobSpatialPyramid.cs:41` (`MaxClaimScanPerChunk = 64`), `:1074-1087`, `:1093-1157`.

**Механизм.** Скан чанка — фиксированное окно 64 кандидата, смещение `scanOffset = epoch % (scanLimit − scanCount + 1)`, где `epoch` — **глобальная** эпоха диспатча (`BeginClaimEpoch`, `:264-274`). Смещение сдвигается на 1 за диспатч, значит работа на позиции `k` попадает в окно только когда «доедет» до неё: до `scanLimit − 63` диспатчей. Комментарий честно называет это лечением от выжигания кванта (`:1074-1079`), но ценой стала латентность.

**Проверено:** формула из кода + §1.3/§1.4. Для фермы 70 000 клеток на ~17×17 чанков средний чанк содержит ~242 работы ⇒ хвост ждёт ~179 диспатчей ≈ **45 игросекунд на 1×**.

**Рантайм:** залогировать для чанка с `count > 64` позицию выданной работы и число прошедших диспатчей до выдачи (гистограмма «время регистрация → клейм»). Это же — метрика FEAT-J06.

---

### J-04 · P0 · `ZoneJobRouter.Sweep` — полный дренаж на каждый диспатч, вне профайлера

**Файл:** `src/simulation/AgentSimulationThread.cs:519-522` (вызов, без `GameProfiler.ScopeCustom`), `src/simulation/zones/ZoneJobRouter.cs:291-296, 298-372`.

**Механизм.** Комментарий перед вызовом утверждает «Throttle 1р/2с внутри (как у SweepStockpileHaulJobs)» (`:519-521`), а комментарий метода — «Throttle 1р/2с внутри» (`:294-296`). **Внутри `Sweep` никакого троттла нет** — только `Coverage.RebuildIfNeeded` (там троттл 2 с) и `RepushAfterMs = 30 с`, который управляет **только** моментом репуша, но не самим дренажом.

Что происходит на каждом вызове:
1. `:305-331` — если `_uncovered` не пуста: `new List<UncoveredEntry>(_uncovered.Count + 16)`, затем полный `TryDequeue`-дренаж, `TryGetJob` на каждую запись, обратный `Enqueue`. **Два полных прохода по очереди за вызов** (`:305-331` и `:360-370`), суммарно 2 списка на `count` записей.
2. `:336-354` — дренаж **всех** очередей зон под `lock(gate)` с пересозданием `List<QueuedJob>`.

Для фермы 70 000 клеток: ни одна клетка фермы не покрыта зоной (см. `ZoneCoverageMap.cs:109` — владеют только `ZoneKind.Work` с `DesiredWorkers > 0`; `ZoneKind.Farm` в покрытии не участвует), значит все 70 000 работ лежат в `_uncovered`. Каждый диспатч: **140 000 дек/энqueue + 140 000 `TryGetJob` + 2 аллокации списков по 70 000 (≈560 КБ каждый)** — на 2.6–5.8 вызовов/с это 360–810 тыс. операций и ~1–6 МБ мусора в секунду.

**Проверено:** статически (чтение тела метода). Это **самый вероятный механизм фризов S5** и роста `TotalStepCycle`, и он **не виден в профиле**.

**Рантайм:** обернуть вызов `:522` в `GameProfiler.ScopeCustom("Simulation.ZoneSweep")`; смотреть `sys.gc0`/`sys.mem_mb` (уже публикуются, `:384-385`) при разметке фермы 70k.

---

### J-05 · P0 · Индекс работ покрывает 512×512 тайлов при острове 1024×1024

**Файл:** `src/simulation/jobs/JobSpatialPyramid.cs:20-27` (`ChunkShift = 4`, `ChunkDim = 32`, `MacroShift = 6`, `MacroDim = 8`), `GetChunkIndexStatic` (с `Clamp`), `:97`.

**Механизм.** `ChunkDim = 32` × 16 тайлов = **512** тайлов на ось. Остров — 1024 (`ActiveArea.Size = 1024`, `MapDataShim.cs:45`). Для любой клетки `x ≥ 496` вычисленный `x >> 4 ≥ 31` зажимается в `31`. Следствия:

1. Последняя колонка чанков (cx = 31) содержит полосу шириной **528 тайлов** (496…1023) — «чанк 16×16» превращается в «528×16»;
2. То же по оси Y — правый нижний квадрант 512×512 складывается в **один** чанк (31,31);
3. Кольцевой скан r = 1..3 для агента в этой зоне перестаёт быть геометрическим: «соседний чанк» может быть в 500 тайлах;
4. Окно скана 64 (`J-03`) применяется к области, где работ может быть тысячи ⇒ латентность хвоста там максимальна;
5. Чанковые счётчики `_chunkUnclaimedCount` для 31-х чанков раздуты ⇒ гейты «в этом чанке есть непокрытые» (`ChunkMayHaveClaimable`) всегда истинны ⇒ диспетчер честно обходит дорогие пути.

Итог: **на 75 % площади острова (клетки, где x > 511 или y > 511) пространственный индекс деградирует** — локальность теряется, а хвост латентности максимален. Это же объясняет, почему «работа в 10 тайлах» может не находиться: для агента в восточной половине «свой чанк» — это 528×16 полоса, где 64-окно почти наверняка не содержит нужную клетку.

**Проверено:** статически, арифметика из констант.

**Рантайм:** напечатать гистограмму `GetChunkIndexStatic(x,y)` по карте (или посчитать на бумаге: 1024² = 1 048 576 клеток, из них в «честные» чанки попадает 512×512 = 262 144 = **25 %**).

---

### J-06 · P0 · За кромкой 1024 работы физически невыполнимы (S1, S6)

Это **три независимых запрета**, каждый из которых сам по себе достаточен.

**(а) Аудит удаляет работу за кромкой.** `JobValidator.cs:320-327`:

```csharp
if ((uint)job.TargetX >= (uint)ctx.MapWidth || (uint)job.TargetY >= (uint)ctx.MapHeight)
    return false;                       // ← «цель не проходима»
return ctx.Ground[job.TargetX, job.TargetY] == TileType.Grass;
```

`ctx.MapWidth = 1024` (`SimulationContext.cs:49-50`, `MapData` = размер `ActiveArea`). В `AuditIndexSlice` (`:113-118`) `!IsTargetWalkable` ⇒ `UnmarkJob(job)` + `index.RemoveJob(jobId)` + `fixed_++`. То есть **любая** работа с целью вне `[0,1024)²` удаляется в первые 30 с, причём метка менеджера снимается (`UnmarkJob`), чтобы `AuditMarksSlice` её не пересоздал. Пользователь этого не видит — работа «просто не появляется».

Тот же `IsTargetWalkable` удаляет работы на цели `!= Grass` (песок, дорога, вода) — см. J-14.

**(б) A\* не умеет маршрут вне острова.** `HierarchicalPathfinder` инициализируется из островной cost-сетки (`InitializeCost:303`, `RegionOfCell:775`, `CostBlocked:40`); для клеток вне области региона нет. `AgentMovementService.RequestHierarchicalPath:145-191` при неудаче ставит `PathRequestCooldown = 3.0 + rng*2.0` (`:189`), при успехе — 1.5 с (`:183`). **Неудача и исчерпание бюджета неразличимы** — и, как показано в §4.5, «бюджет» (`SearchesPerFrame`, `TryReserveSearch`) вообще никогда не вызывается, т.е. это не бюджетный отказ, а настоящий отказ поиска.

Ходьба при этом возможна: `IsTileBlocked:407-423` для координат вне карты спрашивает террейн стриминга (`UnifiedWorldGenerator` + `WorldCellQuery.TerrainAt`) и блокирует только воду/гору. Значит агент идёт **по прямой**, упирается в воду/гору и не может обойти (обхода нет — A* не работает).

**(в) Watchdog снимает агента через 8 игросекунд и вешает 15-секундный кулдаун.** `AgentSimulationThread.cs:1197-1324`:

```
UnreachableNoProgressSeconds = 8f            (:1224)
шаг watchdog'а — каждый 4-й суб-степ          (:1254)
условие:  States == MovingToSource|MovingToTarget
        && CurrentJobId >= 0
        && WaypointCount == 0                 (:1275)   ← см. J-08
        && прогресс < 2 px за 8 игросек
действие: ReleaseJobWorkerForStuck(..., 15000) (:1317)
```

`JobDispatcher.ReleaseJobWorkerForStuck:244` для **не**-Worker'а дополнительно вызывает `MarkJobUnreachable` (`:252-253`), то есть работа уходит в кулдаун 15 с.

**Итоговая петля для работы за кромкой:**

```
агент берёт → идёт по прямой 8 игросек → watchdog снимает → кулдаун 15 с →
агент №2 берёт (если найдёт) → … → JobValidator удаляет работу навсегда (≤30 с)
```

Пока **все** оставшиеся непокрытые работы в кулдауне, срабатывает O(1)-гейт пирамиды `JobSpatialPyramid.cs:963-976`:

```csharp
if (cdGlobal >= unclaimedNow && now < earliest) return false;   // :975
```

— диспетчер выходит **до** любого скана: 1000 бездельников и 2 работы «ждут» синхронно. Это же объясняет и «2 работы ждут десятки секунд» (S2) даже без кромки: достаточно, чтобы обе были на кулдауне.

**Проверено:** статически. **Рантайм:** воспроизвести: поставить дерево/чертёж на клетку 1030 → проверить, что работа исчезает из `UnclaimedCount` ≤30 с и в логе `[JobValidator] исправлено работ: N` растёт (`:602-603`).

**(г) Строительство за кромкой запрещено политикой (S6).** Гейт — **одна константа**:

```csharp
// src/ui/tools/WorldPlacementPass.cs:37
public const bool WorldBlueprintsSupported = false;
```

Её читает `StreamingToolPlacement.IsWorldBlueprintBlocked:67-68` (`!WorldBlueprintsSupported && !IsIslandCell(...)`), а тот — `TryPlaceWall`/`CanPlaceWall`/`TryPlaceBuilding`/`CanPlaceBuilding` (`:95, 109, 133, 147`). Пользовательское сообщение — `BuildTool.cs:190-197` (`HUDController.Instance?.ShowZoneHint(OutsideActiveAreaHint)` + `GD.Print("[BUILD] вне активной области ...")`) и `StreamingToolPlacement.RefuseWorldBlueprint:74-83` (не чаще 1 раза в секунду, счётчик `RefusedBlueprintCount`).

Причина запрета документирована в `StreamingToolPlacement.cs:15-24` и `WorldPlacementPass.cs:18-35`: **у мировых чертежей нет исполнителя** — `WorldJobBoard` и мир-агенты снесены в §118 фазе 5, а островной пайплайн (`ConstructionManager` + `JobBroker` + пирамида) работает только по `MapData` 1024². Дополнительно: раньше чертёж ставился только пока чанк резидентен (окно стриминга ездит за камерой) ⇒ «чертёж поставился и исчез».

**Важное уточнение, которое меняет план работ:** `WorldJobBoard` **не удалён** — файл `src/ui/streaming/WorldJobBoard.cs` (106 строк) существует и полностью функционален (`Refresh` собирает планы загруженных чанков, `TryTakeNearest` с фильтром `allow`, `Complete` с откатом `LostPlans`), но **инстанцируется только в тесте** `tests/TestDestroyerStreaming.cs:422`. То есть запрет вызван не отсутствием кода очереди, а отсутствием **потребителя** (агента/цикла, который её крутит) — см. FEAT-J09.

Рубка/добыча/склад/ферма за кромкой проходят гейт (`StreamingToolPlacement.cs:157-171` пишут `CutTreePlan`/`MinedStonePlan`/`StockpilePlan`/`CropPlan`), но executability у них та же, что в п.(а)-(в): метка мира есть, работы в пирамиде нет, и появиться она не может, т.к. `IslandDeltaApplicator`/`IslandDomainReplay` переносят дельты **только внутрь области** (`IslandDomainReplay.cs:76-85, 146-167`). Утверждение пользователя «рубка/добыча/склад/ферма за кромкой работают» **кодом не подтверждается** — `[требует рантайм-проверки]`: поставить метку рубки на 1030 и посмотреть, появится ли работа.

---

### J-07 · P0 · «200–300 агентов работают» — это счётчик, а не утечка состояния (S3)

**Файл:** `src/ui/controllers/HUDController.cs:811-823`.

```csharp
int employed = total - JobDispatcher.Instance.IdleWorkers.TotalIdleCount;   // :817
```

В «занятые» попадает **всё, что не лежит в плоском пуле бездельников**:
1. агенты в `Evacuating` (толпа, нужды) — J-09;
2. агенты класса `Worker`: они **по дизайну не добавляются в пул** (`AgentSimulationThread.cs:240-262`: `AddIdleWorker` только для `!= AgentClass.Worker`), поэтому все рекрутированные зонами агенты всегда числятся «работающими», даже когда стоят без дела;
3. агенты, застрявшие с waypoints — J-08;
4. агенты, у которых `States == Working`, но работа уже завершена/удалена другим путём (гонка `Complete` vs `JobValidator.RemoveJob`).

**Проверено:** статически (формула HUD + инвариант пула).

**Рантайм:** одновременно снять `idle.TotalIdleCount`, `index.UnclaimedCount`, `Σ(States != Idle)` по классам и `Σ(агентов с живым CurrentJobId)`. Разница `Σ(не-Idle) − Σ(живой jobId)` и есть настоящий «фантом» (FEAT-J07). Ожидание: фантом = Workers + Evacuating, т.е. **число, близкое к 200–300**, при нуле живых работ.

---

### J-08 · P0 · Watchdog пропускает агентов с waypoints

**Файл:** `src/simulation/AgentSimulationThread.cs:1275`.

```csharp
if (... || _pool.WaypointCount[i] > 0) continue;   // :1275 — не судим маршрутников
```

Смысл защиты понятен (маршрут многосегментный, «нет прогресса» не отличить от движения по сегменту), но следствие: агент, чей маршрут зациклился (два waypoint'а, между которыми он качается; waypoint за водой; waypoint внутри стены), **никогда** не будет снят watchdog'ом. `StuckTimer` хендлеров тоже не поможет: он растёт только при нулевом смещении, а агент двигается. Такой агент остаётся в `Working` навсегда — это и есть «работа.т» без работы.

**Проверено:** статически. **Рантайм:** счётчик агентов с `States != Idle && CurrentJobId >= 0 && !JobIndex.TryGetJob(...)` — если он растёт, J-08 подтверждён.

---

### J-09 · P1 · `Evacuating` без watchdog прогресса

**Файл:** `AgentSimulationThread.cs:1187-1193` (bookkeeping), `:673-681` (Worker'ы вообще пропускают клейм), `AgentMovementService.cs:496` (`PushAway` ставит `Evacuating`).

Состояние `Evacuating` выставляется при расталкивании (`PushAway`), обслуживается `_needsSystem`/`WanderJobSystem`, и **не имеет** ни порога «сколько можно быть в эвакуации», ни прогресс-проверки: единственный сторож — `StuckTimer`, который инкрементится только при нулевом смещении. В плотной толпе агенты толкаются непрерывно ⇒ `Evacuating` может длиться минуты. На это время агент: не в пуле бездельников, не берёт работу, числится «занятым» в HUD.

**Рантайм:** распределение времени пребывания в `Evacuating` (гистограмма) — см. FEAT-J06.

---

### J-10 · P1 · Завершение работ безусловно возвращает агента в общий пул (включая Worker'ов)

**Файл (все места):** `TreeChoppingJobHandler.cs:141`, `FarmingJobHandler.cs:106`, `ConstructionJobHandler.cs:166`, `PlantingJobHandler.cs:87, 201`, `MiningJobHandler.cs:138`, `HarvestJobHandler.cs:89`, `StockpileHaulingJobHandler.cs:113`, `BlueprintDeliveryJobHandler.cs:70, 138`, а также пути нужд/блуждания `NeedsJobSystem.cs:308`, `WanderJobSystem.cs:90`.

Во всех ветках завершения — `pool.States[i] = Idle; JobDispatcher.Instance.IdleWorkers.AddIdleWorker(i, pool);` без проверки `pool.Classes[i]`.

Инвариант класса нарушен: `Worker` обслуживается **только** очередью своей зоны (`AgentSimulationThread.cs:1117-1141`, `WorkerZoneJobSystem.cs:42-52`), а `IdleWorkers` — источник для общего диспатчера (`JobDispatcher.cs:101-146`). Попав в пул, Worker получает право взять любую работу пирамиды и уходит из зоны. Зона теряет выделенного исполнителя, а её работы по пирамидному пути не отдаются 60 с (`WorkerHoldMs`, `JobSpatialPyramid.cs:1120-1121`) — при этом по пути `ZoneManager.DispatchZones` они всё равно уходят разнорабочим (J-18), т.е. политика расщепляется: работа уходит «кому угодно», но через разные ворота с разными правилами. Это один из механизмов S4 и источник churn.

**Проверено:** статически (симметрия инварианта: `WorkerAssignmentSystem.PromoteToWorker` **убирает** из пула, `:167`, а хендлеры — добавляют).

**Рантайм:** счётчик `AgentClass.Worker` в `IdleWorkers` (должен быть 0); сейчас ожидается >0.

---

### J-11 · P1 · Откат в диспетчере не вызывает `OnCancel`

**Файл:** `src/simulation/jobs/JobDispatcher.cs:129-145`.

```csharp
handler.OnStart(agentIndex, job, pool, ctx);      // :129 — здесь берутся резервы
if (pool.States[agentIndex] == AgentState.Idle) { // :132 — OnStart не сдвинул состояние
    JobIndex.ReleaseWorkerClaim(...);             // :136
    pool.CurrentJobId[agentIndex] = -1;           // :137-138
    IdleWorkers.AddIdleWorker(...);               // :140
    // OnCancel НЕ вызывается
}
```

`OnStart` хендлеров резервирует ресурсы: слот склада (`StockpileHaulingJobHandler.OnStart:32-70`), предмет на земле, материал стройки (`BlueprintDeliveryJobHandler.OnStart:53-72`). Откат освобождает только клейм пирамиды, но не резервы хендлера ⇒ «зависшие» резервы склада/предметов. Проявляется как «склад полон, а класть некуда» / «предмет зарезервирован никем» ⇒ вторично бьёт по S4 (логи не доставляются).

**Проверено:** статически. **Рантайм:** счётчик `StockpileManager` reserved vs фактических держателей; проверить, растёт ли `ReservedIncoming` без роста занятых слотов.

---

### J-12 · P1 · Мемоизация `CanAgentExecute` без учёта агента

**Файл:** `src/simulation/jobs/JobSpatialPyramid.cs:276-299` (`TryGetMemoCanExecute`/`SetMemoCanExecute`), `:1124-1139`.

Ключ мемо — `jobId` + эпоха диспатча. Но `CanAgentExecute` у части хендлеров **зависит от агента**: `BlueprintDeliveryJobHandler.cs:20` (`bool hasLogs = pool.CarriedItem[agent] == Log || ...`) — «первый осмотренный агент» решает за всех: если он без брёвен, а на складе бревна есть, `hasLogs` может оказаться `true` из-за складской ветки, и наоборот. Итог: (а) работа на эпоху помечается невыполнимой для всех — читается как «не выдают»; (б) работа отдаётся агенту, который в `OnStart` сразу сдаёт её обратно (`BlueprintDeliveryJobHandler.OnStart:53-72` — «remaining <= 0 → release») ⇒ churn клеймов.

**Проверено:** статически (несоответствие ключа мемо и сигнатуры предиката). **Рантайм:** сравнить решения мемо с прямым вызовом для 3–4 разных агентов на одном jobId.

---

### J-13 · P1 · Приоритет категории 0 = тихая блокировка

**Файл:** `JobPriorityManager.cs:56-63` (`Math.Max(0, value)` — 0 разрешён), `JobSpatialPyramid.cs:1141-1144` (`if (userPrio <= 0) continue;`).

Установка приоритета категории в 0 (через `PriorityBoardController` или код) делает **все** работы категории невыдаваемыми, без сообщения и без визуальной причины. `[требует рантайм-проверки]`: проверить, может ли UI выставить 0 (если минимум 1 — дефект остаётся latent).

---

### J-14 · P0 · Строительство встаёт: три групповых гейта + предусловия клетки + голод haul-работ (S4)

**Гейт 1 — партия целиком ждёт полной расчистки.** `ConstructionManager.cs:168-172` + `:212-221`: если хотя бы в одной клетке партии есть дерево/камень/предмет, `batchIsClean == false` и **вся партия** остаётся в `ConstructionStage.Clearance`. Продвижение — `CheckGroupFullClearance:312-345`, и оно требует «ни одной грязной клетки» (`:321-326`).

**Гейт 2 — партия целиком ждёт полной доставки.** `BuildConfig.cs:33` `WaitForAllSiteResources = true`; `DeliverMaterial:350-384` ставит `WaitingAll`; `CheckGroupFullSupply:386-425` требует `IsFullySupplied` **для всех** клеток группы (`:394-402`). Самопочинки у `WaitingAll` нет: `ReconcileTick:645-708` обрабатывает `Clearance` (`:672-691`), `Supplying`, `Assembling` — **ветки для `WaitingAll` нет**, и продвинуть группу может только очередная доставка (`:379`).

**Гейт 3 — предусловия на клетку.**

| Проверка | Где | Что блокирует навсегда |
|---|---|---|
| `Stage == Supplying` | `BlueprintDeliveryJobHandler.cs:34-37` | клетка не в той стадии |
| `Ground == Grass` | `:42-45` | чертёж на песке/дороге |
| нет дерева/камня | `:38-41` | расчистка не дошла |
| нет чужого предмета на клетке | `:46-48` | предмет, который некуда спихнуть |
| бревёна доступны | `:20-30` (`CarriedItem` / `HasAvailableLogs` / `GetLiveItemCount(Log)`) | нет брёвен вообще |
| `site.HasObstacles == false` | `ConstructionJobHandler.cs:34-39` | флаг не сброшен |
| `site.IsFullySupplied` | `:34-39` | группа не добита |
| нет предметов на цели (иначе `TrySpillItemsAdjacent` + `false`) | `ConstructionJobHandler.cs:19-25` | **мутация в предикате**: вызов `CanAgentExecute` меняет мир |
| `TreeOnGrass[target]` | `TreeChoppingJobHandler.cs` `CanAgentExecute` | цель не дерево |

**Голод haul-работ (главный усилитель).** `JobBroker.SweepStockpileHaulJobs:280-346`:

```csharp
if (UnclaimedCount > 2048) return;    // :301
```

Пока в индексе больше 2048 непокрытых работ (а ферма 70 000 клеток — это ровно такой случай), **новые работы по переноске не создаются вообще**. Следствие: выпавшие от рубки брёвна никто не тащит на склад, склад не пополняется, доставка к чертежам не может найти бревёна ⇒ **стройка встаёт по всей карте, хотя «всё расчищено»**. Это прямая связь S5 → S4.

**Мёртвая клетка в группе.** `CheckGroupFullSupply:394-402` требует, чтобы клетка **существовала** в `_sites`. `CompleteConstruction:463-469` удаляет клетку из `_sites`/`_cellToGroup`, но `CheckGroupFullClearance`/`CheckGroupFullSupply` итерируют `_groupToCells` — если клетку убрали не из группы, проверка «все готовы» становится ложной навсегда. `[требует рантайм-проверки]`: воспроизвести партию из 3 клеток, где одну достроили/снесли.

**Проверено:** статически, полный список гейтов. **Рантайм:** для зависшей партии напечатать `Stage`, `HasObstacles`, `TotalDelivered/TotalRequired`, `_groupToCells.Count`, наличие работы каждого типа по позиции и результат `CanAgentExecute` для 3 агентов.

---

### J-15 · P0 · Массовая разметка выполняется на UI-потоке по клетке (S5)

**Файл:** `src/ui/tools/FarmingTool.cs:92`, `src/ui/tools/ZoneDraftTool.cs:771` → `FarmJobManager.MarkPlotsBatch:68-131`.

На **главном потоке**, для каждой из 70 000 клеток:
1. `GroundItemManager.HasItemsAt(x,y)` — `lock(_lock)` (`GroundItemManager.cs:224-232`);
2. при наличии предметов — `TrySpillItemsAdjacent(x, y, 512, 512)` (`GroundItemManager.cs`, вызывается из `MarkPlotsBatch`; в `ConstructionManager.cs:201-205` виден тот же паттерн);
3. `MarkTreesBatch` / `MarkStonesBatch` (свои локи);
4. `RegisterFarmPlotBatch` → `JobSpatialPyramid.RegisterBatch` (один `lock(_registerLock)`, это дешёвая часть) + **на каждую клетку** `TryGetJobIdByPos` (**свой лок**) + `ZoneJobRouter.Push`.

`ZoneJobRouter.Push:175-193` на каждом вызове делает `Coverage.RebuildIfNeeded(now)`, а `ZoneCoverageMap.RebuildLocked:92-140` — **аллоцирует `new ushort[w*h]` = 2 МБ на каждый тип работ с зонами** (`:123`), заливает нулями и красит все диски зон (`:130-131`, зона радиусом 512 = 262 144 клетки). Троттл 2 с (`:75-76`) спасает от 70 000 пересчётов, но не от «одного пересчёта посреди разметки» — а он и даёт фриз.

Именно это пользователь описывает как «менеджер пытался выдать 2000 работ сразу»: выдача синхронна, пачками, без рассрочки по тикам.

**Проверено:** статически (цепочка вызовов от кнопки до лока). **Рантайм:** Godot-профайлер на кадре разметки фермы 70k — ожидается один длинный кадр (сотни мс).

---

### J-16 · P1 · `DequeueNearest` дренирует всю очередь зоны

**Файл:** `src/simulation/zones/ZoneJobRouter.cs:195-247`.

```csharp
lock (gate) {
    var seen = new Dictionary<int, (QueuedJob Job, int Order)>();   // :213
    while (gate.Q.TryDequeue(out var j)) { ... }                    // :218-231
    foreach (var kv in seen) gate.Q.Enqueue(kv.Value.Job);          // :236-241
}
```

Стоимость = O(всей очереди зоны) + аллокация `Dictionary` на `count` + переупорядочивание. Вызывается из `WorkerZoneJobSystem.TryAssignZoneJob` (`:57`) для каждого безработного Worker'а своей зоны с интервалом 2 с (`JobSearchIntervalSec = 2.0`). Для очереди 70 000 записей — это ~10–30 мс и мегабайты мусора на каждый вызов, **под замком зоны**, который держит `Sweep` (тоже полный дренаж).

**Проверено:** статически. **Рантайм:** замер `DequeueNearest` при `QueueDepth > 1000`.

---

### J-17 · P1 · Аудит: «слепые» 8 проб ⇒ живая работа спит 30 с

**Файл:** `src/simulation/jobs/JobValidator.cs:60-177` (`_jobSlice = SnapshotActiveJobs(...)` `:70`, `probes = CollectProbeAgents(pool, 8)` `:76`, `:282+`), `:157-174`.

Логика: если **ни один из 8 первых бездельников** не может выполнить работу, то либо работа удаляется (`!HasManagerMark`), либо получает `MarkJobUnreachable(jobId, 30000)` (`:172`). Пробы берутся из пула бездельников без учёта: (а) их класса (Worker/Laborer), (б) их нужд (голодный агент всё равно вернёт «могу»), (в) их инвентаря — а для `BlueprintDelivery` именно инвентарь (`CarriedItem == Log`) определяет ответ (`:20`). Плюс `try/catch` вокруг `CanAgentExecute` трактуется как «может» (`:149-154`) — разумно, но маскирует исключения.

Итог: **живая работа может получить 30-секундный кулдаун** — прямой вклад в S2 («2 работы ждут десятки секунд»), и одновременно маскировка реальных дефектов.

Ещё: `SnapshotActiveJobs:1206-1226` копирует **O(всей `_posMap`)** и делает это раз в 30 с на sim-потоке; при 70 000 работ это аллокация списка на 2048 (срез) при обходе 70 000 — умеренно, но заметно.

**Проверено:** статически. **Рантайм:** считать, сколько работ получает `MarkJobUnreachable(30000)` и сколько из них затем успешно выполняется (доля ложных приговоров).

---

### J-18 · P1 · Диспетчер зон: чужой тип, немедленная отмена, 60-с холд не чтится

**Файл:** `src/simulation/zones/ZoneManager.cs:651-786` (константы `:22-23, 42`).

**Механизм.** Это **второй, параллельный** диспетчер, работающий с тем же общим пулом бездельников, что и `JobDispatcher`:

- `:686` берёт безработных из **общего** пула (`CollectIdleWorkersInChunk`), т.е. это `AgentClass.Laborer`, а не Worker'ы зоны — «диспетчер зон» раздаёт работы зон разнорабочим;
- в плоском пуле `CollectIdleWorkersInChunk` **игнорирует `chunkIdx`** (`FlatIdleWorkerPool.cs:123-130`), т.е. «по чанкам» — фикция: каждая зона видит весь пул;
- `MaxZonesPerPass = 4` (`:42`), `MaxAssignPerChunk = 8` (`:23`), `WorkersPerChunkBudget = 48` (`:22`) ⇒ за проход раздаётся ≤ 4 зоны × 8 назначений на чанк;
- `TryClaimForWorkerInChunk` (`:706`) **не проверяет** `ZoneJobRouter.IsHeldForWorkerFresh`, т.е. зона отдаёт работу, которая по спецификации зарезервирована за её Worker'ом (`ZoneJobRouter.cs:97-102`, `JobSpatialPyramid.cs:1116-1121`) — две противоположные политики для одной и той же работы;
- работа берётся **любого типа**, а затем на `:733-746` отменяется при `!zone.Matches(type) || !zone.ContainsTile` — с полным циклом `OnStart`→`OnCancel` (`:735`), т.е. резервы/побочные эффекты дважды за одну пустую попытку, и агент возвращается в пул (`:744`).

Итог: одна и та же работа может быть выдана (а) зоной — немедленно, (б) пирамидой — через 60 с, (в) отменена и выдана заново. При этом зон в проходе всего 4 (`MaxZonesPerPass`), обход — по кругу (`_zoneScanIndex`, `:667`), поэтому при большом числе Work-зон часть из них не получает раздачу за проход, и латентность скачет: «выдали мгновенно» или «несколько проходов ничего». Это источник churn в логах и часть S2.

**Проверено:** статически. **Рантайм:** счётчик отмен «не тот тип» на 1000 назначений зоны.

---

### J-19 · P2 · `AI_PROJECT_MAP.md` описывает архитектуру 2–3 поколения назад

Документация — не источник истины; ниже **конкретные расхождения**, проверенные по коду.

| Утверждение `AI_PROJECT_MAP.md` | Факт |
|---|---|
| `:31` «`IdleWorkerSpatialGrid`» в списке подсистем агентов | Класс **мёртв**: `grep` не находит ни одной ссылки вне самого файла; пул бездельников — `FlatIdleWorkerPool` (плоский массив + `lock`) |
| `:32, 51` «`GenericJobSpatialIndex` (SoA+CAS) … 1024x256» | **Мёртв** (1142 строки, ссылок вне файла нет). Реально — `JobSpatialPyramid` (SoA + L0/L1 + макро-маска, 512×512) |
| `:52` «Parallel по `ChunksPerBatch` 512*scale(cap 8): `DispatchChunk`=>`CollectIdle`+`TryClaim`+`OnStart`. Затем `SpillOverPass`+`GlobalPass`. `ReleaseForStuck(+кулдаун 3-5с)`» | Ни `ChunksPerBatch`, ни `DispatchChunk`, ни `SpillOverPass`, ни `GlobalPass` **не существует**. Диспатч — последовательный цикл по плоскому батчу ≤4096 с квантом 0.8 мс и ротацией курсора. `ReleaseForStuck` — 5 с по умолчанию, watchdog — 15 с |
| `:45` «Phase1_ParallelMove … Phase2_Dispatch (таймер Dispatch=>`DispatchPendingJobs`+`batchScale`) … Phase3b_SequentialCommit» | Фазы: `Phase2_ParallelUpdate` (`:713`), `Phase3a_ParallelBookkeeping` (`:915`), `Phase3b_SequentialCommit` (`:1326`). `batchScale` не существует (`dispatchScale`, кап 2, и он **не используется**) |
| `:77` поля `AgentDataPool` «…Next/PrevInIdleCell…» | Полей `Next/PrevInIdleCell` больше нет (плоский пул). Отсутствуют в списке: `Classes`, `AssignedZoneId`, `PathRequestCooldown`, `NeedsRetryTimer`, `NeedsBehavior`, `ReservedItemCount` |
| `:90` «`IdleWorkerSpatialGrid._lock` — следующий кандидат» | Класс мёртв; актуальный кандидат — глобальный `lock` у `FlatIdleWorkerPool` и `GroundItemManager._lock` |
| `:96` «…`ReleaseForStuck`=>кулдаун=>`GlobalPass` дальнему» | `GlobalPass` не существует |
| Нет упоминаний | `JobSpatialPyramid`, `ZoneJobRouter`, `ZoneCoverageMap`, `WorkerAssignmentSystem`, классы `Laborer`/`Worker`, 60-с холд, `JobValidator`, `ActiveArea` 1024, гейт `WorldBlueprintsSupported`, `WorldJobBoard`, `ConstructionSite`-стадии |

Отдельно: комментарий-диагностика `AgentSimulationThread.cs:839-850` («диспетчер сводит пару только внутри одного чанка… дальний глобальный проход ≤128 кандидатов, ≤8 назначений, не чаще раза в 2 с») описывает **несуществующий** алгоритм — такого глобального прохода нет вовсе (есть макро-скан в пирамиде). Автор отчёта по логам `[DISP]` будет делать неверные выводы.

### J-20 · P2 · Мёртвый код и мёртвые параметры

| Что | Где | Размер/смысл |
|---|---|---|
| `GenericJobSpatialIndex.cs` | `src/simulation/jobs/` | **1142 строки**, ссылок нет |
| `IdleWorkerSpatialGrid.cs` | `src/simulation/agents/` | **402 строки**, ссылок нет |
| `WorldJobBoard.cs` | `src/ui/streaming/` | 106 строк, инстанцируется только тестом (`tests/TestDestroyerStreaming.cs:422`) |
| `MaxAssignmentsPerTick = 4096` | `JobDispatcher.cs:22` | объявлен, не используется |
| параметр `chunkBatchScale` | `JobDispatcher.cs:49` | принимается, не используется; `dispatchScale` из `:513` в него уходит в никуда |
| `TriggerImmediateDispatch()` | `JobDispatcher.cs:40-42` | пустое тело; вызывается из `JobBroker.RegisterBlueprint:348-369` и др. → «мгновенной выдачи» нет |
| `SearchesPerFrame`, `ConfigureBudget`, `TryReserveSearch`, `BudgetDeferred` | `HierarchicalPathfinder.cs:144-196` | **`TryReserveSearch` не вызывается нигде** (проверено `grep`) — A* без глобального бюджета |
| `jid > 0` вместо `>= 0` | `JobBroker.cs:162` | работа с `jobId == 0` не пушится в роутер (первая работа в сессии) |
| `[DISP]`-диагностика | `AgentSimulationThread.cs:851-880` | использует `CollectIdleWorkersInChunk` поверх плоского пула ⇒ «бездельников в чанке» = весь пул для каждого чанка. Числа бессмысленны |

### J-21 · P2 · `jid > 0` — первая работа не попадает в очередь зоны

См. J-20, `JobBroker.cs:162`: `if (jid > 0)` в батч-пути против `if (treeJobId >= 0)` в одиночном (`:123-125`). Пирамида нумерует с 0 (§`JobSpatialPyramid`, free-list), поэтому «работа №0» никогда не появится в очереди зоны: 60-с холд/Worker-путь для неё не работают. Мелочь, но симптом «одна работа ведёт себя иначе» воспроизводим.

### J-22 · P2 · Один глобальный `lock(_lock)` на все предметы земли

`GroundItemManager.cs:23` — единственный мьютекс на `_groundItems`, `_itemChunks`, `_totalAvailableByType`. Через него идут: резервирование еды в Phase2 (`NeedsJobSystem`), выдача haul-работ, `BlueprintDelivery.CanAgentExecute` (внутри клейма), спил-операции при расчистке. То есть 16 потоков Phase2 и диспетчер конкурируют за один замок. Само по себе не дефект (корректность), но это ограничитель масштабирования и кандидат на per-chunk шардинг.

### J-23 · P2 · Диагностика `[DISP]` вводит в заблуждение

`AgentSimulationThread.cs:851-880` (`ReportDispatchDistribution`) — см. J-20. Плюс комментарий `:839-850` описывает несуществующий дизайн (J-19). Если у пользователя включён `EnableConsoleDebugAudit`, он видит ложную картину «чанков без бездельников».

---

## 3. Отсутствующие функции/инструменты (FEAT-Jxx)

Приоритет: **P0** — без этого нельзя ни доказать, ни закрыть дефекты; **P1** — снимает симптом; **P2** — гигиена.

### FEAT-J01 · P0 · Бюджет диспатча как бюджет (сейчас его нет)
- **Что:** проверять время **на каждой итерации** (дешёвый `Stopwatch.GetTimestamp()` без ветвлений) + ограничить стоимость **одной** попытки клейма (передавать дедлайн внутрь `TryClaimJobForAgent`, прерывать макро-скан по нему). Убрать «шаг 64».
- **Где:** `JobDispatcher.cs:86-94`, `JobSpatialPyramid.cs:978-1043` (передать `deadlineTicks`).
- **Эффект:** `Simulation.JobDispatch` перестаёт быть источником спайков 60–370 мс; предсказуемая задержка кадра.
- **Риск:** среднее число назначений за вызов упадёт (меньше работы за вызов) — компенсировать частотой (FEAT-J02) или «каруселью» (J-02).

### FEAT-J02 · P0 · Пул бездельников с геометрией и без потолка 4096
- **Что:** вернуть индексацию по чанкам, но **O(1)**: плоский массив + `int[] headOfChunk` + `nextInChunk`, сбор — по чанкам, ближайшим к работе, а не «первые 4096». Альтернатива без структур: `CollectIdleWorkers` отдаёт **окно** `[cursor, cursor+4096)` по кольцу (сейчас кольцо есть, но сбор всегда с 0 — достаточно одного изменения: `CollectIdleWorkers(offset, count, ...)`).
- **Где:** `FlatIdleWorkerPool.cs:103-130` (`CollectIdleWorkers`), `JobDispatcher.cs:64-65, 80-82`, `ZoneManager.cs:686`.
- **Эффект:** каждый бездельник получает шанс за `ceil(idle/4096)` вызовов; исчезает «я всех вижу, а работу не дают».
- **Риск:** низкий (структура данных проще, чем `IdleWorkerSpatialGrid`).

### FEAT-J03 · P0 · Индекс работ на 1024×1024 (убрать `Clamp`)
- **Что:** `ChunkDim = 64`, `MacroDim = 16` (или параметризовать от `ActiveArea.Size`), убрать зажим индекса. Ячейки вне индекса — не «схлопывать», а отбрасывать с явным счётчиком.
- **Где:** `JobSpatialPyramid.cs:20-27, GetChunkIndexStatic, GetMacroIndex`.
- **Эффект:** локальность восстанавливается на всей карте; хвост латентности J-03 перестаёт концентрироваться в одном чанке.
- **Риск:** средний — размеры массивов (`ChunkDim²`), тесты `TestJobRaceStress`/`BenchJobAssignment`, сейвы не затрагиваются (индекс не сериализуется — проверить `SnapshotActiveJobs`/`Clear`).

### FEAT-J04 · P0 · Инкрементальный `Sweep` вместо полного дренажа
- **Что:** не дренировать `_uncovered` целиком. Вариант A: `ConcurrentQueue` + «поколения» (записи младше 30 с не трогать — счётчик `_youngCount`, чтобы не обходить их). Вариант B: `_uncovered` → кольцевой буфер с головой/хвостом и лимитом за проход (`≤1024` записей за вызов), остальное в следующий. Вариант C: не хранить непокрытые вовсе — при появлении зоны сделать один проход по её клеткам (зона знает свои клетки).
- **Где:** `ZoneJobRouter.cs:305-331, 360-370`; вызов `AgentSimulationThread.cs:522`.
- **Эффект:** исчезает O(70 000)×2×4/с и мегабайты мусора; фризы S5 на этапе steady-state.
- **Риск:** средний (семантика репуша 30 с должна сохраниться — есть тест `test_zone_router.cs`).

### FEAT-J05 · P0 · Профильные скоупы на «слепые» участки
- **Что:** добавить `GameProfiler.ScopeCustom` для: (1) `ZoneJobRouter.Sweep` — `Simulation.ZoneSweep`; (2) `JobSpatialPyramid.TryClaimJobForAgent` (или счётчик в наносекундах) — `Simulation.JobClaim`; (3) `HierarchicalPathfinder.TryFindPathScratch` — `Path.AStar`; (4) `FlatIdleWorkerPool.CollectIdleWorkers` + `Add/Remove` — `Idle.Pool`; (5) `GroundItemManager._lock` (счётчик ожиданий) — `Item.LockWait`; (6) `ConstructionManager` доступ под `_lock` — `Build.LockWait`.
- **Где:** `AgentSimulationThread.cs:522`, `JobDispatcher.cs:56-183`, `HierarchicalPathfinder.cs:486+`.
- **Эффект:** исчезает «невидимая» половина `TotalStepCycle`; станет видно, сколько из 620–880 мс/с Phase2 — это A* и ожидание локов, а сколько собственно движение агентов.
- **Риск:** низкий; важно снять `Scope` с горячего пути (`GameProfiler.Record` contention — уже был инцидент, см. `AI_PROJECT_MAP.md:90-B`).

### FEAT-J06 · P0 · Метрики подсистемы (их нет вообще)
- **Что:** публиковать в `SimEvents.Series` (механизм уже есть, `AgentSimulationThread.cs:380-390`):
  1. `jobs.unclaimed`, `jobs.total`, `jobs.assigned_workers` (сейчас есть только в `[DISP]`-логе);
  2. **`jobs.claim_latency_p50/p95`** — «время от регистрации до первого клейма» (гистограмма, 16 бакетов) — прямая метрика S2;
  3. `jobs.cooldown_masked` — сколько непокрытых работ скрыто кулдауном (сейчас гейт `:975` скрывает их полностью);
  4. `jobs.phantom_agents` — агенты в `States != Idle` с **не**-живым `CurrentJobId` (S3);
  5. `agents.evacuating_ms_hist` — распределение времени в `Evacuating` (J-09);
  6. `workers.idle_in_global_pool` — Worker'ы, попавшие в общий пул (должно быть 0, J-10);
  7. `zone.queue_depth_max`, `zone.held_jobs` (60-с холд), `zone.uncovered_count`;
  8. `dispatch.examined`, `dispatch.assigned`, `dispatch.elapsed_ms` (J-01/J-02).
- **Где:** `JobDispatcher.cs:183` (конец метода), `AgentSimulationThread.cs:851-880` (переписать `[DISP]`), `ZoneJobRouter.cs:410` (конец файла).
- **Эффект:** все дальнейшие решения — по числам, а не по «кажется, медленно».
- **Риск:** низкий (без аллокаций в горячем пути).

### FEAT-J07 · P1 · Инвариант-аудит состояний агентов (авто-починка)
- **Что:** раз в 5–10 с реального времени (рядом с `JobValidator`), по срезу 2048 агентов проверять:
  `States != Idle && States != Evacuating && States != Resting` ⇒ `CurrentJobId >= 0` **и** `JobIndex.TryGetJob(id)` живой **и** `job.AssignedWorkers > 0`.
  Нарушение ⇒ `ReleaseJobWorker(i)` + счётчик + лог первых 5 случаев. Это **фикс** J-07/J-08/J-09/J-10 (агент возвращается в пул по правилу своего класса) и одновременно доказательство S3.
- **Где:** новый `src/simulation/jobs/AgentStateAudit.cs` (или расширить `JobValidator`), вызов в `AgentSimulationThread.cs:551-559`.
- **Эффект:** «фантомные 200–300» либо исчезают, либо показываются с причиной. Обратная проверка: `Σ AssignedWorkers == число агентов с этим jobId` (ловит J-11).
- **Риск:** низкий при срезе; важно не трогать `Worker` (он не в пуле — правило: для Worker возврат = `Idle` + не добавлять в пул).

### FEAT-J08 · P1 · Watchdog по прогрессу, а не по «нулевой скорости»
- **Что:** вместо `StuckTimer`/`WaypointCount == 0` — «прогресс к цели»: раз в 8 игросек сравнивать **расстояние до следующей цели** (waypoint или `TargetPosition`). Не уменьшилось на 2 px ⇒ снять работу (купон 15 с остаётся). Применять ко **всем** не-Idle, включая `Evacuating` (для него — «расстояние до безопасной точки», или жёсткий таймаут 120 игросек).
- **Где:** `AgentSimulationThread.cs:1197-1324` (заменить условие `:1275`), `:1187-1193` (Evacuating).
- **Эффект:** закрывает J-08/J-09 (вечные «занятые»).
- **Риск:** средний — ложные срабатывания на медленных агентах (вода ×0.45); порог 2 px за 8 с это терпит.

### FEAT-J09 · P0 (для S6) · Исполнитель работ вне активной области
- **Что:** снять гейт `WorldBlueprintsSupported` **можно только** после появления исполнителя. Минимальный путь:
  1. Подключить существующий `WorldJobBoard` (файл уже готов, `Refresh(focusX, focusY)` + `TryTakeNearest(..., allow)` + `Complete`) — он уже умеет очередь по загруженным чанкам и откат `LostPlans`;
  2. Ввести единый источник истины «работа мира» — либо расширить `JobSpatialPyramid` на мировые координаты (для этого нужен FEAT-J03: без 1024-индекса мировые клетки не адресуются), либо держать мировые работы отдельно и отдавать их через новый `AgentClass.WorldWorker`;
  3. Движение вне острова уже работает (`IsTileBlocked:407-423`), но A* — нет: нужен маршрут по регионам мира (FEAT-J10) или честный «идти по прямой, при упоре — отказ» **до** клейма (`allow`-фильтр в `TryTakeNearest`);
  4. Затем `WorldPlacementPass.WorldBlueprintsSupported = true` и **убрать** пользовательский текст `BuildTool.cs:190-197` (оставить только как info).
- **Где:** `src/ui/tools/WorldPlacementPass.cs:37`, `src/ui/streaming/StreamingToolPlacement.cs:67-68`, `WorldJobBoard.cs`.
- **Эффект:** S6 закрыт «правильно», а не косметикой; заодно закрывается S1 для рубки/добычи (сейчас они за кромкой фактически не работают — J-06).
- **Риск:** **высокий** (новая сущность в sim-цикле, стриминг чанков, сериализация). Нельзя снимать гейт раньше исполнителя — иначе вернётся «чертёж поставился и исчез» (`StreamingToolPlacement.cs:15-24`).

### FEAT-J10 · P1 · Маршруты вне острова (расширить граф регионов)
- **Что:** построить `HierarchicalPathfinder` cost-карту на **весь** загружаемый мир (или на `ActiveArea` + кольцо 128 тайлов), лениво по чанкам. Плюс — подключить мёртвый бюджет: `TryReserveSearch`/`BudgetDeferred` (`HierarchicalPathfinder.cs:144-196`) вызывать из `RequestHierarchicalPath:145-191`, чтобы **отличить** «нет пути» от «не дали бюджет» (сейчас одинаковый кулдаун 3–5 с, `:189`).
- **Эффект:** S1 (агент обходит воду/гору за кромкой) + предсказуемая стоимость Phase2 (сейчас 620–880 мс/с без ограничителя).
- **Риск:** высокий по памяти (cost 1024² уже есть; мир — потенциально безграничен ⇒ только загруженные чанки).

### FEAT-J11 · P1 · Постепенная (rate-limited) выдача работ
- **Что:** очередь регистрации с бюджетом «N работ за тик» + разнос по кадрам для UI-инструментов. Для фермы 70k: сначала зона/покрытие (1 раз), затем работы пачками по 512–1024 за тик, с прогрессом в HUD («размечаю 12 000 / 70 000»).
- **Где:** `FarmJobManager.MarkPlotsBatch:68-131`, `ZoneDraftTool.cs:771`, `FarmingTool.cs:92`, `JobBroker.RegisterFarmPlotBatch`.
- **Эффект:** закрывает фриз S5 «на входе» (J-15), независимо от J-04.
- **Риск:** средний (частично размеченная ферма должна быть консистентной; уже есть `_plotsNeedingClearance`/`_markedPlots`).

### FEAT-J12 · P1 · Регистрация работ вне UI-потока
- **Что:** `MarkPlotsBatch` — в рабочий поток (пул `Task`/`Channel`), на main-потоке остаётся только создание зоны и финальный `CallDeferred` для рендера. Локи `GroundItemManager`/пирамиды к этому готовы (они потокобезопасны), но нужна осторожность с `_markedPlots` (`HashSet` под своим локом — проверить).
- **Где:** `FarmJobManager.cs:68-131`, `ZoneDraftTool.cs:771`.
- **Эффект:** кадр не блокируется даже на 70k.
- **Риск:** средний (гонки с `ReconcileTick`/`JobValidator`, которые читают те же множества).

### FEAT-J13 · P1 · Строительство: убрать «всё-или-ничего» и добавить самопочинку `WaitingAll`
- **Что:** (а) опция `RequireFullGroupClearance/Supply` в `BuildConfig` (по умолчанию — **выкл** для стен, **вкл** для крупных зданий); (б) `ReconcileTick` должен продвигать `WaitingAll` (вызвать `CheckGroupFullSupply` для групп, где все клетки `IsFullySupplied`, но стадия `WaitingAll`); (в) `ConstructionJobHandler.CanAgentExecute` **не должен мутировать мир** (`:19-25`) — спил вынести в `OnCancel`/`ReconcileTick`; (г) убрать стоп-кран `UnclaimedCount > 2048` из `SweepStockpileHaulJobs` или заменить на бюджет «не более N новых работ за проход».
- **Где:** `ConstructionManager.cs:212-221, 312-345, 386-425, 645-708`, `ConstructionJobHandler.cs:17-56`, `JobBroker.cs:301`.
- **Эффект:** S4 — строительство больше не встаёт из-за одной «плохой» клетки и не голодает по брёвнам при большой ферме.
- **Риск:** средний (меняется UX-семантика партии; есть `TestAdversarialConstruction.cs`).

### FEAT-J14 · P1 · 60-секундный холд: унифицировать политику
- **Что:** сейчас две противоречащие политики для одной работы: пирамида прячет её от разнорабочих 60 с (`JobSpatialPyramid.cs:1116-1121`), а `ZoneManager.DispatchZones` отдаёт её разнорабочему немедленно (`ZoneManager.cs:686, 706` — проверки холда нет). Нужно выбрать **одну**:
  (а) холд нужен — тогда `DispatchZones` обязан его чтить, а холд должен быть умным: не держать, если у зоны `AssignedCount == 0` или все её Worker'ы заняты (это знает `ZoneManager`);
  (б) холд не нужен — удалить `IsHeldForWorkerFresh` и оставить Worker'ам приоритет через `JobPriorityManager`/аффинити;
  (в) компромисс: короткий холд (5–10 с) только для «квалифицированных» типов (склад/стройка) и конфигурируемый.
- **Где:** `ZoneJobRouter.cs:97-108, 259-263`, `JobSpatialPyramid.cs:1116-1121`, `ZoneManager.cs:700-746`.
- **Эффект:** убирает либо 60-секундное ожидание, либо его обход; снимает расхождение, из-за которого одна и та же работа ведёт себя по-разному в зависимости от того, кто её увидел первым.
- **Риск:** низкий; тест `test_zone_agents.cs` (пункт «е», 60-с правило) придётся обновить под выбранный вариант.

### FEAT-J15 · P2 · Дешёвое покрытие зон
- **Что:** не `ushort[1024*1024]` на тип (2 МБ аллокации + заливка на каждый пересчёт, `ZoneCoverageMap.cs:123`), а: (а) переиспользуемый буфер (двойная буферизация вместо `new`); (б) `ushort[]` только на bbox зон (обычно 512×512 = 0.5 МБ); (в) битовая карта «есть владелец» + разреженный словарь.
- **Эффект:** убирает GC-мусор при разметке (J-15).
- **Риск:** низкий.

### FEAT-J16 · P1 · `DequeueNearest` без дренажа
- **Что:** очередь зоны — не `ConcurrentQueue` с дренажом, а структура с удалением по id: `ConcurrentDictionary<int, QueuedJob>` + счётчик глубины, либо `PriorityQueue` по расстоянию с ленивой проверкой живости. Ближайший ищется по **чанковой** сетке (как в пирамиде), а не полным перебором.
- **Где:** `ZoneJobRouter.cs:195-247`.
- **Эффект:** `TryAssignZoneJob` перестаёт быть O(70k) на Worker'а.
- **Риск:** средний (детерминизм выбора — тест `test_zone_router.cs` пункт «в»).

### FEAT-J17 · P1 · Аудит без «слепых» приговоров
- **Что:** (а) пробы — не «первые 8 бездельников», а **разнообразные**: по одному на каждый `AgentClass` × наличие инструмента × несёт ли предмет (сейчас берутся первые, `JobValidator.cs:76, 282`); (б) 30-секундный `MarkJobUnreachable` применять **только** если ни один из проб не смог **и** работа не назначена (`AssignedWorkers == 0`) **и** прошло ≥2 циклов аудита (сейчас — с первого раза); (в) логировать, сколько работ «спит» по этой причине (FEAT-J06 п.3).
- **Эффект:** S2 — меньше «работы есть, но она в кулдауне».
- **Риск:** низкий.

### FEAT-J18 · P2 · Гигиена: удалить мёртвое, починить документацию
- **Что:** удалить `GenericJobSpatialIndex.cs` (1142), `IdleWorkerSpatialGrid.cs` (402); подключить или удалить `WorldJobBoard.cs` (106 — см. FEAT-J09); убрать `MaxAssignmentsPerTick`, `chunkBatchScale`, `TriggerImmediateDispatch` (или реализовать последний — он вызывается из `JobBroker`); подключить `TryReserveSearch` (FEAT-J10) или удалить бюджет; переписать `ReportDispatchDistribution` и устаревшие комментарии (`AgentSimulationThread.cs:839-850`, `JobSpatialPyramid.cs:1074-1079`, `ZoneJobRouter.cs:291-296`); обновить `AI_PROJECT_MAP.md` по таблице J-19.
- **Эффект:** минус ~1650 строк ложных ориентиров; следующий анализ не будет ходить по мёртвым путям (я на них потратил ~5 % времени).
- **Риск:** нулевой для удаления мертвецов; `AI_PROJECT_MAP.md` — только текст.

---

## 4. Граф: риски (циклы, god-objects, blast radius — цифрами)

### 4.1 Цикл: вся подсистема — один SCC из 39 файлов

`viz_cli.py cycles --scope src/simulation` вернул **одну** компоненту `c1` (`size: 39`):

```
AgentSimulationThread, AgentDataPool, WorkerAssignmentSystem, WorkerZoneJobSystem,
BlueprintManager, BuildingManager, TileClearanceService, CropGrowthManager, FarmJobManager,
GroundItemManager, ResourceBroker, StockpileManager, JobBroker, JobDispatcher, JobPriorityManager,
JobSpatialPyramid, JobValidator, (8 хендлеров: BlueprintDelivery, Construction, Farming, Harvest,
Mining, Needs, Planting, StockpileHauling, TreeChopping), DynamicWorkScheduler,
IDynamicWorkScheduler, LoadMonitor, WorkQuantum, SimdNeedsBatch, StoneJobManager,
ConstructionManager, StructureManager, TreeJobManager, WorkZoneManagerFacade, ZoneCoverageMap,
ZoneJobRouter, ZoneManager
```

**Что это значит практически.** Любой рефакторинг внутри этой группы (даже «локальный» `JobDispatcher`) не имеет изолированного контракта: изменение сигнатуры/семантики `JobDispatcher`, `JobSpatialPyramid`, `ZoneJobRouter` затрагивает до 39 файлов и **все** тесты подсистемы. Это же объясняет, почему дефекты живут так долго: **нельзя поправить одно место, не проверив цикл**.

**Рекомендация:** не пытаться «разорвать цикл» целиком. Ввести два узких интерфейса (`IJobIndex` для пирамиды, `IJobQueue` для роутера) — это снимет 3 узла из SCC и сделает остальные правки проверяемыми (`JobDispatcher` перестанет тянуть `StockpileManager`/`GroundItemManager` напрямую: `:61-62`).

### 4.2 God-objects и размеры (строки — реальные, из `Get-Content .Count`)

| Файл | Строк | fan-in | fan-out | Роль | Риск |
|---|---|---|---|---|---|
| `src/simulation/AgentSimulationThread.cs` | **1460** | — | **32** (макс.) | весь цикл симуляции: 3 фазы + диспетчеризация + влажность/плодородие/нужды/аудит | **Критический**: единственная точка правки для 8 из 23 дефектов |
| `src/simulation/jobs/JobSpatialPyramid.cs` | **1362** | 7 | — | индекс работ + клейм + кулдауны + зоны-холды | **Критический**: 6 дефектов (J-03, J-05, J-12, J-13, J-17) |
| `src/simulation/pathfinding/HierarchicalPathfinder.cs` | **1524** | — | — | A* + регионы + кеши (и мёртвый бюджет) | Высокий (FEAT-J10) |
| `src/simulation/zones/ZoneManager.cs` | **834** | — | — | зоны + их диспетчер (два разных механизма в одном классе) | Высокий (J-18) |
| `src/simulation/structures/ConstructionManager.cs` | **709** | **22** | — | стадии, группы, материалы, реконсиляция | **Критический** (J-14) |
| `src/simulation/items/GroundItemManager.cs` | **696** | **18** | — | все предметы земли + глобальный лок | Высокий (J-22) |
| `src/simulation/jobs/JobBroker.cs` | **609** | **19** | — | фасад регистрации + складской sweep | Высокий (J-14в, J-21) |
| `src/simulation/agents/AgentMovementService.cs` | **504** | **19** | — | движение, блокировки, waypoints, path-запросы | Средний |
| `src/simulation/zones/ZoneJobRouter.cs` | **410** | **10** | — | очереди зон, холд, репуш, sweep | Высокий (J-04, J-16) |
| `src/simulation/agents/IdleWorkerSpatialGrid.cs` | **402** | **0** | **0** | **мёртв** | — |
| `src/simulation/jobs/GenericJobSpatialIndex.cs` | **1142** | **0** | **0** | **мёртв** | — |
| `src/simulation/agents/FlatIdleWorkerPool.cs` | **147** | — | — | пул бездельников (единственный лок на всех) | Средний (FEAT-J02) |
| `src/ui/controllers/HUDController.cs` | **1559** | — | — | в т.ч. фантомный счётчик занятости | Средний (J-07) |

`AgentSimulationThread` (32 зависимости, 1460 строк) — классический god-object: он одновременно планировщик, диспетчер, тикер мира и диагностика. Любая правка в нём проверяется только запуском.

### 4.3 Нарушения слоёв (`layers`)

| Откуда (core) | Куда (simulation) | Ссылок |
|---|---|---|
| `src/core/world/mutation/IslandDomainReplay.cs` | `FarmJobManager.cs` | 4 |
| `src/core/world/mutation/IslandDomainReplay.cs` | `StockpileManager.cs` | 3 |
| `src/core/world/mutation/IslandDomainReplay.cs` | `StructureManager.cs`, `ConstructionManager.cs`, `StoneJobManager.cs`, `TreeJobManager.cs` | по 2 |
| `src/core/world/mutation/IslandStateReplay.cs` | `JobBroker.cs` | 2 |

Это не «стилистика»: **реплей состояния мира напрямую дергает менеджеры работ**, то есть загрузка/переход состояния может создавать работы в обход обычного пайплайна (и, вероятно, без прогрева покрытия зон — `ZoneCoverageMap`). Отсюда возможен класс «после загрузки сейва работы есть, а зоны их не видят» — `[требует рантайм-проверки]`.

### 4.4 Радиус поражения (blast radius)

| Правка | Прямых потребителей | Транзитивно | Что ломается при ошибке |
|---|---|---|---|
| `JobDispatcher.cs` | **20** | вся игра (`Main`, HUD, все хендлеры, зоны) | выдача работ целиком |
| `ConstructionManager.cs` | **22** | 84 файла | стройка, HUD, сейвы, реплей |
| `JobBroker.cs` | **19** | 87 | регистрация всех работ |
| `GroundItemManager.cs` | **18** | 89 | предметы, еда, доставка, склад |
| `JobSpatialPyramid.cs` | 7 | 69 | клейм и учёт работ |
| `ZoneJobRouter.cs` | 10 | 83 | зоны, 60-с холд, репуш |
| `AgentDataPool.cs` | **16** | 16 | все поля агентов (SoA) |
| `JobValidator.cs` | **1** | 11 | **изолирован** — самый безопасный для правок |
| `WorkerAssignmentSystem.cs` | **1** | 9 | **изолирован**, но и не покрыт тестами |

Практический вывод для плана: **`JobValidator` и `WorkerAssignmentSystem` — безопасные точки входа** для экспериментов (FEAT-J07/J17 можно начать там, не задевая горячий путь).

### 4.5 Конкурентность: где реально стоят локи

| Лок | Кто берёт | Кто конкурирует | Влияние |
|---|---|---|---|
| `FlatIdleWorkerPool._lock` | `Add/Remove/Collect/UpdateWorkerChunk` | диспетчер + `ZoneManager` + **все** хендлеры при завершении | сериализует выдачу |
| `GroundItemManager._lock` | `HasItemsAt`, `TryReserve*`, `Take`, `Spawn` | 16 потоков Phase2 (еда/предметы) + диспетчер + аудит | J-22 |
| `JobSpatialPyramid._registerLock` | `RegisterJob`, `RemoveJob`, макро-маска | все менеджеры при разметке (UI-поток!) | J-15 |
| `ConstructionManager._lock` | **все** аксессоры сайтов | `CanAgentExecute` в клейме + `ReconcileTick` + UI | горячий путь клейма |
| `ZoneJobRouter` `lock(gate)` | `Sweep`, `DequeueNearest`, `OnZoneRemoved` | sim-поток × диспетчер зон | J-04 + J-16 |
| `HierarchicalPathfinder._cacheLock` | кеш регионов | **16 потоков Phase2** | уже отмечен как кандидат (`AI_PROJECT_MAP.md:90-C`) |
| `_zoneQueues` (lock при росте) | `SetQueuedAt` | пуш из UI-потока | редкий, но блокирующий |

Ни один из этих локов не имеет метрики ожидания ⇒ профиль показывает только «итог», а не виновника (FEAT-J05 п.5-6).

### 4.6 Чего в графе нет (слепые пятна)

- `hotpath` неприменим: горячий путь не из `_Process`.
- `Impact`/`callers` не видят вызовы через `Instance`-синглтоны при динамическом `JobRegistry.GetHandler(type)` — то есть **все** `handler.*` вызовы в графе отсутствуют: `JobDispatcher` не имеет рёбер к 8 хендлерам, хотя вызывает их на каждом клейме (`JobDispatcher.cs:129`, `:209`).
- `mutators` не видит SoA-записи (см. §0.3).
- Ни один инструмент не видит **динамику** (латентность, очереди) — только структуру.

---

## 5. Оценка работ и порядок

### 5.1 Порядок (три волны, зависимости явные)

**Волна 0 — «сначала измерь» (без неё остальное — угадывание).**

| # | Работа | Файлы | Дней | Зависимости |
|---|---|---|---|---|
| 0.1 | FEAT-J05 скоупы (`ZoneSweep`, `JobClaim`, `Path.AStar`, `Idle.Pool`) | 4 файла | **0.5** | — |
| 0.2 | FEAT-J06 метрики (8 серий + гистограмма латентности) | `JobDispatcher`, `ZoneJobRouter`, `AgentSimulationThread` | **1.0** | 0.1 |
| 0.3 | FEAT-J07 инвариант-аудит (доказательство S3) | новый файл + вызов | **1.0** | 0.2 |
| 0.4 | Базовый замер: сейв пользователя, 60 с, 3 сценария (idle / ферма 70k / стройка) | — | **0.5** | 0.2 |

**Волна 1 — быстрые победы (низкий риск, прямой эффект на S2/S5).**

| # | Работа | Файлы | Дней | Эффект (ожидаемый) |
|---|---|---|---|---|
| 1.1 | FEAT-J01 бюджет на каждой итерации + дедлайн внутрь клейма | `JobDispatcher`, `JobSpatialPyramid` | **1.0** | спайки 60–370 мс → ≤5 мс |
| 1.2 | FEAT-J02 окно сбора пула (без потолка-головы) | `FlatIdleWorkerPool`, `JobDispatcher` | **0.5** | каждый бездельник получает шанс |
| 1.3 | **J-04** инкрементальный `Sweep` | `ZoneJobRouter` | **1.0** | −40…80 мс/с в `TotalStepCycle`, −1…6 МБ/с мусора |
| 1.4 | FEAT-J16 `DequeueNearest` без дренажа | `ZoneJobRouter` | **0.5** | нет фризов на зонах с большой очередью |
| 1.5 | FEAT-J17 аудит без слепых приговоров | `JobValidator` | **1.0** | меньше 30-с «снов» живой работы |
| 1.6 | J-13 (0 = блокировка) + J-21 (`jid > 0`) + удаление мёртвых параметров | 3 файла | **0.3** | снятие тихих блокировок |

**Волна 2 — структурные (S1/S3/S4).**

| # | Работа | Файлы | Дней | Эффект |
|---|---|---|---|---|
| 2.1 | FEAT-J03 индекс 1024×1024 + адаптивное окно скана (J-05, J-03) | `JobSpatialPyramid` | **2.0** | локальность на 100 % карты; хвост латентности падает с (N−63) до ~const |
| 2.2 | FEAT-J08 watchdog по прогрессу + `Evacuating` таймаут (J-08, J-09) | `AgentSimulationThread` | **1.5** | исчезают вечные «занятые» |
| 2.3 | Инвариант класса Worker (J-10) + `OnCancel` при откате (J-11) | 8 хендлеров + `JobDispatcher` | **1.0** | Worker не уходит из зоны; нет утечек резервов |
| 2.4 | FEAT-J14 унификация политики холда | `ZoneJobRouter`, `ZoneManager` | **0.5** | убирает расхождение «60 с ждём / выдаём сразу»; латентность перестаёт зависеть от того, кто первым увидел работу |
| 2.5 | FEAT-J13 стройка: опция групповых гейтов + `WaitingAll`-ремонт + убрать мутацию в предикате + стоп-кран 2048 | `ConstructionManager`, `ConstructionJobHandler`, `JobBroker` | **2.5** | S4 закрыт |
| 2.6 | FEAT-J11/J12 рассрочка и вынос разметки с UI-потока | `FarmJobManager`, `FarmingTool`, `ZoneDraftTool` | **2.0** | S5 «на входе» закрыт |
| 2.7 | FEAT-J15 дешёвое покрытие зон | `ZoneCoverageMap` | **0.5** | меньше GC |

**Волна 3 — мир (S1/S6), только после 2.1.**

| # | Работа | Файлы | Дней | Эффект |
|---|---|---|---|---|
| 3.1 | FEAT-J10 маршруты вне острова + подключить бюджет A* | `HierarchicalPathfinder`, `AgentMovementService` | **3.0** | обход воды/гор за кромкой; Phase2 под контролем |
| 3.2 | FEAT-J09 исполнитель мира (`WorldJobBoard` + класс агента) | `WorldJobBoard`, `JobDispatcher`/новый, `StreamingToolPlacement` | **4.0** | S6 по-настоящему |
| 3.3 | Снять гейт `WorldBlueprintsSupported` + убрать/переписать текст | `WorldPlacementPass.cs:37`, `BuildTool.cs:190-197` | **0.2** | S6 |

**Технический долг (параллельно, любой волной):** FEAT-J18 (мёртвый код, доки) — **1.0** дня; тесты на дефекты — **2.0** дня (см. §5.3).

**Итого:** волна 0 — 3 дня; волна 1 — 4.3 дня; волна 2 — 10 дней; волна 3 — 7.2 дня; долг/тесты — 3 дня. **≈27.5 чел-дней**, из них первые **7.3 дня** дают основной измеримый эффект по S2/S3/S5.

### 5.2 Что даёт каждая волна (ожидания, не обещания)

| Симптом | Волна 0 | Волна 1 | Волна 2 | Волна 3 |
|---|---|---|---|---|
| **S2** «медленно выдают» | доказательство (метрики) | −спайки, −сны аудита, каждый бездельник виден | −латентность хвоста, −60 с холда | — |
| **S3** «фантомы» | **доказательство** (инвариант-аудит) | — | фикс (watchdog/класс/откат) | — |
| **S4** «не строят» | видно, где встало | — | **фикс** | — |
| **S5** «фризы 70k» | цифры `Sweep` | −O(70k) в `Sweep` | **фикс на входе** (рассрочка) | — |
| **S1** «не идут за 1024» | — | — | — | **фикс** (маршруты) |
| **S6** «гейт» | — | — | — | **фикс** (исполнитель) |

### 5.3 Тесты, которых не хватает (все — новые, `tests/`)

Существующие тесты покрывают **механику**, но не **латентность и состояния**:
`test_zone_agents.cs` (60-с холд, watchdog stuck, Worker-изоляция — пункты а–ж), `test_zone_router.cs` (пуш/покрытие/дедуп/репуш 30 с/Unassign), `TestJobRaceStress.cs` (12 потоков × 4 роли, инварианты счётчиков), `TestAdversarialJobDistribution.cs` (65 536 работ), `BenchJobAssignment.cs` (стоимость регистрации/клейма), `TestAdversarialConstruction.cs` (500×500 чертежей).

**Нужно добавить:**
1. `TestJobClaimLatency` — 8 000 бездельников, 250 работ в одном чанке; измерить p95 «регистрация → клейм»; принять ≤ 5 игросек. Ловит J-03/J-05.
2. `TestPhantomStateInvariant` — прогнать 60 с игрового времени со случайными отменами/нуждами; проверить `Σ(не-Idle) == Σ(живых jobId) + Workers + Evacuating`. Ловит J-07/J-08/J-09/J-10.
3. `TestDispatchBudget` — 10 000 бездельников, 1 работа на дальней границе; проверить, что время вызова ≤ 1 мс + потолок 5 мс. Ловит J-01/J-02.
4. `TestRouterSweepCost` — 70 000 непокрытых работ; `Sweep` ≤ 2 мс. Ловит J-04.
5. `TestCoverageAllocation` — 4 типа зон × 1024²: аллокации на пересчёт ≤ 1 буфера. Ловит J-15.
6. `TestConstructionGroupStall` — партия 5×5, одна клетка с несбрасываемым предметом; за 60 с группа должна достроиться **частично** (или явно сообщить о блокировке). Ловит J-14.
7. `TestWorkerClassInvariant` — после 100 завершений работ `IdleWorkers` не содержит `AgentClass.Worker`. Ловит J-10.

### 5.4 Что нужно от пользователя (рантайм)

Без этих замеров часть выводов останется гипотезами (все помечены `[требует рантайм-проверки]`):

1. **Профиль с новыми скоупами** (FEAT-J05): `Simulation.ZoneSweep`, `Simulation.JobClaim`, `Path.AStar` на 60 с в трёх сценариях. Это ответ на «куда уходит 620–880 мс/с в Phase2».
2. **Числа из нового FEAT-J06**: `dispatch.examined/elapsed`, `jobs.claim_latency_p95`, `jobs.phantom_agents`, `workers.idle_in_global_pool`.
3. **Состояние зависшей стройки** (S4): стадия, `HasObstacles`, `TotalDelivered/Required`, наличие работ по позициям, `CanAgentExecute` для 3 агентов.
4. **Кривая `sys.gc0` / `sys.mem_mb` / `sys.workingset_mb`** (уже пишутся, `AgentSimulationThread.cs:384-386`) при разметке фермы 70k — подтвердит/опровергнет J-04/J-15.
5. **Проверка S1/S6 на практике:** метка рубки и чертёж на клетке (1030, 500): появляется ли работа, живёт ли она дольше 30 с, идёт ли агент.
6. **Есть ли в сейве пользователя `AgentClass.Worker`** (рекрутированные зонами) — от этого зависит вес J-10.

### 5.5 Главные выводы в одну строку каждый

1. **S2 — не «медленный поиск», а три наложенных ограничителя:** бюджет, который проверяется раз в 64 агента (J-01), окно скана 64 на чанк (J-03) и кулдауны 5/15/30 с, скрывающие **все** работы сразу через O(1)-гейт (J-06в, J-17).
2. **S5 — два разных фриза:** на входе (UI-поток + 70k × локи + пересчёт покрытия, J-15) и в steady-state (`ZoneJobRouter.Sweep` дренирует 70k на каждый диспатч, J-04, **вне профиля**).
3. **S3 — измерение, а не (только) утечка:** HUD считает занятыми всех, кто не в пуле бездельников (J-07); настоящие вечные «занятые» — waypoint-застрявшие (J-08), `Evacuating` (J-09) и Worker'ы (по дизайну).
4. **S4 — стройка стоит из-за глобальных гейтов и голода haul-работ:** группа ждёт 100 % расчистки и 100 % доставки (J-14), а при >2048 непокрытых работ переноска вообще не создаётся (`JobBroker.cs:301`).
5. **S1/S6 — за кромкой 1024 нет ни исполнителя, ни маршрута, а работа удаляется аудитом:** `IsTargetWalkable` (`JobValidator.cs:320-327`) + `WorldBlueprintsSupported = false` (`WorldPlacementPass.cs:37`). Снять гейт без исполнителя нельзя — `WorldJobBoard` уже написан, но не подключён.
6. **Индекс работ спроектирован под 512×512, а остров — 1024×1024** (J-05): 75 % площади карты живёт в «схлопнутых» чанках, поэтому «работа в 10 тайлах» может не находиться.