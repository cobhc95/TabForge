using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using TabForge.Avalonia.Views.Docking;
using TabForge.Docking;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the first-start and Reset panels arrangement in the Avalonia dock: the right column (tools, sections) runs the full height beside
/// the fretboard and the score, so the fretboard sits beside the score, not across the top.
/// Does not own: the default tree (DockLayoutTree.CreateDefaultState in Core), the split drawing (DockSplitPanel).
/// Tests: DockWorkspaceView, DockWorkspaceController.DefaultLayout.
/// </summary>
public class DockDefaultLayoutTests
{
    [AvaloniaFact]
    public void DefaultLayoutPutsTheRightColumnBesideBothFretboardAndScore()
    {
        var setup = new DockPaneSetup();
        foreach (var id in DockPaneTable.Ids) setup.Register(id, () => new Border());
        setup.RegisterEditor(() => new Border());
        var view = new DockWorkspaceView(setup.ContentOf);
        var controller = new DockWorkspaceController(view, setup, DockWorkspaceController.DefaultLayout());
        var window = new Window { Width = 1300, Height = 900, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var fretboard = view.HostControl("default-instrument")!;
        var score = view.HostControl("score-editor")!;
        var tools = view.HostControl("default-tool-palette")!;
        double TopOf(Control c) => c.TranslatePoint(default, window)!.Value.Y;
        double LeftOf(Control c) => c.TranslatePoint(default, window)!.Value.X;
        Assert.Equal(TopOf(fretboard), TopOf(tools));
        Assert.Equal(LeftOf(fretboard), LeftOf(score));
        Assert.True(LeftOf(tools) > LeftOf(score) + score.Bounds.Width - 1, "the right column sits to the right of the score");
        window.Close();
    }
}
