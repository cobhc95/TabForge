#include "vst3_host.h"

#include <algorithm>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <filesystem>
#include <new>
#include <string>
#include <vector>

#include "hash_hold.h"

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

std::wstring HelperFolder() {
    wchar_t path[32768] = {};
    const DWORD length = GetModuleFileNameW(nullptr, path, 32768);
    const std::wstring exe(path, length);
    const size_t slash = exe.find_last_of(L"\\/");
    return slash == std::wstring::npos ? std::wstring() : exe.substr(0, slash);
}

bool FileExists(const std::wstring& path) {
    const DWORD attributes = GetFileAttributesW(path.c_str());
    return attributes != INVALID_FILE_ATTRIBUTES && (attributes & FILE_ATTRIBUTE_DIRECTORY) == 0;
}

bool FindBinaryIn(const std::filesystem::path& folder, const std::wstring& stem, std::wstring& binary) {
    if (!std::filesystem::is_directory(folder)) return false;
    const auto named = folder / stem;
    if (FileExists(named.wstring())) { binary = named.wstring(); return true; }
    std::error_code error;
    for (const auto& entry : std::filesystem::directory_iterator(folder, error)) {
        if (!entry.is_regular_file(error)) continue;
        if (entry.path().extension() == L".vst3") { binary = entry.path().wstring(); return true; }
    }
    return false;
}

// Plain values as the managed generic panel shows them (Vst3ParameterMap in C#).
double ToPlain(double normalized, int steps) {
    const double clamped = std::clamp(normalized, 0.0, 1.0);
    return steps > 0 ? std::round(clamped * steps) : clamped;
}

double ToNormalized(double plain, int steps) {
    if (steps > 0) return std::clamp(std::round(plain), 0.0, static_cast<double>(steps)) / steps;
    return std::clamp(plain, 0.0, 1.0);
}

// A VST3 editor call runs third-party code on the helper main thread. Like the VST2 editor path, a fault must not
// terminate the helper (and with it every other plug-in in that process): catch it, remember the fault, and stop
// calling the editor. __try lives in small free functions with no C++ objects that need unwinding.
int32_t EditorIntGuarded(int32_t (*fn)(void*), void* plugin, bool& faulted) {
    __try { return fn(plugin); }
    __except (EXCEPTION_EXECUTE_HANDLER) { faulted = true; return 0; }
}

void EditorVoidGuarded(void (*fn)(void*), void* plugin, bool& faulted) {
    __try { fn(plugin); }
    __except (EXCEPTION_EXECUTE_HANDLER) { faulted = true; }
}

int32_t EditorOpenGuarded(int32_t (*fn)(void*, void*, int32_t*, int32_t*), void* plugin, void* parent,
                          int32_t* width, int32_t* height, bool& faulted) {
    __try { return fn(plugin, parent, width, height); }
    __except (EXCEPTION_EXECUTE_HANDLER) { faulted = true; return 0; }
}

int32_t EditorResizeGuarded(int32_t (*fn)(void*, int32_t*, int32_t*), void* plugin, int32_t* width, int32_t* height,
                            bool& faulted) {
    __try { return fn(plugin, width, height); }
    __except (EXCEPTION_EXECUTE_HANDLER) { faulted = true; return 0; }
}

void EditorSetSizeGuarded(void (*fn)(void*, int32_t, int32_t), void* plugin, int32_t width, int32_t height,
                          bool& faulted) {
    __try { fn(plugin, width, height); }
    __except (EXCEPTION_EXECUTE_HANDLER) { faulted = true; }
}

}  // namespace

