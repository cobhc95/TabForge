# Demo song coverage inventory

Read-only inventory for the long built-in demo song (Remy, 2026-09-30). Source of truth is the code named in each row; nothing was changed.
Purpose: list every technique and notation option TabForge can store, how to set it from code, whether it survives a `.gp`
export, whether playback honours it and whether the renderer engraves it, so the demo can be built in one pass by
`--write-demo-song <out.gp>` (section 7).

How to read the tables

- **API**: the model call that sets it. `N(t,s,f,"Tech")` is the self-test helper `RtNote` (a `TabNote` with `StringIndex=s`, `Fret=f`,
  `MidiValue=t.PitchOf(s,f)`, techniques added). `cell` is a `TabCell`, `bar` a `MeasureModel`, `t` a `TrackModel`, `song` a `SongProject`.
- **Clean .gp**: `GuitarProExporter.Save(song, path, embedProject: false)` (alphaTab .gp writer, then re-read by real readers).
  Y = survives, ~ = approximated (what changes is in the notes column), N = lost.
- **.gp+proj**: `GuitarProExporter.Save(song, path, embedProject: true)` (the default). The whole project is embedded as
  `TabForge/project.tforge.gz`, TabForge reopens from it. Every item is Y here (docs/COMPATIBILITY_RESULTS.md: empty allow-list), so this
  column only says so once; the demo is saved this way.
- **Play**: honoured by `ScoreToMidiCompiler` / `PlaybackOrder` / `SustainResolver` (Y, tag-only = no audible change, N).
- **Engr**: engraved by the renderer (TAB = TAB staff, ST = standard notation staff, both, N).
- IDs (N01, B01, S01, T01) are stable so the demo's bar plan can refer to them.

Counts (rows per section): note-level 37, beat-level 35, bar/song-level 50, track/instrument 27, total 149. About 10 rows are
cross-references or non-features (B14, B21, B24, B33, B34, S40, S44, S45, N37, B19/B22 as "not representable"), so the distinct
settable items are roughly 139.

---

## 1. Note-level techniques (TabNote, TechniqueNames, GpEffects)

Canonical strings are in `Source/src/TabForge.Core/Models/TechniqueNames.cs` (28 constants) and `GpEffects.All` (59 names, `Source/src/TabForge.Core/Models/SongProject.cs`); together 62 distinct
strings (the 59 plus `PM`, `HOPOOrigin`, `HOPODestination`). They are stored in `TabNote.Techniques` (case-insensitive `HashSet<string>`).
Anything that is a bool or number on the model (Ghost, Dead, Tied, Accent, Staccato, Tenuto, Fermata, grace fields) is listed at the place the
renderer and exporter read it; the matching string is what the importer adds, the editor does not.

