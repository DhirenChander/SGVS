# Generated with Claude
cmake_minimum_required(VERSION 3.14)
project(processing_module LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Your code as a library so both the app and tests can link to it
add_library(processing_module
  audio_processor.cpp
  audio_capture.cpp
  encoding_manager.cpp
  video_processor.cpp
)
target_include_directories(processing_module PUBLIC src)

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
include(FetchContent)
FetchContent_Declare(
  googletest
  URL https://github.com/google/googletest/archive/refs/tags/v1.15.2.zip
)
set(gtest_force_shared_crt ON CACHE BOOL "" FORCE)
FetchContent_MakeAvailable(googletest)

enable_testing()

add_executable(processing_tests tests/audio_capture_test.cpp)
target_link_libraries(processing_tests PRIVATE processing_module GTest::gtest_main)

include(GoogleTest)
gtest_discover_tests(processing_tests DISCOVERY_MODE PRE_TEST)