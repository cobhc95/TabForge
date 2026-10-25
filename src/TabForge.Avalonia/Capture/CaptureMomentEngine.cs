using System;
using TabForge.Models;
using TabForge.Playback;

namespace TabForge.Avalonia.Capture;

// Owns: a playback engine that plays nothing and holds a moment the script chooses (a performed bar and a fraction of it), so a capture shows the playing views at any point of the song, repeats included.
// Does not own: scheduling, audio or MIDI (the real engine), the views that draw the moment.
// Tests: TabForge.Avalonia.Tests.CaptureMomentEngineTests.
internal sealed class CaptureMomentEngine : IPlaybackEngine
{
    private PlaybackPosition _moment = new();
    private ScoreTimeline? _timeline;

    public bool IsPlaying { get; private set; }
    public bool IsPaused { get; private set; }

    public event Action<ScoreTimeline>? TimelineChanged;
    public event Action<ScoreTimeline>? TimelineRevised { add { } remove { } }

    /// <summary>The performed bars of the compiled timeline (repeats expanded); 0 before the first start.</summary>
    public int PerformedBars => _timeline?.Bars.Count ?? 0;

    public void Start(SongProject project, PlaybackOptions options, Action<PlaybackPosition> onPosition, Action onFinished, bool startPaused = false)
    {
        _timeline = MidiTimelineBuilder.Build(project, options);
        IsPlaying = true;
        IsPaused = startPaused;
        MoveTo(0, 0);
        TimelineChanged?.Invoke(_timeline);
    }

    /// <summary>Holds the moment at the <paramref name="performed"/>-th performed bar (0-based, repeats counted) and <paramref name="fraction"/> of it.</summary>
    public void MoveTo(int performed, double fraction)
    {
        var timeline = _timeline ?? throw new InvalidOperationException("The engine has not started.");
        var span = timeline.Bars[Math.Clamp(performed, 0, timeline.Bars.Count - 1)];
        _moment = new PlaybackPosition { Bar = span.Bar, Cell = (int)(fraction * span.Slots), BarFraction = fraction, ElapsedMs = span.StartMs + fraction * (span.EndMs - span.StartMs) };
    }

    public void Stop() { IsPlaying = false; IsPaused = false; }
    public void Pause() => IsPaused = true;
    public void Resume() => IsPaused = false;
    public void Seek(SongProject project, int bar, int cell) { }
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
