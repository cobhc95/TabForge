// tfvst32 protocol: the fixed wire and shared-block layout shared with the managed engine.
// Original TabForge code. No VST2 SDK header is included: the AEffect layout lives in vst2_host.h and is declared
// independently from the public binary interface, like the managed Vst2Plugin does.
#pragma once

#include <algorithm>
#include <chrono>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <string>
#include <vector>

namespace tfvst32 {

// ---- control channel -------------------------------------------------------------------------------------
// Frame: [int32 little-endian length][byte type][payload]; length includes the type byte.
constexpr int32_t kMaxFrameBytes = 32 * 1024 * 1024;
constexpr int32_t kMaxStateBytes = 16 * 1024 * 1024;
// A control frame is a few numbers and short texts. Only a state is larger. The managed engine enforces the same
// per-type limits (IsolatedFrameLimits/FrameLimits); the reader here must refuse an oversized frame before allocating it.
constexpr int32_t kMaxControlPayloadBytes = 1024 * 1024;
// MsgFailed text prefix for a state over kMaxStateBytes; the managed side maps it to PluginStateStatus.TooLarge.
constexpr const char* kStateTooLargePrefix = "state-too-large";

// Command ids (same values as RemotePlugin).
enum Command : uint8_t {
    CmdGetState = 1,
    CmdSetState = 2,
    CmdOpenEditor = 3,
    CmdCloseEditor = 4,
    CmdDescribe = 5,
    CmdListParams = 6,
    CmdSetParam = 7,
    CmdQuit = 9,
    CmdSetProgram = 10,
};

// Unsolicited / reply ids (same values as RemotePlugin).
enum Message : uint8_t {
    MsgLoaded = 100,
    MsgFailed = 101,
    MsgState = 102,
    MsgEditor = 103,
    MsgEdited = 104,
    MsgInfo = 105,
    MsgEditorSized = 106,
    MsgParams = 107,
    MsgHello = 108,
    MsgEditorClosed = 109,
};

// The largest payload (everything after the type byte) each message may carry. A state command/reply gets the state
// limit; every other control message gets 1 MiB. Kept in step with IsolatedFrameLimits.ForCommand/ForReply in C#.
constexpr int32_t MaxPayloadBytes(uint8_t type) {
    return type == CmdSetState || type == MsgState ? kMaxStateBytes : kMaxControlPayloadBytes;
}
// One decision shared by both transports and both directions: length includes the type byte. Checked before a payload
// buffer is allocated, so a hostile peer cannot make the helper reserve kMaxFrameBytes for a control message.
constexpr bool FrameAllowed(uint8_t type, int32_t length) {
    return length >= 1 && length <= kMaxFrameBytes &&
           static_cast<int64_t>(length - 1) <= static_cast<int64_t>(MaxPayloadBytes(type));
}

// The helper's own trust check: a "checked" launch must carry a 64-hex SHA-256; only a "location" launch may send none.
// The engine already holds the same rule (PluginIdentity), but the helper must not load unchecked because a caller skipped it.
inline bool IsSha256Hex(const std::string& text) {
    if (text.size() != 64) return false;
    for (char c : text)
        if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))) return false;
    return true;
}
inline bool HostTrustAllowed(const std::wstring& trust, const std::string& sha) {
    if (trust == L"location") return true;
    return trust == L"checked" && IsSha256Hex(sha);
}

// The one little-endian 4-byte length form both transports write and read; a bad length is refused here.
inline void EncodeHeader(uint8_t* header, int32_t length) {
    header[0] = static_cast<uint8_t>(length);
    header[1] = static_cast<uint8_t>(length >> 8);
    header[2] = static_cast<uint8_t>(length >> 16);
    header[3] = static_cast<uint8_t>(length >> 24);
}
inline bool DecodeHeader(const uint8_t* header, int32_t& length) {
    length = static_cast<int32_t>(static_cast<uint32_t>(header[0]) | (static_cast<uint32_t>(header[1]) << 8) |
                                 (static_cast<uint32_t>(header[2]) << 16) | (static_cast<uint32_t>(header[3]) << 24));
    return length >= 1 && length <= kMaxFrameBytes;
}

