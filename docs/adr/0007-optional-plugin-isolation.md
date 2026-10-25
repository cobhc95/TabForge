# 0007. Plug-in process isolation is optional and is not a sandbox

Status: accepted

## Context

VST2 and VST3 plug-ins are native code. Running each one in its own process contains a crash or a hang to that plug-in, but costs latency and memory and does not suit every plug-in. Whatever the mode, a plug-in runs with the user's rights.

## Decision

Two modes, chosen in Preferences:

- In the engine process (default): lowest cost. A crash restarts the engine, and the plug-in named by the breadcrumb is left off.
- A separate process per plug-in (`--plugin-host`, `RemotePlugin`): a crash or hang loses only that plug-in for the session. Effects pass the sound through; instruments go silent.

Isolation is documented as crash isolation only. Separately, a song, preset or rig only names plug-in paths; `Source/src/TabForge.Core/Plugins/PluginTrust.cs` loads a path only if the user scanned it, it is in a scanned local folder, or the user approved that exact binary (checked by hash before every load). VST2 hosting uses TabForge's own interface definition; the VST2 SDK is not bundled.

## Consequences

- Users choose between cost and containment per setup.
- The watchdog covers only plug-in calls that set the breadcrumb first; a new call site must set it.
- Nothing in the docs or UI may call the isolated mode secure. Plug-ins stay trusted code.

See `ARCHITECTURE.md`, section 8, and `SECURITY.md`.
