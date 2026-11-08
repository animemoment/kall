# 03 — Рендер, Z-слои, оверлеи: план минимальных правок

> **Волна:** 2026-11-07 (fixplans) · **Агент:** @destroyer (`hunt-render`) · **Задача:** task-9.
> **Режим:** код read-only (только чтение), git/Godot/`dotnet build` не запускались. Единственный записанный файл — этот план.
> **Источник дефектов:** `_plan/BUGS_REGISTRY.md` §13.2 (`R2-01…R2-08`), §13.3 (`R-12`, `R-13`), §15.2 (`U2-08`), §17 (`W2-09`). Полный аудит — `_reports/hunt311/render.md`.
> **Формат:** `_plan/fixplans/README.md`. Каждая правка опирается на прочитанный код (строка + цитата).

## 0. Сводка

| ID | P | Правка | Файлов | Объём | Перестройка |
|---|---|---|---|---|---|
| R2-01 | **P0** | Z: снять `ZIndex=-1` с корня мира, опустить `_groundSurface` в −1 (вариант A) | `Main.cs`, `MapRenderer.cs` | **S** (2 строки) | нет |
| R2-02 | P1 | `CustomAabb` из общего `MapRenderer.WorldAabb` вместо `[0,65536]²` | `MapRenderer.cs` + 7 рендереров | S/M (1 хелпер + 7 строк) | нет |
| R2-03 | P1 | Растущая ёмкость буфера предметов (шаблон `CropRenderer`) | `GroundItemRenderer.cs`, `StockpileItemRenderer.cs` | M | нет |
| R-12 | P0 | `TerrainWindowTiles` 512 → 1024 (окно накрывает остров) | `MapRenderer.cs` | S (1 строка) | нет |
| R-13 | P0 | Применять готовый буфер по его origin, новый запрос — только при сдвиге > 16 тайлов | `MapRenderer.cs` | S | нет |
| R2-04 | P2 | Оверлей высот: явные ветки `TileType` (гора ≠ вода) | `MapModeOverlayRenderer.cs` | S | нет |
| R2-05 | P2 | Оверлеи режимов: честное ограничение «только остров» (тултип/курсор) *или* мир | `MapModeOverlayRenderer.cs` (+`HUDController`) | S/M | **РЕШЕНИЕ ВЛАДЕЛЬЦА** |
| R2-06 | P2 | Оверлей: 1 байт/клетка + палитра, либо частичный `SetData` | `MapModeOverlayRenderer.cs` | M | нет |
| R2-07 | P2 | «Работы»: не обходить 100k задач в кадре (кэш/маска типов) | `MapModeOverlayRenderer.cs` | S/M | **РЕШЕНИЕ ВЛАДЕЛЬЦА** (частично) |
| R2-08 | P2 | Единая семантика поверхности для влажности/плодородия внутри и снаружи | `FertilityOverlayRenderer.cs`, `HumidityOverlayRenderer.cs` | M | **РЕШЕНИЕ ВЛАДЕЛЬЦА** |
| U2-08 | P2 | `Snapshot/Reset` только при видимом оверлее | `PerformanceOverlay.cs` | S (3 строки) | нет |
| W2-09 | P2 | `terrain_blend.gdshader`: пиксельная сетка/клетки из ЛОКАЛЬНЫХ координат | `terrain_blend.gdshader` | M | нет |

**Порядок по цене/эффекту:** R2-01 → R-13 → R-12 (один симптом «земля квадратом/под всем»), затем R2-02, R2-04, U2-08 — все дешёвые и независимые.

---

## 1. Карточки

### R2-01 · P0 · Статика стримингового мира ушла под шейдерную землю — точная правка Z

- **Файлы:** `scenes/main/Main.cs:279-287`, `src/ui/renderers/MapRenderer.cs:421-431` (плюс комментарии `:808-816`), `src/ui/streaming/StreamingWorldView.cs:1425-1490`.
- **Что сломано (код):**
  - корень мира ниже земли: `Main.cs:284` `ZIndex = -1,`;
  - земля всего мира — `ColorRect` с **относительным** z = 0 внутри `MapRenderer` (тоже z = 0) и он добавлен в дерево позже (`Main.cs:296`), значит при равном z рисуется сверху;
  - подняли z только двум слоям: `StreamingWorldView.cs:1429` `_decorLayer.ZIndex = 2;` (эффективный −1+2 = +1) и `:1443-1444` `_oreLayer.ZAsRelative = false; _oreLayer.ZIndex = 2;`;
  - `_markLayer` (`:1430`), `_mountainLayer` (`:1452`), `_wallLayer` (`:1463`), `_wallPlanLayer` (`:1467`), `_farmLayer` (`:1476`), `_farmPlanLayer` (`:1480`), `_planLayer` (`:1487`) создаёт `MakeLayer` (`:1500-1509`), который z не задаёт ⇒ `ZAsRelative = true`, `ZIndex = 0` ⇒ эффективный **−1 < 0** ⇒ под землёй.

