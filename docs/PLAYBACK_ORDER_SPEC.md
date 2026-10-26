# Playback order: behaviour specification

Audit 5, item A5-06, clean-room step 1. This document states how TabForge turns a score's bar
structure into the order in which bars are performed. It describes musical behaviour as the .gp format
files define it. It is the only input for the implementer. Section 7 lists the worked examples;
they are the acceptance tests.

Conventions used throughout:

- Bars are numbered from **0** (bar index 0 is the first bar of the song). Every example uses indices.
- A **pass** is one time through a repeated section. Passes are counted from 1 in musical language
  ("1st ending", "pass 2").
- `|:` marks a repeat open, `:|` a repeat close, `xN` the close's repeat count, `[1.]` or `[1.2.]` an
  alternate ending, and direction names are written as in section 2.3.

---

## 1. Public surface (drop-in replacement)

Namespace `TabForge.Playback`, one `public static class PlaybackOrder` with exactly these members:

| Member | Signature | Purpose |
|---|---|---|
| Performed order | `public static List<int> Build(SongProject project, PlaybackOptions opt)` | Bar indices in performance order (section 3 to 5). |
| Loop range | `public static (int startBar, int endBar) LoopRange(SongProject project, PlaybackOptions opt)` | Inclusive source-bar range of the loop (section 6). |
| Loop bounds | `public static (double startMs, double endMs, bool available) LoopBounds(ScoreTimeline timeline, SongProject project, PlaybackOptions opt)` | Loop start and end in milliseconds inside a compiled timeline (section 6). |
| Repeat cap | `public const int MaxRepeats = 99;` | The largest pass count a repeat close can have. The importer, the validator and the repeat-count dialog read it. |
| Direction names | `internal static string[] DirectionTokens(string? directions)` | Splits and normalises a bar's direction text (section 2.3). The MusicXML exporter calls it. |

Callers today: the MIDI compiler (builds the order when it is not given one), the timeline builder
(a thin wrapper that returns `Build`), the playback engine (live edits: it rebuilds the order with the
start bar at 0 and finds where the playhead is), and the project validator, importer and editor (the
cap only).

**Output of `Build`.** A plain list of source-bar indices. There is no per-entry pass number or flag.
Callers that need to know which occurrence of a bar they are in count the earlier entries with the
same index. The same bar may appear any number of times. The list is empty only in the cases named in
R36 and R37.

---

## 2. Inputs

### 2.1 Project

- `SongProject.Tracks`: the list of tracks. Each `TrackModel` has `Measures` (a list of
  `MeasureModel`, one per bar).
- **Bar count** = the largest `Measures.Count` over all tracks. No tracks, or all tracks empty, means
  zero bars.
- **Structure comes from the first track only** (R2).

### 2.2 Bar fields read (`MeasureModel`)

| Field | Type | Meaning |
|---|---|---|
| `RepeatStart` | bool | The bar opens a repeat (`|:` before the bar). |
| `RepeatEnd` | bool | The bar closes a repeat (`:|` after the bar). |
| `RepeatCount` | int (default 2) | Total passes for this close. |
| `AlternateEnding` | int 0..8 | Single ending number; 0 = none. |
| `AlternateEndingMask` | int 0..255 | Every pass the ending covers: bit n = pass n+1 (for example `1.2.3.` = 0b111). |
| `EndingPasses` | int (read-only, derived) | The mask if it is non-zero; otherwise the bit for `AlternateEnding` (for example ending 2 = 0b10); otherwise 0. **This is the ending value used everywhere below.** 0 means "not an ending bar". |
| `Directions` | string | Comma-separated direction names (section 2.3). |

### 2.3 Direction names

`Directions` is split on commas; each piece is trimmed and empty pieces are dropped. Each piece is then
normalised as follows (checked in this order, spellings exact and case-sensitive at this step):

