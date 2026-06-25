#nullable enable
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Newtonsoft.Json;
using TsMap;
using TsMap.Canvas;
using TsMap.Common;
using TsMap.Helpers;
using TsMap.Map.Overlays;

var options = GeneratorOptions.Parse(args);
Directory.CreateDirectory(options.OutputRoot);
var localAppData = Path.Combine(Directory.GetParent(options.OutputRoot)?.FullName ?? options.OutputRoot, "localappdata");
Directory.CreateDirectory(localAppData);
Environment.SetEnvironmentVariable("LOCALAPPDATA", localAppData);
var tsMapLogDir = Path.Combine(localAppData, "ts-map");
Directory.CreateDirectory(tsMapLogDir);
Environment.SetEnvironmentVariable("TRUCKCOMPANION_TSMAP_LOG_DIR", tsMapLogDir);
Directory.SetCurrentDirectory(AppContext.BaseDirectory);

if (!Directory.Exists(options.AtsPath))
{
    Console.Error.WriteLine($"ATS install path was not found: {options.AtsPath}");
    return 2;
}

var fingerprint = MapFingerprint.Compute(options);
var manifestPath = Path.Combine(options.OutputRoot, "tile-manifest.json");
var mapDatabasePath = GetMapDatabasePath(options.OutputRoot);
var existingManifest = TileManifest.TryRead(manifestPath);
var tilesAreCurrent = existingManifest is not null &&
    string.Equals(existingManifest.MapFingerprint, fingerprint.Value, StringComparison.OrdinalIgnoreCase) &&
    Directory.Exists(Path.Combine(options.OutputRoot, existingManifest.Version)) &&
    Directory.EnumerateFiles(Path.Combine(options.OutputRoot, existingManifest.Version), "*.png", SearchOption.AllDirectories).Any();

if (!options.Force && tilesAreCurrent && MapDatabaseIsCurrent(mapDatabasePath, fingerprint.Value))
{
    Console.WriteLine($"Tiles are up to date: {existingManifest!.Version}");
    Console.WriteLine($"Fingerprint: {fingerprint.Value}");
    Console.WriteLine($"Map data is up to date: {mapDatabasePath}");
    return 0;
}

if (!options.Force && tilesAreCurrent)
{
    Console.WriteLine($"Tiles are up to date: {existingManifest!.Version}");
    Console.WriteLine($"Fingerprint: {fingerprint.Value}");
    Console.WriteLine("Generating missing ATS map data.");

    var mapper = new TsMapper(options.AtsPath, options.ModPaths.Select(path => new Mod(path) { Load = true }).ToList());
    mapper.Parse();
    WriteMapDatabase(options, mapper, fingerprint);
    return 0;
}

var version = fingerprint.Value[..Math.Min(16, fingerprint.Value.Length)];
var versionRoot = Path.Combine(options.OutputRoot, version);
var workRoot = Path.Combine(options.OutputRoot, $".work-{version}-{Environment.ProcessId}");

if (Directory.Exists(workRoot))
{
    Directory.Delete(workRoot, true);
}

Directory.CreateDirectory(workRoot);

try
{
    Console.WriteLine($"ATS install: {options.AtsPath}");
    Console.WriteLine($"Output root: {options.OutputRoot}");
    Console.WriteLine($"Fingerprint: {fingerprint.Value}");
    Console.WriteLine($"Generating zoom {options.MinZoom}..{options.MaxZoom}");

    var mapper = new TsMapper(options.AtsPath, options.ModPaths.Select(path => new Mod(path) { Load = true }).ToList());
    mapper.Parse();
    var renderer = new TsMapRenderer(mapper);
    var palette = new SimpleMapPalette();
    var renderFlags = options.RenderFlags;

    var tileInfo = TileGeometry.FromMapper(mapper, options.TileSize, options.MapPadding, options.MinZoom, options.MaxZoom);

    WriteTileMapInfo(workRoot, tileInfo);
    GenerateTiles(workRoot, renderer, palette, renderFlags, options, tileInfo);
    WriteMapDatabase(options, mapper, fingerprint);

    if (Directory.Exists(versionRoot))
    {
        Directory.Delete(versionRoot, true);
    }

    Directory.Move(Path.Combine(workRoot, "Tiles"), versionRoot);
    var tileMapInfoPath = Path.Combine(workRoot, "TileMapInfo.json");
    if (File.Exists(tileMapInfoPath))
    {
        File.Move(tileMapInfoPath, Path.Combine(versionRoot, "TileMapInfo.json"), true);
    }

    var manifest = TileManifest.Create(
        version,
        options,
        tileInfo,
        fingerprint,
        mapper);

    var tempManifestPath = Path.Combine(options.OutputRoot, "tile-manifest.json.tmp");
    File.WriteAllText(tempManifestPath, System.Text.Json.JsonSerializer.Serialize(manifest, TileManifest.JsonOptions));
    File.Move(tempManifestPath, manifestPath, true);

    Console.WriteLine($"Generated {manifest.TileCount} tile(s).");
    Console.WriteLine($"Wrote {manifestPath}");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    return 1;
}
finally
{
    if (Directory.Exists(workRoot))
    {
        Directory.Delete(workRoot, true);
    }
}