struct Vst3Host::Api {
    HMODULE module = nullptr;
    int32_t (*list_classes)(const wchar_t*, char (*)[128], int32_t*, int32_t) = nullptr;
    void* (*create)(const wchar_t*, int32_t, double, int32_t, char*) = nullptr;
    int32_t (*process)(void*, float**, int32_t, float**, int32_t, int32_t, const Vst3Midi*, int32_t, double, double, int32_t) = nullptr;
    int32_t (*process_ex)(void*, float**, int32_t, float**, int32_t, int32_t, const Vst3Midi*, int32_t, double, double,
                          int32_t, int32_t, int32_t, double) = nullptr;
    int32_t (*state_begin)(void*) = nullptr;
    int32_t (*state_copy)(void*, uint8_t*, int32_t) = nullptr;
    int32_t (*set_state)(void*, const uint8_t*, int32_t) = nullptr;
    int32_t (*latency)(void*) = nullptr;
    int32_t (*tail)(void*) = nullptr;
    int32_t (*set_offline)(void*, int32_t) = nullptr;
    int32_t (*param_count)(void*) = nullptr;
    int32_t (*param_info)(void*, int32_t, uint32_t*, char*, char*, int32_t*, int32_t*, double*, double*) = nullptr;
    int32_t (*param_text)(void*, uint32_t, double, char*, int32_t) = nullptr;
    int32_t (*param_set)(void*, uint32_t, double) = nullptr;
    void (*destroy)(void*) = nullptr;
    int32_t (*has_editor)(void*) = nullptr;
    int32_t (*open_editor)(void*, void*, int32_t*, int32_t*) = nullptr;
    void (*close_editor)(void*) = nullptr;
    int32_t (*editor_idle)(void*) = nullptr;
    int32_t (*editor_poll_resize)(void*, int32_t*, int32_t*) = nullptr;
    void (*editor_set_size)(void*, int32_t, int32_t) = nullptr;

    template <typename T>
    bool Bind(HMODULE shim, const char* name, T& target) {
        target = reinterpret_cast<T>(GetProcAddress(shim, name));
        return target != nullptr;
    }
};

bool Vst3Host::EnsureShim(std::string& error) {
    if (api_ != nullptr) return true;
    const std::wstring folder = HelperFolder();
#ifdef _WIN64
    const std::wstring name = L"tfvst3.dll";
#else
    const std::wstring name = L"tfvst3-x86.dll";
#endif
    const std::wstring path = folder.empty() ? name : folder + L"\\" + name;
    if (!FileExists(path)) {
        error = "The VST3 host shim is missing from the helper folder: " + Narrow(path);
        return false;
    }
    HMODULE shim = LoadLibraryW(path.c_str());
    if (shim == nullptr) {
        error = "The VST3 host shim could not be loaded: " + Narrow(path);
        return false;
    }
    auto api = new (std::nothrow) Api();
    if (api == nullptr) { FreeLibrary(shim); error = "Out of memory loading the VST3 host shim."; return false; }
    api->module = shim;
    const bool bound =
        api->Bind(shim, "tfv3_list_classes", api->list_classes) &&
        api->Bind(shim, "tfv3_create", api->create) &&
        api->Bind(shim, "tfv3_state_begin", api->state_begin) &&
        api->Bind(shim, "tfv3_state_copy", api->state_copy) &&
        api->Bind(shim, "tfv3_set_state", api->set_state) &&
        api->Bind(shim, "tfv3_latency", api->latency) &&
        api->Bind(shim, "tfv3_tail", api->tail) &&
        api->Bind(shim, "tfv3_set_offline", api->set_offline) &&
        api->Bind(shim, "tfv3_param_count", api->param_count) &&
        api->Bind(shim, "tfv3_param_info", api->param_info) &&
        api->Bind(shim, "tfv3_param_text", api->param_text) &&
        api->Bind(shim, "tfv3_param_set", api->param_set) &&
        api->Bind(shim, "tfv3_destroy", api->destroy);
    // Older shims may only have tfv3_process; process_ex is preferred when present.
    api->Bind(shim, "tfv3_process_ex", api->process_ex);
    // The editor exports are optional as a group: an older shim still hosts audio, it just has no editor.
    const bool editorApi = api->Bind(shim, "tfv3_has_editor", api->has_editor) &&
                           api->Bind(shim, "tfv3_open_editor", api->open_editor) &&
                           api->Bind(shim, "tfv3_close_editor", api->close_editor) &&
                           api->Bind(shim, "tfv3_editor_idle", api->editor_idle) &&
                           api->Bind(shim, "tfv3_editor_poll_resize", api->editor_poll_resize) &&
                           api->Bind(shim, "tfv3_editor_set_size", api->editor_set_size);
    if (!editorApi) {
        api->has_editor = nullptr; api->open_editor = nullptr; api->close_editor = nullptr;
        api->editor_idle = nullptr; api->editor_poll_resize = nullptr; api->editor_set_size = nullptr;
    }
    if (!bound || (api->process_ex == nullptr && !api->Bind(shim, "tfv3_process", api->process))) {
        delete api;
        FreeLibrary(shim);
        error = "The VST3 host shim does not export the required tfv3 functions.";
        return false;
    }
    api_ = api;
    shim_ = shim;
    return true;
}

