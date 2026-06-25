cmake_minimum_required(VERSION 3.24)
project(TruckCompanionTelemetryPlugin LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

if(NOT DEFINED SCS_SDK_DIR)
  message(FATAL_ERROR "SCS_SDK_DIR is required. Pass -DSCS_SDK_DIR=C:/path/to/scs_sdk")
endif()

add_library(truckcompanion-telemetry SHARED
  src/truckcompanion_telemetry.cpp
)

target_include_directories(truckcompanion-telemetry PRIVATE
  include
  "${SCS_SDK_DIR}/include"
)

target_compile_definitions(truckcompanion-telemetry PRIVATE
  WIN32_LEAN_AND_MEAN
  NOMINMAX
)

set_target_properties(truckcompanion-telemetry PROPERTIES
  OUTPUT_NAME "truckcompanion-telemetry"
  RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin"
)
