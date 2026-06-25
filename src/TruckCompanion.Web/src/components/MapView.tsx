import { useEffect, useMemo, useRef, useState } from "react";
import L from "leaflet";
import { BriefcaseBusiness, Crosshair, Fuel, LocateFixed, Map, MapPinned, Navigation, Plus, Settings, Trash2, Wrench, X } from "lucide-react";
import {
  addRouteStop,
  getMapPois,
  getMapRoute,
  getTileMapManifest,
  getTileMapStatus,
  removeRouteStop
} from "../api/client";
import { useWakeLock } from "../hooks/useWakeLock";
import type {
  AtsMapCoordinate,
  AtsMapPoint,
  AtsMapRoute,
  TelemetrySnapshot,
  TileMapManifest,
  TileMapStatus
} from "../types";

type Props = {
  snapshot: TelemetrySnapshot | null;
};

type LeafletState = {
  map: L.Map;
  truckMarker: L.Marker;
  routeLine: L.Polyline;
  routeArrowLayer: L.LayerGroup;
  poiLayer: L.LayerGroup;
  stopLayer: L.LayerGroup;
};

const FALLBACK_CENTER = { atsX: -38611, atsZ: -5261 };
const ROUTE_REROUTE_DISTANCE = 750;
const DEFAULT_POI_FILTERS = {
  fuel: true,
  repair: true,
  business: true,
  city: true
};

