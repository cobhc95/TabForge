using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TabForge.Presenters;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: the timeline bar menu's Copy bar, Paste bar and Delete bar rows for the Avalonia window (this track, and every track): capturing the right-clicked bar with the
//   Core timeline clip calls and putting it on the score clipboard. Copying records no undo step; a paste (Core TimelineClips.PasteBars, recommended
//   paste questions asked through PasteQuestionFlow and the paste questions window) is one undo step through DocumentEdits.Run; Delete bar runs the
//   remembered answer (EditingSettings.BarRangeDelete) or asks through the bar-range prompt (last choice preselected, "Remember my choice" stored)
//   and runs the picked TimelineRangeEdit mode as one undo step.
// Does not own: the menu rows (TimelineMenus in Core), the row-to-id mapping (ContextMenuRows), the clipboard format (ClipboardService), the
//   prompt window (BarRangePromptWindow), the paste questions window (PasteOptionsWindow), saving the settings file (the caller's saveSettings).
// Tests: TabForge.Avalonia.Tests.TimelineBarCopyMenuTests.
public static class MainWindowTimelineBars
{
    /// <summary>The command ids this class handles.</summary>
    public static readonly IReadOnlyList<string> Commands = new[] { ContextMenuRows.BarCopyId, ContextMenuRows.BarCopyAllTracksId,
        ContextMenuRows.BarPasteId, ContextMenuRows.BarPasteAllTracksId,
        ContextMenuRows.BarDeleteId, ContextMenuRows.BarDeleteAllTracksId };

    /// <summary>Shows the Delete-bars prompt (question, preselected action, all tracks) and returns the answer, or null on Cancel.</summary>
    public delegate Task<BarRangeAnswer?> DeletePrompt(string question, BarRangeAction preselect, bool allTracks);

    /// <summary>Adds the Copy bar and Copy bar (all tracks) commands over the shown song and the bar menu's clicked bar and track.</summary>
    public static void Register(MainWindowCommands commands, MainWindowTabs songs, ContextMenuHost menus, Action<string> notice,
        DeletePrompt askDelete, Func<EditingSettings> editing, Action saveSettings, PasteQuestionPrompt askPaste)
    {
        commands.Add(ContextMenuRows.BarDeleteId, () => _ = DeleteBar(songs.Active, menus, allTracks: false, notice, askDelete, editing, saveSettings));
        commands.Add(ContextMenuRows.BarDeleteAllTracksId, () => _ = DeleteBar(songs.Active, menus, allTracks: true, notice, askDelete, editing, saveSettings));
        commands.Add(ContextMenuRows.BarCopyId, () => CopyBar(songs.Active, menus, allTracks: false, notice));
        commands.Add(ContextMenuRows.BarCopyAllTracksId, () => CopyBar(songs.Active, menus, allTracks: true, notice));
        commands.Add(ContextMenuRows.BarPasteId, () => _ = PasteBar(songs.Active, menus, TimelinePasteKind.OverwriteThisTrack, notice, editing, askPaste));
        commands.Add(ContextMenuRows.BarPasteAllTracksId, () => _ = PasteBar(songs.Active, menus, TimelinePasteKind.OverwriteAllTracks, notice, editing, askPaste));
    }

    /// <summary>Shows the paste questions window for the given questions and returns the answers, or null on Cancel.</summary>
    public delegate Task<PasteAnswers?> PasteQuestionPrompt(IReadOnlyCollection<PasteQuestion> questions);

    /// <summary>
    /// Pastes the clipboard's bars at the clicked bar, on the clicked track or every mapped track, as one undo step; the paste questions that apply
    /// (remembered answers in <paramref name="editing"/>) are asked through <paramref name="askPaste"/>, and Cancel changes nothing.
    /// </summary>
    public static async Task PasteBar(IEditableDocument document, ContextMenuHost menus, TimelinePasteKind kind, Action<string> notice,
        Func<EditingSettings> editing, PasteQuestionPrompt askPaste)
    {
        var bar = menus.ClickedBar;
        var track = menus.ClickedTrack;
        var clip = menus.Clipboard?.TryGetClip(out _);
        if (bar < 0 || clip is null) return;
        var settings = editing();
        var message = "";
        await PasteQuestionFlow.RunAsync(asker =>
        {
            var changed = DocumentEdits.Run(document, project =>
            {
                var result = TimelineClips.PasteBars(project, clip, bar, track, kind, settings, asker);
                message = result.Message;
                return result.Changed;
            }).Changed;
            return new PasteOutcome { Changed = changed };
        }, questions => askPaste(questions));
        if (message.Length > 0) notice(message);
    }

    /// <summary>Asks what Delete does to the clicked bar, then runs the picked action on the clicked track or every track as one undo step.</summary>
    public static async Task DeleteBar(IEditableDocument document, ContextMenuHost menus, bool allTracks, Action<string> notice,
        DeletePrompt askDelete, Func<EditingSettings> editing, Action saveSettings) =>
        await DeleteRange(document, menus.ClickedBar, menus.ClickedBar, menus.ClickedTrack, allTracks, notice, askDelete, editing, saveSettings);

