# W-track: мир, стриминг, активная область 1024

> Аналитический трек (W). Только чтение: ни один файл `src/`, `scenes/`, `resources/`,
> `project.godot`, `*.tscn`, `*.cs`, `*.cfg` не изменялся. Создан только этот отчёт.
> Ревизия: рабочее дерево `D:\bebebe-main (1)\bebebe-main` на момент анализа
> (23.10.2026). Внимание: дерево содержит **незакоммиченные** правки (`git status`
> — 20+ файлов, включая `MapRenderer.cs`, `StreamingWorldView.cs`,
> `ZoneDraftTool.cs`, `WorldGenV2.cs`); все `file:line` в отчёте относятся к
> **рабочему дереву**, не к `HEAD`. Файлы не менялись в процессе анализа
> (последние mtime — 21.10.2026).

---

## 0. Метод и инструменты

### 0.1. Обязательный граф зависимостей (`dependency_graph_visualizer\viz_cli.py`)

Все команды запускались с `--repo "D:/bebebe-main (1)/bebebe-main"`; сырые выгрузки —
`_plan\_viz_<cmd>.json`.

| Команда | Получено | Что использовано в отчёте |
|---|---|---|
| `stats` | 301 файл (238 `.cs` / 52 `.gd` / 11 `.tscn`), 2136 символов, 752 ребра, 2295 методов, **0 ошибок парсинга** | §1, §4 |
| `impact --file src/ui/streaming/StreamingWorldView.cs` | direct: `scenes/main/Main.cs`, `src/core/world/streaming/integration/IWallWorld.cs`, `src/ui/streaming/WorldJobBoard.cs`, `tests/TestDestroyerStreaming.cs`, `tests/TestWorldStoreExit.cs`, `tests/test_streamingview_decor_seam.cs`; `in_cycle: true`, `scc_size: 3` | §4.1 |
| `impact --file src/ui/renderers/MapRenderer.cs` | direct: 10 файлов; итого 21, `max_depth 3`, by_layer: ui 9 / scenes 4 / tests 7 | §4.1 |
| `impact --file src/core/world/activearea/ActiveArea.cs` | direct: 20 файлов (вся симуляция агентов, `ShadowCasterRenderer`, `DesignationRenderer`, `WorldMapOverlay`) | §4.1 |
| `impact --file src/core/world/streaming/integration/WorldGameRules.cs` | 19 файлов, цикла нет | §4.1 |
| `cycles --scope src/core/world` | `c1` = `FertilityMap` + `HumidityMap` + `MapData` + `UnifiedWorldGenerator` (размер 4); `c2` = `WorldClimateProvider` + `RegionTraits` + `WorldClimateGenerator` (3) | §4.2 |
| `cycles --scope src/ui` | `c1` размер 5: `PlayerInteractionManager` + `BuildMenuController` + `HUDController` + `BuildTool` + `SelectTool` | §4.2 |
| `layers` | 12+ нарушений «core → simulation» (`IslandDomainReplay.cs` → 6 менеджеров, до 4 ссылок; `SimEvents.cs` → `ZoneJobRouter`; `FertilityMap.cs` → `CropGrowthManager`) | §4.2 |
| `concurrency` | `MapRenderer.cs` risk **4**, 31 запись в общие поля, `volatile`×3, `thread`×2, `jobsystem`×3, `concurrent_collection`×2; `WorldChunkStreamer.cs` 21 `lock`; `WorldChunkGeneratorTiming.cs` 5 `lock`; `WorldChunkStore.cs` 4 `lock`; `WorldSaveCoordinator.cs` 2 `lock`; `WorldCellQuery.cs` jobsystem×2, risk 2 | §4.3 |
| `hotpath` | 715 «горячих» методов, 182 достижимых файла; точки входа `Main._Ready/_Process/_UnhandledInput/_ExitTree`, `ChunkWindowPlanner.Update`, `WorldChunkStreamer.Update`, `FertilityMap.Tick`, `HumidityMap.Tick`; топ по `cost_estimate` — `StreamingWorldView.AddIndices` 127440 и `ProcessEraseWork` 124790 (24 цикла, 58 аллокаций) | §4.3 |
| `coverage` | `src_files 207`, `covered 139`, **67.1 %**; вне тестов: `WorldChunkStore`, `WorldChunkCodec`, `WorldChunkDeltaCodec`, `WorldDecor`, `WorldPlan`, `RegionTraits`, `RegionKey`, `GeneratedCell`, `WorldClimateParams`, `IBlockWorld`, `IWallWorld`, `IWorldCellSource`, `PlanetaryHydrology`, `IslandStateSnapshot`, `scenes/main/Main.cs` | §4.4 |
| `dangling` | `missing_resource_refs: []`, `connections_without_method: []` — **чисто** | §4.5 |
| `deadcode` | 51 «неиспользуемый» файл, 44 orphan-типа, 9 сцен | §4.6 (с оговоркой о ложных срабатываниях) |
| `callers --symbol` | `GetCompositeGroundIndex`, `IsOccludedByColony`, `ApplyTerrainBuffer`, `RecalcWalls` → только definition; `FillTerrainWindowExternal` → 1 call-site в `tests/TerrainWindowBench.cs`; `IslandDecorPass` → 4 call-site | §4.6 |
| `mutators --state` | `_groundSurface` → 1 (`MapRenderer.cs:251`); `_terrainBuffer`, `_chunkData` → `[]`; `ZoneOriginX` → 2 чтения в `ActiveArea.cs` | §4.6 |

### 0.2. Оговорки о качестве инструмента (важно для доверия к §4)

1. **`callers/callees` считают текстовые упоминания, а не семантику.** `IslandDecorPass`
   имеет `call_site` в `src/core/world/generation/WorldGenV2.cs:105` — это **строка
   комментария**, а не вызов. Следствие: счётчики call-site завышены.
2. **`owner` метода определяется неверно для вложенных типов**: `GetCompositeGroundIndex`
   и `IsOccludedByColony` приписаны `ChunkEraseJob`, `FillTerrainWindowExternal` —
   `PendingCell`. Вывод: файл-уровень графа достоверен, метод-уровень — нет.
