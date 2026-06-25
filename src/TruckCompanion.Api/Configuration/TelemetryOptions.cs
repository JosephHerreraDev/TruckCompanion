namespace TruckCompanion.Api.Configuration;

public sealed class TelemetryOptions
{
    public string Mode { get; set; } = "plugin";
    public int PollIntervalMs { get; set; } = 1000;
    public int StaleAfterSeconds { get; set; } = 5;

    public bool UseMock => string.Equals(Mode, "mock", StringComparison.OrdinalIgnoreCase);
    public string EffectiveMode => UseMock ? "mock" : "plugin";
}
