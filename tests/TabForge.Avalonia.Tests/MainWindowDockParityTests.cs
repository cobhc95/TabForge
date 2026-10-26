using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Docking;
using TabForge.Docking;
using TabForge.Documents;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>Owns: the main window's dock measured against the default-layout captures (win-default-dark/light, 200 % halved to DIPs):
/// side panel tab pills, splitter bands, the View and tempo pane and the menu row's layout toggles.</summary>
public class MainWindowDockParityTests
{
    private static MainWindow Open()
    {
        var main = new MainWindow { Width = 1320, Height = 900 };
        main.Show();
        Dispatcher.UIThread.RunJobs();
        return main;
    }

    private static Color ColourOf(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    // WPF: header row 36 px; the selected tab a 1 px accent-edged (#4C9AFF dark) pill on the accent tint with white SemiBold text.
    [AvaloniaFact]
    public void SidePanelTabsUseTheWpfAccentPill()
    {
        using var theme = DialogTestKit.UseTheme(light: false);
        var main = Open();
        var surface = main.GetVisualDescendants().OfType<DockTabSurface>().First(s => s.Node.Panels.Contains("tools"));
        var selected = (Border)surface.Tabs.Single(t => t.Name == "DockTab_" + surface.Selected);
        Assert.Equal(DockTabSurface.StripHeight, ((Control)selected.Parent!.Parent!).Bounds.Height, 0.5);
        // WPF tints #2D5181 (accent blended into the window colour); AccentSoft paints that same tint in both themes.
        Assert.Equal(TabForge.Avalonia.Platform.AvaloniaPalette.ColorOf(TabForge.Avalonia.Platform.AvaloniaPalette.Get("AccentSoft", light: false)), ColourOf(selected.Background));
        Assert.Equal(Color.Parse("#4C9AFF"), ColourOf(selected.BorderBrush));
        Assert.Equal(1, selected.BorderThickness.Bottom);
        var text = (TextBlock)selected.Child!;
        Assert.Equal(Colors.White, ColourOf(text.Foreground));
        Assert.Equal(FontWeight.SemiBold, text.FontWeight);
        main.Close();
    }

    // WPF: 8 px bands between regions, #24282E dark and #AEAEAE light.
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void SplittersAreThinLinesInsideTheGrabBand(bool light)
    {
        using var theme = DialogTestKit.UseTheme(light);
        var main = Open();
        var splitters = main.GetVisualDescendants().OfType<DockSplitter>().ToList();
        Assert.NotEmpty(splitters);
        Assert.All(splitters, s =>
        {
            Assert.Equal(DockSplitter.BarThickness, s.HorizontalSplit ? s.Bounds.Width : s.Bounds.Height, 0.5);
            var line = Assert.IsType<Border>(s.Child);
            Assert.Equal(1, s.HorizontalSplit ? line.Bounds.Width : line.Bounds.Height, 0.5);
            var token = TabForge.Avalonia.Platform.AvaloniaPalette.Get("GeneralBorder", light);
            Assert.Equal(Color.FromRgb(token.R, token.G, token.B), ColourOf(line.Background));
        });
        main.Close();
    }

    [AvaloniaFact]
    public void ViewAndTempoBarSitsInTheSidePanelPane()
    {
        var main = Open();
        var bar = main.GetVisualDescendants().OfType<ViewTempoBarView>().Single();
        var surface = bar.FindAncestorOfType<DockTabSurface>()!;
        Assert.Equal(DockPaneTable.ViewTempoId, surface.Selected);
        Assert.True(bar.TranslatePoint(default, main)!.Value.X > main.Width / 2);
        main.Close();
    }

    [AvaloniaFact]
    public void MenuRowEndsWithTheWpfLayoutToggles()
    {
        var main = Open();
        var strip = main.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "MenuRowToggles");
        var ids = strip.Children.OfType<Button>().Select(b => (string)b.Tag!).ToList();
        Assert.Equal(new[] { "View.SidePanel", "View.InstrumentPanel", "View.LearnMode", "View.BandView", "View.CompactMode", "File.ProjectSettings" }, ids);
        var right = strip.TranslatePoint(new global::Avalonia.Point(strip.Bounds.Width, 0), main)!.Value.X;
        Assert.Equal(main.Width - 10, right, 1.0);
        Assert.All(strip.Children.OfType<Button>(), b => Assert.Equal(27, b.Bounds.Height, 0.5));
        main.Close();
    }

    // The timeline pane is as tall as its rows (the track-list fit): the demo song's 10 rows take more than the old 45 % cap, which only the score's least height limits now.
    [AvaloniaFact]
    public void TimelinePaneFitsItsRowsUpToTheDockShare()
    {
        // The window starts empty now: this test needs the demo song's ten rows, so it hands the window that song.
        var song = new SongDocument(() => null) { Project = FullDemoSongFactory.Create() };
        var main = new MainWindow(first: song) { Width = 1600, Height = 1000 };
        main.Show();
        Dispatcher.UIThread.RunJobs();
        var split = main.GetVisualDescendants().OfType<DockSplitPanel>().First(s => s.Node.Second is { Kind: "tabs" } lower && lower.Panels.Contains("timeline"));
        var share = split.Children[2].Bounds.Height / split.Bounds.Height;
        Assert.True(share > 0.45, $"the timeline pane takes {share:0.00} of the dock");
        main.Close();
    }
}
