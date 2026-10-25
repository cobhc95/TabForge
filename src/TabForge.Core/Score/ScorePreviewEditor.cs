using TabForge.Controllers;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Presenters;
using TabForge.Services;
using TabForge.Shell;
using TabForge.ScoreEditing;

namespace TabForge.Views.Score;

// Owns: the preview score's editing surface: the cursor placed by click (the shared hit rule), moved by the editor key map through
//   ScoreCursorNavigator; the selection (Shift+arrows, click-drag) and hover in the probe's EditorSelectionState; the catalogue's default
//   note, effect, duration, undo and redo keys (HotkeyCatalog) run through ScoreEditCommands.TryRunNoteCommand; and the IScoreEditContext
//   those commands run against, with every edit, undo and redo going through DocumentEdits and a small preview document's undo stack.
// Does not own: drawing and hit maths (ScoreGeometryProbe, ScoreHitRule), the key rules (EditorKeyMap, HotkeyCatalog), the edit commands
//   (ScoreEditCommands), undo grouping (DocumentEdits.Run), the paste rules (PasteCommands) and the clipboard (ClipboardService), files or playback.
// Tests: TabForge.Avalonia.Tests.ScorePreviewEditorTests.
internal sealed partial class ScorePreviewEditor : IScoreEditContext, ISelectionEditor
{
    private readonly ScoreGeometryProbe _probe;
    private IEditableDocument _document;   // the preview's own document until ShowDocument gives it the tab's song
    private readonly ScoreEditCommands _commands;
    // Canonical gesture to command id (HotkeyCatalog.BuildMap of the host's settings); replaced when the host's settings change, UI thread only.
    private IReadOnlyDictionary<string, string> _hotkeys;
    // True once the host gave the map built from its settings; a bare test editor keeps the polarity fallback below.
    private bool _hotkeysFromSettings;

    /// <param name="hotkeys">The composed hotkey catalogue's map (HotkeyCatalog.BuildMap); the host composes the catalogue first.</param>
    internal ScorePreviewEditor(ScoreGeometryProbe probe, SongProject song, IReadOnlyDictionary<string, string> hotkeys)
    {
        _hotkeys = hotkeys;
        _probe = probe;
        _document = new PreviewDocument(song);
        _commands = new ScoreEditCommands(this);
        _probe.Selection.Changed += AnnounceSelection;
    }

    /// <summary>Replaces the catalogue map when Preferences change (the duration-key polarity and user rebinds).</summary>
    internal void SetHotkeys(IReadOnlyDictionary<string, string> hotkeys) { _hotkeys = hotkeys; _hotkeysFromSettings = true; }

    internal (int Measure, int Cell, int String)? Cursor => _probe.Cursor;

    private EditorSelectionState Sel => _probe.Selection;

    // Where the left button went down (page units); null once released. Drag selection starts only after the pointer leaves this spot by DragThreshold.
    private (double X, double Y)? _pressPoint;

    /// <summary>A left click: the cursor goes to the hit beat and string, any range is dropped. With <paramref name="extend"/> the range runs from the cursor to the hit beat instead.</summary>
    internal void Click(double x, double y, bool extend = false)
    {
        _pressPoint = (x, y);
        var hit = _probe.HitTest(x, y);
        if (extend && _probe.Cursor is var (measure, cell, _))
        {
            if (!Sel.Selecting) Sel.Begin(measure, cell);
            Sel.SetEnd(hit.Measure, hit.Cell);
            Sel.MarkDragged();
            _pressPoint = null;
            return;
        }
        Sel.Clear();
        _probe.Cursor = hit;
    }

    /// <summary>The left button went up: later moves select nothing until the next press.</summary>
    internal void Release() => _pressPoint = null;

    /// <summary>A drag with the left button held: once the pointer is past the drag threshold the range runs from the cursor's beat to the beat under it. False when nothing changed.</summary>
    internal bool DragTo(double x, double y)
    {
        if (_pressPoint is not var (pressX, pressY) || _probe.Cursor is not var (measure, cell, _)) return false;
        if (!Sel.Selecting && !TabDragRules.CrossedThreshold(x - pressX, y - pressY)) return false;
        var (m, c, _) = _probe.HitTest(x, y);
        if (Sel.Selecting && m == Sel.EndMeasure && c == Sel.EndCell) return false;
        if (!Sel.Selecting) { Sel.Begin(measure, cell); Sel.MarkDragged(); }
        Sel.SetEnd(m, c);
        return true;
    }

