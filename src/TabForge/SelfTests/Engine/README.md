# SelfTests/Engine

Engine lifecycle tests that need no audio device: the real engine code is driven in process through its harness. Does not own the engine (`src/TabForge.AudioEngine/`).

## Files
| File | Purpose |
| --- | --- |
| `SelfTestEngineHeadless.cs` | Engine lifecycle tests (configure, chain building, clips, retirement) that need no device |

## Pathway
`START_HERE.md`, Golden pathways: **Reach the engine**; and Run one test.

## Tests
`docs/feature-map/mixer-and-audio-engine.md`.