- **Два варианта правки — сравнение**

  **Вариант A (рекомендую): убрать −1 с корня мира и опустить землю.**
  Корень перестаёт быть «отрицательной вселенной», единственный носитель −1 — сама земля. Тогда любой слой мира с z = 0 автоматически над землёй, и «забыть слой» уже нельзя.
  ```
  было:  Main.cs:279-287
         _worldView = new StreamingWorldView { Name = "StreamingWorldView", WorldSeed = ...,
                                               GeneratorVersion = ..., ZIndex = -1, Visible = true };
         AddChild(_worldView);

  стало: _worldView = new StreamingWorldView { Name = "StreamingWorldView", WorldSeed = ...,
                                               GeneratorVersion = ..., Visible = true };
         AddChild(_worldView);

  было:  MapRenderer.cs:421-431
         _groundSurface = new ColorRect { Name = "GroundSurface", Color = new Color(0f,0f,0f), Size = ..., Position = Vector2.Zero, MouseFilter = ... };

  стало: _groundSurface = new ColorRect { Name = "GroundSurface", Color = new Color(0f,0f,0f), Size = ..., Position = Vector2.Zero, MouseFilter = ...,
                                          ZIndex = -1 };   // единственный «низ» сцены
  ```
  Итоговая таблица эффективных z (после A): земля **−1** · island-слои 0 · world-слои 0 (tree order: мир добавлен раньше острова ⇒ остров рисуется поверх, а территории не пересекаются гейтом `PaintCell`) · декор мира 2 · руда мира 2 (абсолютный) · тени предметов 3 · предметы на земле 5 · предметы склада 6 · агенты 10 · метки работ 15.
  Плюсы: 2 строки, лечит корень, слой с дефолтным z больше не может «уехать под землю». Минусы: меняется базовая линия z всего поддерева мира — нужно один раз проверить, что в нём нет узлов, которым нужен z < 0 (по коду таких нет: 9 слоёв + `WorldMapOverlay` в отдельном `CanvasLayer`).

  **Вариант B (фолбэк, если нельзя трогать корень): абсолютный z каждому слою.**
  ```
  было:  _markLayer   = MakeLayer("WorldMark", _tileSet);
         _mountainLayer = MakeLayer("WorldMountain", mountainTiles);
         ... (7 слоёв без ZIndex)

  стало: // таблица одна на файл, рядом с BuildTileLayers
         private const int ZWorldOre = 2, ZWorldDecor = 2, ZWorldMountain = 3, ZWorldWall = 4,
                           ZWorldFarm = 5, ZWorldPlan = 6, ZWorldMark = 7;
         ...
         _markLayer = MakeAbsoluteLayer("WorldMark", _tileSet, ZWorldMark);
         _mountainLayer = MakeAbsoluteLayer("WorldMountain", mountainTiles, ZWorldMountain);
         _wallLayer = MakeAbsoluteLayer("WorldWall", wallTiles, ZWorldWall);
         _wallPlanLayer = MakeAbsoluteLayer("WorldWallPlan", wallTiles, ZWorldPlan);
         _farmLayer = MakeAbsoluteLayer("WorldFarm", farmTiles, ZWorldFarm);
         _farmPlanLayer = MakeAbsoluteLayer("WorldFarmPlan", farmTiles, ZWorldPlan);
         _planLayer = MakeAbsoluteLayer("WorldPlan", _tileSet, ZWorldPlan);
         // + private static TileMapLayer MakeAbsoluteLayer(string n, TileSet ts, int z)
         //   { var l = MakeLayer(n, ts); l.ZAsRelative = false; l.ZIndex = z; return l; }
  ```
  Плюсы: не трогает корень, слои адресуемы по одному. Минусы: 7 мест правки + обязанность помнить про таблицу; `_decorLayer`/`_oreLayer` остаются в «смешанном» виде (relative и absolute) — два способа задания z в одном файле.

  Сравнение: A — 2 строки и один инвариант («ровно один низ»), B — 14 строк и тот же инвариант поддерживается дисциплиной. **Рекомендация: A**, B — если владелец решит, что z корня мира = −1 нужен другим подсистемам (по коду таких потребителей нет: `grep ZIndex` по `StreamingWorldView` даёт только decor/ore + комментарии).

- **Что НЕ трогаем:** `_decorLayer.ZIndex = 2` и `_oreLayer` (absolute 2) остаются как есть — они уже над землёй; шейдер `terrain_blend.gdshader`, размеры окна (это R-12/R-13), `_groundSurface.Visible`/`Material`, порядок `AddChild` в `Main` (не переставляем — при варианте A он безвреден).
- **Риск:** средний-низкий. Единственный реальный риск варианта A — существование узла в поддереве мира, которому нужен z < 0. Проверено: `StreamingWorldView` создаёт только 9 `TileMapLayer` (`BuildTileLayers`, `:1416-1490`), все — содержимое мира. Откат — вернуть `ZIndex = -1` и удалить `ZIndex = -1` у `_groundSurface` (2 строки).
- **Проверка:** `[рантайм]` — за кромкой 1024 поставить стену, грядку, чертёж, складскую зону и клетку-метку работы: всё видно и совпадает по стилю с островом. Статически после правки: `grep -n "ZIndex" scenes/main/Main.cs src/ui/streaming/StreamingWorldView.cs src/ui/renderers/MapRenderer.cs` — ровно три присваивания (земля −1, декор 2, руда 2). Регресс-чек: остров целиком (земля/декор/объекты/агенты) — без изменений, HUD поверх.
- **Объём:** **S** (вариант A: −1 строка, +1 строка; вариант B: +14).
- **Перестройка:** нет.

---

### R2-02 · P1 · `CustomAabb` = `[0, 65536]²` у 7 MultiMesh-рендереров

- **Файлы:** `src/ui/renderers/GroundItemRenderer.cs:44-45,74`; `StockpileItemRenderer.cs:45-46,74`; `CropRenderer.cs:185-186,196`; `ItemShadowRenderer.cs:87,97`; `ShadowCasterRenderer.cs:63`; `AgentRenderer.cs:194-195,204,240`; `DesignationRenderer.cs:51`; новый хелпер — `src/ui/renderers/MapRenderer.cs` (рядом с `MapWidth`/`TileSizePx`).
- **Минимальная правка:** один статический AABB в `MapRenderer` с запасом по миру вместо «островного»:
  ```
  было (в каждом рендерере):
     float mapSizePx = MapRenderer.MapWidth * MapRenderer.TileSizePx;   // 1024*64 = 65536
     var mapAabb = new Aabb(Godot.Vector3.Zero, new Godot.Vector3(mapSizePx, mapSizePx, 1000f));
     ...
     CustomAabb = mapAabb

  стало (MapRenderer.cs — один раз, рядом с MapWidth/MapHeight):
     /// <summary>Запас AABB рендереров за кромкой острова (тайлы в каждую сторону).</summary>
     public const int WorldAabbMarginTiles = 2048;
     public static Aabb WorldAabb => new(
         new Vector3(-WorldAabbMarginTiles * TileSizePx, -WorldAabbMarginTiles * TileSizePx, 0f),
         new Vector3((MapWidth + 2 * WorldAabbMarginTiles) * TileSizePx,
                     (MapHeight + 2 * WorldAabbMarginTiles) * TileSizePx, 1000f));

  стало (в 7 рендерерах):
     CustomAabb = MapRenderer.WorldAabb;      // вместо локального mapAabb
  ```
  Запас 2048 тайлов (≈131 тыс. px в каждую сторону) снимает куллинг на всей обозримой игре и оставляет его на «улетел в бесконечность». В `AgentRenderer`/`CropRenderer`/`DesignationRenderer` локальная переменная `mapAabb` удаляется вместе со строкой `mapSizePx`.
- **Что НЕ трогаем:** `MaxRenderedInstances`, формат буферов, `InstanceCount`, сами буферы (R2-03 отдельно); `AgentRenderer` LOD-меш; `QuadMesh`/текстуры.
- **Риск:** низкий. Увеличение AABB отключает ранний отсев мультимеша — для 7–12 слоёв это доли микросекунды на кадр; зато исчезает «невидимый мир». Проверка `U-04`-симптома (метки при уходе камеры за остров) подтвердит. Откат — вернуть локальный `mapAabb` (7 строк).
- **Проверка:** `[рантайм]` — предмет на клетке `(−5, −5)` и на `(1030, 500)`: видно; агент/тень/культура за кромкой: видно. `[статически]` — `grep -n "new Aabb(Godot.Vector3.Zero" src/ui/renderers/` → 0 совпадений после правки.
- **Объём:** **S** (1 свойство + 7 присваиваний ≈ 15 строк суммарно; правки независимы по файлам).
- **Перестройка:** нет. (Опционально на будущее: динамический min/max по фактическим позициям снапшота — не в этой волне.)