3. **`mutators` не видит `??=` и мутации через `Remove`/индексаторы.** `_terrainBuffer`
   (присваивается в `MapRenderer.cs:688,705,958`) и `_chunkData` (наполняется в
   `ApplyReady`) дали пустой список. Отсутствие результата ≠ отсутствие мутаций.
4. **`deadcode` даёт ложные срабатывания** на интерфейсах, статических константах и
   generic-параметрах: в списке `IslandDeltaApplicator.cs`, `WorldDecor.cs`,
   `WorldChunkCodec.cs`, `IWallWorld.cs` — все реально используются (см. §4.6).
5. Поэтому **все** утверждения ниже опираются на прямое чтение кода (`read`/`grep`),
   а граф используется как карта blast-radius и как индикатор горячих точек.

### 0.3. Ассеты (проверено попиксельно bundled-Python + Pillow)

| Файл | Факт |
|---|---|
| `ui/assets/textures/objects/tree.png` | 64×64, непрозрачных **2840 / 4096** (прозрачный фон), bbox (1,2)-(63,61), строки 2..60 |
| `tree_1.png` / `tree_2.png` | 1688 / 1718 из 4096, bbox по строкам 11..55 / 9..54 |
| `tree_3.png` | заполняет всю клетку: bbox (0,0)-(64,64), 3744 / 4096 |
| `stone.png` | 128×128, прозрачный фон (4 квадранта 64×64) |
| `water.png` | 64×64, центр (29,49,255), **0 / 4096** пикселей не проходят водный профиль `streaming_ground.gdshader:27` |
| `grass.png` | 64×64, центр (39,74,5), полностью непрозрачен |

---

## 1. Карта подсистемы

### 1.1. Две независимые системы земли (корень всех дефектов W1–W4)

Мир рисуется **двумя разными рендерерами**, переключающимися ровно на границе
активной области `[0,1024)×[0,1024)`:

| | внутри 1024 (остров) | снаружи 1024 (стриминг) |
|---|---|---|
| Нода | `MapRenderer._groundSurface` — `ColorRect` 512×512 тайлов, `ZIndex = -2` (`MapRenderer.cs:251-269`) | `StreamingWorldView._groundLayer` — `TileMapLayer` "WorldGroundAndDecor", `ZIndex = -1` (`StreamingWorldView.cs:1286-1312`, `Main.cs:236-243`) |
| Материал | `src/shaders/terrain_blend.gdshader` (213 строк) — непрерывное билинейное поле `terrain_map` 512² R8 | `src/shaders/streaming_ground.gdshader` (63 строки) + композитный атлас 30 тайлов (`StreamingWorldView.cs:1512-1573`) |
| Земля | сглаженная: `evaluate_biome()` + Hermite/билиней + FBM + «зубцы» (`terrain_blend.gdshader:73-120`) | **дискретная**: одна плитка 64×64 на клетку, вода = целая плитка (`StreamingWorldView.cs:1878-1888`) |
| Берег | `shore_curvature = 0.45`, подпиксельная ватерлиния, тень обрыва 0.68/0.84, бровка +0.07, глины у берега (`terrain_blend.gdshader:101-111,152-203`) | границы нет вообще: тест «воды» — цвет пикселя текстуры (`streaming_ground.gdshader:27`), никакой тени/бровки/сглаживания |
| Декор | спрайты в `_objectLayer` (`MapRenderer.cs:1199-1208`), z = 0 | тайлы композита «земля+дерево/камень» (`StreamingWorldView.cs:1707-1756`), **прозрачный фон** у деревьев/камней |
| Зоны/склад | `_stockpileLayer`, `SourceGrass` + `Modulate (0.65,0.15,0.9,0.45)` («фиолетовая трава») | процедурный тайл с рамкой на `_markLayer` (`StreamingWorldView.cs:1379-1400`) |
| Окно | 512 тайлов, шаг сдвига 64 (`MapRenderer.cs:43-46`), камера-центричное | чанки 64², адаптивное окно 3..16 чанков (`StreamingWorldView.cs:43-44,806-823`) |

Порядок по z (относительный, `ZIndex`): `-2` подложка-шейдер → `-1` стриминговые
тайлы и `_markLayer` → `0` островной декор/объекты. Практическое следствие:
**островной шейдер виден всегда, где нет непрозрачной стриминговой плитки**; и
наоборот — стриминговые плитки закрывают шейдер.

### 1.2. Пространства координат

- «локальные» клетки — относительно `UnifiedWorldGenerator.WorldOriginX/Y` (кратно 512,
  `WorldRegionCells = 512`, `Main.cs:22`); остров занимает локальные `[0,1024)²`.
- `WorldCellQuery.CellAt` **сам** добавляет `WorldOrigin` (`WorldCellQuery.cs:45-46`).
- `ActiveArea` хранит `ZoneOriginX/Y` (мировые) и плоские массивы `Size²`;
  геттеры `GetGround/GetBiome` принимают уже **мировые** координаты.
- Границы активной области заданы **четырьмя** независимыми константами:
  `MapRenderer.MapWidth/MapHeight = 1024` (`MapRenderer.cs:33-34`),
  `ActiveArea.Size = 1024` (`ActiveArea.cs:44`),
  `Main.ColonySizeTiles = 1024` (`Main.cs:27`, прокидывается в
  `StreamingToolPlacement` — `Main.cs:291,523`), и «неявно» —
  `ChunkWindowPlanner`/`StreamingWorldView` (через `IsOccludedByColony`).
  `BuildTool` для лога берёт `MapRenderer.MapWidth` (`BuildTool.cs:194`).
  → **FEAT-W01** (единый `ActiveAreaBounds`).

### 1.3. Поток данных земли

