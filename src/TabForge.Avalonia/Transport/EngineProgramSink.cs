using System;
using TabForge.Documents;
using TabForge.Ports;
using TabForge.Services;

namespace TabForge.Avalonia.Transport;

// Owns: the instrument choice's sound side over one song's playback engine: the live program switch, its end (restore, or keep and bring the
//     running schedule and the engine's routes in line with the edited song) and the audition note of a stopped song.
// Does not own: the choice and its rules (InstrumentChoice), the engine's program override (PlaybackEngine), the sessions (AvaloniaPlaybackSessions).
// Tests: TabForge.Avalonia.Tests.InstrumentChoiceTests.
// Threads: UI thread only. Silent when the sessions have no engine.
internal sealed class EngineProgramSink : IProgramSink
{
    private readonly AvaloniaPlaybackSessions _playback;
    private readonly SongDocument _song;
    private readonly Func<PluginSettings> _plugins;

    public EngineProgramSink(AvaloniaPlaybackSessions playback, SongDocument song, Func<PluginSettings> plugins)
    {
        _playback = playback;
        _song = song;
        _plugins = plugins;
    }

    public bool IsPlaying => _playback.Existing(_song)?.IsPlaying ?? false;

    public void BeginPreview(int deviceId, int channel, int program) => _playback.For(_song)?.Engine.BeginProgramPreview(_song.Project, deviceId, channel, program);

    public void EndPreview(int deviceId, int channel, bool keep)
    {
        if (_playback.For(_song) is not { } session) return;
        // The override is dropped and the song's own program sent; a kept one is sent by the re-arm below, once.
        session.Engine.EndProgramPreview(_song.Project, deviceId, channel, resend: !keep);
        if (!keep) return;
        // The committed sound stays in force while the song's changed tracks are compiled into the running schedule.
        _playback.TracksChanged(_song, _plugins(), syncEngine: true);
        session.Engine.RearmChannelSetup();
    }

    public void Audition(int deviceId, int channel, int program, int note, int lengthMs) =>
        _ = _playback.PlayTestNoteAsync(deviceId, channel, program, note, lengthMs);
}
