using TabForge.Models;

namespace TabForge.Playback;

// Owns: a playback engine that plays nothing and holds one made-up moment (bar and fraction of that bar) for the playback captures: it compiles the timeline at Start, raises it, and reports the moment as the audible position.
// Does not own: scheduling, audio or MIDI (the real engine), the views that draw the moment.
// Tests: TabForge.Avalonia.Tests.ScorePlayStateTests.
public sealed class PlayingStateStub(int bar, double fraction) : IPlaybackEngine
{
    private PlaybackPosition _moment = new();

    public bool IsPlaying { get; private set; }
    public bool IsPaused { get; private set; }

    public event Action<ScoreTimeline>? TimelineChanged;
    public event Action<ScoreTimeline>? TimelineRevised { add { } remove { } }

    public void Start(SongProject project, PlaybackOptions options, Action<PlaybackPosition> onPosition, Action onFinished, bool startPaused = false)
    {
        var timeline = MidiTimelineBuilder.Build(project, options);
        var span = timeline.Bars[Math.Clamp(bar, 0, timeline.Bars.Count - 1)];
        _moment = new PlaybackPosition { Bar = span.Bar, Cell = (int)(fraction * span.Slots), BarFraction = fraction, ElapsedMs = span.StartMs + fraction * (span.EndMs - span.StartMs) };
        IsPlaying = true;
        IsPaused = startPaused;
        TimelineChanged?.Invoke(timeline);
    }

    public void Stop() { IsPlaying = false; IsPaused = false; }
    public void Pause() => IsPaused = true;
    public void Resume() => IsPaused = false;
    public void Seek(SongProject project, int toBar, int toCell) { }
    public void SetLoop(bool loop) { }
    public PlaybackPosition Playhead() => _moment;

    public PlaybackPosition AudiblePlayhead(PlaybackPosition frame)
    {
        frame.Bar = _moment.Bar;
        frame.Cell = _moment.Cell;
        frame.BarFraction = _moment.BarFraction;
        frame.ElapsedMs = _moment.ElapsedMs;
        return frame;
    }

    public void Dispose() { }
}
