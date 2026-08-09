<#
    Run a headless Unity Editor method (batchmode) — the generic driver behind
    the rig setup and the APK build, so no one has to click through the Editor.

    Usage:
        powershell -File ci/unity-run.ps1 -Method RoomPlanner.EditorTools.CiTools.SetupRig
        powershell -File ci/unity-run.ps1 -Method RoomPlanner.EditorTools.CiTools.BuildAndroid -TimeoutMin 30

    Requires the Editor to be CLOSED (batchmode needs the project lock).
    Exit code 0 = success.
#>
param(
    [Parameter(Mandatory = $true)] [string]$Method,
    [string]$Unity = 'D:\Unity\Editors\6000.0.81f1\Editor\Unity.exe',
    [int]$TimeoutMin = 20
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

if (-not (Test-Path $Unity)) { Write-Host "Unity not found at $Unity"; exit 1 }
if (Test-Path (Join-Path $root 'Temp\UnityLockfile')) {
    Write-Host "Unity Editor appears to be OPEN (Temp\UnityLockfile present). Close it and retry."
    exit 1
}

$short = $Method.Split('.')[-1]
$log = Join-Path $root "ci-$short.log"
if (Test-Path $log) { Remove-Item $log -Force }

Write-Host ("=== Unity batchmode: {0} ===" -f $Method)
# Unity.exe detaches when launched with '&'; Start-Process -Wait blocks properly.
$proc = Start-Process -FilePath $Unity -PassThru -ArgumentList @(
    '-batchmode', '-quit', '-projectPath', $root,
    '-executeMethod', $Method, '-logFile', $log)

if (-not $proc.WaitForExit($TimeoutMin * 60 * 1000)) {
    Write-Host ("TIMEOUT after {0} min - killing Unity" -f $TimeoutMin)
    try { $proc.Kill() } catch { }
    exit 1
}

$code = $proc.ExitCode
Write-Host ("Unity exit code: {0}" -f $code)

if (Test-Path $log) {
    $errors = Select-String -Path $log -Pattern 'error CS|\[CI\] .*failed|Exception:' -ErrorAction SilentlyContinue
    if ($errors) {
        Write-Host "--- problems found in log ---"
        $errors | Select-Object -First 25 | ForEach-Object { Write-Host $_.Line }
    }
    Write-Host "--- [CI] markers ---"
    Select-String -Path $log -Pattern '\[CI\]|\[Tools\]|\[Measure\]' -ErrorAction SilentlyContinue |
        Select-Object -Last 10 | ForEach-Object { Write-Host $_.Line }
}

exit $code
