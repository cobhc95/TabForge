using TabForge.Documents;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.ScoreEditing;
using TabForge.Services;

namespace TabForge.Views.Score;

// Owns: the preview editor's palette tools: bar properties, bar append, stepping, marker jumps and adding, voice choice, tuplet, sound duration,
//   tempo, signatures and directions, as edits of the shown document through the same Core commands the WPF palette calls.
// Does not own: the questions the dialog tools ask (the Avalonia window layer), the edit rules (ScoreEditCommands, PaletteBarCommands,
//   MeasureProperties, BarSignatureCommands) or the tool state rules (PaletteToolStates).
// Tests: no dedicated test class.
internal sealed partial class ScorePreviewEditor
{
    private int _activeVoice;
    private IPaletteToolContext? _paletteContext;

    private IPaletteToolContext PaletteContext => _paletteContext ??= new PaletteContextView(this);

    /// <summary>The song's bars (the Structure tools clamp to them).</summary>
    internal int BarCount => BarRangeEditor.MaxMeasures(_document.Project);

    /// <summary>The bar under the cursor.</summary>
    internal MeasureModel? CurrentBar => _commands.CurrentMeasure();

    /// <summary>The bars the selection covers when it spans more than one bar (a signature answer then applies to exactly those), else null.</summary>
    internal (int First, int Last)? SelectedBarRange() => BarSignatureCommands.SelectedRange(BarCount, SelectedBars());

    /// <summary>Runs the Structure, Rhythm or Layout tool with this id that needs no question; false for any other id.</summary>
    internal bool RunPaletteTool(string toolId)
    {
        if (_commands.TryRunPaletteTool(toolId)) return true;
        var bar = SelectedMeasure;
        switch (toolId)
        {
            case "gp:append_bar": AppendBar(); return true;
            case "gp:step_back": StepBeat(-1); return true;
            case "gp:step_forward": StepBeat(1); return true;
            case "gp:previous_marker": JumpSection(-1); return true;
            case "gp:next_marker": JumpSection(1); return true;
            case "gp:triplet_feel": return AfterBarTool(PaletteBarCommands.TripletFeel(_document, MasterTrackIndex, bar));
            case "gp:free_time": return AfterBarTool(PaletteBarCommands.FreeTime(_document, MasterTrackIndex, bar));
            case "gp:double_barline": return AfterBarTool(PaletteBarCommands.DoubleBarline(_document, MasterTrackIndex, bar));
            case "gp:repeat_one_bar": return AfterBarTool(PaletteBarCommands.Simile(_document, ShownTrackIndex, bar, 1));
            case "gp:repeat_two_bars": return AfterBarTool(PaletteBarCommands.Simile(_document, ShownTrackIndex, bar, 2));
            case "gp:force_line_break": return AfterBarTool(PaletteBarCommands.LineBreak(_document, MasterTrackIndex, bar, force: true));
            case "gp:prevent_line_break": return AfterBarTool(PaletteBarCommands.LineBreak(_document, MasterTrackIndex, bar, force: false));
            case "gp:inactive_voice_gray": return AfterBarTool(PaletteBarCommands.InactiveVoiceGray(_document));
            case "gp:voice_1": SetActiveVoice(0); return true;
            case "gp:voice_2": SetActiveVoice(1); return true;
            default: return false;
        }
    }

    // Song-wide bar properties (feel, free time, double barline, system breaks) are written to every track and read back from the master-bar track;
    // simile marks live in the shown track's own bars.
    private int MasterTrackIndex => _document.Project.MasterBarTrack is { } master ? Math.Max(0, _document.Project.Tracks.IndexOf(master)) : 0;

    // A bar tool edits the song's bars, so the layout is rebuilt for the new state; the cursor stays where it is.
    private bool AfterBarTool(PaletteBarResult result)
    {
        if (!result.Changed) return true;
        _probe.ReplaceSong(_document.Project);
        if (result.Status.Length > 0) Notice?.Invoke(result.Status);
        return true;
    }

    // The Add bar tool: an empty bar after the last one, built like the one before it; the cursor stays, except for the Right key's arm,
    // which goes on to the new bar.
    private void AppendBar(bool moveCursorToNewBar = false)
    {
        var at = BarCount;
        if (at >= InputLimits.MaxMeasuresPerTrack)
        {
            Notice?.Invoke($"The song is at its limit of {InputLimits.MaxMeasuresPerTrack} bars");
            return;
        }
        if (!BarStructureEdits.InsertBar(_document, at, Math.Max(0, at - 1), moveMarkers: false, Tools.FillBarsWithRests).Changed) return;
        _probe.ReplaceSong(_document.Project);
        if (moveCursorToNewBar) _probe.Cursor = (at, 0, SelectedString);
        Notice?.Invoke($"Added bar {at + 1} at the end");
    }

