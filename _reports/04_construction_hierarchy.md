# Баг #5 — жёсткая иерархия строительства (диагностика)

**Проект:** `D:\bebebe-main (1)\bebebe-main` · Godot 4.7 / .NET 8
**Дата:** 2026-10-21 · режим: только чтение `src/` и `scenes/`, игра и `dotnet build` не запускались.

> **Резюме одной строкой.** Флаги стадий существуют и в норме соблюдаются, но **ни один переход не проверяет мир повторно**:
> этап 1 закрывается внешним событием (частичная выгрузка → «клетка чиста»), этап 2 закрывается счётчиком job-индекса,
> а не комплектом площадки, гейт «весь объект целиком» физически отключён (`WaitForAllSiteResources = false`),
> и после перехода стадия **никогда не пересматривается** (`ReconcileTick` сверяет мир только в `Clearance`).
> Отсюда все симптомы: стройка на грязной клетке, монтаж при неполном комплекте, доставка и стройка одновременно
> на соседних клетках одного объекта.

---

## 1. ТЕКУЩАЯ МАШИНА СОСТОЯНИЙ СТРОЙКИ

### 1.1. Стадии и где они объявлены

`src/simulation/structures/ConstructionSite.cs:7-14`

```csharp
public enum ConstructionStage : byte
{
    Clearance = 0,   // ЭТАП 1: расчистка footprint
    Supplying = 1,   // ЭТАП 2: поднос ресурсов
    WaitingAll = 2,  // ЭТАП 2 (группа): клетка укомплектована, ждём остальные клетки объекта
    Assembling = 3,  // ЭТАП 3: возведение
    Completed = 4,
}
```

Состояние клетки: `ConstructionSite.cs:22` (`Stage = Clearance` по умолчанию), флаги препятствий `ConstructionSite.cs:25-27`
(`NeedTree/NeedStone/NeedHaul`), доставка `ConstructionSite.cs:30-31, 73-99`, `IsFullySupplied` — `73-85`,
`HasObstacles => NeedTree || NeedStone || NeedHaul` — `101`.

Менеджер — **`ConstructionManager`** (`src/simulation/structures/ConstructionManager.cs`).
`ConstructionPipeline.cs` и `BlueprintManager.cs` — пустые адаптеры (`ConstructionPipeline.cs:8-11`, `BlueprintManager.cs:8-13`):
`ConstructionPipeline.StartSite` → `ConstructionManager.StartSitesBatch` (`:18`),
`IsStageAllowed` → `ConstructionManager.IsStageAllowed` (`:58`), `ReconcileTick` → `ConstructionManager.ReconcileTick` (`:63`).
Вся логика пайплайна — в `ConstructionManager` (650 строк).

### 1.2. Все точки перехода стадий (полный список)

| # | Переход | Файл:строка | Условие (как записано в коде) | Мир? |
|---|---------|-------------|-------------------------------|------|
| T0 | создание → `Clearance` | `ConstructionManager.cs:155-173` | `Stage` по умолчанию; флаги `Need*` сняты с `MapData.TreeOnGrass/StoneOnGrass` и `GroundItemManager.HasItemsAt` **в момент размещения** (`:160-166`) | один раз |
| T1 | → `Supplying` (сразу) | `ConstructionManager.cs:168-171` | `if (!site.HasObstacles) site.Stage = Supplying;` | по флагам |
| T1b | → `Supplying` (событие) | `ConstructionManager.cs:265-286` (`CheckAndAdvanceObstacles`) | `clearAction(site)` снимает вызванный флаг, затем `if (stage == Clearance && !HasObstacles) → Supplying` (`:274-277`) | **нет** |
| T1c | → `Supplying` (сверка) | `ConstructionManager.cs:613-633` (`ReconcileTick`, 5 с wall-clock) | пересчёт флагов из `ctx.TreeOnGrass/StoneOnGrass` + `HasItemsAt` (`:615-621`), затем `→ Supplying + RegisterBlueprint` (`:628-632`) | **да**, только `Clearance` |
| T2 | → `WaitingAll`/`Assembling` | `ConstructionManager.cs:304-308` (`DeliverMaterial`) | `if (site.IsFullySupplied) Stage = WaitForAll ? WaitingAll : Assembling` | **нет** |
| T2b | `WaitingAll → Assembling` | `ConstructionManager.cs:327-366` (`CheckGroupFullSupply`) | все клетки группы `IsFullySupplied` (`:338`), смена стадии (`:345-357`) | по счётчикам |
| T3 | `Assembling → Completed` | `ConstructionManager.cs:387-438` (`CompleteConstruction`) | **никакого** условия по стадии/комплекту (вызывается хендлером по таймеру) | нет |
| T4 | восстановление из сейва | `IslandStateReplay.cs:113-139` | `site.Stage = stage` **прямо из payload** (`:125`), `Completed → Assembling` (`:121-122`), затем `RegisterConstructionAt` (`:129-132`) | только payload |

### 1.3. Кто и когда дёргает переходы

**Этап 1 → 2** (все источники):

| Источник | Файл:строка | Когда | Что снимает |
|---|---|---|---|
| Рубка дерева | `TreeChoppingJobHandler.cs:86` (`NotifyTreeCleared`) | после 6 ч работы | `NeedTree` |
| Добыча камня | `MiningJobHandler.cs:94` (`NotifyStoneCleared`) | после 4 ч работы | `NeedStone` |
| Выгрузка на склад | `StockpileHaulingJobHandler.cs:190-191` (`NotifyHaulCleared(Source)`) | после депозита **любого** количества | `NeedHaul` |
| Spill на соседнюю клетку | `GroundItemManager.cs:284-288` | клетка реально опустела | `NeedHaul` |
| Забор предметов | `GroundItemManager.cs:337-341` | **только если** `!HasItemsAt(x,y)` | `NeedHaul` |
| Сверка | `ConstructionManager.cs:586-649` (`ReconcileTick`) | 5 с wall-clock, из `AgentSimulationThread.cs:543-546` | все три |

**Этап 2 → 3** (все источники):

