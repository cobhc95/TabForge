using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the five scale highlight styles on a 6 string, 12 fret fretboard with D natural minor: the draw calls each style makes (dots filled, rings outlined, cell tint rectangles,
//   note names, degrees), the stronger root, the opacity, and a played note drawn after (on top of) the scale marks.
// Does not own: the settings (ScaleSearchTests) or the popup (FretboardScalePopupTests).
// Tests: this class.
public sealed class ScaleHighlightDrawTests
{
    private const string Scale = "D Natural Minor";
    private static readonly Size PaneSize = new(1000, 170);

    private static InstrumentFretNumberTests.RecordingDrawContext Draw(string? scale, string style, int opacity = 60, bool root = true, InstrumentLitNotes.Lit[]? lit = null)
    {
        var track = DemoSongFactory.Create().Tracks[0];
        var view = new InstrumentPanelView { Kind = InstrumentPanelKind.Fretboard };
        view.Show(track, lit ?? System.Array.Empty<InstrumentLitNotes.Lit>());
        view.Measure(PaneSize);
        view.Arrange(new Rect(0, 0, PaneSize.Width, PaneSize.Height));
        var settings = new AppSettings { Editing = { ScaleHighlight = scale, ScaleHighlightStyle = style, ScaleHighlightOpacity = opacity, ScaleEmphasiseRoot = root, FretboardFrets = 12 } };
        view.Look = FretboardSettingsBinding.Look.From(settings);
        var d = new InstrumentFretNumberTests.RecordingDrawContext();
        view.Draw(d, light: false);
        return d;
    }

    private static int FilledDots(InstrumentFretNumberTests.RecordingDrawContext d) => d.Ellipses.Count(e => e.Fill is not null);

    [AvaloniaFact]
    public void DotsDrawOneSoftFilledDotPerScaleNoteAndRingsOutlineInstead()
    {
        var none = Draw(null, ScaleHighlightStyles.Dots);
        var dots = Draw(Scale, ScaleHighlightStyles.Dots);
        var added = FilledDots(dots) - FilledDots(none);
        Assert.True(added > 20, "D minor lights many frets of six strings");
        var rings = Draw(Scale, ScaleHighlightStyles.Rings);
        Assert.Equal(FilledDots(none), FilledDots(rings));
        Assert.Equal(added, rings.Ellipses.Count - none.Ellipses.Count);
        Assert.Equal(none.Rects.Count, dots.Rects.Count);
    }

    [AvaloniaFact]
    public void CellTintDrawsRectanglesNamesAndDegreesDrawLetters()
    {
        var none = Draw(null, ScaleHighlightStyles.Dots);
        var tint = Draw(Scale, ScaleHighlightStyles.CellTint);
        Assert.True(tint.Rects.Count(r => r.Fill is not null) - none.Rects.Count(r => r.Fill is not null) > 20);
        Assert.Equal(none.Ellipses.Count, tint.Ellipses.Count);
        var names = Draw(Scale, ScaleHighlightStyles.NoteNames);
        var letters = names.Texts.Count - none.Texts.Count;
        Assert.True(letters > 20);
        Assert.Contains(names.Texts, t => t.Text == "D");
        Assert.DoesNotContain(names.Texts, t => t.Text == "C#");
        var degrees = Draw(Scale, ScaleHighlightStyles.Degrees);
        Assert.Equal(letters, degrees.Texts.Count - none.Texts.Count);
        Assert.Contains(degrees.Texts, t => t.Text == "1");
        Assert.Contains(degrees.Texts, t => t.Text == "7");
    }

    [AvaloniaFact]
    public void TheRootIsStrongerOnlyWhenEmphasisedAndOpacityIsApplied()
    {
        var sixty = (byte)System.Math.Round(255 * 0.6);
        var alphas = Draw(Scale, ScaleHighlightStyles.Dots).Ellipses.Where(e => e.Fill is { A: > 0 and < 255 }).Select(e => e.Fill!.Value.A).Distinct().OrderBy(a => a).ToList();
        Assert.Contains(sixty, alphas);
        Assert.True(alphas.Max() > sixty, "the root is a little stronger");
        var flat = Draw(Scale, ScaleHighlightStyles.Dots, root: false).Ellipses.Where(e => e.Fill is { A: > 0 and < 255 }).Select(e => e.Fill!.Value.A).Distinct().ToList();
        Assert.DoesNotContain(flat, a => a > sixty);
        var faint = Draw(Scale, ScaleHighlightStyles.Dots, opacity: 20, root: false).Ellipses.Where(e => e.Fill is { A: > 0 and < 100 }).Select(e => e.Fill!.Value.A).Distinct().ToList();
        Assert.Contains(faint, a => a == (byte)System.Math.Round(255 * 0.2));
    }

    [AvaloniaFact]
    public void APlayedNoteIsDrawnAfterTheScaleMarks()
    {
        var lit = Draw(Scale, ScaleHighlightStyles.Dots, lit: new[] { new InstrumentLitNotes.Lit(1, 3, 62) });
        Assert.True(lit.Ellipses[^1].Fill is { A: 255 }, "the played note's solid dot is the last thing drawn");
    }
}
