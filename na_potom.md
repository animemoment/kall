# na_potom.md — разборы, идеи и заметки

Здесь хранятся черновики, разборы и идейки, которые могут пригодиться позже.

---

## 2026-11-05: Распределение работ — разбор кода (до оптимизации)

### Где распределение работает сейчас

`JobDispatcher.DispatchPendingJobs` (src/simulation/jobs/JobDispatcher.cs) →
`JobSpatialPyramid.TryClaimJobForAgent` (src/simulation/jobs/JobSpatialPyramid.cs) →
`TryClaimInChunk` → `handler.CanAgentExecute(...)` (через JobRegistry).

### 📌 Найдено проблем (в порядке важности)

#### 🔴 P1: Квант диспетчеризации 0.8 мс — агенты «висят»

```csharp
// JobDispatcher.cs:38
private const double MaxDispatchBudgetMs = 0.8;

// JobDispatcher.cs:89-93
if ((n & 63) == 0 && n > 0)
{
    double elapsedMs = (Stopwatch.GetTimestamp() - startTicks) * ticksToMs;
    if (elapsedMs >= MaxDispatchBudgetMs)
        break;  // ← диспатч обрывается, остаток idle-агентов ждёт следующего тика
}
```

**Суть:** на каждый тик собирается до 4096 idle-агентов, но если за 0.8 мс всех не распределил — диспатч **обрывается**. Курсор ротации `_dispatchScanCursor` сдвигается, следующие агенты рассматриваются на след. тике. Агенты, которым работа не подошла (`CanAgentExecute = false`), могут висеть idle **несколько тиков подряд**. Визуально: «работу распределяют очень медленно».

**Как проверить:** `PerformanceOverlay` → `Simulation.JobDispatch`. Если среднее ≈ 0.8 мс на тик при большом числе idle — это оно.

**Фикс:** поднять бюджет до ~2-3 мс (диспатч ~10-15% кадра) ИЛИ сделать квант адаптивным: разрешать прервать, только если распределено ≥80% батча.

---

#### 🔴 P2: lock на горячем пути — `StructureManager.HasStructureAt` / `GroundItemManager.HasItemsAt`

```csharp
// StructureManager.cs:39-45
public bool HasStructureAt(int x, int y)
{
    lock (_lock)
    {
        return _structures.ContainsKey((x, y));
    }
}

// GroundItemManager.cs:224-230
public bool HasItemsAt(int x, int y)
{
    lock (_lock)
    {
        return _groundItems.TryGetValue((x, y), out var entry) && entry.Count > 0;
    }
}
```

**Суть:** вызывается на КАЖДОГО кандидата:
`CanAgentExecute` → `TileClearanceService.HasBlockingObstacles` → `GetObstacles` → `StructureManager.Instance.HasStructureAt(x, y)`.

Оценка: 10k агентов × 64 кандидата/чанк × (до 48 кольцевых + 512 макро) чанков = **десятки миллионов lock'ов в тик** (хотя `ChunkMayHaveClaimable` отрезает пустые, живые чанки сканируются). **lock = блокировка другого потока = фриз симуляции**.

**Где в цепочке:** `TileClearanceService.cs:107` — `ConstructionManager.Instance.IsSiteAt(x, y) || BlueprintManager.Instance.IsBlueprintAt(x, y)` + `StructureManager.Instance.HasStructureAt` + `BuildingManager.Instance.HasBuildingAt` + `GroundItemManager.Instance.HasItemsAt`.

**Фикс:** убрать lock'и, перейти на lock-free чек (volatile/bitset) или кэш «клетка занята» в `SimulationContext` (читается без блокировок):
- `ctx.StructureMask[x,y] = true`
- `ctx.GroundItemMask[x,y] = true`
Пересчитывать раз в тик в сим-потоке (не на горячем пути).

---

#### 🟡 P3: Аллокации в горячем пути — `new JobData` и `new Dictionary`

```csharp
// JobSpatialPyramid.cs:1140 — каждый раз на кандидата
canExec = handler.CanAgentExecute(agentIndex, GetJobData(jobId), pool, ctx);

// JobData.cs: GetJobData() = new JobData { ... } — object allocation
```

10k агентов × 64 кандидата = **640k `new JobData`/тик** → GC pressure → фризы каждые N секунд.

```csharp
// ZoneJobRouter.cs:213
var seen = new Dictionary<int, (QueuedJob Job, int Order)>();
```

