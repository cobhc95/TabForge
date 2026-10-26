using System.Collections.Generic;
using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Menus;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: the track row right-click rows that act on the track under the last track row right-click (ContextMenuHost.ClickedTrack): Copy, Cut (which asks the Delete track question first),
//   Paste (after that track), Duplicate, Colour (one row per palette swatch; the row's argument is its hex), Rename (opens the inline name editor
//   on the latest arrangement) and Convert to audio track (after its prompt). Each edit is one undo step through DocumentEdits; copy and paste go
// through ClipboardService.
// Does not own: the menu rows (TrackRowMenus), the Delete and Properties rows (MainWindowTrackMenu), the instrument-to-audio edit
//   (TrackEdits), the unique copy name (TrackEdits.UniqueName), the track list refresh, the name edit itself (TrackNameEditor).
// Tests: TabForge.Avalonia.Tests.MainWindowTrackRowMenuTests.
/// <summary>The track row menu's copy, cut, paste, duplicate, colour and convert rows, bound to the shown song.</summary>
public static class MainWindowTrackRowEdits
{
    private const string CutRefusedText = "The track could not be cut";
    private const string PasteTargetText = "Right-click a track to paste after it";

    /// <summary>The command ids this class handles: the track row menu's rows that it runs.</summary>
    public static readonly IReadOnlyList<string> Commands = new[]
    {
        TrackRowMenus.Copy, TrackRowMenus.Cut, TrackRowMenus.Paste, TrackRowMenus.Duplicate, TrackRowMenus.Colour, TrackRowMenus.Rename,
        TrackRowMenus.ConvertToAudio,
    };

    /// <param name="songs">The tabs; the commands act on the shown song.</param>
    /// <param name="panes">The panes; Rename opens the inline name editor on the latest arrangement.</param>
    /// <param name="owner">The window the convert prompt opens over.</param>
    /// <param name="rows">The host of the track row menus; its ClickedTrack is the track the rows act on.</param>
    /// <param name="clipboard">The window's clipboard: Copy and Cut put a track on it, Paste takes one off.</param>
    /// <param name="status">The notice Cut shows when it refuses the last track; null stays silent.</param>
    public static void Register(MainWindowCommands commands, MainWindowTabs songs, MainWindowDocumentPanes panes, Window owner, ContextMenuHost rows,
        ClipboardService clipboard, Action<string>? status = null, EditRouting? routing = null)
    {
        commands.Add(TrackRowMenus.Copy, () => Copy(songs.Active, clipboard, rows.ClickedTrack));
        Action<string> say = status ?? (_ => { });
        commands.Add(TrackRowMenus.Cut, () => UiTasks.Forget(CutAsync(songs.Active, owner, rows, clipboard, status), "Cut track", say));
        commands.Add(TrackRowMenus.Paste, () => { if (!RunRangePaste(commands, panes.Selection.Model, clipboard, routing)) Paste(songs.Active, clipboard, rows.ClickedTrack, say); });
        commands.Add(TrackRowMenus.Duplicate, () => Duplicate(songs.Active, rows.ClickedTrack));
        commands.AddWithArgument(TrackRowMenus.Colour, hex => SetColour(songs.Active, rows.ClickedTrack, hex));
        commands.Add(TrackRowMenus.Rename, () => panes.Arrangement?.BeginTrackRename(rows.ClickedTrack));
        commands.Add(TrackRowMenus.ConvertToAudio, () => UiTasks.Forget(ConvertToAudioAsync(songs.Active, owner, rows), "Convert to audio track", say));
    }

    /// <summary>With a track header focused and a RANGE the newest copy, EditRouting sends Paste to the range paste (starting at that track); true when it ran. A copied track keeps the whole-track paste.</summary>
    private static bool RunRangePaste(MainWindowCommands commands, SelectionModel model, ClipboardService clipboard, EditRouting? routing)
    {
        if (routing is null || model.Focus != FocusTarget.TrackRow) return false;
        var state = new EditSelectionState(HasTrackRow: model.TrackRowCount > 0, ClipboardHasTrack: clipboard.HasTrack, ClipboardHasRange: clipboard.TryGetRange() is not null);
        if (routing.Resolve(model.Focus, state, EditVerb.Paste).CommandId is not { } id || id == TrackRowMenus.Paste || !commands.Handles(id) || !commands.Applies(id)) return false;
        commands.Run(id);
        return true;
    }

