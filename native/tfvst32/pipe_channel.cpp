#include "pipe_channel.h"

#include "tfvst32_protocol.h"

#include <algorithm>
#include <chrono>

namespace tfvst32 {
namespace {
constexpr uint32_t kPipeBusy = 231;  // ERROR_PIPE_BUSY

bool Available(HANDLE pipe, DWORD& available) {
    available = 0;
    return PeekNamedPipe(pipe, nullptr, 0, nullptr, &available, nullptr) != 0;
}

// Reads only bytes the pipe already holds, so ReadFile never waits for a sender that stopped mid-frame.
bool ReadAvailable(HANDLE pipe, uint8_t* buffer, DWORD count, DWORD& read) {
    read = 0;
    return ReadFile(pipe, buffer, count, &read, nullptr) != 0 && read > 0;
}
}  // namespace

bool PipeChannel::Connect(const std::wstring& id, int timeoutMs, std::string& error) {
    Close();
    broken_.store(false, std::memory_order_release);
    const std::wstring name = L"\\\\.\\pipe\\TabForge.PluginHost." + id;
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::milliseconds(timeoutMs);
    while (true) {
        pipe_ = CreateFileW(name.c_str(), GENERIC_READ | GENERIC_WRITE, 0, nullptr, OPEN_EXISTING, 0, nullptr);
        if (pipe_ != INVALID_HANDLE_VALUE) return true;
        const DWORD code = GetLastError();
        if (code != ERROR_FILE_NOT_FOUND && code != kPipeBusy && code != ERROR_PIPE_NOT_CONNECTED) {
            error = "cannot open the plug-in host pipe (error " + std::to_string(code) + ")";
            return false;
        }
        if (std::chrono::steady_clock::now() >= deadline) {
            error = "the plug-in host pipe did not appear within " + std::to_string(timeoutMs) + " ms";
            return false;
        }
        if (code == kPipeBusy) WaitNamedPipeW(name.c_str(), 100);
        else Sleep(10);
    }
}

bool PipeChannel::Write(uint8_t type, const std::vector<uint8_t>& payload) {
    std::lock_guard<std::mutex> lock(writeMutex_);
    if (pipe_ == INVALID_HANDLE_VALUE) return false;
    if (payload.size() >= static_cast<size_t>(kMaxFrameBytes)) return false;
    const int32_t length = static_cast<int32_t>(payload.size() + 1);
    if (!FrameAllowed(type, length)) return false;
    uint8_t header[5];
    EncodeHeader(header, length);
    header[4] = type;
    DWORD written = 0;
    if (!WriteFile(pipe_, header, sizeof(header), &written, nullptr) || written != sizeof(header)) { broken_.store(true, std::memory_order_release); return false; }
    if (!payload.empty()) {
        written = 0;
        if (!WriteFile(pipe_, payload.data(), static_cast<DWORD>(payload.size()), &written, nullptr) ||
            written != payload.size()) { broken_.store(true, std::memory_order_release); return false; }
    }
    return true;
}

bool PipeChannel::Read(uint8_t& type, std::vector<uint8_t>& payload) {
    if (pipe_ == INVALID_HANDLE_VALUE) { broken_.store(true, std::memory_order_release); return false; }
    PartialFrameDeadline deadline;

    // Reads exactly count bytes that the pipe already holds, draining available bytes and bounding the wait by the
    // shared frame deadline. A state frame can be far larger than the pipe buffer, so waiting for the whole length to
    // become available would deadlock against the engine's single WriteFile.
    auto readExact = [&](uint8_t* buffer, size_t count) {
        size_t got = 0;
        while (got < count) {
            DWORD available = 0;
            if (!Available(pipe_, available)) { broken_.store(true, std::memory_order_release); return false; }
            if (available > 0) {
                DWORD read = 0;
                const DWORD want = std::min<DWORD>(available, static_cast<DWORD>(count - got));
                if (!ReadAvailable(pipe_, buffer + got, want, read)) { broken_.store(true, std::memory_order_release); return false; }
                got += read;
                deadline.Progress();
                continue;
            }
            if (deadline.Expired()) { broken_.store(true, std::memory_order_release); return false; }
            Sleep(1);
        }
        return true;
    };

    uint8_t header[4] = {};
    if (!readExact(header, sizeof(header))) return false;

    int32_t length = 0;
    if (!DecodeHeader(header, length)) { broken_.store(true, std::memory_order_release); return false; }

    // Read the type before the payload so an oversized control frame is refused without allocating its length first.
    uint8_t frameType = 0;
    if (!readExact(&frameType, 1)) return false;
    if (!FrameAllowed(frameType, length)) {
        // An over-limit state is drained and answered, not fatal; every other oversized frame still ends the session.
        if (OversizedStateFrame(frameType, length) &&
            ReadOversizedState([&](uint8_t* buffer, size_t count) { return readExact(buffer, count); }, length, payload)) {
            type = frameType;
            return true;
        }
        broken_.store(true, std::memory_order_release);
        return false;
    }
    const size_t payloadBytes = static_cast<size_t>(length - 1);
    payload.resize(payloadBytes);
    if (!readExact(payload.data(), payload.size())) return false;
    type = frameType;
    return true;
}

bool PipeChannel::HasData() const {
    if (pipe_ == INVALID_HANDLE_VALUE) { broken_.store(true, std::memory_order_release); return false; }
    DWORD available = 0;
    if (!Available(pipe_, available)) {
        broken_.store(true, std::memory_order_release);
        return false;
    }
    return available > 0;
}

void PipeChannel::Close() {
    std::lock_guard<std::mutex> lock(writeMutex_);
    if (pipe_ != INVALID_HANDLE_VALUE) {
        CloseHandle(pipe_);
        pipe_ = INVALID_HANDLE_VALUE;
    }
}

}  // namespace tfvst32
