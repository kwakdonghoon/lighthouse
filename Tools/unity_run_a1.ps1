# A1 world scenery run (batchmode). ASCII only.
# Usage: powershell -File Tools\unity_run_a1.ps1 [-Steps edit,play] [-Filter <test filter>] [-ProjectPath <dir>] [-Out <dir>]
# Takes the MAIN project's Tools\unity.lock (exclusive; waits up to 30 min, 30 s polls) even when -ProjectPath points at an isolated copy. Always removes the lock at the end.
# -ProjectPath: default = this project. An isolated copy (git archive HEAD + only the A1 files) lets the run compile while other agents have half-edited files in the shared tree.
# edit : EditMode tests (-nographics), result XML in <Out>\EditMode.xml
# play : PlayMode tests (needs graphics), result XML in <Out>\PlayMode.xml, smoke.txt
param(
    [string[]]$Steps = @('edit'),
    [string]$ProjectPath = '',
    [string]$Out = "$PSScriptRoot\a1_results",
    [int]$TimeoutSec = 1800,
    [string]$Filter = ''
)
$Steps = @($Steps | ForEach-Object { $_ -split ',' })
$unity = 'C:\Program Files\Unity\Hub\Editor\6000.3.19f1\Editor\Unity.exe'
$proj = if ($ProjectPath) { (Resolve-Path $ProjectPath).Path } else { (Resolve-Path "$PSScriptRoot\..").Path }
$lock = "$PSScriptRoot\unity.lock"
New-Item -ItemType Directory $Out -Force | Out-Null
$got = $false
for ($i = 0; $i -lt 60; $i++) {
    try { $fs = [System.IO.File]::Open($lock, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None); $b = [Text.Encoding]::ASCII.GetBytes("A1 scenery agent $(Get-Date -Format s)"); $fs.Write($b, 0, $b.Length); $fs.Close(); $got = $true; break }
    catch { Write-Host "lock busy, waiting 30 s ($i)"; Start-Sleep -Seconds 30 }
}
if (-not $got) { Write-Host 'could not get unity.lock'; exit 2 }
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
        $xml = "$Out\PlayMode.xml"; Remove-Item $xml -ErrorAction SilentlyContinue; Remove-Item "$Out\smoke.txt" -ErrorAction SilentlyContinue
        $a = @('-runTests', '-testPlatform', 'PlayMode', '-testResults', $xml)
        if ($Filter) { $a += @('-testFilter', $Filter) }
        Run 'test_PlayMode' $a | Out-Null
        Report $xml 'PlayMode'
        $smoke = Join-Path $proj 'Tools\a1_results\smoke.txt'
        if ((Test-Path $smoke) -and ((Resolve-Path $smoke).Path -ne (Join-Path (Resolve-Path $Out).Path 'smoke.txt'))) { Copy-Item $smoke "$Out\smoke.txt" -Force }
        if (Test-Path "$Out\smoke.txt") { Get-Content "$Out\smoke.txt" -Encoding UTF8 }
    }
}
finally { Remove-Item $lock -ErrorAction SilentlyContinue }
