using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Headless.XUnit;
using TabForge.Audio;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the approval a dropped file gets when its drop lands (its folder, for the song, so its waveform request is allowed), and the hover's
/// refusal to approve or to open an unapproved network folder.
/// Does not own: the drag routing (UI-only) or the waveform reader (TimelineWaveStatusTests).
/// Tests: this class.
/// </summary>
public sealed class MainWindowAudioDropApprovalTests
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

    private sealed class NetworkFs : IMediaFileSystem
    {
        public DriveType DriveTypeOf(string root) => DriveType.Network;
        public string? FinalPathOf(string fullPath) => File.Exists(fullPath) ? fullPath : null;
    }

    private static string NewFile()
    {
        var folder = Path.Combine(Path.GetTempPath(), "tf-drop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "drop.wav");
        File.WriteAllBytes(file, new byte[64]);
        return file;
    }

    [AvaloniaFact]
    public void ADroppedFileOnANetworkFolderIsApprovedWhenTheDropLandsAndItsWaveformIsAllowed()
    {
        var saved = WaveformCache.Default.OpenOverride;
        var savedFs = MediaPathPolicy.FileSystem;
        var file = NewFile();
        try
        {
            WaveformCache.Default.ClearAll();
            MediaPathPolicy.FileSystem = new NetworkFs();
            WaveformCache.Default.OpenOverride = _ => new FakeMedia();
            var context = new MediaContext(() => null);
            Assert.Equal(MediaAccessState.NeedsApproval, MediaAccess.Evaluate(file, context).State);

            var plan = AudioDropPlanner.Plan(new[] { file }, AudioDropTarget.NewTrack, 0, _ => 5);
            MainWindowAudioDrop.ApproveLanded(plan, new Dictionary<string, MediaVerdict> { [file] = MediaPathPolicy.Classify(file, null) }, context);

            Assert.Equal(MediaAccessState.Allowed, MediaAccess.Evaluate(file, context).State);
            _ = WaveformCache.Default.StatusOf(file, context);   // the clip's request: queued, then read by the worker
            Assert.True(WaveformCache.Default.WaitIdle(5000), "peaks read");
            Assert.Equal(WaveState.Ready, WaveformCache.Default.StatusOf(file, context).State);
        }
        finally { WaveformCache.Default.OpenOverride = saved; MediaPathPolicy.FileSystem = savedFs; WaveformCache.Default.ClearAll(); Directory.Delete(Path.GetDirectoryName(file)!, true); }
    }

    [AvaloniaFact]
    public void OnlyFilesInThePlanAreApprovedAFileThatDidNotLandStaysUnapproved()
    {
        var savedFs = MediaPathPolicy.FileSystem;
        var landed = NewFile();
        var skipped = NewFile();
        try
        {
            MediaPathPolicy.FileSystem = new NetworkFs();
            var context = new MediaContext(() => null);
            var plan = AudioDropPlanner.Plan(new[] { landed }, AudioDropTarget.NewTrack, 0, _ => 5);
            var verdicts = new Dictionary<string, MediaVerdict>
            {
                [landed] = MediaPathPolicy.Classify(landed, null),
                [skipped] = MediaPathPolicy.Classify(skipped, null),
            };

            MainWindowAudioDrop.ApproveLanded(plan, verdicts, context);

            Assert.Equal(MediaAccessState.Allowed, MediaAccess.Evaluate(landed, context).State);
            Assert.Equal(MediaAccessState.NeedsApproval, MediaAccess.Evaluate(skipped, context).State);
        }
        finally { MediaPathPolicy.FileSystem = savedFs; Directory.Delete(Path.GetDirectoryName(landed)!, true); Directory.Delete(Path.GetDirectoryName(skipped)!, true); }
    }

    [AvaloniaFact]
    public void AHoverMeasuresNothingFromAnUnapprovedNetworkFolderAndApprovesNothing()
    {
        var saved = WaveformCache.Default.OpenOverride;
        var savedFs = MediaPathPolicy.FileSystem;
        var file = NewFile();
        try
        {
            WaveformCache.Default.ClearAll();
            MediaPathPolicy.FileSystem = new NetworkFs();
            WaveformCache.Default.OpenOverride = _ => new FakeMedia();
            var context = new MediaContext(() => null);

            Assert.Equal(0, WaveformCache.Default.LengthOf(file, context));
            Assert.Equal(MediaAccessState.NeedsApproval, MediaAccess.Evaluate(file, context).State);
        }
        finally { WaveformCache.Default.OpenOverride = saved; MediaPathPolicy.FileSystem = savedFs; WaveformCache.Default.ClearAll(); Directory.Delete(Path.GetDirectoryName(file)!, true); }
    }
}
