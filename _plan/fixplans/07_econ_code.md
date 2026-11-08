# 07 · План минимальных правок — зоны / стройка / фермы / предметы (код)

Волна 2026-11-07. Источник багов: `_reports/hunt311/econ.md` (E2-*), `_plan/BUGS_REGISTRY.md` §16 (C2-*), §17 (W2-08).
Режим: **только план**, код read-only, git/Godot/`dotnet build` не трогались. Все правки — по прочитанному коду с `файл:строка`.

Обозначения: **Объём** S ≤10 строк · M 10–50 · L >50/несколько файлов. **Перестройка** «нет» либо «ТРЕБУЕТ РЕШЕНИЯ ВЛАДЕЛЬЦА» + варианты.
Совместимость сейвов: если карточка её касается — оговорено явно (`.zones.json`, island delta payload, `WorldChunkStore`).

---

### E2-03 · P1 · Осиротевшая pending-зона склада и утечка группы при отбраковке кольца стен

- **Файлы:** `src/simulation/structures/ConstructionManager.cs:244-266, 359-364, 552-559, 578-581`; вызывающий — `src/ui/tools/ZoneDraftTool.cs:918-922, 935-936`.
- **Минимальная правка:** в `StartSitesBatch` **после** выхода из `lock` (и до регистрации работ) добавить откат для случая «не создано ни одной площадки»:

```csharp
// было (ConstructionManager.cs:346-365, сразу после lock):
// Регистрация работ доставки для клеток, готовых сразу (без помех)
var readySupply = new List<(int X, int Y)>();
foreach (var s in addedSites) { if (s.Stage == ConstructionStage.Supplying) readySupply.Add((s.X, s.Y)); }
if (readySupply.Count > 0) JobBroker.Instance.RegisterBlueprintBatch(readySupply, def.Type, def.TotalRequiredItems);
if (addedSites.Count > 0) Callable.From(() => OnSitesBatchAdded?.Invoke(addedSites)).CallDeferred();
return groupId;

// стало:
if (addedSites.Count == 0)
{
    // Ни одна клетка кольца не прошла гейты (занято/не трава/руда):
    // pending-зону склада НЕ оставляем «вечной», группу не копим.
    List<(int X, int Y)> orphan = null;
    lock (_lock)
    {
        _groupToCells.Remove(groupId);
        _groupToPendingWarehouse.Remove(groupId, out orphan);
    }
    if (orphan != null && orphan.Count > 0)
        StockpileManager.Instance.RemoveZoneTilesBatch(orphan);   // вне lock: свой лок
    Callable.From(() => OnSitesBatchAdded?.Invoke(addedSites)).CallDeferred(); // пусто = сигнал UI
    return 0;   // «группа не создана»
}
var readySupply = new List<(int X, int Y)>();
foreach (var s in addedSites) { if (s.Stage == ConstructionStage.Supplying) readySupply.Add((s.X, s.Y)); }
if (readySupply.Count > 0) JobBroker.Instance.RegisterBlueprintBatch(readySupply, def.Type, def.TotalRequiredItems);
Callable.From(() => OnSitesBatchAdded?.Invoke(addedSites)).CallDeferred();
return groupId;
```

  `return 0` безопасен для единственного «клеточного» вызова `StartSite` (`:228-238`): там `gid > 0` уже проверяется.
  Факультативно (та же правка, 1 строка): `RemoveZoneTilesBatch` уже снимает `_pendingZones` и бит зоны, поэтому отдельного API в `StockpileManager` не нужно.
- **Что НЕ трогаем:** `ZoneDraftTool.Commit` (порядок «pending → StartSitesBatch») не меняем — правка целиком в приёмнике, UI не переучиваем. Политику «все клетки кольца обязаны пройти» не смягчаем (это к U2-07/GD-02, отдельные карточки).
- **Риск:** `OnSitesBatchAdded` с пустым списком могут ловить рендереры/тесты — проверить потребителей (`grep OnSitesBatchAdded`); при них — не звать событие, а только `GD.PushWarning("[Build] кольцо не поставлено: все клетки заняты")` (одна строка). Откат простой: убрать ветку `addedSites.Count == 0`.
- **Проверка:** юнит-стенд (по образцу `tests/TestIslandDemolish.cs`): `StockpileManager.AddPendingZoneTilesBatch(pending)` → `StartSitesBatch(cells, WoodWall, mapData, pending)` где все `cells` уже заняты (`StructureManager.AddStructure` заранее) → ожидаемо `return 0`, `StockpileManager.IsZoneTile(pending[0]) == false`, `ConstructionManager.GetAllSites().Count == 0`. В игре: нарисовать склад со стенами вплотную к готовым стенам — фиолетовой зоны-призрака не остаётся, в логе предупреждение.
- **Объём:** M (~20–25 строк).
- **Перестройка:** нет.

