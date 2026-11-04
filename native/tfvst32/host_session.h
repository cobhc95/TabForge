// One loaded plug-in session over a control channel and audio block. The transport is chosen by the caller; the
// command bytes, audio layout and lifetime rules are identical for named pipes/events and TCP/file flags.
#pragma once

#include <string>

#include <windows.h>

#include "bridge_block.h"
#include "control_channel.h"

namespace tfvst32 {

// Runs the command/audio loop until CmdQuit, the engine exits, or the control connection closes.
// tcpHello selects the plan's TFH1 hello (token in the environment); the named-pipe mode keeps the TF32 hello.
int RunHostSession(ControlChannel& channel, BridgeBlock& block, const std::wstring& format,
                   const std::wstring& pluginPath, const std::string& sha256, bool wantInstrument,
                   int sampleRate, int maxBlock, HANDLE engine, bool tcpHello, const std::string& tokenHex);

}  // namespace tfvst32