| # | Файл:строка | Условие |
|---|---|---|
| a | `ConstructionManager.cs:311-322` (`DeliverMaterial`, хвост) | `IsFullySupplied && !WaitForAllSiteResources` → `JobBroker.RegisterConstructionAt` |
| b | `ConstructionManager.cs:318-321` → `CheckGroupFullSupply` | `WaitForAllSiteResources == true` и вся группа укомплектована |
| c | `JobBroker.cs:462-474` (`DeliverLogsToBlueprint`) | **счётчик job-индекса** дошёл до `TargetItemCount`, а не площадка |
| d | `ConstructionManager.cs:641-647` (`ReconcileTick`) | стадия уже `Assembling`, но нет Construction-job |
| e | `JobValidator.cs:261-274` | чертёж без работ: сначала delivery, иначе construction (по `IsStageAllowed`) |
| f | `IslandStateReplay.cs:129-132` | стадия из payload == `Assembling` |

### 1.4. Что проверяет стража на входе в работы

| Хендлер | Файл:строка | Гейт этапа 1 | Гейт этапа 2 | Гейт этапа 3 |
|---|---|---|---|---|
| `BlueprintDeliveryJobHandler.CanAgentExecute` | `:17-51` | `:38-43` дерево/камень/вода; `:45-48` **брёвна на клетке не мешают**, остальные предметы блокируют | `:25-29` `Stage == Supplying`, иначе `false` | — |
| `ConstructionJobHandler.CanAgentExecute` | `:17-48` | `:19-25` **сам сдвигает** предметы (`TrySpillItemsAdjacent`); `:40-43` дерево/камень | **отсутствует** — `IsFullySupplied` не читается | `:28-32` `Stage == Assembling` |
| `StockpileHaulingJobHandler.CanAgentExecute` | `:18-30` | `:24` `IsStageAllowed(..., StockpileHauling)` — для этого типа **всегда `true`** (`ConstructionManager.cs:85`) | — | — |
| `JobBroker.RegisterConstructionAt` | `:486-506` | — | — | **никакой** проверки: только дедуп по `(pos,type)` |
| `ConstructionManager.CompleteConstruction` | `:387-438` | — | — | **никакой** проверки, что клетка была `Assembling` |

`IsStageAllowed` (`ConstructionManager.cs:74-86`) — ключевая развилка:

```csharp
if (!TryGetSite(x, y, out var site))
    return true;                                        // :76-77  «вне чертежей разрешено»
if (typeId == JobTypeId.BlueprintDelivery)
    return site.Stage == ConstructionStage.Supplying;   // :79-80
if (typeId == JobTypeId.Construction)
    return site.Stage == ConstructionStage.Assembling;  // :82-83
return true;                                            // :85  рубка/камень/haul разрешены всегда
```

---

## 2. ГДЕ ЛОМАЕТСЯ ПОРЯДОК

### D1. Этап 1 закрывается **частичной** выгрузкой (главный источник «грязной» стройки)

`StockpileHaulingJobHandler.Commit`, `:184-193`:

```csharp
int jobId = pool.CurrentJobId[agentIndex];
if (jobId != -1)
{
    // Этап 1 пайплайна стройки: клетка-источник расчищена —
    // снять флаг haul (идемпотентно; ищем по Source, т.к. Target — слот склада).
    ConstructionManager.Instance.NotifyHaulCleared(
        pool.SourceCellX[agentIndex], pool.SourceCellY[agentIndex]);
    JobDispatcher.Instance.TryUnregisterJob(jobId);
}
```

Агент за рейс уносит `MaxCarryWeight = 25 кг` (`:10`), т.е. ≈29 брёвен, а куча на клетке бывает 21–38 (одно дерево,
`TreeChoppingJobHandler.cs:89`) и до 100 (стак, `ItemRegistry`). Вызов стоит **вне** проверки «клетка опустела»,
в отличие от правильных мест `GroundItemManager.cs:337-341` (`TakeItemsWithType`) и `:284-288` (spill).
Далее `ConstructionManager.CheckAndAdvanceObstacles` верит событию (`:271-279`):

```csharp
if (_sites.TryGetValue((x, y), out site))
{
    clearAction(site);                                     // NeedHaul = false
    if (site.Stage == ConstructionStage.Clearance && !site.HasObstacles)
    {
        site.Stage = ConstructionStage.Supplying;          // :276  мир не проверяется
        becameReady = true;
    }
}
```

и на `:282-285` сразу регистрирует доставку. Итог: доставка/монтаж начинаются, когда в footprint ещё лежат предметы.

### D2. Переход односторонний: после `Supplying` мир больше никто не сверяет

`ReconcileTick` (`:604-648`) пересчитывает препятствия **только** в ветке `Clearance` (`:613-633`).
Для `Supplying` (`:634-640`) и `Assembling` (`:641-647`) выполняется лишь «пересоздать job, если его нет».
Обратных переходов нет нигде: во всём `src/` поле `Stage` присваивается только в T0/T1/T1b/T1c/T2/T2b/T3/T4 (см. 1.2).
Поэтому единожды испорченный флаг (D1) или восстановленная стадия (D6) **не лечатся до сноса** чертежа.

### D3. Гейт этапа 2 в `ConstructionJobHandler` отсутствует, а этап 3 стартует по «счётчику job», а не по площадке

`ConstructionJobHandler.CanAgentExecute:27-36` проверяет **только стадию**:

```csharp
// Разрешено только если площадка в стадии Assembling
if (ConstructionManager.Instance.TryGetSite(job.TargetX, job.TargetY, out var site))
{
    if (site.Stage != ConstructionStage.Assembling)
        return false;
}
```

`IsFullySupplied` не читается ни здесь, ни в `ExecuteParallel` (`:73-88`), ни в `Commit` (`:90-149`),
ни в `CompleteConstruction` (`ConstructionManager.cs:387-438`). То есть **стадия — единственная защита этапа 2**,
и любая ошибка стадии мгновенно превращается в «строим при неполном комплекте».

Параллельно монтаж регистрируется по счётчику job-индекса, а он **независим** от `ConstructionSite._delivered`
(`JobBroker.cs:462-474`):