    /// <summary>The pointer moved without a button: the beat under it gets the hover outline. False when the hovered beat did not change.</summary>
    internal bool HoverAt(double x, double y)
    {
        var (m, c, _) = _probe.HitTest(x, y);
        if (m == Sel.HoverMeasure && c == Sel.HoverCell) return false;
        (Sel.HoverMeasure, Sel.HoverCell) = (m, c);
        return true;
    }

    /// <summary>The pointer left the score: no hover outline. False when there was none.</summary>
    internal bool HoverEnd() => Sel.ClearHover();

    private TrackModel TrackNow => _document.Project.Tracks[Math.Min(_probe.TrackIndex, _document.Project.Tracks.Count - 1)];   // the track the score shows
    private int ShownTrackIndex => Math.Max(0, Math.Min(_probe.TrackIndex, _document.Project.Tracks.Count - 1));   // the clamped track the score shows: every clipboard/cut/paste edit targets it
    private ScoreCursorStops Stops => new(TrackNow, VoiceCells, _probe.SlotsFor);

    /// <summary>Bar > Last bar: the last bar's last written beat (null when the track has no bars), as GP5.</summary>
    internal CursorTarget? LastWrittenBeat()
    {
        if (_document.Project.Tracks.Count == 0) return null;
        var stops = Stops;
        return stops.MeasureCount == 0 ? null : ScoreCursorNavigator.LastBar(stops);
    }

    /// <summary>
    /// Runs the key's editor command, else the catalogue command bound to <paramref name="keyName"/> (the toolkit's key name, e.g. "R",
    /// "OemPeriod"); false when the preview gives the key no meaning (the caller then skips the repaint).
    /// </summary>
    internal bool Key(EditorKey key, string keyName, bool ctrl, bool shift, bool alt)
    {
        if (_probe.Cursor is not var (measure, cell, stringIndex)) return false;
        var command = EditorKeyMap.Resolve(key, ctrl, shift, alt, AppendKeyState(measure, cell), out var digit);
        if (command == EditorCommand.None) return RunCatalogued(keyName, ctrl, shift, alt);
        if (command is EditorCommand.ExtendSelectionLeft or EditorCommand.ExtendSelectionRight) return ExtendSelection(command == EditorCommand.ExtendSelectionRight ? 1 : -1);
        if (command is not (EditorCommand.EnterDigit or EditorCommand.DeleteNote or EditorCommand.DeleteBeat
            or EditorCommand.LongerDuration or EditorCommand.ShorterDuration or EditorCommand.ToggleTriplet)) Sel.Clear();   // moving the cursor ends the range
        var stops = Stops;
        CursorTarget? target = command switch
        {
            EditorCommand.PreviousBeat => ScoreCursorNavigator.BackBeat(stops, measure, cell),
            EditorCommand.NextBeat => ScoreCursorNavigator.ForwardBeat(stops, measure, cell),
            EditorCommand.PreviousBar => ScoreCursorNavigator.Bar(stops, measure, cell, -1),
            EditorCommand.NextBar => ScoreCursorNavigator.Bar(stops, measure, cell, 1),
            EditorCommand.BarStart => new CursorTarget(measure, stops.Snap(measure, 0)),
            EditorCommand.BarEnd => new CursorTarget(measure, ScoreCursorNavigator.BarEnd(stops, measure, cell)),
            EditorCommand.FirstBar => new CursorTarget(0, stops.Snap(0, 0)),
            EditorCommand.LastBar => ScoreCursorNavigator.LastBar(stops),
            _ => null,
        };
        if (target is { } t)
        {
            _probe.Cursor = (t.Measure, t.Cell, stringIndex);
            if (command == EditorCommand.NextBeat) TakeNewSlotDuration(measure, cell, t);
            return true;
        }
        switch (command)
        {
            case EditorCommand.AppendBar: AppendBar(moveCursorToNewBar: true); return true;
            case EditorCommand.StringUp or EditorCommand.StringDown:
                _probe.Cursor = (measure, cell, ScoreCursorNavigator.WrapString(stringIndex, command == EditorCommand.StringUp ? -1 : 1, TrackNow.StringTunings.Count));
                return true;
            case EditorCommand.EnterDigit:
                Sel.Clear();   // typing a fret ends the selection (GP5)
                _commands.EnterFret(digit);
                return true;
            case EditorCommand.DeleteNote: _commands.DeleteNote(); return true;
            case EditorCommand.DeleteBeat: _commands.DeleteBeat(); return true;
            case EditorCommand.LongerDuration: _commands.Longer(); return true;
            case EditorCommand.ShorterDuration: _commands.Shorter(); return true;
            case EditorCommand.ToggleTriplet: _commands.ToggleTriplet(); return true;
            default:
                return false;
        }
    }

