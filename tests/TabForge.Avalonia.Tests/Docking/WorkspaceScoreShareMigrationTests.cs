using TabForge.Docking;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the load-time migration of remembered dock sizes that starve the score: a saved split giving the timeline or the
//   fretboard most of the room is clamped back so the TAB opens readable, and the corrected layout is what the next save persists.
// Does not own: the dock tree (DockLayoutTree), the live drag minimums (DockWorkspaceView) or the fit reserve (MainWindowTrackListFit).
// Tests: WorkspaceNormalizer.Normalize through SettingsValidator.Normalize.
public class WorkspaceScoreShareMigrationTests
{
    private static AppSettings WithWorkspace(DockWorkspaceState workspace) => new() { Workspace = workspace };

    private static DockWorkspaceState StarvedScoreLayout(double rootRatio, double? userRatio = null) => new()
    {
        Version = 1,
        Root = new DockNodeState
        {
            Kind = "split", HostId = "root", Orientation = "Vertical", Ratio = rootRatio, UserRatio = userRatio,
            First = DockLayoutTree.EditorNode("score-editor"),
            Second = DockLayoutTree.Tabs("host-timeline", "timeline"),
        },
        Floating = new(),
        ClosedPanels = new(),
    };

    [Fact]
    public void TimelineHeavySavedSplitIsClampedSoTheScoreKeepsItsShare()
    {
        var settings = WithWorkspace(StarvedScoreLayout(rootRatio: 0.15));
        SettingsValidator.Normalize(settings);

        Assert.NotNull(settings.Workspace);
        Assert.Equal(0.35, settings.Workspace.Root!.Ratio, 6);   // the timeline keeps 0.65, the score the rest
    }

    [Fact]
    public void RememberedDragIsMigratedTheSameWayAsTheSavedRatio()
    {
        var settings = WithWorkspace(StarvedScoreLayout(rootRatio: 0.5, userRatio: 0.10));
        SettingsValidator.Normalize(settings);

        Assert.NotNull(settings.Workspace);
        Assert.Equal(0.5, settings.Workspace.Root!.Ratio, 6);   // the live ratio was healthy and stays
        Assert.Equal(0.35, settings.Workspace.Root!.UserRatio!.Value, 6);   // the remembered drag cannot starve the score again
    }

    [Fact]
    public void FretboardHeavySavedSplitIsClampedSoTheScoreKeepsItsShare()
    {
        var state = new DockWorkspaceState
        {
            Version = 1,
            Root = DockLayoutTree.Split("Vertical", 0.20, DockLayoutTree.EditorNode("score-editor"), DockLayoutTree.Tabs("host-instrument", "instrument")),
            Floating = new(),
            ClosedPanels = new(),
        };
        var settings = WithWorkspace(state);
        SettingsValidator.Normalize(settings);

        Assert.NotNull(settings.Workspace);
        Assert.Equal(0.40, settings.Workspace.Root!.Ratio, 6);   // the fretboard keeps 0.60, the score the rest
    }

    [Fact]
    public void HealthySavedLayoutPassesThroughUnchanged()
    {
        var settings = WithWorkspace(StarvedScoreLayout(rootRatio: 0.74));
        SettingsValidator.Normalize(settings);

        Assert.NotNull(settings.Workspace);
        Assert.Equal(0.74, settings.Workspace.Root!.Ratio, 6);
        Assert.Null(settings.Workspace.Root!.UserRatio);
    }

    [Fact]
    public void RememberedFretboardShareIsClampedWithTheLayout()
    {
        var workspace = StarvedScoreLayout(rootRatio: 0.74);
        workspace.InstrumentVerticalShare = 0.80;
        var settings = WithWorkspace(workspace);
        SettingsValidator.Normalize(settings);

        Assert.NotNull(settings.Workspace);
        Assert.Equal(0.60, settings.Workspace.InstrumentVerticalShare!.Value, 6);
    }
}