```csharp
if (isCompleted && accepted > 0)
{
    JobDispatcher.Instance.UnregisterJob(job.Id);
    ConstructionManager.Instance.NotifySupplyDone(x, y);
    if (!BuildConfig.WaitForAllSiteResources)
        RegisterConstructionAt(x, y);          // :470-473  ни стадии, ни IsFullySupplied
}
```

`isCompleted` — это `_currentDeliveredCount[id] >= _targetItemCount[id]` (`JobSpatialPyramid.cs:1248-1260`),
а `_currentDeliveredCount` **обнуляется при каждой регистрации** (`JobSpatialPyramid.cs:358-359`, `:447-448`),
тогда как `_delivered` площадки сохраняется. При перерегистрации (её делает сам `ReconcileTick:636-639`)
два счётчика расходятся: индекс требует ещё полный `TargetItemCount`, площадка принимает только остаток
(`ConstructionSite.AddDelivered:87-99`), излишек выливается на землю (`JobBroker.cs:439-461`).

### D4. Гейт «весь чертёж целиком» отключён конфигом

`src/simulation/building/BuildConfig.cs:58-60`:

```csharp
// --- Гейт 100%: стройка начинается только когда ВСЕ бревна привезены ---
// True = ждем все ресурсы всей стройки, False = старое поведение (по клеткам).
public static bool WaitForAllSiteResources = false;
```

Машина групповой сборки написана (`ConstructionSite.cs:11`, `ConstructionManager.cs:306`, `:318-321`, `:327-366`,
`:345-357`), но при `false` она **мертва**: каждая клетка уходит в `Assembling` по своим 15 брёвнам.
Для объекта из N клеток (стена, нарисованная прямоугольником — `BuildTool.cs:155-180`; объект со складским полом —
`ZoneDraftTool.cs:778`) это означает: **на клетке A уже идёт монтаж, а на клетке B ещё идёт доставка** —
ровно то «этапы перемешаны», на которое жалуется баг. Заодно теряется смысл `WaitingAll`.

### D5. Этап 1 не является барьером для всего footprint

`StartSitesBatch` (`:207-218`) сразу регистрирует доставку для **всех клеток без препятствий**, не глядя на то,
что соседние клетки того же объекта ещё чистятся:

```csharp
var readySupply = new List<(int X, int Y)>();
foreach (var s in addedSites)
    if (s.Stage == ConstructionStage.Supplying) readySupply.Add((s.X, s.Y));
if (readySupply.Count > 0)
    JobBroker.Instance.RegisterBlueprintBatch(readySupply, def.Type, def.TotalRequiredItems);
```

Т.е. «территория очищена на 100%» выполняется **по-клеточно**, а не по footprint. Групповой проверки расчистки
не существует вовсе (есть только групповая проверка комплекта — D4, и та выключена).

### D6. Восстановление доверяет стадии из payload

`IslandStateReplay.cs:121-136`:

```csharp
if (stage == ConstructionStage.Completed)
    stage = ConstructionStage.Assembling;      // :121-122
foreach (var d in delivered)
    site.AddDelivered(d.Item, d.Count);        // AddDelivered клампится к required
site.Stage = stage;                            // :125  напрямую
...
if (stage == ConstructionStage.Assembling)
    JobBroker.Instance.RegisterConstructionAt(x, y);   // :131  без IsFullySupplied
```

Если в payload `Assembling` при недостаче (сохранение сделано уже на «кривой» стадии из D1/D3, либо payload старой
версии) — после загрузки/перестриминга строится объект без комплекта. `TryReadBuildPlan` валидирует только диапазон
байта (`IslandPayload.cs:140-141`), не сверяя его с `delivered`.

### D7. Ремонт «застрявшего» чертежа неполон

`JobValidator` удаляет мёртвые работы только если пропала метка менеджера (`:165-169`, `:300-313`).
Для `BlueprintDelivery/Construction` метка = `BlueprintManager.IsBlueprintAt` = `ConstructionManager.IsSiteAt`
(`BlueprintManager.cs:86-89`) — т.е. пока площадка жива, «никто не может» лечится лишь кулдауном 30 с (`:172`).
Если площадка застряла в `Supplying` с чужим предметом на клетке (доставка блокируется `:45-48`), а конструкция
ещё не зарегистрирована — **клетка висит вечно**: `ReconcileTick` для `Supplying` только пересоздаёт доставку (`:634-640`),
но не возвращает в этап 1.

### D8. Размещение проверяет не всё, что заявлено в `BuildObstacles`

`BuildObstacles.NeedsClear` (`:31-36`) и `BlocksPlacement` (`:41-47`) — «одна точка правды», но
`ConstructionManager.StartSitesBatch` смотрит только дерево/камень/предметы (`:160-166`) и занятость структурой (`:152`).
`IsBlockedByHandmade` (`BuildObstacles.cs:60-68`: грядка, метка грядки, Building) и `TileClearanceService`
(`:112-130`, `:170-189`) им не используются. Прямые вызовы в обход `BuildTool.CanPlaceBlueprintAt`
(`BuildTool.cs:212-255`): `ZoneDraftTool.cs:778`, `IslandDomainReplay.cs:234` — там рукотворные препятствия не фильтруются.

### D9. Монтажник сам «расчищает» footprint

`ConstructionJobHandler.CanAgentExecute:19-25`:

```csharp
if (GroundItemManager.Instance.HasItemsAt(job.TargetX, job.TargetY))
{
    if (ctx != null)
        GroundItemManager.Instance.TrySpillItemsAdjacent(job.TargetX, job.TargetY, ctx.MapWidth, ctx.MapHeight, ctx.SolidWalls);
    if (GroundItemManager.Instance.HasItemsAt(job.TargetX, job.TargetY))
        return false;
}
```

Это маскирует D1/D2 (стройка «сама себе» освобождает место), но нарушает этап 1: предметы не вывозятся на склад,
а сдвигаются на соседнюю клетку — визуально «стройка началась, а вещи телепортировались».

### Сводка симптом → причина

