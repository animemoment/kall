# 06 · Работы, диспетчер, агенты, поиск пути — план минимальных правок

Волна 2026-11-07 (продолжение §13–§14 `_plan/BUGS_REGISTRY.md`). Режим: код read-only, только план.
Источники: `_reports/hunt311/jobs.md` (J2-*), §13–§14 реестра, `_reports/reviews/AgentSimulationThread_review_2026-11-06.md`.
Обозначения приоритета — из реестра; «Объём» — S ≤10 строк, M 10–50, L >50/несколько файлов.

## Сводка

| № | ID | P | Правка | Объём | Волна |
|---|---|---|---|---|---|
| 1 | **J2-01 + S-08** | **P0** | Защита чанк/макро-индекса во ВСЕХ точках (`-1` → пирамида не трогает L1/L2) | M | 1 |
| 2 | B-01 + N2-05 | P1 | `BlueprintDelivery`: «есть на складе» → `CanReserveAtLeast` | S | 1 |
| 3 | N2-02 + J-25 | P1 | 60с-холд: не держать без Worker'ов; не перезапускать на `Unassign` | S–M | 1 |
| 4 | N-05 | P2 | `Start()`: инкремент поколения ДО подмены `_pool`/`_ctx` | S | 1 |
| 5 | N-04 | P2 | Пересборка масок окружения — вне ветки влаги | S | 2 |
| 6 | N2-03 + J-27 | P2 (латентный) | Бюджетный отказ A* ≠ «недостижимо» (0.05 с вместо 3–5 с) | S | 2 |
| 7 | N2-04 | P2 | Аудит меток: восстановление Harvest-работ | M | 2 |
| 8 | J2-03 | P2 | Удалить дубль `GetScaledIntervalHi` | S | 2 |
| 9 | B-02 | P2 | Расчистка при полном складе: повтор вместо тупика | S | 3 |
| 10 | J-24 | — | Симптом не подтверждён: нужен замер (правки нет) | — | — |
| 11 | J-26 | — | Плоский пул бездельников: **ТРЕБУЕТ РЕШЕНИЯ ВЛАДЕЛЬЦА** | L | — |
| 12 | J-28 | — | Мост `WorldPathfinder` в движении: **ТРЕБУЕТ РЕШЕНИЯ ВЛАДЕЛЬЦА** | L | — |

---

## 1. J2-01 + S-08 · **P0** · Единая защита индекса пирамиды

