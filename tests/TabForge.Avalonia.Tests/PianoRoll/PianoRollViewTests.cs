using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the piano roll pane in a window: a drag moves only a preview (no song change, no undo step, however many moves) and commits once on
//   release; a tab-track drag answered Cancel leaves the song and the undo history as they were; the tool keys, the arrow keys and Esc; the
//   velocity lane; the box selection that sets the loop; the shared snap magnet.
// Does not own: the edits, the commit rules and the sources (the other PianoRoll tests).
// Tests: PianoRollView, PianoRollSurface.
public sealed class PianoRollViewTests
{
    private const int Moves = 40;

    private static (PianoRollView View, Window Window) Show(SongDocument document, PianoRollHost? host = null)
    {
        var view = new PianoRollView(host ?? new PianoRollHost { Document = () => document });
        var window = new Window { Width = 900, Height = 600, Content = view };
        window.Show();
        view.Show(document, 0, newVisit: true);
        window.UpdateLayout();
        return (view, window);
    }

    private static double[] Starts(SongDocument document) =>
        document.Project.Tracks[0].AudioClips[0].Notes!.Select(n => System.Math.Round(n.StartSec, 6)).ToArray();

    [AvaloniaFact]
    public void ADragMovesOnlyAPreviewAndCommitsOneStepOnRelease()
    {
        var document = PianoRollSongs.Midi();
        var (view, window) = Show(document);
        var surface = view.Surface;
        var at = surface.NoteRect(view.Presenter.Notes[0]).Center;
        var steps = document.Undo.UndoCount;
        var marks = 0;
        document.Project.TimelineMarked += _ => marks++;

        Assert.True(surface.Press(at, KeyModifiers.None));
        for (var i = 1; i <= Moves; i++) surface.DragTo(at + new Point(i * surface.PixelsPerBeat / Moves, 0));

        Assert.Equal(steps, document.Undo.UndoCount);   // nothing stored, synced or marked while dragging
        Assert.Equal(0, marks);
        Assert.Equal(new[] { 0.5, 1.0 }, Starts(document));

        surface.Release(KeyModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(steps + 1, document.Undo.UndoCount);
        Assert.Equal(new[] { 1.0, 1.0 }, Starts(document).OrderBy(s => s).ToArray());   // moved one beat (half a second), snapped to the beat grid
        Assert.False(surface.Busy);
        window.Close();
    }

    [AvaloniaFact]
    public void AProgramLaneDragPreviewsWithoutSyncingAndCommitsOneStepOnRelease()
    {
        var document = PianoRollSongs.Midi();
        var syncs = 0; var saves = 0; var marks = 0;
        document.Project.TimelineMarked += _ => marks++;
        var (view, window) = Show(document, new PianoRollHost { Document = () => document, AfterEdit = _ => syncs++, SaveSettings = () => saves++ });
        view.ShowLane(PianoRollControllers.Program);   // saves the lane choice once; the drag below saves nothing
        saves = 0;
        var surface = view.Surface;
        var steps = document.Undo.UndoCount;
        var at = new Point(surface.XOf(1), surface.LaneTop + surface.Lane / 2);

        Assert.True(surface.Press(at, KeyModifiers.None));
        for (var i = 1; i <= 50; i++) surface.DragTo(at + new Point(i * surface.PixelsPerBeat / 50, i * 0.2));

        Assert.Equal(steps, document.Undo.UndoCount);   // nothing stored, synced, saved or marked while dragging
        Assert.Equal(0, marks);
        Assert.Equal(0, syncs);
        Assert.Equal(0, saves);

        surface.Release(KeyModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(steps + 1, document.Undo.UndoCount);   // one gesture: one step, one engine sync, no settings broadcast
        Assert.Equal(1, syncs);
        Assert.Equal(0, saves);
        Assert.Single(view.Presenter.Points(PianoRollControllers.Program));
        Assert.False(surface.Busy);
        window.Close();
    }

    [AvaloniaFact]
    public void CancellingTheTabPromptDiscardsTheDragAndLeavesNoStep()
    {
        var document = PianoRollSongs.Tab();
        var asked = 0;
        var (view, window) = Show(document, new PianoRollHost { Document = () => document, ConfirmTab = _ => { asked++; return Task.FromResult(false); } });
        var surface = view.Surface;
        var at = surface.NoteRect(view.Presenter.Notes[0]).Center;
        var steps = document.Undo.UndoCount;

        surface.Press(at, KeyModifiers.None);
        surface.DragTo(at + new Point(surface.PixelsPerBeat * 2, 0));
        surface.Release(KeyModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, asked);
        Assert.Equal(steps, document.Undo.UndoCount);
        Assert.Single(document.Project.Tracks[0].Measures[0].Cells[0].Notes);
        Assert.False(surface.Busy);
        window.Close();
    }

    [AvaloniaFact]
    public void ToolKeysArrowsAndEscapeWorkWhileThePianoRollHasFocus()
    {
        var document = PianoRollSongs.Midi();
        var (view, window) = Show(document);
        var surface = view.Surface;
        Assert.True(surface.HandleKey(Key.D2, KeyModifiers.None));
        Assert.Equal(PianoRollTool.Erase, view.Presenter.Tool);   // 1 Edit, 2 Erase, 3 Split, 4 Mute, 5 Glue
        Assert.True(surface.HandleKey(Key.D1, KeyModifiers.None));
        Assert.False(surface.HandleKey(Key.Up, KeyModifiers.None));   // nothing selected: the key goes on to the window
        view.Presenter.Select(new[] { 0 });
        Assert.True(surface.HandleKey(Key.Up, KeyModifiers.Shift));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[] { 72, 64 }, document.Project.Tracks[0].AudioClips[0].Notes!.Select(n => n.Pitch).ToArray());   // Shift+Up: an octave
        Assert.True(surface.HandleKey(Key.Escape, KeyModifiers.None));
        Assert.Empty(view.Presenter.Selected);
        Assert.False(surface.HandleKey(Key.Escape, KeyModifiers.None));   // the second Esc goes on (the window clears the loop)
        window.Close();
    }

    [AvaloniaFact]
    public void ABoxSelectionSelectsTheNotesButNeverTouchesTheLoopWhileTheRulerMakesAndClearsIt()
    {
        var document = PianoRollSongs.Midi();
        (int, int, int, int, bool)? loop = null;
        var cleared = 0;
        var (view, window) = Show(document, new PianoRollHost { Document = () => document, MakeLoop = (a, ac, b, bc, sel) => loop = (a, ac, b, bc, sel), ClearLoop = () => cleared++ });
        var surface = view.Surface;
        var first = surface.NoteRect(view.Presenter.Notes[0]);
        var second = surface.NoteRect(view.Presenter.Notes[1]);
        var from = new Point(first.Left - 4, second.Top - 4);
        surface.PressRight(from, KeyModifiers.None);
        surface.Release(KeyModifiers.None);
        Assert.Null(loop);   // a plain click never makes a loop
        surface.PressRight(from, KeyModifiers.None);
        surface.DragTo(new Point(second.Right + 4, first.Bottom + 4));
        surface.Release(KeyModifiers.None);
        Assert.Equal(2, view.Presenter.Selected.Count);
        Assert.Null(loop);   // 26 Oct: in the piano roll a selection never makes or changes the loop
        view.Presenter.SelectAll();
        Assert.Null(loop);

        // The piano roll's ruler: a drag makes the loop (sub-bar), a plain click moves the playhead and clears it, as the timeline ruler does.
        var ruler = PianoRollSurface.RulerSize / 2;
        surface.Press(new Point(surface.XOf(1), ruler), KeyModifiers.Shift);
        surface.DragTo(new Point(surface.XOf(2.5), ruler));
        surface.Release(KeyModifiers.None);
        Assert.Equal((0, 4, 0, 9, false), loop);   // beats 1 to 2.5: sixteenth cells 4 to 9 of bar 1
        surface.Press(new Point(surface.XOf(3), ruler), KeyModifiers.None);
        surface.Release(KeyModifiers.None);
        Assert.Equal(1, cleared);
        window.Close();
    }

    [AvaloniaFact]
    public void TheVelocityLaneSetsABarOnReleaseAsOneStepWithNoPrompt()
    {
        var document = PianoRollSongs.Tab();
        var asked = 0;
        var (view, window) = Show(document, new PianoRollHost { Document = () => document, ConfirmTab = _ => { asked++; return Task.FromResult(true); } });
        var surface = view.Surface;
        var x = surface.XOf(view.Presenter.Notes[0].Start) + 1;
        var steps = document.Undo.UndoCount;
        surface.Press(new Point(x, surface.VelocityY(30)), KeyModifiers.None);
        Assert.Equal(steps, document.Undo.UndoCount);
        surface.Release(KeyModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(steps + 1, document.Undo.UndoCount);
        Assert.Equal(0, asked);
        Assert.InRange(document.Project.Tracks[0].Measures[0].Cells[0].Notes[0].Velocity, 27, 33);
        window.Close();
    }

    [AvaloniaFact]
    public void TheMagnetIsTheTransportsSnapSettings()
    {
        var document = PianoRollSongs.Midi();
        var snap = new SnapSettings { Grid = "1/8T" };
        var (view, window) = Show(document, new PianoRollHost { Document = () => document, Snap = () => snap, ToggleSnap = () => snap.Enabled = !snap.Enabled });
        view.RefreshSnap();
        Assert.Equal(1.0 / 3, view.Surface.StepAt(0), 6);
        snap.Enabled = false;
        view.RefreshSnap();
        Assert.Equal(0.25, view.Surface.StepAt(0), 6);   // the magnet off: arrows step a sixteenth
        window.Close();
    }

    [AvaloniaFact]
    public void SnapItemsFollowWhichNotesAreLeftOutNotHowMany()
    {
        var document = PianoRollSongs.Midi();
        var (view, window) = Show(document);
        var notes = view.Presenter.Notes;
        Assert.True(notes.Count >= 2);
        var first = view.Surface.Items(new[] { 0 }).ToArray();
        var second = view.Surface.Items(new[] { 1 }).ToArray();   // same count, other note
        Assert.Equal(notes[0].Start, second[0]);
        Assert.Equal(notes[1].Start, first[0]);
        window.Close();
    }

    private static ToggleButton ToolButton(Window window, string tipStart) =>
        window.GetVisualDescendants().OfType<ToggleButton>().Single(b => ToolTip.GetTip(b) is string tip && tip.StartsWith(tipStart, System.StringComparison.Ordinal));

    [AvaloniaFact]
    public void TheActiveToolTakesTheToolsPaneSelectedLookAndTheOthersStayPlain()
    {
        var document = PianoRollSongs.Midi();
        var (view, window) = Show(document);
        var edit = ToolButton(window, "Select (V)");
        var erase = ToolButton(window, "Erase (2)");
        var light = edit.ActualThemeVariant == ThemeVariant.Light;
        var selected = AvaloniaPalette.ColorOf(AvaloniaPalette.Get(AvaloniaPalette.BrushPaletteNames["SelectedTintBrush"], light));
        Assert.True(edit.IsChecked == true);
        Assert.Equal(selected, ((ISolidColorBrush)edit.Background!).Color);
        Assert.Equal(0, ((ISolidColorBrush)erase.Background!).Color.A);
        Assert.Equal(0, ((ISolidColorBrush)erase.BorderBrush!).Color.A);
        Assert.True(view.Surface.HandleKey(Key.D2, KeyModifiers.None));   // the keys run the same path as the buttons: RefreshTools
        Assert.True(erase.IsChecked == true);
        Assert.Equal(selected, ((ISolidColorBrush)erase.Background!).Color);
        Assert.Equal(0, ((ISolidColorBrush)edit.Background!).Color.A);
        Assert.Equal(0, ((ISolidColorBrush)edit.BorderBrush!).Color.A);
        window.Close();
    }

    [AvaloniaFact]
    public void ToolbarButtonsRestTransparentAndALitToggleTakesTheSelectedFillAndEdge()
    {
        var document = PianoRollSongs.Midi();
        var (_, window) = Show(document);
        var zoomIn = ToolbarButton(window, "Zoom in");
        var scale = (ToggleButton)ToolbarButton(window, "Step input");
        var light = zoomIn.ActualThemeVariant == ThemeVariant.Light;
        var selected = AvaloniaPalette.ColorOf(AvaloniaPalette.Get(AvaloniaPalette.BrushPaletteNames["SelectedTintBrush"], light));
        var edge = AvaloniaPalette.ColorOf(AvaloniaPalette.Get(AvaloniaPalette.BrushPaletteNames["SelectedOutlineBrush"], light));
        scale.IsChecked = false;
        Assert.Equal(0, ((ISolidColorBrush)zoomIn.Background!).Color.A);
        Assert.Equal(0, ((ISolidColorBrush)scale.Background!).Color.A);
        scale.IsChecked = true;
        Assert.Equal(selected, ((ISolidColorBrush)scale.Background!).Color);
        Assert.Equal(edge, ((ISolidColorBrush)scale.BorderBrush!).Color);
        Assert.Equal(0, ((ISolidColorBrush)zoomIn.Background!).Color.A);
        window.Close();
    }

    private static Button ToolbarButton(Window window, string tip) =>
        window.GetVisualDescendants().OfType<Button>().First(b => (ToolTip.GetTip(b) as string)?.StartsWith(tip, System.StringComparison.Ordinal) == true);
}
