using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the tab strip's press and drag ends (a click, a drag released off the strip, a missed release, a rebuild mid-drag), the
//   tab fills (rest, hover, selected: no size change) and the absence of focus on tabs.
// Does not own: the tear-off and merge across windows (TabTearOffTests), the drop rules (TabDragRulesTests).
// Tests: this class.
public class TabStripPressAndLookTests
{
    /// <summary>A window with the strip on top and a grey area below it, so a release can land off the strip inside the window.</summary>
    private static (Window Window, TabStripView Strip) Host(int documentCount)
    {
        var strip = new TabStripView(new TabSettings());
        strip.Show(Documents(documentCount), 0, 800);
        var below = new Border { Height = 200, Background = Brushes.Gray };
        var dock = new DockPanel { Children = { strip, below } };
        DockPanel.SetDock(strip, Dock.Top);
        var window = new Window { Width = 800, Height = 400, Content = dock };
        window.Show();
        window.UpdateLayout();
        return (window, strip);
    }

    private static TabStripDocument[] Documents(int count) => Enumerable.Range(0, count).Select(i => new TabStripDocument(null, $"Song {i}")).ToArray();

    private static Border Tab(TabStripView strip, int index) => (Border)strip.Tabs[index];

    private static Point Centre(Window window, Control control) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

    private static Point BelowStrip(Window window) => new(400, window.ClientSize.Height - 100);

    private static double TranslateX(Control tab) => (tab.RenderTransform as TranslateTransform)?.X ?? 0;

    private static Color Fill(Border tab) => ((ISolidColorBrush)tab.Background!).Color;

    /// <summary>Turns the tab fades off until disposed: a headless window never runs the transition clock, so a fade would never finish. The fill levels are what these tests check.</summary>
    private static IDisposable WithoutFades()
    {
        UiMotion.Configure(reduceAnimations: true, speed: 1);
        return new FadeScope();
    }

    private sealed class FadeScope : IDisposable
    {
        public void Dispose() => UiMotion.Configure(reduceAnimations: false, speed: 1);
    }

    [AvaloniaFact]
    public void TabsTakeNoFocusSoNoFocusOutlineIsDrawn()
    {
        var (window, strip) = Host(3);

        Assert.All(strip.Tabs.Cast<Border>(), tab => Assert.False(tab.Focusable));
        Assert.False(Tab(strip, 1).Focus());
        window.Close();
    }

    [AvaloniaFact]
    public void RestHoverAndSelectedTabsHaveThreeDifferentFills()
    {
        using var fades = WithoutFades();
        var (window, strip) = Host(3);
        var rest = Tab(strip, 1);
        var selected = Tab(strip, 0);
        var restFill = Fill(rest);
        var selectedFill = Fill(selected);

        window.MouseMove(Centre(window, rest));
        var hoverFill = Fill(rest);

        Assert.NotEqual(restFill, hoverFill);
        Assert.NotEqual(hoverFill, selectedFill);
        Assert.NotEqual(restFill, selectedFill);
        window.Close();
    }

    [AvaloniaFact]
    public void HoveringAnUnselectedTabChangesItsFillAndRevertsOnExitWithoutChangingItsSize()
    {
        using var fades = WithoutFades();
        var (window, strip) = Host(3);
        var tab = Tab(strip, 2);
        var restFill = Fill(tab);
        var boundsAtRest = tab.Bounds;

        window.MouseMove(Centre(window, tab));
        Assert.NotEqual(restFill, Fill(tab));
        Assert.Equal(boundsAtRest, tab.Bounds);

        window.MouseMove(BelowStrip(window));
        Assert.Equal(restFill, Fill(tab));
        Assert.Equal(boundsAtRest, tab.Bounds);
        window.Close();
    }

    [AvaloniaFact]
    public void ClickWithoutMovingLeavesNoDragNoInsertLineAndNoTranslation()
    {
        var (window, strip) = Host(3);
        var moves = 0;
        strip.TabDragMoved += (_, _) => moves++;
        var at = Centre(window, Tab(strip, 1));

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        window.MouseMove(Centre(window, Tab(strip, 2)));

        Assert.Equal(0, moves);
        Assert.False(strip.IsInsertionMarkerShown);
        Assert.All(strip.Tabs.Cast<Control>(), tab => Assert.Equal(0, TranslateX(tab)));
        window.Close();
    }

    [AvaloniaFact]
    public void DragReleasedOffTheStripEndsTheSession()
    {
        var (window, strip) = Host(3);
        var ended = 0;
        var moves = 0;
        strip.TabDragEnded += (_, _) => ended++;
        strip.TabDragMoved += (_, _) => moves++;
        var start = Centre(window, Tab(strip, 0));

        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(new Point(start.X + 30, start.Y), RawInputModifiers.LeftMouseButton);
        window.MouseMove(new Point(start.X + 60, start.Y), RawInputModifiers.LeftMouseButton);
        window.MouseUp(BelowStrip(window), MouseButton.Left);
        var movesAtRelease = moves;
        window.MouseMove(new Point(start.X + 90, start.Y), RawInputModifiers.None);

        Assert.Equal(1, ended);
        Assert.Equal(movesAtRelease, moves);
        Assert.False(strip.IsInsertionMarkerShown);
        window.Close();
    }

    [AvaloniaFact]
    public void NewTabOpenedMidDragResetsEveryTabToItsPlace()
    {
        var (window, strip) = Host(3);
        var cancelled = 0;
        strip.TabDragCancelled += () => cancelled++;
        var start = Centre(window, Tab(strip, 1));

        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(new Point(start.X + 40, start.Y), RawInputModifiers.LeftMouseButton);
        window.MouseMove(new Point(start.X + 80, start.Y), RawInputModifiers.LeftMouseButton);
        strip.Show(Documents(4), 1, 800);
        window.UpdateLayout();

        Assert.Equal(1, cancelled);
        var tabs = strip.Tabs.Cast<Control>().ToList();
        Assert.All(tabs, tab => Assert.Equal(0, TranslateX(tab)));
        for (var i = 1; i < tabs.Count; i++) Assert.True(tabs[i - 1].Bounds.Right <= tabs[i].Bounds.Left + 0.5, $"tab {i - 1} overlaps tab {i}");
        window.MouseUp(start, MouseButton.Left);
        window.Close();
    }
}
