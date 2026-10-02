# TabForge.AudioEngine

The audio engine, a separate process: mixer, synth, plug-in hosting, recording and offline render.

## Key types
- `EngineHost`, `EngineSession`: the process entry and one connection to the app.
- `MixEngine`, `TrackChain`: mixing and per-track chains.
- `GmSynth`: the built-in synthesiser.
- `OfflineRenderer`, `SafetyLimiter`: file render and the master limiter.
- `PluginHostMain`, `RemotePlugin`: plug-ins run in isolated child processes.
- `EngineWatchdog`: detects a stalled engine so the app can restart it.

## Pathway
The app talks to it only through `EngineCommand` and `EngineEvent` (`src/TabForge.Audio.Contracts/README.md`) via `AudioEngineClient`. Audio-thread code is allocation-free and lock-free; changes retire old objects through `RetireQueue`. Real-time rules: `ARCHITECTURE.md`.

## Must not depend on
The app project or WPF. It references only the contracts project.

## Tests
`TestMixer`, `TestIsolatedCallbackBudget`, `TestMuteSoloTruthTable`, `TestRetirementEpochBarrier`, `TestRealtimePolish`, `TestWatchdogPolicy`, `TestMonitorFx`.
