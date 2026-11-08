# FixPlan 02 — мир за кромкой 1024: размещение, гейты, исполнитель

> Волна 2026-11-07 (продолжение). Код **read-only** (`src/`, `scenes/`, `tests/`), git/Godot/сборка не трогались.
> Догма волны: «мир за кромкой должен быть паритетен острову» (`_plan/AUDIT_DOGMAS_AND_PLAN.md:11-13` — «внутри и снаружи кромки всё обязано работать неотличимо; иначе это баг рассинхронизации»).
> Источники — текущее дерево; сборка на входе зелёная (0/0).

## Шапка

| ID | P | S/M/L | Перестройка? | Одна строка |
|---|---|---|---|---|
| **U-01** | P0 | **M** | **ТРЕБУЕТ РЕШЕНИЯ** | мультитайл за кромкой ставится 1 клеткой, ПКМ не отменяет |
| **U-02** | P1 | **S** | нет (минимальный = запрет+сообщение); полный паритет — **ТРЕБУЕТ РЕШЕНИЯ** | отраслевая зона за кромкой: нет превью, молчаливый обрез |
| **U-05** | P1 | **S** | нет | стены/здания ставятся сквозь видимый декор |
| **N-03** | P1 | **M** | нет | гейты декора не знают плодородия (0 вместо сетки) |
| **S-09** | P0 | **S** (док) / **M–L** (если хранить) | **ТРЕБУЕТ РЕШЕНИЯ** | кучи предметов за кромкой не сохраняются |
| **S-10** | P0 | **S** (док) / **L** (если мир-домены) | **ТРЕБУЕТ РЕШЕНИЯ** | доменное состояние за кромкой не восстанавливается |
| **S-07** | P0 | **M** | **ТРЕБУЕТ РЕШЕНИЯ** | нет исполнителя планов мира (`PumpWorldPlans`) |
| **L-04** | P0 (арх.) | **L** | **ТРЕБУЕТ РЕШЕНИЯ** | §118 фазы 5–6 не завершены |

Порядок/зависимости — в «Волнах применения» в конце.

---

### U-01 · P0 · Мультитайловые станки за кромкой: ставится 1 клетка, ПКМ-отмена не работает
- **Файлы:** `src/ui/tools/BuildTool.cs:205-241`, `:249-269`; `src/ui/streaming/StreamingToolPlacement.cs:94-107`; `src/ui/tools/IToolWorldPlacement.cs:38-45`; `src/core/world/streaming/integration/IBlockWorld.cs:11-16`; `StreamingWorldManager.cs:142-157`; `WorldDeltaKind.cs:12-13`.
- **Минимальная правка.** Баг в двух местах, оба — «мир не знает габарита»:
  1) `BuildTool.OnRelease` для мультитайла кладёт в `_cellBuffer` **одну** клетку (`:205-210`), а мировой ветке передаёт только координаты, без типа:
  ```csharp
  // было (BuildTool.cs:205-210 и :233-239)
  if (BuildingFootprint.IsMultiTile(_buildingType)) { _cellBuffer.Add((startTile.X, startTile.Y)); }
  …
  else if (WorldPlacement != null) { … WorldPlacement.TryPlaceBuilding(x, y); }
  ```
  ```csharp
  // стало — мировая ветка передаёт ТИП; для мультитайла обходим габарит:
  else if (WorldPlacement != null)
  {
      if (!BuildingFootprint.IsMultiTile(_buildingType))
          WorldPlacement.TryPlaceBuilding(x, y);
      else if (WorldPlacement.TryPlaceBuilding(startTile.X, startTile.Y, _buildingType)); // тип: габарит регистрирует адаптер
  }
  ```
  2) `StreamingToolPlacement.TryPlaceBuilding(x, y, buildingType)` тип **игнорирует** (`:102-103`: `=> TryPlaceBuilding(cellX, cellY)`) — переписать на обход габарита с откатом:
  ```csharp
  // было (:98-103)
  /// <summary>§120.8: … Пока тип в payload не пишется — делегируем базовой ветке.</summary>
  public bool TryPlaceBuilding(long cellX, long cellY, Game.Core.BuildingType buildingType)
      => TryPlaceBuilding(cellX, cellY);
  ```
  ```csharp
  // стало (эскиз; компоненты габарита — как в BuildTool.cs:52-55)
  public bool TryPlaceBuilding(long cellX, long cellY, Game.Core.BuildingType buildingType)
  {
      var fp = Game.Core.BuildingFootprint.Of(buildingType);
      int w = Math.Max(1, fp.Width), h = Math.Max(1, fp.Height);
      if (w == 1 && h == 1) return TryPlaceBuilding(cellX, cellY);
      var placed = new List<(long X, long Y)>(w * h);
      for (int dy = 0; dy < h; dy++)
          for (int dx = 0; dx < w; dx++)
          {
              if (!_rules.CanBuildAt(cellX + dx, cellY + dy)
                  || !_manager.TryPlaceBlock(WorldDeltaKind.BuildingPlan, cellX + dx, cellY + dy))
              { foreach (var c in placed) _manager.TryRemoveBlock(WorldDeltaKind.BuildingPlan, c.X, c.Y); return false; }
              placed.Add((cellX + dx, cellY + dy));
          }
      return Apply(cellX, cellY, true);
  }
  ```
  3) ПКМ-отмена: `_cellBuffer` мультитайла — только Origin (`:205-210`), поэтому снимается одна клетка. В ветке `else` (`:249-269`) для мультитайла обойти габарит вокруг `startTile` и вызвать `TryRemoveBuilding` по каждой клетке (симметрично (2), ~8 строк).
  Замена «1 план на все клетки» → «план на каждой клетке» **бесплатно даёт занятость** (`WorldDeltaKind.BlockingKinds` включает `BuildingPlan`): второй станок/стена на габарит не встанут, отмена найдёт любую клетку.
