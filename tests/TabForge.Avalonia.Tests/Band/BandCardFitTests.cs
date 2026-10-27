using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Documents;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Avalonia.Views;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the Band view's cards at 2000x1100 in the real main window: they fit the band area (no grey block, no overflow) at the default zoom (3 cards)
/// and at a low zoom (5 cards), the first card is the only one with a fretboard and two lines, and each card starts tight under a short header.
/// the first card's fretboard is smaller (proportions kept) so the staff is readable, sits on the card's colour, the unused top above each tab is cropped, and the user can
/// resize the fretboard by its edge and zoom the cards with Ctrl+wheel and Ctrl+Plus/Minus, compact mode on or off.
/// Renders go to %TEMP%\tf-avalonia-tests\BandCardFitTests.
/// Tests: BandView, BandTrackPanel, BandScoreMenu, BandFretboardSize, BandFretboardGrip.
/// </summary>
public sealed class BandCardFitTests
{
    /// <summary>How far the cards' summed height may differ from the band area's height, in DIP.</summary>
    private const double FitTolerance = 4;
    private const double MaxLeftover = 40;

    /// <summary>The smallest staff zoom three cards with the first card's fretboard may have at 2000x1100.</summary>
    private const double ReadableStaffZoom = 0.5;

    /// <summary>How far the fretboard's drawing scale may differ from its height's share of the natural height (the board is never squeezed).</summary>
    private const double ScaleTolerance = 0.01;

    private static (MainWindow Main, BandView Band) Open(bool light)
    {
        // The cards need the demo song's tracks; the window itself now starts with an empty song.
        var song = new SongDocument(() => null) { Project = FullDemoSongFactory.Create() };
        var main = new MainWindow(first: song) { Width = 2000, Height = 1100, RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark };
        main.Show();
        Dispatcher.UIThread.RunJobs();
        main.GetLogicalDescendants().OfType<MenuItem>().Single(m => m.Tag as string == "View.BandView").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        main.UpdateLayout();
        return (main, main.GetVisualDescendants().OfType<BandView>().Single());
    }

    private static void Shot(MainWindow main, string name)
    {
        using var frame = main.CaptureRenderedFrame();
        frame!.Save(Path.Combine(DialogTestKit.RenderDirectory(nameof(BandCardFitTests)), name + ".png"));
    }

    /// <summary>The cards end inside the view and leave under the last card at most <see cref="MaxLeftover"/>, never a block.</summary>
    private static bool Fits(BandView band) => CardsHeight(band) <= band.Viewport.Height + FitTolerance && band.Viewport.Height - CardsHeight(band) <= MaxLeftover;

    private static double CardsHeight(BandView band) =>
        band.ShownTrackIndices.Select(i => band.PanelFor(i)!.Bounds.Height).Sum() + (band.ShownTrackIndices.Count - 1) * BandView.CardGap;

