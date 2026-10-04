# Rendering

Export of a song to audio files.

## How to change me
1. Entry files: `RenderJob.cs`, `RenderSpecBuilder.cs`.
2. Owner class: `RenderJob` (run), `RenderSpecBuilder` (request).
3. Tests to run: `TestRenderBarRanges`, `TestGainRenderHash`, `TestRenderGuardContainment` (full-suite build), plus `--areas architecture,hygiene` (see the test box in `docs/TESTING.md`).
4. Docs to update: `CHANGELOG.md`, `TOOLS_AND_HOTKEYS.md` if a command changes.

## Key types
- `RenderJob`: runs one render and reports progress.
- `RenderSpecBuilder`: builds the engine's render request from the song and `RenderSettings`.
- `RenderStaging`: staging of the files a render needs.
- `ChainReadiness`: checks every plug-in chain is ready before a render starts.
- `RenderBounds`, `RenderBarRange`: which bars are rendered.

## Pathway
The window collects settings, `RenderSpecBuilder` turns them into a request, `RenderJob` sends it through `AudioEngineClient` (`src/TabForge/Audio/README.md`). The engine mixes offline (`src/TabForge.AudioEngine/README.md`); the master safety limiter is on by default. Output files are written through `FilePathPolicy.WriteAtomically`.

## Must not depend on
WPF windows.

## Tests
`TestRenderBarRanges`, `TestRenderHonoursMute`, `TestGainRenderHash`, `TestMidiExportTiming`.
