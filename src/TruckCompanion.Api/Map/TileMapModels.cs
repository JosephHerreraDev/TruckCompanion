namespace TruckCompanion.Api.Map;

public sealed record TileMapManifest(
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
    IReadOnlyList<TileMapCity> Cities);

public sealed record TileMapCity(
    string Name,
    string TokenName,
    double X,
    double Z);

public sealed record TileMapBounds(
    double MinX,
    double MinZ,
    double MaxX,
    double MaxZ);

public sealed record TilePixelSize(
    int Width,
    int Height);

public sealed record TileMapStatus(
    bool TilesReady,
    bool Stale,
    string State,
    string TileRoot,
    string ManifestPath,
    string InstalledAtsPath,
    string? CurrentFingerprint,
    string? Version,
    int TileCount,
    int? MaxZoom,
    string? Source,
    int DlcArchiveCount,
    DateTimeOffset? LastGeneratedUtc,
    string RecommendedCommand,
    IReadOnlyList<string> Missing);
