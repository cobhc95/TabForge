using System.Collections.Generic;
using System.Linq;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Services;
using TabForge.Views.Score;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the exact bar timing, the free-slot duration rule and the red bar marking that waits until the cursor leaves the bar.
// Does not own: MusicTime.AnalyzeBar (the existing red meaning) or the cursor positions.
public class BarTimingTests
{
    [Fact]
    public void CapacitiesFollowTheTimeSignature()
    {
        var cases = new (int Numerator, int Denominator, long TicksValue)[]
        {
            (4, 4, 3840), (3, 4, 2880), (2, 2, 3840), (5, 8, 2400), (7, 8, 3360),
            (6, 8, 2880), (9, 8, 4320), (12, 8, 5760), (3, 8, 1440)
        };
        foreach (var (numerator, denominator, ticks) in cases)
            Assert.Equal(new Ticks(ticks, 1), BarTiming.BarCapacityTicks(numerator, denominator));
    }

    [Fact]
    public void TimingRulesCasesOneToThreeFiveSixElevenTwelveAndSixteen()
    {
        Assert.Equal(BarTimingStatus.Complete, BarTiming.Status(Bar((4, 0, 0, 0), (4, 0, 0, 0), (4, 0, 0, 0), (8, 0, 0, 0), (8, 0, 0, 0)), 4, 4));
        Assert.Equal(BarTimingStatus.Complete, BarTiming.Status(Bar((2, 0, 0, 0), (4, 0, 0, 0)), 3, 4));
        Assert.Equal(BarTimingStatus.Complete, BarTiming.Status(Bar((4, 1, 0, 0), (4, 1, 0, 0)), 6, 8));
        Assert.Equal(BarTimingStatus.Complete, BarTiming.Status(Bar((4, 0, 3, 2), (4, 0, 3, 2), (4, 0, 3, 2), (2, 0, 0, 0)), 4, 4));
        Assert.Equal(BarTimingStatus.Complete, BarTiming.Status(Bar((8, 0, 3, 2), (8, 0, 3, 2), (8, 0, 3, 2), (8, 0, 3, 2), (8, 0, 3, 2), (8, 0, 3, 2), (2, 0, 0, 0)), 4, 4));
        Assert.Equal(BarTimingStatus.Complete, BarTiming.Status(Bar((4, 1, 0, 0), (4, 1, 0, 0), (4, 1, 0, 0), (4, 1, 0, 0)), 12, 8));
        Assert.Equal(BarTimingStatus.Complete, BarTiming.Status(Bar((4, 1, 0, 0), (4, 1, 0, 0), (4, 1, 0, 0)), 9, 8));
        Assert.Equal(BarTimingStatus.Complete, BarTiming.Status(Bar((2, 0, 0, 0), (8, 0, 0, 0)), 5, 8));
        Assert.Equal(BarTimingStatus.Overfull, BarTiming.Status(Bar((4, 0, 0, 0), (4, 0, 0, 0), (4, 0, 0, 0), (4, 0, 0, 0), (4, 0, 0, 0)), 4, 4));
    }

    [Fact]
    public void SevenSeptupletSixteenthsSumToExactlyOneQuarter()
    {
        var measure = Bar(Enumerable.Repeat((16, 0, 7, 4), 7).ToArray());

        Assert.All(measure.Cells, cell => Assert.Equal(new Ticks(960, 7), BarTiming.CellTicks(cell)));
        Assert.Equal(new Ticks(960, 1), BarTiming.VoiceUsedTicks(measure, 1));
        Assert.Equal(BarTimingStatus.Underfull, BarTiming.Status(measure, 4, 4));
    }

    [Fact]
    public void StatusJudgesVoiceTwoOnlyWhenItHasTimedCells()
    {
        var complete = Bar((4, 0, 0, 0), (4, 0, 0, 0), (4, 0, 0, 0), (4, 0, 0, 0));
        complete.Voice2Cells = new List<TabCell> { Beat(4, 0, 0, 0), Beat(4, 0, 0, 0) };

        Assert.Equal(BarTimingStatus.Complete, BarTiming.Status(complete, 4, 4));   // a shorter voice 2 has implied rests

        complete.Voice2Cells = Enumerable.Repeat(Beat(4, 0, 0, 0), 5).ToList();
        Assert.Equal(BarTimingStatus.Overfull, BarTiming.Status(complete, 4, 4));

        complete.Voice2Cells = new List<TabCell> { GraceBeat() };
        Assert.Equal(BarTimingStatus.Complete, BarTiming.Status(complete, 4, 4));
    }

