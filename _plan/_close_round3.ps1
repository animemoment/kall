$ErrorActionPreference = 'Stop'
$bp = 'D:\bebebe-main (1)\bebebe-main\_plan\_build_master.ps1'
$bt = [System.Text.Encoding]::UTF8.GetString([System.IO.File]::ReadAllBytes($bp))
$old = "`$fixedSet = @('W-01','W-02','W-03','B-01','B-02','B-03','B-04','B-06','B-08','B-09','B-10','C-01','C-02','C-03','C-04','C-05','C-06','C-07','C-08','C-09','C-10','C-11','C-12')"
$new = "`$fixedSet = @('W-01','W-02','W-03','W-05','W-06','W-08','W-09','B-01','B-02','B-03','B-04','B-05','B-06','B-07','B-08','B-09','B-10','C-01','C-02','C-03','C-04','C-05','C-06','C-07','C-08','C-09','C-10','C-11','C-12')"
if ($bt.Contains($old)) {
    [System.IO.File]::WriteAllText($bp, $bt.Replace($old, $new), (New-Object System.Text.UTF8Encoding($true)))
    Write-Output 'fixedSet extended (S: W-05/06/08/09, B: +05/07)'
} else {
    Write-Output 'WARN: fixedSet line not found as expected'
}