    // The append-key state at the cursor: a bar may be added while the song is below the bar limit, and the cursor's own place then decides
    // between the last-bar rule (Ctrl+Right) and the song-end rule (Right), as GP5.
    private EditorKeyState AppendKeyState(int measure, int cell)
    {
        var plusLengthens = Tools.PlusKeyLengthensNotes;
        if (_document.Project.Tracks.Count == 0 || BarRangeEditor.MaxMeasures(_document.Project) >= InputLimits.MaxMeasuresPerTrack)
            return new EditorKeyState(false, false, false, plusLengthens);
        var stops = Stops;
        var lastBar = stops.MeasureCount == 0 || measure >= stops.MeasureCount - 1;
        return new EditorKeyState(lastBar, ScoreCursorNavigator.AtSongEnd(stops, measure, cell), false, plusLengthens);
    }

    private bool RunCatalogued(string keyName, bool ctrl, bool shift, bool alt)
    {
        var mods = (ctrl ? HotkeyModifiers.Control : 0) | (shift ? HotkeyModifiers.Shift : 0) | (alt ? HotkeyModifiers.Alt : 0);
        if (!_hotkeys.TryGetValue(HotkeyCatalog.Canonical(HotkeyCatalog.Format(keyName, mods)), out var id)) return false;
        // A bare editor (the test harness) that never received a settings map still gets the + and - polarity right.
        if (!_hotkeysFromSettings && !ctrl && !alt && id is HotkeyCatalog.NoteLonger or HotkeyCatalog.NoteShorter)
        {
            var plus = keyName == "OemPlus" && !shift;
            if (plus) id = Tools.PlusKeyLengthensNotes ? HotkeyCatalog.NoteLonger : HotkeyCatalog.NoteShorter;
            else if (keyName == "OemMinus") id = Tools.PlusKeyLengthensNotes ? HotkeyCatalog.NoteShorter : HotkeyCatalog.NoteLonger;
        }
        return RunCommand(id);
    }

    /// <summary>Runs a catalogue command id (Edit.Undo, Edit.Redo or a note, beat and bar command) on the shown document; false when the id is not an editor command or changed nothing.</summary>
    /// <summary>The effect commands (trill, grace, harmonic editors) run on this editor's song and cursor.</summary>
    internal ScoreEditCommands Effects => _commands;

    internal bool RunCommand(string id)
    {
        if (ScoreBarCommands.Denominator(id) is { } denominator) { _commands.SetDuration(denominator); return true; }
        if (PaletteToolCatalog.EditorToolOfCommand(id) is { } tool) return RunPaletteTool(tool);
        return id switch
        {
            "Edit.Undo" => Undo(),
            "Edit.Redo" => Redo(),
            "Bar.Insert" or "Bar.Delete" or "Bar.Duplicate" => RunBarCommand(id),
            _ => _commands.TryRunNoteCommand(id),
        };
    }

    /// <summary>The active state of a palette tool at the cursor (true, false, or null when absent or mixed): the Tools pane shows it as selected.</summary>
    internal bool? ToolState(string toolId) => PaletteToolStates.Active(toolId, PaletteContext);

    /// <summary>Whether a palette tool can act at the cursor (the Structure, Rhythm and Layout panes disable the rest).</summary>
    internal bool ToolEnabled(string toolId) => PaletteToolStates.Enabled(toolId, PaletteContext);

    /// <summary>Edit.RepeatSelection with the count the host asked for: the selected bars (else the cursor bar) are repeated that many times after themselves. False when nothing changed.</summary>
    internal bool RepeatSelection(int times) => AfterBarEdit(ScoreBarCommands.Repeat(_document, SelectedMeasure, SelectedBars(), times), SelectedMeasure);

    /// <summary>Inserts an empty bar in front of <paramref name="bar"/> (the timeline bar menu's insert rows; the bar count is the position after the last bar). False when nothing changed.</summary>
    internal bool InsertBarAt(int bar) =>
        bar >= 0 && bar <= BarRangeEditor.MaxMeasures(_document.Project)
        && AfterBarEdit(BarStructureEdits.InsertBar(_document, bar, bar, moveMarkers: false, Tools.FillBarsWithRests).Changed, bar);