| ID | Item | API | Clean .gp | Play | Engr | Parameters / notes |
|---|---|---|---|---|---|---|
| N01 | Palm mute | `Techniques.Add("PalmMute")` (legacy `"PM"` accepted; use `TechniqueNames.HasPalmMute`) | Y | Y: velocity x0.92, length capped at a quarter | both: "P.M.---\|" passage | export also sets the beat flag |
| N02 | Let ring | `Add("LetRing")` | Y | Y: `SustainResolver` extends to the next same-string onset, capped by `PlaybackOptions.LetRingCapMs` (2000 ms) | TAB: span line | |
| N03 | Hammer-on / pull-off | origin `Add("HOPO"); Add("HOPOOrigin")`, destination `Add("HOPO"); Add("HOPODestination")` | Y (origin flag; destination re-derived) | Y: destination velocity x0.8 | ST: slur only when Origin/Destination pair exists; TAB: no mark | the editor's plain `Note.HammerPull` adds only `HOPO`, which draws no slur: the demo must use the importer's pair convention |
| N04 | Bend | `Add("Bend")`, `BendPoints = new(){ new(){Offset=0,Value=0}, new(){Offset=15,Value=4}, new(){Offset=60,Value=4} }`, `BendTypeName`, `BendStyleName` | ~ `.gp` keeps origin/middle/destination only (`SimplifyBend`) | Y: pitch wheel on the track's effect channel, +-12 semitone range; plain `BendTypeName=="Bend"` reaches target at 50 % of the note; `Bend` with no points = generic half-tone bump | TAB: curve + label (P.B., R, amounts 1/4..full..2); ST: none | Offset 0..60 (or a 0..1 fraction), Value in quarter-tones (4 = one tone; compiler halves to semitones). Type names read by the renderer: `Bend`, `Prebend`, `PrebendBend`, `PrebendRelease`, `Release`, `BendRelease` (+ alphaTab `Hold`, `Custom`). Not authorable in the UI (import or code only) |
| N05 | Legato slide | `Add("LegatoSlide")` (alias `"Slide"`, exported as legato), `SlideTargetMidi = t.PitchOf(s,f2)` | Y | Y: target not re-attacked; wheel glides in the last 45 % | TAB: connector | target also found from the next note on the string when 0 |
| N06 | Shift slide | `Add("ShiftSlide")`, `SlideTargetMidi` | Y | Y: target re-attacked, glide in last 45 % | TAB | |
| N07 | Slide in from below / above | `Add("SlideInBelow")` / `Add("SlideInAbove")` | Y | Y: starts ~3 semitones off, <=120 ms, note starts early | TAB: incoming mark | |
| N08 | Slide out up / down | `Add("SlideOutUp")` / `Add("SlideOutDown")` | Y | Y: +-5 semitones from 25 % of the note | TAB: outgoing mark | |
| N09 | Pick slide up / down | `Add("PickSlideUp")` / `Add("PickSlideDown")` | N (alphaTab .gp writer has no pick slide) | N | TAB label "P.S." | import-only technique |
| N10 | Vibrato | `Add("Vibrato")` | Y | Y: sine on the wheel, depth `NormalVibratoSemitones`, fades in/out | both: wavy line | |
| N11 | Wide vibrato | `Add("WideVibrato")` | Y | Y: `WideVibratoSemitones` | both: heavier line | |
| N12 | Tremolo bar (note tag) | `Add("TremBar")` (+ `TremBarWide`, `TremBarDive`, `TremBarDip`, `TremBarHold`, `TremBarPredive`, `TremBarPrediveDive`, `TremBarCustom`) | ~ see B20 | Y: built-in shapes when no curve | TAB: whammy diagram | the sub-type string drives the default curve and the file `WhammyType`; real curve lives on the beat (B20) |
| N13 | Natural harmonic | `Add("Harmonic")`, `HarmonicFret = 12`, `MidiValue = GuitarProImporter.HarmonicMidi("Natural", t.StringTunings[s]+t.Capo, fret, node)` | Y | Y: plays `MidiValue` (no extra transposition) | TAB: "Harm." caption | `MidiValue` must be the SOUNDING pitch; `HarmonicMidi` is `internal` (same assembly, usable by the demo). Nodes 12, 7, 5, 19, 24 |
| N14 | Artificial harmonic | `Add("ArtificialHarmonic")`, `HarmonicFret = 17` (interval), `MidiValue` via `HarmonicMidi("Artificial",...)` | Y | Y | TAB: "A.H." + sounding pitch name | |
| N15 | Pinch harmonic | `Add("PinchHarmonic")`, `HarmonicFret`, `HarmonicMidi("Pinch",...)` | Y | Y | TAB: "P.H." | |
| N16 | Tap harmonic | `Add("TapHarmonic")`, `HarmonicFret = 17.0` (may be fractional, e.g. 5.8) | Y | Y | TAB: "T.H." + tapped fret | |
| N17 | Semi harmonic | `Add("SemiHarmonic")` | Y | Y: fundamental sounds with the harmonic | TAB: "S.H." | |
| N18 | Feedback harmonic | `Add("FeedbackHarmonic")` | Y | Y | TAB: "F.B." | |
| N19 | Tapping | `Add("Tapping")` (right hand) / `Add("LeftTap")` (left hand) | Y (beat.Tap / note.IsLeftHandTapped) | tag only (no audible change) | ST: "+" above; TAB: "T" | |
| N20 | Slap / pop | `Add("Slap")`, `Add("Pop")` | Y | Y: velocity x1.15 / x1.1 (user decision D-2) | TAB: "S" / "P" label | |
| N21 | Dead slapped | `Add("DeadSlapped"); Add("Slap"); note.Dead = true` | ~ comes back as Slap + Dead (tag re-split) | Y (dead click) | TAB: "D.S." | |
| N22 | Trill | `Add("Trill")`, `TrillTargetMidi = t.PitchOf(s,f+2)`, `TrillDurationDenominator = 16` (0,1,2,4,8,16,32,64) | Y (target + speed; a missing target becomes +2 semitones, speed 16) | Y: alternates main/target at the written speed | both: "tr" + wave; TAB: "(fret)" of the upper note | |
| N23 | Tremolo picking (note tag) | `Add("TremoloPick")` with beat value B21 | Y | Y: repeated attacks at 3/4 velocity | ST: 1-3 slashes; TAB: slashes under the fret | speed is the beat field `TremoloPickDenominator` |
| N24 | Fade in | `Add("FadeIn")` | Y (beat.Fade) | Y: CC11 ramp 0..127 over the note | both: "<" hairpin passage | the drum crash (bar 3) sounds alone while it fades: the ramp is channel-wide and would swell any other drum note |
| N25 | Fade out | `Add("FadeOut")` | Y | Y: CC11 ramp down, reset after | both: ">" hairpin passage | |
| N26 | Wah open / closed | `Add("WahOpen")` / `Add("WahClose")` | Y | Y: CC1 110 / 10, reset at note end | ST: "o"/"+" and "Wah-wah on"; TAB-only view: "+"/"o" | |
| N27 | Brush down / up | `Add("BrushDown")` / `Add("BrushUp")` (+ `cell.BrushStepSlots`) | Y (fixed stroke speed; the step length is lost) | Y: strings spread, down = low string first; step from `BrushStepSlots` or 12 ms | both: arrow | |
| N28 | Arpeggio down / up | `Add("ArpeggioDown")` / `Add("ArpeggioUp")` | Y | Y: 28 ms step or `BrushStepSlots` | both: wavy arrow | |
| N29 | Rasgueado | `Add("Rasgueado")` | N | Y: same spread as brush | TAB: "rasg." caption | import-only |
| N30 | Pick stroke down / up | `Add("PickDown")` / `Add("PickUp")` | Y | N | TAB only: square / V under the staff | |
| N31 | Ghost note | `note.Ghost = true` (importer also adds string `"Ghost"`) | Y | Y: velocity x0.8 | both: parentheses | |
| N32 | Dead note | `note.Dead = true` (importer adds `"Dead"`), fret 0 | Y | Y: click of 0.04 beat | TAB "X", ST x head | |
| N33 | Grace note | add a note with `IsGraceNote = true, GraceBeforeBeat = true/false, GraceDurationSlots = 0.5..2, GraceOnsetOffsetSlots` INTO THE SAME CELL as the principal notes; string `"GraceBefore"` / `"GraceOnBeat"` / `"GraceBend"` | Y (before, on beat, bend grace; 1-slot grace length returns as 2) | Y: before-the-beat steals from the previous beat; on-the-beat delays the principal; grace slide/bend glides to principal | ST: small stemmed heads (max 3); TAB: small frets | the plain editor command `Note.Grace` only toggles the string `"GraceBefore"` (no separate grace note); legacy `cell.IsGrace` still read |
| N34 | Tie (note level) | destination `note.Tied = true`; origin `Add("Tie")` (call `GuitarProImporter.LinkTieOrigins(track)` after filling) | Y | Y: destination merged into the origin when contiguous (incl. slide-into-tie) | both: tie arc | beat-level form: B05 |
| N35 | Left / right hand fingering | `note.LeftHandFinger = 1` (0 thumb..4 little), `note.RightHandFinger = 2` (p i m a) | N | N | TAB: fingering row | import-only (no editor UI) |
| N36 | Velocity / dynamic | `note.Velocity = Dynamics.Velocities[i]` (16,33,49,64,80,95,112,127) | ~ quantised to the 8 dynamics; a beat carries the first note's | Y: MIDI velocity | N (no dynamic marks are drawn anywhere) | see B28 for the table |
| N37 | Note-level accidental / spelling | none: pitch is `MidiValue`, spelling is derived from key signature | - | Y (pitch) | ST: derived accidentals | no model field for enharmonic choice; "Change accidental" palette tool is unsupported (`Supported=false`) |

