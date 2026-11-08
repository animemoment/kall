# 05 — Клик по зонам (Farm / Work / склад): разбор и починка

Задача: клик по зоне в мире не выделяет её и не открывает окно зоны («джоб-дисплей»);
клики по деревьям/постройкам работают. Сделать клик работающим для всех трёх видов зон
минимально, без изменения игровой логики. Игру **не запускал** (инстанс на 9090 у Lead),
проверка — по коду + сборка.

Сборка: `dotnet build Game.sln -v q --nologo` → **Сборка успешно завершена. Ошибок: 0**
(6 предупреждений — уже были в `tests/TestJobRaceStress.cs`, к моим правкам не относятся).

---

## 1. Что реально было сломано (file:line)

### 1.1. Склад: его вообще нет в ZoneManager → клик гаснет молча (ГЛАВНЫЙ БАГ)

* `src/simulation/items/StockpileManager.cs:21-24` — складские клетки живут в
  `_zones` / `_pendingZones` / `_zoneBitmask` этого менеджера.
* `src/simulation/zones/ZoneManager.cs:430-453` (`FindZoneAt`, до правки) — знает только
  Farm (`_tileToZoneId`) и Work (`ZoneKind.Work` + радиус). Про склад — ничего.
* `src/ui/tools/SelectTool.cs:82-88` (до правки):

  ```csharp
  if (!_hasDragged)
      ZoneManager.Instance.SelectZoneAt(endTile.X, endTile.Y);   // единственный путь
  ```

  Итог: ЛКМ по клетке склада → `FindZoneAt` вернул `null` → `DeselectZone()` (no-op) →
  **ни выделения, ни окна, ни сообщения**. Клик «проглатывался» полностью.

### 1.2. Work-зоны в живой игре не создаются ВООБЩЕ (проверено поиском по src/)

* `WorkZoneManager.Instance.CreateZoneAt(...)` (`src/simulation/zones/WorkZoneManagerFacade.cs:97`)
  — **нет ни одного вызова** в `src/`.
* `ZoneManager.CreateWorkZoneAt(...)` (`ZoneManager.cs:276`) — вызывается только из этого
  фасада; `RestoreWorkZoneForPersistence` (`ZoneManager.cs:314`) — только из
  `ZonePersistence.TryLoadZones`, а `ZonePersistence.TryLoadZones/SaveZones`
  (`ZonePersistence.cs:42,110`) — **тоже без вызовов**.
* `src/ui/tools/ZoneDraftTool.cs:753-770` знает только `"warehouse"` (склад) и «остальное»
  (= Farm); типа «work» у черновика нет.

Вывод: клик по Work-зоне в текущей сборке проверить нечем — таких зон в мире не бывает,
пока не подключено создание/загрузка. **Сам путь выбора при этом корректен**:
`Zone.ContainsTile` для Work считает радиус (`src/simulation/zones/Zone.cs:151-162`),
`ZoneManager.FindZoneAt` перебирает Work по формуле, фасад адаптирует
`Zone → WorkZone` (`WorkZoneManagerFacade.cs:76-90`), `WorkZoneController` подписан
(`WorkZoneController.cs:69-72`) и открывает окно в `OnZoneSelected` (`:368-425`).

### 1.3. Work-зона не подсвечивается даже при выборе (вне моего скоупа записи)

* `src/ui/renderers/FarmZoneRenderer.cs:59-101, 103-124` рисует заливку/рамку по
  `zone.Tiles`, а у Work-зоны `Tiles == EmptyTiles` (`Zone.cs:28,181`) — то есть
  Work-зона не рисуется ни в обычном виде, ни при наведении/выборе.
  `FarmZoneRenderer.cs` в мой скоуп записи не входит — **не правил**, см. §5.

### 1.4. Farm: путь целый, но два «молчаливых» обрыва

Цепочка (проверена по коду, разрывов нет):
`FarmingTool.OnRelease` → `FarmZoneManager.CreateZone` (`FarmZoneManagerFacade.cs:115-118`)
→ `ZoneManager.CreateFarmZones` (`ZoneManager.cs:98-126`, заполняет `_tileToZoneId`).
Клик: `SelectTool.OnRelease` → `ZoneManager.SelectZoneAt` (`ZoneManager.cs:603-617`) →
`OnZoneSelected` → обёртка фасада (`FarmZoneManagerFacade.cs:53-74`) →
`FarmController.OnZoneSelected` (`FarmController.cs:229-252`) → `_rootPanel.Visible = true`.

Обрывы:

