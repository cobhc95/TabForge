// Loopback TCP control channel for the Wine bridge. Winsock is available both on Windows and under Wine, so the same
// helper binary can be tested natively on Windows and run under Wine without changing the frame bytes.
#pragma once

#include <atomic>
#include <cstdint>
#include <mutex>
#include <string>
#include <vector>

#include "control_channel.h"
#include "tfvst32_protocol.h"

namespace tfvst32 {

class TcpChannel final : public ControlChannel {
public:
    TcpChannel() = default;
    TcpChannel(const TcpChannel&) = delete;
    TcpChannel& operator=(const TcpChannel&) = delete;
    ~TcpChannel() override { Close(); }

    // Connects to 127.0.0.1:<port>. The engine must already be listening; false carries a readable error.
    bool Connect(int port, int timeoutMs, std::string& error);
    bool IsOpen() const { return socket_ != static_cast<uintptr_t>(~0ull); }
    bool Write(uint8_t type, const std::vector<uint8_t>& payload) override;
    bool Read(uint8_t& type, std::vector<uint8_t>& payload) override;
    bool HasData() const override;
    bool Broken() const override { return broken_.load(std::memory_order_acquire); }
    void Close() override;

private:
    bool ReadExact(uint8_t* buffer, size_t count, PartialFrameDeadline& deadline);
    void Fail() { broken_.store(true, std::memory_order_release); }

    uintptr_t socket_ = static_cast<uintptr_t>(~0ull);
    bool winsock_ = false;
    std::mutex writeMutex_;
    mutable std::atomic<bool> broken_{false};
};

}  // namespace tfvst32
