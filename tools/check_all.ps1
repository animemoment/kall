<#
  check_all.ps1 - one command "did I break anything?"

  1) dotnet build Game.csproj (stop on errors)
  2) every headless test tests/run_*.gd (except benches/diagnostics), each with a timeout
  3) compare with baseline tests/check_baseline.json:
       NEW FAIL  - passed in baseline, fails now  => REGRESSION (exit 1)
       KNOWN     - failed in baseline too         => not a new breakage, but still red
       FIXED     - failed in baseline, passes now => run with -UpdateBaseline

  Usage (from repo root):
    powershell -ExecutionPolicy Bypass -File tools/check_all.ps1
    ... -Only zone_model,world_pathfinder     # only selected tests (name without run_ / .gd)
    ... -IncludeSlow                          # also benches / stress
    ... -UpdateBaseline                       # save current result as the new baseline
  Godot path: env GODOT_BIN, otherwise the default below.
  Logs: _reports/check/<test>.log, summary: _reports/check/last_run.json
  Script is ASCII-only on purpose (Windows PowerShell 5.1 reads BOM-less files as ANSI).
#>
param(
  [string[]]$Only = @(),
  [switch]$IncludeSlow,
  [switch]$UpdateBaseline,
  [int]$TimeoutSec = 300
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo

$godot = $env:GODOT_BIN
if (-not $godot) { $godot = 'D:\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe' }
if (-not (Test-Path $godot)) { Write-Host "Godot not found: $godot (set GODOT_BIN)" -ForegroundColor Red; exit 2 }

# Not regular tests: benches, inspectors, diagnostics, long stress. Run with -IncludeSlow.
$slow = @(
  'bench_bulk','bench_cellat','bench_chunks','bench_micro','bench_windows',
  'generator_bench','terrain_window_bench','job_assignment_bench','pathfinding_bench',
  'cell_inspector','seam_inspector','world_plan_flow_diag','job_race_stress',
  'phase3_suite','phase4_suite','phase5_suite'
)

$logDir = Join-Path $repo '_reports\check'
New-Item -ItemType Directory -Force $logDir | Out-Null

# ---- 1. build ----
Write-Host '== dotnet build ==' -ForegroundColor Cyan
$buildLog = Join-Path $logDir '_build.log'
& dotnet build Game.csproj -nologo -v q *> $buildLog
if ($LASTEXITCODE -ne 0) {
  Get-Content $buildLog | Select-String -Pattern 'error' | Select-Object -First 30 | ForEach-Object { Write-Host $_.Line -ForegroundColor Red }
  Write-Host "BUILD FAILED - see $buildLog" -ForegroundColor Red
  exit 1
}
Write-Host 'build OK' -ForegroundColor Green

# ---- 2. tests ----
$runners = Get-ChildItem tests -Filter 'run_*.gd' | Sort-Object Name | ForEach-Object {
  [pscustomobject]@{ Name = ($_.BaseName -replace '^run_', ''); File = $_.Name }
}
if ($Only.Count -gt 0) {
  $want = $Only | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ }
  $runners = $runners | Where-Object { $want -contains $_.Name }
} elseif (-not $IncludeSlow) {
  $runners = $runners | Where-Object { $slow -notcontains $_.Name }
}

$baselinePath = Join-Path $repo 'tests\check_baseline.json'
$baseline = @{}
if (Test-Path $baselinePath) {
  (Get-Content $baselinePath -Raw | ConvertFrom-Json).PSObject.Properties | ForEach-Object { $baseline[$_.Name] = $_.Value }
}

$results = @()
foreach ($r in $runners) {
  $log = Join-Path $logDir ($r.Name + '.log')
  $err = Join-Path $logDir ($r.Name + '.err.log')
  $sw = [Diagnostics.Stopwatch]::StartNew()
  $p = Start-Process -FilePath $godot -ArgumentList @('--headless', '--path', '.', '--script', "res://tests/$($r.File)") `
        -NoNewWindow -PassThru -RedirectStandardOutput $log -RedirectStandardError $err
  $null = $p.Handle   # cache the handle, otherwise ExitCode is empty after exit
  $done = $p.WaitForExit($TimeoutSec * 1000)
  if ($done) { $p.WaitForExit() }
  if (-not $done) { try { $p.Kill() } catch {} ; $status = 'TIMEOUT' }
  elseif ($p.ExitCode -eq 0) { $status = 'PASS' }
  else { $status = 'FAIL' }
  $sec = [math]::Round($sw.Elapsed.TotalSeconds, 1)

  $prev = $baseline[$r.Name]
  $verdict = switch ($status) {
    'PASS' { if ($prev -and $prev -ne 'PASS') { 'FIXED' } else { 'ok' } }
    default { if ($prev -eq 'PASS' -or -not $prev) { 'NEW FAIL' } else { 'KNOWN' } }
  }
  if ($status -ne 'PASS' -and -not $prev) { $verdict = 'FAIL (no baseline)' }

  $color = @{ 'ok' = 'Green'; 'FIXED' = 'Cyan'; 'KNOWN' = 'Yellow' }[$verdict]
  if (-not $color) { $color = 'Red' }
  Write-Host ('{0,-40} {1,-8} {2,7}s  {3}' -f $r.Name, $status, $sec, $verdict) -ForegroundColor $color
  $results += [pscustomobject]@{ test = $r.Name; status = $status; seconds = $sec; verdict = $verdict; log = "_reports/check/$($r.Name).log" }
}

# ---- 3. summary ----
$results | ConvertTo-Json | Set-Content (Join-Path $logDir 'last_run.json') -Encoding UTF8
$newFail = @($results | Where-Object { $_.verdict -like 'NEW FAIL*' -or $_.verdict -like 'FAIL (no*' })
$known   = @($results | Where-Object { $_.verdict -eq 'KNOWN' })
$fixed   = @($results | Where-Object { $_.verdict -eq 'FIXED' })

Write-Host ''
Write-Host ("TOTAL {0} | pass {1} | NEW FAIL {2} | known fail {3} | fixed {4}" -f `
  $results.Count, @($results | Where-Object status -eq 'PASS').Count, $newFail.Count, $known.Count, $fixed.Count)

if ($UpdateBaseline) {
  foreach ($x in $results) { $baseline[$x.test] = $x.status }
  $baseline.GetEnumerator() | Sort-Object Name | ForEach-Object -Begin { $o = [ordered]@{} } -Process { $o[$_.Name] = $_.Value } -End { $o } |
    ConvertTo-Json | Set-Content $baselinePath -Encoding UTF8
  Write-Host "baseline updated: tests/check_baseline.json" -ForegroundColor Cyan
}

if ($newFail.Count -gt 0) {
  Write-Host 'REGRESSION: these tests were green before and are red now:' -ForegroundColor Red
  $newFail | ForEach-Object { Write-Host ("  {0}  -> {1}" -f $_.test, $_.log) -ForegroundColor Red }
  exit 1
}
if ($fixed.Count -gt 0) { Write-Host 'Some known failures are fixed - run with -UpdateBaseline.' -ForegroundColor Cyan }
exit 0
