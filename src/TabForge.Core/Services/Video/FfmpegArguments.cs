using System.Globalization;
using TabForge.Platform;

namespace TabForge.Services.Video;

// Owns: the ffmpeg command lines (raw frames in on stdin, then a stream-copy mux with the spooled audio) and the choice of H.264 encoder from ffmpeg's own encoder list.
// Does not own: starting the process (FfmpegVideoEncoder) or finding it (FfmpegLocator).
// Tests: VideoEncoderPortTests.
/// <summary>Builds ffmpeg argument lists. Each entry is one argument, so nothing needs quoting.</summary>
public static class FfmpegArguments
{
    public const string AudioBitrate = "192k";

    private static string[] SoftwareEncoders => new[] { "libx264", "libopenh264" };

    /// <summary>The arguments that list ffmpeg's encoders.</summary>
    public static IReadOnlyList<string> ListEncoders() => new[] { "-hide_banner", "-encoders" };

    /// <summary>The H.264 encoders worth trying, best first, for a choice and OS; only encoders named in <paramref name="encoderListing"/> (the output of <see cref="ListEncoders"/>) are returned.</summary>
    public static IReadOnlyList<string> Candidates(string encoderListing, string choice, HostOs os)
    {
        var present = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in encoderListing.Split('\n'))
        {
            var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts[0].Length == 6 && parts[0][0] == 'V') present.Add(parts[1]);
        }
        var hardware = HardwareEncoders(os);
        IEnumerable<string> order = choice switch
        {
            VideoEncoderChoices.Software => SoftwareEncoders,
            VideoEncoderChoices.Hardware => hardware,
            _ when os == HostOs.MacOS => hardware.Concat(SoftwareEncoders),
            _ => SoftwareEncoders.Concat(hardware),
        };
        return order.Where(present.Contains).ToArray();
    }

    /// <summary>The hardware H.264 encoders ffmpeg offers on an OS.</summary>
    public static IReadOnlyList<string> HardwareEncoders(HostOs os) => os switch
    {
        HostOs.MacOS => new[] { "h264_videotoolbox" },
        HostOs.Windows => new[] { "h264_nvenc", "h264_qsv", "h264_amf", "h264_mf" },
        _ => new[] { "h264_nvenc", "h264_qsv" },
    };

    /// <summary>Video in as raw top-down BGRA frames on stdin, out as an H.264 MP4 file (no audio).</summary>
    public static IReadOnlyList<string> Video(int width, int height, int fps, string encoder, string outputPath)
    {
        var args = new List<string>
        {
            "-hide_banner", "-loglevel", "error", "-y",
            "-f", "rawvideo", "-pix_fmt", "bgra", "-video_size", $"{width}x{height}", "-framerate", fps.ToString(CultureInfo.InvariantCulture), "-i", "pipe:0",
            "-an", "-c:v", encoder, "-b:v", VideoEncoderRules.VideoBitrate(width, height, fps).ToString(CultureInfo.InvariantCulture), "-pix_fmt", "yuv420p",
        };
        if (encoder == "libx264") args.AddRange(new[] { "-preset", "veryfast" });
        args.AddRange(new[] { "-movflags", "+faststart", "-f", "mp4", outputPath });
        return args;
    }

    /// <summary>The finished video file plus raw little-endian float audio, muxed with the video copied and the audio encoded as AAC.</summary>
    public static IReadOnlyList<string> Mux(string videoPath, string rawAudioPath, VideoAudioFormat audio, string outputPath) => new[]
    {
        "-hide_banner", "-loglevel", "error", "-y",
        "-i", videoPath,
        "-f", "f32le", "-ar", audio.SampleRate.ToString(CultureInfo.InvariantCulture), "-ac", audio.Channels.ToString(CultureInfo.InvariantCulture), "-i", rawAudioPath,
        "-map", "0:v:0", "-map", "1:a:0", "-c:v", "copy", "-c:a", "aac", "-b:a", AudioBitrate, "-shortest",
        "-movflags", "+faststart", "-f", "mp4", outputPath,
    };
}
