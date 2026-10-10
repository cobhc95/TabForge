# TabForge.AudioEngine/Midi

MIDI buffers and the processor chain in front of each instrument. The chain is built on the engine thread; the audio thread only runs it. Processors themselves are in `Processors/`. Does not own the mixer (`Mixing/`).

## Files
| File | Purpose |
| --- | --- |
| `MidiProcessorChain.cs` | Ordered MIDI processors in front of one instrument; JSON is parsed and everything allocated at build time |
| `MidiTypes.cs` | `MidiBuffer`: a fixed-capacity list of block MIDI events; adding never allocates and drops past capacity |
| `NoteFilter.cs` | `NoteSetText`: parses and formats note sets such as "36, 38, 40-45, C2" (C4 = 60) |

## Pathway
`START_HERE.md`, Golden pathways: **Reach the engine**.

## Tests
`docs/feature-map/mixer-and-audio-engine.md`.
