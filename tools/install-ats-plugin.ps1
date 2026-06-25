param(
    [string]$AtsInstallPath
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$nativePluginSource = Join-Path $repoRoot "build\telemetry-plugin\bin\Release\truckcompanion-telemetry.dll"
$fallbackPluginSource = Join-Path $repoRoot "src\TruckCompanion.Api\ThirdParty\Funbit\Ets2Plugins\win_x64\plugins\ets2-telemetry-server.dll"
$pluginSource = if (Test-Path $nativePluginSource) { $nativePluginSource } else { $fallbackPluginSource }
$pluginName = Split-Path -Leaf $pluginSource

if (-not (Test-Path $pluginSource)) {
    throw "Bundled telemetry plugin not found: $pluginSource"
}

$candidatePaths = @(
    $AtsInstallPath,
    "${env:ProgramFiles(x86)}\Steam\steamapps\common\American Truck Simulator",
    "$env:ProgramFiles\Steam\steamapps\common\American Truck Simulator",
    "C:\Program Files (x86)\Steam\steamapps\common\American Truck Simulator",
    "C:\Program Files\Steam\steamapps\common\American Truck Simulator"
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique

$atsRoot = $candidatePaths | Where-Object { Test-Path (Join-Path $_ "bin\win_x64") } | Select-Object -First 1

if (-not $atsRoot) {
    throw "American Truck Simulator install path was not found. Re-run with -AtsInstallPath ""C:\Path\To\American Truck Simulator""."
}

$destinationDirectory = Join-Path $atsRoot "bin\win_x64\plugins"
$destination = Join-Path $destinationDirectory $pluginName

New-Item -ItemType Directory -Force $destinationDirectory | Out-Null
Copy-Item -Force $pluginSource $destination

Write-Host "Installed TruckCompanion telemetry plugin:"
Write-Host $destination
if ($pluginSource -eq $fallbackPluginSource) {
    Write-Host "Warning: installed the legacy fallback plugin. Build the native plugin with tools\build-telemetry-plugin.ps1 when the SCS SDK is available."
}
Write-Host "Restart American Truck Simulator after installing the plugin."
