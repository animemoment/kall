$ErrorActionPreference = 'Stop'
$root = 'D:\bebebe-main (1)\bebebe-main'
function Read-Md($rel) {
    [System.Text.Encoding]::UTF8.GetString([System.IO.File]::ReadAllBytes((Join-Path $root $rel))) -split "`n"
}

$out = New-Object System.Collections.Generic.List[string]
$out.Add('# ЕДИНЫЙ РЕЕСТР ДЕФЕКТОВ (мастер)')
$out.Add('')
$out.Add('**Дата сборки:** 2026-11-05 · собрано из `_plan/BUGS_REGISTRY.md`, `_audit/AUDIT-A-world.md`, `_audit/AUDIT-B-sim.md`, `_audit/AUDIT-C-ui.md`, `_audit/FIXES_APPLIED.md`.')
$out.Add('')
$out.Add('## Зачем этот файл')
$out.Add('')
$out.Add('Раньше дефекты жили в четырёх документах с ПЕРЕСЕКАЮЩИМИСЯ префиксами: `_plan/BUGS_REGISTRY.md` использовал `W-01…W-06` для геймплейных дефектов мира, а `_audit/AUDIT-A-world.md` — `W-01…W-09` для дефектов стриминга/хранилища. Это разные дефекты с одинаковыми ID, из-за чего любой подсчёт «сколько осталось» был неверным.')
$out.Add('')
$out.Add('**Решение о префиксах:**')
$out.Add('')
$out.Add('| Серия | Префикс | Источник | О чём |')
$out.Add('|---|---|---|---|')
$out.Add('| Работы/агенты | `J-` | `_plan/J_jobs_agents.md` | диспетчеризация, клеймы, watchdog, зоны |')
$out.Add('| Мир/рендер (геймплей) | `W-` | `_plan/W_world_streaming.md` | шов 1024, вода, авто-стены |')
$out.Add('| Мир/стриминг (код) | `S-` | `_audit/AUDIT-A-world.md` | **бывший `W-` AUDIT-A**, переименован |')
$out.Add('| Симуляция/потоки | `B-` | `_audit/AUDIT-B-sim.md` | планировщик, пулы, RNG, offline |')
$out.Add('| UI/рендер | `C-` | `_audit/AUDIT-C-ui.md` | кадровые аллокации, guard-ы поколений |')
$out.Add('| Рендер/перф | `R-` | `_plan/R_render_perf.md` | draw calls, бюджет кадра |')
$out.Add('| Документация | `D-` | `_plan/D_docs_inventory.md` | устаревшие планы/контракты |')
$out.Add('| Слои/архитектура | `L-` | `_plan/BUGS_REGISTRY.md` | циклы, мёртвые абстракции |')
$out.Add('')

# ---------- J ----------
$reg = Read-Md '_plan\BUGS_REGISTRY.md'
$j = @()
foreach ($l in $reg) {
    if ($l -match '^\| \*\*(J-\d+)\*\* \|(.+)$') {
        $id = $Matches[1]
        $rest = $Matches[2]
        $cells = $rest -split '\|'
        $prio = ($cells[0] -replace '\*', '').Trim()
        $desc = ''
        if ($cells.Count -ge 3) { $desc = ($cells[2] -replace '\*\*Фикс:\*\*.*$', '').Trim() }
        if ($desc.Length -gt 150) { $desc = $desc.Substring(0, 147) + '...' }
        $closed = if ($l -match 'ЗАКРЫТ') { 'закрыт' } else { 'открыт' }
        $j += [pscustomobject]@{ Id = $id; Prio = $prio; Desc = $desc; Status = $closed }
    }
}

# ---------- W (геймплей, из реестра: заголовки ### W-01 · P1 · Название) ----------
$w = @()
foreach ($l in $reg) {
    if ($l -match '^### (W-0\d)\s*·\s*(.+)$') {
        $id = $Matches[1]
        $tail = $Matches[2]
        $sev = 'P1'
        if ($tail -match '\*\*(P\d)\*\*') { $sev = $Matches[1] }
        $title = ($tail -replace '\*\*P\d\*\*\s*·?\s*', '').Trim()
        if ($title.Length -gt 150) { $title = $title.Substring(0, 147) + '...' }
        $wStatus = 'открыт'; if ($l -match 'ЗАКРЫТ') { $wStatus = 'закрыт' }
        $w += [pscustomobject]@{ Id = $id; Prio = $sev; Desc = $title; Status = $wStatus }
    }
}