---

### E2-04 · P2 · `CompleteConstruction` игнорирует провал `AddStructure` («построено» без постройки)

- **Файлы:** `src/simulation/structures/ConstructionManager.cs:531-585` (ключевая строка — `:575`); `src/simulation/structures/StructureManager.cs:134-150`.
- **Минимальная правка:** превратить `:575` в проверку и не «завершать» сайт при отказе:

```csharp
// было (ConstructionManager.cs:570-584):
FootprintRegistry.ReleaseOrigin(x, y);
StructureManager.Instance.AddStructure(x, y, completedDef, ctx, pool);
if (pendingWarehouseToActivate != null && pendingWarehouseToActivate.Count > 0)
    StockpileManager.Instance.ActivatePendingZone(pendingWarehouseToActivate);
Callable.From(() => OnSiteCompleted?.Invoke(site)).CallDeferred();
return true;

// стало:
FootprintRegistry.ReleaseOrigin(x, y);
if (!StructureManager.Instance.AddStructure(x, y, completedDef, ctx, pool))
{
    // Габарит уже занят (структура есть/чужой захват): НЕ выдаём «готово».
    // Сайт снимаем честно — с возвратом привезённых материалов на землю.
    GD.PushError($"[Build] ({x},{y}) {completedDef.Id}: AddStructure отказал, материалы возвращены");
    UnregisterSiteJobsAndRefund(x, y, site);
    OnProgressStageChanged?.Invoke((x, y), -1);
    Callable.From(() => OnSiteRemoved?.Invoke((x, y))).CallDeferred();
    return false;
}
if (pendingWarehouseToActivate != null && pendingWarehouseToActivate.Count > 0)
    StockpileManager.Instance.ActivatePendingZone(pendingWarehouseToActivate);
Callable.From(() => OnSiteCompleted?.Invoke(site)).CallDeferred();
return true;
```

  Важно: `site` уже удалён из `_sites` и `_cellToGroup` в первом `lock` (`:547-561`), поэтому откат не должен пытаться вернуть его в `_sites` — достаточно вернуть материалы (`UnregisterSiteJobsAndRefund` уже умеет это, `:685-717`) и снять спрайт прогресса.
  Для `ReconcileTick` (`:765-772`) отдельная правка **не нужна**: ветка «структура уже есть» должна вызывать `RemoveStructure`-эквивалент, но минимальный вариант — при `false` сайт просто снимается с возвратом (нынешний путь) — это и есть корректное поведение; сообщение в лог обязательно.
- **Что НЕ трогаем:** `StructureManager.AddStructure` (его контракты и `FootprintRegistry.TryOccupy`) не меняем. `ReconcileTick` не переписываем. Возврат материалов — существующим `UnregisterSiteJobsAndRefund`, новый код возврата не заводим.
- **Риск:** `OnSiteRemoved` вместо `OnSiteCompleted` может изменить поведение UI-маркеров (они и так снимаются по `-1`); риск локален. Откат: `git checkout -- src/simulation/structures/ConstructionManager.cs` (после применения — только `git restore` в ветке правок, не в аудите).
- **Проверка:** юнит: поставить `StructureManager.AddStructure(x,y,WorkTable)` напрямую → `StartSitesBatch` на занятой клетке не создаст сайт; второй сценарий — создать сайт, затем «в обход» занять габарит и вызвать `CompleteConstruction` → ожидаемо `false`, `OnSiteRemoved`, материалы на земле (`GroundItemManager.GetItemCountAt` рядом > 0). Проверить, что счастливый путь (`true`) не затронут: `tests/TestAdversarialConstruction.cs`.
- **Объём:** S (~12 строк).
- **Перестройка:** нет.

---

### E2-05 · P2 · `SolidWalls`/выселение/снос только по Origin + `blocksMovement:false` у станков — **ТРЕБУЕТ РЕШЕНИЯ ВЛАДЕЛЬЦА** (данные vs код)

- **Файлы:** `src/simulation/structures/StructureManager.cs:162-167` (Add), `:215-220` (Remove), `:275-281` (ClearAll); данные — `src/simulation/structures/StructureRegistry.cs:109-211` (все станки `blocksMovement: false`), `:32-41` (`WoodWall` — единственная `true`, 1×1).
- **Минимальная правка — вариант А (только код, не меняя поведения игры сегодня):** непроходимость/выселение/снос по всему габариту, но строго в тех же условиях (`def.BlocksMovement`).

