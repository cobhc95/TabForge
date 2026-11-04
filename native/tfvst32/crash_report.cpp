#include "crash_report.h"

#include <algorithm>
#include <cstdio>
#include <cwchar>
#include <iterator>
#include <string>

#include <windows.h>
#include <dbghelp.h>

#include "tfvst32_protocol.h"

namespace tfvst32 {
namespace {
constexpr int kKeepDumps = 5;
constexpr int kMaxDumpsSeen = 64;

wchar_t g_plugin[1024] = {};
volatile LONG g_writing = 0;

std::wstring CrashFolder() {
    wchar_t local[1024] = {};
    const DWORD length = GetEnvironmentVariableW(L"LOCALAPPDATA", local, static_cast<DWORD>(std::size(local)));
    std::wstring folder = length > 0 && length < std::size(local) ? std::wstring(local, length) : L".";
    folder += L"\\TabForge";
    CreateDirectoryW(folder.c_str(), nullptr);
    folder += L"\\Diagnostics";
    CreateDirectoryW(folder.c_str(), nullptr);
    return folder;
}

void WriteTextFile(const std::wstring& path, const std::wstring& text) {
    HANDLE file = CreateFileW(path.c_str(), GENERIC_WRITE, FILE_SHARE_READ, nullptr, CREATE_ALWAYS,
                              FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return;
    char utf8[2048] = {};
    const int bytes = WideCharToMultiByte(CP_UTF8, 0, text.c_str(), -1, utf8, sizeof(utf8), nullptr, nullptr);
    DWORD written = 0;
    if (bytes > 1) WriteFile(file, utf8, static_cast<DWORD>(bytes - 1), &written, nullptr);
    FlushFileBuffers(file);
    CloseHandle(file);
}

void PruneDumps(const std::wstring& folder) {
    const std::wstring pattern = folder + L"\\" TFVST_HELPER_STEM_W L"-*.dmp";
    WIN32_FIND_DATAW data{};
    HANDLE find = FindFirstFileW(pattern.c_str(), &data);
    if (find == INVALID_HANDLE_VALUE) return;
    wchar_t names[kMaxDumpsSeen][MAX_PATH] = {};
    FILETIME times[kMaxDumpsSeen] = {};
    int count = 0;
    do {
        if ((data.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0 || count >= kMaxDumpsSeen) continue;
        std::wcsncpy(names[count], data.cFileName, MAX_PATH - 1);
        times[count] = data.ftLastWriteTime;
        count++;
    } while (FindNextFileW(find, &data));
    FindClose(find);
    // Newest first; keep kKeepDumps (the file just written is the newest).
    for (int i = 1; i < count; i++) {
        wchar_t name[MAX_PATH] = {};
        std::wcsncpy(name, names[i], MAX_PATH - 1);
        FILETIME time = times[i];
        int j = i - 1;
        while (j >= 0 && CompareFileTime(&times[j], &time) < 0) {
            std::wcsncpy(names[j + 1], names[j], MAX_PATH - 1);
            times[j + 1] = times[j];
            j--;
        }
        std::wcsncpy(names[j + 1], name, MAX_PATH - 1);
        times[j + 1] = time;
    }
    for (int i = kKeepDumps; i < count; i++) {
        const std::wstring stale = folder + L"\\" + names[i];
        DeleteFileW(stale.c_str());
    }
}

void WriteReport(EXCEPTION_POINTERS* info) {
    const DWORD code = info != nullptr && info->ExceptionRecord != nullptr
                           ? info->ExceptionRecord->ExceptionCode
                           : 0xE0000000u;
    const void* address = info != nullptr && info->ExceptionRecord != nullptr
                              ? info->ExceptionRecord->ExceptionAddress
                              : nullptr;

    wchar_t modulePath[MAX_PATH] = {};
    HMODULE module = nullptr;
    if (address != nullptr) {
        GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                           reinterpret_cast<LPCWSTR>(address), &module);
        if (module != nullptr) GetModuleFileNameW(module, modulePath, MAX_PATH);
    }
    const wchar_t* moduleName = modulePath;
    if (const wchar_t* slash = std::wcsrchr(modulePath, L'\\')) moduleName = slash + 1;
    const unsigned long long offset = reinterpret_cast<unsigned long long>(address) -
                                      reinterpret_cast<unsigned long long>(module);

    const std::wstring folder = CrashFolder();
    wchar_t stem[64] = {};
    std::swprintf(stem, std::size(stem), TFVST_HELPER_STEM_W L"-%lu", static_cast<unsigned long>(GetCurrentProcessId()));
    const std::wstring dumpPath = folder + L"\\" + stem + L".dmp";
    const std::wstring reportPath = folder + L"\\" + stem + L".txt";

    HANDLE dump = CreateFileW(dumpPath.c_str(), GENERIC_WRITE, FILE_SHARE_READ, nullptr, CREATE_ALWAYS,
                              FILE_ATTRIBUTE_NORMAL, nullptr);
    if (dump != INVALID_HANDLE_VALUE) {
        MINIDUMP_EXCEPTION_INFORMATION exception{GetCurrentThreadId(), info, FALSE};
        const auto type = static_cast<MINIDUMP_TYPE>(MiniDumpWithDataSegs | MiniDumpWithHandleData | MiniDumpWithThreadInfo);
        const BOOL wrote = MiniDumpWriteDump(GetCurrentProcess(), GetCurrentProcessId(), dump, type,
                                             info != nullptr ? &exception : nullptr, nullptr, nullptr);
        CloseHandle(dump);
        if (!wrote) DeleteFileW(dumpPath.c_str());
    }

    wchar_t line[2048] = {};
    std::swprintf(line, std::size(line),
                  TFVST_HELPER_STEM_W L" crash: pid=%lu code=0x%08lX address=0x%p module=%ls offset=0x%llX plugin=%ls dump=%ls\n",
                  static_cast<unsigned long>(GetCurrentProcessId()), static_cast<unsigned long>(code), address,
                  moduleName[0] != 0 ? moduleName : L"unknown", offset, g_plugin[0] != 0 ? g_plugin : L"unknown",
                  dumpPath.c_str());
    WriteTextFile(reportPath, line);
    PruneDumps(folder);
}

LONG WINAPI CrashFilter(EXCEPTION_POINTERS* info) {
    if (InterlockedExchange(&g_writing, 1) != 0) {
        const DWORD code = info != nullptr && info->ExceptionRecord != nullptr
                               ? info->ExceptionRecord->ExceptionCode
                               : 0xE0000000u;
        TerminateProcess(GetCurrentProcess(), code);
        return EXCEPTION_EXECUTE_HANDLER;
    }
    WriteReport(info);
    const DWORD code = info != nullptr && info->ExceptionRecord != nullptr
                           ? info->ExceptionRecord->ExceptionCode
                           : 0xE0000000u;
    TerminateProcess(GetCurrentProcess(), code);   // no WER dialog, no hanging message loop
    return EXCEPTION_EXECUTE_HANDLER;
}
}  // namespace

void InstallCrashHandler() {
    // A plug-in fault must end this helper quickly, with a report, not show a system dialog or wait for a debugger.
    SetErrorMode(GetErrorMode() | SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX | SEM_NOOPENFILEERRORBOX);
    SetUnhandledExceptionFilter(&CrashFilter);
}

void SetCrashPluginPath(const std::wstring& path) {
    const wchar_t* name = path.c_str();
    if (const wchar_t* slash = std::wcsrchr(path.c_str(), L'\\')) name = slash + 1;
    if (const wchar_t* slash = std::wcsrchr(name, L'/')) name = slash + 1;
    std::wcsncpy(g_plugin, name, std::size(g_plugin) - 1);
    g_plugin[std::size(g_plugin) - 1] = 0;
}
}  // namespace tfvst32
