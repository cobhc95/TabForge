using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views;
using TabForge.Platform;
using TabForge.Presenters;
using TabForge.Services;
using TabForge.Services.Features;
using TabForge.Services.Video;

namespace TabForge.Avalonia.Video;

// Owns: the window's Record video wiring: the Transport.RecordVideo command (the title-bar camera button and its hotkey) toggling the
//   recording, the REC state the title bar shows, and the "Video saved" top-bar notice with its Open folder button (errors go to the top bar too, never modal).
//   Its host (AvaloniaLiveVideoHost) and the controller are created here; the window only calls Register.
// Does not own: the recording logic (LiveVideoRecordController in Core), the pixels (AvaloniaWindowFrameCapture), the regions (LiveVideoRegions),
//   the title bar (TitleBarView) or the export window (File > Export video, MainWindowFileExports).
// Tests: TabForge.Avalonia.Tests.LiveVideoRecordTests, TabForge.Avalonia.Tests.NotificationBarTests.
public sealed class MainWindowLiveVideo : IDisposable
{
    /// <summary>The message box caption of a recording error (kept for history; errors now go to the top bar).</summary>
    public const string Caption = "Record video";

    /// <summary>The status text when the window has no audio engine to record the sound from.</summary>
    public const string NoAudioEngineText = "Record video needs the audio engine.";

    /// <summary>The notice while a close waits for the recording to finish saving.</summary>
    public const string SavingText = "Saving recording…";

    private const string OpenFolderText = "Open folder", OpenLogText = "Open log";

    private readonly AvaloniaPlaybackSessions? _playback;
    private readonly MainWindowSettings? _settings;
    private readonly LiveVideoSettings _defaults = new();
    private readonly LiveVideoRecordController _controller;
    private readonly AvaloniaLiveVideoHost _host;
    private readonly NotificationBar _topBar;
    private string? _savedFolder;

    private MainWindowLiveVideo(MainWindowTabs songs, AvaloniaPlaybackSessions? playback, MainWindowSettings? settings, LiveVideoRegions regions, NotificationBar topBar)
    {
        _playback = playback; _settings = settings; _topBar = topBar;
        _host = new AvaloniaLiveVideoHost(playback, () => songs.Active, StoredSettings, regions, AvaloniaUiThread.ForApplication(),
            elapsed => RecordingChanged?.Invoke(elapsed is not null), Notify) { OutputRate = () => settings?.Settings.Plugins.SampleRate ?? 0, InfoNotice = m => topBar.Show(m, NotificationKind.Info, NoticeCategory.Recording, source: NotificationBar.VideoSource) };
        _controller = new LiveVideoRecordController(_host);
    }

    /// <summary>True while a recording runs, on each REC tick (the title bar's recording dot follows it).</summary>
    public event Action<bool>? RecordingChanged;

    /// <summary>The unified top bar's row; the window docks it at the top above the workspace, hidden until a recording saves.</summary>
    public Control SavedNotice => _topBar.View;

    /// <summary>Wires the command and the session-closing hook. <paramref name="score"/> is the score's scroll view; <paramref name="band"/> and <paramref name="instrument"/> are null while those views do not exist.</summary>
    public static MainWindowLiveVideo Register(MainWindowCommands commands, Window owner, MainWindowTabs songs, AvaloniaPlaybackSessions? playback,
        MainWindowSettings? settings, Control score, Func<Control?> band, Func<Control?> instrument, Action<string> showStatus, NotificationBar topBar)
    {
        var regions = new LiveVideoRegions(() => owner.Content as Visual, score, band, instrument);
        var video = new MainWindowLiveVideo(songs, playback, settings, regions, topBar);
        commands.Add(FeatureHotkeyIds.RecordVideo, () =>
        {
            if (playback is null) showStatus.Error(NoAudioEngineText);
            else video._controller.Toggle();
        });
        if (playback is not null) { playback.SessionClosing += video.OnSessionClosing; }
        return video;
    }

    /// <summary>The recorded song's tab is closing: the recording ends and saves before its playback goes.</summary>
    private void OnSessionClosing(AvaloniaPlaybackSession session)
    {
        if (_controller.IsRecording && ReferenceEquals(_host.RecordedSession, session)) _controller.Stop();
    }

    /// <summary>The window is closing: stops a running recording; the task ends when its MP4 is finished (at once when none runs).</summary>
    public Task FinishBeforeClose()
    {
        if (_controller.IsRecording) _topBar.Show(SavingText, NotificationKind.Info, NoticeCategory.Recording, source: NotificationBar.VideoSource);
        _controller.Stop();
        return _controller.Finalizing;
    }

    public void Dispose()
    {
        if (_playback is not null) { _playback.SessionClosing -= OnSessionClosing; }
        _controller.Stop();
    }

    private LiveVideoSettings StoredSettings() => _settings is null ? _defaults : _settings.Settings.LiveVideo ??= new LiveVideoSettings();

    private void Notify(string message, string? openFolder)
    {
        // UI thread already (the host posts here): errors are sticky Error on the top bar, never modal; success keeps its Open folder button (8 s).
        if (openFolder is null)
        {
            _topBar.Show(message, NotificationKind.Error, NoticeCategory.Recording, (OpenLogText, OpenLog), source: NotificationBar.VideoSource);
            return;
        }
        _savedFolder = openFolder;
        _topBar.Show(message, NotificationKind.Info, NoticeCategory.Recording, (OpenFolderText, OpenSavedFolder), source: null);
    }

    private void OpenLog()
    {
        _topBar.DismissCurrent();
        try { FolderLauncher.Open(_topBar.View, System.IO.Path.GetDirectoryName(FilePathPolicy.DefaultDiagnosticsPath("errors.log"))!); }
        catch (System.IO.IOException) { _topBar.Show("The log folder is not available.", NotificationKind.Error, NoticeCategory.Recording, source: NotificationBar.VideoSource); }
    }

    private void OpenSavedFolder()
    {
        _topBar.DismissCurrent();
        if (_savedFolder is null) return;
        FolderLauncher.Open(_topBar.View, _savedFolder);
    }
}
