cmake_minimum_required(VERSION 3.20)
project(tfvst3 LANGUAGES C CXX)

# tfvst3 for macOS and Linux: libtfvst3.dylib / libtfvst3.so, the same C interface (tfvst3.h) as tfvst3.dll. Windows keeps its own project in
# ../CMakeLists.txt (untouched); this one reuses ../tfvst3.cpp and ../tfvst3.h as they are:
#   * compat/windows.h stands in for <windows.h> (the HWND type, the UTF-8 conversion of the path, the export attribute);
#   * module_mac.mm / module_linux.cpp are the platform half of the SDK's hosting Module (bundle loading), instead of the SDK's own;
#   * tfvst3_posix.cpp exports tfv3_list_classes / tfv3_create with the plug-in path as UTF-16 (what the C# side passes), see that file.
#   * Linux: views attach to an X11 window id (kPlatformTypeX11EmbedWindowID) that the engine creates with libX11 (managed, no build dependency here);
#     tfv3_runloop_linux.cpp is the host run loop (IRunLoop) the view gets from its frame, run by the export tfv3_run_loop_wait. No X11 headers needed.
# macOS: the editor (NSView) is not hosted yet: tfv3_has_editor answers 0 for every plug-in there.
if(WIN32)
    message(FATAL_ERROR "Use Source/native/tfvst3/CMakeLists.txt on Windows.")
endif()
if(APPLE)
    enable_language(OBJCXX)
endif()

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_VISIBILITY_PRESET hidden)
set(CMAKE_OBJCXX_VISIBILITY_PRESET hidden)
set(CMAKE_VISIBILITY_INLINES_HIDDEN ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)
if(NOT CMAKE_BUILD_TYPE AND NOT CMAKE_CONFIGURATION_TYPES)
    set(CMAKE_BUILD_TYPE Release)
endif()

# The same pinned Steinberg VST3 SDK commit as ../CMakeLists.txt (keep the two in step).
set(TFVST3_SDK_TAG    "v3.8.1_build_84")
set(TFVST3_SDK_COMMIT "3cdf9ca5d1f5b1b21e0a86832aa4abe55607bd96")
set(VST3SDK_DIR "" CACHE PATH "Optional: root of an existing Steinberg VST3 SDK checkout (default: fetch the pinned commit)")
if(NOT VST3SDK_DIR)
    include(FetchContent)
    set(FETCHCONTENT_QUIET OFF)
    FetchContent_Declare(vst3sdk
        GIT_REPOSITORY https://github.com/steinbergmedia/vst3sdk.git
        GIT_TAG        ${TFVST3_SDK_COMMIT}
        GIT_SUBMODULES base pluginterfaces public.sdk
        GIT_SUBMODULES_RECURSE OFF
        SOURCE_SUBDIR  _tfvst3_fetch_only)
    FetchContent_MakeAvailable(vst3sdk)
    set(VST3SDK_DIR "${vst3sdk_SOURCE_DIR}")
endif()
if(NOT EXISTS "${VST3SDK_DIR}/public.sdk/source/vst/hosting/module.cpp")
    message(FATAL_ERROR "VST3 SDK not found at ${VST3SDK_DIR}")
endif()

set(SDK "${VST3SDK_DIR}")
set(BRIDGE "${CMAKE_CURRENT_SOURCE_DIR}/..")

if(APPLE)
    set(TFVST3_PLATFORM_SOURCES ${SDK}/public.sdk/source/common/threadchecker_mac.mm module_mac.mm)
else()
    set(TFVST3_PLATFORM_SOURCES ${SDK}/public.sdk/source/common/threadchecker_linux.cpp module_linux.cpp tfv3_runloop_linux.cpp)
endif()

add_library(tfvst3 SHARED
    ${BRIDGE}/tfvst3.cpp
    tfvst3_posix.cpp
    ${TFVST3_PLATFORM_SOURCES}
    ${SDK}/pluginterfaces/base/coreiids.cpp
    ${SDK}/pluginterfaces/base/funknown.cpp
    ${SDK}/pluginterfaces/base/ustring.cpp
    ${SDK}/public.sdk/source/common/commoniids.cpp
    ${SDK}/public.sdk/source/common/commonstringconvert.cpp
    ${SDK}/public.sdk/source/common/memorystream.cpp
    ${SDK}/public.sdk/source/vst/vstinitiids.cpp
    ${SDK}/public.sdk/source/vst/utility/stringconvert.cpp
    ${SDK}/public.sdk/source/vst/hosting/connectionproxy.cpp
    ${SDK}/public.sdk/source/vst/hosting/eventlist.cpp
    ${SDK}/public.sdk/source/vst/hosting/hostclasses.cpp
    ${SDK}/public.sdk/source/vst/hosting/module.cpp
    ${SDK}/public.sdk/source/vst/hosting/pluginterfacesupport.cpp)

# compat/ first, so <windows.h> in tfvst3.cpp finds the stand-in; this folder for tfv3_runloop_linux.h.
target_include_directories(tfvst3 PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}/compat" "${CMAKE_CURRENT_SOURCE_DIR}" "${SDK}" "${BRIDGE}")
target_compile_definitions(tfvst3 PRIVATE $<$<CONFIG:Debug>:DEVELOPMENT=1> $<$<NOT:$<CONFIG:Debug>>:RELEASE=1>)

# tfvst3.cpp's two path functions are built under *_native names (wchar_t is 32 bits here); tfvst3_posix.cpp exports the UTF-16 forms.
set_source_files_properties(${BRIDGE}/tfvst3.cpp PROPERTIES
    COMPILE_DEFINITIONS "tfv3_list_classes=tfv3_list_classes_native;tfv3_create=tfv3_create_native")

target_compile_options(tfvst3 PRIVATE -Wall -Wno-unknown-pragmas)
if(APPLE)
    target_link_libraries(tfvst3 PRIVATE "-framework CoreFoundation" "-framework Foundation")
else()
    target_link_libraries(tfvst3 PRIVATE ${CMAKE_DL_LIBS} pthread)
    # No dependence on the system libstdc++/libgcc version (exports stay the hidden-visibility C interface).
    target_link_options(tfvst3 PRIVATE -static-libstdc++ -static-libgcc)
endif()
set_target_properties(tfvst3 PROPERTIES OUTPUT_NAME tfvst3)