static void WriteMapDatabase(GeneratorOptions options, TsMapper mapper, MapFingerprint fingerprint)
{
    var path = GetMapDatabasePath(options.OutputRoot);
    var outputRoot = Path.GetDirectoryName(path)!;
    Directory.CreateDirectory(outputRoot);

    var points = BuildPoints(mapper).ToArray();
    var routeGraph = BuildRouteGraph(mapper);
    var database = new AtsMapDatabase(
        $"ATS local map data from {options.AtsPath}",
        DateTimeOffset.UtcNow,
        true,
        [],
        [],
        points,
        fingerprint.Value,
        routeGraph);
    var tempPath = path + ".tmp";
    File.WriteAllText(tempPath, System.Text.Json.JsonSerializer.Serialize(database, AtsMapDatabase.JsonOptions));
    File.Move(tempPath, path, true);
    Console.WriteLine($"Wrote {path}");
}

static string GetMapDatabasePath(string tileOutputRoot)
{
    return Path.Combine(Directory.GetParent(tileOutputRoot)?.FullName ?? tileOutputRoot, "ats-map", "ats-map.db");
}

static bool MapDatabaseIsCurrent(string path, string fingerprint)
{
    if (!File.Exists(path))
    {
        return false;
    }

    try
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.TryGetProperty("mapFingerprint", out var fingerprintElement) &&
               document.RootElement.TryGetProperty("schemaVersion", out var schemaElement) &&
               schemaElement.GetInt32() == AtsMapDatabase.CurrentSchemaVersion &&
               string.Equals(fingerprintElement.GetString(), fingerprint, StringComparison.OrdinalIgnoreCase);
    }
    catch
    {
        return false;
    }
}

static IEnumerable<AtsMapPoint> BuildPoints(TsMapper mapper)
{
    foreach (var city in mapper.Cities.Where(city => city.Valid && !city.Hidden && city.City is not null))
    {
        var node = mapper.GetNodeByUid(city.NodeUid);
        if (node is null)
        {
            continue;
        }

        var name = mapper.Localization.GetLocaleValue(city.City.LocalizationToken) ?? city.City.Name;
        yield return new AtsMapPoint($"city-{Slug(city.City.Name)}-{city.Uid:x}", "city", name, name, null, new AtsMapCoordinate(node.X, node.Z));
    }

    foreach (var overlay in mapper.OverlayManager.GetOverlays())
    {
        var kind = GetPointKind(overlay);
        if (kind is null || overlay.IsSecret)
        {
            continue;
        }

        var label = GetPointLabel(overlay);
        yield return new AtsMapPoint(
            $"{kind}-{Slug(overlay.OverlayName)}-{HashCoordinate(overlay.Position.X, overlay.Position.Y)}",
            kind,
            label,
            null,
            kind == "company" ? label : null,
            new AtsMapCoordinate(overlay.Position.X, overlay.Position.Y));
    }
}

