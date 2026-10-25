namespace TabForge.Playback;

// Owns: the hand-over of playback reports from the scheduler thread to the UI frame: the newest report wins, a one-shot finish flag, and the newest
//     timeline the scheduler swapped in.
// Does not own: what the frame does with the position (PlaybackFrameRules, the view controller).
// Tests: no dedicated test class.
// Threads: Report and MarkFinished run on the scheduler thread; TryTake, TakeFinished and Clear on the UI thread. One lock guards the slot.
//     Nothing allocates after construction: reports are copied by value into a held buffer, and TryTake hands out one UI-owned instance
//     that the next TryTake overwrites (the caller reads it within its frame).

/// <summary>The newest playback report waiting for the next rendered frame.</summary>
public sealed class PendingPlayheadSlot
{
    private readonly object _gate = new();
    private readonly PlaybackPosition _pending = new();
    private readonly PlaybackPosition _taken = new();
    private bool _hasPending;
    private bool _finished;
    private ScoreTimeline? _pendingTimeline;

    /// <summary>Keeps the newest timeline the scheduler swapped in, for the UI frame to take (a later one replaces it).</summary>
    public void ReportTimeline(ScoreTimeline timeline)
    {
        lock (_gate) _pendingTimeline = timeline;
    }

    /// <summary>Takes the newest revised timeline, if one arrived since the last take.</summary>
    public bool TakeTimeline(out ScoreTimeline? timeline)
    {
        lock (_gate)
        {
            timeline = _pendingTimeline;
            _pendingTimeline = null;
            return timeline is not null;
        }
    }

    /// <summary>Keeps a copy of <paramref name="position"/>; a later change to the caller's object does not reach the slot.</summary>
    public void Report(PlaybackPosition position)
    {
        lock (_gate)
        {
            CopyInto(position, _pending);
            _hasPending = true;
        }
    }

    /// <summary>Takes the newest report and empties the slot; the instance handed out is overwritten by the next take.</summary>
    public bool TryTake(out PlaybackPosition? position)
    {
        lock (_gate)
        {
            if (!_hasPending) { position = null; return false; }
            CopyInto(_pending, _taken);
            _hasPending = false;
            position = _taken;
            return true;
        }
    }

    public void MarkFinished()
    {
        lock (_gate) _finished = true;
    }

    /// <summary>True once per <see cref="MarkFinished"/>.</summary>
    public bool TakeFinished()
    {
        lock (_gate)
        {
            var finished = _finished;
            _finished = false;
            return finished;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _hasPending = false;
            _finished = false;
        }
    }

    private static void CopyInto(PlaybackPosition from, PlaybackPosition to)
    {
        to.Bar = from.Bar;
        to.Cell = from.Cell;
        to.BarFraction = from.BarFraction;
        to.ElapsedMs = from.ElapsedMs;
    }
}
