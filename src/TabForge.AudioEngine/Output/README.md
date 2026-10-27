# TabForge.AudioEngine/Output

Audio devices: the factory that opens the device chosen in Settings, and the headless null device. Does not own the mix (`Mixing/`).

## Files
| File | Purpose |
| --- | --- |
| `AudioOutputFactory.cs` | Opens the device chosen in Settings > Audio & VST (WASAPI shared or exclusive, ASIO, DirectSound) |
| `DirectSoundWatch.cs` | DirectSound buffer sizing (floor, follows the Buffer size setting) and the refill monitor (late / dry / slow counts, rate-limited log) |
| `OutputLossWatch.cs` | Tells that an open output is lost: a stop with an error, an ASIO reset request, a changed default device |
| `IResetRequestingOutput.cs` | The reset request an output other than an ASIO driver can raise (the headless device, in tests) |
| `NullOutput.cs` | The headless device: no sound card; the mix is read and discarded, clocked at the block cadence |

## Pathway

## Tests