| Симптом из бага | Причина |
|---|---|
| «агенты начинают строить раньше» | D3 (нет гейта комплекта в хендлере) + D1/D2 (стадия могла стать `Assembling`/`Supplying` ошибочно) |
| «стройка стартует при неполном комплекте» | D4 (групповой гейт выключен) + D3 |
| «этапы перемешаны: доставка идёт параллельно стройке» | D4 (у объекта клетки в разных стадиях) + D5 |
| «стройка на неочищенной клетке» | D1 → T1b без сверки мира; D2 не лечит; D9 маскирует |
| «после загрузки/перестриминга всё снова сломано» | D6 |

---

## 3. МИНИМАЛЬНЫЙ ПАТЧ (до/после)

Принципы: **механику работ не меняем** (`BlueprintDelivery`/`Construction`/haul/чоп/майнинг остаются как есть),
меняем только условия переходов и добавляем 3 проверки-инварианта. Порядок применения — P1 → P5; P6 — желателен, P7 — опционален.

### P1. Этап 1 не может закрыться частичной выгрузкой ★ обязательный

`src/simulation/jobs/handlers/StockpileHaulingJobHandler.cs:184-193`

**До:**
```csharp
int jobId = pool.CurrentJobId[agentIndex];
if (jobId != -1)
{
    ConstructionManager.Instance.NotifyHaulCleared(
        pool.SourceCellX[agentIndex], pool.SourceCellY[agentIndex]);
    JobDispatcher.Instance.TryUnregisterJob(jobId);
}
```

**После:**
```csharp
int jobId = pool.CurrentJobId[agentIndex];
if (jobId != -1)
{
    int srcX = pool.SourceCellX[agentIndex];
    int srcY = pool.SourceCellY[agentIndex];
    // ЭТАП 1 закрывается ТОЛЬКО по факту пустой клетки: за рейс агент уносит
    // <= MaxCarryWeight (25 кг ~ 29 брёвен), а куча бывает 21..100 ед.
    if (!GroundItemManager.Instance.HasItemsAt(srcX, srcY))
        ConstructionManager.Instance.NotifyHaulCleared(srcX, srcY);
    JobDispatcher.Instance.TryUnregisterJob(jobId);
}
```

(Можно ещё проще: удалить строки `:187-191` целиком — `GroundItemManager.TakeItemsWithType:337-341` уже
уведомляет площадку ровно в момент, когда клетка опустела. Остаток кучи заберёт следующий агент через
`SweepStockpileHaulJobs`, `JobBroker.cs:286-346`.)

### P2. Мир — авторитет этапа 1, с откатом стадии ★ обязательный (страховка)

`src/simulation/structures/ConstructionManager.cs`, `ReconcileTick`, внутрь `foreach (var site in snapshot)` после `:606`
(до ветки `if (site.Stage == ConstructionStage.Clearance)`).

**До** (`:604-613`):
```csharp
foreach (var site in snapshot)
{
    int x = site.X, y = site.Y;
    if (StructureManager.Instance.HasStructureAt(x, y)) { CompleteConstruction(...); continue; }

    if (site.Stage == ConstructionStage.Clearance) { ... }
```

**После:**
```csharp
foreach (var site in snapshot)
{
    int x = site.X, y = site.Y;
    if (StructureManager.Instance.HasStructureAt(x, y)) { CompleteConstruction(...); continue; }

    // --- ЭТАП 1: единственный авторитет — мир, а не флаги Need* ---
    bool worldTree  = treeArr  != null && (uint)x < (uint)treeArr.GetLength(0)  && (uint)y < (uint)treeArr.GetLength(1)  && treeArr[x, y];
    bool worldStone = stoneArr != null && (uint)x < (uint)stoneArr.GetLength(0) && (uint)y < (uint)stoneArr.GetLength(1) && stoneArr[x, y];
    bool worldItems = GroundItemManager.Instance.HasItemsAt(x, y);
    bool dirty = worldTree || worldStone || worldItems;

    if (site.Stage != ConstructionStage.Clearance && dirty && site.WorkProgress <= 0f)
    {
        // Этапы 2-3 не могут идти на грязной клетке: возвращаем клетку в этап 1.
        // Откат безопасен: монтаж не начат (WorkProgress == 0), _delivered сохраняется.
        bool demoted;
        lock (_lock)
        {
            demoted = _sites.TryGetValue((x, y), out var cur)
                      && ReferenceEquals(cur, site) && cur.WorkProgress <= 0f;
            if (demoted)
            {
                cur.NeedTree = worldTree; cur.NeedStone = worldStone; cur.NeedHaul = worldItems;
                cur.Stage = ConstructionStage.Clearance;
            }
        }
        if (demoted)
        {
            JobBroker.Instance.UnregisterBlueprint(x, y);      // снять delivery + construction
            if (worldTree)  TreeJobManager.Instance.MarkTreesBatch(new List<(int X, int Y)> { (x, y) });
            if (worldStone) StoneJobManager.Instance.MarkStonesBatch(new List<(int X, int Y)> { (x, y) }, stoneArr);
            if (worldItems && StockpileManager.Instance.HasFreeSpace)
                RegisterHaulBatch(new List<(int X, int Y)> { (x, y) });
            continue;   // этап 1 дальше ведёт существующая ветка Clearance
        }
    }

    if (site.Stage == ConstructionStage.Clearance) { ... }   // без изменений
```

Замечания:
* `ReferenceEquals` защищает от гонки с `DeliverMaterial`, который мутирует тот же объект под `_lock`.
* `WorkProgress <= 0f` — гарантия, что откат не тронет уже начатую кладку (`WorkProgress` растёт только в
  `ConstructionJobHandler.ExecuteParallel:83`), т.е. требования «отмена/прогресс» не ломаются.
* Ограничение стоимости прохода: см. риск R6 (слайс по 256 клеток + курсор).

### P3. Жёсткие гейты на входе и на финише монтажа ★ обязательный

`src/simulation/jobs/handlers/ConstructionJobHandler.cs:27-36`

**До:**
```csharp
// Разрешено только если площадка в стадии Assembling
if (ConstructionManager.Instance.TryGetSite(job.TargetX, job.TargetY, out var site))
{
    if (site.Stage != ConstructionStage.Assembling)
        return false;
}
else if (!ConstructionManager.Instance.IsStageAllowed(job.TargetX, job.TargetY, JobTypeId.Construction))
{
    return false;
}
```

