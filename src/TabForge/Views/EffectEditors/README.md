# Views/EffectEditors

Note-effect editor dialogs: Bend, Tremolo bar, Trill, Grace note and Harmonic. Each is a themed dialog with presets, OK / Clean / Cancel, Enter and Esc, one undo step.

## How to change me
1. Entry files: `EffectEditorFlow` (opens an editor for the selection and runs the edit), `ThemedEditorDialog` (frame), `CurveEffectDialog` + `EffectCurveEditor` + `EffectCurve` (curve effects), `EffectEdits` (model writes), `EffectPresetStore` (built-in and user presets).
2. Owner class: the flow for behaviour; never `MainWindow` (its part is `MainWindow.EffectEditors.cs`) or `TabEditorControl`.
3. Tests to run: `TestEffectCurveMath`, `TestEffectEditors`, `TestEffectEditorFiles`, `TestOrnamentEditors`, `TestOrnamentEditorFiles` (full-suite build, `--only`), then `--areas basic`.
4. Docs to update: this README, `CHANGELOG.md`, `TOOLS_AND_HOTKEYS.md` for a new command, the owner's test list.

## Key types
- `EffectCurve`: points 0..60 across the note, value in quarter-tones; end points fixed in time; snapping; pure.
- `EffectCurveEditor`: draws and edits one curve (drag, click to add, right-click or double-click to remove, arrow keys, Delete). Renders only when the curve changes.
- `ThemedEditorDialog`: frame with title bar, body slot and OK / Clean / Cancel (`Result`, `AnswerForTest`).
- `EffectPresetStore`: built-in lists per `EffectEditorKind` and user presets in `AppSettings.EffectPresets` (curve `Points` and/or named `Values`).
- `EffectEdits`: `ApplyBend` / `CleanBend`, `ApplyTremolo` / `CleanTremolo` on notes or beats.
- Value editors (trill, grace note, harmonic): `ValuesEffectDialog` (fields + presets in `EffectPresetEntry.Values`), `OrnamentEditors` (the fields per editor), `EffectFieldControls` (`NumberField`, `ChoiceField`), `OrnamentEdits` (model writes), `EffectEditorFlow.Ornaments.cs` (`OpenOrnament`).
- Selection and the edit pipeline: `src/TabForge/Views/Score/ScoreEditCommands.Effects.cs` (`EffectNotes`, `EffectCells`, `EditEffect` which runs `RunEdit`, i.e. `DocumentEdits.Run`).

## Add another editor in four steps (trill, grace note and harmonic follow it)
1. Add the kind to `EffectEditorKind`; list its built-in presets in `EffectPresetStore.BuiltIn` (non-curve editors keep numbers in `EffectPresetEntry.Values`).
2. Build the controls (number boxes, radio buttons, a pitch picker) in a small class like `CurveEffectDialog` and pass them as the body of `ThemedEditorDialog`; set `KeyboardNavigation.SetTabIndex` on them, below the footer's 100-102.
3. Write the result in `EffectEdits` (`ApplyX` / `CleanX`, return whether anything changed) and add a case to `EffectEditorFlow.Open`; it must go through `ScoreEditCommands.EditEffect`.
4. Wire the entry points (palette case, menu item, command id with no default key, `RunHotkey` case, `TOOLS_AND_HOTKEYS.md`) and add tests; see "Add a note-effect editor" in `docs/RECIPES.md`.
The model already stores what the three need: `TabNote.TrillTargetMidi` / `TrillDurationDenominator`, `IsGraceNote` / `GraceBeforeBeat` / `GraceDurationSlots` / `Dead` / `Velocity` / the `GraceBend` and slide techniques, `HarmonicFret` and the harmonic technique names.
