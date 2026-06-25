param(
    [Parameter(Mandatory = $true)]
    [string]$ScsSdkDir
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path (Join-Path $ScsSdkDir "include"))) {
    throw "SCS SDK include folder was not found under $ScsSdkDir"
}

$vcvars = "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars64.bat"
if (-not (Test-Path $vcvars)) {
    throw "Visual Studio vcvars64.bat was not found: $vcvars"
}

$buildDir = "build\telemetry-plugin"
New-Item -ItemType Directory -Force $buildDir | Out-Null

cmd /c "`"$vcvars`" && cmake -S src\TruckCompanion.TelemetryPlugin -B $buildDir -A x64 -DSCS_SDK_DIR=`"$ScsSdkDir`" && cmake --build $buildDir --config Release"
