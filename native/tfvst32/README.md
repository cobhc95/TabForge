# tfvst32 / tfvst64: the Windows VST2 helpers

One source tree builds two PEs: `tfvst32.exe` (x86, the Windows and Wine WoW64 helper) and `tfvst64.exe` (x64, the
Linux/Wine route for 64-bit Windows VST2; Windows itself loads 64-bit VST2 natively). Each hosts exactly one VST2 plug-in
in its own process so the engine can load it through the existing isolation proxy. It is original TabForge code: the
minimal `AEffect`/opcode declarations are hand-written from the public binary interface, no Steinberg VST2 SDK header and
no third-party library is used.

## Build

Requires Visual Studio 18 with the x86 and x64 C++ tools (`Hostx64\x86\cl.exe`, `Hostx64\x64\cl.exe`) and a Windows
10/11 SDK with both library sets.

```powershell
powershell -ExecutionPolicy Bypass -File Source\native\build-tfvst32.ps1
```

The script builds the same sources twice, pins and records the compiler, SDK, CMake and generator in
`Source\native\BUILD_PROVENANCE32.md` and `BUILD_PROVENANCE64.md`, verifies the PE machines (`0x14C` and `0x8664`), runs
both native self-tests, and copies `tfvst32.exe`/`tfvst64.exe` to `Source\src\TabForge.AudioEngine\native\`. The
deterministic `passthrough32.dll`/`passthrough64.dll`, `tfvst32test.exe`/`tfvst64test.exe` and the separate test copies
`tfvst32-testhooks.exe`/`tfvst64-testhooks.exe` go to `Source\native\tfvst32\test-bin\`; they are test-only and never
shipped. Use `-BuildDir` to keep the CMake trees outside the checkout (`<path>` and `<path>-x64`).

## Modes

- `--vst32-host <id> <enginePid> <rate> <block> <instrument> VST2 <path> <sha|none> <log|none> <trust> <slots>`:
  connects to `\\.\pipe\TabForge.PluginHost.<id>`, opens `Local\TabForge.PluginHost.<id>` and its `.req`/`.done`
  events, sends the 20-byte hello (magic `TF32`, version 1, 64-byte header, block, rate, slots), loads the DLL after a
  SHA-256 check through a writer-denying handle, reports `MsgLoaded`, then processes blocks until `CmdQuit`, pipe EOF or
  the engine process ends. This is the Windows default and is unchanged.
- `--vst32-host-tcp <port> <blockPath> <rate> <block> <instrument> VST2 <path> <sha|none> <log|none> <trust> <slots>`:
  the Wine bridge transport. Connects to `127.0.0.1:<port>`, opens the engine-created file block at `blockPath` and
  polls the header's request/done flags (no events, no delete-on-close), sends the plan's hello (magic `TFH1`, version,
  header, block, rate, slots, arch 32 or 64, flags, helper/Wine versions and the 32 raw token bytes) and validates the
  token from `TABFORGE_HOST_TOKEN` (64 hex characters) on the engine side. The command/audio loop is otherwise the same.
  The x86 build reports `tfvst32/1` and arch 32; the x64 build reports `tfvst64/1` and arch 64.
- `--probe <dll> <sha256>`: prints `Instrument|vendor|product` or `Effect|vendor|product`; exit 5 when the file is missing,
  its hash does not match, or the fingerprint is empty/`none` (refused before the DLL is loaded).
- `--selftest`: checks the shared layout, the serialization round trip, a real named mapping/event round trip and an
  explicit file block with polled flags.

Test-only fuzz harness: `tfvst32fuzz.exe`/`tfvst64fuzz.exe` are built only when `TFVST32_BUILD_TEST=ON` (never shipped).
`--frames <count> <seed>` sends mutated frames through both the named-pipe and TCP readers, `--state <passthrough.dll>
<count> <seed>` mutates the plug-in's state blob, and `--leak <passthrough.dll> <cycles>` loads/unloads the plug-in 200
times and checks private bytes and handles. `-DTFVST32_ASAN=ON` builds the x64 helper and these harnesses with MSVC
AddressSanitizer; the x86 helper cannot be instrumented by MSVC ASan and stays on the normal build.

Test-only: `tfvst32-testhooks.exe`/`tfvst64-testhooks.exe` are built with `TFVST32_TEST_HOOKS`; setting
`TABFORGE_TFVST32_TEST_STALL_AUDIO_MS` to a number of milliseconds before starting one makes its audio thread sleep
instead of processing. The engine never sets it, the shipped helpers have no test path and ignore the variable, and the
hook exists for the deadline/grace tests only.

## Failure rules

The bridge uses two block slots and the existing generation echo. A block the engine times out stays in its own slot;
the engine bypasses (effects dry, instruments silent) until the helper reports that generation done, then resumes on
the other slot. A helper still busy after the 2 s grace (or missing over 25 % of a 5 s window) is failed, killed once
and reported; the plug-in path is quarantined but the engine keeps running. There is no automatic restart: the user can
retry, and a device/sample-rate change (`RemotePlugin.Reconfigure`) restarts the helper and carries the current state.
Bypass is chain-local: the helper stays loaded and idle, and no helper command is sent.

## Protocol

The wire layout is in `tfvst32_protocol.h`, shared byte-for-byte with `PluginHostBlock`/`RemotePlugin`: 64-byte header,
512 event records of 8 bytes, then one or two slots of four float channels (`in L`, `in R`, `out L`, `out R`). A block
is accepted only for the generation the engine requested. Control frames use the existing
`[int32 length][byte type][payload]` format.

The helper implements audio, MIDI input, the host callback, parameters (`ListParams`/`SetParam`), programs
(`SetProgram`), `Describe`,
`GetState`/`SetState` and Quit, plus the plug-in editor. State is the VST2 chunk (`"TF32"` + chunk index + bytes;
index 1 is the current program, index 0 the bank fallback) or the existing parameter-list marker, capped at 16 MiB.
`Describe` answers the `LoadedPluginInfo` layout. The editor is a helper-owned top-level `WS_POPUP` window with the
class `TabForgePluginEditor`, owned by the engine's window: the engine docks it by moving that popup (never a
cross-process child). `effEditOpen`/`GetRect`/`Close`/`Idle` run on the helper main thread and `effEditIdle` ticks at
~33 ms; `sizeWindow` becomes `MsgEditorSized`, a user close becomes `MsgEditorClosed`. The owner's thread must pump
window messages (TabForge's UI does), otherwise some editors wait forever on a cross-process window message.
VST3 editors use the same window through `tfv3_open_editor`/`tfv3_editor_idle`/`tfv3_editor_poll_resize`; a VST3 view
resized by the user gets `tfv3_editor_set_size`. Under Wine the engine cannot pass a Windows HWND: with owner 0 the helper
creates a normal top-level floating window, and `WM_CLOSE` still produces `MsgEditorClosed`. Windows keeps the owner and
docked behaviour.

Control commands are handled on the helper main thread from a queue, one at a time; audio runs on its own thread with
preallocated buffers, denormals flushed (FTZ+DAZ), MMCSS "Pro Audio" and highest thread priority. A plug-in that
hangs in a state/editor call blocks only the helper main thread: the engine's control requests time out (state 5 s,
editor open 5 s, close 2 s) and audio keeps flowing.

Every host-to-plug-in call (`effSetChunk`, `effSetProgram`, parameters, editor calls) and `processReplacing` is
serialised by one plug-in mutex: control calls own it on the main thread, while the audio thread only tries it. While
a plug-in's own popup menu runs its nested message loop, the editor timer stops calling `effEditIdle` (re-entering
some editors there faults) and instead services the pipe: host commands get an immediate readable failure instead of a
timeout that would fault the engine's control channel. An unhandled exception anywhere in the helper (including the
plug-in's own window procedure) writes one text line (exception code, faulting address, module and offset, plug-in
name) beside a minidump `tfvst32-<pid>.dmp` or `tfvst64-<pid>.dmp` in `%LOCALAPPDATA%\TabForge\Diagnostics` (newest five kept), then ends
the helper with the exception code. The engine logs that code in `isolated plug-in lost (...)` and TabForge names the
report in its status line; only that plug-in is lost. When an editor call is in progress the
audio block is skipped (zeroed) and completed normally instead of waiting, so `WM_CLOSE`/`effEditIdle` never race a
plug-in's process function and the audio deadline never blocks on the UI. `effProcessEvents`, `processReplacing`, the
plug-in entry point, every dispatcher call (open/start/close/parameters) and every direct parameter get/set sit behind
SEH (`__try/__except`): a plug-in access violation becomes a silent completed block or a readable editor/state failure,
never a helper crash. State reads and writes fail closed when a chunk pointer or parameter call is unreadable.
