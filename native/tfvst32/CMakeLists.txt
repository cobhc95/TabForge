cmake_minimum_required(VERSION 3.20)
cmake_policy(SET CMP0091 NEW) # CMAKE_MSVC_RUNTIME_LIBRARY
project(tfvst32 LANGUAGES CXX)

# One source tree, two PEs: the x86 build is tfvst32.exe (WOW64 bridge) and the x64 build is tfvst64.exe (the same VST2
# host under Wine). Both use the static CRT, no third-party library and original VST2 declarations only. The generator
# platform selects the output names; the source list and the protocol are shared.
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

option(TFVST32_BUILD_TEST "Build the test-only helper/tests (passthrough, tfvst-testhooks, tfvsttest; never shipped)" ON)
option(TFVST32_ASAN "Build the x64 helper and its test-only harnesses with MSVC AddressSanitizer (CI/local only; never shipped)" OFF)

if(TFVST32_ASAN)
    if(NOT MSVC)
        message(FATAL_ERROR "TFVST32_ASAN uses MSVC /fsanitize=address; configure with the Visual Studio generator.")
    endif()
    if(CMAKE_SIZEOF_VOID_P EQUAL 4)
        message(FATAL_ERROR "MSVC AddressSanitizer supports x64 only; configure with -A x64 (the x86 helper stays uninstrumented).")
    endif()
    # The ASan runtime is a DLL; it needs the dynamic CRT and is copied beside each instrumented target below.
    set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>DLL")
    add_compile_options(/fsanitize=address /Zi /Od)
    add_compile_definitions(TFVST32_ASAN=1)
    # The instrumented objects carry the ASan default library; /fsanitize=address is a cl.exe option and link.exe
    # would only warn about it. /DEBUG keeps line numbers in ASan reports.
    add_link_options(/DEBUG)
endif()

if(CMAKE_SIZEOF_VOID_P EQUAL 8)
    set(TFVST_HELPER_EXE tfvst64)
    set(TFVST_TEST_EXE tfvst64test)
    set(TFVST_TEST_HOOKS tfvst64-testhooks)
    set(TFVST_TEST_FUZZ tfvst64fuzz)
    set(TFVST_TEST_PLUGIN passthrough64)
else()
    set(TFVST_HELPER_EXE tfvst32)
    set(TFVST_TEST_EXE tfvst32test)
    set(TFVST_TEST_HOOKS tfvst32-testhooks)
    set(TFVST_TEST_FUZZ tfvst32fuzz)
    set(TFVST_TEST_PLUGIN passthrough32)
endif()

set(TFVST32_SOURCES
    main.cpp
    host_session.cpp
    probe.cpp
    selftest.cpp
    crash_report.cpp
    host_runtime.cpp
    bridge_block.cpp
    pipe_channel.cpp
    tcp_channel.cpp
    hash_hold.cpp
    vst2_host.cpp
    vst2_calls.cpp
    vst2_state.cpp
    vst3_host.cpp
    plugin_host.cpp
    editor_window.cpp
)

if(TFVST32_ASAN)
    get_filename_component(TFVST_COMPILER_DIR "${CMAKE_CXX_COMPILER}" DIRECTORY)
    file(GLOB TFVST_ASAN_RUNTIMES "${TFVST_COMPILER_DIR}/clang_rt.asan*_dynamic-x86_64.dll")
endif()

function(tfvst32_copy_asan target)
    if(TFVST32_ASAN)
        foreach(runtime IN LISTS TFVST_ASAN_RUNTIMES)
            add_custom_command(TARGET ${target} POST_BUILD
                COMMAND ${CMAKE_COMMAND} -E copy_if_different "${runtime}" "$<TARGET_FILE_DIR:${target}>")
        endforeach()
    endif()
endfunction()

