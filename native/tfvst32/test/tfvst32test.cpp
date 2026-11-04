// Native stage-4 self-test: loads passthrough32.dll through the same Vst2Host the helper uses, then exercises the
// chunk/huge state, editor (sizeWindow, bad ERect, WM_CLOSE during processing), SEH process fault and pipe framing paths.
#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <string>
#include <thread>
#include <vector>

#include <windows.h>
#include <objbase.h>

#include "editor_window.h"
#include "pipe_channel.h"
#include "vst2_host.h"

using namespace tfvst32;

namespace {
int Fail(const char* what) {
    std::fprintf(stderr, "tfvst32test failed: %s\n", what);
    return 1;
}

bool Check(bool condition, const char* what) {
    if (!condition) std::fprintf(stderr, "tfvst32test check failed: %s\n", what);
    return condition;
}

int ExportInt(HMODULE module, const char* name) {
    using Proc = int (*)();
    auto proc = reinterpret_cast<Proc>(GetProcAddress(module, name));
    return proc == nullptr ? -9999 : proc();
}

std::wstring FolderOf(const std::wstring& path) {
    const size_t slash = path.find_last_of(L"\\/");
    return slash == std::wstring::npos ? std::wstring() : path.substr(0, slash);
}

struct Variant {
    std::wstring dll;
    std::vector<std::wstring> files;
};

Variant MakeVariant(const std::wstring& source, std::initializer_list<const wchar_t*> markers) {
    wchar_t temp[MAX_PATH] = {};
    GetTempPathW(MAX_PATH, temp);
    wchar_t folder[MAX_PATH] = {};
    static std::atomic<int> counter{0};
    std::swprintf(folder, MAX_PATH, L"%stfvst32test-%lu-%llu-%d", temp, GetCurrentProcessId(),
                  static_cast<unsigned long long>(GetTickCount64()), counter.fetch_add(1));
    CreateDirectoryW(folder, nullptr);
    Variant variant;
    variant.dll = std::wstring(folder) + L"\\passthrough32.dll";
    CopyFileW(source.c_str(), variant.dll.c_str(), FALSE);
    variant.files.push_back(variant.dll);
    for (const wchar_t* marker : markers) {
        const std::wstring file = std::wstring(folder) + L"\\" + marker;
        HANDLE handle = CreateFileW(file.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (handle != INVALID_HANDLE_VALUE) CloseHandle(handle);
        variant.files.push_back(file);
    }
    return variant;
}

void DeleteVariant(const Variant& variant) {
    for (const auto& file : variant.files) DeleteFileW(file.c_str());
    RemoveDirectoryW(FolderOf(variant.dll).c_str());
}

bool LoadHost(Vst2Host& host, const std::wstring& path, std::string& error) {
    return host.Load(path, "none", 48000, 256, error);
}

bool TestBasic(const std::wstring& path, const std::string& sha) {
    Vst2Host host;
    std::string error;
    if (!host.Load(path, sha, 48000, 256, error)) { std::fprintf(stderr, "load: %s\n", error.c_str()); return false; }
    bool ok = true;
    ok &= Check(host.IsInstrument() == false, "expected an effect");
    ok &= Check(host.HasEditor() == false, "the basic test plug-in has no editor");
    ok &= Check(host.LatencySamples() == 0, "latency must be 0");
    ok &= Check(host.ParameterCount() == 1, "expected one parameter");
    auto parameters = host.Parameters();
    ok &= Check(parameters.size() == 1 && parameters[0].name == "Gain", "parameter name");
    ok &= Check(std::fabs(parameters[0].value - 0.5) <= 0.01, "default parameter");

    constexpr int frames = 256;
    std::vector<float> inL(frames), inR(frames), outL(frames), outR(frames);
    for (int block = 0; block < 8; block++) {
        for (int i = 0; i < frames; i++) {
            inL[i] = 0.25f * std::sin(static_cast<float>(block * frames + i) * 0.13f);
            inR[i] = inL[i];
        }
        Transport transport;
        if (!host.Process(nullptr, 0, inL.data(), inR.data(), outL.data(), outR.data(), frames, transport)) {
            ok = false;
            break;
        }
        for (int i = 0; i < frames; i++) {
            if (!std::isfinite(outL[i]) || !std::isfinite(outR[i])) { ok = false; break; }
            if (std::fabs(outL[i] - inL[i]) > 1.0e-5f) { ok = false; break; }
        }
    }
    ok &= Check(ok, "unity pass-through");

    BlockMidi note{10, 0x90, 60, 100, 0};
    for (int block = 0; block < 8; block++) {
        std::fill(inL.begin(), inL.end(), 0.0f);
        std::fill(inR.begin(), inR.end(), 0.0f);
        Transport transport;
        if (!host.Process(block == 0 ? &note : nullptr, block == 0 ? 1 : 0,
                          inL.data(), inR.data(), outL.data(), outR.data(), frames, transport)) {
            ok = false;
            break;
        }
        if (block == 0) {
            ok &= Check(ExportInt(static_cast<HMODULE>(host.Module()), "TfTestEventCount") == 1, "event count");
            ok &= Check(ExportInt(static_cast<HMODULE>(host.Module()), "TfTestLastDelta0") == 10, "event delta");
        }
    }
    float peak = 0.0f;
    for (float value : outL) peak = std::max(peak, std::fabs(value));
    ok &= Check(peak >= 0.001f, "note-on produced no audio");
    ok &= Check(ExportInt(static_cast<HMODULE>(host.Module()), "TfTestTotalEvents") == 1, "total events");

    ok &= Check(host.SetParameter(0, 0.8), "set parameter");
    std::vector<uint8_t> state;
    ok &= Check(host.GetState(state) && state.size() == 12, "parameter-list state");
    ok &= Check(host.SetParameter(0, 0.2), "second set parameter");
    ok &= Check(host.SetState(state), "set state");
    parameters = host.Parameters();
    ok &= Check(parameters.size() == 1 && std::fabs(parameters[0].value - 0.8) <= 0.01, "state round trip");
    host.Close();
    return ok;
}

bool TestChunkState(const std::wstring& source) {
    Variant variant = MakeVariant(source, {L"passthrough32.chunk"});
    Vst2Host host;
    std::string error;
    bool ok = Check(LoadHost(host, variant.dll, error), "chunk load");
    if (ok) {
        ok &= Check(host.UsesChunks(), "chunk marker must enable chunks");
        std::vector<uint8_t> initial;
        ok &= Check(host.GetState(initial) && initial.size() == 5 + 64, "chunk size");
        ok &= Check(initial.size() >= 5 && std::memcmp(initial.data(), "TF32", 4) == 0, "chunk envelope");
        ok &= Check(host.SetParameter(0, 0.8), "chunk set parameter");
        std::vector<uint8_t> changed;
        ok &= Check(host.GetState(changed) && changed.size() == initial.size(), "changed chunk");
        ok &= Check(changed.size() > 5 && changed[5] != initial[5], "chunk carries the parameter");
        ok &= Check(host.SetParameter(0, 0.2), "chunk reset parameter");
        ok &= Check(host.SetState(changed), "chunk set state");
        auto parameters = host.Parameters();
        ok &= Check(parameters.size() == 1 && std::fabs(parameters[0].value - 0.8) <= 0.01, "chunk state round trip");
        ok &= Check(host.SetState(std::vector<uint8_t>{1, 2, 3}), "short chunk must be accepted");
        std::vector<uint8_t> after;
        ok &= Check(host.GetState(after) && after.size() == initial.size(), "helper alive after a short chunk");
    }
    host.Close();
    DeleteVariant(variant);
    return ok;
}

bool TestHugeState(const std::wstring& source) {
    Variant variant = MakeVariant(source, {L"passthrough32.huge"});
    Vst2Host host;
    std::string error;
    bool ok = Check(LoadHost(host, variant.dll, error), "huge-state load");
    if (ok) {
        std::vector<uint8_t> state;
        ok &= Check(!host.GetState(state), "a 17 MiB chunk must be refused");
        ok &= Check(host.StateTooLarge(), "the refusal must be reported as too large");
        ok &= Check(host.Vendor() == "TabForge" && host.Product() == "Passthrough32", "helper alive after a huge state");
    }
    host.Close();
    DeleteVariant(variant);
    return ok;
}

bool TestProcessFault(const std::wstring& source) {
    Variant variant = MakeVariant(source, {L"passthrough32.faultonce"});
    Vst2Host host;
    std::string error;
    bool ok = Check(LoadHost(host, variant.dll, error), "fault-once load");
    if (ok) {
        constexpr int frames = 256;
        std::vector<float> inL(frames, 0.25f), inR(frames, 0.25f), outL(frames, 1.0f), outR(frames, 1.0f);
        Transport transport;
        ok &= Check(!host.Process(nullptr, 0, inL.data(), inR.data(), outL.data(), outR.data(), frames, transport),
                    "the faulting process must return false");
        bool silent = true;
        for (int i = 0; i < frames; i++) silent &= outL[i] == 0.0f && outR[i] == 0.0f;
        ok &= Check(silent, "a faulting process must leave silence");
        ok &= Check(host.Process(nullptr, 0, inL.data(), inR.data(), outL.data(), outR.data(), frames, transport),
                    "the next process must succeed");
        ok &= Check(std::fabs(outL[0] - 0.25f) <= 1.0e-5f, "audio must flow after the fault");
        ok &= Check(host.Vendor() == "TabForge", "helper alive after the process fault");
    }
    host.Close();
    DeleteVariant(variant);
    return ok;
}

bool TestRefusedEntry(const std::wstring& source) {
    Variant variant = MakeVariant(source, {L"passthrough32.faultentry"});
    Vst2Host host;
    std::string error;
    bool ok = Check(!LoadHost(host, variant.dll, error), "a refused entry must fail the load");
    ok &= Check(!error.empty(), "a refused entry must give a readable error");
    ok &= Check(LoadHost(host, source, error), "a normal plug-in must still load after a refused entry");
    if (ok) {
        ok &= Check(host.Vendor() == "TabForge", "helper alive after a refused entry");
        host.Close();
    }
    DeleteVariant(variant);
    return ok;
}

bool TestParameterFaults(const std::wstring& source) {
    Variant variant = MakeVariant(source, {L"passthrough32.faultparams"});
    Vst2Host host;
    std::string error;
    bool ok = Check(LoadHost(host, variant.dll, error), "fault-params load");
    if (ok) {
        auto parameters = host.Parameters();
        ok &= Check(parameters.size() == 1, "parameter count survives a faulting get");
        ok &= Check(parameters.size() == 1 && parameters[0].name == "Parameter 1", "fallback parameter name");
        ok &= Check(parameters.size() == 1 && parameters[0].value == 0.0, "a faulting get must read as zero");
        ok &= Check(!host.SetParameter(0, 0.5), "a faulting set must return false");
        std::vector<uint8_t> state;
        ok &= Check(!host.GetState(state), "a faulting get must fail state safely");
        ok &= Check(host.Vendor() == "TabForge", "helper alive after parameter faults");
    }
    host.Close();
    DeleteVariant(variant);
    return ok;
}

bool TestBadRect(const std::wstring& source) {
    Variant variant = MakeVariant(source, {L"passthrough32.badrect"});
    Vst2Host host;
    std::string error;
    bool ok = Check(LoadHost(host, variant.dll, error), "bad-rect load");
    if (ok) {
        ok &= Check(host.HasEditor(), "bad-rect mode must report an editor");
        int width = 0, height = 0;
        ok &= Check(host.EditorSize(width, height), "editor size");
        ok &= Check(width == 400 && height == 300, "a garbage ERect pointer must fall back to 400x300");
        EditorWindow editor;
        std::string openError;
        ok &= Check(!editor.Open(host, reinterpret_cast<HWND>(static_cast<uintptr_t>(0x1234)), false, "", openError),
                    "an invalid owner must refuse the editor on Windows");
        ok &= Check(!openError.empty(), "an invalid owner must give a readable error");
    }
    host.Close();
    DeleteVariant(variant);
    return ok;
}

bool TestEditorDuringProcessing(const std::wstring& source) {
    Variant variant = MakeVariant(source, {L"passthrough32.editor"});
    Vst2Host host;
    std::string error;
    bool ok = Check(LoadHost(host, variant.dll, error), "editor load");
    HWND owner = nullptr;
    if (ok) {
        owner = CreateWindowExW(0, L"STATIC", L"tfvst32test owner", WS_OVERLAPPEDWINDOW, 0, 0, 480, 320,
                                nullptr, nullptr, nullptr, nullptr);
        ok &= Check(owner != nullptr, "owner window");
    }
    if (ok) {
        EditorWindow editor;
        std::atomic<bool> userClosed{false};
        editor.SetCallbacks([&] { userClosed.store(true); }, std::function<void(int, int)>());
        ok &= Check(editor.Open(host, owner, false, "", error), "open editor");
        ok &= Check(editor.Width() == 150 && editor.Height() == 200, "initial editor rect");

        std::atomic<bool> stop{false};
        std::atomic<int> processed{0};
        std::thread audio([&] {
            float inL[256] = {}, inR[256] = {}, outL[256] = {}, outR[256] = {};
            for (int i = 0; i < 256; i++) inL[i] = inR[i] = 0.25f;
            while (!stop.load(std::memory_order_acquire)) {
                Transport transport;
                if (host.Process(nullptr, 0, inL, inR, outL, outR, 256, transport)) {
                    processed.fetch_add(1);
                    Sleep(1);
                }
            }
        });

        host.EditorIdle();   // the test plug-in asks for 320x240 once
        int width = 0, height = 0;
        ok &= Check(host.TakeEditorResize(width, height) && width == 320 && height == 240, "sizeWindow");
        // The check below is about audio while the editor is open, so a loaded machine must not close the window
        // before the audio thread ever got its first block: wait for it instead of racing the close against it.
        const ULONGLONG audioDeadline = GetTickCount64() + 3000;
        while (processed.load(std::memory_order_acquire) == 0 && GetTickCount64() < audioDeadline) Sleep(1);
        const HWND popup = FindWindowW(EditorWindow::ClassName(), L"TabForge Plug-in Editor");
        ok &= Check(popup != nullptr, "editor popup");
        if (popup != nullptr) {
            PostMessageW(popup, WM_CLOSE, 0, 0);
            const ULONGLONG deadline = GetTickCount64() + 3000;
            bool closed = false;
            while (GetTickCount64() < deadline) {
                MSG message;
                while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) {
                    TranslateMessage(&message);
                    DispatchMessageW(&message);
                }
                if (FindWindowW(EditorWindow::ClassName(), L"TabForge Plug-in Editor") == nullptr) { closed = true; break; }
                Sleep(1);
            }
            ok &= Check(closed, "WM_CLOSE during processing must close the editor");
            ok &= Check(userClosed.load(), "WM_CLOSE must report a user close");
        }
        stop.store(true, std::memory_order_release);
        audio.join();
        ok &= Check(processed.load() > 0, "audio must keep running while the editor closes");
        float inL[256] = {}, inR[256] = {}, outL[256] = {}, outR[256] = {};
        inL[0] = inR[0] = 0.5f;
        Transport transport;
        ok &= Check(host.Process(nullptr, 0, inL, inR, outL, outR, 256, transport), "process after the editor close");
        ok &= Check(std::fabs(outL[0] - 0.5f) <= 1.0e-5f, "audio after the editor close");
        editor.Close(false);
    }
    if (owner != nullptr) DestroyWindow(owner);
    host.Close();
    DeleteVariant(variant);
    return ok;
}

bool TestPipeFrames() {
    wchar_t id[33] = {};
    std::swprintf(id, 33, L"%08lx%08lx%016llx", GetCurrentProcessId(), GetCurrentThreadId(),
                  static_cast<unsigned long long>(GetTickCount64()));
    const std::wstring name = L"\\\\.\\pipe\\TabForge.PluginHost." + std::wstring(id);
    HANDLE server = CreateNamedPipeW(name.c_str(), PIPE_ACCESS_DUPLEX,
                                      PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT, 1, 4096, 4096, 0, nullptr);
    bool ok = Check(server != INVALID_HANDLE_VALUE, "pipe server");
    HANDLE done = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (ok) {
        std::thread serverThread([&] {
            ConnectNamedPipe(server, nullptr);   // ERROR_PIPE_CONNECTED when the client won the race
            const uint8_t two[2] = {0x10, 0x00};   // half a length header, then the sender stalls
            DWORD written = 0;
            WriteFile(server, two, sizeof(two), &written, nullptr);
            WaitForSingleObject(done, 8000);
            DisconnectNamedPipe(server);
        });
        PipeChannel pipe;
        std::string error;
        ok &= Check(pipe.Connect(id, 3000, error), "pipe connect");
        const auto started = std::chrono::steady_clock::now();
        uint8_t type = 0;
        std::vector<uint8_t> payload;
        const bool read = pipe.Read(type, payload);
        const auto elapsed = std::chrono::duration_cast<std::chrono::milliseconds>(
            std::chrono::steady_clock::now() - started).count();
        ok &= Check(!read, "a truncated frame must not be accepted");
        ok &= Check(pipe.Broken(), "a truncated frame must mark the pipe broken");
        ok &= Check(elapsed < 5000, "a truncated frame must time out, not block forever");
        SetEvent(done);
        serverThread.join();
    }
    if (done != nullptr) CloseHandle(done);
    if (server != INVALID_HANDLE_VALUE) CloseHandle(server);
    return ok;
}
}  // namespace

