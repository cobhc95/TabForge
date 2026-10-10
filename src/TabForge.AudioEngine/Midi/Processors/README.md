# TabForge.AudioEngine/Midi/Processors

The MIDI processors of a track's chain: channel and note mapping, transpose, velocity, timing, choke, repeater, scale and audio-aware processors. Each processor is a small class behind `IMidiProcessor`. Does not own the chain itself (`../MidiProcessorChain.cs`).

## Files
| File | Purpose |
| --- | --- |
| `AudioProcessors.cs` | MIDI processors that also see, and may change, the audio at their position in the plug-in chain |
| `ControlProcessors.cs` | `SenderProcessor` base: passes everything through and sends its own messages first |
| `GeneratorProcessors.cs` | Note repeater: re-sends a held note's off and on pair every size in beats |
| `NoteFlowProcessors.cs` | Choke: a note-on in the choke set ends the sounding notes of that set |
| `NoteProcessors.cs` | Channel filter and remap: 16 rows, each off or an output channel, with an optional solo channel |
| `NoteTransform.cs` | Base of the stateless note processors (channel map, note range, transpose, note map, velocity) |
| `ScaleProcessors.cs` | `MidiScales`: scale tables and parsing helpers shared by the scale processors |
| `TimingProcessors.cs` | MIDI delay in milliseconds, beats and samples; note-ons and note-offs are delayed alike |

## Pathway
`START_HERE.md`, Golden pathways: **Reach the engine**.

## Tests
`docs/feature-map/mixer-and-audio-engine.md`.
