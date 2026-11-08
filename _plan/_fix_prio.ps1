$ErrorActionPreference = 'Stop'
$p = 'D:\bebebe-main (1)\bebebe-main\_plan\BUGS_REGISTRY.md'
$text = [System.Text.Encoding]::UTF8.GetString([System.IO.File]::ReadAllBytes($p))

# Исходные приоритеты (до моей порчи столбца)
$prio = [ordered]@{
    'J-01' = 'P0'; 'J-02' = 'P0'; 'J-03' = 'P0'; 'J-04' = 'P0'; 'J-05' = 'P0'
    'J-07' = 'P0'; 'J-08' = 'P0'; 'J-14' = 'P0'; 'J-15' = 'P0'
    'J-06' = 'P0'
    'J-09' = 'P1'; 'J-10' = 'P1'; 'J-11' = 'P1'; 'J-12' = 'P1'; 'J-13' = 'P1'
    'J-16' = 'P1'; 'J-17' = 'P1'; 'J-18' = 'P1'
    'J-19' = 'P2'; 'J-20' = 'P2'; 'J-21' = 'P2'; 'J-22' = 'P2'; 'J-23' = 'P2'
}

$fixed = 0
foreach ($id in $prio.Keys) {
    $pattern = '\| \*\*' + [regex]::Escape($id) + '\*\* \| \*\*\$[12] → ЗАКРЫТ \(2026-11-05\)\*\* \|'
    $replacement = '| **' + $id + '** | **' + $prio[$id] + ' → ЗАКРЫТ (2026-11-05)** |'
    $newText = [regex]::Replace($text, $pattern, $replacement)
    if ($newText -ne $text) { $fixed++; $text = $newText }
}

[System.IO.File]::WriteAllText($p, $text, (New-Object System.Text.UTF8Encoding($false)))
Write-Output ('priority cells fixed: ' + $fixed)

$rest = [regex]::Matches($text, '\|\s*\*\*([A-Z]-\d+)\*\*\s*\|\s*\*\*\$[12]')
Write-Output ('осталось испорченных: ' + $rest.Count)