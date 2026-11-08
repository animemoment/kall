# ARCHITECTURE.md — остров × бесконечный мир: карта взаимосвязей

ОБЯЗАТЕЛЬНО К ПРОЧТЕНИЮ перед любой правкой, которая касается мира за кромкой,
координат, заданий, персистентности. Обновлено 2026-10-25 (исполнитель §29).

## 1. Суть: два слоя

Игра — НЕ «маленькая карта внутри большой». Это два слоя с разными движками:

| Слой | Что это | Движок/структуры | Границы |
|---|---|---|---|
| **СИМУЛЯЦИЯ (остров)** | Колония: агенты, задания, предметы, нужды | Плоские массивы `bool[,][,]` 1024×1024 (`SimulationContext`: TreeOnGrass, StoneOnGrass, Ground, SolidWalls), SoA-пулы агентов, `JobData` int, `JobSpatialPyramid` — мировые словари `ConcurrentDictionary<(cx,cy),ChunkRec>` (чанк 32×32, макро 128×128; клампов НЕТ с §120.7: скан = кольца 1..3 → макро-кольца r≤8 → фолбэк-перечисление), flow-field + H-патфайндер острова, `GroundItemManager` + `StockpileManager` мировые (чанки-словари) | любые int (канвас); мировые = WorldOrigin+канвас |
| **МИР** | Процедурная планета 1 048 576×524 288 клеток | `UnifiedWorldGenerator` (семпл по WorldOrigin+local), чанк-стор (персистентность), `StreamingWorldView` (рендер, resident-окно 3..16 чанков = 192..1024 клетки вокруг КАМЕРЫ), `WorldPathfinder` (HPA, окно 128..640 клеток), `WorldCostCache` (64×64 чанки, абсолютные) | бесконечно |

**Почему так:** плоские массивы = O(1) доступ в горячих циклах (60 FPS × N агентов).
Разреженные структуры (словари) здесь в 10–50× медленнее. Поэтому «перенести всю
симуляцию в мир» = перепись ядра, а не правка. Вместо этого строятся МОСТЫ:
агенты острова дотягиваются за кромку, данные/рендер живут в мире.

## 2. Пространства координат (главный источник багов)

| Пространство | Тип | Кто его использует |
|---|---|---|
| **Локальный холст** | long (int у менеджеров) | чанк-ключи `WorldCoordinates.ChunkForTile` (FloorDiv, отрицательные валидны), планы/метки чанк-стора, помпа, `StreamingToolPlacement`, ВСЕ менеджеры (ConstructionManager, JobBroker, TreeJobManager, StructureManager, FarmJobManager, StockpileManager), JobData/JobDispatcher, MapRenderer |
| **Индекс внутри чанка** | ushort `y*64+x` | `_blocks` в StreamingWorldView, payload чанков |
| **Абсолютный мир** | long = WorldOrigin + локальный | `IslandOverrideStore` (.island.delta), `WorldPathfinder`, `WorldCostCache`, генератор (UnifiedWorldGenerator семплит WorldOrigin+coords), WorldRegions |

Правило: **WorldOrigin — одна дверь** (`UnifiedWorldGenerator.WorldOriginX/Y`).
Перевод локальный↔мировой — только в `IslandMutationMirror` (origin+local→world)
и в реплеях (world−origin→local, см. §5). Свою формулу не заводить.

Предел int: координаты заданий/менеджеров — int (±2,1 млрд); помпа отсекает
клетки дальше ±536 млн от origin (не бывает рядом с камерой).

## 3. Мосты «агенты острова → мир»

1. **Движение**: `AgentMovementService.RequestHierarchicalPath` — если старт ИЛИ цель
   за [0,1024)² → `WorldPathfinder.TryFindPath(абсолютные)` → распаковка в
   short-вейпоинты (±32768, дальше честный отказ + кулдаун). Проходимость за кромкой:
   `WorldCostCache.IsWalkable` (абсолютные, семпл генератора: вода/гора = блок,
   + оверлей MarkBlocked — стены §29).
