// Crash reporting for the 32-bit helper: one text line and a minidump, then the process ends. The engine treats that as
// an isolated plug-in crash and keeps running; TabForge shows where the report is.
#pragma once

#include <string>

namespace tfvst32 {

/// <summary>Installs the process-wide unhandled-exception filter (call once, before any window or plug-in runs).</summary>
void InstallCrashHandler();

/// <summary>Names the plug-in file the report should mention (the failed path, not necessarily the faulting module).</summary>
void SetCrashPluginPath(const std::wstring& path);

}  // namespace tfvst32
