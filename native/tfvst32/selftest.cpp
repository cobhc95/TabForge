#include <cstdio>
#include <cstring>
#include <string>

#include <windows.h>

#include "bridge_block.h"
#include "tfvst32_protocol.h"
#include "vst3_host.h"

using namespace tfvst32;

namespace {
bool Check(bool condition, const char* what) {
    if (!condition) std::fprintf(stderr, "selftest failed: %s\n", what);
    return condition;
}
}  // namespace

int RunSelfTest() {
    bool ok = true;
    ok &= Check(BlockSize(256, 1) == 8256, "one-slot block size");
    ok &= Check(BlockSize(256, 2) == 12352, "two-slot block size");
    ok &= Check(BlockSize(512, 2) == 20544, "512-frame two-slot block size");
    ok &= Check(sizeof(BlockHeader) == 64, "header size");
    ok &= Check(sizeof(BlockMidi) == 8, "event size");
    ok &= Check(kChannelsOffset == 4160, "channel offset");
    ok &= Check(kTcpHelloMagic == 0x31484654u, "TFH1 hello magic");
    ok &= Check(std::string(kHelperVersion) == std::string(TFVST_HELPER_STEM) + "/1", "helper version");
    ok &= Check(kBridgeArch == (sizeof(void*) == 8 ? 64 : 32), "hello arch matches the PE");
    // Per-type frame limits (Stage 13 A2): a state may be large, every other message is a control message. The readers
    // check this before allocating; the managed side has the same table.
    ok &= Check(MaxPayloadBytes(CmdSetState) == kMaxStateBytes, "state command cap");
    ok &= Check(MaxPayloadBytes(MsgState) == kMaxStateBytes, "state reply cap");
    ok &= Check(MaxPayloadBytes(CmdQuit) == kMaxControlPayloadBytes, "quit cap");
    ok &= Check(MaxPayloadBytes(MsgHello) == kMaxControlPayloadBytes, "hello cap");
    ok &= Check(!FrameAllowed(CmdQuit, 2 * 1024 * 1024), "an oversized control frame must be refused");
    ok &= Check(FrameAllowed(CmdQuit, kMaxControlPayloadBytes + 1), "a control frame at its cap must be accepted");
    ok &= Check(FrameAllowed(CmdSetState, kMaxStateBytes + 1), "a state frame at its cap must be accepted");
    ok &= Check(!FrameAllowed(CmdSetState, kMaxStateBytes + 2), "a state frame over its cap must be refused");
    ok &= Check(!FrameAllowed(MsgState, kMaxFrameBytes), "a state frame over kMaxFrameBytes must be refused");
    // An over-limit state command is drained and answered instead of ending the session; other oversized frames are fatal.
    ok &= Check(OversizedStateFrame(CmdSetState, kMaxStateBytes + 2), "an over-limit state is drained, not fatal");
    ok &= Check(!OversizedStateFrame(CmdQuit, kMaxControlPayloadBytes + 2), "an over-limit control frame stays fatal");
    uint8_t header[4] = {};
    int32_t decoded = 0;
    EncodeHeader(header, 0x00123456);
    ok &= Check(header[0] == 0x56 && header[1] == 0x34 && header[2] == 0x12 && header[3] == 0x00, "frame header is little-endian");
    ok &= Check(DecodeHeader(header, decoded) && decoded == 0x00123456, "frame header round trip");
    EncodeHeader(header, 0);
    ok &= Check(!DecodeHeader(header, decoded), "a zero-length frame is refused");
    EncodeHeader(header, kMaxFrameBytes + 1);
    ok &= Check(!DecodeHeader(header, decoded), "a frame over kMaxFrameBytes is refused");
    // A checked launch may not skip the hash; only location trust may (Stage 13 A4).
    ok &= Check(!HostTrustAllowed(L"checked", "none"), "a checked launch must refuse 'none'");
    ok &= Check(!HostTrustAllowed(L"checked", ""), "a checked launch must refuse an empty hash");
    ok &= Check(!HostTrustAllowed(L"", std::string(64, 'a')), "an unknown trust kind is refused");
    ok &= Check(HostTrustAllowed(L"checked", std::string(64, 'a')), "a checked launch accepts 64 hex characters");
    ok &= Check(HostTrustAllowed(L"location", "none"), "location trust may send no hash");
#ifdef TFVST32_TEST_HOOKS
    ok &= Check(Vst3EditorGuardSelfTest(), "VST3 editor fault guard");
#endif

    Writer writer;
    writer.I32(-123456); writer.Double(1.25); writer.String("hello");
    Reader reader(writer.Data().data(), writer.Data().size());
    ok &= Check(reader.I32() == -123456, "int round trip");
    ok &= Check(reader.Double() == 1.25, "double round trip");
    ok &= Check(reader.String() == "hello", "string round trip");
    ok &= Check(reader.Ok() && reader.Remaining() == 0, "reader consumed");

    wchar_t id[33] = {};
    std::swprintf(id, 33, L"%08lx%08lx%016llx", GetCurrentProcessId(), GetCurrentThreadId(),
                  static_cast<unsigned long long>(GetTickCount64()));
    const int maxBlock = 256, slots = 2;
    const size_t size = BlockSize(maxBlock, slots);
    const std::wstring name = L"Local\\TabForge.PluginHost." + std::wstring(id);
    HANDLE mapping = CreateFileMappingW(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE, 0,
                                        static_cast<DWORD>(size), name.c_str());
    HANDLE request = CreateEventW(nullptr, FALSE, FALSE, (name + L".req").c_str());
    HANDLE done = CreateEventW(nullptr, FALSE, FALSE, (name + L".done").c_str());
    ok &= Check(mapping != nullptr && request != nullptr && done != nullptr, "selftest named objects");
    if (mapping != nullptr && request != nullptr && done != nullptr) {
        BridgeBlock block;
        std::string error;
        ok &= Check(block.Open(id, maxBlock, slots, error), "open selftest block");
        if (block.IsOpen()) {
            ok &= Check(block.MappedBytes() == size, "mapped size");
            block.Header()->frames = maxBlock;
            block.Header()->eventCount = 1;
            block.Events()[0] = BlockMidi{37, 0x90, 60, 100, 0};
            float* slot0 = block.Channel(kChannelOutL, 0);
            float* slot1 = block.Channel(kChannelOutL, 1);
            slot0[0] = 1.0f; slot1[0] = 2.0f;
            ok &= Check(slot1 - slot0 == 4 * maxBlock, "two-slot parity offset");
            ok &= Check(block.Channel(kChannelInL, 0) + 0 == reinterpret_cast<float*>(
                            reinterpret_cast<uint8_t*>(block.Header()) + kChannelsOffset), "channel base");
            block.Complete(9);
            ok &= Check(block.Header()->doneGeneration == 9, "done generation");
            ok &= Check(WaitForSingleObject(done, 1000) == WAIT_OBJECT_0, "done signal");
            block.Close();
        }
    }
    if (request != nullptr) CloseHandle(request);
    if (done != nullptr) CloseHandle(done);
    if (mapping != nullptr) CloseHandle(mapping);

    // File-backed block: the same header, with the request/done ints polled instead of events (the Wine transport).
    wchar_t tempFolder[512] = {};
    wchar_t tempPath[512] = {};
    if (GetTempPathW(512, tempFolder) > 0 && GetTempFileNameW(tempFolder, L"tfv", 0, tempPath) != 0) {
        HANDLE file = CreateFileW(tempPath, GENERIC_READ | GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS,
                                  FILE_ATTRIBUTE_NORMAL, nullptr);
        LARGE_INTEGER length{};
        length.QuadPart = static_cast<LONGLONG>(size);
        if (file != INVALID_HANDLE_VALUE) {
            ok &= Check(SetFilePointerEx(file, length, nullptr, FILE_BEGIN) != 0, "file block seek");
            ok &= Check(SetEndOfFile(file) != 0, "file block size");
            CloseHandle(file);
        }
        BridgeBlock fileBlock;
        std::string error;
        ok &= Check(fileBlock.OpenFile(tempPath, maxBlock, slots, error), "open file block");
        if (fileBlock.IsOpen()) {
            ok &= Check(fileBlock.IsFileBacked(), "file mode flag");
            ok &= Check(fileBlock.MappedBytes() == size, "file mapped size");
            ok &= Check(fileBlock.MaxBlock() == maxBlock && fileBlock.Slots() == slots, "file block arguments");
            fileBlock.Header()->requestGeneration = 17;
            fileBlock.Header()->requestFlag = 1;
            int generation = 0;
            ok &= Check(fileBlock.TakeRequest(1000, generation) && generation == 17, "file request flag");
            fileBlock.Complete(17);
            ok &= Check(fileBlock.Header()->doneGeneration == 17 && fileBlock.Header()->doneFlag == 1, "file done flag");
            fileBlock.Close();
        }
        DeleteFileW(tempPath);
    }
    if (ok) std::printf(TFVST_HELPER_STEM " selftest: ok\n");
    return ok ? 0 : 1;
}