2. **Помпа заданий** `Main.PumpWorldPlans` (тик 500 мс из `_Process`, НЕ привязан к
   камере): собирает планы resident-чанков (`CollectPendingPlans`), фильтрует
   островные (IsIslandCell), для за-кромочных:
   - `WallPlan` → `ConstructionManager.StartSitesBatch(WoodWall, mapData:null)` —
     обычный конвейер (доставка брёвен → монтаж);
   - `CutTreePlan` → `TreeJobManager.MarkTreesBatch` (без ctx → stand = сама клетка);
   - `MinedStonePlan` → `StoneJobManager.MarkStonesBatch` (маска null);
   - + восстановление прогресса из payload островного стора (§5).
3. **Гейты хендлеров за кромкой**: `CanAgentExecute` смотрит `(uint)tx >= MapWidth` →
   «мировая» ветка: задание валидно, если существует метка/сайт
   (IsTreeMarked/IsStoneMarked/IsSiteAt). Мировых NPC НЕТ — работают агенты острова.
4. **Завершение** (события CallDeferred → главный поток):
   - `ConstructionManager.CompleteConstruction` для мирового сайта — ранний выход
     (БЕЗ StructureManager — иначе двойной рендер), событие `OnSiteCompleted`;
   - `TreeJobManager.CompleteTree` → `OnTreeChopped`; `StoneJobManager.CompleteStone`
     → `OnStoneMined`;
   - Main-подписчики: `CompleteWorldPlanBlock(manager, planKind, x, y)` — чанк
     `Plan→Done` атомарно (F-009 `TryReplaceBlock`); если Done не встал — план
     снимается (нет «вечной метки» при закрытой задаче).
5. **Отмена игроком**: `TryRemoveWall` → `CancelSite` (сайт умирает вместе с чертежом);
   `TryCancelCutTree` → `UnmarkTree`; `TryCancelMineStone` → `UnmarkStone`.
   Симметрия «снял план → нет исполнения» — инвариант, проверен тестом.
   Снятие ГОТОВОЙ стены открывает клетку мировому пути (MarkUnblocked+Invalidate).
6. **Предметы** (2026-10-25, п.4): `GroundItemManager` — мировой (чанки-словарь,
   клампов нет). Дроп за кромкой падает НА МЕСТО (TreeChopping/Mining Commit);
   haul-подбор — те же хендлеры (гейта острова нет), движение через WorldPathfinder;
   персистентность — метки 0x06 островного стора (широкая сверка снапшота +
   широкий Enumerate реплея, см. §8 п.2).

Политика «что разрешено за кромкой» — ЕДИНЫЙ файл `WorldPlacementPass`:
`WorldBlueprintsSupported=true` (стены), `WorldBuildingBlueprintsSupported=true`
(§120.8, 2026-10-25: мебель/здания — байт BuildingType в хвосте payload BuildPlan
БЕЗ смены SchemaVersion; постановка пишет тип в островной стор, помпа поднимает
сайт с `StructureRegistry.Get(type)`; готовое Building мировой путь НЕ блокирует —
BlocksMovement только у стен; снятие чертежа → CancelSite).

## 4. Два хранилища (не путать)

| | Чанк-стор | Островной стор |
|---|---|---|
| Файлы | `user://infinite_world_lab_cache/world_{seed:x16}/chunk_{X}_{Y}.bin` | `user://colonies/{zoneId}.island.delta` |
| Координаты | локальный холст (чанк-ключ = FloorDiv) | АБСОЛЮТНЫЕ (origin+local, пишет IslandMutationMirror) |
| Что хранит | блоки: планы (WallPlan/CutTreePlan/MinedStonePlan/BuildingPlan), готовое (Wall/CutTree/MinedStone), payload ПУСТОЙ (прогресса нет) | метки доменов + payload'ы состояния: стадия/доставка/WorkProgress стройки, доли ChopProgress/MineProgress, склады, грядки, кучи предметов |
| Кто пишет | StreamingToolPlacement (TryPlaceBlock), CompleteWorldPlanBlock (Plan→Done) | IslandMutationMirror (события), IslandStateSnapshot (выход из игры, RefreshPayloads) |
| Кто читает | рендер StreamingWorldView (план в _planLayer, готовое гасит декор), помпа (CollectPendingPlans), гейты WorldGameRules | IslandDeltaApplicator (массивы зоны, ДО ResetColonyState), IslandStateReplay (менеджеры+payload, ПОСЛЕ) |