# ---------- S (бывший W из AUDIT-A) ----------
$auditA = Read-Md '_audit\AUDIT-A-world.md'
$fixedSet = @('W-01','W-02','W-03','W-04','W-05','W-06','W-07','W-08','W-09','B-01','B-02','B-03','B-04','B-05','B-06','B-07','B-08','B-09','B-10','C-01','C-02','C-03','C-04','C-05','C-06','C-07','C-08','C-09','C-10','C-11','C-12')
$s = @()
foreach ($l in $auditA) {
    if ($l -match '^## (W-\d+)\s*[—-]\s*(.+)$') {
        $orig = $Matches[1]
        $title = $Matches[2].Trim()
        $newId = 'S-' + ($orig -split '-')[1]
        $status = 'открыт'
        if ($fixedSet -contains $orig) { $status = 'закрыт' }
        $s += [pscustomobject]@{ Id = $newId; Orig = $orig; Prio = 'P1/P2'; Desc = $title; Status = $status }
    }
}

# ---------- B ----------
$auditB = Read-Md '_audit\AUDIT-B-sim.md'
$b = @()
foreach ($l in $auditB) {
    if ($l -match '^## (B-\d+)\s*[—-]\s*(.+)$') {
        $id = $Matches[1]
        $title = $Matches[2].Trim()
        $status = 'открыт'
        if ($fixedSet -contains $id) { $status = 'закрыт' }
        if ($title.Length -gt 150) { $title = $title.Substring(0, 147) + '...' }
        $b += [pscustomobject]@{ Id = $id; Prio = 'P1/P2'; Desc = $title; Status = $status }
    }
}

# ---------- C ----------
$auditC = Read-Md '_audit\AUDIT-C-ui.md'
$c = @()
foreach ($l in $auditC) {
    if ($l -match '^### \[(C-\d+)\]\s*(.+)$') {
        $id = $Matches[1]
        $title = $Matches[2].Trim()
        $status = 'открыт'
        if ($fixedSet -contains $id) { $status = 'закрыт' }
        if ($title.Length -gt 150) { $title = $title.Substring(0, 147) + '...' }
        $c += [pscustomobject]@{ Id = $id; Prio = 'P2/P3'; Desc = $title; Status = $status }
    }
}

# ---------- R / D из реестра (строки таблицы) + L из заголовков ----------
$rd = @()
foreach ($l in $reg) {
    if ($l -match '^\| \*\*((?:R|D)-\d+)\*\* \|(.+)$') {
        $id = $Matches[1]
        $cells = ($Matches[2] -split '\|')
        $prio = ($cells[0] -replace '\*', '').Trim()
        # описание — самая длинная ячейка из оставшихся (у R/D есть колонка «симптом»)
        $desc = ''
        for ($ci = 2; $ci -lt $cells.Count; $ci++) {
            $cand = ($cells[$ci] -replace '\*\*Фикс:\*\*.*$', '').Trim()
            if ($cand.Length -gt $desc.Length) { $desc = $cand }
        }
        if ($desc.Length -gt 150) { $desc = $desc.Substring(0, 147) + '...' }
        $closed = if ($l -match 'ЗАКРЫТ') { 'закрыт' } else { 'открыт' }
        $rd += [pscustomobject]@{ Id = $id; Prio = $prio; Desc = $desc; Status = $closed }
    }
}
# L-серия в реестре оформлена заголовками ### L-01 · P1 · Название
foreach ($l in $reg) {
    if ($l -match '^### (L-0\d)\s*·\s*(.+)$') {
        $id = $Matches[1]
        $tail = $Matches[2]
        $sev = 'P1'
        if ($tail -match '\*\*(P\d[^*]*)\*\*') { $sev = $Matches[1] }
        $title = ($tail -replace '\*\*[^*]+\*\*\s*·?\s*', '').Trim()
        if ($title.Length -gt 150) { $title = $title.Substring(0, 147) + '...' }
        $lStatus = 'открыт'; if ($l -match 'ЗАКРЫТ') { $lStatus = 'закрыт' }
        $rd += [pscustomobject]@{ Id = $id; Prio = $sev; Desc = $title; Status = $lStatus }
    }
}

