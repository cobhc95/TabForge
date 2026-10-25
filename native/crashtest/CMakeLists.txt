cmake_minimum_required(VERSION 3.20)
project(TabForgeCrashTest C)

set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

add_library(TabForgeCrashTest SHARED crashtest.c)

set_target_properties(TabForgeCrashTest PROPERTIES
    OUTPUT_NAME "TabForgeCrashTest"
    SUFFIX ".dll"
)