    [Fact]
    public void EighthsAndTiedSixteenthsAreCompleteAndARestOnlyVoiceTwoIsEmpty()
    {
        Assert.Equal(BarTimingStatus.Complete, BarTiming.Status(Bar(Enumerable.Repeat((8, 0, 0, 0), 8).ToArray()), 4, 4));

        var tied = Bar(Enumerable.Repeat((16, 0, 0, 0), 14).Append((8, 0, 0, 0)).ToArray());   // 14 sixteenths + an eighth tied across a beat
        tied.Cells[13].IsTied = true;
        Assert.Equal(BarTimingStatus.Complete, BarTiming.Status(tied, 4, 4));

        var full = Bar((4, 0, 0, 0), (4, 0, 0, 0), (4, 0, 0, 0), (4, 0, 0, 0));
        full.Voice2Cells = new List<TabCell> { Rest(4, 0) };
        Assert.Equal(BarTimingStatus.Complete, BarTiming.Status(full, 4, 4));

        Assert.Equal(BarTimingStatus.Underfull, BarTiming.Status(Bar((4, 0, 0, 0), (4, 0, 0, 0)), 4, 4));
    }

    [Fact]
    public void NewSlotDurationTakesThePreviousValueOrClipsToTheRemainingSpace()
    {
        Assert.Equal(new Ticks(960, 1), ScoreBeatNavigation.NewSlotDuration(new Ticks(960, 1), new Ticks(2880, 1))!.Value);
        Assert.Equal(new Ticks(960, 1), ScoreBeatNavigation.NewSlotDuration(new Ticks(1920, 1), new Ticks(960, 1))!.Value);
        Assert.Equal(new Ticks(720, 1), ScoreBeatNavigation.NewSlotDuration(new Ticks(1920, 1), new Ticks(720, 1))!.Value);
        Assert.Equal(new Ticks(960, 1), ScoreBeatNavigation.NewSlotDuration(new Ticks(1920, 1), new Ticks(1200, 1))!.Value);
        Assert.Null(ScoreBeatNavigation.NewSlotDuration(new Ticks(960, 1), Ticks.Zero));
        Assert.Equal(new DurationChoice(4, 0), ScoreBeatNavigation.WrittenValue(new Ticks(960, 1))!.Value);
        Assert.Null(ScoreBeatNavigation.WrittenValue(new Ticks(640, 1)));   // a triplet quarter has no plain/dotted choice
    }

    [AvaloniaFact]
    public void TheEditingBarIsExemptFromRedUntilTheCursorLeaves()
    {
        var project = new SongProject { TimeSignatureNumerator = 4, TimeSignatureDenominator = 4 };
        var track = new TrackModel();
        for (var bar = 0; bar < 2; bar++)
        {
            var measure = new MeasureModel { Number = bar + 1 };
            measure.Cells[0].DurationDenominator = 2;
            measure.Cells[0].Notes.Add(new TabNote { StringIndex = 0, Fret = 5 });
            track.Measures.Add(measure);
        }
        project.Tracks.Add(track);
        var probe = new ScoreGeometryProbe(project, new AvaloniaDrawContext()) { Notation = NotationMode.TabOnly, Cursor = (0, 0, 0) };
        var error = probe.Appearance.ErrorColor;

        var editing = new TextRecorder();
        probe.DrawPage(editing, int.MaxValue, withPlayhead: false);

        Assert.Contains(editing.Texts, text => text.Text == "1" && text.Colour != error);
        Assert.Contains(editing.Texts, text => text.Text == "2" && text.Colour == error);

        probe.Cursor = null;
        var left = new TextRecorder();
        probe.DrawPage(left, int.MaxValue, withPlayhead: false);

        Assert.Contains(left.Texts, text => text.Text == "1" && text.Colour == error);
    }

    [Fact]
    public void SpecA_FourSixteenthsInFourFourIsUnderfull()
        => Assert.Equal(BarTimingStatus.Underfull, BarTiming.Status(Bar((16, 0, 0, 0), (16, 0, 0, 0), (16, 0, 0, 0), (16, 0, 0, 0)), 4, 4));

