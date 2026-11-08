# Отчёт 01 — «кривая почанковая подгрузка» + «пропадает декор»

Проект: `D:\bebebe-main (1)\bebebe-main` (исходники — `src/`, `scenes/`).
Только чтение кода, ничего не запускалось и не редактировалось (src/, scenes/ не тронуты).
Инструментарий: `dependency_graph_visualizer\app.exe --cli` (`--func PaintCell`, `--skeletons StreamingWorldView.cs`),
трассировка вручную по цепочкам вызовов.

---

## 0. Ключевые факты о рендере мира (они объясняют оба бага)

1. **Чанк = 64×64 клетки.**
   `WorldCoordinates.cs:34` (`ChunkSize = 64`) → `WorldChunk.cs:8-9` (`Side = ChunkSize`, `CellCount = 4096`).
   Окно стриминга адаптивное: 3…16 чанков в сторону (`StreamingWorldView.cs:43-44`), т.е. до 16×16 = 256 чанков
   = 1024×1024 клетки (`ChunkWindowPlanner.cs:49-50`).

2. **Декор — это НЕ MultiMesh и не отдельный слой.** Дерево/камень «запечены» в тайл общего
   композитного атласа земли `Composite 0..29` (`StreamingWorldView.cs:1434-1447`, сборка атласа
   `1502-1563`, выбор тайла `GetCompositeGroundIndex` `1679-1746`), а рисуются одним слоем
   `_groundLayer = MakeLayer("WorldGroundAndDecor", _tileSet)` (`StreamingWorldView.cs:1285`).
   Отдельный `_decorLayer` объявлен (`StreamingWorldView.cs:56`), но **не используется нигде** —
   ни один `Clear()/EraseCell()/SetCell()` его не трогает (проверено grep по всему проекту).
   → «Пропал декор» и «пропала земля» — это одно и то же событие: стирание тайла в `_groundLayer`.

3. **Пустая земля намеренно НЕ рисуется тайлом** — `PaintCell`, `StreamingWorldView.cs:1865-1874`:
   для `CompositeGrass/Water/Sand/Steppe/Glacier` вызывается `_groundLayer.EraseCell(pos)`, тайл
   ставится только для декора/гор. Базовая земля (трава/песок/вода) приходит из шейдера
   `_groundSurface` (ColorRect + `terrain_blend.gdshader`, `MapRenderer.cs:251-286`, `937-973`, `601-605`).
   → Поэтому стирание декора **не оставляет дыр**: под ним всегда «нормальная» земля шейдера.
   Именно так выглядит баг #2: местность на месте, деревья/камни исчезли.

