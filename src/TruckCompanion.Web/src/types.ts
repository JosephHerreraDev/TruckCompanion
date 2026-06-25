export type MapPosition = {
  atsX: number;
  atsZ: number;
  latitude: number;
  longitude: number;
};

export type StopPoint = {
  city: string;
  company: string;
  position: MapPosition | null;
};

export type TelemetrySnapshot = {
  game: {
    connected: boolean;
    paused: boolean;
    gameName: string;
    gameTime: string;
  };
  truck: {
    position: MapPosition;
    heading: number;
    speedMph: number;
    speedLimitMph: number;
    speedKph: number;
    speedLimitKph: number;
    fuelLiters: number;
    fuelCapacityLiters: number;
    fuelGallons: number;
    maxVehicleDamagePercent: number;
    damage: {
      enginePercent: number;
      transmissionPercent: number;
      cabinPercent: number;
      chassisPercent: number;
      wheelsPercent: number;
      trailerPercent: number;
    };
  };
  job: {
    active: boolean;
    source: StopPoint;
    destination: StopPoint;
    cargo: string;
    income: number;
    eta: string;
    remainingMiles: number;
    remainingKilometers: number;
  };
  navigation: {
    estimatedDistanceMiles: number;
    estimatedDistanceKilometers: number;
    estimatedTimeMinutes: number;
    estimatedTimeSeconds: number;
    speedLimitMph: number;
  };
  driver: {
    sleepHoursLeft: number | null;
  };
  connection: {
    source: string;
    lastUpdateUtc: string;
    stale: boolean;
    error: string | null;
  };
};

export type Poi = {
  id: string;
  type: "city" | "fuel" | "service" | "rest" | "weigh" | "garage" | "dealer";
  name: string;
  city: string;
  state: string;
  position: MapPosition;
};

export type AtsMapCoordinate = {
  x: number;
  z: number;
};

export type AtsMapRoad = {
  id: string;
  kind: string;
  label: string | null;
  geometry: AtsMapCoordinate[];
};

export type AtsMapArea = {
  id: string;
  kind: string;
  label: string | null;
  polygon: AtsMapCoordinate[];
};

export type AtsMapPoint = {
  id: string;
  kind: string;
  label: string;
  city: string | null;
  company: string | null;
  coordinate: AtsMapCoordinate;
};

export type AtsMapViewport = {
  source: string;
  roads: AtsMapRoad[];
  areas: AtsMapArea[];
  points: AtsMapPoint[];
};

export type AtsMapRoute = {
  source: string;
  geometry: AtsMapCoordinate[];
  arrows: AtsMapRouteArrow[];
  stops: AtsRouteStop[];
  isRealMapData: boolean;
  status: "routed" | "noDestination" | "noPath" | "seedData" | string;
  distance: number;
};

export type AtsMapRouteArrow = {
  coordinate: AtsMapCoordinate;
  heading: number;
};

export type AtsRouteStop = {
  id: string;
  kind: string;
  label: string;
  city: string | null;
  company: string | null;
  coordinate: AtsMapCoordinate;
  order: number;
};

export type TileMapManifest = {
  version: string;
  mapFingerprint: string;
  tileSize: number;
  minZoom: number;
  maxZoom: number;
  atsBounds: {
    minX: number;
    minZ: number;
    maxX: number;
    maxZ: number;
  };
  pixelSizeAtMaxZoom: {
    width: number;
    height: number;
  };
  tileUrlTemplate: string;
  generatedAtUtc: string;
  source: string;
  gameVersion: string | null;
  dlcArchiveCount: number;
  tileCount: number;
  cities: TileMapCity[];
};

export type TileMapCity = {
  name: string;
  tokenName: string;
  x: number;
  z: number;
};

export type TileMapStatus = {
  tilesReady: boolean;
  stale: boolean;
  state: "missing" | "ready" | "stale" | string;
  tileRoot: string;
  manifestPath: string;
  installedAtsPath: string;
  currentFingerprint: string | null;
  version: string | null;
  tileCount: number;
  maxZoom: number | null;
  source: string | null;
  dlcArchiveCount: number;
  lastGeneratedUtc: string | null;
  recommendedCommand: string;
  missing: string[];
};