    [Fact]
    public void SpecB_FourSixteenthsPlusQuarterPlusHalfIsComplete()
    {
        var bar = new MeasureModel
        {
            Cells = new List<TabCell>
            {
                Beat(16, 0, 0, 0), Beat(16, 0, 0, 0), Beat(16, 0, 0, 0), Beat(16, 0, 0, 0),
                Rest(4, 0), Rest(2, 0)
            }
        };
        Assert.Equal(BarTimingStatus.Complete, BarTiming.Status(bar, 4, 4));
    }

    [Fact]
    public void SpecC_FourQuartersInThreeFourIsOverfull()
        => Assert.Equal(BarTimingStatus.Overfull, BarTiming.Status(Bar((4, 0, 0, 0), (4, 0, 0, 0), (4, 0, 0, 0), (4, 0, 0, 0)), 3, 4));

    [Fact]
    public void SpecD_TwoDottedQuartersInSixEightAreComplete()
        => Assert.Equal(BarTimingStatus.Complete, BarTiming.Status(Bar((4, 1, 0, 0), (4, 1, 0, 0)), 6, 8));

    [Fact]
    public void SpecE_TripletEighthsPlusThreeQuartersIsComplete()
        => Assert.Equal(BarTimingStatus.Complete, BarTiming.Status(Bar((8, 0, 3, 2), (8, 0, 3, 2), (8, 0, 3, 2), (4, 0, 0, 0), (4, 0, 0, 0), (4, 0, 0, 0)), 4, 4));

    [Fact]
    public void SpecF_QuarterQuarterDottedQuarterInSevenEightIsComplete()
        => Assert.Equal(BarTimingStatus.Complete, BarTiming.Status(Bar((4, 0, 0, 0), (4, 0, 0, 0), (4, 1, 0, 0)), 7, 8));

    [Fact]
    public void SpecG_EmptyBarAndWholeBarRestAreComplete()
    {
        Assert.Equal(BarTimingStatus.Complete, BarTiming.Status(new MeasureModel(), 4, 4));
        var whole = new MeasureModel();
        whole.Cells[0].IsRest = true;
        whole.Cells[0].DurationDenominator = 1;
        Assert.Equal(BarTimingStatus.Complete, BarTiming.Status(whole, 4, 4));
        Assert.Equal(BarTimingStatus.Complete, BarTiming.Status(whole, 3, 4));
    }

    [Fact]
    public void LoneRestOfAnyLengthIsAnEmptyBarButTwoShortRestsAreUnderfull()
    {
        Assert.Equal(BarTimingStatus.Complete, BarTiming.Status(new MeasureModel { Cells = new List<TabCell> { Rest(4, 0) } }, 4, 4));
        Assert.Equal(BarTimingStatus.Complete, BarTiming.Status(new MeasureModel { Cells = new List<TabCell> { Rest(16, 0) } }, 4, 4));
        Assert.Equal(BarTimingStatus.Underfull, BarTiming.Status(new MeasureModel { Cells = new List<TabCell> { Rest(4, 0), Rest(4, 0) } }, 4, 4));
        Assert.Equal(BarTimingStatus.Underfull, BarTiming.Status(Bar((2, 0, 0, 0)), 4, 4));   // a lone note stays incomplete (R2)
    }

    [Fact]
    public void AngelsDontKillBassBar83EightEighthsInTwoFourIsGenuinelyOverfull()
    {
        // children_of_bodom/angels_don_t_kill.gp4, bass track, bar 83: the file's master bar is 2/4 yet the bass holds the 4/4 bar 82 pattern
        // (8 eighths = 3840 ticks); every other track and the other copies of the song have 1920 there. A source defect, so it stays red.
        var bar = Bar(Enumerable.Repeat((8, 0, 0, 0), 8).ToArray());
        Assert.Equal(new Ticks(3840, 1), BarTiming.VoiceUsedTicks(bar, 1));
        Assert.Equal(BarTimingStatus.Overfull, BarTiming.Status(bar, 2, 4));
        Assert.Equal(BarTimingStatus.Complete, BarTiming.Status(bar, 4, 4));
    }

