namespace TabForge.Services.Video;

// Owns: the one swallow-finish shared by the video encoders' Dispose (finishing an already-finished encoder throws; Dispose must not).
// Does not own: finishing itself (each encoder's Finish).
// Tests: VideoExportCancelTests, LiveVideoRecordTests.
internal static class VideoDisposeHelper
{
    /// <summary>Runs a video encoder's Finish for Dispose: a finished encoder throws, which Dispose swallows.</summary>
    public static void TryFinish(System.Action finish)
    {
        try { finish(); } catch (VideoEncoderException) { } // swallow: finishing an already-finished encoder is a no-op
    }
}
