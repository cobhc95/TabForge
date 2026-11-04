// The shared audio block: engine-created named mapping and two auto-reset events on Windows, or an explicit file with
// polled request/done flags for the Wine bridge. The header layout is identical in both modes.
#pragma once

#include <cstdint>
#include <string>

#include <windows.h>

#include "tfvst32_protocol.h"

namespace tfvst32 {

class BridgeBlock {
public:
    BridgeBlock() = default;
    BridgeBlock(const BridgeBlock&) = delete;
    BridgeBlock& operator=(const BridgeBlock&) = delete;
    ~BridgeBlock() { Close(); }

    // Opens the named mapping and events for one session; requires the exact size for maxBlock/slots.
    bool Open(const std::wstring& id, int maxBlock, int slots, std::string& error);
    // Opens the engine's explicit file (Wine: a Z:\ path) and polls header flags 56/60; no events are used.
    bool OpenFile(const std::wstring& path, int maxBlock, int slots, std::string& error);
    void Close();
    bool IsOpen() const { return base_ != nullptr; }

    BlockHeader* Header() const { return header_; }
    BlockMidi* Events() const { return reinterpret_cast<BlockMidi*>(base_ + kEventsOffset); }
    float* Channel(int index, int generation) const;
    size_t MappedBytes() const { return bytes_; }
    int Slots() const { return slots_; }
    int MaxBlock() const { return maxBlock_; }
    bool IsFileBacked() const { return fileMode_; }

    // Worker side: wait for a request and return its generation.
    bool TakeRequest(int timeoutMs, int& generation);
    // Worker side: publish the completed generation, then signal done (release ordering).
    void Complete(int generation);

private:
    HANDLE mapping_ = nullptr;
    HANDLE request_ = nullptr;
    HANDLE done_ = nullptr;
    HANDLE file_ = nullptr;
    uint8_t* base_ = nullptr;
    BlockHeader* header_ = nullptr;
    size_t bytes_ = 0;
    int maxBlock_ = 0;
    int slots_ = 0;
    bool fileMode_ = false;
};

}  // namespace tfvst32