    /// <summary>
    /// Asks what Delete does to bars <paramref name="first"/>…<paramref name="last"/> (or runs the remembered answer), then applies the picked
    /// TimelineRangeEdit mode (leave empty bars or remove them) to every track or to <paramref name="trackIndex"/> as one undo step. Returns the action that ran, or null on Cancel or
    /// when nothing changed. Shared by the bar menu's Delete bar rows and the Range.Delete key command.
    /// </summary>
    /// <param name="onlyTracks">When set (the bars of selected clips), the edit runs on exactly these tracks and the answer's all-tracks choice is ignored.</param>
    public static async Task<BarRangeAction?> DeleteRange(IEditableDocument document, int first, int last, int trackIndex, bool allTracksDefault,
        Action<string> notice, DeletePrompt askDelete, Func<EditingSettings> editing, Action saveSettings, IReadOnlyList<int>? onlyTracks = null)
    {
        var tracks = document.Project.Tracks.Count;
        if (first < 0 || last < first || last >= BarRangeEditor.MaxMeasures(document.Project)) return null;
        var settings = editing();
        BarRangeAnswer answer;
        if (BarRangePromptText.Remembered(settings.BarRangeDelete) is { } remembered) answer = new BarRangeAnswer(remembered.Action, remembered.AllTracks, Remember: true);
        else
        {
            var bars = first == last ? $"Bar {first + 1} is selected." : $"Bars {first + 1}-{last + 1} are selected.";
            var question = bars + " What should Delete do?";
            if (BarRangeGaps.ClipsUnder(document.Project, first, last, -1) is { } impact) question += " " + ClipDeleteImpact.DescribeRange(impact);
            var previous = Enum.TryParse<BarRangeAction>(settings.BarRangeLastChoice, out var previousChoice) && BarRangePromptText.DeleteActions.Contains(previousChoice) ? previousChoice : BarRangeAction.Clear;
            if (await askDelete(question, previous, allTracksDefault) is not { } asked) return null;
            answer = asked;
            settings.BarRangeLastChoice = answer.Action.ToString();
            if (answer.Remember) settings.BarRangeDelete = BarRangePromptText.Store(answer.Action, answer.AllTracks);
            saveSettings();
        }
        var track = answer.AllTracks || tracks <= 1 ? -1 : Math.Clamp(trackIndex, 0, tracks - 1);
        return ApplyRangeEdit(document, first, last, answer.Action, onlyTracks ?? new[] { track }, settings.FillBarsWithRests, notice);
    }

    /// <summary>Clears or removes bars <paramref name="first"/>…<paramref name="last"/> (every track, or only <paramref name="track"/>) as one undo step and reports it. Returns the action that ran, or null when nothing changed.</summary>
    public static BarRangeAction? ApplyRangeEdit(IEditableDocument document, int first, int last, BarRangeAction action, int track, bool fillWithRests, Action<string> notice) =>
        ApplyRangeEdit(document, first, last, action, new[] { track }, fillWithRests, notice);

    /// <summary>As above on each of <paramref name="tracks"/> (-1: every track), all in one undo step.</summary>
    public static BarRangeAction? ApplyRangeEdit(IEditableDocument document, int first, int last, BarRangeAction action, IReadOnlyList<int> tracks, bool fillWithRests, Action<string> notice)
    {
        var mode = action == BarRangeAction.Remove ? TimelineDeleteMode.Remove : TimelineDeleteMode.LeaveEmpty;
        string? refusal = null;
        var changed = DocumentEdits.Run(document, project =>
        {
            var any = false;
            foreach (var track in tracks)
            {
                var one = TimelineRangeEdit.Apply(project, first, last, mode, track, fillWithRests);
                any |= one.Changed;
                refusal ??= one.Refusal;
            }
            return any;
        }).Changed;
        if (refusal is not null) notice(refusal);
        if (!changed) return null;
        notice(first == last
            ? $"{BarRangePromptText.Label(action)}: bar {first + 1}"
            : $"{BarRangePromptText.Label(action)}: bars {first + 1}-{last + 1}");
        return action;
    }

    /// <summary>Copies the clicked bar of the clicked track, or of every track; nothing happens when no bar or track was clicked.</summary>
    private static void CopyBar(IEditableDocument document, ContextMenuHost menus, bool allTracks, Action<string> notice)
    {
        var bar = menus.ClickedBar;
        var track = menus.ClickedTrack;
        var tracks = document.Project.Tracks;
        if (bar < 0 || (!allTracks && (track < 0 || track >= tracks.Count))) return;
        var clip = TimelineClips.CopyBar(document.Project, bar, track, allTracks);
        if (clip.BarCount == 0) return;
        menus.Clipboard?.Copy(clip);
        notice(allTracks ? $"Copied bar {bar + 1} from all tracks" : $"Copied bar {bar + 1} from {tracks[track].Name}");
    }
}