**Сшивка двух сторов у реплея (исправлено 2026-10-25):** домен-менеджеры живут в
ЛОКАЛЬНЫХ клетках — IslandDeltaApplicator и IslandStateReplay обязаны вычитать
origin при сдаче доменных меток (до фикса кормили мировыми → при origin≠0
стены/грядки/метки после загрузки «улетали в никуда»: словарь принимал,
рендер/массивы — нет; закреплено тестом run_island_store, assert на ЛОКАЛЬНЫЕ клетки).

## 5. Персистентность за кромкой (что переживает перезапуск)

- Чанк-планы и готовые блоки — в чанк-сторе (переживают выгрузку чанка и выход).
- Прогресс — payload'ами в островном сторе (снапшот на выходе, помпа на входе):
  стройка = стадия/доставленные предметы/WorkProgress; рубка/добыча = доля 0..1
  (`IslandPayload.WriteProgressFraction`).
- ВосстановлениеSites: помпа читает payload → стадия/доставка/прогресс на сайт,
  RegisterBlueprint/RegisterConstructionAt перезапускают jobs-трубку.
- Граница снапшота: worldSiteKind включает WallPlan/BuildingPlan/CutTreePlan/
  MinedStonePlan; с §120.8 — широкий Enumerate ЗА окном зоны (багфикс: прогресс
  мировых планов терялся, теперь payload пишется и вне окна; тип — тем же байтом).

## 6. Чек-лист «новая фича за кромкой» (6 точек, пропуск = «вечная метка»)

1. `WorldDeltaKind`: kind + `PlanFor`/`DoneFor` + `BlockingKinds` (занятость клетки).
2. Кейс в `Main.PumpWorldPlans` (план → регистрация работы в менеджере-источнике).
3. Гейт в хендлере: мировая ветка CanAgentExecute (координаты вне массивов ctx →
   валидность = существование метки/сайта; гварды на массивы в Commit).
4. Подписка завершения в Main (событие менеджера → CompleteWorldPlanBlock).
5. Кейс в `IslandStateSnapshot` (+ чтение в помпе) — иначе прогресс не переживает
   перезапуск.
6. Отмена в `StreamingToolPlacement` (снятие плана + снятие метки/сайта).
   Тест: копия `TestWorldConstructionExecutor` (tests/TestDestroyerStreaming.cs) —
   полный контур план→задание→завершение→чанк + персистентность + отмена.

## 7. Инварианты (нарушение = баг класса «шов»)

- **ghost==commit**: гейт превью (CanPlace*) и запись (TryPlace*) — одно решение
  (WorldPlacementPass). Проверено тестом.
- **Plan→Done атомарно** (F-009): только TryReplaceBlock, не Remove+Add.
- **Идемпотентность реплея**: повторное применение дельт = 0 изменений.
- **События — CallDeferred**: менеджеры бросают события отложенно; в headless-тестах
  они НЕ доезжают — тесты дёргают связки напрямую (CompleteWorldPlanBlock).
- **Симметрия отмена/исполнение**: снятие плана снимает задание; закрытие задания
  закрывает план.

## 7.1 Правило слоёв: `core` НЕ ссылается на `simulation` и `ui` (L-03)

**Правило (обязательное, проверяется на ревью).** Направление зависимостей —
строго вниз:

```
ui  →  simulation  →  core
ui  →  core        (разрешено: ui — верхний слой, ему видно всё)
simulation  →  core  (разрешено)
core  →  simulation  ✗ ЗАПРЕЩЕНО
core  →  ui          ✗ ЗАПРЕЩЕНО
```

`src/core/**` — слой-фундамент: данные, формулы, профайлер, события,
хранилища. Он не знает ни о менеджерах работ, ни о сценах, ни о рендере.
`core` не должен: `using Game.Simulation;`, `using Game.UI;`, обращаться к
`*.Instance` симуляции, принимать/возвращать её типы, подписываться на её
события. Если нижнему слою нужны данные верхнего — заводится интерфейс в
`core` и реализуется в `simulation`/`ui` (пример задуманного решения —
`IWorldAccess`).

**Почему это дорого.** Цикла в графе нет (`viz_cli cycles --scope src` = 0),
поэтому ошибка не падает, а тихо копится: `core` начинает зависеть от
верхнего слоя, и любой рефакторинг симуляции тянет за собой фундамент.
Также такой файл невозможно покрыть headless-тестом без запуска мира.

