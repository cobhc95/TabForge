using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the header strip's close button sits at the far left (before the caption) in compact mode and the Band view, and the toolbar icons it holds sit at the same
//   right-edge position as in the normal menu row.
// Does not own: the strip's pills and menu (CompactTrackStripTests), the modes (CompactBandHeaderTests).
// Tests: itself.
public class HeaderCloseLeftTests
{
    private const double WindowWidth = 1200;

    private static (Window Window, CompactTrackStrip Strip, Control Toggles) Build()
    {
        var window = new Window { Width = WindowWidth, Height = 300 };
        var commands = new MainWindowCommands(window, () => { });
        var toggles = MenuRowToggles.Build(commands);
        DockPanel.SetDock(toggles, Dock.Right);
        var row = new Border { Name = "MainMenuRow", Child = new DockPanel { Children = { toggles, new Menu() } } };
        var strip = new CompactTrackStrip();
        var root = new DockPanel { Children = { row, strip } };
        DockPanel.SetDock(row, Dock.Top);
        DockPanel.SetDock(strip, Dock.Top);
        window.Content = root;
        window.Show();
        window.UpdateLayout();
        return (window, strip, toggles);
    }

    private static double RightEdge(Window window, Control toggles) => toggles.TranslatePoint(new Point(toggles.Bounds.Width, 0), window)!.Value.X;

    [AvaloniaTheory]
    [InlineData(HeaderMode.Compact)]
    [InlineData(HeaderMode.Band)]
    public void CloseIsLeftmostAndTheIconsKeepTheirPlaceFromTheNormalBar(HeaderMode mode)
    {
        var (window, strip, toggles) = Build();
        var normalRight = RightEdge(window, toggles);
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        MainWindowCompactLayout.SetMenuRowVisible(window, strip, false);
        strip.SetCaption(HeaderModeCaption.CaptionOf(mode));
        strip.SetOn(true, song);
        window.UpdateLayout();

        Assert.Same(toggles, strip.Toolbar);
        Assert.Same(strip.CloseButtonControl, strip.Children[0]);
        var closeLeft = strip.CloseButtonControl.TranslatePoint(new Point(0, 0), strip)!.Value.X;
        Assert.Equal(strip.CloseButtonControl.Margin.Left, closeLeft, 1);
        Assert.True(strip.MenuButton.TranslatePoint(new Point(0, 0), strip)!.Value.X > closeLeft);
        Assert.Equal(normalRight, RightEdge(window, toggles), 1);
        Assert.Equal(WindowWidth, strip.Bounds.Width, 1);
        window.Close();
    }
}
