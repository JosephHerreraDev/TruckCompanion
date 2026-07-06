namespace TruckCompanion.Api.Map;

public sealed record TileMapStatus(
    bool TilesReady,
    bool Stale,
    string State,
    string MapRoot,
    string ManifestPath,
    string InstalledAtsPath,
    string? CurrentFingerprint,
    string? MapFingerprint,
    string? Source,
    DateTimeOffset? LastGeneratedUtc,
    string RecommendedCommand,
    MapArtifactStatus Artifacts,
    IReadOnlyList<string> Missing);

public sealed record MapArtifactStatus(
    string? PmtilesUrl,
    string? SearchUrl,
    string? SpriteUrl,
    string? GlyphsUrl,
    bool ParserOutputReady,
    bool PmtilesReady,
    bool GraphReady,
    bool SearchReady,
    bool SpriteReady);

public sealed record TileMapManifest(
    int SchemaVersion,
    string Source,
    DateTimeOffset GeneratedAtUtc,
    string AtsInstallPath,
    string MapFingerprint,
    string ParserOutput,
    string GeneratedOutput,
    string PmtilesPath,
    string GraphPath,
    string SearchPath,
    string SpritesheetJsonPath,
    string SpritesheetImagePath);
