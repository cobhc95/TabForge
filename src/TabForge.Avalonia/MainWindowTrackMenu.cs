using System.Collections.Generic;
using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: the handlers of the Track menu on the shown song: Add track (the Track properties window in add mode), Add Bass, Add Drums and
//   Add Keys (a new track of that kind at the end), Delete track (after the Delete prompt; the last track stays), Move track up / down, and
//   Track.Next / Track.Previous (the selected track one row down / up).
//   The track row menu's Delete and Properties rows act on the track under the last track row right-click (ContextMenuHost.ClickedTrack).
//   The edits run through DocumentEdits; a new track is built by NewTrackFactory (TabForge.Core).
// Does not own: the menu rows (MainMenuTable in Core), the commands table (MainWindowCommands), the track list rows (MainWindowTrackListRows),
//   the Add track strip of the arrangement (ArrangementView).
// Tests: TabForge.Avalonia.Tests.MainWindowTrackMenuTests.
/// <summary>The Track menu's commands, bound to the shown song.</summary>
public static class MainWindowTrackMenu
{
    /// <summary>The undo key the Delete track prompt names; the default preset's Undo gesture.</summary>
    internal const string UndoCommandId = "Edit.Undo";

    /// <summary>The status notice deleting the last track shows.</summary>
    private const string CannotDeleteLastTrackText = "Cannot delete the last track";

    /// <param name="songs">The tabs; the commands act on the shown song.</param>
    /// <param name="owner">The window the prompts and the Add track window open over.</param>
    /// <param name="rows">The host of the track row menus; its ClickedTrack is the track the TrackRow rows act on.</param>
    /// <param name="status">The status notice deleting the last track shows; null stays silent (the Compose call leaves it unset).</param>
    /// <param name="picker">How the Track properties window's Change instrument button sounds the choice; null stays silent.</param>
    /// <summary>The Track menu command ids this class handles, and the track row menu's Delete and Properties ids.</summary>
    public static readonly IReadOnlyList<string> Commands = new[] { "Track.Add", "Track.Properties", "Track.AddBass", "Track.AddDrums", "Track.AddKeys", "Track.Delete", "Track.MoveUp", "Track.MoveDown", "Track.Next", "Track.Previous", "TrackRow.Delete", "TrackRow.Properties" };

    public static void Register(MainWindowCommands commands, MainWindowTabs songs, Window owner, ContextMenuHost rows, Action<string>? status = null, InstrumentPickerFlow? picker = null)
    {
        Action<string> say = status ?? (_ => { });
        commands.Add("Track.Add", () => UiTasks.Forget(AddInstrumentTrackAsync(songs.Active, owner), "Add track", say));
        commands.Add("Track.Properties", () => UiTasks.Forget(ShowTrackPropertiesAsync(songs.Active, owner, SelectedIndex(songs.Active), picker), "Track properties", say));
        commands.Add("Track.AddBass", () => AddTrack(songs.Active, TrackKind.Bass, DefaultChainOf(owner)));
        commands.Add("Track.AddDrums", () => AddTrack(songs.Active, TrackKind.Drums, DefaultChainOf(owner)));
        commands.Add("Track.AddKeys", () => AddTrack(songs.Active, TrackKind.Keys, DefaultChainOf(owner)));
        commands.Add("Track.Delete", () => UiTasks.Forget(DeleteTrackAsync(songs.Active, owner, SelectedIndex(songs.Active), rows, status), "Delete track", say));
        commands.Add("Track.MoveUp", () => MoveSelectedTrack(songs.Active, -1));
        commands.Add("Track.MoveDown", () => MoveSelectedTrack(songs.Active, 1));
        commands.Add("Track.Next", () => StepSelectedTrack(songs.Active, 1));
        commands.Add("Track.Previous", () => StepSelectedTrack(songs.Active, -1));
        // A track row key runs on the selected track (ArrangementView points ClickedTrack at the clicked row); the row menu sets it too.
        commands.Add("TrackRow.Delete", () => UiTasks.Forget(DeleteTrackAsync(songs.Active, owner, rows.ClickedTrack >= 0 ? rows.ClickedTrack : SelectedIndex(songs.Active), rows, status), "Delete track", say));
        commands.Add("TrackRow.Properties", () => UiTasks.Forget(ShowTrackPropertiesAsync(songs.Active, owner, rows.ClickedTrack >= 0 ? rows.ClickedTrack : SelectedIndex(songs.Active), picker), "Track properties", say));
    }

    /// <summary>Adds a new track of <paramref name="kind"/> at the end of the song, one undo step.</summary>
    public static void AddTrack(IEditableDocument document, TrackKind kind, Action<TrackModel>? created = null) =>
        DocumentEdits.Run(document, project =>
        {
            var track = NewTrackFactory.CreateTrack(project, kind);
            created?.Invoke(track);
            project.Tracks.Add(track);
            return true;
        });