1. `ZoneManager.FindZoneAt` искал Farm **только** по `_tileToZoneId`; устаревшая запись
   индекса (зона удалена, тайл остался) не чистилась и навсегда ломала поиск по этой
   клетке — клик по «видимой» зоне молча гас.
2. Если `Farm.tscn` не найден, `FarmController.LoadScene` пишет только warning
   (`FarmController.cs:92-94`), а `OnZoneSelected` при `_rootPanel == null` выходит
   молча (`:231`) — снаружи это ровно «клик по зоне ничего не делает». То же самое у
   `WorkZoneController.OnZoneSelectedFacade` (`:77-81`).

### 1.5. Крестик/Escape не закрывали окно в новом режиме склада (побочно)

`WorkZoneController` esc → `WorkZoneManager.Instance.DeselectZone()` (было `:165`), а
`WorkZoneManagerFacade.DeselectZone` (`:178-182`) снимает выбор только если выбранная зона
именно Work. Для склада в `ZoneManager` не выбрано ничего → кнопка была бы no-op.
Аналогично `HUDController._UnhandledInput` (`:816-821`) на Escape.

---

## 2. Что изменил (до → после)

### 2.1. `src/ui/tools/SelectTool.cs`

Было (`:82-88`): единственный вызов `ZoneManager.SelectZoneAt`.

Стало: `OnRelease` → `SelectAt(x, y)` (`:97-140`) — явный порядок разбора клика:

1. `ZoneManager.SelectZoneAt` — Farm/Work, окно открывает их контроллер (как раньше);
   плюс `GD.Print("[Select] зона …")` для проверки в консоли и подсказка в HUD, если
   **оба** реестра окон пусты (`ZoneWindowRegistry.FarmWindow/WorkWindow == null` →
   значит HUD не собрал контроллеры; раньше это выглядело как «не работает клик»).
2. Промах по ZoneManager + `StockpileManager.Instance.IsZoneTile(x,y)` → **склад**:
   `StockpileManager.SelectTile(x,y)` + `WorkZoneController.Instance.ShowForStockpile(x,y)`
   (окно зоны в read-only режиме склада) + короткая строка в HUD.
3. Окно склада недоступно (`WorkZoneController.Instance == null`) → клик всё равно не
   теряется: выбор склада ставится, в HUD уходит строка «Склад: клетка (x, y) · клеток
   зоны N (окно склада недоступно)», в консоль — `[Select] склад …`.
4. Иначе — как раньше: `DeselectZone()` и снятие выбора склада.

`Cancel()` (`:132-140`) дополнительно снимает выбор склада (`DeselectTile`).
Драг рамки, ПКМ, клики другими инструментами не тронуты.

### 2.2. `src/simulation/items/StockpileManager.cs`

Добавлено после `IsZoneTile` (`:385`), ничего из существующей логики не менялось:

| Новое | Строки | Зачем |
|---|---|---|
| `SelectedTile` (`(int X,int Y)?`) | `:400-410` | выбранная кликом клетка склада |
| `SelectTile(x,y)` / `DeselectTile()` | `:417-436` | выделение склада (Volatile, без лишних локов) |
| `IsPendingTile(x,y)` | `:441-446` | склад ещё строится (клетка в `_pendingZones`) |
| `FreeSlotsCount` | `:449` | «свободных слотов» для окна |
| `TryGetSlotInfo(...)` | `:453-475` | предмет/кол-во/стак/резервы клетки — данные окна |

`ClearAll()` (`:926-…`) сбрасывает и выбор склада. Запись зон/предметов не менялась.

### 2.3. `src/ui/controllers/WorkZoneController.cs`

Окно `JobDisplay.tscn` переиспользовано для склада (новых сцен и нового UI не создавал):

* `_stockpileMode/_stockpileX/_stockpileY` (`:50-52`);
* `ShowForStockpile(int x, int y)` (`:369-421`): закрывает окно фермы, сбрасывает
  farm-режим, заголовок «Склад», `Tiles`-номер = `(x, y)`, `Worker`-номер = «N кл.»,
  слайдеры `Editable = false`, прячет `EnabledToggle`/`RemoveZone`/`ButtonForFarm`,
  потом `Visible = true` + `OnOpened` + `RefreshInfo`. Возвращает `false`, если сцена
  окна не загрузилась или клетка не склад → `SelectTool` уходит в HUD-фолбэк;
* `BuildStockpileInfo(x,y)` (`:424-455`): «клетка … · клеток зоны N · принимает/строится ·
  слот: Зерно 12/100 (+3 в пути) · свободных слотов X · всего: зерно/брёвна/камень»;
