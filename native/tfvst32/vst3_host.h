// VST3 hosting inside the helper through the existing tfvst3 C API (tfvst3.dll x64, tfvst3-x86.dll x86).
// Audio, MIDI, state, parameters, latency, tail and the plug-in editor are wired here.
#pragma once

#include <cstdint>
#include <string>
#include <vector>

#include <windows.h>

#include "editor_target.h"
#include "tfvst32_protocol.h"
#include "vst2_host.h"   // ParameterInfo/Transport are shared with the VST2 host

namespace tfvst32 {

// Same layout as tfv3_midi in tfvst3.h (8 bytes); declared here so the header needs no shim import.
struct Vst3Midi { int32_t sampleOffset; uint8_t status, data1, data2, pad; };
static_assert(sizeof(Vst3Midi) == 8, "VST3 midi event layout");

class Vst3Host : public EditorTarget {
public:
    Vst3Host() = default;
    Vst3Host(const Vst3Host&) = delete;
    Vst3Host& operator=(const Vst3Host&) = delete;
    ~Vst3Host() { Close(); }

    bool Load(const std::wstring& path, const std::string& expectedSha256, int sampleRate, int maxBlock,
              bool wantInstrument, std::string& error);
    void Close();

    bool Process(const BlockMidi* events, int eventCount, const float* inL, const float* inR,
                 float* outL, float* outR, int frames, const Transport& transport);
    bool SetOfflineMode(bool offline);

    bool IsInstrument() const { return instrument_; }
    bool HasEditor() const override;
    int LatencySamples() const { return latency_; }
    int TailSamples() const { return tail_; }
    int ParameterCount();
    bool UsesChunks() const { return true; }
    int UniqueId() const { return 0; }
    int DeclaredInputs() const { return -1; }
    int DeclaredOutputs() const { return -1; }
    std::vector<ParameterInfo> Parameters();
    bool SetParameter(uint32_t id, double plainValue);
    bool SetProgram(int) { return false; }
    bool GetState(std::vector<uint8_t>& state);
    bool SetState(const std::vector<uint8_t>& state);
    bool StateTooLarge() const { return stateTooLarge_; }
    std::string Vendor() const { return vendor_; }
    std::string Product() const { return product_; }
    bool Refused() const { return refused_; }

    // Editor calls (main thread only). VST3 has no size before the first open, so EditorSize reports the
    // size tfv3_open_editor returned; a view that asks to resize is polled by TakeEditorResize.
    bool OpenEditor(HWND parent) override;
    void CloseEditor() override;
    bool EditorOpen() const override { return editorOpen_; }
    void EditorIdle() override;
    bool EditorSize(int& width, int& height) override;
    bool TakeEditorResize(int& width, int& height) override;
    void SetEditorSize(int width, int height) override;

private:
    struct Api;
    bool EnsureShim(std::string& error);
    bool ResolveBinary(const std::wstring& path, std::wstring& binary, std::string& error) const;
    void FreeHandles();

    Api* api_ = nullptr;
    HMODULE shim_ = nullptr;
    void* plugin_ = nullptr;
    HANDLE hold_ = INVALID_HANDLE_VALUE;
    int index_ = 0;
    int latency_ = 0;
    int tail_ = 0;
    bool instrument_ = false;
    bool refused_ = false;
    bool stateTooLarge_ = false;
    bool editorOpen_ = false;
    bool editorFaulted_ = false;   // a guarded editor call faulted: no further editor calls into this plug-in
    int editorWidth_ = 0;
    int editorHeight_ = 0;
    std::string vendor_;
    std::string product_;
    std::vector<Vst3Midi> events_;
};

#ifdef TFVST32_TEST_HOOKS
// Test-only proof that the VST3 editor SEH wrappers catch a fault instead of ending the helper (native selftest).
bool Vst3EditorGuardSelfTest();
#endif

}  // namespace tfvst32
