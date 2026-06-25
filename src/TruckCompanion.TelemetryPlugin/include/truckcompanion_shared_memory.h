#pragma once

#include <cstdint>

constexpr const wchar_t* TruckCompanionMapName = L"Local\\TruckCompanionTelemetry";
constexpr uint32_t TruckCompanionTelemetryVersion = 1;

#pragma pack(push, 8)
struct TruckCompanionTelemetryFrame
{
    uint32_t version;
    uint32_t sequence;
    uint32_t connected;
    uint32_t paused;

    double game_time_seconds;
    double truck_x;
    double truck_y;
    double truck_z;
    double heading_radians;

    double speed_mps;
    double speed_limit_mps;
    double fuel_liters;
    double fuel_capacity_liters;
    double fuel_rate_liters_per_hour;

    double wear_engine;
    double wear_transmission;
    double wear_cabin;
    double wear_chassis;
    double wear_wheels;
    double wear_trailer;

    double navigation_distance_meters;
    double navigation_time_seconds;

    wchar_t source_city[64];
    wchar_t source_company[64];
    wchar_t destination_city[64];
    wchar_t destination_company[64];
    wchar_t cargo[64];
};
#pragma pack(pop)
