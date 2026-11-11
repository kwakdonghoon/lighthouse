# M1 전용: Unity 배치모드로 순수 로직 시험(Lighthouse.Tests.Core)만 돌린다. ASCII only (PowerShell 5.1).
# 같은 프로젝트에서 Unity 배치모드는 동시에 둘이 못 돈다 -> Tools\unity.lock 을 배타적으로 만들어 잡고(이미 있으면 30초 간격, 최대 20분), 끝나면 반드시 지운다.
# Usage: powershell -File Tools\unity_run_m1.ps1 [-Out <dir>]
param(
    [string]$Out = "$PSScriptRoot\m1_results",
    [int]$TimeoutSec = 900,
    [int]$LockWaitSec = 1200
)
$unity = 'C:\Program Files\Unity\Hub\Editor\6000.3.19f1\Editor\Unity.exe'
$proj = (Resolve-Path "$PSScriptRoot\..").Path
$lock = Join-Path $PSScriptRoot 'unity.lock'
New-Item -ItemType Directory $Out -Force | Out-Null

$got = $false; $waited = 0
while (-not $got) {
    try {
        $fs = [System.IO.File]::Open($lock, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
        $b = [System.Text.Encoding]::ASCII.GetBytes("m1 pid=$PID started=$(Get-Date -Format s)"); $fs.Write($b, 0, $b.Length); $fs.Close(); $got = $true
    } catch [System.IO.IOException] {
        if ($waited -ge $LockWaitSec) { Write-Host "lock wait timeout ($LockWaitSec s): $(Get-Content $lock -ErrorAction SilentlyContinue)"; exit 3 }
        Write-Host "unity.lock held ($(Get-Content $lock -ErrorAction SilentlyContinue)); retry in 30s"; Start-Sleep -Seconds 30; $waited += 30
    }
}
try {
    $xml = "$Out\EditMode_M1.xml"; $log = "$Out\test_M1.log"
    Remove-Item $xml, $log -ErrorAction SilentlyContinue
    $pr = Start-Process -FilePath $unity -ArgumentList @('-batchmode', '-nographics', '-projectPath', $proj, '-runTests', '-testPlatform', 'EditMode', '-assemblyNames', 'Lighthouse.Tests.Core', '-testResults', $xml, '-logFile', $log) -PassThru
    if (-not $pr.WaitForExit($TimeoutSec * 1000)) { $pr.Kill(); Write-Host "TIMEOUT"; exit 2 }
    $errs = Select-String -Path $log -Pattern 'error CS' -ErrorAction SilentlyContinue | Select-Object -First 10
    foreach ($e in $errs) { Write-Host ("  compile error: " + $e.Line.Substring(0, [Math]::Min(240, $e.Line.Length))) }
    Write-Host "unity exit code $($pr.ExitCode)"
    if (Test-Path $xml) {
        $r = ([xml](Get-Content $xml -Encoding UTF8)).'test-run'
        Write-Host "EditMode(M1): total $($r.total) passed $($r.passed) failed $($r.failed) skipped $($r.skipped) duration $($r.duration)"
        foreach ($c in $r.SelectNodes('//test-case')) { Write-Host ("  " + $c.result + " " + $c.name + " (" + $c.duration + "s)") }
        foreach ($c in $r.SelectNodes('//test-case[@result="Failed"]')) { Write-Host ("  FAILED: " + $c.name + " :: " + ($c.failure.message.'#cdata-section' -replace '\s+', ' ')) }
    } else { Write-Host "no result file (compile error?)" }
} finally {
    Remove-Item $lock -Force -ErrorAction SilentlyContinue
}