static AtsRouteGraph BuildRouteGraph(TsMapper mapper)
{
    var nodes = new Dictionary<string, AtsRouteNode>(StringComparer.OrdinalIgnoreCase);
    var edges = new List<AtsRouteEdge>();

    foreach (var road in mapper.Roads.Where(road => road.Valid && !road.Hidden))
    {
        var start = road.GetStartNode();
        var end = road.GetEndNode();
        if (start is null || end is null)
        {
            continue;
        }

        var startId = RouteNodeId("road", start.Uid);
        var endId = RouteNodeId("road", end.Uid);
        var geometry = GetRoadGeometry(road).ToArray();
        if (geometry.Length < 2)
        {
            continue;
        }

        AddNode(nodes, startId, geometry[0]);
        AddNode(nodes, endId, geometry[^1]);
        AddBidirectionalEdge(edges, $"road-{road.Uid:x}", startId, endId, ClassifyRoad(road), null, geometry);
    }

    foreach (var prefab in mapper.Prefabs.Where(prefab => prefab.Valid && !prefab.Hidden && prefab.Prefab?.MapPoints is not null))
    {
        AddPrefabGraph(mapper, prefab, nodes, edges);
    }

    AddNearbyConnectorEdges(nodes, edges);

    return new AtsRouteGraph(nodes.Values.ToArray(), edges);
}

static void AddNearbyConnectorEdges(
    IReadOnlyDictionary<string, AtsRouteNode> nodes,
    List<AtsRouteEdge> edges)
{
    const double connectorRadius = 650;
    const double connectorRadiusSquared = connectorRadius * connectorRadius;
    const int maxConnectorsPerNode = 2;

    var disjointSet = new RouteDisjointSet(nodes.Keys);
    foreach (var edge in edges)
    {
        disjointSet.Union(edge.From, edge.To);
    }

    var connectedPairs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var edge in edges)
    {
        connectedPairs.Add(RoutePairKey(edge.From, edge.To));
    }

    var grid = new Dictionary<(int X, int Z), List<AtsRouteNode>>();
    foreach (var node in nodes.Values)
    {
        var cell = RouteGridCell(node.Coordinate, connectorRadius);
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dz = -1; dz <= 1; dz++)
            {
                var neighbourCell = (cell.X + dx, cell.Z + dz);
                if (!grid.TryGetValue(neighbourCell, out var candidates))
                {
                    continue;
                }

                var addedForNode = 0;
                foreach (var candidate in candidates
                    .Select(candidate => new
                    {
                        Node = candidate,
                        DistanceSquared = DistanceSquared(node.Coordinate, candidate.Coordinate)
                    })
                    .Where(candidate => candidate.DistanceSquared > 0 && candidate.DistanceSquared <= connectorRadiusSquared)
                    .OrderBy(candidate => candidate.DistanceSquared))
                {
                    if (addedForNode >= maxConnectorsPerNode)
                    {
                        break;
                    }

                    if (string.Equals(disjointSet.Find(node.Id), disjointSet.Find(candidate.Node.Id), StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var pairKey = RoutePairKey(node.Id, candidate.Node.Id);
                    if (!connectedPairs.Add(pairKey))
                    {
                        continue;
                    }

                    var geometry = new[]
                    {
                        node.Coordinate,
                        candidate.Node.Coordinate
                    };
                    AddBidirectionalEdge(
                        edges,
                        $"connector-{edges.Count:x}",
                        node.Id,
                        candidate.Node.Id,
                        "connector",
                        "Junction connector",
                        geometry);
                    disjointSet.Union(node.Id, candidate.Node.Id);
                    addedForNode++;
                }
            }
        }

        if (!grid.TryGetValue(cell, out var cellNodes))
        {
            cellNodes = [];
            grid[cell] = cellNodes;
        }

        cellNodes.Add(node);
    }

    Console.WriteLine($"Route graph: {nodes.Count} node(s), {edges.Count} edge(s), {disjointSet.ComponentCount} connected component(s).");
}

