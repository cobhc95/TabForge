# Rendering

Export of a song to audio files.

## How to change me
1. Entry files (this folder, shared by both apps): `RenderJob.cs`, `RenderSpecBuilder.cs`, and the settings they read, `RenderSettings.cs`. The engine sync they apply is `Audio/EngineSongSync.cs`; each app passes its own `Restore` and `ConfirmIncomplete` (WPF `Views/RenderWindow.cs`, Avalonia `Views/AvaloniaRenderRunner.cs`).
2. Owner class: `RenderJob` (run), `RenderSpecBuilder` (request).
3. Tests to run: `TestRenderBarRanges`, `TestGainRenderHash`, `TestRenderGuardContainment` (full-suite build), plus `--areas architecture,hygiene` (see the test box in `Source/docs/TESTING.md`).
4. Docs to update: `CHANGELOG.md`, `TOOLS_AND_HOTKEYS.md` if a command changes.

## Key types
- `RenderJob`: runs one render and reports progress.
- `RenderSpecBuilder`: builds the engine's render request from the song and `RenderSettings`.
- `RenderStaging`: staging of the files a render needs.
- `ChainReadiness`: checks every plug-in chain is ready before a render starts.
- `RenderSettings`, `RenderBounds` (this folder): the last-used Render options and the bounds choice they name. `RenderBarRange` (app): which bars are rendered.

## Pathway
The window collects settings, `RenderSpecBuilder` turns them into a request, `RenderJob` sends it through `AudioEngineClient` (`Source/src/TabForge.Core/Audio/README.md`). The engine mixes offline (`Source/src/TabForge.AudioEngine/README.md`); the master safety limiter is on by default. Output files are written through `FilePathPolicy.WriteAtomically`.

## Must not depend on
WPF windows.

## Tests
`TestRenderBarRanges`, `TestRenderHonoursMute`, `TestGainRenderHash`, `TestMidiExportTiming`.
