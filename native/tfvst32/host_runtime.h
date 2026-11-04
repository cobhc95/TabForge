// Process-wide platform setup for the helper main/audio threads: COM/OLE lifetime and the audio-thread boost.
#pragma once

#include <windows.h>

namespace tfvst32 {

// RAII COM/OLE lifetime: every return path, including the probe's failures, uninitialises what it initialised.
class ComScope {
public:
    ComScope();
    ComScope(const ComScope&) = delete;
    ComScope& operator=(const ComScope&) = delete;
    ~ComScope();

private:
    bool comInitialised_ = false;
    bool oleInitialised_ = false;
};

// Mirror the engine's audio thread: flush denormals (FTZ+DAZ) and register with MMCSS "Pro Audio" at critical
// priority, with the highest Win32 priority as the fallback. The returned handle is reverted when the thread exits.
// realtimeScheduling: MMCSS "Pro Audio". The file/TCP transport polls its request flag with Sleep(1); an MMCSS thread's
// wake is scheduled in MMCSS periods (measured 10-15 ms here), which misses a 256-frame block. The polling helper keeps
// the high thread priority and denormal flush but not the MMCSS class; the event-driven named-pipe helper keeps both.
HANDLE BoostAudioThread(bool realtimeScheduling = true);
void ReleaseAudioThread(HANDLE mmcss);

}  // namespace tfvst32
