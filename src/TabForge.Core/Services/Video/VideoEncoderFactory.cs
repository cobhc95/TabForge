using TabForge.Platform;

namespace TabForge.Services.Video;

// Owns: choosing the MP4 encoder for this OS and setting: Media Foundation (Windows, today's behaviour) or an external ffmpeg; the live-recording availability per OS.
// Does not own: either encoder (VideoEncoder, FfmpegVideoEncoder), finding ffmpeg (FfmpegLocator) or the stored choice (LiveVideoSettings).
// Tests: VideoEncoderPortTests.
public enum VideoEncoderKind { MediaFoundation, Ffmpeg, None }

public static class VideoEncoderFactory
{
    /// <summary>Which encoder serves a choice: Windows keeps Media Foundation unless FFmpeg was chosen and is installed; elsewhere ffmpeg is the only encoder.</summary>
    public static VideoEncoderKind Choose(string? encoderChoice, HostOs os, bool ffmpegFound) =>
        os == HostOs.Windows ? (VideoEncoderChoices.Normalize(encoderChoice) == VideoEncoderChoices.FFmpeg && ffmpegFound ? VideoEncoderKind.Ffmpeg : VideoEncoderKind.MediaFoundation)
        : ffmpegFound ? VideoEncoderKind.Ffmpeg : VideoEncoderKind.None;

    /// <summary>Opens the encoder for this machine. Throws <see cref="VideoEncoderException"/> with a message the UI can show when none is usable.</summary>
    public static IVideoEncoder Open(string path, int width, int height, int fps, VideoAudioFormat? audioFormat = null, bool live = false, VideoEncoderOptions? options = null)
    {
        options ??= new VideoEncoderOptions();
        var ffmpeg = FfmpegLocator.Find();
        switch (Choose(options.Encoder, HostSystem.Current, ffmpeg is not null))
        {
            case VideoEncoderKind.Ffmpeg:
                return FfmpegVideoEncoder.Open(ffmpeg!, path, width, height, fps, audioFormat, live, options);
            case VideoEncoderKind.MediaFoundation:
#if WINDOWS
                return VideoEncoder.Open(path, width, height, fps, audioFormat, live, options);
#else
                break;
#endif
        }
        throw new VideoEncoderException(NoEncoderText);
    }

    /// <summary>
    /// The hardware self-test rule live recording and export share: on Media Foundation with Auto and no stored result, the hardware encoder is
    /// tested once (a short paced file at <paramref name="probePath"/>, removed after) and the verdict goes to the options and HardwareResult.
    /// Slow (up to a few seconds): call it off the UI thread.
    /// </summary>
    public static VideoEncoderOptions WithHardwareProbe(VideoEncoderOptions options, string probePath, int width, int height, int fps)
    {
#if WINDOWS
        if (options is { Encoder: VideoEncoderChoices.Auto, HardwareProbe: "" } && Choose(options.Encoder, HostSystem.Current, false) == VideoEncoderKind.MediaFoundation && Mf.HardwareH264Available())
            return options with { HardwareProbe = VideoEncoder.ProbeHardware(probePath, width, height, fps, options) ? VideoEncoderOptions.ProbePass : VideoEncoderOptions.ProbeFail };
#endif
        return options;
    }

    /// <summary>
    /// The encoder an export uses, by the rule live recording follows: Auto takes the hardware encoder only once it passed the self-test
    /// (and was not seen to hang since); otherwise the software encoder. An explicit choice, and ffmpeg's own Auto elsewhere, are kept.
    /// </summary>
    public static VideoEncoderOptions ForExport(VideoEncoderOptions options, HostOs os) =>
        os == HostOs.Windows && options.Encoder == VideoEncoderChoices.Auto && options.HardwareProbe != VideoEncoderOptions.ProbePass ? options with { Encoder = VideoEncoderChoices.Software } : options;

    /// <summary>The "install ffmpeg" message with an install command for this OS (apt on Linux, brew elsewhere).</summary>
    public static string NoEncoderText => $"Video export needs ffmpeg on this system. Install ffmpeg (for example '{(HostSystem.Current == HostOs.Linux ? "sudo apt install ffmpeg" : "brew install ffmpeg")}' or your package manager) or put the ffmpeg program next to TabForge, then try again.";

    public const string LiveRecordingUnavailableText = "Recording the screen to video is available on Windows only. Use Export video to make a video from the score instead.";

    /// <summary>Whether the live Record video button works here: it needs the Windows screen capture.</summary>
    public static bool LiveRecordingSupported(HostOs os) => os == HostOs.Windows;
}
