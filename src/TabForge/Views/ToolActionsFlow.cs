using System.Windows;
using TabForge.Controllers;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge.Views;

/// <summary>What a tool action redraws after it changed the song (the window maps these to its own refresh).</summary>
[Flags]
internal enum ToolRefresh
{
    None = 0, Score = 1, Repaint = 2, Arrangement = 4, Palette = 8, Instrument = 16,
}

/// <summary>What the tool actions need from their window.</summary>
internal interface IToolActionsHost : IToolPaletteHost
{
    /// <summary>The song on show: every edit goes through <see cref="DocumentEdits"/> on it.</summary>
    DocumentSession ActiveDocument { get; }
    /// <summary>The track row selected in the track list (the track the bar tools act on).</summary>
    int SelectedTrackRow { get; }
    ArrangementController Arrangement { get; }
    void RefreshAfterEdit(ToolRefresh refresh);
    /// <summary>Redraws everything a measure tempo change touches: the visual timeline, the engine, the timeline, the status, the palette and the title.</summary>
    void RefreshAfterTempoChange();
    /// <summary>Selects the marker in the marker list, scrolls to it and moves the keyboard focus there (null: just focuses the list).</summary>
    void FocusMarkerList(MarkerModel? marker);
}

// Owns: the palette tools that edit through a dialog or a bar property (swing feel, simile, line breaks, free time, inactive voice, stem,
//   sound duration, tuplet, measure tempo, marker list focus), Check bars, Transpose, and the chord name insert of the chord finder.
// Does not own: the palette's buttons (ToolPaletteController), the song edits themselves (ArrangementController, MusicTheoryService),
//   the one-line editor tools of the palette (the editor's effects).
// Tests: TestDocumentOperations, TestToolsChordFinderAndSongStats, TestPlayDomainFlows.
internal sealed class ToolActionsFlow
{
    private readonly IToolActionsHost _host;

    public ToolActionsFlow(IToolActionsHost host) => _host = host;

    private TabEditorControl Editor => _host.Editor;
    private DocumentSession Doc => _host.ActiveDocument;

    /// <summary>Runs the palette tool with this id when it is one of these; false for every other tool.</summary>
    public bool TryRun(string id)
    {
        switch (id)
        {
            case "gp:triplet_feel": CycleTripletFeel(); break;
            case "gp:free_time": ToggleMeasureProperty(measure => measure.FreeTime, (measure, value) => measure.FreeTime = value, "Free-time measure"); break;
            case "gp:repeat_one_bar": ToggleSimile(1); break;
            case "gp:repeat_two_bars": ToggleSimile(2); break;
            case "gp:check_bars": CheckBars(); break;
            case "gp:force_line_break": ToggleLineBreak(force: true); break;
            case "gp:prevent_line_break": ToggleLineBreak(force: false); break;
            case "gp:marker_list": FocusMarkerList(); break;
            case "gp:custom_ntuplet":
            case "duration:tuplet-menu": ChooseTuplet(); break;
            case "gp:sound_duration": SetSoundDuration(); break;
            case "gp:inactive_voice_gray": ToggleInactiveVoiceGray(); break;
            case "gp:stem_invert": ToggleStemDirection(); break;
            case "composition:tempo": SetMeasureTempo(); break;
            default: return false;
        }
        return true;
    }

    /// <summary>Check bars (F4): the same rule as the red bar tint, so the report and the score cannot disagree.</summary>
    public void CheckBars()
    {
        var issues = MusicTime.FindBarProblems(_host.Project);
        if (issues.Count == 0)
        {
            MessageBox.Show(_host.Window, "Every bar adds up to its time signature.", "Check bars", MessageBoxButton.OK, MessageBoxImage.Information);
            _host.SetStatus("Bars OK");
            return;
        }
        var lines = issues.Take(20).Select(issue => issue.Describe());
        var more = issues.Count > 20 ? $"\n…and {issues.Count - 20} more" : "";
        MessageBox.Show(_host.Window, string.Join("\n", lines) + more, "Check bars", MessageBoxButton.OK, MessageBoxImage.Warning);
        _host.SetStatus($"{issues.Count} bar{(issues.Count == 1 ? "" : "s")} to check");
    }

