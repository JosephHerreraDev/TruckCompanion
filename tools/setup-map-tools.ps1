param(
    [string]$ToolsRoot = ".\tools\vendor",
    [string]$ScsExtractorPath = ".\tools\vendor\scs-extractor\scs_extractor.exe",
    [string]$TsMapPath = ".\tools\vendor\ts-map"
)

$ErrorActionPreference = "Stop"

$resolvedToolsRoot = Resolve-Path -Path (New-Item -ItemType Directory -Force -Path $ToolsRoot)
if (!(Test-Path $ScsExtractorPath)) {
    throw "SCS Extractor was not found: $ScsExtractorPath"
}

if (!(Test-Path $TsMapPath)) {
    throw "TsMap path was not found: $TsMapPath"
}

$scsExtractor = Resolve-Path $ScsExtractorPath
$tsMapSource = Resolve-Path $TsMapPath

Write-Host "TruckCompanion map tool setup"
Write-Host "Tools root: $resolvedToolsRoot"
Write-Host ""
Write-Host "Expected local tools:"
Write-Host "  SCS Extractor: $scsExtractor"
Write-Host "  TsMap source/build: $tsMapSource"
Write-Host ""
Write-Host "Map tools are ready."