    private static string Detail(BandView band) => $"{band.Viewport.Height} vs {CardsHeight(band)}: " + string.Join(", ", band.ShownTrackIndices.Select(i =>
        $"{band.PanelFor(i)!.Bounds.Height:F0}/{band.PanelFor(i)!.NaturalHeight(band.PanelFor(i)!.SystemCount):F0} z{band.PanelFor(i)!.Staff.Zoom:F2} n{band.PanelFor(i)!.SystemCount}"));

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThreeCardsFitTheBandAreaAtTheDefaultZoom(bool light)
    {
        var (main, band) = Open(light);
        main.UpdateLayout();
        Shot(main, light ? "band-light" : "band-dark");
        Assert.Equal(3, band.ShownTrackIndices.Count);
        Assert.True(band.PanelFor(band.ShownTrackIndices[0])!.Staff.Zoom >= ReadableStaffZoom, Detail(band));
        Assert.True(Fits(band), Detail(band));
        var board = band.PanelFor(band.ShownTrackIndices[0])!;
        Assert.True(board.InstrumentHeight <= board.InstrumentAutoMaxHeight && board.InstrumentHeight >= board.InstrumentMinHeight);
        var scale = board.Instrument.FretboardScale();
        var expected = board.InstrumentHeight / TabForge.Presenters.FretboardScaleRules.NaturalHeight(board.InstrumentStrings, 1);
        Assert.InRange(scale, expected - ScaleTolerance, expected + ScaleTolerance);
        main.Close();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheFretboardSitsOnTheCardColour(bool light)
    {
        var (main, band) = Open(light);
        main.UpdateLayout();
        var card = band.PanelFor(band.ShownTrackIndices[0])!;
        using var frame = main.CaptureRenderedFrame();
        var corner = card.Instrument.TranslatePoint(new Point(2, 2), main)!.Value;
        var cardFill = card.TranslatePoint(new Point(card.BorderThickness.Left + 2, 0), main)!.Value;
        Assert.Equal(ButtonLabelFillTests.PixelAt(frame!, cardFill.X, corner.Y), ButtonLabelFillTests.PixelAt(frame!, corner.X, corner.Y));
        main.Close();
    }

    [AvaloniaFact]
    public void NoCardKeepsTheUnusedRoomAboveItsTab()
    {
        var (main, band) = Open(false);
        foreach (var card in band.ShownTrackIndices.Select(i => band.PanelFor(i)!))
        {
            var probe = card.Staff.Probe;
            var needed = probe.Layout.ExtraAboveNeeded(0, probe.Layout.GetLayout(probe.Track!).Systems[0].LastMeasure);
            // Above the first tab line the window shows the bar number row's room, the marks lane and what the first system's marks need: nothing more.
            var shown = probe.TabTop(0) - card.WindowTop / card.Staff.Zoom;
            var kept = TabForge.Views.Score.ScoreGeometryProbe.TightMarginTop + (probe.TabTop(0) - probe.StaffTop(0)) + needed;
            Assert.True(shown <= kept + 1, $"{card.TrackIndex}: {shown} > {kept}");
        }
        main.Close();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void DraggingTheFretboardEdgeResizesItAndTheCardsFitAgain(bool compact)
    {
        var (main, band) = Open(false);
        if (compact) Click(main, "View.CompactMode");
        main.UpdateLayout();
        var card = band.PanelFor(band.ShownTrackIndices[0])!;
        var before = card.InstrumentHeight;
        var grip = main.GetVisualDescendants().OfType<BandFretboardGrip>().First(g => g.IsVisible && g.Name != BandTrackPanel.CardGripName);
        var from = grip.TranslatePoint(new Point(grip.Bounds.Width / 2, grip.Bounds.Height / 2), main)!.Value;
        var to = new Point(from.X, from.Y - ResizeDragDistance);
        main.MouseDown(from, MouseButton.Left);
        main.MouseMove(to, RawInputModifiers.LeftMouseButton);
        main.MouseUp(to, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        main.UpdateLayout();
        Assert.True(card.InstrumentHeight < before, $"{card.InstrumentHeight} vs {before}");
        Assert.False(card.InstrumentIsAuto);
        Assert.Equal(card.InstrumentHeight, band.FretboardSize.UserHeight(card.InstrumentShape)!.Value, 1);
        Assert.True(CardsHeight(band) <= band.Viewport.Height + FitTolerance, Detail(band));
        main.Close();
    }

    /// <summary>A drag that loses its pointer capture ends: later moves report nothing.</summary>
    [AvaloniaFact]
    public void TheFretboardGripDragEndsWhenCaptureIsLost()
    {
        var board = new Border { Height = 100 };
        var grip = new BandFretboardGrip(board);
        var window = new Window { Width = 300, Height = 300, Content = new StackPanel { Children = { board, grip } } };
        window.Show();
        IPointer? pointer = null;
        var released = 0;
        var dragged = 0;
        grip.AddHandler(InputElement.PointerPressedEvent, (_, e) => pointer = e.Pointer, handledEventsToo: true);
        grip.Released += () => released++;
        grip.Dragged += _ => dragged++;
        var at = grip.TranslatePoint(new Point(10, 4), window)!.Value;
        window.MouseDown(at, MouseButton.Left);
        pointer!.Capture(null);
        window.MouseMove(new Point(at.X, at.Y + 20));
        Assert.Equal(1, released);
        Assert.Equal(0, dragged);
    }

    [Fact]
    public void TheChosenFretboardSizeIsKeptInTheBandSettings()
    {
        var settings = new BandSettings();
        var saves = 0;
        var size = new BandFretboardSize(() => settings, () => saves++);
        Assert.Null(size.UserHeight(ReferenceShape));
        size.Choose(ChosenHeight, ReferenceShape);
        size.Commit();
        Assert.Equal(1, saves);
        Assert.True(settings.InstrumentWidth > 0);
        Assert.Equal(ChosenHeight, new BandFretboardSize(() => settings, () => { }).UserHeight(ReferenceShape)!.Value, 1);
        size.Reset();
        Assert.Null(size.UserHeight(ReferenceShape));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void CtrlWheelAndCtrlPlusMinusZoomTheBandCards(bool compact)
    {
        var (main, band) = Open(false);
        if (compact) Click(main, "View.CompactMode");
        main.UpdateLayout();
        var menu = band.ScoreMenu!;
        var start = menu.LaneZoom;
        var cardPoint = band.PanelFor(band.ShownTrackIndices[0])!.Staff.TranslatePoint(new Point(200, 20), main)!.Value;
        main.MouseWheel(cardPoint, new Vector(0, 1), RawInputModifiers.Control);
        Assert.True(menu.LaneZoom > start, "ctrl+wheel up");
        var afterWheel = menu.LaneZoom;
        main.KeyPress(Key.OemPlus, RawInputModifiers.Control, PhysicalKey.Equal, "=");
        Assert.True(menu.LaneZoom > afterWheel, "ctrl+plus");
        main.KeyPress(Key.OemMinus, RawInputModifiers.Control, PhysicalKey.Minus, "-");
        Assert.Equal(afterWheel, menu.LaneZoom, 6);
        main.Close();
    }

    private const double ResizeDragDistance = 30;
    private const double ChosenHeight = 120;
    private const int ReferenceStrings = 6;
    private static FretboardGeometryRules.Input ReferenceShape => new(ReferenceStrings, 24, 24, 1, false, 0);

    private static void Click(MainWindow main, string tag) =>
        main.GetLogicalDescendants().OfType<MenuItem>().Single(m => m.Tag as string == tag).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

    [AvaloniaFact]
    public void FiveCardsFitWithTheirFretboardOff()
    {
        var (main, band) = Open(false);
        for (var i = 3; i < 5; i++) band.ToggleShown(i);
        band.SetFretboardOn(band.ShownTrackIndices[0], false);
        band.ScoreMenu!.WheelZoom(false);
        // The zoom status is an Info notice, which the unified top bar drops, so no notice row takes room at the window's bottom any more; one that shows is dismissed anyway.
        main.GetVisualDescendants().OfType<StatusNoticeBarView>().FirstOrDefault(n => n.IsVisible)?.Hide();
        main.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        main.UpdateLayout();
        Shot(main, "band-five-low");
        Assert.Equal(5, band.ShownTrackIndices.Count);
        Assert.True(Fits(band), Detail(band));
        main.Close();
    }

    [AvaloniaFact]
    public void TheTightLineBandKeepsTheRoomTheNumberRowAndTheTabNeed()
    {
        var (main, band) = Open(false);
        foreach (var card in band.ShownTrackIndices.Select(i => band.PanelFor(i)!))
        {
            var probe = card.Staff.Probe;
            Assert.True(probe.TightLineBand);
            // The bar number row sits above the tab line, so the room above must hold it.
            Assert.True(probe.StaffTop(0) >= TabForge.Views.Score.ScoreGeometryProbe.NumberRowReach, $"{card.TrackIndex}: {probe.StaffTop(0)}");
        }
        main.Close();
    }
}