- **Что НЕ трогаем:** `MapRenderer.GhostSourceForBuilding` (ghost рисуется цельным спрайтом W×H — это уже верно), ось `IBlockWorld.AddBlock` (payload не добавляем — см. «Перестройка»), островную ветку `ConstructionManager.StartSitesBatch` (там габарит один сайт — корректно).
- **Риск:** при «плане на каждой клетке» будущий исполнитель (S-07) построит 12 отдельных объектов вместо одного 3×4 — поэтому **нельзя включать исполнитель до решения по payload** (см. «Перестройка»). Откат: правка локальна в `BuildTool`/`StreamingToolPlacement`, снимается тремя `git diff`-хунками.
- **Проверка:** сценарий — за кромкой (например, x=1030) поставить Лесопилку 3×4: (а) после отпускания ЛКМ в мире 12 меток `BuildingPlan`, ghost показывает весь габарит, (б) ПКМ по любой из 12 клеток снимает все 12 (откат при частичном успехе), (в) второй станок на те же клетки не встаёт. Статически: `grep -n "TryPlaceBuilding(" src/ui/tools/BuildTool.cs` — тип передаётся.
- **Объём:** **M** (2 файла, ~35–45 строк).
- **Перестройка:** **ТРЕБУЕТ РЕШЕНИЯ ВЛАДЕЛЬЦА.** Вопрос: что такое «станок 3×4 в мире» — один объект или набор клеточных меток? Варианты: (а) **метки по клеткам** (правка выше; дёшево, отмена/занятость из коробки; минус — 12 задач и 12 готовых блоков, когда появится исполнитель); (б) **один план-Origin + реестр габаритов мира** (`WorldFootprintRegistry` рядом с `StreamingWorldManager`, ~60–80 строк; минус — новый стейт, который надо сохранять: в чанк-сторе payload пуст, `IBlockWorld.AddBlock` без Data); (в) **явный UI-запрет** мультитайлов за кромкой до появления payload/исполнителя (`BuildTool.CanPlaceCellAt` → `CanPlaceBuilding` запрещает при `_isMultiTile && !IsValidCoord`) — 5 строк, никакой лжи. **Рекомендация Лида:** (в) как «минимальная честная» сейчас + (б) в волну вместе с S-07.

---

### U-02 · P1 · Отраслевые зоны за кромкой: превью нет, коммит молча обрезается по 1023
- **Файлы:** `src/ui/tools/ZoneDraftTool.cs:382-393` (накопление), `:633-637` (превью), `:885-967` (`Commit`), `:976-1009` (`CommitIndustryZone`); `src/simulation/zones/ZoneManager.cs:417-422` (жёсткий гейт «центр вне карты»).
- **Минимальная правка (S, вариант «не обманывать игрока»):**
  ```csharp
  // было (ZoneDraftTool.cs:633-637)
  private void PaintWorldDragPreview(int zoneSource)
  {
      // Зона-отрасль — диск по острову; мир за кромкой она не размечает.
      if (WorldPlacement == null || IsIndustry) return;
  ```
  ```csharp
  // стало: отрасль рисуется ЗАПРЕЩАЮЩИМ цветом и не копится
  if (WorldPlacement == null) return;
  if (IsIndustry) { PaintForbiddenWorldRect(); return; } // красная рамка как у ghost-invalid
  ```
  и в `AccumulateRect`/`Commit` добавить одно условие-страж (было → стало по смыслу):
  ```csharp
  // :382  было: else if (!IsIndustry && WorldPlacement != null && WorldPlacement.IsWorldCell(x, y))
  // :885  стало: в начале Commit —
  if (IsIndustry && DraftCrossesIslandEdge())   // рамка драга выходит за [0,1024)
  {
      HUD.Toast("Зоны отраслей доступны только на территории колонии"); // существующий канал сообщений HUD
      return false;   // вместо «true и ничего не создано»
  }
  ```
  `DraftCrossesIslandEdge` — сравнение `_dragStart/_dragCurrent` c `WorldPlacementPass.IsIslandCell` по углам (метод уже есть, `ZoneDraftTool.cs:675`).
  Причина «обрезки по 1023» — не явный clamp, а **накопление только островных клеток**: `_zoneCells` пополняется лишь при `IsIslandCell` (`:377-381`), а `CommitIndustryZone` считает центр/радиус по `min/max` этого списка (`:978-998`) — мирная часть драга просто исчезает. Поэтому минимальная правка — **явно сообщить и не коммитить половину**, а не «дорисовать».
