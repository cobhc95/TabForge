# TabForge.Core/Score

The score layout without WPF: bar widths, system breaks, the per-bar staff layouts, the marks derived from notes and the geometry shared with drawing. The renderers draw through `IDrawContext`; the editor's WPF end is `Platform/Wpf/WpfScoreRenderSurface.cs`. The drawing reaches this layout through `IScoreLayoutHost`, `IScoreLayoutSettings` and `IScoreTextWidths`.

## Files
| File | Purpose |
| --- | --- |
| `IScoreRenderHost.cs` | Page metrics, appearance and cursor state shared by drawing and the playback overlay |
| `ScoreAppearance.cs` | Paper, ink and highlight colours (`UiColor`), spacing and labels; `ScoreAppearanceChange` says what a setting needs |
| `ScoreClefKey.cs` | Clef shapes, clef changes and key-signature glyph counts |
| `ScoreLayoutEngine.cs` | `IScoreLayoutHost`: what the layout engine reads from its editor |
| `ScoreLayoutEngine.Incremental.cs` | Relayout that reuses the layout of bars outside a known edit range |
| `ScoreLayoutEngine.MeasureWidths.cs` | Natural bar widths, with the air needed around chords, text and fret labels |
| `ScoreLayoutEngine.Signatures.cs` | Tempo text at the song start and at each tempo change |
| `ScoreLayoutIncrementalState.cs` | Data reused by a stable-range relayout and the current bar's extent scan |
| `ScoreMarkText.cs` | Text and numbers derived from notes: technique and harmonic labels, bend and whammy amounts |
| `ScorePassages.cs` | Passages derived once per score: palm-mute and fade spans, dynamic markings |
| `StaffDrumStems.cs` | Stems and beams of a percussion staff: hands up, feet down, each beamed on its own |
| `StaffNotationArcs.cs` | Builds the ties, hammer-on/pull-off slurs and slide strokes of a measure layout |
| `StaffNotationGeometry.cs` | Glyph metrics and pure geometry shared by staff layout and drawing: staff constants, durations, ledger lines |
| `StaffNotationLayoutBuilder.cs` | The rhythm and geometry of one measure on the staff: beats, beam groups, stems, accidentals and the mark skeleton |
| `StaffNotationModels.cs` | Shared rhythmic and geometric layout for one measure |
| `MarkSkyline.cs` | Layout-time row stacking for the markings around one staff |
| `MeasureWarp.cs` | The duration-based spacing of one bar |
| `ScorePageLayout.cs` | Resolved horizontal geometry of the engraved measures and systems |
| `ScoreLayoutPorts.cs` | `ScoreTextArea`, `IScoreLayoutSettings`, `IScoreTextWidths`: what the layout reads from the app |
| `ScoreText.cs` | Score text styles and draw helpers over `IDrawContext.Text` (the adapter caches shaped text); `ScoreTextWidths` gives the layout its text widths |
| `ScorePassageSpans.cs` | The palm-mute and fade spans the layout derives and the drawing reads |
| `CursorPositions.cs` | The only cells the edit cursor may sit on: real beat starts (note, rest or annotated beat) |
| `ScorePlayedChip.cs` | The glow around the fret number of the note sounding now |
| `ScoreRenderer.cs` | Draws one system: staff and tab frames, every bar, the passages and the cursor |
| `ScoreRenderer.Bars.cs` | Bar furniture: clef, signatures, tempo, section and volta labels, directions, simile bars |
| `ScoreRenderer.Cursor.cs` | Cursor and highlight geometry (not the cursor drawing) |
| `ScoreRenderer.Header.cs` | The page header: title, artist and the words-by and music-by lines |
| `ScoreCursorBox.cs` | The solid edit-cursor boxes: yellow on the tab lane, grey behind a staff notehead; hidden while playing |
| `ScoreRenderer.Marks.cs` | Bend arrows with amounts, and the other marks drawn on notes |
| `ScoreRenderer.Passages.cs` | Drawing of the two passage kinds that span bars |
| `ScoreRenderer.TabLinks.cs` | Tab links between notes |
| `ScorePaths.cs` | The note flag path |
| `StaffNotationDrawing.cs` | Draws glyphs and the already-resolved geometry of a measure layout; no rhythmic decisions happen here |
| `StaffNotationRenderer.cs` | Entry point of staff notation: resolves a measure into beats, beams, pitch geometry and arcs, and draws them |
| `WritingDuration.cs` | Which duration a beat under the cursor stands for: the writing duration or the beat's own length |
| `PlaybackGlowIntensity.cs` | Shared opacity scaling and presets for playback duration shading |
| `TabSlideNotation.cs` | Resolves compact TAB slide marks against the next note on the same string |
| `ScoreCursorStops.cs` | The per-bar cursor stops of one track, from `CursorPositions`, for `ScoreCursorNavigator` (the WPF editor and the preview editor). |
| `ScorePreviewEditor.cs` | The Avalonia preview score's editing: click to cursor, drag and Shift+arrow selection, hover, editor keys and the catalogue's note, effect, duration, undo and redo keys through `ScoreEditCommands` and `DocumentEdits` (`ScorePreviewEditorTests`). |
| `EditorSelectionState.cs` | The score selection and hover as plain state: anchor, end, beat snapping and the Shift+arrow step of the end (`TestEditorSelectionState`, `ScorePreviewEditorTests`). |
| `ScoreHitRule.cs` | The editor's click hit rule (bar, beat, string under a page point), shared by `EditorInputController.HitTest` and the probe. |
| `Editing/ScoreEditCommands*.cs` | The score editor's note commands (durations, ties, techniques, marks, rests, fret entry) against `IScoreEditContext` (namespace `TabForge.ScoreEditing`, not `TabForge.Score`, which would shadow the alphaTab `Score` type); every change through `RunEdit`. |
| `Editing/MeasureProperties.cs` | Bar flag edits shared by the palette, menus and timeline: toggles, swing cycle, simile, line breaks, tempo change, directions (`PaletteToolsTests`, `TestEditControllers`). |
| `Editing/PaletteBarCommands.cs` | The palette tools that flip one bar property (free time, double barline, swing, simile, system breaks, gray voice) as one undo step with their status lines (`PaletteToolsTests`). |
| `Editing/BarSignatureCommands.cs` | Applies a time or key signature answer to the selected bars or from the cursor bar and words where it reached (`PaletteToolsTests`, `TestBarCommandFlow`). |
| `Editing/PaletteToolStates.cs` | Whether a Structure, Rhythm or Layout tool is lit and enabled at the cursor, for both palettes (`PaletteToolsTests`). |
| `Editing/ScoreEditCommands.PaletteTools.cs` | The note-level palette tools: tie, octave marking, beaming, stem direction (`PaletteToolsTests`). |
| `Editing/ScoreBarCommands.cs` | The bar and note-value menu commands (Bar.Insert, Bar.Delete, Bar.Duplicate, Note.Whole to Note.SixtyFourth, repeat) as document edits from the cursor bar and selected bars (`ScorePreviewEditorTests`, `TestBarStructureEditsPinned`). |
| `ScorePreviewEditor.ContextHit.cs` | What a right-click on the page lands on (beat, note, inside the selection) and `SelectForEdit`, the cursor move a note or beat menu makes first |
| `ScoreCursorSight.cs` | The scroll offsets that keep the cursor's system (or one-line bar) in view after a cursor move; pure |
| `ScoreInstrumentScale.cs` | The instrument pane's text scale (zoom times spacing) and the drawing scale it gives (fitted scale times text scale, in range) |
| `ScorePdfPagination.cs` | The A4 page geometry of a score PDF and the split of a score into pages on system boundaries |
| `ScorePdfWriter.cs` | The PDF 1.4 bytes of a score: one Flate-compressed RGB image per page, the page tree and the cross-reference table |
| `ScoreGeometryProbe.cs` | A headless score host (zoom 1, dark paper, no cursor) that lays out a track and draws its first bars through a `GeometryRecorder`, for the WPF vs Avalonia geometry proof (`--score-geometry`, `ScoreGeometryProofTests`) |


## Tests
`TestNotationLayout`, `TestTabEditorLayoutMatrix`.
