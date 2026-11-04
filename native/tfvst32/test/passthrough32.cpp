// Original TabForge 32-bit VST2 test plug-in. Not shipped; built only for the native helper tests.
// It passes audio through at a gain (parameter 0, default unity), and a MIDI note-on makes it add a quiet tone so the
// test can prove the shared event area and effProcessEvents reached the plug-in.
#include <atomic>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <new>
#include <string>
#include <vector>

#include <windows.h>

#define TF_EXPORT extern "C" __declspec(dllexport)
#define TF_CALL __cdecl

namespace {
constexpr int32_t kMagic = 0x56737450;
constexpr int kCanReplacing = 1 << 4, kHasEditor = 1, kProgramChunks = 1 << 5;
constexpr float kTwoPi = 6.28318530717958647692f;
constexpr size_t kHugeStateBytes = 17u * 1024u * 1024u;

struct ERect { int16_t top, left, bottom, right; };

// Test-only switches: a marker file beside this DLL selects chunk state (64 bytes) or a 17 MiB chunk state,
// and another enables a plain child-window editor. The shipped helper never ships this test plug-in.
bool MarkerExists(const wchar_t* name) {
    HMODULE module = nullptr;
    if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                            reinterpret_cast<LPCWSTR>(&MarkerExists), &module)) return false;
    wchar_t path[MAX_PATH] = {};
    if (GetModuleFileNameW(module, path, MAX_PATH) == 0) return false;
    std::wstring file(path);
    const size_t slash = file.find_last_of(L"\\/");
    if (slash != std::wstring::npos) file.resize(slash + 1); else file.clear();
    file += name;
    return GetFileAttributesW(file.c_str()) != INVALID_FILE_ATTRIBUTES;
}

struct AEffect {
    int32_t magic;
    intptr_t dispatcher;
    intptr_t process;
    intptr_t setParameter;
    intptr_t getParameter;
    int32_t numPrograms;
    int32_t numParams;
    int32_t numInputs;
    int32_t numOutputs;
    int32_t flags;
    intptr_t resvd1;
    intptr_t resvd2;
    int32_t initialDelay;
    int32_t realQualities;
    int32_t offQualities;
    float ioRatio;
    void* object;
    void* user;
    int32_t uniqueID;
    int32_t version;
    intptr_t processReplacing;
    intptr_t processDoubleReplacing;
};

struct VstEvent { int32_t type, byteSize, deltaFrames, flags; };
struct VstMidiEvent {
    int32_t type, byteSize, deltaFrames, flags;
    int32_t noteLength, noteOffset;
    uint8_t midiData[4];
    uint8_t detune, noteOffVelocity, reserved1, reserved2;
};
struct VstEvents {
    int32_t numEvents;
    intptr_t reserved;
    VstEvent* events[2];
};

using HostCallback = intptr_t (TF_CALL*)(AEffect*, int32_t, int32_t, intptr_t, void*, float);

// Test-only fault injection: a plug-in call that faults must be caught by the helper's SEH guards.
intptr_t TF_CALL FaultDispatch(AEffect*, int32_t, int32_t, intptr_t, void*, float) {
    volatile int* nowhere = nullptr;
    *nowhere = 1;
    return 0;
}
float TF_CALL FaultGetParameter(AEffect*, int32_t) {
    volatile int* nowhere = nullptr;
    *nowhere = 1;
    return 0.0f;
}
void TF_CALL FaultSetParameter(AEffect*, int32_t, float) {
    volatile int* nowhere = nullptr;
    *nowhere = 1;
}

// Test-only: an unguarded fault inside a plug-in window procedure (DispatchMessage, outside the helper's SEH guards).
// The helper's crash handler must write the report and minidump, and the engine must lose only this helper.
LRESULT CALLBACK CrashEditorProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam) {
    if (message == WM_LBUTTONDOWN) {
        volatile int* nowhere = nullptr;
        *nowhere = 1;
    }
    return DefWindowProcW(window, message, wParam, lParam);
}

// Test-only: a plug-in-owned modal menu loop (TrackPopupMenu) on the helper main thread. It stands in for the real
// TDR preset popup so the test proves host commands are refused while that nested loop runs, without injecting input.
LRESULT CALLBACK ModalEditorProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam) {
    if (message == WM_APP + 1) {
        HMENU menu = CreatePopupMenu();
        if (menu != nullptr) {
            AppendMenuW(menu, MF_STRING, 1, L"Preset one");
            SetTimer(window, 1, 1500, nullptr);
            TrackPopupMenu(menu, TPM_RETURNCMD | TPM_NONOTIFY, 0, 0, 0, window, nullptr);
            KillTimer(window, 1);
            DestroyMenu(menu);
        }
        return 0;
    }
    if (message == WM_TIMER && wParam == 1) { EndMenu(); return 0; }
    return DefWindowProcW(window, message, wParam, lParam);
}