export function MapView({ snapshot }: Props) {
  const mapElement = useRef<HTMLDivElement | null>(null);
  const leaflet = useRef<LeafletState | null>(null);
  const lastRouteOrigin = useRef<AtsMapCoordinate | null>(null);
  const lastRouteKey = useRef<string | null>(null);
  const routeRequestId = useRef(0);
  const [manifest, setManifest] = useState<TileMapManifest | null>(null);
  const [tileStatus, setTileStatus] = useState<TileMapStatus | null>(null);
  const [tileError, setTileError] = useState<string | null>(null);
  const [points, setPoints] = useState<AtsMapPoint[]>([]);
  const [route, setRoute] = useState<AtsMapRoute | null>(null);
  const [follow, setFollow] = useState(true);
  const [settingsOpen, setSettingsOpen] = useState(false);
  const [selectedPoint, setSelectedPoint] = useState<AtsMapPoint | null>(null);
  const [routeVersion, setRouteVersion] = useState(0);
  const [units, setUnits] = useState<"metric" | "imperial">("metric");
  const [mapMode, setMapMode] = useState<"drive" | "map">("drive");
  const [wakeLockEnabled, setWakeLockEnabled] = useState(true);
  const [poiFilters, setPoiFilters] = useState(DEFAULT_POI_FILTERS);
  const wakeLock = useWakeLock(wakeLockEnabled);

  const projection = useMemo(() => manifest ? createProjection(manifest) : null, [manifest]);
  const visiblePoints = useMemo(
    () => points.filter((point) => shouldShowPoint(point, poiFilters, mapMode)),
    [points, poiFilters, mapMode]
  );
  const truckCoordinate = toCoordinate(getMapCenter(snapshot));
  const truckCoordinateRef = useRef(truckCoordinate);

  useEffect(() => {
    truckCoordinateRef.current = truckCoordinate;
  }, [truckCoordinate.x, truckCoordinate.z]);

  useEffect(() => {
    let active = true;

    Promise.allSettled([getTileMapStatus(), getTileMapManifest(), getMapPois()])
      .then(([statusResult, manifestResult, pointsResult]) => {
        if (!active) {
          return;
        }

        if (statusResult.status === "fulfilled") {
          setTileStatus(statusResult.value);
        }

        if (manifestResult.status === "fulfilled") {
          setManifest(manifestResult.value);
          setTileError(null);
        } else {
          setManifest(null);
          setTileError("Generated ATS tiles are missing. Run tools\\generate-ats-tiles.ps1.");
        }

        if (pointsResult.status === "fulfilled") {
          setPoints(pointsResult.value);
        }
      });

    return () => {
      active = false;
    };
  }, []);

  useEffect(() => {
    if (!snapshot) {
      return;
    }

    const origin = toCoordinate(snapshot.truck.position);
    const routeKey = [
      snapshot.game.connected,
      snapshot.job.destination.city,
      snapshot.job.destination.company,
      routeVersion
    ].join("|");
    const movedFarEnough = !lastRouteOrigin.current || distance(lastRouteOrigin.current, origin) >= ROUTE_REROUTE_DISTANCE;
    const routeInputsChanged = lastRouteKey.current !== routeKey;

    if (!movedFarEnough && !routeInputsChanged) {
      return;
    }

    const requestId = routeRequestId.current + 1;
    routeRequestId.current = requestId;
    lastRouteOrigin.current = origin;
    lastRouteKey.current = routeKey;

    getMapRoute(snapshot)
      .then((nextRoute) => {
        if (routeRequestId.current !== requestId) {
          return;
        }

        setRoute(nextRoute);
      })
      .catch(() => {
        if (routeRequestId.current === requestId) {
          setRoute(null);
        }
      });
  }, [
    snapshot?.game.connected,
    snapshot?.truck.position.atsX,
    snapshot?.truck.position.atsZ,
    snapshot?.job.destination.city,
    snapshot?.job.destination.company,
    routeVersion
  ]);

  useEffect(() => {
    if (!manifest || !projection || !mapElement.current || leaflet.current) {
      return;
    }

    const map = L.map(mapElement.current, {
      attributionControl: false,
      crs: L.CRS.Simple,
      maxZoom: manifest.maxZoom,
      minZoom: manifest.minZoom,
      preferCanvas: true,
      zoomControl: false
    });

    const bounds = L.latLngBounds(
      projection.atsToLatLng({ x: manifest.atsBounds.minX, z: manifest.atsBounds.minZ }),
      projection.atsToLatLng({ x: manifest.atsBounds.maxX, z: manifest.atsBounds.maxZ })
    );

    L.tileLayer(manifest.tileUrlTemplate, {
      bounds,
      maxNativeZoom: manifest.maxZoom,
      maxZoom: manifest.maxZoom,
      minNativeZoom: manifest.minZoom,
      minZoom: manifest.minZoom,
      noWrap: true,
      tileSize: manifest.tileSize
    }).addTo(map);

    const truckMarker = L.marker(projection.atsToLatLng(truckCoordinateRef.current), {
      icon: truckIcon(snapshot?.truck.heading ?? 0),
      interactive: false,
      zIndexOffset: 1000
    }).addTo(map);

    const routeLine = L.polyline([], {
      className: "leaflet-route-line",
      color: "#38d9ff",
      opacity: 0.95,
      weight: 12
    }).addTo(map);

    const routeArrowLayer = L.layerGroup().addTo(map);
    const poiLayer = L.layerGroup().addTo(map);
    const stopLayer = L.layerGroup().addTo(map);

    map.setMaxBounds(bounds.pad(0.1));
    map.setView(projection.atsToLatLng(truckCoordinateRef.current), Math.min(manifest.maxZoom, Math.max(manifest.minZoom, 6)));
    map.on("dragstart zoomstart", () => setFollow(false));

    leaflet.current = { map, truckMarker, routeLine, routeArrowLayer, poiLayer, stopLayer };

    return () => {
      leaflet.current = null;
      map.remove();
    };
  }, [manifest, projection]);

  useEffect(() => {
    if (!leaflet.current || !projection) {
      return;
    }

    const state = leaflet.current;
    const truckLatLng = projection.atsToLatLng(truckCoordinate);
    state.truckMarker.setLatLng(truckLatLng);
    state.truckMarker.setIcon(truckIcon(snapshot?.truck.heading ?? 0));

    if (follow) {
      state.map.panTo(truckLatLng, { animate: true, duration: 0.25 });
    }
  }, [projection, follow, snapshot?.truck.heading, truckCoordinate.x, truckCoordinate.z]);

  useEffect(() => {
    if (!leaflet.current || !projection || !manifest) {
      return;
    }

    const targetZoom = mapMode === "drive"
      ? Math.min(manifest.maxZoom, Math.max(manifest.minZoom, 6))
      : Math.min(manifest.maxZoom, leaflet.current.map.getMaxZoom());

    if (mapMode === "drive") {
      setFollow(true);
      leaflet.current.map.setView(projection.atsToLatLng(truckCoordinateRef.current), targetZoom, { animate: false });
      setSelectedPoint(null);
    } else {
      setFollow(false);
      leaflet.current.map.setZoom(targetZoom, { animate: true });
    }
  }, [mapMode, manifest, projection]);

  useEffect(() => {
    if (!leaflet.current || !projection) {
      return;
    }

    const state = leaflet.current;
    const visualRoute = route?.geometry.length ? route : null;
    state.routeLine.setStyle({
      dashArray: visualRoute?.isRealMapData === false ? "10 10" : undefined,
      opacity: visualRoute?.isRealMapData === false ? 0.45 : 0.95
    });
    state.routeLine.setLatLngs(visualRoute?.geometry.map(projection.atsToLatLng) ?? []);
    state.routeArrowLayer.clearLayers();
    state.stopLayer.clearLayers();

    if (visualRoute?.isRealMapData !== false) {
      visualRoute?.arrows.forEach((arrow) => {
      L.marker(projection.atsToLatLng(arrow.coordinate), {
        icon: routeArrowIcon(arrow.heading),
        interactive: false
      }).addTo(state.routeArrowLayer);
      });
    }

    route?.stops.forEach((stop) => {
      L.marker(projection.atsToLatLng(stop.coordinate), {
        icon: stopIcon(stop.order),
        zIndexOffset: 700
      }).addTo(state.stopLayer);
    });
  }, [projection, route]);

  useEffect(() => {
    if (!leaflet.current || !projection) {
      return;
    }

    const state = leaflet.current;
    state.poiLayer.clearLayers();

    visiblePoints.forEach((point) => {
      L.marker(projection.atsToLatLng(point.coordinate), {
        icon: poiIcon(point),
        title: point.label,
        zIndexOffset: point.kind === "city" ? 100 : 400
      })
        .on("click", () => {
          setSelectedPoint(point);
          setFollow(false);
          state.map.panTo(projection.atsToLatLng(point.coordinate), { animate: true, duration: 0.2 });
        })
        .addTo(state.poiLayer);
    });
  }, [projection, visiblePoints]);

  function recenter() {
    setFollow(true);
    setSelectedPoint(null);
    leaflet.current?.map.setView(projection?.atsToLatLng(truckCoordinate) ?? [0, 0], leaflet.current.map.getZoom(), {
      animate: true
    });
  }

  async function addSelectedStop() {
    if (!selectedPoint) {
      return;
    }

    await addRouteStop(selectedPoint.id);
    setRouteVersion((value) => value + 1);
  }

  async function removeStop(id: string) {
    await removeRouteStop(id);
    setRouteVersion((value) => value + 1);
  }

  function togglePoiFilter(filter: keyof typeof DEFAULT_POI_FILTERS) {
    setPoiFilters((value) => ({ ...value, [filter]: !value[filter] }));
  }

  const routeStatus = route?.status ?? "routed";
  const routeWarning = getRouteWarning(routeStatus);

  return (
    <section className={`map-shell ${mapMode === "drive" ? "driving-view" : "top-map-view"}`} aria-label="Live ATS map">
      <div ref={mapElement} className="ats-leaflet-map" />

      {!manifest ? (
        <div className="tile-empty-state">
          <strong>ATS tiles are not ready</strong>
          <span>{tileError ?? tileStatus?.missing[0] ?? "Run tools\\generate-ats-tiles.ps1 to render the game map."}</span>
        </div>
      ) : null}

      {tileStatus && !tileStatus.tilesReady ? (
        <div className="map-data-warning">Tile map incomplete. Run the tile generator before driving.</div>
      ) : tileStatus?.stale ? (
        <div className="map-data-warning">Map update available. Regenerate ATS tiles when you are parked.</div>
      ) : null}

      {routeWarning ? (
        <div className="route-data-warning">{routeWarning}</div>
      ) : null}

      <div className="maneuver-card">
        <strong>{snapshot?.job.destination.city || selectedPoint?.label || "ATS GPS"}</strong>
        <span>{formatDistance(snapshot, units)} · ETA {snapshot?.job.eta || "--:--"}</span>
      </div>

      <div className="view-switch" aria-label="Map view">
        <button type="button" className={mapMode === "drive" ? "active" : ""} onClick={() => setMapMode("drive")} title="Driving view">
          <Navigation size={17} />
          <span>Drive</span>
        </button>
        <button type="button" className={mapMode === "map" ? "active" : ""} onClick={() => setMapMode("map")} title="Top map view">
          <Map size={17} />
          <span>Map</span>
        </button>
      </div>

      <div className="speed-stack">
        <div className="speed-bubble">
          <strong>{Math.round(units === "metric" ? snapshot?.truck.speedKph ?? 0 : snapshot?.truck.speedMph ?? 0)}</strong>
          <span>{units === "metric" ? "km/h" : "mph"}</span>
        </div>
        <div className="limit-sign">
          {Math.round(units === "metric" ? snapshot?.truck.speedLimitKph ?? 0 : snapshot?.truck.speedLimitMph ?? 0)}
        </div>
      </div>

      <div className="map-toolbar compact" aria-label="Map tools">
        <button type="button" className={follow ? "active" : ""} onClick={() => setFollow((value) => !value)} title="Follow truck">
          <LocateFixed size={18} />
        </button>
        <button type="button" onClick={recenter} title="Recenter">
          <Crosshair size={18} />
        </button>
        <button type="button" onClick={() => setSettingsOpen((value) => !value)} title="Settings">
          <Settings size={18} />
        </button>
      </div>

      {mapMode === "map" && !selectedPoint ? (
        <div className="poi-filter-bar" aria-label="Point filters">
          <button type="button" className={poiFilters.fuel ? "active" : ""} onClick={() => togglePoiFilter("fuel")} title="Gas stations">
            <Fuel size={16} />
            <span>Gas</span>
          </button>
          <button type="button" className={poiFilters.repair ? "active" : ""} onClick={() => togglePoiFilter("repair")} title="Repair and service">
            <Wrench size={16} />
            <span>Repair</span>
          </button>
          <button type="button" className={poiFilters.business ? "active" : ""} onClick={() => togglePoiFilter("business")} title="Businesses">
            <BriefcaseBusiness size={16} />
            <span>Business</span>
          </button>
          <button type="button" className={poiFilters.city ? "active" : ""} onClick={() => togglePoiFilter("city")} title="Cities">
            <MapPinned size={16} />
            <span>Cities</span>
          </button>
        </div>
      ) : null}

      {selectedPoint ? (
        <div className="stop-sheet">
          <button type="button" className="sheet-close" onClick={() => setSelectedPoint(null)} title="Close">
            <X size={17} />
          </button>
          <span>{poiKindLabel(selectedPoint)}</span>
          <strong>{selectedPoint.label}</strong>
          <small>{selectedPoint.city || selectedPoint.company || "ATS stop"}</small>
          <button type="button" className="primary-action" onClick={addSelectedStop} disabled={selectedPoint.kind === "city"}>
            <Plus size={18} />
            Add stop
          </button>
        </div>
      ) : null}

      {route && route.stops.length > 0 && mapMode === "map" ? (
        <div className="route-stops-panel">
          <strong>Stops</strong>
          {route.stops.map((stop) => (
            <button key={stop.id} type="button" onClick={() => removeStop(stop.id)}>
              <MapPinned size={15} />
              <span>{stop.order}. {stop.label}</span>
              <Trash2 size={15} />
            </button>
          ))}
        </div>
      ) : null}

      {settingsOpen ? (
        <div className="settings-drawer">
          <label>
            <span>Units</span>
            <select value={units} onChange={(event) => setUnits(event.target.value as "metric" | "imperial")}>
              <option value="metric">Metric</option>
              <option value="imperial">Imperial</option>
            </select>
          </label>
          <label className="setting-row">
            <span>Keep screen awake</span>
            <input
              type="checkbox"
              checked={wakeLockEnabled}
              disabled={!wakeLock.supported}
              onChange={(event) => setWakeLockEnabled(event.target.checked)}
            />
          </label>
          <small>{wakeLockStatus(wakeLock.state)}</small>
          <small>
            {tileStatus?.tilesReady
              ? `${tileStatus.tileCount} tiles loaded · ${tileStatus.state}`
              : "No generated tile map"}
          </small>
          {tileStatus?.stale ? <small>{tileStatus.recommendedCommand}</small> : null}
        </div>
      ) : null}
    </section>
  );
}

