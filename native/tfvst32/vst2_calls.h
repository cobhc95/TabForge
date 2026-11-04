// SEH-guarded calls into plug-in function pointers. The guards contain no C++ objects that need unwinding, so MSVC
// __try/__except is allowed here; a faulting plug-in must never take the helper process down.
#pragma once

#include <cstdint>

#include "vst2_host.h"

namespace tfvst32 {
bool CreateEffectGuarded(void* entry, VstHostCallback callback, AEffect*& effect);
bool EffectMagicGuarded(AEffect* effect);
bool DispatchGuarded(AEffect* effect, int32_t opcode, int32_t index, intptr_t value, void* ptr, float opt,
                     intptr_t& result);
bool GetParameterGuarded(AEffect* effect, int32_t index, float& value);
bool SetParameterGuarded(AEffect* effect, int32_t index, float value);
}  // namespace tfvst32
