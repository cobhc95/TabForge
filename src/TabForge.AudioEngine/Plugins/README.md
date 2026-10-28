# TabForge.AudioEngine/Plugins

In-process plug-in hosting: the instance interface, the one loader that builds an instance from a file, and the VST2 and VST3 hosts. The option to run a plug-in in its own process is in `../Isolation/`. Does not own trust or approval (`Source/src/TabForge.Core/Plugins/PluginTrust.cs`).

## Files
| File | Purpose |
| --- | --- |
| `IPluginInstance.cs` | The plug-in instance interface and `BlockMidi` (a MIDI message at a frame inside the block) |
| `PluginLoading.cs` | The one place that turns a plug-in file into an in-process instance, used by the engine, the isolated host and the scan probe |
| `Vst2Plugin.cs` | Hosts a 64-bit VST2 plug-in through its C interface; buffers are allocated once, `Process` does not allocate |
| `Vst2Plugin.ParameterState.cs` | State of a VST2 plug-in without chunks: the list of its parameter values (mark, then one float each) |
| `Vst2Plugin.Parameters.cs` | The parameter list of a VST2 plug-in (names, display text, 0..1 values) for the generic controls panel |
| `Vst2TestEffect.cs` | Headless test effect: a managed pass-through behind the real VST2 interface, with no DLL on disk |
| `Vst3ClassInfo.cs` | What a loaded plug-in reports about itself (`LoadedPluginInfo`): VST2 id, pin counts and names from the instance; VST3 class id, name and vendor from the module's class factory |
| `Vst3Plugin.cs` | Hosts a VST3 plug-in through `tfvst3.dll`, a small C interface over the VST3 SDK |
| `Vst3Plugin.RunLoop.cs` | Linux: the VST3 host run loop entry point of libtfvst3.so (the open editors' timers and file descriptors), run by the X11 editor host |
| `Vst3Plugin.Parameters.cs` / `.Parameters.Windows.cs` | The parameter list and live changes of a VST3 plug-in (tfv3_param_* of the shim) for the generic controls panel |

## Pathway
Plug-in trust is decided at scan or approval in `Source/src/TabForge.Core/Plugins/`, not at load.

## Tests
