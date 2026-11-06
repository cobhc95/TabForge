using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: Capo card placement, selection, removal, hover geometry and drag commit contract.
// Does not own: pitch/save rules or the instrument panel interaction.
public sealed class CapoPlacementTests
{
    [AvaloniaFact]
    public void FretBoxAndStringPickerFollowSelectedWorkingCapo()
    {
        var track = new TrackModel { Name = "Capo", StringTunings = new List<int> { 64, 59, 55, 50, 45, 40 }, Measures = TemplateFactory.Measures(1) };
        var window = new TrackPropertiesWindow(track) { Width = 1600 };
        using var lifetime = new WindowLifetime(window);
        var picker = Named<WrapPanel>(window, "CapoStrings");
        var box = Named<NumericUpDown>(window, "CapoFretBox");
        Assert.False(picker.IsVisible);   // From and To belong to a partial capo
        box.Value = 5;
        Assert.Equal(5, Named<TrackPropertiesWindowCapoBoard>(window, "CapoBoard").ShownFret);
        box.Value = 0;
        Assert.Equal(0, window.WorkingEffectiveCapo(0));
        DialogTestKit.Click(Named<ToggleButton>(window, "CapoPlacementPartial"));
        Assert.True(picker.IsVisible);
        Assert.True(picker.IsEnabled);
        Assert.Equal(1, Named<ComboBox>(window, "CapoStringFrom").SelectedIndex);   // usable before any partial exists
        Named<ComboBox>(window, "CapoStringTo").SelectedIndex = 4;
        ClickFret(window, 2);
        Assert.Equal(new[] { false, true, true, true, true, false }, window.PartialCapos[0].Strings);
        DialogTestKit.Click(Named<Button>(window, "RemoveSelectedCapoButton"));
        Named<ComboBox>(window, "CapoStringTo").SelectedIndex = 3;
        ClickFret(window, 4);
        ClickFret(window, 7);
        var partial = window.PartialCapos[1];
        var from = Named<ComboBox>(window, "CapoStringFrom");
        var to = Named<ComboBox>(window, "CapoStringTo");
        to.SelectedIndex = 5;
        Assert.True(partial.Strings[5]);
        Assert.True(Named<TrackPropertiesWindowCapoBoard>(window, "CapoBoard").Covered[5]);
        to.SelectedIndex = 4;
        Assert.False(partial.Strings[5]);
        Assert.False(Named<TrackPropertiesWindowCapoBoard>(window, "CapoBoard").Covered[5]);
        box.Value = 9;
        Assert.Equal(9, partial.Fret);
        Assert.Equal(4, window.PartialCapos[0].Fret);
        from.SelectedIndex = 5;   // past the To box: it follows, so the range is one string
        Assert.Equal(new[] { false, false, false, false, false, true }, partial.Strings);
        Assert.Equal(5, to.SelectedIndex);
        from.SelectedIndex = 1;
        ClickFret(window, 4);
        Assert.Equal(4m, box.Value);
        Assert.Equal(3, to.SelectedIndex);
        box.Value = 6;
        Assert.Equal(6, window.PartialCapos[0].Fret);
        Assert.Equal(9, partial.Fret);
        ClickFret(window, 9);
        Assert.Equal(9m, box.Value);
        Assert.Equal(5, to.SelectedIndex);
        Assert.True(Named<TrackPropertiesWindowCapoBoard>(window, "CapoBoard").Covered[5]);
        Assert.Equal(0, track.Capo);
        Assert.Null(track.PartialCapos);
    }

    // Closing the owner uses OwnerWindowClosing, never the dirty editor's real discard modal.
    private sealed class WindowLifetime : IDisposable
    {
        private readonly Window _owner = new();
        public WindowLifetime(Window window)
        {
            if (window is TrackPropertiesWindow editor)
            {
                editor.ConfirmNoteMove = _ => System.Threading.Tasks.Task.FromResult(true);
                editor.ConfirmUndisclosedPitchChange = _ => System.Threading.Tasks.Task.FromResult(true);
            }
            _owner.Show();
            try { window.Show(_owner); }
            catch { _owner.Close(); throw; }
        }
        public void Dispose() => _owner.Close();
    }

