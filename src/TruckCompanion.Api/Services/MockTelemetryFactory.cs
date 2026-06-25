using TruckCompanion.Api.Models;

namespace TruckCompanion.Api.Services;

public static class MockTelemetryFactory
{
    public static TelemetrySnapshot Create(DateTimeOffset now, string source)
    {
        var seconds = now.ToUnixTimeSeconds();
        var progress = (seconds % 240) / 240d;
        var atsX = -80300 + (progress * 23100);
        var atsZ = 73500 + (Math.Sin(progress * Math.PI) * 11000);
        var projector = new CoordinateProjector();
        var position = projector.Project(atsX, atsZ);
        var remainingMiles = Math.Max(0, 286 - (progress * 286));
        var speedMph = 63 + Math.Sin(progress * Math.PI) * 7;
        var fuelGallons = 98 - progress * 18;

        return new TelemetrySnapshot(
            new GameState(true, false, "American Truck Simulator", now.ToLocalTime().ToString("HH:mm")),
            new TruckState(
                position,
                72 + (progress * 35),
                speedMph,
                65,
                speedMph / 0.621371,
                65 / 0.621371,
                fuelGallons / 0.264172,
                560,
                fuelGallons,
                2.4,
                new DamageState(1.1, 0.2, 0.4, 2.4, 0.9, 0.3)),
            new JobState(
                true,
                new StopPoint("Los Angeles", "Voltison Motors", projector.Project(-80300, 73500)),
                new StopPoint("Phoenix", "Rail Export", projector.Project(-57500, 84000)),
                "Medical Equipment",
                18450,
                now.AddMinutes(310 * (1 - progress)).ToLocalTime().ToString("HH:mm"),
                remainingMiles,
                remainingMiles * 1.609344),
            new NavigationState(remainingMiles, remainingMiles * 1.609344, remainingMiles / 58 * 60, remainingMiles / 58 * 3600, 65),
            new ConnectionState(source, now, false, null));
    }
}