- **Что НЕ трогаем:** `ZoneManager.CreateWorkZoneAt` (радиусная work-зона — островная сущность), `CommitIndustryZone` (логика центра/радиуса остаётся), склад/ферму (их мир-ветка работает: `:940-955`).
- **Риск:** тост может дублироваться на каждый кадр драга — вешать только на `Commit`, в превью — красный ghost без текста. Откат: 2 локальные правки.
- **Проверка:** драг отраслевой зоны, полностью за кромкой → красный ghost, Commit возвращает `false` + сообщение, в `WorkZoneManager` зоны нет (раньше: «тишина»); драг через кромку → Commit отклоняется целиком, а не создаёт усечённую зону; драг целиком внутри → ведёт себя как раньше (регресс-тест `tests/` по зонам).
- **Объём:** **S** (1 файл, ~20 строк).
- **Перестройка:** нет для минимального варианта. Полный паритет (отрасль за кромкой) — **ТРЕБУЕТ РЕШЕНИЯ ВЛАДЕЛЬЦА**: `ZoneManager.CreateWorkZoneAt` отвергает центр вне `_mapW/_mapH` (`:417-422`), значит нужны «мировые Work-зоны» (новый стейт + реестр + персистентность .zones.json). Вариант Б: отрасль только на острове (документировать как правило; это и есть минимальная правка), вариант В: world-зоны как набор клеточных меток зоны (без радиусной модели).

---

### U-05 · P1 · Стены и здания за кромкой ставятся сквозь видимый декор
- **Файлы:** `src/core/world/streaming/integration/WorldGameRules.cs:244-247` (`IsReadyBase`), `:79-89` (`CanBuildAt`/`CanPlaceWallAt`), `:152-157` (`CanStockpileAt`), `:198-211` (`GetVisibleDecor`), `:220-241` (`IsArableSoil` — готовый образец).
- **Минимальная правка.** `IsReadyBase` не спрашивает декор вовсе:
  ```csharp
  // было (:244-247)
  private bool IsReadyBase(long cellX, long cellY)
      => IsLoadedAt(cellX, cellY)
          && GetCell(cellX, cellY).Terrain == BaseTerrainKind.Grass
          && !HasEntityBlocking(cellX, cellY);
  ```
  ```csharp
  // стало (ровно та же схема, что уже работает для грядки — :234-240)
  private bool IsReadyBase(long cellX, long cellY)
  {
      if (!IsLoadedAt(cellX, cellY)) return false;
      GeneratedCell cell = GetCell(cellX, cellY);
      if (cell.Terrain != BaseTerrainKind.Grass) return false;
      if (HasEntityBlocking(cellX, cellY)) return false;
      (bool hasTree, bool hasStone) = GetVisibleDecor(in cell, cellX, cellY);
      if (hasTree && !_world.HasBlock(WorldDeltaKind.CutTree, cellX, cellY)) return false;
      if (hasStone && !_world.HasBlock(WorldDeltaKind.MinedStone, cellX, cellY)) return false;
      return true;
  }
  ```
  Закрывает сразу `CanBuildAt`, `CanPlaceWallAt`, `CanStockpileAt` (все три идут через `IsReadyBase`) — т.е. паритет «сначала сруби/добудь, потом строй», как на острове. Уже вырубленное/добытое (`CutTree`/`MinedStone`) клетку разблокирует — иначе после рубки на месте дерева нельзя было бы строить.
- **Что НЕ трогаем:** `CanMineStoneAt`/`CanCutTreeAt`/`CanPlantAt` (у них декор уже учтён), `HasEntityBlocking`/`BlockingKinds`, `IsLoadedAt`.
- **Риск:** старые сейвы мира: игрок уже поставил стену «в дерево» — гейт её не увидит (он только на постановку), блок останется; лечится естественно. Драг стеной по роще станет «дырявым» (часть клеток откажется) — это **и есть паритет**, но UX изменится: mass-drag будет ставить меньше; сообщать не нужно (как на острове). Откат: одна функция.
- **Проверка:** роща за кромкой: поставить стену/дом на клетку с видимым деревом → отказ (ghost красный), после метки рубки и завершения — ставится. Статически: `grep -n "GetVisibleDecor" WorldGameRules.cs` — 4 вызова (было 3).
- **Объём:** **S** (+8 строк, 1 файл).
- **Перестройка:** нет.

