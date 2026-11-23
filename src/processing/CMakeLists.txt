# Generated with Claude

# Raised from 3.14: the Visual Studio generator needs 3.20+ to map C++20 to /std:c++20
cmake_minimum_required(VERSION 3.20)

# Must come BEFORE project(). Pins the Windows SDK so C++/WinRT uses <coroutine>.
# Change the version to whichever SDK folder you have installed, or delete this
# line if you pass -DCMAKE_SYSTEM_VERSION=... on the command line instead.
set(CMAKE_SYSTEM_VERSION 10.0.22621.0)

# Only one project() per CMakeLists
project(processing_module LANGUAGES CXX)

# C++/WinRT needs C++20 (coroutines). Applies to the whole project so the
# audio code and screen capture code compile with the same standard.
set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

include(FetchContent)

# --- Opus (audio encoder) ---
# Official release tarball, checksum from https://downloads.xiph.org/releases/opus/SHA256SUMS.txt
FetchContent_Declare(
  opus
  URL https://downloads.xiph.org/releases/opus/opus-1.5.2.tar.gz
  URL_HASH SHA256=65c1d2f78b9f2fb20082c38cbe47c951ad5839345876e46941612ee87f9a7ce1
)
set(OPUS_INSTALL_PKG_CONFIG_MODULE OFF CACHE BOOL "" FORCE)
set(OPUS_INSTALL_CMAKE_CONFIG_MODULE OFF CACHE BOOL "" FORCE)
FetchContent_MakeAvailable(opus)

# Local prebuilt shared_mem_client library
set(SHARED_MEM_CLIENT_LIB
  "${CMAKE_CURRENT_SOURCE_DIR}/../shared_mem_lib/cpp/lib/shared_mem_client.lib"
)

if(NOT EXISTS "${SHARED_MEM_CLIENT_LIB}")
  message(FATAL_ERROR "shared_mem_client.lib not found at: ${SHARED_MEM_CLIENT_LIB}")
endif()

add_library(shared_mem_client_local STATIC IMPORTED)
set_target_properties(shared_mem_client_local PROPERTIES
  IMPORTED_LOCATION "${SHARED_MEM_CLIENT_LIB}"
)

# Your code as a library so both the app and tests can link to it
add_library(processing_module
  audio_processor.cpp
  audio_capture.cpp
  encoding_manager.cpp
  video_processor.cpp
  screen_capture.cpp
)
target_include_directories(processing_module PUBLIC src)
target_link_libraries(processing_module PUBLIC Opus::opus)

if(MSVC)  # true for both cl.exe and clang-cl
  # /EHsc: standard C++ exception handling
  # /permissive-: strict conformance, recommended for C++/WinRT
  target_compile_options(processing_module PUBLIC /EHsc /permissive-)

  # NOMINMAX stops windows.h from defining min/max macros that clash with std::min/max
  # (WIN32_LEAN_AND_MEAN is deliberately NOT set: it can hide headers audio code needs)
  target_compile_definitions(processing_module PUBLIC NOMINMAX)

  # Windows system libraries (the part after /link)
  target_link_libraries(processing_module PUBLIC
    ole32.lib
    mmdevapi.lib
    avrt.lib
    runtimeobject.lib
    bcrypt
    # Added for screen capture:
    windowsapp.lib   # C++/WinRT / Windows.Graphics.Capture
    d3d11.lib        # Direct3D 11
    dxgi.lib         # DXGI
  )
endif()

# --- GoogleTest ---
FetchContent_Declare(
  googletest
  URL https://github.com/google/googletest/archive/refs/tags/v1.15.2.zip
)
set(gtest_force_shared_crt ON CACHE BOOL "" FORCE)
FetchContent_MakeAvailable(googletest)

# Main executable
add_executable(processing_main main.cpp)
target_link_libraries(processing_main PRIVATE processing_module shared_mem_client_local)

enable_testing()

add_executable(processing_tests
  tests/audio_capture_test.cpp
  tests/audio_processor_test.cpp
)
target_link_libraries(processing_tests PRIVATE processing_module GTest::gtest_main)

include(GoogleTest)
gtest_discover_tests(processing_tests DISCOVERY_MODE PRE_TEST)

# Functional Tests
add_executable(desktop_audio_recorder tests/functional/desktop_audio_recorder.cpp)
target_link_libraries(desktop_audio_recorder PRIVATE processing_module)

add_executable(mic_audio_recorder tests/functional/mic_audio_recorder.cpp)
target_link_libraries(mic_audio_recorder PRIVATE processing_module)

add_executable(desktop_audio_encoder tests/functional/desktop_audio_encoder.cpp)
target_link_libraries(desktop_audio_encoder PRIVATE processing_module)


add_executable(screen_capture_demo tests/functional/screen_capture_demo.cpp)
target_link_libraries(screen_capture_demo PRIVATE processing_module)