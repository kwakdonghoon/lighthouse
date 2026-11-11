# M3 UI run (batchmode). ASCII only. Usage: powershell -File Tools\unity_run_m3.ps1 [-Steps edit,prep,fonts,prefabs,capture] [-Out <dir>] [-Res 320x568,...]
# Takes Tools\unity.lock (exclusive; waits up to 20 min, 30 s polls). Always removes the lock at the end.
# edit    : EditMode tests (-nographics), result XML in <Out>\EditMode.xml
# prep    : M3Tools.Step1_Prepare  (TMP essentials, frame/font import)      [-nographics ok]
# fonts   : M3Tools.Step2_Fonts    (TMP SDF font assets + glyph check)       [needs graphics]
# prefabs : M3Tools.Step3_Prefabs  (UiRoot and screen prefabs, M3UI scene)   [needs graphics]
# capture : M3Tools.Step4_Capture  (resolution matrix PNG + audit)           [needs graphics]
param(
    [string[]]$Steps = @('edit'),
    [string]$Out = "$PSScriptRoot\m3_results",
    [int]$TimeoutSec = 900,
    [string]$Filter = '',
    [string]$Res = ''
)
$Steps = @($Steps | ForEach-Object { $_ -split ',' })
$unity = 'C:\Program Files\Unity\Hub\Editor\6000.3.19f1\Editor\Unity.exe'
$proj = (Resolve-Path "$PSScriptRoot\..").Path
$lock = "$PSScriptRoot\unity.lock"
New-Item -ItemType Directory $Out -Force | Out-Null
$got = $false
for ($i = 0; $i -lt 40; $i++) {
    try { $fs = [System.IO.File]::Open($lock, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None); $b = [Text.Encoding]::ASCII.GetBytes("M3 ui agent $(Get-Date -Format s)"); $fs.Write($b, 0, $b.Length); $fs.Close(); $got = $true; break }
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
    if ($Steps -contains 'prep') { Run 'prep' @('-nographics', '-executeMethod', 'Lighthouse.EditorTools.M3Tools.Step1_Prepare') | Out-Null }
    if ($Steps -contains 'fonts') { Run 'fonts' @('-executeMethod', 'Lighthouse.EditorTools.M3Tools.Step2_Fonts') | Out-Null; if (Test-Path "$proj\Tools\m3_results\m3\fonts.txt") { Get-Content "$proj\Tools\m3_results\m3\fonts.txt" -Encoding UTF8 } }
    if ($Steps -contains 'prefabs') { Run 'prefabs' @('-executeMethod', 'Lighthouse.EditorTools.M3Tools.Step3_Prefabs') | Out-Null }
    if ($Steps -contains 'capture') {
        $a = @('-executeMethod', 'Lighthouse.EditorTools.M3Tools.Step4_Capture', '-m3Out', "$Out\m3"); if ($Res) { $a += @('-m3Res', $Res) }
        Run 'capture' $a | Out-Null
        if (Test-Path "$Out\m3\audit.txt") { Get-Content "$Out\m3\audit.txt" -Encoding UTF8 | Select-Object -First 80 } else { Write-Host 'capture: no audit'; if (Test-Path "$Out\m3\audit_error.txt") { Get-Content "$Out\m3\audit_error.txt" -Encoding UTF8 | Select-Object -First 20 } }
    }
    if ($Steps -contains 'edit') {
        $xml = "$Out\EditMode.xml"; Remove-Item $xml -ErrorAction SilentlyContinue
        $args2 = @('-nographics', '-runTests', '-testPlatform', 'EditMode', '-testResults', $xml)
        if ($Filter) { $args2 += @('-testFilter', $Filter) }
        Run 'test_EditMode' $args2 | Out-Null
        if (Test-Path $xml) {
            $r = ([xml](Get-Content $xml -Encoding UTF8)).'test-run'
            Write-Host "EditMode: total $($r.total) passed $($r.passed) failed $($r.failed) skipped $($r.skipped)"
            foreach ($c in $r.SelectNodes('//test-case[@result="Failed"]')) { Write-Host ("  FAILED: " + $c.name + " :: " + ($c.failure.message.'#cdata-section' -replace '\s+', ' ')) }
        } else { Write-Host "EditMode: no result file (compile error?)" }
    }
}
finally { Remove-Item $lock -ErrorAction SilentlyContinue }