function(tfvst32_target target)
    add_executable(${target} ${TFVST32_SOURCES})
    target_include_directories(${target} PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}")
    target_compile_definitions(${target} PRIVATE UNICODE _UNICODE NOMINMAX WIN32_LEAN_AND_MEAN _CRT_SECURE_NO_WARNINGS)
    target_compile_options(${target} PRIVATE /utf-8 /MP /W3 /EHsc)
    target_link_libraries(${target} PRIVATE bcrypt ole32 oleaut32 user32 advapi32 shell32 avrt dbghelp ws2_32 winmm)
    target_link_options(${target} PRIVATE /LARGEADDRESSAWARE)
    set_target_properties(${target} PROPERTIES OUTPUT_NAME ${target})
    tfvst32_copy_asan(${target})
endfunction()

tfvst32_target(${TFVST_HELPER_EXE})

if(TFVST32_BUILD_TEST)
    # A separate test copy with the stall hook compiled in; never shipped, and the shipped exe above has no test path.
    tfvst32_target(${TFVST_TEST_HOOKS})
    target_compile_definitions(${TFVST_TEST_HOOKS} PRIVATE TFVST32_TEST_HOOKS)

    add_library(${TFVST_TEST_PLUGIN} SHARED test/passthrough32.cpp)
    target_compile_definitions(${TFVST_TEST_PLUGIN} PRIVATE UNICODE _UNICODE NOMINMAX WIN32_LEAN_AND_MEAN _CRT_SECURE_NO_WARNINGS)
    target_compile_options(${TFVST_TEST_PLUGIN} PRIVATE /utf-8 /MP /W3 /EHsc)
    target_link_options(${TFVST_TEST_PLUGIN} PRIVATE /LARGEADDRESSAWARE)

    add_executable(${TFVST_TEST_EXE} test/tfvst32test.cpp vst2_host.cpp vst2_calls.cpp vst2_state.cpp hash_hold.cpp
        editor_window.cpp pipe_channel.cpp)
    target_include_directories(${TFVST_TEST_EXE} PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}")
    target_compile_definitions(${TFVST_TEST_EXE} PRIVATE UNICODE _UNICODE NOMINMAX WIN32_LEAN_AND_MEAN _CRT_SECURE_NO_WARNINGS)
    target_compile_options(${TFVST_TEST_EXE} PRIVATE /utf-8 /MP /W3 /EHsc)
    target_link_libraries(${TFVST_TEST_EXE} PRIVATE bcrypt ole32 user32)
    target_link_options(${TFVST_TEST_EXE} PRIVATE /LARGEADDRESSAWARE)
    add_dependencies(${TFVST_TEST_EXE} ${TFVST_TEST_PLUGIN}) # loaded at run time from the same folder
    tfvst32_copy_asan(${TFVST_TEST_EXE})

    # Fuzz harness (test-only, never shipped): both frame transports, state blobs and the load/unload leak loop.
    add_executable(${TFVST_TEST_FUZZ} test/fuzz_frames.cpp vst2_host.cpp vst2_calls.cpp vst2_state.cpp hash_hold.cpp
        editor_window.cpp pipe_channel.cpp tcp_channel.cpp bridge_block.cpp)
    target_include_directories(${TFVST_TEST_FUZZ} PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}")
    target_compile_definitions(${TFVST_TEST_FUZZ} PRIVATE UNICODE _UNICODE NOMINMAX WIN32_LEAN_AND_MEAN _CRT_SECURE_NO_WARNINGS)
    target_compile_options(${TFVST_TEST_FUZZ} PRIVATE /utf-8 /MP /W3 /EHsc)
    target_link_libraries(${TFVST_TEST_FUZZ} PRIVATE bcrypt ole32 user32 ws2_32 psapi)
    target_link_options(${TFVST_TEST_FUZZ} PRIVATE /LARGEADDRESSAWARE)
    add_dependencies(${TFVST_TEST_FUZZ} ${TFVST_TEST_PLUGIN})
    tfvst32_copy_asan(${TFVST_TEST_FUZZ})
endif()