```
ActiveArea.Fill(seed, ver)          // ActiveArea.cs:103-156
  └─ WorldGenV2.GenerateArea(1024²) // WorldGenV2.cs:206-287  (SampleCellCore + DecorPass)
       ├─ Ground/Tree/Stone/Biome/Variant → плоские массивы
       ├─ ApplyStartClearance(r=14)  // ActiveArea.cs:164-188
       └─ Humidity/Fertility.Initialize
MapDataShim.ToMapData(area)          // копия 6 массивов, без транспонирования
  └─ MapRenderer.ExternalPendingMap → ApplyMap          // MapRenderer.cs:1107-1143
       ├─ populateGround = _groundLayer.Visible == false  →  тайлы земли острова НЕ ставятся
       └─ _objectLayer: декор всех 1024² клеток
Main.OnRegionSelected                // Main.cs:406-479
  └─ _mapRenderer.InvalidateTerrainTexture + StartAsync
MapRenderer._Process (камера)        // MapRenderer.cs:576-614
  ├─ окно ЦЕЛИКОМ внутри острова → FillIslandWindowFromMapData (синхронно, байты из mapData)
  └─ иначе → Task.Run(FillTerrainWindowExternal)   // 16-клеточная климат-сетка + билиней
StreamingWorldView.Update(visible)   // StreamingWorldView.cs:246-274
  ├─ ChunkWindowPlanner → желаемые чанки
  ├─ ApplyReady → _chunkData, PaintChunk
  └─ ProcessPaintSlices (4 мс) → PaintCell (пропуск клеток острова — :1873-1874)
```

### 1.4. Генераторы (данные непрерывны!)

`UnifiedWorldGenerator.SampleCell` и `WorldGenV2.SampleCellCore` совпадают побитово
в домене острова (единственное отличие — эвклидов `Mod100` в отрицательных квадрантах,
`WorldGenV2.cs:48-55`). Тесты подтверждают: `tests/TestWorldGenV2.cs` TEST 3
(окно 256² × 3 смещения), TEST 5 (20 случайных чанков × 8 полей), TEST 2 (тайлинг),
TEST 6 (отрицательные `wx`). **Значит шов на 1024 — не в данных, а в рендере.**

### 1.5. Что разрешено/запрещено за кромкой 1024

| Действие | Снаружи 1024 | Доказательство |
|---|---|---|
| Рубка дерева | да | `StreamingToolPlacement.cs:153-159` |
| Добыча камня | да | `:165-171` |
| Склад (`StockpilePlan`) | да | `:87-88, 115-117` |
| Ферма (`CropPlan`) | да | `:90-91, 123-125` |
| **Чертёж стены** | **нет** | `:93-102` → `IsWorldBlueprintBlocked` (`:67-68`) |
| **Чертёж постройки** | **нет** | `:131-140` |
| Авто-стены зоны | **нет** | `ZoneDraftTool.cs:369-371, 398-401` |

Гейт: `WorldPlacementPass.WorldBlueprintsSupported = false` (`WorldPlacementPass.cs:37`).

---

## 2. Дефекты

### W1. Деревья/кусты «стоят на воде»

**Класс: артефакт рендера, не ошибка генерации.** Декор физически не может родиться
на воде: и `GenerateChunk`, и `DecorPass` ставят дерево только при
`terrain == Grass` (`UnifiedWorldGenerator.cs:594-612`, `WorldGenV2.cs:489-499,503-519`),
а `WorldGenV2.cs:455-459` переводит клетку в `Water` при `Biome == DeepWater`.
Ватерлиния на острове — **подпиксельная**, маска декора — **поклеточная**, поэтому
ствол оказывается в воде.

Механика с числами (`terrain_blend.gdshader`):

- `tile_uv = p/64 − 0.5`, т.е. **f = 0.5 приходится на центр клетки** (`:73-95`);
- `fbm = get_shore_fbm(p) * shore_curvature`, где `get_shore_fbm ∈ [−0.5, +0.5]`
  (`0.62+0.28+0.10 = 1.0`), `shore_curvature = 0.45` ⇒ `fbm ∈ ±0.225` (`:100-101`);
- `teeth = (floor(noise(p*0.075)*3)/3 − 0.33) * 0.18 ∈ [−0.06, +0.12]` (`:104`);
- вода побеждает при `w_water + fbm − teeth > w_land − fbm + teeth`
  ⇒ порог `t = 0.5 − fbm + teeth` (`:106-111`);
- `s = 3f² − 2f³` (Hermite, `:78-84`), значит:
  - максимум воды: `t = 0.215` ⇒ `f ≈ 0.30` ⇒ вода покрывает клетку суши
    от строки **19** до края — **70 % клетки**;
  - максимум суши: `t = 0.845` ⇒ `f ≈ 0.76` ⇒ вода только в строках **49..63** — **24 %**.

Итог: ватерлиния гуляет в полосе ≈30 px (±15 px) вокруг центра клетки. Спрайт
дерева непрозрачен в строках 2..60 (см. §0.3) ⇒ у берега **более половины деревьев
визуально стоят в воде**, а «кусты» — это те же деревья с `tree_1/tree_2`
(прозрачные края: 1688 и 1718 из 4096 непрозрачных пикселей), у которых вода
просвечивает сквозь крону.

В стриминге тот же эффект **сильнее**: тайл «дерево» собран через
`Image.CreateEmpty` + `BlendRect` (`StreamingWorldView.cs:1519,1534-1563`), т.е. фон
**прозрачный**; под ним — шейдер `terrain_blend` (z = −2), который продолжает
рисовать сглаженную воду вплоть до 1024 и дальше. Поэтому вода видна **сквозь**
спрайт (комментарий в коде прямо фиксирует, что раньше «деревья висели на синем
фоне», `StreamingWorldView.cs:1878-1885`).

Дополнительный, но **мёртвый** канал: `GetCompositeGroundIndex` при
`Terrain == Grass && Biome == DeepWater` возвращает `CompositeWater`
(`StreamingWorldView.cs:1710-1712`), тогда как остров рисует на такой клетке траву
и дерево (`MapRenderer.cs:1179-1207`). В данных эта комбинация недостижима
(`WorldGenV2.cs:455`), т.е. ветка защитная; в отчёт включена как **латентное
расхождение контрактов** (FEAT-W04), не как причина W1.

