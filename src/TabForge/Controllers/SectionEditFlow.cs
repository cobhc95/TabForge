using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge.Controllers;

/// <summary>Where the score editor's caret is.</summary>
internal readonly record struct EditorCaret(int Bar, int Cell, int String);

/// <summary>What the bar, section and area commands of the timeline need from the window that shows the song they edit.</summary>
internal interface ISectionEditHost : IBarRangePromptHost
{
    void SaveSettings();
    /// <summary>True while <paramref name="document"/> is the song the window shows (false for a background or a closed song).</summary>
    bool IsShown(DocumentSession document);
    /// <summary>The score clipboard shared by every window and by the score editor.</summary>
    ClipboardService Clipboard { get; }
    /// <summary>Asks the user what a paste should do when the settings do not say.</summary>
    IPasteQuestionAsker PasteQuestions { get; }
    void SetStatus(string text);
    /// <summary>The track row selected in the track list (0 when none).</summary>
    int SelectedTrackIndex { get; }
    EditorCaret Caret { get; }
    /// <summary>Puts the caret on a bar and cell without moving the playhead.</summary>
    void SetCaret(int bar, int cell, int stringIndex);
    /// <summary>Selects a bar in the score (the playhead follows when the song is not playing).</summary>
    void SelectBar(int bar);
    /// <summary>The selected bar range follows its bars through a bar mapping.</summary>
    void RemapSelection(int[] oldToNewBar);
    void ClearSelection();
    /// <summary>Selects bars <paramref name="start"/> to <paramref name="end"/> in the score and the timeline.</summary>
    void SelectBars(int start, int end);
    /// <summary>Turns the loop off when it is on.</summary>
    void EndLoop();
    void SyncAreaVisuals();
    /// <summary>The audio engine loads the clips as they are now (clips moved or copied with a section).</summary>
    void SyncAudioEngine();
    /// <summary>The views' half of an edit that changed the bar structure: playback and selection follow the mapping, then every view redraws.</summary>
    void FinishStructureEdit(string status, int[] oldToNewBar, int? continueAtBar);
    /// <summary>The views' half of a bar edit that kept the bar structure.</summary>
    void FinishBarEdit(string status);
    /// <summary>A yes/no question with "No" as the default; true on yes.</summary>
    bool ConfirmWarning(string text, string caption);
    void ShowLastBarWarning();
    /// <summary>A themed question with a destructive <paramref name="yesText"/> button and Cancel as the default; true on yes.</summary>
    bool Ask(string title, string text, string yesText, bool withUndoHint);
}

// Owns: the bar, section and area commands of the arrangement timeline: copy, cut, paste, duplicate, insert, delete and move.
// Does not own: the model edits (DocumentEdits, BarRangeEditor) and the timeline drawing.
// Tests: TestClipAndSectionEdits, TestDocumentOperations.
/// <summary>
/// The bar, section and area commands of the arrangement timeline: copy, cut, paste, duplicate, insert, delete and move. Every command is given the song it
/// edits and does nothing when the window no longer shows it. The model half is one <see cref="DocumentEdits"/> step in <see cref="ArrangementController"/>;
/// this class decides what to ask, what to select afterwards and what to tell the user, and the host redraws.
/// </summary>
internal sealed class SectionEditFlow
{
    private readonly ISectionEditHost _host;
    private readonly ArrangementController _arrangement;
    private readonly EmptyBarFlow _empty;

    public SectionEditFlow(ISectionEditHost host, ArrangementController arrangement)
    {
        _host = host;
        _arrangement = arrangement;
        _empty = new EmptyBarFlow(host, arrangement);
        Range = new BarRangeFlow(host);
    }

    /// <summary>The bar-range commands (clear, remove, insert a gap) and the Delete prompt.</summary>
    public BarRangeFlow Range { get; }

