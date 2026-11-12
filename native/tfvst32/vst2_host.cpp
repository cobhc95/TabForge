#include "vst2_host.h"

#include <algorithm>
#include "vst2_calls.h"
#include <cmath>
#include <cstddef>
#include <cstdio>
#include <cstring>
#include <mutex>
#include <string>

namespace tfvst32 {
namespace {
// VstEvents has an int32 count, then pointer-aligned VstIntPtr reserved and the event pointers. On x64 that means four
// padding bytes after the count, so the pointer array does not start at sizeof(int32_t) + sizeof(void*).
struct VstEventsHeader {
    int32_t numEvents;
    intptr_t reserved;
    VstEvent* events[1];   // the storage behind it holds kMaxEvents pointers
};
constexpr size_t kEventPointersOffset = offsetof(VstEventsHeader, events);

constexpr int kEffOpen = 0, kEffClose = 1, kEffSetProgram = 2, kEffGetProgram = 3,
    kEffGetParamLabel = 6, kEffGetParamDisplay = 7, kEffGetParamName = 8,
    kEffSetSampleRate = 10, kEffSetBlockSize = 11, kEffMainsChanged = 12, kEffEditClose = 15,
    kEffProcessEvents = 25,
    kEffGetEffectName = 45, kEffGetVendorString = 47, kEffGetProductString = 48,
    kEffGetTailSize = 52, kEffStartProcess = 71,
    kEffStopProcess = 72, kEffSetProcessPrecision = 77;
constexpr int kFlagHasEditor = 1, kFlagCanReplacing = 1 << 4, kFlagProgramChunks = 1 << 5,
    kFlagIsSynth = 1 << 8;
constexpr int kMaxChannels = 128;
constexpr int kParamText = 256;
constexpr int kVstMaxString = 64;   // VST2 vendor/product buffers are 64 bytes, not kParamText
constexpr int kMaxCanDoString = 64;
constexpr int kMaxTailSeconds = 30; // matches the offline renderer's 30 s plug-in tail cap

struct ReaperPaths {
    std::string resource;
    std::string ini;

