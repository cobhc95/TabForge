// Owns: the P2-timeline perf guards (fix1): the row-sync change report, the
// follower/footer no-op stability, and the waveform margin + zoom reuse.
// Tests: ArrangementTrackRows.SyncSelectedRow, TimelineFollowScroll,
// TimelineWaveforms (SetViewport/SetZooming), TimelineView.VisibleBarRange.
using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using TabForge.Audio;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Presets;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

public class TimelinePerfGuardsTests
{
    [AvaloniaFact]
    public void RowSyncReportsChangeOnlyWhenTheRowChanges()
    {
        var song = DemoSongFactory.Create();
        var document = new SongDocument(() => null) { Project = song };
        var timeline = new TimelineView(song);
        var header = new TrackHeaderColumn(timeline, song);
        var rows = new ArrangementTrackRows(timeline, header, document, () => 400, _ => { });

        Assert.True(rows.SyncSelectedRow());
        Assert.False(rows.SyncSelectedRow());
        document.TrackIndex = 1;
        Assert.True(rows.SyncSelectedRow());
        Assert.False(rows.SyncSelectedRow());
    }

    [AvaloniaFact]
    public void PagingAnInsideBarLeavesTheOffsetAndFooterAlone()
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var view = new ArrangementView(song.Project, song);
        var window = new Window { Width = 800, Height = 500, Content = view };
        try
        {
            window.Show();
            view.Timeline.ZoomFactor = 0.5;
            window.UpdateLayout();
            var sideways = view.Timeline.GetVisualAncestors().OfType<ScrollViewer>().First();
            sideways.Offset = new Vector(view.Timeline.BarX(view.Timeline.BarCount / 2), 0);
            window.UpdateLayout();
            var midBar = view.Timeline.BarAtX(sideways.Offset.X + sideways.Viewport.Width / 2);
            var firstVisible = view.Timeline.BarAtX(sideways.Offset.X);
            var lastVisible = view.Timeline.BarAtX(sideways.Offset.X + sideways.Viewport.Width - 1);
            Assert.True(midBar - firstVisible >= ArrangementFollowRules.EdgeBarCount
                && lastVisible - midBar >= ArrangementFollowRules.EdgeBarCount,
                $"interior check: mid={midBar} first={firstVisible} last={lastVisible} off={sideways.Offset.X} vp={sideways.Viewport.Width} bars={view.Timeline.BarCount}");
            var before = sideways.Offset.X;

            TimelineFollowScroll.PageForEdgeBar(sideways, view.Timeline, midBar);
            window.UpdateLayout();

            Assert.Equal(before, sideways.Offset.X);
            Assert.Equal(sideways.Viewport.Width, view.Timeline.MinWidth);
            Assert.Equal(sideways.Offset.X, view.FooterScrollBar.Value, 6);
        }
        finally { window.Close(); }
    }

    private sealed class FixedFs : IMediaFileSystem
    {
        public DriveType DriveTypeOf(string root) => DriveType.Fixed;
        public string? FinalPathOf(string fullPath) => File.Exists(fullPath) ? fullPath : null;
    }

    private sealed class FakeMedia : IMediaSource
    {
        private long _left;
        public FakeMedia(double seconds, long frames) { TotalSeconds = seconds; _left = frames; }
        public int SampleRate => 8000;
        public int Channels => 1;
        public double TotalSeconds { get; }
        public int Read(float[] buffer, int offset, int count)
        {
            var n = (int)Math.Min(count, _left);
            for (var i = 0; i < n; i++) buffer[offset + i] = 0.5f;
            _left -= n;
            return n;
        }
        public void Dispose() { }
    }

    [AvaloniaFact]
    public void WaveformOutlineSurvivesSmallScrollsAndZoomSteps()
    {
        var savedOpen = WaveformCache.Default.OpenOverride;
        var savedFs = MediaPathPolicy.FileSystem;
        var temp = Path.Combine(Path.GetTempPath(), "tf-perf-guard-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            File.WriteAllBytes(temp, new byte[64]);
            WaveformCache.Default.ClearAll();
            MediaPathPolicy.FileSystem = new FixedFs();
            WaveformCache.Default.OpenOverride = _ => new FakeMedia(300, 8000L * 300);
            var view = new TimelineView(DemoSongFactory.Create());
            var clip = new AudioClip { File = temp, Name = "Take", StartSec = 0, SourceLengthSec = 20, FileLengthSec = 20 };
            var box = new Rect(0, 0, 2000, 30);
            var colour = new UiColor(255, 200, 100, 50);
            var draw = new NullDrawContext();
            view.Waveforms.Draw(draw, clip, box, colour, 1, songWidth: 5000);
            Assert.True(WaveformCache.Default.WaitIdle(5000), "peaks read");
            view.Waveforms.Draw(draw, clip, box, colour, 1, songWidth: 5000);
            var built = view.Waveforms.OutlinesBuilt;
            Assert.Equal(1, built);

            // A small scroll keeps the same span: no rebuild, columns within the viewport plus margins.
            view.Waveforms.SetViewport(100, 1000);
            view.Waveforms.Draw(draw, clip, box, colour, 1, songWidth: 5000);
            Assert.Equal(built, view.Waveforms.OutlinesBuilt);
            Assert.True(view.Waveforms.WaveColumnsBuilt <= 3001, $"columns {view.Waveforms.WaveColumnsBuilt}");

            // A zoom step draws the cached outline scaled: no rebuild; settling rebuilds once at full quality.
            view.Waveforms.SetZooming(true);
            var zoomed = new Rect(0, 0, 3000, 30);
            view.Waveforms.Draw(draw, clip, zoomed, colour, 1, songWidth: 7500);
            Assert.Equal(built, view.Waveforms.OutlinesBuilt);
            view.Waveforms.SetZooming(false);
            view.Waveforms.Draw(draw, clip, zoomed, colour, 1, songWidth: 7500);
            Assert.Equal(built + 1, view.Waveforms.OutlinesBuilt);

            // Scrolled far past the built span, an overlapping clip rebuilds for its own span.
            view.Waveforms.SetViewport(5000, 1000);
            view.Waveforms.Draw(draw, clip, new Rect(4500, 0, 3000, 30), colour, 1, songWidth: 7500);
            Assert.Equal(built + 2, view.Waveforms.OutlinesBuilt);
        }
        finally
        {
            WaveformCache.Default.OpenOverride = savedOpen;
            MediaPathPolicy.FileSystem = savedFs;
            WaveformCache.Default.ClearAll();
            try { File.Delete(temp); } catch (IOException) { }
        }
    }
}
