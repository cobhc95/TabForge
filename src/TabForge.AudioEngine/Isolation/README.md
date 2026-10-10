# TabForge.AudioEngine/Isolation

Plug-ins that run in their own process: the engine side of the link, the shared memory block, and the child process that hosts one plug-in. Does not own the trust decision (`src/TabForge/Plugins/PluginTrust.cs`).

## Files
| File | Purpose |
| --- | --- |
| `PluginControlChannel.cs` | Engine side of the control channel to an isolated plug-in; each command carries a request id |
| `PluginHostBlock.cs` | Shared memory for one plug-in in its own process: audio in and out, MIDI and transport |
| `PluginHostLink.cs` | Audio half of an isolated plug-in: each block goes to the child process and must come back |
| `PluginHostMain.cs` | The child process that runs exactly one plug-in (the `--plugin-host` entry) |
| `PluginInfoProbe.cs` | The `--plugin-info` probe: loads one plug-in in a throwaway process and prints its details |
| `RemotePlugin.cs` | A plug-in in its own process, seen by the engine as an ordinary instrument |

## Pathway
`START_HERE.md`, Golden pathways: **Reach the engine**. Untrusted plug-ins reach the engine as skipped.

## Tests
`docs/feature-map/plug-ins.md`.
