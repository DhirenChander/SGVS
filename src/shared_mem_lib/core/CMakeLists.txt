# Created with the help of Claude
cmake_minimum_required(VERSION 3.15)
project(shared_mem_client VERSION 1.0.0 LANGUAGES C)

set(CMAKE_C_STANDARD 23)
set(CMAKE_C_STANDARD_REQUIRED ON)
set(CMAKE_C_EXTENSIONS OFF)
set(CMAKE_EXPORT_COMPILE_COMMANDS ON)

add_library(shared_mem_client ringbuff.c framebuff.c encoding.c)
add_library(shared_mem_client::shared_mem_client ALIAS shared_mem_client)

target_include_directories(shared_mem_client
  PUBLIC
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}>
    $<INSTALL_INTERFACE:include>
)

if(MSVC)
  target_compile_options(shared_mem_client PRIVATE /W4)
else()
  target_compile_options(shared_mem_client PRIVATE -Wall -Wextra -Wpedantic)
endif()

if(CMAKE_SOURCE_DIR STREQUAL CMAKE_CURRENT_SOURCE_DIR)
  include(CTest)
  if(BUILD_TESTING)
    add_subdirectory(tests)
  endif()
endif()

install(TARGETS shared_mem_client)
install(DIRECTORY include/ DESTINATION include)