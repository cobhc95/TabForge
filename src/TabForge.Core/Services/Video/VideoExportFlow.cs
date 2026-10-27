using System.Diagnostics;
using TabForge.Audio;
using TabForge.Audio.Contracts;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Playback;
using TabForge.Rendering;

namespace TabForge.Services.Video;

/// <summary>The port a host's frame drawing fills: the song at a time, as top-down BGRA pixels at the spec's size (width x height x 4).</summary>
public interface IVideoFrameSource : IDisposable
{
    /// <summary>Draws the song at <paramref name="ms"/> (song time, as the timeline counts it); the span is valid until the next call. UI thread.</summary>
    ReadOnlySpan<byte> Render(double ms);

    /// <summary>Per-phase drawing times (a diagnostics hook; a host may leave them at zero).</summary>
    VideoFrameTimings Timings { get; }
}

public sealed class VideoExportRequest
{
    public required SongProject Project { get; init; }
    public required AppSettings Settings { get; init; }
    public required AudioEngineClient Engine { get; init; }
    public MediaContext? Media { get; init; }
    public required ScoreTimeline Timeline { get; init; }
    /// <summary>The song range in timeline milliseconds (from <see cref="RenderSpecBuilder.Bounds"/>).</summary>
    public required double StartMs { get; init; }
    public required double EndMs { get; init; }
    public required VideoViewSpec Spec { get; init; }
    public required string Path { get; init; }
    /// <summary>Opens the host's frame source for <see cref="Spec"/> (Avalonia AvaloniaVideoFrameSource). Called on the UI thread.</summary>
    public required Func<IVideoFrameSource> OpenFrames { get; init; }
    /// <summary>The UI thread the frames are drawn on; the flow yields to it between batches so the window keeps answering.</summary>
    public required IUiThread Ui { get; init; }
    public int Fps { get; init; } = 30;
    /// <summary>Audio output settings of the render (the app's by default).</summary>
    public PluginSettings? Plugins { get; init; }
    public Action? Restore { get; init; }
    public Func<IReadOnlyList<string>, Task<bool>>? ConfirmIncomplete { get; init; }
    /// <summary>Called with each frame's number and pixels before it is encoded (a test and diagnostics hook).</summary>
    public Action<int, ReadOnlyMemory<byte>>? FrameTap { get; init; }
    /// <summary>Called once with the time spent per phase of the frames (a diagnostics hook).</summary>
    public Action<VideoFrameTimings>? TimingsTap { get; init; }
    /// <summary>The encoder chosen in the dialog: Auto (the Preferences choice; Media Foundation uses hardware only once it passed the self-test), Hardware or Software.</summary>
    public string Encoder { get; init; } = VideoEncoderChoices.Auto;
    /// <summary>A test hook: opens this encoder instead of the factory's (given the staging path and the options the flow chose).</summary>
    internal Func<string, VideoEncoderOptions, IVideoEncoder>? OpenEncoder { get; init; }
    /// <summary>A test hook: how often the finish watchdog looks at the encoder's heartbeat.</summary>
    internal TimeSpan? FinishPoll { get; init; }
    /// <summary>A test hook: how long a hardware encoder may make no progress before the video is redone in software.</summary>
    internal TimeSpan? HardwareStall { get; init; }
}

public sealed record VideoExportResult(int Frames, double Seconds, double ElapsedSeconds);

// File > Export > Video, shared by both apps: renders the song range to audio offline (every track and plug-in, as File > Render does, with the
// no tail: the video ends with the range, not with a ring-out of silence under frozen frames), then draws frame i at time i / fps through the host's frame source and encodes both into an MP4.
// The video lasts as long as the rendered sound (the range), so no last samples are cut and no frozen frames follow. The MP4 is
// written to a staging file in the app's temp folder and moved over the target only on success: a failure or cancel never touches a file that
// was there. The encoder follows live recording's rule (Auto: hardware only after the self-test passed); a hardware encoder that makes no
// progress for 15 s is given up on and the video is redone with the software encoder. The UI thread never waits for the encoder.
// Owns: the order of the steps, the frame clock, the audio hand-off, the staging files, the hardware-stall fallback and the "video export" log lines.
// Does not own: the range choice (the window), drawing (IVideoFrameSource) or encoding (VideoEncoder).
// Tests: VideoExportFinishTests, VideoExportCancelTests, TabForge.Avalonia.Tests.VideoExportParityTests.
public static partial class VideoExportFlow
{
    private const int AudioRate = 48000;
    private const int YieldEveryMs = 40;   // the longest the UI thread is held between yields
    private const int ProgressEveryFrames = 8;