bool Vst3Host::ResolveBinary(const std::wstring& path, std::wstring& binary, std::string& error) const {
    if (FileExists(path)) { binary = path; return true; }
    if (GetFileAttributesW(path.c_str()) == INVALID_FILE_ATTRIBUTES) {
        error = "The VST3 plug-in was not found: " + Narrow(path);
        return false;
    }
    const std::filesystem::path bundle(path);
    const std::wstring stem = bundle.filename().wstring();
#ifdef _WIN64
    const std::wstring arches[] = { L"x86_64-win" };
#else
    const std::wstring arches[] = { L"x86-win32", L"x86-win" };
#endif
    for (const auto& arch : arches) {
        if (FindBinaryIn(bundle / L"Contents" / arch, stem, binary)) return true;
    }
#ifdef _WIN64
    error = "The VST3 bundle has no 64-bit Windows binary (Contents\\x86_64-win).";
#else
    error = "The VST3 bundle has no 32-bit Windows binary (Contents\\x86-win32).";
#endif
    return false;
}

bool Vst3Host::Load(const std::wstring& path, const std::string& expectedSha256, int sampleRate, int maxBlock,
                    bool wantInstrument, std::string& error) {
    Close();
    stateTooLarge_ = false;
    refused_ = false;
    editorFaulted_ = false;
    std::wstring binary;
    if (!ResolveBinary(path, binary, error)) return false;
    HoldFailure failure = HoldFailure::None;
    hold_ = HoldAndVerify(binary, expectedSha256, failure, error);
    if (hold_ == INVALID_HANDLE_VALUE) {
        if (failure == HoldFailure::Mismatch || failure == HoldFailure::Hash) refused_ = true;
        return false;
    }
    if (!EnsureShim(error)) { Close(); return false; }
    char names[32][128] = {};
    int32_t instruments[32] = {};
    const int32_t count = api_->list_classes(binary.c_str(), names, instruments, 32);
    if (count <= 0) { error = "No VST3 audio processor was found in this plug-in."; Close(); return false; }
    index_ = 0;
    for (int32_t i = 0; i < count; i++) {
        if ((instruments[i] != 0) == wantInstrument) { index_ = i; break; }
    }
    instrument_ = instruments[index_] != 0;
    product_ = names[index_];
    char text[512] = {};
    plugin_ = api_->create(binary.c_str(), index_, sampleRate, maxBlock, text);
    if (plugin_ == nullptr) {
        error = text[0] != 0 ? text : "The VST3 plug-in could not be started.";
        Close();
        return false;
    }
    latency_ = std::max(0, api_->latency(plugin_));
    tail_ = std::max(0, api_->tail(plugin_));
    events_.assign(kMaxEvents, Vst3Midi{});
    if (hold_ != INVALID_HANDLE_VALUE) { CloseHandle(hold_); hold_ = INVALID_HANDLE_VALUE; }
    return true;
}

void Vst3Host::Close() {
    if (plugin_ != nullptr && api_ != nullptr && api_->destroy != nullptr) api_->destroy(plugin_);
    plugin_ = nullptr;
    editorOpen_ = false;
    editorFaulted_ = false;
    editorWidth_ = 0;
    editorHeight_ = 0;
    if (shim_ != nullptr) { FreeLibrary(shim_); shim_ = nullptr; }
    delete api_;
    api_ = nullptr;
    if (hold_ != INVALID_HANDLE_VALUE) { CloseHandle(hold_); hold_ = INVALID_HANDLE_VALUE; }
    events_.clear();
}

