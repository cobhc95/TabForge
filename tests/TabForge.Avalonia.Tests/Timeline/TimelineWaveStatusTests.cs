using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using TabForge.Audio;
using TabForge.Avalonia.Platform;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the timeline waveform reading in the shown document media scope, what a clip shows while it has no waveform (loading, lock, warning),
/// and the import notice for a file that cannot be read.
/// Does not own: the peak reader (AudioSmokeTests) or the outline reuse (TimelinePerfGuardsTests).
/// Tests: this class.
/// </summary>
public sealed class TimelineWaveStatusTests
{
    private sealed class FakeMedia : IMediaSource
    {
        private long _left = 8000L * 5;
        public int SampleRate => 8000;
        public int Channels => 1;
        public double TotalSeconds => 5;
        public int Read(float[] buffer, int offset, int count)
        {
            var n = (int)Math.Min(count, _left);
            for (var i = 0; i < n; i++) buffer[offset + i] = 0.5f;
            _left -= n;
            return n;
        }
        public void Dispose() { }
    }

    private sealed class DriveFs : IMediaFileSystem
    {
        private readonly DriveType _type;
        public DriveFs(DriveType type) { _type = type; }
        public DriveType DriveTypeOf(string root) => _type;
        public string? FinalPathOf(string fullPath) => File.Exists(fullPath) ? fullPath : null;
    }

    private static readonly Rect Box = new(0, 0, 400, 30);
    private static readonly UiColor Colour = new(255, 200, 100, 50);

    private static string NewFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "tf-wave-status-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static string NewFile(string folder)
    {
        var file = Path.Combine(folder, "wv-" + Guid.NewGuid().ToString("N") + ".wav");
        File.WriteAllBytes(file, new byte[64]);
        return file;
    }

    [AvaloniaFact]
    public void TheTimelineReadsAClipFileInTheDocumentMediaScope()
    {
        var saved = WaveformCache.Default.OpenOverride;
        var savedFs = MediaPathPolicy.FileSystem;
        var folder = NewFolder();
        try
        {
            var file = NewFile(folder);
            WaveformCache.Default.ClearAll();
            MediaPathPolicy.FileSystem = new DriveFs(DriveType.Network);
            WaveformCache.Default.OpenOverride = _ => new FakeMedia();
            // The folder is approved for this document: only its own scope sees that (the anonymous scope can never approve).
            var context = new MediaContext(() => null);
            MediaAccess.Approve(MediaPathPolicy.Classify(file, null), context);
            var view = new TimelineView(DemoSongFactory.Create()) { Media = context };
            var clip = new AudioClip { File = file, Name = "Take", SourceLengthSec = 5, FileLengthSec = 5 };
            var draw = new NullDrawContext();

            view.Waveforms.Draw(draw, clip, Box, Colour, 1, 400);
            Assert.True(WaveformCache.Default.WaitIdle(5000), "peaks read");
            view.Waveforms.Draw(draw, clip, Box, Colour, 1, 400);

            Assert.Equal(WaveState.Ready, view.Waveforms.StatusOf(clip).State);
            Assert.Equal(1, view.Waveforms.OutlinesBuilt);
        }
        finally { WaveformCache.Default.OpenOverride = saved; MediaPathPolicy.FileSystem = savedFs; WaveformCache.Default.ClearAll(); Directory.Delete(folder, true); }
    }

    [AvaloniaFact]
    public void AClipWaitingForApprovalShowsALockAndTheWordsToClick()
    {
        var saved = WaveformCache.Default.OpenOverride;
        var savedFs = MediaPathPolicy.FileSystem;
        var folder = NewFolder();
        try
        {
            var file = NewFile(folder);
            WaveformCache.Default.ClearAll();
            MediaPathPolicy.FileSystem = new DriveFs(DriveType.Network);
            WaveformCache.Default.OpenOverride = _ => new FakeMedia();
            var view = new TimelineView(DemoSongFactory.Create()) { Media = new MediaContext(() => null) };
            var clip = new AudioClip { File = file, Name = "Take", SourceLengthSec = 5, FileLengthSec = 5 };
            view.Waveforms.Draw(new NullDrawContext(), clip, Box, Colour, 1, 400);
            Assert.True(WaveformCache.Default.WaitIdle(5000));

            var draw = new InstrumentFretNumberTests.RecordingDrawContext();
            view.Waveforms.Draw(draw, clip, Box, Colour, 1, 400);

            Assert.Equal(WaveState.NeedsApproval, view.Waveforms.StatusOf(clip).State);
            Assert.Contains(draw.Texts, t => t.Text == TimelineWaveforms.ApproveText);
            Assert.Single(draw.Ellipses);   // the shackle of the lock
            Assert.Equal(0, view.Waveforms.OutlinesBuilt);
        }
        finally { WaveformCache.Default.OpenOverride = saved; MediaPathPolicy.FileSystem = savedFs; WaveformCache.Default.ClearAll(); Directory.Delete(folder, true); }
    }

    [AvaloniaFact]
    public void AClipThatCannotBeDecodedShowsAWarningAndRetryReadsItAgain()
    {
        var saved = WaveformCache.Default.OpenOverride;
        var savedFs = MediaPathPolicy.FileSystem;
        var folder = NewFolder();
        try
        {
            var file = NewFile(folder);
            WaveformCache.Default.ClearAll();
            MediaPathPolicy.FileSystem = new DriveFs(DriveType.Fixed);
            WaveformCache.Default.OpenOverride = _ => throw new InvalidDataException("not audio");
            var context = new MediaContext(() => null);
            var view = new TimelineView(DemoSongFactory.Create()) { Media = context };
            var clip = new AudioClip { File = file, Name = "Take", SourceLengthSec = 5, FileLengthSec = 5 };
            view.Waveforms.Draw(new NullDrawContext(), clip, Box, Colour, 1, 400);
            Assert.True(WaveformCache.Default.WaitIdle(5000));

            var draw = new InstrumentFretNumberTests.RecordingDrawContext();
            view.Waveforms.Draw(draw, clip, Box, Colour, 1, 400);

            Assert.Equal(WaveState.Failed, view.Waveforms.StatusOf(clip).State);
            Assert.Contains(draw.Texts, t => t.Text == TimelineWaveforms.WarningGlyph);

            WaveformCache.Default.OpenOverride = _ => new FakeMedia();
            WaveformCache.Default.Retry(file, context);
            view.Waveforms.StatusOf(clip);
            Assert.True(WaveformCache.Default.WaitIdle(5000));
            Assert.Equal(WaveState.Ready, view.Waveforms.StatusOf(clip).State);
        }
        finally { WaveformCache.Default.OpenOverride = saved; MediaPathPolicy.FileSystem = savedFs; WaveformCache.Default.ClearAll(); Directory.Delete(folder, true); }
    }

    [Fact]
    public void AFileThatMeasuresZeroGivesANoticeNamingItAndWhy()
    {
        var plan = AudioDropPlanner.Plan(new[] { @"C:\drops\broken.mp3", @"C:\drops\fine.wav" }, AudioDropTarget.NewTrack, 0, file => file.EndsWith("fine.wav") ? 2 : 0);

        var notice = AudioDropPlanner.SkippedNotice(plan);

        Assert.NotNull(notice);
        Assert.Contains("broken.mp3", notice);
        Assert.Contains("could not be read", notice);
        Assert.DoesNotContain("fine.wav", notice);
        Assert.Null(AudioDropPlanner.SkippedNotice(AudioDropPlanner.Plan(new[] { @"C:\drops\fine.wav" }, AudioDropTarget.NewTrack, 0, _ => 2)));
    }
}