    /// <summary>The Delete key on selected whole bars without notes: asks, then removes them (true = handled).</summary>
    public bool TryDeleteSelectedEmpty(DocumentSession doc, int trackIndex, int m1, int c1, int m2, int c2) => _empty.TryDeleteSelectedEmpty(doc, trackIndex, m1, c1, m2, c2);

    public void DeleteEmptyInRange(DocumentSession doc, int start, int end) => _empty.DeleteEmptyInRange(doc, start, end);

    /// <summary>Warns before bars are deleted under audio or MIDI clips; false when the user cancels.</summary>
    private bool ConfirmClips(SongProject project, int start, int endInclusive) =>
        ClipDeleteImpact.Find(project, new[] { (start, endInclusive) }) is not { } impact
        || _host.Ask("Delete bars", ClipDeleteImpact.Describe(impact), "Continue", withUndoHint: false);

    private static string WithNote(string status, string note) => note.Length > 0 ? $"{status} ({note})" : status;

    // ---------- copy ----------

    public void CopyBar(DocumentSession doc, int bar, TrackModel? track, bool allTracks)
    {
        if (!_host.IsShown(doc)) return;
        var trackIndex = track is null ? -1 : doc.Project.Tracks.IndexOf(track);
        if (!allTracks && track is null) return;
        CopyClip(() => TimelineClips.CopyBar(doc.Project, bar, trackIndex, allTracks),
            allTracks ? $"Copied bar {bar + 1} from all tracks" : $"Copied bar {bar + 1} from {track!.Name}");
    }

    /// <summary>Copies the selected bars of every track, or of track <paramref name="onlyTrack"/> alone (a one-track selection).</summary>
    public void CopyArea(DocumentSession doc, int start, int end, int onlyTrack = -1)
    {
        if (!_host.IsShown(doc)) return;
        var from = onlyTrack >= 0 && onlyTrack < doc.Project.Tracks.Count ? $" from {doc.Project.Tracks[onlyTrack].Name}" : "";
        CopyClip(() => TimelineClips.CopyArea(doc.Project, start, end, onlyTrack), $"Copied bars {start + 1}-{end + 1}{from}");
    }

    /// <summary>Cut on the selected bars: every track copies and removes them; one track copies and clears them (its bars stay in line with the others).</summary>
    public void CutArea(DocumentSession doc, int start, int end, int onlyTrack = -1)
    {
        CopyArea(doc, start, end, onlyTrack);
        if (onlyTrack < 0 || doc.Project.Tracks.Count <= 1) DeleteArea(doc, start, end, "Cut");
        else Range.Run(doc, BarRangeAction.Clear, start, end, allTracks: false, askAboutClips: false);
    }

    /// <summary>Puts a Bars clip on the shared score clipboard (the same one the score editor pastes from) and reports it.</summary>
    private void CopyClip(Func<ScoreClip> capture, string status)
    {
        try
        {
            var written = _host.Clipboard.Copy(capture());
            _host.SetStatus(written ? status : status + " (clipboard busy: paste works inside TabForge only)");
        }
        catch (System.IO.InvalidDataException ex) { _host.SetStatus(ex.Message); } // Not logged: status line shows the message; the edit is refused
    }

    /// <summary>Copies the section that contains <paramref name="bar"/>; does nothing on a bar outside every section.</summary>
    public void CopySectionAt(DocumentSession doc, int bar)
    {
        if (!_host.IsShown(doc)) return;
        if (_arrangement.SectionAt(doc.Project, bar) is { } marker) CopySection(doc, marker);
    }

    public void CopySection(DocumentSession doc, MarkerModel marker)
    {
        if (!_host.IsShown(doc)) return;
        try
        {
            if (_arrangement.CopySection(doc.Project, marker, _host.Clipboard, out var written) is null) return;
            _host.SetStatus(written ? $"Copied section '{marker.Title}'" : $"Copied section '{marker.Title}' (clipboard busy: paste works inside TabForge only)");
        }
        catch (System.IO.InvalidDataException ex) { _host.SetStatus(ex.Message); } // Not logged: status line shows the message; the edit is refused
    }

