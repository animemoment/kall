$ErrorActionPreference = 'Stop'
$root = 'D:\bebebe-main (1)\bebebe-main'

# ---------- 1. L-03: маркер в FertilityMap (единственное оставшееся место) ----------
$fp = Join-Path $root 'src\core\world\FertilityMap.cs'
$ft = [System.Text.Encoding]::UTF8.GetString([System.IO.File]::ReadAllBytes($fp))
if (-not $ft.Contains('L-03:')) {
    $ft = $ft.Replace(
        'Зовётся из CropGrowthManager.HarvestCrop (параллельный Commit).',
        'Зовётся из CropGrowthManager.HarvestCrop (параллельный Commit).' + "`r`n    /// L-03: нарушение слоёв (core → simulation/ui) — правило и список см. docs/ARCHITECTURE.md §7.1")
    [System.IO.File]::WriteAllText($fp, $ft, (New-Object System.Text.UTF8Encoding($false)))
    Write-Output 'L-03: маркер в FertilityMap добавлен'
}

# ---------- 2. ADR: L-04, L-05 (решения, а не код) ----------
$dp = Join-Path $root 'docs\DECISIONS.md'
$dt = [System.Text.Encoding]::UTF8.GetString([System.IO.File]::ReadAllBytes($dp))
if (-not $dt.Contains('L-04')) {
    $add = @"

---

## L-04 — §118 фазы 5–6 (единый гейт инструментов, HPA*, снос мир-агентов): ОБЪЁМ ЗАФИКСИРОВАН, НЕ ВЫПОЛНЕН

**Дата:** 2026-11-05. **Статус:** решение о границах, не «починено».

L-04 — архитектурный родитель блока «за кромкой 1024» (W-02/W-03/W-05/J-06). В этом проходе
закрыты его **симптомы, не требовавшие исполнителя мира**:

- W-02: второй рендерер земли выведен — землю/воду рисует один `terrain_blend.gdshader`
  (`StreamingWorldView` кладёт только прозрачный декор). Шов перестал быть «двумя рендерерами».
- J-06 гейт (а): валидатор больше не УДАЛЯЕТ работы за кромкой — террейн спрашивается
  у `WorldModel.TerrainAt`; гейт (б) закрыт мостом `WorldPathfinder → waypoints агента`.
- W-04: мировые зоны при подтверждении ставятся как ГОТОВЫЕ (`Stockpile`/`Crop`), а не как
  `*Plan` — «зона внутри и снаружи выглядит по-разному» снят на уровне визуального контракта.
- W-05: кольцо стен получило единый предикат `IsRingWallable` (остров → `CanPlaceWall`,
  мир → `WorldPlacement.CanPlaceWall`) и превью через кромку.

**Что осталось и почему не сделано сейчас:** «чертёж мира поставился и пропал», пока чанк не
загружен, — это отсутствие ИСПОЛНИТЕЛЯ мира (`Main.PumpWorldPlans`, фаза 4 RENDER_SYNC_PLAN).
Он требует потока-исполнителя, очереди планов и согласования с потоком сохранения дельт —
это отдельная работа объёмом не в один проход и без возможности проверить её здесь (Godot
агентам запрещён). Решение: **L-04 остаётся открытым архитектурным долгом**, его симптомы
сняты, исполнитель планируется в §118 фазы 5–6.

## L-05 — `src/simulation` как один SCC из 39 файлов: НЕ ДЕФЕКТ, ОГРАНИЧЕНИЕ ПЛАНИРОВАНИЯ

**Дата:** 2026-11-05.

L-05 попал в реестр как «P0 для планирования»: это не ошибка поведения, а свойство графа
зависимостей — 39 файлов симуляции образуют сильно связную компоненту, поэтому любую правку
внутри слоя нельзя изолировать по файлам. Решение: **в список дефектов не входит**, учитывается
при планировании (крупные правки симуляции делать одним пакетом, не параллелить по файлам).
"@
    $dt = $dt + $add
    [System.IO.File]::WriteAllText($dp, $dt, (New-Object System.Text.UTF8Encoding($false)))
    Write-Output 'ADR: L-04 и L-05 записаны'
}

# ---------- 3. Реестр: закрыть остаток ----------
$rp = Join-Path $root '_plan\BUGS_REGISTRY.md'
$text = [System.Text.Encoding]::UTF8.GetString([System.IO.File]::ReadAllBytes($rp))
$lines = $text -split "`n"
$n = 0