---

### R2-03 · P1 · Полная перезаливка 512 КБ буфера на каждый тип предмета каждый кадр

- **Файлы:** `src/ui/renderers/GroundItemRenderer.cs:11, 66-85, 116-148`; `src/ui/renderers/StockpileItemRenderer.cs:17, 66-86, 110-141`. Образец в этом же дереве — `src/ui/renderers/CropRenderer.cs:149-169, 178-197`.
- **Что сломано (код):** `InstanceCount = MaxRenderedInstances` (16384) фиксирован навсегда, буфер `new float[16384 * 8]` (512 КБ), и `mesh.Buffer = buffer` заливает **все 16384** инстанса даже когда `VisibleInstanceCount = 1`. Слои ленивые по типу (`:104-108`), комментарий рядом — до 91 типа.
  > Важно: «залить только `count*8`» **нельзя** — Godot требует длину буфера ровно `instance_count * stride`, иначе отклоняет буфер (механизм R-067). Поэтому минимальная правильная правка — уменьшать `InstanceCount` вместе с буфером, как уже сделано в `CropRenderer`.
- **Минимальная правка (шаблон `CropRenderer` — растущая ёмкость):**
  ```
  было (GroundItemRenderer):
     private const int MaxRenderedInstances = 16384;
     ...
     InstanceCount = MaxRenderedInstances,
     VisibleInstanceCount = 0,
     ...
     _renderers[id] = (multiMesh, instance, new float[MaxRenderedInstances * 8]);
     ...
     mesh.VisibleInstanceCount = count;
     if (count > 0) { ...запись count*8 float...; mesh.Buffer = buffer; }

  стало:
     private const int MaxRenderedInstances = 16384;   // верхний предел (оставлен)
     private const int InitialCapacity = 256;          // стартовая ёмкость слоя
     private readonly Dictionary<ItemId, int> _capacity = new();
     ...
     InstanceCount = InitialCapacity,                  // не 16384
     ...
     _renderers[id] = (multiMesh, instance, new float[InitialCapacity * 8]);
     _capacity[id] = InitialCapacity;
     ...
     if (count > _capacity[id]) Grow(id, count);       // удвоение до MaxRenderedInstances,
                                                       // InstanceCount = ёмкость, Buffer перезаливается в этом же кадре
     mesh.VisibleInstanceCount = count;
     if (count > 0) { ...; mesh.Buffer = buffer; }     // длина буфера == InstanceCount*8 всегда
  ```
  `Grow` — 12 строк, копируется по образцу `CropRenderer.GrowCombo` (`:149-169`): новая ёмкость `min(MaxRenderedInstances, cap*2)`, `Array.Copy` старого префикса, `local.x.InstanceCount = cap`, `local.x.Buffer` перезаливается текущим кадром. То же самое — в `StockpileItemRenderer` (код идентичен, правка копируется).
  Эффект: при 1–50 предметах слоя заливается 2–4 КБ вместо 512 КБ (×91 тип: ~0.4 МБ вместо ~46 МБ в худшем кадре).
- **Что НЕ трогаем:** `CustomAabb` (R2-02), формат 8 float/инстанс, ленивое создание слоёв, `ItemShadowRenderer` (там уже dirty-флаг, `:63-72`), порядок `_Process`, снапшоты менеджеров. Слияние 91 слоя в один MultiMesh с `UseColors/UseCustomData` — не в этой волне (L).
- **Риск:** низкий-средний. Опасное место одно: между `InstanceCount = новая ёмкость` и `Buffer = buffer` движок может отрисовать кадр со «старым» содержимым, но это внутри одного кадра `_Process` до отрисовки — безопасно (так же работает `CropRenderer`). Откат — вернуть константу `MaxRenderedInstances`.
- **Проверка:** `[рантайм]` — профайлер: `_Process` `GroundItemRenderer` и объём маршалинга на кадр при 5 типах предметов на земле; визуально — предметы на месте, ничего не «прыгает» при росте числа (переход 256→512). `[статически]` — `mesh.Buffer` присваивается только буферу длины `InstanceCount*8`.
- **Объём:** **M** (2 файла × ~25 строк).
- **Перестройка:** нет.

---

### R-12 · P0 · Окно земли 512 тайлов меньше острова 1024 — суша обрезается квадратом

- **Файлы:** `src/ui/renderers/MapRenderer.cs:44-52` (`TerrainWindowTiles`, `TerrainWindowShift`), `:421-431` (`_groundSurface.Size`), `:452`, `:815-816`, `:944-949`, `:1227`.
- **Что сломано (код):** `:49` `public const int TerrainWindowTiles = 512;` — от этой константы выведено ВСЁ: размер `ColorRect` (`:427`), `map_size` шейдера (`:452`, `:1227`), буферы заливки (`:949`, `:966`, `:1202`), центр follow-окна (`:815-816`), условие «окно целиком внутри острова» (`:946-947`). Остров — `MapWidth/MapHeight = 1024` (`MapRenderer.cs:44-49`), т.е. окно физически не может накрыть остров: за полигоном 512² шейдер не растрируется.
  > Связка с R-13: пока камера стоит, окно следует за ней и квадрат не виден; симптом вылезает именно потому, что R-13 выбрасывает готовые окна на ходу. Поэтому порядок правок: **сначала R-13, потом R-12** (см. §2).
- **Минимальная правка:**
  ```
  было:  MapRenderer.cs:49
         public const int TerrainWindowTiles = 512;

  стало: public const int TerrainWindowTiles = 1024;   // = MapWidth/MapHeight: окно накрывает остров целиком
  ```
  Все производные величины пересчитываются сами (размер ColorRect, `map_size`, размеры буферов, шаг follow-окна). Дополнительно — один комментарий у константы: «не уменьшать ниже `MapWidth` — иначе на острове появляется кромка окна».
