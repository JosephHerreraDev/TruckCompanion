import { useEffect, useMemo, useRef, useState } from "react";
import maplibregl, { type GeoJSONSource, type Map as MapLibreMap, type Marker } from "maplibre-gl";
import { Protocol } from "pmtiles";
import proj4 from "proj4";
import { BriefcaseBusiness, Crosshair, Fuel, LocateFixed, Map, MapPinned, Navigation, Plus, Settings, Trash2, Wrench, X } from "lucide-react";
import {
  addRouteStop,
  getMapPois,
  getMapRoute,
  getTileMapStatus,
  removeRouteStop
} from "../api/client";
import { useWakeLock } from "../hooks/useWakeLock";
import type {
  AtsMapCoordinate,
  AtsMapPoint,
  AtsMapRoute,
  TelemetrySnapshot,
  TileMapStatus
} from "../types";

type Props = {
  snapshot: TelemetrySnapshot | null;
};

type MapState = {
  map: MapLibreMap;
  truckMarker: Marker;
  stopMarkers: Marker[];
};

const FALLBACK_CENTER = { atsX: -38611, atsZ: -5261 };
const ROUTE_REROUTE_DISTANCE = 750;
const DEFAULT_POI_FILTERS = {
  fuel: true,
  repair: true,
  business: true,
  city: true
};

const EARTH_RADIUS_METERS = 6370997;
const LENGTH_OF_DEGREE = (EARTH_RADIUS_METERS * Math.PI) / 180;
const ATS_FACTOR_X = 0.000176689948;
const ATS_FACTOR_Y = -0.00017706234;
const atsProjection = proj4([
  "+proj=lcc",
  `+R=${EARTH_RADIUS_METERS}`,
  "+lat_1=33",
  "+lat_2=45",
  "+lat_0=39",
  "+lon_0=-96"
].join(" "));

let pmtilesRegistered = false;

