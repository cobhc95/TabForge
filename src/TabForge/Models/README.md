# Models

Song data and the plain types around it. No WPF, no I/O.

## How to change me
1. Entry files: `SongProject.cs`, `NotationEnums.cs`.
2. Owner class: `SongProject` / `TrackModel` / `MeasureModel`; no WPF, no I/O.
3. Tests to run: `TestModelRoundTrip`, `TestAudioTrackModel`, `TestSelectionModel` (full-suite build), plus `--areas architecture,hygiene` (see the test box in `docs/TESTING.md`).
4. Docs to update: `ProjectService` format notes, `CHANGELOG.md`.

## Key types
- `SongProject`: the whole song (tracks, tempo map, sections, mixer, clips).
- `MeasureModel`, `TrackModel` and the note types in `NotationEnums.cs`.
- `TrackKind.Audio` (value 5): a clips-only track (no notes, tuning or instrument; empty bars equal in number to the other tracks). `TrackModel.IsAudio` / HasNotation; SongProject properties NotationTracks, FirstNotationTrack (null in an audio-only song) and MasterBarTrack (first notation track, else the first track: where tempo, time, key and section attributes are read). Use these instead of `Tracks[0]`. FormatVersion reads 3 exactly while an audio track exists (otherwise 2 or older), so songs without audio save as before; `ProjectValidator` accepts 1 to 3 and clears notes and pads or trims bars on audio tracks. TrackController.ConvertAudioToInstrument turns an audio track into an instrument track (the only direction), moving its clips down one lane.
- `AudioClip`: an audio or MIDI clip placed on the timeline.
- `SongExtentMeasureCache`: the per-project runtime cache for measured bar timing; it is excluded from project files and checks timing metadata plus the timeline revision.
- `SongProject.Runtime.cs`: runtime-only dirty state, timeline revision batching, and the cache slot; `SongProject.cs` retains the serialized model types.
- `MixerSettings`, `MixerOptions`: per-track and master levels.
- `SelectionModel`: the editor selection (bars, beats, notes).
- `TrackOrdering`: display order of tracks.

## Pathway
Code that changes a song does it inside `DocumentEdits.Run` (see `src/TabForge/Documents/README.md`); models never mark themselves changed. Saving goes through `ProjectService`.

## Must not depend on
WPF assemblies (checked by `TestArchitectureLayering`). Nothing here may reference the audio engine.

## Tests
`TestModelRoundTrip`, `TestProjectRoundtrip`, `TestSelectionModel`, `TestTrackOrderingModel`, `TestSongRigs`, `TestPersistenceSchema`, `TestAudioTrackModel`, `TestAudioTrackPersistence`, `TestAudioTrackConversion`.
