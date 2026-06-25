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
    [int]$ApiPort = 5000,
    [int]$WebPort = 5173,
    [switch]$ForceTiles,
    [switch]$ExtractArchives,
    [switch]$SkipTileGeneration,
    [switch]$SkipNpmInstall
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot "..")
$webRoot = Join-Path $repoRoot "src\TruckCompanion.Web"
$apiProject = Join-Path $repoRoot "src\TruckCompanion.Api\TruckCompanion.Api.csproj"
$setupScript = Join-Path $repoRoot "tools\setup-map-tools.ps1"
$tileScript = Join-Path $repoRoot "tools\generate-ats-tiles.ps1"

function Invoke-Step {
    param(
        [string]$Name,
        [scriptblock]$Action
    )

    Write-Host ""
    Write-Host "==> $Name"
    & $Action
}

function Get-PortListeners {
    param([int[]]$Ports)

    $listeners = @()
    foreach ($port in $Ports) {
        $connections = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue
        foreach ($connection in $connections) {
            $process = Get-CimInstance Win32_Process -Filter "ProcessId = $($connection.OwningProcess)" -ErrorAction SilentlyContinue
            if ($process) {
                $listeners += [pscustomobject]@{
                    Port = $port
                    ProcessId = $process.ProcessId
                    Name = $process.Name
                    CommandLine = $process.CommandLine
                }
            }
        }
    }

    return $listeners
}

function Stop-TruckCompanionListeners {
    param([int[]]$Ports)

    $repoRootText = $repoRoot.ToString()
    $listeners = Get-PortListeners -Ports $Ports

    foreach ($listener in $listeners) {
        $commandLine = [string]$listener.CommandLine
        $isTruckCompanionProcess =
            $commandLine.IndexOf($repoRootText, [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
            $commandLine.IndexOf("TruckCompanion.Api", [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
            $commandLine.IndexOf("truckcompanion-web", [StringComparison]::OrdinalIgnoreCase) -ge 0

        if (!$isTruckCompanionProcess) {
            Write-Warning "Port $($listener.Port) is in use by $($listener.Name) ($($listener.ProcessId)); leaving it running because it does not look like TruckCompanion."
            continue
        }

        Write-Host "Stopping $($listener.Name) ($($listener.ProcessId)) on port $($listener.Port)."
        Stop-Process -Id $listener.ProcessId -Force
    }
}

function Start-DetachedPowerShell {
    param(
        [string]$Title,
        [string]$Command,
        [string]$WorkingDirectory
    )

    $encodedCommand = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes(@"
`$Host.UI.RawUI.WindowTitle = '$Title'
Set-Location -LiteralPath '$WorkingDirectory'
$Command
"@))

    Start-Process powershell.exe -ArgumentList @(
        "-NoExit",
        "-ExecutionPolicy",
        "Bypass",
        "-EncodedCommand",
        $encodedCommand
    ) | Out-Null
}

Invoke-Step "Verify map tools" {
    & $setupScript -ToolsRoot $ToolsRoot
}

if (!$SkipTileGeneration) {
    Invoke-Step "Generate ATS map tiles" {
        $tileArgs = @{
            AtsInstallPath = $AtsInstallPath
            CacheRoot = $CacheRoot
            ToolsRoot = $ToolsRoot
            MinZoom = $MinZoom
            MaxZoom = $MaxZoom
            TileSize = $TileSize
            MapPadding = $MapPadding
            RenderFlags = $RenderFlags
        }

        if ($Mods) {
            $tileArgs.Mods = $Mods
        }

        if ($ForceTiles) {
            $tileArgs.Force = $true
        }

        if ($ExtractArchives) {
            $tileArgs.ExtractArchives = $true
        }

        & $tileScript @tileArgs
    }
}
else {
    Write-Host "Skipping tile generation."
}

Invoke-Step "Prepare frontend dependencies" {
    if ((Test-Path (Join-Path $webRoot "node_modules")) -or $SkipNpmInstall) {
        Write-Host "Frontend dependencies are already installed or npm install was skipped."
    }
    else {
        Push-Location $webRoot
        try {
            npm.cmd install
        }
        finally {
            Pop-Location
        }
    }
}

Invoke-Step "Restart local map servers" {
    Stop-TruckCompanionListeners -Ports @($ApiPort, $WebPort)

    Start-DetachedPowerShell `
        -Title "TruckCompanion API" `
        -WorkingDirectory $repoRoot `
        -Command "dotnet run --project '$apiProject' -- --urls 'http://localhost:$ApiPort'"

    Start-DetachedPowerShell `
        -Title "TruckCompanion Web" `
        -WorkingDirectory $webRoot `
        -Command "npm.cmd run dev -- --host 0.0.0.0 --port $WebPort"
}

Write-Host ""
Write-Host "TruckCompanion map restart requested."
Write-Host "API: http://localhost:$ApiPort"
Write-Host "Web: http://localhost:$WebPort"