| Stored spelling | Normalised to |
|---|---|
| `JumpDaCoda` | `ToCoda` |
| `JumpDaDoubleCoda` | `ToDoubleCoda` |
| `JumpDaCapoAlFine` | `DaCapo` |
| `JumpDalSegnoAlFine` | `DalSegno` |
| `JumpDaCapoAlDoubleCoda` | `DaCapoAlCoda` |
| `JumpDalSegnoAlDoubleCoda` | `DalSegnoAlCoda` |
| `JumpDalSegnoSegno`, `JumpDalSegnoSegnoAlFine` | `DalSegnoSegno` |
| `JumpDalSegnoSegnoAlCoda`, `JumpDalSegnoSegnoAlDoubleCoda` | `DalSegnoSegnoAlCoda` |
| any other piece starting with `Target` and longer than 6 characters | the piece without `Target` (`TargetSegno` becomes `Segno`, `TargetFine` becomes `Fine`) |
| any other piece starting with `Jump` and longer than 4 characters | the piece without `Jump` (`JumpDaCapo` becomes `DaCapo`) |
| anything else | unchanged |

After normalisation, names are compared **case-insensitively**. The names that affect playback:

| Kind | Names |
|---|---|
| Destinations | `Segno`, `SegnoSegno`, `Coda`, `DoubleCoda` |
| Stop mark | `Fine` |
| Jump back to the start | `DaCapo`, `DaCapoAlCoda` |
| Jump back to the segno | `DalSegno`, `DalSegnoAlCoda` |
| Jump back to the double segno | `DalSegnoSegno`, `DalSegnoSegnoAlCoda` |
| Leave for the coda | `ToCoda`, `ToDoubleCoda` |

"al Coda" jumps are `DaCapoAlCoda`, `DalSegnoAlCoda` and `DalSegnoSegnoAlCoda`. Every other jump
behaves as "al Fine". Unknown names are ignored, including unprefixed spellings not in the table such
as `DaCapoAlFine` (example E47).

### 2.4 Options read (`PlaybackOptions`)

| Field | Used by | Meaning |
|---|---|---|
| `RepeatExpansion` (default true) | `Build` | false: bars play once each in written order, before directions are applied (R35). |
| `StartBar` (default 0) | `Build` | Start playback from this bar (R36). |
| `LoopStartBar`, `LoopEndBar` | loop range and bounds | Loop bars, in either order. |
| `LoopStartCell` | loop bounds | Grid cell inside the first loop bar. |
| `LoopEndCell` (default -1) | loop bounds | Inclusive grid cell inside the last loop bar; -1 means the end of the bar. |

---

## 3. Stage 1: repeats and alternate endings

Stage 1 walks the bars from bar 0 and produces the **repeat-expanded order**. Stage 2 (section 4) then
applies directions to it, and stage 3 (section 5) applies the start bar.

### 3.1 Where a repeat starts

- **R1.** Song start acts as an open. Until the first open bar is reached, a close sends playback back to
  bar 0.
- **R2.** Only the first track's bars carry structure. A bar beyond the end of the first track (another
  track is longer) is a plain bar: no repeat, no ending, no direction.
- **R3.** When an open bar is played, it becomes the return point of the current repeat. If several open
  bars come before one close, the most recent one wins; the earlier opens are ordinary bars (E8).
- **R4.** The pass counter starts again at pass 1 only when playback reaches an open bar for the first
  time (going further forward in the song than it has been). Returning to the same open bar during its
  own repeat does not reset the count.
- **R5.** A close with no open of its own goes back to the bar just after the previous **finished**
  repeat's close, or to bar 0 when no repeat has finished yet (E2, E10, E11, E12). *Decision point A in
  section 8.*

### 3.2 Repeat counts

- **R6.** A close's `RepeatCount` is the total number of passes. Values below 2 count as 2; values above
  99 count as 99 (E3, E5, E6).
- **R7.** Each close counts its own jumps back. A close with count N sends playback back N-1 times, then
  lets it through. The count belongs to the close, not to any ending over it (E16).
- **R8.** A bar that is both an open and a close is a one-bar repeat and plays N times in a row (E7).
- **R9.** When a repeat finishes (the close lets playback through and no further close of the same repeat
  follows), the repeat is over: the pass counter and all closes' jump counts are cleared.

### 3.3 Several closes sharing one open

- **R10.** A repeat may have several closes after one open, usually each under its own ending (1st
  ending with a close, 2nd ending with a close, 3rd ending without). Each close sends playback back to the
  same open, following R7 separately (E17).
- **R11.** A close that is passed while its jumps are used up does not end the repeat if another close
  follows before the next open bar; playback continues to that close. The repeat ends at the last close
  of the group.

### 3.4 Alternate endings