**Как помечать.** Место-нарушение помечается комментарием
`// L-03: нарушение слоёв — см. docs/ARCHITECTURE.md`, сама зависимость
при этом НЕ переписывается точечно: развязка делается отдельной волной
(интерфейс в `core` + реализация сверху), иначе правка «в один файл»
ломает сборку. Полный список — ниже.

**Текущие нарушения (21 связь, реестр `_plan/BUGS_REGISTRY.md` §6 L-03).**
Помечены в разрешённой зоне; файлы `src/core/world/**` в этой волне не
трогались — метки там ставит координатор:

| Файл | Тянет наверх | Связей |
|---|---|---|
| `src/core/world/mutation/IslandDomainReplay.cs` | `FarmJobManager` ×4, `StockpileManager` ×3, `StructureManager` ×2, `ConstructionManager` ×2, `StoneJobManager` ×2, `TreeJobManager` ×2 | 15 |
| `src/core/world/mutation/IslandStateReplay.cs` | `JobBroker` ×2, `ConstructionSite` | 3 |
| `src/core/world/FertilityMap.cs` | `CropGrowthManager` | 1 |
| `src/core/world/IslandOverrideStore.cs` | `DynamicWorkScheduler` | 1 |
| `src/core/common/SimulationWatchService.cs` | `StockpileManager.Instance` (`using Game.Simulation`) | 1 |

Примечание: связь `SimEvents.cs → ZoneJobRouter`, указанная в реестре,
на 2026-11-05 в коде отсутствует (после J-04 файл не ссылается на
симуляцию) — строка реестра устарела, новых нарушений в `SimEvents.cs` нет.

## 8. Известные долги (поимённо, 2026-10-25)

1. ~~**Стены за кромкой не блокируют мировой поиск пути**~~ — ЗАКРЫТО 2026-10-25
   (долг п.5): `WorldCostCache.MarkBlocked/MarkUnblocked` (оверлей
   `ConcurrentDictionary`, АБСОЛЮТНЫЕ координаты, read-merge поверх чанков —
   чанки не инвалидируются, воркеры читают без lock); ставится
   `Main.CompleteWorldPlanBlock` (Wall), снимается `TryRemoveWall` (готовой
   стены); окна сбрасывает `WorldPathfinder.Invalidate()` (дебаунс 200 мс,
   хвост доезжает в GetOrBuild — одинокая стена не протухает). Перезапуск:
   помпа `Main.SyncWorldWallCosts` (500 мс) идемпотентно перерегистрирует
   стены resident-чанков (ApplyReady кладёт блоки с диска БЕЗ событий).
   Смена мира: `InitStreamingWorld` чистит оба кэша. Тест:
   TestDestroyerStreaming ч.4б/6а/6б (блокирует/переживает рестарт/открывается).
   ОГРАНИЧЕНИЕ (существующее, отдельный долг): короткая пара (<1 региона HPA
   16×16) при заблокированном прямом LOS даёт false — агент идёт напрямую.
2. ~~**Предметы за кромкой**: дроп брёвен/камня за кромкой отключён~~ — ЗАКРЫТО
   2026-10-25 (долг п.4): `GroundItemManager` мировой — чанки-словарь без клампов
   (кламп 0..63 телепортировал дроп на кромку), спираль резерва от реальной позиции
   агента (в т.ч. отрицательные клетки), spill за кромкой проверяет проходимость
   через `WorldCostCache` (одна дверь), дроп в TreeChopping/Mining Commit падает
   НА МЕСТО ((tx,ty) за кромкой), ноша при отмене (StockpileHauling/
   BlueprintDelivery OnCancel) падает на место без клампа 0..MapWidth.
   Персистентность: метки 0x06 в островном сторе по АБСОЛЮТНЫМ координатам —
   снапшот пишет ВСЕ кучи (широкая сверка, без окна зоны), реплей читает широким
   Enumerate (kind-фильтр) → SpawnItems на канвас-координатах; формат стора не
   менялся. Рендер: существующий MultiMesh (px = tile×64, любые int), AABB
   расширен за остров. Подбор: haul-работы регистрируются на любых клетках,
   движение через WorldPathfinder. Тест: TestDestroyerStreaming ч.9/9б
   (дроп/резерв/подъём/spill на мировых и отрицательных клетках + round-trip
   снапшот→реплей). Оставшиеся пределы (осознанные): Reserved не персистится
   (как на острове), общие капы снапшота 16384/тип (мировые кучи вытесняют
   островные из рендера при переполнении), мировые haul-работы индексируются
   пирамидой в СВОИ чанки (§120.7: клампов нет, скан честный).
