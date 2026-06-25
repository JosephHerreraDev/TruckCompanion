using System.Text.Json;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace TruckCompanion.Api.Map;

public sealed class TileMapService(IWebHostEnvironment environment)
{
    private const string DefaultAtsPath = @"C:\Program Files (x86)\Steam\steamapps\common\American Truck Simulator";
    private const string GeneratorVersion = "2";
    private const string DefaultRenderFlags = "Prefabs, Roads, MapAreas, MapOverlays, FerryConnections, CityNames, SecretRoads";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string tileRoot = ResolveTileRoot(environment.ContentRootPath);
    private readonly string manifestPath = Path.Combine(ResolveTileRoot(environment.ContentRootPath), "tile-manifest.json");

    public TileMapStatus GetStatus()
    {
        var manifest = TryReadManifest();
        var missing = new List<string>();
        var fingerprint = TryComputeFingerprint(DefaultAtsPath);

        if (!Directory.Exists(tileRoot))
        {
            missing.Add("Tile root does not exist. Run tools\\generate-ats-tiles.ps1.");
        }

        if (!Directory.Exists(DefaultAtsPath))
        {
            missing.Add($"ATS install path was not found: {DefaultAtsPath}");
        }

        if (!File.Exists(manifestPath))
        {
            missing.Add("tile-manifest.json was not found.");
        }

        var versionRoot = manifest is null ? null : Path.Combine(tileRoot, manifest.Version);
        var tileCount = versionRoot is not null && Directory.Exists(versionRoot)
            ? Directory.EnumerateFiles(versionRoot, "*.png", SearchOption.AllDirectories).Count()
            : 0;

        if (manifest is not null && tileCount == 0)
        {
            missing.Add("No PNG tiles were found under the tile root.");
        }

        var tilesReady = manifest is not null && tileCount > 0;
        var stale = tilesReady &&
            manifest is not null &&
            fingerprint is not null &&
            !string.Equals(manifest.MapFingerprint, fingerprint.Value, StringComparison.OrdinalIgnoreCase);
        var state = !tilesReady ? "missing" : stale ? "stale" : "ready";

        return new TileMapStatus(
            tilesReady,
            stale,
            state,
            tileRoot,
            manifestPath,
            DefaultAtsPath,
            fingerprint?.Value,
            manifest?.Version,
            tileCount,
            manifest?.MaxZoom,
            manifest?.Source,
            fingerprint?.DlcArchiveCount ?? manifest?.DlcArchiveCount ?? 0,
            manifest?.GeneratedAtUtc,
            $@".\tools\generate-ats-tiles.ps1 -AtsInstallPath ""{DefaultAtsPath}"" -MaxZoom 7 -TileSize 512",
            missing);
    }

    public TileMapManifest? GetManifest() => TryReadManifest();

    public string? GetTilePath(string version, int z, int x, int y)
    {
        var manifest = TryReadManifest();
        if (manifest is null || !string.Equals(manifest.Version, version, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (z < manifest.MinZoom || z > manifest.MaxZoom || x < 0 || y < 0)
        {
            return null;
        }

        var path = Path.GetFullPath(Path.Combine(tileRoot, version, z.ToString(), x.ToString(), $"{y}.png"));
        if (!path.StartsWith(Path.GetFullPath(tileRoot), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return File.Exists(path) ? path : null;
    }

    private TileMapManifest? TryReadManifest()
    {
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(manifestPath);
            return JsonSerializer.Deserialize<TileMapManifest>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveTileRoot(string contentRoot)
    {
        var repoRoot = Path.GetFullPath(Path.Combine(contentRoot, "..", ".."));
        return Path.Combine(repoRoot, ".truckcompanion-cache", "ats-tiles");
    }

    private static MapFingerprint? TryComputeFingerprint(string atsPath)
    {
        if (!Directory.Exists(atsPath))
        {
            return null;
        }

        var files = EnumerateMapFiles(atsPath).ToArray();
        var gameVersion = TryGetGameVersion(atsPath);
        var builder = new StringBuilder();
        builder.AppendLine($"generator={GeneratorVersion}");
        builder.AppendLine($"gameVersion={gameVersion}");
        builder.AppendLine($"minZoom=0;maxZoom=7;tileSize=512;padding=500;flags={DefaultRenderFlags}");

        foreach (var file in files)
        {
            builder.AppendLine($"{file.RelativePath}|{file.Length}|{file.LastWriteTimeUtc:O}");
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
        var dlcArchiveCount = files.Count(file => Path.GetFileName(file.RelativePath).StartsWith("dlc", StringComparison.OrdinalIgnoreCase));
        return new MapFingerprint(hash, dlcArchiveCount);
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

    private static string? TryGetGameVersion(string atsPath)
    {
        var exe = Path.Combine(atsPath, "bin", "win_x64", "amtrucks.exe");
        return File.Exists(exe) ? FileVersionInfo.GetVersionInfo(exe).ProductVersion : null;
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

    private sealed record MapFingerprint(string Value, int DlcArchiveCount);
}