    // A new track takes the default chain saved for its instrument, through the FX window registry of the main window.
    private static Action<TrackModel> DefaultChainOf(Window owner) => track => TabForge.Avalonia.Views.Fx.FxChainWindows.For(owner)?.ApplyDefaultChain(track);

    /// <summary>Track.Add: the Track properties window in add mode, which adds its guitar track on OK.</summary>
    private static async Task AddInstrumentTrackAsync(IEditableDocument document, Window owner) =>
        await new TrackPropertiesWindow(NewTrackFactory.CreateTrack(document.Project, TrackKind.Guitar), document, adding: true).ShowAndWaitAsync(owner);

    /// <summary>TrackRow.Properties: the Track properties window for the track at <paramref name="index"/>, editing the shown song.</summary>
    internal static async Task ShowTrackPropertiesAsync(IEditableDocument document, Window owner, int index, InstrumentPickerFlow? picker = null)
    {
        if (index < 0 || index >= document.Project.Tracks.Count) return;
        await new TrackPropertiesWindow(document.Project.Tracks[index], document, picker: picker).ShowAndWaitAsync(owner);
    }

    /// <summary>Track.Delete and TrackRow.Delete: asks once; a Yes deletes the track at <paramref name="index"/>, or every selected track when it is one of them, in one undo step. The last track stays.</summary>
    private static async Task DeleteTrackAsync(IEditableDocument document, Window owner, int index, ContextMenuHost rows, Action<string>? status = null)
    {
        var targets = TargetsOf(document, index);
        if (await ConfirmDeleteAsync(document, owner, targets, rows, status)) RemoveTracks(document, targets);
    }

    /// <summary>The rows an action on the track at <paramref name="index"/> covers: the track list's selected tracks when it is one of them, else just it.</summary>
    internal static IReadOnlyList<int> TargetsOf(IEditableDocument document, int index) => document.Selection.Tracks.Targets(document.Project.Tracks, index);

    /// <summary>The Delete track question for the tracks at <paramref name="indices"/> (one question listing them); true only when it was answered Delete. Removing every track is refused with a notice
    /// and no row asks nothing. Every way a track goes (Delete, the track row's Cut) asks through here.</summary>
    internal static async Task<bool> ConfirmDeleteAsync(IEditableDocument document, Window owner, IReadOnlyList<int> indices, ContextMenuHost rows, Action<string>? status = null)
    {
        var tracks = document.Project.Tracks;
        var doomed = indices.Where(i => i >= 0 && i < tracks.Count).ToList();
        if (doomed.Count == 0) return false;
        if (doomed.Count >= tracks.Count) { status?.Invoke(CannotDeleteLastTrackText); return false; }
        var undoHint = HotkeyCatalog.DisplayAll(rows.Hotkeys(), UndoCommandId);
        var outcome = await PromptWindow.AskAsync(owner, PromptTextsPresenter.DeleteTracks(doomed.Select(i => tracks[i].Name).ToList(), undoHint));
        return PromptTextsPresenter.IsConfirmed(outcome.Answer);
    }

    /// <summary>Removes the track at <paramref name="index"/>, one undo step; the last track stays.</summary>
    public static EditResult RemoveTrack(IEditableDocument document, int index) => RemoveTracks(document, new[] { index });

    /// <summary>Removes the tracks at <paramref name="indices"/> together, one undo step; at least one track stays. The track row's Cut row removes through it too.</summary>
    public static EditResult RemoveTracks(IEditableDocument document, IReadOnlyCollection<int> indices) =>
        DocumentEdits.Run(document, project =>
        {
            if (!TrackGroupEdits.Remove(project, indices)) return false;
            document.Selection.Rebind(project);   // the removed tracks leave the selected rows, the anchor and the clips (the undo step keeps them: it was taken before)
            return true;
        });

    /// <summary>Track.MoveUp / Track.MoveDown: the selected track one place up (-1) or down (+1); a track at the end does not move.</summary>
    private static void MoveSelectedTrack(IEditableDocument document, int direction)
    {
        var from = SelectedIndex(document);
        var count = document.Project.Tracks.Count;
        if (from < 0 || from >= count) return;
        var to = Math.Clamp(from + direction, 0, count - 1);
        if (to == from) return;
        if (DocumentEdits.Run(document, project => project.MoveTrack(from, to)).Changed && document is SongDocument song) song.TrackIndex = to;   // the moved track stays the active one
    }

    /// <summary>Track.Next / Track.Previous: the selected track one row down (+1) or up (-1); the first and last track stay at the ends.</summary>
    private static void StepSelectedTrack(SongDocument song, int delta)
    {
        var count = song.Project.Tracks.Count;
        if (count == 0) return;
        song.TrackIndex = Math.Clamp(song.TrackIndex + delta, 0, count - 1);
    }

    /// <summary>The track the Track menu acts on: the song's selected track row; the first track when the song keeps none.</summary>
    internal static int SelectedIndex(IEditableDocument document) => document is SongDocument song ? song.TrackIndex : 0;
}