- **Файлы:** `src/simulation/jobs/JobSpatialPyramid.cs:262-270` (сам индекс), `:483-484`, `:493`, `:500-502`, `:583-584`, `:593-596`, `:687-688`, `:702-707`, `:915-916`, `:941`, `:1234-1239`, `:1252-1254`, `:1350`, `:1612`, `:1774-1775`, `:1780`; защиты ради — `src/tools/CellInspector.cs:67,181`, `src/simulation/AgentSimulationThread.cs:1005`.
- **Механизм (подтверждено):** `ChunkDim=64`, `MacroDim=8`; `GetChunkIndexStatic` = `(tileY>>4)*64 + (tileX>>4)` без проверки, `GetMacroIndex` = `(tileY>>7)*8 + (tileX>>7)`. При `tileY≥1024` → chunk ≥ 4096 → `IndexOutOfRangeException` уже в `RegisterJob` (`:483-484`); при `tileX<0, tileY<16` → chunk == −1; при `tileX∈[1024,1039]` — «чужой ряд» (алиасинг чанков); при мире `macroIndex ≥ 64` → выход за `_macroJobCounts` / сдвиг `1UL << 65` (= бит 1). Комментарий `:264-266` («JobValidator отбрасывает ≥ MapWidth») устарел: J-06 разрешил за-кромочные цели (`JobValidator.cs:422-429`), вход — `OreMiningTool.cs:108 → OreJobManager.MarkOreWorld (:332-344) → JobBroker.RegisterOreMine (:252-275) → RegisterJob`.
- **Минимальная правка (было → стало):**
  ```csharp
  // было (JobSpatialPyramid.cs:262-270)
  public static int GetChunkIndexStatic(int tileX, int tileY)
  { int cx = tileX >> ChunkShift; int cy = tileY >> ChunkShift; return cy * ChunkDim + cx; }

  // стало
  public const int CoverageTiles = ChunkDim << ChunkShift;   // 1024 — без нового «магического» числа
  public static bool TryGetChunkIndex(int tileX, int tileY, out int chunkIndex)
  {
      chunkIndex = -1;
      if ((uint)tileX >= (uint)CoverageTiles || (uint)tileY >= (uint)CoverageTiles) return false;
      chunkIndex = ((tileY >> ChunkShift) * ChunkDim) + (tileX >> ChunkShift);
      return true;
  }
  public static int GetChunkIndexStatic(int tileX, int tileY)
      => TryGetChunkIndex(tileX, tileY, out int ci) ? ci : -1;
  ```
  и **одинаковый паттерн во всех точках**: `int ci = GetChunkIndexStatic(...); if (ci < 0) return/skip;`
  - `RegisterJob` / `RegisterJobLocked`: при `ci < 0` — НЕ вставлять в `_chunkJobs`/`_chunkCount`/`_chunkUnclaimedCount` и НЕ трогать `_macroJobCounts`/`_macroActiveMask` (SoA + `_posMap` регистрируются как обычно: дедуп и аудит продолжают работать).
  - `RemoveJobCore:687`, `ReleaseWorkerClaim:915`, `CdCountingTryAdd:1234`, `CdCountingTryRemove:1252`, `TryClaimJobById:1774`, `MacroAdd/MacroRemove` — при `ci < 0`/`mi` вне `[0,64)` пропускать только инкремент/декремент чанка и макро (SoA-часть выполняется).
  - `TryClaimJobForAgent:1350` (чанк агента) — `if (ci < 0) return false;` (агенты всегда на острове, но защита бесплатна).
  - `CellInspector.cs:67-71` — у него уже есть свой `PyramidCoverageTiles = ChunkDim << ChunkShift` и признак `outsidePyramid`; минимально заменить `GetChunkIndexStatic` на `TryGetChunkIndex` и при `false` выводить «мир, вне пирамиды» (иначе `chunkCx = -1 % 64` уедет в диагностический текст). `CellInspector.cs:181` и `AgentSimulationThread.cs:1005` читают `GetChunkJobCount`, который сам отсекает `chunkIndex < 0` (`:845-849`) — им правка не нужна.
- **Что НЕ трогаем:** не заводим мировую пирамиду работ и не расширяем `_chunkJobs` до мировых размеров — это отдельное архитектурное решение (см. карточку 12/J-28 и «Перестройка»). `JobValidator.IsTargetWalkable`, `OreJobManager`, `JobBroker` не меняются.
- **Риск:** за-кромочные работы становятся формально зарегистрированными, но невидимыми для claim-скана (поведение «нет исполнителя», как сегодня, но без краша). Риск регресса для островных работ — нулевой: для `0 ≤ x,y < 1024` функция возвращает то же значение. Откат — одна функция.
- **Проверка:** новый стенд `tests/TestJobSpatialBounds`: `RegisterJob` с целями `(1024,0)`, `(0,1024)`, `(-1,5)` → нет исключения, `TotalCount==3`, `UnclaimedCount==3`, `GetChunkJobCount(0..4095)` без изменений; затем `RemoveJobByPos` по каждой — счётчики возвращаются в 0, `PendingFreeCount==0`. В игре — пометить руду за нижней кромкой (`OreMiningTool`), работы нет, но кадр не падает и метка не теряется.
- **Объём:** M.
- **Перестройка:** нет (полный паритет «мир = остров» — вопрос владельца, см. J-28).
- **Дубль:** закрывает **S-08** (тот же корень, «достижимость не доказана» → доказана J2-01). J-05 — исторический предок (кламп ≥512), не дубль.

---

## 2. B-01 + N2-05 · P1 · Доставка на чертёж должна проверять резервируемость

- **Файлы:** `src/simulation/jobs/handlers/BlueprintDeliveryJobHandler.cs:44-47`; образец — `src/simulation/production/ProductionManager.cs:229`.
- **Было → стало:** `if (!carriesItem && !GroundItemManager.Instance.HasAvailableItemsOfType(item) && StockpileManager.Instance.GetLiveItemCount(item) <= 0) return false;`
  → заменить `GetLiveItemCount(item) <= 0` на `!StockpileManager.Instance.CanReserveAtLeast(item, 1)`; при этом ветку земли оставить как есть (`HasAvailableItemsOfType` уже учитывает доступное без резервов).
