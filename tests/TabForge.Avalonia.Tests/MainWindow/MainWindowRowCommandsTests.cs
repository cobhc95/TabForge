using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Menus;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Views.Score;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the Avalonia handlers of the track list and score right-click rows (MainWindowTrackListRows, MainWindowScoreRows) and the Core zoom
//   rules they use (ScoreZoomRules): each row runs its WPF change on the shown song or score.
// Does not own: the rows (TrackListMenus, ScoreMenus), the drawing, the Settings window's pages (PreferencesWindowTests).
// Tests: this file.
public class MainWindowRowCommandsTests
{
    private static MainWindowTabs Tabs() => new(MainWindowTabs.NewSong(), _ => Task.FromResult(PromptButton.Cancel), _ => Task.FromResult(false));

    [AvaloniaFact]
    public void TrackListRowsFlipGroupsAndMasterLaneAndHandleSettings()
    {
        var tabs = Tabs();
        var score = new ScorePreviewView();
        var panes = new MainWindowDocumentPanes(tabs, score);
        var arrangement = panes.NewArrangement();
        var owner = new Window();
        var commands = new MainWindowCommands(owner, () => { });
        MainWindowTrackListRows.Register(commands, tabs, panes, null, owner, _ => { }, () => { });

        var groupsBefore = tabs.Active.Project.Mixer.ShowGroupsInTrackList;
        commands.Run(TrackListMenus.GroupsId);
        Assert.NotEqual(groupsBefore, tabs.Active.Project.Mixer.ShowGroupsInTrackList);

        var masterBefore = arrangement.Timeline.MasterLaneShown;
        commands.Run(TrackRowMenus.MasterLaneCommandId);
        Assert.NotEqual(masterBefore, arrangement.Timeline.MasterLaneShown);

        Assert.All(new[] { TrackListMenus.ColourByGroupId, ContextMenuRows.ResetTrackRowHeightId, TrackListMenus.SettingsId },
            id => Assert.True(commands.Handles(id), id));
    }

    [AvaloniaFact]
    public void ScoreRowsTogglePresentationZoomLayoutAndHandleSettings()
    {
        var score = new ScorePreviewView();
        var owner = new Window();
        var commands = new MainWindowCommands(owner, () => { });
        MainWindowScoreRows.Register(commands, score, null, owner);

        commands.Run(ScoreMenus.NotationId);
        Assert.Equal(NotationMode.TabOnly, score.Probe.Notation);
        commands.Run(ScoreMenus.NotationId);
        Assert.Equal(NotationMode.TabAndStaff, score.Probe.Notation);

        var outlineBefore = score.Probe.Appearance.ShowNoteOutline;
        commands.Run(ScoreMenus.OutlineId);
        Assert.NotEqual(outlineBefore, score.Probe.Appearance.ShowNoteOutline);

        commands.Run(ScoreMenus.ZoomInId);
        Assert.Equal(1.1, score.Zoom, 3);
        commands.Run(ScoreMenus.ZoomOutId);
        commands.Run(ScoreMenus.ZoomOutId);
        Assert.Equal(0.9, score.Zoom, 3);

        commands.Run(ContextMenuRows.ScoreLayoutIdPrefix + "continuous");
        Assert.True(score.Probe.Appearance.CenterSystems);
        commands.Run(ContextMenuRows.ScoreLayoutIdPrefix + "horizontal");
        Assert.True(score.Probe.HorizontalScroll);

        Assert.All(new[] { ScoreMenus.FitWidthId, ScoreMenus.SettingsId, ContextMenuRows.ScoreLayoutIdPrefix + "page", ContextMenuRows.ScoreLayoutIdPrefix + "vertical" },
            id => Assert.True(commands.Handles(id), id));
        Assert.Equal(ContextMenuRows.ScoreLayoutIdPrefix + "continuous", ContextMenuRows.CommandIdOf(new MenuSpec { Id = ScoreMenus.LayoutId, Arg = "continuous" }));
    }

    [Fact]
    public void ZoomStepsAreTenPercentWithinFiftyToTwoHundredAndFitCapsAtOneAndAHalf()
    {
        Assert.Equal(1.1, ScoreZoomRules.Step(1.0, 1), 3);
        Assert.Equal(0.5, ScoreZoomRules.Step(0.5, -1), 3);
        Assert.Equal(2.0, ScoreZoomRules.Step(2.0, 1), 3);
        Assert.Equal(1.0, ScoreZoomRules.FitContinuous(900), 3);
        Assert.Equal(1.3, ScoreZoomRules.FitContinuous(2000), 3);
        Assert.Equal(1.5, ScoreZoomRules.FitContinuous(5000), 3);
    }
}