    /// <summary>The "Confirm before deleting a bar" setting; when it is on, Bar.Delete asks through <see cref="AskDeleteBar"/> first.</summary>
    internal Func<bool> ConfirmDeleteBarSetting { get; set; } = () => false;

    /// <summary>Asks the host whether to delete the bar at the given 0-based measure; the host answers by calling <see cref="DeleteBarConfirmed"/>.</summary>
    internal Action<int>? AskDeleteBar { get; set; }

    /// <summary>Shows a status line (the refused Bar.Delete); the host shows it in its notice row.</summary>
    internal Action<string>? Notice { get; set; }

    /// <summary>Runs Bar.Delete on <paramref name="measure"/> after the host confirmed it. False when nothing changed.</summary>
    internal bool DeleteBarConfirmed(int measure) => RunBarEdit("Bar.Delete", measure);

    private bool RunBarCommand(string id)
    {
        var bar = SelectedMeasure;
        if (id == "Bar.Delete")
        {
            if (ScoreBarCommands.DeleteRefusal(_document) is { } refusal)
            {
                Notice?.Invoke(refusal);
                return true;
            }
            if (ConfirmDeleteBarSetting() && AskDeleteBar is { } ask)
            {
                ask(bar);
                return true;
            }
        }
        return RunBarEdit(id, bar);
    }

    private bool RunBarEdit(string id, int bar) =>
        AfterBarEdit(ScoreBarCommands.TryRun(id, _document, ref bar, SelectedBars(), Tools.FillBarsWithRests), bar);

    private (int First, int Last)? SelectedBars() => Sel.HasSelection ? (SelectionRange().m1, SelectionRange().m2) : null;

    // A bar edit changes the number of bars: the layout is rebuilt for the new song state and the cursor goes to the bar the edit names.
    private bool AfterBarEdit(bool changed, int cursorBar)
    {
        if (!changed) return false;
        Sel.Clear();
        _probe.ReplaceSong(_document.Project);
        _probe.Cursor = (cursorBar, 0, SelectedString);
        return true;
    }

    /// <summary>
    /// Edits <paramref name="song"/> from now on: the shown tab's document, with its own undo stack. The cursor of the song shown before
    /// is kept on it and the new song's cursor is restored; the range and hover end.
    /// </summary>
    internal void ShowDocument(SongDocument song)
    {
        if (_document is SongDocument previous && _probe.Cursor is { } c) (previous.CursorBar, previous.CursorCell, previous.CursorString) = c;
        _document = song;
        Sel.Clear();
        Sel.ClearHover();
        _probe.ReplaceSong(song.Project);
        _probe.Cursor = (song.CursorBar, song.CursorCell, song.CursorString);
    }

    private bool ExtendSelection(int direction)
    {
        if (!Sel.ExtendEnd(TrackNow, VoiceCells, _probe.SlotsFor, direction, SelectedMeasure, SelectedCell)) return false;
        // The cursor follows the range's end going right and steps back a beat going left, as the score editor does.
        var target = direction > 0 ? new CursorTarget(Sel.EndMeasure, Sel.EndCell) : ScoreCursorNavigator.BackBeat(Stops, SelectedMeasure, SelectedCell);
        if (target is { } t) _probe.Cursor = (t.Measure, t.Cell, SelectedString);
        return true;
    }

    /// <summary>A copied timeline range as a Bars clip of the shown track (the track it was copied from if it is in the range, else its first tab track), for a paste at the cursor; null when it has no tab track.</summary>
    internal ScoreClip? ClipFromRange(RangeSnapshot range) => _document.Project.Tracks.Count == 0 ? null : range.ToScoreClip(TrackNow.Id);

    /// <summary>The selection as a score clip (whole bars: a Bars clip; otherwise a Beats clip), or the cursor beat; null when nothing can be captured.</summary>
    internal ScoreClip? CaptureClip()
    {
        if (_document.Project.Tracks.Count == 0) return null;
        var (m1, c1, m2, c2) = SelectionRange();
        try
        {
            var songId = TimelineClips.SongId(_document.Project);
            return HasSelection
                ? ClipboardService.CaptureSelection(_document.Project, ShownTrackIndex, _activeVoice, m1, c1, m2, c2, songId)
                : ClipboardService.CaptureBeats(_document.Project, ShownTrackIndex, _activeVoice, m1, c1, m1, c1, songId);
        }
        catch (System.IO.InvalidDataException) { return null; } // Not logged: the range holds nothing capturable, so nothing is copied
    }

