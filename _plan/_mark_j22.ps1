$ErrorActionPreference = 'Stop'
$p = 'D:\bebebe-main (1)\bebebe-main\_plan\BUGS_REGISTRY.md'
$text = [System.Text.Encoding]::UTF8.GetString([System.IO.File]::ReadAllBytes($p))
$lines = $text -split "`n"
$fix = 'бакеты предметов расширены с 32×32 (512×512, с клампом) до 64×64 = весь остров без клампа; `HasItemsAt` (горячий путь выдачи работ) отвечает по lock-free счётчику бакета и берёт глобальный лок только если в бакете реально что-то есть; счётчик публикуется до вставки и уменьшается после удаления, поэтому ложных «пусто» не бывает'
$n = 0
for ($i = 0; $i -lt $lines.Length; $i++) {
    if ($lines[$i] -match '^\| \*\*J-22\*\* \| (P\d) \|' -and $lines[$i] -notmatch 'ЗАКРЫТ') {
        $lines[$i] = $lines[$i] -replace '^\| \*\*J-22\*\* \| (P\d) \|', '| **J-22** | **$1 → ЗАКРЫТ (2026-11-05)** |'
        $lines[$i] = $lines[$i].TrimEnd()
        if ($lines[$i].EndsWith('|')) {
            $lines[$i] = $lines[$i].Substring(0, $lines[$i].Length - 1).TrimEnd() + ' **Фикс:** ' + $fix + ' |'
        }
        $n++
    }
}
[System.IO.File]::WriteAllText($p, ($lines -join "`n"), (New-Object System.Text.UTF8Encoding($false)))
Write-Output ('J-22 rows updated: ' + $n)