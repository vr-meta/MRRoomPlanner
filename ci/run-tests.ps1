<#
    Headless Unity test runner (EditMode + PlayMode) via batchmode.
    Lets tests run from the command line / CI / an agent without opening the Editor.

    Usage (from anywhere):
        powershell -File ci/run-tests.ps1               # both EditMode + PlayMode
        powershell -File ci/run-tests.ps1 -Mode EditMode
        powershell -File ci/run-tests.ps1 -Mode PlayMode

    Requires the Editor to be CLOSED (batchmode can't take the project lock while
    the Editor holds it). Exit code 0 = all passed, 1 = failures / run problem.
#>
param(
    [ValidateSet('EditMode', 'PlayMode', 'All')] [string]$Mode = 'All',
    [string]$Unity = 'D:\Unity\Editors\6000.0.81f1\Editor\Unity.exe'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot   # repo root (this script lives in ci/)

if (-not (Test-Path $Unity)) { Write-Host "Unity not found at $Unity" ; exit 1 }
if (Test-Path (Join-Path $root 'Temp\UnityLockfile')) {
    Write-Host "Unity Editor appears to be OPEN (Temp\UnityLockfile present). Close it and retry."
    exit 1
}

$platforms = if ($Mode -eq 'All') { @('EditMode', 'PlayMode') } else { @($Mode) }
$overall = 0

foreach ($p in $platforms) {
    $xml = Join-Path $root "TestResults-$p.xml"
    $log = Join-Path $root "ci-$p.log"
    if (Test-Path $xml) { Remove-Item $xml -Force }

    Write-Host "=== Running $p tests (batchmode) ==="
    # Unity.exe is a GUI-subsystem app: launched with '&' it detaches and the shell continues.
    # Start-Process -Wait blocks until the batchmode run actually finishes.
    $proc = Start-Process -FilePath $Unity -PassThru -Wait -ArgumentList @(
        '-runTests', '-batchmode', '-projectPath', $root,
        '-testPlatform', $p, '-testResults', $xml, '-logFile', $log)
    $code = $proc.ExitCode

    if (Test-Path $xml) {
        [xml]$r = Get-Content $xml
        $tr = $r.'test-run'
        Write-Host ("{0}: total={1} passed={2} failed={3} skipped={4} (exit {5})" -f `
            $p, $tr.total, $tr.passed, $tr.failed, $tr.skipped, $code)
        if ([int]$tr.failed -gt 0) {
            $overall = 1
            $r.SelectNodes("//test-case[@result='Failed']") | ForEach-Object {
                Write-Host ("  FAIL: " + $_.fullname)
            }
        }
    }
    else {
        Write-Host ("{0}: no results produced (exit {1}). Tail of {2}:" -f $p, $code, $log)
        if (Test-Path $log) { Get-Content $log -Tail 25 }
        $overall = 1
    }
}

if ($overall -eq 0) { Write-Host "ALL TESTS PASSED" } else { Write-Host "TEST FAILURES / PROBLEMS" }
exit $overall
