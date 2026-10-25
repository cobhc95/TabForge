# Audio

The app's only way to reach the audio engine process, plus device and capture helpers.

## How to change me
1. Entry files: `Audio/AudioEngineClient*.cs` (Commands, Sync); engine side in `Source/src/TabForge.AudioEngine`.
2. Owner class: `AudioEngineClient` (reach the engine only through it).
3. Tests to run: `TestClipWaveformSpan`, `TestWaveformCacheBounds`, `TestEngineMultiTabPlayback` (full-suite build); `--areas engine`, plus `--areas architecture,hygiene` (see the test box in `Source/docs/TESTING.md`).
4. Docs to update: `ARCHITECTURE.md` if IPC changes.

## Key types
- `AudioEngineClient` (partial files: core, Sync, Commands, Probes): sends commands, receives events. Windows only: the client and `AutoPitchMatcher` compile only into `net8.0-windows` (`TabForge.Core.csproj`); the Avalonia app composes one in `Source/src/TabForge.Avalonia/Playback`.
- `AudioEngineClient.PluginInfo.cs`: asks the engine what each loaded plug-in of a chain reports about itself (ids, pin counts, names); no answer (an engine without the request, a plug-in that is not loaded) is "not available", never an error.
- `IAudioDevices`, `AvaloniaEngineRouting`: devices and routing.
- `ClickDestination`: the metronome and count-in click's own hidden engine slot (a General MIDI synth, no chain, in no song); `RoutedMidiOutput.SendMetronome` writes the clicks there while the engine runs; the sync in `AudioEngineClient.Sync.cs` keeps it.
- `MidiInputCapture`, `MidiInputHub`, `RoutedMidiOutput`: MIDI in and out. Clients can pick one device by name (`IMidiInputClient.Device`); `Rescan()` picks up devices connected or removed while open. The hub (one per engine client, `AudioEngineClient.MidiInput`) owns the single set of input handles and hands each listener (recording, Keyboard mode) a client; the devices open while one client holds them and close with the last.
- `TimelineSongClock`: playback position shared by the views.
- `DeviceChangePause`: pauses playing songs while the audio device is reopened and resumes them when it is ready.
- `IPluginLoadGate`: the engine client's trust port (which slots may load, which file changed at load); the client takes it in its constructor, `PluginTrustLoadGate` (Source/src/TabForge.Core/Plugins) answers it from `PluginTrust` and fails closed (untrusted slots are Skip).
- `EngineStartInfo`: builds the command line that starts one engine process (mode, session, TabForge's process id and this run's log file); each argument is its own list entry. Starting the process is `AudioEngineClient`'s job.
- `SongOwnerIds`: each open document's engine transport id (the engine keeps one song transport per id).
- Clips reach the engine in `AudioEngineClient.Sync.cs` (`EngineCommand.SetClips`, built from each track's audio clips); the engine side is `ClipPlayer`.
- `WaveformCache`: bounded waveform data for clips.
- `AutoPitchMatcher`: pitch matching of recordings.

## Pathway
All engine traffic goes through `AudioEngineClient`. After an engine restart the state is resent (`Source/src/TabForge.Avalonia/Transport/AvaloniaPlaybackSessions.cs`), so code that sends engine state must be resendable from there. Commands and events are defined in `Source/src/TabForge.Audio.Contracts/README.md`.

## Must not depend on
Views (no WPF windows). Only this folder, `Program` and diagnostics reference the engine project (`TestArchitectureLayering`).

## Tests
`TestEngineMultiTabPlayback`, `TestEngineOwnerTransports`, `TestEngineWarmOwnership`, `TestPumpOnce`, `TestRecordingPipeline`, `TestWaveformCacheBounds`, `TestRoutingCycles`, `TestHeadlessDeviceReconfigure`, `TestEngineLivenessAndSlowLoad`.
