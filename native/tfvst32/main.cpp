// tfvst32.exe: one 32-bit VST2 plug-in, or one Windows VST3 plug-in, in one helper process. Modes:
//   --vst32-host <id> <enginePid> <rate> <block> <instrument> VST2|VST3 <path> <sha|none> <log|none> <trust> <slots>
//   --vst32-host-tcp <port> <blockPath> <rate> <block> <instrument> VST2|VST3 <path> <sha|none> <log|none> <trust> <slots>
//   --probe <path> <sha256>
//   --selftest
// The named-pipe/event mode is the unchanged Windows default. The TCP/file mode is the Wine bridge transport; its
// session token comes from TABFORGE_HOST_TOKEN (64 hex characters) and is checked by the engine in the hello frame.
#include <algorithm>
#include <cstdio>
#include <cwchar>
#include <string>

#include <windows.h>
#include <timeapi.h>

#include "bridge_block.h"
#include "crash_report.h"
#include "editor_window.h"
#include "host_session.h"
#include "pipe_channel.h"
#include "tcp_channel.h"
#include "tfvst32_protocol.h"

using namespace tfvst32;

int RunProbe(int argc, wchar_t** argv);
int RunSelfTest();

namespace {
// The Wine transport polls a flag in the shared block and sleeps 1 ms while idle. Without a 1 ms timer the process gets
// the default ~15.6 ms tick, so a 256-frame block (5.3 ms) can miss every time. Hold the finer resolution for the helper's
// lifetime, as the engine already does in its own process; the named-pipe/event transport is not affected.
struct TimerResolution {
    UINT result = timeBeginPeriod(1);
    ~TimerResolution() { if (result == TIMERR_NOERROR) timeEndPeriod(1); }
};

bool ParseInt(const wchar_t* text, int& value) {
    if (text == nullptr || *text == 0) return false;
    wchar_t* end = nullptr;
    const long long parsed = wcstoll(text, &end, 10);
    if (end == nullptr || *end != 0 || parsed < INT32_MIN || parsed > INT32_MAX) return false;
    value = static_cast<int>(parsed);
    return true;
}

bool ValidId(const std::wstring& id) {
    if (id.size() != 32) return false;
    for (wchar_t c : id) if (!iswalnum(c)) return false;
    return true;
}

bool ValidToken(const std::string& token) {
    if (token.size() != kTokenBytes * 2) return false;
    for (char c : token)
        if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))) return false;
    return true;
}

std::string TokenFromEnvironment() {
    wchar_t text[130] = {};
    const DWORD length = GetEnvironmentVariableW(L"TABFORGE_HOST_TOKEN", text, 130);
    if (length == 0 || length >= 130) return {};
    std::string token(static_cast<size_t>(length), '\0');
    for (DWORD i = 0; i < length; i++) token[i] = static_cast<char>(text[i]);
    return token;
}

std::string Narrow(const std::wstring& text) {
    if (text.empty()) return {};
    const int size = WideCharToMultiByte(CP_UTF8, 0, text.c_str(), -1, nullptr, 0, nullptr, nullptr);
    if (size <= 1) return {};
    std::string result(static_cast<size_t>(size), '\0');
    WideCharToMultiByte(CP_UTF8, 0, text.c_str(), -1, result.data(), size, nullptr, nullptr);
    result.resize(static_cast<size_t>(size - 1));
    return result;
}

void SendFailure(ControlChannel& channel, const std::string& text) {
    Writer writer;
    writer.String(text);
    channel.Write(MsgFailed, writer.Data());
}