function Add-Section($out, $title, $rows) {
    $out.Add('## ' + $title)
    $out.Add('')
    $out.Add('| ID | Приоритет | Что это | Статус |')
    $out.Add('|---|---|---|---|')
    foreach ($r in $rows) {
        $st = '**открыт**'
        if ($r.Status -eq 'закрыт') { $st = 'закрыт' }
        $out.Add('| ' + $r.Id + ' | ' + $r.Prio + ' | ' + $r.Desc + ' | ' + $st + ' |')
    }
    $out.Add('')
}

Add-Section $out 'J — работы и агенты (24)' $j
Add-Section $out 'W — мир/рендер, геймплей (6)' $w
Add-Section $out 'S — мир/стриминг, код (9, бывшие W-01…W-09 из AUDIT-A)' $s
Add-Section $out 'B — симуляция, потоки, пулы (10)' $b
Add-Section $out 'C — UI / рендер / интеграция (12)' $c
Add-Section $out 'R / D / L — рендер, документация, слои' $rd

$all = @() + $j + $w + $s + $b + $c + $rd
$open = @($all | Where-Object { $_.Status -ne 'закрыт' })
$closed = @($all | Where-Object { $_.Status -eq 'закрыт' })

$out.Insert(20, ('**Всего дефектов: ' + $all.Count + '** · закрыто: ' + $closed.Count + ' · **открыто: ' + $open.Count + '**'))
$out.Insert(21, '')

$out.Add('## Сводка')
$out.Add('')
$out.Add('| Метрика | Значение |')
$out.Add('|---|---|')
$out.Add('| Всего дефектов с ID | ' + $all.Count + ' |')
$out.Add('| Закрыто | ' + $closed.Count + ' |')
$out.Add('| **Открыто** | **' + $open.Count + '** |')
$out.Add('')
$out.Add('## Чего в 359 findings из PLAN.md §112 (не путать)')
$out.Add('')
$out.Add('В `PLAN.md:6079` стоит «359 findings» — это метрики **графа зависимостей** (`dependency-graph (2).json`): 179 HIGH_FAN_IN, 124 HIGH_FAN_OUT, 32 LARGE_FILE, 22 ISOLATED, 2 CYCLE. Это не дефекты поведения: «сколько файлов ссылается на файл» и «файл слишком большой». Реальных архитектурных пунктов там два — Cycle 2 и Cycle 3 — и они учтены как `L-`серия.')
$out.Add('')
$out.Add('## Не дефекты (справочно)')
$out.Add('')
$out.Add('- `_plan/MISSING_FEATURES.md` — отсутствующие фичи, не баги.')
$out.Add('- `_plan/REGRESSIONS.md` — стражи регрессий (R-0NN), почти все «стабилен»; это защита, не список багов.')
$out.Add('- `GLOBAL_AUDIT_REPORT.md` (B1…B5) — разобран и опровергнут в `_audit/AUDIT-A-world.md`.')
$out.Add('- `docs/WorldDeltaApiAudit.md`, `docs/MapDataWriterAudit.md` — справочные анализы API.')

$dest = Join-Path $root '_plan\DEFECTS_MASTER.md'
[System.IO.File]::WriteAllText($dest, ($out -join "`n"), (New-Object System.Text.UTF8Encoding($false)))
Write-Output ('written: ' + $dest)
Write-Output ('J=' + $j.Count + ' W=' + $w.Count + ' S=' + $s.Count + ' B=' + $b.Count + ' C=' + $c.Count + ' RDL=' + $rd.Count)
Write-Output ('TOTAL=' + $all.Count + ' closed=' + $closed.Count + ' open=' + $open.Count)