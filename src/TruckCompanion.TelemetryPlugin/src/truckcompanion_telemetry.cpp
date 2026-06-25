#include "truckcompanion_shared_memory.h"

#include <Windows.h>
#include <algorithm>
#include <cwchar>

// This file is intentionally a thin SCS SDK integration point. The concrete
// channel registration code requires the official SCS telemetry SDK headers,
// supplied at build time with -DSCS_SDK_DIR.
#include <scssdk/scssdk.h>
#include <scssdk/scssdk_telemetry.h>

namespace
{
    HANDLE map_handle = nullptr;
    TruckCompanionTelemetryFrame* frame = nullptr;

    void write_string(wchar_t* target, const wchar_t* value)
    {
        if (!target)
        {
            return;
        }

        const auto source = value ? value : L"";
        wcsncpy_s(target, 64, source, _TRUNCATE);
    }

    bool open_shared_memory()
    {
        map_handle = CreateFileMappingW(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE, 0, sizeof(TruckCompanionTelemetryFrame), TruckCompanionMapName);
        if (!map_handle)
        {
            return false;
        }

        frame = static_cast<TruckCompanionTelemetryFrame*>(MapViewOfFile(map_handle, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(TruckCompanionTelemetryFrame)));
        if (!frame)
        {
            CloseHandle(map_handle);
            map_handle = nullptr;
            return false;
        }

        ZeroMemory(frame, sizeof(TruckCompanionTelemetryFrame));
        frame->version = TruckCompanionTelemetryVersion;
        return true;
    }

    void close_shared_memory()
    {
        if (frame)
        {
            frame->connected = 0;
            UnmapViewOfFile(frame);
            frame = nullptr;
        }

        if (map_handle)
        {
            CloseHandle(map_handle);
            map_handle = nullptr;
        }
    }
}

extern "C" SCSAPI_RESULT scs_telemetry_init(const scs_u32_t version, const scs_telemetry_init_params_t* const params)
{
    if (version != SCS_TELEMETRY_VERSION_1_00 || params == nullptr)
    {
        return SCS_RESULT_unsupported;
    }

    if (!open_shared_memory())
    {
        return SCS_RESULT_generic_error;
    }

    frame->connected = 1;
    frame->sequence++;
    write_string(frame->cargo, L"TruckCompanion");

    // TODO: register SCS channels here once the exact SDK version is vendored.
    // The shared memory schema and backend reader are already versioned for the
    // complete channel set listed in truckcompanion_shared_memory.h.
    return SCS_RESULT_ok;
}

extern "C" SCSAPI_VOID scs_telemetry_shutdown()
{
    close_shared_memory();
}