```csharp
// было (StructureManager.cs:162-181):
if (def.BlocksMovement && ctx != null)
{
    if ((uint)x < (uint)ctx.MapWidth && (uint)y < (uint)ctx.MapHeight)
        ctx.SolidWalls[x, y] = true;
    FlowFieldManager.Instance?.ClearCache();
    HierarchicalPathfinder.Instance?.Invalidate();
    if (ctx.SpatialGrid != null && ctx.Movement != null && pool != null)
    {
        int insideAgent = ctx.SpatialGrid.GetFirstAgent(x, y);
        while (insideAgent != -1) { ctx.Movement.EjectFromWall(insideAgent, x, y, pool, ctx); insideAgent = ctx.SpatialGrid.GetNextAgent(insideAgent, pool); }
    }
}

// стало (общий хелпер в том же файле, ~10 строк):
private static void ForEachFootprintCell(int x, int y, StructureDefinition def, Action<int, int> action)
{
    int w = Math.Max(1, def.Width), h = Math.Max(1, def.Height);
    for (int oy = 0; oy < h; oy++)
        for (int ox = 0; ox < w; ox++)
            action(x + ox, y + oy);
}

if (def.BlocksMovement && ctx != null)
{
    ForEachFootprintCell(x, y, def, (cx, cy) =>
    {
        if ((uint)cx < (uint)ctx.MapWidth && (uint)cy < (uint)ctx.MapHeight)
            ctx.SolidWalls[cx, cy] = true;
    });
    FlowFieldManager.Instance?.ClearCache();
    HierarchicalPathfinder.Instance?.Invalidate();
    if (ctx.SpatialGrid != null && ctx.Movement != null && pool != null)
    {
        ForEachFootprintCell(x, y, def, (cx, cy) =>
        {
            int insideAgent = ctx.SpatialGrid.GetFirstAgent(cx, cy);
            while (insideAgent != -1) { ctx.Movement.EjectFromWall(insideAgent, cx, cy, pool, ctx); insideAgent = ctx.SpatialGrid.GetNextAgent(insideAgent, pool); }
        });
    }
}
```
  Симметрично — `RemoveStructure` (`:215-220`, `= false`) и `ClearAll` (`:275-281`, `= false`).
  Оговорка по габариту: `def.Width/Height` совпадают с тем, что занимает `FootprintRegistry.TryOccupy` (`ConstructionManager.OccupySiteFootprint:223-226` передаёт ровно `def.Width/Height`) ⇒ новый цикл попадёт в те же клетки, что и занятость.

- **Вариант Б (данные, если владелец считает станки препятствием):** `blocksMovement: true` у 8 станков (`StructureRegistry.cs:116, 129, 142, 155, 168, 181, 194, 207`) — **без варианта А это создаст новый баг** «непроходима 1 клетка из 12». Поэтому Б применяется только вместе с А, иначе не применять вовсе.
- **Что НЕ трогаем:** `FootprintRegistry` (занятость уже по габариту), `JobValidator`/`SolidWalls`-потребители, навигационные кэши — их инвалидация уже есть в тех же ветках.
- **Риск:** А меняет только латентный путь (сегодня ни одна мультитайловая дефиниция не `BlocksMovement`), поэтому поведение игры не меняется; производительность — +W×H итераций на постройку (≤12), шум. Б меняет геймплей (станки непроходимы) — только по решению владельца.
- **Проверка:** юнит с тестовой дефиницией 2×2 `blocksMovement:true`: `AddStructure` → `ctx.SolidWalls` по всем 4 клеткам `true`; `RemoveStructure` → все `false`; `ClearAll` → все `false`. Регресс: `tests/TestAdversarialConstruction.cs`, `tests/TestStockpileLocks.cs`.
- **Объём:** А — M (~25 строк + хелпер), Б — S (8 строк данных).
- **Перестройка:** **ТРЕБУЕТ РЕШЕНИЯ ВЛАДЕЛЬЦА.** Вопрос: «Производственные станки (3×4 и др.) должны быть препятствием для агентов или проходимы?» Варианты: (1) **оставить проходимыми** (`blocksMovement:false` как сейчас) — агенты ходят к станку с любой стороны, коллизий нет, максимум: применить только А для будущих стен/ворот; (2) **сделать препятствием** — реалистично, но нужны точки подхода (`FindStandPosition` уже ищет соседей) и проверка, что работа на станке не встанет из-за окружения; (3) **компромисс**: непроходим только центр габарита, кромка проходима — больше кода, чем А. Рекомендация: (1)+А, вопрос — п.4 §18 реестра.