static void AddPrefabGraph(
    TsMapper mapper,
    TsMap.TsItem.TsPrefabItem prefab,
    Dictionary<string, AtsRouteNode> nodes,
    List<AtsRouteEdge> edges)
{
    var originNode = mapper.GetNodeByUid(prefab.Nodes[0]);
    if (originNode is null || prefab.Prefab.PrefabNodes is null || prefab.Origin >= prefab.Prefab.PrefabNodes.Count)
    {
        return;
    }

    var originPoint = prefab.Prefab.PrefabNodes[prefab.Origin];
    var rotation = (float)(originNode.Rotation - Math.PI - Math.Atan2(originPoint.RotZ, originPoint.RotX) + Math.PI / 2);
    var prefabStartX = originNode.X - originPoint.X;
    var prefabStartZ = originNode.Z - originPoint.Z;
    var mapPoints = prefab.Prefab.MapPoints;
    var coordinates = mapPoints
        .Select(point => RenderHelper.RotatePoint(prefabStartX + point.X, prefabStartZ + point.Z, rotation, originNode.X, originNode.Z))
        .Select(point => new AtsMapCoordinate(point.X, point.Y))
        .ToArray();

    for (var i = 0; i < coordinates.Length; i++)
    {
        var point = mapPoints[i];
        if (point.Hidden || point.ControlNodeIndex < 0 || point.ControlNodeIndex >= prefab.Nodes.Count)
        {
            continue;
        }

        var fromExternalNode = mapper.GetNodeByUid(prefab.Nodes[point.ControlNodeIndex]);
        if (fromExternalNode is null)
        {
            continue;
        }

        var fromId = RouteNodeId("road", fromExternalNode.Uid);
        AddNode(nodes, fromId, new AtsMapCoordinate(fromExternalNode.X, fromExternalNode.Z));

        foreach (var neighbourIndex in point.Neighbours.Where(index => index >= 0 && index < coordinates.Length))
        {
            var neighbour = mapPoints[neighbourIndex];
            if (neighbour.Hidden ||
                neighbourIndex < i ||
                neighbour.ControlNodeIndex < 0 ||
                neighbour.ControlNodeIndex >= prefab.Nodes.Count ||
                neighbour.ControlNodeIndex == point.ControlNodeIndex)
            {
                continue;
            }

            var toExternalNode = mapper.GetNodeByUid(prefab.Nodes[neighbour.ControlNodeIndex]);
            if (toExternalNode is null)
            {
                continue;
            }

            var toId = RouteNodeId("road", toExternalNode.Uid);
            AddNode(nodes, toId, new AtsMapCoordinate(toExternalNode.X, toExternalNode.Z));
            AddBidirectionalEdge(edges, $"prefab-{prefab.Uid:x}-{i}-{neighbourIndex}", fromId, toId, "prefab", null,
            [
                new AtsMapCoordinate(fromExternalNode.X, fromExternalNode.Z),
                coordinates[i],
                coordinates[neighbourIndex],
                new AtsMapCoordinate(toExternalNode.X, toExternalNode.Z)
            ]);
        }
    }
}

static IReadOnlyList<AtsMapCoordinate> GetRoadGeometry(TsMap.TsItem.TsRoadItem road)
{
    if (road.HasPoints())
    {
        return road.GetPoints().Select(point => new AtsMapCoordinate(point.X, point.Y)).ToArray();
    }

    var startNode = road.GetStartNode();
    var endNode = road.GetEndNode();
    if (startNode is null || endNode is null)
    {
        return [];
    }

    var radius = Math.Sqrt(Math.Pow(startNode.X - endNode.X, 2) + Math.Pow(startNode.Z - endNode.Z, 2));
    var tanSx = Math.Cos(-(Math.PI * 0.5f - startNode.Rotation)) * radius;
    var tanEx = Math.Cos(-(Math.PI * 0.5f - endNode.Rotation)) * radius;
    var tanSz = Math.Sin(-(Math.PI * 0.5f - startNode.Rotation)) * radius;
    var tanEz = Math.Sin(-(Math.PI * 0.5f - endNode.Rotation)) * radius;
    var geometry = new List<AtsMapCoordinate>();

    for (var i = 0; i < 12; i++)
    {
        var s = i / 11f;
        var x = TsRoadLook.Hermite(s, startNode.X, endNode.X, tanSx, tanEx);
        var z = TsRoadLook.Hermite(s, startNode.Z, endNode.Z, tanSz, tanEz);
        geometry.Add(new AtsMapCoordinate(x, z));
    }

    return geometry;
}

static void AddNode(Dictionary<string, AtsRouteNode> nodes, string id, AtsMapCoordinate coordinate)
{
    nodes.TryAdd(id, new AtsRouteNode(id, coordinate));
}