- **Механизм:** `GetLiveItemCount` (`StockpileManager.cs:83`) — сырой тотал без вычета `ReservedOutgoing` и без гранулярности слота; правильный критерий уже реализован в `CanReserveAtLeast` (`:647`, сравнение `entry.Count - entry.ReservedOutgoing >= count`, `:663`) и используется `ProductionManager.HasInputsAvailable` (`:229`). Иначе агент берёт доставку, `OnStart` не может зарезервировать → отказ/кулдаун → шторм `FailStart` (симптом B-01/S-B1).
- **Что НЕ трогаем:** `StockpileManager`, `GroundItemManager`, `ProductionJobHandler` — только предикат `CanAgentExecute`.
- **Риск:** квант `1` вместо «сколько нужно»: агент с грузом проверяет `carriesItem` до этого условия, поэтому ложных отказов «несу, но склад пуст» не будет. Если в будущем у доставки станет `TargetItemCount > 1` на рейс — заменить `1` на `Math.Min(remaining, MaxCarryWeight)`.
- **Проверка:** стенд: склад 10 брёвен, 10 зарезервированы другой работой → `CanAgentExecute(BlueprintDelivery)` = false (сейчас true). В игре: два чертежа рядом, брёвен ровно на один — второй не должен дёргать агентов в цикле отказов.
- **Объём:** S (1 строка + комментарий).
- **Перестройка:** нет. **Дубль:** N2-05 — то же место; закрываются одной правкой.

---

## 3. N2-02 + J-25 · P1 · 60-секундный холд работы в зоне

- **Файлы:** `src/simulation/zones/ZoneJobRouter.cs:129` (`WorkerHoldMs`), `:302-306` (`IsHeldForWorkerFresh`), `:321-330` (`Unassign`, `:328` `SetQueuedAt`).
- **Механизм:** холд не зависит от того, есть ли в зоне назначенные Worker'ы (`_assigned`, `:194`, хранит только `jobId → (zoneId, job)`, счётчика «сколько Worker'ов в зоне» нет); каждый `Unassign` (нужда/сторож/`TryClaimJobById`-кулдаун) перезаписывает `queuedAt`, поэтому цикл «взял → вернул» держит работу в зоне бесконечно, а Laborer её не получает.
- **Минимальная правка (две независимые половины, каждая S):**
  1. `Unassign` — не перезапускать холд: передавать в `SetQueuedAt` исходный штамп, если он ещё «свежий»:
     ```csharp
     // было (:328): SetQueuedAt(jobId, now);
     // стало: сохранить первое поступление
     if (!TryGetQueuedAt(jobId, out long firstMs) || now - firstMs > WorkerHoldMs)
         SetQueuedAt(jobId, now);
     ```
     (т.е. холд не продлевается возвратами; после истечения 60с — новый отсчёт).
  2. `IsHeldForWorkerFresh` — при `assignedCount(zoneId) == 0` не держать вовсе. Счётчик: добавить `ConcurrentDictionary<int,int> _assignedPerZone` (инкремент в `DequeueNearest` при `_assigned[bestId] = …` `:286`, декремент в `Unassign` `:324` и в местах выдачи), либо — ещё дешевле — считать «есть ли Worker в зоне» через `ZoneIndustryRegistry` (готовый `DesiredWorkers/assigned`) и передавать признак в `IsHeldForWorkerFresh(jobId, zoneHasWorkers)` из `JobDispatcher`.
- **Что НЕ трогаем:** `DequeueNearest` (алгоритм выбора ближайшей), `Sweep`, покрытие зон, `WorkerHoldMs` как константу.
- **Риск:** половинка (1) может отдать зональную работу Laborer'у чуть раньше 60с после серии возвратов — по спеке это допустимо («после 60с простоя»), а простой считается от первого поступления. Половинка (2) требует источника `assignedCount`; если брать `ZoneIndustryRegistry`, правка затрагивает 2 файла (S→M).
- **Проверка:** `TestZoneRouterHold` (если есть — дополнить): зона без Worker'ов, `Push(job)` → `IsHeldForWorkerFresh` == false сразу; зона с Worker'ом: `Unassign` ×5 → `NowMs - GetQueuedAtMs(jobId)` не растёт. В игре: зона-отрасль без назначенных Worker'ов + 1000 Laborer'ов — время до первой задачи.
- **Объём:** S (половинка 1) + S/M (половинка 2). **Перестройка:** нет. **Дубль:** N2-02 уточняет J-25; одним пакетом.