    /// <summary>
    /// Cut after <paramref name="clip"/> was captured from the selection: clears what it took as one undo step and puts the cursor at the start.
    /// A cut of whole bars (PasteCommands.CutTakesBars) goes through WholeBarCut: a one-track song loses the bars, one track of several has
    /// them emptied; the cursor stays on its bar number, as the score editor does. <paramref name="clipQuestion"/> answers the clip question.
    /// </summary>
    internal bool CutClear(ScoreClip clip, IBarDeleteQuestions clipQuestion)
    {
        var (m1, c1, m2, c2) = SelectionRange();
        if (PasteCommands.CutTakesBars(_document.Project, clip.Kind, ShownTrackIndex, _activeVoice, (m1, c1, m2, c2), Sel.IsWholeBars) is var (first, last))
        {
            var bar = SelectedMeasure;
            var changed = WholeBarCut.RemovesBars(_document.Project, ShownTrackIndex)
                ? WholeBarCut.DeleteBars(_document, first, last, clipQuestion) is { Changed: true }
                : WholeBarCut.ClearBars(_document, first, last, ShownTrackIndex, Tools.FillBarsWithRests).Changed;
            return AfterBarEdit(changed, Math.Min(bar, TrackNow.Measures.Count - 1));
        }
        if (!RunEdit(() => PasteCommands.CutClear(_document.Project, clip.Kind, ShownTrackIndex, _activeVoice, m1, c1, m2, HasSelection ? c2 : c1))) return false;
        Sel.Clear();
        _probe.Cursor = (m1, c1, SelectedString);
        return true;
    }

    /// <summary>Pastes at the selection start (or the cursor) through <paramref name="paste"/> (PasteCommands.RunPaste or RunPasteSpecial); the cursor lands on the last pasted beat.</summary>
    internal PasteOutcome Paste(Func<IEditableDocument, PasteTarget, PasteOutcome> paste)
    {
        var (m1, c1, m2, c2) = SelectionRange();
        var outcome = paste(_document, HasSelection ? new PasteTarget(ShownTrackIndex, _activeVoice, m1, c1, m2, c2) : new PasteTarget(ShownTrackIndex, _activeVoice, m1, c1));
        if (!outcome.Changed) return outcome;
        Sel.Clear();
        _probe.ReplaceSong(_document.Project);
        if (outcome.CursorBar >= 0) _probe.Cursor = (outcome.CursorBar, outcome.CursorCell, SelectedString);
        return outcome;
    }

    /// <summary>Undoes the latest edit through the document undo stack; false when nothing is left to undo.</summary>
    internal bool Undo() => Restored(DocumentEdits.Undo(_document));

    /// <summary>Redoes the latest undone edit; false when nothing is left to redo.</summary>
    internal bool Redo() => Restored(DocumentEdits.Redo(_document));

    private bool Restored(UndoSnapshot? snapshot)
    {
        if (snapshot is null) return false;
        _probe.ReplaceSong(_document.Project);
        // The undo restored the document's own selection; the score shows that one (clearing it here would echo back and wipe the restored selection).
        var restored = _document.Selection;
        if (!restored.HasRange) Sel.Clear();
        else if (!SelectionMatches(restored.StartBar, restored.StartCell, restored.EndBar, restored.EndCell)) SelectRange(restored.StartBar, restored.StartCell, restored.EndBar, restored.EndCell);
        return true;
    }