export function MapView({ snapshot }: Props) {
  const mapElement = useRef<HTMLDivElement | null>(null);
  const mapState = useRef<MapState | null>(null);
  const lastRouteOrigin = useRef<AtsMapCoordinate | null>(null);
  const lastRouteKey = useRef<string | null>(null);
  const routeRequestId = useRef(0);
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

  const visiblePoints = useMemo(
    () => points.filter((point) => shouldShowPoint(point, poiFilters, mapMode)),
    [points, poiFilters, mapMode]
  );
  const truckCoordinate = toCoordinate(getMapCenter(snapshot));
  const pmtilesReady = tileStatus?.artifacts.pmtilesReady === true;

  useEffect(() => {
    let active = true;

    Promise.allSettled([getTileMapStatus(), getMapPois()])
      .then(([statusResult, pointsResult]) => {
        if (!active) {
          return;
        }

        if (statusResult.status === "fulfilled") {
          setTileStatus(statusResult.value);
          setTileError(statusResult.value.tilesReady ? null : statusResult.value.missing[0] ?? "Generated ATS map data is missing.");
        } else {
          setTileError("Generated ATS map data is missing. Run tools\\generate-ats-tiles.ps1.");
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
        if (routeRequestId.current === requestId) {
          setRoute(nextRoute);
        }
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
    if (!pmtilesReady || !mapElement.current || mapState.current) {
      return;
    }

    if (!pmtilesRegistered) {
      const protocol = new Protocol();
      maplibregl.addProtocol("pmtiles", protocol.tile);
      pmtilesRegistered = true;
    }

    const map = new maplibregl.Map({
      container: mapElement.current,
      attributionControl: false,
      center: atsToLngLat(truckCoordinate),
      zoom: 5.8,
      minZoom: 3,
      maxZoom: 14,
      pitch: 0,
      style: createMapStyle()
    });

    const truckMarker = new maplibregl.Marker({
      element: createTruckMarker(snapshot?.truck.heading ?? 0),
      rotationAlignment: "map"
    })
      .setLngLat(atsToLngLat(truckCoordinate))
      .addTo(map);

    map.on("dragstart", () => setFollow(false));
    map.on("zoomstart", () => setFollow(false));
    map.on("load", () => {
      map.addSource("truckcompanion-route", emptyLineSource());
      map.addLayer({
        id: "truckcompanion-route-line",
        type: "line",
        source: "truckcompanion-route",
        paint: {
          "line-color": "#38d9ff",
          "line-width": ["interpolate", ["linear"], ["zoom"], 4, 3, 10, 9, 14, 15],
          "line-opacity": 0.94
        },
        layout: {
          "line-cap": "round",
          "line-join": "round"
        }
      });

      map.addSource("truckcompanion-pois", emptyPoiSource());
      map.addLayer({
        id: "truckcompanion-poi-points",
        type: "circle",
        source: "truckcompanion-pois",
        paint: {
          "circle-radius": ["case", ["==", ["get", "kind"], "city"], 3, 8],
          "circle-color": [
            "match",
            ["get", "kind"],
            "fuel", "#2fb86c",
            "service", "#e25543",
            "garage", "#e25543",
            "dealer", "#e25543",
            "company", "#f8c931",
            "parking", "#60a5fa",
            "weigh", "#a78bfa",
            "#f8fafc"
          ],
          "circle-stroke-color": "#111827",
          "circle-stroke-width": 2
        }
      });
      map.on("click", "truckcompanion-poi-points", (event) => {
        const id = event.features?.[0]?.properties?.id as string | undefined;
        const point = points.find((candidate) => candidate.id === id);
        if (!point) {
          return;
        }

        setSelectedPoint(point);
        setFollow(false);
        map.easeTo({ center: atsToLngLat(point.coordinate), duration: 200 });
      });
      map.on("mouseenter", "truckcompanion-poi-points", () => {
        map.getCanvas().style.cursor = "pointer";
      });
      map.on("mouseleave", "truckcompanion-poi-points", () => {
        map.getCanvas().style.cursor = "";
      });
    });

    mapState.current = { map, truckMarker, stopMarkers: [] };

    return () => {
      mapState.current = null;
      map.remove();
    };
  }, [pmtilesReady]);

  useEffect(() => {
    const state = mapState.current;
    if (!state) {
      return;
    }

    const truckLngLat = atsToLngLat(truckCoordinate);
    state.truckMarker.setLngLat(truckLngLat);
    state.truckMarker.getElement().style.setProperty("--truck-heading", `${((90 - (snapshot?.truck.heading ?? 0)) % 360 + 360) % 360}deg`);

    if (follow) {
      state.map.easeTo({ center: truckLngLat, duration: 250 });
    }
  }, [follow, snapshot?.truck.heading, truckCoordinate.x, truckCoordinate.z]);

  useEffect(() => {
    const state = mapState.current;
    if (!state) {
      return;
    }

    if (mapMode === "drive") {
      setFollow(true);
      setSelectedPoint(null);
      state.map.easeTo({ center: atsToLngLat(truckCoordinate), zoom: Math.max(state.map.getZoom(), 6), duration: 0 });
    } else {
      setFollow(false);
      state.map.easeTo({ zoom: Math.max(state.map.getZoom(), 5), duration: 250 });
    }
  }, [mapMode]);

  useEffect(() => {
    const map = mapState.current?.map;
    if (!map || !map.isStyleLoaded()) {
      return;
    }

    const source = map.getSource("truckcompanion-route") as GeoJSONSource | undefined;
    source?.setData(routeToGeoJson(route));

    const routeLayer = map.getLayer("truckcompanion-route-line");
    if (routeLayer) {
      map.setPaintProperty("truckcompanion-route-line", "line-opacity", route?.isRealMapData === false ? 0.45 : 0.94);
      map.setPaintProperty("truckcompanion-route-line", "line-dasharray", route?.isRealMapData === false ? [1, 1] : [1, 0]);
    }

    mapState.current?.stopMarkers.forEach((marker) => marker.remove());
    mapState.current!.stopMarkers = (route?.stops ?? []).map((stop) =>
      new maplibregl.Marker({ element: createStopMarker(stop.order) })
        .setLngLat(atsToLngLat(stop.coordinate))
        .addTo(map)
    );
  }, [route]);

  useEffect(() => {
    const map = mapState.current?.map;
    if (!map || !map.isStyleLoaded()) {
      return;
    }

    const source = map.getSource("truckcompanion-pois") as GeoJSONSource | undefined;
    source?.setData(pointsToGeoJson(visiblePoints));
  }, [visiblePoints]);

  function recenter() {
    setFollow(true);
    setSelectedPoint(null);
    mapState.current?.map.easeTo({ center: atsToLngLat(truckCoordinate), duration: 250 });
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
      <div ref={mapElement} className="ats-maplibre-map" />

      {!pmtilesReady ? (
        <div className="tile-empty-state">
          <strong>ATS map data is not ready</strong>
          <span>{tileError ?? tileStatus?.missing[0] ?? "Run tools\\generate-ats-tiles.ps1 to render the game map."}</span>
        </div>
      ) : null}

      {tileStatus && !tileStatus.tilesReady ? (
        <div className="map-data-warning">Map data incomplete. Run the generator before driving.</div>
      ) : tileStatus?.stale ? (
        <div className="map-data-warning">Map update available. Regenerate ATS map data when you are parked.</div>
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
              ? `PMTiles loaded · ${tileStatus.state}`
              : "No generated map data"}
          </small>
          {tileStatus?.stale ? <small>{tileStatus.recommendedCommand}</small> : null}
        </div>
      ) : null}
    </section>
  );
}

function createMapStyle(): maplibregl.StyleSpecification {
  return {
    version: 8,
    sprite: "/map/spritesheet",
    sources: {
      ats: {
        type: "vector",
        url: "pmtiles:///map/ats.pmtiles"
      }
    },
    layers: [
      {
        id: "background",
        type: "background",
        paint: { "background-color": "#202428" }
      },
      {
        id: "map-areas",
        type: "fill",
        source: "ats",
        "source-layer": "ats",
        filter: ["in", ["get", "type"], ["literal", ["mapArea", "prefab"]]],
        paint: {
          "fill-color": ["coalesce", ["get", "color"], "#3a4148"],
          "fill-opacity": 0.82
        }
      },
      {
        id: "roads-casing",
        type: "line",
        source: "ats",
        "source-layer": "ats",
        filter: ["==", ["get", "type"], "road"],
        paint: {
          "line-color": "#111418",
          "line-width": ["interpolate", ["linear"], ["zoom"], 4, 1.5, 10, 7, 14, 16]
        },
        layout: { "line-cap": "round", "line-join": "round" }
      },
      {
        id: "roads",
        type: "line",
        source: "ats",
        "source-layer": "ats",
        filter: ["==", ["get", "type"], "road"],
        paint: {
          "line-color": [
            "match",
            ["get", "roadType"],
            "freeway", "#f5b61f",
            "divided", "#e7aa20",
            "local", "#8b8f94",
            "#9da3aa"
          ],
          "line-width": ["interpolate", ["linear"], ["zoom"], 4, 0.8, 10, 4, 14, 10]
        },
        layout: { "line-cap": "round", "line-join": "round" }
      },
      {
        id: "ferries",
        type: "line",
        source: "ats",
        "source-layer": "ats",
        filter: ["in", ["get", "type"], ["literal", ["ferry", "train"]]],
        paint: {
          "line-color": "#60a5fa",
          "line-width": 2,
          "line-dasharray": [2, 2]
        }
      },
      {
        id: "base-pois",
        type: "symbol",
        source: "ats",
        "source-layer": "ats",
        filter: ["==", ["get", "type"], "poi"],
        layout: {
          "icon-image": ["get", "sprite"],
          "icon-size": 0.7,
          "icon-allow-overlap": false
        }
      }
    ]
  };
}

function emptyLineSource(): maplibregl.GeoJSONSourceSpecification {
  return {
    type: "geojson",
    data: {
      type: "Feature",
      properties: {},
      geometry: {
        type: "LineString",
        coordinates: []
      }
    }
  };
}

function emptyPoiSource(): maplibregl.GeoJSONSourceSpecification {
  return {
    type: "geojson",
    data: pointsToGeoJson([])
  };
}

function routeToGeoJson(route: AtsMapRoute | null) {
  return {
    type: "Feature" as const,
    properties: {},
    geometry: {
      type: "LineString" as const,
      coordinates: route?.geometry.map(atsToLngLat) ?? []
    }
  };
}

function pointsToGeoJson(points: AtsMapPoint[]) {
  return {
    type: "FeatureCollection" as const,
    features: points.map((point) => ({
      type: "Feature" as const,
      properties: {
        id: point.id,
        kind: point.kind,
        label: point.label
      },
      geometry: {
        type: "Point" as const,
        coordinates: atsToLngLat(point.coordinate)
      }
    }))
  };
}

function atsToLngLat(point: AtsMapCoordinate): [number, number] {
  return atsProjection.inverse([
    point.x * ATS_FACTOR_X * LENGTH_OF_DEGREE,
    point.z * ATS_FACTOR_Y * LENGTH_OF_DEGREE
  ]) as [number, number];
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

function createTruckMarker(heading: number) {
  const marker = document.createElement("div");
  marker.className = "maplibre-truck-marker";
  marker.style.setProperty("--truck-heading", `${((90 - heading) % 360 + 360) % 360}deg`);
  marker.innerHTML = `
    <svg width="36" height="36" viewBox="0 0 36 36" aria-hidden="true">
      <path d="M18 3 L30 31 L18 25 L6 31 Z" />
    </svg>
  `;
  return marker;
}

function createStopMarker(order: number) {
  const marker = document.createElement("div");
  marker.className = "maplibre-stop-marker";
  marker.textContent = order.toString();
  return marker;
}
