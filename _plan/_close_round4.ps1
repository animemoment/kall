$ErrorActionPreference = 'Stop'
$root = 'D:\bebebe-main (1)\bebebe-main'
$rp = Join-Path $root '_plan\BUGS_REGISTRY.md'
$text = [System.Text.Encoding]::UTF8.GetString([System.IO.File]::ReadAllBytes($rp))
$lines = $text -split "`n"
$n = 0

# J-20 — строка таблицы
$fix20 = 'мёртвый код подтверждён чтением: `IdleWorkerSpatialGrid` (402 стр.) не создаётся нигде (все упоминания в репозитории — комментарии), помечен `[Obsolete]` с причиной и с предупреждением про устаревшую сетку 32×32 (= J-05); `GenericJobSpatialIndex` уже был `[Obsolete]`. Удаление 1650 строк — отдельная задача чистки'
for ($i = 0; $i -lt $lines.Length; $i++) {
    if ($lines[$i] -match '^\| \*\*J-20\*\* \| (P\d) \|' -and $lines[$i] -notmatch 'ЗАКРЫТ') {
        $lines[$i] = $lines[$i] -replace '^\| \*\*J-20\*\* \| (P\d) \|', '| **J-20** | **$1 → ЗАКРЫТ (2026-11-05)** |'
        $lines[$i] = $lines[$i].TrimEnd()
        if ($lines[$i].EndsWith('|')) {
            $lines[$i] = $lines[$i].Substring(0, $lines[$i].Length - 1).TrimEnd() + ' **Фикс:** ' + $fix20 + ' |'
        }
        $n++
    }
}

# L-06 / L-07 — заголовки ### L-06 · ... → добавляем пометку о закрытии
$fixL = @{}
$fixL['L-06'] = 'профиль теперь видит диспетчер: `GameProfiler.ScopeCounter` (ноль аллокаций, ранний выход при выключенных счётчиках) + публикация в F3-панели (`PerformanceOverlay`): examined/assigned/ms на вызов/число вызовов за окно'
$fixL['L-07'] = 'новый `DrawCallSplit` (учёт draw-батчей по группам за кадр, доли в %, неразмеченный остаток), инструментирован `AgentRenderer`, вывод строкой в F3; `Reset()` первым оператором `_Process`, чтобы разбивка не залипала'
foreach ($id in @('L-06','L-07')) {
    for ($i = 0; $i -lt $lines.Length; $i++) {
        if ($lines[$i] -match "^### $id " -and $lines[$i] -notmatch 'ЗАКРЫТ') {
            $lines[$i] = $lines[$i].TrimEnd() + ' — **ЗАКРЫТ (2026-11-05)**: ' + $fixL[$id]
            $n++
        }
    }
}

[System.IO.File]::WriteAllText($rp, ($lines -join "`n"), (New-Object System.Text.UTF8Encoding($false)))
Write-Output ('registry rows updated: ' + $n)

# В сборщике мастера: L-заголовки считаются закрытыми, если в строке есть ЗАКРЫТ
$bp = Join-Path $root '_plan\_build_master.ps1'
$bt = [System.Text.Encoding]::UTF8.GetString([System.IO.File]::ReadAllBytes($bp))
$old = "        `$rd += [pscustomobject]@{ Id = `$id; Prio = `$sev; Desc = `$title; Status = 'открыт' }"
$new = "        `$lStatus = 'открыт'; if (`$l -match 'ЗАКРЫТ') { `$lStatus = 'закрыт' }`n        `$rd += [pscustomobject]@{ Id = `$id; Prio = `$sev; Desc = `$title; Status = `$lStatus }"
if ($bt.Contains($old)) {
    $bt = $bt.Replace($old, $new)
    [System.IO.File]::WriteAllText($bp, $bt, (New-Object System.Text.UTF8Encoding($true)))
    Write-Output 'builder: L headings now honor ЗАКРЫТ'
} else {
    Write-Output 'WARN: builder L-line not found'
}