cmake_minimum_required(VERSION 3.20)
cmake_policy(SET CMP0091 NEW) # CMAKE_MSVC_RUNTIME_LIBRARY
project(tfvst3 LANGUAGES CXX)

# Static CRT so no VC++ runtime is needed next to the DLL.
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Steinberg VST3 SDK (MIT licence since v3.8.0), PINNED to one exact commit. The three submodules tfvst3 uses are
# recorded by that commit (base fcf9da0b, pluginterfaces 4f547e8e, public.sdk 586dc5e6); vstgui, doc and the
# tutorials are not fetched. Nothing is patched. Pass -DVST3SDK_DIR=<path> to use an existing checkout instead
# (it must be the same commit; native/BUILD.md shows how to check).
set(TFVST3_SDK_TAG    "v3.8.1_build_84")
set(TFVST3_SDK_COMMIT "3cdf9ca5d1f5b1b21e0a86832aa4abe55607bd96")
set(VST3SDK_DIR "" CACHE PATH "Optional: root of an existing Steinberg VST3 SDK checkout (default: fetch the pinned commit)")
if(NOT VST3SDK_DIR)
    include(FetchContent)
    set(FETCHCONTENT_QUIET OFF)
    FetchContent_Declare(vst3sdk
        GIT_REPOSITORY https://github.com/steinbergmedia/vst3sdk.git
        GIT_TAG        ${TFVST3_SDK_COMMIT}   # the commit of ${TFVST3_SDK_TAG}
        GIT_SUBMODULES base pluginterfaces public.sdk
        GIT_SUBMODULES_RECURSE OFF
        SOURCE_SUBDIR  _tfvst3_fetch_only)    # no such folder: fetch the sources, do not add the SDK's own CMake project
    FetchContent_MakeAvailable(vst3sdk)
    set(VST3SDK_DIR "${vst3sdk_SOURCE_DIR}")
endif()
if(NOT EXISTS "${VST3SDK_DIR}/public.sdk/source/vst/hosting/module.cpp")
    message(FATAL_ERROR "VST3 SDK not found at ${VST3SDK_DIR}")
endif()

option(TFVST3_BUILD_TEST "Build the tfv3test console program" ON)

set(SDK "${VST3SDK_DIR}")
set(TFVST3_SDK_SOURCES
    # pluginterfaces/base
    ${SDK}/pluginterfaces/base/coreiids.cpp
    ${SDK}/pluginterfaces/base/funknown.cpp
    ${SDK}/pluginterfaces/base/ustring.cpp
    # public.sdk common
    ${SDK}/public.sdk/source/common/commoniids.cpp
    ${SDK}/public.sdk/source/common/commonstringconvert.cpp
    ${SDK}/public.sdk/source/common/memorystream.cpp
    ${SDK}/public.sdk/source/common/threadchecker_win32.cpp
    # public.sdk vst
    ${SDK}/public.sdk/source/vst/vstinitiids.cpp
    ${SDK}/public.sdk/source/vst/utility/stringconvert.cpp
    ${SDK}/public.sdk/source/vst/hosting/connectionproxy.cpp
    ${SDK}/public.sdk/source/vst/hosting/eventlist.cpp
    ${SDK}/public.sdk/source/vst/hosting/hostclasses.cpp
    ${SDK}/public.sdk/source/vst/hosting/module.cpp
    ${SDK}/public.sdk/source/vst/hosting/module_win32.cpp
    ${SDK}/public.sdk/source/vst/hosting/pluginterfacesupport.cpp
)

add_library(tfvst3 SHARED tfvst3.cpp tfvst3.h ${TFVST3_SDK_SOURCES})
target_include_directories(tfvst3 PRIVATE "${SDK}" "${CMAKE_CURRENT_SOURCE_DIR}")
target_compile_definitions(tfvst3 PRIVATE
    UNICODE _UNICODE NOMINMAX WIN32_LEAN_AND_MEAN
    _CRT_SECURE_NO_WARNINGS _SILENCE_CXX17_CODECVT_HEADER_DEPRECATION_WARNING
    $<$<CONFIG:Debug>:DEVELOPMENT=1> $<$<NOT:$<CONFIG:Debug>>:RELEASE=1>)
target_compile_options(tfvst3 PRIVATE /utf-8 /MP /W3 /EHsc)
target_link_libraries(tfvst3 PRIVATE ole32 shell32 user32)
set_target_properties(tfvst3 PROPERTIES OUTPUT_NAME tfvst3)

if(TFVST3_BUILD_TEST)
    add_executable(tfv3test test/tfv3test.cpp)
    target_include_directories(tfv3test PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}")
    target_compile_definitions(tfv3test PRIVATE UNICODE _UNICODE NOMINMAX _CRT_SECURE_NO_WARNINGS)
    target_compile_options(tfv3test PRIVATE /utf-8 /W3 /EHsc)
    target_link_libraries(tfv3test PRIVATE ole32)
    add_dependencies(tfv3test tfvst3) # loaded at run time from the same folder
endif()