    /// <summary>Transposes the selection, or the whole track, by a typed number of semitones.</summary>
    public void Transpose()
    {
        var t = _host.SelectedTrack; if (t is null) return;
        if (t.IsAudio) { _host.SetStatus(EditorGuard.Hint); return; }
        // Percussion numbers are instruments, not pitches: a drum track is never transposed.
        if (t.Kind == TrackKind.Drums || t.MidiChannel == 9) { _host.SetStatus($"{t.Name} is a drum track: drums are not transposed"); return; }
        // With bars/beats selected only those move; otherwise the whole track (both voices).
        var hasSelection = Editor.HasSelection;
        var txt = GpDialogs.Prompt("Transpose", hasSelection ? "Semitones (-12..12) for the selection:" : "Semitones (-12..12) for the whole track:", "0");
        if (txt is null || !int.TryParse(txt, out var st)) return;
        st = Math.Clamp(st, -12, 12);
        if (st == 0) return;
        var range = hasSelection ? Editor.SelectionCellRange : ((int, int, int, int)?)null;
        var unplaced = 0;
        DocumentEdits.Run(Doc, project => { (_, unplaced) = MusicTheoryService.TransposeTrack(t, st, range); return true; });
        _host.RefreshAfterEdit(ToolRefresh.Score | ToolRefresh.Instrument);
        _host.SetStatus($"Transposed {(hasSelection ? "the selection of " : "")}{t.Name} {st:+0;-0} st" +
            (unplaced > 0 ? $" ({unplaced} note{(unplaced == 1 ? "" : "s")} did not fit on a free string and kept their fret)" : ""));
    }

    /// <summary>Attaches a chord name to the beat under the cursor; false when there is no cursor cell.</summary>
    public bool InsertChordName(string name)
    {
        var c = Editor.Effects.CurrentCell(); if (c is null) return false;
        DocumentEdits.Run(Doc, _ => { c.ChordName = name; return true; }); _host.RefreshAfterEdit(ToolRefresh.Score);
        return true;
    }

    public void ToggleMeasureProperty(Func<MeasureModel, bool> getter, Action<MeasureModel, bool> setter, string label)
    {
        if (_host.CurrentBar is null) return;
        var value = false;
        if (!DocumentEdits.Run(Doc, p => _host.Arrangement.TryToggleMeasureProperty(p, _host.SelectedTrackRow,
                Editor.SelectedMeasure, getter, setter, out value)).Changed) return;
        _host.RefreshAfterEdit(ToolRefresh.Score | ToolRefresh.Arrangement | ToolRefresh.Palette);
        _host.SetStatus(value ? $"{label} on" : $"{label} off");
    }

    public void CycleTripletFeel()
    {
        if (_host.CurrentBar is null) return;
        var next = "";
        if (!DocumentEdits.Run(Doc, p => _host.Arrangement.TryCycleTripletFeel(p, _host.SelectedTrackRow,
                Editor.SelectedMeasure, out next)).Changed) return;
        _host.RefreshAfterEdit(ToolRefresh.Score | ToolRefresh.Palette);
        _host.SetStatus(next switch { "None" => "Straight feel", "Triplet8th" => "Eighth-note swing", _ => "Sixteenth-note swing" });
    }

    public void ToggleSimile(int barCount)
    {
        if (_host.CurrentBar is null || Editor.SelectedMeasure < barCount) return;
        var enabled = false;
        if (!DocumentEdits.Run(Doc, p => _host.Arrangement.TryToggleSimile(p, _host.SelectedTrackRow,
                Editor.SelectedMeasure, barCount, out enabled)).Changed) return;
        _host.RefreshAfterEdit(ToolRefresh.Score | ToolRefresh.Arrangement | ToolRefresh.Palette);
        _host.SetStatus(enabled ? $"Repeating previous {barCount} bar{(barCount == 1 ? "" : "s")}" : "Simile repeat removed");
    }