**После:**
```csharp
if (ConstructionManager.Instance.TryGetSite(job.TargetX, job.TargetY, out var site))
{
    // ЭТАП 3 наступает только после 1 и 2 — проверяем все три условия, а не одну стадию.
    if (site.Stage != ConstructionStage.Assembling) return false;  // этап 3
    if (site.HasObstacles)                          return false;  // этап 1 не закрыт
    if (!site.IsFullySupplied)                      return false;  // этап 2 не закрыт
}
else if (!ConstructionManager.Instance.IsStageAllowed(job.TargetX, job.TargetY, JobTypeId.Construction))
{
    return false;
}
```

И тот же инвариант на финише — `ConstructionJobHandler.Commit`, **до** строки `:110`:

**До:**
```csharp
else if (state == AgentState.Working && pool.WorkProgress[agentIndex] >= BuildDuration)
{
    if (ConstructionManager.Instance.CompleteConstruction(sx, sy, ctx, pool, out var completedDef))
```
**После:**
```csharp
else if (state == AgentState.Working && pool.WorkProgress[agentIndex] >= BuildDuration)
{
    // Гейт на финише: площадку могли откатить в этап 1/2 между тиками (P2).
    if (!ConstructionManager.Instance.TryGetSite(sx, sy, out var fin)
        || fin.Stage != ConstructionStage.Assembling
        || !fin.IsFullySupplied
        || fin.HasObstacles)
    {
        JobDispatcher.Instance.ReleaseJobWorker(agentIndex, pool, ctx);
        return;
    }
    if (ConstructionManager.Instance.CompleteConstruction(sx, sy, ctx, pool, out var completedDef))
```

### P4. Гейт этапа 2→3 переносим на площадку ★ обязательный

`src/simulation/jobs/JobBroker.cs:462-474`

**До:**
```csharp
if (isCompleted && accepted > 0)
{
    JobDispatcher.Instance.UnregisterJob(job.Id);
    ConstructionManager.Instance.NotifySupplyDone(x, y);
    if (!BuildConfig.WaitForAllSiteResources)
        RegisterConstructionAt(x, y);
}
```

**После:**
```csharp
// Авторитет этапа 2 — площадка (IsFullySupplied), а не счётчик job-индекса:
// при перерегистрации (_currentDeliveredCount сбрасывается в 0) они расходятся.
// Регистрацию монтажа делает сам ConstructionManager.DeliverMaterial (или
// CheckGroupFullSupply) — дублировать её здесь больше не нужно.
bool stillSupplying = ConstructionManager.Instance.TryGetSite(x, y, out var siteAfter)
                      && siteAfter.Stage == ConstructionStage.Supplying;
if (accepted > 0 && !stillSupplying)
{
    JobDispatcher.Instance.UnregisterJob(job.Id);   // площадка уже не в этапе 2 — job не нужен
}
```

`src/simulation/structures/ConstructionManager.cs:298-325` (`DeliverMaterial`) — закрыть вход в этап 3:

**До:**
```csharp
accepted = site.AddDelivered(item, count);
if (site.IsFullySupplied)
{
    site.Stage = BuildConfig.WaitForAllSiteResources ? ConstructionStage.WaitingAll : ConstructionStage.Assembling;
    _cellToGroup.TryGetValue((x, y), out groupId);
}
```
**После:**
```csharp
accepted = site.AddDelivered(item, count);
// Этап 2 -> 3: только из этапа 2 и только на чистой клетке.
bool advance = site.IsFullySupplied
               && site.Stage == ConstructionStage.Supplying
               && !site.HasObstacles;
if (advance)
{
    site.Stage = BuildConfig.WaitForAllSiteResources ? ConstructionStage.WaitingAll : ConstructionStage.Assembling;
    _cellToGroup.TryGetValue((x, y), out groupId);
}
```
(и ниже, во внешнем блоке `:311-322`, опираться на локальный `advance`, а не на повторное чтение `site.IsFullySupplied` —
сейчас это чтение вне `_lock`).

### P5. Групповые барьеры: этап 1 и этап 2 на весь чертёж ★ для строгой иерархии

**5a.** Новый метод в `ConstructionManager` (рядом с `CheckGroupFullSupply`):

```csharp
/// <summary>
/// ЭТАП 1 для всего объекта: клетки переходят в Supplying только когда
/// очищены ВСЕ клетки группы (footprint очищен на 100%).
/// </summary>
private void CheckGroupClearanceReady(int groupId)
{
    List<(ConstructionSite Site, int Remaining)> ready = null;
    lock (_lock)
    {
        if (!_groupToCells.TryGetValue(groupId, out var cells) || cells.Count == 0) return;

        foreach (var pos in cells)
            if (!_sites.TryGetValue(pos, out var s) || s.HasObstacles)
                return;                                    // этап 1 не закрыт для объекта

        ready = new List<(ConstructionSite, int)>(cells.Count);
        foreach (var pos in cells)
        {
            if (!_sites.TryGetValue(pos, out var s)) continue;
            if (s.Stage != ConstructionStage.Clearance) continue;
            int remaining = s.Definition.GetRequiredCount(ItemId.Log) - s.GetDelivered(ItemId.Log);
            if (remaining <= 0) continue;
            s.Stage = ConstructionStage.Supplying;
            ready.Add((s, remaining));
        }
    }
    foreach (var (s, remaining) in ready)
        JobBroker.Instance.RegisterBlueprint(s.X, s.Y, s.Definition.Type, remaining); // заказываем ОСТАТОК
}
```

**5b.** `CheckAndAdvanceObstacles` (`:265-286`) — вместо одиночного перехода вызываем групповой:

**До:**
```csharp
if (site.Stage == ConstructionStage.Clearance && !site.HasObstacles)
{
    site.Stage = ConstructionStage.Supplying;
    becameReady = true;
}
...
if (becameReady && site != null)
    JobBroker.Instance.RegisterBlueprint(x, y, site.Definition.Type, site.Definition.TotalRequiredItems);
```
**После:**
```csharp
_cellToGroup.TryGetValue((x, y), out int gid);
...
if (site != null) CheckGroupClearanceReady(gid);   // вся группа, а не одна клетка
```

