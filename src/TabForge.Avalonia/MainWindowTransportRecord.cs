using System;
using System.Collections.Generic;
using TabForge.Audio;
using TabForge.Avalonia.Features.Recording.Presenter;
using TabForge.Avalonia.Transport;
using TabForge.Documents;
using TabForge.LiveInput;
using TabForge.Features.Recording.Rules;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: the Transport.Record command id and the window wiring: Register builds the recording presenter and returns its session; the transport
//   and Record buttons show the recording state, and "no track armed" goes to the top bar (Info, never modal).
// Does not own: the recording logic and its live takes, landing and undo step (Features/Recording/Presenter/RecordingPresenter), the recording rules
//   (TransportRecording in Core), the recording UI (Platform/TransportBarView, Platform/TimelineLiveTakeLayer).
// Tests: TabForge.Avalonia.Tests.TransportRecordingTests, MidiTakeCaptureTests, CountInRecordingTests, RecordingBatchOneTests, RecordingLiveTakeTests, NotificationBarTests.
public static class MainWindowTransportRecord
{
    /// <summary>The command id of Transport.Record (the Record button runs it too).</summary>
    public const string CommandId = RecordingPresenter.CommandId;

    /// <summary>The command ids this class handles.</summary>
    public static readonly IReadOnlyList<string> Commands = new[] { CommandId };

    /// <param name="playback">The playback sessions and their engine; null (no engine host, as in the preview) makes the command do nothing.</param>
    /// <param name="songs">The tabs; the command records the shown song.</param>
    /// <param name="showStatus">Shows the outcome text on the top bar (Info).</param>
    /// <param name="plugins">The plug-in settings the engine sync uses, so arm toggles reach the engine before it records.</param>
    /// <param name="panes">The document panes; each arrangement's track arm toggles, armed-row edits and input meter are hooked, the first one included.</param>
    /// <returns>The window's recording state; disposing it (the window closed) ends a recording in progress and detaches the hooks.</returns>
    internal static RecordingPresenter.RecordingSession Register(MainWindowCommands commands, MainWindowDocumentPanes panes, AvaloniaPlaybackSessions? playback, MainWindowTabs songs,
        Action<string> showStatus, Func<PluginSettings> plugins) =>
        new RecordingPresenter(commands, panes, playback, songs, showStatus, plugins, MeterFraction).Register();

    /// <summary>A linear input peak (0..1) as the armed row meter's fraction (0..1): the live strip's dB curve, one source of truth for both meters.</summary>
    private static double MeterFraction(double peak) => LiveStripRules.MeterFraction(LiveStripRules.PeakToDb(peak));

    /// <summary>The Record button of the shown arrangement shows whether the window is capturing.</summary>
    internal static void ShowRecording(MainWindowDocumentPanes panes, bool on) => RecordingPresenter.ShowRecording(panes, on);

    /// <summary>Transport.Record: starts or ends the shown song's recording (the presenter owns the rule).</summary>
    internal static void Toggle(TransportRecording recording, AvaloniaPlaybackSession session, SongDocument song, Action<string> showStatus, RecordingPresenter.RecordStart start,
        RecordingPresenter.EngineHooks? hooks = null, (double Start, double End)? loop = null, AvaloniaPlaybackSessions? playback = null,
        RepeatPosition? position = null) =>
        RecordingPresenter.Toggle(recording, session, song, showStatus, start, hooks, loop, playback, position: position);

    /// <summary>Starts the one-pass re-record of a loop pass (the presenter owns the punch and its landing).</summary>
    internal static bool ReRecordPass(TransportRecording recording, AvaloniaPlaybackSession session, SongDocument song, TrackModel track, AudioClip clip,
        Action<string> showStatus, RecordingPresenter.EngineHooks? hooks, AvaloniaPlaybackSessions? playback = null) =>
        RecordingPresenter.StartPunch(recording, session, song, track, clip, showStatus, hooks, playback);

    /// <summary>Lands the MIDI takes a stopped recording finished (the presenter owns the landing and its undo step).</summary>
    public static void LandMidiTakes(TransportRecording recording, SongDocument song, UndoSnapshot? before = null,
        PassPunch? punch = null) =>
        RecordingPresenter.LandMidiTakes(recording, song, before, punch);

    /// <summary>Lands one finished engine recording (the presenter owns the landing and its undo step).</summary>
    public static string? LandTake(IEnumerable<SongDocument> songs, TrackModel track, string file, double startSec, double lengthSec,
        (double Start, double End)? loop = null, UndoSnapshot? before = null, PassPunch? punch = null) =>
        RecordingPresenter.LandTake(songs, track, file, startSec, lengthSec, loop, before, punch);
}
