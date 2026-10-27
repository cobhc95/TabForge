using System.Diagnostics;
using TabForge.Platform;
using TabForge.Rendering;

namespace TabForge.Services.Video;

// Owns: choosing the export's encoder (live recording's rule: one hardware self-test, Auto uses hardware only after it passed), opening it off the
// UI thread, waiting for queue room without blocking, redoing the video with the software encoder when a hardware encoder stalls, and the
// export's working files (the app temp folder, deleted once no encoder holds them).
// Does not own: the steps and the frame loop (VideoExportFlow), the encoders, finishing (VideoEncoderFinisher).
// Tests: VideoExportCancelTests.
public static partial class VideoExportFlow
{
    private static readonly TimeSpan RoomPoll = TimeSpan.FromMilliseconds(250);

    /// <summary>The status while the video is redone with the software encoder.</summary>
    public const string HardwareStalledText = "Hardware encoder stalled — finishing with the software encoder…";

    /// <summary>The folder of the export's working files: the app's temp folder, never the folder the video goes to.</summary>
    public static string StagingFolder => Path.Combine(AppFolders.Current.Temp, "TabForge", "video-export");

    /// <summary>The staging MP4 of a job (".name.tfrender-job.tmp.mp4" in <see cref="StagingFolder"/>), moved over the target on success.</summary>
    public static string StagingPath(string finalPath, string jobId) => Path.Combine(StagingFolder, RenderStaging.TempName(Path.GetFileName(finalPath), jobId));

    /// <summary>Encodes the frames and sound to <paramref name="path"/>; a hardware encoder that stalls is given up on and the video is redone with the software encoder. Returns the file written.</summary>
    internal static async Task<string> EncodeAsync(VideoExportRequest r, string wav, string path, int frames, IProgress<VideoExportProgress>? progress, CancellationToken cancel)
    {
        var options = await OptionsAsync(r, path, cancel);
        try
        {
            await EncodeOnceAsync(r, wav, path, frames, options, progress, cancel);
            return path;
        }
        catch (VideoEncoderException e) when (e.HardwareStalled && options.Encoder != VideoEncoderChoices.Software)
        {
            Log($"hardware encoder stalled ({e.Message}); redoing the video with the software encoder");
            options.HardwareResult?.Invoke(false);   // remembered: Auto uses software (live and export) until the hardware is tested again
            DeleteSoon(path);
            var software = Path.ChangeExtension(path, ".software" + Path.GetExtension(path));
            progress?.Report(new VideoExportProgress(VideoExportProgress.AudioShare, HardwareStalledText));
            var prefixed = progress is null ? null : new Prefixed(progress);
            try { await EncodeOnceAsync(r, wav, software, frames, options with { Encoder = VideoEncoderChoices.Software }, prefixed, cancel); }
            catch (Exception x) when (x is not VideoEncoderException { KeptFile: not null }) { DeleteSoon(software); throw; }
            return software;
        }
    }

    // Says on every report that the software encoder is redoing the video.
    private sealed class Prefixed(IProgress<VideoExportProgress> inner) : IProgress<VideoExportProgress>
    {
        public void Report(VideoExportProgress value) => inner.Report(value with { Text = HardwareStalledText + " " + value.Text });
    }

    // Live recording's rule: one hardware self-test when none is stored (off the UI thread), then Auto uses hardware only after a pass.
    private static async Task<VideoEncoderOptions> OptionsAsync(VideoExportRequest r, string path, CancellationToken cancel)
    {
        var live = r.Settings.LiveVideo ?? new LiveVideoSettings();
        var options = VideoEncoderOptions.Remembering(live);
        if (r.Encoder is VideoEncoderChoices.Hardware or VideoEncoderChoices.Software) options = options with { Encoder = r.Encoder };
        var probe = Path.ChangeExtension(path, ".probe" + Path.GetExtension(path));
        if (r.OpenEncoder is null) options = await Task.Run(() => VideoEncoderFactory.WithHardwareProbe(options, probe, r.Spec.Width, r.Spec.Height, r.Fps)).WaitAsync(cancel);   // a cancel does not wait for the self-test
        var chosen = VideoEncoderFactory.ForExport(options, HostSystem.Current);
        Log($"encoder: dialog {r.Encoder}, Preferences {live.Encoder}, hardware self-test '{options.HardwareProbe}' -> {chosen.Encoder}");
        return chosen;
    }

    // Opens on a pool thread (a driver may take long); a cancel returns at once and the late encoder is closed when it arrives.
    private static async Task<IVideoEncoder> OpenAsync(VideoExportRequest r, string path, VideoEncoderOptions options, CancellationToken cancel)
    {
        var spec = r.Spec;
        var opening = Task.Run(() => r.OpenEncoder is { } open ? open(path, options) : VideoEncoderFactory.Open(path, spec.Width, spec.Height, r.Fps, new VideoAudioFormat(AudioRate, 2), options: options));
        try { return await opening.WaitAsync(cancel); }
        catch (OperationCanceledException)
        {
            _ = opening.ContinueWith(t => { t.Result.Abort("the export was cancelled while the encoder opened"); t.Result.Dispose(); }, TaskContinuationOptions.OnlyOnRanToCompletion);
            throw;
        }
    }

    // Offline encoders hold a few frames: room is awaited (the UI thread stays free), and an encoder that stops taking frames is given up on.
    private static async Task WaitForRoomAsync(IVideoEncoder encoder, TimeSpan stall, CancellationToken cancel)
    {
        if (await encoder.WaitForRoomAsync(TimeSpan.Zero, cancel)) return;
        var last = encoder.Heartbeat;
        var moved = Stopwatch.StartNew();
        while (!await encoder.WaitForRoomAsync(RoomPoll, cancel))
        {
            var beat = encoder.Heartbeat;
            if (beat != last) { last = beat; moved.Restart(); continue; }
            if (moved.Elapsed <= stall) continue;
            var why = $"the encoder took no frame for {stall.TotalSeconds:0.#} s";
            encoder.Abort(why);
            Log($"{why} ({encoder.EncoderName}, hardware {encoder.UsesHardware})");
            throw new VideoEncoderException("The video encoder stopped responding (" + why + ").") { HardwareStalled = encoder.UsesHardware };
        }
    }

    private static void Log(string line) => Trace.Error(Trace.Engine, "video export: " + line);

    private static bool TryDelete(string path)
    {
        try { File.Delete(path); return !File.Exists(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; } // swallow: best-effort delete of a leftover (DeleteSoon tries again)
    }

    // A file a given-up encoder still holds is deleted once it lets go (tried for a minute, off the UI thread); a later export clears what is left.
    private static void DeleteSoon(string path)
    {
        if (TryDelete(path)) return;
        _ = Task.Run(async () =>
        {
            for (var i = 0; i < 60 && !TryDelete(path); i++) await Task.Delay(1000);
            if (File.Exists(path)) Log("could not delete the leftover " + path + " (an encoder still holds it)");
        });
    }

    // Working files older than a day are leftovers of an export that never finished (an encoder that held them, a crash).
    private static void CleanStale(string folder)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(folder))
                if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-1)) TryDelete(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log("could not clear old working files: " + e.Message); }
    }
}
