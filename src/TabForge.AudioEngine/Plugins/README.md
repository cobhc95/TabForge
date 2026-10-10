# TabForge.AudioEngine/Plugins

In-process plug-in hosting: the instance interface, the one loader that builds an instance from a file, and the VST2 and VST3 hosts. The option to run a plug-in in its own process is in `../Isolation/`. Does not own trust or approval (`src/TabForge/Plugins/PluginTrust.cs`).

## Files
| File | Purpose |
| --- | --- |
| `IPluginInstance.cs` | The plug-in instance interface and `BlockMidi` (a MIDI message at a frame inside the block) |
| `PluginLoading.cs` | The one place that turns a plug-in file into an in-process instance, used by the engine, the isolated host and the scan probe |
| `Vst2Plugin.cs` | Hosts a 64-bit VST2 plug-in through its C interface; buffers are allocated once, `Process` does not allocate |
| `Vst2TestEffect.cs` | Headless test effect: a managed pass-through behind the real VST2 interface, with no DLL on disk |
| `Vst3Plugin.cs` | Hosts a VST3 plug-in through `tfvst3.dll`, a small C interface over the VST3 SDK |

## Pathway
`START_HERE.md`, Golden pathways: **Reach the engine**. Plug-in trust is decided at scan or approval in `src/TabForge/Plugins/`, not at load.

## Tests
`docs/feature-map/plug-ins.md`.
