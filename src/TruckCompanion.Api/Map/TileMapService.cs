using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TruckCompanion.Api.Map;

public sealed class TileMapService(IWebHostEnvironment environment)
{
    private const string DefaultAtsPath = @"C:\Program Files (x86)\Steam\steamapps\common\American Truck Simulator";
    private const string GeneratorVersion = "truckermudgeon-maps:d56d0e3fb319230e84284f3029f8bda2c4b572a2";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string mapRoot = ResolveMapRoot(environment.ContentRootPath);
    private readonly string manifestPath = Path.Combine(ResolveMapRoot(environment.ContentRootPath), "manifest.json");

    public TileMapStatus GetStatus()
    {
        var manifest = TryReadManifest();
        var fingerprint = TryComputeFingerprint(DefaultAtsPath);
        var parserOutputReady = manifest is not null && Directory.Exists(manifest.ParserOutput);
        var pmtilesReady = File.Exists(GetGeneratedPath(manifest, "ats.pmtiles"));
        var graphReady = File.Exists(GetGeneratedPath(manifest, "usa-graph.json"));
        var searchReady = File.Exists(GetGeneratedPath(manifest, "ats-search.geojson"));
        var spriteReady = File.Exists(GetGeneratedPath(manifest, "sprites.json")) &&
                          File.Exists(GetGeneratedPath(manifest, "sprites.png"));
        var missing = new List<string>();

        if (!Directory.Exists(DefaultAtsPath))
        {
            missing.Add($"ATS install path was not found: {DefaultAtsPath}");
        }

        if (!File.Exists(manifestPath))
        {
            missing.Add("manifest.json was not found. Run tools\\generate-ats-tiles.ps1.");
        }

        if (!parserOutputReady)
        {
            missing.Add("Parser output is missing.");
        }

        if (!pmtilesReady)
        {
            missing.Add("ats.pmtiles is missing.");
        }

        if (!graphReady)
        {
            missing.Add("usa-graph.json is missing.");
        }

        if (!searchReady)
        {
            missing.Add("ats-search.geojson is missing.");
        }

        if (!spriteReady)
        {
            missing.Add("MapLibre spritesheet files are missing.");
        }

        var ready = parserOutputReady && pmtilesReady && graphReady && searchReady && spriteReady;
        var stale = ready &&
            manifest is not null &&
            fingerprint is not null &&
            !string.Equals(manifest.MapFingerprint, fingerprint.Value, StringComparison.OrdinalIgnoreCase);

        return new TileMapStatus(
            ready,
            stale,
            !ready ? "missing" : stale ? "stale" : "ready",
            mapRoot,
            manifestPath,
            DefaultAtsPath,
            fingerprint?.Value,
            manifest?.MapFingerprint,
            manifest?.Source,
            manifest?.GeneratedAtUtc,
            $@".\tools\generate-ats-tiles.ps1 -AtsInstallPath ""{DefaultAtsPath}""",
            new MapArtifactStatus(
                pmtilesReady ? "/map/ats.pmtiles" : null,
                searchReady ? "/map/ats-search.geojson" : null,
                spriteReady ? "/map/spritesheet" : null,
                null,
                parserOutputReady,
                pmtilesReady,
                graphReady,
                searchReady,
                spriteReady),
            missing);
    }

    public TileMapManifest? GetManifest() => TryReadManifest();

    public string? GetMapFilePath(string fileName)
    {
        var manifest = TryReadManifest();
        var path = fileName switch
        {
            "ats.pmtiles" => GetGeneratedPath(manifest, "ats.pmtiles"),
            "ats-search.geojson" => GetGeneratedPath(manifest, "ats-search.geojson"),
            "spritesheet.json" => GetGeneratedPath(manifest, "sprites.json"),
            "spritesheet.png" => GetGeneratedPath(manifest, "sprites.png"),
            _ => null
        };

        if (path is null || !File.Exists(path))
        {
            return null;
        }

        var generatedRoot = Path.GetFullPath(manifest?.GeneratedOutput ?? Path.Combine(mapRoot, "generated"));
        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(generatedRoot, StringComparison.OrdinalIgnoreCase) ? fullPath : null;
    }

    private TileMapManifest? TryReadManifest()
    {
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TileMapManifest>(File.ReadAllText(manifestPath), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static string? GetGeneratedPath(TileMapManifest? manifest, string fileName)
    {
        var root = manifest?.GeneratedOutput;
        return string.IsNullOrWhiteSpace(root) ? null : Path.Combine(root, fileName);
    }

    private static string ResolveMapRoot(string contentRoot)
    {
        var repoRoot = Path.GetFullPath(Path.Combine(contentRoot, "..", ".."));
        return Path.Combine(repoRoot, ".truckcompanion-cache", "ats-map-v2");
    }

    private static MapFingerprint? TryComputeFingerprint(string atsPath)
    {
        if (!Directory.Exists(atsPath))
        {
            return null;
        }

        var files = EnumerateMapFiles(atsPath).ToArray();
        var builder = new StringBuilder();
        builder.AppendLine(GeneratorVersion);

        foreach (var file in files)
        {
            builder.AppendLine($"{file.RelativePath}|{file.Length}|{file.LastWriteTimeUtc:O}");
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
        return new MapFingerprint(hash);
    }

    private static IEnumerable<FingerprintFile> EnumerateMapFiles(string atsPath)
    {
        foreach (var name in new[] { "base.scs", "def.scs", "base_map.scs" })
        {
            var path = Path.Combine(atsPath, name);
            if (File.Exists(path))
            {
                yield return FingerprintFile.From(atsPath, path);
            }
        }

        foreach (var path in Directory.EnumerateFiles(atsPath, "dlc*.scs", SearchOption.TopDirectoryOnly).Order(StringComparer.OrdinalIgnoreCase))
        {
            yield return FingerprintFile.From(atsPath, path);
        }
    }

    private sealed record FingerprintFile(string RelativePath, long Length, DateTimeOffset LastWriteTimeUtc)
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

    private sealed record MapFingerprint(string Value);
}