    [AvaloniaFact]
    public void StringBoxBackgroundsFitTheBoxSoTheirOutlineIsWhole()
    {
        var track = new TrackModel { Name = "Capo", StringTunings = new List<int> { 64, 59, 55, 50, 45, 40 }, Measures = TemplateFactory.Measures(1) };
        var window = new TrackPropertiesWindow(track) { Width = 1600 };
        using var lifetime = new WindowLifetime(window);
        DialogTestKit.Click(Named<ToggleButton>(window, "CapoPlacementPartial"));
        Dispatcher.UIThread.RunJobs();
        foreach (var name in new[] { "CapoStringFrom", "CapoStringTo" })
        {
            var box = Named<ComboBox>(window, name);
            var background = box.GetVisualDescendantsOf<Border>().First(b => b.Name == "Background");
            Assert.True(background.Bounds.X >= 0 && background.Bounds.Right <= box.Bounds.Width, $"{name}: {background.Bounds} in {box.Bounds.Width}");
        }
    }

    internal static T Named<T>(Window window, string name) where T : Control =>
        window.GetLogicalDescendants().OfType<T>().Single(c => c.Name == name);

    internal static void ClickFret(TrackPropertiesWindow window, int fret)
    {
        window.UpdateLayout(); // Partial-mode visibility can move the board before the next pointer event.
        var board = Named<TrackPropertiesWindowCapoBoard>(window, "CapoBoard");
        var partial = window.PartialCapos.FirstOrDefault(p => p.Fret == fret);
        var y = partial is null ? board.StringY(2) : board.StringY(partial.Strings.FindIndex(covered => covered));
        var at = board.TranslatePoint(new Point(board.FretX(fret), y), window)!.Value;
        window.MouseMove(at);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
    }

    private static void ClickToggle(Window window, ToggleButton button)
    {
        window.UpdateLayout();
        var at = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseMove(at);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
    }

    [AvaloniaFact]
    public void CapoBarKeepsTheArtworkProportionsAndNarrowsOnlyOnLateFrets()
    {
        var layout = FretboardGeometryRules.Compute(new FretboardGeometryRules.Input(6, 12, 12, 1, false, 0, false), new UiRect(0, 0, 1200, 240), limits: FretboardView.BoardLimits);
        var wood = FretboardArtist.Wood(layout, bass: false);
        var natural = wood.Height * FretboardBoardPainter.CapoOverhang * DialogMetrics.TrackCapoImageWidth / DialogMetrics.TrackCapoImageVisible * DialogMetrics.TrackCapoBarShare;
        Assert.Equal(natural, FretboardBoardPainter.BarBounds(layout, wood, 3).Width, 3);
        Assert.True(natural > wood.Height / 8, "the visible bar shrank to a sliver");
    }

    [AvaloniaFact]
    public void CapsuleActiveHoverAndFocusShareInstrumentAccentToken()
    {
        Assert.Equal("InstrumentAccent", TrackPropertiesWindow.CapoToggle.AccentToken);
        var capsule = new TrackPropertiesWindow.CapoToggle { Content = "Partial" };
        var window = new Window { Content = capsule, Width = 100, Height = 40 };
        using var lifetime = new WindowLifetime(window);
        window.MouseMove(new Point(10, 10));
        Assert.True(capsule.IsPointerOver);
        Assert.NotEqual(true, capsule.IsChecked);
        ClickToggle(window, capsule);
        Assert.True(capsule.IsChecked);
    }

    internal static void PlacePartial(TrackPropertiesWindow window, int fret)
    {
        DialogTestKit.Click(Named<ToggleButton>(window, "CapoPlacementPartial"));
        ClickFret(window, fret);
    }