// A state command over kMaxStateBytes but within kMaxFrameBytes is drained and answered with the state-too-large
// failure instead of ending the session: an over-limit save must not take the helper and its plug-in down. The caller
// has already read the type byte; ReadExact must consume exactly count bytes and respect the partial-frame deadline.
constexpr bool OversizedStateFrame(uint8_t type, int32_t length) {
    return type == CmdSetState && length > MaxPayloadBytes(type) + 1 && length <= kMaxFrameBytes;
}
template <typename ReadExact>
bool ReadOversizedState(ReadExact&& readExact, int32_t length, std::vector<uint8_t>& payload) {
    if (length < 5) return false;
    uint8_t id[4] = {};
    if (!readExact(id, sizeof(id))) return false;
    int64_t remaining = static_cast<int64_t>(length) - 1 - static_cast<int64_t>(sizeof(id));
    uint8_t discard[64 * 1024];
    while (remaining > 0) {
        const size_t chunk = static_cast<size_t>(std::min<int64_t>(remaining, sizeof(discard)));
        if (!readExact(discard, chunk)) return false;
        remaining -= static_cast<int64_t>(chunk);
    }
    // The command loop reads the request id, then a length over the cap, and answers state-too-large.
    payload.resize(8);
    std::copy(id, id + sizeof(id), payload.begin());
    EncodeHeader(payload.data() + 4, kMaxStateBytes + 1);
    return true;
}

// One partial-frame deadline for both transports: no progress for 1 s or 15 s for the whole frame ends the session.
constexpr auto kPartialFrameIdle = std::chrono::milliseconds(1000);
constexpr auto kPartialFrameTotal = std::chrono::milliseconds(15000);
struct PartialFrameDeadline {
    std::chrono::steady_clock::time_point started = std::chrono::steady_clock::now();
    std::chrono::steady_clock::time_point last = started;
    void Progress() { last = std::chrono::steady_clock::now(); }
    bool Expired() const {
        const auto now = std::chrono::steady_clock::now();
        return now - started >= kPartialFrameTotal || now - last >= kPartialFrameIdle;
    }
};

// Hello frame payload (20 bytes), sent before the plug-in is loaded on the named-pipe transport.
constexpr uint32_t kHelloMagic = 0x32334654u;   // "TF32" (bytes 54 46 33 32)
// The loopback-TCP transport sends the plan's fuller hello: magic "TFH1", version, block/rate/slots, arch, flags,
// helper and Wine versions and the 32-byte session token from TABFORGE_HOST_TOKEN.
constexpr uint32_t kTcpHelloMagic = 0x31484654u;   // "TFH1" (bytes 54 46 48 31)
constexpr size_t kTokenBytes = 32;
// One source, two PEs: only the architecture-dependent text and the hello arch differ. The x86 build keeps its exact
// "tfvst32/1" hello, crash-report stem and self-test text; the x64 build reports the same protocol as "tfvst64/1".
#if defined(_WIN64)
#define TFVST_HELPER_STEM "tfvst64"
#define TFVST_HELPER_STEM_W L"tfvst64"
constexpr int32_t kBridgeArch = 64;
#else
#define TFVST_HELPER_STEM "tfvst32"
#define TFVST_HELPER_STEM_W L"tfvst32"
constexpr int32_t kBridgeArch = 32;
#endif
constexpr uint16_t kProtocolVersion = 1;
constexpr uint16_t kHeaderSize = 64;
constexpr const char* kHelperVersion = TFVST_HELPER_STEM "/1";

// ---- shared audio block ---------------------------------------------------------------------------------
constexpr int kMaxEvents = 512;
constexpr int kMinBlock = 16;
constexpr int kMaxBlock = 8192;
constexpr int kMinSampleRate = 8000;
constexpr int kMaxSampleRate = 384000;

struct BlockMidi {
    int32_t frame;
    uint8_t status;
    uint8_t data1;
    uint8_t data2;
    uint8_t pad;
};
static_assert(sizeof(BlockMidi) == 8, "BlockMidi must stay 8 bytes");

struct BlockHeader {
    int32_t frames;
    int32_t eventCount;
    double tempo;
    double ppq;
    int32_t playing;
    int32_t offline;
    int32_t requestGeneration;
    int32_t doneGeneration;
    double barStartPpq;
    int32_t timeSigNumerator;
    int32_t timeSigDenominator;
    int32_t requestFlag;
    int32_t doneFlag;
};
static_assert(sizeof(BlockHeader) == 64, "block header must stay 64 bytes");
static_assert(offsetof(BlockHeader, tempo) == 8, "tempo offset");
static_assert(offsetof(BlockHeader, barStartPpq) == 40, "bar start offset");
static_assert(offsetof(BlockHeader, timeSigNumerator) == 48, "time signature offset");
static_assert(offsetof(BlockHeader, requestFlag) == 56, "request flag offset");

