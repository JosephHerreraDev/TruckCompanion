param(
    [string]$AtsInstallPath = "C:\Program Files (x86)\Steam\steamapps\common\American Truck Simulator",
    [string]$CacheRoot = ".\.truckcompanion-cache",
    [string]$ToolsRoot = ".\tools\vendor",
    [string]$TruckermudgeonMapsPath = "",
    [string]$TippecanoeImage = "klokantech/tippecanoe:latest",
    [switch]$Force,
    [switch]$SkipDocker,
    [switch]$SkipParserBuild
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path "."
$resolvedCacheRoot = Resolve-Path -Path (New-Item -ItemType Directory -Force -Path $CacheRoot)
$mapRoot = Join-Path $resolvedCacheRoot "ats-map-v2"
$parserOut = Join-Path $mapRoot "parser"
$generatedOut = Join-Path $mapRoot "generated"
$metadataPath = Join-Path $mapRoot "manifest.json"

if ([string]::IsNullOrWhiteSpace($TruckermudgeonMapsPath)) {
    $TruckermudgeonMapsPath = Join-Path (Resolve-Path -Path (New-Item -ItemType Directory -Force -Path $ToolsRoot)) "truckermudgeon-maps"
}

$mapsRoot = Resolve-Path $TruckermudgeonMapsPath
$nodeModules = Join-Path $mapsRoot "node_modules"
$installMarker = Join-Path $nodeModules ".truckcompanion-map-tools-installed"
$tsxBin = Join-Path $nodeModules ".bin\tsx.cmd"
$esbuildPlatformPackage = Join-Path $nodeModules "@esbuild\win32-x64"
$parserCli = Join-Path $mapsRoot "packages\clis\parser\index.ts"
$generatorCli = Join-Path $mapsRoot "packages\clis\generator\index.ts"
$extraLabels = Join-Path $mapsRoot "packages\clis\generator\resources\usa-labels.geojson"
$libdeflateSource = Join-Path $mapsRoot "packages\clis\parser\gdeflate\libdeflate\lib\deflate_decompress.c"
$extraLabelsSubmodule = Join-Path $mapsRoot "packages\clis\generator\resources\extra-labels"

if (!(Test-Path $AtsInstallPath)) {
    throw "ATS install path was not found: $AtsInstallPath"
}

if (!(Test-Path $mapsRoot)) {
    throw "truckermudgeon/maps was not found at $mapsRoot"
}

if (!(Test-Path (Join-Path $mapsRoot "package.json"))) {
    throw "truckermudgeon/maps package.json was not found under $mapsRoot"
}

function Invoke-MapsNpm {
    param([string[]]$Arguments)

    Push-Location $mapsRoot
    try {
        & npm.cmd @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "npm command failed with exit code ${LASTEXITCODE}: npm $($Arguments -join ' ')"
        }
    }
    finally {
        Pop-Location
    }
}

function Install-MapsDependencies {
    Write-Host "Installing truckermudgeon/maps parser/generator dependencies."
    Invoke-MapsNpm -Arguments @(
        "install",
        "--workspace",
        "@truckermudgeon/parser",
        "--workspace",
        "@truckermudgeon/generator",
        "--ignore-scripts"
    )

    New-Item -ItemType File -Force -Path $installMarker | Out-Null
}

function Invoke-Tool {
    param(
        [string]$Command,
        [string[]]$Arguments
    )

    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Command failed with exit code $LASTEXITCODE."
    }
}

function Invoke-MapsCli {
    param(
        [string]$Cli,
        [string[]]$Arguments
    )

    if (!(Test-Path $tsxBin)) {
        throw "tsx was not found at $tsxBin. Re-run this script to install truckermudgeon/maps parser/generator dependencies."
    }

    Push-Location $mapsRoot
    $previousNodeOptions = $env:NODE_OPTIONS
    try {
        $heapOption = "--max-old-space-size=8192"
        if ([string]::IsNullOrWhiteSpace($env:NODE_OPTIONS)) {
            $env:NODE_OPTIONS = $heapOption
        }
        elseif ($env:NODE_OPTIONS -notmatch "--max-old-space-size=") {
            $env:NODE_OPTIONS = "$previousNodeOptions $heapOption"
        }

        & $tsxBin $Cli @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "$Cli failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        $env:NODE_OPTIONS = $previousNodeOptions
        Pop-Location
    }
}