function createProjection(manifest: TileMapManifest) {
  const scaleX = manifest.pixelSizeAtMaxZoom.width / (manifest.atsBounds.maxX - manifest.atsBounds.minX);
  const scaleZ = manifest.pixelSizeAtMaxZoom.height / (manifest.atsBounds.maxZ - manifest.atsBounds.minZ);
  const divisor = 2 ** manifest.maxZoom;

  return {
    atsToLatLng(point: AtsMapCoordinate) {
      const x = (point.x - manifest.atsBounds.minX) * scaleX;
      const y = (point.z - manifest.atsBounds.minZ) * scaleZ;
      return L.latLng(-y / divisor, x / divisor);
    }
  };
}

function wakeLockStatus(state: "unsupported" | "inactive" | "active" | "error") {
  if (state === "active") {
    return "Screen wake lock active";
  }

  if (state === "unsupported") {
    return "Wake Lock API is not supported by this browser";
  }

  if (state === "error") {
    return "Wake lock blocked; try installing/opening the app over HTTPS or localhost";
  }

  return "Wake lock inactive";
}

function getMapCenter(snapshot: TelemetrySnapshot | null) {
  if (!snapshot?.game.connected || (snapshot.truck.position.atsX === 0 && snapshot.truck.position.atsZ === 0)) {
    return FALLBACK_CENTER;
  }

  return snapshot.truck.position;
}