3. ~~Мебель/здания за кромкой отказаны~~ — ЗАКРЫТО §120.8 (2026-10-25): байт
   BuildingType в хвосте payload BuildPlan (SchemaVersion не поднимался — сейвы
   совместимы), TryPlaceBuilding(x,y,type) пишет payload в островной стор
   (TryApply создать / TrySetPayload перезаписать), помпа BuildingPlan →
   StartSitesBatch ПО ТИПАМ + RestoreWorldSites (стадия), снятие → CancelSite.
   Готовое Building мировой путь НЕ блокирует (BlocksMovement только у WoodWall).
   Тест 6в/6в-2: Bed полный цикл + рестарт типа (Nightstand из payload).
   Долг: per-type текстуры мирового рендера (сейчас generic ProcessOfWork).
4. **Зоны склада/фермы за кромкой** — метки-намерения; склад частично исполним
   (§120.6: зоны-слоты мировые — координаты без клампов, депонирование/резерв/
   выдача/персистентность работают, тест 10/10б); исполнитель фермы — долг.
5. **Площадка стены под деревом за кромкой** не запускает расчистку (mapData=null,
   стадии Clearing нет) — косметика.
6. ~~Помпа мировых стен хардкодит WoodWall~~ — стены так и живут (единственный
   тип стены); мебель идёт по типу из payload (§120.8).

## 9. Тесты-стражи (что чем доказано)

| Раннер | Что доказывает |
|---|---|
| run_destroyer_streaming | TestWorldConstructionExecutor: стены (план→сайт→доставка→монтаж→Wall, БЕЗ StructureManager), рубка/камень (план→метка→задание→Plan→Done), персистентность прогресса, отмены, дедуп помпы; ч.4б/6а/6б — стена блокирует WorldCostCache+путь в обход, синк переживает Clear («рестарт»), снятие готовой стены открывает клетку; ч.6в/6в-2 (§120.8) — мебель Bed за кромкой: тип в payload → сайт нужного def → доставка/монтаж/Done, путь свободен, снятие убивает сайт, рестарт сохраняет тип; ч.9/9б — мировые кучи: дроп/резерв/подъём/spill + round-trip снапшот→реплей; ч.10/10б (§120.6) — мир-склад: зоны/слоты/резерв/выдача + персистентность |
| run_world_cost_cache | стоимости из генератора + оверлей блокировок (потоки: читают воркеры) |
| run_world_pathfinder | мост движения: обход берега, пути 1024–4096 за кромкой, short-вейпоинты |
| run_island_store | зеркало, world↔local, домен-реплей в ЛОКАЛЬНЫХ клетках, round-trip |
| run_adversarial_construction | конвейер стройки, идемпотентность, самопочинка ReconcileTick |
| run_work_progress_persistence | прогресс переживает прерывание/перезапуск |
| run_architecture_guards | ракетка: обходы политики/магии ловятся. С 2026-10-25 (§120.9, PLAN.md) планка `magic_1024` = **0** — в src нет ни одного литерала 1024: канон острова `WorldModel.IslandTiles = 1 << 10` (и `ActiveArea.Size` от него), канон планеты — `WorldRegions.WorldRegionsX/Y`; `coverage_512` = 0. `is_island_cell_defs`/`decor_pass_defs` = 1 — счётчики САМИХ канонических определений (1 = дублей нет; ноль означал бы удаление канона) |
| run_island_zone_persistence, run_phase5_suite, run_store_exit | зоны, стриминг, выход (FlushResidentDeltas) |

## 10. Планирование правок

Новый план работ по миру/агентам — СНАЧАЛА графовый визуализатор
(`dependency_graph_visualizer/viz_cli.py`, см. VIZ_API.md: callers/callees/impact),
потом текст плана. Индекс понимает и событийные цепочки (kind="event_dispatch":
`X.Event += Handler` → ребро «файл-источник события → файл хендлера»), так что
CallDeferred-конвейеры §29 видны в callers/impact/cycles. Для совсем динамических
связей (строковые имена, рефлексия) — grep по имени; прямые call-графы и события
визуализатор покрывает. Перед использованием после правок кода — `--rebuild`
(кэш по mtime инвалидируется сам, но версия схемы — только пересборкой).