static void AddBidirectionalEdge(
    List<AtsRouteEdge> edges,
    string id,
    string from,
    string to,
    string kind,
    string? label,
    IReadOnlyList<AtsMapCoordinate> geometry)
{
    if (geometry.Count < 2)
    {
        return;
    }

    var distance = GeometryLength(geometry);
    edges.Add(new AtsRouteEdge(id, from, to, distance, kind, label, geometry));
}

static double GeometryLength(IReadOnlyList<AtsMapCoordinate> geometry)
{
    var distance = 0d;
    for (var i = 1; i < geometry.Count; i++)
    {
        distance += Math.Sqrt(Math.Pow(geometry[i].X - geometry[i - 1].X, 2) + Math.Pow(geometry[i].Z - geometry[i - 1].Z, 2));
    }

    return distance;
}

static string ClassifyRoad(TsMap.TsItem.TsRoadItem road)
{
    var lanes = (road.RoadLook?.LanesLeft.Count ?? 0) + (road.RoadLook?.LanesRight.Count ?? 0);
    return lanes >= 4 ? "interstate" : lanes >= 2 ? "highway" : "surface";
}

static string? GetPointKind(MapOverlay overlay)
{
    if (overlay.OverlayType == OverlayType.Company)
    {
        return "company";
    }

    return overlay.TypeName switch
    {
        "Fuel" => "fuel",
        "Service" => "service",
        "Garage" => "garage",
        "TruckDealer" => "dealer",
        "Parking" => "parking",
        "WeightStation" => "weigh",
        _ => null
    };
}

static string GetPointLabel(MapOverlay overlay)
{
    if (overlay.OverlayType == OverlayType.Company)
    {
        return string.IsNullOrWhiteSpace(overlay.OverlayName)
            ? "Business"
            : CultureLabel(overlay.OverlayName);
    }

    return overlay.TypeName switch
    {
        "TruckDealer" => "Truck Dealer",
        "WeightStation" => "Weigh Station",
        _ => overlay.TypeName
    };
}

static string CultureLabel(string value)
{
    var text = value.Replace('_', ' ').Replace('.', ' ');
    return string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(part => part.Length <= 1 ? part.ToUpperInvariant() : char.ToUpperInvariant(part[0]) + part[1..]));
}

static string Slug(string value)
{
    var builder = new StringBuilder();
    foreach (var c in value.ToLowerInvariant())
    {
        if (char.IsLetterOrDigit(c))
        {
            builder.Append(c);
        }
        else if (builder.Length > 0 && builder[^1] != '-')
        {
            builder.Append('-');
        }
    }

    return builder.ToString().Trim('-');
}

static string HashCoordinate(float x, float z)
{
    return $"{Math.Round(x):0}-{Math.Round(z):0}".Replace("-", "m");
}

static string RouteNodeId(string type, ulong uid, int? index = null) => index is null
    ? $"{type}-{uid:x}"
    : $"{type}-{uid:x}-{index.Value}";

static (int X, int Z) RouteGridCell(AtsMapCoordinate coordinate, double cellSize) =>
    ((int)Math.Floor(coordinate.X / cellSize), (int)Math.Floor(coordinate.Z / cellSize));

static string RoutePairKey(string a, string b) => string.Compare(a, b, StringComparison.OrdinalIgnoreCase) <= 0
    ? $"{a}|{b}"
    : $"{b}|{a}";

static double DistanceSquared(AtsMapCoordinate a, AtsMapCoordinate b) =>
    Math.Pow(a.X - b.X, 2) + Math.Pow(a.Z - b.Z, 2);

static void GenerateTiles(
    string outputRoot,
    TsMapRenderer renderer,
    MapPalette palette,
    RenderFlags renderFlags,
    GeneratorOptions options,
    TileGeometry tileInfo)
{
    for (var z = options.MinZoom; z <= options.MaxZoom; z++)
    {
        var tileCount = (int)Math.Pow(2, z);
        var targetSize = tileCount * options.TileSize;
        var (position, zoom) = tileInfo.ForZoom(targetSize, targetSize);
        Console.WriteLine($"Zoom {z}: {tileCount * tileCount} tile(s)");

        for (var x = 0; x < tileCount; x++)
        {
            for (var y = 0; y < tileCount; y++)
            {
                SaveTileImage(z, x, y, position, zoom, outputRoot, renderer, palette, renderFlags, options.TileSize);
            }
        }
    }
}