Каждый `DequeueNearest` (выдаёт работу Worker'у) — **new Dictionary**. При 10k Worker'ов — тысячи аллокаций/тик.

**Фикс:**
- Передать `in JobData` уже из пула (не new) — или вообще убрать передачу: хендлеры читают данные из `pool`/`ctx` по job.Id (есть методы доступа).
- В `DequeueNearest` использовать preallocated array + индекс, вместо Dictionary.

---

#### 🟡 P4: `TryClaimJobForAgent` — 3 последовательных прохода на КАЖДОГО агента

```csharp
// 1. Свой чанк
if (TryClaimInChunk(agentChunk, ...)) return true;

// 2. Кольцо радиусом 1..3 чанка (~48 проходов)
for (int r = 1; r <= 3; r++)
    for (int cy = minY; cy <= maxY; cy++)
        for (int cx = minX; cx <= maxX; cx++)
            if (TryClaimInChunk(ci, ...)) return true;

// 3. Глобальный макро-поиск (до 512 чанков!)
ulong mask = Volatile.Read(ref _macroActiveMask);
for (int mi = 0; mi < MacroCount; mi++)
    if ((mask & (1UL << macroIdx)) != 0 && _macroJobCounts[mi] > 0)
        for (int cy = 0; cy < 8; cy++)
            for (int cx = 0; cx < 8; cx++)
                if (TryClaimInChunk(ci, ...)) return true;
```

**Суть:** если на 1-м и 2-м проходе `TryClaimInChunk` **не нашла работу, но и НЕ вернула `false` рано** (а проверила всех кандидатов, отбросила их) — агент идёт в **глобальный проход**, перебирая ВСЕ макро-сектора. При массе работ в мире → O(512 чанков) на агента.

**Фикс:** в `TryClaimInChunk` возвращать не только `bool` (найдено), а `ClaimResult { Found, Worked }` — «проверял и ничего не подошло» vs «не проверил (кулдаун/пусто)». Тогда 3-й проход запускается только если **не проверено вообще**.

---

#### 🟡 P5: `FindStandPosition` — 3 цикла поиска стояния на каждую работу

```csharp
// JobBroker.cs:25-80
// 1. Кардинальные соседи (4)
// 2. Диагональные (8)
// 3. Радиус 2..3 (до 24 проверок проходимости)
```

Вызывается в `OnStart` на каждую работу (в среднем 1 раз/2 сек на агента) + в `CanAgentExecute` хендлеров. 36 проверок проходимости (`ctx.SolidWalls`, `TreeOnGrass`, `StoneOnGrass`, `Ground` — **многомерные массивы!**).

**Фикс:** кэшировать результат в `SimulationContext` (битовая карта проходимости, обновляется в сим-потоке) → `O(1)` вместо 36 чтений.

---

#### 🟡 P6: `ChunkMayHaveClaimable` проверяет кулдаун, но не `CanAgentExecute`

Говорит «сканируй», дальше 64 кандидата проверяют `CanAgentExecute`, которые отбрасываются. Частично компенсируется `MaxClaimScanPerChunk = 64`, но если чанк полон (грядки + haul) — 64 × дороги проверки.

**Фикс:** кэш «чанк готов к claim» (не обновлять каждый тик, а раз в N тиков + при мутациях).

---

#### 🟠 P7: `GetJobData()` — создание объекта (см. P3) + `GetPriority` дважды

```csharp
// JobSpatialPyramid.cs:1148-1153
int userPrio = JobPriorityManager.Instance.GetPriority(
    JobPriorityManager.Instance.GetCategory(_typeId[jobId]));
```

Два метода на кандидата. `GetPriority` читает volatile + bounds check.

**Фикс:** `GetPriorityForJobType` + кэш в цикле (переменная `userPrio = priorities[(byte)typeId]`).

---

#### 🟠 P8: `GetAffinityPenalty` читает `LastJobCategory` — OK, но 400f penalty

```csharp
// JobSpatialPyramid.cs:1056-1061
private static float GetAffinityPenalty(...)
{
    int last = pool.LastJobCategory[agentIndex];
    return JobPriorityManager.Instance.GetCategory(jobType) == (JobCategory)last ? 0f : 400f;
}
```

`distSq + 400f` — это **радиус 20 тайлов штрафа**. Если агент далеко от работы — работа игнорируется. При большом мире это может заставить агента идти НЕ туда. Влияет на качество распределения, не на скорость.

---

### 📊 Оценка влияния

| Проблема | Тип | Ожидаемый выигрыш | Сложность |
|---|---|---|---|
| P1: квант 0.8 мс | Логика | +2-3x скорость распределения | 10 мин |
| P2: lock'и в CanAgentExecute | КРИТИЧНО | +5-10x (убрать фриз) | 1-2 ч |
| P3: аллокации `new JobData`/`Dictionary` | GC | Убирает фризы | 30 мин |
| P4: 3 прохода | Логика | +1.5-2x | 1 ч |
| P5: `FindStandPosition` | Кэш | +10-20% CanAgentExecute | 30 мин |
| P6, P7, P8 | Микро | +5-10% | 1 ч |

**Приоритет:** P2 → P1 → P3 → P4.

---

### 🧪 Как тестировать

1. Запустить игру, открыть `PerformanceOverlay` (клавиша ? или кнопка).
2. Смотреть метку `Simulation.JobDispatch` — сколько мс, сколько агентов распределилось.
3. Смотреть `Simulation.Phase2` — ошибки `UpdateSingleAgent`.
4. Если диспатч ≈ 0.8 мс стабильно — это P1.
5. Если есть всплески (spikes > 10 мс) — это P3 (GC) или P2 (lock).

---

### 🔗 Связанные файлы

- `src/simulation/jobs/JobDispatcher.cs` — точка входа, квант 0.8 мс
- `src/simulation/jobs/JobSpatialPyramid.cs` — пирамида, TryClaimInChunk, GetJobData
- `src/simulation/jobs/JobBroker.cs` — FindStandPosition, регистрация работ
- `src/simulation/jobs/JobPriorityManager.cs` — GetCategory/GetPriority
- `src/simulation/building/TileClearanceService.cs` — HasBlockingObstacles (P2)
- `src/simulation/structures/StructureManager.cs` — lock (P2)
- `src/simulation/zones/ZoneJobRouter.cs` — DequeueNearest (P3)
- `src/core/jobs/JobData.cs` — объект, который new'ится

---

## Оставшиеся вопросы для владельца

1. **Сколько агентов сейчас в игре при замере?** (10k? 1k?)
2. **Что показывает PerformanceOverlay на `Simulation.JobDispatch`?**
3. **Есть ли фризы/подёргивания?** (указывает на P2/P3)
4. **Много ли работ одновременно в мире?** (влияет на P4/P6)

---
