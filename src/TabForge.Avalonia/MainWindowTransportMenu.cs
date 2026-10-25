using System.Collections.Generic;
using TabForge.Avalonia.Features.Recording.Presenter;
using TabForge.Avalonia.Transport;

namespace TabForge.Avalonia;

// Owns: the handlers of the Transport menu and the transport bar's transport commands: Play from beginning, Play/Pause, Stop and Escape stop,
//   including the WPF rule that stopping playback while a recording runs ends the recording first (and the recording's own stop restores the
//   cursor and stops the playback).
// Does not own: the menu rows (MainMenuTable in Core), the commands table (MainWindowCommands), the playback session and engine
//   (AvaloniaPlaybackSessions), the recording (MainWindowTransportRecord), the Metronome and Count-in toggles (MainWindowTransportToggles).
// Tests: TabForge.Avalonia.Tests.MainWindowMenuTests, MainWindowSpaceEscapeRoutingTests, RecordingBatchOneTests.
public static class MainWindowTransportMenu
{
    /// <summary>The Transport menu command ids this class handles.</summary>
    public static readonly IReadOnlyList<string> Commands = new[] { "Transport.PlayFromStart", "Transport.PlayPause", "Transport.Stop" };

    /// <summary>The fixed Escape command: stops a playing or paused song. Not in the hotkey catalogue; it does nothing while the song is stopped.</summary>
    public const string EscapeStopCommand = "Transport.EscapeStop";

    /// <param name="playback">The playback sessions; null (no engine host, as in the preview) makes the commands do nothing.</param>
    /// <param name="songs">The tabs; the commands act on the shown song.</param>
    /// <param name="recording">The window's recording state; a run-time Stop, Space or Esc ends a recording first, as WPF does.</param>
    internal static void Register(MainWindowCommands commands, AvaloniaPlaybackSessions? playback, MainWindowTabs songs,
        RecordingPresenter.RecordingSession? recording = null)
    {
        commands.AddWhen(EscapeStopCommand, () =>
        {
            if (EndRecording(recording)) return;
            if (playback?.For(songs.Active) is { } session) session.Stop();
        }, () => recording?.IsRecording == true
                || (playback?.For(songs.Active) is { } session && (session.IsPlaying || session.IsPaused)));
        commands.Add("Transport.PlayFromStart", () =>
        {
            if (EndRecording(recording)) return;   // Shift+Space while recording ends it, as WPF's Space does
            if (playback?.For(songs.Active) is { } session) PlayFromStart(session);
        });
        commands.Add("Transport.PlayPause", () =>
        {
            if (EndRecording(recording)) return;   // Space ends a recording instead of pausing it
            if (playback?.For(songs.Active) is { } session) session.PlayOrPause();
        });
        commands.Add("Transport.Stop", () =>
        {
            if (EndRecording(recording)) return;   // the recording's stop stops the playback and restores the cursor
            if (playback?.For(songs.Active) is { } session) session.Stop();
        });
    }

    /// <summary>Ends the recording in progress if there is one; true when it did (the caller then leaves the transport alone).</summary>
    private static bool EndRecording(RecordingPresenter.RecordingSession? recording)
    {
        if (recording?.IsRecording != true) return false;
        recording.StopRecording();
        return true;
    }

    /// <summary>Transport.PlayFromStart: restarts the song at its first bar, playing it when it was stopped or paused.</summary>
    public static void PlayFromStart(AvaloniaPlaybackSession session)
    {
        if (session.IsPlaying) session.Stop();
        session.Seek(0, 0);
        session.PlayOrPause();
    }
}
