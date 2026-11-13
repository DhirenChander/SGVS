# Created with the help of Claude
cmake_minimum_required(VERSION 3.21)
project(threewayintegration VERSION 1.0.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

find_package(OpenSSL REQUIRED)

add_executable(threewayintegration main.cpp)
add_library(shared_mem_lib STATIC IMPORTED)

set_target_properties(shared_mem_lib PROPERTIES
    IMPORTED_LOCATION "${CMAKE_CURRENT_SOURCE_DIR}/lib/shared_mem_client.lib"
    INTERFACE_INCLUDE_DIRECTORIES "${CMAKE_CURRENT_SOURCE_DIR}/../../../../core/include"
    INTERFACE_LINK_LIBRARIES "OpenSSL::Crypto"
)

target_include_directories(threewayintegration
  PUBLIC
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
    $<INSTALL_INTERFACE:include>
)

target_link_libraries(threewayintegration PRIVATE shared_mem_lib)

if(MSVC)
  target_compile_options(threewayintegration PUBLIC /W4 /std:c17 /experimental:c11atomics)
else()
  target_compile_options(threewayintegration PUBLIC -Wall -Wextra -Wpedantic)
endif()

install(TARGETS threewayintegration)