---

### N-03 · P1 · Гейты декора и рендер расходятся по плодородию
- **Файлы:** `src/core/world/streaming/integration/WorldGameRules.cs:198-211` (`const ushort soilFertilityQ16 = 0`); `src/ui/streaming/StreamingWorldView.cs:1746-1749` (рендер: `needsFertility ? CellSoilFertilityQ16 : 0`), `:1656-1672` (`SoilFertilityAtCell` — формула), `:1691-1708` (кэш сетки 5×5 на чанк); `src/core/world/generation/WorldGenV2.cs:681-687` (`groveThreshold = 55 + fertility*25/65535`); `src/core/world/streaming/integration/WorldCellQuery.cs` (core-API, куда класть формулу); `src/core/world/streaming/integration/WorldDecor.cs:10` (`ForestMinQ16 = 30_000`).
- **Минимальная правка (3 шага, core без ссылки на ui):**
  1. В core (`WorldCellQuery.cs`, рядом с `SampleLatticeAbsolute`) добавить **чистую** функцию — дословный перенос формулы из `StreamingWorldView.SoilFertilityAtCell` (`:1656-1672`) + `LerpQ16`:
  ```csharp
  public static ushort SoilFertilityAtCell(ulong seed, uint ver, long cellX, long cellY)
  {
      ChunkKey key = WorldCoordinates.ChunkForTile(cellX, cellY);
      LocalCell local = WorldCoordinates.LocalForTile(cellX, cellY);
      long baseX = UnifiedWorldGenerator.WorldOriginX + key.X * (long)WorldCoordinates.ChunkSize;
      long baseY = UnifiedWorldGenerator.WorldOriginY + key.Y * (long)WorldCoordinates.ChunkSize;
      … // 4 × WorldClimateGenerator.SampleBlendedCell + LerpQ16 — как в UI-версии
  }
  ```
  2. UI-версию сделать делегирующей (одна строка тела), чтобы формула осталась ОДНА:
  ```csharp
  public static ushort SoilFertilityAtCell(ulong worldSeed, uint generatorVersion, long cellX, long cellY)
      => WorldCellQuery.SoilFertilityAtCell(worldSeed, generatorVersion, cellX, cellY);
  ```
  3. `GetVisibleDecor` — передать реальное плодородие только клеткам рощи (как рендер):
  ```csharp
  // было (:203-210)
  const ushort soilFertilityQ16 = 0;
  return WorldGenV2.DecorPass(wx, wy, in cell, soilFertilityQ16);
  // стало
  ushort fertility = cell.ForestQ16 >= WorldDecor.ForestMinQ16
      ? WorldCellQuery.SoilFertilityAtCell(_seed, _generatorVersion, cellX, cellY)
      : (ushort)0;
  return WorldGenV2.DecorPass(wx, wy, in cell, fertility);
  ```
- **Что НЕ трогаем:** `WorldGenV2.DecorPass` (единая формула — трогать нельзя, догма «одна формула»), `FertilityMap` острова (это другая сущность), `WorldClimateGenerator`.
- **Риск:** стоимость: `SoilFertilityAtCell` = 4 × `SampleBlendedCell` (дорого: 4 региона + BlendBilinear 18 полей), но вызывается **только** для клеток с `ForestQ16 ≥ 30_000` и только в гейтах (не в рендере). При массовом драге по роще (сотни клеток) это сотни × 4 семплов; если станет заметно — добавить в core 5×5-кэш на чанк (как у рендера, ~30 строк). Формула при этом побитово та же — расхождения с рендером не будет (порог рощи 55…80 совпадёт).
- **Проверка:** роща с высоким плодородием за кромкой: (а) метка рубки ставится ровно на нарисованных деревьях (раньше ~до 25 % «невидимых» клеток рощи отказывали); (б) грядка/посев не разрешаются поверх нарисованного дерева/камня; (в) сравнить `WorldCellQuery.SoilFertilityAtCell` с `StreamingWorldView.SoilFertilityAtCell` — побитово равны на 10⁴ случайных клетках (новый тест-стенд).
- **Объём:** **M** (2–3 файла, ~25–35 строк).
- **Перестройка:** нет (это ровно то, что просил реестр: «прокинуть плодородие в core без ссылки core→ui»).

---

