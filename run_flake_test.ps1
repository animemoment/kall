Set-Location 'D:\bebebe-main (1)\bebebe-main'
for ($i = 1; $i -le 3; $i++) {
    $env:GODOT_BIN='D:\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe'
    $out = & .\tools\check_all.ps1 -Only adversarial_job_distribution_test 2>&1 | Select-String 'PASS|FAIL|Avg|Max|TOTAL'
    $out | ForEach-Object { Write-Host ("RUN {0}: {1}" -f $i, $_) }
    Start-Sleep -Seconds 2
}
