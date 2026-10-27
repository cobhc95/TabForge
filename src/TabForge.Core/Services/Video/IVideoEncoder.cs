namespace TabForge.Services.Video;

/// <summary>Audio layout of the encoded track: interleaved float samples.</summary>
public readonly record struct VideoAudioFormat(int SampleRate, int Channels);

/// <summary>An encoder failure with a message the UI can show as it is.</summary>
public sealed class VideoEncoderException : Exception
{
    public VideoEncoderException(string message, Exception? inner = null) : base(message, inner) { }

    /// <summary>Set when the encoder was given up on while saving: the unfinished file at this path was kept, not deleted.</summary>
    public string? KeptFile { get; init; }

    /// <summary>Set when a hardware encoder stopped making progress (frames or finishing): the export redoes the video with the software encoder.</summary>
    public bool HardwareStalled { get; init; }
}

// Owns: the port every MP4 encoder implements (Media Foundation on Windows, an ffmpeg child process anywhere) and the size, rate and bitrate rules they share.
// Does not own: choosing between them (VideoEncoderFactory) or writing the file (VideoEncoder, FfmpegVideoEncoder).
// Tests: VideoEncoderPortTests, LiveVideoRecordTests.
/// <summary>Writes top-down BGRA frames and interleaved float audio to an H.264/AAC MP4. Live mode never blocks the caller: a full queue drops the frame and counts it.</summary>
public interface IVideoEncoder : IDisposable
{
    /// <summary>Frames dropped because the queue was full (live mode).</summary>
    long DroppedFrames { get; }

    /// <summary>True when the video encoder in use is a hardware encoder.</summary>
    bool UsesHardware { get; }

    /// <summary>The encoder in use, for logs and notices.</summary>
    string EncoderName { get; }

    /// <summary>What the encoder did that the caller may want to see.</summary>
    IReadOnlyList<string> Notes { get; }

    /// <summary>Queues one top-down BGRA frame (width x height x 4 bytes). Live mode returns false when the frame was dropped.</summary>
    bool WriteFrame(ReadOnlySpan<byte> bgra, TimeSpan timestamp);

    /// <summary>Offline mode: waits (without blocking the caller) until <see cref="WriteFrame"/> would not wait; false after <paramref name="timeout"/>. Live mode and encoders without a bounded queue: true at once.</summary>
    ValueTask<bool> WaitForRoomAsync(TimeSpan timeout, CancellationToken cancel) => ValueTask.FromResult(true);

    /// <summary>Queues interleaved float samples (-1..1) in the format given at open. Audio is never dropped.</summary>
    void WriteAudio(ReadOnlySpan<float> samples);

    /// <summary>A number that changes whenever the encoder makes progress (a frame written, the file growing while it is finalised); a watchdog fails an encoder only when it stays the same for long.</summary>
    long Heartbeat { get; }

    /// <summary>Writes what is queued and finalises the MP4 (can take long at 4K: call it off the UI thread). Throws when encoding failed.</summary>
    void Finish();

    /// <summary>Gives up on a stuck encoder (any thread); the unfinished file is kept as it is.</summary>
    void Abort(string why);
}

/// <summary>The sizes, frame rates, sample rates and bitrate every encoder accepts.</summary>
public static class VideoEncoderRules
{
    private static bool IsSize(int w, int h) => (w, h) is (1920, 1080) or (3840, 2160) or (1080, 1920) or (2160, 3840);   // landscape and portrait

    /// <summary>Video bits per second for a size and frame rate (about 0.1 bit per pixel, capped at 100 Mbit/s).</summary>
    public static int VideoBitrate(int width, int height, int fps) => (int)Math.Min(100_000_000.0, width * (double)height * fps * 0.1);

    /// <summary>True for the sample rates the MP4 audio stream takes (44.1 and 48 kHz).</summary>
    public static bool SupportsSampleRate(int rate) => rate is 44100 or 48000;

    /// <summary>The length of a file an encoder writes, 0 when it cannot be read (a heartbeat source: growth means progress).</summary>
    internal static long FileLength(string path)
    {
        try { return new System.IO.FileInfo(path).Length; }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException) { return 0; }   // swallow: an unreadable file just shows no growth
    }

    /// <summary>Throws <see cref="VideoEncoderException"/> when the size, frame rate or audio layout is not one the encoders take.</summary>
    public static void Validate(int width, int height, int fps, VideoAudioFormat? audioFormat)
    {
        if (!IsSize(width, height)) throw new VideoEncoderException($"Video size {width}x{height} is not supported (use 1080p or 4K, landscape or portrait).");
        if (fps is not (30 or 60 or 120)) throw new VideoEncoderException($"{fps} frames per second is not supported (use 30, 60 or 120).");
        if (audioFormat is { } a && (a.Channels is < 1 or > 2 || !SupportsSampleRate(a.SampleRate))) throw new VideoEncoderException("The audio must be 44.1 or 48 kHz, mono or stereo.");
    }
}
