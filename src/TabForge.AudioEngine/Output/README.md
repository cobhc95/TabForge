# TabForge.AudioEngine/Output

Audio devices: the factory that opens the device chosen in Settings, and the headless null device. Does not own the mix (`Mixing/`).

## Files
| File | Purpose |
| --- | --- |
| `AudioOutputFactory.cs` | Opens the device chosen in Settings > Audio & VST (WASAPI shared or exclusive, ASIO, DirectSound) |
| `NullOutput.cs` | The headless device: no sound card; the mix is read and discarded, clocked at the block cadence |

## Pathway
`START_HERE.md`, Golden pathways: **Reach the engine**.

## Tests
`docs/feature-map/mixer-and-audio-engine.md`.