    [Fact]
    public void SpecH_AnacrusisFreeTimeAndSimileAreExempt()
    {
        var shortBar = Bar((4, 0, 0, 0));
        Assert.Equal(BarTimingStatus.Underfull, BarTiming.Status(shortBar, 4, 4));
        var pickup = Bar((4, 0, 0, 0));
        pickup.Anacrusis = true;
        Assert.Equal(BarTimingStatus.Complete, BarTiming.Status(pickup, 4, 4));
        var free = Bar((4, 0, 0, 0));
        free.FreeTime = true;
        Assert.Equal(BarTimingStatus.Complete, BarTiming.Status(free, 4, 4));
        var simile = Bar((4, 0, 0, 0));
        simile.SimileOneBar = true;
        Assert.Equal(BarTimingStatus.Complete, BarTiming.Status(simile, 4, 4));
    }

    [Fact]
    public void SpecI_VoiceTwoShortIsCompleteWithImpliedRests()
    {
        var bar = Bar((4, 0, 0, 0), (4, 0, 0, 0), (4, 0, 0, 0), (4, 0, 0, 0));
        bar.Voice2Cells = new List<TabCell> { Beat(4, 0, 0, 0), Beat(4, 0, 0, 0) };
        Assert.Equal(BarTimingStatus.Complete, BarTiming.Status(bar, 4, 4));
    }

    [Fact]
    public void SpecJ_SingleQuarterInFourFourIsUnderfull()
        => Assert.Equal(BarTimingStatus.Underfull, BarTiming.Status(Bar((4, 0, 0, 0)), 4, 4));

    [AvaloniaFact]
    public void UnderfullBarDrawsItsStaffSegmentWithTheErrorColour()
    {
        var project = new SongProject { TimeSignatureNumerator = 4, TimeSignatureDenominator = 4 };
        var track = new TrackModel();
        var underfull = new MeasureModel { Number = 1 };
        underfull.Cells[0].DurationDenominator = 4;
        underfull.Cells[0].Notes.Add(new TabNote { StringIndex = 0, Fret = 5 });
        var full = new MeasureModel { Number = 2 };
        foreach (var cell in new[] { 0, 4, 8, 12 })
        {
            full.Cells[cell].DurationDenominator = 4;
            full.Cells[cell].Notes.Add(new TabNote { StringIndex = 0, Fret = 5 });
        }
        track.Measures.Add(underfull);
        track.Measures.Add(full);
        project.Tracks.Add(track);
        var probe = new ScoreGeometryProbe(project, new AvaloniaDrawContext()) { Cursor = null };
        var error = probe.Appearance.ErrorColor;

        var red = new LineRecorder();
        probe.DrawPage(red, int.MaxValue, withPlayhead: false);
        Assert.Contains(red.Lines, line => line.Colour == error);

        probe.Cursor = (0, 0, 0);
        var editing = new LineRecorder();
        probe.DrawPage(editing, int.MaxValue, withPlayhead: false);
        Assert.DoesNotContain(editing.Lines, line => line.Colour == error);
    }

    private static TabCell Beat(int denominator, int dots, int tupletNumerator, int tupletDenominator)
    {
        var cell = new TabCell
        {
            DurationDenominator = denominator,
            Dots = dots,
            TupletNumerator = tupletNumerator,
            TupletDenominator = tupletDenominator
        };
        cell.Notes.Add(new TabNote { StringIndex = 0, Fret = 1 });
        return cell;
    }

    private static TabCell GraceBeat()
    {
        var cell = new TabCell { IsGrace = true, DurationDenominator = 8 };
        cell.Notes.Add(new TabNote { StringIndex = 0, Fret = 1, IsGraceNote = true });
        return cell;
    }

    private static TabCell Rest(int denominator, int dots)
        => new() { IsRest = true, DurationDenominator = denominator, Dots = dots };

    private static MeasureModel Bar(params (int Denominator, int Dots, int TupletNumerator, int TupletDenominator)[] beats)
        => new() { Cells = beats.Select(beat => Beat(beat.Denominator, beat.Dots, beat.TupletNumerator, beat.TupletDenominator)).ToList() };

