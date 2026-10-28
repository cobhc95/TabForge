using System.Collections.Generic;
using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Platform;

namespace TabForge.Avalonia;

// Owns: the handlers of the two View menu rows that change how each track lane draws in the arrangement: Show the notes in each track lane
//   and Show one continuous line per track lane. Each flips its mode on the arrangement's timeline (the two modes are exclusive), then sets
//   both menu checks from the timeline.
// Does not own: the rows (MainMenuTable in Core), the lane drawing (TimelineView and TimelineNoteMarks), the commands table (MainWindowCommands),
// the settings file (MainWindowSettings; the modes are kept in its Timeline settings, and applied to each new arrangement pane).
// Tests: TabForge.Avalonia.Tests.MainWindowMenuTests (every menu row is handled or disabled), MainWindowArrangementModesTests.
/// <summary>The View menu's arrangement lane modes.</summary>
public static class MainWindowArrangementModes
{
    private const string IndividualNotesId = "View.ArrangementIndividualNotes";
    private const string ContinuousBlocksId = "View.ArrangementContinuousBlocks";

    /// <summary>The View menu command ids this class handles.</summary>
    public static readonly IReadOnlyList<string> Commands = new[] { IndividualNotesId, ContinuousBlocksId };

    /// <param name="commands">The commands table the two rows run from.</param>
    /// <param name="panes">The latest arrangement pane, whose timeline draws the lanes.</param>
    /// <param name="owner">The window whose View menu rows show the checks.</param>
    /// <param name="settings">The settings file the modes are saved to and restored from; null keeps them for this run only.</param>
    public static void Register(MainWindowCommands commands, MainWindowDocumentPanes panes, Window owner, MainWindowSettings? settings = null)
    {
        commands.Add(IndividualNotesId, () => Change(panes, owner, settings, timeline => timeline.ShowIndividualNotes = !timeline.ShowIndividualNotes));
        commands.Add(ContinuousBlocksId, () => Change(panes, owner, settings, timeline => timeline.ShowContinuousBlocks = !timeline.ShowContinuousBlocks));
        panes.ArrangementCreated += arrangement => Restore(arrangement.Timeline, owner, settings);
        panes.ArrangementCreated += arrangement => RestoreTrackListWidth(arrangement, settings);
        // A previewed or applied Settings window edit of the two lane-mode rows reaches the arrangement showing now.
        // Named (not a lambda) so the window's close can unsubscribe it.
        if (settings is not null)
        {
            void OnSettingsChanged(TabForge.Services.AppSettings _) { if (panes.Arrangement is { } shown) Restore(shown.Timeline, owner, settings); }
            settings.SettingsChanged += OnSettingsChanged;
            owner.Closed += (_, _) => settings.SettingsChanged -= OnSettingsChanged;
        }
    }

    // Puts the saved track list width into a new arrangement and saves the width each splitter drag ends at.
    private static void RestoreTrackListWidth(TabForge.Avalonia.Views.ArrangementView arrangement, MainWindowSettings? settings)
    {
        if (settings is null) return;
        arrangement.SetTrackListWidth(TrackListWidthRules.ResolveSaved(settings.Settings.Timeline.TrackListWidth, settings.Settings.Appearance));
        arrangement.TrackListWidthCommitted += width =>
        {
            settings.Settings.Timeline.TrackListWidth = width;
            settings.SaveSettings();
        };
    }

    /// <summary>Sets <paramref name="timeline"/>'s lane modes from the saved Timeline settings and syncs the menu checks.</summary>
    public static void Restore(TimelineView timeline, Window owner, MainWindowSettings? settings)
    {
        if (settings is not null)
        {
            var saved = settings.Settings.Timeline;
            timeline.ShowContinuousBlocks = saved.ShowContinuousLine;
            timeline.ShowIndividualNotes = saved.ShowIndividualNotes;
        }
        SyncChecks(timeline, owner);
    }

    private static void Change(MainWindowDocumentPanes panes, Window owner, MainWindowSettings? settings, Action<TimelineView> change)
    {
        var timeline = panes.Arrangement?.Timeline;
        if (timeline is null) return;
        change(timeline);
        SyncChecks(timeline, owner);
        if (settings is null) return;
        settings.Settings.Timeline.ShowIndividualNotes = timeline.ShowIndividualNotes;
        settings.Settings.Timeline.ShowContinuousLine = timeline.ShowContinuousBlocks;
        settings.SaveSettings();
    }

    private static void SyncChecks(TimelineView timeline, Window owner)
    {
        SetCheck(owner, IndividualNotesId, timeline.ShowIndividualNotes);
        SetCheck(owner, ContinuousBlocksId, timeline.ShowContinuousBlocks);
    }

    /// <summary>Sets the check of every View menu row whose tag is <paramref name="id"/>.</summary>
    internal static void SetCheck(Window owner, string id, bool on)
    {
        foreach (var item in owner.GetLogicalDescendants().OfType<MenuItem>().Where(item => Equals(item.Tag, id)))
            item.IsChecked = on;
    }
}
