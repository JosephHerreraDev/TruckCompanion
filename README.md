# TruckCompanion

TruckCompanion is a local American Truck Simulator companion app. The backend reads live telemetry from a shared-memory ATS telemetry plugin or from built-in mock data, normalizes it, serves an ATS-coordinate map dataset, and streams everything to a phone/tablet-friendly PWA with drive and browse map modes.

## Projects

- `src/TruckCompanion.Api` - ASP.NET Core API and telemetry stream.
- `src/TruckCompanion.Web` - Vite, React, TypeScript, ATS-coordinate PWA.
- `tools/vendor/truckermudgeon-maps` - pinned upstream ATS map parser/generator used for PMTiles, search, and route graph data.
- `src/TruckCompanion.TileGenerator` - retired TsMap-based local ATS tile generator kept for reference only.
- `src/TruckCompanion.MapImport` - legacy local ATS-coordinate seed database generator.
- `src/TruckCompanion.TelemetryPlugin` - native telemetry plugin source scaffold.

## Run

Install the bundled ATS plugin once:

```powershell
.\tools\install-ats-plugin.ps1
```

If ATS is installed outside the default Steam location:

```powershell
.\tools\install-ats-plugin.ps1 -AtsInstallPath "D:\SteamLibrary\steamapps\common\American Truck Simulator"
```

Restart ATS after installing the plugin.

Generate local ATS map data. The active map workflow uses the pinned `truckermudgeon/maps` submodule under `tools/vendor/truckermudgeon-maps`, runs its parser/generator, and writes PMTiles/search/route-graph artifacts under `.truckcompanion-cache\ats-map-v2`:

```powershell
.\tools\generate-ats-tiles.ps1
```

Useful options:

```powershell
.\tools\generate-ats-tiles.ps1 -Force
.\tools\generate-ats-tiles.ps1 -SkipDocker
.\tools\generate-ats-tiles.ps1 -AtsInstallPath "D:\SteamLibrary\steamapps\common\American Truck Simulator"
```

Docker is used for `tippecanoe` PMTiles generation by default with `klokantech/tippecanoe:latest`. `-SkipDocker` is only useful when `ats.pmtiles` already exists or you are debugging parser/generator output. Generated data stays under `.truckcompanion-cache` and is ignored by git. The app serves generated map files at `/map/*` and reports readiness/staleness at `/api/map/status`.

One-command local restart:

```powershell
.\tools\restart-map.ps1
```

Backend:

```powershell
dotnet run --project .\src\TruckCompanion.Api
```

Frontend:

```powershell
cd .\src\TruckCompanion.Web
npm install
npm.cmd run dev
```

The frontend dev server proxies `/api` and `/stream` to the backend.

## Telemetry

The API defaults to the bundled plugin reader:

```powershell
dotnet user-secrets set "Telemetry:Mode" "plugin" --project .\src\TruckCompanion.Api
```

For UI development without ATS:

```powershell
dotnet user-secrets set "Telemetry:Mode" "mock" --project .\src\TruckCompanion.Api
```

The plugin is derived from Funbit's GPL-3.0 telemetry plugin and writes to `Local\Ets2TelemetryServer`. TruckCompanion reads that shared memory directly; no external Funbit server is required.

TruckCompanion also includes a new native plugin scaffold targeting `Local\TruckCompanionTelemetry`. Build it once the official SCS telemetry SDK headers are available:

```powershell
.\tools\build-telemetry-plugin.ps1 -ScsSdkDir "C:\Path\To\scs_sdk"
```

Debug raw telemetry:

```text
http://localhost:5000/api/telemetry/raw
```

Map endpoints:

```text
http://localhost:5000/api/map/status
http://localhost:5000/api/map/tile-manifest
http://localhost:5000/map/ats.pmtiles
http://localhost:5000/map/ats-search.geojson
http://localhost:5000/map/spritesheet.json
http://localhost:5000/map/spritesheet.png
http://localhost:5000/api/map/viewport?minX=-90000&minZ=60000&maxX=-50000&maxZ=90000&detail=3
http://localhost:5000/api/map/route?fromX=-80300&fromZ=73500&toCompany=Rail%20Export&toCity=Phoenix
http://localhost:5000/api/map/pois?kind=fuel
http://localhost:5000/api/route/stops
```