class Passthrough32 {
public:
    AEffect effect{};
    std::atomic<float> parameter{0.5f};   // 0.5 -> unity gain (0..1 maps to 0..2)
    float sampleRate = 44100.0f;
    int maxBlock = 512;
    float phase = 0.0f;
    float envelope = 0.0f;
    int lastEventCount = 0;
    int lastDelta0 = -1;
    int totalEvents = 0;
    bool chunkMode = false;
    bool hugeState = false;
    bool hasEditor = false;
    bool editorSentResize = false;
    bool badRect = false;
    bool faultProcess = false;   // passthrough32.fault: every processReplacing faults
    bool faultOnce = false;      // passthrough32.faultonce: the first processReplacing faults, then audio flows
    bool faultParams = false;    // passthrough32.faultparams: parameter get/set and name dispatches fault
    bool faulted = false;
    bool crashOnMessage = false; // passthrough32.crashwindow: an unguarded fault in the editor's own window procedure
    bool modalOnMessage = false; // passthrough32.modal: the editor opens a plug-in-owned TrackPopupMenu loop on WM_APP+1
    std::vector<uint8_t> chunk;
    HWND editorWindow = nullptr;
    ERect editorRect{0, 0, 200, 150};
    HostCallback callback = nullptr;
    static Passthrough32* instance;

    static intptr_t TF_CALL Dispatch(AEffect* effect, int32_t opcode, int32_t index, intptr_t value, void* ptr, float opt);
    static void TF_CALL ProcessReplacing(AEffect* effect, float** inputs, float** outputs, int32_t frames);
    static void TF_CALL SetParameter(AEffect* effect, int32_t index, float value);
    static float TF_CALL GetParameter(AEffect* effect, int32_t index);

    float Gain() const { return parameter.load(std::memory_order_relaxed) * 2.0f; }
};

Passthrough32* Passthrough32::instance = nullptr;

