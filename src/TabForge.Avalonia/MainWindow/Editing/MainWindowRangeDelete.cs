using System;
using System.IO;
using System.Threading.Tasks;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: the three commands of bars selected on the timeline: Range.Copy (RangeCapture -> the clipboard, never asks), Range.Delete (the Delete-bars question, or the remembered
//   Editing.BarRangeDelete answer, then one undo step through TimelineRangeEdit on all tracks, and the selection/loop adjustment afterwards) and Range.Cut (the snapshot is taken first,
//   then exactly the Delete flow; the clipboard gets it only after the delete went through, so Cancel or a refusal leaves the old clipboard). A score (ThisTrack) selection never reaches
//   them: the score keeps its own copy, cut and delete.
// Does not own: the command table / key routing (MainWindowCommands, MainWindowKeyboard, EditRouting), the prompt window (BarRangePromptWindow), the delete rules (TimelineRangeEdit),
//   the snapshot (RangeCapture, ClipboardService), the shared selection (SelectionModel) or the bar menu single-bar Delete (MainWindowTimelineBars.DeleteRange, which this reuses).
// Tests: TabForge.Avalonia.Tests.TimelineDeleteRoutingTests, TimelineRangeDeleteTests, RangeClipboardTests.
internal static class MainWindowRangeDelete
{
    /// <summary>The command id of Delete; Cut and Copy are <see cref="EditRouting.RangeCutId"/> and <see cref="EditRouting.RangeCopyId"/>.</summary>
    public const string DeleteId = "Range.Delete";

    public static void Register(MainWindowCommands commands, MainWindowTabs songs, MainWindowDocumentPanes panes, ClipboardService clipboard, Action<string> notice,
        MainWindowTimelineBars.DeletePrompt askDelete, Func<EditingSettings> editing, Action saveSettings)
    {
        bool HasRange() => SelectionBars.For(panes.Selection.Model, songs.Active.Project) is not null;
        commands.AddWhen(DeleteId, () => _ = DeleteAsync(songs.Active, panes.Selection.Model, null, notice, askDelete, editing, saveSettings), HasRange);
        commands.AddWhen(EditRouting.RangeCutId, () => _ = DeleteAsync(songs.Active, panes.Selection.Model, clipboard, notice, askDelete, editing, saveSettings), HasRange);
        commands.AddWhen(EditRouting.RangeCopyId, () => Copy(songs.Active.Project, panes.Selection.Model, clipboard, notice), HasRange);
    }

    /// <summary>Copies the bars of SelectionBars (the loop on every track, else the selected clips' tracks) to <paramref name="clipboard"/>; never asks. False (with a notice) when nothing could be captured.</summary>
    internal static bool Copy(SongProject project, SelectionModel model, ClipboardService clipboard, Action<string> notice)
    {
        if (Capture(project, model, notice) is not { } snapshot) return false;
        clipboard.CopyRange(snapshot);
        notice(Describe("Copied", snapshot));
        return true;
    }

    /// <summary>
    /// Asks what Delete does to the selected bars and applies it as one undo step. With <paramref name="cutTo"/> it is a Cut: the bars are captured first and put on that clipboard only
    /// once the delete went through. The bars are SelectionBars: the loop (every track) or the selected clips on their tracks. Returns true when the song changed
    /// (removed bars move or drop the loop through the edit itself, so undo brings it back).
    /// </summary>
    internal static async Task<bool> DeleteAsync(IEditableDocument document, SelectionModel model, ClipboardService? cutTo, Action<string> notice,
        MainWindowTimelineBars.DeletePrompt askDelete, Func<EditingSettings> editing, Action saveSettings)
    {
        if (SelectionBars.For(model, document.Project) is not { } range) return false;
        RangeSnapshot? snapshot = null;
        if (cutTo is not null && (snapshot = Capture(document.Project, model, notice)) is null) return false;
        var action = await MainWindowTimelineBars.DeleteRange(document, range.Start, range.End, range.FirstTrack, allTracksDefault: range.Tracks is null, notice, askDelete,
            editing, saveSettings, range.Tracks);
        if (action is null) return false;
        if (snapshot is not null) cutTo!.CopyRange(snapshot);
        return true;
    }

    private static RangeSnapshot? Capture(SongProject project, SelectionModel model, Action<string> notice)
    {
        if (SelectionBars.For(model, project) is not { } range) return null;
        try { return range.Tracks is { Count: > 0 } t ? RangeCapture.Capture(project, range.Start, range.End, t[0], t[^1]) : RangeCapture.Capture(project, range.Start, range.End); }
        catch (InvalidDataException ex) { notice(ex.Message); return null; }
    }

    private static string Describe(string verb, RangeSnapshot snapshot) =>
        snapshot.BarCount == 1 ? $"{verb} bar {snapshot.FirstBar + 1} of all tracks" : $"{verb} bars {snapshot.FirstBar + 1}-{snapshot.FirstBar + snapshot.BarCount} of all tracks";
}
