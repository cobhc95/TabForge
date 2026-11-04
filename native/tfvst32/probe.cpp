#include <cstdio>
#include <string>

#include <windows.h>

#include "crash_report.h"
#include "host_runtime.h"
#include "plugin_host.h"

using namespace tfvst32;

namespace {
std::string Narrow(const std::wstring& text) {
    if (text.empty()) return {};
    const int size = WideCharToMultiByte(CP_UTF8, 0, text.c_str(), -1, nullptr, 0, nullptr, nullptr);
    if (size <= 1) return {};
    std::string result(static_cast<size_t>(size), '\0');
    WideCharToMultiByte(CP_UTF8, 0, text.c_str(), -1, result.data(), size, nullptr, nullptr);
    result.resize(static_cast<size_t>(size - 1));
    return result;
}

std::wstring FolderOf(const std::wstring& path) {
    const size_t slash = path.find_last_of(L"\\/");
    return slash == std::wstring::npos ? std::wstring() : path.substr(0, slash);
}
}  // namespace

int RunProbe(int argc, wchar_t** argv) {
    if (argc < 4) {
        std::fprintf(stderr, "usage: " TFVST_HELPER_STEM ".exe --probe <plugin.dll> <sha256>\n");
        return 2;
    }
    const std::wstring path = argv[2];
    SetCrashPluginPath(path);
    const std::string sha = Narrow(argv[3]);
    if (sha.empty() || _stricmp(sha.c_str(), "none") == 0) {
        std::fprintf(stderr, "a SHA-256 fingerprint is required to probe a plug-in (empty or 'none' is refused)\n");
        return 5;   // fail closed, like the managed Hold, before any DLL is mapped
    }
    if (!FolderOf(path).empty()) SetCurrentDirectoryW(FolderOf(path).c_str());
    tfvst32::ComScope com;
    const bool vst3 = path.size() >= 5 && _wcsicmp(path.c_str() + path.size() - 5, L".vst3") == 0;
    tfvst32::PluginHost host;
    std::string error;
    if (!host.Load(vst3 ? L"VST3" : L"VST2", path, sha, 48000, 256, false, error)) {
        std::fprintf(stderr, "%s\n", error.c_str());
        return host.Refused() ? 5 : 1;
    }
    auto clean = [](std::string text) {
        for (char& c : text) if (c == '|' || c == '\r' || c == '\n') c = ' ';
        while (!text.empty() && text.back() == ' ') text.pop_back();
        return text;
    };
    std::printf("%s|%s|%s\n", host.IsInstrument() ? "Instrument" : "Effect",
                clean(host.Vendor()).c_str(), clean(host.Product()).c_str());
    std::fflush(stdout);
    host.Close();
    return 0;
}
