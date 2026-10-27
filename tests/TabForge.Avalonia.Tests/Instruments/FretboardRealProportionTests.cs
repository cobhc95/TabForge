using System;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using TabForge.Avalonia.Platform;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the fretboard keeps the real proportions of its board. At three dock widths (normal mode) and three band card heights (band mode) the
/// board's width : height is the real aspect of its shown frets and strings within 2%. The wide dock (2000 px) is saved in both themes to
/// %TEMP%\tf-avalonia-tests\FretboardRealProportionTests for a look by eye.
/// Tests: InstrumentPanelView (FretboardBoardLimits.RealProportions), BandFretboardSize.
/// </summary>
public class FretboardRealProportionTests
{
    private const double AspectTolerance = 0.02;
    private const double CaptureWidth = 2000;
    private static readonly double[] DockWidths = { 600, 1000, CaptureWidth };
    /// <summary>Band card heights as shares of the board's natural height (the band's own range is half to full).</summary>
    private static readonly double[] BandHeightShares = { 0.6, 0.8, 1.0 };

    [AvaloniaFact]
    public void BoardKeepsItsRealAspectAtThreeSizesInNormalAndBandMode()
    {
        var track = DemoSongFactory.Create().Tracks[0];
        var shape = Panel(track, CaptureWidth, InstrumentPanelView.PanelHeight, FretboardScaleRules.MinScale).FretInput;
        var natural = FretboardScaleRules.NaturalHeight(shape.Strings, 1);

        foreach (var width in DockWidths)
            AssertRealAspect(Panel(track, width, InstrumentPanelView.PanelHeight, FretboardScaleRules.MinScale), width, InstrumentPanelView.PanelHeight, "normal dock");

        foreach (var share in BandHeightShares)
        {
            var height = share * natural;
            var width = BandFretboardSize.WidthFor(height, shape);
            AssertRealAspect(Panel(track, width, height, BandFretboardSize.MinScale), width, height, "band card");
        }

        foreach (var (theme, variant) in new[] { ("dark", ThemeVariant.Dark), ("light", ThemeVariant.Light) })
        {
            var view = Panel(track, CaptureWidth, InstrumentPanelView.PanelHeight, FretboardScaleRules.MinScale);
            var window = new Window { Width = CaptureWidth, Height = InstrumentPanelView.PanelHeight + 40, RequestedThemeVariant = variant, Content = view };
            DialogTestKit.SavePng(window, nameof(FretboardRealProportionTests), $"dock-{CaptureWidth:0}-{theme}.png");
            window.Close();
        }
    }

    private static InstrumentPanelView Panel(TrackModel track, double width, double height, double minScale)
    {
        var view = new InstrumentPanelView
        {
            Kind = InstrumentPanelKind.Fretboard,
            Width = width,
            Height = height,
            MinDrawingScale = minScale,
        };
        view.Show(track, Array.Empty<InstrumentLitNotes.Lit>());
        return view;
    }

    /// <summary>Lays the panel out in a window and checks its board's width : height against the real aspect of the shown frets and strings.</summary>
    private static void AssertRealAspect(InstrumentPanelView view, double width, double height, string mode)
    {
        var window = new Window { Width = width + 40, Height = height + 40, Content = view };
        window.Show();
        window.UpdateLayout();
        var layout = view.FretboardLayout();
        window.Close();

        var board = layout.Board;
        var shown = layout.LastFret - layout.FirstFret + 1;
        var target = FretboardSizeRules.RealAspect(shown, layout.Strings, view.FretInput.Bass);
        var ratio = board.Width / board.Height;
        Assert.True(Math.Abs(ratio / target - 1) <= AspectTolerance,
            $"{mode} {width:0} x {height:0}: board {ratio:F3} : 1, real {target:F3} : 1 ({layout.Strings} strings, {shown} frets)");
    }
}
