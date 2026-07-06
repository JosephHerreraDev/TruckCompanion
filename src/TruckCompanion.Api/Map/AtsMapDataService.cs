using System.Text.Json;
using System.Text.Json.Serialization;

namespace TruckCompanion.Api.Map;

public sealed class AtsMapDataService(IWebHostEnvironment environment)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Lazy<MapRuntimeData> data = new(() => LoadData(environment.ContentRootPath));
    private readonly object routeLock = new();
    private readonly List<AtsRouteStop> routeStops = [];

    public AtsMapViewport GetViewport(double minX, double minZ, double maxX, double maxZ, int detail)
    {
        var padding = Math.Clamp(detail, 1, 5) * 2500;
        minX -= padding;
        minZ -= padding;
        maxX += padding;
        maxZ += padding;

        var points = data.Value.Points
            .Where(point => InBounds(point.Coordinate, minX, minZ, maxX, maxZ))
            .ToArray();

        return new AtsMapViewport(data.Value.Source, [], [], points);
    }

    public IReadOnlyList<AtsMapPoint> GetPois(string? kind = null)
    {
        var points = data.Value.Points;
        return string.IsNullOrWhiteSpace(kind)
            ? points
            : points.Where(point => string.Equals(point.Kind, kind, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    public AtsMapRoute GetRoute(double fromX, double fromZ, string? toCompany, string? toCity)
    {
        var runtime = data.Value;
        var start = new AtsMapCoordinate(fromX, fromZ);
        var stops = GetStops();
        var destination = FindDestination(runtime.Points, start, toCompany, toCity);
        var targets = stops
            .Select(stop => stop.Coordinate)
            .Concat(destination is null ? [] : [destination.Coordinate])
            .ToArray();

        if (targets.Length == 0)
        {
            return new AtsMapRoute(runtime.Source, [start], [], stops, runtime.Ready, "noDestination");
        }

        if (!runtime.Ready)
        {
            var fallback = BuildDirectRoute(start, targets);
            return new AtsMapRoute(runtime.Source, fallback, CreateArrows(fallback), stops, false, "seedData", RouteLength(fallback));
        }

        var route = BuildRouteThroughTargets(runtime, start, targets);
        if (route.Geometry.Count == 0)
        {
            return new AtsMapRoute(runtime.Source, [], [], stops, true, "noPath");
        }

        return new AtsMapRoute(
            runtime.Source,
            route.Geometry,
            CreateArrows(route.Geometry),
            stops,
            true,
            route.Complete ? "routed" : "partialRoute",
            route.Distance);
    }

    public IReadOnlyList<AtsRouteStop> GetStops()
    {
        lock (routeLock)
        {
            return routeStops.OrderBy(stop => stop.Order).ToArray();
        }
    }

    public AtsRouteStop? AddStop(string pointId)
    {
        var point = data.Value.Points.FirstOrDefault(candidate => string.Equals(candidate.Id, pointId, StringComparison.OrdinalIgnoreCase));
        if (point is null)
        {
            return null;
        }

        lock (routeLock)
        {
            var existing = routeStops.FirstOrDefault(stop => string.Equals(stop.Id, point.Id, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                return existing;
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
            var requested = new HashSet<string>(stopIds, StringComparer.OrdinalIgnoreCase);
            var reordered = stopIds
                .Where(byId.ContainsKey)
                .Select(id => byId[id])
                .Concat(routeStops.Where(stop => !requested.Contains(stop.Id)))
                .Select((stop, index) => stop with { Order = index + 1 })
                .ToList();

            routeStops.Clear();
            routeStops.AddRange(reordered);
            return routeStops.ToArray();
        }
    }

    private static MapRuntimeData LoadData(string contentRoot)
    {
        var repoRoot = Path.GetFullPath(Path.Combine(contentRoot, "..", ".."));
        var mapRoot = Path.Combine(repoRoot, ".truckcompanion-cache", "ats-map-v2");
        var manifestPath = Path.Combine(mapRoot, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            var seed = SeedAtsMap.Create("embedded-seed");
            return new MapRuntimeData(seed.Source, false, seed.Points, new Dictionary<string, GraphNode>(), new Dictionary<string, GraphNeighbors>());
        }

        var manifest = JsonSerializer.Deserialize<TileMapManifest>(File.ReadAllText(manifestPath), JsonOptions);
        if (manifest is null)
        {
            throw new InvalidOperationException("ATS map v2 manifest could not be parsed.");
        }

        var nodes = LoadNodes(Path.Combine(manifest.ParserOutput, "usa-nodes.json"));
        var graph = LoadGraph(manifest.GraphPath);
        var points = LoadSearchPoints(manifest.SearchPath);
        return new MapRuntimeData(manifest.Source, nodes.Count > 0 && graph.Count > 0, points, nodes, graph);
    }

    private static Dictionary<string, GraphNode> LoadNodes(string path)
    {
        if (!File.Exists(path))
        {
            return new Dictionary<string, GraphNode>(StringComparer.OrdinalIgnoreCase);
        }

        var nodes = new Dictionary<string, GraphNode>(StringComparer.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var element in document.RootElement.EnumerateArray())
        {
            var id = element.GetProperty("uid").GetString();
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            nodes[id] = new GraphNode(id, element.GetProperty("x").GetDouble(), element.GetProperty("y").GetDouble());
        }

        return nodes;
    }

    private static Dictionary<string, GraphNeighbors> LoadGraph(string path)
    {
        if (!File.Exists(path))
        {
            return new Dictionary<string, GraphNeighbors>(StringComparer.OrdinalIgnoreCase);
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var graph = new Dictionary<string, GraphNeighbors>(StringComparer.OrdinalIgnoreCase);
        var graphElement = document.RootElement.GetProperty("graph");

        foreach (var entry in graphElement.EnumerateArray())
        {
            var nodeId = entry[0].GetString();
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                continue;
            }

            var neighbors = entry[1];
            graph[nodeId] = new GraphNeighbors(
                ReadNeighbors(neighbors, "forward"),
                ReadNeighbors(neighbors, "backward"));
        }

        return graph;
    }

    private static IReadOnlyList<GraphNeighbor> ReadNeighbors(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var neighbors) || neighbors.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<GraphNeighbor>();
        foreach (var neighbor in neighbors.EnumerateArray())
        {
            var nodeUid = neighbor.GetProperty("nodeUid").GetString();
            if (string.IsNullOrWhiteSpace(nodeUid))
            {
                continue;
            }

            result.Add(new GraphNeighbor(
                nodeUid,
                neighbor.TryGetProperty("distance", out var distance) ? distance.GetDouble() : 0,
                neighbor.TryGetProperty("duration", out var duration) ? duration.GetDouble() : 0,
                neighbor.TryGetProperty("direction", out var direction) ? direction.GetString() ?? "forward" : "forward"));
        }

        return result;
    }

    private static IReadOnlyList<AtsMapPoint> LoadSearchPoints(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var points = new List<AtsMapPoint>();
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("features", out var features))
        {
            return [];
        }

        var index = 0;
        foreach (var feature in features.EnumerateArray())
        {
            var properties = feature.GetProperty("properties");
            var geometry = feature.GetProperty("geometry");
            var coordinates = geometry.GetProperty("coordinates");
            var type = properties.TryGetProperty("type", out var typeElement) ? typeElement.GetString() ?? "poi" : "poi";
            var label = properties.TryGetProperty("label", out var labelElement) ? labelElement.GetString() ?? type : type;
            var city = TryGetCity(properties);
            var company = type == "company" ? label : null;
            var kind = ToPointKind(type, properties);
            points.Add(new AtsMapPoint(
                $"{kind}-{Slug(label)}-{index++}",
                kind,
                label,
                city,
                company,
                new AtsMapCoordinate(coordinates[0].GetDouble(), coordinates[1].GetDouble())));
        }

        return points;
    }

    private static string? TryGetCity(JsonElement properties)
    {
        if (!properties.TryGetProperty("city", out var cityElement) || cityElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return cityElement.TryGetProperty("name", out var name) ? name.GetString() : null;
    }

    private static string ToPointKind(string type, JsonElement properties)
    {
        if (type == "city" || type == "scenery")
        {
            return "city";
        }

        if (type == "dealer")
        {
            return "dealer";
        }

        if (type == "company")
        {
            return "company";
        }

        if (properties.TryGetProperty("sprite", out var spriteElement))
        {
            var sprite = spriteElement.GetString() ?? "";
            if (sprite.Contains("gas", StringComparison.OrdinalIgnoreCase))
            {
                return "fuel";
            }

            if (sprite.Contains("service", StringComparison.OrdinalIgnoreCase))
            {
                return "service";
            }
        }

        return type;
    }

    private static AtsMapPoint? FindDestination(IReadOnlyList<AtsMapPoint> points, AtsMapCoordinate start, string? toCompany, string? toCity)
    {
        if (string.IsNullOrWhiteSpace(toCompany) && string.IsNullOrWhiteSpace(toCity))
        {
            return null;
        }

        return points
            .Where(point => Matches(point, toCompany, toCity))
            .OrderBy(point => DistanceSquared(point.Coordinate, start))
            .FirstOrDefault();
    }

    private static (IReadOnlyList<AtsMapCoordinate> Geometry, double Distance, bool Complete) BuildRouteThroughTargets(
        MapRuntimeData runtime,
        AtsMapCoordinate start,
        IReadOnlyList<AtsMapCoordinate> targets)
    {
        var route = new List<AtsMapCoordinate> { start };
        var totalDistance = 0d;
        var current = start;
        var complete = true;

        foreach (var target in targets)
        {
            var segment = FindShortestPath(runtime, current, target);
            if (segment.Geometry.Count == 0)
            {
                return ([], 0, false);
            }

            route.AddRange(segment.Geometry.Skip(1));
            totalDistance += segment.Distance;
            complete = complete && segment.Complete;
            current = target;
        }

        return (route, totalDistance, complete);
    }

    private static (IReadOnlyList<AtsMapCoordinate> Geometry, double Distance, bool Complete) FindShortestPath(
        MapRuntimeData runtime,
        AtsMapCoordinate start,
        AtsMapCoordinate destination)
    {
        var startNode = FindNearestNode(runtime.Nodes.Values, start);
        var destinationNode = FindNearestNode(runtime.Nodes.Values, destination);
        var forward = FindShortestPath(runtime, startNode.Id, destinationNode.Id, "forward");
        var backward = FindShortestPath(runtime, startNode.Id, destinationNode.Id, "backward");
        var candidates = new[] { forward, backward }
            .Where(route => route.NodeIds.Count > 0)
            .OrderBy(route => route.Distance)
            .ToArray();

        if (candidates.Length == 0)
        {
            return ([], 0, false);
        }

        var best = candidates[0];
        var geometry = new List<AtsMapCoordinate> { start };
        geometry.AddRange(best.NodeIds
            .Select(id => runtime.Nodes[id])
            .Select(node => new AtsMapCoordinate(node.X, node.Y)));
        geometry.Add(destination);

        return (geometry, RouteLength(geometry), true);
    }

    private static (IReadOnlyList<string> NodeIds, double Distance) FindShortestPath(
        MapRuntimeData runtime,
        string startNodeId,
        string destinationNodeId,
        string startDirection)
    {
        var start = new RouteState(startNodeId, startDirection);
        var frontier = new PriorityQueue<RouteState, double>();
        var distances = new Dictionary<RouteState, double>();
        var previous = new Dictionary<RouteState, RouteState>();
        var visited = new HashSet<RouteState>();

        distances[start] = 0;
        frontier.Enqueue(start, 0);

        RouteState? end = null;
        while (frontier.TryDequeue(out var current, out _))
        {
            if (!visited.Add(current))
            {
                continue;
            }

            if (string.Equals(current.NodeId, destinationNodeId, StringComparison.OrdinalIgnoreCase))
            {
                end = current;
                break;
            }

            if (!runtime.Graph.TryGetValue(current.NodeId, out var neighbors))
            {
                continue;
            }

            var edges = string.Equals(current.Direction, "forward", StringComparison.OrdinalIgnoreCase)
                ? neighbors.Forward
                : neighbors.Backward;

            foreach (var edge in edges)
            {
                if (!runtime.Nodes.ContainsKey(edge.NodeUid))
                {
                    continue;
                }

                var next = new RouteState(edge.NodeUid, edge.Direction);
                var cost = edge.Duration > 0 ? edge.Duration : edge.Distance;
                var nextDistance = distances[current] + cost;
                if (distances.TryGetValue(next, out var existing) && existing <= nextDistance)
                {
                    continue;
                }

                distances[next] = nextDistance;
                previous[next] = current;
                var priority = nextDistance + Math.Sqrt(DistanceSquared(runtime.Nodes[edge.NodeUid].Coordinate, runtime.Nodes[destinationNodeId].Coordinate));
                frontier.Enqueue(next, priority);
            }
        }

        if (end is null)
        {
            return ([], 0);
        }

        var states = new List<RouteState> { end.Value };
        var step = end.Value;
        while (!step.Equals(start))
        {
            step = previous[step];
            states.Add(step);
        }

        states.Reverse();
        return (states.Select(state => state.NodeId).ToArray(), distances[end.Value]);
    }

    private static GraphNode FindNearestNode(IEnumerable<GraphNode> nodes, AtsMapCoordinate coordinate) =>
        nodes.OrderBy(node => DistanceSquared(node.Coordinate, coordinate)).First();

    private static IReadOnlyList<AtsMapCoordinate> BuildDirectRoute(AtsMapCoordinate start, IReadOnlyList<AtsMapCoordinate> targets)
    {
        var route = new List<AtsMapCoordinate> { start };
        route.AddRange(targets);
        return route;
    }

    private static double RouteLength(IReadOnlyList<AtsMapCoordinate> route)
    {
        var length = 0d;
        for (var i = 1; i < route.Count; i++)
        {
            length += Math.Sqrt(DistanceSquared(route[i - 1], route[i]));
        }

        return length;
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

    private static bool InBounds(AtsMapCoordinate point, double minX, double minZ, double maxX, double maxZ) =>
        point.X >= minX && point.X <= maxX && point.Z >= minZ && point.Z <= maxZ;

    private static double DistanceSquared(AtsMapCoordinate a, AtsMapCoordinate b) =>
        Math.Pow(a.X - b.X, 2) + Math.Pow(a.Z - b.Z, 2);

    private static string Slug(string value)
    {
        var chars = value.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        return new string(chars).Trim('-');
    }

    private void ReindexStops()
    {
        for (var i = 0; i < routeStops.Count; i++)
        {
            routeStops[i] = routeStops[i] with { Order = i + 1 };
        }
    }

    private sealed record MapRuntimeData(
        string Source,
        bool Ready,
        IReadOnlyList<AtsMapPoint> Points,
        IReadOnlyDictionary<string, GraphNode> Nodes,
        IReadOnlyDictionary<string, GraphNeighbors> Graph);

    private sealed record GraphNode(string Id, double X, double Y)
    {
        [JsonIgnore]
        public AtsMapCoordinate Coordinate => new(X, Y);
    }

    private sealed record GraphNeighbors(IReadOnlyList<GraphNeighbor> Forward, IReadOnlyList<GraphNeighbor> Backward);

    private sealed record GraphNeighbor(string NodeUid, double Distance, double Duration, string Direction);

    private readonly record struct RouteState(string NodeId, string Direction);
}
