#include "bridge_block.h"

#include <atomic>
#include <chrono>
#include <thread>

namespace tfvst32 {
namespace {
bool ValidateSizes(int maxBlock, int slots, std::string& error) {
    if (maxBlock < kMinBlock || maxBlock > kMaxBlock || slots < 1 || slots > 2) {
        error = "bad block size or slot count";
        return false;
    }
    return true;
}
}  // namespace

bool BridgeBlock::Open(const std::wstring& id, int maxBlock, int slots, std::string& error) {
    Close();
    if (!ValidateSizes(maxBlock, slots, error)) return false;
    maxBlock_ = maxBlock;
    slots_ = slots;
    bytes_ = BlockSize(maxBlock, slots);
    const std::wstring mappingName = L"Local\\TabForge.PluginHost." + id;
    mapping_ = OpenFileMappingW(FILE_MAP_READ | FILE_MAP_WRITE, FALSE, mappingName.c_str());
    if (mapping_ == nullptr) {
        error = "the shared audio block is missing (error " + std::to_string(GetLastError()) + ")";
        return false;
    }
    base_ = static_cast<uint8_t*>(MapViewOfFile(mapping_, FILE_MAP_READ | FILE_MAP_WRITE, 0, 0, bytes_));
    if (base_ == nullptr) {
        error = "the shared audio block could not be mapped (error " + std::to_string(GetLastError()) + ")";
        Close();
        return false;
    }
    request_ = OpenEventW(SYNCHRONIZE | EVENT_MODIFY_STATE, FALSE, (mappingName + L".req").c_str());
    done_ = OpenEventW(SYNCHRONIZE | EVENT_MODIFY_STATE, FALSE, (mappingName + L".done").c_str());
    if (request_ == nullptr || done_ == nullptr) {
        error = "the plug-in host events are missing (error " + std::to_string(GetLastError()) + ")";
        Close();
        return false;
    }
    header_ = reinterpret_cast<BlockHeader*>(base_);
    return true;
}

bool BridgeBlock::OpenFile(const std::wstring& path, int maxBlock, int slots, std::string& error) {
    Close();
    if (!ValidateSizes(maxBlock, slots, error)) return false;
    maxBlock_ = maxBlock;
    slots_ = slots;
    bytes_ = BlockSize(maxBlock, slots);
    file_ = CreateFileW(path.c_str(), GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                        nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file_ == INVALID_HANDLE_VALUE) {
        file_ = nullptr;
        error = "the shared audio block file is missing (error " + std::to_string(GetLastError()) + ")";
        return false;
    }
    LARGE_INTEGER length{};
    if (!GetFileSizeEx(file_, &length) || length.QuadPart != static_cast<LONGLONG>(bytes_)) {
        error = "the shared audio block file has the wrong size (expected " + std::to_string(bytes_) + " bytes)";
        Close();
        return false;
    }
    mapping_ = CreateFileMappingW(file_, nullptr, PAGE_READWRITE, 0, static_cast<DWORD>(bytes_), nullptr);
    if (mapping_ == nullptr) {
        error = "the shared audio block file could not be mapped (error " + std::to_string(GetLastError()) + ")";
        Close();
        return false;
    }
    base_ = static_cast<uint8_t*>(MapViewOfFile(mapping_, FILE_MAP_READ | FILE_MAP_WRITE, 0, 0, bytes_));
    if (base_ == nullptr) {
        error = "the shared audio block file view failed (error " + std::to_string(GetLastError()) + ")";
        Close();
        return false;
    }
    fileMode_ = true;
    header_ = reinterpret_cast<BlockHeader*>(base_);
    return true;
}

void BridgeBlock::Close() {
    if (base_ != nullptr) {
        UnmapViewOfFile(base_);
        base_ = nullptr;
        header_ = nullptr;
    }
    if (request_ != nullptr) { CloseHandle(request_); request_ = nullptr; }
    if (done_ != nullptr) { CloseHandle(done_); done_ = nullptr; }
    if (mapping_ != nullptr) { CloseHandle(mapping_); mapping_ = nullptr; }
    if (file_ != nullptr) { CloseHandle(file_); file_ = nullptr; }
    bytes_ = 0;
    maxBlock_ = 0;
    slots_ = 0;
    fileMode_ = false;
}

float* BridgeBlock::Channel(int index, int generation) const {
    // Two slots alternate by generation parity; one slot is always slot 0 (the stage-3 managed block).
    const int slot = slots_ > 1 ? (generation & 1) : 0;
    const size_t offset = kChannelsOffset +
        static_cast<size_t>(slot) * 4u * static_cast<size_t>(maxBlock_) * sizeof(float) +
        static_cast<size_t>(index) * static_cast<size_t>(maxBlock_) * sizeof(float);
    return reinterpret_cast<float*>(base_ + offset);
}

bool BridgeBlock::TakeRequest(int timeoutMs, int& generation) {
    generation = 0;
    if (header_ == nullptr) return false;
    if (!fileMode_) {
        if (request_ == nullptr) return false;
        if (WaitForSingleObject(request_, static_cast<DWORD>(timeoutMs)) != WAIT_OBJECT_0) return false;
        // Read the generation first, then acquire: the fence orders the payload loads that follow it after this load,
        // which synchronises with the release fence/store on the engine side.
        generation = header_->requestGeneration;
        std::atomic_thread_fence(std::memory_order_acquire);
        return true;
    }
    const auto started = std::chrono::steady_clock::now();
    for (;;) {
        if (header_->requestFlag != 0) {
            header_->requestFlag = 0;
            generation = header_->requestGeneration;
            std::atomic_thread_fence(std::memory_order_acquire);
            return true;
        }
        const auto waited = std::chrono::steady_clock::now() - started;
        if (timeoutMs >= 0 && waited >= std::chrono::milliseconds(timeoutMs)) return false;
        if (waited < std::chrono::microseconds(200)) std::this_thread::yield();
        else Sleep(1);
    }
}

void BridgeBlock::Complete(int generation) {
    if (header_ == nullptr) return;
    std::atomic_thread_fence(std::memory_order_release);
    header_->doneGeneration = generation;
    if (fileMode_) {
        header_->doneFlag = 1;
        return;
    }
    if (done_ != nullptr) SetEvent(done_);
}

}  // namespace tfvst32
