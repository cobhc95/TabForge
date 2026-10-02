# Playback

Turns a score into a timeline and MIDI events, and runs playback. No WPF.

## Key types
- `ScoreTimeline`, `NoteTimeline`: the song laid out in time (bars, repeats, tempo).
- `ScoreToMidiCompiler` (partial files): notes and techniques to MIDI events.
- `PlaybackEngine` (partial files): transport, scheduling and the arrangement.
- `PlaybackOrder`: repeat and jump order; `SustainResolver`: note lengths.
- `PlayheadMapper`: playback time to a position in the score.
- `SongSnapshot`: the immutable copy of a song that playback reads.

## Pathway
Playback reads a `SongSnapshot`, never the live project. Sound goes to the engine only through `AudioEngineClient` (`src/TabForge/Audio/README.md`). A changed song invalidates the timeline through `DocumentEdits.Run`.

## Must not depend on
WPF assemblies (`TestArchitectureLayering`); the audio engine project.

## Tests
`TestTimelineBasics`, `TestTimelineTechniques`, `TestTimelineMetronome`, `TestTimelineLoopAndOrder`, `TestTimelineSnapshot`, `TestPlaybackOrderSpec`, `TestNoHangingNotes`, `TestFermataPlayback`, `TestCountInIsHeard`, `TestTempoMath`, `TestCapoRepitchesNotes`.