static void SaveTileImage(
    int z,
    int x,
    int y,
    PointF position,
    float zoom,
    string outputRoot,
    TsMapRenderer renderer,
    MapPalette palette,
    RenderFlags renderFlags,
    int tileSize)
{
    using var bitmap = new Bitmap(tileSize, tileSize);
    using var graphics = Graphics.FromImage(bitmap);
    graphics.Clear(Color.FromArgb(27, 31, 36));
    var tilePosition = new PointF(
        x == 0 ? position.X : position.X + (bitmap.Width / zoom) * x,
        y == 0 ? position.Y : position.Y + (bitmap.Height / zoom) * y);

    renderer.Render(
        graphics,
        new Rectangle(0, 0, bitmap.Width, bitmap.Height),
        zoom,
        tilePosition,
        palette,
        renderFlags & ~RenderFlags.TextOverlay);

    var tileDirectory = Path.Combine(outputRoot, "Tiles", z.ToString(), x.ToString());
    Directory.CreateDirectory(tileDirectory);
    bitmap.Save(Path.Combine(tileDirectory, $"{y}.png"), ImageFormat.Png);
}

static void WriteTileMapInfo(string outputRoot, TileGeometry tileInfo)
{
    var tileMapInfo = new
    {
        x1 = tileInfo.X1,
        x2 = tileInfo.X2,
        y1 = tileInfo.Y1,
        y2 = tileInfo.Y2,
        minZoom = tileInfo.MinZoom,
        maxZoom = tileInfo.MaxZoom
    };

    File.WriteAllText(
        Path.Combine(outputRoot, "TileMapInfo.json"),
        JsonConvert.SerializeObject(tileMapInfo, Formatting.Indented));
}

internal sealed record GeneratorOptions(
    string AtsPath,
    string OutputRoot,
    int MinZoom,
    int MaxZoom,
    int TileSize,
    int MapPadding,
    RenderFlags RenderFlags,
    bool Force,
    IReadOnlyList<string> ModPaths)
{
    public const string Version = "2";

    public static GeneratorOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var key = args[i][2..];
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                values[key] = args[++i];
            }
            else
            {
                flags.Add(key);
            }
        }

        var atsPath = Get(values, "ats-path", @"C:\Program Files (x86)\Steam\steamapps\common\American Truck Simulator");
        var outputRoot = Path.GetFullPath(Get(values, "output-root", Path.Combine(Directory.GetCurrentDirectory(), ".truckcompanion-cache", "ats-tiles")));
        var renderFlags = ParseRenderFlags(Get(values, "render-flags", "Prefabs,Roads,MapAreas,MapOverlays,FerryConnections,CityNames,SecretRoads"));
        var modPaths = values.TryGetValue("mods", out var mods)
            ? mods.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];

        return new GeneratorOptions(
            Path.GetFullPath(atsPath),
            outputRoot,
            ParseInt(values, "min-zoom", 0),
            ParseInt(values, "max-zoom", 7),
            ParseInt(values, "tile-size", 512),
            ParseInt(values, "map-padding", 500),
            renderFlags,
            flags.Contains("force"),
            modPaths.Select(Path.GetFullPath).ToArray());
    }

    private static string Get(Dictionary<string, string> values, string key, string fallback)
    {
        return values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;
    }

    private static int ParseInt(Dictionary<string, string> values, string key, int fallback)
    {
        return values.TryGetValue(key, out var value) && int.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static RenderFlags ParseRenderFlags(string value)
    {
        var flags = RenderFlags.None;
        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse<RenderFlags>(part, true, out var parsed))
            {
                flags |= parsed;
            }
        }

        return flags == RenderFlags.None ? RenderFlags.All : flags;
    }
}

