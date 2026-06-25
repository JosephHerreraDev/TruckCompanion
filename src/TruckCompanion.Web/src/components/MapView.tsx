import { useEffect, useMemo, useRef, useState } from "react";
import L from "leaflet";
import { Crosshair, LocateFixed, Map, MapPinned, Navigation, Plus, Settings, Trash2, X } from "lucide-react";
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
  tileLayer: L.TileLayer;
  truckMarker: L.Marker;
  routeLine: L.Polyline;
  drivenLine: L.Polyline;
  routeArrowLayer: L.LayerGroup;
  poiLayer: L.LayerGroup;
  stopLayer: L.LayerGroup;
};

const FALLBACK_CENTER = { atsX: -38611, atsZ: -5261 };
const DRIVEN_PATH_KEY = "truckcompanion.drivenPath.v1";

export function MapView({ snapshot }: Props) {
  const mapElement = useRef<HTMLDivElement | null>(null);
  const leaflet = useRef<LeafletState | null>(null);
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
  const [drivenPath, setDrivenPath] = useState<AtsMapCoordinate[]>(() => readDrivenPath());
  const wakeLock = useWakeLock(wakeLockEnabled);

  const projection = useMemo(() => manifest ? createProjection(manifest) : null, [manifest]);
  const truckCoordinate = toCoordinate(getMapCenter(snapshot));

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

    getMapRoute(snapshot)
      .then(setRoute)
      .catch(() => setRoute(null));
  }, [
    snapshot?.game.connected,
    snapshot?.truck.position.atsX,
    snapshot?.truck.position.atsZ,
    snapshot?.job.destination.city,
    snapshot?.job.destination.company,
    routeVersion
  ]);

  useEffect(() => {
    if (!snapshot?.game.connected) {
      return;
    }

    const next = toCoordinate(snapshot.truck.position);
    setDrivenPath((current) => {
      const last = current[current.length - 1];
      if (last && distance(last, next) < 220) {
        return current;
      }

      const updated = [...current, next].slice(-700);
      localStorage.setItem(DRIVEN_PATH_KEY, JSON.stringify(updated));
      return updated;
    });
  }, [snapshot?.game.connected, snapshot?.truck.position.atsX, snapshot?.truck.position.atsZ]);

  useEffect(() => {
    if (!manifest || !projection || !mapElement.current || leaflet.current) {
      return;
    }

    const map = L.map(mapElement.current, {
      attributionControl: false,
      crs: L.CRS.Simple,
      maxZoom: manifest.maxZoom + 3,
      minZoom: manifest.minZoom,
      preferCanvas: true,
      zoomControl: false
    });

    const bounds = L.latLngBounds(
      projection.atsToLatLng({ x: manifest.atsBounds.minX, z: manifest.atsBounds.minZ }),
      projection.atsToLatLng({ x: manifest.atsBounds.maxX, z: manifest.atsBounds.maxZ })
    );

    const tileLayer = L.tileLayer(manifest.tileUrlTemplate, {
      bounds,
      maxNativeZoom: manifest.maxZoom,
      maxZoom: manifest.maxZoom + 3,
      minNativeZoom: manifest.minZoom,
      minZoom: manifest.minZoom,
      noWrap: true,
      tileSize: manifest.tileSize
    }).addTo(map);

    const truckMarker = L.marker(projection.atsToLatLng(truckCoordinate), {
      icon: truckIcon(snapshot?.truck.heading ?? 0),
      interactive: false,
      zIndexOffset: 1000
    }).addTo(map);

    const drivenLine = L.polyline([], {
      className: "leaflet-driven-line",
      color: "#fff0a3",
      opacity: 0.92,
      weight: 7
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
    map.setView(projection.atsToLatLng(truckCoordinate), Math.min(manifest.maxZoom + 3, Math.max(manifest.minZoom, manifest.maxZoom + 1)));
    map.on("dragstart zoomstart", () => setFollow(false));

    leaflet.current = { map, tileLayer, truckMarker, routeLine, drivenLine, routeArrowLayer, poiLayer, stopLayer };

    return () => {
      leaflet.current = null;
      map.remove();
    };
  }, [manifest, projection, snapshot?.truck.heading, truckCoordinate.x, truckCoordinate.z]);

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
      ? Math.min(manifest.maxZoom + 2, leaflet.current.map.getMaxZoom())
      : Math.min(manifest.maxZoom, leaflet.current.map.getMaxZoom());

    if (mapMode === "drive") {
      setFollow(true);
      leaflet.current.map.setView(projection.atsToLatLng(truckCoordinate), targetZoom, { animate: true });
      setSelectedPoint(null);
    } else {
      setFollow(false);
      leaflet.current.map.setZoom(targetZoom, { animate: true });
    }
  }, [mapMode, manifest, projection, truckCoordinate.x, truckCoordinate.z]);

  useEffect(() => {
    if (!leaflet.current || !projection) {
      return;
    }

    leaflet.current.drivenLine.setLatLngs(drivenPath.map(projection.atsToLatLng));
  }, [projection, drivenPath]);

  useEffect(() => {
    if (!leaflet.current || !projection) {
      return;
    }

    const state = leaflet.current;
    const visualRoute = route?.isRealMapData ? route : null;
    state.routeLine.setLatLngs(visualRoute?.geometry.map(projection.atsToLatLng) ?? []);
    state.routeArrowLayer.clearLayers();
    state.stopLayer.clearLayers();

    visualRoute?.arrows.forEach((arrow) => {
      L.marker(projection.atsToLatLng(arrow.coordinate), {
        icon: routeArrowIcon(arrow.heading),
        interactive: false
      }).addTo(state.routeArrowLayer);
    });

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

    if (mapMode === "drive") {
      return;
    }

    points.forEach((point) => {
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
  }, [projection, points, mapMode]);

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

      {selectedPoint && mapMode === "map" ? (
        <div className="stop-sheet">
          <button type="button" className="sheet-close" onClick={() => setSelectedPoint(null)} title="Close">
            <X size={17} />
          </button>
          <span>{selectedPoint.kind}</span>
          <strong>{selectedPoint.label}</strong>
          <small>{selectedPoint.city || selectedPoint.company || "ATS stop"}</small>
          <button type="button" className="primary-action" onClick={addSelectedStop}>
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

function readDrivenPath() {
  try {
    const value = localStorage.getItem(DRIVEN_PATH_KEY);
    if (!value) {
      return [];
    }

    const parsed = JSON.parse(value) as AtsMapCoordinate[];
    return Array.isArray(parsed) ? parsed.filter((point) => Number.isFinite(point.x) && Number.isFinite(point.z)) : [];
  } catch {
    return [];
  }
}

function distance(a: AtsMapCoordinate, b: AtsMapCoordinate) {
  return Math.hypot(a.x - b.x, a.z - b.z);
}

function truckIcon(heading: number) {
  return L.divIcon({
    className: "leaflet-truck-icon",
    html: `<span style="transform: rotate(${heading}deg)"></span>`,
    iconAnchor: [22, 22],
    iconSize: [44, 44]
  });
}

function poiIcon(point: AtsMapPoint) {
  const label = point.kind === "fuel" ? "F" : point.kind === "service" || point.kind === "garage" ? "W" : point.kind === "company" ? "C" : "";
  return L.divIcon({
    className: `leaflet-poi-icon poi-${point.kind}`,
    html: point.kind === "city" ? `<span>${point.label}</span>` : `<b>${label}</b>`,
    iconAnchor: point.kind === "city" ? [8, 8] : [13, 13],
    iconSize: point.kind === "city" ? [16, 16] : [26, 26]
  });
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
