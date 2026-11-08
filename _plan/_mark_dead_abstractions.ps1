$ErrorActionPreference = 'Stop'
$root = 'D:\bebebe-main (1)\bebebe-main'

$targets = @(
    @{ File = 'src\core\world\access\IWorldAccess.cs';        Id = 'L-01'; Why = 'мёртвая абстракция: интерфейс не реализуется ни одной живой веткой — реализация только в ChunkWorldAccess, который тоже никем не создаётся' },
    @{ File = 'src\core\world\access\ChunkWorldAccess.cs';    Id = 'L-01'; Why = 'мёртвая реализация мёртвого интерфейса: ни одного `new ChunkWorldAccess(` в src/ и tests/' },
    @{ File = 'src\ui\streaming\WorldJobBoard.cs';            Id = 'L-02'; Why = 'очередь мировых планов НЕ подключена: ни одного `new WorldJobBoard(` в игре (только тесты). Планы мира сейчас идут через StreamingToolPlacement/WorldModel, минуя доску' },
    @{ File = 'src\simulation\jobs\WorldJobBridge.cs';        Id = 'L-02'; Why = 'мост к мёртвой WorldJobBoard: создаётся только тестами, в игровом коде `new WorldJobBridge(` отсутствует' },
    @{ File = 'src\simulation\jobs\WorldPlanHandler.cs';      Id = 'L-02'; Why = 'хендлер планов мира не регистрируется в JobRegistry и не создаётся в игре — мёртвая ветка' }
)

$stamp = '<!-- ' + '' + ' -->'
$done = 0
foreach ($t in $targets) {
    $p = Join-Path $root $t.File
    if (-not (Test-Path $p)) { Write-Output ('SKIP (нет файла): ' + $t.File); continue }
    $bytes = [System.IO.File]::ReadAllBytes($p)
    $hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 239 -and $bytes[1] -eq 187 -and $bytes[2] -eq 191)
    $text = [System.Text.Encoding]::UTF8.GetString($bytes)
    if ($hasBom) { $text = $text.Substring(1) }
    if ($text.Contains(($t.Id + ': МЁРТВЫЙ КОД'))) { Write-Output ('уже помечен: ' + $t.File); continue }

    $header = '// ' + $t.Id + ': МЁРТВЫЙ КОД (' + (Get-Date -Format 'yyyy-MM-dd') + '). ' + $t.Why + '.' + "`r`n" +
              '// Удаление — отдельная задача чистки (см. _plan/DEFECTS_MASTER.md). Не подключать без решения владельца.' + "`r`n"

    # Вставляем после последней using-строки (или в начало файла, если using нет)
    $lines = $text -split "`r?`n"
    $insertAt = 0
    for ($i = 0; $i -lt [Math]::Min($lines.Length, 40); $i++) {
        if ($lines[$i] -match '^\s*using\s') { $insertAt = $i + 1 }
        elseif ($lines[$i] -match '^\s*namespace\s') { break }
    }
    $newLines = @()
    if ($insertAt -eq 0) {
        $newLines += $header.TrimEnd("`r`n").Split("`r`n")
        $newLines += ''
        $newLines += $lines
    } else {
        $newLines += $lines[0..($insertAt - 1)]
        $newLines += ''
        $newLines += $header.TrimEnd("`r`n").Split("`r`n")
        $newLines += $lines[$insertAt..($lines.Length - 1)]
    }
    $out = ($newLines -join "`r`n")
    [System.IO.File]::WriteAllText($p, $out, (New-Object System.Text.UTF8Encoding($hasBom)))
    $done++
}
Write-Output ('files marked: ' + $done)