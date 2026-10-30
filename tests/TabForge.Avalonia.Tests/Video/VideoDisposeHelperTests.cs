using TabForge.Services.Video;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the shared swallow-finish the video encoders' Dispose runs (a finished encoder's Finish throws; Dispose must not).
// Does not own: finishing itself (VideoExportCancelTests, LiveVideoRecordTests).
// Tests: this file.
public sealed class VideoDisposeHelperTests
{
    [Fact]
    public void ASuccessfulFinishRunsAndAThrownFinishedErrorIsSwallowed()
    {
        var ran = 0;
        VideoDisposeHelper.TryFinish(() => ran++);
        Assert.Equal(1, ran);
        VideoDisposeHelper.TryFinish(() => throw new VideoEncoderException("already finished"));
    }
}