### S-09 · P0 · Кучи предметов за кромкой не сохраняются
- **Файлы:** `src/core/world/mutation/IslandStateSnapshot.cs:41-45` (Enumerate по зоне), `:150-158` (фильтр куч); `src/core/world/mutation/IslandOverrideStore.cs:219-238` (`Enumerate(minX,maxX…)` — фильтр по прямоугольнику), `:203-213` (`TrySetPayload` — payload поддержан); `src/simulation/items/GroundItemManager.cs:16-20` (сетка **64×64 = 1024×1024**, не «мировой словарь»), `:132-137` (clamp чанк-индекса); `docs/ARCHITECTURE.md:65-69` (док: «GroundItemManager — мировой (чанки-словарь, клампов нет)» — **док устарел**).
- **Минимальная правка — сегодня её НЕТ, и это честный ответ.** Факты: (1) предметы кладёт только островной `GroundItemManager` (сетка 1024², clamp); (2) мировой исполнитель отсутствует (S-07), поэтому куч за кромкой **сегодня не возникает** (кладут только завершённые рубка/добыча); (3) островной стор перечисляется в границах зоны (`:41-45`), мировой чанк-стор хранит только блоки-метки (payload пуст по контракту `IBlockWorld`). ⇒ фильтр `:154-155` — не причина, а страховка; удалить его = писать в островной стор записи, которые нечем восстанавливать (S-10). Поэтому:
  **Минимальный вариант (S, 0 кода):** зафиксировать в `docs/ARCHITECTURE.md` §3 п.6 / §5 строку «кучи предметов за кромкой не персистятся; появятся вместе с исполнителем мира (S-07)» и в реестре перевести S-09 в «латентен до S-07». Симптом сегодня не воспроизводим.
  **Если хранить — минимальный технический вариант (M):** писать мировые кучи в **островной стор** (он keyed абсолютными координатами и умеет payload) и на загрузке спавнить их обратно: `IslandStateSnapshot` — убрать фильтр и расширить `Enumerate` на диапазон `GroundItemManager` (а не зоны), `IslandStateReplay` — снимать фильтр `:106-107` для `kind == GroundItems` и звать `GroundItemManager.SpawnItems(x, y, …)` уже в мировых координатах. Но `GroundItemManager` клампит чанк-индекс (`:136-137`) → дальняя куча попадёт в крайний бакет (найдётся запросом, но «прилипнет» к кромке в логистике) — придётся расширять сетку до мировых чанков.
- **Что НЕ трогаем:** `WorldDeltaKind.GroundItems` (0x06) в чанк-сторе — он и так есть, но payload-API у `IBlockWorld.AddBlock` нет; не вводим payload в этой волне.
- **Риск:** расширение Enumerate без расширения `GroundItemManager` = «предметы призраки» у кромки (появятся в неправильном бакете). Откат: правки в 2 местах.
- **Проверка:** (минимальный вариант) — статически: `grep -n "GroundItems" src/core/world/mutation/IslandStateSnapshot.cs` и подтвердить латентность отсутствием исполнителя; (если храним) — стенд: куча на (1030, 700), выход, вход, сравнение `GetItemsSnapshot()`.
- **Объём:** **S** (док-вариант) / **M** (островной стор + GroundItemManager) / **L** (мировой стор с payload).
- **Перестройка:** **ТРЕБУЕТ РЕШЕНИЯ ВЛАДЕЛЬЦА.** Варианты: (А) **не персистить** кучи мира до ввода мировой экономики (док + закрыть как латентный; 0 кода; минус — при появлении исполнителя ресурсы будут теряться, нужно не забыть); (Б) **островной стор + расширение `GroundItemManager` на мировые чанки** (M; плюс — один стор, payload готов; минус — «мировой» менеджер в слое simulation получает мировые координаты, нужен кламп-free бакет); (В) **мировой чанк-стор c payload** (`TrySetBlockPayload` в `IBlockWorld` + кодек `WorldChunkDeltaCodec`) — честная архитектура (L: ~150–250 строк, формат дельт меняется), зато паритет «куча живёт в чанке, как стена».

---