    public void CutSection(DocumentSession doc, MarkerModel marker)
    {
        if (!_host.IsShown(doc)) return;
        if (!_arrangement.CanDeleteSection(doc.Project, marker, out _))
        {
            _host.ShowLastBarWarning();
            return;
        }
        CopySection(doc, marker);
        DeleteSection(doc, marker, confirm: false, status: $"Cut section '{marker.Title}'", takeClips: true);
    }

    // ---------- section ----------

    public void DuplicateSection(DocumentSession doc, MarkerModel marker)
    {
        if (!_host.IsShown(doc)) return;
        var snapshot = _arrangement.CaptureSectionSnapshot(doc.Project, marker);
        if (snapshot is null || !_arrangement.TryGetSectionBounds(doc.Project, marker, out _, out var end)) return;
        InsertSnapshot(doc, end, snapshot, $"Duplicated section '{marker.Title}'");
    }

    public void PasteSectionAfter(DocumentSession doc, MarkerModel marker)
    {
        if (!_host.IsShown(doc) || !_arrangement.TryGetSectionBounds(doc.Project, marker, out _, out var end)) return;
        PasteClipAsSection(doc, end);
    }

    public void PasteSectionAt(DocumentSession doc, int bar)
    {
        if (!_host.IsShown(doc)) return;
        PasteClipAsSection(doc, Math.Clamp(bar, 0, BarRangeEditor.MaxMeasures(doc.Project)));
    }

    /// <summary>
    /// Inserts the shared Bars clip before bar <paramref name="at"/> on all tracks: a copied section comes back with its title and
    /// colour; bars copied in the score or as an area come in as plain bars (no section marker).
    /// </summary>
    private void PasteClipAsSection(DocumentSession doc, int at)
    {
        if (_host.Clipboard.TryGetClip(out var error) is not { } clip) { _host.SetStatus(error ?? ClipboardService.NotTabForgeNotesMessage); return; }
        if (!TimelineClips.CanPasteOnTimeline(clip)) { _host.SetStatus("The timeline pastes whole bars only; paste beats in the score."); return; }
        var tracks = TimelineClips.BarsPerTrack(clip, doc.Project, _host.SelectedTrackIndex, out var note);
        var marker = _arrangement.SectionMarkerFor(clip);
        InsertSnapshot(doc, at, new SectionClipboardSnapshot(tracks, marker, marker is not null ? _arrangement.SectionClipsFor(clip) : null),
            WithNote(marker is not null ? $"Pasted section '{marker.Title}'" : $"Pasted {clip.BarCount} bar(s) at bar {at + 1}", note));
    }

    private void InsertSnapshot(DocumentSession doc, int at, SectionClipboardSnapshot snapshot, string status)
    {
        var bars = BarRangeEditor.MaxMeasures(doc.Project);
        at = Math.Clamp(at, 0, bars);
        if (_arrangement.InsertSection(doc, at, snapshot).Value is not { } mapping) return;
        var max = Math.Max(0, BarRangeEditor.MaxMeasures(doc.Project) - 1);
        _host.SetCaret(Math.Clamp(at, 0, max), 0, _host.Caret.String);
        _host.FinishStructureEdit(status, mapping, null);
        if (snapshot.Clips is { IsEmpty: false }) _host.SyncAudioEngine();
    }

