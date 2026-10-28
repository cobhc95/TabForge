namespace TabForge.Services.Video;

// Owns: the pure rules of the record-video focus: whether the main window must be restored and brought forward before a recording
//   (or when a recording is found black), and the reason text for a black picture.
// Does not own: the window (AvaloniaLiveVideoHost), the pixels (AvaloniaWindowFrameCapture) or the notices (LiveVideoRecordController).
// Tests: LiveVideoRecordTests.
public static class LiveVideoFocus
{
    /// <summary>True when the window must be restored or activated: it is minimised, hidden or not the foreground window.</summary>
    public static bool NeedsBringingForward(bool minimised, bool inFront, bool shown) => minimised || !inFront || !shown;

    /// <summary>The reason a recording came out black, from the window's state at that time.</summary>
    public static string BlackReason(bool minimised, bool inFront, bool shown)
    {
        if (minimised) return "the window was minimised";
        if (!shown) return "the window was hidden";
        if (!inFront) return "another window had the focus";
        return "the window was not painted";
    }
}
