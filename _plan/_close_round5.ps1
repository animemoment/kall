$ErrorActionPreference = 'Stop'
$root = 'D:\bebebe-main (1)\bebebe-main'
$rp = Join-Path $root '_plan\BUGS_REGISTRY.md'
$text = [System.Text.Encoding]::UTF8.GetString([System.IO.File]::ReadAllBytes($rp))
$lines = $text -split "`n"
$n = 0

# ---------- строки таблицы: R-01, R-02, R-05, R-06, R-08 ----------
$rowFix = @{}
$rowFix['R-01'] = 'новый `StockpileHoverRenderer` (ZIndex 7, троттлинг 0.05с, не светит под HUD) читает публичный `StockpileManager.IsZoneTile` и рисует заливку+рамку склада; инстанцируется в `MapRenderer._Ready`. Остаток (событие HoveredTile в менеджере) не нужен для симптома'
$rowFix['R-02'] = '`MapRenderer._Process` → `FlushPendingCells()` с бюджетом ПО ВРЕМЕНИ (1.5 мс, часы раз в 64 клетки, hard cap 4096) вместо фиксированных 16384 SetCell/кадр'
$rowFix['R-05'] = '`_ghostLayer.Visible` = «есть использованные клетки» (проверка раз в 4 кадра) вместо постоянного true'
$rowFix['R-06'] = '`FarmZoneRenderer` больше не рисует по клетке: склейка прогонов строк/столбцов в прямоугольники + внешние грани в отрезки + кэш геометрии зоны (`FarmZoneGeometryCache`)'
$rowFix['R-08'] = 'закрыт вместе с C-09: `TerrainTileLayer.Flush` делает один проход и один `exists()` на клетку, переиспользует массив, бюджет 8192 клетки/кадр'

foreach ($id in $rowFix.Keys) {
    for ($i = 0; $i -lt $lines.Length; $i++) {
        if ($lines[$i] -match "^\| \*\*$id\*\* \| (P\d) \|" -and $lines[$i] -notmatch 'ЗАКРЫТ') {
            $lines[$i] = $lines[$i] -replace "^\| \*\*$id\*\* \| (P\d) \|", "| **$id** | **`$1 → ЗАКРЫТ (2026-11-05)** |"
            $lines[$i] = $lines[$i].TrimEnd()
            if ($lines[$i].EndsWith('|')) {
                $lines[$i] = $lines[$i].Substring(0, $lines[$i].Length - 1).TrimEnd() + ' **Фикс:** ' + $rowFix[$id] + ' |'
            }
            $n++
        }
    }
}

# ---------- заголовки: W-01, W-03, W-06 ----------
$headFix = @{}
$headFix['W-01'] = 'согласованы маски со стороны воды: `shore_inset = 0.12` в `terrain_blend.gdshader` (полоса гашения воды у клетки суши, вода только убирается); общий шейдер для острова и мира'
$headFix['W-03'] = 'проверено: стриминг не рисует землю/воду вовсе (только прозрачный декор), кромка идёт через тот же `terrain_blend`; старый `streaming_ground.gdshader` помечен выведенным и не подключается'
$headFix['W-06'] = 'island-seam тесты переведены на `WorldModel.IslandTiles` (1024): `test_streamingview_decor_seam.cs`, `TestDestroyerWorldGen.cs` (WTEST 5), `TestAdversarialConstruction.cs`. Тесты 512-тайловых РЕГИОНОВ генератора не тронуты осознанно'

foreach ($id in $headFix.Keys) {
    for ($i = 0; $i -lt $lines.Length; $i++) {
        if ($lines[$i] -match "^### $id " -and $lines[$i] -notmatch 'ЗАКРЫТ') {
            $lines[$i] = $lines[$i].TrimEnd() + ' — **ЗАКРЫТ (2026-11-05)**: ' + $headFix[$id]
            $n++
        }
    }
}

[System.IO.File]::WriteAllText($rp, ($lines -join "`n"), (New-Object System.Text.UTF8Encoding($false)))
Write-Output ('registry updated: ' + $n)

# ---------- сборщик мастера: S-серия (W-04/W-07 закрыты), W-заголовки учитывают ЗАКРЫТ ----------
$bp = Join-Path $root '_plan\_build_master.ps1'
$bt = [System.Text.Encoding]::UTF8.GetString([System.IO.File]::ReadAllBytes($bp))

$oldSet = "`$fixedSet = @('W-01','W-02','W-03','W-05','W-06','W-08','W-09'"
$newSet = "`$fixedSet = @('W-01','W-02','W-03','W-04','W-05','W-06','W-07','W-08','W-09'"
if ($bt.Contains($oldSet)) { $bt = $bt.Replace($oldSet, $newSet); Write-Output 'S-series: +W-04 (S-04), +W-07 (S-07)' }

$oldW = "        `$w += [pscustomobject]@{ Id = `$id; Prio = `$sev; Desc = `$title; Status = 'открыт' }"
$newW = "        `$wStatus = 'открыт'; if (`$l -match 'ЗАКРЫТ') { `$wStatus = 'закрыт' }`n        `$w += [pscustomobject]@{ Id = `$id; Prio = `$sev; Desc = `$title; Status = `$wStatus }"
if ($bt.Contains($oldW)) { $bt = $bt.Replace($oldW, $newW); Write-Output 'W headings: honor ЗАКРЫТ' } else { Write-Output 'WARN: W heading line not found' }

[System.IO.File]::WriteAllText($bp, $bt, (New-Object System.Text.UTF8Encoding($true)))