    [AvaloniaFact]
    public void CapoCardTopRowIsOneLineWithPlainFretBoxAndRightHint()
    {
        var track = new TrackModel { Name = "Capo", StringTunings = new List<int> { 64, 59, 55, 50, 45, 40 }, Measures = TemplateFactory.Measures(1) };
        var window = new TrackPropertiesWindow(track) { Width = 1600 };
        using var lifetime = new WindowLifetime(window);
        Assert.Equal(DialogMetrics.TrackCapoCardBoardHeight, Named<TrackPropertiesWindowCapoBoard>(window, "CapoBoard").Height);
        var box = Named<NumericUpDown>(window, "CapoFretBox");
        Assert.False(box.ShowButtonSpinner);
        Assert.Equal(0, box.Minimum);
        Assert.Equal(TrackPropertiesPresenter.MaxCapo, box.Maximum);
        var row = Assert.IsType<StackPanel>(box.Parent);
        Assert.Equal(Orientation.Horizontal, row.Orientation);
        var chips = Named<WrapPanel>(window, "CapoChips");
        Assert.Contains(Named<ToggleButton>(window, "CapoPlacementFull"), chips.Children);
        Assert.Contains(Named<ToggleButton>(window, "CapoPlacementPartial"), chips.Children);
        // One line, in order: the Full/Partial chips, the Fret box, then the From/To strings.
        var from = Named<ComboBox>(window, "CapoStringFrom");
        var to = Named<ComboBox>(window, "CapoStringTo");
        var stringsPanel = Named<WrapPanel>(window, "CapoStrings");
        Assert.True(row.Children.IndexOf(chips) < row.Children.IndexOf(box));
        Assert.True(row.Children.IndexOf(box) < row.Children.IndexOf(stringsPanel));
        Assert.Equal(DialogMetrics.TrackCapoStringBoxWidth, from.Width);
        Assert.Equal(DialogMetrics.TrackCapoStringBoxWidth, to.Width);
    }

    [AvaloniaFact]
    public void PlacementSelectionDragLimitAndRemovalOnlyChangeWorkingCopies()
    {
        var track = new TrackModel { Name = "Capo", StringTunings = new List<int> { 64, 59, 55, 50, 45, 40 }, Measures = TemplateFactory.Measures(1) };
        var window = new TrackPropertiesWindow(track);
        using var lifetime = new WindowLifetime(window);
        Dispatcher.UIThread.RunJobs();
        var board = Named<TrackPropertiesWindowCapoBoard>(window, "CapoBoard");
        Assert.True(Named<ToggleButton>(window, "CapoPlacementFull").IsChecked);
        Assert.DoesNotContain(window.GetLogicalDescendants().OfType<Control>(), c => c.Name == "CapoSettings");
        ClickFret(window, 5);
        Assert.Equal(5, board.ShownFret);
        Assert.Null(board.GhostBounds);
        Assert.Equal(0, track.Capo);
        PlacePartial(window, 7);
        var partial = Assert.Single(window.PartialCapos);
        Assert.Equal(7, partial.Fret);
        Assert.Equal(new[] { false, true, true, true, false, false }, partial.Strings);
        Assert.Equal(5, window.WorkingEffectiveCapo(0));
        var at = board.TranslatePoint(new Point(board.FretX(7), board.Bounds.Height / 2), window)!.Value;
        window.MouseMove(at);
        Assert.Null(board.Cursor);   // the hover shows the ghost, outline and tooltip; the pointer shape never changes
        Assert.Equal("Partial capo fret 7, drag to move", ToolTip.GetTip(board));
        var commits = new List<int>();
        board.CommitRequested += fret => { commits.Add(fret); return true; };
        Assert.True(board.BeginDrag(new Point(board.FretX(7), board.Bounds.Height / 2)));
        board.EndDrag();
        Assert.Empty(commits);
        Assert.True(board.BeginDrag(new Point(board.FretX(7), board.Bounds.Height / 2)));
        board.Drag(new Point(board.FretX(9), board.Bounds.Height / 2));
        Assert.Equal(9, board.ShownFret);
        Assert.Equal(7, partial.Fret);
        Assert.Empty(commits);
        board.EndDrag();
        board.EndDrag();
        Assert.Equal(new[] { 9 }, commits);
        Assert.Equal(9, partial.Fret);
        foreach (var fret in new[] { 2, 4, 6 }) ClickFret(window, fret);
        Assert.Equal(4, window.PartialCapos.Count);
        ClickFret(window, 3);
        Assert.Equal(4, window.PartialCapos.Count);
        Assert.Equal("Partial capo limit reached", ToolTip.GetTip(board));
        var left = board.TranslatePoint(new Point(0, board.Bounds.Height / 2), window)!.Value;
        window.MouseDown(left, MouseButton.Left);
        window.MouseUp(left, MouseButton.Left);
        Assert.Equal(4, window.PartialCapos.Count);
        ClickFret(window, 5); // select full, not a placement
        DialogTestKit.Click(Named<Button>(window, "RemoveSelectedCapoButton"));
        Assert.Equal(0, window.WorkingEffectiveCapo(0));
        DialogTestKit.Click(Named<Button>(window, "RemoveSelectedCapoButton"));
        DialogTestKit.Click(Named<Button>(window, "RemoveAllCaposButton"));
        Assert.Empty(window.PartialCapos);
        Assert.Equal(0, window.WorkingEffectiveCapo(2));
        Assert.Null(track.PartialCapos);
        DialogTestKit.Click(Named<ToggleButton>(window, "CapoPlacementFull"));
        ClickFret(window, 5);
        PlacePartial(window, 7);
        ClickFret(window, 9);
        Assert.Equal(2, window.PartialCapos.Count);
        Assert.Equal(5, window.WorkingEffectiveCapo(0));
        DialogTestKit.Click(Named<Button>(window, "RemoveAllCaposButton"));
        Assert.Empty(window.PartialCapos);
        Assert.Equal(0, window.WorkingEffectiveCapo(0));
        DialogTestKit.Click(Named<Button>(window, "CancelButton"));
        Assert.False(window.Applied);
    }