function Assert-WindowsNativeBuildTools {
    $vsRoot = "C:\Program Files\Microsoft Visual Studio\18\Community"
    $vcvarsAll = Join-Path $vsRoot "VC\Auxiliary\Build\vcvarsall.bat"
    $cl = Get-ChildItem -Path (Join-Path $vsRoot "VC\Tools\MSVC") -Recurse -Filter "cl.exe" -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -like "*\bin\Hostx64\x64\cl.exe" } |
        Select-Object -First 1
    $windowsHeader = Get-ChildItem -Path "C:\Program Files (x86)\Windows Kits" -Recurse -Filter "Windows.h" -ErrorAction SilentlyContinue |
        Select-Object -First 1

    $missing = @()
    if (!(Test-Path $vcvarsAll)) {
        $missing += $vcvarsAll
    }
    if ($null -eq $cl) {
        $missing += "MSVC x64 compiler (cl.exe)"
    }
    if ($null -eq $windowsHeader) {
        $missing += "Windows SDK headers (Windows.h)"
    }

    if ($missing.Count -gt 0) {
        throw @"
Visual Studio native build tools are incomplete, so truckermudgeon/maps cannot build its parser addon.

Missing:
  $($missing -join "`n  ")

Install the Visual Studio "Desktop development with C++" workload and a Windows SDK, then rerun:
  .\tools\restart-map.ps1

Command-line installer option:
  & "C:\Program Files (x86)\Microsoft Visual Studio\Installer\vs_installer.exe" modify --installPath "C:\Program Files\Microsoft Visual Studio\18\Community" --add Microsoft.VisualStudio.Workload.NativeDesktop --includeRecommended
"@
    }
}

function Assert-MapsSubmodules {
    $missing = @()
    if (!(Test-Path $libdeflateSource)) {
        $missing += "packages/clis/parser/gdeflate/libdeflate"
    }
    if (!(Test-Path $extraLabelsSubmodule)) {
        $missing += "packages/clis/generator/resources/extra-labels"
    }

    if ($missing.Count -gt 0) {
        throw @"
truckermudgeon/maps nested submodules are missing.

Missing:
  $($missing -join "`n  ")

Populate them, then rerun:
  git -C tools\vendor\truckermudgeon-maps submodule update --init --recursive
  .\tools\restart-map.ps1
"@
    }
}

function Get-MapFingerprint {
    param([string]$Path)

    $files = @("base.scs", "def.scs", "base_map.scs") +
        (Get-ChildItem -Path $Path -Filter "dlc*.scs" -File | Sort-Object Name | Select-Object -ExpandProperty Name)
    $builder = New-Object System.Text.StringBuilder
    [void]$builder.AppendLine("truckermudgeon-maps=d56d0e3fb319230e84284f3029f8bda2c4b572a2")

    foreach ($name in ($files | Select-Object -Unique)) {
        $filePath = Join-Path $Path $name
        if (Test-Path $filePath) {
            $file = Get-Item $filePath
            [void]$builder.AppendLine("$name|$($file.Length)|$($file.LastWriteTimeUtc.ToString("O"))")
        }
    }

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($builder.ToString())
    $hash = [System.Security.Cryptography.SHA256]::HashData($bytes)
    return [Convert]::ToHexString($hash).ToLowerInvariant()
}

function Write-MapManifest {
    param([string]$Fingerprint)

    $manifest = [ordered]@{
        schemaVersion = 2
        source = "truckermudgeon/maps d56d0e3fb319230e84284f3029f8bda2c4b572a2"
        generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
        atsInstallPath = (Resolve-Path $AtsInstallPath).ToString()
        mapFingerprint = $Fingerprint
        parserOutput = $parserOut
        generatedOutput = $generatedOut
        pmtilesPath = (Join-Path $generatedOut "ats.pmtiles")
        graphPath = (Join-Path $generatedOut "usa-graph.json")
        searchPath = (Join-Path $generatedOut "ats-search.geojson")
        spritesheetJsonPath = (Join-Path $generatedOut "sprites.json")
        spritesheetImagePath = (Join-Path $generatedOut "sprites.png")
    }

    $manifest | ConvertTo-Json -Depth 5 | Set-Content -Path $metadataPath -Encoding UTF8
}

$fingerprint = Get-MapFingerprint -Path $AtsInstallPath
$existingManifest = $null
if (Test-Path $metadataPath) {
    try {
        $existingManifest = Get-Content $metadataPath -Raw | ConvertFrom-Json
    }
    catch {
        $existingManifest = $null
    }
}

