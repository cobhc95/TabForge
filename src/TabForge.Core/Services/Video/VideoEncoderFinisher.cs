using System.Diagnostics;

namespace TabForge.Services.Video;

// Owns: finishing an encoder for an offline export: Finish runs on a worker thread (never the UI thread), the status says "Finishing video…",
// and the encoder is given up on only when its heartbeat has not moved for the stall allowance (a hardware stall is marked so the export can
// redo the video in software). A given-up encoder keeps its unfinished file. A cancel abandons the encoder at once and never waits for it.
// Does not own: the encoders' own finalising (VideoEncoder, FfmpegVideoEncoder) or the export steps (VideoExportFlow).
// Tests: VideoExportFinishTests.
public static class VideoEncoderFinisher
{
    private static readonly TimeSpan DefaultPoll = TimeSpan.FromMilliseconds(250);

    /// <summary>How long a finishing encoder may show no progress: 30 s, up to 60 s at 4K 60 fps (the sink writer encodes its whole backlog while finishing).</summary>
    public static TimeSpan StallAllowance(int width, int height, int fps) =>
        TimeSpan.FromSeconds(30 + 30.0 * width * height * fps / (3840.0 * 2160 * 60));

    /// <summary>The no-progress allowance of a hardware encoder: past it the export is redone with the software encoder.</summary>
    public static readonly TimeSpan HardwareStall = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Finishes <paramref name="encoder"/> without blocking the caller. Throws <see cref="VideoEncoderException"/> (with <see cref="VideoEncoderException.KeptFile"/>,
    /// and <see cref="VideoEncoderException.HardwareStalled"/> for a hardware encoder) when it stalls, and <see cref="OperationCanceledException"/> at once on
    /// <paramref name="cancel"/>: the encoder is then abandoned on its own thread and never waited for.
    /// </summary>
    public static async Task FinishAsync(IVideoEncoder encoder, TimeSpan stall, string keptPath, IProgress<VideoExportProgress>? progress, TimeSpan? poll = null, CancellationToken cancel = default)
    {
        var clock = Stopwatch.StartNew();
        progress?.Report(Status(0));
        Trace.Error(Trace.Engine, $"video export: finishing {encoder.EncoderName} (no-progress allowance {stall.TotalSeconds:0.#} s)");
        var finish = Task.Run(encoder.Finish);   // a pool thread: the UI keeps answering
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancel.Register(() => cancelled.TrySetResult());
        var last = encoder.Heartbeat;
        var moved = clock.Elapsed;
        while (await Task.WhenAny(finish, Task.Delay(poll ?? DefaultPoll), cancelled.Task) != finish)
        {
            if (cancel.IsCancellationRequested)
            {
                encoder.Abort("the export was cancelled while finishing");
                Observe(finish);
                Trace.Error(Trace.Engine, $"video export: cancelled while finishing after {clock.ElapsedMilliseconds} ms; the encoder was left to its own thread");
                throw new OperationCanceledException(cancel);
            }
            var beat = encoder.Heartbeat;
            if (beat != last) { last = beat; moved = clock.Elapsed; }
            if (clock.Elapsed - moved > stall)
            {
                var why = $"the encoder made no progress for {stall.TotalSeconds:0.#} s while finishing the file";
                encoder.Abort(why);
                Observe(finish);   // the stall is the error
                Trace.Error(Trace.Engine, $"video export: {why} ({encoder.EncoderName}, hardware {encoder.UsesHardware})");
                throw Kept(new VideoEncoderException("The video encoder stopped responding while saving (" + why + ")."), keptPath, encoder.UsesHardware);
            }
            progress?.Report(Status((int)clock.Elapsed.TotalSeconds));
        }
        try { await finish; }
        catch (VideoEncoderException e) when (e.KeptFile is { } kept) { throw Kept(e, kept, e.HardwareStalled); }
        Trace.Error(Trace.Engine, $"video export: finished in {clock.ElapsedMilliseconds} ms");
    }

    // A finish given up on may still fail later: its error is observed, not rethrown.
    private static void Observe(Task finish) => _ = finish.ContinueWith(t => t.Exception, TaskContinuationOptions.OnlyOnFaulted);

    /// <summary>"thread 12 'VideoEncoder' MTA background": the calling thread and its COM apartment, for the logs.</summary>
    public static string ThreadText()
    {
        var t = Thread.CurrentThread;
        return $"thread {t.ManagedThreadId} '{t.Name ?? (t.IsThreadPoolThread ? "pool" : "unnamed")}' {t.GetApartmentState()}{(t.IsBackground ? " background" : "")}";
    }

    private static VideoEncoderException Kept(VideoEncoderException e, string path, bool hardware) =>
        new(e.Message + $" The unfinished video was kept at {path}.", e) { KeptFile = path, HardwareStalled = hardware };

    private static VideoExportProgress Status(int seconds) =>
        new(0.99, seconds < 2 ? "Finishing video…" : $"Finishing video… {seconds} s");
}