### S-10 · P0 · Доменное состояние за кромкой не восстанавливается при загрузке
- **Файлы:** `src/core/world/mutation/IslandStateReplay.cs:72-76` (Enumerate по зоне), `:91-94` (домен — локальные координаты), `:104-107` (payload — только в зоне); `IslandOverrideStore.cs:219-238`; `src/ui/streaming/StreamingWorldView.cs:711-754` (`ApplyReady` — **восстанавливает метки мира из чанк-стора**), `:407-459` (`CollectPendingPlans` — очередь чертежей).
- **Минимальная правка — тоже «нет», но с важным уточнением.** Реестр говорит «постройки/склады/грядки не восстанавливаются». По коду **метки** восстанавливаются: чанк-стор → `ApplyReady` → `_blocks` → рендер/гейты/`CollectPendingPlans` (`StreamingWorldView.cs:716-748`). Не восстанавливается **доменное состояние**, которого для мира не существует: содержимое мирового склада (`StockpileManager` — островной, локальные клетки), рост культуры (`CropGrowthManager`), стадия сайта (`ConstructionManager`) — все три живут только в островных координатах (`IslandStateReplay.cs:113-190`). ⇒ фильтры `:91-94,104-107` **корректны для своего стора**; убирать их = кормить островные менеджеры мировыми координатами (регресс буghunt 04-1, зафиксирован в комментарии `:78-95`).
  **Минимальный вариант (S):** ничего не менять в реплее; в реестре переформулировать S-10: «восстановление блоков — есть (чанк-стор); восстановление доменного состояния за кромкой — отсутствует вместе с исполнителем (S-07)». Добавить в чек-лист «новая фича за кромкой» (`docs/ARCHITECTURE.md` §6) пункт: «если домен мирового объекта есть — где его стор?».
- **Что НЕ трогаем:** `IslandStateReplay` доменные вызовы (Stockpile/Crop/BuildPlan/Progress) — они верны для острова.
- **Риск:** «починка» фильтров без мировой модели = фантомные записи в островных менеджерах (как в bughunt 04-1) и падения `checked((int)(wx - originX))` (`:104-105`) на дальних координатах. Откат не нужен, если не трогать.
- **Проверка:** статически — маршрут `ApplyReady` (`StreamingWorldView.cs:711-754`) + отсутствие мировых домен-менеджеров; рантайм (после S-07) — стенд «план → выполнен → выход → вход»: блоки на месте, стадия/прогресс — нет.
- **Объём:** **S** (формулировка/док) / **L** (мировые домены).
- **Перестройка:** **ТРЕБУЕТ РЕШЕНИЯ ВЛАДЕЛЬЦА.** Варианты: (А) **метки — истина**: мировые объекты не имеют доменного состояния, прогресс стройки/рубки живёт в островном сторе payload'ами по за-кромочным координатам (док §5 так и задумывал «широкий Enumerate за окном зоны» — сейчас его нет: `IslandStateSnapshot.cs:45`, `IslandStateReplay.cs:76` ограничены зоной); M: расширить Enumerate диапазоном всей планеты/`BlockCount` и в реплее пропускать за-кромочные доменные записи в менеджеры, а отдавать их помпе (S-07). (Б) **мировые домен-менеджеры** (StockpileManager-World, CropGrowth-World…) — L, новый слой стейта и сейв-формат. (В) **ограничить мир зоной**: за кромкой только стены/рубка/добыча без складов/грядок — UI-запрет (S), минус — ломает догму паритета.

---

### S-07 · P0 · Нет исполнителя планов мира (`Main.PumpWorldPlans` отсутствует)
- **Файлы:** `scenes/main/Main.cs` — **нет** ни `PumpWorldPlans`, ни подписок `CompleteWorldPlanBlock`/`OnSiteCompleted` (grep: `CompleteWorldPlanBlock|OnSiteCompleted|OnTreeChopped|OnStoneMined|TryReplaceBlock` → 0); `docs/ARCHITECTURE.md:35-76` (§3.2 — задекларированная помпа и завершения), `:78-104` (два стора); **готовый, но не подключённый каркас:** `src/ui/streaming/WorldJobBoard.cs:27-31,45-46,53-77,83-90` (`Refresh`/`TryTakeNearest`/`Complete`), `src/simulation/jobs/WorldJobBridge.cs:46-50,57-76` (`Tick`), `src/simulation/jobs/WorldPlanHandler.cs:31,45-50`; тесты уже есть: `tests/test_world_job_bridge.cs`, `tests/test_world_plan_flow_diag.cs`, `tests/TestWorldPlanCancel.cs`; `src/tools/CellInspector.cs:76,88,129` — сам честно пишет `world_executor: pending: Main.PumpWorldPlans (RENDER_SYNC_PLAN фаза 4)`.
- **Минимальная правка (M) — подключить готовый каркас, не писать новый:**
  1) поля в `Main`: `WorldJobBoard _worldBoard; WorldJobBridge _worldBridge;`
  2) в `InitStreamingWorld` **после** `_worldManager.Open` (`Main.cs:349-352`):
  ```csharp
  _worldBoard = new WorldJobBoard(_worldView, _worldManager);
  _worldBridge = new WorldJobBridge(_worldBoard, JobDispatcher.Instance);
  JobRegistry.Register(new WorldPlanHandler(_worldBoard));
  ```
  3) в `_Process` (`Main.cs:682-742`) — тик ≤ 500 мс по wall-clock (как у `_worldView.Update`, но **без привязки к камере**):
  ```csharp
  if (_worldBridge != null && Time.GetTicksMsec() - _lastWorldPumpMsec >= 500)
  { _lastWorldPumpMsec = Time.GetTicksMsec(); _worldBridge.Tick(camTileX, camTileY); }
  ```
  4) в `_ExitTree` — `_worldBridge = null; _worldBoard = null;` (очередь планов не персистится: «план без готового» и есть задача — cм. `WorldJobBoard.cs:12-16`).
  Итог: планы `WallPlan/BuildingPlan/CropPlan/StockpilePlan/CutTreePlan/MinedStonePlan` из загруженных чанков превращаются в `JobTypeId.WorldPlan`, агенты идут и по завершении `WorldJobBoard.Complete` делает `TryReplaceBlock(Plan→Done)` (`WorldJobBoard.cs:83-90`) — это и есть «исполнитель» в минимальном объёме.
