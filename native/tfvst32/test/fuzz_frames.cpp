// Test-only fuzz harness for the VST2 helper: frame readers on both transports, state blobs and the load/unload loop.
// Built only with TFVST32_BUILD_TEST=ON; never linked into tfvst32.exe/tfvst64.exe and never shipped.
#include <algorithm>
#include <atomic>
#include <chrono>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <string>
#include <thread>
#include <vector>

#include <windows.h>
#include <psapi.h>
#include <winsock2.h>
#include <ws2tcpip.h>

#include "pipe_channel.h"
#include "tcp_channel.h"
#include "tfvst32_protocol.h"
#include "vst2_host.h"

using namespace tfvst32;

namespace {

constexpr int kDefaultCount = 200;
constexpr int kDefaultSeed = 20261101;
constexpr int kFrameTimeLimitMs = 5000;

struct Rng {
    uint64_t state;
    explicit Rng(uint64_t seed) : state(seed ? seed : 0x9E3779B97F4A7C15ull) {}
    uint32_t Next() {
        state ^= state >> 12;
        state ^= state << 25;
        state ^= state >> 27;
        return static_cast<uint32_t>((state * 0x2545F4914F6CDD1Dull) >> 32);
    }
    int Range(int low, int high) { return low + static_cast<int>(Next() % static_cast<uint32_t>(high - low + 1)); }
};

void Encode(std::vector<uint8_t>& out, int32_t length, uint8_t type, const std::vector<uint8_t>& payload) {
    // At least the 4-byte length and the type byte, even for a deliberately negative length (the reader must refuse it).
    out.resize(std::max<size_t>(5, static_cast<size_t>(std::max(0, length)) + 4));
    EncodeHeader(out.data(), length);
    out[4] = type;
    const size_t copy = std::min(payload.size(), out.size() > 5 ? out.size() - 5 : 0);
    if (copy > 0) std::memcpy(out.data() + 5, payload.data(), copy);
}

std::vector<uint8_t> RandomBytes(Rng& rng, size_t count) {
    std::vector<uint8_t> bytes(count);
    for (auto& b : bytes) b = static_cast<uint8_t>(rng.Next());
    return bytes;
}

// One case: a well-formed frame, a truncated header/payload, a bad length, an oversized state header, or random bytes.
std::vector<uint8_t> FrameCase(Rng& rng, int index) {
    const uint8_t types[] = {CmdGetState, CmdSetState, CmdOpenEditor, CmdCloseEditor, CmdDescribe, CmdListParams,
                             CmdSetParam, CmdQuit, CmdSetProgram, MsgHello, MsgState, MsgFailed, MsgLoaded};
    const uint8_t type = types[rng.Next() % (sizeof(types) / sizeof(types[0]))];
    const int shape = (index + static_cast<int>(rng.Next())) % 7;
    std::vector<uint8_t> frame;
    switch (shape) {
        case 0: {
            auto payload = RandomBytes(rng, static_cast<size_t>(rng.Range(0, 64)));
            Encode(frame, static_cast<int32_t>(payload.size() + 1), type, payload);
            return frame;
        }
        case 1: {
            frame.resize(static_cast<size_t>(rng.Range(0, 3)));
            for (auto& b : frame) b = static_cast<uint8_t>(rng.Next());
            return frame;
        }
        case 2: {
            const int32_t lengths[] = {0, -1, 1, 2, kMaxFrameBytes, kMaxFrameBytes + 1, 0x7FFFFFFF};
            Encode(frame, lengths[rng.Next() % (sizeof(lengths) / sizeof(lengths[0]))], type, {});
            frame.resize(5);
            return frame;
        }
        case 3: {
            const int32_t length = static_cast<int32_t>(rng.Range(2, 4096));
            Encode(frame, length, type, RandomBytes(rng, static_cast<size_t>(length - 1)));
            frame.resize(static_cast<size_t>(rng.Range(0, static_cast<int>(frame.size()))));
            return frame;
        }
        case 4: {
            const int32_t length = static_cast<int32_t>(rng.Range(2, 4096));
            Encode(frame, length, type, RandomBytes(rng, static_cast<size_t>(length - 1)));
            return frame;
        }
        case 5: {
            // A state command over the state cap: the reader drains it and answers state-too-large.
            const int32_t length = kMaxStateBytes + 2 + rng.Range(0, 64);
            Encode(frame, length, CmdSetState, RandomBytes(rng, static_cast<size_t>(rng.Range(0, 64))));
            frame.resize(static_cast<size_t>(rng.Range(5, static_cast<int>(frame.size()))));
            return frame;
        }
        default:
            return RandomBytes(rng, static_cast<size_t>(rng.Range(0, 128)));
    }
}

bool WriteAll(SOCKET socket, const std::vector<uint8_t>& bytes) {
    size_t sent = 0;
    while (sent < bytes.size()) {
        const int n = send(socket, reinterpret_cast<const char*>(bytes.data() + sent),
                           static_cast<int>(std::min<size_t>(bytes.size() - sent, 4096)), 0);
        if (n <= 0) return false;
        sent += static_cast<size_t>(n);
    }
    return true;
}

bool TcpCase(const std::vector<uint8_t>& bytes) {
    SOCKET listener = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    if (listener == INVALID_SOCKET) return false;
    sockaddr_in address{};
    address.sin_family = AF_INET;
    address.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
    address.sin_port = 0;
    if (bind(listener, reinterpret_cast<sockaddr*>(&address), sizeof(address)) != 0 ||
        listen(listener, 1) != 0) {
        closesocket(listener);
        return false;
    }
    int length = sizeof(address);
    if (getsockname(listener, reinterpret_cast<sockaddr*>(&address), &length) != 0) {
        closesocket(listener);
        return false;
    }
    const int port = ntohs(address.sin_port);
    std::thread server([listener, bytes] {
        SOCKET client = accept(listener, nullptr, nullptr);
        if (client != INVALID_SOCKET) {
            WriteAll(client, bytes);
            shutdown(client, SD_BOTH);
            closesocket(client);
        }
        closesocket(listener);
    });
    TcpChannel channel;
    std::string error;
    bool ok = true;
    if (!channel.Connect(port, 3000, error)) ok = false;
    else {
        uint8_t type = 0;
        std::vector<uint8_t> payload;
        const auto started = std::chrono::steady_clock::now();
        channel.Read(type, payload);   // true or false are both controlled; the harness is about no crash and no hang
        const auto elapsed = std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::steady_clock::now() - started).count();
        if (elapsed > kFrameTimeLimitMs) {
            std::fprintf(stderr, "tcp frame case took %lld ms (limit %d)\n", static_cast<long long>(elapsed), kFrameTimeLimitMs);
            ok = false;
        }
    }
    channel.Close();
    server.join();
    return ok;
}