* `RefreshInfo` — ветка склада (`:577-594`) вместо данных Work-зоны;
* `_Process` — ветка склада (`:648-658`), обновляет инфо-строку раз в секунду;
* `Close()` (`:687-697`) сбрасывает `_stockpileMode` всегда (иначе `RefreshInfo`,
  подписанный на `OnZonesUpdated`, продолжал бы слать данные склада в скрытое окно);
* esc/крестик (`:172-188`) в режиме склада закрывает окно напрямую;
* `ShowForFarmZone` / `OnZoneSelected` / `OnFarmBackPressed` сбрасывают `_stockpileMode`
  и возвращают `Editable` приоритету (`:326`, `:463`, `:493`).

Поведение обычных Work-зон и режима «ферма → JobDisplay» не изменилось.

### 2.4. `src/simulation/zones/ZoneManager.cs` — `FindZoneAt` (`:433-479`)

Было: Farm — только `_tileToZoneId`; Work — перебор зон с фильтром `Kind != Work → continue`.

Стало (та же семантика при согласованном состоянии):

* попадание по индексу — как раньше (быстрый путь);
* **устаревшая запись индекса** (зона удалена, тайл остался) удаляется, а не просто
  игнорируется — иначе клетка навсегда переставала выбираться;
* если индекса нет — скан зон по их СОБСТВЕННЫМ данным: Farm через
  `Zone.ContainsTile` (тайлы), Work через радиус. Farm-тайл приоритетнее радиуса Work —
  ровно как раньше, когда Farm шёл первым; при равенстве — меньший id (детерминизм).

Асимптотика не выросла: скан по зонам на промахе был и раньше (для Work), добавился лишь
`HashSet.Contains` на Farm-зону внутри того же цикла. `TryGetZoneAt` сохранён (`:418`).

### 2.5. `src/ui/controllers/HUDController.cs`

* `ShowZoneHint(string, double = 4.0)` + `TickZoneHint(delta)` (`:771-…`): короткая строка
  внизу слева (`Label`, создаётся кодом, `MouseFilter = Ignore`, без сцен — как легенды
  карты), чтобы клик по объекту **не терялся молча**. Тик — из `_Process`.
* Escape (`:816-834`): окна зон закрываются явно (`FarmController.Close()` /
  `WorkZoneController.Close()` + `StockpileManager.DeselectTile()`), потому что
  `ZoneManager.DeselectZone()` в режиме склада ничего не снимает (нечего снимать).

Затронутые файлы — `git diff --stat`:

```
src/simulation/items/StockpileManager.cs |  85 ++++
src/simulation/zones/ZoneManager.cs      |  38 +-
src/ui/controllers/HUDController.cs      |  49 +++
src/ui/controllers/WorkZoneController.cs | 144 ++++++
src/ui/tools/SelectTool.cs               |  50 +-
```

Ничего вне скоупа не менял: `core/world/**`, `simulation/structures/**`,
`simulation/jobs/**`, `scenes/**`, `FarmZoneRenderer.cs` — без правок.

---

## 3. Как проверить в игре

1. **Ферма.** HUD → «Ферма» (BuildMenu → `farming`) → драг по земле → `CheckMark` (Commit).
   Затем сбросить инструмент в дефолтный (Esc / повторное нажатие кнопки) и **ЛКМ по клетке
   зоны**. Ожидаемо: открылось окно `Farm.tscn` («Ферма #N», «N кл.», «Нужно зерна…»,
   кнопка JobDisplay). В консоли рядом с `[PIM] btn=1 pressed=… tool=SelectTool` должна
   появиться строка `[Select] зона Farm #N "Ферма #N" по клетке (x,y) — окно открывает
   контроллер зоны.`
   *Если вместо окна видна строка внизу слева «Зона … выбрана, но окна зон не созданы»* —
   проблема не в клике, а в бутстрапе контроллеров HUD (`Farm.tscn`/`JobDisplay.tscn`
   не загрузились).
2. **Склад.** HUD → «Склад» (`WarehouseArea`) → драг → Commit (без стен — склад активен
   сразу; со стенами — сначала `pending`). ЛКМ по клетке склада: открывается
   `JobDisplay.tscn` в режиме «Склад» — заголовок «Склад», клетка `(x, y)`, «клеток зоны N»,
   «строится/принимает предметы», содержимое слота, свободные слоты, итоги по
   зерну/брёвнам/камню. Слайдеры Radиус/Worker/Приоритет неактивны, «Снести зону» скрыта,
   крестик и Esc закрывают окно. Внизу слева 4 с — строка
   «Склад: клетка (x, y) · клеток зоны N».
