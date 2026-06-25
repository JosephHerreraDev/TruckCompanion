using System.Threading.Channels;
using Microsoft.Extensions.Options;
using TruckCompanion.Api.Configuration;
using TruckCompanion.Api.Models;

namespace TruckCompanion.Api.Services;

public sealed class TelemetryHub(IOptions<TelemetryOptions> options)
{
    private readonly object syncRoot = new();
    private TelemetrySnapshot latest = MockTelemetryFactory.Create(DateTimeOffset.UtcNow, "mock");

    public TelemetrySnapshot Latest
    {
        get
        {
            lock (syncRoot)
            {
                return MarkStale(latest);
            }
        }
    }

    public event Action<TelemetrySnapshot>? SnapshotChanged;

    public void Publish(TelemetrySnapshot snapshot)
    {
        lock (syncRoot)
        {
            latest = snapshot;
        }

        SnapshotChanged?.Invoke(MarkStale(snapshot));
    }

    public IAsyncEnumerable<TelemetrySnapshot> Subscribe(CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<TelemetrySnapshot>();

        void Handler(TelemetrySnapshot snapshot)
        {
            channel.Writer.TryWrite(snapshot);
        }

        SnapshotChanged += Handler;
        channel.Writer.TryWrite(Latest);

        cancellationToken.Register(() =>
        {
            SnapshotChanged -= Handler;
            channel.Writer.TryComplete();
        });

        return channel.Reader.ReadAllAsync(cancellationToken);
    }

    private TelemetrySnapshot MarkStale(TelemetrySnapshot snapshot)
    {
        var staleAfter = TimeSpan.FromSeconds(Math.Max(1, options.Value.StaleAfterSeconds));
        var stale = DateTimeOffset.UtcNow - snapshot.Connection.LastUpdateUtc > staleAfter;
        return snapshot with
        {
            Connection = snapshot.Connection with { Stale = stale || snapshot.Connection.Stale }
        };
    }
}