bool PipeCase(const std::vector<uint8_t>& bytes, int index) {
    wchar_t id[33] = {};
    std::swprintf(id, 33, L"%08lx%08lx%016llx%08x", GetCurrentProcessId(), GetCurrentThreadId(),
                  static_cast<unsigned long long>(GetTickCount64()), static_cast<unsigned>(index));
    const std::wstring name = L"\\\\.\\pipe\\TabForge.PluginHost." + std::wstring(id);
    HANDLE server = CreateNamedPipeW(name.c_str(), PIPE_ACCESS_DUPLEX,
                                      PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT, 1, 65536, 65536, 0, nullptr);
    if (server == INVALID_HANDLE_VALUE) return false;
    std::thread writer([server, bytes] {
        ConnectNamedPipe(server, nullptr);
        DWORD written = 0;
        size_t sent = 0;
        while (sent < bytes.size()) {
            const DWORD chunk = static_cast<DWORD>(std::min<size_t>(bytes.size() - sent, 4096));
            if (!WriteFile(server, bytes.data() + sent, chunk, &written, nullptr) || written == 0) break;
            sent += written;
        }
        // Do not FlushFileBuffers: it waits for the client to drain every byte, and a truncated-frame case stops reading
        // early by design. Closing the handle delivers the bytes already written and then end-of-pipe to the reader.
        Sleep(20);
        CloseHandle(server);
    });
    PipeChannel channel;
    std::string error;
    bool ok = true;
    if (!channel.Connect(id, 3000, error)) ok = false;
    else {
        uint8_t type = 0;
        std::vector<uint8_t> payload;
        const auto started = std::chrono::steady_clock::now();
        channel.Read(type, payload);
        const auto elapsed = std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::steady_clock::now() - started).count();
        if (elapsed > kFrameTimeLimitMs) {
            std::fprintf(stderr, "pipe frame case took %lld ms (limit %d)\n", static_cast<long long>(elapsed), kFrameTimeLimitMs);
            ok = false;
        }
    }
    channel.Close();
    writer.join();
    return ok;
}

