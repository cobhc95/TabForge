using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using TabForge.Avalonia.Views.Docking;
using TabForge.Docking;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the layout the window starts with: a saved Workspace is used only when it shows the required panes, otherwise the default layout
/// appears (the fretboard above the score, the tools column beside both, the timeline pane at least a quarter of the height).
/// Does not own: the settings file (MainWindowSettings), the default tree (DockLayoutTree.CreateDefaultState).
/// Tests: DockWorkspaceController.StartupLayout, DockWorkspaceController, DockWorkspaceView.
/// </summary>
public class DockStartupLayoutTests
{
    /// <summary>A saved Workspace as an earlier build wrote it while compact mode was on: the side panels are closed.</summary>
    private const string CompactSavedWorkspace = """
        {"Version":1,"Root":{"Kind":"split","HostId":"root","Panels":[],"SelectedPanel":null,"Orientation":"Vertical","Ratio":0.7227,
        "First":{"Kind":"split","HostId":"upper","Panels":[],"SelectedPanel":null,"Orientation":"Vertical","Ratio":0.68,
          "First":{"Kind":"editor","HostId":"score-editor","Panels":[],"SelectedPanel":null,"Orientation":"Horizontal","Ratio":0.5,"First":null,"Second":null},
          "Second":{"Kind":"tabs","HostId":"default-instrument","Panels":["instrument"],"SelectedPanel":"instrument","Orientation":"Horizontal","Ratio":0.5,"First":null,"Second":null}},
        "Second":{"Kind":"tabs","HostId":"default-timeline","Panels":["timeline"],"SelectedPanel":"timeline","Orientation":"Horizontal","Ratio":0.5,"First":null,"Second":null}},
        "Floating":[],"ClosedPanels":["sections","lyrics","band","learn","tools","structure","rhythm","layout","viewtempo"]}
        """;

    private static DockWorkspaceState Parse(string json) => JsonSerializer.Deserialize<DockWorkspaceState>(json)!;

    /// <summary>The layout as JSON with the host ids dropped: the default tree gives its hosts fresh ids on every call.</summary>
    private static string Json(DockWorkspaceState state) => Regex.Replace(JsonSerializer.Serialize(state), "\"HostId\":\"[^\"]*\"", "");

    [AvaloniaFact]
    public void ASavedWorkspaceWithTheSidePanelsClosedFallsBackToTheDefaultLayout()
    {
        Assert.Equal(Json(DockWorkspaceController.DefaultLayout()), Json(DockWorkspaceController.StartupLayout(Parse(CompactSavedWorkspace))));
    }

    [AvaloniaFact]
    public void TheStartLayoutShowsTheRightColumnBesideFretboardAndScoreWithATallTimeline()
    {
        var setup = new DockPaneSetup();
        foreach (var id in DockPaneTable.Ids) setup.Register(id, () => new Border());
        setup.RegisterEditor(() => new Border());
        var view = new DockWorkspaceView(setup.ContentOf);
        _ = new DockWorkspaceController(view, setup, DockWorkspaceController.StartupLayout(Parse(CompactSavedWorkspace)));
        var window = new Window { Width = 1300, Height = 900, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        double TopOf(Control c) => c.TranslatePoint(default, window)!.Value.Y;
        var fretboard = view.HostControl("default-instrument")!;
        var tools = view.HostControl("default-tool-palette")!;
        var timeline = view.HostControl("default-timeline")!;
        Assert.Equal(TopOf(fretboard), TopOf(tools));
        Assert.True(TopOf(fretboard) < TopOf(view.HostControl("score-editor")!));
        Assert.True(timeline.Bounds.Height >= window.Height * 0.25, $"timeline {timeline.Bounds.Height} of {window.Height}");
        window.Close();
    }
}
