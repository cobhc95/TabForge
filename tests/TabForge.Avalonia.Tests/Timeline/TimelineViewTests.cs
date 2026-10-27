using TabForge.Avalonia.Platform;
using TabForge.Platform;
using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using TabForge.Avalonia.Views;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Models;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the read-only timeline check: it draws the demo song in both themes without throwing, and its bar, row and section counts and positions
/// are the Core rules'. Renders go to %TEMP%\tf-avalonia-tests\TimelineViewTests for a look by eye.
/// Tests: TimelineView.
/// </summary>
public class TimelineViewTests
{
    [AvaloniaFact]
    public void CompactHidesTrackRowsAndAddTrackStripWithDoubleMasterLane()
    {
        var song = DemoSongFactory.Create();
        var view = new TimelineView(song) { Compact = true };
        Assert.Equal(0, view.RowCount);
        Assert.Equal(MasterLaneRules.Height(MasterLaneRules.Rows(true, true), TimelineView.DefaultRowHeight), view.MasterLaneHeight);
        view.Compact = false;
        Assert.Equal(song.Tracks.Count, view.RowCount);
    }

    [AvaloniaFact]
    public void SectionBracketStrokeWidthFollowsTheSettingValue()
    {
        var view = new TimelineView(DemoSongFactory.Create());
        view.SetPlayhead(1, 0);   // Inside the demo song's first section (Intro, bars 0 to 8).
        foreach (var thickness in new double[] { 10, 4 })
        {
            view.SectionBracketThickness = thickness;
            var draw = new InstrumentFretNumberTests.RecordingDrawContext();
            view.DrawActiveSectionBrackets(draw, _ => new UiColor(255, 255, 0, 0), 0);
            Assert.Equal(6, draw.LineStrokes.Count);   // Two brackets, three lines each.
            var expected = thickness / TimelineView.SectionBracketThicknessDivisor;
            Assert.All(draw.LineStrokes, stroke => Assert.Equal(expected, stroke.Width, 6));
        }
    }

    [AvaloniaFact]
    public void DrawsDemoSongAtTheCoreRulesCounts()
    {
        var song = DemoSongFactory.Create();
        var view = new TimelineView(song) { HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top };
        Assert.Equal(song.Tracks.Max(t => t.Measures.Count), view.BarCount);
        Assert.Equal(song.Tracks.Count, view.RowCount);
        Assert.Equal(song.Markers.Count, view.SectionCount);
        Assert.Equal(TimelineGeometryRules.BarWidth(30, MusicTime.BarSlots(song, 0)), view.BarX(1), 6);
        Assert.Equal(TimelineView.RulerHeight + TimelineView.SectionHeight + TimelineView.DefaultRowHeight, view.RowTop(1), 6);

        var dir = DialogTestKit.RenderDirectory(nameof(TimelineViewTests));
        foreach (var (name, variant) in new[] { ("dark", ThemeVariant.Dark), ("light", ThemeVariant.Light) })
        {
            var window = new Window { Width = 1300, Height = 400, Content = view, RequestedThemeVariant = variant };
            window.Show();
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame!.Save(Path.Combine(dir, name + ".png"));
            window.Content = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void PointerOverAnAudioClipGivesItsRowAndIndexAsTheHoverClip()
    {
        var song = DemoSongFactory.Create();
        var track = NewTrackFactory.CreateTrack(song, TrackKind.Audio);
        song.Tracks.Add(track);
        var clip = new AudioClip { Name = "Take", StartSec = 0.5, SourceLengthSec = 1 };
        track.AudioClips.Add(clip);
        var view = new TimelineView(song);
        var centre = TimelineAudioClips.ClipBounds(view, track, clip).Center;

        Assert.Equal((song.Tracks.Count - 1, 0), TimelineAudioClips.ClipIndexAt(view, centre));
        Assert.Null(TimelineAudioClips.ClipIndexAt(view, new Point(0, 0)));
    }
}