int RunFrames(int count, int seed) {
    WSADATA data{};
    if (WSAStartup(MAKEWORD(2, 2), &data) != 0) {
        std::fprintf(stderr, "tfvst32fuzz: Winsock could not start\n");
        return 1;
    }
    Rng rng(static_cast<uint64_t>(seed));
    int failed = 0;
    for (int i = 0; i < count; i++) {
        const auto frame = FrameCase(rng, i);
        if (!TcpCase(frame)) { failed++; std::fprintf(stderr, "tcp case %d failed\n", i); }
        if (!PipeCase(frame, i)) { failed++; std::fprintf(stderr, "pipe case %d failed\n", i); }
    }
    WSACleanup();
    std::printf("tfvst32fuzz frames: %d cases per transport (seed %d), %d failed\n", count, seed, failed);
    return failed == 0 ? 0 : 1;
}

std::wstring AbsolutePath(std::wstring path) {
    wchar_t full[32768] = {};
    const DWORD length = GetFullPathNameW(path.c_str(), 32768, full, nullptr);
    return length > 0 && length < 32768 ? std::wstring(full, length) : path;
}

std::vector<uint8_t> MutateState(Rng& rng, const std::vector<uint8_t>& seed) {
    if (seed.empty()) return RandomBytes(rng, static_cast<size_t>(rng.Range(0, 256)));
    std::vector<uint8_t> data = seed;
    switch (rng.Next() % 5) {
        case 0:
            for (int n = rng.Range(1, 8); n > 0 && !data.empty(); n--) data[rng.Next() % data.size()] ^= static_cast<uint8_t>(rng.Range(1, 255));
            return data;
        case 1:
            return std::vector<uint8_t>(data.begin(), data.begin() + static_cast<std::ptrdiff_t>(rng.Range(0, static_cast<int>(data.size()))));
        case 2:
            data.resize(static_cast<size_t>(rng.Range(0, 4096)));
            for (auto& b : data) b = static_cast<uint8_t>(rng.Next());
            return data;
        case 3: {
            const size_t sizes[] = {0, 1, 4, 5, 8, 11, 12, 64, 1024, 64 * 1024};
            return RandomBytes(rng, sizes[rng.Next() % (sizeof(sizes) / sizeof(sizes[0]))]);
        }
        default:
            data.insert(data.end(), 1 + rng.Next() % 16, static_cast<uint8_t>(rng.Next()));
            return data;
    }
}

int RunState(const std::wstring& dllPath, int count, int seed) {
    const std::wstring dll = AbsolutePath(dllPath);
    Vst2Host host;
    std::string error;
    if (!host.Load(dll, "none", 48000, 256, error)) {
        std::fprintf(stderr, "tfvst32fuzz: state seed load failed: %s\n", error.c_str());
        return 1;
    }
    std::vector<uint8_t> seedState;
    if (!host.GetState(seedState)) {
        std::fprintf(stderr, "tfvst32fuzz: the seed plug-in returned no state\n");
        host.Close();
        return 1;
    }
    Rng rng(static_cast<uint64_t>(seed));
    int failed = 0;
    for (int i = 0; i < count; i++) {
        auto state = MutateState(rng, seedState);
        if (state.size() > static_cast<size_t>(kMaxStateBytes) + 1) state.resize(kMaxStateBytes + 1);
        const auto started = std::chrono::steady_clock::now();
        host.SetState(state);
        std::vector<uint8_t> after;
        host.GetState(after);
        const auto elapsed = std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::steady_clock::now() - started).count();
        if (elapsed > 2000 || host.Vendor() != "TabForge") {
            failed++;
            std::fprintf(stderr, "state case %d failed (%lld ms, vendor '%s')\n", i, static_cast<long long>(elapsed), host.Vendor().c_str());
        }
    }
    // The over-limit blob must be refused before it reaches the plug-in.
    std::vector<uint8_t> tooLarge(static_cast<size_t>(kMaxStateBytes) + 1, 0x41);
    if (host.SetState(tooLarge)) { failed++; std::fprintf(stderr, "a state over the 16 MiB cap was accepted\n"); }
    host.Close();
    std::printf("tfvst32fuzz state: %d mutated blobs (seed %d), %d failed\n", count, seed, failed);
    return failed == 0 ? 0 : 1;
}

