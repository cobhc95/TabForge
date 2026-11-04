using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Presenters;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the capo checks for the fretboard: a written fret is drawn at its string's capo plus the fret (never behind the capo), an open string sits
/// at the capo, a click behind a capo picks nothing and a click past it picks the written fret counted from the capo, partial capos do this on
/// their strings only, and the board behind a capo is dimmed.
/// Tests: FretboardGeometryRules (PositionOfTab, HitTest, PackCapos), InstrumentPanelView.
/// </summary>
public class FretboardCapoPositionTests
{
    private static readonly UiRect Content = new(0, 0, 1600, 300);

    private static FretboardGeometryRules.Input InputWith(long capos) => new(6, 24, 24, 1, false, 0, false, capos);

    private static FretboardGeometryRules.Layout LayoutOf(FretboardGeometryRules.Input input) => FretboardGeometryRules.Compute(input, Content, limits: InstrumentPanelView.BoardLimits);

    [Fact]
    public void PackedCaposReadBackPerStringAndPartialsRaiseOnlyTheirStrings()
    {
        var partial = new List<PartialCapo> { new() { Fret = 7, StringMask = 0b000111 } };
        var layout = LayoutOf(InputWith(FretboardGeometryRules.PackCapos(2, partial, 6)));
        Assert.Equal(new[] { 7, 7, 7, 2, 2, 2 }, Enumerable.Range(0, 6).Select(layout.CapoOf).ToArray());
        Assert.Equal(0, LayoutOf(InputWith(0)).CapoOf(3));
        Assert.Equal(0, layout.CapoOf(99));
    }

    [Fact]
    public void AWrittenFretSitsAtTheCapoPlusItAndAnOpenStringAtTheCapo()
    {
        var plain = LayoutOf(InputWith(0));
        var layout = LayoutOf(InputWith(FretboardGeometryRules.PackCapos(5, null, 6)));
        for (var s = 0; s < 6; s++)
        {
            Assert.Equal(FretboardGeometryRules.FretX(layout, 6), FretboardGeometryRules.PositionOfTab(layout, s, 1).X, 6);
            Assert.Equal(FretboardGeometryRules.FretX(layout, 9), FretboardGeometryRules.PositionOfTab(layout, s, 4).X, 6);
            Assert.Equal(FretboardGeometryRules.FretX(layout, 5), FretboardGeometryRules.PositionOfTab(layout, s, 0).X, 6);
            Assert.Equal(FretboardGeometryRules.StringY(layout, s), FretboardGeometryRules.PositionOfTab(layout, s, 1).Y, 6);
            Assert.Equal(FretboardGeometryRules.PositionOf(plain, s, 3), FretboardGeometryRules.PositionOfTab(plain, s, 3));
        }
    }

    [Fact]
    public void ClicksBehindACapoPickNothingAndClicksPastItCountFromTheCapo()
    {
        var input = InputWith(FretboardGeometryRules.PackCapos(5, null, 6));
        var layout = LayoutOf(input);
        var y = FretboardGeometryRules.StringY(layout, 2);
        bool Hit(double x, out int fret) => FretboardGeometryRules.HitTest(input, Content, new UiPoint(x, y), out _, out fret, limits: InstrumentPanelView.BoardLimits);

        Assert.False(Hit(FretboardGeometryRules.FretX(layout, 3), out _));
        Assert.False(Hit(layout.Board.X - 8, out _));
        Assert.True(Hit(FretboardGeometryRules.FretX(layout, 5), out var open));
        Assert.Equal(0, open);
        Assert.True(Hit(FretboardGeometryRules.FretX(layout, 8), out var third));
        Assert.Equal(3, third);
        // Without a capo the same click is the physical fret, and the strip left of the nut is the open string.
        var plain = LayoutOf(InputWith(0));
        Assert.True(FretboardGeometryRules.HitTest(InputWith(0), Content, new UiPoint(FretboardGeometryRules.FretX(plain, 3), FretboardGeometryRules.StringY(plain, 2)), out _, out var same, limits: InstrumentPanelView.BoardLimits));
        Assert.Equal(3, same);
        Assert.True(FretboardGeometryRules.HitTest(InputWith(0), Content, new UiPoint(plain.Board.X - 8, FretboardGeometryRules.StringY(plain, 2)), out _, out var openPlain, limits: InstrumentPanelView.BoardLimits));
        Assert.Equal(0, openPlain);
    }