internal sealed record TileGeometry(
    float X1,
    float X2,
    float Y1,
    float Y2,
    int MinZoom,
    int MaxZoom,
    int TileSize,
    int MapPadding,
    float MapperMinX,
    float MapperMaxX,
    float MapperMinZ,
    float MapperMaxZ)
{
    public static TileGeometry FromMapper(TsMapper mapper, int tileSize, int mapPadding, int minZoom, int maxZoom)
    {
        var geometry = new TileGeometry(0, 0, 0, 0, minZoom, maxZoom, tileSize, mapPadding, mapper.minX, mapper.maxX, mapper.minZ, mapper.maxZ);
        var (position, zoom) = geometry.ForZoom(tileSize, tileSize);
        return geometry with
        {
            X1 = position.X,
            X2 = position.X + tileSize / zoom,
            Y1 = position.Y,
            Y2 = position.Y + tileSize / zoom
        };
    }

    public (PointF Position, float Zoom) ForZoom(float targetWidth, float targetHeight)
    {
        var mapWidth = MapperMaxX - MapperMinX + MapPadding * 2;
        var mapHeight = MapperMaxZ - MapperMinZ + MapPadding * 2;
        if (mapWidth > mapHeight)
        {
            var zoom = targetWidth / mapWidth;
            var z = MapperMinZ - MapPadding + -(targetHeight / zoom) / 2f + mapHeight / 2f;
            return (new PointF(MapperMinX - MapPadding, z), zoom);
        }

        var heightZoom = targetHeight / mapHeight;
        var x = MapperMinX - MapPadding + -(targetWidth / heightZoom) / 2f + mapWidth / 2f;
        return (new PointF(x, MapperMinZ - MapPadding), heightZoom);
    }
}

internal sealed record MapFingerprint(string Value, string? GameVersion, IReadOnlyList<FingerprintFile> Files)
{
    public static MapFingerprint Compute(GeneratorOptions options)
    {
        var files = EnumerateMapFiles(options).ToArray();
        var gameVersion = TryGetGameVersion(options.AtsPath);
        var builder = new StringBuilder();
        builder.AppendLine($"generator={GeneratorOptions.Version}");
        builder.AppendLine($"gameVersion={gameVersion}");
        builder.AppendLine($"minZoom={options.MinZoom};maxZoom={options.MaxZoom};tileSize={options.TileSize};padding={options.MapPadding};flags={options.RenderFlags}");

        foreach (var file in files)
        {
            builder.AppendLine($"{file.RelativePath}|{file.Length}|{file.LastWriteTimeUtc:O}");
        }

        foreach (var modPath in options.ModPaths.Order(StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine($"mod={modPath}");
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
        return new MapFingerprint(hash, gameVersion, files);
    }

    private static IEnumerable<FingerprintFile> EnumerateMapFiles(GeneratorOptions options)
    {
        var names = new[] { "base.scs", "def.scs", "base_map.scs" };
        foreach (var name in names)
        {
            var path = Path.Combine(options.AtsPath, name);
            if (File.Exists(path))
            {
                yield return FingerprintFile.From(options.AtsPath, path);
            }
        }

        foreach (var path in Directory.EnumerateFiles(options.AtsPath, "dlc*.scs", SearchOption.TopDirectoryOnly).Order(StringComparer.OrdinalIgnoreCase))
        {
            yield return FingerprintFile.From(options.AtsPath, path);
        }

        foreach (var path in options.ModPaths.Where(File.Exists).Order(StringComparer.OrdinalIgnoreCase))
        {
            yield return FingerprintFile.From(Path.GetDirectoryName(path) ?? "", path);
        }
    }

    private static string? TryGetGameVersion(string atsPath)
    {
        var exe = Path.Combine(atsPath, "bin", "win_x64", "amtrucks.exe");
        return File.Exists(exe) ? FileVersionInfo.GetVersionInfo(exe).ProductVersion : null;
    }
}

internal sealed record FingerprintFile(string RelativePath, long Length, DateTimeOffset LastWriteTimeUtc)
{
    public static FingerprintFile From(string root, string path)
    {
        var file = new FileInfo(path);
        return new FingerprintFile(
            Path.GetRelativePath(root, path).Replace('\\', '/'),
            file.Length,
            file.LastWriteTimeUtc);
    }
}

internal sealed record TileManifest(
    string Version,
    string MapFingerprint,
    int TileSize,
    int MinZoom,
    int MaxZoom,
    TileMapBounds AtsBounds,
    TilePixelSize PixelSizeAtMaxZoom,
    string TileUrlTemplate,
    DateTimeOffset GeneratedAtUtc,
    string Source,
    string? GameVersion,
    int DlcArchiveCount,
    int TileCount,
    IReadOnlyList<TileCity> Cities)
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static TileManifest Create(string version, GeneratorOptions options, TileGeometry geometry, MapFingerprint fingerprint, TsMapper mapper)
    {
        var tileCount = (int)Enumerable.Range(options.MinZoom, options.MaxZoom - options.MinZoom + 1).Sum(zoom => Math.Pow(4, zoom));
        var pixelSize = options.TileSize * (int)Math.Pow(2, options.MaxZoom);
        var dlcCount = fingerprint.Files.Count(file => Path.GetFileName(file.RelativePath).StartsWith("dlc", StringComparison.OrdinalIgnoreCase));
        var cities = mapper.Cities
            .Where(city => city.Valid && city.City is not null)
            .GroupBy(city => city.City.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var city = group.First();
                var localizedName = mapper.Localization.GetLocaleValue(city.City.LocalizationToken) ?? city.City.Name;
                return new TileCity(localizedName, city.City.Name, city.X, city.Z);
            })
            .OrderBy(city => city.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new TileManifest(
            version,
            fingerprint.Value,
            options.TileSize,
            options.MinZoom,
            options.MaxZoom,
            new TileMapBounds(geometry.X1, geometry.Y1, geometry.X2, geometry.Y2),
            new TilePixelSize(pixelSize, pixelSize),
            $"/tiles/ats/{version}/{{z}}/{{x}}/{{y}}.png",
            DateTimeOffset.UtcNow,
            $"ATS local tiles from {options.AtsPath}",
            fingerprint.GameVersion,
            dlcCount,
            tileCount,
            cities);
    }

    public static TileManifest? TryRead(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<TileManifest>(File.ReadAllText(path), JsonOptions);
        }
        catch
        {
            return null;
        }
    }
}

