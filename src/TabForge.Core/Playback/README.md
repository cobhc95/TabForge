# Playback

Turns a score into a timeline and MIDI events, and runs playback. No WPF.

## How to change me
1. Entry files: `PlaybackEngine*.cs`, `ScoreToMidiCompiler*.cs`.
2. Owner class: `PlaybackEngine` (transport), `ScoreToMidiCompiler` (events).
3. Tests to run: `TestPlaybackOrderSpec`, `TestFermataPlayback` (full-suite build); `--areas playback`, plus `--areas architecture,hygiene` (see the test box in `Source/docs/guides/TESTING.md`).
4. Docs to update: `CHANGELOG.md` (list audible changes).

## Key types
- `ScoreTimeline`, `NoteTimeline`: the song laid out in time (bars, repeats, tempo).
- `ScoreToMidiCompiler` (partial files): notes and techniques to MIDI events. Piano roll lane points (`ClipController` 0-127 CC, 128 pitch bend,
129 program change, 130 channel aftertouch) play on the track's channel from their position and are chased into the channel setup on a seek;
a lane program change overrides the track's own program (mixer / channel setup) from its beat, and the track's program plays again only where no
lane point holds. `MidiExportService` writes the same timeline, so an exported file carries the lane events. `.gp` cannot hold per-beat lane points
(only track-level program/volume/pan automations); they survive only in `.tforge` (or a `.gp` with the project embedded). Poly aftertouch is out of
scope: it needs per-note storage the clip model has none of.
- `ScoreToMidiCompiler` (partial files): notes and techniques to MIDI events.
- `PlaybackEngine` (partial files): transport, scheduling and the arrangement. `PlaybackEngine.StartWorker.cs`: `BeginStart`, the start whose timeline compiles on a worker from a song copy (the Avalonia transport's Play and speed change use it), and the playhead hold until the scheduler anchors the clock. Tests: PlaybackStartWorkerTests, PlaybackStartHoldTests.
- `ArrangementRefreshCompiler` builds the immutable future splice; `AudioGrowthRefreshFlow` bounds compilation after `PlaybackEngine.Arrangement.cs` reserves the boundary and captures the owner-thread snapshot.
- `PlaybackScheduleReuse`: checks the captured project revision, options, MIDI routes and performed-bar traversal before a seek reuses a timeline; unavailable or stale targets use the normal compile path.
- `PlaybackSeekRebuild`: the background rebuild of the playing timeline for a seek the schedule cannot reuse (latest seek wins, the compile runs from a song copy and the scheduler swaps the result in), plus the compile rules a start and a seek share. Tests: TestSeekRebuildReturnsAtOnce.
- `PlaybackOrder`: repeat and jump order; `SustainResolver`: note lengths.
- `LoopSettingsRules`: the loop button's no-area scope (section, bar, song), the loop behaviour read from the audio settings (`IPlaybackEngine.SetPreferences` hands it and the section count-in to the engine; `LoopPlayheadHold` keeps the drawn playhead inside the loop until the wrap) and the loop settings' box text. Tests: TransportSettingsPopupTests, TransportLoopCountInTests.
- `PlayheadMapper`: playback time to a position in the score (`MapInto` fills a held position for the per-frame path).
- `AudiblePositionHold`: keeps the engine's audible playhead from moving back under one scheduler anchor when the output latency rises mid-start.
- `PlaybackFrameRules`: per-frame decisions (live clock, bar remap, score and instrument cadences) and the audible-time maths; `PendingPlayheadSlot`: the allocation-free scheduler-to-frame hand-over; `IPlaybackEngine`: the engine port the view uses (the WPF `PlaybackEngine` implements it).
- `SongSnapshot`: the immutable copy of a song that playback reads.

## Pathway
Playback reads a `SongSnapshot`, never the live project. A compatible seek is handed to the existing scheduler; a changed project, options, route, traversal or unavailable prefix keeps the normal compile path. Sound goes to the engine only through `AudioEngineClient` (`Source/src/TabForge.Core/Audio/README.md`). A changed score invalidates the timeline through `DocumentEdits.Run`. Audio-only clip growth reserves the current bar boundary before audio upload and swaps the compiled future there; MIDI clip or routing changes keep the safe rebuild path.

## Must not depend on
WPF assemblies (`TestArchitectureLayering`); the audio engine project.

## Tests
`TestTimelineBasics`, `TestTimelineTechniques`, `TestTimelineMetronome`, `TestTimelineLoopAndOrder`, `TestTimelineSnapshot`, `TestPlaybackOrderSpec`, `TestNoHangingNotes`, `TestFermataPlayback`, `TestCountInIsHeard`, `TestTempoMath`, `TestCapoRepitchesNotes`.
