# TruckCompanion Telemetry Plugin

This project is the native ATS telemetry plugin target.

It requires the official SCS telemetry SDK headers at build time:

```powershell
.\tools\build-telemetry-plugin.ps1 -ScsSdkDir "C:\Path\To\scs_sdk"
```

The plugin publishes `TruckCompanionTelemetryFrame` to:

```text
Local\TruckCompanionTelemetry
```

The current source includes the shared-memory contract and exported plugin entrypoints. Channel registration must be completed against the exact SCS SDK header version installed locally.
