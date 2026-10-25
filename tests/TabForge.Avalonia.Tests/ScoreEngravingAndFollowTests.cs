using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Platform.Testing;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Views.Score;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the proof that empty bars are engraved with a whole-bar rest in the staff and the tab, that the page, continuous and one-line views size
/// the score the way the WPF views do, and that playback follow scrolls vertically and horizontally (Jump and Smooth move, Off does not).
/// Does not own: the follow maths (FollowSettingsTests) or the rest drawing itself (Core ScoreRenderer).
/// Tests: ScoreRenderer.DrawnCells, ScoreGeometryProbe.ContentSize, ScorePreviewView.FollowPlayhead.
/// </summary>
public class ScoreEngravingAndFollowTests
{
    private const string WholeRestGlyph = "𝄻";
    private const int SilentLeadingBars = 4;   // the demo song's first track is silent in bars 1 to 9
    private const int FollowedBarIndex = 39;   // bar 40
    private const int SmoothFrames = 120;

    private static List<DrawRecord> DrawFirstBars(NotationMode notation, bool restsOn)
    {
        var draw = new AvaloniaDrawContext();
        var probe = new ScoreGeometryProbe(FullDemoSongFactory.Create(), draw) { Notation = notation };
        probe.Appearance.DrawEmptyBarRests = restsOn;
        using var bitmap = new RenderTargetBitmap(new PixelSize(1280, 800), new Vector(96, 96));
        using var dc = bitmap.CreateDrawingContext();
        return probe.Draw(draw.Use(dc), SilentLeadingBars);
    }

    // The staff draws a whole-bar rest as the thick bar hanging from the fourth line (11 wide, half a staff gap tall); the tab as the rest glyph.
    private const double StaffBarRestWidth = 11, StaffBarRestHeight = 4.5;

    private static int WholeRests(List<DrawRecord> records) => records.Count(r => (r.Kind == "Text" && r.Text == WholeRestGlyph)
        || (r.Kind == "Rect" && r.Values.Length >= 4 && Math.Abs(r.Values[2] - StaffBarRestWidth) < 0.01 && Math.Abs(r.Values[3] - StaffBarRestHeight) < 0.01));

    [AvaloniaTheory]
    [InlineData(NotationMode.TabAndStaff)]
    [InlineData(NotationMode.TabOnly)]
    public void EmptyBarsDrawAWholeBarRestInTheStaffAndTheTab(NotationMode notation)
    {
        Assert.Equal(0, WholeRests(DrawFirstBars(notation, restsOn: false)));
        Assert.True(WholeRests(DrawFirstBars(notation, restsOn: true)) >= SilentLeadingBars, "Every silent bar needs its whole-bar rest.");
    }

    [AvaloniaFact]
    public void ViewModesSizeThePageLikeTheWpfViews()
    {
        var probe = new ScoreGeometryProbe(FullDemoSongFactory.Create(), new AvaloniaDrawContext());
        var (pageWidth, pageHeight) = probe.ContentSize();
        Assert.Equal(ScoreGeometryProbe.PageWidth, pageWidth);

        probe.PageWidthOverride = 700;   // continuous: the page reflows to the viewer
        var (continuousWidth, continuousHeight) = probe.ContentSize();
        Assert.Equal(700, continuousWidth);
        Assert.True(continuousHeight > pageHeight, "A narrower page wraps into more systems.");

        probe.PageWidthOverride = 0;
        probe.HorizontalScroll = true;   // one line
        var (lineWidth, lineHeight) = probe.ContentSize();
        Assert.True(lineWidth > ScoreGeometryProbe.PageWidth, "The one-line page is as wide as the song.");
        Assert.True(lineHeight < pageHeight, "One line is one system tall.");
    }

