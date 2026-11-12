// One 32-bit VST2 plug-in in this process: AEffect, host callback, audio buffers and parameter helpers.
// The structs below are hand-declared from the public VST2 binary interface; no Steinberg SDK header is used.
#pragma once

#include <atomic>
#include <cstdint>
#include <mutex>
#include <string>
#include <vector>

#include <windows.h>

#include "hash_hold.h"
#include "editor_target.h"
#include "tfvst32_protocol.h"

namespace tfvst32 {

#define VST_CALLBACK __cdecl

using VstHostCallback = intptr_t (*)(struct AEffect*, int32_t, int32_t, intptr_t, void*, float);

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

struct VstEvent {
    int32_t type;
    int32_t byteSize;
    int32_t deltaFrames;
    int32_t flags;
};

struct VstMidiEvent {
    int32_t type;
    int32_t byteSize;
    int32_t deltaFrames;
    int32_t flags;
    int32_t noteLength;
    int32_t noteOffset;
    uint8_t midiData[4];
    uint8_t detune;
    uint8_t noteOffVelocity;
    uint8_t reserved1;
    uint8_t reserved2;
};
static_assert(sizeof(VstMidiEvent) == 32, "VstMidiEvent layout");

struct VstTimeInfo {
    double samplePos;
    double sampleRate;
    double nanoSeconds;
    double ppqPos;
    double tempo;
    double barStartPos;
    double cycleStartPos;
    double cycleEndPos;
    int32_t timeSigNumerator;
    int32_t timeSigDenominator;
    int32_t smpteOffset;
    int32_t smpteFrameRate;
    int32_t samplesToNextClock;
    int32_t flags;
};

struct Transport {
    double tempo = 120.0;
    double ppq = 0.0;
    double barStartPpq = 0.0;
    bool playing = false;
    int timeSigNumerator = 0;
    int timeSigDenominator = 0;
};

struct ParameterInfo {
    uint32_t id = 0;
    std::string name;
    std::string display;
    double value = 0.0;
    // VST3 only: 0 = continuous, otherwise the number of steps; the wire uses plain values (0..steps).
    int steps = 0;
    double defaultValue = 0.0;
    bool readOnly = false;
};

class Vst2Host : public EditorTarget {
public:
    Vst2Host() = default;
    Vst2Host(const Vst2Host&) = delete;
    Vst2Host& operator=(const Vst2Host&) = delete;
    ~Vst2Host() { Close(); }

    bool Load(const std::wstring& path, const std::string& expectedSha256, int sampleRate, int maxBlock, std::string& error);
    void Close();

    bool Process(const BlockMidi* events, int eventCount, const float* inL, const float* inR,
                 float* outL, float* outR, int frames, const Transport& transport);
    // False when an editor call currently owns the plug-in (the block is skipped); the audio thread never waits.
    bool SetOfflineMode(bool offline);

    bool IsInstrument() const { return instrument_; }
    bool HasEditor() const override { return hasEditor_; }
    int LatencySamples() const { return latency_; }
    int TailSamples() const;
    int ParameterCount() const;
    bool UsesChunks() const;
    int UniqueId() const;
    int DeclaredInputs() const;
    int DeclaredOutputs() const;
    std::vector<ParameterInfo> Parameters();
    bool SetParameter(uint32_t id, double value);
    bool SetProgram(int program);
    int CurrentProgram();
    bool GetState(std::vector<uint8_t>& state);
    bool SetState(const std::vector<uint8_t>& state);
    /// <summary>True when the last GetState failed only because the plug-in's state is over the 16 MiB limit.</summary>
    bool StateTooLarge() const { return stateTooLarge_; }
    std::string Vendor();
    std::string Product();

    // Editor calls: helper main thread only (never the audio thread). EditorSize returns the VST2 rectangle,
    // or a sensible default when the plug-in gives none; it returns false when the plug-in has no editor.
    bool EditorSize(int& width, int& height) override;
    bool OpenEditor(HWND parent) override;
    void CloseEditor() override;
    void EditorIdle() override;
    bool EditorOpen() const override { return editorOpen_; }
    /// <summary>audioMasterSizeWindow request from the plug-in (atomic; consumed by the helper main thread).</summary>
    bool TakeEditorResize(int& width, int& height) override;
    void SetEditorSize(int, int) override {}
    /// <summary>audioMasterAutomate/beginEdit/endEdit happened (atomic; sent as MsgEdited by the main thread).</summary>
    bool TakeEdited() { return edited_.exchange(0, std::memory_order_acq_rel) != 0; }
    void* Module() const { return module_; }
    // True when the file itself was refused (missing or changed), which maps to exit code 5 in --probe.
    bool Refused() const { return refused_; }

    intptr_t HostCallback(int32_t opcode, int32_t index, intptr_t value, void* ptr, float opt);
    static Vst2Host* g_host;

private:
    intptr_t Dispatch(int opcode, int index, intptr_t value, void* ptr, float opt);    std::string AskString(int opcode, int fallback = -1);
    void FreeBuffers();
    // SEH-guarded plug-in calls (MSVC __try/__except); each returns false when the plug-in faulted.
    bool ProcessGuarded(void* events, int count, int frames);
    bool EditorDispatchGuarded(int opcode, int index, intptr_t value, void* ptr, float opt, intptr_t* result);
    bool ReadEditorRect(int& width, int& height);

    HMODULE module_ = nullptr;
    AEffect* effect_ = nullptr;
    void* process_ = nullptr;
    int sampleRate_ = 48000;
    int maxBlock_ = 256;
    int latency_ = 0;
    bool instrument_ = false;
    bool hasEditor_ = false;
    bool opened_ = false;
    bool refused_ = false;
    bool editorOpen_ = false;
    bool stateTooLarge_ = false;
    double samplePos_ = 0.0;
    bool wasPlaying_ = false;
    std::vector<float*> inputs_;
    std::vector<float*> outputs_;
    std::vector<float*> inputBuffers_;
    std::vector<float*> outputBuffers_;
    std::vector<uint8_t> eventBytes_;
    VstTimeInfo timeInfo_{};
    std::atomic<DWORD> audioThread_{0};
    std::atomic<int> offline_{0};
    std::atomic<int> pendingWidth_{0}, pendingHeight_{0};
    std::atomic<int> edited_{0}, resized_{0};
    std::atomic<int> dispatchFaulted_{0};   // a guarded dispatcher/parameter call faulted during Load
    // Serialises plug-in editor calls against processReplacing. Process/SetOfflineMode only try-lock: while an editor
    // call owns the plug-in the audio thread skips that block (silence) instead of waiting for the main thread.
    std::mutex pluginMutex_;
};

}  // namespace tfvst32
