#include "host_session.h"

#include <algorithm>
#include <atomic>
#include <cstdio>
#include <cstring>
#include <deque>
#include <thread>
#include <utility>
#include <vector>

#include "editor_window.h"
#include "host_runtime.h"
#include "crash_report.h"
#include "plugin_host.h"
#include "tfvst32_protocol.h"
#include "vst2_host.h"

namespace tfvst32 {
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

bool EngineAlive(HANDLE engine) {
    return engine != nullptr && WaitForSingleObject(engine, 0) == WAIT_TIMEOUT;
}

std::string WineVersion() {
    wchar_t text[128] = {};
    const DWORD length = GetEnvironmentVariableW(L"TABFORGE_WINE_VERSION", text, 128);
    return length > 0 && length < 128 ? Narrow(std::wstring(text, length)) : std::string();
}

std::vector<uint8_t> TokenBytes(const std::string& hex) {
    std::vector<uint8_t> token(kTokenBytes, 0);
    if (hex.size() != kTokenBytes * 2) return token;
    auto digit = [](char c) { return c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1; };
    for (size_t i = 0; i < kTokenBytes; i++) {
        const int high = digit(hex[i * 2]), low = digit(hex[i * 2 + 1]);
        if (high < 0 || low < 0) return std::vector<uint8_t>(kTokenBytes, 0);
        token[i] = static_cast<uint8_t>((high << 4) | low);
    }
    return token;
}

void SendFailure(ControlChannel& channel, const std::string& text) {
    Writer writer;
    writer.String(text);
    channel.Write(MsgFailed, writer.Data());
}

void SendReply(ControlChannel& channel, uint8_t type, uint32_t id, const Writer& payload) {
    Writer writer;
    writer.U32(id);
    writer.Data().insert(writer.Data().end(), payload.Data().begin(), payload.Data().end());
    channel.Write(type, writer.Data());
}

void SendFailedReply(ControlChannel& channel, uint32_t id, const std::string& text) {
    Writer payload;
    payload.String(text);
    SendReply(channel, MsgFailed, id, payload);
}

void SendEditorReply(ControlChannel& channel, uint32_t id, int width, int height) {
    Writer payload;
    payload.I32(width);
    payload.I32(height);
    SendReply(channel, MsgEditor, id, payload);
}

void AnswerCommand(ControlChannel& channel, uint8_t type, uint32_t id, Reader& reader, PluginHost& host, EditorWindow& editor) {
    switch (type) {
        case CmdGetState: {
            std::vector<uint8_t> state;
            if (!host.GetState(state)) {
                if (host.StateTooLarge()) SendFailedReply(channel, id, std::string(kStateTooLargePrefix) + ": the plug-in's state is larger than 16 MiB");
                else { Writer payload; payload.I32(0); SendReply(channel, MsgState, id, payload); }
                return;
            }
            Writer payload;
            payload.I32(static_cast<int32_t>(state.size()));
            payload.Data().insert(payload.Data().end(), state.begin(), state.end());
            SendReply(channel, MsgState, id, payload);
            return;
        }
        case CmdSetState: {
            const int32_t length = reader.I32();
            if (!reader.Ok() || length < 0) { SendFailedReply(channel, id, "Bad state length."); return; }
            if (length > kMaxStateBytes) { SendFailedReply(channel, id, std::string(kStateTooLargePrefix) + ": the state is larger than 16 MiB"); return; }
            const auto state = reader.Bytes(static_cast<size_t>(length));
            if (!reader.Ok()) { SendFailedReply(channel, id, "Truncated state."); return; }
            Writer payload;
            payload.I32(host.SetState(state) ? 1 : 0);
            SendReply(channel, MsgState, id, payload);
            return;
        }
        case CmdOpenEditor: {
            const uint64_t low = reader.U32();
            const uint64_t high = reader.U32();
            const bool docked = reader.Bool();
            if (!reader.Ok()) { SendFailedReply(channel, id, "Malformed editor request."); return; }
            if (host.Editor() == nullptr || !host.HasEditor()) { SendEditorReply(channel, id, 0, 0); return; }
            // USER handles are 32-bit-significant and may arrive sign-extended from the 64-bit engine.
            if (high != 0 && high != 0xFFFFFFFFull) { SendFailedReply(channel, id, "the editor owner window handle is not a 32-bit window handle"); return; }
            const auto owner = reinterpret_cast<HWND>(static_cast<uintptr_t>(low));
            std::string error;
            if (!editor.Open(*host.Editor(), owner, docked, host.Product(), error)) { SendFailedReply(channel, id, error); return; }
            SendEditorReply(channel, id, editor.Width(), editor.Height());
            editor.AnnounceSize();
            return;
        }
        case CmdCloseEditor:
            editor.Close(false);
            SendEditorReply(channel, id, 0, 0);
            return;
        case CmdDescribe: {
            Writer payload;
            if (host.Vst2() == nullptr) {
                // VST3: class name and role; class id/vendor stay best-effort (section 3.5 of the Linux plan).
                payload.U8(2);   // LoadedPluginInfo.Vst3
                payload.U32(0);
                payload.String("");
                payload.I32(-1);
                payload.I32(-1);
                payload.Bool(true);
                payload.String(host.Vendor());
                payload.String(host.Product());
            } else {
                payload.U8(1);   // LoadedPluginInfo.Vst2
                payload.U32(static_cast<uint32_t>(host.UniqueId()));
                payload.String("");
                payload.I32(host.DeclaredInputs());
                payload.I32(host.DeclaredOutputs());
                payload.Bool(host.UsesChunks());
                payload.String(host.Vendor());
                payload.String(host.Product());
            }
            SendReply(channel, MsgInfo, id, payload);
            return;
        }
        case CmdListParams: {
            Writer payload;
            const auto parameters = host.Parameters();
            payload.I32(static_cast<int32_t>(parameters.size()));
            for (const auto& parameter : parameters) {
                const bool stepped = parameter.steps > 0;
                payload.U32(parameter.id);
                payload.String(parameter.name);
                payload.String("");
                payload.Double(0.0);
                payload.Double(stepped ? static_cast<double>(parameter.steps) : 1.0);
                payload.Double(parameter.defaultValue);
                payload.Double(parameter.value);
                payload.Bool(stepped);
                payload.Bool(parameter.readOnly);
                payload.String(parameter.display);
            }
            SendReply(channel, MsgParams, id, payload);
            return;
        }
        case CmdSetParam: {
            const uint32_t parameter = reader.U32();
            const double value = reader.Double();
            const bool accepted = reader.Ok() && host.SetParameter(parameter, value);
            Writer payload;
            payload.I32(accepted ? 1 : 0);
            SendReply(channel, MsgParams, id, payload);
            return;
        }
        case CmdSetProgram: {
            const int program = reader.I32();
            Writer payload;
            payload.I32(reader.Ok() && host.SetProgram(program) ? 1 : 0);
            SendReply(channel, MsgParams, id, payload);
            return;
        }
        default:
            SendFailedReply(channel, id, "Unknown helper command " + std::to_string(type) + ".");
            return;
    }
}

bool SendHello(ControlChannel& channel, int maxBlock, int sampleRate, int slots, bool tcp, const std::string& tokenHex) {
    Writer hello;
    if (!tcp) {
        hello.U32(kHelloMagic);
        hello.U16(kProtocolVersion);
        hello.U16(kHeaderSize);
        hello.I32(maxBlock);
        hello.I32(sampleRate);
        hello.I32(slots);
        return channel.Write(MsgHello, hello.Data());
    }
    hello.U32(kTcpHelloMagic);
    hello.U16(kProtocolVersion);
    hello.U16(kHeaderSize);
    hello.I32(maxBlock);
    hello.I32(sampleRate);
    hello.I32(slots);
    hello.I32(kBridgeArch);
    hello.I32(0);   // flags: none at hello time; MsgLoaded reports instrument/editor
    hello.String(kHelperVersion);
    hello.String(WineVersion());
    const auto token = TokenBytes(tokenHex);
    hello.Data().insert(hello.Data().end(), token.begin(), token.end());
    return channel.Write(MsgHello, hello.Data());
}
}  // namespace