3. **Work-зона.** Из UI её по-прежнему не создать (см. §1.2) — до подключения создания
   зон этот пункт проверить нельзя. Когда зона появится, клик пойдёт тем же путём:
   `FindZoneAt` вернёт Work по радиусу → откроется обычный JobDisplay (радиус/рабочие/
   приоритет/тумблер/снос).
4. **Регресс.** Драг рамки (SelectionBox) по-прежнему только рамка — выбор зоны идёт
   строго по клику без драга (`_hasDragged` не менял); клики деревьями/камнём/стенами
   идут своими инструментами и не тронуты; ПКМ в дефолтном инструменте по-прежнему
   ничего не делает.

---

## 4. Чего не хватает для ПОЛНОЦЕННОГО окна склада

1. **Своей сцены/контроллера склада** (`Stockpile.tscn` + `StockpileController`): сейчас
   переиспользуется `JobDisplay.tscn` в read-only режиме. В настоящем окне нужны:
   список содержимого по предметам (иконка/кол-во/стак), кнопки «снять клетку склада» и
   «снести склад», фильтр принимаемых предметов, приоритет склада.
2. **Модель «склад = одна зона».** `StockpileManager` хранит все клетки склада плоско
   (`_zones`, `_storage`); понятия «этот склад» нет. Поэтому окно показывает
   `ZoneTileCount`/`FreeSlotsCount`/итоги **по всем** складам колонии, а не по тому, в
   который кликнули. Нужны группы зон (id склада при постановке) — правка модели
   `StockpileManager` + черновика `ZoneDraftTool`/`WarehouseTool`.
3. **Публичный обход клеток зоны и агрегаты по предметам**: есть только
   `GetStorageSnapshot()` (плоский `(x,y,item,count)`) и `GetTotalItemCount(item)`.
   Для окна «этот склад» нужен `IEnumerable<(x,y,item,count)>` с фильтром по зоне/bbox.
4. **Визуальное выделение склада.** `StockpileItemRenderer` рисует предметы, а не зону;
   подсветки выбранной складской клетки нет (аналогично п.1.3 для Work-зон). Нужен слой
   подсветки (новый `StockpileZoneRenderer` или расширение `FarmZoneRenderer.cs` — вне
   моего скоупа записи).
5. **Стриминговый мир (важно!).** Всё, что ставится ВНЕ острова `[0,1024)×[0,1024)`,
   уходит не в `StockpileManager`/`ZoneManager`, а в мир-метки:
   `FarmingTool.cs:78-86` / `ZoneDraftTool.cs:782-792` → `WorldPlacement.TryPlaceFarmPlot/
   TryPlaceStockpile` (`StreamingToolPlacement.cs:62-76`) → `StreamingWorldManager`
   (`WorldDeltaKind.CropPlan` / `StockpilePlan`), см. `WorldPlacementPass.cs:70-71`.
   Публичного «какая метка в этой клетке мира» нет: у `WorldGameRules` есть только
   `HasEntityBlocking` (bool без типа) и приватный `_world.HasBlock(kind,x,y)`
   (`WorldGameRules.cs:42-51`). **Значит клик по зоне/складу, поставленному в
   стриминговом мире, по-прежнему ничего не найдёт.**
   Минимальный следующий шаг (вне моего скоупа, `core/world/**`):

   ```csharp
   // WorldGameRules
   public bool TryGetZoneMarkerAt(long x, long y, out byte kind) // CropPlan/Crop/StockpilePlan/Stockpile
   ```

   и третья ветка в `SelectTool.SelectAt` (мир) — по тому же образцу, что склад.

---

## 5. Что осталось за скоупом (сознательно не трогал)

* `src/ui/renderers/FarmZoneRenderer.cs` — Work-зоны не рисуются (см. §1.3): рисовать по
  `zone.Tiles` для Work нельзя (пусто), нужен путь «центр + радиус» (`DrawArc`/`DrawCircle`
  с клампом по `ZoneManager.MapWidth/Height`).
* `src/ui/tools/ZoneDraftTool.cs` / `HUDController.StartZoneDraft` — нет типа зоны «work»,
  поэтому Work-зону нельзя создать из UI (см. §1.2). Требуется решение Lead: отдельная
  кнопка/тип черновика или восстановление `CreateZoneAt` из загрузки колонии.
* `src/simulation/zones/ZonePersistence.cs` — `SaveZones/TryLoadZones` не вызываются;
  Farm-зоны в v1 не сохраняются (комментарий `:25-26`) — после загрузки колонии ферм в
  `ZoneManager` нет (клик по их клеткам закономерно ничего не найдёт, пока не подключена
  персистентность Farm-зон).
* `src/core/world/**` — запрос метки мира (см. §4.5).