---

## 2. Beat-level options (TabCell)

| ID | Item | API | Clean .gp | Play | Engr | Notes |
|---|---|---|---|---|---|---|
| B01 | Note value whole..64th | `cell.DurationDenominator = 1/2/4/8/16/32/64` | Y | Y | both | whole = 16 slots; a 64th is 0.25 slot and so needs `RhythmicPosition` |
| B02 | Dotted | `cell.Dots = 1` (x1.5) | Y | Y | both | |
| B03 | Double dotted | `cell.Dots = 2` (x1.75) | Y | Y | both | |
| B04 | Triplet | `cell.IsTriplet = true` (or `TupletNumerator=3, TupletDenominator=2`) | Y | Y | ST: bracket / beam number 3 | fractional onsets: `cell.RhythmicPosition = slot` |
| B05 | Tuplet N:D (5:4, 6:4, 7:4, 9:8, 10:8, 11:8, 13:8, 2:3 duplet) | `cell.TupletNumerator = 5; cell.TupletDenominator = 4;` | Y | Y | ST: number | validator allows 0..64 each (both set or both 0); the UI `SetTuplet` accepts 2..13 : 1..12 |
| B06 | Rest | `cell.IsRest = true` (no notes) | Y | Y (silence) | both | an empty bar plays as a whole-bar rest; annotation-only beats (text/chord/lyric) count as beats |
| B07 | Tie (beat level) | `cell.IsTied = true` (plus N34 per note) | Y | Y | both | |
| B08 | Fermata | `cell.Fermata = true` | Y (bar-position attribute in the file format, so it also appears on the other tracks) | N (no hold) | both: fermata sign | |
| B09 | Accent | `cell.Accent = 1` | Y | Y: velocity +22 (D-1) | ST ">" | |
| B10 | Heavy accent | `cell.Accent = 2` | Y | Y: +40 | ST "^" | |
| B11 | Staccato | `cell.Staccato = true` | Y | Y: length x0.5 | ST: dot | |
| B12 | Tenuto | `cell.Tenuto = true` | N (alphaTab has no tenuto flag) | N | ST and TAB-only view: dash | |
| B13 | Brush / arpeggio stroke speed | `cell.BrushStepSlots = 0.33` (slots per string) | N (fixed 60) | Y | - | see N27/N28 for the direction |
| B14 | Pick stroke | see N30 | Y | N | TAB | |
| B15 | Tremolo bar curve (whammy) | `cell.WhammyPoints = new(){ new(){Offset=0,Value=0}, new(){Offset=30,Value=-8}, new(){Offset=60,Value=0} }` + N12 tag | ~ curve reduced to <=4 points (`SimplifyWhammy`); sub-type name rebuilt on import | Y: wheel, value/2 semitones | TAB: whammy diagram with amounts | |
| B16 | Tremolo picking speed | `cell.TremoloPickDenominator = 8/16/32/64` (allowed 0,1,2,4,8,16,32,64) | Y (beat.TremoloSpeed) | Y | slashes 1 (1/8), 2 (1/16), 3 (1/32+) | |
| B17 | Beat text | `cell.Text = "riff A"` | Y | N | above the staff | max 4096 chars |
| B18 | Chord name | `cell.ChordName = "Am7"` | ~ name only; diagram never written (`ShowDiagram=false`) | N | above the staff (accent colour) | |
| B19 | Chord diagram | none | N | N | N | **not representable**: no fingering/fret data in the model |
| B20 | Lyrics (per beat) | `cell.Lyrics = "Show-\ncase"` (lines split by `\n`, max 64 lines) | Y | N | under the TAB, one row per line | song-level free text: `song.Lyrics` (written to .tforge only) |
| B21 | Fade in / out (beat effect) | see N24 / N25 | Y | Y | both | |
| B22 | Volume swell | none | N | N | N | **not representable**: only FadeIn then FadeOut on consecutive beats approximates it (alphaTab `FadeType.VolumeSwell` has no TabForge field) |
| B23 | Voice 2 (independent rhythm grid) | `var cells = bar.CellsForVoice(1, create: true);` then fill like voice 1 | Y (a second voice is written to EVERY bar of a track that has one anywhere) | Y: own tie chain | both: stems split, `song.GrayInactiveVoice` dims the inactive voice | exactly 2 voices in the model (import merges more into voice 2) |
| B24 | Let ring / palm mute spans | see N01 / N02 | Y | Y | TAB spans | |
| B25 | Octave marks 8va / 8vb / 15ma / 15mb | `cell.OctaveShiftSemitones = 12 / -12 / 24 / -24` | Y (Ottavia) | Y: transposes the sounding pitch (D-4) | ST: label + dashed line over contiguous beats | only these five values validate |
| B26 | Stem direction override | `cell.StemDirection = StemDirection.Up / Down / Invert / Auto` | N (engraving-only) | - | Y | |
| B27 | Beam override | `cell.BeamMode = BeamMode.Force / Break / Auto`, `cell.BreakSecondaryBeamBefore = true` | N | - | Y | |
| B28 | Dynamics ppp..fff | `note.Velocity = Dynamics.VelocityFor("pp")` (table: ppp 16, pp 33, p 49, mp 64, mf 80, f 95, ff 112, fff 127) | ~ 8 steps, one per beat | Y | **N** (no dynamic marks drawn) | editor palette `dynamic:*` sets velocity only |
| B29 | Sound duration % | `cell.SoundDurationPercent = 50..200` | N | Y | N | validator 1..200 |
| B30 | Mix-table point (beat) | `cell.Mix = new MixChange{ Volume=12, Pan=-4, Chorus=8, Reverb=10, Phaser=4, Tremolo=2, Program=29, TransitionBeats=2, AllTracks=false }` | N (clean .gp loses it) | Y: CC7/10/93/91/95/92 and program change, optional ramp over `TransitionBeats` beats, optionally on all tracks | red dot above the beat | volume/effects 0..16, pan -8..8; `MixChange.Tempo` is not used by the compiler (tempo goes on the bar, S12) |
| B31 | Off-grid onset | `cell.RhythmicPosition = 4.8` (sixteenth slots) | Y (recomputed) | Y | Y | the rule `RhythmicPosition ?? max(i, cursor)` is shared by every consumer (`MusicTime`) |
| B32 | Annotation-only beat | `cell.ChordName`/`Text`/`Lyrics` on a cell with no notes | Y | N | Y | `HasAnnotation` |
| B33 | Repeat previous beat | editor command only (`Note.RepeatBeat`) | - | - | - | copies the last beat; nothing new to store |
| B34 | Dead/ghost/accent combos | combine N31/N32 with B09-B12 | Y | Y | Y | |
| B35 | Drum notes (beat) | `new TabNote{ StringIndex = GuitarProImporter.DrumLine(midi), Fret = midi, MidiValue = midi, Velocity = v }` | Y (one file articulation per GM sound) | Y | both (per drum map, T14) | lines: 0 cymbals, 1 hi-hat, 2 high toms, 3 snare, 4 low toms, 5 kick |

