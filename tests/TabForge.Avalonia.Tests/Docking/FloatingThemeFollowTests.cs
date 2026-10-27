using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using TabForge.Avalonia.Views.Docking;
using TabForge.Docking;
using TabForge.Platform;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: a floating dock window following its owner's theme: it takes the owner's variant when it opens, follows a later
//   light/dark switch, and drops the hook when it closes (no stale or leaked handler).
// Does not own: the dock layout (DockWorkspaceControllerTests) or the palette resources (ThemeLiveSwitchTests).
// Tests: this file.
public class FloatingThemeFollowTests
{
    private sealed class FixedWorkArea(UiRect area) : IMonitorWorkAreas
    {
        public UiRect WorkAreaNear(UiRect bounds) => area;
    }

    private static readonly UiRect Work = new(0, 0, 1920, 1040);

    private static (Window Owner, DockWorkspaceController Controller) Open()
    {
        var setup = new DockPaneSetup();
        foreach (var id in DockPaneTable.Ids) setup.Register(id, () => new Border { Name = "Pane_" + id, Child = new TextBlock { Text = id } });
        setup.RegisterEditor(() => new Border());
        var view = new DockWorkspaceView(setup.ContentOf);
        var controller = new DockWorkspaceController(view, setup, null, new FixedWorkArea(Work));
        var owner = new Window { Width = 1100, Height = 700, Content = view, RequestedThemeVariant = ThemeVariant.Dark };
        owner.Show();
        return (owner, controller);
    }

    [AvaloniaFact]
    public void TheFloatingWindowFollowsTheOwnerAndDropsTheHookWhenClosed()
    {
        var (owner, dock) = Open();
        try
        {
            dock.FloatPanel("rhythm");
            var window = Assert.Single(dock.FloatingWindows.Values);
            Assert.Equal(ThemeVariant.Dark, window.RequestedThemeVariant);

            owner.RequestedThemeVariant = ThemeVariant.Light;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(ThemeVariant.Light, window.RequestedThemeVariant);

            dock.ResetAllPanels();
            owner.RequestedThemeVariant = ThemeVariant.Dark;
            Dispatcher.UIThread.RunJobs();
            // Closed windows no longer follow the owner: no handler is left on it.
            Assert.Equal(ThemeVariant.Light, window.RequestedThemeVariant);
        }
        finally
        {
            dock.Dispose();
            owner.Close();
        }
    }
}
