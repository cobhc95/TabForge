using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using TabForge.Avalonia.Views.Docking;
using TabForge.Docking;
using TabForge.Platform;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: floating dock windows: float from the menu, the user's X, restore fitted onto the work area, move/resize capture, the
/// settings round trip and the release on close. Renders go to %TEMP%\tf-avalonia-tests\DockFloatingWindowTests.
/// Tests: DockFloatingWindow, DockWorkspaceController (floating), AvaloniaMonitorWorkAreas.
/// </summary>
public class DockFloatingWindowTests
{
    private sealed class FixedWorkArea(UiRect area) : IMonitorWorkAreas
    {
        public UiRect WorkAreaNear(UiRect bounds) => area;
    }

    private static readonly UiRect Work = new(0, 0, 1920, 1040);

    private static (Window Owner, DockWorkspaceController Controller) Open(DockWorkspaceState? saved = null, bool light = false)
    {
        var setup = new DockPaneSetup();
        foreach (var id in DockPaneTable.Ids) setup.Register(id, () => new Border { Name = "Pane_" + id, Child = new TextBlock { Text = id } });
        setup.RegisterEditor(() => new Border());
        var view = new DockWorkspaceView(setup.ContentOf);
        var controller = new DockWorkspaceController(view, setup, saved, new FixedWorkArea(Work));
        var owner = new Window { Width = 1100, Height = 700, Content = view, RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark };
        owner.Show();
        return (owner, controller);
    }

    [AvaloniaFact]
    public void FloatOpensAnOwnedWindowWithThePanel()
    {
        var (owner, dock) = Open();
        Assert.True(dock.FloatPanel("rhythm"));

        var floating = Assert.Single(dock.State.Floating);
        Assert.Equal(new[] { "rhythm" }, DockWorkspaceState.EnumeratePanelIds(floating.Root));
        Assert.DoesNotContain("rhythm", DockWorkspaceState.EnumeratePanelIds(dock.State.Root));
        var window = dock.FloatingWindows[floating.Id];
        Assert.True(window.IsVisible);
        Assert.False(window.ShowInTaskbar);
        Assert.Same(owner, window.Owner);
        Assert.NotNull(window.View.HostControl(floating.Root!.HostId));
    }

    [AvaloniaFact]
    public void FloatPanelIsInThePanelMenu()
    {
        var menu = DockPanelMenu.Build("rhythm", _ => { }, _ => { }, () => { }, _ => { });
        Assert.Contains(menu.Items.OfType<MenuItem>(), i => (string?)i.Header == DockPanelMenu.FloatLabel);
    }

    [AvaloniaFact]
    public void TheUsersXHidesTheWindowAndDocksThePanelBack()
    {
        var (_, dock) = Open();
        dock.FloatPanel("rhythm");
        var window = dock.FloatingWindows.Values.Single();
        var expected = DockWorkspaceRules.Capture(dock.State);
        FloatingSessionRules.CloseByUser(expected, window.Id, id => { var r = DockPaneTable.Rows.First(x => x.Id == id); return (r.DefaultHost, r.DefaultAnchor); });

        window.Close();

        Assert.False(window.IsVisible);
        Assert.Empty(dock.State.Floating);
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(DockWorkspaceRules.Capture(dock.State)));
        Assert.True(window.IsReleased);
    }

    [AvaloniaFact]
    public void RestoreFitsAnOffScreenFloatingWindowOntoTheWorkArea()
    {
        var saved = DockWorkspaceController.DefaultLayout();
        FloatingSessionRules.OpenFloating(saved, "rhythm", "float-host", new UiRect(5000, -900, 400, 300), out var floating);

        var (_, dock) = Open(saved);

        var restored = dock.State.Floating.Single();
        Assert.Equal((1520.0, 0.0, 400.0, 300.0), (restored.Left, restored.Top, restored.Width, restored.Height));
        var window = dock.FloatingWindows[restored.Id];
        Assert.Equal(new PixelPoint(1520, 0), window.Position);
        Assert.Equal(floating.Id, restored.Id);
    }

    [AvaloniaFact]
    public void MovingAndResizingUpdatesTheCapturedState()
    {
        var (_, dock) = Open();
        dock.FloatPanel("rhythm");
        var window = dock.FloatingWindows.Values.Single();
        DockWorkspaceState? saved = null;
        dock.LayoutChanged += s => saved = s;

        window.Position = new PixelPoint(300, 200);
        window.Width = 500;
        window.Height = 320;
        Dispatcher.UIThread.RunJobs();
        dock.FlushLayoutChanged();

        var captured = Assert.Single(saved!.Floating);
        Assert.Equal((300.0, 200.0, 500.0, 320.0), (captured.Left, captured.Top, captured.Width, captured.Height));
    }

    [AvaloniaFact]
    public void FloatingStateRoundTripsThroughSettingsJsonByteIdentically()
    {
        var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tf-avalonia-tests", "floating-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var path = Path.Combine(folder, "settings.json");
            var layout = DockWorkspaceController.DefaultLayout();
            FloatingSessionRules.OpenFloating(layout, "rhythm", "float-host", new UiRect(200, 150, 420, 280), out _);
            SettingsFileService.SaveAtomic(path, new AppSettings { Workspace = DockWorkspaceRules.Capture(layout) });
            SettingsFileService.SaveAtomic(path, SettingsFileService.Load(path));
            var bytes = File.ReadAllBytes(path);

            var settings = MainWindowSettings.Open(path);
            var (_, dock) = Open(settings.StartLayout);
            settings.SaveLayout(DockWorkspaceRules.Capture(dock.State));
            settings.Dispose();

            Assert.Equal(bytes, File.ReadAllBytes(path));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [AvaloniaFact]
    public void DisposingTheWorkspaceReleasesEveryFloatingWindow()
    {
        var (owner, dock) = Open();
        dock.FloatPanel("rhythm");
        var window = dock.FloatingWindows.Values.Single();

        dock.Dispose();

        Assert.False(window.IsVisible);
        Assert.True(window.IsReleased);
        owner.Close();
    }

    [AvaloniaFact]
    public void ResetAllClosesTheWindowAndDetachesItsHooks()
    {
        var (_, dock) = Open();
        dock.FloatPanel("rhythm");
        var window = dock.FloatingWindows.Values.Single();

        dock.ResetAllPanels();

        Assert.Empty(dock.FloatingWindows);
        Assert.False(window.IsVisible);
        Assert.True(window.IsReleased);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void RendersAFloatingPanelWindow(bool light)
    {
        var (_, dock) = Open(light: light);
        dock.FloatPanel("rhythm");
        var window = dock.FloatingWindows.Values.Single();
        var dir = DialogTestKit.RenderDirectory(nameof(DockFloatingWindowTests));
        using var frame = window.CaptureRenderedFrame();
        frame!.Save(Path.Combine(dir, "floating-" + (light ? "light" : "dark") + ".png"));
    }
}
