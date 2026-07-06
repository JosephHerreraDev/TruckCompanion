import type {
  AtsMapPoint,
  AtsMapRoute,
  AtsMapViewport,
  AtsRouteStop,
  Poi,
  TelemetrySnapshot,
  TileMapStatus
} from "../types";

export async function getSnapshot(): Promise<TelemetrySnapshot> {
  const response = await fetch("/api/snapshot");
  if (!response.ok) {
    throw new Error(`Snapshot request failed: ${response.status}`);
  }

  return response.json();
}

export async function getPois(): Promise<Poi[]> {
  const response = await fetch("/api/pois");
  if (!response.ok) {
    throw new Error(`POI request failed: ${response.status}`);
  }

  return response.json();
}

export async function getMapViewport(
  minX: number,
  minZ: number,
  maxX: number,
  maxZ: number,
  detail = 2
): Promise<AtsMapViewport> {
  const params = new URLSearchParams({
    minX: minX.toString(),
    minZ: minZ.toString(),
    maxX: maxX.toString(),
    maxZ: maxZ.toString(),
    detail: detail.toString()
  });
  const response = await fetch(`/api/map/viewport?${params}`);
  if (!response.ok) {
    throw new Error(`Map viewport request failed: ${response.status}`);
  }

  return response.json();
}

export async function getMapRoute(snapshot: TelemetrySnapshot): Promise<AtsMapRoute> {
  const params = new URLSearchParams({
    fromX: snapshot.truck.position.atsX.toString(),
    fromZ: snapshot.truck.position.atsZ.toString()
  });

  if (snapshot.job.destination.company) {
    params.set("toCompany", snapshot.job.destination.company);
  }

  if (snapshot.job.destination.city) {
    params.set("toCity", snapshot.job.destination.city);
  }

  const response = await fetch(`/api/map/route?${params}`);
  if (!response.ok) {
    throw new Error(`Map route request failed: ${response.status}`);
  }

  return response.json();
}

export async function getMapPois(kind?: string): Promise<AtsMapPoint[]> {
  const params = new URLSearchParams();
  if (kind) {
    params.set("kind", kind);
  }

  const response = await fetch(`/api/map/pois?${params}`);
  if (!response.ok) {
    throw new Error(`Map POI request failed: ${response.status}`);
  }

  return response.json();
}

export async function addRouteStop(pointId: string): Promise<AtsRouteStop> {
  const response = await fetch("/api/route/stops", {
    method: "POST",
    headers: {
      "Content-Type": "application/json"
    },
    body: JSON.stringify({ pointId })
  });

  if (!response.ok) {
    throw new Error(`Add stop request failed: ${response.status}`);
  }

  return response.json();
}

export async function removeRouteStop(id: string): Promise<void> {
  const response = await fetch(`/api/route/stops/${encodeURIComponent(id)}`, {
    method: "DELETE"
  });

  if (!response.ok) {
    throw new Error(`Remove stop request failed: ${response.status}`);
  }
}

export async function getTileMapStatus(): Promise<TileMapStatus> {
  const response = await fetch("/api/map/status");
  if (!response.ok) {
    throw new Error(`Map status request failed: ${response.status}`);
  }

  return response.json();
}

export function subscribeTelemetry(
  onSnapshot: (snapshot: TelemetrySnapshot) => void,
  onError: (error: string) => void
) {
  const source = new EventSource("/stream/telemetry");

  source.addEventListener("telemetry", (event) => {
    const message = event as MessageEvent<string>;
    onSnapshot(JSON.parse(message.data) as TelemetrySnapshot);
  });

  source.onerror = () => {
    onError("Live telemetry stream disconnected");
  };

  return () => source.close();
}