    /// <summary>Deletes a section's bars from every track, after a question when the setting asks for one (<paramref name="confirm"/>).</summary>
    public void DeleteSection(DocumentSession doc, MarkerModel marker, bool confirm, string? status = null, bool takeClips = false)
    {
        if (!_host.IsShown(doc)) return;
        if (!_arrangement.CanDeleteSection(doc.Project, marker, out _))
        {
            _host.ShowLastBarWarning();
            return;
        }
        if (confirm && _host.Settings.General.ConfirmDeleteSection && !_host.ConfirmWarning(
                $"Delete the '{marker.Title}' section and its bars and notes from every track? Undo can restore them.", "Delete Section"))
            return;

        if (_arrangement.TryGetSectionBounds(doc.Project, marker, out var from, out var to) && !ConfirmClips(doc.Project, from, to - 1)) return;
        var caret = _host.Caret;
        if (_arrangement.DeleteSection(doc, marker, takeClips).Value is not { } removal) return;
        var mappedSelection = caret.Bar >= 0 && caret.Bar < removal.OldToNewBar.Length
            ? removal.OldToNewBar[caret.Bar]
            : -1;
        var newSelection = mappedSelection >= 0 ? mappedSelection : removal.ContinueAtBar;
        _host.SetCaret(Math.Clamp(newSelection, 0, Math.Max(0, BarRangeEditor.MaxMeasures(doc.Project) - 1)), caret.Cell, caret.String);
        if (takeClips) _host.SyncAudioEngine();
        _host.FinishStructureEdit(status ?? $"Deleted section '{marker.Title}' and its bars from every track (Undo restores them)", removal.OldToNewBar,
            removal.ContinueAtBar);
    }

    // ---------- bars ----------

    public void InsertBar(DocumentSession doc, int at)
    {
        if (!_host.IsShown(doc)) return;
        var oldBars = BarRangeEditor.MaxMeasures(doc.Project);
        at = _arrangement.InsertBar(doc, at, at == 0 ? 0 : at - 1, moveMarkers: true, fillRests: _host.Settings.Editing.FillBarsWithRests).Value!.At;
        var bars = BarRangeEditor.MaxMeasures(doc.Project);
        if (bars > oldBars) _host.RemapSelection(SelectionModel.InsertMap(oldBars, at));
        _host.SelectBar(Math.Min(at, Math.Max(0, bars - 1)));
        _host.FinishBarEdit($"Added bar {at + 1}");
    }

    public void DeleteBar(DocumentSession doc, int bar, TrackModel? track, bool allTracks)
    {
        if (!_host.IsShown(doc) || bar < 0) return;
        var project = doc.Project;
        if (allTracks)
        {
            if (BarRangeEditor.MaxMeasures(project) <= 1) return;
            if (_host.Settings.Editing.ConfirmDeleteBar && !_host.ConfirmWarning($"Delete bar {bar + 1} from every track?", "Delete bar")) return;
            if (!ConfirmClips(project, bar, bar)) return;
            var oldBars = BarRangeEditor.MaxMeasures(project);
            if (!_arrangement.DeleteBar(doc, bar, -1, allTracks: true, moveMarkers: true).Changed) return;
            if (BarRangeEditor.MaxMeasures(project) < oldBars) _host.RemapSelection(SelectionModel.RemoveMap(oldBars, bar, bar));
        }
        else
        {
            if (track is null || track.Measures.Count <= 1 || bar >= track.Measures.Count) return;
            if (_host.Settings.Editing.ConfirmDeleteBar && !_host.ConfirmWarning($"Delete bar {bar + 1} from {track.Name}?", "Delete bar")) return;
            var trackIndex = project.Tracks.IndexOf(track);
            if (!_arrangement.DeleteBar(doc, bar, trackIndex, allTracks: false, moveMarkers: false).Changed) return;
        }
        _host.SelectBar(Math.Clamp(bar, 0, Math.Max(0, BarRangeEditor.MaxMeasures(project) - 1)));
        _host.FinishBarEdit($"Deleted bar {bar + 1}");
    }

