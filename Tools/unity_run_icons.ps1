# UI icon capture runs (batchmode). ASCII only.
# Usage: powershell -File Tools\unity_run_icons.ps1 -Tag before -Dprs 1,2,2.625,2.75,3 [-Res 320x568,393x852,tablet768x1024,tabletland1024x768] [-States settings,main,...]
# Takes Tools\unity.lock (exclusive; waits up to 20 min, 30 s polls) and always removes it at the end.
param(
    [string]$Tag = 'before',
    [string[]]$Dprs = @('2'),
    [string]$Res = '320x568,393x852,tablet768x1024,tabletland1024x768',
    [string]$States = 'settings,main,mail,growth,deco,shop',
    [int]$TimeoutSec = 900
)
$Dprs = @($Dprs | ForEach-Object { $_ -split ',' })
$unity = 'C:\Program Files\Unity\Hub\Editor\6000.3.19f1\Editor\Unity.exe'
$proj = (Resolve-Path "$PSScriptRoot\..").Path
$lock = "$PSScriptRoot\unity.lock"
$got = $false
for ($i = 0; $i -lt 60; $i++) {
    try { $fs = [System.IO.File]::Open($lock, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None); $b = [Text.Encoding]::ASCII.GetBytes("icons agent $(Get-Date -Format s)"); $fs.Write($b, 0, $b.Length); $fs.Close(); $got = $true; break }
    catch { Write-Host "lock busy, waiting 30 s ($i)"; Start-Sleep -Seconds 30 }
}
if (-not $got) { Write-Host 'could not get unity.lock'; exit 2 }
try {
    foreach ($d in $Dprs) {
        $out = "$proj\Tools\ui_icon_results\$Tag\dpr$d"
        New-Item -ItemType Directory $out -Force | Out-Null
        $log = "$out\capture.log"; Remove-Item $log -ErrorAction SilentlyContinue
        $a = @('-batchmode', '-projectPath', $proj, '-executeMethod', 'Lighthouse.EditorTools.M3Tools.Step4_Capture', '-m3Out', $out, '-m3Res', $Res, '-m3Dpr', $d, '-m3States', $States, '-logFile', $log)
        $pr = Start-Process -FilePath $unity -ArgumentList $a -PassThru
        if (-not $pr.WaitForExit($TimeoutSec * 1000)) { $pr.Kill(); Write-Host "dpr ${d}: TIMEOUT"; continue }
        Write-Host "dpr ${d}: exit $($pr.ExitCode)"
        Select-String -Path $log -Pattern 'error CS' -ErrorAction SilentlyContinue | Select-Object -First 8 | ForEach-Object { Write-Host ("  " + $_.Line) }
        if (Test-Path "$out\audit.txt") { Get-Content "$out\audit.txt" -Encoding UTF8 | Select-Object -First 6 }
    }
}
finally { Remove-Item $lock -ErrorAction SilentlyContinue }