    /// <summary>Puts a copy of the track at <paramref name="index"/> (or of every selected track when it is one of them) on the clipboard; the song does not change.</summary>
    private static void Copy(IEditableDocument document, ClipboardService clipboard, int index) =>
        clipboard.CopyTracks(MainWindowTrackMenu.TargetsOf(document, index).Select(i => document.Project.Tracks[i]));

    /// <summary>Asks the Delete track question once; only on OK copies the track(s) and removes them (one undo step). Cancel leaves the song and the clipboard as they were. The last track stays.</summary>
    private static async Task CutAsync(IEditableDocument document, Window owner, ContextMenuHost rows, ClipboardService clipboard, Action<string>? status)
    {
        var targets = MainWindowTrackMenu.TargetsOf(document, rows.ClickedTrack);
        if (!await MainWindowTrackMenu.ConfirmDeleteAsync(document, owner, targets, rows, status)) return;
        var cut = targets.Select(i => document.Project.Tracks[i]).ToList();
        if (MainWindowTrackMenu.RemoveTracks(document, targets.ToList()).Changed) clipboard.CopyTracks(cut);   // the clipboard changes only when the tracks really went
        else status?.Invoke(CutRefusedText);
    }

    /// <summary>Inserts the copied tracks, in order, after the track at <paramref name="index"/> under unique names (one undo step).</summary>
    private static void Paste(IEditableDocument document, ClipboardService clipboard, int index, Action<string> say)
    {
        if (!clipboard.HasTrack) return;
        if (TrackAt(document, index) is null) { say(PasteTargetText); return; }
        if (clipboard.TryGetTracks() is { Count: > 0 } copies) InsertAfter(document, index, copies);
    }

    /// <summary>Inserts a copy of the track right after itself under a unique name (one undo step). The clipboard keeps what it held.</summary>
    public static void Duplicate(IEditableDocument document, int index)
    {
        if (TrackAt(document, index) is not { } original) return;
        var holder = new ClipboardService(null);
        holder.CopyTrack(original);
        if (holder.TryGetTrack() is { } copy) InsertAfter(document, index, new[] { copy });
    }

    /// <summary>Sets the colour of the track at <paramref name="index"/> to the swatch's hex (one undo step).</summary>
    private static void SetColour(IEditableDocument document, int index, string hex) =>
        DocumentEdits.Run(document, project =>
        {
            if (index < 0 || index >= project.Tracks.Count) return false;
            project.Tracks[index].ColorHex = hex;
            return true;
        });

    /// <summary>Track.ConvertToAudio: an instrument track becomes an audio track after the prompt (one undo step).</summary>
    private static async Task ConvertToAudioAsync(IEditableDocument document, Window owner, ContextMenuHost rows)
    {
        if (TrackAt(document, index: rows.ClickedTrack) is not { IsAudio: false } track) return;
        var undoHint = HotkeyCatalog.DisplayAll(rows.Hotkeys(), MainWindowTrackMenu.UndoCommandId);
        var outcome = await PromptWindow.AskAsync(owner, PromptTextsPresenter.ConvertTrackToAudio(track.Name, undoHint));
        if (PromptTextsPresenter.IsConfirmed(outcome.Answer)) TrackEdits.ConvertInstrumentToAudio(document, track);
    }

    /// <summary>Inserts <paramref name="copies"/> after the track at <paramref name="after"/> under unique names (one undo step).</summary>
    private static void InsertAfter(IEditableDocument document, int after, IReadOnlyList<TrackModel> copies) =>
        DocumentEdits.Run(document, project =>
        {
            if (after < 0 || after >= project.Tracks.Count) return false;
            var at = after + 1;
            foreach (var copy in copies)
            {
                copy.Name = TrackEdits.UniqueName(project, copy.Name);
                project.Tracks.Insert(at++, copy);
            }
            return true;
        });

    private static TrackModel? TrackAt(IEditableDocument document, int index)
    {
        var tracks = document.Project.Tracks;
        return index >= 0 && index < tracks.Count ? tracks[index] : null;
    }
}