internal sealed record TileMapBounds(double MinX, double MinZ, double MaxX, double MaxZ);

internal sealed record TilePixelSize(int Width, int Height);

internal sealed record TileCity(string Name, string TokenName, double X, double Z);

internal sealed record AtsMapDatabase(
    string Source,
    DateTimeOffset GeneratedAtUtc,
    bool IsRealMapData,
    IReadOnlyList<AtsMapRoad> Roads,
    IReadOnlyList<AtsMapArea> Areas,
    IReadOnlyList<AtsMapPoint> Points,
    string? MapFingerprint,
    AtsRouteGraph? RouteGraph)
{
    public const int CurrentSchemaVersion = 5;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };
}

internal sealed record AtsMapRoad(
    string Id,
    string Kind,
    string? Label,
    IReadOnlyList<AtsMapCoordinate> Geometry);

internal sealed record AtsMapArea(
    string Id,
    string Kind,
    string? Label,
    IReadOnlyList<AtsMapCoordinate> Polygon);

internal sealed record AtsMapPoint(
    string Id,
    string Kind,
    string Label,
    string? City,
    string? Company,
    AtsMapCoordinate Coordinate);

internal sealed record AtsMapCoordinate(double X, double Z);

internal sealed record AtsRouteGraph(
    IReadOnlyList<AtsRouteNode> Nodes,
    IReadOnlyList<AtsRouteEdge> Edges);

internal sealed record AtsRouteNode(
    string Id,
    AtsMapCoordinate Coordinate);

internal sealed record AtsRouteEdge(
    string Id,
    string From,
    string To,
    double Distance,
    string Kind,
    string? Label,
    IReadOnlyList<AtsMapCoordinate> Geometry);

internal sealed class RouteDisjointSet
{
    private readonly Dictionary<string, string> parents;

    public RouteDisjointSet(IEnumerable<string> ids)
    {
        parents = ids.ToDictionary(id => id, id => id, StringComparer.OrdinalIgnoreCase);
        ComponentCount = parents.Count;
    }

    public int ComponentCount { get; private set; }

    public string Find(string id)
    {
        var parent = parents[id];
        if (string.Equals(parent, id, StringComparison.OrdinalIgnoreCase))
        {
            return parent;
        }

        var root = Find(parent);
        parents[id] = root;
        return root;
    }

    public void Union(string a, string b)
    {
        var rootA = Find(a);
        var rootB = Find(b);
        if (string.Equals(rootA, rootB, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        parents[rootB] = rootA;
        ComponentCount--;
    }
}