    // ---- IScoreEditContext: the cursor and selection are the probe's; the active voice is set by the Voice tools ----
    /// <summary>The preview's song.</summary>
    public SongProject? Project => _document.Project;
    /// <summary>The track the score shows.</summary>
    public TrackModel? Track => TrackNow;
    /// <summary>The shown track's index.</summary>
    public int SelectedTrackIndex => _probe.TrackIndex;
    /// <summary>The active voice for entry.</summary>
    public int ActiveVoiceIndex => _activeVoice;
    /// <summary>The cursor's measure.</summary>
    public int SelectedMeasure { get => _probe.Cursor?.Measure ?? 0; set => _probe.Cursor = (value, SelectedCell, SelectedString); }
    /// <summary>The cursor's cell.</summary>
    public int SelectedCell { get => _probe.Cursor?.Cell ?? 0; set => _probe.Cursor = (SelectedMeasure, value, SelectedString); }
    /// <summary>The cursor's string.</summary>
    public int SelectedString { get => _probe.Cursor?.String ?? 0; set => _probe.Cursor = (SelectedMeasure, SelectedCell, value); }
    /// <summary>True while a range is selected.</summary>
    public bool HasSelection => Sel.HasSelection;
    /// <summary>The entry tool state new notes use.</summary>
    public ScoreEditToolState Tools { get; } = new();
    /// <summary>The voice cells of a measure, creating them when asked.</summary>
    public List<TabCell> CellsFor(MeasureModel measure, bool create = false) => measure.CellsForVoice(ActiveVoiceIndex, create);
    /// <summary>The slot count of a measure.</summary>
    public int SlotsFor(int measure) => _probe.SlotsFor(measure);

    /// <summary>Runs one song change as one undo step and revalidates the touched bars.</summary>
    public bool RunEdit(Func<bool> mutate, bool markTimeline = true, bool continuesLastStep = false)
    {
        var (first, _, last, _) = SelectionRange();
        var changed = DocumentEdits.Run(_document, _ => mutate(), before: continuesLastStep ? _document.Undo.Latest : null, invalidatesTimeline: markTimeline).Changed;
        if (changed) _probe.Layout.InvalidateMeasures(first, last);
        return changed;
    }

    /// <summary>Raised for each note the editor enters or changes, with the sound the host should audition for it.</summary>
    internal event Action<NotePreview>? NotePreviewed;

    /// <summary>A note's sound: its MIDI note, the output it plays on, and the length of the audition in milliseconds (0 when the cell has none).</summary>
    internal readonly record struct NotePreview(int Midi, int DeviceId, int Channel, int Program, int LengthMs);

    /// <summary>Auditions a note on the track's MIDI output (audio tracks have none).</summary>
    public void PreviewNote(TabNote note)
    {
        var track = TrackNow;
        if (!EditorGuard.CanEdit(track)) return;   // an audio track has no notation to sound
        var midi = note.MidiValue > 0 ? note.MidiValue : ScoreEditCommands.MidiOf(track, note.StringIndex, note.Fret);
        var cell = _commands.CurrentCell();
        var lengthMs = cell is not null ? MusicTime.NoteLengthMs(_document.Project, SelectedMeasure, SelectedCell, cell) : 0;
        NotePreviewed?.Invoke(new NotePreview(midi, track.MidiOutputDeviceId, track.MidiChannel, track.MidiProgram, lengthMs));
    }
    /// <summary>The tool state changed without a song edit; toolbars re-read it.</summary>
    public void NotifyState() { }
    /// <summary>The view repaints after every handled key.</summary>
    public void Redraw() { }   // the view repaints after every handled key
    /// <summary>Shows a status line through the host.</summary>
    public void Say(string text) { }
    /// <summary>Ends the selection; the cursor stays.</summary>
    public void ClearSelection() => Sel.Clear();
    /// <summary>The ordered selection range, or the cursor beat when nothing is selected.</summary>
    public (int m1, int c1, int m2, int c2) SelectionRange() =>
        Sel.HasSelection ? ScoreSelectionRules.Ordered(Sel.Range()) : (SelectedMeasure, SelectedCell, SelectedMeasure, SelectedCell);
    /// <summary>Moves the cursor to a beat and string.</summary>
    public void SetPosition(int measure, int cell, int stringIndex) => _probe.Cursor = (measure, cell, stringIndex);
    /// <summary>Moves the cursor to the next beat.</summary>
    public void MoveForwardBeat(TrackModel track)
    {
        var (fromMeasure, fromCell) = (SelectedMeasure, SelectedCell);
        if (ScoreCursorNavigator.ForwardBeat(Stops, fromMeasure, fromCell) is { } next)
        {
            _probe.Cursor = (next.Measure, next.Cell, SelectedString);
            TakeNewSlotDuration(fromMeasure, fromCell, next);
        }
    }

