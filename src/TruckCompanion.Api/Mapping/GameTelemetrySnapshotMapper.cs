using TruckCompanion.Api.Models;
using TruckCompanion.Api.Services;
using TruckCompanion.Api.Telemetry.Funbit;

namespace TruckCompanion.Api.Mapping;

public sealed class GameTelemetrySnapshotMapper(CoordinateProjector projector)
{
    public TelemetrySnapshot Map(GameTelemetryFrame frame, DateTimeOffset now)
    {
        var position = projector.Project(frame.AtsX, frame.AtsZ);
        var remainingMiles = MetersToMiles(frame.RemainingNavigationMeters);
        var eta = frame.RemainingNavigationSeconds > 0
            ? now.AddSeconds(frame.RemainingNavigationSeconds).ToLocalTime().ToString("HH:mm")
            : "--:--";

        return new TelemetrySnapshot(
            new GameState(
                Connected: frame.PluginConnected,
                Paused: frame.Paused,
                GameName: frame.GameName,
                GameTime: frame.GameTime),
            new TruckState(
                Position: position,
                Heading: frame.Heading,
                SpeedMph: KphToMph(frame.SpeedKph),
                SpeedLimitMph: KphToMph(frame.SpeedLimitKph),
                SpeedKph: frame.SpeedKph,
                SpeedLimitKph: frame.SpeedLimitKph,
                FuelLiters: frame.FuelLiters,
                FuelCapacityLiters: frame.FuelCapacityLiters,
                FuelGallons: LitersToGallons(frame.FuelLiters),
                MaxVehicleDamagePercent: frame.DamagePercent,
                Damage: new DamageState(
                    frame.WearEnginePercent,
                    frame.WearTransmissionPercent,
                    frame.WearCabinPercent,
                    frame.WearChassisPercent,
                    frame.WearWheelsPercent,
                    frame.WearTrailerPercent)),
            new JobState(
                Active: !string.IsNullOrWhiteSpace(frame.DestinationCity),
                Source: new StopPoint(frame.SourceCity, frame.SourceCompany, null),
                Destination: new StopPoint(frame.DestinationCity, frame.DestinationCompany, null),
                Cargo: string.IsNullOrWhiteSpace(frame.TrailerName) ? "Trailer" : frame.TrailerName,
                Income: frame.Income,
                Eta: eta,
                RemainingMiles: remainingMiles,
                RemainingKilometers: frame.RemainingNavigationMeters / 1000d),
            new NavigationState(
                EstimatedDistanceMiles: remainingMiles,
                EstimatedDistanceKilometers: frame.RemainingNavigationMeters / 1000d,
                EstimatedTimeMinutes: frame.RemainingNavigationSeconds / 60d,
                EstimatedTimeSeconds: frame.RemainingNavigationSeconds,
                SpeedLimitMph: KphToMph(frame.SpeedLimitKph)),
            new ConnectionState("plugin", now, !frame.PluginConnected, BuildError(frame)));
    }

    private static string? BuildError(GameTelemetryFrame frame)
    {
        if (!frame.MemoryConnected)
        {
            return "Telemetry plugin shared memory was not found. Install the bundled plugin into ATS and start driving.";
        }

        if (!frame.PluginConnected)
        {
            return "Telemetry plugin is loaded, waiting for live simulator data.";
        }

        return null;
    }

    private static double KphToMph(double kph) => kph * 0.621371;
    private static double MetersToMiles(double meters) => meters / 1609.344;
    private static double LitersToGallons(double liters) => liters * 0.264172;
}