**Направление исправления (не реализовывать в этом треке):**
1. маска воды должна быть **общей** для рендера и декора: либо декор получает
   сглаженную маску («не ставить дерево, если `evaluate_biome(центр ± 0.2 клетки)`
   == вода»), либо шейдер перестаёт «залезать» в клетку суши
   (`shore_curvature` → 0.15 и/или `t` прижать к 0.5);
2. порог «воды» в шейдере привести к порогу данных: сейчас `w_water > 0.05` —
   фиктивный гейт (`:109`), реально работает только `t`;
3. для стриминга — не полагаться на прозрачный фон под деревом: рисовать под
   декором непрозрачную землю (см. FEAT-W02).

---

### W2. Шов/«недополоса» на границе 1024 — вода пролезает между островом и стримингом

**Класс: структурный (переключение рендерера), не ошибка данных.**

Доказанная часть:

1. Данные непрерывны (§1.4), значит любой видимый разрыв на 1024 — рендер.
2. Ровно на `x/y = 1024` происходит **смена рендерера**: клетки `[0,1024)` рисует
   `MapRenderer._groundSurface` (z = −2) из `mapData`, клетки `≥1024` —
   `StreamingWorldView._groundLayer` (z = −1). Стриминг **сознательно не рисует**
   клетки острова: `PaintCell`, `StreamingWorldView.cs:1873-1874`,
   и не планирует чанки, полностью лежащие в острове: `IsOccludedByColony`,
   `:960-976, 978-982`.
3. Обе ветки считают одну и ту же клетку по-разному:
   - вода в шейдере — **кривая** с подпиксельной ватерлинией (W1, ±15 px);
   - вода в стриминге — **целая плитка 64×64** (`streaming_ground.gdshader:27,30-59`).
   ⇒ вдоль всей линии 1024 ватерлиния скачком смещается на величину от 0 до
   ±0.5 тайла. Если берег/река пересекает кромку, Вы видите именно
   «недополосу, в которой вода пролезает»: со стороны острова вода сглажена и
   срезана, со стороны стриминга — стоит квадратом 64×64.
4. Вторая составляющая шва — **декор**: внутри острова деревья рисует
   `_objectLayer` (z = 0, полупрозрачные спрайты над сглаженной землёй), снаружи —
   тайлы композита с прозрачным фоном над той же сглаженной землёй. Плотность и
   вид леса меняются на линии, даже если данные совпадают (формулы совпадают
   побитово — `IslandDecorPass` / `DecorPass`).
5. Третья составляющая — **текстура песка**: шейдер тайлит `tex_sand` с периодом
   128 px (`fract(wp/128)`, `terrain_blend.gdshader:127`), стриминг — регионом
   64×64 (`GetCompositeGroundIndex` → `CompositeSand`), т.е. рисунок песка
   масштабируется ×2 на кромке.

Не доказано без запуска (см. §6 «Требует проверки в рантайме»):
наблюдаемая «полоса» может быть **и** транзиентной: непокрашенные клетки чанка
показывают шейдер (z = −2), который в этой зоне рисует сглаженную воду; при
бюджете покраски 4 мс/кадр (`StreamingWorldView.cs:984-1034`,
`MaxCellsPerPaintSlice = 4096`) полоса исчезает через кадры. Различить два
механизма можно только скриншотом в момент наблюдения.

**Подтверждение из самого кода** — ровно этот класс артефакта уже был и
задокументирован разработчиками (`StreamingWorldView.cs:1878-1884`, перевод):
> «ИСПРАВЛЕНИЕ „за кромкой острова пропал мир": раньше базовую землю
> (вода/трава/песок/степь/лёд) НЕ рисовали тайлом — её показывал шейдер
> GroundSurface. Но шейдер наполняется только по своему follow-окну (и внутри
> карты колонии), поэтому сразу за кромкой 1024×1024 окно пустое → шейдер отдаёт
> „воду", а декор (деревья/камни) при этом рисовался — деревья висели на синем
> фоне, а земля „пропадала после прогрузки"».

Отсюда следует важное для W2/W1: **подложка-шейдер не является источником истины
за кромкой**, и любая щель в стриминговых плитках (непокрашенный чанк, прозрачный
фон спрайта, задержка покраски) немедленно проявляется как «вода/синий фон».

**Направление исправления:**
- либо **один** рендерер земли на весь мир (стриминговые чанки рисуют и остров,
  а шейдер-окно остаётся только «нижним слоем» без права показываться в стриминге —
  сейчас он показывается, потому что прозрачен под декором);
- либо общий «контракт берега»: обе ветки обязаны выдавать одинаковую геометрию
  ватерлинии (например, шейдер получает ту же дискретизацию, что и чанки, а
  сглаживание остаётся только как затенение внутри плитки);
- в любом случае — блокировать показ шейдера там, где стриминг обязан рисовать
  (нет данных → рисовать заглушку, а не подложку).

---

### W3. Сглаживание воды потеряно в стриминге (жёсткие ступени за 1024)

**Причина установлена точно: у стримингового шейдера нет ни одного механизма
сглаживания.**

| Возможность | `terrain_blend.gdshader` (остров) | `streaming_ground.gdshader` (стриминг) |
|---|---|---|
| `terrain_map` / `map_size` / `map_origin` | есть (`:4-12`) | **нет** |
| `evaluate_biome` (билиней + smoothstep) | есть (`:73-120`) | **нет** |
| FBM берега / `shore_curvature` | есть (`:100-101`) | **нет** |
| «зубцы» `teeth` | есть (`:104`) | **нет** |
| определение воды | по взвешенному потенциалу клеток (`:106-111`) | **по цвету пикселя текстуры**: `b > 0.45 && r < 0.35 && g < 0.60` (`:27`) |
| тень берега 0.68 / 0.84 | есть (`:152-163`) | **нет** |
| бровка травы +0.07, срез грунта | есть (`:191-203`) | **нет** |
| глиттеры у берега | только если рядом нет суши (`:164-188`) | **всегда**, без учёта берега |