**5c.** `StartSitesBatch` (`:207-218`) — заменить немедленную регистрацию доставки на групповой барьер:

**До:**
```csharp
var readySupply = new List<(int X, int Y)>();
foreach (var s in addedSites)
    if (s.Stage == ConstructionStage.Supplying) readySupply.Add((s.X, s.Y));
if (readySupply.Count > 0)
    JobBroker.Instance.RegisterBlueprintBatch(readySupply, def.Type, def.TotalRequiredItems);
```
**После:**
```csharp
// Этап 1 — барьер для всего footprint: доставка стартует только когда
// очищены ВСЕ клетки объекта (внутри — проверка HasObstacles по группе).
CheckGroupClearanceReady(groupId);
```
(у одиночной клетки поведение не меняется: группа из одной клетки проходит проверку сразу).
При этом `if (!site.HasObstacles) Stage = Supplying` в `:168-171` **удалить** — стадию ставит только групповой барьер.

**5d.** Включить групповой гейт комплекта: `BuildConfig.cs:60` → `public static bool WaitForAllSiteResources = true;`
(комментарий `:58-59` этому и соответствует). Обязательно вместе с 5e.

**5e.** Закрыть дедлок `WaitingAll` (см. R2):

* `ConstructionManager.CancelSite` (`:440-472`) и `CancelSitesBatch` (`:474-524`): после удаления клетки из группы
  вызвать `CheckGroupFullSupply(gid)` и `CheckGroupClearanceReady(gid)` для оставшихся.
* `ReconcileTick` — добавить недостающую ветку (сейчас `WaitingAll` не обрабатывается вообще):
```csharp
else if (site.Stage == ConstructionStage.WaitingAll)
{
    // Состав группы мог измениться (отмена/сдача соседней клетки) — перепроверяем.
    if (_cellToGroup.TryGetValue((x, y), out int gidW)) CheckGroupFullSupply(gidW);
}
```

### P6. Восстановление: стадию выводим из факта, а не берём из payload ★ желательный

`src/core/world/mutation/IslandStateReplay.cs:121-136`

**До:**
```csharp
if (stage == ConstructionStage.Completed) stage = ConstructionStage.Assembling;
foreach (var d in delivered) site.AddDelivered(d.Item, d.Count);
site.Stage = stage;
...
if (stage == ConstructionStage.Assembling) JobBroker.Instance.RegisterConstructionAt(x, y);
else if (stage == ConstructionStage.Supplying || stage == ConstructionStage.WaitingAll)
    JobBroker.Instance.RegisterBlueprint(x, y, site.Definition.Type, site.Definition.TotalRequiredItems);
```
**После:**
```csharp
foreach (var d in delivered) site.AddDelivered(d.Item, d.Count);
ConstructionStage st;
if (site.HasObstacles)                          st = ConstructionStage.Clearance;
else if (!site.IsFullySupplied)                 st = ConstructionStage.Supplying;
else if (stage >= ConstructionStage.Assembling) st = ConstructionStage.Assembling;
else                                            st = ConstructionStage.Supplying; // группу добьёт CheckGroupFullSupply
site.Stage = st;
int rem = site.Definition.GetRequiredCount(ItemId.Log) - site.GetDelivered(ItemId.Log);
if (st == ConstructionStage.Clearance) { /* работы пересоздаст ReconcileTick */ }
else if (st == ConstructionStage.Supplying && rem > 0)
    JobBroker.Instance.RegisterBlueprint(x, y, site.Definition.Type, rem);
else if (st == ConstructionStage.Assembling)
    JobBroker.Instance.RegisterConstructionAt(x, y);
```

### P7. Опционально: убрать «саморасчистку» у монтажника

`ConstructionJobHandler.cs:19-25` — после P1/P2/P3 блок можно удалить (заменён на `if (site.HasObstacles) return false;`
из P3). Минус: при редком залипании предмета агент будет ждать, а не расчищать. Плюс: этап 1 снова обслуживают
только haul-работы, как и заявлено в `BuildObstacles.cs:31-36`.

### Что менять **не** нужно

`TreeChoppingJobHandler`, `MiningJobHandler`, `BlueprintDeliveryJobHandler` (его гейт `Stage == Supplying` уже корректен),
`JobDispatcher`, `JobSpatialPyramid`, `GenericJobSpatialIndex`, `AgentDataPool`, `WorkProgressTracker`, UI.
Патч не меняет ни одну длительность, ни один тип работы, ни структуру `JobData`.

---

## 4. РИСКИ

**R1. Групповой барьер этапа 1 (P5a) останавливает весь объект из-за одной клетки.**
Клетка, которую невозможно очистить (дерево на недостижимом тайле, запертое стенами), блокирует доставку всего
здания. Смягчение: `BuildTool.CanPlaceBlueprintAt` уже запрещает камень/воду/здания (`BuildTool.cs:212-255`),
деревья всегда рубимы (`ReconcileTick:636-639` пересоздаёт chop-job, `JobValidator:210-231` — тоже).
Диагностика: логировать `groupId / footprint blocked` в `ReconcileTick` и показывать «застрявший чертёж» в HUD.

**R2. Дедлок `WaitingAll` при включении `WaitForAllSiteResources = true` — реальный, не гипотетический.**
Сейчас `CheckGroupFullSupply` вызывается только из `DeliverMaterial` (`:318-321`). Если последняя неукомплектованная
клетка отменена (`CancelSite` убирает её из `_groupToCells`, `:449-461`), то `allReady` уже `true`, но **никто не
вызовет проверку снова** → остальные клетки навсегда в `WaitingAll` (этап 3 недостижим, работы нет).
Обязательно применить P5e вместе с P5d, иначе сборка зависнет на объектах, где игрок отменил одну клетку.

**R3. «Нет ресурсов» — не дедлок, а ожидание (по спеке этап 2 ждёт).**
`BlueprintDeliveryJobHandler` при отсутствии брёвен ставит `MarkJobUnreachable(2000)` и снимает клейм (`:126-139`),
сама работа не удаляется; `ReconcileTick:636-639` пересоздаёт её, если пропала. Таймаута «чертёж висит N минут» в
игре нет и после патча не появится — это осознанно, но UI должен показывать нехватку (например, `0/15`).
Отдельно: при `MarkJobUnreachable` работа не «умирает» — проверено, что удаления нет, значит бесконечного
пересоздания job-мусора тоже не будет.