---

## 4. N-05 · P2 · Поколение петли инкрементится до подмены состояния

- **Файлы:** `src/simulation/AgentSimulationThread.cs:278` (`_pool = new AgentDataPool`), `:283` (`_ctx = new SimulationContext`), `:372` (`int generation = ++_loopGeneration`), `:395` (условие жизни петли).
- **Было → стало:** первой строкой тела `Start()` (до `if (_running …) Stop(5000)`):
  ```csharp
  public void Start(...)
  {
      int generation = ++_loopGeneration;   // ← сюда, до всего
      try { … }
  ```
  вторую строку `int generation = ++_loopGeneration;` (`:372`) заменить на использование уже вычисленной переменной.
- **Механизм:** между `:278` и `:372` старая петля не инвалидирована (`generation == _loopGeneration` ещё истинно), при таймауте `Stop(5000)` она продолжает итерации и читает уже подменённые `_pool`/`_ctx` другого размера → `IndexOutOfRangeException` в параллельной фазе, поток умирает без сообщения.
- **Что НЕ трогаем:** `Stop()`, `_running`, `Volatile`-обёртки `_loopGeneration` (поле уже `static volatile int`, `:92`), порядок создания пула.
- **Риск:** минимальный — локальная переменная уже есть в петле; при неудачном `Stop` старая петля увидит `generation != _loopGeneration` на ближайшей проверке и выйдет.
- **Проверка:** статически — порядок строк; рантайм — искусственно замедлить шаг (лог в начале итерации), дёрнуть смену региона: в логе две петли не должны пересечься, счётчик активных петель == 1.
- **Объём:** S. **Перестройка:** нет. **Дубль:** A-1 ревью (без карточки), N-05 завёл с точными строками.

---

## 5. N-04 · P2 · Пересборка масок окружения вне ветки влаги

- **Файлы:** `src/simulation/AgentSimulationThread.cs:518` (`_envMaskRebuildTimer += currentStepDt`), `:526` (`if (_humidityTimer >= humidityInterval && _ctx?.Humidity != null)`), `:550-574` (проверка + пересборка + сброс таймера), `:612`.
- **Механизм:** проверка `_envMaskRebuildTimer >= EnvMaskRebuildIntervalGameSec` и вся пересборка `_reusableForestMask`/`_reusableFarmMask` вложены в ветку влаги ⇒ фактический период = тик влаги, а при `Humidity == null` пересборки нет вовсе; комментарий `:546-549` обещает период 20 игровых секунд.
- **Было → стало:** вынести блок из тела `if (_humidityTimer …)` в самостоятельный `if` перед ним, с собственными размерами:
  ```csharp
  _envMaskRebuildTimer += currentStepDt;
  if (_environmentalMasksDirty || _envMaskRebuildTimer >= EnvMaskRebuildIntervalGameSec)
  {
      var g = _ctx.Ground; int gw = g.GetLength(0), gh = g.GetLength(1);
      EnsureEnvironmentalMasks(gw, gh, g);          // ← маски по размеру карты, не влаги
      Array.Clear(_reusableForestMask, 0, _reusableForestMask.Length);
      … существующий цикл заполнения …
      FarmJobManager.Instance.FillGardenBedFlatMask(_reusableFarmMask, gw, gh);
      _environmentalMasksDirty = false; _envMaskRebuildTimer = 0f;
  }
  ```
  ветку влаги оставить только с `humidityMap.Tick(...)`; в ветке плодородия (`:612`) оставить `EnsureEnvironmentalMasks(fw, fh, ground)` без пересборки.
