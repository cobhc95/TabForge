# Built-in demo song plan: "Ashen Meridian" (full arrangement)

Composer's specification (Remy, 2026-09-30). **Implemented** in `Source/src/TabForge.Core/Presets/FullDemoSong/FullDemoSongFactory.cs`. It specifies the arrangement for the built-in demo song `Ashen Meridian`.

- **Inputs:**
  - `Source/docs/DEMO_SONG_COVERAGE.md` (feature IDs N01..N37, B01..B35, S01..S50 and T01..T27, referred to as "the coverage doc");
  - `samples/make_demo_song.py` (no longer in this checkout as of 2026-10-22; the old 34-bar Ashen Meridian, CC0, used only as a motif source).
- **Credits:** title "Ashen Meridian", artist "TabForge Demo", CC0 1.0.
- **Output:** `--write-demo-song <out.gp>` writes a .gp with the embedded project (`embedProject: true`), so every item below
  survives.
- **Originality.** All music below is new. The only borrowed material is our own: the old chorus cell (step, step, leap back up a third)
  and the old chorus progression (VI–III–VII–i). Both come from the CC0 sample and are re-harmonised in A.
  No riff, melody, solo, lyric or drum fill is taken or paraphrased from any other song.

---

## 0. Conventions used in this plan

- **Bars** are numbered from 1 in *source* order. The code index is `bar - 1` (`MeasureIndex`, `Measures[bar-1]`).
- **Slots:** a 16th note is 1 slot and a whole note is 16. A 4/4 bar has 16 slots, 7/8 has 14, 3/4 has 12, 2/4 has 8 and 1/4 has 4.
- **Strings:** `sN` counts from the highest string, so s1 is the top string. The code uses `StringIndex = N - 1`, which matches
  `StringTunings`, listed high to low. Every fret is written relative to the capo.
- **Pitch:** `MidiValue = t.PitchOf(s, f)` everywhere, except harmonics (`HarmonicMidi`) and drums (the GM note itself).
  Octave marks (B25) are **not** folded into `MidiValue`, because the compiler adds them (D-4).
  Confirm with `--dump` on one 8va bar.

### 0.1 Riff grammar (guitars, bass and keys)

A bar is a space-separated list of tokens: `DUR:CONTENT[+flag...]`. **Every bar's durations are checked by the `Riff()` helper
and must add up to the bar's slot count** (see section 6.2).

| Part | Syntax | Meaning |
|---|---|---|
| DUR | `1 2 4 8 16 32 64`, then `.` or `..`, then `t` (3:2) or `(n:d)` | e.g. `8.` = 3 slots, `2..` = 14, `16(5:4)` = 0.8, `8(2:3)` = 3 |
| CONTENT | `r` | rest |
| | `s7:3` | one note |
| | `[s7:0 s6:0 s5:0]` | chord (one cell) |
| Shorthand (7-string drop A) | `O` | `s7:0+pm` (palm-muted open chug) |
| | `On` | `s7:n+pm` |
| | `X` | `s7:0` dead note |
| | `Pn` | power chord `[s7:n s6:n s5:n]` |
| | `Qn` | low octave shape `[s6:n s4:n+2]` |
| Flags | `>` / `>>` | accent (`cell.Accent = 1`) / heavy accent (`= 2`) |
| | `pm` `lr` | palm mute / let ring |
| | `st` `tn` | staccato / tenuto |
| | `g` | ghost (`note.Ghost = true`) |
| | `vib` `wv` | vibrato / wide vibrato |
| | `h` | HOPO origin; the next note on the same string becomes the destination (the pair convention from N03) |
| | `ls` `ss` | legato slide / shift slide to the next note on the string (`SlideTargetMidi` set) |
| | `sib` `sia` | slide in from below / above |
| | `sou` `sod` | slide out up / down |
| | `psu` `psd` | pick slide up / down |
| | `pd` `pu` | pick stroke down / up |
| | `tie` | tied from the previous note; call `LinkTieOrigins` afterwards |
| | `fin` `fout` | fade in / out |
| | `wo` `wc` | wah open / closed |
| | `trem8/16/32/64` | `TremoloPick` + `cell.TremoloPickDenominator` |
| | `ferm` | fermata |
| | `bd` `bu` | brush down / up |
| | `ad` `au` | arpeggio down / up |
| | `rasg` | rasgueado |
| | `ph` `nh` `ah` `th` `sh` `fb` | pinch, natural, artificial, tap, semi and feedback harmonic; the node or fret is given in the bar text |
| | `slap` `pop` `ds` | slap / pop / dead-slapped |

Bends, whammy curves, trills and grace notes are spelled out where they are used. Curve points are
`(Offset 0..60, Value in quarter-tones)`, so 4 = one tone and −24 = −12 semitones.

### 0.2 Velocity for pitched tracks

- **Base:** each section sets a dynamic (section 2.2). `note.Velocity` = that dynamic's value (ppp 16, pp 33, p 49, mp 64,
  mf 80, f 95, ff 112, fff 127).
- **Accents:** accent flags add +22 / +40 at playback (D-1), so accented notes keep the base velocity.
- **Chugs:** a seeded jitter of ±3 is added (section 5.2) so the down-picking does not sound machine-gunned.
- **Double-tracking:** Rhythm R uses the same base −2 with its own seed, so L and R never match note for note.

---

## 1. Concept

### 1.1 Key, modes and harmony

- **Home key:** A minor. `song.KeySignature = 0`, `song.KeySignatureMinor = true`.
- **Modes by section:**

  | Section | Mode | Colour |
  |---|---|---|
  | Verses, bridge, breakdown | A Phrygian | Bb (the half-step up from the open low string); Eb tritone stabs |
  | Choruses, solo | A Aeolian | progression **F – C – G – Am – F – C – G – E** (VI–III–VII–i, with a harmonic-minor V at the turnaround) |
  | Pre-choruses | Aeolian | Am – F – G – E, as ringing octaves |
  | Lead climaxes | A harmonic minor | G# |

- **Key change:** the final chorus moves **up a whole step to B minor** at bar 118 (`bar.KeySignature = 2`,
  `KeySignatureMinor = true`). The pivot is a 2/4 "hiccup" bar: G5 → A5 (bVI–bVII of B minor) → Bm.
- **Motif M (the "Meridian cell"):** degrees 5-4-3-5, E–D–C–E, played dotted quarter, 8th, quarter, quarter.
  - Developed from the old CC0 chorus cell.
  - It appears in the Intro B riff (low), the chorus hook, the solo call, the interlude artificial harmonics, the piano and the outro.

### 1.2 Tempo map

| Where | Event | API |
|---|---|---|
| Song | 150 BPM | `song.Tempo = 150` (S12) |
| Bar 90, slot 12 | **Mid-bar drop** to 112, on the beat of silence before the breakdown | `MidBarTempos = { new TempoPoint(12, 112) }` (S14) |
| Bar 91 | 112 restated, so the tempo text sits at the section head | `TempoChange = 112` (S13) |
| Bar 101 | **Accelerando ramp** from 112 to 150 across the wind-up bar | `MidBarTempos = { new TempoPoint(0, 150, RampSlots: 16) }` (S15) |
| Bar 102 | 150 restated for the solo | `TempoChange = 150` |
| Bar 139 | Ritardando from 150 to 141 | `TempoPoint(0, 141, 16)` |
| Bar 140 | Ritardando from 141 to 132 | `TempoPoint(0, 132, 16)` (ramps clip at the bar end, hence one per bar) |
| Bars 141–144 | 132, held | |

### 1.3 Time signatures

- **Song:** 4/4 (S01).
- **Per-bar signatures (S02):**

  | Bar(s) | Signature | Role |
  |---|---|---|
  | 1 | **1/4** | pickup (S48 + `Anacrusis = true`, S19) |
  | 10 | **3/4** | the swell |
  | 83–86 | **7/8** | odd-meter bridge, grouped 2+2+3 |
  | 118 | **2/4** | key-change hiccup |

- **Other structure:**
  - Bars 87–89 are 4/4 with a 7-sixteenth polymetric cell.
  - Bar 142 is **free time** (S18).
  - Bars that are not 4/4 must have `EnsureSlots(bar)` applied on every track.

### 1.4 Tunings

| Instrument | Tuning (high to low) | Notes |
|---|---|---|
| 7-string guitar | Drop A: `{64,59,55,50,45,40,33}` (E4 B3 G3 D3 A2 E2 A1), T02 | |
| 8-string guitar | `{64,59,55,50,45,40,33,28}`: drop A plus a low **E1**, T03 | The E1 string is used only for the breakdown "drop" |
| 6-string guitar | E standard `{64,59,55,50,45,40}`, T01 | The top six strings of drop A are E standard, so lead fingerings transfer directly |
| Clean guitar | E standard, **capo 5**, T12 | Open "Em / C / G / D / B7" shapes sound as Am / F / C / G / E |
| 5-string bass | Drop A `{43,38,33,28,21}` (G2 D2 A1 E1 A0), T05 | |
| Sub Drop | 4-string custom tuning `{50,45,40,33}` (D3 A2 E2 A1), T04 + T11 | `Transpose = -12` (T21): written an octave up, like a real bass part, and sounds an octave down |

### 1.5 Line-up (10 tracks)

Channel 9 is reserved for drums. The effect channels are assigned automatically. Five tracks bend or whammy (RhyL, RhyR, Lead,
LeadHarm, Sub), which fits the six free channels (10–15). **Do not add bends to Bass, Clean or the keys.**

| # | Track (`Name`) | Kind | Tuning | Prog (T16) | Ch (T17) | Vol (T18) | Pan (T19) | Rev / Cho (T20) | Group (T24) | Colour (T23) | Frets (T13) |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | Rhythm Gtr L | Guitar | 7-str drop A | 30 Distortion | 0 | 100 | 0 (hard L) | 16 / 0 | Guitars | `#D9423A` | 24 |
| 2 | Rhythm Gtr R | Guitar | 8-str | 30 | 1 | 100 | 127 (hard R) | 16 / 0 | Guitars | `#B8322C` | 24 |
| 3 | Lead Gtr | Guitar | 6-str E std | 29 Overdrive | 2 | 96 | 64 | 40 / 8 | Guitars | `#F0A830` | 24 |
| 4 | Lead Harmony | Guitar | 6-str E std | 29 | 3 | 86 | 50 | 40 / 8 | Guitars | `#E0C050` | 24 |
| 5 | Clean Gtr | Guitar | 6-str E std, capo 5 | 27 Clean | 4 | 84 | 84 | 72 / 48 | Guitars | `#58B8D8` | 22 |
| 6 | Bass | Bass | 5-str drop A | 34 Pick | 5 | 104 | 64 | 8 / 0 | Rhythm section | `#3C78DC` | 24 |
| 7 | Sub Drop | Bass | 4-str custom, Transpose −12 | 38 Synth Bass 1 | 6 | 92 | 64 | 0 / 0 | Rhythm section | `#6A4CC8` | 24 |
| 8 | Drums | Drums | 6 lines (T07) | kit 0 Standard (25 TR-808 via the mix table) | 9 | 108 | 64 | 28 / 0 | Rhythm section | `#8A8A8A` | – |
| 9 | Pad / Strings | Keys (T08) | octave strings `{96,84,72,60,48,36,24}` | 89 Warm Pad → 48 Strings (B30) | 7 | 70 | 64 | 80 / 40 | Keys | `#7FC8A0` | – |
| 10 | Piano | Keys | octave strings | 0 Grand | 8 | 80 | 44 | 64 / 0 | Keys | `#E8E0D0` | – |

**Other track settings:**

- **Performer (T23):** "TabForge Demo" on every track.
- **Track notes (T23):**
  - RhyR: "8-string: the low E1 is used only for the breakdown drop."
  - Sub: "Written an octave up; sounds an octave down (Transpose −12)."
  - Clean: "Capo 5. Frets are relative to the capo."
- **Sound source and rig (T27):** `SoundSource = Midi` on all tracks. `Rig.ArticulationMap` is "Generic Guitar" for tracks 1–5,
  "Generic Bass" for 6–7, "GM Drums" for 8 and "Piano" for 9–10. Rig names: "High Gain" (1–2), "Lead" (3–4), "Clean" (5),
  "Bass DI" (6), "Sub" (7), "Kit" (8), "Pad" (9), "Grand" (10).