- **R12.** A bar with no ending plays on every pass, unless it is inside an ending bracket (R14).
- **R13.** An ending bar plays on the passes in its mask (bit n = pass n+1) and is skipped on the others.
  An ending may cover several passes (`1.2.3.`).
- **R14.** Ending bracket span. From an ending bar, look forward: if a close is reached before any other
  ending bar or any open bar, the bracket covers every bar up to and including that close. Bars without an
  ending mark inside the bracket take its passes (E14, E20). Otherwise the bracket covers only the bars
  that carry the mark. Each of several consecutive bars with the same mark is its own ending bar (E19,
  E21).
- **R15.** A skipped close still counts: if it has jumps left, it sends playback back (and uses one of
  them) even though its bar was not played (E13, E22).
- **R16.** If a skipped close has no jumps left and no further close of the same repeat follows (R11), the
  repeat is over (R9).
- **R17.** Endings only name passes 1 to 8. On pass 9 and later, any bar with an ending (or inside a
  bracket) is skipped; bars with no ending still play (E24).
- **R18.** After a repeat has finished, ending bars that follow it (a `3.` after the last close, a `2.`
  after a single close) play once, whatever their passes, until the next open bar is played (E14, E15,
  E16, E17, E21).
- **R19.** Before any repeat has finished, the song start acts as an open (R1) and counts as pass 1. So a
  stray ending that does not include pass 1 and has no close after it is skipped; one that includes pass 1
  plays (E26, E27).
- **R20.** An ending bar that also opens the next repeat, placed directly after a finished repeat's close
  (or on bar 0), belongs to the repeat before it. It plays as that repeat's ending and then as the first
  bar of the new repeat. Inside its own repeat it plays on every pass (E18, E25).
- **R21.** Endings are checked before the open on the same bar: a skipped bar never becomes a return point.

---

## 4. Stage 2: directions (D.C., D.S., Coda, Fine)

Stage 2 reads the repeat-expanded order from the start and copies it to the output, entry by entry,
until a jump is taken.

- **R22.** Before any jump, `Fine`, `ToCoda` and `ToDoubleCoda` are ignored. Fine does not end playback
  unless a D.C. or D.S. has been taken (user decision D-5; E31).
- **R23.** Only one D.C./D.S. jump is taken per playback. After it, every D.C./D.S. mark is ignored,
  including the one that caused the jump (E38).
- **R24.** A jump bar is played in full, then the jump happens.
- **R25.** A jump bar inside a repeat is acted on only on the repeat's **last** pass, after the pass
  plays out to that bar. On earlier passes it is an ordinary bar (E52, E53). *Decision point B in section
  8.*
- **R26.** Jump targets:
  - D.C. (either spelling): the first bar of the repeat-expanded order (bar 0 in practice).
  - D.S.: the first bar in the repeat-expanded order that carries `Segno`.
  - D.S.S.: the first bar that carries `SegnoSegno`; when there is none, the first `Segno` (E35).
- **R27.** If the target does not exist (a D.S. with no segno anywhere), the mark is ignored and playback
  goes on; a later D.C./D.S. can still be taken (E36, E37).
- **R28.** After the jump, playback runs **straight through**, bar by bar in written order, from the
  target to the end of the song. Repeats are not taken again.
- **R29.** After the jump, only the **last** ending of each repeat is played. A bar in an ending bracket
  is left out when a later ending of the same repeat names a higher pass; the bracket extends as in R14
  (through its close). The later ending counts even when its bar also opens the next repeat (E49, E50,
  E51, E54). *Decision point C in section 8.*
- **R30.** After the jump, a bar carrying `Fine` is played and playback stops there, whatever other marks
  it carries (E29, E30, E39, E40).
- **R31.** After an "al Coda" jump, the first bar carrying `ToCoda` or `ToDoubleCoda` is played, then
  playback continues at the nearest bar carrying `Coda` or `DoubleCoda`: the first one after it in written
  order; if there is none after it, the first one from the song start. The coda bar is played and
  playback continues straight on from there (E32, E33, E42, E43). A second coda jump is not taken.
- **R32.** After a plain D.C./D.S. (al Fine), `ToCoda` marks are ignored (E41).
- **R33.** If a To Coda sign has no coda anywhere, it is ignored.
- **R34.** If no jump is taken, the output is the repeat-expanded order unchanged.