- **Что НЕ трогаем:** `HumidityMap.Tick`/`FertilityMap.Tick` и их интервалы, `_staticMountainMask` (см. A-3 — отдельный вопрос), сигнатуры менеджеров.
- **Риск:** маски станут длиной по размеру карты, а `HumidityMap`/`FertilityMap` могут иметь другой размер (A-2) — на живых данных они равны `MapWidth/Height`; если это не так, `EnsureEnvironmentalMasks` пересоздаст обе маски на каждом тике (дорого, но не ломает расчёт: `Tick` принимает `forestMask` и индексирует `y*Width+x`). Проверить размеры перед правкой.
- **Проверка:** `[статически]` — вложенность; `[рантайм]` — вырубить участок леса, при 1× смотреть влажность/плодородие соседних клеток в пределах 20 игровых секунд; при `_ctx.Humidity == null` (стенд) пересборка теперь происходит.
- **Объём:** S. **Перестройка:** нет. **Дубль:** уточняет закрытые 02-3/07-12; A-2/A-3 — соседние, но не дубли.

---

## 6. N2-03 + J-27 · P2 (латентный) · Бюджетный отказ A* не должен вешать кулдаун недостижимости

- **Файлы:** `src/simulation/agents/AgentMovementService.cs:213, 234-239`; флаг — `src/simulation/pathfinding/HierarchicalPathfinder.cs:181, 588-595`.
- **Механизм:** `TryFindPathScratch` возвращает `false` и в случае «недостижимо», и в случае «отложено бюджетом» (`_lastSearchDeferred = true`), но `AgentMovementService` на любой `false` ставит `PathRequestCooldown = 3.0 + rand*2.0` (`:238`), `LastSearchDeferredByBudget` не читается нигде. Сегодня не воспроизводится: `BudgetEnabled = false` (`AgentSimulationThread.cs:292`), но включение бюджета (R-09) вернёт ступор.
- **Было → стало (в `else`-ветке `:234-239`):**
  ```csharp
  else
  {
      pool.PathRequestCooldown[agentIndex] =
          HierarchicalPathfinder.Instance.LastSearchDeferredByBudget
              ? 0.05f                                   // «отложено», повторить следующим суб-степом
              : 3.0f + (float)ParallelRng.NextDouble() * 2.0f;
  }
  ```
- **Что НЕ трогаем:** сам бюджет (`TryReserveSearch`, `ConfigureBudget`) и `BudgetEnabled=false` — включать бюджет по-прежнему нельзя без замера.
- **Риск:** при включённом бюджете больше агентов будут «долбить» запрос каждые 0.05 с — это и есть цель; при выключенном бюджете ветка недостижима, поведение идентично.
- **Проверка:** стенд: `BudgetEnabled=true`, `ConfigureBudget(1)`, 100 запросов в одном потоке → кулдаун первого отказа 0.05, не 3–5.
- **Объём:** S. **Перестройка:** нет (подготовка к R-09).

---

## 7. N2-04 · P2 · Аудит меток: восстановление Harvest-работ

- **Файлы:** `src/simulation/jobs/JobValidator.cs:242-365` (`AuditMarksSlice`, 5 фаз), `:386-403` (`HasManagerMark`, комментарий `:399` «Planting/Harvest — меток нет»); источник зрелых культур — `src/simulation/farming/CropGrowthManager.cs:156-160` (`IsHarvestQueued = true`), `:385`.
- **Механизм:** `IsHarvestQueued` ставится один раз и нигде не сбрасывается; аудит пересоздаёт Farming/деревья/камень/руду/чертежи, а Harvest — нет ⇒ если задача сбора исчезнет (снятие работы, `JobValidator` снял по другой причине, пропуск OnStart), культура остаётся зрелой навсегда.
- **Минимальная правка:** в `AuditMarksSlice` добавить фазу (`_markCursor % 5` → `% 6`, новый `case`) — «зрелые культуры без работы»:
  ```csharp
  case 5:
  {
      var ready = CropGrowthManager.Instance.GetReadyForHarvest(MaxMarksPerTick); // новый аксессор: копия stage==4
      foreach (var (x, y) in ready)
          if (!index.HasJobAt(x, y, JobTypeId.Harvesting))
              JobBroker.Instance.RegisterHarvest(x, y);
      break;
  }
  ```
  Плюс сброс `IsHarvestQueued`, если работы нет: `CropGrowthManager.ResetHarvestQueued(x,y)` вызывать из аудита после перерегистрации (или из `CropGrowthManager.UpdateGrowth`, когда `stage==4 && !JobIndex.HasJobAt(...)`).
