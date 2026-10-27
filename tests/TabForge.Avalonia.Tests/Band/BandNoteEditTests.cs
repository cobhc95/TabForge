using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Tests;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: editing in a Band card without moving the tab: a click places the edit cursor and leaves the card's window where it was,
/// and writing a note does not re-share the cards' bar widths (only a bar that came or went does).
/// Does not own: the click rules (Core ScorePreviewEditor), the card's window maths (BandTrackPanel) or the score input (ScorePreviewInput).
/// Tests: TabForge.Avalonia.Platform.ScorePreviewInput, TabForge.Avalonia.Platform.BandView, TabForge.Avalonia.Platform.BandTrackPanel.
/// </summary>
public class BandNoteEditTests
{
    private static SongDocument DemoDocument() => new(() => null) { Project = DemoSongFactory.Create() };

    private static void Settle(Window window)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }

    /// <summary>The point in the window of a page position inside a card's staff (the staff draws scaled by its zoom).</summary>
    private static Point InCard(BandTrackPanel panel, double pageX, double pageY, Window window) =>
        panel.Staff.TranslatePoint(new Point(pageX * panel.Staff.Zoom, pageY * panel.Staff.Zoom), window)!.Value;

    [AvaloniaFact]
    public void ClickingABarPlacesTheCursorWithoutSlidingTheCardsWindow()
    {
        var window = new BandWindow(DemoDocument());
        window.Show();
        Settle(window);
        var view = window.View;
        var panel = view.PanelFor(view.ShownTrackIndices[0])!;
        var track = panel.Staff.Probe.Track!;
        var bar = panel.Staff.Probe.Layout.GetLayout(track).Measure(2);
        var at = InCard(panel, bar.X + bar.Width * 0.25, panel.Staff.Probe.TabTop(0) + 6, window);
        var windowTopBefore = panel.WindowTop;

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Settle(window);

        Assert.Equal(2, panel.Staff.Editor.SelectedMeasure);
        Assert.False(panel.Staff.Editor.HasSelection, "a click in a Band card selected a range");
        Assert.Equal(windowTopBefore, panel.WindowTop, 0.5);
        window.Close();
    }

    [AvaloniaFact]
    public void WritingANoteKeepsTheSharedBarWidthsAndOnlyABarChangeResharesThem()
    {
        var document = DemoDocument();
        var window = new BandWindow(document);
        window.Show();
        Settle(window);
        var view = window.View;
        var first = view.PanelFor(view.ShownTrackIndices[0])!;
        var second = view.PanelFor(view.ShownTrackIndices[1])!;
        var shared = first.Staff.Probe.Layout.MinBarWidths;
        Assert.NotNull(shared);
        Assert.Same(shared, second.Staff.Probe.Layout.MinBarWidths);   // every card lays its bars out on one array

        var track = first.Staff.Probe.Track!;
        double[] BarXs() => Enumerable.Range(0, 12).Select(bar => first.Staff.Probe.Layout.GetLayout(track).Measure(bar).X).ToArray();
        var barXs = BarXs();

        first.Staff.Editor.SetPosition(1, 0, 0);
        first.Staff.Editor.Effects.EnterFret(5, autoAdvance: false);
        Settle(window);

        Assert.True(document.IsDirty);
        Assert.Same(shared, first.Staff.Probe.Layout.MinBarWidths);   // an in-place edit keeps every bar where it was
        Assert.Same(shared, second.Staff.Probe.Layout.MinBarWidths);
        Assert.Equal(barXs, BarXs());

        first.Staff.InsertBarAt(2);
        Settle(window);

        Assert.NotSame(shared, first.Staff.Probe.Layout.MinBarWidths);   // a bar came in: the widths are measured again
        Assert.Same(first.Staff.Probe.Layout.MinBarWidths, second.Staff.Probe.Layout.MinBarWidths);
        window.Close();
    }
}