function toCoordinate(point: AtsMapCoordinate | { atsX: number; atsZ: number }) {
  if ("atsX" in point) {
    return { x: point.atsX, z: point.atsZ };
  }

  return point;
}

function formatDistance(snapshot: TelemetrySnapshot | null, units: "metric" | "imperial") {
  if (!snapshot) {
    return "--";
  }

  if (units === "metric") {
    return `${snapshot.job.remainingKilometers.toFixed(snapshot.job.remainingKilometers < 10 ? 1 : 0)} km`;
  }

  return `${snapshot.job.remainingMiles.toFixed(snapshot.job.remainingMiles < 10 ? 1 : 0)} mi`;
}

function distance(a: AtsMapCoordinate, b: AtsMapCoordinate) {
  return Math.hypot(a.x - b.x, a.z - b.z);
}

function truckIcon(heading: number) {
  const rotation = ((90 - heading) % 360 + 360) % 360;

  return L.divIcon({
    className: "leaflet-truck-icon",
    html: `
      <div class="truck-marker__arrow" style="transform: rotate(${rotation}deg)">
        <svg width="36" height="36" viewBox="0 0 36 36" aria-hidden="true">
          <path d="M18 3 L30 31 L18 25 L6 31 Z" />
        </svg>
      </div>
    `,
    iconAnchor: [18, 18],
    iconSize: [36, 36]
  });
}

