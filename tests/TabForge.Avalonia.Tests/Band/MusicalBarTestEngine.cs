using System;
using TabForge.Models;
using TabForge.Playback;

namespace TabForge.Avalonia.Tests;

/// <summary>A playback engine that seeks and reports musical bars (the song's own bar numbers).</summary>
internal sealed class MusicalBarTestEngine : IPlaybackEngine
{
    private ScoreTimeline? _timeline;
    private PlaybackPosition _moment = new();

    public bool IsPlaying { get; private set; }
    public bool IsPaused { get; private set; }

    public event Action<ScoreTimeline>? TimelineChanged;
    public event Action<ScoreTimeline>? TimelineRevised { add { } remove { } }

    public void Start(SongProject project, PlaybackOptions options, Action<PlaybackPosition> onPosition, Action onFinished, bool startPaused = false)
    {
        _timeline = MidiTimelineBuilder.Build(project, options);
        IsPlaying = true;
        IsPaused = startPaused;
        MoveTo(0);
        TimelineChanged?.Invoke(_timeline);
    }

    public void Stop() { IsPlaying = false; IsPaused = false; }
    public void Pause() => IsPaused = true;
    public void Resume() => IsPaused = false;
    public void Seek(SongProject project, int bar, int cell) => MoveTo(bar);
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

    private void MoveTo(int bar)
    {
        var bars = _timeline!.Bars;
        var span = bars[Math.Clamp(bar, 0, bars.Count - 1)];
        _moment = new PlaybackPosition { Bar = span.Bar, Cell = 0, BarFraction = 0, ElapsedMs = span.StartMs };
    }
}
