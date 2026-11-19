@echo off
(
    echo Starting...
    rmdir /s /q cpp_build
    rmdir /s /q go_build 
    rmdir /s /q csharp_build
    cmake -S . -B cpp_build
    cmake -S . -B go_build -G Ninja -DCMAKE_C_COMPILER=gcc -DCMAKE_CXX_COMPILER=g++
    cmake -S . -B csharp_build -DSHARED_MEM_CLIENT_BUILD_DLL=ON
    cmake --build cpp_build
    cmake --build go_build
    cmake --build csharp_build
    move cpp_build\Debug\shared_mem_client.lib ../cpp/lib
    move csharp_build\Debug\shared_mem_client.dll ../csharp/lib
    move go_build\libshared_mem_client.a ../go/lib
    echo Done
) > "build.log" 2>&1