**R4. Отмена/снос не должны терять материалы.**
Возврат уже реализован: `UnregisterSiteJobsAndRefund` (`:526-558`) выставляет `_delivered` на землю рядом,
`CancelSite:469` вызывается и для батча (`:516`). Патч P2 (откат стадии) **не должен** сбрасывать `_delivered`,
`WorkProgress` и не должен рефандить — клетка возвращается в этап 1, ресурсы остаются «на площадке» (виртуально).
Это важно: откат ≠ отмена.

**R5. Прогресс монтажа при откате.**
Откат разрешён только при `WorkProgress <= 0` (P2). Иначе можно потерять часы работы (`BuildDurationSec = 2000 игр.сек`,
`BuildConfig.cs:38`). Если продукт захочет откатывать и «в процессе» — списывать прогресс явно и логировать.

**R6. Производительность `ReconcileTick`.**
Сейчас `ReconcileTick` — O(sites) каждые 5 с wall-clock (`:588-591`), и уже берёт 3–4 блокировки на клетку в ветке
`Clearance`. P2 добавляет `HasItemsAt` (lock `Ground`) **на каждую** клетку, включая `Assembling`. При массовой
застройке (тест `TestAdversarialConstruction` создаёт 250 000 площадок) это дорого.
Обязательно ограничить: слайс ≤256 клеток за проход + курсор (тот же приём, что `JobValidator._jobCursor`, `:67-86`),
плюс ранний выход по `dirty`. Профиль: `Simulation.PipelineReconcile` (`AgentSimulationThread.cs:543-546`) не должен
выйти за ~0.2 мс.

**R7. Гонки при смене стадии.**
`ReconcileTick` читает `snapshot` без `_lock` и уже сейчас мутирует поля `site` вне блокировки (`:615-632`).
P2 пишет под `_lock` и проверяет `ReferenceEquals`, но `RegisterBlueprint/UnregisterBlueprint` вызываются вне
блокировки (как и в `CheckAndAdvanceObstacles:282-285`) — порядок блокировок не меняем (manager → JobBroker, никогда
наоборот), иначе риск дедлока `ConstructionManager._lock` ↔ `GenericJobSpatialIndex._registerLock`.

**R8. `RegisterJob` дедуп не обновляет цель.**
`JobSpatialPyramid.RegisterJob:305-307` возвращает существующий id и **игнорирует** новый `TargetItemCount`.
Поэтому «заказывать остаток» (P5a) надо только при создании новой работы; для уже существующей цели остаётся
старой → возможен перезаказ и выливание излишка рядом (`JobBroker.cs:439-461`). Это терпимо (ресурсы не теряются),
но именно поэтому P4 делает авторитетом площадку, а не `isCompleted`.

**R9. Совместимость сейвов.**
P6 меняет вывод стадии при загрузке. Старые сейвы с `Assembling` и полным комплектом загрузятся как `Assembling`
(поведение сохранится); сейвы с `Assembling` и недостачей — как `Supplying` (объект потребует догрузку). Формат
payload не меняется — миграция не нужна.

**R10. Ожидаемый побочный эффект.**
После P1/P4 бригады перестанут «докидывать» брёвна на уже полные клетки (сейчас это лишние рейсы и spill на землю).
Скорость застройки многоклеточных объектов слегка упадёт (ожидание полного комплекта при
`WaitForAllSiteResources = true`) — это цена строгой иерархии, а не регресс.

---

## 5. КАК ПРОВЕРИТЬ

### 5.1. Инварианты, которые должны стать машинно проверяемыми

| ID | Инвариант | Где проверять |
|---|---|---|
| I1 | `Stage != Clearance` ⇒ в клетке нет дерева/камня/предметов **в мире** | `ReconcileTick` + тест |
| I2 | `Stage == Assembling` ⇒ `IsFullySupplied` | `ConstructionJobHandler.Can` (P3) + тест |
| I3 | Для объекта из N клеток: любая клетка в `Assembling` ⇒ **все** клетки группы `IsFullySupplied` (при `WaitForAllSiteResources = true`) | `CheckGroupFullSupply` + тест |
| I4 | Нет `BlueprintDelivery`-работы на клетке, чья стадия ≠ `Supplying` | `ReconcileTick`, `JobValidator` |
| I5 | Нет `Construction`-работы на клетке, чья стадия ≠ `Assembling` или не полон комплект | `JobBroker.RegisterConstructionAt` (assert-лог) |

После патча в `src/` поле `Stage` должно присваиваться **только** в: `ConstructionSite.cs:22` (инициализатор),
`ConstructionManager.cs` (T1c / T2 / T2b / T3 + откат P2) и `IslandStateReplay.cs` (через вывод из факта, P6).
Проверка: `grep -rn "\.Stage =" src/`.

### 5.2. Автотест (без запуска игры и без `dotnet build`)

В проекте уже есть готовый носитель: `tests/TestAdversarialConstruction.cs` (класс `RefCounted`, метод `Run()`),
запускается через `tests/run_adversarial_construction_test.gd` (SceneTree tool-скрипт, `:4-30`).
Там же есть образец «строгой иерархии» для фермы — `TestFarmStrictHierarchy` (`:362-399`). По его образцу добавить
`TestConstructionStrictHierarchy()` и вызвать из `Run()` (`:19-45`):

```csharp
// 1) Частичная выгрузка НЕ закрывает этап 1.
GroundItemManager.Instance.SpawnItems(80, 80, ItemId.Log, 100);
ConstructionManager.Instance.StartSitesBatch(new List<(int,int)>{(80,80)}, StructureRegistry.WoodWall, mapData);
// имитируем один рейс haul-агента: TakeItemsWithType(80,80,29)
GroundItemManager.Instance.TakeItemsWithType(80, 80, 29);
if (ConstructionManager.Instance.TryGetSite(80, 80, out var s1) && s1.Stage != ConstructionStage.Clearance)
    throw new Exception("I1: этап 1 закрылся при 71 предмете на клетке!");

// 2) Монтаж невозможен без полного комплекта.
var consJob = new JobData { TargetX = 80, TargetY = 80 };
var cons = new Game.Simulation.Jobs.ConstructionJobHandler();
if (cons.CanAgentExecute(0, consJob, null, ctx)) throw new Exception("I2: монтаж без комплекта!");

// 3) Группа: 3 клетки, укомплектована одна — Assembling запрещён всем.
// 4) Отмена неукомплектованной клетки не вешает остальные в WaitingAll.
// 5) Демоция: положить Grain на клетку в Supplying → после ReconcileTick стадия Clearance.
```

