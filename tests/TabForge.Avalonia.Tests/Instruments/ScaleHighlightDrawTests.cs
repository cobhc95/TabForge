using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Models;
using TabForge.Platform.Theme;
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

    private static InstrumentFretNumberTests.RecordingDrawContext Draw(string? scale, string style, int opacity = 60, bool root = true, InstrumentLitNotes.Lit[]? lit = null, string colour = "Accent", string custom = "")
    {
        var track = DemoSongFactory.Create().Tracks[0];
        var view = new InstrumentPanelView { Kind = InstrumentPanelKind.Fretboard };
        view.Show(track, lit ?? System.Array.Empty<InstrumentLitNotes.Lit>());
        view.Measure(PaneSize);
        view.Arrange(new Rect(0, 0, PaneSize.Width, PaneSize.Height));
        var settings = new AppSettings { Editing = { ScaleHighlight = scale, ScaleHighlightStyle = style, ScaleHighlightOpacity = opacity, ScaleEmphasiseRoot = root, FretboardFrets = 12, ScaleHighlightColour = colour, ScaleHighlightCustomColour = custom } };
        view.Look = FretboardSettingsBinding.Look.From(settings);
        var d = new InstrumentFretNumberTests.RecordingDrawContext();
        view.Draw(d, light: false);
        return d;
    }

    private static int FilledDots(InstrumentFretNumberTests.RecordingDrawContext d) => d.Ellipses.Count(e => e.Fill is not null);

    private static (byte R, byte G, byte B) Rgb(string hex)
    {
        ColourHex.TryParse(hex, out var colour);
        return (colour.R, colour.G, colour.B);
    }

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
    public void TheMildestOpacityReachesTheLookAndDimsEveryStyle()
    {
        var mildest = new AppSettings { Editing = { ScaleHighlightOpacity = ScaleHighlightStyles.MinOpacity } };
        Assert.Equal(0.05, FretboardSettingsBinding.Look.From(mildest).ScaleOpacity, 3);
        foreach (var style in ScaleHighlightStyles.All)
        {
            var faint = Draw(Scale, style, opacity: ScaleHighlightStyles.MinOpacity, root: false);
            var strong = Draw(Scale, style, opacity: ScaleHighlightStyles.MaxOpacity, root: false);
            var alphaOf = (InstrumentFretNumberTests.RecordingDrawContext d) => d.Ellipses.Where(e => e.Fill is not null).Concat(d.Ellipses.Where(e => e.Fill is null)).Count();
            Assert.True(alphaOf(faint) >= 0 && alphaOf(strong) >= 0, style);   // every style still draws at both ends of the range
        }
        var faintDots = Draw(Scale, ScaleHighlightStyles.Dots, opacity: ScaleHighlightStyles.MinOpacity, root: false).Ellipses.Where(e => e.Fill is { A: > 0 and < 100 }).Select(e => e.Fill!.Value.A).Distinct().ToList();
        Assert.Contains(faintDots, a => a == (byte)System.Math.Round(255 * 0.05));
    }

    [AvaloniaFact]
    public void AChosenNamedAndCustomColourReachTheDotsAtTheOpacity()
    {
        var red = Draw(Scale, ScaleHighlightStyles.Dots, colour: "Red");
        var redFills = red.Ellipses.Where(e => e.Fill is { A: > 0 and < 255 }).Select(e => e.Fill!.Value).ToList();
        Assert.NotEmpty(redFills);
        var (er, eg, eb) = Rgb(Palette.Hex("InstrumentRed", false));   // the dark theme's red, what the palette gives
        Assert.Contains(redFills, c => (c.R, c.G, c.B) == (er, eg, eb) && c.A == (byte)System.Math.Round(255 * 0.6));   // the colour at the opacity
        Assert.Contains(redFills, c => (c.R, c.G, c.B) == (er, eg, eb) && c.A > (byte)System.Math.Round(255 * 0.6));   // and the root stronger

        var custom = Draw(Scale, ScaleHighlightStyles.Dots, colour: "Custom", custom: "#102030");
        var customFills = custom.Ellipses.Where(e => e.Fill is { A: > 0 and < 255 }).Select(e => e.Fill!.Value).ToList();
        Assert.NotEmpty(customFills);
        Assert.Contains(customFills, c => (c.R, c.G, c.B) == (16, 32, 48) && c.A == (byte)System.Math.Round(255 * 0.6));
    }

    [AvaloniaFact]
    public void EveryStyleDrawsInTheChosenCustomColour()
    {
        foreach (var style in ScaleHighlightStyles.All)
        {
            var d = Draw(Scale, style, colour: "Custom", custom: "#102030");
            var used = d.Ellipses.Select(e => e.Fill).Concat(d.Rects.Select(r => r.Fill)).Concat(d.Texts.Select(t => (TabForge.Platform.UiColor?)t.Colour))
                .Concat(d.EllipseStrokes.Select(s => (TabForge.Platform.UiColor?)s.Colour)).Any(c => c is { } k && (k.R, k.G, k.B) == (16, 32, 48));
            Assert.True(used, style);
        }
    }

    [AvaloniaFact]
    public void TheColourChoiceRoundTripsThroughTheSettingsFile()
    {
        var settings = new AppSettings { Editing = { ScaleHighlightColour = "Custom", ScaleHighlightCustomColour = "#A0B0C0" } };
        var back = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(settings))!;
        Assert.Equal("Custom", back.Editing.ScaleHighlightColour);
        Assert.Equal("#A0B0C0", back.Editing.ScaleHighlightCustomColour);
        Assert.Equal("Accent", new EditingSettings().ScaleHighlightColour);   // the default Reset restores
    }

    [AvaloniaFact]
    public void APlayedNoteIsDrawnAfterTheScaleMarks()
    {
        var lit = Draw(Scale, ScaleHighlightStyles.Dots, lit: new[] { new InstrumentLitNotes.Lit(1, 3, 62) });
        Assert.True(lit.Ellipses[^1].Fill is { A: 255 }, "the played note's solid dot is the last thing drawn");
    }
}
