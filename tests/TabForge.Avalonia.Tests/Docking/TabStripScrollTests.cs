using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using TabForge.Avalonia.Shell;
using TabForge.Avalonia.Views;
using TabForge.Platform;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the tab strip's live fit and scroll: the width and scroll maths (TabStripPresenter) and the strip re-fitting on a resize, with
//   arrows and the active tab in view when the tabs overflow.
// Does not own: the press and drag rules (TabStripPressAndLookTests).
// Tests: this class.
public class TabStripScrollTests
{
    private static readonly TabSettings Defaults = new();

    [Fact]
    public void ThreeTabsInAWideStripAreAtMaxWidth() =>
        Assert.Equal(320, TabStripPresenter.TabWidths(Defaults, 1600, 36, 3).Min);

    [Fact]
    public void NarrowerStripShrinksTheTabs() =>
        Assert.True(TabStripPresenter.TabWidths(Defaults, 900, 36, 3).Min < TabStripPresenter.TabWidths(Defaults, 1600, 36, 3).Min);

    [Fact]
    public void TwentyTabsKeepTheMinimumWidth() =>
        Assert.Equal(120, TabStripPresenter.TabWidths(Defaults, 1000, 36, 20).Min);

    [Fact]
    public void ViewportLeavesRoomForButtonsAndOnlyOverflowAddsArrows()
    {
        Assert.Equal(1000 - 14 - 36 - 44, TabStripPresenter.Viewport(1000, 36, 22, 120, 20));
        Assert.Equal(1000 - 14 - 36, TabStripPresenter.Viewport(1000, 36, 22, 282, 3));
    }

    [Fact]
    public void ArrowsEnableOnlyWhereThereIsMore()
    {
        Assert.Equal((false, true), TabStripPresenter.CanScroll(0, 2000, 800));
        Assert.Equal((true, true), TabStripPresenter.CanScroll(300, 2000, 800));
        Assert.Equal((true, false), TabStripPresenter.CanScroll(1200, 2000, 800));
        Assert.Equal((false, false), TabStripPresenter.CanScroll(0, 700, 800));
    }

    [Fact]
    public void RevealMovesLeastToShowATab()
    {
        Assert.Equal(500, TabStripPresenter.RevealOffset(500, 120, 600, 800));
        Assert.Equal(520, TabStripPresenter.RevealOffset(1200, 120, 0, 800));
        Assert.Equal(600, TabStripPresenter.RevealOffset(700, 120, 600, 800));
    }

    [Fact]
    public void StepMovesOneTabEitherWay()
    {
        var tabs = Enumerable.Range(0, 10).Select(i => (Left: i * 123.0, Width: 120.0)).ToList();
        Assert.Equal(58, TabStripPresenter.StepOffset(tabs, 0, 800, forward: true));
        Assert.Equal(369, TabStripPresenter.StepOffset(tabs, 419, 800, forward: false));
        Assert.Equal(0, TabStripPresenter.StepOffset(tabs, 0, 800, forward: false));
    }

    [Fact]
    public void StepClampsAnOffsetIntoTheScrollRange()
    {
        var tabs = Enumerable.Range(0, 10).Select(i => (Left: i * 123.0, Width: 120.0)).ToList();
        Assert.Equal(58, TabStripPresenter.StepOffset(tabs, -50, 800, forward: true));
        Assert.Equal(427, TabStripPresenter.StepOffset(tabs, 5000, 800, forward: true));
        Assert.Equal(369, TabStripPresenter.StepOffset(tabs, 5000, 800, forward: false));
    }

    [Fact]
    public void ArrowMovesExactlyOneTabWidth()
    {
        var tabs = Enumerable.Range(0, 10).Select(i => (Left: i * 120.0, Width: 120.0)).ToList();
        Assert.Equal(120, TabStripPresenter.StepOffset(tabs, 0, 240, forward: true));
        Assert.Equal(240, TabStripPresenter.StepOffset(tabs, 120, 240, forward: true));
        Assert.Equal(120, TabStripPresenter.StepOffset(tabs, 240, 240, forward: false));
    }

    private sealed class QuietChrome : IWindowChrome
    {
        public WindowChromeState State => WindowChromeState.Normal;
        public event System.EventHandler? StateChanged { add { } remove { } }
        public void BeginMove() { }
        public void BeginResize(ResizeEdge edge) { }
        public void Minimize() { }
        public void ToggleMaximise() { }
        public void Close() { }
    }

    [AvaloniaFact]
    public void ScrolledOutTabProducesNoHitRect()
    {
        var strip = new TabStripView(new TabSettings());
        var bar = new TitleBarView(strip, new QuietChrome(), () => { }, () => { });
        var window = new Window { Width = 1000, Height = 200, Content = new DockPanel { LastChildFill = false, Children = { bar } } };
        DockPanel.SetDock(bar, Dock.Top);
        window.Show();
        strip.Show(Enumerable.Range(0, 20).Select(i => new TabStripDocument(null, $"Song {i}")).ToList(), 0, 1000);
        window.UpdateLayout();
        Assert.NotEmpty(bar.TabBounds);
        Assert.True(bar.TabBounds.Count < 20);
        Assert.All(bar.TabBounds, r => Assert.True(r.Width > 0));
        window.Close();
    }

    private static (Window Window, TabStripView Strip, Border Room) Host(int count, int width, int active = 0)
    {
        var strip = new TabStripView(new TabSettings());
        strip.Show(Enumerable.Range(0, count).Select(i => new TabStripDocument(null, $"Song {i}")).ToList(), active, width);
        var room = new Border { Width = width, HorizontalAlignment = HorizontalAlignment.Left, Child = strip };
        var window = new Window { Width = 1700, Height = 200, Content = room };
        window.Show();
        window.UpdateLayout();
        return (window, strip, room);
    }

    private static Button Arrow(TabStripView strip, string name) => strip.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);

    [AvaloniaFact]
    public void ResizingTheWindowRefitsTheTabs()
    {
        var (window, strip, room) = Host(3, 1600);
        Assert.All(strip.Tabs, t => Assert.Equal(320, t.Bounds.Width, 1));
        room.Width = 900;
        window.UpdateLayout();
        var expected = TabStripPresenter.TabWidths(new TabSettings(), strip.Bounds.Width, 36, 3).Min;
        Assert.InRange(expected, 120, 319);
        Assert.All(strip.Tabs, t => Assert.Equal(expected, t.Bounds.Width, 1));
        window.Close();
    }

    [AvaloniaFact]
    public void OverflowingTabsShowArrowsAndKeepTheActiveTabInView()
    {
        var (window, strip, _) = Host(20, 1000, active: 19);
        Assert.All(strip.Tabs, t => Assert.Equal(120, t.Bounds.Width, 1));
        Assert.True(Arrow(strip, "TabScrollLeft").IsVisible);
        Assert.True(Arrow(strip, "TabScrollRight").IsVisible);
        var scroll = strip.TabViewport as ScrollViewer;
        var active = strip.Tabs[19];
        Assert.True(active.Bounds.Right <= scroll!.Offset.X + scroll.Viewport.Width + 1);
        Assert.True(Arrow(strip, "TabScrollLeft").IsEnabled);
        Assert.False(Arrow(strip, "TabScrollRight").IsEnabled);
        window.Close();
    }
}
