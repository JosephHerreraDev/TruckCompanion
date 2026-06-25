using System.Text;
using System.Runtime.Versioning;

namespace TruckCompanion.Api.Telemetry.Funbit;

// Reads shared memory produced by the bundled Funbit-derived SCS telemetry plugin.
[SupportedOSPlatform("windows")]
public sealed class GameTelemetryReader : IDisposable
{
    private const string MappedFileName = "Local\\Ets2TelemetryServer";
    private readonly SharedProcessMemory<Ets2TelemetryStructure> sharedMemory = new(MappedFileName);
    private readonly object syncRoot = new();

    public bool IsConnected => sharedMemory.IsConnected;

    public GameTelemetryFrame Read()
    {
        lock (syncRoot)
        {
            sharedMemory.Data = default;
            sharedMemory.Read();
            return GameTelemetryFrame.From(sharedMemory.Data, IsConnected);
        }
    }

    public void Dispose()
    {
        sharedMemory.Dispose();
    }
}

public sealed record GameTelemetryFrame(
    bool MemoryConnected,
    bool PluginConnected,
    bool Paused,
    string GameName,
    string GameTime,
    double AtsX,
    double AtsY,
    double AtsZ,
    double Heading,
    double SpeedKph,
    double SpeedLimitKph,
    double FuelLiters,
    double FuelCapacityLiters,
    double FuelRateLitersPerHour,
    double DamagePercent,
    double WearEnginePercent,
    double WearTransmissionPercent,
    double WearCabinPercent,
    double WearChassisPercent,
    double WearWheelsPercent,
    double WearTrailerPercent,
    string SourceCity,
    string SourceCompany,
    string DestinationCity,
    string DestinationCompany,
    string TrailerName,
    double Income,
    int RemainingNavigationMeters,
    int RemainingNavigationSeconds)
{
    internal static GameTelemetryFrame From(Ets2TelemetryStructure data, bool memoryConnected)
    {
        var pluginConnected = memoryConnected &&
                              data.ets2_telemetry_plugin_revision != 0 &&
                              data.timeAbsolute != 0;

        return new GameTelemetryFrame(
            MemoryConnected: memoryConnected,
            PluginConnected: pluginConnected,
            Paused: data.paused != 0,
            GameName: "American Truck Simulator",
            GameTime: MinutesToClock(data.timeAbsolute),
            AtsX: data.coordinateX,
            AtsY: data.coordinateY,
            AtsZ: data.coordinateZ,
            Heading: RadiansToDegrees(data.rotationX),
            SpeedKph: data.speed * 3.6,
            SpeedLimitKph: data.navigationSpeedLimit > 0 ? data.navigationSpeedLimit * 3.6 : 0,
            FuelLiters: data.fuel,
            FuelCapacityLiters: data.fuelCapacity,
            FuelRateLitersPerHour: data.fuelRate,
            DamagePercent: MaxDamage(data) * 100,
            WearEnginePercent: data.wearEngine * 100,
            WearTransmissionPercent: data.wearTransmission * 100,
            WearCabinPercent: data.wearCabin * 100,
            WearChassisPercent: data.wearChassis * 100,
            WearWheelsPercent: data.wearWheels * 100,
            WearTrailerPercent: data.wearTrailer * 100,
            SourceCity: BytesToString(data.jobCitySource),
            SourceCompany: BytesToString(data.jobCompanySource),
            DestinationCity: BytesToString(data.jobCityDestination),
            DestinationCompany: BytesToString(data.jobCompanyDestination),
            TrailerName: BytesToString(data.trailerName),
            Income: data.jobIncome,
            RemainingNavigationMeters: Math.Max(0, (int)data.navigationDistance),
            RemainingNavigationSeconds: Math.Max(0, (int)data.navigationTime));
    }

    private static string BytesToString(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0)
        {
            return string.Empty;
        }

        var length = Array.FindIndex(bytes, value => value == 0);
        if (length < 0)
        {
            length = bytes.Length;
        }

        return Encoding.UTF8.GetString(bytes, 0, length);
    }

    private static string MinutesToClock(int minutes)
    {
        if (minutes <= 0)
        {
            return "--:--";
        }

        var time = TimeSpan.FromMinutes(minutes);
        return $"{(int)time.TotalHours % 24:00}:{time.Minutes:00}";
    }

    private static double RadiansToDegrees(double radians)
    {
        var degrees = radians * 180 / Math.PI;
        return degrees < 0 ? degrees + 360 : degrees;
    }

    private static double MaxDamage(Ets2TelemetryStructure data)
    {
        return Math.Max(
            Math.Max(data.wearEngine, data.wearTransmission),
            Math.Max(Math.Max(data.wearCabin, data.wearChassis), Math.Max(data.wearWheels, data.wearTrailer)));
    }
}
