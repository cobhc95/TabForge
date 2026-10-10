# TabForge.AudioEngine/Mixing

The audio callback and everything it runs: the mixer, the track chains, the master tap and limiter, the song transport, the offline renderer and the callback health counters. Code here runs on the audio thread unless noted. Does not own the device (`Output/`).

## Files
| File | Purpose |
| --- | --- |
| `CallbackMetrics.cs` | Real-time health of the callback: duration against deadline, deadline misses, late calls, allocations |
| `MasterTap.cs` | Master output tap: a ring the callback fills; a drain thread hands blocks to a sink after the limiter |
| `MixEngine.cs` | The audio callback: places timed MIDI at its frame, renders every track chain and sums them |
| `OfflineRenderer.cs` | Faster-than-realtime render of the loaded track chains, in worker threads |
| `PitchProbe.cs` | One pitch measurement of an instrument (`EngineHost.MeasurePitch`) |
| `RenderWavSink.cs` | One WAV file of an offline render: downmix, resample when the rates differ, 16-bit conversion |
| `RetireQueue.cs` | The callback epoch: odd while a callback runs, so replaced objects are freed only when it is safe |
| `SafetyLimiter.cs` | Master safety limiter: stereo-linked peak limiter with a short lookahead |
| `SongTransport.cs` | The song transport as the app last described it (position, tempo, bar map), as immutable parts |
| `TrackChain.cs` | One track's path: sound (VST and/or the built-in GM synth), effects in order, then level and pan |

## Pathway
`START_HERE.md`, Golden pathways: **Reach the engine**. Objects the callback may still read are replaced through `RetireQueue`, never freed in place. Real-time rules are in `ARCHITECTURE.md`.

## Tests
`docs/feature-map/mixer-and-audio-engine.md`.
