# Generated with Claude
cmake_minimum_required(VERSION 3.14)
project(processing_module LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
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

# Your code as a library so both the app and tests can link to it
add_library(processing_module
  audio_processor.cpp
  audio_capture.cpp
  encoding_manager.cpp
  video_processor.cpp
)
target_include_directories(processing_module PUBLIC src)
target_link_libraries(processing_module PUBLIC Opus::opus)

if(MSVC)  # true for both cl.exe and clang-cl
  # /EHsc: standard C++ exception handling
  target_compile_options(processing_module PUBLIC /EHsc)

  # Windows system libraries (the part after /link)
  target_link_libraries(processing_module PUBLIC
    ole32.lib
    mmdevapi.lib
    avrt.lib
    runtimeobject.lib
  )
endif()

# --- GoogleTest ---
FetchContent_Declare(
  googletest
  URL https://github.com/google/googletest/archive/refs/tags/v1.15.2.zip
)
set(gtest_force_shared_crt ON CACHE BOOL "" FORCE)
FetchContent_MakeAvailable(googletest)

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