Следствие: за кромкой 1024 вода имеет **пиксельные 64-px ступени**, а внутри —
плавную кромку с тенью и бровкой. Это не «баг в одном месте», а свойство второго
шейдера: 63 строки против 213.

Проверено, что вода не «подменяется» текстурой: все 4096 пикселей `water.png`
проходят профиль `is_water` (§0.3), значит перекраска срабатывает на всей плитке
и граница получается строго по границе тайла.

**Направление:** перенести в `streaming_ground.gdshader` хотя бы
`evaluate_biome` поверх `terrain_map` (он уже есть на CPU-стороне — 16-клеточная
сетка в `MapRenderer.FillTerrainWindowExternal`), либо переключить стриминг на
тот же шейдер, что и остров, с `map_origin`/`map_size` на чанк.

---

### W4. Зоны выглядят по-разному внутри и снаружи 1024

**Четыре независимых канала различия** (все проверены по коду):

1. **Тайл зоны.** Остров: `_stockpileLayer` + `SourceGrass` с
   `Modulate = (0.65f, 0.15f, 0.9f, 0.45f)` (`MapRenderer.cs:323-329` — modulate
   на :326; постановка клеток `SourceGrass` на :397-400) — полупрозрачная
   фиолетовая трава **без рамки**. Стриминг: процедурный тайл с **рамкой**
   `(0.65,0.15,0.9,0.5)` + `(0.85,0.35,1.0,0.9)`
   (`StreamingWorldView.cs:1379-1400`) на слое `_markLayer`.
   Ирония: ровно такой bordered-тайл на острове — это **ghost**
   (`AddGhostStockpileTile`, `MapRenderer.cs:1654-1673`, заливка 0.55 / рамка 0.95),
   т.е. подтверждённая зона снаружи выглядит как **превью** зоны внутри.
2. **Слой и z.** Остров — `_stockpileLayer`, ребёнок `MapRenderer` (эффективный
   z = 0), добавлен **до** `_objectLayer` (`MapRenderer.cs:329` против `:331`),
   т.е. тонировка лежит **под** деревьями. Стриминг — `_markLayer`, ребёнок
   `StreamingWorldView` с `ZIndex = -1` (`Main.cs`), значит эффективный z = −1,
   и тайл зоны рисуется **над** землёй чанка (`_groundLayer` добавлен раньше,
   `StreamingWorldView.cs:1311-1312`). На кромке 1024 z тайла зоны скачком
   меняется на +1, а соседи-объекты оказываются то сверху, то снизу.
3. **Подсветка/ghost.** Остров проходит `ZoneDraftTool.CanPlaceZone`
   (`ZoneDraftTool.cs:599-614`, гейт по биому `Desert`), стриминг —
   `CanPlaceStockpile → _rules.CanStockpileAt` (`StreamingToolPlacement.cs:87-88`),
   а ghost-рамка зоны дополнительно гейтится `CanCommitZoneCell`
   (`ZoneDraftTool.cs:428-440`). Разные предикаты ⇒ в одном случае превью
   зелёное, в другом молча красное/серое.
4. **Авто-стены** — см. W5: внутри зона обводится стеной, снаружи нет.

**Направление:** один рендерер «маркеров зон» для обеих областей (общий слой,
общий тайлсет, общий modulate), а `CanPlaceZone`/`CanStockpileAt` свести к одному
предикату (сейчас их два: `ZoneDraftTool.cs:599` и `WorldGameRules.cs:IsReadyBase`).

---

### W5. Авто-стены вокруг зоны не ставятся снаружи 1024

**Механизм — три звена, каждое подтверждено кодом:**

```
ZoneDraftTool.RecalcWalls                      // ZoneDraftTool.cs:336-405
 ├─ островные соседи  → CanPlaceWall(x,y)      // :360  (mapData-гейт, работает)
 │    └─ commit → ConstructionManager.StartSitesBatch → стены встают
 └─ мировые соседи   → WorldPlacement.CanPlaceWall(x,y)   // :369-371
      └─ StreamingToolPlacement.CanPlaceWall  // StreamingToolPlacement.cs:108-109
           = !IsWorldBlueprintBlocked(cell) && _rules.CanPlaceWallAt(cell)
             IsWorldBlueprintBlocked = !WorldBlueprintsSupported && !IsIslandCell
                                     = true && true            // :67-68
           → false на ЛЮБОЙ клетке вне [0,1024)
      ⇒ кольцо `_worldWallCells` (ZoneDraftTool.cs:375-404) остаётся пустым
      ⇒ после commit у зоны снаружи нет ни стен, ни чертежей
```

Корневой выключатель: `WorldPlacementPass.WorldBlueprintsSupported = false`
(`WorldPlacementPass.cs:37`) с обоснованием в шапке файла — у мировых планов нет
исполнителя, а видимость зависела от резидентности чанка (план «появлялся и
исчезал»). То есть W5 — **следствие сознательного решения**, а не забытой ветки.
Отдельно: даже при `true` запись требует резидентного чанка
(`StreamingWorldView.AddBlock` → `_chunkData.ContainsKey`, `:282-283`;
`WorldGameRules.IsLoadedAt`), поэтому просто «включить флаг» нельзя — стены
снаружи начнут исчезать при панорамировании.

**Направление:** зона и её стены должны жить в одном месте — либо зона снаружи
не обводится стенами **и это явно сказано игроку** (сейчас молча), либо
`WallPlan` за кромкой получает персистентного исполнителя (тот же
`StreamingWorldManager`-канал, что и `CutTreePlan`, который снаружи работает —
`StreamingToolPlacement.cs:153-159`).

---

### W6 (сверх списка). Тесты шва проверяют устаревшую границу 512

Автоматической проверки шва **на 1024 нет**. Все существующие тесты закреплены на
старой границе 512:

- `tests/test_streamingview_decor_seam.cs:112` — `GenerateColonyWindow(seed,ver,0,0,512,512)`,
  затем сравниваются чанки X=6,7 у `x=512`; при острове 1024 это **внутренние**
  клетки (шапка файла `:18-22` прямо говорит «остров 512²»);
- `tests/TestDestroyerWorldGen.cs:211-275` — `TestColonyEdgeVsStreamingSeam`
  аудирует пару `511|512` (`:231-240`) и «обрыв высоты» на этой кромке;
- `tests/TestDestroyerStreaming.cs:222-234` — та же пара `511|512`, уже по данным
  чанков (`TryCellInfo(511,y)` vs `(512,y)`);
- `tests/TestWorldGenSeamAudit.cs:65-68` — переход `511/512`;
  `:170,177` — берёт `MapRenderer.MapWidth / 2` (= 512) вместо
  `TerrainWindowTiles / 2` (= 256), т.е. рассинхронизирован с продуктовым окном;
- `tests/TestAdversarialConstruction.cs:212-220` — проверяет
  `IsIslandCell(511,511) == true`, что при острове 1024 стало **серединой**
  острова: тест проходит, но угол кромки больше не покрывает.

Плюс: `UnifiedWorldGenerator.GenerateColonyWindow` в продукте **больше не
вызывается** — остров приходит из `ActiveArea.Fill → WorldGenV2.GenerateArea`
(`ActiveArea.cs:103-156`, `Main.cs:406-479`); `GenerateColonyWindow` остался только
в тестах и в autogen-пути `MapRenderer`. Значит «паритет окна и чанков»,
на который ссылаются тесты, — про legacy-путь.
→ **FEAT-W03** (см. §3).

---

## 3. Отсутствующие функции/инструменты