- **Что НЕ трогаем:** `FarmJobManager`, `ZoneManager.RemoveTiles` (там культура удаляется вместе с тайлами), стадии роста.
- **Риск:** нужен новый публичный аксессор в `CropGrowthManager` (снапшот под lock) — иначе гонка с sim-потоком; аудит идёт из sim-потока, поэтому достаточно `ConcurrentDictionary`-перечисления существующего стора. Возможен «churn» регистрации, если `RegisterHarvest` дедупа нет — он дедупится `HasJobAt`.
- **Проверка:** стенд: зрелая культура + `UnregisterHarvest` → через один аудит (30 с) работа снова в индексе. В игре: ферма, дождаться зрелости, дать одному рабочему прерваться нуждой, проверить, что сбор не потерян.
- **Объём:** M (JobValidator + аксессор в CropGrowthManager).
- **Перестройка:** нет.

---

## 8. J2-03 · P2 · Удалить дубль `GetScaledIntervalHi`

- **Файлы:** `src/simulation/AgentSimulationThread.cs:779-785` и `:793-799`; вызовы `:424-426`.
- **Было → стало:** тела двух функций совпадают дословно, комментарии обещают разное (sweep «усиленно»). Заменить `GetScaledIntervalHi(base, speed)` на `GetScaledInterval(base, speed)` в вызове `:426` и удалить вторую функцию вместе с комментарием; либо (если «не чаще раза в тик» для sweep действительно нужно) реализовать разницу явно. Минимально и безопасно — первое (поведение не меняется вовсе).
- **Что НЕ трогаем:** значения `IntervalScaleSpeed/Hi`, базовые интервалы, `GetScaledInterval`.
- **Риск:** нет — функции эквивалентны; правка убирает ловушку для следующего правщика.
- **Проверка:** `Select-String "GetScaledIntervalHi"` → 0 совпадений; сборка.
- **Объём:** S. **Перестройка:** нет. **Дубль:** A-9 ревью.

---

## 9. B-02 · P2 · Расчистка при полном складе: не тупик, но и не «молчание»

- **Файлы:** `src/simulation/structures/ConstructionManager.cs:774-794` (`Reconcile`-ветка `Clearance`), `:784-787` (`TrySpillItemsAdjacent` только если нет свободного места), `:789-793` (переход в `Supplying` только при `!site.HasObstacles`).
- **Статус по реестру:** P2, деградация, а не вечный тупик: выходы — `NotifyHaulCleared` из hauling-хендлера и повторный `ReconcileTick`. Симптом: стройка стоит, пока склад полон; игроку — ничего.
- **Минимальная правка (диагностическая, без изменения поведения):** один раз на сайт (по аналогии с `_deliveryRejectStreak` в `JobBroker`) писать `GD.PushWarning` вида «Расчистка застряла: склад полон, кучи не вынести (x,y)». Больше ничего не менять — логика самовосстановления уже есть.
- **Что НЕ трогаем:** `TrySpillItemsAdjacent`, `NeedHaul`, `NotifyHaulCleared`, порядок стадий.
- **Риск:** нулевой (только лог); при желании — счётчик в `SimStats` вместо лога.
- **Проверка:** забить склад под ноль, поставить чертёж на кучу → предупреждение ровно один раз; после освобождения склада стройка продолжается сама.
- **Объём:** S. **Перестройка:** нет.

---

## 10. J-24 · Симптом не подтверждён статикой — правки нет

- **Файлы:** `src/simulation/jobs/JobDispatcher.cs:72-108` (бюджет на итерацию, ротация пула), `src/simulation/jobs/JobSpatialPyramid.cs:1443-1481` (окно скана со сдвигом на длину).
- После J-01/J-02/J-03 механизм «8–15 агентов за тик» из кода не выводится; архитектура осталась worker-driven.
- **Минимальная правка:** нет. **Проверка:** замер на стенде — 1000 Idle-агентов, 2 работы, лог времени от `Register` до `Claim` по каждому агенту (p50/p99). Если p99 > 2 с — вернуться к карточке с профилем.
- **Объём:** —. **Перестройка:** нет (до замера).

---

## 11. J-26 · Плоский пул бездельников — **ТРЕБУЕТ РЕШЕНИЯ ВЛАДЕЛЬЦА**

