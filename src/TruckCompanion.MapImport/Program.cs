using System.Text.Json;
using TruckCompanion.Api.Map;

var atsPath = GetOption(args, "--ats-path") ??
              @"C:\Program Files (x86)\Steam\steamapps\common\American Truck Simulator";
var output = GetOption(args, "--output") ??
             Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "TruckCompanion.Api", "Data", "ats-map.db"));
var cachePath = GetOption(args, "--cache") ??
                Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), ".truckcompanion-cache", "ats-map"));

if (!Directory.Exists(atsPath))
{
    Console.Error.WriteLine($"ATS install path was not found: {atsPath}");
    Environment.ExitCode = 2;
    return;
}

var baseMap = Path.Combine(atsPath, "base_map.scs");
if (!File.Exists(baseMap))
{
    Console.Error.WriteLine($"ATS base_map.scs was not found: {baseMap}");
    Environment.ExitCode = 3;
    return;
}

Directory.CreateDirectory(Path.GetDirectoryName(output)!);
Directory.CreateDirectory(cachePath);

var archives = Directory.EnumerateFiles(atsPath, "*.scs", SearchOption.TopDirectoryOnly)
    .Select(Path.GetFileName)
    .Where(name => name is not null)
    .Order(StringComparer.OrdinalIgnoreCase)
    .ToArray();

var database = SeedAtsMap.Create($"seeded-from:{atsPath}");
var json = JsonSerializer.Serialize(database, new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    WriteIndented = true
});

File.WriteAllText(output, json);
Console.WriteLine($"Wrote {output}");
Console.WriteLine($"Prepared local cache: {cachePath}");
Console.WriteLine($"Detected {archives.Length} ATS archive(s): {string.Join(", ", archives.Take(10))}{(archives.Length > 10 ? ", ..." : string.Empty)}");
Console.WriteLine("Warning: SCS archive sector extraction is not implemented yet; this database is seeded and isRealMapData=false.");
Console.WriteLine("The web app will show a seed-map warning until a real ATS map database is generated.");

static string? GetOption(string[] args, string name)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
        {
            return args[i + 1];
        }
    }

    return null;
}
