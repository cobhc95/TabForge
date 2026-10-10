# Views/Score

Parts of the score and tab editor (`TabEditorControl` in `Views/` is the host): page layout, drawing, mouse input and the edit commands. Does not own the song (Models) or the edit transaction (`DocumentEdits.Run`, reached through `IScoreEditHost`).

## Files
| File | Purpose |
| --- | --- |
| `AudioTrackPlaceholder.cs` | The centred "audio track, no notation" message while an audio track is selected |
| `CursorPositions.cs` | The only cells the edit cursor may sit on: real beat starts (note, rest or annotated beat) |
| `EditorAutomation.cs` | `IEditorDescribeHost`: what the editor describer reads (track, cursor, selection, layout) |
| `EditorInputController.cs` | Mouse input: hit testing, click, shift-click and drag selection, hover, context-menu requests |
| `EditorSelectionState.cs` | Selection and hover as plain state: anchor, end, and whether a range is being dragged |
| `IScoreEditHost.cs` | The one way an edit command changes the song: runs the edit on the displayed document |
| `IScoreRenderHost.cs` | Page metrics, appearance and cursor state shared by drawing and the playback overlay |
| `PlaybackOverlay.cs` | The overlay of notes sounding now, and what it reads from its editor |
| `ScoreAppearance.cs` | `ScoreAppearanceChange` (Layout or Repaint): what an appearance setting needs |
| `ScoreClefKey.cs` | Clef shapes, clef changes and key-signature glyph counts |
| `ScoreEditCommands.cs` | `IScoreEditContext`: what the edit commands read and write (song, cursor, entry state, hooks) |
| `ScoreEditCommands.Effects.cs` | What the note-effect dialogs act on (selection or cursor note) and how the change is applied |
| `ScoreEditCommands.Entry.cs` | Typing a fret: the writing duration on an empty slot or a placeholder rest |
| `ScoreEditCommands.Marks.cs` | Per-beat marks, ties, techniques, beat and bar operations, and their tool-state queries |
| `ScoreEditCommands.Rests.cs` | With the rest fill on, merges the touched bars' rests into the fewest standard rests |
| `ScoreEditCommands.SelectionEnd.cs` | Selection after Backspace / Delete, and where the cursor lands after Undo / Redo |
| `ScoreEditCommands.Ties.cs` | Per-string ties and beat ties at the cursor |
| `ScoreEditPreparation.cs` | Edit guard, optional rest-fill wrapping, and range cleanup when a mutation fails |
| `ScoreLayoutEngine.cs` | `IScoreLayoutHost`: what the layout engine reads from its editor |
| `ScoreLayoutEngine.Incremental.cs` | Relayout that reuses the layout of bars outside a known edit range |
| `ScoreLayoutEngine.MeasureWidths.cs` | Natural bar widths, with the air needed around chords, text and fret labels |
| `ScoreLayoutEngine.Signatures.cs` | Tempo text at the song start and at each tempo change |
| `ScoreLayoutIncrementalState.cs` | Data reused by a stable-range relayout and the current bar's extent scan |
| `ScoreMarkText.cs` | Text and numbers derived from notes: technique and harmonic labels, bend and whammy amounts |
| `ScorePassages.cs` | Passages derived once per score: palm-mute and fade spans, dynamic markings |
| `ScorePlayedChip.cs` | The glow around the fret number of the note sounding now |
| `ScoreRenderer.cs` | Draws one system: staff and tab frames, every bar, the passages and the cursor |
| `ScoreRenderer.Bars.cs` | Bar furniture: clef, signatures, tempo, section and volta labels, directions, simile bars |
| `ScoreRenderer.Cursor.cs` | Cursor and highlight geometry (not the cursor drawing) |
| `ScoreRenderer.Marks.cs` | Bend arrows with amounts, and the other marks drawn on notes |
| `ScoreRenderer.Passages.cs` | Drawing of the two passage kinds that span bars |
| `ScoreRenderer.TabLinks.cs` | Tab links between notes |
| `ScoreText.cs` | `ScoreTextArea`: the areas of a score that carry their own text style |
| `StaffNotationArcs.cs` | Builds the ties, hammer-on/pull-off slurs and slide strokes of a measure layout |
| `StaffNotationDrawing.cs` | Draws glyphs and the already-resolved geometry of a measure layout; no rhythmic decisions happen here |
| `StaffNotationGeometry.cs` | Glyph metrics and pure geometry shared by staff layout and drawing: staff constants, durations, ledger lines |
| `StaffNotationLayoutBuilder.cs` | The rhythm and geometry of one measure on the staff: beats, beam groups, stems, accidentals and the mark skeleton |
| `StaffNotationModels.cs` | Shared rhythmic and geometric layout for one measure |
| `StaffNotationRenderer.cs` | Entry point of staff notation: resolves a measure into beats, beams, pitch geometry and arcs, and draws them |
| `SystemDrawingCache.cs` | A frozen drawing per score system, replayed instead of drawing the system again |
| `WritingDuration.cs` | Which duration a beat under the cursor stands for: the writing duration or the beat's own length |

## Pathway
`START_HERE.md`, Golden pathways: **Edit the song** (every change goes through `DocumentEdits.Run`) and **Commands** (bindings through `HotkeyCatalog`).

## Tests
`docs/feature-map/editing-and-notation.md`.
