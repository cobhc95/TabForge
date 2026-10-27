using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia.Shell;
using TabForge.Avalonia.Views;
using TabForge.Platform;
using TabForge.Shell;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>Owns: the per-OS title row checks with a faked OS mode (mac inset and hidden caption buttons, Linux grips and frame, Windows unchanged). Tests: TitleBarView modes, LinuxChromeAdapter.Frame.</summary>
public class ChromeModeTests
{
    private sealed class FakeChrome : IWindowChrome
    {
        public List<ResizeEdge> Resizes { get; } = new();
        public WindowChromeState State { get; set; }
        public event EventHandler? StateChanged;
        public void SetState(WindowChromeState state) { State = state; StateChanged?.Invoke(this, EventArgs.Empty); }
        public void BeginMove() { }
        public void BeginResize(ResizeEdge edge) => Resizes.Add(edge);
        public void Minimize() { }
        public void ToggleMaximise() { }
        public void Close() { }
    }

    private static (Window Window, TitleBarView Bar, FakeChrome Chrome) Open(ChromeOs os, bool light = false)
    {
        var chrome = new FakeChrome();
        var tabs = new TabStripView(new TabForge.Services.TabSettings());
        var bar = new TitleBarView(tabs, chrome, () => { }, () => { }, os);
        DockPanel.SetDock(bar, Dock.Top);
        var body = new DockPanel { LastChildFill = false, Children = { bar } };
        var window = new Window { Width = 900, Height = 300, Content = bar.Frame(body) };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, bar, chrome);
    }

    private static Button Button(TitleBarView bar, string name) => bar.GetVisualDescendantsOf<Button>().Single(b => b.Name == name);

    [AvaloniaFact]
    public void MacHidesCaptionButtonsAndInsetsTabsExceptInFullScreen()
    {
        var (window, bar, chrome) = Open(ChromeOs.MacOs);
        var tabs = bar.GetVisualDescendantsOf<TabStripView>().Single();
        Assert.All(new[] { "Minimise", "Maximise", "Close" }, n => Assert.False(Button(bar, n).IsVisible));
        Assert.True(Button(bar, "Settings").IsVisible);
        Assert.Equal(78, tabs.TranslatePoint(new Point(0, 0), window)!.Value.X, 1);
        chrome.SetState(WindowChromeState.FullScreen);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, tabs.TranslatePoint(new Point(0, 0), window)!.Value.X, 1);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(ChromeOs.Windows)]
    [InlineData(ChromeOs.Linux)]
    public void WindowsAndLinuxKeepCaptionButtonsAndNoInset(ChromeOs os)
    {
        var (window, bar, _) = Open(os);
        var tabs = bar.GetVisualDescendantsOf<TabStripView>().Single();
        Assert.All(new[] { "Minimise", "Maximise", "Close" }, n => Assert.True(Button(bar, n).IsVisible));
        Assert.Equal(0, tabs.TranslatePoint(new Point(0, 0), window)!.Value.X, 1);
        Assert.Equal(os == ChromeOs.Linux, window.GetVisualDescendantsOf<Grid>().Any(g => g.Name == "ResizeGrips"));
        window.Close();
    }

    [AvaloniaFact]
    public void LinuxHasEightGripsThatCallTheChromeWithTheirEdgeAndHideWhenMaximised()
    {
        var (window, bar, chrome) = Open(ChromeOs.Linux);
        var grips = window.GetVisualDescendantsOf<Border>().Where(b => b.Name?.StartsWith("Grip") == true).ToList();
        Assert.Equal(8, grips.Count);
        Assert.Equal(Enum.GetValues<ResizeEdge>().OrderBy(e => e), grips.Select(g => (ResizeEdge)g.Tag!).OrderBy(e => e));
        // Each grip's centre classifies to its own edge with the shared border rule.
        var bounds = new UiRect(0, 0, window.Bounds.Width, window.Bounds.Height);
        foreach (var grip in grips)
        {
            var origin = grip.TranslatePoint(new Point(grip.Bounds.Width / 2, grip.Bounds.Height / 2), window)!.Value;
            Assert.Equal((ResizeEdge)grip.Tag!, ChromeHitRules.ClassifyResizeEdge(new UiPoint(origin.X, origin.Y), bounds, LinuxChromeAdapter.GripSize, false));
        }
        foreach (var grip in grips)
        {
            var at = grip.TranslatePoint(new Point(grip.Bounds.Width / 2, grip.Bounds.Height / 2), window)!.Value;
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
        }
        Assert.Equal(grips.Select(g => (ResizeEdge)g.Tag!), chrome.Resizes);
        chrome.SetState(WindowChromeState.Maximised);
        Assert.All(window.GetVisualDescendantsOf<Grid>().Where(g => g.Name == "ResizeGrips"), g => Assert.False(g.IsVisible));
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(ChromeOs.MacOs, false)]
    [InlineData(ChromeOs.MacOs, true)]
    [InlineData(ChromeOs.Linux, false)]
    [InlineData(ChromeOs.Linux, true)]
    public void TitleRowRendersInEachMode(ChromeOs os, bool light)
    {
        using var theme = DialogTestKit.UseTheme(light);
        var (window, _, _) = Open(os, light);
        DialogTestKit.SavePng(window, nameof(ChromeModeTests), $"title-row-{os}-{(light ? "light" : "dark")}.png");
        window.Close();
    }

    [Fact]
    public void LinuxResizeGripMatchesWindowsHitBorder()
    {
        Assert.Equal(TabForge.Avalonia.Shell.Win32.NcHitTestHook.ResizeBorderDips, LinuxChromeAdapter.GripSize);
    }

    [AvaloniaFact]
    public void WindowsChromeAdapterReportsMaximisedAfterToggle()
    {
        if (!OperatingSystem.IsWindows()) return;
        var window = new Window { Width = 300, Height = 200 };
        window.Show();
        var chrome = new TabForge.Avalonia.Shell.Win32.WindowsChromeAdapter(window);
        chrome.ToggleMaximise();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(WindowChromeState.Maximised, chrome.State);
        window.Close();
    }
}
