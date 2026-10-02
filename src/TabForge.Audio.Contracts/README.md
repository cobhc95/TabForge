# TabForge.Audio.Contracts

Wire contracts shared by the app and the audio engine. No dependencies.

## Key types
- `EngineCommand`, `EngineEvent` (in `EngineProtocol.cs`): what the app sends and what the engine answers.
- `SharedBlock`: shared-memory block between processes.
- `TransportMap`, `TransportMeter`: playback position exchange.
- `ClipPathGuard`, `SafeFileNames`: path and file-name checks for clips.
- `EngineConfig`, `ClipSpec`, `PluginSpec`: engine setup records.

## Pathway
A new engine command is added here first, then handled in `src/TabForge.AudioEngine/` and sent from `AudioEngineClient` (`docs/RECIPES.md`). Keep messages bounded; every size limit is stated in `ARCHITECTURE.md`.

## Must not depend on
Any other project, WPF, or the app. A lower layer never references a higher one.

## Tests
`TestArchitectureLayering`, `TestCommandFrameRobustness`, `TestSharedRingProducers`, `TestGainBitIdentical`.