- **Что НЕ трогаем:** `TerrainWindowShift = 64` (квантование сдвига; инкрементальный пересчёт полосы 64×1024 остаётся), алгоритм заливки `FillTerrainWindowExternal` (сетка 16 остаётся, станет 65×65 узлов вместо 33×33), шейдер (R2-01/W2-09 отдельно), параметры камеры.
- **Риск:** средний по перфу, низкий по логике. Цена: буферы 512 КБ → 2 МБ (`byte[1024*1024]` ×2, оба в LOH), R8-текстура 1 МБ, полная заливка окна ~4× дороже (была ~0.3 мс синхронно внутри острова / фоном снаружи), инкрементальный сдвиг — 64×1024 вместо 64×512. Всё это происходит **не каждый кадр** (только при сдвиге окна на 64 тайла), на фоновом потоке для внешних окон. Если 2 МБ буферов неприемлемы — альтернатива в «Перестройке».
- **Проверка:** `[рантайм]` — встать камерой на угол острова `(1000,1000)` и отъехать (zoom 0.25): кромки окна/обрыва суши по квадрату нет; за кромкой 1024 мир рисуется без шва. `[статически]` — `TerrainWindowTiles >= MapWidth`.
- **Объём:** **S** (1 строка + комментарий).
- **Перестройка:** формально **нет** (минимальная правка закрывает симптом), но есть выбор «правильного» решения, который лучше зафиксировать:
  - **(а) 1024 фиксированно** (рекомендую): просто, окно ≥ острова при любом зуме; минус — 2 МБ LOH и лишняя работа при сильном отдалении (окно больше, чем нужно).
  - **(б) динамическое окно**: `side = clamp(max(MapWidth, viewportTiles + 128) округлённое до 64, 256, 2048)`; плюс — расход пропорционален зуму; минус — константа перестаёт быть `const`, `map_size`/буферы пересоздаются при смене зума (M/L, риск утечек).
  - **(в) полноэкранный квад в экранных координатах + пересчёт в мир в шейдере**: снимает вопрос размера совсем (заодно закрывает W2-09), но это L-перестройка «одного рендера земли» — отдельной задачей.

---

### R-13 · P0 · Готовый буфер окна земли выбрасывается при движении камеры (земля залипает)

- **Файлы:** `src/ui/renderers/MapRenderer.cs:818-838` (`_Process`), `:929-1006` (`RequestTerrainWindowUpdate`), `:1192-1228` (`ApplyTerrainBuffer`).
- **Что сломано (код):** `:821-832`
  ```
  if (_pendingReadyBuffer != null)
  {
      byte[] readyBuf = _pendingReadyBuffer;
      _pendingReadyBuffer = null;
      if (_pendingReadyGen == _terrainWindowGen
          && _pendingReadyOriginX == targetOriginX
          && _pendingReadyOriginY == targetOriginY)   // ← строгое равенство
      { ApplyTerrainBuffer(...); _terrainWindowValidGen = _pendingReadyGen; }
  }
  ```
  `targetOrigin` пересчитывается от позиции камеры каждый кадр (`:815-816`); пока камера едет, равенство не выполняется **никогда** ⇒ свежий буфер уничтожается (`_pendingReadyBuffer = null`) и запрос запускается заново (`:834-837`). Земля обновляется только после полной остановки камеры.
- **Минимальная правка:** применять буфер по **его собственному** origin (он уже записан в `_pendingReadyOriginX/Y` и переносится в `_groundSurface.Position` внутри `ApplyTerrainBuffer`, `:1224`), а новый запрос инициировать только при уходе камеры дальше порога:
  ```
  было:
     if (_pendingReadyGen == _terrainWindowGen
         && _pendingReadyOriginX == targetOriginX
         && _pendingReadyOriginY == targetOriginY)

  стало:
     if (_pendingReadyGen == _terrainWindowGen)

  было: (ниже, :834)
     if (targetOriginX != _currentTerrainOriginX || targetOriginY != _currentTerrainOriginY || _terrainTextureNeedsUpdate)
         RequestTerrainWindowUpdate(targetOriginX, targetOriginY);

  стало:
     // Новый запрос — только когда окно реально ушло от применённого больше чем на 16 тайлов
     // (запас покрывает цветной хвост и не гоняет фоновую заливку каждый кадр).
     const int RecomputeSlackTiles = 16;
     bool needsNewWindow = _terrainTextureNeedsUpdate
         || targetOriginX < _currentTerrainOriginX - RecomputeSlackTiles
         || targetOriginX > _currentTerrainOriginX + RecomputeSlackTiles
         || targetOriginY < _currentTerrainOriginY - RecomputeSlackTiles
         || targetOriginY > _currentTerrainOriginY + RecomputeSlackTiles;
     if (needsNewWindow)
         RequestTerrainWindowUpdate(targetOriginX, targetOriginY);
  ```
  (при `_currentTerrainOriginX == long.MinValue` первый запрос всё равно уйдёт — сравнение с `long.MinValue` даст «уехали»).
- **Что НЕ трогаем:** инкрементальный сдвиг `front→back` (`:968-999`, `canReuse`-условия), поколения `_terrainWindowGen`, fast-path «окно внутри острова» (`:944-954`), `TerrainWindowTiles` (это R-12), поток заливки.
- **Риск:** низкий-средний. Приняв буфер чужого origin, мы на один-два кадра покажем окно, чуть сдвинутое относительно идеального центра, — это тот же порядок, что и раньше при остановке камеры. Порог 16 тайлов меньше половины окна (256) ⇒ «дыры» на краю экрана не будет: окно 512² покрывает вьюпорт при zoom ≥ 0.12. Откат — вернуть строгое равенство (это 2 строки).
- **Проверка:** `[рантайм]` — лететь WASD со Shift непрерывно 3–5 с: земля под ногами меняется плавно, без «залипания на секунду» и рывка после остановки. Дополнительно — счётчик запусков фонового таска (лог/профайлер) должен упасть с «каждый кадр» до «раз в ~16 тайлов».
- **Объём:** **S** (≈10 строк в одном методе).
- **Перестройка:** нет.

---

### R2-04 · P2 · Оверлей «Высоты» красит горы цветом воды

- **Файлы:** `src/ui/renderers/MapModeOverlayRenderer.cs:295-314`; справочник типов — `src/core/world/TileType.cs:7-13`.
- **Минимальная правка:**
  ```
  было:
     bool water = _map.Ground[x, y] != TileType.Grass;
     Color c;
     if (water) { c = new Color(0.12f, 0.32f, 0.62f, 0.70f); }
     else { float t = ...; c = ElevationColor(t); c.A = 0.78f; }

  стало:
     TileType surface = _map.Ground[x, y];
     Color c;
     if (surface == TileType.Water)
     {
         c = new Color(0.12f, 0.32f, 0.62f, 0.70f);
     }
     else
     {
         float t = Math.Clamp((elev[row + x] - min) / span, 0f, 1f);
         c = ElevationColor(t);
         // Гора — не вода: каменный тон и максимальная непрозрачность,
         // иначе хребет читается как озеро.
         if (surface == TileType.Mountain) c = c.Lerp(new Color(0.46f, 0.43f, 0.40f, 0.88f), 0.65f);
         c.A = 0.78f;
     }
  ```