- **Drum map (T14, T15):** `DrumMapPreset = DrumMaps.Custom` with a single entry, the ride bell drawn as a diamond:
  `new DrumMapEntry{ Midi = 53, TabLine = 0, Label = "RB", StaffStep = <.gp5 default for 53>, Head = "diamond" }`.
  Every other note falls back to .gp5. Call `DrumMaps.Apply(drums, DrumMaps.Custom)` after filling.
- **Group levels (T25):**
  - `song.Mixer.Grouping = MixerGrouping.ByInstrument`; the explicit `MixerGroup` names override it.
  - `Edit("Guitars").Volume = 100`, `Edit("Rhythm section").Volume = 100`, `Edit("Keys").Volume = 85`.
  - Group pan 0 and pitch 0.
- **Clefs (S06):**
  - `Clefs.Guitar` is set explicitly on guitars.
  - `Clefs.Bass` on Bass and Sub, on all bars.
  - Piano uses `Treble`, except bars 91 and 95, which use `Bass`.
  - Pad uses `Treble`, except bars 135–140, which use **`Alto`** (the viola-register string line).
- **Must stay at defaults:** Mute and Solo remain false (T22). Master and bus plug-in chains stay empty (T26).

---

## 2. Form (source bars, with the performed order)

### 2.1 Section map

| Bars | Section (marker title) | Sig / tempo | Performed | Structure |
|---|---|---|---|---|
| 1 | Intro: Embers (marker at bar 1) | 1/4, 150 | 1 | pickup; `Anacrusis` |
| 2–9 | (Intro: Embers) | 4/4 | 8 | clean, ambient, let ring, harmonics; 808 from bar 4 |
| 10 | Swell | **3/4** | 1 | marker `LengthBars = 1` |
| 11–18 | Intro: Meridian | 4/4 | 8 | double bar after 18 |
| 19–31 | Verse 1 | | 16 | **19 RepeatStart**; **22 = ending 1 + RepeatEnd** (count 2); **23 = ending 2**; 24–31 continue |
| 32–39 | Pre-chorus 1 | | 8 | **Segno on 36**; **To Coda on 39** |
| 40–47 | Chorus 1 | | 8 | |
| 48–50 | Post-chorus | | 6 | **48 RepeatStart**; **49 = ending "1.2." (mask 0b011) + RepeatEnd, count 3** ("x3"); **50 = ending 3** |
| 51–62 | Verse 2 | | 12 | simile bars 57–58 |
| 63–70 | Pre-chorus 2 | | 8 | |
| 71–78 | Chorus 2 | | 8 | double bar after 78 |
| 79–82 | Interlude: Glass | 4/4, **triplet feel 8th** | 4 | the swung breath |
| 83–90 | Bridge | 83–86 **7/8**, 87–89 4/4 polymeter, 90 4/4 | 8 | bar 90 has the mid-bar tempo drop |
| 91–98 | Breakdown: Meridian Falls | 112, half-time | 8 | |
| 99–100 | Bounce | **triplet feel 16th** | 2 | |
| 101 | Wind-up | ramp 112 → 150 | 1 | |
| 102–117 | Solo | 150 | 16 | **117: D.S. al Coda** (`"DalSegnoAlCoda"`), double bar |
| 118 | Coda: Lift | **2/4**, key change to B minor | 1 | **Coda** sign |
| 119–134 | Final Chorus | B minor | 16 | **134: To Double Coda** |
| 135–140 | Outro | ramps 139–140 | 6 | **135: Double Coda** |
| 141–144 | Fake-out | 132 | 4 | 141 unison hit with fermata; 142 **free time**, silence with fermata; 143 final chug; 144 ring out, **Fine** |

### 2.2 Performed order and length

```
1-18 | 19 20 21 22 19 20 21 23 24-31 | 32-47 | 48 49 48 49 48 50 | 51-117 (D.S.) | 36 37 38 39 (To Coda) | 118-144 (Fine)
```

- **Length:** 144 source bars and **154 performed bars**.
- **Duration** (1.6 s per 4/4 bar at 150): about 27 s intro, 26 s verse 1, 26 s pre-chorus and chorus 1, 10 s post-chorus, 45 s verse 2
  through chorus 2, 6 s interlude, 12 s bridge, 23 s breakdown and wind-up, 26 s solo, 6 s D.S. reprise, 26 s coda and final chorus,
  10 s outro and 7 s fake-out. The total is **about 4:10 (about 250 s)**.
- **Checked against the playback rules** (`PlaybackOrder` stage 2):
  - Only the one D.S. is taken.
  - To Coda on 39 is ignored on the first pass. After the jump it leads to the nearest coda after it, which is 118.
  - After the jump, To Double Coda (134) is a To Coda that has already been used, so it is ignored. Bar 135 follows directly, so the
    printed route and the played route agree.
  - Fine on 144 ends playback at the song's end.
  - The reprise 36–39 contains no repeats or endings, so nothing is dropped.

### 2.3 Section dynamics (the base velocity for pitched tracks)

| Bars | Dynamic | Bars | Dynamic |
|---|---|---|---|
| 1–3 | p (clean); pad ppp → fade | 71–78 | ff |
| 4–5 | mp | 79–82 | mp (clean and piano), pp (pad) |
| 6–10 | mf | 83–90 | f |
| 11–18 | ff | 91 beat 1 | **fff**, then ff |
| 19–31 | f | 99–101 | ff, wind-up ramps to fff |
| 32–39 | mf → ff (+1 step every 2 bars) | 102–109 | f (lead), rhythm f |
| 40–47 | ff (lead f) | 110–117 | ff |
| 48–50 | fff stabs | 118–134 | ff (lead ff, 127–134 fff on chord hits) |
| 51–62 | f | 135–140 | ff → f |
| 63–70 | mf → ff | 141 | fff; 142 feedback pp → f (fade in); 143 fff; 144 ff, fading out |

### 2.4 Bar-level flags on every track (use `ForAllTracks`)

- **Double bar (S11):** 9, 10, 18, 31, 47, 50, 62, 78, 82, 90, 101, 117, 134, 140, 144.
- **Force line break (S38):** 11, 19, 24, 32, 40, 48, 51, 63, 71, 79, 83, 91, 102, 110, 118, 135, 141.
- **Prevent line break (S39):**
  - Bar 10 keeps the swell on the Embers system.
  - Bar 119 keeps the hiccup with the chorus downbeat.
  - Check both with `--layout-audit`.
- **Markers (S37)** at 0-based index, with `bar.SectionName` set to the same title:

  | Index | Title | Colour |
  |---|---|---|
  | 0 | Intro: Embers | `#5A8CDC` |
  | 9 | Swell | `#5A8CDC`, `LengthBars = 1` |
  | 10 | Intro: Meridian | `#D9423A` |
  | 18 | Verse 1 | `#5AB46E` |
  | 31 | Pre-chorus 1 | `#E6B43C` |
  | 39 | Chorus 1 | `#E6962A` |
  | 47 | Post-chorus | `#C8643C` |
  | 50 | Verse 2 | `#5AB46E` |
  | 62 | Pre-chorus 2 | `#E6B43C` |
  | 70 | Chorus 2 | `#E6962A` |
  | 78 | Interlude: Glass | `#58B8D8` |
  | 82 | Bridge | `#8B5CF6` |
  | 90 | Breakdown: Meridian Falls | `#D23C3C` |
  | 98 | Bounce | `#D23C3C` |
  | 100 | Wind-up | `#D23C3C` |
  | 101 | Solo | `#F0A830`, `LockPosition = true` |
  | 117 | Coda: Lift | `#E6962A` |
  | 118 | Final Chorus | `#E6962A` |
  | 134 | Outro | `#966EC8` |
  | 140 | Fake-out | `#8A8A8A` |

- **Directions (S22–S33):** `Directions` = "Segno" on 36, "ToCoda" on 39, "DalSegnoAlCoda" on 117, "Coda" on 118,
  "ToDoubleCoda" on 134, "DoubleCoda" on 135 and "Fine" on 144.
- **Swing:**
  - `TripletFeelKind = TripletFeels.Eighth` on 79–82.
  - `TripletFeels.Sixteenth` on 99–100.
- **Free time:** `FreeTime = true` on 142.
- **Simile:**
  - `SimileOneBar` on 25 and 29 (RhyL, RhyR, Bass, Drums).
  - `SimileTwoBar` on 57 and 58 (RhyL, RhyR, Bass).
  - This is per track, not structure.
  - **Fill simile bars with an exact copy** of the bar being repeated, so a clean .gp still sounds right. If the renderer draws those
    notes under the simile glyph, leave the cells empty, as `DemoSongFactory` does, and add the clean-.gp gap to the allow-list.

---

## 3. Bar-by-bar content

### 3.1 Named guitar patterns (Rhythm L; R follows the rules in 3.2)

| Name | Tokens (all 16 slots unless noted) |
|---|---|
| **RiffM1** | `16:O> 16:O 8:s4:2> 16:O 16:O 8:s4:0 16:O 16:O 8:s5:3 8:O 8:s4:2` (motif E3–D3–C3–E3 over the pedal) |
| **RiffM2** | `16:O> 16:O 8:s4:3> 16:O 16:O 8:s4:2 16:O 16:O 8:s5:3 8:P1> 8:X` (F3–E3–C3, then the Bb Phrygian stab) |
| **RiffM2ph** | RiffM2 with the last two tokens replaced by `4:s5:1+ph+vib` (Bb2 pinch, `HarmonicFret = 13`) |
| **ChordM(n)** | `4.:Pn> 16:On 16:On 8:Pn 8:On 8:On 8:Pn` |
| **RiffV** | `16:P0> 16:O 16:O 16:O 8:P1> 16:O 16:O 16:O 16:O 8:P3> 8t:O 8t:O 8t:s7:1>`; triplet turn on beat 4 |
| **RiffVb** | RiffV with the triplet `8t:s7:3> 8t:s7:1 8t:O` |
| **RiffVe1** | RiffV slots 0–11, then `4:P6>>` (Eb5 tritone) |
| **RiffD** (djent 3+3+3+3+4) | `16:P0> 16:O 16:O 16:P1> 16:O 16:O 16:O> 16:O 16:O 16:P3> 16:O 16:O 8:P1> 16:O 16:X` |
| **RiffDph** | RiffD slots 0–11, then `4:s5:3+ph+vib` (`HarmonicFret = 15`) |
| **RiffDend** | `8:P0> 8:X> 8:P1> 8:X> 4:P3> 16:O 16:O 16:O 16:O` (dead + accent, B34) |
| **RiffP(shape)** | `4.:shape>+lr 4.:shape+lr 4:shape+lr` (3-3-2 ringing octaves) |
| **Build** | 12 × `16:O` (velocity ramps linearly 80 → 112), then `8:P7> 8:P8>` |
| **RiffC(n)** | `2.:Pn> 16:On 16:On 8:On` (sustained chorus chord) |
| **RiffC2(n)** | `8:Pn> 8:On 8:On 8:On 8:Pn 8:On 8:On 8:On` (8th drive) |
| **ChEnd(n)** | `2:Pn> 4:Pn 4:Pn+sod` (last chorus bar) |
| **Stab48** | `8:P0>+st 8:r 16:O 16:O 8:P1>+st 8:r 8:P3>+st 8:P1>+st 8:r` |
| **Stab49** | `8:P0>+st 8:r 16:O 16:O 8:P6>>+st 4:r 16:O 16:O 8:r` |
| **Stab50** | `8:P0>+st 8:X> 4:r 4:r 16:O 16:O 16:O 16:O` (beat text "gang: HEY!" on slot 2) |
| **B7** (7/8, 14 slots) | `16:O> 16:O 8:P1 16:O> 16:O 8:P3 16:O> 16:O 16:O 16:O 8:P6>` |
| **B7d** (7/8) | `16:O> 16:O 8:P1 16:O> 16:O 8:P3 8(2:3):P6> 8(2:3):P5>` (2:3 duplet over the 3-group) |
| **C7** (7-sixteenth cell) | `O> O P1 O O P3 X`, all 16ths |
| **BD1** | `8:P0>> 16:O 16:r 16:r 16:O 8:O 4..:P1> 16:O` (double-dotted quarter, B03) |
| **BD2** | `8:O> 16:O 16:O 8:r 8:s5:3+ph+vib 4:P6> 8:O 8:X` (`HarmonicFret = 15`) |
| **Bounce** | 16 × 16th: `O> O r O O> O r O P1> O r O P3> O X O` |

