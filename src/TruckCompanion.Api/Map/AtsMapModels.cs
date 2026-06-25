namespace TruckCompanion.Api.Map;

public sealed record AtsMapDatabase(
    string Source,
    DateTimeOffset GeneratedAtUtc,
    bool IsRealMapData,
    IReadOnlyList<AtsMapRoad> Roads,
    IReadOnlyList<AtsMapArea> Areas,
    IReadOnlyList<AtsMapPoint> Points,
    string? MapFingerprint = null,
    AtsRouteGraph? RouteGraph = null);

public sealed record AtsMapRoad(
    string Id,
    string Kind,
    string? Label,
    IReadOnlyList<AtsMapCoordinate> Geometry);

public sealed record AtsMapArea(
    string Id,
    string Kind,
    string? Label,
    IReadOnlyList<AtsMapCoordinate> Polygon);

public sealed record AtsMapPoint(
    string Id,
    string Kind,
    string Label,
    string? City,
    string? Company,
    AtsMapCoordinate Coordinate);

public sealed record AtsMapCoordinate(double X, double Z);

public sealed record AtsRouteGraph(
    IReadOnlyList<AtsRouteNode> Nodes,
    IReadOnlyList<AtsRouteEdge> Edges);

public sealed record AtsRouteNode(
    string Id,
    AtsMapCoordinate Coordinate);

public sealed record AtsRouteEdge(
    string Id,
    string From,
    string To,
    double Distance,
    string Kind,
    string? Label,
    IReadOnlyList<AtsMapCoordinate> Geometry);

public sealed record AtsMapViewport(
    string Source,
    IReadOnlyList<AtsMapRoad> Roads,
    IReadOnlyList<AtsMapArea> Areas,
    IReadOnlyList<AtsMapPoint> Points);

public sealed record AtsMapRoute(
    string Source,
    IReadOnlyList<AtsMapCoordinate> Geometry,
    IReadOnlyList<AtsMapRouteArrow> Arrows,
    IReadOnlyList<AtsRouteStop> Stops,
    bool IsRealMapData,
    string Status = "routed",
    double Distance = 0);

public sealed record AtsMapRouteArrow(
    AtsMapCoordinate Coordinate,
    double Heading);

public sealed record AtsRouteStop(
    string Id,
    string Kind,
    string Label,
    string? City,
    string? Company,
    AtsMapCoordinate Coordinate,
    int Order);

public sealed record AtsRouteStopRequest(
    string PointId);

public sealed record AtsRouteReorderRequest(
    IReadOnlyList<string> StopIds);
