namespace TruckCompanion.Api.Models;

public sealed record Poi(
    string Id,
    string Type,
    string Name,
    string City,
    string State,
    MapPosition Position);

public sealed record CalibrationAnchor(
    string City,
    string State,
    double AtsX,
    double AtsZ,
    double Latitude,
    double Longitude);

public sealed record CalibrationMetadata(
    string Strategy,
    string Accuracy,
    IReadOnlyList<CalibrationAnchor> Anchors);

public sealed record ApiStatus(
    string Mode,
    bool Connected,
    bool Stale,
    string? Error,
    DateTimeOffset LastUpdateUtc);
