# Created with the help of Claude
cmake_minimum_required(VERSION 3.21)
project(shared_mem_client VERSION 1.0.0 LANGUAGES C)

set(CMAKE_C_STANDARD 17)
set(CMAKE_C_STANDARD_REQUIRED ON)
set(CMAKE_C_EXTENSIONS OFF)
set(CMAKE_EXPORT_COMPILE_COMMANDS ON)

option(SHARED_MEM_CLIENT_BUILD_DLL "Build shared_mem_client as a shared library (DLL)" OFF)

find_package(OpenSSL REQUIRED)

if(SHARED_MEM_CLIENT_BUILD_DLL)
  add_library(shared_mem_client SHARED)
else()
  add_library(shared_mem_client STATIC)
endif()

target_sources(shared_mem_client PRIVATE
  src/ringbuff.c
  src/framebuff.c
  src/encoding.c
  src/frame.c
)
add_library(shared_mem_client::shared_mem_client ALIAS shared_mem_client)
target_link_libraries(shared_mem_client PUBLIC OpenSSL::SSL)

target_include_directories(shared_mem_client
  PUBLIC
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
    $<INSTALL_INTERFACE:include>
)

if(SHARED_MEM_CLIENT_BUILD_DLL)
  set_target_properties(shared_mem_client PROPERTIES
    WINDOWS_EXPORT_ALL_SYMBOLS ON
  )
endif()

if(MSVC)
  target_compile_options(shared_mem_client PUBLIC /W4 /std:c17 /experimental:c11atomics /we4133)
else()
  target_compile_options(shared_mem_client PUBLIC -Wall -Wextra -Wpedantic -Werror=incompatible-pointer-types)
endif()

if(CMAKE_SOURCE_DIR STREQUAL CMAKE_CURRENT_SOURCE_DIR)
  include(CTest)
  if(BUILD_TESTING)
    add_subdirectory(tests)
  endif()
endif()

install(TARGETS shared_mem_client
  RUNTIME DESTINATION bin
  LIBRARY DESTINATION lib
  ARCHIVE DESTINATION lib
)
install(DIRECTORY include/ DESTINATION include)