    /// <summary>Exports the video. Throws <see cref="OperationCanceledException"/>, <see cref="RenderException"/> or <see cref="VideoEncoderException"/>; the target is then left as it was.</summary>
    public static async Task<VideoExportResult> RunAsync(VideoExportRequest r, IProgress<VideoExportProgress>? progress, CancellationToken cancel)
    {
        progress?.Report(VideoExportProgress.Audio(0));
        var clock = Stopwatch.StartNew();
        var duration = Math.Max(0, r.EndMs - r.StartMs);
        if ((int)Math.Round(duration / 1000.0 * r.Fps) < 1) throw new InvalidOperationException("The chosen range is empty.");
        var jobId = Guid.NewGuid().ToString("N")[..8];
        var staging = StagingPath(r.Path, jobId);
        var wav = System.IO.Path.Combine(StagingFolder, "TabForge-video-" + jobId + ".wav");
        var written = staging;
        Log($"begin {r.Spec.Width}x{r.Spec.Height} {r.Fps} fps, range {r.StartMs:0}-{r.EndMs:0} ms, encoder choice {r.Encoder}, target {r.Path}, staging {staging}, UI {VideoEncoderFinisher.ThreadText()}");
        int frames;
        try
        {
            Directory.CreateDirectory(StagingFolder);
            CleanStale(StagingFolder);
            var render = r.Settings.Render;
            var settings = new RenderSettings
            {
                Format = 2, TailMode = (int)RenderTailMode.None, TailMs = 0, SampleRate = AudioRate, Mono = false,
                SingleThread = render.SingleThread, RealtimePace = render.RealtimePace, SafetyLimiter = render.SafetyLimiter,
            };
            var request = new RenderRequest
            {
                Project = r.Project, Media = r.Media, Plugins = r.Plugins ?? r.Settings.Plugins, MasterPercent = r.Settings.Audio.MasterVolume, Settings = settings,
                Timeline = r.Timeline, StartMs = r.StartMs, EndMs = r.EndMs, MasterFile = wav, Restore = r.Restore, ConfirmIncomplete = r.ConfirmIncomplete,
            };
            var audioProgress = new Progress<RenderProgressInfo>(p => progress?.Report(VideoExportProgress.Audio(p.Fraction)));
            await RenderJob.RunAsync(request, audioProgress, cancel, r.Engine);
            frames = FramesFor(WavFloatReader.FramesIn(wav), r.Fps);
            written = await EncodeAsync(r, wav, staging, frames, progress, cancel);
            cancel.ThrowIfCancellationRequested();
            await Task.Run(() => File.Move(written, r.Path, overwrite: true));   // the save dialog asked before an existing file is replaced; off the UI thread (it may copy across drives)
            Log($"done: {frames} frames, moved to {r.Path} after {clock.ElapsedMilliseconds} ms");
        }
        catch (Exception e)
        {
            Log($"{(e is OperationCanceledException ? "cancelled" : "failed")} after {clock.ElapsedMilliseconds} ms: {e.Message}");
            if (e is not VideoEncoderException { KeptFile: not null }) { DeleteSoon(staging); DeleteSoon(written); }   // a video the encoder could not finish stays on disk, and the message says where
            throw;
        }
        finally { TryDelete(wav); }
        progress?.Report(new VideoExportProgress(1, "Done"));
        return new VideoExportResult(frames, frames / (double)r.Fps, clock.Elapsed.TotalSeconds);
    }

    /// <summary>The frames that cover <paramref name="audioFrames"/> of sound at 48 kHz: the last frame ends at or after the last sample.</summary>
    public static int FramesFor(long audioFrames, int fps) => (int)Math.Max(1, (audioFrames * fps + AudioRate - 1) / AudioRate);

