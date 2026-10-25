# 0001. The audio engine runs in its own process

Status: accepted

## Context

The audio engine loads third-party plug-ins (native code) and runs a real-time callback. A plug-in crash or hang inside the window process would lose the user's unsaved song. The editor must also never wait on the audio thread.

## Decision

The UI process owns the song, documents, undo and settings. The engine runs in a second process, started from the same executable as `TabForge.exe --audio-engine <session> <parentPid>`; `Source/src/TabForge.Avalonia/Program.cs` dispatches to it before any window exists. `Source/src/TabForge.Core/Audio/AudioEngineClient.cs` is the only link from the UI to the engine. The two sides talk over a per-session named pipe (`CurrentUserOnly`, length-prefixed frames with a size limit) and a shared-memory block (a MIDI ring, a crash breadcrumb, meters). The wire types are in `Source/src/TabForge.Audio.Contracts`, which has no dependencies.

## Consequences

- A native crash or a hang kills or restarts only the engine. A breadcrumb in shared memory names the plug-in being called, so the restart can leave it off.
- The UI never runs plug-ins and is never on the audio callback path.
- Every call across the boundary is a message with explicit limits. New engine features need a contract type and a bounded reader.
- Both sides are the same build, so the shared-memory layout needs no version negotiation.
- The IPC is a same-user, same-machine transport, not an authentication boundary.

See `ARCHITECTURE.md`, sections 2, 3 and 8.
