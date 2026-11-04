// Named-pipe client for the tfvst32 control channel (the default Windows transport). One writer at a time, like the
// managed host. The bytes on the pipe are unchanged by the TCP/file transport added later.
#pragma once

#include <atomic>
#include <cstdint>
#include <mutex>
#include <string>
#include <vector>

#include <windows.h>

#include "control_channel.h"

namespace tfvst32 {

class PipeChannel final : public ControlChannel {
public:
    PipeChannel() = default;
    PipeChannel(const PipeChannel&) = delete;
    PipeChannel& operator=(const PipeChannel&) = delete;
    ~PipeChannel() override { Close(); }

    // Connects to \\.\pipe\TabForge.PluginHost.<id>; false with a readable error when the server is not there.
    bool Connect(const std::wstring& id, int timeoutMs, std::string& error);
    bool IsOpen() const { return pipe_ != INVALID_HANDLE_VALUE; }
    bool Write(uint8_t type, const std::vector<uint8_t>& payload) override;
    // Bounded frame read on the main thread: only bytes PeekNamedPipe reports available are read, so a partial frame
    // can never block in ReadFile; a frame that stops making progress times out and is treated as a broken pipe/quit.
    bool Read(uint8_t& type, std::vector<uint8_t>& payload) override;
    // True when at least one byte is available (used by the command loop to stay responsive to Quit/engine exit).
    bool HasData() const override;
    // True after PeekNamedPipe/ReadFile failed or a partial frame timed out: the engine's end is gone.
    bool Broken() const override { return broken_.load(std::memory_order_acquire); }
    void Close() override;

private:
    HANDLE pipe_ = INVALID_HANDLE_VALUE;
    std::mutex writeMutex_;
    mutable std::atomic<bool> broken_{false};
};

}  // namespace tfvst32
