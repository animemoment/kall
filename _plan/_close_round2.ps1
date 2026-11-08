$ErrorActionPreference = 'Stop'
$root = 'D:\bebebe-main (1)\bebebe-main'

# --- 1. J-23 в основном реестре ---
$rp = Join-Path $root '_plan\BUGS_REGISTRY.md'
$text = [System.Text.Encoding]::UTF8.GetString([System.IO.File]::ReadAllBytes($rp))
$lines = $text -split "`n"
$fix23 = 'диагностика [DISP] считает бездельников по ФАКТИЧЕСКИМ координатам агентов (было: CollectIdleWorkersInChunk на каждый чанк, а плоский пул возвращает ОДИН И ТОТ ЖЕ список на любой чанк и вдобавок двигает курсор забора) — отчёт больше не маскирует «полосы»'
$n = 0
for ($i = 0; $i -lt $lines.Length; $i++) {
    if ($lines[$i] -match '^\| \*\*J-23\*\* \| (P\d) \|' -and $lines[$i] -notmatch 'ЗАКРЫТ') {
        $lines[$i] = $lines[$i] -replace '^\| \*\*J-23\*\* \| (P\d) \|', '| **J-23** | **$1 → ЗАКРЫТ (2026-11-05)** |'
        $lines[$i] = $lines[$i].TrimEnd()
        if ($lines[$i].EndsWith('|')) {
            $lines[$i] = $lines[$i].Substring(0, $lines[$i].Length - 1).TrimEnd() + ' **Фикс:** ' + $fix23 + ' |'
        }
        $n++
    }
}
[System.IO.File]::WriteAllText($rp, ($lines -join "`n"), (New-Object System.Text.UTF8Encoding($false)))
Write-Output ('J-23 updated: ' + $n)

# --- 2. Расширить набор закрытых в сборщике мастера ---
$bp = Join-Path $root '_plan\_build_master.ps1'
$bt = [System.Text.Encoding]::UTF8.GetString([System.IO.File]::ReadAllBytes($bp))
$old = "`$fixedSet = @('W-01','W-02','W-03','B-01','B-02','B-03','C-01','C-02','C-03')"
$new = "`$fixedSet = @('W-01','W-02','W-03','B-01','B-02','B-03','B-04','B-06','B-08','B-09','B-10','C-01','C-02','C-03','C-04','C-05','C-06','C-07','C-08','C-09','C-10','C-11','C-12')"
if ($bt.Contains($old)) {
    $bt = $bt.Replace($old, $new)
    [System.IO.File]::WriteAllText($bp, $bt, (New-Object System.Text.UTF8Encoding($true)))
    Write-Output 'builder fixedSet extended'
} else {
    Write-Output 'WARN: fixedSet line not found'
}