    public void ToggleLineBreak(bool force)
    {
        if (_host.CurrentBar is null) return;
        var enabled = false;
        if (!DocumentEdits.Run(Doc, p => _host.Arrangement.TryToggleLineBreak(p, _host.SelectedTrackRow,
                Editor.SelectedMeasure, force, out enabled)).Changed) return;
        _host.RefreshAfterEdit(ToolRefresh.Score | ToolRefresh.Palette);
        _host.SetStatus(force
            ? enabled ? "System break forced before this measure" : "Forced system break removed"
            : enabled ? "Automatic system break prevented before this measure" : "System-break prevention removed");
    }

    public void ToggleInactiveVoiceGray()
    {
        DocumentEdits.Run(Doc, p => { p.GrayInactiveVoice = !p.GrayInactiveVoice; return true; });
        _host.RefreshAfterEdit(ToolRefresh.Repaint | ToolRefresh.Palette);
        _host.SetStatus(_host.Project.GrayInactiveVoice ? "Inactive voice dimmed" : "Inactive voice at normal brightness");
    }

    public void ToggleStemDirection()
    {
        var cell = Editor.Effects.CurrentCell();
        if (cell is null || !Editor.Effects.HasEditableNotes) return;
        Editor.Effects.SetStemDirection(cell.StemDirection == StemDirection.Invert ? StemDirection.Auto : StemDirection.Invert);
    }

    public void SetSoundDuration()
    {
        var cell = Editor.Effects.CurrentCell();
        if (cell is null || !Editor.Effects.HasEditableNotes) return;
        var text = GpDialogs.Prompt("Sound duration", "Sounding duration (% of written value, 1–200):",
            cell.SoundDurationPercent.ToString());
        if (text is null || !int.TryParse(text, out var percent)) return;
        Editor.Effects.SetSoundDurationPercent(Math.Clamp(percent, 1, 200));
        _host.SetStatus($"Sound duration {Math.Clamp(percent, 1, 200)}%");
    }

    public void FocusMarkerList()
    {
        var markers = _host.Project.Markers;
        var selected = markers.OrderBy(marker => marker.MeasureIndex)
            .FirstOrDefault(marker => marker.MeasureIndex >= Editor.SelectedMeasure)
            ?? markers.OrderBy(marker => marker.MeasureIndex).LastOrDefault();
        _host.FocusMarkerList(selected);
        _host.SetStatus(markers.Count == 0 ? "No markers yet" : "Marker list focused");
    }

    public void ChooseTuplet()
    {
        var current = Editor.Effects.CurrentCell()?.Tuplet ?? (3, 2);
        var numeratorText = GpDialogs.Prompt("Tuplet", "Notes in the tuplet (numerator):", current.Numerator.ToString());
        if (numeratorText is null || !int.TryParse(numeratorText, out var numerator)) return;
        var denominatorText = GpDialogs.Prompt("Tuplet", "Normal note value (denominator):", current.Denominator.ToString());
        if (denominatorText is null || !int.TryParse(denominatorText, out var denominator)) return;
        Editor.Effects.SetTuplet(numerator, denominator);
    }

    public void SetMeasureTempo()
    {
        var measure = Editor.Effects.CurrentMeasure();
        if (measure is null) return;
        var value = GpDialogs.Prompt("Tempo change", "Tempo in beats per minute (20–400):",
            (measure.TempoChange ?? _host.Project.Tempo).ToString());
        if (value is null || !int.TryParse(value, out var tempo)) return;
        tempo = Math.Clamp(tempo, 20, 400);
        DocumentEdits.Run(Doc, p => { _host.Arrangement.TrySetTempoChange(p, Editor.SelectedMeasure, tempo); return true; });
        Editor.InvalidateScoreLayout();
        _host.RefreshAfterTempoChange();
        _host.SetStatus($"Tempo change: ♩={tempo}");
    }
}
