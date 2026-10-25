# Copy / paste design: bar paste prompt, beat-range paste, cross-instrument paste

> **Status 2026-10-22 (grep check, not a full review):** partly built. Bar paste with overwrite and insert kinds is in `Source/src/TabForge.Avalonia/MainWindowTimelineBars.cs`, and range commands are in `Source/src/TabForge.Avalonia/MainWindowRangeCommands.cs`. The text below is the original 2026-09-30 design and was not re-checked line by line.

Status: design only (2026-09-30). No product code changed.
Owner request: (1) pasting whole bars onto bars with content must ask *Overwrite / Insert before / Insert after*
instead of silently overwriting; (2) Guitar-Pro-style copy/paste of part of a bar (a few beats or notes), pasted at
the cursor, also across instruments (guitar to bass, other tunings, other string counts).

---

## 1. .gp5 behaviour (functional description, in our own words)

Sources: the .gp5 English manual (Help\GuitarPro_EN.pdf: "Manage Bars", "Enter Notes / Multiple Selection",
"Use Cut/Copy/Paste", "Use Wizards", keyboard reference) plus general knowledge of .gp5. Items marked
**(manual)** are stated by the manual; items marked **(believed)** are not in the manual and were **not verified**
in .gp5 in this pass (see chunk C0 for an optional hidden-desktop check).

### 1.1 Selection units
- **Bars are global.** Every bar exists on every track with the same time signature, repeats and key. Inserting or
  cutting bars always acts on all tracks. (manual)
- **Beat selection inside one bar.** Dragging or Shift+arrows inside a single bar selects beats; copy/cut/paste then
  act on those beats only, and only on the **current voice**. (manual)
- **Bar selection.** A selection that crosses a bar line, or one made with Ctrl held, is treated as whole bars; whole-bar
  actions apply to **both voices**. (manual) Anything that is not an in-bar beat selection is a bar operation. (manual)
- **Tracks.** Bar copy can take **the active track only or all tracks**. There is no rectangular beat selection spanning
  several tracks in .gp5. (manual for bar copy; believed for the absence of multi-track beat selection)

### 1.2 Copy
- Bar copy opens a small window: bar range (from/to, prefilled from the selection or cursor bar) and scope
  (all tracks / active track). (manual)
- Beat copy takes the selected beats of the current voice (durations, notes, per-note and per-beat effects, chord
  diagram and beat text). (believed)
- The clipboard survives between two .gp5 windows/sessions, so tracks can be moved between files. (manual)
- "C" repeats the selected beat at the end of the bar (TabForge already has this as `Note.RepeatBeat`). (manual)

### 1.3 Paste
- Bar paste opens a window with **insert vs replace** and a **number of repetitions** (handy to fill a drum track
  from one bar). The paste follows the copy scope (single track or all tracks). (manual)
- Single-track bar paste requires the source and destination tracks to have **the same number of strings**. (manual)
  Positions are kept as string/fret, so a different tuning plays different pitches. (believed)
- Bar insert pushes later bars right (on all tracks, because bars are global); replace overwrites bar contents and
  adds bars at the end of the song if needed. (believed for the end-of-song growth)
- **Beat paste** puts the copied beats into the current bar at the cursor, pushing the rest of that bar to the
  right; it does **not** reflow into following bars. The bar then shows as over-full (red) until the user fixes it or
  runs the **Bar Arranger** / **Complete-Reduce bars with rests** wizards. (believed; the red over-full bar display
  and the two wizards are manual)
- Inserting or deleting one bar on one track only is done by copying "this bar to the end" and pasting it one bar
  later (or earlier), then clearing the leftover bar. (manual)

### 1.4 Instruments, drums, effects, voices, chords, lyrics
- No pitch-based re-fretting on paste: string/fret positions are copied as they are (hence the equal-string-count
  rule). Changing a track's string count separately offers to transpose its notes (manual); MIDI import uses an
  automatic fingering routine (manual) - neither is applied to paste. (believed for paste)