int RunHostSession(ControlChannel& channel, BridgeBlock& block, const std::wstring& format,
                   const std::wstring& pluginPath, const std::string& sha256, bool wantInstrument,
                   int sampleRate, int maxBlock, HANDLE engine, bool tcp, const std::string& tokenHex) {
    if (!SendHello(channel, maxBlock, sampleRate, block.Slots(), tcp, tokenHex)) return 3;
    SetCrashPluginPath(pluginPath);
    if (!FolderOf(pluginPath).empty()) SetCurrentDirectoryW(FolderOf(pluginPath).c_str());
    ComScope com;
    PluginHost host;
    std::string error;
    if (!host.Load(format, pluginPath, sha256, sampleRate, maxBlock, wantInstrument, error)) {
        SendFailure(channel, error);
        host.Close();
        return 4;
    }
    Writer loaded;
    loaded.Bool(host.IsInstrument());
    loaded.Bool(host.HasEditor());
    loaded.I32(host.LatencySamples());
    loaded.I32(host.TailSamples());
    if (!channel.Write(MsgLoaded, loaded.Data())) { host.Close(); return 4; }

    // One editor per helper; all plug-in editor calls run on this main thread. A hang here stops only control
    // commands (the engine times them out) and never the helper's audio thread or TabForge's UI.
    EditorWindow editor;
    const std::vector<uint8_t> noPayload;
    editor.SetCallbacks(
        [&channel, &noPayload] { channel.Write(MsgEditorClosed, noPayload); },
        [&channel](int width, int height) { Writer payload; payload.I32(width); payload.I32(height); channel.Write(MsgEditorSized, payload.Data()); });

    std::atomic<bool> quit{false};
    editor.SetNestedService([&channel, &quit] {
        for (int served = 0; served < 8 && !quit && channel.HasData(); served++) {
            uint8_t type = 0; std::vector<uint8_t> payload;
            if (!channel.Read(type, payload)) { quit = true; return; }
            if (type == CmdQuit) { quit = true; return; }
            Reader reader(payload.data(), payload.size()); const uint32_t requestId = reader.U32();
            if (reader.Ok()) SendFailedReply(channel, requestId, "The plug-in's own menu is open; try again when it closes.");
        }
    });
    auto engineGone = [&] { return engine != nullptr ? !EngineAlive(engine) : channel.Broken(); };
#ifdef TFVST32_TEST_HOOKS
    // Test-only hang hook, compiled only into the separate test copy (never the shipped Release exe).
    int stallAudioMs = 0;
    wchar_t stallText[16] = {};
    if (const DWORD stallLength = GetEnvironmentVariableW(L"TABFORGE_TFVST32_TEST_STALL_AUDIO_MS", stallText, 16);
        stallLength > 0 && stallLength < 16) {
        wchar_t* end = nullptr;
        const long long parsed = wcstoll(stallText, &end, 10);
        if (end != nullptr && *end == 0) stallAudioMs = static_cast<int>(std::clamp<long long>(parsed, 0, 60000));
    }
#endif
    std::thread audio([&] {
        // The file/TCP transport polls its flag and must wake within a block; MMCSS period scheduling delays that wake
        // by up to a period (measured 10-15 ms), so only the event-driven named-pipe path uses MMCSS.
        const HANDLE mmcss = BoostAudioThread(!tcp);
        int lastOffline = 0;
        while (!quit.load(std::memory_order_acquire)) {
            int generation = 0;
            if (!block.TakeRequest(100, generation)) {
                if (engineGone()) quit.store(true, std::memory_order_release);
                continue;
            }
            const int frames = std::clamp(block.Header()->frames, 0, maxBlock);
#ifdef TFVST32_TEST_HOOKS
            if (stallAudioMs > 0 && frames > 0) Sleep(static_cast<DWORD>(stallAudioMs));   // test copy only
#endif
            const int offline = block.Header()->offline != 0 ? 1 : 0;
            if (offline != lastOffline && host.SetOfflineMode(offline != 0)) lastOffline = offline;
            const int count = std::clamp(block.Header()->eventCount, 0, kMaxEvents);
            Transport transport;
            transport.tempo = block.Header()->tempo;
            transport.ppq = block.Header()->ppq;
            transport.barStartPpq = block.Header()->barStartPpq;
            transport.playing = block.Header()->playing != 0;
            transport.timeSigNumerator = block.Header()->timeSigNumerator;
            transport.timeSigDenominator = block.Header()->timeSigDenominator;
            float* outL = block.Channel(kChannelOutL, generation);
            float* outR = block.Channel(kChannelOutR, generation);
            const bool processed = host.Process(block.Events(), count,
                         block.Channel(kChannelInL, generation), block.Channel(kChannelInR, generation),
                         outL, outR, frames, transport);
            if (!processed && frames > 0) {
                std::memset(outL, 0, sizeof(float) * static_cast<size_t>(frames));
                std::memset(outR, 0, sizeof(float) * static_cast<size_t>(frames));
            }
            block.Complete(generation);
        }
        ReleaseAudioThread(mmcss);
    });

    // Main-thread command queue: frames are read here (never on the audio thread) and handled one at a time;
    // between them the Win32 message pump delivers the 33 ms editor timer.
    std::deque<std::pair<uint8_t, std::vector<uint8_t>>> commands;
    DWORD lastEdited = GetTickCount();
    while (!quit.load(std::memory_order_acquire)) {
        MSG message;
        while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) {
            if (message.message == WM_QUIT) { quit.store(true, std::memory_order_release); break; }
            TranslateMessage(&message);
            editor.BeginDispatch();
            DispatchMessageW(&message);
            editor.EndDispatch();
        }
        if (quit.load(std::memory_order_acquire)) break;
        if (engineGone()) { quit.store(true, std::memory_order_release); break; }
        const DWORD now = GetTickCount();
        if (now - lastEdited >= 33) {
            lastEdited = now;
            if (host.TakeEdited()) channel.Write(MsgEdited, noPayload);
        }
        if (channel.HasData()) {
            uint8_t type = 0;
            std::vector<uint8_t> payload;
            if (!channel.Read(type, payload)) { quit.store(true, std::memory_order_release); break; }
            commands.emplace_back(type, std::move(payload));
        }
        while (!commands.empty() && !quit.load(std::memory_order_acquire)) {
            auto command = std::move(commands.front());
            commands.pop_front();
            if (command.first == CmdQuit) { quit.store(true, std::memory_order_release); break; }
            Reader reader(command.second.data(), command.second.size());
            const uint32_t requestId = reader.U32();
            if (!reader.Ok()) { SendFailedReply(channel, requestId, "Malformed request."); continue; }
            AnswerCommand(channel, command.first, requestId, reader, host, editor);
        }
        if (commands.empty() && !channel.HasData()) {
            if (channel.Broken()) { quit.store(true, std::memory_order_release); break; }
            Sleep(5);
        }
    }
    editor.Close(false);
    editor.SetCallbacks({}, {});
    quit.store(true, std::memory_order_release);
    if (audio.joinable()) audio.join();
    host.Close();
    return 0;   // ComScope uninitialises COM/OLE on every path
}
}  // namespace tfvst32
