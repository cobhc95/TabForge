using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: selecting on the score inside the Band view with real pointer events: a drag selects bars in a card, a drag that ends over another card stays
/// on the start track, Shift+click extends, a click in another card clears and switches the active track, the timeline mirrors the selection, and
/// copy and paste act on the card that was pressed. Normal view selection is covered by ScorePreviewEditorTests and SelectionLoopLinkTests.
/// Does not own: the selection model (SelectionModelTests) or the card layout (BandCardTests).
/// Tests: SelectionLoopLink.AttachStaff, ScorePreviewInput, BandView.
/// </summary>
public class BandSelectionTests
{
    private sealed record Rig(MainWindow Window, BandView View, SongDocument Document, ArrangementView Arrangement);

    private static Rig Open()
    {
        var document = new SongDocument(() => null) { Project = FullDemoSongFactory.Create() };
        var window = new MainWindow(first: document) { Width = 1320, Height = 900 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Pick(window, "View.BandView");
        var view = window.GetLogicalDescendants().OfType<BandView>().Single();
        var arrangement = window.GetLogicalDescendants().OfType<ArrangementView>().Single();
        Settle(window);
        return new Rig(window, view, document, arrangement);
    }

    private static void Pick(MainWindow window, string commandId)
    {
        window.GetLogicalDescendants().OfType<MenuItem>().Single(m => m.Tag as string == commandId).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static void Settle(Window window)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }

    // The window point on the tab line, a quarter into a bar of the card's first system.
    private static Point InBar(BandTrackPanel panel, Window window, int bar)
    {
        var probe = panel.Staff.Probe;
        var layout = probe.Layout.GetLayout(probe.Track!).Measure(bar);
        return panel.Staff.TranslatePoint(new Point((layout.X + layout.Width * 0.25) * panel.Staff.Zoom, (probe.TabTop(0) + 6) * panel.Staff.Zoom), window)!.Value;
    }

    private static BandTrackPanel Card(Rig rig, int shownOrder) => rig.View.PanelFor(rig.View.ShownTrackIndices[shownOrder])!;

    private static void Drag(Window window, Point from, Point to)
    {
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(to, RawInputModifiers.LeftMouseButton);
        window.MouseUp(to, MouseButton.Left);
        Settle(window);
    }

    [AvaloniaFact]
    public void DragInACardSelectsBarsAndTheTimelineShowsThem()
    {
        var rig = Open();
        var card = Card(rig, 0);
        Drag(rig.Window, InBar(card, rig.Window, 1), InBar(card, rig.Window, 2));

        Assert.True(card.Staff.Editor.HasSelection);
        var range = card.Staff.Editor.SelectionCellRange;
        Assert.Equal((1, 2), (range.StartMeasure, range.EndMeasure));
        Assert.Equal((1, 2), rig.Document.Selection.BarRange);
        Assert.Equal(card.TrackIndex, rig.Document.Selection.TrackIndex);
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void DragEndingOverAnotherCardStaysOnTheStartTrack()
    {
        var rig = Open();
        var first = Card(rig, 0);
        var second = Card(rig, 1);
        Drag(rig.Window, InBar(first, rig.Window, 0), InBar(second, rig.Window, 1));

        Assert.Equal(first.TrackIndex, rig.Document.Selection.TrackIndex);
        Assert.True(first.Staff.Editor.HasSelection);
        Assert.False(second.Staff.Editor.HasSelection);
        Assert.Equal(first.TrackIndex, rig.View.SelectedTrackIndex);
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void ShiftClickExtendsTheSelection()
    {
        var rig = Open();
        var card = Card(rig, 0);
        var at = InBar(card, rig.Window, 1);
        rig.Window.MouseDown(at, MouseButton.Left);
        rig.Window.MouseUp(at, MouseButton.Left);
        var extend = InBar(card, rig.Window, 3);
        rig.Window.MouseDown(extend, MouseButton.Left, RawInputModifiers.Shift);
        rig.Window.MouseUp(extend, MouseButton.Left, RawInputModifiers.Shift);
        Settle(rig.Window);

        var range = card.Staff.Editor.SelectionCellRange;
        Assert.Equal((1, 3), (range.StartMeasure, range.EndMeasure));
        Assert.Equal((1, 3), rig.Document.Selection.BarRange);
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void ClickInAnotherCardClearsTheOldSelectionAndSwitchesTheActiveTrack()
    {
        var rig = Open();
        var first = Card(rig, 0);
        var second = Card(rig, 1);
        Drag(rig.Window, InBar(first, rig.Window, 0), InBar(first, rig.Window, 2));
        Assert.True(first.Staff.Editor.HasSelection);

        var other = InBar(second, rig.Window, 1);
        rig.Window.MouseDown(other, MouseButton.Left);
        rig.Window.MouseUp(other, MouseButton.Left);
        Settle(rig.Window);

        Assert.False(first.Staff.Editor.HasSelection);
        Assert.False(second.Staff.Editor.HasSelection);
        Assert.False(rig.Document.Selection.HasRange);
        Assert.Equal(second.TrackIndex, rig.View.SelectedTrackIndex);
        Assert.True(second.IsSelected);
        rig.Window.Close();
    }

    [AvaloniaFact]
    public async Task CopyAndPasteActOnTheSelectedCard()
    {
        var rig = Open();
        var card = Card(rig, 0);
        const int source = 1, target = 3;
        card.Staff.Editor.SetPosition(source, 0, 0);
        card.Staff.Editor.Effects.EnterFret(5, autoAdvance: false);
        Settle(rig.Window);
        Assert.DoesNotContain(rig.Document.Project.Tracks[card.TrackIndex].Measures[target].CellsForVoice(0), c => c.Notes.Count > 0);
        var from = InBar(card, rig.Window, source);
        Drag(rig.Window, from, InBar(card, rig.Window, source) + new Point(40, 0));
        Assert.True(card.Staff.Editor.HasSelection);
        Pick(rig.Window, "Edit.Copy");

        card.Staff.Editor.ClearSelection();
        card.Staff.Editor.SetPosition(target, 0, 0);
        Pick(rig.Window, "Edit.Paste");
        for (var i = 0; i < 20 && !card.Staff.Probe.Track!.Measures[target].CellsForVoice(0).Any(c => c.Notes.Count > 0); i++)
        {
            await Task.Delay(20);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.Contains(card.Staff.Probe.Track!.Measures[target].CellsForVoice(0), c => c.Notes.Count > 0);
        rig.Window.Close();
    }
}
