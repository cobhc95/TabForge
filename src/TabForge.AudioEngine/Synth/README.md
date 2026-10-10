# TabForge.AudioEngine/Synth

The built-in General MIDI sound: the synth rendered in the engine (so effects can process it), the conversion of the Windows General MIDI bank, and the diagnostic probe. Does not own the track chain (`Mixing/TrackChain.cs`).

## Files
| File | Purpose |
| --- | --- |
| `DlsToSoundFont.cs` | Converts the Windows General MIDI bank (DLS) into an in-memory SoundFont the synth can play |
| `GmSynth.cs` | The General MIDI sound of a track, rendered in the engine |
| `GmSynthProbe.cs` | The `--probe-gm` diagnostic: measures what the built-in synth produces for each playing technique |

## Pathway
`START_HERE.md`, Golden pathways: **Reach the engine**.

## Tests
`docs/feature-map/mixer-and-audio-engine.md`.