| ID | Чего нет | Зачем | Объём |
|---|---|---|---|
| **FEAT-W01** | Единый источник границ активной области (`ActiveAreaBounds`) вместо 4 констант (`MapRenderer.cs:33-34`, `ActiveArea.cs:44`, `Main.cs:27`, `BuildTool.cs:194`, `StreamingToolPlacement` ctor) | сейчас смена 1024→512 требует правки 4+ мест; уже есть рассинхрон в тестах (W6) | S |
| **FEAT-W02** | Контракт «кто рисует землю» для клетки: `IGroundRenderer` с `bool Handles(cell)`; подложка-шейдер обязана быть скрыта там, где рисует стриминг (или наоборот) | убивает W1/W2/W3 в корне: сейчас два рендерера рисуют одну землю по-разному и оба «правы» | M |
| **FEAT-W03** | Тест шва на **фактической** границе 1024: (а) байты терраин-окна vs `GeneratedCell.Terrain` по обе стороны кромки; (б) `IslandDecorPass` vs `DecorPass` в полосе 1020..1028; (в) скриншот-эталон полосы. + починить 3 теста из W6 | без него любой регресс шва невидим | S |
| **FEAT-W04** | Контракт «данные ↔ рендер»: `Terrain`→пиксели и `Biome`→плитка в одном месте. Сейчас: `MapRenderer.TerrainByteFromMapData` (0/1/2), `GetCompositeGroundIndex` (30 тайлов + мёртвая ветка `Grass+DeepWater`), `streaming_ground` (профиль по цвету) — три разные правды | W3/W4 + латентный баг из W1 | M |
| **FEAT-W05** | Рантайм-диагностика шва: оверлей «cell(x,y): terrain=… biome=… renderer=island/stream/chunk(-)` + счётчик непокрашенных клеток в видимой области | W2 нельзя закрыть без наблюдаемости: сейчас нет способа отличить «нет данных» от «шейдер пролез» | S |
| **FEAT-W06** | Персистентный исполнитель `WallPlan`/`BuildingPlan` вне 1024 (или явный UI-запрет зон со стенами за кромкой) | W5; сейчас игрок получает молчаливый отказ | L |
| **FEAT-W07** | Публичный API `ActiveArea` без «магического» `GetGround → Water` вне зоны (`ActiveArea.cs:207-213`) — метод используется только тестами, в продукте мёртв; при случайном использовании даст «воду» за кромкой | латентный источник W1-подобных симптомов | XS |
| **FEAT-W08** | Покрытие тестами хранилища чанков: `WorldChunkStore/Codec/DeltaCodec` — 0 тестов (см. §4.4) | мир бесконечен, persistence — слепая зона | M |

---

## 4. Граф: риски

### 4.1. Blast radius (по `impact`)

| Файл | direct | итого | Комментарий |
|---|---|---|---|
| `ActiveArea.cs` | **20** | ≥ 40 (вывод `impact` обрезан на `WallBuildManager`) | тянут **вся симуляция агентов** (`AgentSimulationThread`, `AgentDataPool`, `FlowFieldManager`, `HierarchicalPathfinder`, `SimdNeedsBatch`), `ShadowCasterRenderer`, `DesignationRenderer`, `WorldMapOverlay`. Любая правка структуры зоны = риск для симуляции |
| `MapRenderer.cs` | 10 | 21 (`ui 9 / scenes 4 / tests 7`) | тянут `StreamingWorldView`, `BuildTool`, `ZoneDraftTool`, `HUDController`, `MainMenuController` |
| `StreamingWorldView.cs` | 6 | — | **`in_cycle: true`, `scc_size: 3`** (Main.cs ↔ IWallWorld ↔ WorldJobBoard) |
| `WorldGameRules.cs` | — | 19 | цикла нет; относительно безопасная точка расширения |

**Год-объекты:** `MapRenderer.cs` (1823 строки), `StreamingWorldView.cs` (1993
строки, 84 КБ), `Main.cs` (762), `ZoneDraftTool.cs` (822), `AgentSimulationThread`
(`fan_out` 32), `Main.cs` (`fan_out` 26). Для W-трека критичны первые два: в них
одновременно рендер, гейты размещения, ввод, персистентность и планирование.

### 4.2. Циклы и слои

- `src/core/world`: `FertilityMap ↔ HumidityMap ↔ MapData ↔ UnifiedWorldGenerator`
  (размер 4) — исторический цикл «данные карты ↔ климат».
- `src/ui`: `PlayerInteractionManager ↔ BuildMenuController ↔ HUDController ↔ BuildTool ↔ SelectTool`
  (5) — ввод/UI.
- `layers`: 12+ нарушений **core → simulation** (`IslandDomainReplay.cs` → 6
  менеджеров, до 4 ссылок на файл). Для W-трека это значит: `core/world/*` нельзя
  считать «чистым» слоем — при рефакторинге границ области он потянет simulation.

### 4.3. Гонки и горячий путь

- `MapRenderer.cs` — **risk 4**: 31 запись в общие поля, 3 `volatile`
  (`_terrainUpdateInProgress`, `_pendingReadyBuffer`, …), `Task.Run` +
  `Parallel.For` по окну 512² (`TerrainFillMaxDop = max(2, cores/4)`,
  `MapRenderer.cs:662-666`), обмен буферами front/back (`:642-655, 756-777, 948-974`).
  Это самое гоночно-опасное место W-подсистемы: любое изменение формата
  `terrain_map` (например, «передать в стриминг ту же текстуру» для W3) обязано
  сохранить инвариант «front не пишется из фонового потока».
- `WorldChunkStreamer.cs` — 21 `lock`; `WorldChunkGeneratorTiming` — 5;
  `WorldChunkStore` — 4; `WorldSaveCoordinator` — 2. Модель: рабочие потоки
  генерят чанки, главный поток применяет (`ApplyReady`) и красит слайсами.
- `hotpath`: топ по `cost_estimate` — `StreamingWorldView.AddIndices` (127440) и
  `ProcessEraseWork` (124790), у обоих 24 цикла и 58 аллокаций; атрибуция
  подозрительна (см. §0.2 п.2), но зона `ChunkEraseJob` — реально горячая.

### 4.4. Покрытие

67.1 % (139 / 207). Для W-трека **не покрыто** главное хранилище мира:
`WorldChunkStore`, `WorldChunkCodec`, `WorldChunkDeltaCodec`, `WorldSaveMetadata`,
а также `WorldDecor`, `WorldPlan`, `RegionKey`, `RegionTraits`, `GeneratedCell`,
`WorldClimateParams`, `IWallWorld/IBlockWorld/IWorldCellSource`,
`PlanetaryHydrology`, `scenes/main/Main.cs`. Тесты шва существуют, но про 512 (W6).

### 4.5. Висячие ссылки

`dangling`: `missing_resource_refs: []`, `connections_without_method: []` — чисто.
Ресурсных «дыр» в сценах/шейдерах нет, т.е. W1–W4 не из-за незагруженной текстуры.

### 4.6. Ложные срабатывания `deadcode`/`callers` (не считать дефектами)

`deadcode` перечисляет `IslandDeltaApplicator.cs`, `WorldDecor.cs`,
`WorldChunkCodec.cs`, `IWallWorld.cs`, `IBlockWorld.cs`, `IWorldCellSource.cs`,
`RegionKey.cs` — все используются (например, `WorldDecor` читается константами
`ForestDecorMin`/`StoneDecorMin` в `StreamingWorldView.cs:1265-1266` и далее на
`:1594,1605`; `IslandDeltaApplicator` — в `Main.OnRegionSelected`).
Причина: инструмент не разрешает обращения через интерфейсы, статические
константы и generic-параметры. Аналогично `callers` посчитал комментарий
(`WorldGenV2.cs:105`) вызовом `IslandDecorPass`.

---

## 5. Оценка работ и порядок

Принцип: сначала **наблюдаемость** (без неё W2 не закрыть), потом **корень**
(один рендерер земли), потом следствия.

| Шаг | Работа | Пункты | Оценка | Риск |
|---|---|---|---|---|
| 1 | FEAT-W05: рантайм-оверлей «кто рисует клетку» + счётчик непокрашенных | W2 | **S** (0.5–1 д) | низкий |
| 2 | FEAT-W03: тест шва на 1024 + починка 3 тестов на 512 | W6 | **S** (1 д) | низкий |
| 3 | Зафиксировать W2 по оверлею: постоянный шов или транзиентная непокраска | W2 | **XS** (0.5 д) | — |
| 4 | FEAT-W02: контракт «кто рисует землю»; подложку-шейдер скрыть там, где рисует стриминг | W2, W3, W1 | **M** (3–5 д) | **высокий**: z-порядок и `volatile`-инварианты `MapRenderer` (§4.3) |
| 5 | FEAT-W04: одна таблица «Terrain/Biome → пиксели/плитка»; убрать ветку `Grass+DeepWater` | W1, W3, W4 | **M** (2–3 д) | средний (нужны скриншот-эталоны) |
| 6 | W4: общий слой маркеров зон + один предикат `CanPlaceZone` | W4 | **M** (2–3 д) | средний (`ZoneDraftTool` 822 строки, цикл ui) |
| 7 | W5: либо явный UI-запрет зон-со-стенами за кромкой, либо FEAT-W06 (персистентный исполнитель) | W5 | **S** / **L** | высокий для L (персистентность вне окна) |
| 8 | W1 финально: согласовать маску воды и маску декора (`shore_curvature`/`teeth` vs «центр клетки») | W1 | **S** после шага 5 | низкий |
| 9 | FEAT-W01: `ActiveAreaBounds` | гигиена | **S** | низкий |
| 10 | FEAT-W08: тесты хранилища чанков | риск | **M** | низкий |

**Порядок обязателен для 1→3→4**: шаг 4 без шага 1 нельзя проверить, а шаг 4
меняет z-порядок — единственное место, где W-трек пересекается с R-треком
(рендер).

---

## 6. Требует проверки в рантайме

1. **W2**: скриншот полосы на линии 1024 при пересечении берега/реки. Отличить
   постоянный шов от транзиентной непокраски: `_paintedChunks` — приватное поле,
   из GDScript/`get_property` его не достать, поэтому нужен либо счётчик-геттер
   (FEAT-W05), либо наблюдение во времени — снять 2–3 скриншота через ~0.5 с:
   постоянный шов не меняется, транзиентная непокраска «дозаливается».
   Вспомогательно: `get_ui_elements`/`get_scene_tree` показывают, что
   `GroundSurface` (512 тайлов, шаг 64) действительно накрывает полосу.
2. **W1**: zoom-in на берег внутри 1024 и сразу за 1024. Ожидание по расчёту
   §W1: внутри — строй деревьев «по колено» в воде, снаружи — целые плитки воды
   под прозрачным спрайтом.
3. **W3**: один и тот же берег по обе стороны кромки на zoom 1.0 — ступени 64 px
   снаружи против кривой внутри.
4. **W5**: чертёж зоны, пересекающей 1024: подтвердить, что за кромкой нет ни
   стен, ни чертежей, и что сообщение об отказе показывается один раз в секунду
   (`StreamingToolPlacement.RefuseWorldBlueprint`, `:74-85`).
5. **W6**: прогнать `TestDestroyerWorldGen`/`TestWorldGenSeamAudit`/
   `test_streamingview_decor_seam` — зелёные они или уже нет (они про 512).

---

## 7. Передача в J-track и R-track

### 7.1. J-track (строительство/зоны) — «строительство вне активной области недоступно»

Точная цепочка (для правки):

| Звено | Файл:строка |
|---|---|
| Политика «мировые чертежи запрещены» | `src/ui/tools/WorldPlacementPass.cs:37` (`WorldBlueprintsSupported = false`) |
| Гейт клетки | `src/ui/streaming/StreamingToolPlacement.cs:67-68` (`IsWorldBlueprintBlocked`), `:174-175` (`IsIslandCell`, `[0,_islandWidth)`) |
| Чертёж стены | `StreamingToolPlacement.cs:93-102` (`TryPlaceWall`), `:108-109` (`CanPlaceWall`) |
| Чертёж постройки | `StreamingToolPlacement.cs:131-147` |
| Точка ввода (кисть) | `src/ui/tools/BuildTool.cs:167-197` — вне острова вызывает `WorldPlacement.TryPlace*`, при отказе `HUDController.ShowZoneHint` + лог `[BUILD] вне активной области {MapWidth}×{MapHeight}` (`:194`) |
| Текст игроку | `StreamingToolPlacement.cs:81-84` («нет исполнителя; рубка/добыча/склад/ферма работают») |
| Почему нельзя просто включить | запись требует резидентного чанка: `StreamingWorldView.cs:282-283` (`_chunkData.ContainsKey`), `WorldGameRules.IsLoadedAt`; видимость зависит от окна чанков (`:960-976`) |

**Ключевой вывод для J:** вне 1024 работают рубка/добыча/склад/ферма
(`StreamingToolPlacement.cs:87-91,115-171`), не работают **стены и постройки**.
Это политика, а не регресс — но UI-сообщение об этом показывается только при
попытке и не объясняет, что авто-стены зоны тоже не встанут (W5).

### 7.2. R-track (рендер) — авто-стены и вид кромки

| Что | Файл:строка |
|---|---|
| Два рендерера земли, переключение на 1024 | `MapRenderer.cs:251-269` (z = −2) / `StreamingWorldView.cs:1286-1312` (z = −1) |
| Стриминг не рисует остров | `StreamingWorldView.cs:1873-1874` (`PaintCell`), `:978-982` (`IsOccludedByColony`) |
| Сглаживание есть только у острова | `terrain_blend.gdshader:73-120,152-203`; у стриминга — только перекраска по цвету `streaming_ground.gdshader:27-59` |
| Прозрачный фон деревьев/камней в стриминге | `StreamingWorldView.cs:1519` (`Image.CreateEmpty`), `:1534-1563` (`BlendRect`) |
| Остров: декор поверх сглаженной земли | `MapRenderer.cs:1199-1208` |
| Разный тайл зоны | `MapRenderer.cs:323-329,397-400` vs `StreamingWorldView.cs:1379-1400` |
| Авто-стены зоны (их отсутствие) | `ZoneDraftTool.cs:369-371,398-401` |

---

## 8. Вопросы к лиду

1. **Приоритет W2.** Нужен скриншот-подтверждённый диагноз (шаги 1–3 §5) или
   закрываем «по структуре», приняв, что шов = смена рендерера, и сразу идём в
   FEAT-W02? Второй путь дешевле, но если полоса окажется транзиентной
   (непокраска), FEAT-W02 её не вылечит — лечить надо бюджет покраски.
2. **Куда целимся по W3**: переносить `evaluate_biome` в `streaming_ground`
   (дешево, но два шейдера живут дальше) или **один** рендерер земли на мир
   (FEAT-W02, дорого, но закрывает W1+W2+W3 разом)?
3. **W5 — продуктовое решение.** Зона снаружи: (а) обводить стенами (нужен
   персистентный исполнитель, FEAT-W06, иначе стены исчезнут при панорамировании),
   или (б) запретить зоны со стенами за кромкой с явным сообщением? Сейчас
   молчаливый отказ — худший из вариантов.
4. **1024 — финальная константа?** Если планируется возврат к 512 (комментарий
   `Main.cs:23-27` называет 1024 «единой точкой отката»), то FEAT-W01 обязателен
   до любых правок рендера, иначе снова разъедется 4 константы + 3 теста.
5. **Кто владеет шейдерами** (`src/shaders/*.gdshader`) — W-трек или R-track?
   Правки W1/W3 лежат именно там, и без согласования мы получим конфликт правок.
6. **Рантайм-проверки** (§6) — их делает лид единым прогоном или выделяем
   отдельного исполнителя? Без них W2 остаётся гипотезой с двумя ветками.
7. **`GenerateColonyWindow`**: он больше не участвует в продукте (остров =
   `ActiveArea.Fill`). Помечаем `[Obsolete]`/удаляем после перевода тестов на
   `WorldGenV2` (FEAT-W03) или держим как «fallback» для autogen-пути
   `MapRenderer`?