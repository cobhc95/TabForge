#include "vst2_calls.h"

namespace tfvst32 {
namespace {
constexpr int32_t kEffectMagic = 0x56737450;   // 'VstP'
using DispatchProc = intptr_t (VST_CALLBACK*)(AEffect*, int32_t, int32_t, intptr_t, void*, float);
using EntryPoint = AEffect* (VST_CALLBACK*)(VstHostCallback);
using GetProc = float (VST_CALLBACK*)(AEffect*, int32_t);
using SetProc = void (VST_CALLBACK*)(AEffect*, int32_t, float);
}  // namespace

bool CreateEffectGuarded(void* entry, VstHostCallback callback, AEffect*& effect) {
    __try {
        effect = reinterpret_cast<EntryPoint>(entry)(callback);
        return true;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        effect = nullptr;
        return false;
    }
}

bool EffectMagicGuarded(AEffect* effect) {
    __try {
        return effect != nullptr && effect->magic == kEffectMagic;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return false;
    }
}

bool DispatchGuarded(AEffect* effect, int32_t opcode, int32_t index, intptr_t value, void* ptr, float opt,
                     intptr_t& result) {
    __try {
        if (effect == nullptr || effect->dispatcher == 0) { result = 0; return true; }
        result = reinterpret_cast<DispatchProc>(effect->dispatcher)(effect, opcode, index, value, ptr, opt);
        return true;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        result = 0;
        return false;
    }
}

bool GetParameterGuarded(AEffect* effect, int32_t index, float& value) {
    __try {
        if (effect == nullptr || effect->getParameter == 0) { value = 0.0f; return true; }
        value = reinterpret_cast<GetProc>(effect->getParameter)(effect, index);
        return true;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        value = 0.0f;
        return false;
    }
}

bool SetParameterGuarded(AEffect* effect, int32_t index, float value) {
    __try {
        if (effect == nullptr || effect->setParameter == 0) return false;
        reinterpret_cast<SetProc>(effect->setParameter)(effect, index, value);
        return true;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return false;
    }
}

}  // namespace tfvst32