- **Что НЕ трогаем:** `StreamingToolPlacement` (планы уже пишутся), `JobValidator` (J-06 гейт уже разрешает мир-цели), `WorldPathfinder`, render-слои. Не трогаем `JobSpatialPyramid` **в этой карточке** — но см. зависимость.
- **Риск (критический):** `WorldJobBridge.RegisterNewJobs` регистрирует работу с `TargetX/TargetY = plan.CellX/CellY` (long→**int** и координаты ≥1024) → сразу срабатывает **J2-01/S-08**: `JobSpatialPyramid.GetChunkIndexStatic` (`JobSpatialPyramid.cs:262-270`) не проверяет диапазон → `IndexOutOfRangeException`/алиасинг чанков. ⇒ **S-07 нельзя включать раньше фикса J2-01** (task-12, карточка `06_jobs.md`). Второй риск: `WorldPlanHandler` идёт прямым движением (`WorldJobBridge.cs:19`) — путь за кромку на макро-слое (см. `08_pathfinding.md` PF-04) не обходит широкую воду → «взял и бросил»; это не блокер подключения, но симптом.
- **Проверка:** стенд `tests/test_world_job_bridge.cs` уже покрывает план→задачу→завершение; **новый** — этап «Main подключил мост»: в игре поставить стену за кромкой → в логе `RegisteredCount > 0`, через N секунд `WallPlan→Wall` (рендер `_wallLayer`), `WorldJobBoard.LostPlans == 0`. Самое дешёвое: смотреть `CellInspector` — `world_executor` должен стать `connected`.
- **Объём:** **M** (~30–45 строк в `Main.cs`, 0 новых классов).
- **Перестройка:** **ТРЕБУЕТ РЕШЕНИЯ ВЛАДЕЛЬЦА.** Вопрос: делать ли «мир» **исполнимым** сейчас, если экономика/склады за кромкой не доменные (S-09/S-10)? Варианты: (А) **подключить мост минимально** (правка выше) — работы выполняются, метки переходят в готовое, но прогресс/предметы мира не персистятся и склады-домены не работают; это «честно видимая работа без экономики» (M, риск J2-01 в зависимости); (Б) **подключить + расширить payload/сторы** (S-09/S-10 вариант Б/В) — полный контур (L, ~3–5 карточек); (В) **явно отключить**: вместо мёртвой кнопки — UI-предупреждение «работы за кромкой появятся позже» на всех инструментах (S: 6 гейтов `WorldPlacementPass`/`IToolWorldPlacement` → `false`, но это ломает уже работающие склады/фермы/стены-планы, которые ставятся сегодня). **Рекомендация:** (А) после J2-01, с явной записью в DECISIONS о границе возможностей мира.

---