---

### E2-06 · P2 · `StartSitesBatch`: кучи предметов проверяются только по Origin (габарит 3×4)

- **Файлы:** `src/simulation/structures/ConstructionManager.cs:299-305` (вычисление `NeedHaul`), `:187-220` (`CanOccupyFootprint` — предметы не проверяет), `:317-319` (`itemsToHaul`).
- **Минимальная правка:** считать `hasItems`/`itemsToHaul` по всему габариту, а не по origin:

```csharp
// было (ConstructionManager.cs:299-305):
bool hasTree = treeMask != null && (uint)pos.X < (uint)treeMask.GetLength(0) && (uint)pos.Y < (uint)treeMask.GetLength(1) && treeMask[pos.X, pos.Y];
bool hasStone = stoneMask != null && (uint)pos.X < (uint)stoneMask.GetLength(0) && (uint)pos.Y < (uint)stoneMask.GetLength(1) && stoneMask[pos.X, pos.Y];
bool hasItems = GroundItemManager.Instance.HasItemsAt(pos.X, pos.Y);
site.NeedTree = hasTree;
site.NeedStone = hasStone;
site.NeedHaul = hasItems;

// стало (предметы — по всем клеткам габарита def.Width×def.Height):
bool hasItems = false;
int fw = Math.Max(1, def.Width), fh = Math.Max(1, def.Height);
for (int oy = 0; oy < fh; oy++)
    for (int ox = 0; ox < fw; ox++)
    {
        var cell = (X: pos.X + ox, Y: pos.Y + oy);
        if (!GroundItemManager.Instance.HasItemsAt(cell.X, cell.Y)) continue;
        hasItems = true;
        itemsToHaul.Add(cell);     // вынести КАЖДУЮ кучу габарита
    }
site.NeedTree = hasTree;
site.NeedStone = hasStone;
site.NeedHaul = hasItems;
```
  Ниже (`:317-319`) уже есть `if (hasItems) itemsToHaul.Add(pos);` — при новой схеме эта строка удаляется (иначе origin попадёт дважды; `RegisterHaulBatch`/`MarkTreesBatch` батчи не дедуплицируют сами).
  Важно: `hasTree/hasStone` по-прежнему только по origin — деревья/камни на габарите уже отбраковываются `CanOccupyFootprint` (`:208`, `multiTile && (TreeOnGrass || StoneOnGrass)`), это B-03 (закрыт).