---

## 3. Bar and song-level options (MeasureModel, SongProject)

Structure (time signature, key, repeats, directions, tempo) is read from the FIRST track (`PlaybackOrder`, `MusicTime.BarOf`, exporter), so set it
on every track like the showcase does (`foreach t in song.Tracks`).

| ID | Item | API | Clean .gp | Play | Engr | Notes |
|---|---|---|---|---|---|---|
| S01 | Song time signature | `song.TimeSignatureNumerator = 4; song.TimeSignatureDenominator = 4;` | Y | Y | ST | |
| S02 | Bar time signature (3/4, 6/8, 7/8, 5/4, 7/4, 12/8, 2/2, 9/16...) | `bar.TimeSigNum = 7; bar.TimeSigDenom = 8;` | Y | Y | ST: shown when it changes | numerator 1..32, denominator a power of two to 64. `BarSlots = n*16/d`. **`bar.Cells` must hold at least `BarSlots` cells** (template default is 16; 7/4 needs 28) |
| S03 | Song key, major | `song.KeySignature = -7..7` | Y | no audio effect (pitch is explicit) | ST: accidentals | |
| S04 | Bar key change, major | `bar.KeySignature = 2` | Y | - | ST: naturals + new accidentals | |
| S05 | Key, minor | `song.KeySignatureMinor = true` / `bar.KeySignatureMinor = true` | Y | - | accidentals of the relative key (the word "minor" is not engraved; `KeyName` is for status text) | |
| S06 | Clef | `bar.Clef = Clefs.Guitar ("G8") / Clefs.Treble ("G") / Clefs.Bass ("F4") / Clefs.Alto ("C")` | N | - | ST | parser also takes `bass`, `alto`, `tenor`, `G/F/C` + line 1-5 and an `8va`/`8vb` suffix; UI cycles the four |
| S07 | Repeat start | `bar.RepeatStart = true` | Y | Y | both | |
| S08 | Repeat end with count | `bar.RepeatEnd = true; bar.RepeatCount = 3` (2..99) | Y | Y | both: dots, "x3" above when >2 | |
| S09 | Alternate endings 1..8 | `bar.AlternateEnding = 1` | Y | Y | ST: volta bracket | UI: one number per bar |
| S10 | Multi-pass ending ("1.2.") | `bar.AlternateEndingMask = 0b011` (bit n = pass n+1) | Y | Y | bracket "1.2." | code/import only |
| S11 | Double bar line | `bar.IsDoubleBar = true` | Y | - | both | |
| S12 | Tempo (song) | `song.Tempo = 120` (20..400) | Y | Y | tempo text on bar 1 | |
| S13 | Tempo change at a bar | `bar.TempoChange = 140` | Y | Y (carries on until the next change) | tempo text | |
| S14 | Tempo change inside a bar | `bar.MidBarTempos = new(){ new TempoPoint(8, 80) }` (slot, bpm) | Y (instant step; one automation per point) | Y | tempo text | code/import only |
| S15 | Tempo ramp (accelerando / ritardando) | `new TempoPoint(0, 60, RampSlots: 16)` (linear per beat to the target over N slots) | ~ clean .gp keeps the step, ramp length lost (and a bar-start ramp's start tempo) | Y (exact integral, `MusicTime.RampMs`) | tempo text | code/import only |
| S16 | Triplet feel / swing 8th | `bar.TripletFeelKind = TripletFeels.Eighth` ("Triplet8th"; legacy `bar.TripletFeel = true`) | **N** (exporter does not write `MasterBar.TripletFeel`; fixable) | Y: plain eighth pairs only | "(♫ = ♩♪)" text | |
| S17 | Triplet feel 16th | `bar.TripletFeelKind = TripletFeels.Sixteenth` | **N** (same) | Y: plain sixteenth pairs | "(♬ = ♪♬)" | |
| S18 | Free time | `bar.FreeTime = true` | **N** (fixable: alphaTab `IsFreeTime`) | N | text "free" | |
| S19 | Pickup / anacrusis bar | `bar.Anacrusis = true` (and give the bar a shorter time signature) | **N** | N (flag only) | N (flag only) | TabForge expresses a pickup with a short time signature; the flag is imported and copied, not used |
| S20 | Simile, repeat 1 bar | `bar.SimileOneBar = true` | **N** (exporter writes the bar's own notes; the sign is lost) | Y: replays the previous bar of the same track | glyph | |
| S21 | Simile, repeat 2 bars | `bar.SimileTwoBar = true` | **N** | Y | glyph | |
| S22 | Direction: Segno | `bar.Directions = "Segno"` (comma-joined list, several allowed) | Y (`TargetSegno`) | Y | sign at left | |
| S23 | Direction: Double Segno | `"SegnoSegno"` | Y | Y | sign | not in the UI dialog |
| S24 | Direction: Coda | `"Coda"` | Y | Y | sign | |
| S25 | Direction: Double Coda | `"DoubleCoda"` | Y | played as Coda | sign | not in the UI dialog |
| S26 | Direction: Fine | `"Fine"` | Y | Y: ends playback only after a D.C./D.S. (D-5) | "fine" | |
| S27 | Direction: To Coda | `"ToCoda"` | Y (`JumpDaCoda`) | Y | "To Coda" | |
| S28 | Direction: To Double Coda | `"ToDoubleCoda"` | Y | played as To Coda | text | not in UI |
| S29 | Direction: D.C. | `"DaCapo"` | Y | Y | "D.C." | |
| S30 | Direction: D.C. al Coda | `"DaCapoAlCoda"` | Y | Y | "D.C. al Coda" | |
| S31 | Direction: D.C. al Fine | `"DaCapo"` + a `"Fine"` bar (the spelling `"DaCapoAlFine"` is engraved but is NOT a jump for `PlaybackOrder`) | Y | Y | "D.C." | file name `JumpDaCapoAlFine` also works and is normalised |
| S32 | Direction: D.S. | `"DalSegno"` | Y | Y | "D.S." | |
| S33 | Direction: D.S. al Coda | `"DalSegnoAlCoda"` | Y | Y | "D.S. al Coda" | |
| S34 | Direction: D.S. al Fine | `"DalSegno"` + `"Fine"` | Y | Y | "D.S." | |
| S35 | Direction: D.S.S. / D.S.S. al Coda | `"DalSegnoSegno"` / `"DalSegnoSegnoAlCoda"` | Y | Y (falls back to Segno when no double segno) | "D.S.S." | not in UI |
| S36 | Direction: Al Double Coda variants | `"DaCapoAlDoubleCoda"`, `"DalSegnoAlDoubleCoda"`, `"DalSegnoSegnoAlDoubleCoda"` | Y | normalised to the plain Coda jump | text | file-name only |
| S37 | Section / marker | `song.Markers.Add(new MarkerModel{ MeasureIndex=8, Title="Solo", ColorHex="#8B5CF6", LockPosition=false, LengthBars=null })` | ~ title only (colour, lock, length lost) | - | coloured heading | `bar.SectionName` is a second, text-only copy the importer fills; the renderer prefers the marker; set both with the same title |
| S38 | Forced line break | `bar.ForceLineBreak = true` | N | - | Y (new system) | |
| S39 | Prevent line break | `bar.PreventLineBreak = true` | N | - | Y | |
| S40 | Mix-table tempo / bar automation | see B30 and S13 | | | | |
| S41 | Title block | `song.Title, Subtitle, Artist, Album, MusicAuthor, LyricsAuthor, Copyright, TabAuthor, Instructions, Notice` | Y | - | header | |
| S42 | Whole-song lyrics text | `song.Lyrics = "line one\nline two"` | N | - | N | per-beat lyrics are B20 |
| S43 | Gray inactive voice | `song.GrayInactiveVoice = true` | N | - | Y | presentation preference |
| S44 | Dynamics ppp..fff | B28 | | | | |
| S45 | Bar numbering, tempo text visibility | view settings, not in the song | | | | |
| S46 | Bar with content shorter/longer than its signature | leave cells unfilled / overfill | Y | short bars play as written (.gp imports only trim) | red bar when overfilled | test data only |
| S47 | Empty bar (whole-bar rest) | untouched `TemplateFactory.Measures` bar | Y | silence | blank staff | |
| S48 | Anacrusis via short signature | `bar.TimeSigNum = 1; bar.TimeSigDenom = 4` in bar 0 then 4/4 | Y | Y | ST | the supported way to write a pickup |
| S49 | Common-time / cut-time symbols | none | N | - | N | **not representable**: only numeric signatures |
| S50 | Tempo text / tempo note value (e.g. "Allegro", dotted quarter = 60) | none | N | - | N | **not representable**: BPM of a quarter only |

Rows S40, S44 and S45 are cross-references or view settings, kept so the ID list matches the plan; they add no new model field.

---

## 4. Track and instrument options (TrackModel, MixerSettings)

| ID | Item | API | Clean .gp | Play | Engr | Notes |
|---|---|---|---|---|---|---|
| T01 | Guitar 6 string | `new TrackModel{ Kind=TrackKind.Guitar, StringTunings = new(){64,59,55,50,45,40} }` (high to low) | Y | Y | TAB 6 lines + ST | tuning presets: Standard E, Eb, D, C#, C, Drop D/C#/C/B/A, Open G, Open D, DADGAD |
| T02 | Guitar 7 string | `StringTunings = {64,59,55,50,45,40,35}` | Y | Y | TAB 7 lines | presets: Standard B, Drop A, A standard |
| T03 | Guitar 8 (and 9) string | `{64,59,55,50,45,40,35,30}` (9: `...,25`) | Y | Y | | validator allows up to 16 strings |
| T04 | Bass 4 string | `Kind=Bass, {43,38,33,28}`, program 33/34 | Y | Y | | presets: E, Eb, D, Drop D, Drop C |
| T05 | Bass 5 string | `{43,38,33,28,23}` | Y | Y | | B and "E, high C" tunings |
| T06 | Bass 6 / 7 string | `{48,43,38,33,28,23}` / `{53,48,43,38,33,28,23}` | Y | Y | | |
| T07 | Drums | `Kind=Drums, MidiChannel=9, InstrumentName="Drum Kit (Standard)", StringTunings={49,42,48,38,43,36}` (SIX lines; five fails the kick line check) | Y (articulations per GM sound) | Y (channel 10) | drum staff + TAB by map | |
| T08 | Keyboard / piano | `Kind=TrackKind.Keys, MidiProgram=0`, `StringTunings = {96,84,72,60,48,36,24}` (importer's octave "strings"; `TrackController` uses six) | Y (pitch-only) | Y | no grand staff: one staff, clef per bar | notes placed with the private `PlacePitch` rule: string = highest octave string at or below the pitch, fret = remainder |
| T09 | Other GM instruments (strings, brass, reed, synth, ethnic...) | `t.MidiProgram = 0..127`, `t.InstrumentName = GeneralMidi.NameOf(program)` (`InstrumentCatalog.All`: 128 GM programs in 16 families + 9 drum kits); use `Kind = Guitar` (fretted look) or `Keys` | Y (program) | Y | | `TrackKind.Other` exists but nothing creates it (importer maps pitch-only tracks to Keys) |
| T10 | Drum kits | `MidiProgram` on channel 9: Standard 0, Room 8, Power 16, Electronic 24, TR-808 25, Jazz 32, Brush 40, Orchestra 48, SFX 56 | Y (program) | Y | | |
| T11 | Custom tuning | any list of 0..127, `Tuning` edit in `TrackPropertiesWindow` | Y | Y | | |
| T12 | Capo | `t.Capo = 2` (0..48); `MidiValue = t.PitchOf(s,f)` = tuning + capo + fret | Y | Y | TAB shows frets relative to the capo | |
| T13 | Number of frets | `t.NumberOfFrets = 24` | N | - | fretboard view | |
| T14 | Drum map preset | `t.DrumMapPreset = DrumMaps.GuitarPro5 / GuitarPro6 / DrumTab / Custom` | N | - | Y (TAB labels, 6 or 7 lines) | ".gp5", ".gpx/.gp", "Drum tab (lines)", "Custom" |
| T15 | Custom drum map | `t.CustomDrumMap = new(){ new DrumMapEntry{ Midi=38, TabLine=3, Label="SD", StaffStep=3, Head="normal" } }` (heads: x, normal, circle, diamond) | N | - | Y | |
| T16 | MIDI program | `t.MidiProgram = 29` (0..127) | Y | Y | | |
| T17 | MIDI channel | `t.MidiChannel = 0..15` (9 = drums; `ChannelAllocator` may reassign) | ~ re-assigned on import | Y | | |
| T18 | Track volume | `t.Volume = 100` (0..127) | ~ quantised to 0..16 | Y (CC7, after group level) | mixer | |
| T19 | Track pan | `t.Pan = 64` (0..127, 64 centre) | ~ 0..16 | Y (CC10) | mixer | |
| T20 | Chorus / reverb send | `t.Chorus = 0; t.Reverb = 24` (0..127) | N | Y | mixer | |
| T21 | Transpose | `t.Transpose = -12..12` | N | Y | | |
| T22 | Mute / solo | `t.Mute = true; t.Solo = true` | Y | Y | | leave off in the demo (it must play all tracks) |
| T23 | Name, colour, performer, notes | `t.Name, t.ColorHex = "#F61A16", t.Performer, t.TrackNotes` | name, colour Y; performer, notes N | - | row | |
| T24 | Mixer group per track | `t.MixerGroup = "Rhythm"` (<=64 chars; null = by instrument) | N (level baked) | Y | | auto groups: Guitars, Basses, Keys, Drums, Other (+ All tracks, Other instruments) |
| T25 | Group levels | `song.Mixer.Edit("Guitars").Volume = 80; .Pan = -10; .Pitch = 0; .Mute; .Solo`; `song.Mixer.Grouping = MixerGrouping.ByInstrument / Compact / None` | ~ volume and pan baked into each track; pitch not baked | Y | mixer | group volume 0..200 %, pan -64..63, pitch -24..24 |
| T26 | Master / buses | `song.Mixer.MasterPan`, `song.Mixer.Master`, `song.Mixer.Bus("Guitars")` | N | Y (live chain) | | plug-in chains hold machine paths: leave empty in a redistributable demo |
| T27 | Sound source and rig | `t.SoundSource = SoundSources.Midi`, `t.Rig = new RigPreset{ Name="Clean", ArticulationMap="Generic Guitar" }` | N | Y | | `ArticulationMap`: Generic Guitar, Generic Bass, Piano, GM Drums |

---

## 5. Cannot be represented (or only partly)

Model cannot hold it at all:

- Chord diagrams (B19): only the chord NAME exists.
- Volume swell (B22): only FadeIn / FadeOut.
- More than two voices (B23).
- Common-time / cut-time symbols (S49) and tempo wording or non-quarter tempo units (S50).
- Barline styles other than single, double and repeat (no final, dashed or heavy barline on a mid-song bar).
- Grand staff for keyboards (one staff per track; only the clef changes per bar), pedal marks, hairpins/crescendo wedges, staff text other than beat text.
- Fine-grained per-note accidental spelling (T37 / N37).

Model holds it, but a renderer or playback gap exists (the demo can carry it, nothing shows or sounds):

- Dynamics ppp..fff are not engraved (they only change velocity).
- Fermata is not played as a hold; Free time and the Anacrusis flag have no playback effect (Anacrusis also no engraving).
- Tapping, pick strokes and finger numbers do not change audio.
- Bends and whammy are engraved in TAB only, not on the staff.
- The minor-key flag changes nothing visible (the key signature shows the relative major accidentals only).

Lost in a CLEAN .gp (fine with the embedded project, which the demo uses; each is an exporter gap, several are fixable in `GuitarProExporter.Build`):

- Triplet feel, free time, simile bars, pickup flag, clefs, line breaks (S16-S21, S06, S38, S39): `MasterBar.TripletFeel`, `IsFreeTime`, `SimileMark`, `IsAnacrusis` and `Bar.Clef` exist in alphaTab and are simply not written.
- Mix-table points (B30), sound duration % (B29), tenuto (B12), beam and stem overrides (B26, B27), finger numbers (N35), rasgueado and pick slides (N29, N09), tempo ramp length (S15), marker colours (S37), track reverb/chorus/transpose/performer/notes/drum map (T20, T21, T14, T23), whole-song lyrics text (S42).
- Approximated: bends to 3 points, whammy to 4 points, dynamics to 8 steps (one per beat), volume and pan to 0..16 with group level baked in.

Not authorable in the editor UI (code or import only): bend curves, whammy curves, harmonic node/fret, trill target and speed, slide targets, finger numbers,
grace notes as separate notes, mid-bar tempo, tempo ramps, multi-pass endings, most directions beyond nine, the five-line drum staff presets beyond the four in the dialog.
The demo can still set all of them because it builds the model directly.

---

## 6. Playback, export and render facts the demo builder must respect

1. `TabNote.MidiValue` is the SOUNDING pitch and what playback uses; always set it (`t.PitchOf(s,f)`, drums `midi`, harmonics `HarmonicMidi`).
2. Whole note = 16 slots; a beat onset is `cell.RhythmicPosition ?? max(i, cursor)`; fractional onsets (tuplets, 32nds, 64ths) need `RhythmicPosition`; the `Put` helper in `SelfTestGpFixture` moves to the next free cell and sets it when the slot is not the cell index.
3. `bar.Cells.Count` must be at least `MusicTime.BarSlots(num, den)`; `TemplateFactory.Measures(n)` gives 16 cells per bar. Replace `bar.Cells` with `Enumerable.Range(0, slots).Select(_ => new TabCell()).ToList()` for a bar longer than 16 slots. The importer only grows to 8x, the validator caps 256 cells.
4. Bar structure on EVERY track (first track wins for playback, export reads the first track that has the bar).
5. A track with voice 2 in any bar is exported with a second voice in every bar (handled by the exporter); in the model create it with `CellsForVoice(1, create: true)`.
6. After filling ties, call `GuitarProImporter.LinkTieOrigins(track)` so origins carry `"Tie"`.
7. Drum tracks need six drum lines, `Kind = Drums`, `MidiChannel = 9`.
8. Validation limits (`ProjectValidator`, `InputLimits`): tracks 256, measures per track 20,000, cells per measure 256, notes per cell 256, strings 16, frets 127, techniques per note 64, curve points 512, tempo 20..400, time-signature numerator <= 32 and power-of-two denominator <= 64, title 256 chars, beat text 4096.
9. Run `ProjectValidator.Validate(song)` (or simply `ProjectService.RestorePersistedBytes(ProjectService.PersistBytes(song))`) before writing; it throws `InvalidDataException` with the reason.
10. `song.IsDirty = false`; `song.MarkTimelineChanged()` is called by the editor, not needed for a fresh project.
11. Dynamics that a palette tool writes: velocities 16, 33, 49, 64, 80, 95, 112, 127.

---

## 7. Existing fixture generators and the plan for `--write-demo-song <out.gp>`

### What exists

| Generator | Where | What it builds | How it is used |
|---|---|---|---|
| `SelfTest.BuildSyntheticGpSong()` ("basic") | former GuitarPro fixture (full suite, removed in Batch 2 ([canonical note](TESTING.md#removed-in-batch-2-canonical-note))) | 2 tracks (guitar, 4-string bass), 4 bars of quarter notes; `TemplateFactory.Measures(4)`, `cell.DurationDenominator = 4`, `cell.Notes.Add(new TabNote{ StringIndex, Fret, MidiValue })` | `--write-gp-fixture <out.gp> basic`; self-test export/re-import |
| `SelfTest.BuildShowcaseSong()` ("showcase") | same file | 4 tracks (lead, drop-D rhythm, bass, 6-line drums), 8 + N technique bars + outro: structure on every track (tempo changes, repeat with endings 1/2, 7/8 bar), markers, lyrics, palm-mute chugs, power chords, slap/pop, full drum kit. Helpers `Put(m,t,slot,den,dots,trip,notes)`, `FretNote(t,s,fret,...tech)`, `Drum(midi,vel,ghost)`. It pulls `GmSongAudit.TechniqueSong()` (one bar per `GpEffects.All` name plus six bend shapes) and moves those `Cells` into the solo section, adding accent/staccato/tenuto/fermata/ghost/dead and a default whammy curve per technique bar | `--write-gp-fixture <out.gp> [showcase]` writes a CLEAN .gp (`embedProject: false`); `CheckShowcaseGuitarProFixture` round-trips it |
| `GmSongAudit.TechniqueSong()` | former WPF app (removed in Avalonia migration; was `src/TabForge/Diagnostics/GmSongAudit.cs`), now `Source/src/TabForge.Avalonia/SelfTests/Release/GuitarPro/SelfTestTechniqueSong.cs` | `TemplateFactory.Blank()` plus one bar per technique name | `--audit-gm-techniques`; `RtTechniqueSong` canonicalises it (`RtCanonicalizeTechniqueBars`) |
| `RtDenseSong()` | `SelfTestRoundTripSemantics.cs` | 12 bars x 5 tracks: capo 2 lead, DADGAD, 5-string bass, 7-string guitar, drums; 4/4, 3/4, 6/8, repeat x3 with mask ending, key change to a minor key, mid-bar tempo, double bar, dynamics ladder, bends with curves, tie across bars, grace note, natural + artificial harmonic via `HarmonicMidi`, slides with `SlideTargetMidi`, triplet, 5:4, dotted, accent/heavy/staccato/fermata, vibrato kinds, voice 2, trill with target. Helpers `RtNote`, `RtPut` | `--roundtrip-semantics`; compares .tforge, embedded .gp and clean .gp |
| `SfSong` / `SfNote` / `SfBar` and the `Sf*` cases | `SelfTestSyntheticFixtures.cs` | small songs per topic: repeat/endings (incl. `AlternateEndingMask`), D.C./D.S./Coda navigation (TabForge and file direction names), tempo steps and ramps (`TempoPoint`), mix-table (`MixChange`), tuplets (triplet, 5:4, 6:4, dotted, double dotted), two voices, spans; asserts performed bar order and onsets | self-test (`TestSyntheticFixtures`) |
| `DemoSongFactory.Create()` | `Source/src/TabForge.Core/Presets/DemoSongFactory.cs` | "Precision Demo": 5 tracks, 64 bars, mixed metres, simile bar, repeats, 9 sections, dense/sparse/empty bars | `--gendemo [out.tforge]` (.tforge) |
| `DiagnosticSongFactory.All()` | `Source/src/TabForge.Core/Presets/DiagnosticSongFactory.cs` | 19 one-behaviour songs (pulses, triplets, dotted, ties, tempo changes...) | `--gendiag <dir>` |
| "Ashen Meridian" sample | shipped file `TabForge Demo - Ashen Meridian.gp` in the `Samples` folder at the repository root, next to the source folder (its generator, a pyguitarpro .gp5 script, is not in this checkout) | shipped 34-bar, 4-track demo | `SfDemoSong` asserts it |
| `--exportgp <song> <out.gp>` | `DiagnosticCommands.RunExportGp` | exports ANY song WITH the embedded project and re-imports to compare counts | |

Common recipe (what `--write-gp-fixture`, the showcase and the dense song all do):

```csharp
var song = new SongProject { Title = "...", Tempo = 120, KeySignature = 2 };
var lead = new TrackModel { Name = "Lead", Kind = TrackKind.Guitar, MidiProgram = 29, MidiChannel = 0,
                            Measures = TemplateFactory.Measures(totalBars) };          // 16 empty cells per bar
song.Tracks.Add(lead);
foreach (var t in song.Tracks) { t.Measures[4].RepeatStart = true; t.Measures[4].TempoChange = 140; ... }   // structure on every track
var cell = lead.Measures[0].Cells[0];
cell.DurationDenominator = 4;
cell.Notes.Add(new TabNote { StringIndex = 1, Fret = 5, MidiValue = lead.PitchOf(1, 5), Velocity = 95 });
song.Markers.Add(new MarkerModel { MeasureIndex = 0, Title = "Intro", ColorHex = "#2E74B5" });
song.IsDirty = false;
GuitarProExporter.Save(song, path, embedProject: false);   // fixture default; the demo uses true
```

### Plan: `--write-demo-song <out.gp>`

1. New builder `Presets/FullDemoSongFactory.cs` (public static `SongProject Create()`), kept apart from `DemoSongFactory` (the precision demo) and from the Python sample.
2. Register in `DiagnosticCommands.Commands`, like `--write-gp-fixture`:
   ```csharp
   ["--write-demo-song"] = args => args.Length < 2 ? Usage("--write-demo-song <out.gp>") : Guard("Demo song", () =>
   {
       var song = FullDemoSongFactory.Create();
       var outPath = FilePathPolicy.OutputFile(args[1], "score file", ".gp");
       GuitarProExporter.Save(song, outPath, embedProject: true);          // the whole project rides inside the .gp
       var back = GuitarProExporter.TryReadEmbedded(outPath);              // verify the embedded copy
       return back is not null && ProjectService.ContentHash(back).AsSpan().SequenceEqual(ProjectService.ContentHash(song)) ? Ok : CheckFailed;
   })
   ```
   Documented in the usage list (README/TOOLS_AND_HOTKEYS "Diagnostics" section, CHANGELOG "Unreleased").
3. Build it bar-plan first: a table of bars to rows N01..N37, B01..B35, S01..S50, T01..T27 in this file, one technique family per section (Intro = structure, Verse = durations and tuplets, Solo = one bar per technique, Bridge = voices/mix/tempo/feel, Outro = directions), so the coverage can be asserted by a self-test (`DemoCoverage`: flatten the song and check every technique string, every direction, every bar flag appears at least once).
4. Helpers to copy (they are `private` in `SelfTest`, so copy into the factory or make them `internal`): `Put`, `FretNote`/`RtNote`, `Drum`, plus a new `EnsureSlots(bar)` that resizes `Cells` to `BarSlots` for long bars, plus `ApplyToAllTracks(bar => ...)`.
5. Harmonics via `GuitarProImporter.HarmonicMidi`; ties via `LinkTieOrigins`; HOPO via the Origin/Destination pair; drums via `GuitarProImporter.DrumLine`.
6. Instruments: lead guitar (6), rhythm guitar drop D (6, capo 0), 7-string, 8-string, bass 4, bass 5, bass 6, keys (program 0 or strings 48), drums, plus one track per extra GM family as needed; set a Capo on one track; two mixer groups.
7. Tests to add: `TestFullDemoSong` (registered with `Guard` in `SelfTest.cs`): validator passes, `.tforge` and embedded `.gp` round trip with an empty allow-list via `RtVerify(..., RtTforge / RtGpEmbedded, ...)`, `PlaybackOrder` covers every bar, timeline compiles, and the coverage walk finds each technique string. Clean `.gp` is logged with `RtGpClean` allow-list additions for the gaps in section 5.
8. After building: `--dump`, `--audit` (playback), `--render` and `--layout-audit` on the output to check engraving overlaps, then open it in the running app (visual inspection rule).