### L-04 · P0 (архитектурный) · Незавершённая миграция §118 фаз 5–6
- **Файлы:** `docs/ARCHITECTURE.md:35-76` (§3.2 — целевой контур, исполнен частично), `:78-104` (два стора — сшивка заявлена, фактически реплей ограничен зоной: `IslandStateReplay.cs:76`), `:106-117` (чек-лист «новая фича за кромкой»); `_plan/BUGS_REGISTRY.md:195-198` (L-04 «ЗАКРЫТ решением… остаётся ИСПОЛНИТЕЛЬ мира»); симметричные факты: `scenes/main/Main.cs:361` («§118 фаза 5: мир-агенты снесены»), `StreamingWorldView.cs:711-754` (метки мира живут в чанк-сторе), `src/tools/CellInspector.cs:129` (`world_executor: pending`).
- **Минимальная правка (S) — не «доделать миграцию», а зафиксировать остаток.** Сейчас L-04 в реестре закрыт «решением», но 5 из 6 пунктов чек-листа §6 `ARCHITECTURE.md` фактически не выполнены (нет помпы/завершений — S-07; прогресс мира не персистится — S-09/S-10; дубли рендера земли — R2-01; мёртвые фасады — L-01/L-02). Минимальное честное действие: в `docs/DECISIONS.md` одна запись «Мир за кромкой v2: границы» с таблицей «работает / не работает» и ссылками на S-07/S-09/S-10/R2-01, и в реестре у L-04 статус «открыт (фаза 5 — частично, фаза 6 — не начата)» с этими ссылками. Кода: 0.
- **Что НЕ трогаем:** `UnifiedWorldGenerator` (фаза 6 — снос) — он всё ещё источник формул для чанков/острова; `IslandDecorPass` удалён ранее; второй рендерер земли (W-02/W-03/R2-01) — отдельная карточка `03_render.md`.
- **Риск:** если пометить L-04 «закрытым» повторно, следующая волна снова начнёт с нуля (как эта) — поэтому нужна трассировка «L-04 → 4 открытых карточки», а не статус.
- **Проверка:** сверка §6-чек-листа `ARCHITECTURE.md` с кодом: 1) `WorldDeltaKind` — да; 2) «кейс в `Main.PumpWorldPlans`» — **нет** (grep 0); 3) гейт в хендлере — `JobValidator.cs:422-429` да; 4) подписка завершения `CompleteWorldPlanBlock` — **нет** (grep 0); 5) кейс в `IslandStateSnapshot` — **нет** (зона); 6) отмена в `StreamingToolPlacement` — да (есть `TryRemove*`). Итог 3/6.
- **Объём:** **L** (>50 строк, несколько подсистем) — но эта волна: **S** (док + статус).
- **Перестройка:** **ТРЕБУЕТ РЕШЕНИЯ ВЛАДЕЛЬЦА.** Варианты: (А) **закрыть фазу 5 в этой волне**: J2-01 (guard пирамиды) → S-07 (мост) → S-10-вариант «широкий Enumerate payload прогресса» → PF-02 (островная cost-сетка, см. `08_pathfinding.md`) — 4 связанные карточки, ~2–4 дня, риск симуляции (SCC 39 файлов, L-05); (Б) **«мир только как мастерская»**: исполнитель для стен/зданий/рубки/добычи, без складов/грядок/грузовой логистики за кромкой (2 карточки: J2-01 + S-07; S-09/S-10 — «не поддерживается» в доке) — 1–2 дня, догма A.2 нарушена явно, но граница записана; (В) **не открывать мир** (запрет инструментов за кромкой до фазы 6) — 1 день на UI-запреты и доки, минус — сломает уже работающие сегодня склады/фермы/стены-планы, потребует чистки.

---

## Волны применения

**Волна 1 — «гейты не врут» (можно параллельно, независимы друг от друга, только core/ui-гейты):**
1. **U-05** (S) — декор в `IsReadyBase`: 1 функция, закрывает стены/здания/склады на декор. **Делать первым**: он же закладывает шаблон для N-03.
2. **N-03** (M) — плодородие в core + `GetVisibleDecor`. Зависит от U-05 по смыслу (оба про `GetVisibleDecor`/декор), но файлы разные (U-05 — `IsReadyBase`, N-03 — `WorldCellQuery`+`StreamingWorldView`+`GetVisibleDecor`) — можно в один PR-пакет.
3. **U-02** (S) — запрет+сообщение для отраслевых зон за кромкой. Независим.

**Волна 2 — «размещение паритетно»:**
4. **U-01** (M) — габарит мультитайла в мире; **обязательно стикуется с решением владельца** (метки-по-клеткам vs реестр габаритов vs UI-запрет). Если владелец выбирает (в)-запрет — это 5 строк и волна не нужна.
5. **U-01-запрет (если (в))** — можно параллельно с волной 1 (тогда фикс габарита ждёт S-07).

**Волна 3 — «исполнитель мира» (последовательная, с жёсткими зависимостями):**
6. **J2-01 / S-08** (`06_jobs.md`, hunt-jobs) — guard `GetChunkIndexStatic` + не вставлять в пирамиду при `-1`. **Строго до S-07.**
7. **S-07** (M) — подключить `WorldJobBoard`/`WorldJobBridge`/`WorldPlanHandler` в `Main`. После 6.
8. **S-09** и **S-10** — после S-07 и решения владельца (варианты А/Б/В): если А (метки-истина) — M, если Б/В — L. Нельзя «починить фильтры» без мировой модели: получим фантомы в островных менеджерах.

**Волна 4 — «архитектурная честность»:**
9. **L-04** (S в этой волне: док + статус; L — если владелец выбирает (А)) — закрывает формулировки после волн 1–3, чтобы реестр не врал.

**Не в этой волне (зависимости из других отчётов):** `R2-01` (Z-слои, `03_render.md`) — без него «исполненное» за кромкой всё равно не видно; `PF-02`/`PF-04` (островная cost-сетка и макро-слой, `08_pathfinding.md`) — без них S-07 даёт «взял-бросил» за водой.