constexpr size_t kEventsOffset = sizeof(BlockHeader);
constexpr size_t kChannelsOffset = kEventsOffset + static_cast<size_t>(kMaxEvents) * sizeof(BlockMidi);

constexpr size_t BlockSize(int maxBlock, int slots) {
    return kChannelsOffset + static_cast<size_t>(slots) * 4u * static_cast<size_t>(maxBlock) * sizeof(float);
}

// Per-slot channel order: 0 = in L, 1 = in R, 2 = out L, 3 = out R.
constexpr int kChannelInL = 0, kChannelInR = 1, kChannelOutL = 2, kChannelOutR = 3;

// ---- .NET BinaryWriter primitives ------------------------------------------------------------------------
// A string is a 7-bit encoded UTF-8 byte length followed by UTF-8 bytes.
class Writer {
public:
    void U8(uint8_t value) { data_.push_back(value); }
    void Bool(bool value) { U8(value ? 1u : 0u); }
    void U16(uint16_t value) {
        data_.push_back(static_cast<uint8_t>(value));
        data_.push_back(static_cast<uint8_t>(value >> 8));
    }
    void U32(uint32_t value) {
        data_.push_back(static_cast<uint8_t>(value));
        data_.push_back(static_cast<uint8_t>(value >> 8));
        data_.push_back(static_cast<uint8_t>(value >> 16));
        data_.push_back(static_cast<uint8_t>(value >> 24));
    }
    void I32(int32_t value) { U32(static_cast<uint32_t>(value)); }
    void Double(double value) {
        uint64_t bits = 0;
        static_assert(sizeof(bits) == sizeof(value), "double size");
        memcpy(&bits, &value, sizeof(bits));
        U32(static_cast<uint32_t>(bits));
        U32(static_cast<uint32_t>(bits >> 32));
    }
    void String(const std::string& text) {
        size_t length = text.size();
        while (length >= 0x80) {
            U8(static_cast<uint8_t>((length & 0x7F) | 0x80));
            length >>= 7;
        }
        U8(static_cast<uint8_t>(length));
        data_.insert(data_.end(), text.begin(), text.end());
    }
    const std::vector<uint8_t>& Data() const { return data_; }
    std::vector<uint8_t>& Data() { return data_; }

private:
    std::vector<uint8_t> data_;
};

class Reader {
public:
    Reader(const uint8_t* data, size_t size) : data_(data), size_(size) {}
    bool Ok() const { return ok_; }
    size_t Remaining() const { return ok_ ? size_ - pos_ : 0; }
    uint8_t U8() {
        if (!Need(1)) return 0;
        return data_[pos_++];
    }
    bool Bool() { return U8() != 0; }
    uint32_t U32() {
        if (!Need(4)) return 0;
        uint32_t value = static_cast<uint32_t>(data_[pos_]) |
                         (static_cast<uint32_t>(data_[pos_ + 1]) << 8) |
                         (static_cast<uint32_t>(data_[pos_ + 2]) << 16) |
                         (static_cast<uint32_t>(data_[pos_ + 3]) << 24);
        pos_ += 4;
        return value;
    }
    int32_t I32() { return static_cast<int32_t>(U32()); }
    double Double() {
        uint64_t value = U32();
        value |= static_cast<uint64_t>(U32()) << 32;
        double result = 0;
        memcpy(&result, &value, sizeof(result));
        return result;
    }
    std::vector<uint8_t> Bytes(size_t count) {
        std::vector<uint8_t> result;
        if (!Need(count)) return result;
        result.assign(data_ + pos_, data_ + pos_ + count);
        pos_ += count;
        return result;
    }
    std::string String() {
        size_t length = 0, shift = 0;
        for (int i = 0; i < 5; i++) {
            uint8_t byte = U8();
            if (!ok_) return {};
            length |= static_cast<size_t>(byte & 0x7F) << shift;
            if ((byte & 0x80) == 0) {
                if (!Need(length)) return {};
                std::string text(reinterpret_cast<const char*>(data_ + pos_), length);
                pos_ += length;
                return text;
            }
            shift += 7;
        }
        ok_ = false;
        return {};
    }

private:
    bool Need(size_t count) {
        if (!ok_ || count > size_ - pos_) {
            ok_ = false;
            return false;
        }
        return true;
    }

    const uint8_t* data_;
    size_t size_;
    size_t pos_ = 0;
    bool ok_ = true;
};

}  // namespace tfvst32