- **Что НЕ трогаем:** `ElevationColor` (общий градиент), палитры `OreColor`/`JobColor`/`StockpileItemColor`, сбор высот `BeginBulkSample`, режим «Руда».
- **Риск:** низкий (только цвет одной ветки). Откат — вернуть условие `!= Grass`.
- **Проверка:** `[рантайм]` — включить «Высоты» на острове с горами: хребет серый/светлый, вода синяя, трава/песок — градиент.
- **Объём:** **S** (≈8 строк).
- **Перестройка:** нет. (По §18.5 владельца — закрывать вместе с R2-08 одной задачей «единая семантика поверхности клетки»; технически правки независимы.)

---

### R2-05 · P2 · Оверлеи режимов карты слепы за кромкой 1024

- **Файлы:** `src/ui/renderers/MapModeOverlayRenderer.cs:65-76` (`_Ready`: `Size = MapWidth*TileSizePx`), `:128-149` (`SetMap`), `:219-249` (`BuildOre`), `:318-356` (`BuildStockpile`). Точка информирования игрока: `src/ui/controllers/HUDController.cs` (переключение `MapMode`).
- **Механизм (код):** текстура и `Size` строго `MapData.Width/Height` (остров), построители читают только `_map.*`/`StockpileManager` острова; ветки для мира нет — в отличие от `HumidityOverlayRenderer.cs:101-111`, где «ДВЕРЬ» в `WorldModel` уже сделана.
- **Минимальная правка (вариант «честно ограничить», S):** не расширять покрытие, а сообщить игроку и не врать пустотой:
  ```
  было (HUDController, переключение режима — там, где ставится `_mapModeOverlay.SetMode(mode)`):
     _mapRenderer.SetMapMode(mode);

  стало:
     _mapRenderer.SetMapMode(mode);
     if (MapModeOverlayRenderer.Handles(mode) && mode != MapMode.Elevation
         && !WorldPlacementPass.IsIslandCell(cursorTile.X, cursorTile.Y))
         ShowToast("Режим карты показывает данные только на территории колонии");   // существующий механизм тостов
  ```
  Плюс — сузить режим по кнопке/HUD: для клеток вне острова палитра не обещает данные (по §18.5 это одна задача «единая семантика клетки»).
- **Что НЕ трогаем:** сами палитры и построители, `SetMap`, размер текстуры, режимы «Влажность/Плодородие» (у них мир уже есть), `WorldMapOverlay` (глобальная карта — отдельная подсистема).
- **Риск:** минимальный (UI-текст); при желании легко откатить.
- **Проверка:** `[рантайм]` — включить «Руда», уехать за кромку: понятно, что данных нет (а не «руды нет вовсе»).
- **Объём:** **S** (≈5 строк в HUD) / **M** если делать мир (см. решение).
- **Перестройка:** **ТРЕБУЕТ РЕШЕНИЯ ВЛАДЕЛЬЦА.**
  Вопрос: оверлеи режимов должны покрывать бесконечный мир (паритет 1 в 1) или это «инструмент колонии» по определению?
  - **(1) Инструмент колонии** (рекомендую на эту волну): 5 строк UI-текста, честно, дёшево; минус — паритет Д не соблюдён формально.
  - **(2) Мир через детерминированные поля**: руда — `OreWorldField` (чистая функция координат) в текстуру 1 байт/клетка вокруг камеры; склад — клетки `_markLayer`/`SrcStockpile` из `StreamingWorldView`. Плюс — паритет; минус — M/L: новая текстура «следует за камерой», событий «руда изменилась в мире» нет (`OreWorldState.CellChanged` есть — можно подписаться).
  - **(3) Общая мировая текстура режимов** (одна на мир, тайлами 64 px, ленивая по чанкам) — согласуется с R2-06, но это отдельная перестройка слоя оверлеев (L).

---

### R2-06 · P2 · Перестройка оверлея заливает 4 МБ текстуры на любое изменение

- **Файлы:** `src/ui/renderers/MapModeOverlayRenderer.cs:34-35` (интервалы dirty), `:195-216` (`Rebuild`), `:394-403` (`WritePixel`).
- **Что сломано (код):** `:199-215`
  ```
  if (_pixels == null || _pixels.Length != _w * _h * 4) _pixels = new byte[_w * _h * 4];  // 4 МБ LOH
  _image ??= Image.CreateEmpty(_w, _h, false, Image.Format.Rgba8);                        // 4 МБ GPU
  Array.Clear(_pixels, 0, _pixels.Length);                                                // 4 МБ memset
  ... построители ...
  _image.SetData(_w, _h, false, Image.Format.Rgba8, _pixels);                             // 4 МБ upload
  _texture.SetImage(_image);
  ```
  Изменилась одна клетка руды — пересчитана и перезалита вся карта (1024×1024 RGBA).
- **Минимальная правка (два независимых шага, можно брать по одному):**
  1. **Частичный upload (S, рекомендую первым):** помнить bbox изменённых клеток в рамках `Rebuild` (по факту `WritePixel` мин/макс x/y) и заливать только эту область:
     ```
     стало (в конце Rebuild, вместо полного SetData):
        if (_dirtyRect.HasArea())
            _image.SetData(_w, _h, false, Image.Format.Rgba8, _pixels);   // оставляем, если региона не хватает
        // предпочтительно: собрать плотный под-массив bbox и использовать Image.BlitRect(_image, src, rect)
        // + _texture.Update(_image)
     ```
     На практике проще: `Image.CreateEmpty` держим, но вместо полного `byte[_w*_h*4]`+`Array.Clear` каждый раз — `_image.Fill(Color.Transparent)` только когда меняется набор данных, а для инкремента копить список изменённых клеток (`OnOreCellChanged(pos)` уже даёт координату! `:100-104`) и писать `SetPixel` точечно.
     Здесь важна честность: `Image.SetPixel` в цикле — это интероп на клетку, поэтому точечный путь выгоден только при малом числе изменений за интервал (1–50 клеток за 0.35 с — типично для добычи).
  2. **1 байт/клетка + палитра (M):** вместо RGBA8 держать R8-«код цвета» (0=пусто, 1..23 руда, 24.. — склад/работы) и раскрашивать в шейдере по таблице `uniform vec4 palette[32]`. 4 МБ → 1 МБ, upload ×4 меньше, `Array.Clear` ×4 дешевле. Минус — нужен маленький шейдер для оверлея и разделение палитр по режимам.
