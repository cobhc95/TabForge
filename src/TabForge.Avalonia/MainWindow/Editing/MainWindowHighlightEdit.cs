using System;
using System.IO;
using System.Threading.Tasks;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: the commands on the grey timeline highlight: Highlight.Copy (exactly the highlighted cells, never asks; a non-rectangular Ctrl-selection shows "Copy needs a rectangular selection" and does nothing), Highlight.Delete and Highlight.Cut (the question "Clear the notes in N highlighted bars? The bars stay."
//   unless "Don't ask again" was ticked, then one undo step that clears the notes of the highlighted tab cells), Highlight.Paste (the copied cells overwrite from the top-left highlighted cell, bars are added past the
//   end, one undo step) and Highlight.SelectBars (the highlighted bars become the all-tracks range selection).
// Does not own: the highlight (BarHighlight), the cell rules (HighlightEdits), the paste rules (RangePaste), the question window (PromptWindow) or the key routing (EditRouting, MainWindowEditRouting).
// Tests: TabForge.Avalonia.Tests.TimelineBarHighlightEditTests.
internal static class MainWindowHighlightEdit
{
    public const string CopyId = "Highlight.Copy", CutId = "Highlight.Cut", DeleteId = "Highlight.Delete", PasteId = "Highlight.Paste";

    /// <summary>Asks whether to clear <paramref name="cells"/> highlighted bars: the answer, and whether "Don't ask again" was ticked; Cancel is (false, false).</summary>
    public delegate Task<(bool Confirmed, bool DontAskAgain)> ClearPrompt(int cells);

    public static void Register(MainWindowCommands commands, MainWindowTabs songs, MainWindowClipboard flow, Action<string> notice, ClearPrompt ask, Func<EditingSettings> editing, Action saveSettings)
    {
        bool Has() => songs.Active.Highlight.Has;
        commands.AddWhen(CopyId, () => Copy(songs.Active, flow.Clipboard, notice), Has);
        commands.AddWhen(CutId, () => Platform.UiTasks.Forget(ClearAsync(songs.Active, flow.Clipboard, notice, ask, editing, saveSettings), "Cut", notice), Has);
        commands.AddWhen(DeleteId, () => Platform.UiTasks.Forget(ClearAsync(songs.Active, null, notice, ask, editing, saveSettings), "Delete", notice), Has);
        commands.AddWhen(PasteId, () => Platform.UiTasks.Forget(PasteAsync(songs.Active, flow, notice), "Paste", notice), Has);
        commands.AddWhen(Views.ContextMenuRows.SelectHighlightedBarsId, () => SelectBars(songs.Active), Has);
    }

    /// <summary>Copies exactly the highlighted cells to <paramref name="clipboard"/>; never asks. False (with a notice) when nothing could be captured.</summary>
    internal static bool Copy(IEditableDocument document, ClipboardService clipboard, Action<string> notice)
    {
        if (document.Highlight.ExtraCount > 0) { notice(NeedsRectangle("Copy")); return false; }
        if (Capture(document, notice) is not { } snapshot) return false;
        clipboard.CopyRange(snapshot);
        var cells = HighlightEdits.CellCount(document.Project, document.Highlight, includeExtras: false);   // the clipboard holds the rectangle only
        notice($"Copied {cells} highlighted {(cells == 1 ? "bar" : "bars")}");
        return true;
    }

    /// <summary>
    /// Clears the notes of the highlighted cells (the bars stay) as one undo step, after the question unless it was turned off. With <paramref name="cutTo"/> it is a Cut: the cells are captured first and put on
    /// that clipboard once the question was answered Yes. Returns true when the song changed.
    /// </summary>
    internal static async Task<bool> ClearAsync(IEditableDocument document, ClipboardService? cutTo, Action<string> notice, ClearPrompt ask, Func<EditingSettings> editing, Action saveSettings)
    {
        if (cutTo is not null && document.Highlight.ExtraCount > 0) { notice(NeedsRectangle("Cut")); return false; }
        var cells = HighlightEdits.CellCount(document.Project, document.Highlight, includeExtras: cutTo is null);   // a Cut removes exactly what Copy captured (the rectangle)
        if (cells == 0) return false;
        RangeSnapshot? snapshot = null;
        if (cutTo is not null && (snapshot = Capture(document, notice)) is null) return false;
        var settings = editing();
        if (settings.ConfirmHighlightClear)
        {
            var (confirmed, dontAskAgain) = await ask(cells);
            if (!confirmed) return false;
            if (dontAskAgain)
            {
                settings.ConfirmHighlightClear = false;
                saveSettings();
            }
        }
        var highlight = document.Highlight;
        var changed = DocumentEdits.Run(document, project => HighlightEdits.Clear(project, highlight, settings.FillBarsWithRests, includeExtras: cutTo is null), label: cutTo is null ? "Clear highlighted bars" : "Cut highlighted bars").Changed;
        if (snapshot is not null) cutTo!.CopyRange(snapshot);
        return changed;
    }

    /// <summary>Pastes the copied cells from the top-left highlighted cell, the first copied track on the highlighted track, as one undo step (bars are added past the end); the highlight then covers the pasted cells.</summary>
    internal static async Task<bool> PasteAsync(IEditableDocument document, MainWindowClipboard flow, Action<string> notice)
    {
        var highlight = document.Highlight;
        if (!highlight.Has) return false;
        if (await flow.RangeToPasteAsync() is not { } snapshot)
        {
            notice("Copy highlighted bars first");
            return false;
        }
        var (bar, track) = (highlight.FirstBar, highlight.FirstTrack);
        RangePasteResult? result = null;
        DocumentEdits.Run(document, project =>
        {
            result = RangePaste.Apply(project, snapshot, bar, track, new RangePasteOptions(What: RangePasteWhat.Tabs));
            if (!result.Changed) return false;
            highlight.SetBlock(track, bar, Math.Min(track + snapshot.Tracks.Count - 1, project.Tracks.Count - 1), result.LastBar);   // inside the edit: the paste's one undo step carries the new block too
            return true;
        }, label: "Paste highlighted bars");
        if (result is not { Changed: true })
        {
            if (result?.Notice is { } why) notice(why);
            return false;
        }
        var bars = result.LastBar - bar + 1;
        notice($"Pasted {bars} {(bars == 1 ? "bar" : "bars")} at bar {bar + 1}" + (result.Notice is { } extra ? "; " + extra : ""));
        return true;
    }

    // The highlighted bars become the loop region, one undo step (the highlight stays).
    private static void SelectBars(IEditableDocument document)
    {
        var highlight = document.Highlight;
        DocumentEdits.EditLoop(document, model => model.SetLoop(highlight.FirstBar, highlight.LastBar), "Loop highlighted bars");
    }

    private static string NeedsRectangle(string verb) => verb + " needs a rectangular selection";

    private static RangeSnapshot? Capture(IEditableDocument document, Action<string> notice)
    {
        try { return HighlightEdits.Capture(document.Project, document.Highlight); }
        catch (InvalidDataException ex) { notice(ex.Message); return null; }
    }
}