intptr_t TF_CALL Passthrough32::Dispatch(AEffect* effect, int32_t opcode, int32_t index, intptr_t value, void* ptr, float opt) {
    auto* self = static_cast<Passthrough32*>(effect->object);
    if (self->faultParams && (opcode == 6 || opcode == 7 || opcode == 8))
        return FaultDispatch(effect, opcode, index, value, ptr, opt);
    switch (opcode) {
        case 0: return 0;                                            // effOpen
        case 1:                                                      // effClose
            if (self == instance) instance = nullptr;
            delete self;
            return 0;
        case 6:                                                      // effGetParamLabel
            if (ptr) std::strcpy(static_cast<char*>(ptr), "dB");
            return 0;
        case 7: {                                                    // effGetParamDisplay
            if (ptr) {
                const float gain = self->Gain();
                const float db = gain > 0.0001f ? 20.0f * std::log10(gain) : -60.0f;
                std::snprintf(static_cast<char*>(ptr), 64, "%.1f", static_cast<double>(db));
            }
            return 0;
        }
        case 8:                                                      // effGetParamName
            if (ptr) std::strcpy(static_cast<char*>(ptr), index == 0 ? "Gain" : "");
            return 0;
        case 10: self->sampleRate = opt; return 0;                   // effSetSampleRate
        case 11: self->maxBlock = static_cast<int>(value); return 0; // effSetBlockSize
        case 12: return 0;                                           // effMainsChanged
        case 13:                                                     // effEditGetRect
            if (ptr != nullptr) {
                // passthrough32.badrect: a garbage pointer must fall back to 400x300 in the helper, not crash it.
                *static_cast<ERect**>(ptr) = self->badRect
                    ? reinterpret_cast<ERect*>(static_cast<uintptr_t>(0xDEADBEEF))
                    : &self->editorRect;
            }
            return 1;
        case 14: {                                                   // effEditOpen
            if (self->crashOnMessage || self->modalOnMessage) {
                WNDCLASSEXW cls{};
                cls.cbSize = sizeof(cls);
                cls.lpfnWndProc = self->modalOnMessage ? &ModalEditorProc : &CrashEditorProc;
                cls.hInstance = GetModuleHandleW(nullptr);
                cls.lpszClassName = self->modalOnMessage ? L"Passthrough32ModalEditor" : L"Passthrough32CrashEditor";
                RegisterClassExW(&cls);
                const wchar_t* title = self->modalOnMessage ? L"Passthrough32 modal editor" : L"Passthrough32 crash editor";
                self->editorWindow = CreateWindowExW(0, cls.lpszClassName, title, WS_CHILD | WS_VISIBLE,
                                                     0, 0, self->editorRect.right, self->editorRect.bottom,
                                                     static_cast<HWND>(ptr), nullptr, cls.hInstance, nullptr);
            } else {
                self->editorWindow = CreateWindowExW(0, L"STATIC", L"Passthrough32 editor", WS_CHILD | WS_VISIBLE,
                                                     0, 0, self->editorRect.right, self->editorRect.bottom,
                                                     static_cast<HWND>(ptr), nullptr, GetModuleHandleW(nullptr), nullptr);
            }
            return self->editorWindow == nullptr ? 0 : 1;
        }
        case 15:                                                     // effEditClose
            if (self->editorWindow != nullptr) DestroyWindow(self->editorWindow);
            self->editorWindow = nullptr;
            self->editorSentResize = false;
            return 0;
        case 19:                                                     // effEditIdle: ask for a new size once
            if (self->editorWindow != nullptr && self->callback != nullptr && !self->editorSentResize) {
                self->editorSentResize = true;
                self->callback(&self->effect, 15, 320, 240, nullptr, 0.0f);
            }
            return 0;
        case 23: {                                                   // effGetChunk
            if (!self->chunkMode) return 0;
            if (!self->chunk.empty()) {
                self->chunk[0] = static_cast<uint8_t>(self->parameter.load(std::memory_order_relaxed) * 255.0f);
            }
            if (ptr != nullptr) *static_cast<void**>(ptr) = self->chunk.data();
            return static_cast<intptr_t>(self->chunk.size());
        }
        case 24:                                                     // effSetChunk
            if (!self->chunkMode || ptr == nullptr || value <= 0) return 0;
            if (static_cast<size_t>(value) <= self->chunk.size() && !self->hugeState) {
                std::memcpy(self->chunk.data(), ptr, static_cast<size_t>(value));
                self->parameter.store(self->chunk[0] / 255.0f, std::memory_order_relaxed);
            }
            return 0;
        case 25: {                                                   // effProcessEvents
            auto* list = static_cast<VstEvents*>(ptr);
            if (list == nullptr) { self->lastEventCount = 0; return 0; }
            const int count = list->numEvents < 0 ? 0 : list->numEvents > 512 ? 512 : list->numEvents;
            self->lastEventCount = count;
            self->lastDelta0 = count > 0 ? list->events[0]->deltaFrames : -1;
            self->totalEvents += count;
            for (int i = 0; i < count; i++) {
                auto* event = reinterpret_cast<VstMidiEvent*>(list->events[i]);
                if (event == nullptr || event->type != 1) continue;
                const uint8_t status = event->midiData[0] & 0xF0;
                const uint8_t velocity = event->midiData[2];
                if (status == 0x90 && velocity > 0) { self->envelope = 1.0f; self->phase = 0.0f; }
                else if (status == 0x80 || (status == 0x90 && velocity == 0)) self->envelope = 0.0f;
            }
            return 0;
        }
        case 45: if (ptr) std::strcpy(static_cast<char*>(ptr), "Passthrough32"); return 0;
        case 47: if (ptr) std::strcpy(static_cast<char*>(ptr), "TabForge"); return 0;
        case 48: if (ptr) std::strcpy(static_cast<char*>(ptr), "Passthrough32"); return 0;
        case 52: return 0;                                           // effGetTailSize
        case 58: return 2400;                                        // effGetVstVersion
        case 71: case 72: case 77: return 0;                         // start/stop process, precision
        default: return 0;
    }
}

void TF_CALL Passthrough32::ProcessReplacing(AEffect* effect, float** inputs, float** outputs, int32_t frames) {
    auto* self = static_cast<Passthrough32*>(effect->object);
    if (self->faultProcess || (self->faultOnce && !self->faulted)) {
        self->faulted = true;
        // Test-only fault injection: the helper's SEH guard must survive this, complete a silent block and keep serving.
        volatile float* nowhere = nullptr;
        *nowhere = 1.0f;
        return;
    }
    const float gain = self->Gain();
    const float step = kTwoPi * 440.0f / (self->sampleRate > 1.0f ? self->sampleRate : 44100.0f);
    for (int32_t i = 0; i < frames; i++) {
        const float tone = 0.25f * self->envelope * std::sin(self->phase);
        self->phase += step;
        if (self->phase >= kTwoPi) self->phase -= kTwoPi;
        self->envelope *= 0.9995f;
        outputs[0][i] = inputs[0][i] * gain + tone;
        outputs[1][i] = inputs[1][i] * gain + tone;
    }
}

