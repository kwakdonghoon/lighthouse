# M4 game loop run (batchmode). ASCII only. Usage: powershell -File Tools\unity_run_m4.ps1 [-Steps setup,edit,play] [-Out <dir>] [-Filter <test filter>]
# Takes Tools\unity.lock (exclusive; waits up to 20 min, 30 s polls). Always removes the lock at the end.
# setup : M4Tools.Setup (materials in Resources/Materials + Main.unity + build settings)   [needs graphics]
# edit  : EditMode tests (-nographics), result XML in <Out>\EditMode.xml
# play  : PlayMode tests (Main scene smoke: frames, save/restart restore, offline return)    [needs graphics], result XML in <Out>\PlayMode.xml
param(
    [string[]]$Steps = @('edit'),
    [string]$Out = "$PSScriptRoot\m4_results",
    [int]$TimeoutSec = 1200,
    [string]$Filter = ''
)
$Steps = @($Steps | ForEach-Object { $_ -split ',' })
$unity = 'C:\Program Files\Unity\Hub\Editor\6000.3.19f1\Editor\Unity.exe'
$proj = (Resolve-Path "$PSScriptRoot\..").Path
$lock = "$PSScriptRoot\unity.lock"
New-Item -ItemType Directory $Out -Force | Out-Null
$got = $false
for ($i = 0; $i -lt 40; $i++) {
    try { $fs = [System.IO.File]::Open($lock, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None); $b = [Text.Encoding]::ASCII.GetBytes("M4 loop agent $(Get-Date -Format s)"); $fs.Write($b, 0, $b.Length); $fs.Close(); $got = $true; break }
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
    if ($Steps -contains 'setup') { Run 'setup' @('-executeMethod', 'Lighthouse.EditorTools.M4Tools.Setup') | Out-Null }
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
        if (Test-Path "$Out\smoke.txt") { Get-Content "$Out\smoke.txt" -Encoding UTF8 }
    }
}
finally { Remove-Item $lock -ErrorAction SilentlyContinue }
