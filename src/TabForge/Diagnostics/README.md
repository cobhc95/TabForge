# Diagnostics

Headless commands run from the command line, for checking the app without a person.

## Key types
- `DiagnosticCommands` (partial files): the command table, `--selftest`, `--playtest`, renders.
- `FeatureMapGenerator`: writes `docs/FEATURE_MAP.md`.
- `AudioAudit`, `MidiTimingAudit`, `PitchAudit`: playback and timing checks.
- `LayoutAudit`, `BarAuditRunner`: notation checks.
- `WindowProbes` (partial files): screenshots and window checks.

## Pathway
Add a command to `DiagnosticCommands`, one line in `docs/DEBUGGING.md`, and a test if it checks behaviour. Commands print nothing; read the log file. The full list: `docs/DEBUGGING.md`.

## Must not depend on
Nothing in the app depends on this folder. It may reference the audio engine project.

## Tests
`TestDebuggingDocInSync`, `TestFeatureMapInSync`, `TestAudioAudit`, `TestBarAuditTool`, `TestLayoutAuditTechniqueSong`, `TestTraceSwitchAreas`.
