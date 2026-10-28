using System;
using TabForge.Avalonia.Transport;
using TabForge.Services;
using TabForge.Views.Score;

namespace TabForge.Avalonia.Platform;

// Owns: the sound of a note the score or the fretboard enters or changes: a short audition through the playback engine's test-note path,
//     when the Preview note sound setting is on, and the note's length from the cell (the setting's length when the cell gives none).
// Does not own: the note rules and the events (ScorePreviewEditor.NotePreviewed), the Tools menu row (MainWindowToolsMenu), the engine (AvaloniaPlaybackSessions).
// Tests: TabForge.Avalonia.Tests.FretboardNoteAuditionTests.
// Threads: UI thread only; the audition is fire-and-forget and reports its own failures through the editor's notice line.

/// <summary>Auditions each note the score editor reports, the way the editor does on a typed or clicked note.</summary>
internal sealed class NotePreviewSound
{
    private readonly Func<AvaloniaPlaybackSessions?> _playback;
    private readonly Func<AudioSettings> _audio;
    private readonly Action<string> _report;
    private readonly Func<TabForge.Audio.RoutedMidiOutput?>? _instrumentRouting;
    private readonly TrackInstrumentPreview _instrument = new();

    /// <param name="editor">The score editor whose entered notes are auditioned.</param>
    /// <param name="playback">The playback sessions that own the engine; null when no engine exists, so nothing sounds.</param>
    /// <param name="audio">The audio settings: the Preview note sound flag and the length used when a note has none.</param>
    /// <param name="report">Shows a failed audition as a notice.</param>
    /// <param name="instrumentRouting">The shown song's engine routes, so a track with an instrument plug-in sounds through it; null: always the built-in synth.</param>
    public NotePreviewSound(ScorePreviewEditor editor, Func<AvaloniaPlaybackSessions?> playback, Func<AudioSettings> audio, Action<string> report,
        Func<TabForge.Audio.RoutedMidiOutput?>? instrumentRouting = null)
    {
        _instrumentRouting = instrumentRouting;
        _playback = playback;
        _audio = audio;
        _report = report;
        editor.NotePreviewed += OnNotePreviewed;
    }

    private void OnNotePreviewed(ScorePreviewEditor.NotePreview preview)
    {
        var settings = _audio();
        if (!settings.PreviewNotes || _playback() is not { } sessions) return;
        var lengthMs = preview.LengthMs > 0 ? preview.LengthMs : settings.PreviewLengthMs;
        if (PreviewSources.Get(settings) == PreviewSources.Track && _instrumentRouting?.Invoke() is { } routing
            && _instrument.TryPlay(routing, preview.Channel, preview.Midi, lengthMs)) return;   // the track's plug-in instrument sounds it; no program change
        UiTasks.Forget(sessions.PlayTestNoteAsync(preview.DeviceId, preview.Channel, preview.Program, preview.Midi, lengthMs), "Note preview", _report);
    }
}
