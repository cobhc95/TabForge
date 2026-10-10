# TabForge.Audio.Contracts

Wire contracts shared by the app and the audio engine. No dependencies.

## Key types
- `EngineCommand`, `EngineEvent` (in `EngineProtocol.cs`): what the app sends and what the engine answers.
- `EngineMessages.cs`, `EngineMessageLists.cs`: one message record per command with a payload, each with one Write and one Read; see Command messages below.
- `SharedBlock`: shared-memory block between processes.
- `TransportMap`, `TransportMeter`: playback position exchange.
- `ClipPathGuard`, `SafeFileNames`: path and file-name checks for clips.
- `EngineConfig`, `ClipSpec`, `PluginSpec`: engine setup records.

## Command messages
The pipe protocol is positional binary: the payload of a command is its fields in order, no names. Each command's order is written once, in its message record (a Write over a BinaryWriter and a Read over a BinaryReader); `AudioEngineClient` sends the record's Write and `EngineHost` calls its Read, so the two sides cannot drift apart. The Read returns the raw values and bounds only what could allocate without limit; the engine range-checks. Optional tails (owner id, bar map, recording offset) are read when present and absent from older senders.
- The wire bytes are pinned: `TestEngineMessagesGolden` compares every pair with the old positional writer and with a golden hex string; `TestEngineMessagesRoundTrip` reads back every field. A new command adds a record and a case to both.
- Already a single pair: the device configuration (`EngineConfig`), the offline render (`RenderSpec`), the chain load and its state frames (`ChainLoadProtocol`).
- No payload: panic, render cancel, shutdown.

## Pathway
A new engine command is added here first, then handled in `src/TabForge.AudioEngine/` and sent from `AudioEngineClient` (`docs/RECIPES.md`). Keep messages bounded; every size limit is stated in `ARCHITECTURE.md`.

## Must not depend on
Any other project, WPF, or the app. A lower layer never references a higher one.

## Tests
`TestEngineMessagesRoundTrip`, `TestEngineMessagesGolden`, `TestArchitectureLayering`, `TestCommandFrameRobustness`, `TestSharedRingProducers`, `TestGainBitIdentical`.