function poiIcon(point: AtsMapPoint) {
  const label = point.kind === "fuel"
    ? "G"
    : isRepairPoint(point) ? "R"
      : point.kind === "company" ? "B" : point.kind === "parking" ? "P" : point.kind === "dealer" ? "D" : "";
  return L.divIcon({
    className: `leaflet-poi-icon poi-${point.kind}`,
    html: point.kind === "city" ? `<span>${point.label}</span>` : `<b>${label}</b>`,
    iconAnchor: point.kind === "city" ? [8, 8] : [13, 13],
    iconSize: point.kind === "city" ? [16, 16] : [26, 26]
  });
}

function shouldShowPoint(
  point: AtsMapPoint,
  filters: typeof DEFAULT_POI_FILTERS,
  mapMode: "drive" | "map"
) {
  if (point.kind === "city") {
    return mapMode === "map" && filters.city;
  }

  if (point.kind === "fuel") {
    return filters.fuel;
  }

  if (isRepairPoint(point)) {
    return filters.repair;
  }

  if (point.kind === "company") {
    return mapMode === "map" && filters.business;
  }

  return mapMode === "map";
}

function isRepairPoint(point: AtsMapPoint) {
  return point.kind === "service" || point.kind === "garage" || point.kind === "dealer";
}

function poiKindLabel(point: AtsMapPoint) {
  if (point.kind === "fuel") {
    return "Gas station";
  }

  if (isRepairPoint(point)) {
    return "Repair";
  }

  if (point.kind === "company") {
    return "Business";
  }

  return point.kind;
}

function getRouteWarning(status: string) {
  if (status === "noPath") {
    return "No road route found for the selected destination.";
  }

  if (status === "partialRoute") {
    return "Showing a best-effort route. Regenerate map data to improve road connections.";
  }

  if (status === "noDestination") {
    return "No destination or stop is selected.";
  }

  if (status === "seedData") {
    return "Using seed map data. Generate ATS map data for road-following navigation.";
  }

  return null;
}

function routeArrowIcon(heading: number) {
  return L.divIcon({
    className: "leaflet-route-arrow",
    html: `<span style="transform: rotate(${heading}deg)"></span>`,
    iconAnchor: [14, 14],
    iconSize: [28, 28]
  });
}

function stopIcon(order: number) {
  return L.divIcon({
    className: "leaflet-stop-icon",
    html: `<span>${order}</span>`,
    iconAnchor: [15, 15],
    iconSize: [30, 30]
  });
}