    ReaperPaths() {
        wchar_t localAppData[MAX_PATH] = {};
        DWORD length = GetEnvironmentVariableW(L"LOCALAPPDATA", localAppData, MAX_PATH);
        std::wstring folder = length > 0 && length < MAX_PATH ? std::wstring(localAppData, length) : L".";
        folder += L"\\TabForge";
        CreateDirectoryW(folder.c_str(), nullptr);
        folder += L"\\PluginData";
        CreateDirectoryW(folder.c_str(), nullptr);
        char utf8[MAX_PATH * 3] = {};
        const int written = WideCharToMultiByte(CP_UTF8, 0, folder.c_str(), -1, utf8, sizeof(utf8), nullptr, nullptr);
        resource = written > 0 ? std::string(utf8) : ".";
        ini = resource + "\\reaper.ini";
    }
};

const ReaperPaths& Paths() {
    static ReaperPaths paths;
    return paths;
}

const char* GetResourcePathFn() { return Paths().resource.c_str(); }
const char* GetIniFileFn() { return Paths().ini.c_str(); }
intptr_t ResolveReaper(const char* name);

void* ResolveFn(const char* name) {
    if (name == nullptr) return nullptr;
    if (std::strcmp(name, "GetResourcePath") == 0) return reinterpret_cast<void*>(&GetResourcePathFn);
    if (std::strcmp(name, "get_ini_file") == 0) return reinterpret_cast<void*>(&GetIniFileFn);
    if (std::strcmp(name, "resolve_fn") == 0) return reinterpret_cast<void*>(&ResolveFn);
    return nullptr;
}

intptr_t ResolveReaper(const char* name) {
    return reinterpret_cast<intptr_t>(ResolveFn(name));
}

intptr_t WriteText(void* ptr, const char* text) {
    if (ptr == nullptr) return 0;
    std::strncpy(static_cast<char*>(ptr), text, kVstMaxString - 1);
    static_cast<char*>(ptr)[kVstMaxString - 1] = 0;
    return 1;
}

// The plug-in owns the canDo pointer; read it under SEH and never scan past a small cap.
bool SafeCanDo(const char* what, const char* expected) {
    if (what == nullptr) return false;
    __try {
        for (int i = 0; i < kMaxCanDoString; i++) {
            const char actual = what[i];
            if (actual != expected[i]) return false;
            if (actual == 0) return true;
        }
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return false;
    }
    return false;
}

// The plug-in's close sequence runs under SEH: a faulting plug-in must not take the helper down while it unloads.
// __try cannot live in a function with C++ objects that need unwinding, so this stays a small free function.
void CloseDispatchGuarded(AEffect* effect, bool closeEditor, bool stopProcess) {
    using DispatchProc = intptr_t (VST_CALLBACK*)(AEffect*, int32_t, int32_t, intptr_t, void*, float);
    __try {
        if (effect == nullptr || effect->dispatcher == 0) return;
        auto dispatch = reinterpret_cast<DispatchProc>(effect->dispatcher);
        if (closeEditor) dispatch(effect, kEffEditClose, 0, 0, nullptr, 0);
        if (stopProcess) {
            dispatch(effect, kEffStopProcess, 0, 0, nullptr, 0);
            dispatch(effect, kEffMainsChanged, 0, 0, nullptr, 0);
        }
        dispatch(effect, kEffClose, 0, 0, nullptr, 0);
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        // Abandon the rest of the close sequence; the caller still clears state and unloads the module.
    }
}

double FiniteOr(double value, double fallback) { return std::isfinite(value) ? value : fallback; }
}  // namespace

Vst2Host* Vst2Host::g_host = nullptr;

namespace {
intptr_t VST_CALLBACK HostCallbackEntry(AEffect* effect, int32_t opcode, int32_t index, intptr_t value, void* ptr, float opt) {
    (void)effect;
    return Vst2Host::g_host == nullptr ? 0 : Vst2Host::g_host->HostCallback(opcode, index, value, ptr, opt);
}
}  // namespace

intptr_t Vst2Host::HostCallback(int32_t opcode, int32_t index, intptr_t value, void* ptr, float opt) {
    switch (opcode) {
        case 1: return 2400;                                                   // audioMasterVersion
        case 0: case 43: case 44:                                              // automate / beginEdit / endEdit
            edited_.store(1);
            return 0;
        case 6: return 1;                                                      // wantMidi
        case 8: return 1;                                                      // processEvents (MIDI-out is not forwarded in v1)
        case 7: return reinterpret_cast<intptr_t>(&timeInfo_);                 // getTime
        case 13: return 1;                                                     // ioChanged
        case 14: return 0;                                                     // idle
        case 15:                                                               // sizeWindow
            pendingWidth_.store(index);
            pendingHeight_.store(static_cast<int>(value));
            resized_.store(1);
            return 1;
        case 16: return sampleRate_;                                           // getSampleRate
        case 17: return maxBlock_;                                             // getBlockSize
        case 18: case 19: return 0;                                            // input / output latency
        case 23:                                                               // currentProcessLevel
            if (offline_.load() != 0) return 4;
            return GetCurrentThreadId() == audioThread_.load() ? 2 : 1;
        case 30: return 0;                                                     // getDirectory (the helper sets its cwd)
        case 32: return WriteText(ptr, "TabForge");                            // getVendorString
        case 33: return WriteText(ptr, "TabForge Audio Engine");               // getProductString
        case 34: return 1;                                                     // getVendorVersion
        case 37: {                                                             // canDo
            const char* what = static_cast<const char*>(ptr);
            return (SafeCanDo(what, "sendVstEvents") || SafeCanDo(what, "sendVstMidiEvent") ||
                    SafeCanDo(what, "sizeWindow") || SafeCanDo(what, "sendVstTimeInfo") ||
                    SafeCanDo(what, "receiveVstEvents") || SafeCanDo(what, "receiveVstMidiEvent"))
                       ? 1 : 0;
        }
        case 42: return 1;                                                     // updateDisplay
        case 61: return 1;                                                     // host language: English
        case static_cast<int32_t>(0xDEADBEEF):                                 // Reaper-only lookup (ReaPlugs)
            return index == static_cast<int32_t>(0xDEADF00D)
                       ? ResolveReaper(static_cast<const char*>(ptr)) : 0;
        default: return 0;
    }
}

bool Vst2Host::Load(const std::wstring& path, const std::string& expectedSha256, int sampleRate, int maxBlock, std::string& error) {
    Close();
    sampleRate_ = std::clamp(sampleRate, kMinSampleRate, kMaxSampleRate);
    maxBlock_ = std::clamp(maxBlock, kMinBlock, kMaxBlock);
    refused_ = false;
    stateTooLarge_ = false;
    editorOpen_ = false;
    dispatchFaulted_.store(0, std::memory_order_release);

    HoldFailure failure = HoldFailure::None;
    HANDLE hold = HoldAndVerify(path, expectedSha256, failure, error);
    if (hold == INVALID_HANDLE_VALUE) {
        refused_ = failure == HoldFailure::Open || failure == HoldFailure::Hash || failure == HoldFailure::Mismatch;
        return false;
    }
    // Keep the read-only handle (writers denied) open until the module has been mapped, then release it.
    module_ = LoadLibraryExW(path.c_str(), nullptr,
                             LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
    DWORD loadError = GetLastError();
    if (module_ == nullptr && loadError == ERROR_MOD_NOT_FOUND) {
        // Support DLLs in a vendor folder on PATH: one retry in the standard order every other host uses (own folder first).
        module_ = LoadLibraryExW(path.c_str(), nullptr, LOAD_WITH_ALTERED_SEARCH_PATH);
        loadError = GetLastError();
    }
    CloseHandle(hold);
    if (module_ == nullptr) {
        error = "the plug-in could not be loaded (error " + std::to_string(loadError) +
                "). It may need a DLL that is missing, or it is not a 32-bit VST2 plug-in.";
        return false;
    }
    auto entry = reinterpret_cast<void*>(GetProcAddress(module_, "VSTPluginMain"));
    if (entry == nullptr) entry = reinterpret_cast<void*>(GetProcAddress(module_, "main"));
    if (entry == nullptr) {
        error = "not a VST2 plug-in (no VSTPluginMain export)";
        Close();   // unmap the refused module, never leave it loaded
        return false;
    }

    g_host = this;
    AEffect* effect = nullptr;
    if (!CreateEffectGuarded(entry, &HostCallbackEntry, effect) || !EffectMagicGuarded(effect)) {
        error = "the plug-in did not start (bad VST2 entry)";
        if (effect != nullptr) CloseDispatchGuarded(effect, false, false);   // the refused instance still closes under SEH
        g_host = nullptr;
        Close();   // unmap the module whose entry refused this host
        return false;
    }
    effect_ = effect;
    // processReplacing, or the accumulating process of a VST 2.0-era plug-in (Process clears the outputs before each call).
    const intptr_t process = (effect->flags & kFlagCanReplacing) != 0 && effect->processReplacing != 0
        ? effect->processReplacing : effect->process;
    if (process == 0) {
        error = "the plug-in does not support 32-bit float processing";
        Close();
        return false;
    }
    process_ = reinterpret_cast<void*>(process);
    Dispatch(kEffOpen, 0, 0, nullptr, 0);
    opened_ = true;
    instrument_ = (effect->flags & kFlagIsSynth) != 0;
    hasEditor_ = (effect->flags & kFlagHasEditor) != 0;
    latency_ = effect->initialDelay;

    const int declaredIn = effect->numInputs;
    const int declaredOut = effect->numOutputs;
    if (declaredIn < 0 || declaredIn > kMaxChannels || declaredOut < 0 || declaredOut > kMaxChannels) {
        error = "the plug-in declares " + std::to_string(declaredIn) + " inputs and " +
                std::to_string(declaredOut) + " outputs (at most " + std::to_string(kMaxChannels) + " are supported)";
        Close();
        return false;
    }
    const int inChannels = std::max(2, declaredIn);
    const int outChannels = std::max(2, declaredOut);
    inputBuffers_.resize(static_cast<size_t>(inChannels));
    outputBuffers_.resize(static_cast<size_t>(outChannels));
    inputs_.resize(static_cast<size_t>(inChannels));
    outputs_.resize(static_cast<size_t>(outChannels));
    for (int c = 0; c < inChannels; c++) {
        inputBuffers_[c] = static_cast<float*>(_aligned_malloc(sizeof(float) * maxBlock_, 32));
        if (inputBuffers_[c] == nullptr) { error = "not enough memory for the plug-in's inputs"; Close(); return false; }
        std::memset(inputBuffers_[c], 0, sizeof(float) * maxBlock_);
        inputs_[c] = inputBuffers_[c];
    }
    for (int c = 0; c < outChannels; c++) {
        outputBuffers_[c] = static_cast<float*>(_aligned_malloc(sizeof(float) * maxBlock_, 32));
        if (outputBuffers_[c] == nullptr) { error = "not enough memory for the plug-in's outputs"; Close(); return false; }
        std::memset(outputBuffers_[c], 0, sizeof(float) * maxBlock_);
        outputs_[c] = outputBuffers_[c];
    }
    const size_t pointerBytes = static_cast<size_t>(kMaxEvents) * sizeof(void*);
    eventBytes_.assign(kEventPointersOffset + pointerBytes +
                       static_cast<size_t>(kMaxEvents) * sizeof(VstMidiEvent), 0);
    auto** pointers = reinterpret_cast<VstEvent**>(eventBytes_.data() + kEventPointersOffset);
    auto* eventData = reinterpret_cast<VstMidiEvent*>(eventBytes_.data() + kEventPointersOffset + pointerBytes);
    for (int i = 0; i < kMaxEvents; i++) pointers[i] = reinterpret_cast<VstEvent*>(eventData + i);

    Dispatch(kEffSetSampleRate, 0, 0, nullptr, static_cast<float>(sampleRate_));
    Dispatch(kEffSetBlockSize, 0, maxBlock_, nullptr, 0);
    Dispatch(kEffSetProcessPrecision, 0, 0, nullptr, 0);   // 0 = 32-bit float
    Dispatch(kEffMainsChanged, 0, 1, nullptr, 0);
    Dispatch(kEffStartProcess, 0, 0, nullptr, 0);
    if (dispatchFaulted_.load(std::memory_order_acquire) != 0) {
        error = "the plug-in faulted while starting";
        Close();
        return false;
    }
    return true;
}

void Vst2Host::Close() {
    // Idempotent and used by every early-return path in Load(): always let the plug-in close, even when it was refused
    // before effOpen, then unload its module. A reconfigure reload therefore cannot leak the previous mapping.
    if (effect_ != nullptr) {
        const bool closeEditor = editorOpen_;
        const bool stopProcess = opened_;
        editorOpen_ = false;
        if (effect_->dispatcher != 0) CloseDispatchGuarded(effect_, closeEditor, stopProcess);
        effect_ = nullptr;
    }
    if (g_host == this) g_host = nullptr;
    process_ = nullptr;
    opened_ = false;
    FreeBuffers();
    const HMODULE module = module_;
    module_ = nullptr;
    if (module != nullptr) FreeLibrary(module);
}

void Vst2Host::FreeBuffers() {
    for (float* buffer : inputBuffers_) if (buffer != nullptr) _aligned_free(buffer);
    for (float* buffer : outputBuffers_) if (buffer != nullptr) _aligned_free(buffer);
    inputBuffers_.clear();
    outputBuffers_.clear();
    inputs_.clear();
    outputs_.clear();
    eventBytes_.clear();
}

intptr_t Vst2Host::Dispatch(int opcode, int index, intptr_t value, void* ptr, float opt) {
    intptr_t result = 0;
    if (!DispatchGuarded(effect_, opcode, index, value, ptr, opt, result)) {
        dispatchFaulted_.store(1, std::memory_order_release);
        return 0;
    }
    return result;
}

bool Vst2Host::Process(const BlockMidi* events, int eventCount, const float* inL, const float* inR,
                       float* outL, float* outR, int frames, const Transport& transport) {
    if (effect_ == nullptr || process_ == nullptr || frames <= 0) return false;
    frames = std::min(frames, maxBlock_);
    audioThread_.store(GetCurrentThreadId());

    // The audio thread never waits for the editor: when a plug-in editor call owns the plug-in, this block is skipped
    // (the caller turns false into silence) and the main thread's dispatch finishes without an audio deadline stall.
    std::unique_lock<std::mutex> lock(pluginMutex_, std::try_to_lock);
    if (!lock.owns_lock()) {
        audioThread_.store(0);
        return false;
    }

    const double tempo = std::clamp(FiniteOr(transport.tempo, 120.0), 1.0, 1000.0);
    const double ppq = FiniteOr(transport.ppq, 0.0);
    const double barStart = FiniteOr(transport.barStartPpq, 0.0);
    timeInfo_.sampleRate = sampleRate_;
    timeInfo_.samplePos = samplePos_;
    timeInfo_.tempo = tempo;
    timeInfo_.ppqPos = ppq;
    int flags = (transport.playing ? 2 : 0) | (1 << 9) | (1 << 10);
    if (transport.playing != wasPlaying_) { flags |= 1; wasPlaying_ = transport.playing; }
    if (transport.timeSigNumerator > 0) {
        timeInfo_.timeSigNumerator = std::clamp(transport.timeSigNumerator, 1, 64);
        timeInfo_.timeSigDenominator = std::clamp(transport.timeSigDenominator, 1, 64);
        timeInfo_.barStartPos = barStart;
        flags |= (1 << 11) | (1 << 13);
    } else {
        timeInfo_.timeSigNumerator = 4;
        timeInfo_.timeSigDenominator = 4;
        timeInfo_.barStartPos = 0;
    }
    timeInfo_.flags = flags;
    samplePos_ += frames;

    for (size_t c = 0; c < inputBuffers_.size(); c++) {
        float* destination = inputBuffers_[c];
        const float* source = c == 0 ? inL : c == 1 ? inR : nullptr;
        if (source != nullptr) std::memcpy(destination, source, sizeof(float) * static_cast<size_t>(frames));
        else std::memset(destination, 0, sizeof(float) * static_cast<size_t>(frames));
    }
    for (float* buffer : outputBuffers_) std::memset(buffer, 0, sizeof(float) * static_cast<size_t>(frames));   // the accumulating process adds into these

    const int count = events == nullptr ? 0 : std::clamp(eventCount, 0, kMaxEvents);
    void* eventBase = nullptr;
    if (count > 0) {
        eventBase = eventBytes_.data();
        *reinterpret_cast<int32_t*>(eventBase) = count;
        const size_t pointerBytes = static_cast<size_t>(kMaxEvents) * sizeof(void*);
        auto* data = reinterpret_cast<VstMidiEvent*>(static_cast<uint8_t*>(eventBase) + kEventPointersOffset + pointerBytes);
        for (int i = 0; i < count; i++) {
            VstMidiEvent& event = data[i];
            std::memset(&event, 0, sizeof(event));
            event.type = 1;
            event.byteSize = sizeof(VstMidiEvent);
            event.deltaFrames = std::clamp(events[i].frame, 0, std::max(0, frames - 1));
            event.flags = 1;  // realtime
            event.midiData[0] = events[i].status;
            event.midiData[1] = events[i].data1;
            event.midiData[2] = events[i].data2;
        }
    }

    const bool processed = ProcessGuarded(eventBase, count, frames);
    if (!processed) {
        // A plug-in fault inside effProcessEvents/processReplacing must leave a complete silent block, never a torn one.
        for (float* buffer : outputBuffers_) std::memset(buffer, 0, sizeof(float) * static_cast<size_t>(frames));
        if (outL != nullptr) std::memset(outL, 0, sizeof(float) * static_cast<size_t>(frames));
        if (outR != nullptr) std::memset(outR, 0, sizeof(float) * static_cast<size_t>(frames));
        audioThread_.store(0);
        return false;
    }
    if (outL != nullptr) std::memcpy(outL, outputBuffers_[0], sizeof(float) * static_cast<size_t>(frames));
    if (outR != nullptr) std::memcpy(outR, outputBuffers_[std::min<size_t>(1, outputBuffers_.size() - 1)],
                                     sizeof(float) * static_cast<size_t>(frames));
    audioThread_.store(0);
    return true;
}

bool Vst2Host::ProcessGuarded(void* events, int count, int frames) {
    __try {
        if (count > 0 && events != nullptr) Dispatch(kEffProcessEvents, 0, 0, events, 0);
        using ProcessProc = void (VST_CALLBACK*)(AEffect*, float**, float**, int32_t);
        reinterpret_cast<ProcessProc>(process_)(effect_, inputs_.data(), outputs_.data(), frames);
        return true;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return false;
    }
}

bool Vst2Host::SetOfflineMode(bool offline) {
    offline_.store(offline ? 1 : 0);
    if (effect_ == nullptr) return true;
    std::unique_lock<std::mutex> lock(pluginMutex_, std::try_to_lock);
    if (!lock.owns_lock()) return false;   // the main loop retries after the editor call finishes
    const DWORD previousThread = audioThread_.exchange(GetCurrentThreadId());
    Dispatch(kEffStopProcess, 0, 0, nullptr, 0);
    Dispatch(kEffMainsChanged, 0, 0, nullptr, 0);
    Dispatch(kEffMainsChanged, 0, 1, nullptr, 0);
    Dispatch(kEffStartProcess, 0, 0, nullptr, 0);
    audioThread_.store(previousThread);
    samplePos_ = 0.0;
    return true;
}

int Vst2Host::TailSamples() const {
    if (effect_ == nullptr) return 0;
    auto self = const_cast<Vst2Host*>(this);
    std::lock_guard<std::mutex> lock(self->pluginMutex_);
    const int tail = static_cast<int>(self->Dispatch(kEffGetTailSize, 0, 0, nullptr, 0));
    if (tail <= 1) return 0;
    const long long cap = static_cast<long long>(sampleRate_) * kMaxTailSeconds;
    return static_cast<int>(std::min<long long>(tail, cap));
}

int Vst2Host::ParameterCount() const {
    return effect_ == nullptr ? 0 : std::clamp(effect_->numParams, 0, 2048);
}

bool Vst2Host::UsesChunks() const { return effect_ != nullptr && (effect_->flags & kFlagProgramChunks) != 0; }
int Vst2Host::UniqueId() const { return effect_ == nullptr ? 0 : effect_->uniqueID; }
int Vst2Host::DeclaredInputs() const { return effect_ == nullptr ? 0 : std::clamp(effect_->numInputs, 0, kMaxChannels); }
int Vst2Host::DeclaredOutputs() const { return effect_ == nullptr ? 0 : std::clamp(effect_->numOutputs, 0, kMaxChannels); }

std::string Vst2Host::AskString(int opcode, int fallback) {
    if (effect_ == nullptr) return {};
    std::lock_guard<std::mutex> lock(pluginMutex_);
    char buffer[kParamText] = {};
    if (Dispatch(opcode, 0, 0, buffer, 0) == 0 && fallback >= 0) {
        std::memset(buffer, 0, sizeof(buffer));
        Dispatch(fallback, 0, 0, buffer, 0);
    }
    buffer[kParamText - 1] = 0;
    return std::string(buffer);
}

std::string Vst2Host::Vendor() { return AskString(kEffGetVendorString); }
std::string Vst2Host::Product() { return AskString(kEffGetProductString, kEffGetEffectName); }

std::vector<ParameterInfo> Vst2Host::Parameters() {
    std::vector<ParameterInfo> list;
    if (effect_ == nullptr || effect_->getParameter == 0) return list;
    std::lock_guard<std::mutex> lock(pluginMutex_);
    const int count = ParameterCount();
    list.reserve(static_cast<size_t>(count));
    for (int i = 0; i < count; i++) {
        char name[kParamText] = {}, display[kParamText] = {}, label[kParamText] = {};
        Dispatch(kEffGetParamName, i, 0, name, 0);
        Dispatch(kEffGetParamDisplay, i, 0, display, 0);
        Dispatch(kEffGetParamLabel, i, 0, label, 0);
        std::string shown(display);
        const std::string suffix(label);
        if (!suffix.empty() && !shown.empty() && shown.size() < suffix.size() + 1)
            shown += " " + suffix;
        else if (!suffix.empty() && !shown.empty() && shown.compare(shown.size() - suffix.size(), suffix.size(), suffix) != 0)
            shown += " " + suffix;
        ParameterInfo info;
        info.id = static_cast<uint32_t>(i);
        info.name = name[0] != 0 ? std::string(name) : "Parameter " + std::to_string(i + 1);
        info.display = shown;
        float value = 0.0f;
        if (!GetParameterGuarded(effect_, i, value)) dispatchFaulted_.store(1, std::memory_order_release);
        info.value = std::isfinite(value) ? std::clamp(static_cast<double>(value), 0.0, 1.0) : 0.0;
        list.push_back(std::move(info));
    }
    return list;
}

bool Vst2Host::SetParameter(uint32_t id, double value) {
    if (effect_ == nullptr || effect_->setParameter == 0 || id >= static_cast<uint32_t>(ParameterCount()) ||
        !std::isfinite(value)) return false;
    std::lock_guard<std::mutex> lock(pluginMutex_);
    if (!SetParameterGuarded(effect_, static_cast<int32_t>(id),
                             static_cast<float>(std::clamp(value, 0.0, 1.0)))) {
        dispatchFaulted_.store(1, std::memory_order_release);
        return false;
    }
    return true;
}

bool Vst2Host::SetProgram(int program) {
    if (effect_ == nullptr) return false;
    const int count = effect_->numPrograms;
    if (program < 0 || (count > 0 && program >= count)) return false;
    std::lock_guard<std::mutex> lock(pluginMutex_);
    Dispatch(kEffSetProgram, 0, program, nullptr, 0);   // the program number is the value argument
    return dispatchFaulted_.load(std::memory_order_relaxed) == 0;
}

int Vst2Host::CurrentProgram() {
    if (effect_ == nullptr) return -1;
    std::lock_guard<std::mutex> lock(pluginMutex_);
    return static_cast<int>(Dispatch(kEffGetProgram, 0, 0, nullptr, 0));
}

}  // namespace tfvst32