Прогон (делает исполнитель, не этот аудит):
`godot_run_headless --script tests/run_adversarial_construction_test.gd --quit-after N` (или из редактора).

### 5.3. Ручной сценарный чек-лист (в игре)

| # | Сценарий | Ожидаемо |
|---|---|---|
| S1 | Стена на клетке с деревом | chop → **только после** пустой клетки → delivery → assembling |
| S2 | Куча 100 брёвен на клетке + чертёж | haul 29 → стадия **не** меняется; 0 → `Supplying` |
| S3 | Чертёж, в мире 0 брёвен | вечно `Supplying`, Construction-job не берётся, нет `Assembling` |
| S4 | Стена-прямоугольник 3 клетки, снабжена 1 | при `WaitForAll = true` монтаж не стартует нигде |
| S5 | Отмена 1 клетки из S4 | остальные достраиваются, материалы отменённой — на земле |
| S6 | Сейв/лоад в середине | стадия = факт (комплект + чистота), работа продолжается |
| S7 | Зерно на клетке в `Supplying` | ≤5 с → откат в `Clearance`, delivery снят, haul создан |
| S8 | Доставка на `Assembling`-клетку | `BlueprintDelivery.Can == false`, агент не едет |

### 5.4. Диагностика в рантайме

Временные `GD.Print($"[STAGE] ({x},{y}) {old}->{new} reason={why}")` в 5 точках (T1b, T1c, T2, T2b и откат P2) —
по логу видно, кто именно «двигает» стадию. Профиль `Simulation.PipelineReconcile` — в `PerformanceOverlay`
(проверить, что P2 не выбивается из бюджета). `grep -rn "\.Stage =" src/` — контроль инвариантов после правок.

### 5.5. Замечание по инструменту

`dependency_graph_visualizer\app.exe --cli --deps ...` на этом проекте **падает** на любой файл:
`[CLI Error]: sequence item 0: expected str instance, dict found` (воспроизведено для
`src/simulation/building/ConstructionPipeline.cs` и `src/simulation/structures/ConstructionSite.cs`, форматы
`--format compact|json`, прямой и обратный слэш, из корня воркспейса и из папки проекта; `--help` работает).
Поэтому граф зависимостей собран вручную (`grep` + чтение); все цитаты — с реальными файлами и строками.
Тем же инструментом без `--deps` (`--deep-dive`, `--hotpaths`, `--query`) пользоваться можно.

---

## Приложение A. Файлы, изученные при аудите

```
src/simulation/building/ConstructionPipeline.cs          (адаптер, 65 стр.)
src/simulation/building/BlueprintManager.cs              (адаптер, 133 стр.)
src/simulation/building/BuildConfig.cs                   (все цифры стройки)
src/simulation/building/BuildObstacles.cs                (список препятствий)
src/simulation/building/TileClearanceService.cs          (маски препятствий)
src/simulation/structures/ConstructionManager.cs         (авторитет пайплайна, 650 стр.)
src/simulation/structures/ConstructionSite.cs            (состояние клетки)
src/simulation/structures/StructureDefinition.cs / StructureRegistry.cs
src/simulation/jobs/handlers/ConstructionJobHandler.cs
src/simulation/jobs/handlers/BlueprintDeliveryJobHandler.cs
src/simulation/jobs/handlers/StockpileHaulingJobHandler.cs
src/simulation/jobs/handlers/TreeChoppingJobHandler.cs / MiningJobHandler.cs
src/simulation/jobs/JobBroker.cs / JobDispatcher.cs / JobValidator.cs / JobRegistry.cs
src/simulation/jobs/JobSpatialPyramid.cs (claim + progress) / GenericJobSpatialIndex.cs
src/simulation/items/GroundItemManager.cs / StockpileManager.cs / DemandRegistry.cs / ResourceBroker.cs
src/simulation/trees/TreeJobManager.cs / stones/StoneJobManager.cs
src/simulation/agents/WorkerZoneJobSystem.cs / zones/ZoneManager.cs
src/simulation/AgentSimulationThread.cs (вызов ReconcileTick)
src/core/jobs/JobData.cs / JobTypeId.cs / JobCategory.cs
src/core/world/BuildingType.cs / MapData.cs
src/core/world/mutation/IslandStateReplay.cs / IslandPayload.cs
src/ui/tools/BuildTool.cs / ZoneDraftTool.cs
tests/TestAdversarialConstruction.cs / tests/run_adversarial_construction_test.gd
```

## Приложение B. Быстрый diff-план (только условия переходов)

```
StockpileHaulingJobHandler.cs   :184-193   P1  NotifyHaulCleared только при пустой клетке
ConstructionManager.cs          :604-613   P2  перепроверка мира для всех стадий + откат
ConstructionManager.cs          :298-325   P4  переход 2->3 только из Supplying и на чистой клетке
ConstructionManager.cs          :265-286   P5b групповой барьер этапа 1
ConstructionManager.cs          :207-218   P5c барьер вместо немедленной доставки
ConstructionManager.cs          :440-524   P5e перепроверка группы после отмены
ConstructionManager.cs          :641-647   P5e ветка WaitingAll в ReconcileTick
ConstructionJobHandler.cs       :27-36     P3  гейты HasObstacles + IsFullySupplied
ConstructionJobHandler.cs       :110       P3  гейт на финише
JobBroker.cs                    :462-474   P4  авторитет площадки вместо счётчика индекса
BuildConfig.cs                  :60        P5d WaitForAllSiteResources = true
IslandStateReplay.cs            :121-136   P6  стадия из факта
```