    /// <summary>
    /// After Right reaches the free slot after the last beat, the next note takes the previous beat's length clipped to the bar's
    /// remaining time; only the tool state changes (no rest, no model edit, no undo step).
    /// </summary>
    private void TakeNewSlotDuration(int fromMeasure, int fromCell, CursorTarget to)
    {
        if (to.Measure != fromMeasure || to.Cell <= fromCell) return;
        var track = TrackNow;
        if (fromMeasure < 0 || fromMeasure >= track.Measures.Count) return;
        var measure = track.Measures[fromMeasure];
        var cells = VoiceCells(measure);
        if (fromCell < 0 || fromCell >= cells.Count || to.Cell >= cells.Count) return;
        if (!CursorPositions.IsBeatCell(cells[fromCell])) return;
        var target = cells[to.Cell];
        var freeSlot = target.Notes.Count == 0 && (!CursorPositions.IsBeatCell(target) || target.IsRest && !target.WrittenRest);
        if (!freeSlot) return;
        for (var i = to.Cell + 1; i < cells.Count; i++) if (cells[i].Notes.Count > 0) return;   // only after the last note
        var (numerator, denominator) = BarSignatures.TimeAt(_document.Project, fromMeasure);
        var remaining = BarTiming.BarCapacityTicks(numerator, denominator) - BarTiming.VoiceUsedTicks(measure, _activeVoice + 1, to.Cell);
        if (ScoreBeatNavigation.NewSlotDuration(BarTiming.CellTicks(cells[fromCell]), remaining) is not { } ticks) return;
        if (ScoreBeatNavigation.WrittenValue(ticks) is not { } value) return;
        _commands.SetWritingDuration(value.Denominator, value.Dots);
    }

    /// <summary>The preview's one open song: its own undo stack; no playback, file, tuning or skip memory.</summary>
    internal sealed class PreviewDocument : IEditableDocument
    {
        /// <summary>Takes the song to preview.</summary>
        public PreviewDocument(SongProject project) => Project = project;
        /// <summary>The song being previewed.</summary>
        public SongProject Project { get; set; }
        /// <summary>The preview's own undo stack.</summary>
        public UndoController Undo { get; } = new();
        /// <summary>The preview's own selection (not shown anywhere).</summary>
        public SelectionModel Selection { get; } = new();
        /// <summary>The preview shows no timeline, so its highlight is never drawn.</summary>
        public BarHighlight Highlight { get; } = new();
        /// <summary>No selection to remember in the preview.</summary>
        public void RememberSelection(ProjectState state, SelectionSnapshot selection) { }
        /// <summary>No selection to restore in the preview.</summary>
        public void RestoreSelection(ProjectState state) { }
        /// <summary>Per-string tuning shifts (unused by the preview).</summary>
        public int[] TuningShift { get; } = new int[6];
        /// <summary>Skip ranges (unused by the preview).</summary>
        public List<(int Start, int End)> SkipRanges { get; } = new();
        /// <summary>No bar mapping to remember in the preview.</summary>
        public void RememberBarMapping(UndoSnapshot snapshot) { }
        /// <summary>No song id to carry in the preview.</summary>
        public void CarrySongId(SongProject from, SongProject to) { }
        /// <summary>No tuning shift to remember in the preview.</summary>
        public void RememberTuningShift(ProjectState state) { }
        /// <summary>No tuning shift to remember in the preview.</summary>
        public void RememberTuningShift(ProjectState state, int[] shift) { }
        /// <summary>No tuning shift to restore in the preview.</summary>
        public void RestoreTuningShift(ProjectState state) { }
        /// <summary>No skip ranges to remember in the preview.</summary>
        public void RememberSkipRanges(ProjectState state, (int Start, int End)[] ranges) { }
        /// <summary>No skip ranges to remember in the preview.</summary>
        public void RememberSkipRangesForReturn(ProjectState current, ProjectState target) { }
        /// <summary>No skip ranges to restore in the preview.</summary>
        public void RestoreSkipRanges(ProjectState state) { }
        /// <summary>The preview is never clean content.</summary>
        public bool IsCleanContent(UndoSnapshot snapshot) => false;
        /// <summary>Nothing watches the preview's song.</summary>
        public event Action<SongProject>? TimelineMarked { add { } remove { } }
        /// <summary>Nothing watches the preview's song.</summary>
        public event Action<SongProject, SongProject>? ProjectChanged { add { } remove { } }
        /// <summary>Nothing watches the preview's undo.</summary>
        public event Action? Restored { add { } remove { } }
        /// <summary>Nothing watches the preview's undo.</summary>
        public void NotifyRestored() { }
    }
}
