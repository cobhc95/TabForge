# Playback

Turns a score into a timeline and MIDI events, and runs playback. No WPF.

## How to change me
1. Entry files: `PlaybackEngine*.cs`, `ScoreToMidiCompiler*.cs`.
2. Owner class: `PlaybackEngine` (transport), `ScoreToMidiCompiler` (events).
3. Tests to run: `TestPlaybackOrderSpec`, `TestFermataPlayback` (full-suite build); `--areas playback`, plus `--areas architecture,hygiene` (see the test box in `docs/TESTING.md`).
4. Docs to update: `CHANGELOG.md` (list audible changes).

## Key types
- `ScoreTimeline`, `NoteTimeline`: the song laid out in time (bars, repeats, tempo).
- `ScoreToMidiCompiler` (partial files): notes and techniques to MIDI events.
- `PlaybackEngine` (partial files): transport, scheduling and the arrangement.
- `ArrangementRefreshCompiler` builds the immutable future splice; `AudioGrowthRefreshFlow` bounds compilation after `PlaybackEngine.Arrangement.cs` reserves the boundary and captures the owner-thread snapshot.
- `PlaybackScheduleReuse`: checks the captured project revision, options, MIDI routes and performed-bar traversal before a seek reuses a timeline; unavailable or stale targets use the normal compile path.
- `PlaybackOrder`: repeat and jump order; `SustainResolver`: note lengths.
- `PlayheadMapper`: playback time to a position in the score.
- `SongSnapshot`: the immutable copy of a song that playback reads.

## Pathway
Playback reads a `SongSnapshot`, never the live project. A compatible seek is handed to the existing scheduler; a changed project, options, route, traversal or unavailable prefix keeps the normal compile path. Sound goes to the engine only through `AudioEngineClient` (`src/TabForge/Audio/README.md`). A changed score invalidates the timeline through `DocumentEdits.Run`. Audio-only clip growth reserves the current bar boundary before audio upload and swaps the compiled future there; MIDI clip or routing changes keep the safe rebuild path.

## Must not depend on
WPF assemblies (`TestArchitectureLayering`); the audio engine project.

## Tests
`TestTimelineBasics`, `TestTimelineTechniques`, `TestTimelineMetronome`, `TestTimelineLoopAndOrder`, `TestTimelineSnapshot`, `TestPlaybackOrderSpec`, `TestNoHangingNotes`, `TestFermataPlayback`, `TestCountInIsHeard`, `TestTempoMath`, `TestCapoRepitchesNotes`.
