# TabForge.Core/Midi

MIDI buffers, block MIDI / transport shapes and the processor chain shared by the live engine and the MIDI file export.
The chain is built on the engine thread; the audio thread only runs it; the export runs it offline over each track's
events. Processors themselves are in `Processors/`. Does not own the mixer (the engine's `Mixing/`).

## Files
| File | Purpose |
| --- | --- |
| `MidiProcessorChain.cs` | Ordered MIDI processors in front of one instrument; JSON is parsed and everything allocated at build time |
| `MidiTypes.cs` | `MidiBuffer`: a fixed-capacity list of block MIDI events; adding never allocates and drops past capacity |
| `NoteFilter.cs` | `NoteSetText`: parses and formats note sets such as "36, 38, 40-45, C2" (C4 = 60) |

## Pathway

## Tests
