using Microsoft.Extensions.Options;
using TruckCompanion.Api.Configuration;
using TruckCompanion.Api.Mapping;
using TruckCompanion.Api.Models;
using TruckCompanion.Api.Telemetry.Funbit;
using TruckCompanion.Api.Telemetry.TruckCompanion;

namespace TruckCompanion.Api.Services;

public sealed class TelemetryWorker(
    IOptions<TelemetryOptions> options,
    TelemetryHub hub,
    TruckCompanionTelemetryReader truckCompanionReader,
    GameTelemetryReader fallbackReader,
    GameTelemetrySnapshotMapper mapper,
    ILogger<TelemetryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;

            try
            {
                var snapshot = options.Value.UseMock
                    ? MockTelemetryFactory.Create(now, "mock")
                    : mapper.Map(ReadPluginFrame(), now);

                hub.Publish(snapshot);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Telemetry poll failed");
                hub.Publish(WithError(hub.Latest, now, FormatError(ex)));
            }

            await Task.Delay(Math.Max(250, options.Value.PollIntervalMs), stoppingToken);
        }
    }

    private GameTelemetryFrame ReadPluginFrame()
    {
        return truckCompanionReader.IsConnected ? truckCompanionReader.Read() : fallbackReader.Read();
    }

    private static TelemetrySnapshot WithError(TelemetrySnapshot snapshot, DateTimeOffset now, string error)
    {
        return snapshot with
        {
            Game = snapshot.Game with { Connected = false },
            Connection = new ConnectionState(snapshot.Connection.Source, now, true, error)
        };
    }

    private static string FormatError(Exception exception) => exception.Message;
}
