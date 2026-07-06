using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using TruckCompanion.Api.Configuration;
using TruckCompanion.Api.Data;
using TruckCompanion.Api.Models;
using TruckCompanion.Api.Services;
using TruckCompanion.Api.Mapping;
using TruckCompanion.Api.Telemetry.Funbit;
using TruckCompanion.Api.Map;
using TruckCompanion.Api.Telemetry.TruckCompanion;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<TelemetryOptions>(builder.Configuration.GetSection("Telemetry"));
builder.Services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyHeader()
            .AllowAnyMethod()
            .AllowAnyOrigin();
    });
});

builder.Services.AddSingleton<CoordinateProjector>();
builder.Services.AddSingleton<GameTelemetryReader>();
builder.Services.AddSingleton<TruckCompanionTelemetryReader>();
builder.Services.AddSingleton<GameTelemetrySnapshotMapper>();
builder.Services.AddSingleton<TelemetryHub>();
builder.Services.AddSingleton<AtsMapDataService>();
builder.Services.AddSingleton<TileMapService>();
builder.Services.AddHostedService<TelemetryWorker>();

var app = builder.Build();

app.UseCors();

app.MapGet("/api/status", (TelemetryHub hub, IOptions<TelemetryOptions> options) =>
{
    var snapshot = hub.Latest;
    return Results.Ok(new ApiStatus(
        options.Value.EffectiveMode,
        snapshot.Game.Connected,
        snapshot.Connection.Stale,
        snapshot.Connection.Error,
        snapshot.Connection.LastUpdateUtc));
});

app.MapGet("/api/snapshot", (TelemetryHub hub) => Results.Ok(hub.Latest));

app.MapGet("/api/telemetry/raw", (IOptions<TelemetryOptions> options, TruckCompanionTelemetryReader truckCompanionReader, GameTelemetryReader fallbackReader, TelemetryHub hub) =>
{
    if (options.Value.UseMock)
    {
        return Results.Ok(new { mode = options.Value.EffectiveMode, snapshot = hub.Latest });
    }

    var frame = truckCompanionReader.IsConnected ? truckCompanionReader.Read() : fallbackReader.Read();
    return Results.Ok(new { mode = options.Value.EffectiveMode, frame, snapshot = hub.Latest });
});

app.MapGet("/api/pois", (CoordinateProjector projector) => Results.Ok(AtsMapData.GetPois(projector)));

app.MapGet("/api/map/calibration", (CoordinateProjector projector) => Results.Ok(projector.GetMetadata()));

app.MapGet("/api/map/viewport", (
    double minX,
    double minZ,
    double maxX,
    double maxZ,
    int? detail,
    AtsMapDataService map) => Results.Ok(map.GetViewport(minX, minZ, maxX, maxZ, detail ?? 2)));

app.MapGet("/api/map/pois", (string? kind, AtsMapDataService map) => Results.Ok(map.GetPois(kind)));

app.MapGet("/api/map/status", (TileMapService tiles) => Results.Ok(tiles.GetStatus()));

app.MapGet("/api/map/tile-manifest", (TileMapService tiles) =>
{
    var manifest = tiles.GetManifest();
    return manifest is null
        ? Results.NotFound(new { error = "Generated ATS map data was not found. Run tools\\generate-ats-tiles.ps1." })
        : Results.Ok(manifest);
});

app.MapGet("/map/ats.pmtiles", (TileMapService tiles) =>
{
    var path = tiles.GetMapFilePath("ats.pmtiles");
    return path is null
        ? Results.NotFound()
        : Results.File(path, "application/octet-stream", enableRangeProcessing: true);
});

app.MapGet("/map/ats-search.geojson", (TileMapService tiles) =>
{
    var path = tiles.GetMapFilePath("ats-search.geojson");
    return path is null
        ? Results.NotFound()
        : Results.File(path, "application/geo+json", enableRangeProcessing: true);
});

app.MapGet("/map/spritesheet.json", (TileMapService tiles) =>
{
    var path = tiles.GetMapFilePath("spritesheet.json");
    return path is null
        ? Results.NotFound()
        : Results.File(path, "application/json", enableRangeProcessing: true);
});

app.MapGet("/map/spritesheet.png", (TileMapService tiles) =>
{
    var path = tiles.GetMapFilePath("spritesheet.png");
    return path is null
        ? Results.NotFound()
        : Results.File(path, "image/png", enableRangeProcessing: true);
});

app.MapGet("/api/map/route", (
    double fromX,
    double fromZ,
    string? toCompany,
    string? toCity,
    AtsMapDataService map) => Results.Ok(map.GetRoute(fromX, fromZ, toCompany, toCity)));

app.MapGet("/api/route/stops", (AtsMapDataService map) => Results.Ok(map.GetStops()));

app.MapPost("/api/route/stops", (AtsRouteStopRequest request, AtsMapDataService map) =>
{
    var stop = map.AddStop(request.PointId);
    return stop is null ? Results.NotFound(new { error = "Map point was not found." }) : Results.Ok(stop);
});

app.MapDelete("/api/route/stops/{id}", (string id, AtsMapDataService map) =>
    map.RemoveStop(id) ? Results.NoContent() : Results.NotFound(new { error = "Route stop was not found." }));

app.MapPost("/api/route/reorder", (AtsRouteReorderRequest request, AtsMapDataService map) =>
    Results.Ok(map.ReorderStops(request.StopIds)));

app.MapGet("/api/setup/plugin", (IWebHostEnvironment environment) =>
{
    var root = environment.ContentRootPath;
    return Results.Ok(new
    {
        sourceX64 = Path.Combine(root, "ThirdParty", "Funbit", "Ets2Plugins", "win_x64", "plugins", "ets2-telemetry-server.dll"),
        sourceX86 = Path.Combine(root, "ThirdParty", "Funbit", "Ets2Plugins", "win_x86", "plugins", "ets2-telemetry-server.dll"),
        atsX64Destination = @"<ATS install>\bin\win_x64\plugins\ets2-telemetry-server.dll",
        atsX86Destination = @"<ATS install>\bin\win_x86\plugins\ets2-telemetry-server.dll"
    });
});

app.MapGet("/stream/telemetry", async (HttpContext context, TelemetryHub hub) =>
{
    context.Response.Headers.CacheControl = "no-cache";
    context.Response.Headers.Connection = "keep-alive";
    context.Response.ContentType = "text/event-stream";

    await foreach (var snapshot in hub.Subscribe(context.RequestAborted))
    {
        var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await context.Response.WriteAsync($"event: telemetry\ndata: {json}\n\n", context.RequestAborted);
        await context.Response.Body.FlushAsync(context.RequestAborted);
    }
});

app.Run();