- Drum tracks are 6-"string" tracks whose fret numbers are drum sounds, so paste between drums and a 6-string guitar is
  allowed but meaningless musically. (believed)
- Effects travel with notes/beats; mix-table changes and chord diagrams are beat data and travel with beats.
  (believed)
- Two voices; beat operations act on the current voice, bar operations on both. (manual)
- **Lyrics are one text block per track**, not beat data, so copy/paste of bars/beats does not carry lyrics.
  (manual for the text-block model)

---

## 2. TabForge today (audit)

| Path | Where | What it does |
|---|---|---|
| Score Ctrl+C | `MainWindow.View.cs` `Copy_Click` -> `TabEditorControl.Selection.cs` `CopySelection()` | Serialises a whole `SongProject` (1 track) as JSON **text** on the Windows clipboard. With no selection: the cursor beat. With a selection: every bar from start to end, with cells **outside** the range replaced by empty cells (so the clip keeps the source cell offsets). Active voice only. No source tuning, time signature or bar properties. |
| Score Ctrl+V | `Paste_Click` -> `PasteSelection(string)` (lines 168-194) | **The silent overwrite.** `destCells[c] = CloneCell(srcCells[c])` for every cell of each clip bar, starting at `SelectedMeasure`. |
| Score Ctrl+X | `Cut_Click` | Copies the selection, then `DeleteBeats()` removes **only the beat at the cursor** (not the selection). |
| Timeline bar menu | `ArrangementController.CopyBar/PasteBar` (in-memory `_barClipboard` / `_allTracksBarClipboard`) | "Paste bar into this track / all tracks" **replaces** the whole `MeasureModel` (including bar properties). |
| Timeline area menu | `MainWindow.Arrangement.cs` `_areaClipboard`, `BarRangeEditor.Capture/Insert` | All tracks; "Paste before area" **inserts** bars structurally (one undo transaction, bar map remapped). |
| Sections | `ArrangementController.SectionClipboard` | Section + marker copy/paste (insert). Out of scope; keep. |
| Repeat | `RepeatSelection_Click` (Ctrl+Shift+R), `Note.RepeatBeat` (C) | Bar repeat x N; previous-beat repeat. |
| Undo | editor `EditStarting` -> `CaptureUndo()`; timeline uses `_undo.BeginTransaction/Commit` | Paste is already one undo step. |
| Selection | `Models/SelectionModel.cs` (shared score/timeline, bar range + start/end cell, one track) | Mouse drag + Shift+arrows + Ctrl+A. Reuse as-is. |

Bugs found in `PasteSelection` besides the overwrite:
1. **Partial selections paste at their source offset, not at the cursor.** Copy cells 4-7 of a bar and paste at cell 0:
   cells 0-3 and 8-15 of the target are blanked and the notes land at 4-7.
2. **Single-beat paste only works at cell 0.** With no selection the clip has one cell at index 0; for
   `SelectedCell > 0` the `c < SelectedCell` skip drops it, yet the method returns true ("Pasted").
3. Cells beyond the target bar length are cut off silently (time-signature mismatch); stale `RhythmicPosition`s are kept.
4. **No instrument mapping**: `StringIndex`/`Fret`/`MidiValue` are copied verbatim. On a 4-string bass, notes on
   strings 5-6 point to strings that do not exist; on another tuning the fret no longer matches `MidiValue`.
   Guitar -> drums produces nonsense drum notes.
5. Two parallel clipboards (Windows text clipboard vs in-memory timeline clipboards) that do not see each other.
6. `TestEditorCopyPaste` only asserts that paste returns true.

Useful existing pieces: `TrackModel.PitchOf/FretOf`, `MidiClipToTab.StringNote` (lowest-fret free string),
`TrackPropertiesWindow.ApplyTuning` (keep-pitch re-fret), `DrumMaps.For(track, midi)`, `MusicTime.CellSlots/BarSlots`,
`BarRangeEditor.Insert/Remove` (+ bar map), `ProjectService.CloneMeasure`, `ProjectValidator`, `InputLimits`,
`EditingSettings.PreventBarOverflow`.

