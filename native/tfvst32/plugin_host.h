// One plug-in host in this helper: the unchanged VST2 host, or the VST3 host that loads the tfvst3 shim.
// The helper protocol, audio block and control channel are the same for both formats.
#pragma once

#include <memory>
#include <string>
#include <vector>

#include <windows.h>

#include "editor_target.h"
#include "tfvst32_protocol.h"
#include "vst2_host.h"
#include "vst3_host.h"

namespace tfvst32 {

class PluginHost {
public:
    PluginHost() = default;
    PluginHost(const PluginHost&) = delete;
    PluginHost& operator=(const PluginHost&) = delete;
    ~PluginHost();

    // format: "VST2" or "VST3". wantInstrument selects the VST3 class matching the requested role.
    bool Load(const std::wstring& format, const std::wstring& path, const std::string& expectedSha256,
              int sampleRate, int maxBlock, bool wantInstrument, std::string& error);
    void Close();

    bool Process(const BlockMidi* events, int eventCount, const float* inL, const float* inR,
                 float* outL, float* outR, int frames, const Transport& transport);
    bool SetOfflineMode(bool offline);

    bool IsInstrument() const;
    bool HasEditor() const;
    int LatencySamples() const;
    int TailSamples() const;
    int ParameterCount() const;
    bool UsesChunks() const;
    int UniqueId() const;
    int DeclaredInputs() const;
    int DeclaredOutputs() const;
    std::vector<ParameterInfo> Parameters();
    bool SetParameter(uint32_t id, double value);
    bool SetProgram(int program);
    bool GetState(std::vector<uint8_t>& state);
    bool SetState(const std::vector<uint8_t>& state);
    bool StateTooLarge() const;
    std::string Vendor() const;
    std::string Product() const;
    bool Refused() const;
    // VST2 automation/beginEdit happened; VST3 parameter changes are reported through the editor feed (Stage 7).
    bool TakeEdited() { return vst2_ ? vst2_->TakeEdited() : false; }

    // VST2-only surface (quirks that have no VST3 equivalent). Null for a VST3 instance.
    Vst2Host* Vst2() { return vst2_.get(); }
    // The loaded instance's editor surface, whichever format; null only before a successful Load.
    EditorTarget* Editor() { return vst3_ ? static_cast<EditorTarget*>(vst3_.get()) : vst2_.get(); }

private:
    std::unique_ptr<Vst2Host> vst2_;
    std::unique_ptr<Vst3Host> vst3_;
};

}  // namespace tfvst32