- **Что НЕ трогаем:** палитры (`OreColor`, `ElevationColor`, `StockpileItemColor`, `JobColor`), троттлинг `DirtyRefreshIntervalSec = 0.35`, `RefreshThrottled`-гейты, `SetMap`/`SetMode`, построители (R2-05/R2-07 отдельно).
- **Риск:** низкий. Точечная запись требует, чтобы `Array.Clear` больше не вызывался вслепую — иначе «точечный» путь будет рисовать по чистому буферу и карта исчезнет; порядок шагов в карточке это учитывает. Откат — вернуть полный `SetData`.
- **Проверка:** `[рантайм]` — режим «Руда», добыть одну жилу: длительность кадра не меняется; профиль `Rebuild` < 0.5 мс. `[статически]` — в горячем пути нет `new byte[_w*_h*4]` и полного `Array.Clear` на каждое событие.
- **Объём:** шаг 1 — **S/M** (≈20 строк), шаг 2 — **M** (свой шейдер + переиндексация палитр).
- **Перестройка:** нет (шаг 1); шаг 2 — согласовать с R2-05(3)/W2-01-соседями, но формально тоже правка одного класса.

---

### R2-07 · P2 · Оверлей «Работы» дёргает до 100 000 `GetJobData()` из кадра

- **Файлы:** `src/ui/renderers/MapModeOverlayRenderer.cs:36, 173-186, 358-390`.
- **Что сломано (код):** `:365-389`
  ```
  int total = index.TotalCount;                       // может быть ≫ 100k
  index.SnapshotActiveJobs(_jobIds, Math.Min(total, MaxJobDraw), ref cursor);
  for (int i = 0; i < _jobIds.Count; i++)
  {
      JobData job;
      try { job = index.GetJobData(_jobIds[i]); } catch { continue; }   // межъязыковой вызов на КАЖДУЮ задачу
      if (!job.IsActive || job.TypeId == JobTypeId.None) continue;
      ...
  }
  ```
  Плюс `_jobIds` не сортируется/не фильтруется — рисуется всё, что попало в срез.
- **Минимальная правка (S, без изменения индекса):** ограничить объём и не работать на пустом кадре:
  ```
  было:  private const int MaxJobDraw = 100_000;
  стало: private const int MaxJobDraw = 8_192;      // одна строка: бюджет кадра оверлея

  было:  for (int i = 0; i < _jobIds.Count; i++) { try { job = index.GetJobData(_jobIds[i]); } catch { continue; } ... }

  стало: for (int i = 0; i < _jobIds.Count; i++)
         {
             if (!index.TryGetJobData(_jobIds[i], out JobData job)) continue;   // если такого API нет —
                                                                                // оставить try/catch, но вынести из цикла нечего
             if (!job.IsActive || job.TypeId == JobTypeId.None) continue;
             ...
         }
  ```
  Плюс (важно): режим «Работы» перестраивается раз в `JobsRefreshIntervalSec = 1.0` **даже если ничего не изменилось** (`:179` — для Jobs `_dirty` не проверяется). Минимально — добавить `_dirty` в проверку и ставить его из `JobDispatcher`-событий, если такие есть; если событий нет — оставить 1 Гц, но с ограничением `MaxJobDraw` выше.
- **Что НЕ трогаем:** `JobSpatialPyramid` (его правят в другой волне), формат `JobData`, палитру `JobColor`, остальные режимы.
- **Риск:** низкий: при >8192 активных задач оверлей покажет не все метки — это честнее, чем 100k межъязыковых вызовов в кадре (и всё равно неразличимо на экране). Откат — вернуть константу.
- **Проверка:** `[рантайм]` — 10k+ активных задач, режим «Работы» включён: спайка раз в секунду нет (`Performance`/`SimEvents`), метки рисуются.
- **Объём:** **S** (1 константа + возможно замена вызова).
- **Перестройка:** **ТРЕБУЕТ РЕШЕНИЯ ВЛАДЕЛЬЦА** только для «обновлять по событию»: в `JobSpatialPyramid`/`JobDispatcher` нужен публичный счётчик-поколение (`JobsVersion++` на Register/Complete). Варианты: (1) поколение-счётчик и `_dirty` по изменению — M, точнее всего; (2) оставить 1 Гц с бюджетом 8192 — S, сейчас; (3) отключить режим при >N задач — S, но хуже UX.

---

### R2-08 · P2 · Влажность/плодородие за кромкой считаются другой формулой — шов по 1024

- **Файлы:** `src/ui/renderers/FertilityOverlayRenderer.cs:89-128` (`:99-118` — выбор источника), `src/ui/renderers/HumidityOverlayRenderer.cs:89-121` (`:101-111`).
- **Что сломано (код):**
  ```
  if (fertility != null && (uint)mapX < (uint)fertW && (uint)mapY < (uint)fertH)
      fert = fertility.Get(mapX, mapY);                    // внутри острова: FertilityMap (симуляция почвы)
  else
  {
      var cell = Game.Core.World.WorldModel.CellAtLocal(mapX, mapY);
      if (cell.Terrain == BaseTerrainKind.Water || cell.Terrain == BaseTerrainKind.Mountain) fert = 0;
      else fert = (int)(cell.ForestQ16 * 150L / 65535) + 50;   // снаружи: лес, а не почва
  }
  ```
  Разные величины (динамическая почва vs статический лес) и разные шкалы ⇒ на линии 1024 виден ступенчатый перепад цвета. То же в `HumidityOverlayRenderer` (`humidity.Get` против `WorldModel.MoistureQ16AtLocal`, `:101-111`).
- **Минимальная правка (симметрия шкалы, S/M):** привести за-кромочную формулу к той же шкале 0..200, что и островная, и убрать «лес вместо почвы» для плодородия:
  ```
  стало (FertilityOverlayRenderer):
      else
      {
          var cell = Game.Core.World.WorldModel.CellAtLocal(mapX, mapY);
          if (cell.Terrain == BaseTerrainKind.Water || cell.Terrain == BaseTerrainKind.Mountain) fert = 0;
          else fert = Math.Clamp(cell.SoilFertilityQ16 * 200 / 65535, 0, 200);   // та же величина, что и FertilityMap
      }
  ```
  Если `CellAtLocal` не отдаёт почву в Q16 (нужно проверить по `WorldModel`/`GeneratedCell`) — оставить `ForestQ16`, но с явным комментарием «приближение: почва за кромкой не моделируется» и поставить `_plan`-пометку. Это ровно то место, где нужна «единая семантика поверхности» из §18.5 (вместе с R2-04 и N-03).