---

## 3. Target behaviour

### 3.1 Two clip kinds, decided at copy time
- **Bars clip**: the selection covers whole bars (start cell 0 and end = bar end), or it came from the timeline
  (bar/area/"copy bar"). Carries both voices and the bar properties.
- **Beats clip**: anything else, including a lone cursor beat and a selection that starts/ends mid-bar and crosses bar
  lines (TabForge extension: .gp5 limits beat copy to one bar; the owner asked for "a few beats" wherever they are).
  Active voice only (.gp5 rule). Bar lines inside the clip are **not** stored: the clip is a continuous run of events
  with offsets in 16th slots from the first selected beat's onset; its length runs to the end of the last selected beat.

### 3.2 Bar paste (Ctrl+V with a Bars clip)
Target range: bars `cursorBar .. cursorBar + n - 1` on the target track(s).
- **All target bars empty** (no notes, explicit rests or annotations in either voice) or past the end of the song:
  paste directly = Overwrite, appending bars if the song is too short. (Today's good behaviour, kept.)
- **Any target bar has content**: prompt

  > Paste 2 bars at bar 17 (bars 17-18 have notes)
  > [Overwrite]  [Insert before bar 17]  [Insert after bar 18]  [Cancel]
  > [ ] Don't ask again - always do this (change in Preferences > Editing > Paste)

  Default button: Insert before (non-destructive). Esc = Cancel. If the clip's meter differs from the target bars, add
  one line: "Clip is 3/4, bar 17 is 4/4 - Insert keeps the clip's meter."
- **Overwrite**: replaces the notes of both voices bar by bar. Target bars keep their own bar properties (time
  signature, key, repeats, tempo, section name, directions), because bars are global. Content longer than a target
  bar is dropped at the bar line and reported ("3 beats did not fit in bar 17 (4/4 vs 5/4)"); shorter content leaves
  implicit rests.
- **Insert before / after**: structural insert through `BarRangeEditor.Insert` at `cursorBar` or `lastTargetBar + 1`,
  on **all** tracks (other tracks get empty bars), so tracks stay aligned (.gp5 model). New bars take the clip's time
  signature and key; repeat signs, tempo changes, section names and directions are not copied (see open question 4).
  Markers/sections and the playback bar map are remapped exactly as `PasteAreaAt` does.
- Setting: `EditingSettings.PasteBarsMode` = `Ask` (default) | `Overwrite` | `InsertBefore` | `InsertAfter`, shown in
  Preferences > Editing > "When pasting bars onto bars with notes". "Don't ask again" writes it.

### 3.3 Beat paste (Ctrl+V with a Beats clip)
**Beats onto written notes (follows GP5, quiet runs l6 and l6b, sequences p01-p05).** Ctrl+V on a written beat asks nothing: the
copied beats are inserted before the cursor beat inside the same bar (`EditCommands.InsertInBar`). The bar may overfill (drawn
red); nothing moves into the next bar. The cursor then sits on the last pasted beat, so the next typed fret replaces it
(`1 2 3 4`, paste two beats on the 3: `1 2 1 9 3 4`). The Replace and Insert paragraphs below describe Paste Special, which
still chooses Replace or the song-wide Insert explicitly. Pasting onto empty spots or rests and pasting bars are unchanged.

Anchor = the cursor's absolute position P0 (bar start + cell onset) in the **active voice** of the active track.
Default mode **Replace** (the cursor is where the first pasted beat starts; see open question 1):
1. **Placement across bar lines.** For each event with offset o, target position = P0 + o. Walk the target bars
   using each bar's own `MusicTime.BarSlots` (the target meter wins; the source meter is irrelevant).
2. **Clearing (Replace).** In the target voice, over the span [P0, P0 + clipLength): every beat whose onset lies in the
   span is removed. A beat that starts before P0 and rings into the span is shortened to end at P0 (split into tied
   representable durations if needed). The gap after the span stays as implicit rests (empty cells), which the grid
   model already treats as rests.
3. **Splitting at bar lines.** An event that crosses a bar line is split: first part = slots left in the bar, the
   rest at the start of the next bar with every note `Tied = true`. Each part is decomposed into representable
   values (whole ... 64th, single/double dots) with ties between the pieces - new helper `MusicTime.Decompose(slots)`.
   Rests are split without ties.
4. **Tuplets.** A tuplet group is never split. If it would cross a bar line the paste is refused with a status message
   naming the bar ("A triplet group would cross the bar line into bar 18"). Nothing changes.
5. **Ties at the edges.** If the first pasted note is `Tied` but the target's previous note on that string/pitch
   differs, drop the tie. A tie from a note after the span into the pasted material is re-checked the same way.
6. **End of song.** If the paste runs past the last bar, bars are appended to all tracks (target meter = last bar's).
7. **Over-full bars never occur** (unlike .gp5): placement always respects bar lengths.

Paste Special adds **Insert**: everything in the target voice of the active track from P0 onwards is shifted right by
clipLength (re-flowed through bar lines with the same split/tie rules), then the clip is placed in the gap. Only the
active track moves, so it drifts against other tracks by clipLength; the dialog says so. Bars are appended if needed.

### 3.4 Cross-instrument mapping (every paste where source and target instruments differ)
`NoteMapper` decides per paste, and tells the user what it did in the status bar.
1. **Same instrument layout** (same string count, tuning and capo; or drums -> drums): keep string/fret as they are.
   Exact, and the same fingering. Drums -> drums: keep `MidiValue`; recompute `StringIndex` from
   `DrumMaps.For(targetTrack, midi)` because the drum map preset can differ.
2. **Pitched -> pitched, different layout: keep pitch, re-fret.** Sounding pitch = `MidiValue` (already
   tuning + capo + fret). Per beat (all notes of a chord together):
   - Candidate positions per note = every target string with `0 <= FretOf(s, pitch) <= NumberOfFrets`.
   - Choose one distinct string per note by a small search (at most 8 strings x 8 notes) that minimises:
     distance from the **hand anchor** (mean fretted position of the previous pasted beat; for the first beat, the
     source beat's mean fret, clamped to the target range), the chord's **fret span** (penalty above 4 frets),
     frets above 12 (small penalty), and a bonus for keeping the source string index when that string's pitch matches.
     Open strings cost nothing.
   - **Octave**: Paste Special "Octave shift" (-2..+2, default 0) is applied first. With "Fit out-of-range notes by
     octave" (default on) a note outside the target range is moved by whole octaves into range.
   - **Notes that still do not fit** (range, or a chord with more notes than strings): left out. For chords, keep the
     highest and lowest notes and drop inner notes first. The status bar reports the count
     ("Pasted 12 beats on Bass; 3 notes out of range were left out").
   - Paste Special also offers **"Keep string and fret (classic style)"** for users who want .gp5 behaviour; notes on
     strings the target lacks are left out and `MidiValue` is recomputed with `PitchOf`.
3. **Pitched <-> drums**: refused with a clear message ("Can't paste guitar notes into a drum track"). See open
   question 9 for a rhythm-only option.
4. **Techniques after re-fretting**
   - Kept as they are: bends (relative quarter-tones), vibrato, palm mute, let ring, dead/ghost notes, accents,
     staccato/tenuto, hammer/pull, tapping, tremolo picking, grace notes (the grace note is re-fretted like any note),
     `SlideTargetMidi` and `TrillTargetMidi` (pitches, still valid).
   - **Harmonics** (`HarmonicFret`): kept only if the chosen string has the same open pitch as the source string,
     otherwise the harmonic mark is removed and the note plays its sounding pitch (counted in the report).
   - **Left-hand fingering**: removed when the fret changed. Right-hand fingering kept.
   - A **slide** whose target lands on another string or out of range: the slide mark is removed.
5. **Beat data** (both clip kinds): chord names and beat text are pasted. Mix-table changes (`TabCell.Mix`) are
   pasted only into the same track (they carry program/volume/pan for their source track). Lyrics: pasted only into
   the same track by default (TabForge stores lyrics per beat; .gp5 does not paste them at all) - see open question 5.

### 3.5 Multi-track
- **Bars clips can hold several tracks**: timeline area copy and "Copy bar (all tracks)" produce one. The score
  editor copies the active track only; the .gp5 "active track / all tracks" choice lives on the timeline, which already
  has "this track / all tracks". Keep that split.
- **Pasting a multi-track clip**: if the clip came from the same song and has the same track count, track i -> track i.
  Otherwise clip track k -> target track (activeTrack + k), in the track list order; clip tracks beyond the last track
  are left out and reported. Each pair goes through `NoteMapper`. The Overwrite/Insert prompt applies once to the whole
  paste (Insert is structural anyway).
- **Multi-track beat selection** (rectangles over several tracks): .gp5 does not have it. Not in v1 (open question 6).

### 3.6 Paste Special dialog (Ctrl+Shift+V)
Fields (last choices remembered in settings):
- Mode: Bars clip -> Overwrite / Insert before / Insert after; Beats clip -> Replace / Insert (shift this track).
- Repetitions: 1-64 (.gp5). Copies are laid end to end; for Beats clips with Replace the whole run is one span.
- Voice: active voice / voice 1 / voice 2 (Beats clips; Bars clips always paste both voices).
- Mapping: Keep pitch (re-fret) / Keep string and fret; Octave shift -2..+2; "Fit out-of-range notes by octave".
- Include: chord names and text; lyrics; mix-table changes. (Bars insert only: "Copy time signature and key" on by default.)
- Preview line: "Pastes 6 beats at bar 17 beat 3 on Bass (4 strings); 2 notes will be re-fretted, 1 left out."
  Computed by running the paste on a clone (cheap: only the affected bars) - no per-frame work.

### 3.7 Cut
- Beats clip: copy, then take the selected beats out of the active voice; the later beats of the bar move up and the
  rest fill closes the bar (as GP5, `EditCommands.CutClear`).
- Bars clip from the score, or a one-track selection from a bar's start to its end (`EditCommands.CutTakesBars`): copy,
  then the bars are removed in a one-track song (as GP5) and emptied on this track when the song has more tracks
  (`SectionEditFlow.CutArea`).

### 3.8 Clipboard format
`ScoreClip` (new, `Services/ScoreClip.cs`), JSON via System.Text.Json:
```
{ "format": "TabForge.ScoreClip", "version": 1, "kind": "Bars" | "Beats",
  "sourceSongId": "...", "sourceTrackCount": 5,
  "tracks": [ { "sourceTrackId", "kind", "name", "stringTunings", "capo", "numberOfFrets", "drumMapPreset",
                "bars":   [ MeasureModel ... ]                                 // Bars clips (both voices, bar properties)
                "events": [ { "offsetSlots": 0.0, "cell": TabCell } ... ],     // Beats clips (active voice)
                "lengthSlots": 6.0 } ] }
```
- Windows clipboard: set both a private format `TabForge.ScoreClip.v1` (UTF-8 JSON) and plain text (same JSON, so
  copy between two TabForge windows keeps working through text-only paths). Paste reads the private format first,
  then text. The legacy text format (a raw `SongProject` snapshot) is still accepted and converted to a Bars/Beats
  clip once.
- The clipboard is **untrusted input**: size cap (e.g. 16 MiB, via `InputLimits`), bounded counts (bars <= 4096,
  events <= 65536, notes per beat <= string limit), every `TabCell`/`TabNote` range-checked with the existing
  `ProjectValidator` rules; anything invalid -> "The clipboard does not contain TabForge notes", no change.
- One in-process `ClipboardService.Current` cache avoids re-parsing on every menu open (`CanPaste`).
- Timeline "Copy bar", "Copy area" and the score share this one clipboard (the section clipboard stays separate).

### 3.9 Undo
One paste = one undo step: `_undo.BeginTransaction(project)` before, `Commit` after (the pattern of `PasteAreaAt`),
including structural inserts, appended bars and repetitions. A refused or empty paste cancels the transaction.
Structural pastes call `RememberPlaybackBarMapping` and `FinishSectionStructureEdit` like the area paste.

### 3.10 Hotkeys (bindable, in presets, in TOOLS_AND_HOTKEYS.md)
| Id | Name | Default | Notes |
|---|---|---|---|
| `Edit.Copy` | Copy | Ctrl+C | rename "Copy beat" -> "Copy selection" |
| `Edit.Cut` | Cut | Ctrl+X | "Cut selection" |
| `Edit.Paste` | Paste | Ctrl+V | prompt rules above |
| `Edit.PasteSpecial` | Paste special... | Ctrl+Shift+V | new; free in the catalogue today |
| `Edit.PasteOverwrite` | Paste (overwrite) | unbound | new; skips the prompt |
| `Edit.PasteInsertBefore` | Paste (insert before) | unbound | new |
| `Edit.PasteInsertAfter` | Paste (insert after) | unbound | new |
.gp5 preset: same as defaults. Alternative preset: same Ctrl+C/X/V; paste special unbound. Timeline focus keeps the
existing `Clip.*` routing for audio clips. Add all to Edit menu and the score context menu.

---

## 4. Files and classes

| New / changed | Responsibility |
|---|---|
| `Services/ScoreClip.cs` (new) | Clip model, (de)serialisation, validation, legacy conversion. |
| `Services/ClipboardService.cs` (new) | Capture from a track/range (`CaptureBars`, `CaptureBeats`), Windows clipboard read/write, in-process cache. No WPF except the clipboard adapter (injectable for self-tests). |
| `Services/BarGrid.cs` (new) or `MusicTime` additions | Absolute slot <-> (bar, cell, `RhythmicPosition`); `Decompose(slots)`; `ClearSpan(track, voice, from, to)`; `ShiftFrom(track, voice, from, by)`; append bars. Extract the onset logic now private in `TabEditorControl.Editing.cs` (`BeatStart`, `AbsoluteCellStart`). |
| `Services/NoteMapper.cs` (new) | Section 3.4. Pure, deterministic, returns a `MapReport`. Reuses `PitchOf/FretOf`, `DrumMaps.For`. |
| `Services/EditCommands.cs` | `PasteBeats(project, trackIndex, voice, anchor, clip, options)`, `PasteBars(...)`, `CutSpan(...)`, returning `PasteResult` (changed bar range, bar map for structural inserts, report). Model only. |
| `Views/TabEditorControl.Selection.cs` | `CopySelection`/`PasteSelection` become thin: expose the selection range and anchor; the old bodies go away. |
| `MainWindow.View.cs` | Copy/Cut/Paste/PasteSpecial handlers: build clip, prompt, undo transaction, call EditCommands, refresh, status text. |
| `Views/PasteBarsPrompt` (in `GpDialogs.cs`) + `Views/PasteSpecialWindow.cs` (new) | Dialogs, themed like other GpDialogs. |
| `Controllers/ArrangementController.cs`, `MainWindow.Arrangement.cs` | Timeline "Copy/Paste bar", "Copy/Paste area" go through `ClipboardService` and `EditCommands.PasteBars`; remove `_barClipboard`, `_allTracksBarClipboard`, `_areaClipboard`. |
| `Services/AppSettings.cs` (`EditingSettings.PasteBarsMode`, `PasteSpecial` defaults), `Services/SettingsCatalog.cs` | Preference entries; migration default `Ask`. |
| `Services/HotkeyCatalog.cs`, `MainWindow.Settings.cs` dispatch, `TOOLS_AND_HOTKEYS.md` | Hotkeys and docs. |
| `SelfTestClipboard.cs` (new) | Tests (section 6). |

---

## 5. Work chunks (about 30-40 min each)

| Chunk | Content | Depends on | Suggested owner |
|---|---|---|---|
| **C0** (optional) | Verify the "(believed)" .gp5 items with the hidden-desktop kit (`work/archive/audit3/technique/hidden-capture`, WM_COMMAND menu ids, dialog capture; close .gp5 with WM_CLOSE). Update section 1. | - | Sage |
| **C1** | `ScoreClip` + `ClipboardService` capture/serialise/validate/legacy import, clipboard adapter; unit self-tests. | - | Remy |
| **C2** | `BarGrid`: slot mapping, `Decompose`, `ClearSpan`, `ShiftFrom`, bar-line split with ties, tuplet-straddle detection, append bars; self-tests. | - | Remy / Atlas |
| **C3** | `NoteMapper`: passthrough, keep-pitch re-fret with hand-anchor search, octave fit, drums, technique pruning, report; self-tests (guitar->bass, 6->7 strings, drop D, capo, drums, chord > strings). | - | Atlas |
| **C4** | `EditCommands.PasteBeats/PasteBars/CutSpan` + repetitions + multi-track mapping + `PasteResult`; self-tests on a real project. | C1, C2, C3 | Atlas |
| **C5** | Score Copy/Cut/Paste wiring, undo transaction, bar-paste prompt with "don't ask again", `PasteBarsMode` setting + Preferences entry; status messages. | C4 (can start on the C4 signatures) | Iris |
| **C6** | Paste Special window, `Edit.PasteSpecial` + three direct-mode hotkeys, presets, menus, context menu, TOOLS_AND_HOTKEYS.md. | C4 signatures | Iris + Sage (docs) |
| **C7** | Timeline bar/area clipboard moved onto `ClipboardService`/`PasteBars`; remove old clipboards; keep section clipboard. | C1, C4 | Iris |
| **C8** | End-to-end self-tests, REBUILD, in-app inspection on Blinded (copy beats guitar->bass, bar prompt, undo), CHANGELOG. | all | manager |

Parallel plan: C0, C1, C2, C3 together -> C4 -> C5, C6, C7 together -> C8. Fix the C4 API (method signatures and
`PasteOptions`/`PasteResult` records) at the start of C4 and hand it to C5-C7 so they are not blocked on its body.

---

## 6. Self-test plan (`SelfTestClipboard.cs`, run by `--selftest`)
- Clip round trip: capture -> JSON -> restore equals; legacy `SongProject` text converts; oversize/garbage/invalid
  string index rejected without changes.
- Regression of today's bugs: single beat pastes at cell 5 (not only 0); a mid-bar selection pastes at the cursor, not
  at its source offset; nothing outside the pasted span changes.
- Replace across a bar line: 3 quarter notes pasted at beat 4 of a 4/4 bar -> 1 in bar N, 2 in bar N+1, correct
  onsets; a half note pasted at beat 4 becomes quarter + tied quarter.
- Time signatures: a Beats clip from 4/4 into 3/4 bars flows by the target meter.
- Tuplet group crossing a bar line -> refused, project unchanged (compare snapshots).
- Insert (shift): content after the cursor moves by clip length through bar lines; bars appended at the end.
- Bar paste: empty target -> no prompt path; content -> each mode (Overwrite keeps target bar properties; Insert
  before/after inserts on all tracks and remaps markers); repetitions x4; `PasteBarsMode` honoured.
- Mapping: E standard guitar -> 4-string bass (in-range notes keep pitch, out-of-range folded or reported);
  6 -> 7 strings; drop D; capo 2; chord with 6 notes -> 4 strings keeps top and bottom; "keep string and fret" mode;
  guitar -> drums refused; drums -> drums keeps `MidiValue`, restrings by drum map; harmonic/left-hand-finger pruning.
- Undo: each paste (including structural insert + repetitions) is exactly one undo step and restores byte-identical
  project JSON.
- Multi-track: all-tracks clip into same song maps i -> i; into a song with fewer tracks reports the dropped ones.
- Hotkeys: new ids exist in the catalogue, presets resolve, no duplicate default gesture.

---

## 7. Risks
- **Grid model**: cells are 16th slots with `RhythmicPosition` for tuplets. Placement at fractional offsets must reuse
  one onset rule, or pasted tuplets will drift. Mitigation: extract the editor's onset code into `BarGrid` first (C2).
- **Voice 2** grids are independent and may be empty (`CellsForVoice(1, create: true)`).
- **Structural inserts** touch markers, sections, loop/skip ranges and the playback bar map; follow `PasteAreaAt`
  exactly.
- **Untrusted clipboard** text from other apps: strict size and validation limits (section 3.8).
- **Undo snapshot cost** on large pastes with many repetitions: cap repetitions (64) and total inserted bars.
- **Focus routing**: Ctrl+C/V are shared with `Clip.*` on the timeline; keep the existing focus-based dispatch.
- **Performance**: mapping and preview run once per action, never per frame or per layout pass.
- **.gp5 details marked "(believed)"** could be wrong; C0 checks them before we call anything ".gp5-like" in the UI.

## 8. Open questions for the owner
1. Beat paste default: **Replace** at the cursor (recommended, no over-full bars) or **Insert** (.gp5 pushes the rest of
   the bar and leaves it over-full)?
2. Guitar -> bass: keep the exact pitch (often high frets on bass) or automatically drop an octave when the target is a
   bass? (Recommended: keep pitch; Paste Special offers octave shift.)
3. Insert bars: always on all tracks (recommended, tracks stay aligned) or offer "this track only" (shifts later music
   of this track against the others)?
4. Bar properties on Insert: copy time signature and key only (recommended), or also repeats, tempo changes, section
   names and directions?
5. Lyrics: paste them only within the same track (recommended), never (.gp5), or always?
6. Multi-track beat selection (a rectangle across tracks), which .gp5 lacks - wanted in a later version?
7. Ctrl+X on whole bars in the score: empty them (recommended) or delete them (shifts all tracks)?
8. Does a bar that holds only rests count as "has content" for the prompt? (Recommended: no, only notes and
   annotations count.)
9. Guitar -> drums: refuse (recommended) or paste the rhythm onto one chosen drum sound?

---

## Owner decisions (2026-09-30), these override the recommendations above

**One combined paste dialog.**
- Whenever a paste needs confirmation, ONE dialog appears, containing only the questions that apply to this paste, and it sizes itself to them. With a single question it is small.
- Each question has its own "Remember my choice" checkbox. Ticked, the answer is stored and that question is not asked again. Unticked, it is asked every time.
- Every remembered answer can be changed, or reset to "Ask every time", in Preferences > Editing > "Copy and paste". Each entry is searchable in the settings search.
- The wording never mentions any other product.

**Questions the dialog can contain:**

| # | Question | Options |
|---|---|---|
| Q1 | Pasting beats onto existing notes | **Replace the notes at the cursor (recommended)** / Insert and push the following notes along |
| Q2 | Pasting between instruments of different range (e.g. guitar and bass) | **Keep the exact pitch** / Shift by an octave automatically |
| Q3 | Pasting whole bars onto bars that have notes | **Overwrite** / Insert before / Insert after |
| Q4 | The copied bars carry bar settings (time signature, key, tempo and similar) | **Copy the bar settings** / Keep the target's settings |
| Q5 | Pasting a pitched instrument onto a drum track (or the reverse) | **Paste the rhythm onto one drum sound** / Don't paste |

**Other rules:**
- Insert before/after adds the bars on all tracks, so the tracks stay aligned.
- Lyrics are not pasted.
- Beat selection across several tracks is left for later; beta.6 does one track.
- Cutting whole bars removes them in a one-track song and empties them on one track of several.
- A bar containing only rests does not count as "has notes", so no Q1/Q3 prompt.