    /// <summary>Records each laid-out text with its colour; shapes are ignored.</summary>
    private sealed class TextRecorder : IDrawContext
    {
        public List<(string Text, UiColor Colour)> Texts { get; } = new();
        public double PixelsPerDip => 1;
        public IPathFactory Paths { get; } = new AvaloniaPathsAndText();

        public UiText Text(string text, string fontFamily, double size, UiColor colour, double pixelsPerDip, double maxWidth = 0, UiTextAlign align = UiTextAlign.Left,
            int weight = 400, bool italic = false, UiStroke? outline = null, bool idealLayout = false)
        {
            Texts.Add((text, colour));
            return new(text.Length * 6, 12, 10, null!);
        }

        public void DrawText(in UiText text, double x, double y) { }
        public void Rect(UiColor? fill, UiStroke? stroke, double x, double y, double width, double height, double radius = 0) { }
        public void Rect(IUiPaint? fill, IUiPen? pen, double x, double y, double width, double height, double radiusX = 0, double radiusY = 0) { }
        public void Line(UiStroke stroke, double x1, double y1, double x2, double y2) { }
        public void Line(IUiPen pen, double x1, double y1, double x2, double y2) { }
        public void Ellipse(UiColor? fill, UiStroke? stroke, double cx, double cy, double rx, double ry) { }
        public void Ellipse(IUiPaint? fill, IUiPen? pen, double cx, double cy, double rx, double ry) { }
        public void Path(UiColor? fill, UiStroke? stroke, IUiPath path) { }
        public void Path(IUiPaint? fill, IUiPen? pen, IUiPath path) { }
        public void Image(IUiImage image, double x, double y, double width, double height) { }
        public void PushClip(double x, double y, double width, double height) { }
        public void PushClip(IUiPath clip) { }
        public void PushTranslate(double dx, double dy) { }
        public void PushScale(double sx, double sy, double centreX = 0, double centreY = 0) { }
        public void PushRotate(double degrees, double centreX = 0, double centreY = 0) { }
        public void PushTransform(IUiTransform transform) { }
        public void PushOpacity(double opacity) { }
        public void Drawing(IUiDrawing drawing) { }
        public void Pop() { }
    }

    /// <summary>Records every line stroke with its colour; text and fills are ignored.</summary>
    private sealed class LineRecorder : IDrawContext
    {
        public List<(UiColor Colour, double X1, double Y1, double X2, double Y2)> Lines { get; } = new();
        public double PixelsPerDip => 1;
        public IPathFactory Paths { get; } = new AvaloniaPathsAndText();

        public UiText Text(string text, string fontFamily, double size, UiColor colour, double pixelsPerDip, double maxWidth = 0, UiTextAlign align = UiTextAlign.Left,
            int weight = 400, bool italic = false, UiStroke? outline = null, bool idealLayout = false)
            => new(text.Length * 6, 12, 10, null!);

        public void DrawText(in UiText text, double x, double y) { }
        public void Rect(UiColor? fill, UiStroke? stroke, double x, double y, double width, double height, double radius = 0) { }
        public void Rect(IUiPaint? fill, IUiPen? pen, double x, double y, double width, double height, double radiusX = 0, double radiusY = 0) { }
        public void Line(UiStroke stroke, double x1, double y1, double x2, double y2) => Lines.Add((stroke.Colour, x1, y1, x2, y2));
        public void Line(IUiPen pen, double x1, double y1, double x2, double y2) { }
        public void Ellipse(UiColor? fill, UiStroke? stroke, double cx, double cy, double rx, double ry) { }
        public void Ellipse(IUiPaint? fill, IUiPen? pen, double cx, double cy, double rx, double ry) { }
        public void Path(UiColor? fill, UiStroke? stroke, IUiPath path) { }
        public void Path(IUiPaint? fill, IUiPen? pen, IUiPath path) { }
        public void Image(IUiImage image, double x, double y, double width, double height) { }
        public void PushClip(double x, double y, double width, double height) { }
        public void PushClip(IUiPath clip) { }
        public void PushTranslate(double dx, double dy) { }
        public void PushScale(double sx, double sy, double centreX = 0, double centreY = 0) { }
        public void PushRotate(double degrees, double centreX, double centreY) { }
        public void PushTransform(IUiTransform transform) { }
        public void PushOpacity(double opacity) { }
        public void Drawing(IUiDrawing drawing) { }
        public void Pop() { }
    }
}
