# Presets

Built-in songs and templates.

## How to change me
1. Entry files: `DemoSongFactory.cs`, `TemplateFactory.cs`.
2. Owner class: the factory for that song or template.
3. Tests to run: `TestTemplateKeepsSetupOnly` (full-suite build), plus `--areas architecture,hygiene` (see the test box in `docs/TESTING.md`).
4. Docs to update: `CHANGELOG.md`.

## Key types
- `DemoSongFactory`: the demo song.
- `TemplateFactory`: new-song templates.
- `DiagnosticSongFactory`: songs built for headless diagnostics.

## Pathway
A factory returns a fresh `SongProject`; opening it goes through `DocumentPlacement` like any song. User templates are handled by `UserTemplates` in `src/TabForge/Services/`.

## Must not depend on
Views or the audio engine.

## Tests
`TestFullDemoSong`, `TestTemplateKeepsSetupOnly`, `TestUserTemplatesAndFaultedChain`.
