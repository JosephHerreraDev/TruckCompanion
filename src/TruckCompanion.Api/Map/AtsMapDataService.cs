using System.Text.Json;

namespace TruckCompanion.Api.Map;

public sealed class AtsMapDataService(IWebHostEnvironment environment)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Lazy<AtsMapDatabase> database = new(() => LoadDatabase(environment.ContentRootPath));
    private readonly object routeLock = new();
    private readonly List<AtsRouteStop> routeStops = [];

    public AtsMapViewport GetViewport(double minX, double minZ, double maxX, double maxZ, int detail)
    {
        var db = database.Value;
        var padding = Math.Clamp(detail, 1, 5) * 2500;
        minX -= padding;
        minZ -= padding;
        maxX += padding;
        maxZ += padding;

        var roads = db.Roads.Where(road => road.Geometry.Any(point => InBounds(point, minX, minZ, maxX, maxZ))).ToArray();
        var areas = db.Areas.Where(area => area.Polygon.Any(point => InBounds(point, minX, minZ, maxX, maxZ))).ToArray();
        var points = db.Points.Where(point => InBounds(point.Coordinate, minX, minZ, maxX, maxZ)).ToArray();

        return new AtsMapViewport(
            db.Source,
            roads,
            areas,
            points);
    }

    public IReadOnlyList<AtsMapPoint> GetPois(string? kind = null)
    {
        var points = database.Value.Points;
        if (string.IsNullOrWhiteSpace(kind))
        {
            return points;
        }

        return points
            .Where(point => string.Equals(point.Kind, kind, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public AtsMapRoute GetRoute(double fromX, double fromZ, string? toCompany, string? toCity)
    {
        var db = database.Value;
        var start = new AtsMapCoordinate(fromX, fromZ);
        var stops = GetStops();
        var destination = FindDestination(db, start, toCompany, toCity);
        var routeTargets = stops
            .Select(stop => stop.Coordinate)
            .Concat(destination is null ? [] : [destination.Coordinate])
            .ToArray();

        if (routeTargets.Length == 0)
        {
            return new AtsMapRoute(db.Source, [start], [], stops, db.IsRealMapData);
        }

        var route = BuildRouteThroughTargets(db, start, routeTargets);
        return new AtsMapRoute(db.Source, route, CreateArrows(route), stops, db.IsRealMapData);
    }

    public IReadOnlyList<AtsRouteStop> GetStops()
    {
        lock (routeLock)
        {
            return routeStops
                .OrderBy(stop => stop.Order)
                .ToArray();
        }
    }

    public AtsRouteStop? AddStop(string pointId)
    {
        var db = database.Value;
        var point = db.Points.FirstOrDefault(candidate => string.Equals(candidate.Id, pointId, StringComparison.OrdinalIgnoreCase));
        if (point is null)
        {
            return null;
        }

        lock (routeLock)
        {
            if (routeStops.Any(stop => string.Equals(stop.Id, point.Id, StringComparison.OrdinalIgnoreCase)))
            {
                return routeStops.First(stop => string.Equals(stop.Id, point.Id, StringComparison.OrdinalIgnoreCase));
            }

            var stop = new AtsRouteStop(
                point.Id,
                point.Kind,
                point.Label,
                point.City,
                point.Company,
                point.Coordinate,
                routeStops.Count + 1);
            routeStops.Add(stop);
            return stop;
        }
    }

    public bool RemoveStop(string id)
    {
        lock (routeLock)
        {
            var removed = routeStops.RemoveAll(stop => string.Equals(stop.Id, id, StringComparison.OrdinalIgnoreCase)) > 0;
            ReindexStops();
            return removed;
        }
    }

    public IReadOnlyList<AtsRouteStop> ReorderStops(IReadOnlyList<string> stopIds)
    {
        lock (routeLock)
        {
            var byId = routeStops.ToDictionary(stop => stop.Id, StringComparer.OrdinalIgnoreCase);
            var reordered = stopIds
                .Where(byId.ContainsKey)
                .Select(id => byId[id])
                .Concat(routeStops.Where(stop => !stopIds.Contains(stop.Id, StringComparer.OrdinalIgnoreCase)))
                .Select((stop, index) => stop with { Order = index + 1 })
                .ToList();

            routeStops.Clear();
            routeStops.AddRange(reordered);
            return routeStops.ToArray();
        }
    }

    private static AtsMapDatabase LoadDatabase(string contentRoot)
    {
        var path = Path.Combine(contentRoot, "Data", "ats-map.db");
        if (File.Exists(path))
        {
            var json = File.ReadAllText(path);
            var parsed = JsonSerializer.Deserialize<AtsMapDatabase>(json, JsonOptions);
            if (parsed is not null)
            {
                return parsed;
            }
        }

        return SeedAtsMap.Create("embedded-seed");
    }

    private static AtsMapPoint? FindDestination(AtsMapDatabase db, AtsMapCoordinate start, string? toCompany, string? toCity)
    {
        return db.Points
            .Where(point => Matches(point, toCompany, toCity))
            .OrderBy(point => DistanceSquared(point.Coordinate, start))
            .FirstOrDefault();
    }

    private static IReadOnlyList<AtsMapCoordinate> BuildRouteThroughTargets(
        AtsMapDatabase db,
        AtsMapCoordinate start,
        IReadOnlyList<AtsMapCoordinate> targets)
    {
        var route = new List<AtsMapCoordinate> { start };
        var current = start;

        foreach (var target in targets)
        {
            var segment = BuildRouteOverNearestRoads(db, current, target);
            route.AddRange(segment.Skip(1));
            current = target;
        }

        return route;
    }

    private static IReadOnlyList<AtsMapCoordinate> BuildRouteOverNearestRoads(
        AtsMapDatabase db,
        AtsMapCoordinate start,
        AtsMapCoordinate destination)
    {
        var candidates = db.Roads
            .OrderBy(road => road.Geometry.Min(point => DistanceSquared(point, start)))
            .Take(24)
            .SelectMany(road => road.Geometry)
            .OrderBy(point => DistanceSquared(point, start))
            .Take(3)
            .Concat(
                db.Roads
                    .OrderBy(road => road.Geometry.Min(point => DistanceSquared(point, destination)))
                    .Take(24)
                    .SelectMany(road => road.Geometry)
                    .OrderBy(point => DistanceSquared(point, destination))
                    .Take(3))
            .Distinct()
            .OrderBy(point => DistanceSquared(point, start))
            .ToArray();

        return [start, .. candidates, destination];
    }

    private static IReadOnlyList<AtsMapRouteArrow> CreateArrows(IReadOnlyList<AtsMapCoordinate> route)
    {
        if (route.Count < 2)
        {
            return [];
        }

        const double spacing = 4200;
        var arrows = new List<AtsMapRouteArrow>();
        var distanceSinceArrow = spacing * 0.45;

        for (var i = 1; i < route.Count; i++)
        {
            var a = route[i - 1];
            var b = route[i];
            var dx = b.X - a.X;
            var dz = b.Z - a.Z;
            var segmentLength = Math.Sqrt(dx * dx + dz * dz);
            if (segmentLength <= 0)
            {
                continue;
            }

            distanceSinceArrow += segmentLength;
            if (distanceSinceArrow < spacing)
            {
                continue;
            }

            var ratio = Math.Clamp(1 - ((distanceSinceArrow - spacing) / segmentLength), 0, 1);
            var coordinate = new AtsMapCoordinate(a.X + dx * ratio, a.Z + dz * ratio);
            var heading = Math.Atan2(dx, -dz) * 180 / Math.PI;
            arrows.Add(new AtsMapRouteArrow(coordinate, heading));
            distanceSinceArrow = 0;
        }

        return arrows;
    }

    private static bool Matches(AtsMapPoint point, string? company, string? city)
    {
        var companyMatches = string.IsNullOrWhiteSpace(company) ||
                             string.Equals(point.Company, company, StringComparison.OrdinalIgnoreCase) ||
                             point.Label.Contains(company, StringComparison.OrdinalIgnoreCase);
        var cityMatches = string.IsNullOrWhiteSpace(city) ||
                          string.Equals(point.City, city, StringComparison.OrdinalIgnoreCase) ||
                          point.Label.Contains(city, StringComparison.OrdinalIgnoreCase);

        return companyMatches && cityMatches;
    }

    private static bool InBounds(AtsMapCoordinate point, double minX, double minZ, double maxX, double maxZ)
    {
        return point.X >= minX && point.X <= maxX && point.Z >= minZ && point.Z <= maxZ;
    }

    private static double DistanceSquared(AtsMapCoordinate a, AtsMapCoordinate b)
    {
        return Math.Pow(a.X - b.X, 2) + Math.Pow(a.Z - b.Z, 2);
    }

    private void ReindexStops()
    {
        for (var i = 0; i < routeStops.Count; i++)
        {
            routeStops[i] = routeStops[i] with { Order = i + 1 };
        }
    }
}
