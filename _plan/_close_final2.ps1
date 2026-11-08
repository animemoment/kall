$ErrorActionPreference = 'Stop'
$rp = 'D:\bebebe-main (1)\bebebe-main\_plan\BUGS_REGISTRY.md'
$lines = [System.Text.Encoding]::UTF8.GetString([System.IO.File]::ReadAllBytes($rp)) -split "`n"
$n = 0

$rowFix = @{}
$rowFix['J-06'] = 'гейт (а): `JobValidator.IsTargetWalkable` для координат вне острова спрашивает `WorldModel.TerrainAt` и больше НЕ удаляет работу с 30-с кулдауном; гейт (б): путь за кромку строит `WorldPathfinder`, мост «мир → waypoints агента» уже есть; гейт (в) — watchdog оставлен штатным предохранителем (усилен J-08). Остаток — исполнитель мира, см. L-04'
$rowFix['R-04'] = 'кэш террейна мира: `IsWorldTileBlockedCached` в `AgentMovementService` (ключ — клетка, штамп seed+origin для инвалидации, потолок 256k записей). Раньше на каждую проверку вне окна колонии шёл полный `SampleCell` (тектоника/Voronoi/18 полей) — 1–5 раз на суб-степ на агента'
$rowFix['R-07'] = '`AgentLerpBatch.FillGpuInstanceConstants` пишет неизменяемый identity-трансформ (8 float из 16 на инстанс) ОДИН раз при аллокации буфера; `FillGpuInstanceBuffer(..., writeTransformConstants: false)` в кадре пишет только 8 меняющихся float — минус ~320 КБ записи на снапшот при 10k агентов'
$rowFix['R-10'] = 'закрыт тем же кэшем, что R-04 (это была «самая дешёвая часть» R-04): `ConcurrentDictionary<long,byte>` по клетке + инвалидация по seed/origin'
$rowFix['R-11'] = '`TickSoilTooltip` принимает реальную `delta` кадра (`double.IsFinite`-гард) вместо константы `1.0f/60.0f`: таймер 0.25 с больше не зависит от FPS'
$rowFix['D-01'] = 'в `AI_PROJECT_MAP.md` добавлено предупреждение: список «26 мёртвых файлов» из `_audit/cycles_after/orphans.md` недостоверен — проверено, что `JobTypeId.cs`, `JobCategory.cs`, `TileType.cs` и автолоад `mcp_interaction_server.gd` живые'
$rowFix['D-02'] = 'сетка тайлов исправлена на 64 px: `addons/live_bridge/live_bridge.gd:_tile_to_screen` (`tx*64+32`), команда `tile` в `tools/live.py` проксирует мост — раньше клик систематически уходил вдвое ближе к центру'
$rowFix['D-03'] = '`AI_PROJECT_MAP.md` переписан по факту: main_scene `menu.tscn`, карта 1024×1024 при 64 px, `UnifiedWorldGenerator`+`WorldGenV2`, чанки 64×64 по 16×16, `ItemId` включает Stone, 12 TileMapLayer; в шапке зафиксировано, что карта неполна (~30 % кода)'
$rowFix['D-04'] = '`GLOBAL_AUDIT_REPORT.md` получил шапку «НЕ ИСПОЛЬЗОВАТЬ КАК ИСТОЧНИК»: построен на 0 из 6 упомянутых тестов и 5 из 5 несуществующих файлов, опровергнут в `_audit/AUDIT-A-world.md`'
$rowFix['D-05'] = '`docs/WorldDeltaApiAudit.md`: утверждение «payload всегда пустые» исправлено — схема v1 реализована и пишется (`IslandPayload.SchemaVersion = 1`, `IslandStateSnapshot.RefreshPayloads`), null означает метку без состояния'
$rowFix['D-06'] = '`audit-cubemeter.md` помечен как аудит ЧУЖОГО проекта (`game.1t.ru`, JS-бандл Cubemeter) — к этой игре не относится'
$rowFix['D-07'] = '«оптимизации» в `AI_PROJECT_MAP.md` переформулированы как нарушения D-08 (радиусы еды 32→3 и переноски 32→4), в `docs/DECISIONS.md` добавлен раздел про замаскированные под оптимизации нарушения'
$rowFix['D-08'] = 'автопроход по `PLAN.md`: проверено 299 ссылок, 17 путей исправлено на реальные, 65 строк помечено `<!-- D-08: файл не найден -->`; в шапке — счётчик проверки'
$rowFix['D-09'] = '`PLAN.md` помечен как конгломерат трёх документов: шапка с границами частей (§99/§100, §0…§20, §21…§121) и указанием, какие разделы актуальны'
$rowFix['D-10'] = '`docs/DECISIONS.md`: записи D-12 (радиусы урезаны под перф = долг, не оптимизация) и D-13 (реальная сетка тайлов 64 px, правка в `live_bridge.gd`)'
$rowFix['D-11'] = '`AGENTS.md`: добавлены реальные каталоги (`docs/`, `_plan/`, `_audit/`, `_reports/`, `tests/`) и правило сборки/запуска (agents: `dotnet build`; Godot только через `godot_run_headless`)'
$rowFix['D-12'] = '`_reports/d12_duplicates.md`: SHA-256-сверка 5912 файлов родителя — 48 идентичны, 17 расходятся, остальное чужое (venv/node_modules); решение в `docs/DECISIONS.md` D-14: единственный источник истины — `bebebe-main\`'

$headFix = @{}
$headFix['W-04'] = 'мировые зоны при подтверждении ставятся как ГОТОВЫЕ (`Stockpile`/`Crop`), плановая метка снимается — визуальный контракт «зона внутри и снаружи 1024» один. Плюс единая точка `PaintZoneCell` в `ZoneDraftTool`'

foreach ($id in $rowFix.Keys) {
    for ($i = 0; $i -lt $lines.Length; $i++) {
        if ($lines[$i] -match "^\| \*\*$id\*\* \| \*{0,2}(P\d)\*{0,2} \|" -and $lines[$i] -notmatch 'ЗАКРЫТ') {
            $lines[$i] = $lines[$i] -replace "^\| \*\*$id\*\* \| \*{0,2}(P\d)\*{0,2} \|", "| **$id** | **`$1 → ЗАКРЫТ (2026-11-05)** |"
            $lines[$i] = $lines[$i].TrimEnd()
            if ($lines[$i].EndsWith('|')) {
                $lines[$i] = $lines[$i].Substring(0, $lines[$i].Length - 1).TrimEnd() + ' **Фикс:** ' + $rowFix[$id] + ' |'
            }
            $n++
        }
    }
}
foreach ($id in $headFix.Keys) {
    for ($i = 0; $i -lt $lines.Length; $i++) {
        if ($lines[$i] -match "^### $id " -and $lines[$i] -notmatch 'ЗАКРЫТ') {
            $lines[$i] = $lines[$i].TrimEnd() + ' — **ЗАКРЫТ (2026-11-05)**: ' + $headFix[$id]
            $n++
        }
    }
}

[System.IO.File]::WriteAllText($rp, ($lines -join "`n"), (New-Object System.Text.UTF8Encoding($false)))
Write-Output ('updated: ' + $n)