    private static (Window Window, ScrollViewer Viewer, ScorePreviewView Score, AvaloniaPlaybackSession Session, AvaloniaPlaybackSessions Sessions) PlayingAtBar40(bool horizontal, string mode, bool followPlayback = true)
    {
        var sessions = new AvaloniaPlaybackSessions(new AvaloniaUiThread(Dispatcher.UIThread), () => new PlayingStateStub(FollowedBarIndex, 0.5));
        var document = new SongDocument(() => null) { Project = FullDemoSongFactory.Create() };
        var score = new ScorePreviewView { Follow = new FollowSettings { Mode = mode, FollowPlayback = followPlayback } };
        score.Show(document);
        score.Probe.HorizontalScroll = horizontal;
        var viewer = new ScrollViewer { Content = score, Width = 900, Height = 600, HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        var window = new Window { Content = viewer, Width = 900, Height = 600 };
        window.Show();
        score.Refresh();
        Dispatcher.UIThread.RunJobs();
        var session = sessions.For(document)!;
        score.BindPlayback(session);
        return (window, viewer, score, session, sessions);
    }

    [AvaloniaTheory]
    [InlineData(false, FollowModes.Jump)]
    [InlineData(false, FollowModes.Smooth)]
    [InlineData(true, FollowModes.Jump)]
    [InlineData(true, FollowModes.Smooth)]
    public void PlaybackIntoBar40ScrollsTheScoreToKeepThePlayingBarInView(bool horizontal, string mode)
    {
        var (window, viewer, score, session, sessions) = PlayingAtBar40(horizontal, mode);
        using (sessions)
        {
            var before = viewer.Offset;
            session.PlayOrPause();
            for (var frame = 0; frame < SmoothFrames; frame++) { session.PumpFrame(); Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
            var line = score.Probe.Play.PlayheadLine(score.Probe)!.Value;
            Assert.True(viewer.Offset.X > before.X || viewer.Offset.Y > before.Y, "The score did not scroll.");
            Assert.InRange(score.Bounds.X + viewer.Offset.X + line.X * score.Zoom, viewer.Offset.X, viewer.Offset.X + viewer.Viewport.Width);
            Assert.InRange(line.Top * score.Zoom, viewer.Offset.Y, viewer.Offset.Y + viewer.Viewport.Height);
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void FollowOffLeavesTheScoreWhereItIs(bool horizontal)
    {
        var (window, viewer, score, session, sessions) = PlayingAtBar40(horizontal, FollowModes.Smooth, followPlayback: false);
        using (sessions)
        {
            var before = viewer.Offset;
            session.PlayOrPause();
            for (var frame = 0; frame < 10; frame++) { session.PumpFrame(); Dispatcher.UIThread.RunJobs(); }
            Assert.Equal(before, viewer.Offset);
            Assert.NotNull(score.Probe.Play.PlayheadLine(score.Probe));
            window.Close();
        }
    }

    [AvaloniaFact]
    public void PageViewStaysCentredInTheViewerAndHitTestsUseTheOffset()
    {
        var score = new ScorePreviewView();
        var viewer = new ScrollViewer { Content = score, Width = 1800, Height = 600, HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        var window = new Window { Content = viewer, Width = 1800, Height = 600 };
        window.Show();
        score.Refresh();
        Dispatcher.UIThread.RunJobs();
        var page = ScoreGeometryProbe.PageWidth * score.Zoom;
        Assert.InRange(score.Bounds.X - (viewer.Viewport.Width - score.Bounds.Right), -1, 1);
        Assert.InRange(score.Bounds.X, (viewer.Viewport.Width - page) / 2 - 1, (viewer.Viewport.Width - page) / 2 + 1);
        // Pointer positions are the view's own, so a point inside the page maps to the same page point wherever the page sits.
        Assert.Equal(score.Bounds.X + 10, score.TranslatePoint(new Point(10, 10), viewer)!.Value.X, 1);

        viewer.Width = 1300;
        window.Width = 1300;
        Dispatcher.UIThread.RunJobs();
        Assert.InRange(score.Bounds.X - (viewer.Viewport.Width - score.Bounds.Right), -1, 1);
        window.Close();
    }
}
