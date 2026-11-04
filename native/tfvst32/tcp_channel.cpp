#include "tcp_channel.h"

#include <algorithm>
#include <chrono>

#include <winsock2.h>
#include <ws2tcpip.h>

#include "tfvst32_protocol.h"

namespace tfvst32 {
namespace {
constexpr int kSendTimeoutMs = 5000;

SOCKET AsSocket(uintptr_t value) { return static_cast<SOCKET>(value); }
uintptr_t AsValue(SOCKET socket) { return static_cast<uintptr_t>(socket); }

bool WaitFor(SOCKET socket, bool writable, int timeoutMs) {
    fd_set set;
    FD_ZERO(&set);
    FD_SET(socket, &set);
    timeval timeout{};
    timeout.tv_sec = timeoutMs / 1000;
    timeout.tv_usec = (timeoutMs % 1000) * 1000;
    const int result = select(0, writable ? nullptr : &set, writable ? &set : nullptr, nullptr, &timeout);
    return result > 0;
}
}  // namespace

bool TcpChannel::Connect(int port, int timeoutMs, std::string& error) {
    Close();
    broken_.store(false, std::memory_order_release);
    if (port < 1 || port > 65535) {
        error = "bad control port " + std::to_string(port);
        return false;
    }
    WSADATA data{};
    if (WSAStartup(MAKEWORD(2, 2), &data) != 0) {
        error = "Winsock could not start (error " + std::to_string(WSAGetLastError()) + ")";
        return false;
    }
    winsock_ = true;
    SOCKET socket = ::socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    if (socket == INVALID_SOCKET) {
        error = "could not create the control socket (error " + std::to_string(WSAGetLastError()) + ")";
        Close();
        return false;
    }
    u_long nonBlocking = 1;
    ioctlsocket(socket, FIONBIO, &nonBlocking);

    sockaddr_in address{};
    address.sin_family = AF_INET;
    address.sin_port = htons(static_cast<u_short>(port));
    inet_pton(AF_INET, "127.0.0.1", &address.sin_addr);
    const int connected = connect(socket, reinterpret_cast<sockaddr*>(&address), sizeof(address));
    if (connected == SOCKET_ERROR) {
        const int code = WSAGetLastError();
        if (code != WSAEWOULDBLOCK || !WaitFor(socket, true, timeoutMs)) {
            error = "the plug-in host control port did not accept within " + std::to_string(timeoutMs) + " ms";
            closesocket(socket);
            Close();
            return false;
        }
        int soError = 0;
        int length = sizeof(soError);
        if (getsockopt(socket, SOL_SOCKET, SO_ERROR, reinterpret_cast<char*>(&soError), &length) != 0 || soError != 0) {
            error = "the plug-in host control connection failed (error " + std::to_string(soError) + ")";
            closesocket(socket);
            Close();
            return false;
        }
    }
    nonBlocking = 0;
    ioctlsocket(socket, FIONBIO, &nonBlocking);
    DWORD timeout = kSendTimeoutMs;
    setsockopt(socket, SOL_SOCKET, SO_SNDTIMEO, reinterpret_cast<const char*>(&timeout), sizeof(timeout));
    // Control frames are small except state; disabling Nagle keeps Quit/Edited immediate.
    BOOL noDelay = TRUE;
    setsockopt(socket, IPPROTO_TCP, TCP_NODELAY, reinterpret_cast<const char*>(&noDelay), sizeof(noDelay));
    socket_ = AsValue(socket);
    return true;
}

bool TcpChannel::Write(uint8_t type, const std::vector<uint8_t>& payload) {
    std::lock_guard<std::mutex> lock(writeMutex_);
    if (!IsOpen()) { Fail(); return false; }
    if (payload.size() >= static_cast<size_t>(kMaxFrameBytes)) return false;
    const int32_t length = static_cast<int32_t>(payload.size() + 1);
    if (!FrameAllowed(type, length)) return false;
    std::vector<uint8_t> frame(static_cast<size_t>(length) + 4);
    EncodeHeader(frame.data(), length);
    frame[4] = type;
    if (!payload.empty()) std::copy(payload.begin(), payload.end(), frame.begin() + 5);
    size_t sent = 0;
    while (sent < frame.size()) {
        const int result = send(AsSocket(socket_), reinterpret_cast<const char*>(frame.data() + sent),
                                static_cast<int>(frame.size() - sent), 0);
        if (result <= 0) { Fail(); return false; }
        sent += static_cast<size_t>(result);
    }
    return true;
}

bool TcpChannel::ReadExact(uint8_t* buffer, size_t count, PartialFrameDeadline& deadline) {
    size_t got = 0;
    while (got < count) {
        if (!IsOpen()) return false;
        if (deadline.Expired()) return false;
        if (!WaitFor(AsSocket(socket_), false, 1)) continue;
        const int result = recv(AsSocket(socket_), reinterpret_cast<char*>(buffer + got), static_cast<int>(count - got), 0);
        if (result == 0) return false;   // the engine closed its end
        if (result == SOCKET_ERROR) {
            if (WSAGetLastError() == WSAEWOULDBLOCK) continue;
            return false;
        }
        got += static_cast<size_t>(result);
        deadline.Progress();
    }
    return true;
}

bool TcpChannel::Read(uint8_t& type, std::vector<uint8_t>& payload) {
    if (!IsOpen()) { Fail(); return false; }
    PartialFrameDeadline deadline;
    uint8_t header[4] = {};
    if (!ReadExact(header, sizeof(header), deadline)) { Fail(); return false; }
    int32_t length = 0;
    if (!DecodeHeader(header, length)) { Fail(); return false; }
    // Read the type before the payload so an oversized control frame is refused without allocating its length first.
    uint8_t frameType = 0;
    if (!ReadExact(&frameType, 1, deadline)) { Fail(); return false; }
    if (!FrameAllowed(frameType, length)) {
        // An over-limit state is drained and answered, not fatal; every other oversized frame still ends the session.
        if (OversizedStateFrame(frameType, length) &&
            ReadOversizedState([&](uint8_t* buffer, size_t count) { return ReadExact(buffer, count, deadline); }, length, payload)) {
            type = frameType;
            return true;
        }
        Fail();
        return false;
    }
    const size_t payloadBytes = static_cast<size_t>(length - 1);
    payload.resize(payloadBytes);
    if (!ReadExact(payload.data(), payload.size(), deadline)) { Fail(); return false; }
    type = frameType;
    return true;
}

bool TcpChannel::HasData() const {
    if (!IsOpen()) { broken_.store(true, std::memory_order_release); return false; }
    // FIONREAD is 0 both for "no data yet" and for a peer that closed or reset the connection. Select plus a peek
    // distinguishes them: a readable socket with recv(MSG_PEEK) == 0 (or a connection error) means the engine is gone,
    // which the helper must notice even when the engine is killed without CmdQuit.
    fd_set read;
    FD_ZERO(&read);
    FD_SET(AsSocket(socket_), &read);
    timeval timeout{};
    const int selected = select(0, &read, nullptr, nullptr, &timeout);
    if (selected == SOCKET_ERROR) { broken_.store(true, std::memory_order_release); return false; }
    if (selected == 0) return false;
    char peeked = 0;
    const int result = recv(AsSocket(socket_), &peeked, 1, MSG_PEEK);
    if (result == 0) { broken_.store(true, std::memory_order_release); return false; }   // orderly close: EOF
    if (result == SOCKET_ERROR) {
        if (WSAGetLastError() != WSAEWOULDBLOCK) broken_.store(true, std::memory_order_release);
        return false;
    }
    return true;
}

void TcpChannel::Close() {
    std::lock_guard<std::mutex> lock(writeMutex_);
    if (IsOpen()) {
        SOCKET socket = AsSocket(socket_);
        shutdown(socket, SD_BOTH);
        closesocket(socket);
        socket_ = static_cast<uintptr_t>(~0ull);
    }
    if (winsock_) {
        WSACleanup();
        winsock_ = false;
    }
}

}  // namespace tfvst32