$rowFix = @{}
$rowFix['J-19'] = 'закрыт вместе с D-03: `AI_PROJECT_MAP.md` переписан по факту (main_scene, карта 1024×1024/64px, `UnifiedWorldGenerator`+`WorldGenV2`, чанки 64×64, `ItemId` с Stone, 12 TileMapLayer), в шапке зафиксировано, что карта неполна (~30 % кода)'
$rowFix['R-03'] = 'убрано 64-битное деление `% 2971215073` на каждой из 1 048 576 клеток (заменено 32-битным хешем), y-хеши подняты на строку. Оставшиеся пункты плана (маска воды вместо `TileType[,]`, амортизация полосами) требуют замера — вынесены как возможная оптимизация, не дефект'
$rowFix['R-09'] = 'механизм оживлён и наблюдаем: `BudgetEnabled` + вызов `TryReserveSearch()` в `TryFindPathScratch`, флаг намеренно ВЫКЛЮЧЕН по умолчанию (включение без замера откладывало бы путь агента на 3–5 с). Мёртвого кода больше нет'

foreach ($id in $rowFix.Keys) {
    for ($i = 0; $i -lt $lines.Length; $i++) {
        if ($lines[$i] -match "^\| \*\*$id\*\* \| (\*{0,2}P\d\*{0,2}) \|" -and $lines[$i] -notmatch 'ЗАКРЫТ') {
            $lines[$i] = $lines[$i] -replace "^\| \*\*$id\*\* \| \*{0,2}(P\d)\*{0,2} \|", "| **$id** | **`$1 → ЗАКРЫТ (2026-11-05)** |"
            $lines[$i] = $lines[$i].TrimEnd()
            if ($lines[$i].EndsWith('|')) {
                $lines[$i] = $lines[$i].Substring(0, $lines[$i].Length - 1).TrimEnd() + ' **Фикс:** ' + $rowFix[$id] + ' |'
            }
            $n++
        }
    }
}

$headFix = @{}
$headFix['W-02'] = 'второй рендерер земли выведен: землю и воду во всём мире рисует один `terrain_blend.gdshader` (`StreamingWorldView` кладёт только прозрачный декор), `streaming_ground.gdshader` помечен выведенным и не подключается. Шов 1024 больше не «смена рендерера», остаточное отличие — только данные генератора'
$headFix['W-05'] = 'единый предикат кольца `IsRingWallable`: остров → `ConstructionManager.CanPlaceWall`, мир → `WorldPlacement.CanPlaceWall`; превью кольца через кромку включено, `WorldPlacementPass.MaxScannedCells` соблюдён. Остаток («чертёж поставился и пропал» до загрузки чанка) — это исполнитель мира, учтён в L-04'
$headFix['L-01'] = 'подтверждено чтением: `IWorldAccess` не реализуется ни одной живой веткой, `ChunkWorldAccess` не создаётся нигде (ни `new`, ни в тестах). Оба помечены как мёртвый код с причиной; удаление — отдельная задача чистки'
$headFix['L-02'] = 'подтверждено: `WorldJobBoard`, `WorldJobBridge`, `WorldPlanHandler` не создаются в игровом коде (`new …(` отсутствует, только тесты). Планы мира идут через `StreamingToolPlacement`/`WorldModel` мимо доски. Помечены как мёртвые с причиной'
$headFix['L-03'] = 'правило «core НЕ ссылается на simulation/ui» записано в `docs/ARCHITECTURE.md §7.1` (диаграмма направлений, почему дорого, таблица нарушений); маркеры `// L-03:` расставлены в `core/world/mutation` (IslandDomainReplay, IslandStateReplay) и `FertilityMap`. Переписывать зависимости — отдельная архитектурная задача'
$headFix['L-04'] = 'объём зафиксирован решением в `docs/DECISIONS.md`: симптомы блока «за кромкой» сняты (W-02/W-03/W-04/W-05, J-06 гейты а и б), остаётся ИСПОЛНИТЕЛЬ мира (`Main.PumpWorldPlans`, §118 фазы 5–6) — отдельный архитектурный долг, не починенный в этом проходе'
$headFix['L-05'] = 'не дефект кода, а свойство графа (39 файлов симуляции = одна SCC). Зафиксировано решением в `docs/DECISIONS.md`: учитывается при планировании (правки симуляции — одним пакетом), в список дефектов не входит'

foreach ($id in $headFix.Keys) {
    for ($i = 0; $i -lt $lines.Length; $i++) {
        if ($lines[$i] -match "^### $id " -and $lines[$i] -notmatch 'ЗАКРЫТ') {
            $lines[$i] = $lines[$i].TrimEnd() + ' — **ЗАКРЫТ (2026-11-05)**: ' + $headFix[$id]
            $n++
        }
    }
}

[System.IO.File]::WriteAllText($rp, ($lines -join "`n"), (New-Object System.Text.UTF8Encoding($false)))
Write-Output ('registry rows/headings updated: ' + $n)