- **Что НЕ трогаем:** `MoistureColor`/`FertilityColor` (палитры), разрешение 128×128 и центрирование текселя (`:95-99` — это уже FIX bughunt 14-7), троттлинг 1 с, `HumidityMap`/`FertilityMap` как классы (их правит другая волна), генератор тепловых карт.
- **Риск:** низкий, но именно здесь легко «подкрутить» цвет и получить другой шов. Откат — вернуть прежнюю формулу.
- **Проверка:** `[рантайм]` — камера медленно едет через линию 1024 с включённым оверлеем плодородия/влажности: ступеньки по прямой линии нет (допустимый перепад — только от данных, не от ветки кода).
- **Объём:** **M** (2 файла, по 5–10 строк) — при наличии `SoilFertilityQ16`/`MoistureQ16` в `CellAtLocal`; **S**, если ограничиться комментарием-приближением.
- **Перестройка:** **ТРЕБУЕТ РЕШЕНИЯ ВЛАДЕЛЬЦА** (по §18.5 — один вопрос на три карточки).
  Вопрос: какая величина считается «плодородием» для всего мира — динамическая почва (`FertilityMap`, только остров) или климатическая (генерируемая везде)?
  - **(1) Климатическая везде** (включая остров как базу + дельты симуляции): один источник, шва нет; минус — на острове оверлей начнёт отличаться от текущего (придётся пересчитать цвета).
  - **(2) Разные с явной пометкой**: остров — почва, мир — климат, на границе — плавный блендинг по 32 тайлам (M, простая интерполяция двух значений в полосе).
  - **(3) Заморозить как есть**: карточка закрывается как «принятое ограничение», в `docs/` пишется, что оверлей за кромкой — климатическое приближение.

---

### U2-08 · P2 · `PerformanceOverlay` собирает разбивку draw calls даже когда скрыт

- **Файлы:** `src/ui/PerformanceOverlay.cs:449-462`; счётчик — `src/core/common/DrawCallSplit.cs:56-95`.
- **Что сломано (код):** `:452-461`
  ```
  public override void _Process(double delta)
  {
      DrawCallSplit.Snapshot(out _dcSplitCache);   // new SplitEntry[_groups.Count] + Array.Sort каждый кадр
      DrawCallSplit.Reset();                       // обход словаря + Interlocked на группу
      ...
  ```
  `_isVisible` проверяется только ниже (в `_Input`, `:434`), т.е. скрытый оверлей всё равно каждый кадр аллоцирует массив, сортирует его и обнуляет счётчики (при этом `Report` всё равно перезаписывает значения в этом же кадре — учёт «на землю»).
- **Минимальная правка:**
  ```
  было:
     public override void _Process(double delta)
     {
         DrawCallSplit.Snapshot(out _dcSplitCache);
         DrawCallSplit.Reset();

         // Плавное затухание статуса копирования
         ...

  стало:
     public override void _Process(double delta)
     {
         // Скрытый оверлей не собирает статистику: снимок и Reset() каждый кадр
         // аллоцировали массив и сортировали его впустую (L-07 живёт только для панели).
         if (_isVisible)
         {
             DrawCallSplit.Snapshot(out _dcSplitCache);
             DrawCallSplit.Reset();
         }

         // Плавное затухание статуса копирования
         ...
  ```
- **Что НЕ трогаем:** `DrawCallSplit` (его контракт и `Report` из рендереров), сам `_Draw` панели, `_refreshTimer`-блок (0.1 с), `_copyStatus`, остальной `_Process`.
- **Риск:** минимальный. Единственный нюанс: при первом включении панели `_dcSplitCache` может содержать данные кадра, в котором оверлей был скрыт (пустой массив — старые данные затираются `Reset`-ом не будут, но `Snapshot` при следующем видимом кадре всё пересоберёт). Проверяется сразу: включить F3 — разбивка появляется в тот же кадр.
- **Проверка:** `[рантайм]` — F3 выключен: в профиле/аллокаторе нет `SplitEntry[]` на кадр (`DrawCallSplit` аллокации = 0); включить F3 — панель показывает те же группы, что и до правки.
- **Объём:** **S** (3 строки).
- **Перестройка:** нет.

---

### W2-09 · P2 · `terrain_blend.gdshader`: на дальних координатах float32 ломает пиксель-сетку и клетки

- **Файлы:** `src/shaders/terrain_blend.gdshader:44-48` (`varying world_pos`), `:66-71` (`sample_cell`), `:90-93` (`evaluate_biome`: `p/64`), `:214-222` (`pix_wp = floor(world_pos/pixel_size)*pixel_size`). C#-сторона уже передаёт origin: `src/ui/renderers/MapRenderer.cs:1226` `SetShaderParameter("map_origin", new Vector2(originX, originY))`.
- **Что сломано (код):**
  ```
  varying vec2 world_pos;
  void vertex() { world_pos = (MODEL_MATRIX * vec4(VERTEX, 0.0, 1.0)).xy; }   // абсолютные мировые ПИКСЕЛИ
  ...
  vec2 pix_wp = floor(world_pos / pixel_size) * pixel_size;                   // pixel_size = 2.0
  ```
  `MODEL_MATRIX` содержит позицию окна (`origin` в пикселях). При уходе от origin на ~2^26 px ulp(float32) ≳ 4 px ⇒ `floor(p/2)*2` перестаёт быть сеткой 2 px (пиксель-арт «плывёт»), а `p/64` в `evaluate_biome` даёт неверную клетку на границе.
- **Минимальная правка:** считать **сетку и номера клеток** из локальных координат квада (точных), а абсолютные координаты оставить только для непрерывных шумов (иначе FBM-рисунок берега «переползёт» при сдвиге окна):
  ```
  было:
     varying vec2 world_pos;
     void vertex() { world_pos = (MODEL_MATRIX * vec4(VERTEX, 0.0, 1.0)).xy; }
     int sample_cell(ivec2 cell) { ivec2 local_cell = cell - ivec2(map_origin); ... }

  стало:
     varying vec2 world_pos;   // абсолютные px — ТОЛЬКО для непрерывных шумов (FBM/зубцы)
     varying vec2 local_pos;   // px внутри окна (0 .. окно*64) — точные, без потери разрядности
     void vertex() {
         world_pos = (MODEL_MATRIX * vec4(VERTEX, 0.0, 1.0)).xy;
         local_pos = VERTEX;                       // ColorRect: вершина уже в локальных px
     }
     int sample_cell(ivec2 cell) { ivec2 local_cell = cell - ivec2(map_origin); ... }   // без изменений

  // в fragment(): пиксельная сетка — из local_pos (фаза та же: origin кратен 64 клеткам = 4096 px,
  // а 4096 делится на pixel_size=2); номера клеток — целочисленно:
  было:
     vec2 pix_wp = floor(world_pos / pixel_size) * pixel_size;
     int main_type = evaluate_biome(pix_wp);
  стало:
     vec2 pix_local = floor(local_pos / pixel_size) * pixel_size;
     vec2 pix_abs   = vec2(ivec2(map_origin) * 64) + pix_local;   // для непрерывных шумов, без дробной части
     int main_type = evaluate_biome(pix_abs, pix_local);
     // evaluate_biome получает ДВА аргумента: abs — для noise()/fbm, local — для клеток:
     //   vec2 tile_uv = (local / 64.0) - vec2(0.5);  ivec2 cell_i = ivec2(map_origin) + ivec2(floor(tile_uv));
     //   sample_cell(cell_i)  — как сейчас (вычитает map_origin → локальный тексель)
  ```
  Так дискретная часть (сетка 2 px, номер клетки, `texelFetch`) считается в точных локальных числах, а непрерывные шумы (`get_shore_fbm`, `sand_shift`, `steppe_shift`, `rock`) остаются на абсолютных координатах — **рисунок берега не меняется**. Остаточная погрешность шумов на 2^26 px (≈4 px на масштабе 28–200 px) — это и есть текущее поведение, отдельной правки не требует.