    [Fact]
    public void APartialCapoHoldsBackOnlyItsStrings()
    {
        var partial = new List<PartialCapo> { new() { Fret = 7, StringMask = 0b000111 } };
        var input = InputWith(FretboardGeometryRules.PackCapos(0, partial, 6));
        var layout = LayoutOf(input);
        bool Hit(int stringIndex, int physicalFret) => FretboardGeometryRules.HitTest(input, Content,
            new UiPoint(FretboardGeometryRules.FretX(layout, physicalFret), FretboardGeometryRules.StringY(layout, stringIndex)), out _, out _, limits: InstrumentPanelView.BoardLimits);
        Assert.False(Hit(0, 4));
        Assert.True(Hit(4, 4));
        Assert.True(Hit(0, 9));
    }

    private static InstrumentFretNumberTests.RecordingDrawContext Draw(int capo)
    {
        var track = DemoSongFactory.Create().Tracks[0];
        track.Capo = capo;
        var view = new InstrumentPanelView();
        view.Show(track, System.Array.Empty<InstrumentLitNotes.Lit>());
        view.Measure(new Size(1100, 170));
        view.Arrange(new Rect(0, 0, 1100, 170));
        var draw = new InstrumentFretNumberTests.RecordingDrawContext();
        view.Draw(draw, light: false);
        return draw;
    }

    [AvaloniaFact]
    public void TheBoardBehindACapoIsDimmedAndAFreeBoardIsNot()
    {
        var free = Draw(0);
        var capoed = Draw(5);
        // One dimming band per capo'd string (the demo guitar has six), none without a capo.
        Assert.Equal(free.Rects.Count + 6, capoed.Rects.Count);
    }

    [Xunit.Fact]
    public void AMarkerNamesItsRealFretOnTheNeckNotItsWrittenFret()
    {
        var layout = LayoutOf(InputWith(FretboardGeometryRules.PackCapos(5, null, 6)));
        var draw = new InstrumentFretNumberTests.RecordingDrawContext();
        var fill = new UiColor(255, 0, 128, 0);
        InstrumentPanelView.MarkFret(draw, _ => new UiColor(255, 0, 0, 0), layout, 2, 3, fill, 11);
        Assert.Contains(draw.Texts, t => t.Text == "8");
        // Without a capo the written fret is the real fret.
        var plain = LayoutOf(InputWith(0));
        var plainDraw = new InstrumentFretNumberTests.RecordingDrawContext();
        InstrumentPanelView.MarkFret(plainDraw, _ => new UiColor(255, 0, 0, 0), plain, 2, 3, fill, 11);
        Assert.Contains(plainDraw.Texts, t => t.Text == "3");
    }

    [AvaloniaFact]
    public void ALitNoteWithACapoShowsItsRealFret()
    {
        var track = DemoSongFactory.Create().Tracks[0];
        track.Capo = 2;
        var view = new InstrumentPanelView();
        view.Show(track, new[] { new InstrumentLitNotes.Lit(0, 5, 0) });
        view.Measure(new Size(1100, 170));
        view.Arrange(new Rect(0, 0, 1100, 170));
        var draw = new InstrumentFretNumberTests.RecordingDrawContext();
        view.Draw(draw, light: false);
        Assert.Contains(draw.Texts, t => t.Text == "7");
    }

    [Xunit.Fact]
    public void CapoLandingBadgeDraws()
    {
        var draw = new InstrumentFretNumberTests.RecordingDrawContext();
        var bar = new UiRect(100, 40, 20, 112);
        var accent = new UiColor(255, 232, 163, 61);
        InstrumentPanelView.DrawLandingBadge(draw, _ => new UiColor(255, 0, 0, 0), accent, bar, 6, 1.0);
        Assert.Contains(draw.Texts, t => t.Text == "6");
    }
}