int RunHost(int argc, wchar_t** argv) {
    if (argc < 13) return 2;
    const std::wstring id = argv[2];
    int enginePid = 0, sampleRate = 0, maxBlock = 0, slots = 1;
    if (!ValidId(id) || !ParseInt(argv[3], enginePid) || !ParseInt(argv[4], sampleRate) ||
        !ParseInt(argv[5], maxBlock) || !ParseInt(argv[12], slots)) return 2;
    if (sampleRate < kMinSampleRate || sampleRate > kMaxSampleRate ||
        maxBlock < kMinBlock || maxBlock > kMaxBlock || slots < 1 || slots > 2) return 2;
    const std::wstring format = argv[7];
    if (format != L"VST2" && format != L"VST3") return 2;
    const std::string sha = Narrow(argv[9]);
    if (!HostTrustAllowed(argv[11], sha)) {
        std::fprintf(stderr, "the host launch must carry a SHA-256, or be location-trusted\n");
        return 5;   // fail closed before LoadLibrary
    }

    HANDLE engine = OpenProcess(SYNCHRONIZE | PROCESS_QUERY_LIMITED_INFORMATION, FALSE, static_cast<DWORD>(enginePid));
    if (engine == nullptr || WaitForSingleObject(engine, 0) != WAIT_TIMEOUT) {
        if (engine != nullptr) CloseHandle(engine);
        return 3;
    }
    PipeChannel channel;
    std::string error;
    if (!channel.Connect(id, 5000, error)) {
        std::fprintf(stderr, "%s\n", error.c_str());
        CloseHandle(engine);
        return 3;
    }
    BridgeBlock block;
    if (!block.Open(id, maxBlock, slots, error)) {
        SendFailure(channel, error);
        CloseHandle(engine);
        return 4;
    }
    const int result = RunHostSession(channel, block, format, argv[8], sha, std::wstring(argv[6]) == L"1",
                                      sampleRate, maxBlock, engine, false, "");
    block.Close();
    channel.Close();
    CloseHandle(engine);
    return result;
}

int RunTcpHost(int argc, wchar_t** argv) {
    if (argc < 13) return 2;
    int port = 0, sampleRate = 0, maxBlock = 0, slots = 1;
    if (!ParseInt(argv[2], port) || !ParseInt(argv[4], sampleRate) || !ParseInt(argv[5], maxBlock) ||
        !ParseInt(argv[12], slots)) return 2;
    if (port < 1 || port > 65535 || sampleRate < kMinSampleRate || sampleRate > kMaxSampleRate ||
        maxBlock < kMinBlock || maxBlock > kMaxBlock || slots < 1 || slots > 2) return 2;
    const std::wstring format = argv[7];
    if (format != L"VST2" && format != L"VST3") return 2;
    const std::string sha = Narrow(argv[9]);
    if (!HostTrustAllowed(argv[11], sha)) {
        std::fprintf(stderr, "the host launch must carry a SHA-256, or be location-trusted\n");
        return 5;   // fail closed before loading anything
    }
    const std::string token = TokenFromEnvironment();
    if (!ValidToken(token)) {
        std::fprintf(stderr, "TABFORGE_HOST_TOKEN must be 64 hex characters for the TCP transport\n");
        return 2;
    }
    TcpChannel channel;
    std::string error;
    if (!channel.Connect(port, 5000, error)) {
        std::fprintf(stderr, "%s\n", error.c_str());
        return 3;
    }
    BridgeBlock block;
    if (!block.OpenFile(argv[3], maxBlock, slots, error)) {
        SendFailure(channel, error);
        channel.Close();
        return 4;
    }
    const int result = RunHostSession(channel, block, format, argv[8], sha, std::wstring(argv[6]) == L"1",
                                      sampleRate, maxBlock, nullptr, true, token);
    const bool engineLost = channel.Broken();
    block.Close();
    channel.Close();
    // The engine deletes the explicit block on its normal and crash paths. When the engine process itself is killed it
    // cannot do that, and this helper is the only remaining owner: remove the file here instead of leaving it for the
    // engine's 2-hour stale sweep. A failed delete (another live engine mapping it) is harmless; the engine owns it then.
    if (engineLost) DeleteFileW(argv[3]);
    return result;
}
}  // namespace

int wmain(int argc, wchar_t** argv) {
    TimerResolution timer;
    InstallCrashHandler();
    EditorWindow::EnableDpiAwareness();   // per-monitor v2 before any window exists
    if (argc < 2) {
        std::fprintf(stderr, TFVST_HELPER_STEM ".exe: --vst32-host | --vst32-host-tcp | --probe | --selftest\n");
        return 2;
    }
    const std::wstring mode = argv[1];
    if (mode == L"--selftest") return RunSelfTest();
    if (mode == L"--probe") return RunProbe(argc, argv);
    if (mode == L"--vst32-host") return RunHost(argc, argv);
    if (mode == L"--vst32-host-tcp") return RunTcpHost(argc, argv);
    std::fprintf(stderr, TFVST_HELPER_STEM ".exe: unknown mode %ls\n", mode.c_str());
    return 2;
}