- **Что НЕ трогаем:** C#-сторону (`map_origin` уже передаётся, `map_size` тоже), формулы шума/порогов (`shore_inset`, `shore_curvature`, `teeth`), палитры биомов, ветки обрыва/бликов. Для веток обрыва (`evaluate_biome(pix_wp ± pixel_size)`) достаточно передавать `pix_local ± pixel_size` вместе с теми же абсолютными.
- **Риск:** средний — шейдер большой, вызовов `evaluate_biome` шесть. Обязательный визуальный регресс-чек у берега при обычных координатах (0..1024): рисунок должен быть **пиксель-в-пиксель** прежним (там ulp ≪ 1 px, поэтому правка не должна ничего менять). Откат — один файл шейдера.
- **Проверка:** `[рантайм]` — регион далеко от origin (`SetWorldOrigin` на ~10^6 px): берег/пиксель-сетка такие же чёткие, как дома; дома — скриншот до/после совпадает (допустимый ноль отличий). `[статически]` — в шейдере `floor(... / pixel_size)` не применяется к абсолютному `world_pos`.
- **Объём:** **M** (`terrain_blend.gdshader`, ~20 строк; зависимостей в C# нет).
- **Перестройка:** нет (это и есть минимальный вариант; радикальный — вариант (в) из R-12 «полноэкранный квад», отдельная задача).

---

## 2. Волны применения

Волна 0 (можно параллельно, разные файлы/не пересекаются):
1. **U2-08** — `src/ui/PerformanceOverlay.cs` (S, изолирован).
2. **R2-04** — `src/ui/renderers/MapModeOverlayRenderer.cs` (S, изолирован от R2-05/06/07 по строкам, но **один файл** — см. конфликты).

Волна 1 — «земля и слои» (один симптом, порядок важен):
3. **R-13** (`MapRenderer.cs`, `_Process` + запрос окна) — без него R-12 не видно.
4. **R-12** (`MapRenderer.cs:49`, одна константа) — сразу после R-13, иначе окно 1024 останется залипающим.
5. **R2-01** (`Main.cs:284` −1 строка, `MapRenderer.cs` `_groundSurface.ZIndex = -1` +1 строка) — независим от R-12/R-13 по строкам, но **обязан** идти после них: иначе визуальная проверка за кромкой будет искажена «квадратом земли».

Волна 2 — MultiMesh (независимо друг от друга, но R2-02 первым):
6. **R2-02** — `MapRenderer.cs` (хелпер) + 7 рендереров: сначала один файл, потом остальные; проверка каждого отдельно.
7. **R2-03** — `GroundItemRenderer.cs` и `StockpileItemRenderer.cs`: правка идентична, второй файл копируется после проверки первого.

Волна 3 — оверлеи (требуют решения владельца, кроме R2-06):
8. **R2-06** — `MapModeOverlayRenderer.cs` (шаг 1: частичный upload).
9. **R2-05 / R2-07 / R2-08** — после ответа владельца (§18.5 и вопросы ниже).

Волна 4 — шейдер:
10. **W2-09** — `terrain_blend.gdshader` отдельно от всех C#-правок (меняет только GPU-код, регресс-чек скриншотом у берега).

## 3. Конфликты по файлам и порядок

| Файл | Карточки | Порядок / как разводить |
|---|---|---|
| `src/ui/renderers/MapRenderer.cs` | **R-13**, **R-12**, **R2-01**(1 строка), **R2-02**(хелпер) | строго: R-13 → R-12 → R2-01 → R2-02. Участки разные (`_Process` и `RequestTerrainWindowUpdate` / константа `:49` / `_Ready`-ColorRect / новый static рядом с `MapWidth`), но правки в одном классе — применять последовательно, после каждой пересборка не требуется (план), проверка визуальная. |
| `src/ui/streaming/StreamingWorldView.cs` | **R2-01** (вариант B, если выбран) | только после решения A/B. При варианте A файл **не трогаем вообще** (только комментарии `:1437-1442` можно обновить, не обязательно). |
| `scenes/main/Main.cs` | **R2-01** (вариант A: −`ZIndex = -1`) | 1 строка, конфликтов нет. |
| `src/ui/renderers/MapModeOverlayRenderer.cs` | **R2-04**, **R2-05**, **R2-06**, **R2-07** | один класс, 4 правки: безопасный порядок R2-04 (цвет) → R2-07 (константа/цикл) → R2-06 (upload) → R2-05 (UI/мир). R2-05 в варианте «мир» переписывает `BuildOre/BuildStockpile` — делать последним, после R2-06 (общая буферизация). |
| `src/ui/renderers/GroundItemRenderer.cs` / `StockpileItemRenderer.cs` | R2-02 + R2-03 | обе правки в одном файле: сначала R2-02 (`CustomAabb`), затем R2-03 (ёмкость). Разные участки (`EnsureRenderer` создание / цикл `_Process`). |
| `src/ui/renderers/FertilityOverlayRenderer.cs`, `HumidityOverlayRenderer.cs` | R2-08 | близнецы; править вместе, иначе шов будет только в одном режиме. |
| `src/ui/PerformanceOverlay.cs` | U2-08 | изолирован. |
| `src/shaders/terrain_blend.gdshader` | W2-09 | изолирован; не совмещать с R-12 в одном коммите, чтобы регресс-скриншот был однозначным. |

## 4. Вопросы владельцу (сводно)

1. **R2-01:** A (снять −1 с корня мира, опустить землю) или B (7 слоёв абсолютным z)? Рекомендуется **A** (2 строки, единый инвариант «ровно один низ»).
2. **R2-05:** оверлеи режимов карты — «инструмент колонии» (ограничить + сообщить) или полный паритет с миром (M/L)?
3. **R2-07:** вводить ли `JobsVersion`-поколение в `JobSpatialPyramid` для событийного обновления, или бюджет 8192 + 1 Гц достаточно?
4. **R2-08:** «плодородие мира» — климатическая величина везде, или остров = почва / мир = климат с блендингом 32 тайла, или заморозить как принятое ограничение? (Общий вопрос с R2-04 и N-03 — §18.5 реестра.)
5. **R-12:** фиксированные 1024 (S) или динамическое окно по зуму (M)? Рекомендуется 1024 сейчас, динамика — отдельной задачей после R-09/перф-замеров.