bool Vst3Host::Process(const BlockMidi* events, int eventCount, const float* inL, const float* inR,
                       float* outL, float* outR, int frames, const Transport& transport) {
    if (plugin_ == nullptr || api_ == nullptr || frames <= 0) return false;
    const int count = std::clamp(eventCount, 0, kMaxEvents);
    if (events_.size() < static_cast<size_t>(count)) return false;
    for (int i = 0; i < count; i++) {
        events_[i].sampleOffset = std::clamp(events[i].frame, 0, frames - 1);
        events_[i].status = events[i].status;
        events_[i].data1 = events[i].data1;
        events_[i].data2 = events[i].data2;
        events_[i].pad = 0;
    }
    float* inputs[2] = { const_cast<float*>(inL), const_cast<float*>(inR) };
    float* outputs[2] = { outL, outR };
    if (api_->process_ex != nullptr) {
        return api_->process_ex(plugin_, instrument_ ? nullptr : inputs, instrument_ ? 0 : 2, outputs, 2, frames,
                                events_.data(), count, transport.tempo, transport.ppq, transport.playing ? 1 : 0,
                                transport.timeSigNumerator, transport.timeSigDenominator, transport.barStartPpq) != 0;
    }
    return api_->process(plugin_, instrument_ ? nullptr : inputs, instrument_ ? 0 : 2, outputs, 2, frames,
                         events_.data(), count, transport.tempo, transport.ppq, transport.playing ? 1 : 0) != 0;
}

bool Vst3Host::SetOfflineMode(bool offline) {
    if (plugin_ == nullptr || api_ == nullptr || api_->set_offline == nullptr) return false;
    return api_->set_offline(plugin_, offline ? 1 : 0) != 0;
}

int Vst3Host::ParameterCount() {
    if (plugin_ == nullptr || api_ == nullptr) return 0;
    return std::max(0, api_->param_count(plugin_));
}

std::vector<ParameterInfo> Vst3Host::Parameters() {
    std::vector<ParameterInfo> list;
    if (plugin_ == nullptr || api_ == nullptr) return list;
    const int count = std::min(ParameterCount(), 8192);
    char name[256] = {};
    char units[64] = {};
    char text[128] = {};
    for (int i = 0; i < count; i++) {
        uint32_t id = 0;
        int32_t steps = 0, flags = 0;
        double def = 0, value = 0;
        if (api_->param_info(plugin_, i, &id, name, units, &steps, &flags, &def, &value) == 0) continue;
        if ((flags & (2 | 4)) != 0) continue;   // hidden, program change
        ParameterInfo parameter;
        parameter.id = id;
        parameter.name = name;
        parameter.steps = std::max(0, steps);
        parameter.readOnly = (flags & 1) != 0;
        parameter.defaultValue = ToPlain(def, parameter.steps);
        parameter.value = ToPlain(value, parameter.steps);
        if (api_->param_text(plugin_, id, value, text, 128) != 0) parameter.display = text;
        if (units[0] != 0 && !parameter.display.empty() && parameter.display.find(units) == std::string::npos) {
            parameter.display += " ";
            parameter.display += units;
        }
        list.push_back(std::move(parameter));
        if (list.size() >= 2048) break;
    }
    return list;
}

bool Vst3Host::SetParameter(uint32_t id, double plainValue) {
    if (plugin_ == nullptr || api_ == nullptr || !std::isfinite(plainValue)) return false;
    for (const auto& parameter : Parameters()) {
        if (parameter.id != id) continue;
        if (parameter.readOnly) return false;
        return api_->param_set(plugin_, id, ToNormalized(plainValue, parameter.steps)) != 0;
    }
    return false;
}

bool Vst3Host::GetState(std::vector<uint8_t>& state) {
    state.clear();
    if (plugin_ == nullptr || api_ == nullptr) return false;
    const int32_t size = api_->state_begin(plugin_);
    if (size <= 0) return false;
    if (size > kMaxStateBytes) {
        stateTooLarge_ = true;
        api_->state_copy(plugin_, nullptr, 0);
        return false;
    }
    state.resize(static_cast<size_t>(size));
    if (api_->state_copy(plugin_, state.data(), size) != size) { state.clear(); return false; }
    return true;
}

bool Vst3Host::SetState(const std::vector<uint8_t>& state) {
    if (plugin_ == nullptr || api_ == nullptr || state.empty() || state.size() > kMaxStateBytes) return false;
    return api_->set_state(plugin_, state.data(), static_cast<int32_t>(state.size())) != 0;
}

bool Vst3Host::HasEditor() const {
    return plugin_ != nullptr && api_ != nullptr && !editorFaulted_ && api_->has_editor != nullptr && api_->has_editor(plugin_) != 0;
}

