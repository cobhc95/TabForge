// The framed control channel used by the helper: a named pipe on Windows, loopback TCP under Wine.
// Framing is [int32 little-endian length][byte type][payload]; length includes the type byte.
#pragma once

#include <cstdint>
#include <string>
#include <vector>

namespace tfvst32 {

class ControlChannel {
public:
    virtual ~ControlChannel() = default;

    virtual bool Write(uint8_t type, const std::vector<uint8_t>& payload) = 0;
    // Reads one complete frame; false when the peer closed or a partial frame stopped making progress.
    virtual bool Read(uint8_t& type, std::vector<uint8_t>& payload) = 0;
    // True when at least one byte is available (the command loop must stay responsive to Quit).
    virtual bool HasData() const = 0;
    // True after a read/write failed or the peer closed: the engine's end is gone.
    virtual bool Broken() const = 0;
    virtual void Close() = 0;
};

}  // namespace tfvst32
