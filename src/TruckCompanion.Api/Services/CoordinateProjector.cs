using TruckCompanion.Api.Data;
using TruckCompanion.Api.Models;

namespace TruckCompanion.Api.Services;

public sealed class CoordinateProjector
{
    private static readonly double CenterAtsX = AtsMapData.CalibrationAnchors.Average(anchor => anchor.AtsX);
    private static readonly double CenterAtsZ = AtsMapData.CalibrationAnchors.Average(anchor => anchor.AtsZ);
    private static readonly double CenterLat = AtsMapData.CalibrationAnchors.Average(anchor => anchor.Latitude);
    private static readonly double CenterLon = AtsMapData.CalibrationAnchors.Average(anchor => anchor.Longitude);

    private static readonly double LatPerAtsZ = CalculateSlope(
        AtsMapData.CalibrationAnchors.Select(anchor => anchor.AtsZ).ToArray(),
        AtsMapData.CalibrationAnchors.Select(anchor => anchor.Latitude).ToArray());

    private static readonly double LonPerAtsX = CalculateSlope(
        AtsMapData.CalibrationAnchors.Select(anchor => anchor.AtsX).ToArray(),
        AtsMapData.CalibrationAnchors.Select(anchor => anchor.Longitude).ToArray());

    public MapPosition Project(double atsX, double atsZ)
    {
        var latitude = CenterLat + ((atsZ - CenterAtsZ) * LatPerAtsZ);
        var longitude = CenterLon + ((atsX - CenterAtsX) * LonPerAtsX);

        return new MapPosition(atsX, atsZ, latitude, longitude);
    }

    public CalibrationMetadata GetMetadata()
    {
        return new CalibrationMetadata(
            "linear-city-anchor",
            "approximate v1 transform calibrated from a small ATS city anchor set",
            AtsMapData.CalibrationAnchors);
    }

    private static double CalculateSlope(IReadOnlyList<double> source, IReadOnlyList<double> target)
    {
        var sourceMean = source.Average();
        var targetMean = target.Average();
        var numerator = 0d;
        var denominator = 0d;

        for (var i = 0; i < source.Count; i++)
        {
            numerator += (source[i] - sourceMean) * (target[i] - targetMean);
            denominator += Math.Pow(source[i] - sourceMean, 2);
        }

        return denominator == 0 ? 0 : numerator / denominator;
    }
}
