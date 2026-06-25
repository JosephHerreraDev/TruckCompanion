using System.Runtime.Versioning;
using TruckCompanion.Api.Telemetry.Funbit;

namespace TruckCompanion.Api.Telemetry.TruckCompanion;

[SupportedOSPlatform("windows")]
public sealed class TruckCompanionTelemetryReader : IDisposable
{
    private const string MappedFileName = "Local\\TruckCompanionTelemetry";
    private readonly SharedProcessMemory<TruckCompanionTelemetryStructure> sharedMemory = new(MappedFileName);
    private readonly object syncRoot = new();

    public bool IsConnected => sharedMemory.IsConnected;

    public GameTelemetryFrame Read()
    {
        lock (syncRoot)
        {
            sharedMemory.Data = default;
            sharedMemory.Read();
            return ToFrame(sharedMemory.Data, IsConnected);
        }
    }

    public void Dispose()
    {
        sharedMemory.Dispose();
    }

    private static GameTelemetryFrame ToFrame(TruckCompanionTelemetryStructure data, bool memoryConnected)
    {
        var pluginConnected = memoryConnected && data.Version == 1 && data.Connected != 0;

        return new GameTelemetryFrame(
            MemoryConnected: memoryConnected,
            PluginConnected: pluginConnected,
            Paused: data.Paused != 0,
            GameName: "American Truck Simulator",
            GameTime: SecondsToClock(data.GameTimeSeconds),
            AtsX: data.TruckX,
            AtsY: data.TruckY,
            AtsZ: data.TruckZ,
            Heading: RadiansToDegrees(data.HeadingRadians),
            SpeedKph: data.SpeedMps * 3.6,
            SpeedLimitKph: data.SpeedLimitMps * 3.6,
            FuelLiters: data.FuelLiters,
            FuelCapacityLiters: data.FuelCapacityLiters,
            FuelRateLitersPerHour: data.FuelRateLitersPerHour,
            DamagePercent: MaxWear(data) * 100,
            WearEnginePercent: data.WearEngine * 100,
            WearTransmissionPercent: data.WearTransmission * 100,
            WearCabinPercent: data.WearCabin * 100,
            WearChassisPercent: data.WearChassis * 100,
            WearWheelsPercent: data.WearWheels * 100,
            WearTrailerPercent: data.WearTrailer * 100,
            SourceCity: data.SourceCity ?? string.Empty,
            SourceCompany: data.SourceCompany ?? string.Empty,
            DestinationCity: data.DestinationCity ?? string.Empty,
            DestinationCompany: data.DestinationCompany ?? string.Empty,
            TrailerName: data.Cargo ?? string.Empty,
            Income: 0,
            RemainingNavigationMeters: Math.Max(0, (int)data.NavigationDistanceMeters),
            RemainingNavigationSeconds: Math.Max(0, (int)data.NavigationTimeSeconds),
            SleepHoursLeft: null);
    }

    private static string SecondsToClock(double seconds)
    {
        if (seconds <= 0)
        {
            return "--:--";
        }

        var time = TimeSpan.FromSeconds(seconds);
        return $"{(int)time.TotalHours % 24:00}:{time.Minutes:00}";
    }

    private static double RadiansToDegrees(double radians)
    {
        var degrees = radians * 180 / Math.PI;
        return degrees < 0 ? degrees + 360 : degrees;
    }

    private static double MaxWear(TruckCompanionTelemetryStructure data)
    {
        return Math.Max(
            Math.Max(data.WearEngine, data.WearTransmission),
            Math.Max(Math.Max(data.WearCabin, data.WearChassis), Math.Max(data.WearWheels, data.WearTrailer)));
    }
}
