using System;
using System.IO;
using TabForge.Services.Video;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the video frame size rule: a frame that is not exactly width x height x 4 bytes is refused, never silently
// truncated or copied past the native buffer.
// Does not own: encoding itself (VideoExportCancelTests, LiveVideoRecordTests).
public sealed class VideoEncoderFrameSizeTests
{
    [Fact]
    public void FramesThatAreNotExactlyWidthByHeightBy4AreRefused()
    {
        if (!OperatingSystem.IsWindows()) return;   // Media Foundation is Windows-only
        var path = Path.Combine(Path.GetTempPath(), "tf-frame-size-" + Guid.NewGuid().ToString("N") + ".mp4");
        try
        {
            using var encoder = VideoEncoder.Open(path, 1920, 1080, 30, new VideoAudioFormat(48000, 2), live: true,
                options: new VideoEncoderOptions(VideoEncoderChoices.Software));
            var size = 1920 * 1080 * 4;
            Assert.Throws<ArgumentException>(() => { encoder.WriteFrame(new byte[size - 1], TimeSpan.Zero); });
            Assert.Throws<ArgumentException>(() => { encoder.WriteFrame(new byte[size + 1], TimeSpan.Zero); });
            Assert.True(encoder.WriteFrame(new byte[size], TimeSpan.Zero));
            encoder.Finish();
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