$requiredOutputs = @(
    (Join-Path $generatedOut "ats.pmtiles"),
    (Join-Path $generatedOut "usa-graph.json"),
    (Join-Path $generatedOut "ats-search.geojson"),
    (Join-Path $generatedOut "sprites.json"),
    (Join-Path $generatedOut "sprites.png")
)

$outputsReady = ($requiredOutputs | Where-Object { !(Test-Path $_) }).Count -eq 0
if (!$Force -and $outputsReady -and (!$existingManifest -or $existingManifest.mapFingerprint -eq $fingerprint)) {
    if (!$existingManifest) {
        Write-MapManifest -Fingerprint $fingerprint
    }

    Write-Host "ATS map v2 data is up to date."
    Write-Host "Map root: $mapRoot"
    Write-Host "Fingerprint: $fingerprint"
    return
}

New-Item -ItemType Directory -Force -Path $parserOut | Out-Null
New-Item -ItemType Directory -Force -Path $generatedOut | Out-Null

Assert-MapsSubmodules

if (!(Test-Path $installMarker) -or !(Test-Path $tsxBin) -or !(Test-Path $esbuildPlatformPackage)) {
    Install-MapsDependencies
}

if (!$SkipParserBuild) {
    Assert-WindowsNativeBuildTools
    Write-Host "Building truckermudgeon/maps parser native addon."
    try {
        Invoke-MapsNpm -Arguments @("run", "build", "-w", "@truckermudgeon/parser")
    }
    catch {
        throw @"
Failed to build the truckermudgeon/maps parser native addon.

Check the node-gyp output above for the specific compiler or source-file error. Common fixes are installing the Visual Studio "Desktop development with C++" workload and populating truckermudgeon/maps nested submodules:
  git -C tools\vendor\truckermudgeon-maps submodule update --init --recursive

Then rerun:
  .\tools\restart-map.ps1

Original error:
$($_.Exception.Message)
"@
    }
}

Write-Host "Parsing ATS map archives with truckermudgeon/maps."
Invoke-MapsCli -Cli $parserCli -Arguments @("-i", $AtsInstallPath, "-o", $parserOut)

Write-Host "Generating ATS GeoJSON."
Invoke-MapsCli -Cli $generatorCli -Arguments @("map", "-m", "usa", "-i", $parserOut, "-o", $generatedOut, "-t", "geojson")

Write-Host "Generating ATS route graph."
Invoke-MapsCli -Cli $generatorCli -Arguments @("graph", "-m", "usa", "-i", $parserOut, "-o", $generatedOut)

if (!(Test-Path $extraLabels)) {
    Write-Warning "USA extra label data was not found at $extraLabels. Creating an empty label collection for search generation."
    $extraLabels = Join-Path $generatedOut "usa-labels-empty.geojson"
    '{"type":"FeatureCollection","features":[]}' | Set-Content -Path $extraLabels -Encoding UTF8
}

Write-Host "Generating ATS search data."
Invoke-MapsCli -Cli $generatorCli -Arguments @("search", "-m", "usa", "-i", $parserOut, "-o", $generatedOut, "-x", $extraLabels)

Write-Host "Generating MapLibre spritesheet."
Invoke-MapsCli -Cli $generatorCli -Arguments @("spritesheet", "-m", "usa", "-i", $parserOut, "-o", $generatedOut)

$geoJsonPath = Join-Path $generatedOut "ats.geojson"
$pmTilesPath = Join-Path $generatedOut "ats.pmtiles"
if (!(Test-Path $geoJsonPath)) {
    throw "Expected ATS GeoJSON was not found: $geoJsonPath"
}

if (!$SkipDocker) {
    Write-Host "Generating ATS PMTiles with Dockerized tippecanoe."
    $generatedDockerPath = ($generatedOut -replace "\\", "/")
    docker run --rm `
        -v "${generatedDockerPath}:/data" `
        --entrypoint tippecanoe `
        $TippecanoeImage `
        -Z4 -z13 -B 4 -b 10 --force -o /data/ats.pmtiles /data/ats.geojson

    if ($LASTEXITCODE -ne 0) {
        throw "Dockerized tippecanoe failed with exit code $LASTEXITCODE."
    }
}
elseif (!(Test-Path $pmTilesPath)) {
    Write-Warning "Skipping Dockerized tippecanoe and ats.pmtiles does not exist yet."
}

Write-MapManifest -Fingerprint $fingerprint

Write-Host "Generated ATS map v2 data."
Write-Host "Map root: $mapRoot"
Write-Host "Manifest: $metadataPath"