**Stem and beam rules (B26, B27):**

- In RiffD bars, set `BreakSecondaryBeamBefore = true` on slots 3, 6 and 9, so the beams show the 3+3+3+3+4 grouping.
- In B7 and B7d bars, set `BeamMode.Force` over the 3-group (slots 8–13).
- In the C7 bars (87–89), set `BeamMode.Break` at every cell start: 87 at slots 7 and 14, 88 at 5 and 12, 89 at 3 and 10.
  Apply this on RhyL, RhyR, Bass and Drums.

### 3.2 Rhythm Guitar L (track 1), bar by bar

| Bars | Content |
|---|---|
| 1–9 | empty (S47) |
| 10 | `2:r 8:r 16:O 16:O` (12 slots) |
| 11 | RiffM1 |
| 12 | RiffM2 |
| 13 | RiffM1 |
| 14 | RiffM2ph |
| 15–17 | ChordM(8) F, ChordM(3) C, ChordM(10) G |
| 18 | `2:P7> 4:P7 8:O 8:X` |
| 19 | RiffV, every token `+pd` (pick strokes, N30); beat text "Riff V" |
| 20 | RiffVb |
| 21 | RiffV |
| 22 (ending 1) | RiffVe1 |
| 23 (ending 2) | `4.:P8> 8:O 4:P6> 16:O 16:O 16:O 16:O` |
| 24 | RiffD, 16ths alternating `+pd` / `+pu`; beat text "Riff D (3+3+3+3+4)" |
| 25 | simile |
| 26 | RiffD |
| 27 | RiffDph |
| 28 | RiffD |
| 29 | simile |
| 30 | RiffD |
| 31 | RiffDend |
| 32–35 | RiffP with shapes Am `[s7:0 s5:0 s3:2]`, F `Q1`, G `Q3`, E `Q0` |
| 36–38 | RiffP with Am, F, G |
| 39 | Build |
| 40–46 | RiffC with 8 F (first chord `+sib`), 3 C, 10 G, 0 Am, 8 F, 3 C, 10 G |
| 47 | ChEnd(7) |
| 48 | Stab48 (`SoundDurationPercent = 60` on stab cells, B29) |
| 49 | Stab49 |
| 50 | Stab50 |
| 51 | RiffV |
| 52 | RiffVb |
| 53 | RiffV |
| 54 | `16:P0> 16:O 16:O 16:O 8:P1> 16:O 16:O 4:P5> 4:P6>+ss` (new ending; shift slide down into bar 55) |
| 55 | RiffV |
| 56 | RiffVe1 |
| 57–58 | `SimileTwoBar` |
| 59 | `1:P0+trem16` (blast) |
| 60 | `1:P1+trem16` |
| 61 | `1:P3+trem16` |
| 62 | `2:P6>+trem16 4:P5+trem16 4:X` |
| 63–70 | as 32–39 |
| 71–77 | as 40–46 (bar 71 without `+sib`) |
| 78 | ChEnd(7) |
| 79–82 | whole rests |
| 83–85 | B7 (bar 83 beat text "7/8: 2+2+3") |
| 86 | B7d |
| 87–89 | the C7 stream (48 slots: six whole cells, then the first six of a seventh; see below); beat text on 87 "7 over 4" |
| 90 | `16:P0> 16:O 16:O 16:O 8:P1> 8:P3> 4:[s7:0 s6:0]+psd 4:r`; beat text "pick scrape" |
| 91 | BD1, first chord fff; beat text "gang: RISE!" |
| 92 | BD2; the pinch harmonic also gets `TremBar` with `WhammyPoints (0,0)(30,-2)(60,0)` |
| 93 | BD1 |
| 94 | `8:O> 8:O 2:s7:0 4:r`; the half note gets `TremBarDive` with `(0,0)(20,-8)(45,-24)(60,-24)`; beat text "dive bomb" |
| 95 | BD1 |
| 96 | BD2 |
| 97 | BD1 |
| 98 | `4:P0>> 4:P1>> 4:P3>> 4:P6>>` |
| 99–100 | Bounce (bar 100: the last 4 tokens become `4:P0>>`) |
| 101 | 16 × `16:O`, with `P0>` on slots 0, 4, 8 and 12; velocity ramps 80 → 127 |
| 102–108 | RiffC with F C G Am F C G (8, 3, 10, 0, 8, 3, 10) |
| 109 | ChEnd(7) |
| 110–116 | RiffC2 with F C G Am F C G |
| 117 | ChEnd(7) |
| 118 (2/4) | `8:P10> 8:P12> 4:r` |
| 119–125 | RiffC with G D A Bm G D A (10, 5, 12, 2, 10, 5, 12) |
| 126 | ChEnd(9) (F#) |
| 127–133 | RiffC2 with the same chords |
| 134 | ChEnd(9) |
| 135–138 | RiffM1 / RiffM2 / RiffM1 / RiffM2, **transposed +2**: `O` → `O2`, frets +2, P1 → P3 (C5 is the Phrygian b2 of B) |
| 139 | `2:P2> 2:P10>` |
| 140 | `2:P12> 2:P9>` |
| 141 | `4:P2>>+ferm 4:r 2:r` |
| 142 | `1:r+ferm` |
| 143 | `16:O2> 16:O2 16:O2 16:P3> 16:O2 16:O2 16:P3> 16:O2 8:P5> 8:P3> 4:r` |
| 144 | `1:[s7:2 s6:2 s5:2 s4:4]>>+lr+fout` |

**Bars 87–89 in full** (the C7 cell repeated, then truncated so the pattern realigns on bar 90):

| Bar | Tokens |
|---|---|
| 87 | `O> O P1 O O P3 X O> O P1 O O P3 X O> O` |
| 88 | `P1 O O P3 X O> O P1 O O P3 X O> O P1 O` |
| 89 | `O P3 X O> O P1 O O P3 X O> O P1 O O P3` |

**Rhythm Guitar R (track 2)** is L with these deliberate differences. Everything else is identical, with the velocity offset from 0.2.

1. **Choruses, solo and final chorus** (40–47, 71–78, 102–117, 119–134): every `Pn` also gets the octave `s4:n+2`.
2. **Pre-chorus 2** (63–69): the octave shapes move up: Am `[s5:12 s3:14]`, F `[s5:8 s3:10]`, G `[s5:10 s3:12]`, E `[s5:7 s3:9]`.
3. **Ghosted chugs:** bar 21 slot 1 is `O+g`; bar 49, the two 16ths at slots 12–13 are `+g`.
4. **Bar 53:** the first triplet note is `8t:s5:3+sh` (semi-harmonic squeal, `HarmonicFret = 15`).
5. **Bar 70:** the last token is `8:[s6:0 s5:0]+psu` (pick slide up), instead of `P8>`.
6. **Bar 92:** the pinch harmonic uses `TremBarPrediveDive` with `(0,-4)(20,-4)(60,-16)`.
7. **Bar 94:** the half note is `2:s8:0` (**E1**), with `TremBarDive` `(0,0)(40,-8)(60,-8)`.
8. **Bar 96:** the pinch harmonic uses `TremBarPredive` with `(0,-6)(15,0)(60,0)`.
9. **Bar 98:** beat 4 is `[s8:0 s7:6 s6:6]>>`.
10. **No pick-stroke marks.**

### 3.3 Bass (track 6)

**Default rule, BassFollow.** Take the lowest note of each Rhythm L attack and play it an octave below: guitar `s7:n` → bass `s5:n`,
`s6:n` → `s4:n`, `s5:n` → `s3:n`, `s4:n` → `s2:n`, `s3:n` → `s1:n`, and 8-string `s8:0` → `s4:0`.

- Keep the rhythm, `pm`, dead and accent flags.
- Harmonics and whammy become a plain root.
- Simile bars follow the guitar.

Bars that override the rule:

| Bars | Bass |
|---|---|
| 1–5 | empty |
| 6 | `1:s3:0+fin` (A1, fade in) |
| 7 | `1:s4:1` |
| 8 | `1:s3:3` |
| 9 | `1:s4:0` |
| 10 | `2.:s4:0+tie` (a cross-bar tie into 3/4) |
| 32–38 | whole notes with `+lr`: Am `s5:0`, F `s4:1`, G `s4:3`, E `s4:0`, then Am, F, G (39 follows the rule) |
| Choruses and solo (40–46, 71–77, 102–108) | root 8ths (`8:root` × 8, first `>`). Roots: F `s4:1`, C `s3:3`, G `s4:3`, A `s3:0`, E `s4:0` |
| 47, 78, 109, 117 | `2:s4:0> 4:s4:0 4:s4:0+sod` |
| 110–116 | the same root 8ths |
| 119–133 | root 8ths in B minor: G `s4:3`, D `s3:5`, A `s3:0`, Bm `s3:2`, F# `s4:2` |
| 126, 134 | `2:s4:2> 4:s4:2 4:s4:2+sod` |
| 79 (Dm7, slap) | `8:s2:0+slap 8:s1:7+pop 8:s2:0+slap 8:s2:0+ds 8:s2:3+slap 8:s1:7+pop 8:s1:5+slap 8:s1:2+slap` |
| 80 (Em7) | `8:s2:2+slap 8:s1:9+pop 8:s2:2+slap 8:s2:2+ds 8:s2:5+slap 8:s1:9+pop 8:s1:7+slap 8:s1:4+slap` |
| 81 (Fmaj7) | `8:s2:3+slap 8:s1:10+pop 8:s2:3+slap 8:s2:3+ds 8:s2:7+slap 8:s1:10+pop 8:s1:9+slap 8:s1:5+slap` |
| 82 (E7) | `4:s4:0+slap 4:s1:12+nh 4:s2:2+slap 8:s2:2+ds 8:s1:1` (G3 harmonic at node 12; G#2 leads to A) |
| 141 | `4:s5:2>>+ferm 4:r 2:r` |
| 142 | `1:r+ferm` |
| 144 | `1:s5:2+lr+fout` |

- **Sound duration (B29):** `SoundDurationPercent = 80` on bars 79–82.
- **Dead slap:** a `ds` note is `DeadSlapped` + `Slap` + `Dead = true` (N21).

### 3.4 Sub Drop (track 7)

The Sub is silent except for these bars. It is written an octave up, so `s4:12` is written A2 and sounds A1.

| Bar | Content |
|---|---|
| 11 | `1:s4:12>` with `TremBarDive` `(0,0)(40,-24)(60,-24)` (the 808 drop on the first band hit) |
| 91 | `2:s4:12>>` with `TremBarDive` `(0,0)(30,-24)(60,-24)`, then `2:s4:12+tie` with `TremBarHold` `(0,-24)(60,-24)` |
| 119 | `1:s4:14>` with `TremBarDive` `(0,0)(40,-24)(60,-24)` (key-change impact on B) |
| 141 | `4:s4:14>>+ferm 4:r 2:r` |
| 142 | `1:r+ferm` |
| 143 | `2:s4:14>>` with `TremBarDive` `(0,0)(50,-24)(60,-24)`, then `2:r` |

### 3.5 Lead Guitar (track 3) and Lead Harmony (track 4)

The chorus hook is written in A. Each bar gives the Lead tokens, the lyric per note, the harmony in 3rds (chorus 1) and the harmony in
6ths (chorus 2 and final chorus).

| Chorus bar | Chord | Lead | Lyric (ch. 1 / ch. 2) | Harmony, 3rds | Harmony, 6ths |
|---|---|---|---|---|---|
| 1 | F | `4.:s1:12 8:s1:10 4:s1:8 4:s2:10` (E5 D5 C5 A4) | Hold / the / last / light · Sing / through / the / smoke | `4.:s1:8 8:s2:12 4:s2:10 4:s2:6` | `4.:s2:8 8:s2:6 4:s2:5 4:s3:5` |
| 2 | C | `2:s2:8+tn 4:s1:12 4:s1:10` (G4 E5 D5) | as / it / fades · till / it / clears | `2:s2:5+tn 4:s1:8 4:s2:12` | `2:s3:4+tn 4:s2:8 4:s2:6` |
| 3 | G | `4.:s1:10 8:s1:8 4:s2:12 4:s1:10` (D5 C5 B4 D5) | we / are / the / ash · we / are / the / spark | `4.:s2:12 8:s2:10 4:s2:8 4:s2:12` | `4.:s2:6 8:s2:5 4:s2:3 4:s2:6` |
| 4 | Am | `2..:s1:8+vib 8:s2:12` (C5 B4) | of / the · in / the | `2..:s2:10+vib 8:s2:8` | `2..:s2:5+vib 8:s2:3` |
| 5 | F | `4.:s1:12 8:s1:10 4:s1:8 4:s1:17` (E5 D5 C5 A5) | sun / that / burned / us · dark / that / re- / mains | `4.:s1:8 8:s2:12 4:s2:10 4:s1:13` | `4.:s2:8 8:s2:6 4:s2:5 4:s1:8` |
| 6 | C | `2:s1:15+vib 4:s1:12 4:s1:8` (G5 E5 C5) | down / to / the · when / the / world | `2:s1:12+vib 4:s1:8 4:s2:10` | `2:s2:12+vib 4:s2:8 4:s2:5` |
| 7 | G | `4.:s1:10 8:s1:12 4:s1:10 4:s2:12` (D5 E5 D5 B4) | end / of / the / long · falls / to / the / cold | `4.:s2:12 8:s1:8 4:s2:12 4:s2:8` | `4.:s2:6 8:s2:8 4:s2:6 4:s2:3` |
| 8 | E | `2:s2:12+vib 4:s2:9 4:s2:12+vib` (B4 G#4 B4) | mer- / i- / dian (both) | `2:s2:9 4:s2:5 4:s2:9+vib` | `2:s2:3 4:s3:4 4:s2:3+vib` |

**Chorus 1 (40–47)** and **chorus 2 (71–78)** use the hook above with these additions.

| Bar | Addition |
|---|---|
| 40 | Lead and Harmony enter. Chorus 1 uses the 3rds column; chorus 2 uses the 6ths column. |
| 44, beat 4 (A5) | Before the note, a grace note in the same cell: `s1:15`, `IsGraceNote`, `GraceBeforeBeat = true`, `GraceDurationSlots = 0.5`, plus `LegatoSlide` (N33 GraceBefore). |
| 71, beat 1 | `+sia` (slide in from above). |
| 75, beat 4 | The grace note is `s1:15` with `"GraceBend"`, bent +4 to A5. |
| 78, last note | `TremBarDip` `(0,0)(20,-4)(40,0)(60,0)`. |
| 41, beat 1 | `+tn` (tenuto). |
| Lyrics | One `cell.Lyrics` per note (B20); the final syllable carries the hyphen convention ("mer-"). |

**Final chorus (119–134):** the Lead and Harmony tokens are the same with **every fret +2** (B minor).

- **119–126:** the Lead uses the lyrics of chorus 1; the Harmony uses the 6ths column.
- **123, beat 4:** the grace note is on the beat (`GraceBeforeBeat = false`, `"GraceOnBeat"`): `s1:17` → principal `s1:19`.
- **127–134:** the Lead repeats with `OctaveShiftSemitones = +12` on every cell (8va, B25), and beat text on 127:
  "octave pedal +1 oct". The Harmony keeps the 6ths with no 8va, so the spread becomes a 13th.
- **127–134 lyrics:** two lines, `"Hold\nSing"` and so on (line 1 = chorus 1, line 2 = chorus 2).
- **134:** the last note ends with `+vib` (the wide whammy is on bar 140).

**Other Lead bars.** The Lead is silent elsewhere, except for the bars below.

| Bars | Lead |
|---|---|
| 15 | `4.:s1:12 8:s1:10 4:s1:8 4:s1:12` |
| 16 | `2:s1:10+vib 4:s1:8 4:s2:8` |
| 17 | `4.:s1:10 8:s1:8 4:s2:12 4:s1:10` |
| 18 | `2:s2:12+vib 2:s2:9+wv` |
| 51 | `1:s2:13+fin+tn` (C5) |
| 52 | `2:s2:11+vib 2:s2:10+ls` (Bb4; A4 slides legato into bar 53's `s2:13`) |
| 53 | `2:s2:13 4:s2:11 4:s2:10` |
| 54 | `2.:s3:14+wv 4:r` |
| 63 | `1:s1:12+wc` |
| 64 | `1:s1:13+wo` |
| 65 | `1:s1:15+wc` |
| 66 | `2:s1:12+wo 2:s1:16+wc` |
| 67 | `2:s1:12+trem8 2:s1:15+trem8` |
| 68 | `2:s1:13+trem16 2:s1:17+trem16` |
| 69 | `2:s1:15+trem32 4:s1:13+trem32 4:s1:12+trem32` |
| 70 | `2:s1:12+trem64 2:s1:16+trem64+vib` (the tremolo speeds up 8 → 16 → 32 → 64) |
| 91 | an **annotation-only** cell (B32) at slot 0 with `Lyrics = "RISE!"`, beat text "gang vocal", no notes |
| 135 | `4.:s1:14 8:s1:12 4:s1:10 4:s1:14` |
| 136 | `2:s1:12+vib 2:r` |
| 137 | as 135 |
| 138 | `2:s1:12 2:s1:10+wv` |
| 139 | `1:s1:14+tn` |
| 140 | `1:s1:19` with `TremBarWide` `(0,0)(10,-2)(20,0)(30,-2)(40,0)(50,-2)(60,0)` |
| 141 | `4:s1:19>>+ferm 4:r 2:r` |
| 142 | `1:s2:12+fb+fin` (feedback harmonic at node 12, sounding B4); beat text "(feedback)" |

**Lead Harmony, other bars:**

| Bars | Lead Harmony |
|---|---|
| 135–138 | 6ths below the Lead: `4.:s1:5 8:s2:8 4:s2:7 4:s1:5`, `2:s2:8 2:r`, repeat, `2:s2:8 2:s2:7` |
| 139 | `1:s1:5` |
| 140 | `1:s1:10+vib` |
| 141 | `4:s1:10>>+ferm 4:r 2:r` |

**Solo (102–117)** follows the planned arc. The drums are backbeat for 102–109, then double-time.

| Bar | Chord | Lead | Role / notes |
|---|---|---|---|
| 102 | F | `4.:s1:12 8:s1:10 2:s1:8+vib` | Call (motif M) |
| 103 | C | `2:s2:15` Bend, type **BendRelease** `(0,0)(12,4)(36,4)(48,0)(60,0)`; then `4:s2:13+wv 4:r` | D5 → E5 → D5, then air |
| 104 | G | `4.:s1:15 8:s1:13 2:s1:10` **PrebendRelease** `(0,4)(20,4)(40,0)(60,0)` | Response, a 3rd higher: E5 cries down to D5 |
| 105 | Am | `4:s1:10` **Prebend** `(0,4)(60,4)`; `4:s1:8 2:s2:10+wv` | Ends on the root |
| 106 | F | 9 × `8t` (3 beats): `s1:12 s1:10 s1:8 s1:10 s1:8 s2:10 s1:8 s2:10 s2:8`; then `8:s3:9+ss 8:s3:12` | Sequence down in triplets; shift slide |
| 107 | C | `8:s3:10+ls 4.:s3:12+ph+wv 4:r 16:s2:8 16:s2:10 16:s2:12 16:s1:8` | Legato slide into a pinch (`HarmonicFret = 24`) |
| 108 | G | Beat 1: `16(5:4)` × 5 `s3:7+h s3:9+h s3:10 s2:8+h s2:10`. Beat 2: `16(5:4)` × 5 `s2:12 s1:8+h s1:10+h s1:12+h s1:13`. Beat 3: `4:s1:15` **Bend** `(0,0)(15,4)(60,4)`. Beat 4: `4:s1:15+tie` **Release** `(0,4)(30,0)(60,0)` | Legato lift; a middle note carries both origin and destination |
| 109 | E | `4:s1:16` **PrebendRelease** `(0,2)(24,2)(48,0)(60,0)`, `4:s1:12+vib`; beat 3: `16(7:4)` × 7 `s1:10+h s1:8+h s1:7 s2:10+h s2:9+h s2:6+h s2:5`; beat 4: `4:s2:9+vib` | Harmonic-minor pull-offs |
| 110 | F | 24 × `16t` (16th triplets). Beats 1–3: `s1:13` (**Tapping**, h), `s1:5` (h), `s1:8`, repeated. Beat 4: `s1:13` tap, `s1:5`, `s1:8`, `s1:12` (**LeftTap**), `s1:8`, `s1:5` | Tapped arpeggio; beat text "tap"; mix pan sweep |
| 111 | C | Beats 1–2: 12 × `16t` `s1:15` tap, `s1:8`, `s1:12`. Beats 3–4: `2:s1:3+th` (`HarmonicFret = 15`, sounds G5) | Tap harmonic |
| 112 | G | `2:s1:10+trem32 4:s1:12+trem32 4:s1:10+trem32` | **Harmony joins:** `2:s2:12+trem32 4:s1:8+trem32 4:s2:12+trem32`; beat text "harmony" |
| 113 | Am | `2:s1:8+trem32 2:s1:12` **Trill** (`TrillTargetMidi = PitchOf(s1,13)`, `TrillDurationDenominator = 16`) | Harmony: `2:s2:10+trem32 2:s1:8` Trill → `s1:10` |
| 114 | F | Beat 1: `16(6:4)` × 6 up `s5:8 s4:7 s3:5 s2:6 s1:5+h s1:8`. Beat 2: × 6 down `s1:8+h s1:5 s2:6 s3:5 s4:7 s5:8`. Beat 3: `32(9:8)` × 9 `s2:5 s2:6 s2:9 s2:10 s1:7 s1:8 s1:10 s1:12 s1:13`. Beat 4: `4:s1:17+wv` | F sweep (sextuplets); harmonic-minor 9-tuplet |
| 115 | C | Beat 1: `16(6:4)` up `s5:15 s4:14 s3:12 s2:13 s1:12 s1:15`. Beat 2: down (reverse). Beat 3: `32(13:8)` × 13 `s1:15 s1:13 s1:12 s1:10 s1:8 s2:12 s2:10 s2:8 s2:6 s2:5 s3:7 s3:5 s3:4`. Beat 4: `8:s2:8+sou 8:r` | C sweep; 13-tuplet run down |
| 116 | G | `2:[s1:19+wv s2:22]`; the s2 note gets **Bend** type **Custom** `(0,0)(10,4)(25,4)(35,3)(45,4)(60,4)`. Then `2:[s1:19+tie+wv s2:22+tie]`; the s2 note gets type **Hold** `(0,4)(60,4)` | Unison bend at the peak. Harmony `1:s1:15+wv` |
| 117 | E | `4:s2:9` **PrebendBend** `(0,2)(15,2)(30,6)(60,6)`, then `2.:s4:2+ph` (`HarmonicFret = 14`) with **`TremBarCustom`** `(0,0)(8,4)(16,-4)(24,4)(36,-12)(60,-24)`; beat text "dive!" | A gargle, then a dive into the D.S. |

**Bend notes:** set `BendTypeName` exactly as named in the table. `BendStyleName` is left at its default.

**Tuplets:** set `TupletNumerator` / `TupletDenominator` and `RhythmicPosition` on every tuplet cell (section 6.2).

### 3.6 Clean Guitar (track 5, capo 5; frets are relative to the capo)

**Voice layout, intro bars 2–9 (B23):**

- **Voice 1 (arpeggio):** `StemDirection.Up`.
- **Voice 2 (bass notes):** `CellsForVoice(1, create: true)`, `StemDirection.Down`.
- **Song setting:** `song.GrayInactiveVoice = true` (S43).

**Chord shapes (sounding chord → shape):**

| Sounding | Shape | Frets |
|---|---|---|
| Am(add9) | Em | s6:0 s5:2 s4:2 s3:0 s2:0 s1:2 |
| Fmaj7 | Cmaj7 | s5:3 s4:2 s3:0 s2:0 s1:0 |
| C(add9) | G | s6:3 s5:2 s4:0 s3:2 s2:3 s1:3 |
| E7 | B7 | s5:2 s4:1 s3:2 s2:0 s1:2 |
| G | D | s4:0 s3:2 s2:3 s1:2 |
| F | C | s5:3 s4:2 s3:0 s2:1 s1:0 |
| C | G | s6:3 s5:2 s4:0 s3:0 s2:3 s1:3 |

**Intro (1–10):**

| Bar | Voice 1 | Voice 2 | Chord name (B18) |
|---|---|---|---|
| 1 (1/4) | `8:s2:0 8:s1:0` (p) | – | |
| 2 | 8ths `s4:2 s3:0 s2:0 s1:2 s2:0 s3:0 s1:2 s2:0`, all `+lr` | `2.:s6:0 4:s5:2` | Am(add9) |
| 3 | `s3:0 s2:0 s1:0 s2:0 s3:0 s2:0 s1:0 s2:0` `+lr` | `2.:s5:3 4:s4:2` | Fmaj7 |
| 4 (mp) | **Echo grid:** 16ths; even slots play `s4:0 s3:2 s2:3 s1:3 s2:3 s3:2 s1:3 s2:3` `+lr` (mp). Odd slot k = the note of slot k−3, velocity 49 + `Ghost` (slot 1 = the last note of bar 3) | `2.:s6:3 4:s5:2` | C(add9) |
| 5 | `4:s3:12+nh 4:s2:12+nh 4:s1:12+nh 8:s4:7+nh 8:s3:7+nh` (nodes 12 and 7) | `1:s6:0` | Am11 |
| 6 (mf) | as bar 2 | as bar 2 | Am(add9) |
| 7 | as 3 | as 3 | Fmaj7 |
| 8 | as 4 (echo grid) | as 4 | C(add9) |
| 9 | `2:[s5:2 s4:1 s3:2 s2:0 s1:2]+au+lr 4:s2:12+nh 4:s2:5+nh` (E5 and E6; node 5) | `1:s5:2` | E7 |
| 10 (3/4) | `2.:r` | `2.:s5:2+tie` (a tie across the barline) | E7 |

- **Fingering (N35), bars 2–3:** `RightHandFinger` p on voice 2, i on s4/s3, m on s2 and a on s1.
  `LeftHandFinger`: Em shape s5:2 = 1, s4:2 = 2, s1:2 = 4; Cmaj7 shape s5:3 = 3, s4:2 = 2.
- **Beat text** on bar 2: "let ring, fingerstyle".

**Later clean bars:**

- **Choruses (40–47, 71–78):** strum pattern `4:chord+bd 8:chord+bd 8:chord+bu 4:r 8:chord+bu 8:chord+bd`, all `+lr`,
  `BrushStepSlots = 0.33` (B13). Shapes per chord use the F/C/G/Am/E rows above. Chord names F C G Am F C G E.
- **Final chorus (119–134):** the same strum in B minor with `BrushStepSlots = 0.5`.

  | Sounding | Shape | Frets |
  |---|---|---|
  | G | D | xx0232 |
  | D | A | s5:0 s4:2 s3:2 s2:2 s1:0 |
  | A | E | 022100 |
  | Bm | F#m | 244222 |
  | F# | C# | s5:4 s4:6 s3:6 s2:6 s1:4 |

  Chord names G D A Bm G D A F#.
- **Interlude (79–82, swing):** an artificial-harmonic melody. `+ah` = `ArtificialHarmonic` with `HarmonicFret = fret + 12`,
  so each note sounds an octave up.

  | Bar | Clean | Sounding / chord name |
  |---|---|---|
  | 79 | `4.:s2:5+ah 8:s2:3+ah 4:s2:1+ah 4:s2:5+ah` | A5 G5 F5 A5 (motif); Dm7 |
  | 80 | `2:s2:3+ah 4:s2:0+ah 4:s2:3+ah` | Em7 |
  | 81 | `4.:s1:3+ah 8:s1:1+ah 2:s1:0+ah` | Fmaj7 |
  | 82 | `4:[B7 shape]+rasg+lr 4:r 2:[B7 shape]+ad+lr` | E7(b9) |

### 3.7 Pad / Strings (track 9, Keys)

- **Placement:** notes use the `Key(midi)` placement rule (section 6.2). Every chord is a whole note with `+tn`, unless stated otherwise.
- **Swells:** there is no swell technique (B22), so the pad uses `+fin` (FadeIn) on bars 2, 10 and 110.

| Bars | Content |
|---|---|
| 1 | empty; this cell carries the mix `Volume = 2` |
| 2–9 | A3 C4 E4 B4 (`+fin`, **ppp**) · F3 A3 C4 E4 · C4 E4 G4 D5 · A3 D4 G4 C5 · (repeat, pp) · E3 G#3 B3 D4 (bar 9) |
| 10 | `2.:[E3 G#3 B3 D4]+fin` |
| 15–18 | F3 A3 C4 · C4 E4 G4 · G3 B3 D4 · E3 G#3 B3 |
| 32–39 and 63–70 | A3 C4 E4 · F3 A3 C4 · G3 B3 D4 · E3 G#3 B3 (×2) |
| 40–47 and 71–78 | F4 A4 C5 · C4 E4 G4 · G4 B4 D5 · A4 C5 E5 · F4 A4 C5 · C4 E4 G4 · G4 B4 D5 · E4 G#4 B4 |
| 79–82 (pp) | D4 F4 A4 C5 · E4 G4 B4 D5 · F4 A4 C5 E5 · E4 G#4 B4 D5 |
| 110–117 | the chorus chords (40–47 voicings), mp, bar 110 `+fin` |
| 119–134 | **Strings** after the program change: G3 B3 D4 G4 · D4 F#4 A4 D5 · A3 C#4 E4 A4 · B3 D4 F#4 B4 · G · D · A · F#3 A#3 C#4 F#4 (126); 127–134 the same, ff |
| 135–140 | **Alto clef**, halves: `2:B3 2:A3` · `2:G3 2:F#3` · `2:B3 2:D4` · `2:C#4 2:A3` · `1:B3` · `1:A#3` |
| 141 | `4:[B3 D4 F#4]>>+ferm 4:r 2:r` |
| 142 | `1:r+ferm` |
| 144 | `1:[B2 F#3 B3 D4]+fout` |
| All other bars | empty |

### 3.8 Piano (track 10, Keys)

Voice 1 is the right hand and voice 2 the left hand.

| Bars | Voice 1 | Voice 2 |
|---|---|---|
| 6–9 (mp) | **8va** (`+12` on every cell): `4.:E5 8:D5 4:C5 4:E5` · `2:D5+tn 2:r` · `4.:E5 8:D5 4:C5 4:G5` · `2:B4 2:G#4` | `1:A2` · `1:F2` · `1:C3` · `1:E2` |
| 79–81 | `4:r 8:r 8:chord+st 4:r 8:r 8:chord+st`. Chords: Dm9 [F4 A4 C5 E5], Em7 [G4 B4 D5 E5], Fmaj7 [A4 C5 E5 F5]. Bar 81's second chord has `StemDirection.Invert` | `1:D3` · `1:E3` · `1:F3` |
| 82 | `1:[G#4 B4 D5 F5]+au` | `1:E2` |
| 91, 95 | `1:A2>>` with **15mb** (`-24`, sounds A0); **Bass clef** on these two bars; mix Reverb | – |
| 119–134 | whole-note triads, `+tn`, B minor voicings, mf | **8vb** (`-12`), halves: `2:G3 2:G3` and so on (the chord root) |
| 135–138 | **15ma** (`+24`), pp: `8:B4 8:D5 8:F#5 8:D5` × 2 per bar | – |
| 141 | `4:[B3 D4 F#4 B4]>>+ferm 4:r 2:r` | `4:[B1 B2]+ferm 4:r 2:r` |
| 144 | `1:[B3 D4 F#4]+fout` | `1:[B1 B2]` |

### 3.9 Drums (track 8)

**Lane key.** Each pattern is 16 characters per 4/4 bar (14 for 7/8, 12 for 3/4, 8 for 2/4). `.` means no hit. Every hit cell lasts
until the next onset in the bar; `c` means a 16th choke followed by rests.

| Lane | GM note | Characters → **centre velocity** (before humanisation, section 5.2) |
|---|---|---|
| K | 36 | `X` 120 breakdown/unison · `x` 110 groove · `d` 100 double-bass 16ths · `b` 96 blast · `p` 49 feathered |
| S | 38 | `X` 127 + `Accent = 2` · `x` 122 backbeat · `f` flam (grace note 62, `GraceBeforeBeat`, 0.5 slot, then main 122) · `g` 45 + `Ghost` · `b` 104 blast · `r` fill ramp |
| SS | 37 | `x` 80 |
| CL | 39 (808 clap) | `x` 96 |
| H | 42 / 46 / 44 | `X` 90 · `x` 76 · `y` 66 (closed 42) · `o` 102 (open 46) · `p` 70 (pedal 44) |
| R | 51 / 53 | `X` 94 · `x` 80 (bow 51) · `b` 110 (bell 53) |
| C | 49 / 57 | `X` 120 · `x` 108 (49) · `2` 120 (57) · `c` choke 120 |
| CH | 52 | `X` 118 |
| SP | 55 | `x` 104 |
| T1 / T2 / T3 / T4 | 50 / 48 / 45 / 43 | `r` ramp · `X` 122 |

- **Fill ramp `r`:** interpolate 90 → 120 across all `r` hits of the fill, in time order (all lanes together). The next downbeat's crash
  is fff.
- **Line placement:** `StringIndex = GuitarProImporter.DrumLine(midi)`, `Fret = midi`, `MidiValue = midi` (B35).

**Fill kinds** (the slots are fixed; velocities follow the ramp rule):

| Kind | Content |
|---|---|
| **P4** (1-beat pickup) | S `rrrr` on slots 12–15; K removed on 12–15 |
| **P8** (half bar) | slots 8–15: S S T1 T1 T2 T2 T4 T4 |
| **F16** (1 bar) | S slots 0–3 (slot 0 is `f` where marked "flam"); T1 4–7; T2 8–9; T3 10–11; T4 12–14 `r` and slot 15 `X`; K on 0, 4, 8, 12 |
| **Roll39** | S 16ths on 0–7; beat 3: 16th triplets × 6 (`16t`); beat 4: 32nds × 8; K on 0, 4, 8, 12; ramp 90 → 127 |
| **F6** (beats 3–4) | 12 × `16(6:4)`: S T1 S T2 S T3 T1 T2 T3 T4 T4 T4 |
| **FTrip8** | 12 × `8t`: T1 T1 T1 T2 T2 T2 T3 T3 T3 T4 T4 T4 (last `X`); K on every beat |

**Bar map:**

| Bars | Lanes (K / S / hats-ride / cymbals / other) |
|---|---|
| 1 (1/4) | rest cell with mix `Program = 25` (TR-808) and beat text "808 kit" |
| 2 | empty (S47) |
| 3 | rest on beats 1-2; C `X` on beat 3, a half note, `+fin`: the reverse-cymbal swell on its own, rising into the 808 kick of bar 4 (A7-A07: a fade-in is a channel-wide expression ramp, so nothing else may sound during it) |
| 4–5 **808a** | K `x.........x.....` |
| 6–7 **808b** | K `x.........x.....`; CL `........x.......`; H `X.x.X.x.X.x.X.x.` |
| 8 **808c** | K `x.........x.x...`; CL `........x.......`; H `X.x.X.x.` on beats 1–2, then beat 3 = `16(6:4)` × 6 `y` and beat 4 = `16(5:4)` × 5 `y`, each roll ramping 60 → 90 |
| 9 **808d** | K `x...............`; CL `........x.......`. H rolls: beat 1 `16(7:4)` × 7; beat 2 `32(9:8)` × 9; beat 3 `32(10:8)` × 10; beat 4 `16 16 16` on slots 12–14, then **4 × 64th** on slots 15, 15.25, 15.5 and 15.75 (`RhythmicPosition`); velocities 60 → 95, with the last 64th an `o` |
| 10 (3/4) **Swell** | S (808 snare 38): beat 1 = 16ths × 4 (60 → 80); beat 2 = `16(6:4)` × 6 (80 → 100); beat 3 = `32(11:8)` × 11 (100 → 124). K `x` slot 0 (p). No cymbal: the reverse-cymbal swell moved to bar 3 (A7-A07; the fade-in had swelled the whole kit from silence) |
| 11 | mix `Program = 0` on slot 0. **HalfM:** K `xx..xx..xx..x...`, S `........x.......`, CH `....X.......X...`, plus C `X` and K `X` on slot 0 |
| 12 | K `xx..xx..xx..x.x.`; the S and CH lanes as in bar 11 |
| 13 | as 11 (no crash) |
| 14 | as 12, plus P4 |
| 15–17 **Back16** | K `x.....x.x.....x.`; S `....x..g....x...`; R `X.x.X.x.X.x.X.x.`; bar 15 slot 0 C `X` |
| 18 | F16 with flam |
| 19–21 **DBV** | K `dddddddddddddddd`; S `....x.......x...`; R `X...X...X...X...`; bar 19 slot 0 C `X` instead of R |
| 22 (ending 1) | K `dddddddddddd....`; S `....x.......rrrr`; R `X...X...X.......` |
| 23 (ending 2) | K `x...x...x...x...`; S `rrrrrrrr........`; T2 `........rr......`; T3 `..........rr....`; T4 `............rrrX` |
| 24, 26, 28, 30 **DjentD** | K `x..x..x..x..x.x.`; S `........x.......`; H `X...X...X...X...`; bar 24 slot 0 C `X` |
| 25, 29 | `SimileOneBar` (a copy of the previous bar) |
| 27 | K `x..x..x..x......`; S `........x...rrrr`; H `X...X...X.......` |
| 31 | DjentD beats 1–2, plus P8 with slot 8 = S `f` |
| 32–33 **Build1** | K `x.......x.......`; C `X.......x.......` |
| 34–35 **Build2** | K `x...x...x...x...`; C `X...x...x...x...`; H `..o...o...o...o.` |
| 36–37 **Build3** | K `x.x.x.x.x.x.x.x.`; C `X...x...x...x...`; H `..o...o...o...o.` |
| 38 **Build4** | K `dddddddddddddddd`; C `X...x...x...x...` |
| 39 | Roll39 |
| 40–46 **ChorusHT** | K `x..x..x.x.....x.`; S `........x.......`; C `X...2...x...2...`. Bar 43 adds P4. Bar 45 adds SP `..............x.`. Bars 40 and 44: slot 0 C `X` plus CH `X` |
| 47 | F16 with flam |
| 48 **Stab** | K `x...xxx...x.x...`; S `......x...x.x...`; CH `x.....x...x.x...` (all `X` velocity) |
| 49 | K `x...xxx.....xx..`; S `......x.........`; CH `X.....X.........` |
| 50 | K `x.x.........dddd`; CH `X.X.............`; S `............rrrr` |
| 51–53, 55–57 **GrooveV2** | K `x.x...x.x.x...x.`; S `....x..g.g..x...`; R `b.x.X.x.X.x.X.x.`; bar 51 slot 0 C `X` instead of `b` |
| 54 | GrooveV2 plus P4 |
| 58 | F16 |
| 59–61 **Blast** | K `b.b.b.b.b.b.b.b.`; S `.b.b.b.b.b.b.b.b`; CH `X...X...X...X...`; bar 59 slot 0 C `X` |
| 62 | Blast on slots 0–11; slots 12–15: T1 T2 T3 T4 `r` (last `X`) |
| 63–66 | Build1, Build1, Build2, Build2 |
| 67–69 | K `dddddddddddddddd`; S `....x.......x...`; C `X...x...x...x...` |
| 70 | as 67 for beats 1–2, plus F6 |
| 71–74 | ChorusHT (71 slot 0 C + CH `X`; 73 adds P4) |
| 75–77 **ChorusDB** | K `dddddddddddddddd`; S `....x.......x...`; C `X...2...x...2...` |
| 78 | ChorusDB on slots 0–11; slot 12: K `X` + C `c`; slots 13–15 rest |
| 79–81 **Swing** | R `X...X.x.X...X.x.`; SS `....x.......x...`; H `....p.......p...`; K `p.......p.......`; S `..........g.....` |
| 82 | Swing on slots 0–11, plus S `rrrr` (80 → 110) |
| 83–85 **Bridge7** (14) | K `xxx.xxx.xxxxx.`; S `........x.....`; CH `X...X...X.....`; bar 83 slot 0 C `X` |
| 86 | K `xxx.xxx.x..x..`; S `........x..x..`; CH `X...X...X..X..` |
| 87–89 **Poly7** | K all 16ths `d`, with `X` at the cell starts (87: 0, 7, 14; 88: 5, 12; 89: 3, 10); CH `X` on the same slots; S `........x.......`; R `b...b...b...b...` |
| 90 | K `dddddddd........`; CH slot 0 `X`; slot 8: S `f` + T4 `X` + C `c`; slots 9–15 silent |
| 91 **Stomp** | K `X.X..XX.X......X`; S `........f.......`; CH `X...X.......X...`; C slot 0 `X` |
| 92 | K `X.XX..X.X...X.X.`; S `........x.......`; CH `....X.......X...` |
| 93 | as 91 with S `x` and no C |
| 94 | K `X.X.X...........`; CH `X...............`; C `....X...........` |
| 95–97 **StompDB** | K 16 × `d`; S `........x.......`; CH `....X.......X...`; bar 95 slot 0 C `X` |
| 98 | K, S and CH all `X...X...X...X...` |
| 99–100 **Bounce** | K `x..xx..xx..xx..x`; S `........x.......`; CH `X...X...X...X...` |
| 101 **WindUp** | K 16 × `d`; S `rrrrrrrrrrrr....`; T1 T2 T3 T4 on slots 12–15 (last `X`); ramp 80 → 127 |
| 102–108 **GrooveS1** | K `x..x..x.x.x...x.`; S `....x..g....x..g`; R `X.x.X.x.X.x.X.x.`; bar 102 slot 0 C `X`; bar 105 adds P4 |
| 109 | F16 with flam |
| 110–115 **GrooveS2** | K 16 × `d`; S `....x.......x...`; R `X.x.X.x.X.x.X.x.`; bar 110 slot 0 C `X`; bar 113 adds P4 |
| 116 | K 16 × `d`; S `....x.......x...`; C `X...X...X...X...` |
| 117 | F16 (the D.S. lands on bar 36's crash) |
| 118 (2/4) | K `x.x.....`; C `X.X.....`; S `..f.....` |
| 119–125 | ChorusHT (bar 119 slot 0 C + CH `X`; bar 122 adds P4) |
| 126 | ChorusHT on slots 0–7, plus P8 |
| 127–133 | ChorusDB with C `X.x.X.x.X.x.X.x.` (crash 8ths); bar 130 adds P4 |
| 134 | F16 with flam |
| 135–137 **OutroDB** | K 16 × `d`; S `........x.......`; CH `....X.......X...`; bar 135 slot 0 C `X` |
| 138 | OutroDB plus P4 |
| 139 | ChorusHT |
| 140 | FTrip8 |
| 141 | slot 0: K `X`, S `X`, CH `X`, C `c`, all with fermata; the rest silent |
| 142 | whole rest + fermata; beat text "hold for the feedback" |
| 143 | K `xxxxxxxxX.X.....`; S `........X.X.....`; CH `X.......X.X.....` |
| 144 | slot 0: K `X` + T4 `X`; C `X` as a whole note with `+fout` |

**Checks against the SG targets:**

- **Fills:** 1-bar fills at 18, 23, 31 (half), 39, 47, 58, 62 (beat 4), 70 (half), 78 (choke), 90, 101, 109, 117, 134 and 140.
  That is about 10% of performed bars, all at phrase ends.
- **Ghosts:** roughly 45 ghosts against roughly 430 snare hits, about 10%.
- **Blast:** one 4-bar blast, bars 59–62.

---

## 4. Technique coverage matrix

Legend:

- **✓** means placed. "Bar.beat" is a source bar and a 1-based beat.
- **✗ excl.** means representable but deliberately not used (the reason is given).
- **n/r** means not representable, or a cross-reference or view setting (no model field of its own).

### 4.1 Note level (N)

| ID | Item | Where (track, bar.beat) | Musical role |
|---|---|---|---|
| N01 | Palm mute | RhyL/R and Bass, all `O` chugs, e.g. 19.1 | Pedal chugs |
| N02 | Let ring | Clean 2–9; RhyL/R 32–39 octaves | Ambient and pre-chorus wash |
| N03 | HOPO origin/destination | Lead 108.1–2, 109.3, 110–111, 114.1–3 | Legato and tapping |
| N04 | Bend (all types) | Lead: Bend 108.3 · Release 108.4 · BendRelease 103.1 · Prebend 105.1 · PrebendRelease 104.3 and 109.1 · PrebendBend 117.1 · Custom 116.1 · Hold 116.3 | Solo arc |
| N05 | Legato slide | Lead 52.3, 107.1; grace at 44.4 | Phrasing |
| N06 | Shift slide | Lead 106.4; RhyL/R and Bass 54.4 | Turnarounds |
| N07 | Slide in below / above | RhyL/R 40.1 (below); Lead 71.1 (above) | Chorus entries |
| N08 | Slide out up / down | Lead 115.4 (up); RhyL/R and Bass 47.4, 78.4, 109.4, 117.4, 126.4, 134.4 (down) | Chorus exits |
| N09 | Pick slide down / up | RhyL 90.3 (down); RhyR 70.4 (up) | Scrapes into the drop and into chorus 2 |
| N10 | Vibrato | Lead 102.3 and chorus long notes | |
| N11 | Wide vibrato | Lead 103.3, 105.3, 116; 18.3 | Phrase ends |
| N12 | Tremolo bar, all subtypes | TremBar RhyL 92.3 · Dive RhyL 94.2 and Sub 11/91/119/143 · Dip Lead 78.4 · Hold Sub 91.3 · Predive RhyR 96.3 · PrediveDive RhyR 92.3 · Wide Lead 140.1 · Custom Lead 117.2 | Dive bombs, sub drops, gargle |
| N13 | Natural harmonic | Clean 5 (nodes 12, 7), 9.3 (12), 9.4 (node 5); Bass 82.2 | Intro shimmer |
| N14 | Artificial harmonic | Clean 79–81 | Glass melody |
| N15 | Pinch harmonic | RhyL 14.4, 27.4, 92.3; RhyR 92.3, 96.3; Lead 107.1, 117.2 | Chug punctuation |
| N16 | Tap harmonic | Lead 111.3 | Solo |
| N17 | Semi harmonic | RhyR 53.4 | Double-track difference |
| N18 | Feedback harmonic | Lead 142.1 | Fake-out |
| N19 | Tapping / left tap | Lead 110–111 / 110.4 | Solo second wind |
| N20 | Slap / pop | Bass 79–82 | Swing interlude |
| N21 | Dead slapped | Bass 79.2&, 80.2&, 81.2&, 82.4 | |
| N22 | Trill | Lead and LeadHarm 113.3 | Solo harmony |
| N23 | Tremolo picking | Lead 67–70, 112–113; RhyL/R 59–62 | Build, blast |
| N24 | Fade in | Pad 2, 10, 110; Drums crash 3; Bass 6; Lead 51, 142 | Swells |
| N25 | Fade out | RhyL/R, Bass, Pad, Piano, Drums 144 | Ring-out |
| N26 | Wah open / closed | Lead 63–66 | Pre-chorus 2 filter lead |
| N27 | Brush down / up | Clean 40–47, 71–78, 119–134 | Strums |
| N28 | Arpeggio up / down | Clean 9.1 (up), 82.3 (down); Piano 82.1 (up) | Rolled chords |
| N29 | Rasgueado | Clean 82.1 | Swing flourish |
| N30 | Pick stroke down / up | RhyL 19 (all down), 24 (alternate) | Picking guide |
| N31 | Ghost note | Clean echo grid 4, 8; RhyR 21, 49; Drums `g` | Delay echoes, ghost chugs, ghost snare |
| N32 | Dead note | RhyL/R `X` (RiffM2, RiffD, 31, 50, …) | Chug texture |
| N33 | Grace before / on beat / bend | Lead 44.4 / 123.4 / 75.4; Drum flams 18, 31, 47, 90, 91, 109, 118, 134 | |
| N34 | Tie | Lead 108.4, 116.3; Clean 10 (voice 2, cross-bar); Bass 10; Sub 91.3 | |
| N35 | Fingering LH / RH | Clean 2–3 | Fingerstyle intro |
| N36 | Velocity / dynamic | all tracks (section 2.3; drums section 3.9) | |
| N37 | Accidental spelling | n/r | |

### 4.2 Beat level (B)

| ID | Item | Where |
|---|---|---|
| B01 | Values from whole to 64th | Pad whole notes (2); halves; quarters; 8ths; 16ths; 32nds (Roll39, 114–115); **64ths** Drums 9.4 |
| B02 | Dotted | Chorus hook 40.1; ChordM; `8.` |
| B03 | Double-dotted | Lead chorus bar 4 (43.1, 74.1, 122.1); RhyL BD1 91.3 |
| B04 | Triplet | RiffV 19.4; Lead 106; 16th triplets 110–111; Roll39 |
| B05 | Tuplets | 5:4 Lead 108 and Drums 8.4 · 6:4 Lead 114–115, Drums 8.3, 10.2 and F6 (70) · 7:4 Lead 109.3 and Drums 9.1 · 9:8 Lead 114.3 and Drums 9.2 · 10:8 Drums 9.3 · 11:8 Drums 10.3 · 13:8 Lead 115.3 · **2:3 duplet** RhyL/R and Bass 86 |
| B06 | Rest | Throughout; the silent beat at 90.4 |
| B07 | Tie (beat) | as N34 |
| B08 | Fermata | All tracks 141.1 and 142.1 |
| B09 / B10 | Accent / heavy accent | Chug accents / 98, 141, BD1 |
| B11 | Staccato | Stabs 48–50; Piano 79–81 |
| B12 | Tenuto | Pad whole notes; Lead 41.1, 51, 139 |
| B13 | Brush step | Clean 0.33 (choruses), 0.5 (final chorus) |
| B14 | Pick stroke | n/r (cross-reference, see N30) |
| B15 | Whammy curve | Every N12 note (the curves are listed in section 3) |
| B16 | Tremolo speed 8/16/32/64 | Lead 67 / 68 / 69 / 70 |
| B17 | Beat text | 1 "808 kit", 2 "let ring, fingerstyle", 19, 24, 50 "gang: HEY!", 83, 87, 90, 91, 94, 110, 112, 117, 127, 142 |
| B18 | Chord name | Clean 2–10, 40–47, 71–78, 79–82, 119–134 |
| B19 | Chord diagram | n/r |
| B20 | Lyrics (multi-line) | Lead 40–47, 71–78, 119–126; **two lines** 127–134 |
| B21 / B24 | Fade and span cross-references | n/r |
| B22 | Volume swell | n/r (FadeIn is used instead) |
| B23 | Voice 2 | Clean 2–10; Piano 6–9, 79–82, 119–134, 141, 144 |
| B25 | 8va / 8vb / 15ma / 15mb | Lead 127–134 and Piano 6–9 / Piano LH 119–134 / Piano 135–138 / Piano 91 and 95 |
| B26 | Stem override | Clean voice 1 Up and voice 2 Down (2–9); Piano 81 `Invert` |
| B27 | Beam override | RhyL/R, Bass and Drums 87–89 `Break`; 83–86 `Force`; RiffD bars `BreakSecondaryBeamBefore` |
| B28 | Dynamics ppp..fff | ppp Pad 2 · pp Pad 6 and 79 · p Clean 1–3 · mp Clean 4, Piano 6 · mf Clean 6 · f Verse · ff Chorus · fff 91.1 |
| B29 | Sound duration % | Stabs 48–50 at 60%; Bass 79–82 at 80% |
| B30 | Mix-table point | see 5.1 (volume, pan, chorus, reverb, phaser, tremolo, program, transitions, all tracks) |
| B31 | Off-grid onset | All tuplets, the 64ths and grace offsets |
| B32 | Annotation-only beat | Lead 91.1 lyric "RISE!" |
| B33 | Repeat previous beat | n/r (an editor command; nothing is stored) |
| B34 | Dead / ghost / accent combinations | RiffDend (dead + accent) 31; RhyR ghost + palm mute 21; flam with heavy accent |
| B35 | Drum notes | Drums, every bar |

### 4.3 Bar and song level (S)

| ID | Item | Where |
|---|---|---|
| S01 | Song time signature | 4/4 |
| S02 | Bar time signature | 1/4 (1), 3/4 (10), 7/8 (83–86), 2/4 (118) |
| S03 | Song key | 0 (A minor) |
| S04 | Bar key change | 118 → 2 sharps |
| S05 | Minor key flag | Song and bar 118 |
| S06 | Clef | Guitar, Bass (Bass and Sub tracks), Treble (keys), Bass (Piano 91 and 95), **Alto** (Pad 135–140) |
| S07–S08 | Repeat start / repeat end with count | 19 → 22 (×2); 48 → 49 (**×3**) |
| S09 | Alternate endings | 22 = 1, 23 = 2, 50 = 3 |
| S10 | Multi-pass ending | 49, mask 0b011 ("1.2.") |
| S11 | Double bar | section 2.4 list |
| S12 | Song tempo | 150 |
| S13 | Tempo change at a bar | 91 (112), 102 (150) |
| S14 | Tempo change inside a bar | 90, slot 12 → 112 |
| S15 | Tempo ramp | 101 (112 → 150), 139, 140 |
| S16 | Triplet feel 8th | 79–82 |
| S17 | Triplet feel 16th | 99–100 |
| S18 | Free time | 142 |
| S19 | Anacrusis flag | 1 |
| S20 | Simile, 1 bar | 25, 29 |
| S21 | Simile, 2 bars | 57–58 |
| S22 | Segno | 36 |
| S24 | Coda | 118 |
| S25 | Double Coda | 135 |
| S26 | Fine | 144 |
| S27 | To Coda | 39 |
| S28 | To Double Coda | 134 |
| S33 | D.S. al Coda | 117 |
| S23, S29, S30, S31, S32, S34, S35, S36 | Double Segno; D.C.; D.C. al Coda; D.C. al Fine; D.S.; D.S. al Fine; D.S.S.; al Double Coda variants | **✗ excl.** A performance follows exactly one D.C./D.S. (`PlaybackOrder` stage 2). Any more would change the form or print instructions the player never follows. These routes stay covered by the `SfSong` navigation fixtures. |
| S37 | Markers | section 2.4 (20 markers; colour, `LockPosition`, `LengthBars`) |
| S38 / S39 | Force / prevent line break | section 2.4 |
| S40 / S44 / S45 | Cross-references and view settings | n/r |
| S41 | Title block | section 6.3 |
| S42 | Whole-song lyrics text | `song.Lyrics` = both chorus texts plus "HEY!" and "RISE!" |
| S43 | Gray inactive voice | true |
| S46 | Bar shorter or longer than its signature | **✗ excl.** Malformed bars would be a defect in the showcase. |
| S47 | Empty bars | Lead 1–14; Drums 2; Rhythm 1–9 |
| S48 | Pickup via a short signature | 1 (1/4) |
| S49 / S50 | Common-time symbol; tempo words | n/r |

### 4.4 Track and instrument (T)

| ID | Item | Where |
|---|---|---|
| T01 | 6-string guitar | Lead, LeadHarm, Clean |
| T02 | 7-string guitar | RhyL |
| T03 | 8-string guitar | RhyR |
| T04 | 4-string bass | Sub |
| T05 | 5-string bass | Bass |
| T06 | 6/7-string bass | **✗ excl.** One bass voice in the band (5-string per the brief); already covered in `RtDenseSong` |
| T07 | Drums | Drums |
| T08 | Keyboard | Pad, Piano |
| T09 | Other GM instruments | Pad 89 → 48, Sub 38 |
| T10 | Drum kits | 0 Standard and 25 TR-808 (mix program change) |
| T11 | Custom tuning | Sub |
| T12 | Capo | Clean, 5 |
| T13 | Number of frets | section 1.5 |
| T14 / T15 | Drum map preset / custom drum map | Custom, ride bell as a diamond |
| T16–T20 | Program, channel, volume, pan, sends | section 1.5 |
| T21 | Transpose | Sub, −12 |
| T22 | Mute / solo | **✗ excl.** Must stay off so every track plays |
| T23 | Name, colour, performer, notes | section 1.5 |
| T24 / T25 | Mixer group / group levels | Guitars, Rhythm section, Keys |
| T26 | Master / buses | **✗ excl.** Plug-in chains hold machine paths; left at defaults |
| T27 | Sound source and rig | section 1.5 |

### 4.5 Totals

- **Rows:** 149. Of these, 12 are n/r or cross-references (N37, B14, B19, B21, B22, B24, B33, S40, S44, S45, S49, S50), which leaves
  **137 representable**.
- **Placed:** **125 of 137.**
- **Deliberately excluded:** 12: eight alternative D.C./D.S. routes, S46, T06, T22 and T26.
- **Technique strings:** every one of the 62 `GpEffects.All` / `TechniqueNames` strings has a home.
  - The flag-backed ones are set through the model fields: Ghost, Dead, Accent, HeavyAccent, Staccato, Tenuto, Fermata, Tie, and
    GraceBefore/GraceOnBeat/GraceBend on grace notes.
  - The strings `"Slide"` and `"Legato"` are import aliases of LegatoSlide; the coverage test should treat them as LegatoSlide
    (section 6.4).

---

## 5. Mix, humanisation and why it works

### 5.1 Mix-table automation (B30; `cell.Mix = new MixChange{...}`)

Mix values use the 0..16 scale and pan −8..8.

| Track | Bar.slot | Change |
|---|---|---|
| Pad | 1.0 | `Volume = 2` |
| Pad | 2.0 | `Volume = 10, TransitionBeats = 16` (a 4-bar fade-up) |
| Drums | 1.0 | `Program = 25` (808) |
| Drums | 11.0 | `Program = 0` |
| Clean | 40.0 | `Volume = 9` |
| Clean | 79.0 | `Volume = 11, Chorus = 10, Phaser = 5` |
| Clean | 83.0 | `Volume = 9, Chorus = 4, Phaser = 0` |
| Pad | 79.0 | `Tremolo = 8` |
| Pad | 83.0 | `Tremolo = 0` |
| Piano | 91.0 | `Reverb = 12` |
| Piano | 102.0 | `Reverb = 8` |
| Lead | 102.0 | `Reverb = 6, Pan = 0` |
| Lead | 110.0 | `Pan = -3`, then 110.4 `Pan = 3, TransitionBeats = 12` (auto-pan through the tapping) |
| Lead | 114.0 | `Pan = 0, TransitionBeats = 4` |
| Lead | 117.4 | `Reverb = 12, TransitionBeats = 2` (the dive into space) |
| Lead | 118.0 | `Reverb = 6` |
| Pad | 119.0 | **`Program = 48, Volume = 11`** (the instrument change to strings) |
| RhyL | 141.0 | `Reverb = 14, AllTracks = true` (the unison hit blooms into the silence) |
| RhyL | 143.0 | `Reverb = 3, AllTracks = true` |

**Checks after building:**

- If `--dump` shows that a mix on a **rest** cell (bars 1, 11, 110, 117) is not emitted, move it onto the next note cell.
- Drums bar 1 falls back to `MidiProgram = 25` on the track, plus `Program = 0` at 11.

### 5.2 Humanisation (drums, plus chug jitter on the guitars and bass)

```
v = centre(lane, char)
  + metric(slot)            // hats, ride, `d` kicks: slot 0 +6, slot 8 +3, odd 16ths -8
  + alt                     // continuous 16ths (`d`, hat rolls): +3 on even index, -3 on odd
  + contour(section, lane)  // see below
  + jitter                  // uniform int in [-j, +j]
clamp to 1..127 (ghosts clamp to <= 60, flam graces to 55..70)
```

**Jitter range j:**

| Hits | j |
|---|---|
| Kick, backbeat snare, crash | ±4 |
| Hats, ride | ±6 |
| Ghosts, toms | ±8 |
| Guitar and bass chugs | ±3 |
| Fill ramps | ±3 on top of the ramp |

**Seed:**
`seed = FNV1a32("AshenMeridian|" + track.Name + "|" + bar + "|" + (int)Math.Round(slot * 1000) + "|" + midiOrFret)`, then
`SplitMix64(seed)` gives the value. There is no `Random` anywhere, so every build is byte-identical (see REPRODUCIBLE_BUILDS).

**Contour (relative to the verse):**

| Section | Adjustment |
|---|---|
| Intro 808 | −25 |
| Swell | ramps as written |
| Pre-chorus | +3 → +5, rising by bar |
| Chorus | +2 |
| Post-chorus | +4 |
| Interlude | −20 |
| Bridge | +2 |
| Breakdown | K and CH +6, hats −6 |
| Solo 110–117 | +3 |
| Final chorus | +4 |
| Outro | +3 |

`g` ghosts get no contour. They stay at 35–55 and play at about ×0.8 of that.

**Export note:** a clean .gp snaps these values to the 8 dynamic steps. The centres were chosen near those steps, and the
embedded project keeps the exact values.

### 5.3 Mix notes

- **Panning:** Rhythm L and R are panned hard, the Lead sits in the centre and the Harmony slightly left.
- **Clean guitar:** it sits right of centre with a lot of chorus and reverb, so it never competes with the Lead.
- **Pad:** it is out through every verse, the bridge and the breakdown, so the guitars feel bigger when they return.
- **Low end:** the Sub and the piano 15mb notes are "events", used only on six hits, so the bottom end stays tight.
- **Group level:** Keys runs at 85% so the pad supports the guitars and stays under them.

### 5.4 Why it's a banger

- **The hook** (E–D–C–E over F–C–G–Am) appears five times in different clothes:
  - an 808-era piano line;
  - a low pedal-tone riff;
  - a harmonised sung chorus;
  - swung glass harmonics;
  - a pitch-shifted 8va final chorus in a new key.

  The listener learns it before the first chorus arrives.
- **The dynamics arc:**
  - A trap-flavoured clean intro with odd-tuplet hat rolls swells through a 3/4 reverse cymbal into the band.
  - The verses alternate a double-bass gallop with a djent 3+3+3+3+4 grid.
  - The choruses open into half-time ringing chords.
  - A swung jazz breath with slap bass sets up the bridge.
  - In the bridge, 7/8 falls into a polymetric 7-over-4 that realigns just as the tempo drops **mid-bar into a beat of silence**.
  - The breakdown hits fff at 112 with china, pinch squeals, an open-string dive bomb and an 8-string E1.
- **The surprises:**
  - a bouncing 16th-swing bar;
  - an accelerando wind-up that throws the solo back to 150;
  - a D.S. that sends the song back to the pre-chorus build;
  - a 2/4 hiccup that lifts everything a whole step;
  - a fake-out where only guitar feedback survives a free-time fermata before the last chug.
- **The solo** tells a story: call, response, development, legato lift, tapping, harmony, sweeps, a unison-bend peak and a gargled dive.

---

## 6. Implementation split (three engineers, one `FullDemoSongFactory`)

### 6.1 Files

All files are in `Source/src/TabForge.Core/Presets/FullDemoSong/`, as partial classes of `internal static partial class FullDemoSongFactory`.

| File | Owner | Contents |
|---|---|---|
| `FullDemoSongFactory.cs` | **(a)** | `public static SongProject Create()`: skeleton → `BuildDrums` → `BuildRhythm` → `BuildLeadAndKeys` → finalise |
| `FullDemoSongFactory.Helpers.cs` | **(a)**, first; **signatures frozen by this plan** | Shared helpers (6.2). Push a compiling stub within 30 minutes so (b) and (c) can build against it. |
| `FullDemoSongFactory.Skeleton.cs` | **(a)** | The title block; 10 tracks and all T-items; 144 bars on every track; time signatures with `EnsureSlots`; tempo events, keys, repeats, endings, directions, feels, free time, anacrusis and simile flags; double bars and line breaks; markers and `SectionName`; clefs; the mixer groups; every `MixChange`; `song.Lyrics` |
| `FullDemoSongFactory.Drums.cs` | **(b)** | `BuildDrums(song)`: section 3.9, fill kinds, humanisation (5.2), the drum map |
| `FullDemoSongFactory.Rhythm.cs` | **(c1)** | `BuildRhythm(song)`: RhyL, RhyR, Bass and Sub (3.1–3.4) |
| `FullDemoSongFactory.LeadKeys.cs` | **(c2)** | `BuildLeadAndKeys(song)`: Lead, LeadHarm, Clean, Pad and Piano (3.5–3.8), including lyrics and chord names |

- **Track order and names are fixed** by section 1.5. Each builder finds its tracks by `Name`.
- **Structure is written only by (a).** It is set on every track, so no builder touches time signatures or directions.

**Finalise** (owned by (a)):

1. `GuitarProImporter.LinkTieOrigins(t)` for each track.
2. `DrumMaps.Apply(drums, DrumMaps.Custom)`.
3. `song.IsDirty = false`.
4. `ProjectValidator.Validate(song)`.

### 6.2 Shared helpers (the signatures are the contract)

```csharp
// Structure
static void ForAllTracks(SongProject s, int bar, Action<MeasureModel> set);           // bar is 1-based
static void EnsureSlots(MeasureModel m);                                              // Cells.Count >= BarSlots(num, den), both voices
static TrackModel Track(SongProject s, string name);

// Pitched parts
static void Riff(TrackModel t, int bar, string tokens, int baseVel, int voice = 0,
                 int transposeFrets = 0, int velOffset = 0, Func<string,string>? swap = null);
    // Parses the section 0.1 grammar. Asserts the slot sum equals the bar length (throws with bar/track in the message).
    // Places each token with Put. Applies flags. Chug jitter comes from Hv(). transposeFrets implements "+2".
static TabCell Put(MeasureModel m, double slot, int den, int dots = 0, int tupN = 0, int tupD = 0, int voice = 0);
    // Sets RhythmicPosition when the slot is fractional or differs from the cell index. Same rule as SelfTestGpFixture.Put.
static TabNote Fret(TrackModel t, int s /*1 = top*/, int fret, int vel, params string[] tech);   // MidiValue = PitchOf
static TabNote Harm(TrackModel t, int s, int fret, string kind /*Natural|Artificial|Pinch|Tap|Semi|Feedback*/,
                    double harmonicFret, int vel);   // uses GuitarProImporter.HarmonicMidi(kind, tuning[s]+capo, fret, harmonicFret)
static void Bend(TabNote n, string type, params (int off, int val)[] pts);              // Add("Bend"), BendTypeName, BendPoints
static void Whammy(TabCell c, TabNote n, string subtype, params (int off, int val)[] pts); // tag + cell.WhammyPoints
static void Grace(TabCell c, TrackModel t, int s, int fret, bool before, string tag, double slots = 0.5, int vel = 62);
static void Hopo(TabNote origin, TabNote dest);                                         // the HOPO + Origin/Destination pair
static TabNote Key(TrackModel keys, int midi, int vel);   // octave-string placement: highest string <= pitch, fret = remainder

// Drums
static void Grid(TrackModel drums, int bar, Section sec, params (string lane, string pattern)[] lanes);
static void Fill(TrackModel drums, int bar, FillKind kind, bool flam = false);
static void Roll(TrackModel drums, int bar, double startSlot, int count, int tupN, int tupD, int den, int midi, int v0, int v1);
static TabNote Drum(int midi, int vel, bool ghost = false);   // StringIndex = DrumLine(midi), Fret = midi, MidiValue = midi

// Humanisation (5.2)
static int Hv(string track, int bar, double slot, int midiOrFret, int centre, int jitter,
              Section sec, Lane lane, int indexInRun = -1);
enum Section { IntroA, Swell, IntroB, Verse1, Pre, Chorus, Post, Verse2, Interlude, Bridge, Breakdown, Bounce, WindUp,
               Solo1, Solo2, Coda, FinalChorus, Outro, FakeOut }
```

### 6.3 Owner checklists

**(a) Skeleton:**

- **Title block:**

  | Field | Value |
  |---|---|
  | Title | "Ashen Meridian" |
  | Subtitle | "Built-in demo, full arrangement" |
  | Artist | "TabForge Demo" |
  | Album | "Examples" |
  | MusicAuthor / LyricsAuthor | "TabForge contributors" |
  | Copyright | "(c) 2026 TabForge contributors, CC0 1.0" |
  | TabAuthor | "TabForge" |
  | Instructions | "Original composition written for TabForge. Public domain (CC0 1.0). RhyL 7-string drop A, RhyR 8-string, Clean capo 5." |
  | Notice | "Every riff, melody, lyric and drum part was written for TabForge." |

- Everything in sections 1.2–1.5, 2.4 and 5.1.
- Register `--write-demo-song` (coverage doc §7).
- **Self-tests** (details in 6.4): `TestFullDemoSong` and the coverage walk.
- **Replace the old sample:**
  - Regenerate `samples/TabForge Demo - Ashen Meridian.gp` with `--write-demo-song`.
  - Update `SfDemoSong` (10 tracks, 144 bars, 154 performed bars, 20 markers, tempo 150).
  - The owner decides whether to delete `samples/make_demo_song.py` and the `.gp5`. Only `*.gp` is shipped (`TabForge.csproj`). (Status 2026-10-22: the script is already gone from this checkout.)

**(b) Drums:** section 3.9 exactly. Humanisation is applied only through `Hv` (no randomness anywhere else).

**(c1) Rhythm, bass and sub:** sections 3.1–3.4, using RiffX constants as strings. R is produced as L plus the numbered deltas.

**(c2) Lead and keys:** sections 3.5–3.8, including lyrics (B20), chord names (B18), octave shifts (B25), voices (B23) and stems (B26).

### 6.4 Acceptance (all owners, before merge)

- **Validation:** `ProjectValidator.Validate` passes.
- **Round trips:** `.tforge` and embedded `.gp` round-trip with an empty allow-list (`RtVerify`, `RtTforge`, `RtGpEmbedded`).
- **Clean .gp:** logged with the section 5 gaps of the coverage doc added to the allow-list.
- **Playback order:** `PlaybackOrder` equals the sequence in 2.2, as a literal expected list in the test.
- **Timeline:** the total is 245–255 s.
- **Coverage walk:** `DemoCoverage` asserts:
  - each placed ID of section 4;
  - every `GpEffects.All` string, with the flag-backed and alias mapping from 4.5;
  - every `Directions` token used;
  - every bar flag;
  - every tuplet ratio listed in B05;
  - the 64ths;
  - octave shifts of +12, −12, +24 and −24.
- **Slot sums:** every bar of every track adds up to its signature (the `Riff` and `Grid` assertions).
- **Engraving and listening:** run `--dump`, `--audit`, `--render` and `--layout-audit`, then open the result in the running app and
  compare against .gp5 (the visual-inspection rule). Specifically check:
  - 8va / 15ma / 15mb;
  - the alto clef;
  - simile glyphs;
  - the volta "1.2.";
  - the 7/8 beams;
  - voice 2 stems;
  - that the Sub is audible;
  - that the 808 kit switches back at bar 11.

---

## 7. Not representable, so skipped

- **Chord diagrams (B19):** only chord names are used.
- **Volume swell (B22):** FadeIn is used instead.
- **Common or cut time and tempo words (S49, S50).**
- **Final or heavy barlines:** a double bar is used at 144.
- **Hairpins, pedal marks and a grand staff:** the piano uses two voices on one staff.
- **Per-note accidental spelling (N37):** accidentals are derived from the key.
- **Model holds it, but nothing sounds:** fermatas do not hold, free time does not change timing, and dynamics are not engraved.
  The plan does not rely on any of these sounding (the fake-out silence is a real bar of rests).
