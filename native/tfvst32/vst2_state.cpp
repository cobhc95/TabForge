// VST2 state: chunk (program/bank) and parameter-list state, with the 16 MiB contract.
// Runs on the helper main thread only, never the audio thread.
#include "vst2_host.h"

#include <cmath>
#include <cstring>

#include "vst2_calls.h"

namespace tfvst32 {
namespace {
constexpr int kEffGetChunk = 23, kEffSetChunk = 24;
constexpr uint8_t kParameterListMark[8] = {0xEF, 0xBE, 0xAD, 0xDE, 0x0D, 0xF0, 0xAD, 0xDE};
// Chunk state envelope: "TF32", the chunk index (1 = current program, 0 = bank), then the plug-in's bytes. VST2
// splits chunk state into a bank (0) and a program (1); some plug-ins (TAL) only apply the program chunk to the
// live sound, so the index must travel with the saved state.
constexpr uint8_t kChunkStateMagic[4] = {'T', 'F', '3', '2'};
constexpr size_t kChunkStateHeader = 5;

// The plug-in owns the chunk pointer; fail closed when it is unreadable instead of faulting the helper main thread.
bool CopyChunkGuarded(void* destination, const void* source, size_t bytes) {
    __try {
        std::memcpy(destination, source, bytes);
        return true;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return false;
    }
}

// effGetChunk runs under SEH too: a plug-in fault while it hands out chunk state fails closed, never kills the helper.
bool GetChunkGuarded(AEffect* effect, int index, void*& data, intptr_t& size) {
    using DispatchProc = intptr_t (VST_CALLBACK*)(AEffect*, int32_t, int32_t, intptr_t, void*, float);
    if (effect->dispatcher == 0) return true;   // no dispatcher: no chunk, same as Dispatch() returning 0
    auto dispatch = reinterpret_cast<DispatchProc>(effect->dispatcher);
    __try {
        size = dispatch(effect, kEffGetChunk, index, 0, &data, 0);
        return true;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return false;
    }
}
}  // namespace

bool Vst2Host::GetState(std::vector<uint8_t>& state) {
    state.clear();
    stateTooLarge_ = false;
    if (effect_ == nullptr) return false;
    std::lock_guard<std::mutex> lock(pluginMutex_);   // same lock as the editor and processReplacing
    if (UsesChunks()) {
        // Prefer the current program chunk (index 1): it is the state that restores the live sound. Fall back to the
        // bank chunk (index 0) for plug-ins that implement only one chunk.
        int index = 1;
        void* data = nullptr;
        intptr_t size = 0;
        if (!GetChunkGuarded(effect_, index, data, size)) { state.clear(); return false; }
        if (size <= 0 || data == nullptr) {
            index = 0;
            data = nullptr;
            size = 0;
            if (!GetChunkGuarded(effect_, index, data, size)) { state.clear(); return false; }
        }
        if (size > kMaxStateBytes) { stateTooLarge_ = true; return false; }
        if (size <= 0 || data == nullptr || size + static_cast<intptr_t>(kChunkStateHeader) > kMaxStateBytes) return false;
        state.resize(kChunkStateHeader + static_cast<size_t>(size));
        std::memcpy(state.data(), kChunkStateMagic, sizeof(kChunkStateMagic));
        state[4] = static_cast<uint8_t>(index);
        if (!CopyChunkGuarded(state.data() + kChunkStateHeader, data, static_cast<size_t>(size))) {
            state.clear();   // fail closed: no stale or partial state is ever returned
            return false;
        }
        return true;
    }
    const int count = ParameterCount();
    if (count == 0 || effect_->getParameter == 0) return false;
    state.assign(kParameterListMark, kParameterListMark + sizeof(kParameterListMark));
    state.resize(sizeof(kParameterListMark) + static_cast<size_t>(count) * sizeof(float));
    for (int i = 0; i < count; i++) {
        float value = 0.0f;
        if (!GetParameterGuarded(effect_, i, value)) {
            state.clear();   // fail closed: never return a partially read parameter state
            return false;
        }
        std::memcpy(state.data() + sizeof(kParameterListMark) + static_cast<size_t>(i) * sizeof(float), &value, sizeof(value));
    }
    return true;
}

bool Vst2Host::SetState(const std::vector<uint8_t>& state) {
    if (effect_ == nullptr || state.empty() || state.size() > static_cast<size_t>(kMaxStateBytes)) return false;
    std::lock_guard<std::mutex> lock(pluginMutex_);   // a chunk restore never races the editor or processReplacing
    if (UsesChunks()) {
        int index = 0;
        const uint8_t* data = state.data();
        size_t size = state.size();
        if (state.size() >= kChunkStateHeader && std::memcmp(state.data(), kChunkStateMagic, sizeof(kChunkStateMagic)) == 0) {
            index = state[4] <= 1 ? state[4] : 0;
            data += kChunkStateHeader;
            size -= kChunkStateHeader;
        }
        if (size == 0) return false;
        Dispatch(kEffSetChunk, index, static_cast<intptr_t>(size), const_cast<uint8_t*>(data), 0);
        return true;
    }
    if (state.size() < 12 || (state.size() - sizeof(kParameterListMark)) % sizeof(float) != 0 ||
        std::memcmp(state.data(), kParameterListMark, sizeof(kParameterListMark)) != 0) return false;
    if (effect_->setParameter == 0) return true;
    const size_t count = std::min((state.size() - sizeof(kParameterListMark)) / sizeof(float),
                                  static_cast<size_t>(ParameterCount()));
    for (size_t i = 0; i < count; i++) {
        float value = 0;
        std::memcpy(&value, state.data() + sizeof(kParameterListMark) + i * sizeof(float), sizeof(value));
        if (std::isfinite(value) && std::fabs(value) <= 1e6f &&
            !SetParameterGuarded(effect_, static_cast<int32_t>(i), value)) return false;
    }
    return true;
}

}  // namespace tfvst32
