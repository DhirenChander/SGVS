# Created with the help of Claude
include(FetchContent)
FetchContent_Declare(
  unity
  GIT_REPOSITORY https://github.com/ThrowTheSwitch/Unity.git
  GIT_TAG        v2.6.0
)
FetchContent_MakeAvailable(unity)

foreach(t ringbuff_test)
  add_executable(${t} ${t}.c)
  target_link_libraries(${t} PRIVATE shared_mem_client unity)
  add_test(NAME ${t} COMMAND ${t})
endforeach()