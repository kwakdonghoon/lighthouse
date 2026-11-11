# M2 render run (batchmode). ASCII only. Usage: powershell -File Tools\unity_run_m2.ps1 [-Steps edit,m2] [-Out <dir>]
# Takes Tools\unity.lock (exclusive; waits up to 20 min, 30 s polls) because two batchmode Unity cannot open the same project. Always removes the lock at the end.
# edit : EditMode tests (-nographics), result XML in <Out>\EditMode.xml
# m2   : Lighthouse.EditorTools.M2Tools.Run (needs graphics): GPU frame compare, bake timing/memory, PNG sheets, scene M2Showcase.unity
param(
    [string[]]$Steps = @('edit', 'm2'),
    [string]$Out = "$PSScriptRoot\m2_results",
    [int]$TimeoutSec = 540,
    [string]$Filter = ''
)
$Steps = @($Steps | ForEach-Object { $_ -split ',' })
$unity = 'C:\Program Files\Unity\Hub\Editor\6000.3.19f1\Editor\Unity.exe'
$proj = (Resolve-Path "$PSScriptRoot\..").Path
$lock = "$PSScriptRoot\unity.lock"
New-Item -ItemType Directory $Out -Force | Out-Null

$got = $false
for ($i = 0; $i -lt 40; $i++) {
    try { $fs = [System.IO.File]::Open($lock, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None); $b = [Text.Encoding]::ASCII.GetBytes("M2 render agent $(Get-Date -Format s)"); $fs.Write($b, 0, $b.Length); $fs.Close(); $got = $true; break }
    catch { Write-Host "lock busy, waiting 30 s ($i)"; Start-Sleep -Seconds 30 }
}
if (-not $got) { Write-Host 'could not get unity.lock'; exit 2 }
try {
    function Run($name, [string[]]$extra) {
        $log = "$Out\$name.log"; Remove-Item $log -ErrorAction SilentlyContinue
        $pr = Start-Process -FilePath $unity -ArgumentList (@('-batchmode', '-projectPath', $proj) + $extra + @('-logFile', $log)) -PassThru
        if (-not $pr.WaitForExit($TimeoutSec * 1000)) { $pr.Kill(); Write-Host "${name}: TIMEOUT"; return $false }
        $errs = Select-String -Path $log -Pattern 'error CS' -ErrorAction SilentlyContinue | Select-Object -First 8
        foreach ($e in $errs) { Write-Host ("  compile error: " + $e.Line.Substring(0, [Math]::Min(240, $e.Line.Length))) }
        Write-Host "${name}: exit code $($pr.ExitCode)"
        return ($pr.ExitCode -eq 0)
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
    if ($Steps -contains 'm2') {
        Run 'm2' @('-quit', '-executeMethod', 'Lighthouse.EditorTools.M2Tools.Run', '-m2Out', "$Out\m2") | Out-Null
        if (Test-Path "$Out\m2\m2_measure.txt") { Get-Content "$Out\m2\m2_measure.txt" -Encoding UTF8 } else { Write-Host "m2: no output" }
    }
}
finally { Remove-Item $lock -ErrorAction SilentlyContinue }