---

## 5. Stage 3: options

- **R35.** Repeat expansion off: stage 1 outputs every bar once, in written order (repeats and endings are
  ignored). Stage 2 still applies directions, including R29 after a jump (E58, E62).
- **R36.** Start bar. If the start bar is above 0, playback starts at the **first performance** of that bar
  and follows the whole performed order from there, including a later return to earlier bars (a D.S./D.C.
  jump or the next pass of a repeat) (E55, E59). If the start bar is never performed, playback starts at
  the first performed bar with a higher written index. If neither exists, the result is the single start bar,
  provided it is inside the song (E56, E60). A start bar at or past the song's end gives an empty list (E57).
- **R37.** A score with no bars gives an empty list (E61).

### Safety limits

- **R38.** `Build` must finish for every input, including contradictory structures. Stage 1 stops after
  (bar count × 101 + 1024) bar visits, played or skipped, and returns what it has.
- **R39.** Stage 2 stops after max(256, 64 × expanded-order length) entries before a jump, and after
  (4 × bar count + 16) bars after it.
- **R40.** Passes per close are capped at 99 (R6). With a legal structure, the caps in R38 and R39 are never
  reached.

---

## 6. Loop range and bounds

- **R41.** Loop range: clamp `LoopStartBar` and `LoopEndBar` to 0 .. (bar count - 1) (to 0 when there are
  no bars); if start is after end, swap them. Returns the inclusive range.
- **R42.** Loop bounds, using the timeline's bar list (each entry: source bar, start ms, end ms, slot
  count):
  1. If the loop's start bar or end bar has no entry in the timeline, return (0, 0, false).
  2. Take the **earliest** entry of the start bar and the **latest** entry of the end bar.
  3. Start cell = `LoopStartCell` clamped to 0 .. slots - 1 of the start entry.
     Start = entry start + (entry length × start cell ÷ slots).
  4. End cell: -1 means the full bar (slots). Otherwise `LoopEndCell` + 1, clamped to 1 .. slots of the
     end entry. End = entry start + (entry length × end cell ÷ slots). A slot count below 1 counts as 1.
  5. If end > start, return (start, end, true); otherwise (0, 0, false).

---

## 7. Worked examples (acceptance tests)