    public void PasteBar(DocumentSession doc, int bar, TrackModel? track, bool allTracks)
    {
        if (!_host.IsShown(doc)) return;
        var trackIndex = track is null ? -1 : doc.Project.Tracks.IndexOf(track);
        if (_host.Clipboard.TryGetClip(out var error) is not { } clip) { _host.SetStatus(error ?? ClipboardService.NotTabForgeNotesMessage); return; }
        TimelinePasteResult? answered = null;
        var paste = DocumentEdits.Run<TimelinePasteResult>(doc, project =>   // one undo step per paste
        {
            answered = TimelineClips.PasteBars(project, clip, bar, trackIndex, allTracks ? TimelinePasteKind.OverwriteAllTracks : TimelinePasteKind.OverwriteThisTrack,
                _host.Settings.Editing, _host.PasteQuestions);
            return answered.Changed ? answered : null;
        });
        _host.SaveSettings();   // a "Remember my choice" answer
        if (paste.Value is not { } result) { _host.SetStatus(answered?.Message ?? ""); return; }
        if (result.OldToNewBar is { } map)   // answered "Insert before/after": structural, like the area paste
        {
            _host.FinishStructureEdit(result.Message, map, null);
            return;
        }
        _host.FinishBarEdit(result.Message);
    }

    // ---------- areas (bars selected on the timeline) ----------

    public void DeleteArea(DocumentSession doc, int start, int end, string verb)
    {
        if (!_host.IsShown(doc)) return;
        if (!ConfirmClips(doc.Project, start, end)) return;
        if (_arrangement.DeleteBars(doc, start, end).Value is not { } map) { _host.ShowLastBarWarning(); return; }
        _host.EndLoop();
        _host.ClearSelection();   // score and timeline drop the deleted range together
        doc.SkipRanges.Clear();
        _host.SyncAreaVisuals();
        var bars = BarRangeEditor.MaxMeasures(doc.Project);
        _host.SetCaret(Math.Clamp(start, 0, Math.Max(0, bars - 1)), 0, _host.Caret.String);
        _host.FinishStructureEdit($"{verb} bars {start + 1}-{end + 1}", map, Math.Min(start, bars - 1));
    }

    public void PasteAreaAt(DocumentSession doc, int at)
    {
        if (!_host.IsShown(doc)) return;
        if (_host.Clipboard.TryGetClip(out var error) is not { } clip) { _host.SetStatus(error ?? ClipboardService.NotTabForgeNotesMessage); return; }
        // One track's bars go onto the selected track in place; bars of several tracks are inserted on every track.
        if (clip.Tracks.Count == 1 && doc.Project.Tracks.Count > 1) { PasteBar(doc, at, doc.Project.Tracks.ElementAtOrDefault(_host.SelectedTrackIndex), allTracks: false); return; }
        TimelinePasteResult? answered = null;
        var paste = DocumentEdits.Run<TimelinePasteResult>(doc, project =>   // one undo step for the whole paste
        {
            answered = TimelineClips.PasteBars(project, clip, at, _host.SelectedTrackIndex, TimelinePasteKind.InsertBars,
                _host.Settings.Editing, _host.PasteQuestions);
            return answered.Changed && answered.OldToNewBar is not null ? answered : null;
        });
        _host.SaveSettings();   // a "Remember my choice" answer
        if (paste.Value is not { } result) { _host.SetStatus(answered?.Message ?? ""); return; }
        _host.FinishStructureEdit(result.Message, result.OldToNewBar!, null);
        _host.SelectBars(at, at + result.BarsPasted - 1);   // after the remap: these are already new bar numbers
    }

    /// <summary>Moves the selected bars <paramref name="start"/> to <paramref name="end"/> in front of bar <paramref name="insertBefore"/>; a drop inside the area or off the timeline cancels.</summary>
    public void MoveArea(DocumentSession doc, int start, int end, int insertBefore)
    {
        if (!_host.IsShown(doc)) return;
        if (insertBefore < 0 || (insertBefore >= start && insertBefore <= end + 1)) { _host.SetStatus("Move cancelled"); return; }
        var count = end - start + 1;
        if (_arrangement.MoveBars(doc, start, end, insertBefore).Value is not { } moved) return;
        doc.SkipRanges.Clear();
        _host.FinishStructureEdit($"Moved bars {start + 1}-{end + 1} to bar {moved.At + 1}", moved.Map, null);
        _host.SelectBars(moved.At, moved.At + count - 1);   // after the remap: these are already new bar numbers
        _host.SyncAreaVisuals();
    }
}
