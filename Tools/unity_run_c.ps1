# Area C run (batchmode). ASCII only. Usage: powershell -File Tools\unity_run_c.ps1 [-Steps edit,play,release] [-Out <dir>] [-Filter <test filter>]
# Takes Tools\unity.lock (exclusive; waits up to 20 min, 30 s polls). Always removes the lock at the end.
# Stops if Temp\UnityLockfile exists WITHOUT unity.lock (= a human has the editor open).
# edit    : EditMode tests (-nographics), result XML in <Out>\EditMode.xml
# play    : PlayMode tests (Main scene; M7PlayTests: import -> Restart, corrupt save notice)  [needs graphics], <Out>\PlayMode.xml
# release : LIGHTHOUSE_DEV off -> EditMode tests in release config -> real Windows player build + binary scan -> LIGHTHOUSE_DEV on again
#           (ProjectSettings.asset is copied first and restored byte for byte at the end)
param(
    [string[]]$Steps = @('edit'),
    [string]$Out = "$PSScriptRoot\c_results",
    [int]$TimeoutSec = 1800,
    [string]$Filter = ''
)
$Steps = @($Steps | ForEach-Object { $_ -split ',' })
$unity = 'C:\Program Files\Unity\Hub\Editor\6000.3.19f1\Editor\Unity.exe'
$proj = (Resolve-Path "$PSScriptRoot\..").Path
$lock = "$PSScriptRoot\unity.lock"
New-Item -ItemType Directory $Out -Force | Out-Null
if ((Test-Path "$proj\Temp\UnityLockfile") -and -not (Test-Path $lock)) { Write-Host 'Temp\UnityLockfile exists and no unity.lock: a human editor is open. Stopping.'; exit 4 }
$got = $false
for ($i = 0; $i -lt 60; $i++) {
    try { $fs = [System.IO.File]::Open($lock, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None); $b = [Text.Encoding]::ASCII.GetBytes("C agent $(Get-Date -Format s)"); $fs.Write($b, 0, $b.Length); $fs.Close(); $got = $true; break }
    catch { Write-Host "lock busy, waiting 30 s ($i)"; Start-Sleep -Seconds 30 }
}
if (-not $got) { Write-Host 'could not get unity.lock'; exit 2 }
$settings = "$proj\ProjectSettings\ProjectSettings.asset"; $settingsBak = "$Out\ProjectSettings.asset.orig"
try {
    function Run($name, [string[]]$extra) {
        $log = "$Out\$name.log"; Remove-Item $log -ErrorAction SilentlyContinue
        $pr = Start-Process -FilePath $unity -ArgumentList (@('-batchmode', '-projectPath', $proj) + $extra + @('-logFile', $log)) -PassThru
        if (-not $pr.WaitForExit($TimeoutSec * 1000)) { $pr.Kill(); Write-Host "${name}: TIMEOUT"; return $false }
        $errs = Select-String -Path $log -Pattern 'error CS' -ErrorAction SilentlyContinue | Select-Object -First 12
        foreach ($e in $errs) { Write-Host ("  compile error: " + $e.Line.Substring(0, [Math]::Min(260, $e.Line.Length))) }
        Write-Host "${name}: exit code $($pr.ExitCode)"
        return ($pr.ExitCode -eq 0)
    }
    function Report($xml, $label) {
        if (Test-Path $xml) {
            $r = ([xml](Get-Content $xml -Encoding UTF8)).'test-run'
            Write-Host "${label}: total $($r.total) passed $($r.passed) failed $($r.failed) skipped $($r.skipped)"
            foreach ($c in $r.SelectNodes('//test-case[@result="Failed"]')) { Write-Host ("  FAILED: " + $c.name + " :: " + ($c.failure.message.'#cdata-section' -replace '\s+', ' ')) }
        } else { Write-Host "${label}: no result file (compile error?)" }
    }
    if ($Steps -contains 'edit') {
        $xml = "$Out\EditMode.xml"; Remove-Item $xml -ErrorAction SilentlyContinue
        $a = @('-nographics', '-runTests', '-testPlatform', 'EditMode', '-testResults', $xml)
        if ($Filter) { $a += @('-testFilter', $Filter) }
        Run 'test_EditMode' $a | Out-Null
        Report $xml 'EditMode'
    }
    if ($Steps -contains 'play') {
        $xml = "$Out\PlayMode.xml"; Remove-Item $xml -ErrorAction SilentlyContinue
        $a = @('-runTests', '-testPlatform', 'PlayMode', '-testResults', $xml)
        if ($Filter) { $a += @('-testFilter', $Filter) }
        Run 'test_PlayMode' $a | Out-Null
        Report $xml 'PlayMode'
    }
    if ($Steps -contains 'release') {
        Copy-Item $settings $settingsBak -Force
        try {
            Run 'release_symbol_off' @('-quit', '-nographics', '-executeMethod', 'Lighthouse.EditorTools.DevTools.BatchSymbolOff') | Out-Null
            $xml = "$Out\EditMode_release.xml"; Remove-Item $xml -ErrorAction SilentlyContinue
            Run 'release_EditMode' @('-nographics', '-runTests', '-testPlatform', 'EditMode', '-testResults', $xml) | Out-Null
            Report $xml 'EditMode (release config)'
            $pdir = "$Out\release_player"; Remove-Item $pdir -Recurse -Force -ErrorAction SilentlyContinue
            Run 'release_player' @('-nographics', '-executeMethod', 'Lighthouse.EditorTools.DevTools.BatchVerifyReleasePlayer', '-lhOut', $pdir) | Out-Null
            if (Test-Path "$pdir\dev_exclusion.txt") { Get-Content "$pdir\dev_exclusion.txt" -Encoding UTF8 } else { Write-Host 'release_player: no dev_exclusion.txt' }
        }
        finally {
            Copy-Item $settingsBak $settings -Force
            Write-Host 'ProjectSettings.asset restored (LIGHTHOUSE_DEV state as before)'
        }
    }
}
finally { Remove-Item $lock -ErrorAction SilentlyContinue }
