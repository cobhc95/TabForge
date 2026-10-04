# Diagnostics

Headless commands run from the command line, for checking the app without a person.

## How to change me
1. Entry files: `DiagnosticCommands*.cs` (command table).
2. Owner class: `DiagnosticCommands` or the audit class for that check.
3. Tests to run: `--areas hygiene`, then run the command itself with a scratch `--profile`, plus `--areas architecture,hygiene` (see the test box in `docs/TESTING.md`).
4. Docs to update: `docs/DEBUGGING.md`.

## Key types
- `DiagnosticCommands` (partial files): the command table, `--selftest`, `--playtest`, renders.
- `FeatureMapGenerator`: writes `docs/FEATURE_MAP.md`.
- `AudioAudit`, `MidiTimingAudit`, `PitchAudit`: playback and timing checks.
- `LayoutAudit`, `BarAuditRunner`: notation checks.
- `WindowProbes` (partial files): screenshots and window checks; `WindowProbes.SpeedAudit*.cs` is the action speed timer (see docs/DEBUGGING.md).

## Pathway
Add a command to `DiagnosticCommands`, one line in `docs/DEBUGGING.md`, and a test if it checks behaviour. Commands print nothing; read the log file. The full list: `docs/DEBUGGING.md`.

## Must not depend on
Nothing in the app depends on this folder. It may reference the audio engine project.

## Tests
`TestDebuggingDocInSync`, `TestFeatureMapInSync`, `TestAudioAudit`, `TestBarAuditTool`, `TestLayoutAuditTechniqueSong`, `TestTraceSwitchAreas`.
