# Full-content release: local APK build (includes the laminate bakes, which CI
# cannot have — proprietary sources) + GitHub Release with the APK attached.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File ci/release.ps1 -Version v1.0.0
#
# Requires: Unity Editor CLOSED (batchmode), gh CLI authenticated.
param(
    [Parameter(Mandatory = $true)][string]$Version,   # tag, e.g. v1.0.0
    [string]$Notes = ""
)

$ErrorActionPreference = "Stop"

if ($Version -notmatch '^v\d+\.\d+\.\d+$') {
    Write-Error "Version must look like v1.2.3 (got '$Version')"
    exit 1
}

# Release builds must contain the full material set — refuse to ship without laminate.
$lam = @(Get-ChildItem "Assets/RoomPlanner/Textures/Laminate" -Filter *.png -ErrorAction SilentlyContinue)
if ($lam.Count -lt 15) {
    Write-Error "Laminate bake incomplete ($($lam.Count) png) - run RoomPlanner > Bake Laminate first (sources in D:\Maps)."
    exit 1
}

Write-Host "[release] building APK ($Version)..."
& powershell -NoProfile -ExecutionPolicy Bypass -File ci/unity-run.ps1 -Method RoomPlanner.EditorTools.CiTools.BuildAndroid -TimeoutMin 30
if ($LASTEXITCODE -ne 0) {
    Write-Error "[release] build failed"
    exit 1
}
if (-not (Test-Path "Build/MRRoomPlanner.apk")) {
    Write-Error "[release] Build/MRRoomPlanner.apk not found after build"
    exit 1
}

Write-Host "[release] creating GitHub release $Version..."
if ([string]::IsNullOrEmpty($Notes)) {
    gh release create $Version "Build/MRRoomPlanner.apk" --title "MR Room Planner $Version" --generate-notes
} else {
    gh release create $Version "Build/MRRoomPlanner.apk" --title "MR Room Planner $Version" --notes $Notes
}
if ($LASTEXITCODE -ne 0) {
    Write-Error "[release] gh release create failed"
    exit 1
}
Write-Host "[release] done: $Version"
