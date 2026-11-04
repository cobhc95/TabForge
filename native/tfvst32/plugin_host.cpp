#include "plugin_host.h"

#include "vst3_host.h"

namespace tfvst32 {

PluginHost::~PluginHost() { Close(); }

bool PluginHost::Load(const std::wstring& format, const std::wstring& path, const std::string& expectedSha256,
                      int sampleRate, int maxBlock, bool wantInstrument, std::string& error) {
    Close();
    if (format == L"VST3") {
        auto host = std::make_unique<Vst3Host>();
        if (!host->Load(path, expectedSha256, sampleRate, maxBlock, wantInstrument, error)) {
            vst3_ = std::move(host);   // keep refused_ readable by --probe after a failed load
            return false;
        }
        vst3_ = std::move(host);
        return true;
    }
    if (format != L"VST2") { error = "Unsupported plug-in format for the helper."; return false; }
    auto host = std::make_unique<Vst2Host>();
    if (!host->Load(path, expectedSha256, sampleRate, maxBlock, error)) {
        vst2_ = std::move(host);   // keep refused_ readable by --probe after a failed load
        return false;
    }
    vst2_ = std::move(host);
    return true;
}

void PluginHost::Close() {
    if (vst2_) { vst2_->Close(); vst2_.reset(); }
    if (vst3_) { vst3_->Close(); vst3_.reset(); }
}

bool PluginHost::Process(const BlockMidi* events, int eventCount, const float* inL, const float* inR,
                         float* outL, float* outR, int frames, const Transport& transport) {
    if (vst3_) return vst3_->Process(events, eventCount, inL, inR, outL, outR, frames, transport);
    if (vst2_) return vst2_->Process(events, eventCount, inL, inR, outL, outR, frames, transport);
    return false;
}

bool PluginHost::SetOfflineMode(bool offline) {
    if (vst3_) return vst3_->SetOfflineMode(offline);
    if (vst2_) return vst2_->SetOfflineMode(offline);
    return false;
}

bool PluginHost::IsInstrument() const { return vst3_ ? vst3_->IsInstrument() : vst2_ ? vst2_->IsInstrument() : false; }
bool PluginHost::HasEditor() const { return vst3_ ? vst3_->HasEditor() : vst2_ ? vst2_->HasEditor() : false; }
int PluginHost::LatencySamples() const { return vst3_ ? vst3_->LatencySamples() : vst2_ ? vst2_->LatencySamples() : 0; }
int PluginHost::TailSamples() const { return vst3_ ? vst3_->TailSamples() : vst2_ ? vst2_->TailSamples() : 0; }
int PluginHost::ParameterCount() const { return vst2_ ? vst2_->ParameterCount() : 0; }
bool PluginHost::UsesChunks() const { return vst3_ ? vst3_->UsesChunks() : vst2_ ? vst2_->UsesChunks() : false; }
int PluginHost::UniqueId() const { return vst3_ ? vst3_->UniqueId() : vst2_ ? vst2_->UniqueId() : 0; }
int PluginHost::DeclaredInputs() const { return vst3_ ? vst3_->DeclaredInputs() : vst2_ ? vst2_->DeclaredInputs() : 0; }
int PluginHost::DeclaredOutputs() const { return vst3_ ? vst3_->DeclaredOutputs() : vst2_ ? vst2_->DeclaredOutputs() : 0; }

std::vector<ParameterInfo> PluginHost::Parameters() {
    if (vst3_) return vst3_->Parameters();
    if (vst2_) return vst2_->Parameters();
    return {};
}

bool PluginHost::SetParameter(uint32_t id, double value) {
    if (vst3_) return vst3_->SetParameter(id, value);
    if (vst2_) return vst2_->SetParameter(id, value);
    return false;
}

bool PluginHost::SetProgram(int program) {
    if (vst3_) return vst3_->SetProgram(program);
    if (vst2_) return vst2_->SetProgram(program);
    return false;
}

bool PluginHost::GetState(std::vector<uint8_t>& state) {
    if (vst3_) return vst3_->GetState(state);
    if (vst2_) return vst2_->GetState(state);
    return false;
}

bool PluginHost::SetState(const std::vector<uint8_t>& state) {
    if (vst3_) return vst3_->SetState(state);
    if (vst2_) return vst2_->SetState(state);
    return false;
}

bool PluginHost::StateTooLarge() const { return vst3_ ? vst3_->StateTooLarge() : vst2_ ? vst2_->StateTooLarge() : false; }
std::string PluginHost::Vendor() const { return vst3_ ? vst3_->Vendor() : vst2_ ? vst2_->Vendor() : std::string(); }
std::string PluginHost::Product() const { return vst3_ ? vst3_->Product() : vst2_ ? vst2_->Product() : std::string(); }
bool PluginHost::Refused() const { return vst3_ ? vst3_->Refused() : vst2_ ? vst2_->Refused() : false; }

}  // namespace tfvst32
