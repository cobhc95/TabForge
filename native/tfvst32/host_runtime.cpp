#include "host_runtime.h"

#include <float.h>

#include <avrt.h>
#include <objbase.h>
#include <ole2.h>

namespace tfvst32 {

ComScope::ComScope() {
    const HRESULT com = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    comInitialised_ = com == S_OK || com == S_FALSE;
    const HRESULT ole = OleInitialize(nullptr);
    oleInitialised_ = ole == S_OK || ole == S_FALSE;
}

ComScope::~ComScope() {
    if (oleInitialised_) OleUninitialize();
    if (comInitialised_) CoUninitialize();
}

HANDLE BoostAudioThread(bool realtimeScheduling) {
    unsigned int control = 0;
    _controlfp_s(&control, _DN_FLUSH, _MCW_DN);
    SetThreadPriority(GetCurrentThread(), THREAD_PRIORITY_HIGHEST);
    if (!realtimeScheduling) return nullptr;
    DWORD taskIndex = 0;
    HANDLE mmcss = AvSetMmThreadCharacteristicsW(L"Pro Audio", &taskIndex);
    if (mmcss != nullptr) AvSetMmThreadPriority(mmcss, AVRT_PRIORITY_CRITICAL);
    return mmcss;
}

void ReleaseAudioThread(HANDLE mmcss) {
    if (mmcss != nullptr) AvRevertMmThreadCharacteristics(mmcss);
}

}  // namespace tfvst32
