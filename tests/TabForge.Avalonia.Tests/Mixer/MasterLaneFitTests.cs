using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Menus;
using TabForge.Presenters;
using TabForge.Views.Score;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the track list fit runs when the master lane is shown or hidden, so the timeline pane grows or shrinks by the lane (WPF re-fits on the same
/// setting, see MainWindowTrackListFit).
/// Does not own: the fit maths (DockSizeRules), the fit itself (MainWindowTrackListFit) or the lane's drawing (TimelineView).
/// Tests: TabForge.Avalonia.Tests.MasterLaneFitTests.
/// </summary>
public class MasterLaneFitTests
{
    [AvaloniaFact]
    public void TogglingTheMasterLaneRunsTheTrackListFitOnce()
    {
        var tabs = new MainWindowTabs(MainWindowTabs.NewSong(), _ => Task.FromResult(PromptButton.Cancel), _ => Task.FromResult(false));
        var panes = new MainWindowDocumentPanes(tabs, new ScorePreviewView());
        var arrangement = panes.NewArrangement();
        var owner = new Window();
        var commands = new MainWindowCommands(owner, () => { });
        var fits = 0;
        MainWindowTrackListRows.Register(commands, tabs, panes, null, owner, _ => { }, () => fits++);

        commands.Run(TrackRowMenus.MasterLaneCommandId);
        Assert.True(arrangement.Timeline.MasterLaneShown);
        Assert.Equal(1, fits);

        commands.Run(TrackRowMenus.MasterLaneCommandId);
        Assert.False(arrangement.Timeline.MasterLaneShown);
        Assert.Equal(2, fits);
    }
}
