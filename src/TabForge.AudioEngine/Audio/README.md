# TabForge.AudioEngine/Audio

Audio clips and live input: clip playback from disk, the input device, recording to WAV, the tuner's pitch detector and the take alignment. Does not own the mixer (`Mixing/`) or the device (`Output/`).

## Files
| File | Purpose |
| --- | --- |
| `ClipPlayer.cs` | Plays one audio clip; a disk thread decodes the file ahead of the audio thread |
| `InputBlock.cs` | One block of live input (inputs 1 and 2 at the engine rate), shared read-only per mixer block |
| `InputCapture.cs` | Audio input (WASAPI, the chosen recording device) delivering inputs 1 and 2 to the audio thread |
| `PitchDetector.cs` | Monophonic pitch detector for the tuner (YIN on a decimated copy of the window) |
| `Recorder.cs` | Writes the input of each armed track to its own 32-bit float WAV file |
| `SincResampler.cs` | Streaming stereo windowed-sinc resampler for the capture path |
| `TakeAlignment.cs` | Where a recorded take starts on the song timeline, compensating the output delay |

## Pathway
`START_HERE.md`, Golden pathways: **Reach the engine**. Real-time rules (no allocation or locks on the audio thread) are in `ARCHITECTURE.md`.

## Tests
`docs/feature-map/recording.md`, `docs/feature-map/mixer-and-audio-engine.md`.