4. **За кадр**: принимается максимум 4 готовых чанка (`MaxReadyChunksPerFrame = 4`,
   `StreamingWorldView.cs:37`, дренаж `:220-223`), на покраску — до 4 «слайсов»
   (`MaxChunkOpsPerFrame = 4`, `:46`, `:883`), но **слайс = целый чанк** (см. #1). Стирание — 2048
   клеток/кадр (`MaxCellsErasedPerFrame = 2048`, `:48`, `:1074-1122`) → чанк стирается за 2 кадра.

5. **Порядок в кадре** (`StreamingWorldView._Process`, `:214-243`):
   `DrainReady → ApplyReady()` → `ProcessExits()` (выгрузка/стирание) → `RepaintDirty()` (покраска).
   Это порядок важен для #2 (стирание идёт ПОСЛЕ применения свежего чанка в том же кадре).

---

## 1. КОРЕНЬ #1 — «местность появляется резкими прямоугольными кусками со швами»

### 1.1 Причина (file:line)

`StreamingWorldView.ProcessPaintSlices`, `src/ui/streaming/StreamingWorldView.cs:974-1024`:

```csharp
// :47
[Export(PropertyHint.Range, "128,4096,128")] public int MaxCellsPerPaintSlice = 4096;
...
// :977
int maxCells = Math.Clamp(MaxCellsPerPaintSlice, 128, WorldCoordinates.ChunkSize * WorldCoordinates.ChunkSize);
...
// :1003
int end = Math.Min(total, job.Cursor + maxCells);   // total == 4096 == ChunkSize²
```

`4096 = 64×64` — **ровно один чанк**. Поэтому `end` всегда равен `total`: «прогрессивный» курсор
`job.Cursor` (задуманный для размазывания покраски на несколько кадров, `:1003-1022`) **фактически
мёртв** — чанк красится целиком в одном кадре, а `_paintJobs.Remove(key)` (`:1016`) завершает задачу.

Дополнительно:
* Ограничитель по времени `if (sw.ElapsedMilliseconds >= 4) break;` (`:981`) проверяется **только между
  чанками**, а не внутри; то есть квант работы = чанк, и он неделим.
* За кадр таких неделимых квантов до 4 (`MaxChunkOpsPerFrame`, `:46`) ⇒ до 16384 `SetCell/EraseCell`.
* Стирание при выгрузке ещё грубее: 2048 клеток/кадр ⇒ чанк исчезает за 2 кадра (`:1090-1116`).
* **Нет прогрессивного «проявления»**: тайл ставится сразу с полной альфой; ни `Modulate`, ни ramp,
  ни per-chunk fade в шейдере `streaming_ground.gdshader` нет. Никакого визуального сглаживания края
  загруженного чанка также нет: «шов» — это граница 64×64 между полем декора и пустой землёй
  (пустая земля = отсутствие тайла, см. §0.3), т.е. прямой угол без перехода.
* Усугубляет «перезагрузку кусками» пересборка окна: `Update()` дергается ~8 раз/с
  (`Main.cs:31`, `:606-628`), внутри `ApplyAdaptiveWindow` → `_planner.SetWindowSide(side)`
  (`StreamingWorldView.cs:796-813`); `SetWindowSide` при смене стороны делает
  `_currentMinChunk = long.MinValue` (`ChunkWindowPlanner.cs:88-103`) ⇒ весь набор ключей окна
  пересчитывается как «вышло/вошло», десятки чанков уходят в `Exited` → erase → повторная загрузка.

### 1.2 Цепочка вызовов

`Main._Process` (`scenes/main/Main.cs:606-628`) → `StreamingWorldView.Update(visible)`
(`:246-274`) → `WorldChunkStreamer.Update` (`runtime/WorldChunkStreamer.cs:131-155`) →
worker `ProcessChunk` (`:444-495`) → `UnifiedWorldGenerator.GenerateChunk` (`:480`) →
`CompleteChunk` (`:522-540`) → (следующий кадр) `StreamingWorldView._Process`
(`:220-223`) → `DrainReady` (`:161-187`) → `ApplyReady` (`:649-696`, только `_textureDirty.Add`) →
`RepaintDirty` (`:881-913`) → `ScheduleChunkPaint` (`:950-966`) →
`ProcessPaintSlices` (`:974-1024`) → `PaintCell` (`:1857-1927`) × **4096 клеток за кадр**.

### 1.3 Минимальный патч (без изменения логики)

Файл `src/ui/streaming/StreamingWorldView.cs`, строка `:47`.

Было:
```csharp
[Export(PropertyHint.Range, "128,4096,128")] public int MaxCellsPerPaintSlice = 4096;
```
Стало (квант = 1/8 чанка → чанк «прорисовывается» за 8 кадров, край перестаёт быть мгновенным срезом):
```csharp
[Export(PropertyHint.Range, "128,4096,128")] public int MaxCellsPerPaintSlice = 512;
```

Этого достаточно, чтобы «прямоугольник» перестал появляться целиком (появляется полоса-«протяжка»
64×8 клеток, `ProcessPaintSlices` уже умеет продолжать по курсору). Ничего больше менять не нужно —
`Math.Clamp(..., 128, 4096)` в `:977` остаётся валидным.

Опционально (если нужен край «из центра», а не сверху вниз), одна строка в том же цикле `:1004`:
```csharp
// было: for (int index = job.Cursor; index < end; index++)
// стало: проход по полосам от строки, ближайшей к фокусу камеры
```
— это уже изменение поведения, поэтому в «минимальный» патч не включено.

---

## 2. КОРЕНЬ #2 — «после прогрузки местности исчезают деревья и камни»

Причин две, они независимы и обе дают один и тот же внешний симптом («земля есть — декора нет»,
пропадает прямоугольниками 64×64). Патч нужен для обеих.

### 2.A Стирание декора у чанка, который уже снова в окне (гонка выгрузки)

#### 2.A.1 Причина (file:line)

`StreamingWorldView.ProcessExits`, `:698-762`, цикл завершения выгрузок:

```csharp
// :742-761 — проверки IsDesired здесь НЕТ
for (int i = _unloadTasks.Count - 1; i >= 0; i--)
{
    var (k, t) = _unloadTasks[i];
    if (!t.IsCompleted) continue;
    if (t.IsFaulted) { ... }
    else
    {
        RemoveChunkTiles(k);          // :758  ← стирает тайлы ВСЕГДА
    }
    _unloadTasks.RemoveAt(i);
}
```

`RemoveChunkTiles`, `:764-789`:
```csharp
if (FitsTileMap(originX, originY) && _paintedChunks.Contains(key))
    ScheduleEraseChunk(key, ...);     // :773-779  → ProcessEraseWork :1074-1122 → _groundLayer.EraseCell ×4096
_paintedChunks.Remove(key);           // :781
_chunkData.Remove(key);               // :782  ← выбрасывает СВЕЖИЕ данные чанка
...
_textureDirty.Remove(key);            // :786  ← снимает флаг «перекрасить»
_freshPaint.Remove(key);              // :787
```

Проверка «не выгружаем, если чанк снова желаем» есть **только в момент постановки** задачи
(`:704` `if (!_streamer.IsResident(key) || _streamer.IsDesired(key)) { ... continue; }`), а на
завершении (`:742-761`) её нет — и `WorldChunkStreamer.UnloadChunkAsync` тоже проверяет `_desired`
только синхронно, до постановки в очередь (`runtime/WorldChunkStreamer.cs:289-304`), а запись на диск
выполняется позже воркером (`:497-514`).

Порядок кадра (`:220-230`) делает гонку гарантированно достижимой:

```
кадр N   : DrainReady → ApplyReady(key)   // _chunkData[key] = новый чанк, _textureDirty.Add(key)
кадр N   : ProcessExits()                 // старый unload-таск по ЭТОМУ ЖЕ key завершился
                                          //   → RemoveChunkTiles(key): erase 4096 клеток +
                                          //     _chunkData.Remove + _textureDirty.Remove
кадр N.. : декор чанка стёрт, данные выброшены, перекраска не запланирована
```

Дальше `_eraseJobs.ContainsKey(key)` блокирует и покраску (`:899-900`, `:1131`), а запись в
`_textureDirty` уже снята → чанк визуально остаётся пустым до следующей полной перезагрузки
(стример ещё раз сгенерирует его и `ApplyReady` покрасит — но камера к тому времени обычно уже
уехала, а при движении гонка повторяется). Триггер гонки — любой churn окна: панорама
(8 клеток/120 мс, `Main.cs:611-612`), смена стороны окна (`ApplyAdaptiveWindow` → `SetWindowSide`,
`ChunkWindowPlanner.cs:88-103`), телепорт камеры.

Почему виден именно «исчезнувший декор», а не дыра: пустая земля не рисуется тайлом (§0.3), под
стёртым тайлом всегда остаётся базовая земля шейдера `_groundSurface`.

#### 2.A.2 Цепочка вызовов
`Update` (Exited) → `_exitQueue` (`:259-266`) → `ProcessExits` → `_streamer.UnloadChunkAsync`
(`:721`, worker `ProcessUnload` `WorldChunkStreamer.cs:497-514`, `CompleteUnload` `:589-610`) →
следующий кадр: `StreamingWorldView._Process` → `ProcessExits` (`:742-761`) → `RemoveChunkTiles`
(`:764-789`) → `ScheduleEraseChunk` (`:1026-1032`) → `ProcessEraseWork` (`:1074-1122`) →
`_groundLayer.EraseCell` (`:1095`).

#### 2.A.3 Минимальный патч

`src/ui/streaming/StreamingWorldView.cs`, `RemoveChunkTiles` (`:764`): одна защита в начале метода
(покрывает всех вызывающих).

Было:
```csharp
private void RemoveChunkTiles(ChunkKey key)
{
    MarkTerrainForUnload(key);
```
Стало:
```csharp
private void RemoveChunkTiles(ChunkKey key)
{
    // Гонка выгрузки: unload стартовал, пока чанк был вне окна, а к моменту завершения
    // камеру уже вернуло — ApplyReady в этом же кадре положил СВЕЖИЙ чанк и его dirty-флаг.
    // Стирать тайлы и выбрасывать данные такого чанка нельзя.
    if (_streamer.IsDesired(key) && _chunkData.ContainsKey(key))
        return;

    MarkTerrainForUnload(key);
```
(никакой другой логики не меняет: для действительно выгружаемых чанков `_streamer.IsDesired(key)`
== false, поведение прежнее).

### 2.B Декор стриминга физически перекрыт непрозрачным ColorRect базовой земли (z-order)

#### 2.B.1 Причина (file:line)

* `scenes/main/Main.cs:218-226` — стриминговый вид создаётся с `ZIndex = -1`;
* `src/ui/renderers/MapRenderer.cs:251-261` — `_groundSurface = new ColorRect { Color = black, Size = 512*64 px }`,
  `:283-285` — `_groundSurface.Material = _terrainMaterial; _groundSurface.Visible = true;`
  (непрозрачный `COLOR = final_col` в `terrain_blend.gdshader:212`);
* `MapRenderer.cs:937-973` `ApplyTerrainBuffer` позиционирует этот rect на follow-окно 512×512 клеток
  (квант 64 клетки, `:601-605`) — т.е. он всегда накрывает экран,
* `MapRenderer.cs:1126-1132` — при этом собственный тайловый слой земли острова выключен
  (`_groundLayer.Visible = false`), т.к. землю рисует шейдер.

Итог: всё, что рисует `StreamingWorldView` (декор, горы, стены), лежит под непрозрачным rect
базовой земли. Как только follow-окно террейна применено («местность прогрузилась»), декор
стриминга перестаёт быть видимым — навсегда, при этом земля (шейдер) остаётся. Ровно формулировка
игрока: «декор был виден в процессе загрузки, после прогрузки исчез».

Подтверждение фактом из репозитория: `shots/beyond_edge_1093.png` (камера сразу за кромкой острова,
клетка ~1093) — на весь экран гладкая земля шейдера, **ни одного дерева/камня и ни одного тайла
стриминга**, хотя чанки там загружены (`_chunkData`, `LoadedChunks > 0`, декор в них есть).
Это же видно в `shots/live/…`: вся картинка — шейдерная земля + декор чанков-тайлов, которые
исчезают, когда окно террейна их накрывает.

#### 2.B.2 Цепочка вызовов
`MapRenderer._Ready` (`:251-286`) создаёт и включает `_groundSurface` → `_Process` (`:575-605`)
считает `targetOrigin` и зовёт `RequestTerrainWindowUpdate` → `ApplyTerrainBuffer` (`:937-973`)
ставит `_groundSurface.Position` (окно 512×512 клеток поверх экрана).
`StreamingWorldView._Process` → `RepaintDirty` → `ProcessPaintSlices` → `PaintCell` ставит тайл
декора в `_groundLayer` (`:1873`) — но слой лежит под `_groundSurface` (z: `_worldView` = -1,
MapRenderer и его дети = 0).

#### 2.B.3 Минимальный патч

Опустить именно базовый ColorRect под стриминговый вид (не поднимая `_worldView` выше агентов/острова!):

Файл `src/ui/renderers/MapRenderer.cs`, инициализатор `_groundSurface` (`:251-261`).

Было:
```csharp
_groundSurface = new ColorRect
{
    Name = "GroundSurface",
    Color = new Color(0f, 0f, 0f),
    Size = new Vector2(TerrainWindowTiles * TileSizePx, TerrainWindowTiles * TileSizePx),
    Position = Vector2.Zero,
    MouseFilter = Control.MouseFilterEnum.Ignore
};
```
Стало:
```csharp
_groundSurface = new ColorRect
{
    Name = "GroundSurface",
    Color = new Color(0f, 0f, 0f),
    Size = new Vector2(TerrainWindowTiles * TileSizePx, TerrainWindowTiles * TileSizePx),
    Position = Vector2.Zero,
    MouseFilter = Control.MouseFilterEnum.Ignore,
    // Базовая земля шейдера должна лежать ПОД стриминговым видом (StreamingWorldView.ZIndex = -1),
    // иначе непрозрачный rect навсегда перекрывает декор/горы/стены стриминга.
    ZIndex = -2
};
```
Проверить, что декор появился, а агенты/предметы/стены острова остались сверху (они в MapRenderer и в
рендерерах Main с z ≥ 0).

> Альтернатива, которая НЕ рекомендуется без доп. проверки: `Main.cs:223` `ZIndex = -1` → `ZIndex = 1`:
> тогда декор стриминга окажется выше `AgentRenderer`/`GroundItemRenderer` (дети Main с z = 0) и
> будет перекрывать агентов, стоящих за кромкой острова.

---

## 3. Отвергнутые гипотезы

1. **«Деревья/камни рисуются в MultiMesh, кто-то ставит `instance_count = 0`».**
   Отвергнуто: в стриминге декора-MultiMesh нет вообще. MultiMesh в проекте — только агенты
   (`AgentRenderer`), тени (`ShadowCasterRenderer`), предметы/урожай/склад
   (`GroundItemRenderer`, `ItemShadowRenderer`, `StockpileItemRenderer`, `CropRenderer`),
   обозначения (`DesignationRenderer`). Декор мира — тайлы `_groundLayer` (§0.2).
2. **«Декор берётся из `GeneratedCell` при генерации, но не восстанавливается при загрузке чанка
   из storage/кэша/дельт».**
   Отвергнуто: `WorldChunkStreamer.ProcessChunk` **всегда** заново генерирует чанк
   (`runtime/WorldChunkStreamer.cs:480`, `UnifiedWorldGenerator.GenerateChunk`); snapshot из стора
   несёт только дельты меток (стены/срублено/добыто), декор в него не пишется
   (`storage/WorldChunkCodec.cs`, `storage/WorldChunkDeltaCodec.cs`). Декор детерминирован:
   `GeneratedCell` + `IslandDecorPass` (`StreamingWorldView.cs:1579-1601`) + `CellSoilFertilityQ16`
   (`:1636-1650`), при любом повторном входе он вычисляется идентично.
3. **«Кэш `_fertilityGrid`/`_fertilityChunk` даёт разный декор в соседних чанках».**
   Отвергнуто по корректности: ключ кэша сверяется с текущим чанком и пересобирается при
   несовпадении (`:1639-1640`, `:1653-1669`) — результат детерминирован. Это только перф-замечание
   (кэш на 1 чанк пинг-понгует при чередовании `PaintCell`/`RepaintCell` из разных чанков).
4. **«`IslandDecorPass` неверно режет декор за кромкой (512→1024)».**
   Отвергнуто: стриминг вообще не рисует область острова (`IsOccludedByColony`, `:958`, `:968-972`;
   `PaintCell` `:1860-1861`), а кромка 1024 кратна 64, т.е. совпадает с сеткой чанков — «половинных»
   чанков нет.
5. **«Слой `_decorLayer` перестраивается/чистится (`Clear`/`RebuildAll`)».**
   Отвергнуто: `_decorLayer` (`:56`) — мёртвое поле, ему нигде ничего не присваивается;
   `TerrainTileLayer.RebuildAll` вызывается только для гор/стен/грядок (`MapRenderer.cs:1210`,
   `:1314`), `Clear()` — только из `ResetWorld` (`:168-180`), т.е. при смене мира/региона.
6. **«`ProcessEraseWork` стирает соседние/чужие чанки (переполнение координат)».**
   Отвергнуто: индекс курсора переводится в абсолютную клетку от собственного origin ключа
   (`:1088-1094`), задания ключуются по `ChunkKey` (`_eraseJobs`, `:1026-1032`) — кросс-чанковых
   записей нет. Реальная проблема erase — не адресация, а 2.A (кого стираем) и квант 4096/2048.
7. **«`ResetWorld` вызывается после загрузки и чистит слои».**
   Отвергнуто для описанного сценария: `ResetWorld` вызывается только из путей выбора региона/смены
   сида (`Main.cs:396`, `:472`), а не по завершении подгрузки. (Но учитывать стоит: если игрок меняет
   регион, глядя на мир, все слои вида чистятся `:168-180` — это отдельный, штатный эффект.)

---

## 4. Как проверить (в игре, без правок кода)

Все проверки — через `godot_command eval` / `get_property` (игровой процесс должен быть запущен).

**#1 (квант покраски = чанк):**
```gdscript
# 1) сколько чанков реально «в работе» и сколько тайлов уже стоит
var v = get_node("/root/Main/StreamingWorldView")
print(v.LoadedChunks, " loaded")
print(v._groundLayer.GetUsedCells().Size(), " tiles")
# 2) на ходу сравнить поведение: 4096 (по умолчанию) vs 512
v.MaxCellsPerPaintSlice = 512
```
Ожидание: при 4096 новый чанк декора появляется мгновенно целым квадратом 64×64; при 512 —
полосой (8 кадров), «шовный» прямоугольник исчезает. Число `tiles` при этом то же.

**#2.A (стирание чанка, который уже вернулся):**
Добавить временный лог (только чтение поведения, не правка логики) в `RemoveChunkTiles`:
условие `_streamer.IsDesired(key) && _chunkData.ContainsKey(key)` → печатать `key`.
Дёргать камеру туда-обратно через границу чанка в течение ~20 с: при наличии строк «erasing desired
chunk …» баг воспроизведён. Соответствие: `StreamingWorldView.cs:758` (вызов) и `:782/:786`
(выброс данных и dirty-флага).

**#2.B (перекрытие z-order):**
```gdscript
var m = get_node("/root/Main/MapRenderer")
print(m.get_node("GroundSurface").ZIndex, " ", get_node("/root/Main/StreamingWorldView").ZIndex)
print(m.get_node("GroundSurface").Position, " ", m.get_node("GroundSurface").Size)
```
Если `GroundSurface` накрывает экран (Position/Size окна 512×512 клеток) и `ZIndex` стриминга
меньше — декор стриминга физически невидим. Прямая проверка «нарисовано, но перекрыто»: сравнить
`Count()` использованных клеток `v._groundLayer.GetUsedCells()` (в окне стриминга) с числом
деревьев/камней, которое видно на экране: `used_cells > 0`, а на экране декора нет → перекрытие.
Быстрый эксперимент: `m.get_node("GroundSurface").ZIndex = -2` (или `.Visible = false`) — декор
должен немедленно появиться.

**Быстрый контроль обоих симптомов после патчей:** панорамировать по кромке острова (1024) —
сначала должен прорисоваться декор (полосами, §1.3), затем он не должен исчезать ни через 1 с, ни
после прогрузки окна террейна (§2.A/2.B), а `beyond_edge`-район должен перестать быть пустым.

---

## 5. Сводка правок (для разработчика)

| # | Файл:строка | Правка | Закрывает |
|---|---|---|---|
| 1 | `src/ui/streaming/StreamingWorldView.cs:47` | `MaxCellsPerPaintSlice = 4096` → `512` | «квадраты» и мгновенные швы подгрузки |
| 2A | `src/ui/streaming/StreamingWorldView.cs:764-767` | guard в начале `RemoveChunkTiles` (`IsDesired && _chunkData.ContainsKey` → `return`) | декор стирается у вернувшегося чанка |
| 2B | `src/ui/renderers/MapRenderer.cs:251-261` | `ZIndex = -2` у `_groundSurface` | декор стриминга перекрыт непрозрачным rect базовой земли |