    // One try with one encoder: the frames are drawn on the UI thread, which never waits for the encoder (room is awaited, finishing runs on a
    // pool thread); a cancel or failure gives the encoder up at once and disposes it off the UI thread.
    private static async Task EncodeOnceAsync(VideoExportRequest r, string wav, string path, int frames, VideoEncoderOptions options, IProgress<VideoExportProgress>? progress, CancellationToken cancel)
    {
        var spec = r.Spec;
        using var audio = WavFloatReader.Open(wav);
        // Finishing may take long at 4K (the encoder holds a backlog): it only fails after this long with no progress at all.
        var allowance = VideoEncoderFinisher.StallAllowance(spec.Width, spec.Height, r.Fps);
        var encoder = await OpenAsync(r, path, options with { FinalizeTimeout = allowance }, cancel);
        var stall = encoder.UsesHardware ? r.HardwareStall ?? VideoEncoderFinisher.HardwareStall : allowance;
        Log($"encoder opened: {encoder.EncoderName}, hardware {encoder.UsesHardware}, no-progress allowance {stall.TotalSeconds:0.#} s, {frames} frames -> {path}");
        var finished = false;
        try
        {
            using var source = r.OpenFrames();
            var slice = Stopwatch.StartNew();
            var phase = Stopwatch.StartNew();
            long audioFrames = 0;
            var buffer = new float[(AudioRate / r.Fps + 2) * 2];
            for (var i = 0; i < frames; i++)
            {
                cancel.ThrowIfCancellationRequested();
                await WaitForRoomAsync(encoder, stall, cancel);   // the UI thread is never held by a full queue, and an encoder that stops taking frames is noticed
                WriteFrame(source, encoder, audio, r, i, buffer, ref audioFrames);
                if (i % ProgressEveryFrames == 0) progress?.Report(VideoExportProgress.Frame(i, frames, phase.Elapsed.TotalSeconds));
                if (slice.ElapsedMilliseconds > YieldEveryMs)
                {
                    await r.Ui.YieldAsync(UiPriority.Background);
                    slice.Restart();
                }
            }
            r.TimingsTap?.Invoke(source.Timings);
            Log($"all {frames} frames handed over in {phase.ElapsedMilliseconds} ms (last frame at {(frames - 1) * 1000.0 / r.Fps:0.#} ms, sound {audioFrames} frames to {audioFrames * 1000.0 / AudioRate:0.#} ms)");
            await VideoEncoderFinisher.FinishAsync(encoder, stall, path, progress, r.FinishPoll, cancel);   // off the UI thread, with a "Finishing video" status
            finished = true;
        }
        finally
        {
            if (!finished) encoder.Abort("the export stopped before the video was finished");
            _ = Task.Run(encoder.Dispose);   // never on the UI thread: a finished or given-up encoder returns at once, a stuck one is not waited for
        }
    }

    private static void WriteFrame(IVideoFrameSource source, IVideoEncoder encoder, WavFloatReader audio, VideoExportRequest r, int i, float[] buffer, ref long audioFrames)
    {
        var pixels = source.Render(Math.Min(r.EndMs, r.StartMs + i * 1000.0 / r.Fps));   // the tail holds the range's last moment
        r.FrameTap?.Invoke(i, pixels.ToArray());
        var t0 = Stopwatch.GetTimestamp();
        encoder.WriteFrame(pixels, TimeSpan.FromTicks(i * 10_000_000L / r.Fps));
        var upTo = (long)Math.Round((i + 1) * (double)AudioRate / r.Fps);
        var count = (int)(upTo - audioFrames);
        audio.Read(buffer.AsSpan(0, count * 2));
        encoder.WriteAudio(buffer.AsSpan(0, count * 2));
        audioFrames = upTo;
        source.Timings.Encode += Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        source.Timings.Frames++;
    }


    /// <summary>Reads interleaved float samples from a 32-bit float stereo WAV; past the end it returns silence.</summary>
    private sealed class WavFloatReader : IDisposable
    {
        private const int StereoFloatBytes = 8;
        private readonly FileStream _file;
        private long _left;

        private WavFloatReader(FileStream file, long dataBytes) { _file = file; _left = dataBytes; }

        public long DataBytes { get; private init; }

        /// <summary>The stereo frames the WAV holds.</summary>
        public static long FramesIn(string path) { using var reader = Open(path); return reader.DataBytes / StereoFloatBytes; }

        public static WavFloatReader Open(string path)
        {
            var file = File.OpenRead(path);
            try
            {
                var head = new byte[12];
                if (file.Read(head, 0, 12) != 12 || head[0] != 'R' || head[8] != 'W') throw new VideoEncoderException("The rendered audio is not a WAV file.");
                var chunk = new byte[8];
                while (file.Read(chunk, 0, 8) == 8)
                {
                    var size = BitConverter.ToUInt32(chunk, 4);
                    if (chunk[0] == 'd' && chunk[1] == 'a')
                    {
                        var bytes = size == 0xFFFFFFFF ? file.Length - file.Position : Math.Min(size, file.Length - file.Position);
                        return new WavFloatReader(file, bytes) { DataBytes = bytes };
                    }
                    file.Seek(size + (size & 1), SeekOrigin.Current);
                }
                throw new VideoEncoderException("The rendered audio has no sample data.");
            }
            catch { file.Dispose(); throw; }
        }

        public void Read(Span<float> samples)
        {
            samples.Clear();
            var bytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(samples);
            var take = (int)Math.Min(bytes.Length, _left);
            var got = take > 0 ? _file.Read(bytes[..take]) : 0;
            _left -= got;
        }

        public void Dispose() => _file.Dispose();
    }
}

/// <summary>Accumulated milliseconds per phase of drawing frames: Layout (playhead, page turn, layout passes), Bake (score page bake, score-only
/// and Focus without an instrument), Instrument, Band (Band tick and control hiding), Render (rasterising the tree), Copy (readback), Compose
/// (score-only page copy and playhead blend) and Encode (the hand-off to the encoder, including any wait for queue room).</summary>
public sealed class VideoFrameTimings
{
    public double Layout, Bake, Instrument, Band, Render, Copy, Compose, Encode;
    public int Bakes;
    public int Frames;
}
