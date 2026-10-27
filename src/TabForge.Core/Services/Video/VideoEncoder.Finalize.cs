using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TabForge.Services.Video;

// Owns: the one end-of-file sequence of the Media Foundation encoder, shared by live recording and offline export (the queue drained,
// then the sink writer finalised), and its diagnostics: the threads and COM apartments involved, the per-stream sample counts and
// last timestamps, the sink writer's own statistics and the encoder transform's name and vendor, all in errors.log; pacing an offline hardware
// encoder so it is never far behind (as live recording feeds it).
// Does not own: the queue and pump (VideoEncoder, VideoEncoder.Writer) or the watchdogs (VideoEncoder.Finish, VideoEncoderFinisher).
// Tests: VideoExportCancelTests.
public sealed partial class VideoEncoder
{
    /// <summary>The steps of <see cref="FinishSequence"/> in order, as the notes name them ("finalize step 'name'").</summary>
    internal static readonly IReadOnlyList<string> FinishSteps = new[] { "input ended", "finalize writer", "finalized" };

    private long _videoSamples, _lastVideoTicks, _audioSamples, _audioEndTicks;   // written by the worker only

    private static string ThreadText() => VideoEncoderFinisher.ThreadText();

    /// <summary>The most frames an offline hardware encoder may fall behind the frames written (a live recording, paced by its clock, never is).</summary>
    private const int PaceFrames = 8;
    private const int PaceGiveUpMs = 2000;
    private long _pacedMs;
    private bool _paceOff;

    // Worker thread, offline hardware only: the sink writer does not throttle (as live), so frames written faster than real time would pile up
    // in it (hundreds of MB at 1080p, GB at 4K) and be encoded only while finalising. This waits until the encoder is at most PaceFrames behind,
    // which is how a live recording feeds it. An encoder that holds more frames before it outputs any is not waited for (pacing then stops).
    private void Pace(IMFSinkWriter writer, int video)
    {
        if (_paceOff) return;
        var st = new SinkWriterStatistics { Size = Marshal.SizeOf<SinkWriterStatistics>() };
        var encoded = ulong.MaxValue;
        var since = Environment.TickCount64;
        var start = since;
        while (!_abandoned.IsSet && writer.GetStatistics(video, ref st) >= 0 && st.Received > st.Encoded + PaceFrames)
        {
            var now = Environment.TickCount64;
            if (st.Encoded != encoded) { encoded = st.Encoded; since = now; }
            else if (now - since > PaceGiveUpMs)
            {
                _paceOff = true;
                Note($"pacing off: the encoder output nothing for {PaceGiveUpMs} ms with {st.Received - st.Encoded} frames in (received {st.Received}, encoded {st.Encoded})");
                break;
            }
            Thread.Sleep(1);
        }
        Interlocked.Add(ref _pacedMs, Environment.TickCount64 - start);
    }

    // Any thread: what the worker has written so far (plain reads of counters only the worker writes).
    private string Counts() =>
        $"video samples {Interlocked.Read(ref _videoSamples)} (last at {Interlocked.Read(ref _lastVideoTicks) / 10_000.0:0.#} ms), " +
        $"audio samples {Interlocked.Read(ref _audioSamples)} (end at {Interlocked.Read(ref _audioEndTicks) / 10_000.0:0.#} ms), queued {QueuedCount()}, paced {Interlocked.Read(ref _pacedMs)} ms, file {FileLength(_path)} B";

    private int QueuedCount()
    {
        try { return _queue.Count; }
        catch (ObjectDisposedException) { return 0; }   // swallow: a finished encoder has nothing queued
    }

    // Worker thread, after Pump returned: live and offline run exactly this.
    private void FinishSequence(IMFSinkWriter writer, int video, int audio)
    {
        var clock = Stopwatch.StartNew();
        Step(clock, FinishSteps[0], $"{Counts()}; sink writer video [{Statistics(writer, video)}], audio [{Statistics(writer, audio)}]");
        if (_abandoned.IsSet) { Note("finalize step skipped: the encoder was given up on (cancelled or stalled), the file is not finalised"); return; }
        Step(clock, FinishSteps[1], "calling IMFSinkWriter.Finalize on " + ThreadText());
        if (_options.SimulateFinalizeHang) _abandoned.Wait();
        else writer.FinalizeWriter();
        Step(clock, FinishSteps[2], $"finalize writer took {clock.ElapsedMilliseconds} ms, file {FileLength(_path)} B");
    }

    private void Step(Stopwatch clock, string name, string detail) => Note($"finalize step '{name}' at +{clock.ElapsedMilliseconds} ms: {detail}");

    // MF_SINK_WRITER_STATISTICS of one stream: what reached the writer, the encoder and the file.
    private static string Statistics(IMFSinkWriter writer, int stream)
    {
        if (stream < 0) return "none";
        try
        {
            var st = new SinkWriterStatistics { Size = Marshal.SizeOf<SinkWriterStatistics>() };
            var hr = writer.GetStatistics(stream, ref st);
            if (hr < 0) return $"no statistics (0x{hr:X8})";
            return $"received {st.Received} (last {st.LastTimeReceived / 10_000.0:0.#} ms), encoded {st.Encoded} (last {st.LastTimeEncoded / 10_000.0:0.#} ms), " +
                $"written {st.Processed} (last {st.LastTimeProcessed / 10_000.0:0.#} ms), queued {st.BytesQueued} B, sink requests {st.OutstandingRequests}";
        }
        catch (Exception e) when (e is COMException or InvalidCastException) { return "no statistics: " + e.Message; }
    }

    // The encoder transform's own attributes (name, vendor, driver URL) and the registered hardware encoders with their CLSIDs.
    private static string DescribeTransform(object transform)
    {
        var own = "attributes unavailable";
        if (transform is IMFTransformAttributes t && t.GetAttributes(out var attributes) >= 0 && attributes is not null)
        {
            try { own = $"name '{Mf.ReadString(attributes, Mf.FriendlyName)}' vendor '{Mf.ReadString(attributes, Mf.HardwareVendor)}' url '{Mf.ReadString(attributes, Mf.HardwareUrl)}' clsid {Mf.ReadGuid(attributes, Mf.TransformClsid)?.ToString("B") ?? "not given"}"; }
            finally { Marshal.ReleaseComObject(attributes); }
        }
        var registered = Mf.H264EncoderDetails(4);
        return own + "; registered hardware H.264 encoders: " + (registered.Count > 0 ? string.Join("; ", registered) : "none");
    }
}
