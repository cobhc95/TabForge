using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using TabForge.Audio;
using TabForge.Audio.Contracts;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Rendering;
using TabForge.Services;
using TabForge.Services.Video;

namespace TabForge.Views.Video;

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
    public int Fps { get; init; } = 30;
    /// <summary>Audio output settings of the render (the app's by default).</summary>
    public PluginSettings? Plugins { get; init; }
    public Action? Restore { get; init; }
    public Func<IReadOnlyList<string>, Task<bool>>? ConfirmIncomplete { get; init; }
    /// <summary>Called with each frame's number and pixels before it is encoded (a test and diagnostics hook).</summary>
    public Action<int, ReadOnlyMemory<byte>>? FrameTap { get; init; }
    /// <summary>The live score's look to draw with (the defaults when null).</summary>
    internal Views.Score.ScoreAppearance? Look { get; init; }
    /// <summary>Called once with the time spent per phase of the frames (a diagnostics hook).</summary>
    public Action<VideoFrameTimings>? TimingsTap { get; init; }
}

public sealed record VideoExportResult(int Frames, double Seconds, double ElapsedSeconds);

// File > Export > Video: renders the song range to audio offline (every track and plug-in, as File > Render does), then draws frame i at
// time i / fps with VideoFrameSource and encodes both into an MP4. Rendering hops to the dispatcher in short batches, so the window keeps
// answering; encoding runs on the encoder's own thread.
// Owns: the order of the steps, the frame clock, the audio hand-off and cleanup of the temporary and half-written files.
// Does not own: the range choice (the window), drawing or encoding.
// Tests: TestVideoExport.
public static class VideoExportFlow
{
    private const int AudioRate = 48000;

    /// <summary>Exports the video. Throws <see cref="OperationCanceledException"/> (the partial file is removed), <see cref="RenderException"/> or <see cref="VideoEncoderException"/>.</summary>
    public static async Task<VideoExportResult> RunAsync(VideoExportRequest r, IProgress<VideoExportProgress>? progress, CancellationToken cancel)
    {
        progress?.Report(VideoExportProgress.Audio(0));
        var clock = Stopwatch.StartNew();
        var duration = Math.Max(0, r.EndMs - r.StartMs);
        var frames = (int)Math.Round(duration / 1000.0 * r.Fps);
        if (frames < 1) throw new InvalidOperationException("The chosen range is empty.");
        var wav = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TabForge-video-" + Guid.NewGuid().ToString("N")[..8] + ".wav");
        try
        {
            var settings = new RenderSettings
            {
                Format = 2, TailMode = 0, SampleRate = AudioRate, Mono = false,
                SingleThread = r.Settings.Render.SingleThread, RealtimePace = r.Settings.Render.RealtimePace, SafetyLimiter = r.Settings.Render.SafetyLimiter,
            };
            var request = new RenderRequest
            {
                Project = r.Project, Media = r.Media, Plugins = r.Plugins ?? r.Settings.Plugins, MasterPercent = r.Settings.Audio.MasterVolume, Settings = settings,
                Timeline = r.Timeline, StartMs = r.StartMs, EndMs = r.EndMs, MasterFile = wav, Restore = r.Restore, ConfirmIncomplete = r.ConfirmIncomplete,
            };
            var audioProgress = new Progress<RenderProgressInfo>(p => progress?.Report(VideoExportProgress.Audio(p.Fraction)));
            await RenderJob.RunAsync(request, audioProgress, cancel, r.Engine);
            await EncodeAsync(r, wav, frames, progress, cancel);
        }
        catch
        {
            try { File.Delete(r.Path); } catch (IOException) { }
            throw;
        }
        finally { try { File.Delete(wav); } catch (IOException) { } }
        progress?.Report(new VideoExportProgress(1, "Done"));
        return new VideoExportResult(frames, duration / 1000.0, clock.Elapsed.TotalSeconds);
    }

    private static async Task EncodeAsync(VideoExportRequest r, string wav, int frames, IProgress<VideoExportProgress>? progress, CancellationToken cancel)
    {
        var spec = r.Spec;
        using var audio = WavFloatReader.Open(wav);
        using var encoder = VideoEncoder.Open(r.Path, spec.Width, spec.Height, r.Fps, new VideoAudioFormat(AudioRate, 2), options: VideoEncoderOptions.From(r.Settings.LiveVideo ?? new LiveVideoSettings()));
        using var source = new VideoFrameSource(r.Project, r.Timeline, r.Settings, spec, r.Look);
        var slice = Stopwatch.StartNew();
        var phase = Stopwatch.StartNew();
        long audioFrames = 0;
        var buffer = new float[(AudioRate / r.Fps + 2) * 2];
        for (var i = 0; i < frames; i++)
        {
            cancel.ThrowIfCancellationRequested();
            WriteFrame(source, encoder, audio, r, i, buffer, ref audioFrames);
            if (i % 8 == 0) progress?.Report(VideoExportProgress.Frame(i, frames, phase.Elapsed.TotalSeconds));
            if (slice.ElapsedMilliseconds > 40)
            {
                await Dispatcher.Yield(DispatcherPriority.Background);
                slice.Restart();
            }
        }
        encoder.Finish();
        r.TimingsTap?.Invoke(source.Timings);
    }

    private static void WriteFrame(VideoFrameSource source, VideoEncoder encoder, WavFloatReader audio, VideoExportRequest r, int i, float[] buffer, ref long audioFrames)
    {
        var pixels = source.Render(r.StartMs + i * 1000.0 / r.Fps);
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
        private readonly FileStream _file;
        private long _left;

        private WavFloatReader(FileStream file, long dataBytes) { _file = file; _left = dataBytes; }

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
                    if (chunk[0] == 'd' && chunk[1] == 'a') return new WavFloatReader(file, size == 0xFFFFFFFF ? file.Length - file.Position : Math.Min(size, file.Length - file.Position));
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
