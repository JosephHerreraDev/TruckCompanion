using TruckCompanion.Api.Models;
using TruckCompanion.Api.Services;

namespace TruckCompanion.Api.Data;

public static class AtsMapData
{
    public static readonly IReadOnlyList<CalibrationAnchor> CalibrationAnchors =
    [
        new("Los Angeles", "CA", -80300, 73500, 34.0522, -118.2437),
        new("San Diego", "CA", -79400, 85000, 32.7157, -117.1611),
        new("San Francisco", "CA", -90500, 54500, 37.7749, -122.4194),
        new("Las Vegas", "NV", -64000, 67000, 36.1699, -115.1398),
        new("Phoenix", "AZ", -57500, 84000, 33.4484, -112.0740),
        new("Reno", "NV", -81500, 41000, 39.5296, -119.8138),
        new("Portland", "OR", -84500, 18000, 45.5152, -122.6784),
        new("Seattle", "WA", -80500, 5000, 47.6062, -122.3321),
        new("Salt Lake City", "UT", -42000, 47000, 40.7608, -111.8910),
        new("Denver", "CO", -16500, 55500, 39.7392, -104.9903),
        new("Dallas", "TX", 20500, 90000, 32.7767, -96.7970),
        new("Boise", "ID", -58000, 24000, 43.6150, -116.2023)
    ];

    public static IReadOnlyList<Poi> GetPois(CoordinateProjector projector)
    {
        return
        [
            Build("city-los-angeles", "city", "Los Angeles", "Los Angeles", "CA", -80300, 73500, projector),
            Build("city-las-vegas", "city", "Las Vegas", "Las Vegas", "NV", -64000, 67000, projector),
            Build("city-phoenix", "city", "Phoenix", "Phoenix", "AZ", -57500, 84000, projector),
            Build("city-san-francisco", "city", "San Francisco", "San Francisco", "CA", -90500, 54500, projector),
            Build("fuel-barstow", "fuel", "Barstow Fuel Stop", "Barstow", "CA", -70000, 73500, projector),
            Build("service-reno", "service", "Reno Service", "Reno", "NV", -81500, 41000, projector),
            Build("rest-flagstaff", "rest", "Flagstaff Rest Area", "Flagstaff", "AZ", -52000, 73500, projector),
            Build("weigh-bakersfield", "weigh", "Bakersfield Weigh Station", "Bakersfield", "CA", -79000, 65000, projector),
            Build("garage-vegas", "garage", "Las Vegas Garage", "Las Vegas", "NV", -63800, 66800, projector),
            Build("dealer-phoenix", "dealer", "Phoenix Truck Dealer", "Phoenix", "AZ", -57200, 84200, projector)
        ];
    }

    private static Poi Build(
        string id,
        string type,
        string name,
        string city,
        string state,
        double atsX,
        double atsZ,
        CoordinateProjector projector)
    {
        return new Poi(id, type, name, city, state, projector.Project(atsX, atsZ));
    }
}