    // The step tools move one beat (across bars) and end any range, as the cursor keys do.
    private void StepBeat(int direction)
    {
        Sel.Clear();
        var (fromMeasure, fromCell) = (SelectedMeasure, SelectedCell);
        var target = direction > 0 ? ScoreCursorNavigator.ForwardBeat(Stops, fromMeasure, fromCell) : ScoreCursorNavigator.BackBeat(Stops, fromMeasure, fromCell);
        if (target is { } t)
        {
            _probe.Cursor = (t.Measure, t.Cell, SelectedString);
            if (direction > 0) TakeNewSlotDuration(fromMeasure, fromCell, t);
        }
        Notice?.Invoke($"Bar {SelectedMeasure + 1}, beat {SelectedCell + 1}");
    }

    // The marker tools: the nearest marker before or after the cursor bar; without markers they move one bar.
    private void JumpSection(int direction)
    {
        Sel.Clear();
        var markers = _document.Project.Markers.OrderBy(marker => marker.MeasureIndex).ToList();
        if (markers.Count == 0)
        {
            if (ScoreCursorNavigator.Bar(Stops, SelectedMeasure, SelectedCell, direction) is { } bar) _probe.Cursor = (bar.Measure, bar.Cell, SelectedString);
            return;
        }
        var current = SelectedMeasure;
        var target = direction < 0 ? markers.LastOrDefault(marker => marker.MeasureIndex < current) : markers.FirstOrDefault(marker => marker.MeasureIndex > current);
        if (target is not null) _probe.Cursor = (target.MeasureIndex, Stops.Snap(target.MeasureIndex, 0), SelectedString);
    }

    // The active voice's beats of a bar for cursor stops; a bar the voice has not written yet has one empty beat (the first slot).
    private List<TabCell> VoiceCells(MeasureModel measure)
    {
        var cells = measure.CellsForVoice(_activeVoice);
        return cells.Count > 0 ? cells : new List<TabCell> { new() };
    }

    // Voice 1 or 2: the cursor beats and the note commands work on that voice's beats; the cursor moves to the nearest beat it has.
    private void SetActiveVoice(int voice)
    {
        if (_activeVoice == voice) return;
        _activeVoice = voice;
        _probe.Cursor = (SelectedMeasure, Stops.Snap(SelectedMeasure, SelectedCell), SelectedString);
    }

    /// <summary>The tuplet ratio shown in the N-tuplet question: the beat's own, else a triplet.</summary>
    internal (int Numerator, int Denominator) CurrentTuplet => _commands.CurrentCell()?.Tuplet ?? (3, 2);

    /// <summary>Applies a validated n:m tuplet (a ratio already lit is cleared by the same call).</summary>
    internal bool SetTuplet(int numerator, int denominator)
    {
        _commands.SetTuplet(numerator, denominator);
        return true;
    }

    /// <summary>The sounding length in percent of the beat at the cursor, or null when the cursor has no notes to set.</summary>
    internal int? SoundDurationPercent => _commands.CurrentCell() is { } cell && _commands.HasEditableNotes ? cell.SoundDurationPercent : null;

    /// <summary>Sets the sounding length of the selected beats (1 to 200 percent of the written value).</summary>
    internal bool SetSoundDuration(int percent)
    {
        percent = Math.Clamp(percent, 1, 200);
        _commands.SetSoundDurationPercent(percent);
        Notice?.Invoke($"Sound duration {percent}%");
        return true;
    }

    /// <summary>The tempo shown in the tempo question: the bar's own change, else the song's.</summary>
    internal int TempoAtCursor => _commands.CurrentMeasure()?.TempoChange ?? _document.Project.Tempo;

    /// <summary>Sets a tempo change on the cursor bar (20 to 400 beats per minute).</summary>
    internal bool SetTempoChange(int tempo)
    {
        tempo = Math.Clamp(tempo, 20, 400);
        var bar = SelectedMeasure;
        if (!DocumentEdits.Run(_document, project => MeasureProperties.SetTempoChange(project, bar, tempo)).Changed) return true;
        _probe.ReplaceSong(_document.Project);
        Notice?.Invoke($"Tempo change: ♩={tempo}");
        return true;
    }

    /// <summary>Applies the time signature the question returned; the status line names the bars it reached.</summary>
    internal bool SetTimeSignature(int numerator, int denominator, bool onlyThisBar)
    {
        var status = BarSignatureCommands.SetTime(_document, SelectedMeasure, SelectedBarRange(), numerator, denominator, onlyThisBar);
        Notice?.Invoke(status);
        _probe.ReplaceSong(_document.Project);
        return true;
    }

    /// <summary>Applies the key signature the question returned.</summary>
    internal bool SetKeySignature(int signature, bool minor, bool onlyThisBar)
    {
        var status = BarSignatureCommands.SetKey(_document, SelectedMeasure, SelectedBarRange(), signature, minor, onlyThisBar);
        Notice?.Invoke(status);
        _probe.ReplaceSong(_document.Project);
        return true;
    }