int wmain(int argc, wchar_t** argv) {
    if (argc < 2) {
        std::fprintf(stderr, "usage: " TFVST_HELPER_STEM "test <passthrough.dll> [sha256]\n");
        return 2;
    }
    // Use an absolute path: a relative plug-in path can fail with ERROR_INVALID_PARAMETER after a refused entry on
    // some loaders, and the helper is always launched with an absolute path in production.
    std::wstring path = argv[1];
    wchar_t full[32768] = {};
    const DWORD fullLength = GetFullPathNameW(path.c_str(), 32768, full, nullptr);
    if (fullLength > 0 && fullLength < 32768) path.assign(full, fullLength);
    std::string sha = "none";
    if (argc > 2) {
        char buffer[256] = {};
        WideCharToMultiByte(CP_UTF8, 0, argv[2], -1, buffer, sizeof(buffer), nullptr, nullptr);
        sha = buffer;
    }
    if (!FolderOf(path).empty()) SetCurrentDirectoryW(FolderOf(path).c_str());
    CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);

    bool ok = TestBasic(path, sha);
    ok &= TestChunkState(path);
    ok &= TestHugeState(path);
    ok &= TestProcessFault(path);
    ok &= TestRefusedEntry(path);
    ok &= TestParameterFaults(path);
    ok &= TestBadRect(path);
    ok &= TestEditorDuringProcessing(path);
    ok &= TestPipeFrames();

    CoUninitialize();
    if (!ok) return Fail("one or more checks failed");
    std::printf(TFVST_HELPER_STEM "test: ok\n");
    return 0;
}