void TF_CALL Passthrough32::SetParameter(AEffect* effect, int32_t index, float value) {
    if (index != 0) return;
    auto* self = static_cast<Passthrough32*>(effect->object);
    if (!std::isfinite(value)) return;
    self->parameter.store(value < 0.0f ? 0.0f : value > 1.0f ? 1.0f : value, std::memory_order_relaxed);
}

float TF_CALL Passthrough32::GetParameter(AEffect* effect, int32_t index) {
    return index == 0 ? static_cast<Passthrough32*>(effect->object)->parameter.load(std::memory_order_relaxed) : 0.0f;
}
}  // namespace

TF_EXPORT AEffect* VSTPluginMain(HostCallback callback) {
    if (MarkerExists(L"passthrough32.faultentry")) {
        // Test-only: a broken entry that must be closed under SEH and unloaded, never crash the helper.
        static AEffect refused{};
        refused.magic = 0;
        refused.dispatcher = reinterpret_cast<intptr_t>(&FaultDispatch);
        return &refused;
    }
    auto* plugin = new (std::nothrow) Passthrough32();
    if (plugin == nullptr) return nullptr;
    plugin->effect.magic = kMagic;
    plugin->effect.dispatcher = reinterpret_cast<intptr_t>(&Passthrough32::Dispatch);
    plugin->effect.setParameter = reinterpret_cast<intptr_t>(&Passthrough32::SetParameter);
    plugin->effect.getParameter = reinterpret_cast<intptr_t>(&Passthrough32::GetParameter);
    plugin->effect.numPrograms = 1;
    plugin->effect.numParams = 1;
    plugin->effect.numInputs = 2;
    plugin->effect.numOutputs = 2;
    plugin->chunkMode = MarkerExists(L"passthrough32.chunk") || MarkerExists(L"passthrough32.huge");
    plugin->hugeState = MarkerExists(L"passthrough32.huge");
    plugin->badRect = MarkerExists(L"passthrough32.badrect");
    plugin->crashOnMessage = MarkerExists(L"passthrough32.crashwindow");
    plugin->modalOnMessage = MarkerExists(L"passthrough32.modal");
    plugin->hasEditor = MarkerExists(L"passthrough32.editor") || plugin->badRect || plugin->crashOnMessage || plugin->modalOnMessage;
    plugin->faultProcess = MarkerExists(L"passthrough32.fault");
    plugin->faultOnce = MarkerExists(L"passthrough32.faultonce") && !plugin->faultProcess;
    plugin->faultParams = MarkerExists(L"passthrough32.faultparams");
    if (plugin->faultParams) {
        plugin->effect.setParameter = reinterpret_cast<intptr_t>(&FaultSetParameter);
        plugin->effect.getParameter = reinterpret_cast<intptr_t>(&FaultGetParameter);
    }
    plugin->callback = callback;
    plugin->effect.flags = kCanReplacing | (plugin->chunkMode ? kProgramChunks : 0) | (plugin->hasEditor ? kHasEditor : 0);
    if (plugin->chunkMode) {
        plugin->chunk.assign(plugin->hugeState ? kHugeStateBytes : 64u, 0x5A);
        plugin->chunk[0] = 128;
    }
    plugin->effect.ioRatio = 1.0f;
    plugin->effect.object = plugin;
    plugin->effect.uniqueID = 0x50415353;   // 'PASS'
    plugin->effect.version = 1000;
    plugin->effect.processReplacing = reinterpret_cast<intptr_t>(&Passthrough32::ProcessReplacing);
    Passthrough32::instance = plugin;
    if (callback != nullptr) callback(&plugin->effect, 1, 0, 0, nullptr, 0.0f);
    return &plugin->effect;
}

TF_EXPORT int TfTestEventCount() { return Passthrough32::instance == nullptr ? -1 : Passthrough32::instance->lastEventCount; }
TF_EXPORT int TfTestLastDelta0() { return Passthrough32::instance == nullptr ? -1 : Passthrough32::instance->lastDelta0; }
TF_EXPORT int TfTestTotalEvents() { return Passthrough32::instance == nullptr ? -1 : Passthrough32::instance->totalEvents; }
TF_EXPORT int TfTestParameter() {
    return Passthrough32::instance == nullptr ? -1 : static_cast<int>(Passthrough32::instance->parameter.load() * 1000.0f);
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID) {
    (void)instance;
    (void)reason;
    return TRUE;
}
