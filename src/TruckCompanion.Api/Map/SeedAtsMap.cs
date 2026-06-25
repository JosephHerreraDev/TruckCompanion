namespace TruckCompanion.Api.Map;

public static class SeedAtsMap
{
    public static AtsMapDatabase Create(string source)
    {
        var roads = new List<AtsMapRoad>
        {
            Road("i5", "interstate", "I-5", (-90500, 54500), (-84000, 62000), (-80300, 73500), (-79400, 85000)),
            Road("i15", "interstate", "I-15", (-79400, 85000), (-70000, 73500), (-64000, 67000), (-42000, 47000)),
            Road("i10", "interstate", "I-10", (-80300, 73500), (-70000, 76500), (-57500, 84000), (-33000, 82500), (20500, 90000)),
            Road("us95", "highway", "US-95", (-81500, 41000), (-76000, 52000), (-64000, 67000), (-57500, 84000)),
            Road("i80", "interstate", "I-80", (-90500, 54500), (-81500, 41000), (-58000, 24000), (-42000, 47000), (-16500, 55500))
            ,
            Road("i90-mt", "interstate", "I-90", (-50500, -12500), (-45500, -7000), (-38000, -1000), (-31500, 3200), (-25000, 6200)),
            Road("us212-mt", "highway", "US-212", (-41000, -5200), (-38000, -1000), (-34200, 2400)),
            Road("local-laurel", "surface", "Laurel", (-39700, -2800), (-38000, -1000), (-36000, -1800), (-34400, -3600))
        };

        var areas = new List<AtsMapArea>
        {
            Area("yard-la", "company", "Voltison Motors", -80550, 73100, 1600, 1100),
            Area("yard-phx", "company", "Rail Export", -57800, 83600, 1500, 1050),
            Area("yard-vegas", "service", "Las Vegas Service", -64200, 66600, 1200, 900),
            Area("yard-reno", "garage", "Reno Garage", -81700, 40700, 1200, 900),
            Area("yard-laurel-walmart", "company", "Walmart Megastore", -37900, -1400, 1300, 900),
            Area("service-laurel", "service", "Laurel Service", -36600, -2100, 900, 700)
        };

        var points = new List<AtsMapPoint>
        {
            Point("city-los-angeles", "city", "Los Angeles", "Los Angeles", null, -80300, 73500),
            Point("city-san-diego", "city", "San Diego", "San Diego", null, -79400, 85000),
            Point("city-san-francisco", "city", "San Francisco", "San Francisco", null, -90500, 54500),
            Point("city-las-vegas", "city", "Las Vegas", "Las Vegas", null, -64000, 67000),
            Point("city-phoenix", "city", "Phoenix", "Phoenix", null, -57500, 84000),
            Point("company-voltison-la", "company", "Voltison Motors", "Los Angeles", "Voltison Motors", -80300, 73500),
            Point("company-rail-export-phx", "company", "Rail Export", "Phoenix", "Rail Export", -57500, 84000),
            Point("service-vegas", "service", "Service", "Las Vegas", null, -63800, 66800),
            Point("fuel-barstow", "fuel", "Fuel", "Barstow", null, -70000, 73500),
            Point("garage-reno", "garage", "Garage", "Reno", null, -81500, 41000),
            Point("city-laurel", "city", "Laurel", "Laurel", null, -38000, -1000),
            Point("company-walmart-laurel", "company", "Walmart Megastore", "Laurel", "Walmart Megastore", -37900, -1400),
            Point("service-laurel", "service", "Service", "Laurel", null, -36600, -2100),
            Point("fuel-laurel", "fuel", "Fuel", "Laurel", null, -39200, -2600)
        };

        return new AtsMapDatabase(source, DateTimeOffset.UtcNow, false, roads, areas, points);
    }

    private static AtsMapRoad Road(string id, string kind, string label, params (double X, double Z)[] points)
    {
        return new AtsMapRoad(id, kind, label, points.Select(point => new AtsMapCoordinate(point.X, point.Z)).ToArray());
    }

    private static AtsMapArea Area(string id, string kind, string label, double x, double z, double width, double height)
    {
        return new AtsMapArea(id, kind, label,
        [
            new AtsMapCoordinate(x - width / 2, z - height / 2),
            new AtsMapCoordinate(x + width / 2, z - height / 2),
            new AtsMapCoordinate(x + width / 2, z + height / 2),
            new AtsMapCoordinate(x - width / 2, z + height / 2),
            new AtsMapCoordinate(x - width / 2, z - height / 2)
        ]);
    }

    private static AtsMapPoint Point(string id, string kind, string label, string? city, string? company, double x, double z)
    {
        return new AtsMapPoint(id, kind, label, city, company, new AtsMapCoordinate(x, z));
    }
}