One track unless stated; bars not mentioned are plain; repeat count is 2 unless stated; default options
unless stated. **T** = an existing self-test checks this exact order. **Q** = a quirk the spec keeps on
purpose. **A/B/C** = depends on the decision in section 8 (the listed order is the spec's; the current
build's differs as noted there).

### Repeats

| # | Bars | Structure | Expected order | Rules |
|---|---|---|---|---|
| E1 | 4 | 0 `|:`, 2 `:|` | 0,1,2,0,1,2,3 | R3 R7 **T** |
| E2 | 3 | 1 `:|` (no open) | 0,1,0,1,2 | R1 R5 |
| E3 | 3 | 0 `:|x1` (no open) | 0,0,1,2 | R1 R6 |
| E4 | 3 | 0 `|:`, 1 `:|x3` | 0,1,0,1,0,1,2 | R7 |
| E5 | 3 | 0 `|:`, 1 `:|x0` | 0,1,0,1,2 | R6 |
| E6 | 5 | 0 `|:`, 1 `:|x100` | (0,1) × 99, then 2,3,4 (201 entries) | R6 R40 |
| E7 | 4 | 0 `|:` and `:|x4`, 1 `|:`, 2 `:|` | 0,0,0,0,1,2,1,2,3 | R8 R4 **T** |
| E8 | 5 | 1 `|:`, 2 `|:`, 3 `:|x12` | 0,1, then (2,3) × 12, then 4 | R3 R6 **T** |
| E9 | 5 | 0 `|:`, 1 `:|`, 2 `|:`, 3 `:|` | 0,1,0,1,2,3,2,3,4 | R3 R9 |
| E10 | 5 | 0 `|:`, 1 `:|`, 3 `:|` (no second open) | 0,1,0,1,2,3,2,3,4 | R5 **A** |
| E11 | 3 | 0 `:|`, 2 `:|` (no opens) | 0,0,1,2,1,2 | R5 **A** |
| E12 | 4 | 0 `|:`, 1 `|:` and `:|`, 2 `:|` | 0,1,1,2,2,3 | R3 R5 **A** |

### Alternate endings

| # | Bars | Structure | Expected order | Rules |
|---|---|---|---|---|
| E13 | 4 | 0 `|:`, 1 `[1.]`, 2 `[2.]`, 3 `:|` | 0,1,0,2,3 | R14 R15 **T** |
| E14 | 6 | 0 `|:`, 2 `[1.]`, 3 `:|`, 4 `[2.]` | 0,1,2,3,0,1,4,5 | R14 R18 **T** |
| E15 | 4 | 1 `[1.2.]` and `:|x3` (no open), 2 `[3.]` | 0,1,0,1,0,2,3 | R1 R13 R16 R18 **T** |
| E16 | 4 | 0 `|:`, 1 `[1.2.3.]` and `:|x2`, 2 `[4.]` | 0,1,0,1,2,3 | R7 R18 **T** |
| E17 | 6 | 0 `|:`, 1 `[1.]` `:|`, 2 `[2.]` `:|`, 3 `[3.]` | 0,1,0,2,0,3,4,5 | R10 R11 R18 **T** |
| E18 | 5 | 0 `|:`, 1 `[1.]` `:|`, 2 `[2.]` and `|:`, 3 `:|` | 0,1,0,2,3,2,3,4 | R20 **T** |
| E19 | 6 | 0 `|:`, 1 `[1.]`, 2 `[1.]`, 3 `:|`, 4 `[2.]` | 0,1,2,3,0,4,5 | R14 |
| E20 | 6 | 0 `|:`, 1 `[1.]`, 3 `:|`, 4 `[2.]` | 0,1,2,3,0,4,5 | R14 (bar 2 inside the bracket) |
| E21 | 5 | 0 `|:`, 1 `[1.]` `:|`, 2 `[2.]`, 3 `[2.]` | 0,1,0,2,3,4 | R14 R18 (ending without a close, two bars) |
| E22 | 5 | 0 `|:`, 1 `[1.2.]` and `:|x4`, 2 `[3.]` | 0,1,0,1,0,0,2,3,4 | R15 R16 **Q** |
| E23 | 4 | 0 `|:`, 1 `[1.]` and `:|x10`, 2 `[2.]` | 0,1, then 0 × 9, then 2,3 | R15 **Q** |
| E24 | 4 | 0 `|:`, 1 `[1.2.3.4.5.6.7.8.]` and `:|x10` | (0,1) × 8, then 0,0,2,3 | R17 **Q** |
| E25 | 3 | 0 `|:` and `[1.]`, 1 `:|`, 2 `[2.]` | 0,1,0,1,2 | R20 **Q** |
| E26 | 4 | 1 `[2.]` (no repeats at all) | 0,2,3 | R19 **Q** |
| E27 | 4 | 1 `[1.]` (no repeats at all) | 0,1,2,3 | R19 |
| E28 | 7 | 0 `|:`, 1 `[1.]` `:|`, 2 `[2.]` `:|`, 3 `[3.]`, 4 `|:`, 5 `:|` | 0,1,0,2,0,3,4,5,4,5,6 | R9 R10 R18 |

### Directions

| # | Bars | Structure | Expected order | Rules |
|---|---|---|---|---|
| E29 | 4 | 0 Segno, 2 Fine, 3 DalSegno | 0,1,2,3,0,1,2 | R22 R26 R30 **T** |
| E30 | 6 | 1 Segno, 3 Fine, 4 DalSegno | 0,1,2,3,4,1,2,3 | R24 R30 **T** |
| E31 | 4 | 1 Fine (no jump) | 0,1,2,3 | R22 (D-5) **T** |
| E32 | 5 | 1 Coda, 3 ToCoda, 4 DaCapoAlCoda | 0,1,2,3,4,0,1,2,3,1,2,3,4 | R31 (coda found by wrapping to the start) **T** |
| E33 | 6 | 2 ToCoda, 3 DaCapoAlCoda, 4 Coda | 0,1,2,3,0,1,2,4,5 | R22 R31 **T** |
| E34 | 6 | 0 Segno, 1 SegnoSegno, 3 Fine, 4 DalSegnoSegno | 0,1,2,3,4,1,2,3 | R26 **T** |
| E35 | 6 | 0 Segno, 1 DalSegnoSegno (no double segno) | 0,1,0,1,2,3,4,5 | R26 |
| E36 | 5 | 0 DalSegno (no segno) | 0,1,2,3,4 | R27 |
| E37 | 6 | 1 DalSegno (no segno), 3 DaCapo | 0,1,2,3,0,1,2,3,4,5 | R23 R27 |
| E38 | 6 | 1 DaCapo, 3 DaCapo | 0,1,0,1,2,3,4,5 | R23 |
| E39 | 6 | 0 Fine, 2 DaCapo | 0,1,2,0 | R30 |
| E40 | 6 | 1 ToCoda and Fine, 2 DaCapoAlCoda, 4 Coda | 0,1,2,0,1 | R30 (Fine wins) |
| E41 | 6 | 1 ToCoda, 2 DaCapo, 3 Coda | 0,1,2,0,1,2,3,4,5 | R32 |
| E42 | 6 | 1 ToCoda, 2 DaCapoAlCoda, 4 DoubleCoda | 0,1,2,0,1,4,5 | R31 |
| E43 | 6 | 0 Segno, 1 ToDoubleCoda, 2 DalSegnoAlCoda, 4 Coda | 0,1,2,0,1,4,5 | R31 |
| E44 | 5 | 0 TargetSegno, 1 TargetSegnoSegno, 2 JumpDaDoubleCoda, 3 JumpDalSegnoSegnoAlCoda, 4 TargetDoubleCoda | 0,1,2,3,1,2,4 | section 2.3, R31 |
| E45 | 5 | 1 TargetSegno, 2 TargetFine, 3 JumpDalSegnoAlFine | 0,1,2,3,1,2 | section 2.3, R30 |
| E46 | 5 | 1 Fine, 3 JumpDaCapoAlFine | 0,1,2,3,0,1 | section 2.3, R30 |
| E47 | 5 | 2 DaCapoAlFine, 3 Fine | 0,1,2,3,4 | section 2.3 (unknown spelling ignored) **Q** |
| E48 | 6 | 2 daCapo (lower case d) | 0,1,2,0,1,2,3,4,5 | section 2.3 (case-insensitive) |

### Directions with repeats

| # | Bars | Structure | Expected order | Rules |
|---|---|---|---|---|
| E49 | 6 | 0 `|:`, 2 `[1.]`, 3 `:|`, 4 `[2.]`, 5 DaCapo | 0,1,2,3,0,1,4,5,0,1,4,5 | R28 R29 |
| E50 | 6 | 0 `|:`, 1 `[1.]` `:|`, 2 `[2.]` `:|`, 3 `[3.]`, 4 DaCapo | 0,1,0,2,0,3,4,0,3,4,5 | R29 |
| E51 | 6 | 0 `|:` and Segno, 1 `[1.]` `:|`, 2 `[2.]`, 3 DalSegno | 0,1,0,2,3,0,2,3,4,5 | R29 |
| E52 | 4 | 0 `|:` and Segno, 1 `:|` and DalSegno | 0,1,0,1,0,1,2,3 | R25 R28 **B** |
| E53 | 6 | 0 `|:`, 1 `:|`, 2 Segno, 3 `|:`, 4 `:|` and DalSegno | 0,1,0,1,2,3,4,3,4,2,3,4,5 | R25 R28 **B** |
| E54 | 6 | 0 `|:`, 1 `[1.]` `:|`, 2 `[2.]` and `|:`, 3 `:|`, 4 DaCapo | 0,1,0,2,3,2,3,4,0,2,3,4,5 | R29 **C** |

### Options and edge cases

| # | Bars | Structure and options | Expected order | Rules |
|---|---|---|---|---|
| E55 | 4 | 0 `|:`, 2 `:|`; start bar 1 | 1,2,0,1,2,3 | R36 |
| E56 | 4 | as E55; start bar 3 | 3 | R36 |
| E57 | 4 | as E55; start bar 9 | (empty) | R36 |
| E58 | 4 | as E55; repeat expansion off | 0,1,2,3 | R35 |
| E59 | 6 | 1 Segno, 3 Fine, 4 DalSegno; start bar 2 | 2,3,0,1,2 | R36 |
| E60 | 6 | 0 Fine, 2 DaCapo; start bar 4 | 4 | R36 (no later bar performed) |
| E61 | 0 | no bars | (empty) | R37 |
| E62 | 6 | as E49; repeat expansion off | 0,1,2,3,4,5,0,1,4,5 | R35 R29 |
| E63 | 2 + 4 | Track 1: 2 bars, 0 `|:`, 1 `:|`. Track 2: 4 plain bars | 0,1,0,1,2,3 | R2 |

### Loop range and bounds

| # | Setup | Expected | Rules |
|---|---|---|---|
| E64 | 4 bars, 4/4 at 120 bpm (2000 ms a bar, 16 slots), loop bars 1 to 2 | (2000, 6000, true) | R42 **T** |
| E65 | as E64, loop start cell 4, end cell 7 | (2500, 5000, true) | R42 **T** |
| E66 | 4 bars, loop bars 3 to 1 | range (1, 3) | R41 |
| E67 | 4 bars, loop bars -2 to 9 | range (0, 3) | R41 |
| E68 | loop bar missing from the timeline (for example, empty timeline) | (0, 0, false) | R42 |

---

## 8. Decision points (the spec departs from the current build)

The current build differs from this spec in three untested cases. The spec follows the musical rule.
The manager should confirm before implementation.

- **A. Close without an open after a finished repeat (R5; E10, E11, E12).** The current build goes back to
  the most recent open anywhere earlier in the song. That replays the finished repeat, which clears the
  counts again, so playback cycles until the safety cap (about 1,300 to 1,500 entries). The spec returns to
  the bar after the previous close.
- **B. D.C./D.S. inside a repeat (R25; E52, E53).** The current build takes the jump the first time the
  bar is reached, so the rest of that repeat's passes are lost: E52 gives 0,1,0,1,2,3 and E53 gives
  0,1,0,1,2,3,4,2,3,4,5. The spec plays the repeat out first.
- **C. Earlier ending after a jump, when the later ending's bar also opens the next repeat (R29; E54).**
  The current build plays both endings after the jump (…,4,0,1,2,3,4,5). The spec drops the earlier one
  (…,4,0,2,3,4,5).

Everything else in section 7 matches the current build exactly.

---

## 9. Acceptance gate (existing self-tests)

These `--selftest` checks must still pass unchanged:

| Test method (file) | Checks that rely on this spec |
|---|---|
| `TestTimelineLoopAndOrder` (SelfTest.cs) | "repeat expands 4 bars to 7", "repeat replays bars 1-3" (E1) |
| `CheckRepeatEndings` (SelfTestPlayback.cs) | "alternate endings: first pass uses ending 1, second uses ending 2 (bracket runs to the close)" (E13); "a first-ending bracket spans every bar up to the repeat close" (E14); "a 1.2. ending plays on passes 1 and 2, and a close with no open repeats from the start" (E15); "a one-bar repeat plays its bar on every pass" (E7); "a repeat goes back to the most recent open and can play 12 times" (E8); "a close plays its own repeat count, then the next ending plays" (E16); "each close of a repeat with several endings sends playback back once" (E17); "an ending bar that opens the next repeat is repeated with it" (E18) |
| `TestGp5EditingSemantics` (SelfTestGp5.cs) | "Dal Segno repeats from the Segno destination through Fine" (E29); "Da Capo al Coda jumps to the Coda after To Coda" (E32); "D.S. al Fine: the D.S. bar ends the first pass, then back to the Segno and stop at Fine" (E30); "Fine before any jump does not end playback (D-5)" (E31); "D.C. al Coda: To Coda is ignored on the first pass, taken after the jump" (E33); "D.S.S. jumps to the double segno" (E34) |
| `CheckLoopBounds` (SelfTestPlayback.cs) | "loop: bounds available", "loop: starts at bar 2 (2000 ms)", "loop: ends after bar 3 (6000 ms)", and the three selected-cell checks (E64, E65) |
| `TestMusicXmlGuitarPro8Encoding` (SelfTestImportContract.cs) | "GP8 encoding: segno and D.S. marks are written" (uses the direction names in section 2.3) |

The whole `--selftest` run must also pass (the old headless `--playtest` step was retired 25 Oct; the Playback self-tests cover it), since
the timeline, playhead and live-edit tests build on this order. Examples E2 to E6, E9 to E12, E19 to E28,
E35 to E63 and E66 to E68 should be added as new self-test checks alongside the rewrite.
