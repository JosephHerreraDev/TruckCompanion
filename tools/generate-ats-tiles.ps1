param(
    [string]$AtsInstallPath = "C:\Program Files (x86)\Steam\steamapps\common\American Truck Simulator",
    [string]$CacheRoot = ".\.truckcompanion-cache",
    [string]$ToolsRoot = ".\tools\vendor",
    [int]$MinZoom = 0,
    [int]$MaxZoom = 7,
    [int]$TileSize = 512,
    [int]$MapPadding = 500,
    [string]$RenderFlags = "Prefabs,Roads,MapAreas,MapOverlays,FerryConnections,CityNames,SecretRoads",
    [string]$Mods = "",
    [switch]$Force,
    [switch]$ExtractArchives
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path "."
$resolvedCacheRoot = Resolve-Path -Path (New-Item -ItemType Directory -Force -Path $CacheRoot)
$tileRoot = Join-Path $resolvedCacheRoot "ats-tiles"
$toolsRootResolved = Resolve-Path -Path (New-Item -ItemType Directory -Force -Path $ToolsRoot)
$scsExtractor = Join-Path $toolsRootResolved "scs-extractor\scs_extractor.exe"
$tileGeneratorProject = Join-Path $repoRoot "src\TruckCompanion.TileGenerator\TruckCompanion.TileGenerator.csproj"

if (!(Test-Path $AtsInstallPath)) {
    throw "ATS install path was not found: $AtsInstallPath"
}

if (!(Test-Path $tileGeneratorProject)) {
    throw "Tile generator project was not found: $tileGeneratorProject"
}

$archives = @(
    "base.scs",
    "def.scs",
    "base_map.scs"
) + (Get-ChildItem -Path $AtsInstallPath -Filter "dlc*.scs" -File | Select-Object -ExpandProperty Name)

$archives = $archives | Where-Object { Test-Path (Join-Path $AtsInstallPath $_) } | Select-Object -Unique

Write-Host "ATS install: $AtsInstallPath"
Write-Host "Cache root: $resolvedCacheRoot"
Write-Host "Found $($archives.Count) archive(s)."

if ($ExtractArchives) {
    if (!(Test-Path $scsExtractor)) {
        throw "SCS Extractor was not found at $scsExtractor. Run tools\setup-map-tools.ps1 or pass -ToolsRoot."
    }

    $extractRoot = Join-Path $resolvedCacheRoot "ats-extracted"
    New-Item -ItemType Directory -Force -Path $extractRoot | Out-Null

    Write-Warning "Diagnostic extraction requested. This is slow and is not needed for normal tile generation."
    foreach ($archive in $archives) {
        $archivePath = Join-Path $AtsInstallPath $archive
        $archiveOut = Join-Path $extractRoot ([IO.Path]::GetFileNameWithoutExtension($archive))
        New-Item -ItemType Directory -Force -Path $archiveOut | Out-Null
        Write-Host "Extracting $archive"
        & $scsExtractor $archivePath $archiveOut
    }
}

$generatorArgs = @(
    "--ats-path", $AtsInstallPath,
    "--output-root", $tileRoot,
    "--min-zoom", $MinZoom,
    "--max-zoom", $MaxZoom,
    "--tile-size", $TileSize,
    "--map-padding", $MapPadding,
    "--render-flags", $RenderFlags
)

if ($Mods) {
    $generatorArgs += @("--mods", $Mods)
}

if ($Force) {
    $generatorArgs += "--force"
}

Write-Host "Generating ATS tiles with TruckCompanion.TileGenerator."
Write-Host "Tile root: $tileRoot"

dotnet run --project $tileGeneratorProject -- @generatorArgs

if ($LASTEXITCODE -ne 0) {
    throw "Tile generation failed with exit code $LASTEXITCODE."
}