unsigned long long PrivateBytes() {
    PROCESS_MEMORY_COUNTERS_EX counters{};
    if (!GetProcessMemoryInfo(GetCurrentProcess(), reinterpret_cast<PROCESS_MEMORY_COUNTERS*>(&counters), sizeof(counters))) return 0;
    return static_cast<unsigned long long>(counters.PrivateUsage);
}

int RunLeak(const std::wstring& dllPath, int iterations) {
    const std::wstring dll = AbsolutePath(dllPath);
    std::string error;
    // Warm up the loader, CRT heaps and the plug-in's own one-time allocations before the baseline.
    for (int i = 0; i < 5; i++) {
        Vst2Host warm;
        if (!warm.Load(dll, "none", 48000, 256, error)) {
            std::fprintf(stderr, "tfvst32fuzz: leak warm-up load failed: %s\n", error.c_str());
            return 1;
        }
        std::vector<uint8_t> state;
        warm.GetState(state);
        warm.Close();
    }
    const unsigned long long beforeBytes = PrivateBytes();
    DWORD beforeHandles = 0;
    GetProcessHandleCount(GetCurrentProcess(), &beforeHandles);
    int failed = 0;
    for (int i = 0; i < iterations; i++) {
        Vst2Host host;
        if (!host.Load(dll, "none", 48000, 256, error)) {
            std::fprintf(stderr, "leak loop %d: load failed: %s\n", i, error.c_str());
            failed++;
            break;
        }
        std::vector<uint8_t> state;
        if (!host.GetState(state)) failed++;
        host.Close();
    }
    const unsigned long long afterBytes = PrivateBytes();
    DWORD afterHandles = 0;
    GetProcessHandleCount(GetCurrentProcess(), &afterHandles);
    const unsigned long long growth = afterBytes > beforeBytes ? afterBytes - beforeBytes : 0;
    const DWORD handleGrowth = afterHandles > beforeHandles ? afterHandles - beforeHandles : 0;
    const unsigned long long limit = 8ull * 1024 * 1024;
    const bool ok = failed == 0 && growth <= limit && handleGrowth <= 16;
    std::printf("tfvst32fuzz leak: %d load/unload cycles, private bytes +%llu (limit %llu), handles +%lu (limit 16)\n",
                iterations, growth, limit, static_cast<unsigned long>(handleGrowth));
    return ok ? 0 : 1;
}

}  // namespace

int wmain(int argc, wchar_t** argv) {
    if (argc < 2) {
        std::fprintf(stderr, "usage: " TFVST_HELPER_STEM "fuzz --frames [count] [seed]\n"
                             "       " TFVST_HELPER_STEM "fuzz --state <passthrough.dll> [count] [seed]\n"
                             "       " TFVST_HELPER_STEM "fuzz --leak <passthrough.dll> [iterations]\n");
        return 2;
    }
    const std::wstring mode = argv[1];
    if (mode == L"--frames") {
        const int count = argc > 2 ? _wtoi(argv[2]) : kDefaultCount;
        const int seed = argc > 3 ? _wtoi(argv[3]) : kDefaultSeed;
        return RunFrames(count > 0 ? count : kDefaultCount, seed);
    }
    if (mode == L"--state" && argc > 2) {
        const int count = argc > 3 ? _wtoi(argv[3]) : kDefaultCount;
        const int seed = argc > 4 ? _wtoi(argv[4]) : kDefaultSeed;
        return RunState(argv[2], count > 0 ? count : kDefaultCount, seed);
    }
    if (mode == L"--leak" && argc > 2) {
        const int iterations = argc > 3 ? _wtoi(argv[3]) : 200;
        return RunLeak(argv[2], iterations > 0 ? iterations : 200);
    }
    std::fprintf(stderr, "tfvst32fuzz: unknown or incomplete mode\n");
    return 2;
}
