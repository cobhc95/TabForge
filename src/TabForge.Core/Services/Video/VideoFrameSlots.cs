namespace TabForge.Services.Video;

// Owns: the constant-frame-rate slot maths of a live recording and the "capture cannot keep up" decision. Pure: the clock is passed in.
// Does not own: grabbing or encoding frames (LiveVideoSession), the notice (LiveVideoRecordController).
// Tests: LiveVideoRecordTests (VideoFrameSlotsTests).
/// <summary>Frame slot n = floor(audioMs * fps / 1000). A slot is taken once; a skipped slot is filled by repeating the last frame.</summary>
public sealed class VideoFrameSlots
{
    private readonly int _fps;
    private long _last = -1;

    public VideoFrameSlots(int fps) => _fps = fps;

    /// <summary>1 = every slot gets a fresh frame; 2 = every second slot (30 fps picture in a 60 fps file, after a fall-back).</summary>
    public int Stride { get; set; } = 1;

    public static long SlotOf(double audioMs, int fps) => (long)Math.Floor(audioMs * fps / 1000.0);

    /// <summary>True when <paramref name="audioMs"/> reaches a slot not taken yet: the frame goes to <paramref name="slot"/>, and
    /// <paramref name="firstFill"/>..slot-1 are the slots to fill with the previous frame (the very first frame also fills from slot 0).</summary>
    public bool TryNext(double audioMs, out long slot, out long firstFill)
    {
        slot = SlotOf(audioMs, _fps) / Stride * Stride;
        firstFill = _last + 1;
        return slot > _last;
    }

    public void Commit(long slot) => _last = slot;

    public bool HasFrame => _last >= 0;
}

/// <summary>Counts really captured frames per second; after 3 continuous seconds below 85% of the target it says "fall back", once.</summary>
public sealed class CaptureRateMonitor
{
    private const long WindowMs = 1000;
    private const int BadWindows = 3;
    private readonly int _target;
    private long _windowStart = -1;
    private int _count, _bad;
    private bool _fired;

    public CaptureRateMonitor(int targetFps) => _target = targetFps;

    public void OnFrame() => _count++;

    /// <summary>Call with the current time (ms) as often as convenient; true exactly once, when the rate has been too low for 3 seconds in a row.</summary>
    public bool Tick(long nowMs)
    {
        if (_fired) return false;
        if (_windowStart < 0) { _windowStart = nowMs; return false; }
        while (nowMs - _windowStart >= WindowMs)
        {
            _bad = _count < _target * 0.85 ? _bad + 1 : 0;
            _count = 0;
            _windowStart += WindowMs;
            if (_bad >= BadWindows) { _fired = true; return true; }
        }
        return false;
    }
}
