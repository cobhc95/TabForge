using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge.Controllers;

// Owns: the timeline's bar-range commands (clear, remove and close the gap, insert a gap before / after): the Delete prompt and its remembered answer, the clip
//     warning, one undo step per command, and the selection afterwards.
// Does not own: the bar and clip edits (BarRangeGaps), the prompt window (BarRangePrompt) or key maps (HotkeyMaps).
// Tests: TestBarRangeGaps.
internal sealed class BarRangeFlow
{
    private readonly ISectionEditHost _host;

    public BarRangeFlow(ISectionEditHost host) => _host = host;

    /// <summary>The command for a key while bars are selected and the timeline (not a clip, the score or a text box) has the focus; null otherwise.</summary>
    public static string? Route(bool timelineFocused, bool hasRange, bool clipContextActive, IReadOnlyDictionary<string, string> rangeMap, string gesture) =>
        timelineFocused && !clipContextActive && rangeMap.TryGetValue(gesture, out var id) ? id : null;

    /// <summary>The action a Range.* command id stands for; null for Range.Delete (the prompt) and other ids.</summary>
    public static BarRangeAction? ActionOf(string id) => Enum.GetValues<BarRangeAction>().Cast<BarRangeAction?>().FirstOrDefault(a => BarRangePromptText.CommandOf(a!.Value) == id);

    /// <summary>Runs a Range.* command on the selected bars (direct commands act on every track); false when the id is not one.</summary>
    public bool RunCommand(DocumentSession doc, string id, int start, int end)
    {
        if (id == "Range.Delete") { Delete(doc, start, end); return true; }
        if (ActionOf(id) is not { } action) return false;
        Run(doc, action, start, end, allTracks: true, askAboutClips: true);
        return true;
    }

    /// <summary>
    /// Delete on selected bars: the remembered answer (action and scope) runs directly; otherwise the prompt asks (the last choice preselected, "All tracks"
    /// preselected: a selection on the timeline covers every track unless "This track" is picked) and "Remember my answer" stores the action and scope.
    /// Cancel changes nothing.
    /// </summary>
    public void Delete(DocumentSession doc, int start, int end)
    {
        if (!_host.IsShown(doc)) return;
        var editing = _host.Settings.Editing;
        if (BarRangePromptText.Remembered(editing.BarRangeDelete) is { } remembered) { Run(doc, remembered.Action, start, end, remembered.AllTracks, askAboutClips: true); return; }
        var text = start == end ? $"Bar {start + 1} is selected. What should Delete do?" : $"Bars {start + 1}-{end + 1} are selected. What should Delete do?";
        if (BarRangeGaps.ClipsUnder(doc.Project, start, end, -1) is { } impact)
            text += " " + ClipDeleteImpact.DescribeRange(impact);
        var last = Enum.TryParse<BarRangeAction>(editing.BarRangeLastChoice, out var previous) ? previous : BarRangeAction.Clear;
        if (_host.AskBarRange(text, last, allTracks: true) is not { } answer) return;
        editing.BarRangeLastChoice = answer.Action.ToString();
        if (answer.Remember) editing.BarRangeDelete = BarRangePromptText.Store(answer.Action, answer.AllTracks);
        _host.SaveSettings();
        Run(doc, answer.Action, start, end, answer.AllTracks, askAboutClips: false);
    }

    private int TrackScope(DocumentSession doc, bool allTracks) =>
        allTracks || doc.Project.Tracks.Count <= 1 ? -1 : Math.Clamp(_host.SelectedTrackIndex, 0, doc.Project.Tracks.Count - 1);

    /// <summary>Runs one action as one undo step. <paramref name="askAboutClips"/>: a remove that cuts or drops clips asks first (the prompt already said so).</summary>
    public void Run(DocumentSession doc, BarRangeAction action, int start, int end, bool allTracks, bool askAboutClips)
    {
        if (!_host.IsShown(doc)) return;
        var project = doc.Project;
        var bars = BarRangeEditor.MaxMeasures(project);
        if (start < 0 || start >= bars) return;
        end = Math.Clamp(end, start, bars - 1);
        var track = TrackScope(doc, allTracks);
        var fill = _host.Settings.Editing.FillBarsWithRests;
        var label = start == end ? $"bar {start + 1}" : $"bars {start + 1}-{end + 1}";
        var scope = track < 0 ? "" : $" on {project.Tracks[track].Name}";
        int[]? map = null;
        var clipsChanged = false;
        var (at, count) = (0, 0);
        switch (action)
        {
            case BarRangeAction.Clear:
                if (!DocumentEdits.Run(doc, p => BarRangeGaps.Clear(p, start, end, track, fill)).Changed) return;
                _host.FinishBarEdit($"Cleared {label}{scope}");
                _host.SelectBars(start, end);
                return;
            case BarRangeAction.Remove:
                if (askAboutClips && BarRangeGaps.ClipsUnder(project, start, end, track) is { } impact
                    && !_host.Ask("Remove bars", ClipDeleteImpact.DescribeRange(impact), "Continue", withUndoHint: false)) return;
                var removed = DocumentEdits.Run(doc, p =>
                {
                    if (BarRangeGaps.Remove(p, start, end, track) is not { } r) return false;
                    (map, clipsChanged) = r;
                    return true;
                });
                if (!removed.Changed) { if (track < 0) _host.ShowLastBarWarning(); return; }
                if (clipsChanged) _host.SyncAudioEngine();
                if (map is null) { _host.FinishBarEdit($"Removed {label}{scope} and closed the gap"); _host.SelectBars(start, end); return; }
                _host.EndLoop();
                _host.ClearSelection();
                doc.SkipRanges.Clear();
                _host.SyncAreaVisuals();
                var left = BarRangeEditor.MaxMeasures(project);
                _host.SetCaret(Math.Clamp(start, 0, Math.Max(0, left - 1)), 0, _host.Caret.String);
                _host.FinishStructureEdit($"Removed {label} and closed the gap", map, Math.Min(start, left - 1));
                return;
            default:
                var before = action == BarRangeAction.InsertBefore;
                var inserted = DocumentEdits.Run<int[]>(doc, p =>
                {
                    if (BarRangeGaps.InsertGap(p, start, end, before, track, fill) is not { } g) return null;
                    (at, count) = (g.At, g.Count);
                    clipsChanged = p.Tracks.Any(t => t.AudioClips.Count > 0);
                    return g.Map;
                });
                if (inserted.Value is not { } insertMap) return;
                if (clipsChanged) _host.SyncAudioEngine();
                _host.FinishStructureEdit($"Inserted a gap of {count} bar{(count == 1 ? "" : "s")} {(before ? "before" : "after")} {label}{scope}", insertMap, null);
                if (before) _host.SelectBars(start + count, end + count);
                else _host.SelectBars(start, end);
                return;
        }
    }
}