    [AvaloniaTheory]
    [InlineData(320.0)]
    [InlineData(600.0)]
    [InlineData(900.0)]
    public void GhostBoundsMatchArtworkCentreForEveryFretAndNearestCentreRoundTrips(double width)
    {
        var board = new FretboardView();
        board.Show(0, Enumerable.Repeat(true, 6).ToArray());
        var window = new Window { Width = width, Height = DialogMetrics.TrackCapoBoardHeight, Content = board };
        using var lifetime = new WindowLifetime(window);
        var layout = FretboardGeometryRules.Compute(new FretboardGeometryRules.Input(6, 12, 12, 1, false, 0, false),
            new UiRect(0, 0, board.Bounds.Width, board.Bounds.Height), limits: FretboardView.BoardLimits);
        var wood = FretboardArtist.Wood(layout, false);
        var image = new FretboardAssets().Capo(FretboardStyles.DefaultLook.CapoLook, shadow: false);
        Assert.NotNull(image);
        var size = FretboardAssets.SizeOf(image)!.Value;
        for (var fret = 1; fret <= 12; fret++)
        {
            var x = FretboardGeometryRules.FretX(layout, fret);
            window.MouseMove(new Point(x, board.StringY(2)));
            Assert.NotNull(board.GhostBounds);
            var ghost = board.GhostBounds!.Value;
            var height = wood.Height * FretboardBoardPainter.CapoOverhang * DialogMetrics.TrackCapoImageHeight / DialogMetrics.TrackCapoImageVisible;
            var bar = FretboardBoardPainter.DrawCapoBar(new NullDrawContext(), image, null, layout, wood, fret, height * size.Width / size.Height, height);
            var drawnCentre = bar.X + bar.Width / 2;
            Assert.InRange(Math.Abs(ghost.X + ghost.Width / 2 - drawnCentre), 0, 1);
            Assert.InRange(Math.Abs(ghost.Width - bar.Width), 0, 1);
            Assert.True(ghost.Width <= Math.Abs(FretboardGeometryRules.WireX(layout, fret) - FretboardGeometryRules.WireX(layout, fret - 1)) + 1);
            Assert.True(ghost.X >= FretboardGeometryRules.WireX(layout, fret - 1) - 1);
            Assert.True(ghost.Right <= FretboardGeometryRules.WireX(layout, fret) + 1);
            board.ShowCapos(fret, Array.Empty<PartialCapo>(), -1, 6, false);
            var ring = board.SelectionBounds!.Value;
            Assert.InRange(Math.Abs(ring.X - ghost.X), 0, 1);
            Assert.InRange(Math.Abs(ring.Width - ghost.Width), 0, 1);
            board.Show(0, Enumerable.Repeat(true, 6).ToArray());
            Assert.Equal(fret, FretboardCapoRules.FretAtX(layout, x));
        }
        for (var x = layout.Board.X; x <= layout.Board.Right; x += 1)
        {
            var nearest = Enumerable.Range(1, 12).OrderBy(f => Math.Abs(FretboardGeometryRules.FretX(layout, f) - x)).First();
            Assert.Equal(nearest, FretboardCapoRules.FretAtX(layout, x));
        }
    }
}
