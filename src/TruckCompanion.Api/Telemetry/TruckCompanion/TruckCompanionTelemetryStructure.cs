using System.Runtime.InteropServices;

namespace TruckCompanion.Api.Telemetry.TruckCompanion;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct TruckCompanionTelemetryStructure
{
    public uint Version;
    public uint Sequence;
    public uint Connected;
    public uint Paused;

    public double GameTimeSeconds;
    public double TruckX;
    public double TruckY;
    public double TruckZ;
    public double HeadingRadians;

    public double SpeedMps;
    public double SpeedLimitMps;
    public double FuelLiters;
    public double FuelCapacityLiters;
    public double FuelRateLitersPerHour;

    public double WearEngine;
    public double WearTransmission;
    public double WearCabin;
    public double WearChassis;
    public double WearWheels;
    public double WearTrailer;

    public double NavigationDistanceMeters;
    public double NavigationTimeSeconds;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
    public string SourceCity;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
    public string SourceCompany;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
    public string DestinationCity;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
    public string DestinationCompany;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
    public string Cargo;
}