- **Что НЕ трогаем:** `CanOccupyFootprint` (не начинаем запрещать стройку из-за куч — этап `Clearance` для этого и существует); `TreeJobManager`/`StoneJobManager`; политику `TrySpillItemsAdjacent` при полном складе (`:331-343`).
- **Риск:** `NeedHaul` теперь может включиться на мультитайле, где раньше сайт сразу шёл в `Supplying` — сайт чаще попадает в `Clearance` (это корректно, но `ReconcileTick` (`:774-793`) обязан успевать пересчитывать: он считает `hasItems` по origin (`:778`) → **поправить и там** одной строкой (цикл по габариту), иначе сайт «залипнет» в `Clearance` с `NeedHaul=true`, который никто не снимет.
- **Проверка:** юнит: 3×4-дефиниция, `SpawnItems(x+1,y,Log,5)`, `StartSitesBatch` → ожидаемо `site.NeedHaul == true` и создана haul-работа на `(x+1,y)`; затем `TrySpillItemsAdjacent`/сбор → `Stage == Supplying`. В игре: поставить лесопилку на клетки с брёвнами — брёвна выносятся.
- **Объём:** M (~15 строк в двух местах).
- **Перестройка:** нет.

---

### E2-07 · P1 · Разбиение Farm-зоны теряет культуру и семя (лён/хлопок → пшеница)

- **Файлы:** `src/simulation/zones/ZoneManager.cs:313-331` (ветка сплита), `src/simulation/zones/Zone.cs:119-122` (дефолты), образец правильного переноса — `ZoneManager.cs:206-213`.
- **Минимальная правка:** перенести `Crop` и `RequiredSeedItem` в создаваемые зоны:

```csharp
// было (ZoneManager.cs:323-326):
var newZone = new Zone(newId, $"Ферма #{newId}", ZoneKind.Farm, comp)
{
    AutoPlantEnabled = wasAuto
};

// стало:
var newZone = new Zone(newId, $"Ферма #{newId}", ZoneKind.Farm, comp)
{
    AutoPlantEnabled = wasAuto,
    Crop = zone.Crop,                          // культура исходной зоны
    RequiredSeedItem = zone.RequiredSeedItem    // семя = урожай культуры
};
```
  `zone` в этой ветке снят с `_zones` (`:317`), но переменная жива — читать поля безопасно (внутри `lock (_lock)`, `:269-335`).
  Тот же дефект у `CreateFarmZones` (`:120-126`): зона создаётся без культуры (дефолт `Wheat`) — **это ожидаемо для нового черновика** (культуру ставит UI через `SetCrop`), не трогаем, чтобы не менять поведение драга.
- **Что НЕ трогаем:** `FarmZoneManagerFacade` (там `Crop` уже копируется в `Adapt`, `:130`); `ZonePersistence` (пишет `(int)fz.Crop`, `:104` — после правки будет писать верное значение); `SetAutoPlant`/посадку.
- **Риск:** минимальный; единственное — если игрок ожидает «новая зона = пшеница», это меняется на «та же культура», что и требовалось. Откат — две строки.
- **Проверка:** юнит: `CreateFarmZones` полоса 1×6 → `SetCrop(id, FlaxFiber)` → `RemoveTiles(средняя клетка)` → две зоны, у обеих `Crop == FlaxFiber`, `RequiredSeedItem == FlaxFiber.HarvestItem`. В игре: засеять лён, удалить полосу, открыть окно фермы — культура льна.
- **Объём:** S (2 строки).
- **Перестройка:** нет. **Сейвы:** формат не меняется; `.zones.json` начнёт писать правильный `crop` (раньше писался `1` = Wheat) — данные не теряются, лечится при следующем сохранении.

---

### E2-08 · P2 · `ResourceBroker.NotifyResourceAvailable` глушит переноску новых куч на склад

- **Файлы:** `src/simulation/items/GroundItemManager.cs:280-295`; `src/simulation/items/ResourceBroker.cs:19-34`; `src/simulation/items/DemandRegistry.cs:117-141`.
- **Минимальная правка:** не использовать факт «есть заявка на ТИП» как «куча забрана» — регистрировать переноску всегда, а брокер оставить только как «пнули диспетчер»:

```csharp
// было (GroundItemManager.cs:281-295):
bool matchedDemand = ResourceBroker.Instance.NotifyResourceAvailable(id, placedX, placedY, count);
if (!matchedDemand && hasFreeStockpileSpace)
{
    JobBroker.Instance.RegisterStockpileHaul(placedX, placedY);
}

// стало:
ResourceBroker.Instance.NotifyResourceAvailable(id, placedX, placedY, count); // пуш-пробуждение
if (hasFreeStockpileSpace)
{
    JobBroker.Instance.RegisterStockpileHaul(placedX, placedY);
}
```
  `RegisterStockpileHaul` идемпотентен по клетке (регистрирует haul на клетку, дедуп в индексе работ) — дублей не будет.
- **Что НЕ трогаем:** `ResourceBroker` (его поведение очистки кулдауна `BlueprintDelivery` и `TriggerImmediateDispatch` полезно и остаётся); `DemandRegistry` (мёртвое поле `ReservedCount` — отдельная гигиена, не в этой правке).
- **Риск:** больше haul-работ на карте — только при наличии свободного склада (`hasFreeStockpileSpace` уже в условии), поэтому «шторм работ» не возникает; ранее подавленные кучи теперь доедут до склада. Откат — вернуть гейт.
- **Проверка:** юнит: зарегистрировать `DemandRegistry.RegisterDemand(Blueprint, далеко, далеко, Log, 100)` → `SpawnItems(x,y,Log,20)` → ожидаемо существует haul-работа на `(x,y)` (`JobDispatcher.JobIndex.HasJobAt(x,y,JobTypeId.StockpileHauling)`).
- **Объём:** S (~3 строки).
- **Перестройка:** нет.

---

### C2-04 · P1 · `ActiveAreaRegistry`: `ActiveZone` остаётся на вытесненной зоне при повторной регистрации того же id

- **Файлы:** `src/core/world/activearea/ActiveAreaRegistry.cs:114-150`.
- **Минимальная правка:** снимать `ActiveZone`, если он указывает на запись, которую мы заменяем, и проверять результат `MakeRoomLocked`:

```csharp
// было (:119-142):
var remove = new List<string>();
foreach (ZoneRecord z in _zones.Values)
{
    if (z.Id == id) continue;                 // ← своя запись пропускается, её ActiveZone не снимается
    ...
    if (z.Area != null && ReferenceEquals(ActiveZone, z.Area)) ActiveZone = null;
    remove.Add(z.Id);
}
for (int i = 0; i < remove.Count; i++) _zones.Remove(remove[i]);
MakeRoomLocked(id);                            // ← результат игнорируется
_zones[id] = new ZoneRecord { ... };

// стало:
if (_zones.TryGetValue(id, out var previous) && previous.Area != null
    && ReferenceEquals(ActiveZone, previous.Area))
{
    ActiveZone = null;                        // замена «своей» зоны тоже снимает активную ссылку
}
var remove = new List<string>();
foreach (ZoneRecord z in _zones.Values)
{
    if (z.Id == id) continue;
    ...
}
for (int i = 0; i < remove.Count; i++) _zones.Remove(remove[i]);
if (!MakeRoomLocked(id)) return false;        // не смогли освободить место — не подменяем запись
_zones[id] = new ZoneRecord { ... };
```
- **Что НЕ трогаем:** `ClearInactive` (`:162-174`) — он корректно сохраняет активную; `RegisterZone` (`:60-98`); `ActiveArea`-жизненный цикл и освобождение 15 МБ.
- **Риск:** `return false` при неудаче `MakeRoomLocked` — вызывающий уже умеет обрабатывать `false` (как в `RegisterZone:87-88`); проверить вызывающих `RegisterOrReplaceZone` (Main при выборе региона) → отказ должен логироваться. Откат — 2 строки.
- **Проверка:** юнит: `RegisterOrReplaceZone("A", 0,0)` → `TryGetZone("A")`/`ActiveZone` установлен → `RegisterOrReplaceZone("A", 512,512)` → `ActiveZone == null` (раньше указывал на старую зону). Плюс: `ReferenceEquals(ActiveZone, TryGetZone("A"))` после повторного `TryGetZone`.
- **Объём:** S (~6 строк).
- **Перестройка:** нет.

---

### C2-06 · P2 · `HumidityMap.RebuildWaterNear` аллоцирует 4 МБ LOH на каждый вызов

- **Файлы:** `src/core/world/HumidityMap.cs:169-180`; вызов — `src/core/world/activearea/MapDataShim.cs:111`.
- **Минимальная правка:** вынести BFS-очередь в переиспользуемое поле (размер `W*H` — один раз на карту):

```csharp
// было (HumidityMap.cs:178-180):
// Кольцевой буфер очереди BFS (кодируем x,y в один int).
int[] queue = new int[w * h];
int head = 0, tail = 0;

// стало:
// поле класса рядом с _waterFeed:
private int[] _bfsQueue = System.Array.Empty<int>();
...
if (_bfsQueue.Length < w * h) _bfsQueue = new int[w * h];   // один раз на размер карты
int[] queue = _bfsQueue;
int head = 0, tail = 0;
```
  `Array.Clear(_waterFeed, 0, _waterFeed.Length)` (`:176`) оставляем — он нужен для сброса прошлого результата (1 МБ, не LOH).
- **Что НЕ трогаем:** сам BFS, `_waterFeed` (тип/семантику), `DigCanal`/`WaterFeedAt`, `RebuildWaterNear` в `ActiveArea.Fill`.
- **Риск:** поле живёт столько же, сколько `HumidityMap` (карта) → +4 МБ постоянной памяти вместо 4 МБ мусора на вызов. При острове 1024² это выгодно (вызов на каждое изменение рельефа/зон); если карт много одновременно — учитывать. Откат — вернуть локальную переменную.
- **Проверка:** `[арифметика]`: `GC.GetAllocatedBytesForCurrentThread()` до/после 10 вызовов `RebuildWaterNear` → ожидаемо ~40 МБ меньше; в игре — фризы при терраформинге.
- **Объём:** S (~5 строк).
- **Перестройка:** нет.

---

### C2-07 · P2 · Порядок домен-реплея недетерминирован (обход `Dictionary`), «план + готовое» разрешается по-разному

- **Файлы:** `src/core/world/mutation/IslandDomainReplay.cs:121-130` (сборка групп), `:130` (`foreach` по словарю), `:75-92` (`Classify`), `src/core/world/streaming/integration/WorldDeltaKind.cs:10-79`.
- **Минимальная правка:** явный канонический порядок: **сначала готовое состояние, потом планы** (внутри принимающих менеджеров гейты идемпотентны, поэтому «готовое» перекрывает «план»):

```csharp
// было (IslandDomainReplay.cs:130):
foreach (KeyValuePair<byte, List<(long X, long Y)>> group in groups)
{

// стало — фиксированный порядок (ready → plan), а не порядок вставки в Dictionary:
private static readonly byte[] ReplayOrder =
{
    WorldDeltaKind.Wall, WorldDeltaKind.Building, WorldDeltaKind.Stockpile, WorldDeltaKind.Crop,
    WorldDeltaKind.WallPlan, WorldDeltaKind.BuildingPlan,
    WorldDeltaKind.StockpilePlan, WorldDeltaKind.CropPlan,
    WorldDeltaKind.CutTreePlan, WorldDeltaKind.MinedStonePlan,
};
...
for (int oi = 0; oi < ReplayOrder.Length; oi++)
{
    if (!groups.TryGetValue(ReplayOrder[oi], out var list)) continue;
    byte kind = ReplayOrder[oi];
    ... // тело прежнего цикла
}
```
  Почему нельзя «по возрастанию byte»: `Wall = 0x77`, а планы — `0x11..0x16` (`WorldDeltaKind.cs:10,64-79`), т.е. числовой порядок поставил бы `WallPlan` раньше `Wall`. Готовое (`Building 0x01`, `Stockpile 0x04`, `Crop 0x05`) оказалось бы раньше планов случайно — на это нельзя опираться.
- **Что НЕ трогаем:** `Classify`, домены-приёмники (их идемпотентность), `IslandDeltaApplicator` (фильтр/локальные координаты).
- **Риск:** порядок — наблюдаемое поведение: на клетке, где исторически лежали и `Wall`, и `WallPlan`, теперь гарантированно побеждает готовая стена. Это и есть цель; но если в чьём-то сейве есть «план поверх готового», визуально план пропадёт — логируется не будет (можно добавить счётчик в `(Applied, Skipped)`).
- **Проверка:** юнит: подать `ReplayAll` дважды с разным порядком входа `cells` (готовая стена + план на одной клетке) → результат одинаков: `StructureManager.HasStructureAt == true`, `ConstructionManager.IsSiteAt == false`.
- **Объём:** S (~10 строк).
- **Перестройка:** нет. **Сейвы:** формат дельт не меняется.

---

### C2-10 · P2 · Метки, которых нет в базе, никогда не снимаются — стор растёт

- **Файлы:** `src/core/world/mutation/IslandDeltaApplicator.cs:87-119` (применение `CutTree`/`MinedStone`), API стора — `src/core/world/mutation/IslandStateSnapshot.cs:142` (`store.RemoveBatch`).
- **Минимальная правка:** собрать «мёртвые» метки и удалить их из стора после прохода:

```csharp
// было (IslandDeltaApplicator.cs:99-103):
if (area.TreeOnGrass[idx])
{
    area.TreeOnGrass[idx] = false;
    applied++;
}
break;

// стало:
if (area.TreeOnGrass[idx])
{
    area.TreeOnGrass[idx] = false;
    applied++;
}
else
{
    // Метка есть, дерева в базе нет (уже срублено/база другая) — метка мусорная.
    (stale ??= new List<(byte, long, long)>()).Add((kind, wx, wy));
}
break;
```
  симметрично для `MinedStone` (`:106-119`); после цикла:
```csharp
if (stale != null && stale.Count > 0)
    store.RemoveBatch(stale);   // тот же API, что в IslandStateSnapshot.cs:142
```
  (`stale` объявить рядом с `domainCells`, `:85`.)
- **Что НЕ трогаем:** `IslandOverrideStore` (формат/кодек); `CutTreePlan`/`MinedStonePlan` (это метки работ, не факты рубки); `GroundItems`-кейс (`:120-127` — он намеренно no-op).
- **Риск:** удаление меток из стора — операция записи в хранилище при загрузке. Если база когда-нибудь начнёт генерироваться иначе (другой сид/версия), «мёртвая» метка может оказаться валидной для нового мира — но seed-гейт (`IslandOverrideStore` сверяет сид) не даёт грузить чужой стор. Откат — убрать `RemoveBatch`.
- **Проверка:** стенд: стор с меткой `CutTree` на клетке, где дерева нет → `Apply` → `store.Count(WorldDeltaKind.CutTree)` уменьшился; повторный `Apply` — `applied == 0`, стор не растёт. Замер: размер `<zoneId>.island.delta` стабилен между сессиями.
- **Объём:** M (~15 строк).
- **Перестройка:** нет. **Сейвы:** payload не меняется, только удаляются недостижимые метки.

---

### W2-08 · P2 · `OreDepositGenerator` берёт canvas-origin из мутабельной статики (два чтения, не снимок)

- **Файлы:** `src/core/world/generation/OreDepositGenerator.cs:41-44`; вызывающий — `src/core/world/activearea/MapDataShim.cs:125-126`.
- **Минимальная правка:** снимок origin в локальные переменные до вычитания (минимум), либо передать origin параметром (правильно):

```csharp
// было (OreDepositGenerator.cs:41-44):
long canvasOriginX = originAbsX - UnifiedWorldGenerator.WorldOriginX;
long canvasOriginY = originAbsY - UnifiedWorldGenerator.WorldOriginY;
return OreWorldField.FillMapData(data, worldSeed, generatorVersion, canvasOriginX, canvasOriginY);

// стало (минимум — пара читается один раз):
long worldOriginX = UnifiedWorldGenerator.WorldOriginX;
long worldOriginY = UnifiedWorldGenerator.WorldOriginY;
long canvasOriginX = originAbsX - worldOriginX;
long canvasOriginY = originAbsY - worldOriginY;
return OreWorldField.FillMapData(data, worldSeed, generatorVersion, canvasOriginX, canvasOriginY);
```
  Правильный вариант (если у вызывающего origin уже есть): добавить в сигнатуру `long worldOriginX, long worldOriginY` и передавать из `MapDataShim` — тогда мутации статики вообще не участвуют.
- **Что НЕ трогаем:** `OreWorldField.FillMapData` (снимок origin внутри `ActiveArea.Fill` уже сделан — класс S-05 закрыт для террейна); `UnifiedWorldGenerator.SetWorldOrigin`; `GenerateColonyWindow` (старая сигнатура сохранена ради совместимости).
- **Риск:** снимок не устраняет гонку «origin сменили между снимком и FillMapData» (стати единственная точка — `SetWorldOrigin` на главном потоке, генерация — тоже; риск теоретический). Правильный вариант с параметром устраняет полностью, но трогает 2 файла + вызовы.
- **Проверка:** стенд: `SetWorldOrigin(A)` → старт `Generate` → `SetWorldOrigin(B)` из другого потока → проверить, что все клетки руды смещены на один и тот же canvas-origin (нет «шва» по половине окна). Статически — порядок чтений.
- **Объём:** S (минимум, 2 строки) / M (параметром).
- **Перестройка:** нет. **Сейвы:** не касается (мутации руды — чанк-стор по абсолютному ключу).

---

## Волны применения

**Волна 1 — безопасные локальные правки (можно параллельно, 1 пакет = 1 коммит):**
`E2-07` (2 строки, зоны/фермы), `E2-08` (3 строки, предметы), `C2-04` (6 строк, реестр зон), `C2-06` (5 строк, LOH), `C2-07` (10 строк, детерминизм), `W2-08` (2 строки, руда). Все — S, геймплей не меняют (кроме E2-07: культура сохраняется вместо подмены), сборка/тесты не ломают.

**Волна 2 — стройка (зависимость внутри файла):**
`E2-03` → `E2-04` (обе в `ConstructionManager`, разные методы; можно одной задачей, но проверять отдельно). Потом `E2-06` (трогает `StartSitesBatch`/`ReconcileTick` — тем же пакетом, чтобы не было конфликта правок в одном методе). Тесты: `tests/TestAdversarialConstruction.cs`, `tests/TestIslandDemolish.cs`, `tests/TestStockpileLocks.cs`.

**Волна 3 — персистентность (нужен прогон с сейвами):**
`C2-10` (удаление мусорных меток). Отдельно от волны 1, потому что пишет в стор при загрузке; проверять на копии `<zoneId>.island.delta`.

**Волна 4 — только после решения владельца:**
`E2-05` (варианты А/Б: проходимость станков). Без решения применять только А (латентная страховка), Б — никогда отдельно от А.

**Совместимость сейвов:**
- `.zones.json`: ни одна правка не меняет формат. E2-07 просто сохраняет правильное `crop` вместо пшеницы; чтение старых файлов не затронуто (`ZonePersistence.cs:196-260`).
- Островные дельты (`*.island.delta`): C2-10 меняет только набор записей (удаляет метки, которых нет в базе), кодек/payload/версия — без изменений; W2-08 — только окно генерации руды.
- `WorldChunkStore`: правки не касаются.
- Правки в `src/core/world/soil` и `SparseFarmDomain` этой волной **не планируются** (см. `_reports/systems/03_farms.md`) — там нужна отдельная задача с решением владельца.