bool Vst3Host::OpenEditor(HWND parent) {
    if (plugin_ == nullptr || api_ == nullptr || editorFaulted_ || api_->open_editor == nullptr || editorOpen_) return false;
    int32_t width = 0, height = 0;
    if (EditorOpenGuarded(api_->open_editor, plugin_, parent, &width, &height, editorFaulted_) == 0) return false;
    editorOpen_ = true;
    editorWidth_ = std::max(1, static_cast<int>(width));
    editorHeight_ = std::max(1, static_cast<int>(height));
    return true;
}

void Vst3Host::CloseEditor() {
    if (plugin_ == nullptr || api_ == nullptr || !editorOpen_) return;
    editorOpen_ = false;
    if (api_->close_editor != nullptr) EditorVoidGuarded(api_->close_editor, plugin_, editorFaulted_);
}

void Vst3Host::EditorIdle() {
    if (plugin_ == nullptr || api_ == nullptr || editorFaulted_ || !editorOpen_ || api_->editor_idle == nullptr) return;
    EditorIntGuarded(api_->editor_idle, plugin_, editorFaulted_);
}

bool Vst3Host::EditorSize(int& width, int& height) {
    width = editorWidth_ > 0 ? editorWidth_ : 400;
    height = editorHeight_ > 0 ? editorHeight_ : 300;
    return editorOpen_;
}

bool Vst3Host::TakeEditorResize(int& width, int& height) {
    if (plugin_ == nullptr || api_ == nullptr || editorFaulted_ || !editorOpen_ || api_->editor_poll_resize == nullptr) return false;
    int32_t w = 0, h = 0;
    if (EditorResizeGuarded(api_->editor_poll_resize, plugin_, &w, &h, editorFaulted_) == 0 || w <= 0 || h <= 0) return false;
    width = w;
    height = h;
    return true;
}

void Vst3Host::SetEditorSize(int width, int height) {
    if (plugin_ == nullptr || api_ == nullptr || editorFaulted_ || !editorOpen_ || api_->editor_set_size == nullptr) return;
    if (width <= 0 || height <= 0) return;
    editorWidth_ = width;
    editorHeight_ = height;
    EditorSetSizeGuarded(api_->editor_set_size, plugin_, width, height, editorFaulted_);
}

#ifdef TFVST32_TEST_HOOKS
namespace {
int32_t FaultingEditorInt(void*) { RaiseException(EXCEPTION_ACCESS_VIOLATION, 0, 0, nullptr); return 0; }
void FaultingEditorVoid(void*) { RaiseException(EXCEPTION_ACCESS_VIOLATION, 0, 0, nullptr); }
int32_t FaultingEditorOpen(void*, void*, int32_t*, int32_t*) { RaiseException(EXCEPTION_ACCESS_VIOLATION, 0, 0, nullptr); return 0; }
int32_t FaultingEditorResize(void*, int32_t*, int32_t*) { RaiseException(EXCEPTION_ACCESS_VIOLATION, 0, 0, nullptr); return 0; }
void FaultingEditorSetSize(void*, int32_t, int32_t) { RaiseException(EXCEPTION_ACCESS_VIOLATION, 0, 0, nullptr); }
}  // namespace

// Test copy only: every editor wrapper must convert a plug-in fault into a remembered failure, not process death.
bool Vst3EditorGuardSelfTest() {
    bool faulted = false;
    int32_t width = 0, height = 0;
    if (EditorIntGuarded(FaultingEditorInt, nullptr, faulted) != 0 || !faulted) return false;
    faulted = false;
    EditorVoidGuarded(FaultingEditorVoid, nullptr, faulted);
    if (!faulted) return false;
    faulted = false;
    if (EditorOpenGuarded(FaultingEditorOpen, nullptr, nullptr, &width, &height, faulted) != 0 || !faulted) return false;
    faulted = false;
    if (EditorResizeGuarded(FaultingEditorResize, nullptr, &width, &height, faulted) != 0 || !faulted) return false;
    faulted = false;
    EditorSetSizeGuarded(FaultingEditorSetSize, nullptr, 100, 100, faulted);
    return faulted;
}
#endif

}  // namespace tfvst32
