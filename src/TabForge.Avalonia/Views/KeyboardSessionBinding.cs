using System;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Documents;
using TabForge.KeyboardMode;
using TabForge.Models;

namespace TabForge.Avalonia.Views;

// Owns: driving one keyboard strip from the active playback session: the song position (SessionKeyboardSongPosition), the notes of the chosen track
//   once the song plays, the progress, and a repaint on every live frame. With a Learn engine, the strip also gets the run (judge, score, feedback,
//   wait-gating, loop) and the bar drives the engine; without one, only the notes and progress show (captures and the legacy path).
// Does not own: the strip's drawing, the session, the track choice (the owner passes it), the engine or the bar.
// Tests: TabForge.Avalonia.Tests.KeyboardLearnEngineTests, TabForge.Avalonia.Tests.ScorePlayStateTests.
public sealed class KeyboardSessionBinding : IDisposable
{
    private readonly KeyboardStripView _strip;
    private readonly AvaloniaPlaybackSession _session;
    private readonly Func<SongProject> _project;
    private readonly Func<int> _track;
    private readonly KeyboardLearnEngine? _engine;
    private readonly KeyboardModeBar? _bar;
    private object? _timelineOfSource;

    public KeyboardSessionBinding(KeyboardStripView strip, AvaloniaPlaybackSession session, Func<SongProject> project, Func<int> track)
    {
        _strip = strip;
        _session = session;
        _project = project;
        _track = track;
        strip.Position = new SessionKeyboardSongPosition(session);
        session.PlayheadFrame += Follow;
        session.StateChanged += Follow;
        Follow();
    }

    /// <summary>Drives <paramref name="strip"/> and <paramref name="bar"/> through <paramref name="engine"/> (both bars of a window share it, so the pane and the pop-out stay in sync).</summary>
    public KeyboardSessionBinding(KeyboardStripView strip, KeyboardModeBar bar, AvaloniaPlaybackSession session, Func<SongDocument> document, KeyboardLearnEngine engine)
    {
        _strip = strip;
        _bar = bar;
        _session = session;
        _engine = engine;
        _project = () => document().Project;
        _track = () => Math.Max(0, document().TrackIndex);
        bar.Changed += OnBarChanged;
        strip.Position = new SessionKeyboardSongPosition(session);
        session.PlayheadFrame += Follow;
        session.StateChanged += Follow;
        Follow();
    }

    private void OnBarChanged()
    {
        if (_engine is { } engine && _bar is { } bar) engine.ApplyBar(bar);
    }

    // The notes are rebuilt when the timeline changed (a new play); every call moves the progress and repaints.
    private void Follow()
    {
        if (_engine is { } engine && _bar is { } bar)
        {
            engine.Follow(_strip, bar);
            return;
        }
        var transport = _session.Transport;
        var timeline = transport.Timeline;
        if (timeline is not null && !ReferenceEquals(timeline, _timelineOfSource))
        {
            _timelineOfSource = timeline;
            var project = _project();
            var track = Math.Clamp(_track(), 0, project.Tracks.Count - 1);
            _strip.Source = KeyboardNoteSource.Build(timeline, project, track);
        }
        _strip.Progress = timeline is { TotalMs: > 0 } ? Math.Clamp(transport.PlayheadMs / timeline.TotalMs, 0, 1) : 0;
        _strip.InvalidateVisual();
    }

    public void Dispose()
    {
        _session.PlayheadFrame -= Follow;
        _session.StateChanged -= Follow;
        if (_bar is not null) _bar.Changed -= OnBarChanged;
    }
}