    /// <summary>Sets the cursor bar's navigation marks and alternate ending.</summary>
    internal bool SetDirections(string directions, int ending)
    {
        var bar = SelectedMeasure;
        if (DocumentEdits.Run(_document, project => MeasureProperties.SetDirections(project, bar, directions, ending)).Changed) _probe.ReplaceSong(_document.Project);
        return true;
    }

    /// <summary>
    /// Transposes the shown track by the semitones, clamped to the transpose range: the selected beats when there is a selection, else the
    /// whole track (both voices). Drum and audio tracks are skipped. False when nothing changed.
    /// </summary>
    internal bool TransposeShownTrack(int semitones)
    {
        var track = Track;
        if (track is null) return false;
        if (track.IsAudio) { Notice?.Invoke(EditorGuard.Hint); return false; }
        // Percussion numbers are instruments, not pitches: a drum track is never transposed.
        if (track.IsDrumTrack) { Notice?.Invoke($"{track.Name} is a drum track: drums are not transposed"); return false; }
        var shift = Math.Clamp(semitones, -MusicTheoryService.MaxTransposeSemitones, MusicTheoryService.MaxTransposeSemitones);
        if (shift == 0) return false;
        var hasSelection = HasSelection;
        (int StartMeasure, int StartCell, int EndMeasure, int EndCell)? range = hasSelection ? OrderedSelectionRange() : null;
        var unplaced = 0;
        DocumentEdits.Run(_document, project => { (_, unplaced) = MusicTheoryService.TransposeTrack(track, shift, range); return true; });
        _probe.ReplaceSong(_document.Project);
        Notice?.Invoke($"Transposed {(hasSelection ? "the selection of " : "")}{track.Name} {shift:+0;-0} st" +
            (unplaced > 0 ? $" ({unplaced} note{(unplaced == 1 ? "" : "s")} did not fit on a free string and kept their fret)" : ""));
        return true;
    }

    /// <summary>The selection as (start bar, start beat, end bar, end beat), the earlier end first.</summary>
    private (int StartMeasure, int StartCell, int EndMeasure, int EndCell) OrderedSelectionRange()
    {
        var (m1, c1, m2, c2) = Sel.Range();
        var anchorFirst = m1 < m2 || (m1 == m2 && c1 <= c2);
        return anchorFirst ? (m1, c1, m2, c2) : (m2, c2, m1, c1);
    }

    /// <summary>Cycles the clef of the cursor bar on the selected track and shows the new clef in the status line.</summary>
    internal bool CycleClef()
    {
        var bar = SelectedMeasure;
        var clef = "";
        if (DocumentEdits.Run(_document, project => MeasureProperties.TryCycleClef(project, SelectedTrackIndex, bar, out clef)).Changed)
        {
            _probe.ReplaceSong(_document.Project);
            Notice?.Invoke($"Clef {clef}");
        }
        return true;
    }

    /// <summary>The marker that starts at the cursor bar, or null.</summary>
    internal MarkerModel? MarkerAtCursor => _document.Project.Markers.FirstOrDefault(marker => marker.MeasureIndex == SelectedMeasure);

    /// <summary>Adds a section marker that starts at the cursor bar; a marker already there (one section per bar) gets the new name and colour.</summary>
    internal bool AddOrEditMarker(string title, string colourHex)
    {
        var bar = Math.Clamp(SelectedMeasure, 0, Math.Max(0, BarCount - 1));
        DocumentEdits.Run(_document, project =>
        {
            if (project.Markers.FirstOrDefault(marker => marker.MeasureIndex == bar) is { } existing) (existing.Title, existing.ColorHex) = (title, colourHex);
            else project.Markers.Add(new MarkerModel { MeasureIndex = bar, Title = title, ColorHex = colourHex });
            return true;
        });
        Notice?.Invoke($"Section \"{title}\" at bar {bar + 1}");
        return true;
    }

    /// <summary>The problems Check bars finds in the song, as the status and message lines.</summary>
    internal IReadOnlyList<string> BarProblems() => MusicTime.FindBarProblems(_document.Project).Select(issue => issue.Describe()).ToList();

    // The editor seen through the palette state rules.
    private sealed class PaletteContextView(ScorePreviewEditor editor) : IPaletteToolContext
    {
        public SongProject Project => editor._document.Project;
        public MeasureModel? CurrentBar => editor._commands.CurrentMeasure();
        public ScoreEditCommands Edit => editor._commands;
        public int SelectedMeasure => editor.SelectedMeasure;
        public int SelectedString => editor.SelectedString;
        public int ActiveVoiceIndex => editor._activeVoice;
        public bool HasTrack => editor._document.Project.Tracks.Count > 0;
        public int MaxMeasures => editor.BarCount;
    }
}
