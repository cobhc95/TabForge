using Avalonia.Headless;
using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Audio;
using TabForge.Avalonia.Capture;
using TabForge.Avalonia.Platform;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: a clip's name and waveform showing at every render scale (1, 1.5, 2: a HiDPI screen and the 2x capture render).
/// Does not own: the clip painters (TimelineAudioClips) or the waveform outline (TimelineWaveforms).
/// Tests: TimelineAudioClips, TimelineWaveforms, CaptureImage.
/// </summary>
/// <summary>Note: a real HiDPI window draws the name; only the capture bitmap path needed the clip workaround (AvaloniaDrawContext.SkipClips).</summary>
public class TimelineClipScaleTests
{
    private static (TimelineView View, global::Avalonia.Rect Box, AudioClip Clip, Window Host) Setup(string name, string? file, double scale = 1)
    {
        TabForge.Avalonia.Program.InstallMediaPathPolicy();
        var song = DemoSongFactory.Create();
        var track = NewTrackFactory.CreateTrack(song, TrackKind.Audio);
        song.Tracks.Add(track);
        var clip = new AudioClip { Name = name, File = file ?? "", StartSec = 0, SourceLengthSec = 6, FileLengthSec = 6 };
        track.AudioClips.Add(clip);
        var view = new TimelineView(song);
        if (file is not null)
        {
            var context = new MediaContext(() => null);
            MediaAccess.Approve(MediaPathPolicy.Classify(file, null), context);
            view.Media = context;
        }
        var host = ScaledWindow(view, scale);
        return (view, TimelineAudioClips.ClipBounds(view, track, clip), clip, host);
    }

    // The share of pixels in the part of the clip box (DIPs) that differ from the plain fill sampled at the box's top-left inside corner.
    private static double InkIn(CaptureImage image, double scale, double x, double y, double w, double h)
    {
        var part = image.Crop((int)(x * scale), (int)(y * scale), (int)(w * scale), (int)(h * scale));
        return part.InkShare();
    }


    /// <summary>A headless window at a render scaling (a HiDPI screen: 150%, 200%); the frame it paints is in device pixels.</summary>
    private static Window ScaledWindow(Control content, double scale)
    {
        content.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left; content.VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top; content.Width = 1200; content.Height = 400;
        var host = new Window { Content = content, Width = 1200, Height = 700 };
        var impl = host.PlatformImpl!;
        impl.GetType().GetField("<RenderScaling>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(impl, scale);
        ((Action<double>?)impl.GetType().GetProperty("ScalingChanged")!.GetValue(impl))?.Invoke(scale);
        host.Show();
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return host;
    }

    [AvaloniaTheory]
    [InlineData(1.5), InlineData(2.0)]
    public void TheClipNameDrawsInARealWindowAtAHiDpiScaling(double scale)
    {
        TabForge.Avalonia.Program.InstallMediaPathPolicy();
        var song = DemoSongFactory.Create();
        var track = NewTrackFactory.CreateTrack(song, TrackKind.Audio);
        song.Tracks.Add(track);
        track.AudioClips.Add(new AudioClip { Name = "Vocal take", StartSec = 0, SourceLengthSec = 6, FileLengthSec = 6 });
        var view = new TimelineView(song);
        var host = ScaledWindow(view, scale);
        var box = TimelineAudioClips.ClipBounds(view, track, track.AudioClips[0]);
        Assert.Equal(scale, host.RenderScaling);
        using var frame = host.CaptureRenderedFrame()!;
        using var locked = frame.Lock();
        var differing = 0;
        var first = System.Runtime.InteropServices.Marshal.ReadInt32(locked.Address + (int)((box.Y + 2) * scale) * locked.RowBytes + (int)((box.X + 1) * scale) * 4);
        for (var y = (int)((box.Y + 2) * scale); y < (int)((box.Y + 14) * scale); y++)
            for (var x = (int)((box.X + 5) * scale); x < (int)((box.X + 65) * scale); x++)
                if (System.Runtime.InteropServices.Marshal.ReadInt32(locked.Address + y * locked.RowBytes + x * 4) != first) differing++;
        Assert.True(differing > 20, $"name ink in a real window at {scale}x");
    }
    [AvaloniaTheory]
    [InlineData(1.0), InlineData(1.5), InlineData(2.0)]
    public void TheClipNameDrawsAtEveryRenderScale(double scale)
    {
        var (view, box, _, _) = Setup("Vocal take", null);
        var image = CaptureImage.Render(view, scale);
        Assert.True(InkIn(image, scale, box.X + 5, box.Y + 2, 60, 12) > 0.02, $"name ink at scale {scale}");
    }

    [AvaloniaTheory]
    [InlineData(1.0), InlineData(1.5), InlineData(2.0)]
    public void TheClipWaveformDrawsAtEveryRenderScale(double scale)
    {
        var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tf-scale-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var file = CaptureFixtures.WriteWave(folder, "tone", 6);
            var (view, box, _, host) = Setup("", file);
            CaptureImage.Render(view, 1);
            Assert.True(WaveformCache.Default.WaitIdle(5000), "peaks read");
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var image = CaptureImage.Render(view, scale);
            Assert.True(view.Waveforms.OutlinesBuilt > 0, "outline built");
            Assert.True(InkIn(image, scale, box.X + 2, box.Y + box.Height / 2 - 10, box.Width - 4, 20) > 0.02, $"wave ink at scale {scale}");
        }
        finally { WaveformCache.Default.ClearAll(); Directory.Delete(folder, true); }
    }

    [Fact]
    public void ANormalRenderRunningDuringACaptureStillClips()
    {
        bool? otherFlowSkips = null;
        AvaloniaDrawContext.WithClipsSkipped(true, () =>
        {
            Assert.True(AvaloniaDrawContext.ClipsAreSkipped, "the capture render skips clips");
            using (System.Threading.ExecutionContext.SuppressFlow())
            {
                var other = new System.Threading.Thread(() => otherFlowSkips = AvaloniaDrawContext.ClipsAreSkipped);
                other.Start();
                other.Join();
            }
        });
        Assert.False(otherFlowSkips, "a normal render on another flow still clips");
        Assert.False(AvaloniaDrawContext.ClipsAreSkipped, "the flag ends with the capture");
    }
}
