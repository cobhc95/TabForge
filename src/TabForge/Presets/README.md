# Presets

Built-in songs and templates.

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
