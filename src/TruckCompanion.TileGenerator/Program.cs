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
var existingManifest = TileManifest.TryRead(manifestPath);

if (!options.Force &&
    existingManifest is not null &&
    string.Equals(existingManifest.MapFingerprint, fingerprint.Value, StringComparison.OrdinalIgnoreCase) &&
    Directory.Exists(Path.Combine(options.OutputRoot, existingManifest.Version)) &&
    Directory.EnumerateFiles(Path.Combine(options.OutputRoot, existingManifest.Version), "*.png", SearchOption.AllDirectories).Any())
{
    Console.WriteLine($"Tiles are up to date: {existingManifest.Version}");
    Console.WriteLine($"Fingerprint: {fingerprint.Value}");
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
            ParseInt(values, "max-zoom", 4),
            ParseInt(values, "tile-size", 256),
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