- **Суть:** `JobDispatcher.Instance.IdleWorkers` — плоский список (исторически: поиск работы по всему пулу, до тысячи обращений на диспатч). Реестр не перепроверял, но при 10k бездельников это O(N)-обход на каждый тик диспатча.
- **Варианты:**
  1. **Оставить как есть** до появления профиля: + просто, − O(N) на диспатч, риск фризов на 10k+.
  2. **Чанк-сетка бездельников** (`UpdateWorkerChunk` уже есть, `AgentSimulationThread.cs:1313`): + O(1) выборка рядом с работой, − новая структура и синхронизация (ранее `IdleWorkerSpatialGrid` был удалён как мёртвый код — придётся вернуть).
  3. **Кольцевой курсор по пулу** (по 64–128 агентов за диспатч с ротацией): + минимум кода (S), − работа дальше от агента, возможен «голод» дальних.
- **Минимальная правка:** вариант 3 — S; но выбор влияет на баланс загрузки ⇒ нужно решение владельца.
- **Проверка:** стенд с 10k бездельников + N работ: время диспатча на тик и p99 назначения.

---

## 12. J-28 · Мост `WorldPathfinder` в движении — **ТРЕБУЕТ РЕШЕНИЯ ВЛАДЕЛЬЦА**

- **Факт:** `src/simulation/agents/AgentMovementService.cs:564-567` — `WorldPathfinder.TryFindPath` упомянут только в комментарии-контракте; вызовов во всём `src/` нет. За кромкой путь строится прямым LOS (при отсутствии воды ≤ `MaxDirectWaterTiles`), т.е. до за-кромочной цели агент идёт «по прямой», а при препятствии — как в J-28.
- **Варианты:**
  1. **Подключить мировой путь при `tx/ty` вне острова** (в `AgentMovementService.RequestHierarchicalPath`): + паритет с островом (догма Б.3), − `WorldPathfinder` придётся валидировать на кромке/отрицательных координатах и грузить чанки на сим-потоке.
  2. **Запретить за-кромочные задания** (гейт в `JobValidator.IsTargetWalkable` + инструментах): + дёшево, − откат J-06/task-8, «мир за кромкой» перестаёт быть рабочим (нарушение догм).
  3. **Оставить прямой LOS**, а за-кромочные препятствия обходить локальным скольжением: + ноль правок, − агенты упираются в горы/реки мира, watchdog снимает работу через 8–12 с (симптом «не хотят идти за кромку»).
- **Минимальная правка:** отсутствует (L, архитектура). Пока вариант 3 — задокументировать в `docs/DECISIONS.md`, что за-кромочный путь = прямой.
- **Проверка:** цепочка `grep "WorldPathfinder\." src/` → 0 вызовов (сейчас); после варианта 1 — агент за кромкой обходит гору, не упирается.

---

## Волны применения

**Волна 1 (сразу, независимы друг от друга):**
1. **J2-01 + S-08** — единственная P0-правка; делать первой (`JobSpatialPyramid` целиком read-only для остальных карточек, кроме J2-03/N2-04 по другим файлам).
2. **B-01 + N2-05** — одна строка, отдельный файл.
3. **N2-02 + J-25** — половинку (1) можно в волну 1; половинку (2) — после выбора источника `assignedCount`.
4. **N-05** — один оператор, отдельный файл (`AgentSimulationThread`).

**Волна 2 (после волны 1, тот же файл `AgentSimulationThread` — только по очереди):**
5. **N-04** (там же), затем **J2-03** (там же), затем **N2-03/J-27** (`AgentMovementService`).
6. **N2-04** (`JobValidator` + `CropGrowthManager`).

**Волна 3 (диагностика/решения):**
7. **B-02** (лог), затем решения владельца по **J-26**, **J-28**, замер по **J-24**.

**Объединения:** J2-01 + S-08 — одна правка (закрывает обе карточки); B-01 + N2-05 — одна строка; N2-02 + J-25 — один пакет в `ZoneJobRouter`.

**Конфликты файлов:** `AgentSimulationThread.cs` правят N-05, N-04, J2-03 — применять строго по одной, после каждой — сборка (`dotnet build Game.csproj`) вручную владельцем сборки. `JobSpatialPyramid.cs` — только J2-01.