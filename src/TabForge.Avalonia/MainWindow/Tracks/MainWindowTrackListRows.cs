using System;
using Avalonia.Controls;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Menus;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: the handlers of the track list's right-click rows on the shown song: Show tracks in groups, Colour tracks by group, Colour tracks... (the
//   Colour tracks window), Show master track, Reset track list height and Track list settings... (the Settings window on the Timeline & Tracks page).
// Each runs what the row runs.
// Does not own: the rows (TrackListMenus, TrackRowMenus), the group headers and the master lane drawing (the timeline views), the Colour tracks window
//   (TrackColoursWindow), the commands table (MainWindowCommands).
// Tests: TabForge.Avalonia.Tests.MainWindowRowCommandsTests.
public static class MainWindowTrackListRows
{
    /// <param name="songs">The tabs; the rows act on the shown song.</param>
    /// <param name="panes">The latest arrangement pane, whose master lane the master track row shows or hides.</param>
    /// <param name="settings">The shared settings (group colours, the master track setting); null uses the defaults.</param>
    /// <param name="owner">The window the Settings page opens over.</param>
    /// <param name="notice">Shows a status line (the colour count).</param>
    public static void Register(MainWindowCommands commands, MainWindowTabs songs, MainWindowDocumentPanes panes, MainWindowSettings? settings,
        Window owner, Action<string> notice, Action fitTrackList)
    {
        commands.Add(TrackListMenus.GroupsId, () => ToggleGroupsShown(songs.Active));
        commands.Add(TrackListMenus.ColourByGroupId, () => ColourByGroup(songs.Active, settings, notice));
        commands.Add(TrackRowMenus.MasterLaneCommandId, () => ToggleMasterLane(panes, settings, fitTrackList));
        commands.Add(ContextMenuRows.ResetTrackRowHeightId, () => panes.Arrangement?.Timeline.Reload(songs.Active.Project));
        commands.Add(TrackListMenus.SettingsId, () => MainWindowSettings.ShowPreferencesPage(settings, owner, SettingsCatalog.Timeline));
        commands.Add(TrackListMenus.ColourTracksId, () => OpenColourTracks(songs.Active, owner));
    }

    /// <summary>"Show tracks in groups": the song's own setting (the Mixer's "Groups in track list" box is the same one).</summary>
    internal static void ToggleGroupsShown(IEditableDocument document) =>
        DocumentEdits.Run(document, p => { p.Mixer.ShowGroupsInTrackList = !p.Mixer.ShowGroupsInTrackList; return true; }, invalidatesTimeline: false);

    /// <summary>"Colour tracks by group": every track takes its group's colour; the status line says how many changed.</summary>
    private static void ColourByGroup(IEditableDocument document, MainWindowSettings? settings, Action<string> notice)
    {
        var changed = 0;
        DocumentEdits.Run(document, p => { changed = TrackColouring.ByGroup(p, settings?.Settings.Appearance.GroupColours); return changed > 0; }, label: "Colour tracks by group");
        notice(changed == 0 ? "Tracks already have their group colours" : $"Coloured {changed} track{(changed == 1 ? "" : "s")} by group");
    }

    /// <summary>"Colour tracks...": the Colour tracks window on the song's tracks. The first colour change stores the undo step before it; each change marks the song changed.</summary>
    private static void OpenColourTracks(IEditableDocument document, Window owner) =>
        _ = new TrackColoursWindow(document.Project.Tracks, beforeFirstChange: () => DocumentEdits.Checkpoint(document, "Colour tracks"), changed: () => DocumentEdits.MarkChanged(document)).ShowDialog(owner);

    /// <summary>"Show master track": the lane above track 1 and the Timeline setting that starts new windows with it.</summary>
    private static void ToggleMasterLane(MainWindowDocumentPanes panes, MainWindowSettings? settings, Action fitTrackList)
    {
        var arrangement = panes.Arrangement;
        if (arrangement is null) return;
        var shown = !arrangement.Timeline.MasterLaneShown;
        arrangement.ShowMasterLane(shown);
        fitTrackList();
        if (settings is null) return;
        settings.Settings.Timeline.ShowMasterTrack = shown;
        settings.SaveSettingsDeferred();
    }
}
