# Presets/FullDemoSong

The built-in full demo song, written in code: 10 tracks and 144 bars, split into one file per part. `FullDemoSongFactory` is the entry point; the other files are partial files of it. The other presets are in `../README.md`.

## Files
| File | Purpose |
| --- | --- |
| `FullDemoSongFactory.cs` | Entry point: builds the demo song document from the parts below |
| `FullDemoSongFactory.Skeleton.cs` | Everything that is not a note: structure, sections, tempo, and the performed bar order |
| `FullDemoSongFactory.Drums.cs` | The drum part for every bar; velocities come from the Grid, Fill and Roll helpers |
| `FullDemoSongFactory.Rhythm.cs` | Rhythm guitars and bass: base velocities, and the bass following the rhythm parts |
| `FullDemoSongFactory.LeadKeys.cs` | Lead, harmony, clean, pad and piano parts, with lyrics and chord names |
| `FullDemoSongFactory.Helpers.cs` | Shared contract: the song sections, bar, slot and string conventions, the riff grammar (Riff), structure and finalise helpers |
| `FullDemoSongFactory.Notes.cs` | Note helpers: cell and beat placement (Put), fretted, harmonic, bend, whammy, grace, HOPO and keys notes, and the seeded velocity humanisation (Hv) |
| `FullDemoSongFactory.DrumHelpers.cs` | Drum helpers: the drum lanes, Grid, Fill, Roll and the drum re-duration |

## Pathway

## Tests
`TestFullDemoSong`.
