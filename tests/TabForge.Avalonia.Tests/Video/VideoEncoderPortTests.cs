using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TabForge.Platform;
using TabForge.Services.Video;
using Xunit;
using Xunit.Abstractions;

namespace TabForge.Avalonia.Tests;

// Owns: cross-platform Stage 5 video: the IVideoEncoder choice per OS (Media Foundation stays the Windows encoder), the ffmpeg lookup, the ffmpeg argument lists and
//   encoder choice, and a real ffmpeg pipe smoke test that runs only when an ffmpeg is installed on the machine.
// Does not own: the Media Foundation encoder itself (LiveVideoRecordTests, SelfTestVideoGolden).
// Tests: itself.
public sealed class VideoEncoderPortTests
{
    private readonly ITestOutputHelper _output;

    public VideoEncoderPortTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData("Auto", HostOs.Windows, true, VideoEncoderKind.MediaFoundation)]
    [InlineData("Software", HostOs.Windows, true, VideoEncoderKind.MediaFoundation)]
    [InlineData("Hardware", HostOs.Windows, false, VideoEncoderKind.MediaFoundation)]
    [InlineData("FFmpeg", HostOs.Windows, true, VideoEncoderKind.Ffmpeg)]
    [InlineData("FFmpeg (found at C:\\tools\\ffmpeg.exe)", HostOs.Windows, true, VideoEncoderKind.Ffmpeg)]
    [InlineData("FFmpeg", HostOs.Windows, false, VideoEncoderKind.MediaFoundation)]
    [InlineData("Auto", HostOs.Linux, true, VideoEncoderKind.Ffmpeg)]
    [InlineData("Auto", HostOs.MacOS, true, VideoEncoderKind.Ffmpeg)]
    [InlineData("Auto", HostOs.Linux, false, VideoEncoderKind.None)]
    [InlineData("Auto", HostOs.MacOS, false, VideoEncoderKind.None)]
    public void TheEncoderFollowsTheOsAndTheChoice(string choice, HostOs os, bool ffmpegFound, VideoEncoderKind expected) =>
        Assert.Equal(expected, VideoEncoderFactory.Choose(choice, os, ffmpegFound));

    [Fact]
    public void LiveRecordingIsWindowsOnly()
    {
        Assert.True(VideoEncoderFactory.LiveRecordingSupported(HostOs.Windows));
        Assert.False(VideoEncoderFactory.LiveRecordingSupported(HostOs.Linux));
        Assert.False(VideoEncoderFactory.LiveRecordingSupported(HostOs.MacOS));
        Assert.Contains("Windows only", VideoEncoderFactory.LiveRecordingUnavailableText);
    }

    [Fact]
    public void WindowsOpensTheMediaFoundationAdapter()
    {
        if (!OperatingSystem.IsWindows()) { _output.WriteLine("not available: the Media Foundation adapter exists on Windows only"); return; }
        var path = Path.Combine(Path.GetTempPath(), "tf-port-" + Guid.NewGuid().ToString("N") + ".mp4");
        try
        {
            using var encoder = VideoEncoderFactory.Open(path, 1920, 1080, 30, null, live: false, options: new VideoEncoderOptions(VideoEncoderChoices.Software));
            Assert.IsType<VideoEncoder>(encoder);
            Assert.IsAssignableFrom<IVideoEncoder>(encoder);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void TheDefaultChoiceListIsUnchangedWithoutFfmpeg()
    {
        Assert.Equal(new[] { "Auto", "Software", "Hardware" }, VideoEncoderChoices.All);
        Assert.Equal("FFmpeg", VideoEncoderChoices.Normalize("FFmpeg (found at /usr/bin/ffmpeg)"));
        Assert.Equal("Auto", VideoEncoderChoices.Normalize("nonsense"));
        var offered = VideoEncoderChoices.Available();
        if (FfmpegLocator.Find() is null || !OperatingSystem.IsWindows()) Assert.Equal(VideoEncoderChoices.All, offered);
        else Assert.StartsWith("FFmpeg (found at ", offered[^1]);
    }

    [Fact]
    public void FfmpegIsFoundByVariableThenAppFolderThenPath()
    {
        var existing = new HashSet<string> { Path.Combine("/opt/tf", "ffmpeg"), Path.Combine("/usr/bin", "ffmpeg"), "/custom/ff" };
        Assert.Equal("/custom/ff", FfmpegLocator.FindIn("/custom/ff", "/opt/tf", "/usr/bin", false, existing.Contains));
        Assert.Equal(Path.Combine("/opt/tf", "ffmpeg"), FfmpegLocator.FindIn(null, "/opt/tf", "/usr/bin", false, existing.Contains));
        Assert.Equal(Path.Combine("/usr/bin", "ffmpeg"), FfmpegLocator.FindIn("/missing", "/app", "/bin" + Path.PathSeparator + "/usr/bin", false, existing.Contains));
        Assert.Null(FfmpegLocator.FindIn(null, "/app", "/bin", false, existing.Contains));
        Assert.Equal(Path.Combine("/app", "ffmpeg.exe"), FfmpegLocator.FindIn(null, "/app", null, true, p => p == Path.Combine("/app", "ffmpeg.exe")));
    }

    [Fact]
    public void TheVideoArgumentsPipeRawBgraIntoH264Mp4()
    {
        var args = FfmpegArguments.Video(1920, 1080, 60, "libx264", "out.mp4").ToList();
        Assert.Equal("pipe:0", args[args.IndexOf("-i") + 1]);
        Assert.Equal("rawvideo", args[args.IndexOf("-f") + 1]);
        Assert.Equal("bgra", args[args.IndexOf("-pix_fmt") + 1]);
        Assert.Equal("1920x1080", args[args.IndexOf("-video_size") + 1]);
        Assert.Equal("60", args[args.IndexOf("-framerate") + 1]);
        Assert.Equal("libx264", args[args.IndexOf("-c:v") + 1]);
        Assert.Equal(VideoEncoderRules.VideoBitrate(1920, 1080, 60).ToString(), args[args.IndexOf("-b:v") + 1]);
        Assert.Contains("-preset", args);
        Assert.Equal("out.mp4", args[^1]);
        Assert.DoesNotContain("-preset", FfmpegArguments.Video(1920, 1080, 30, "h264_videotoolbox", "o.mp4"));
    }

    [Fact]
    public void ThePathWithSpacesStaysOneArgument()
    {
        var args = FfmpegArguments.Mux("my video.part", "my audio.part", new VideoAudioFormat(48000, 2), "C:\\My Videos\\out file.mp4");
        Assert.Equal("my video.part", args[args.ToList().IndexOf("-i") + 1]);
        Assert.Equal("C:\\My Videos\\out file.mp4", args[^1]);
    }

    [Fact]
    public void TheMuxCopiesVideoAndEncodesAacFromRawFloats()
    {
        var args = FfmpegArguments.Mux("v.part", "a.part", new VideoAudioFormat(44100, 2), "out.mp4").ToList();
        Assert.Equal("copy", args[args.IndexOf("-c:v") + 1]);
        Assert.Equal("aac", args[args.IndexOf("-c:a") + 1]);
        Assert.Equal("f32le", args[args.IndexOf("-f") + 1]);
        Assert.Equal("44100", args[args.IndexOf("-ar") + 1]);
        Assert.Equal("2", args[args.IndexOf("-ac") + 1]);
        Assert.Contains("-shortest", args);
    }

    private static readonly string Listing = string.Join("\n",
        "Encoders:",
        " V..... = Video",
        " ------",
        " V....D libx264              libx264 H.264 / AVC (codec h264)",
        " V....D libopenh264          OpenH264 H.264 / AVC (codec h264)",
        " V....D h264_nvenc           NVIDIA NVENC H.264 encoder (codec h264)",
        " V....D h264_videotoolbox    VideoToolbox H.264 Encoder (codec h264)",
        " A....D aac                  AAC (Advanced Audio Coding)");

    [Theory]
    [InlineData("Auto", HostOs.Linux, "libx264,libopenh264,h264_nvenc")]
    [InlineData("Auto", HostOs.Windows, "libx264,libopenh264,h264_nvenc")]
    [InlineData("Auto", HostOs.MacOS, "h264_videotoolbox,libx264,libopenh264")]
    [InlineData("Software", HostOs.MacOS, "libx264,libopenh264")]
    [InlineData("Hardware", HostOs.Linux, "h264_nvenc")]
    [InlineData("Hardware", HostOs.MacOS, "h264_videotoolbox")]
    public void TheH264EncoderIsChosenFromTheFfmpegEncoderList(string choice, HostOs os, string expected) =>
        Assert.Equal(expected, string.Join(",", FfmpegArguments.Candidates(Listing, choice, os)));

    [Fact]
    public void AnFfmpegWithoutH264HasNoCandidates() =>
        Assert.Empty(FfmpegArguments.Candidates(" A....D aac  AAC\n V....D mpeg4  MPEG-4 part 2", "Auto", HostOs.Linux));

    [Fact]
    public void TheSharedRulesRefuseWhatTheEncodersCannotWrite()
    {
        Assert.Throws<VideoEncoderException>(() => VideoEncoderRules.Validate(1000, 1000, 30, null));
        Assert.Throws<VideoEncoderException>(() => VideoEncoderRules.Validate(1920, 1080, 25, null));
        Assert.Throws<VideoEncoderException>(() => VideoEncoderRules.Validate(1920, 1080, 30, new VideoAudioFormat(22050, 2)));
        VideoEncoderRules.Validate(3840, 2160, 120, new VideoAudioFormat(48000, 1));
    }

    [Fact]
    public void FfmpegPipeSmokeTestWritesAnMp4WhenFfmpegIsInstalled()
    {
        var exe = FfmpegLocator.Find();
        if (exe is null) { _output.WriteLine("not available: no ffmpeg on PATH, next to the app or in TABFORGE_FFMPEG; the pipe test did not run"); return; }
        var path = Path.Combine(Path.GetTempPath(), "tf-ffmpeg-" + Guid.NewGuid().ToString("N") + ".mp4");
        try
        {
            FfmpegVideoEncoder encoder;
            try { encoder = FfmpegVideoEncoder.Open(exe, path, 1920, 1080, 30, new VideoAudioFormat(48000, 2)); }
            catch (VideoEncoderException e) { _output.WriteLine("not available: this ffmpeg has no usable H.264 encoder: " + e.Message); return; }
            using (encoder)
            {
                var frame = new byte[1920 * 1080 * 4];
                var audio = new float[1600 * 2];
                for (var i = 0; i < 30; i++)
                {
                    for (var p = 0; p < frame.Length; p += 4) { frame[p] = (byte)(i * 8); frame[p + 1] = (byte)(p >> 12); frame[p + 2] = 90; frame[p + 3] = 255; }
                    Assert.True(encoder.WriteFrame(frame, TimeSpan.FromTicks(i * 10_000_000L / 30)));
                    for (var s = 0; s < audio.Length; s++) audio[s] = MathF.Sin(s * 0.05f) * 0.3f;
                    encoder.WriteAudio(audio);
                }
                encoder.Finish();
                _output.WriteLine("encoder: " + encoder.EncoderName);
            }
            var bytes = File.ReadAllBytes(path);
            Assert.True(bytes.Length > 1000, "the MP4 is empty");
            Assert.Equal("ftyp", System.Text.Encoding.ASCII.GetString(bytes, 4, 4));
            Assert.False(File.Exists(path + ".audio.part") || File.Exists(path + ".video.part"), "a spool file was left behind");
        }
        finally
        {
            foreach (var f in new[] { path, path + ".audio.part", path + ".video.part" }) if (File.Exists(f)) File.Delete(f);
        }
    }
}
