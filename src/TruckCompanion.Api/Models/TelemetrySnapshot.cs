namespace TruckCompanion.Api.Models;

public sealed record TelemetrySnapshot(
    GameState Game,
    TruckState Truck,
    JobState Job,
    NavigationState Navigation,
    DriverState Driver,
    ConnectionState Connection);

public sealed record GameState(
    bool Connected,
    bool Paused,
    string GameName,
    string GameTime);

public sealed record TruckState(
    MapPosition Position,
    double Heading,
    double SpeedMph,
    double SpeedLimitMph,
    double SpeedKph,
    double SpeedLimitKph,
    double FuelLiters,
    double FuelCapacityLiters,
    double FuelGallons,
    double MaxVehicleDamagePercent,
    DamageState Damage);

public sealed record JobState(
    bool Active,
    StopPoint Source,
    StopPoint Destination,
    string Cargo,
    double Income,
    string Eta,
    double RemainingMiles,
    double RemainingKilometers);

public sealed record NavigationState(
    double EstimatedDistanceMiles,
    double EstimatedDistanceKilometers,
    double EstimatedTimeMinutes,
    double EstimatedTimeSeconds,
    double SpeedLimitMph);

public sealed record DriverState(
    double? SleepHoursLeft);

public sealed record DamageState(
    double EnginePercent,
    double TransmissionPercent,
    double CabinPercent,
    double ChassisPercent,
    double WheelsPercent,
    double TrailerPercent);

public sealed record ConnectionState(
    string Source,
    DateTimeOffset LastUpdateUtc,
    bool Stale,
    string? Error);

public sealed record MapPosition(
    double AtsX,
    double AtsZ,
    double Latitude,
    double Longitude);

public sealed record StopPoint(
    string City,
    string Company,
    MapPosition? Position);
