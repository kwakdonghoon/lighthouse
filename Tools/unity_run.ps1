# Run Unity unattended (batchmode) for ek_lighthouse_unity. ASCII only (Windows PowerShell 5.1 reads BOM-less files as ANSI).
# Usage: powershell -File Tools\unity_run.ps1 [-Steps setup,edit,measure] [-Out <dir>]
# Output: <Out>\*.log, EditMode.xml, m0\m0_measure.txt, m0\scene_*.png. Fails if another Unity has this project open.
# Reference: patterned on ek_mukhyeop_unity\Tools\unity_run.ps1 (read-only reference, not modified).
param(
    [string[]]$Steps = @('edit'),
    [string]$Out = "$env:TEMP\lighthouse_unity_run",
    [int]$TimeoutSec = 540
)
$Steps = @($Steps | ForEach-Object { $_ -split ',' })
$unity = 'C:\Program Files\Unity\Hub\Editor\6000.3.19f1\Editor\Unity.exe'
$proj = (Resolve-Path "$PSScriptRoot\..").Path
New-Item -ItemType Directory $Out -Force | Out-Null

function Run($name, [string[]]$extra) {
    $log = "$Out\$name.log"; Remove-Item $log -ErrorAction SilentlyContinue
    $pr = Start-Process -FilePath $unity -ArgumentList (@('-batchmode', '-projectPath', $proj) + $extra + @('-logFile', $log)) -PassThru
    if (-not $pr.WaitForExit($TimeoutSec * 1000)) { $pr.Kill(); Write-Host "${name}: TIMEOUT"; return $false }
    $errs = Select-String -Path $log -Pattern 'error CS' -ErrorAction SilentlyContinue | Select-Object -First 8
    foreach ($e in $errs) { Write-Host ("  compile error: " + $e.Line.Substring(0, [Math]::Min(220, $e.Line.Length))) }
    Write-Host "${name}: exit code $($pr.ExitCode)"
    return ($pr.ExitCode -eq 0)
}

if ($Steps -contains 'setup') { Run 'setup' @('-quit', '-nographics', '-executeMethod', 'Lighthouse.EditorTools.ProjectSetup.RunAll') | Out-Null }
if ($Steps -contains 'edit') {
    $xml = "$Out\EditMode.xml"; Remove-Item $xml -ErrorAction SilentlyContinue
    Run 'test_EditMode' @('-nographics', '-runTests', '-testPlatform', 'EditMode', '-testResults', $xml) | Out-Null
    if (Test-Path $xml) {
        $r = ([xml](Get-Content $xml -Encoding UTF8)).'test-run'
        Write-Host "EditMode: total $($r.total) passed $($r.passed) failed $($r.failed) skipped $($r.skipped)"
        foreach ($c in $r.SelectNodes('//test-case[@result="Failed"]')) { Write-Host ("  FAILED: " + $c.name + " :: " + ($c.failure.message.'#cdata-section' -replace '\s+', ' ')) }
    } else { Write-Host "EditMode: no result file (compile error?)" }
}
if ($Steps -contains 'measure') {
    # needs graphics (no -nographics): renders the scene to a RenderTexture and saves PNGs
    Run 'measure' @('-quit', '-executeMethod', 'Lighthouse.EditorTools.M0Tools.Measure', '-m0Out', "$Out\m0") | Out-Null
    if (Test-Path "$Out\m0\m0_measure.txt") { Get-Content "$Out\m0\m0_measure.txt" -Encoding UTF8 } else { Write-Host "measure: no output" }
}
