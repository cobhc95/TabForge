using System;
using TabForge.Avalonia.Transport;
using TabForge.Documents;
using TabForge.Platform;
using TabForge.Services;
using TabForge.Services.Video;

namespace TabForge.Avalonia.Video;

// Owns: the Avalonia side of the live video recorder's port (ILiveVideoRecordHost): the playback of the song that was shown when the
//   recording began, the engine's master tap and its chunks, the frame capture of the window, the timers and the notices it is handed.
// Does not own: the recording state machine (LiveVideoRecordController), the pixels (AvaloniaWindowFrameCapture), the regions (LiveVideoRegions),
//   the notices (MainWindowLiveVideo) or the engine (AudioEngineClient).
// Threads: UI thread for every member except the MasterAudio chunks, which arrive on the engine's reader thread.
// Tests: TabForge.Avalonia.Tests.LiveVideoRecordTests.
internal sealed class AvaloniaLiveVideoHost : ILiveVideoRecordHost
{
    private readonly AvaloniaPlaybackSessions? _playback;
    private readonly Func<SongDocument> _activeSong;
    private readonly Func<LiveVideoSettings> _settings;
    private readonly LiveVideoRegions _regions;
    private readonly IUiThread _ui;
    private readonly Action<TimeSpan?> _showRecording;
    private readonly Action<string, string?> _notify;

    /// <param name="playback">The playback sessions and their shared engine; null without an audio engine (the recording then has no sound and does not save).</param>
    /// <param name="activeSong">The song shown now; the recording plays the one shown when it began.</param>
    /// <param name="settings">The stored live video settings, read each time so a Preferences change applies to the next recording.</param>
    /// <param name="regions">The regions the recording can show.</param>
    /// <param name="showRecording">The REC state: the elapsed time while recording, null when it stopped.</param>
    /// <param name="notify">A message, with the folder to open when it was saved (null for an error).</param>
    public AvaloniaLiveVideoHost(AvaloniaPlaybackSessions? playback, Func<SongDocument> activeSong, Func<LiveVideoSettings> settings, LiveVideoRegions regions,
        IUiThread ui, Action<TimeSpan?> showRecording, Action<string, string?> notify)
    {
        _playback = playback; _activeSong = activeSong; _settings = settings; _regions = regions; _ui = ui; _showRecording = showRecording; _notify = notify;
    }

    /// <summary>The playback the recording runs with (set when the master tap goes on); null before the first recording.</summary>
    public AvaloniaPlaybackSession? RecordedSession { get; private set; }

    public LiveVideoSettings Settings => _settings();

    /// <summary>The engine rate from the audio settings, so a rate the MP4 cannot take is refused before the recording starts (0 without settings).</summary>
    public int OutputSampleRate => OutputRate();

    /// <summary>Reads the engine's sample rate; set by the window from its audio settings.</summary>
    public Func<int> OutputRate { get; init; } = () => 0;

    public void SetMasterTap(bool on)
    {
        if (on) RecordedSession = _playback?.For(_activeSong());
        _playback?.Engine?.SetMasterTap(on);
    }

    /// <summary>The engine's master output chunks; the events are added to the engine the sessions share (none without an engine).</summary>
    public event Action<int, long, float[], int> MasterAudio
    {
        add { if (_playback?.Engine is { } engine) engine.MasterAudio += value; }
        remove { if (_playback?.Engine is { } engine) engine.MasterAudio -= value; }
    }

    public ScreenRect? RegionRect(string region) => _regions.RectOf(region);

    public IVideoCapture BeginCapture(int width, int height, Func<ScreenRect?> region) =>
        new AvaloniaWindowFrameCapture(width, height, LiveVideoChoices.CaptureFps(Settings.Fps), () => _regions.Content, region, _ui);

    public void ShowRecording(TimeSpan? elapsed) => _showRecording(elapsed);

    public void Notify(string message, string? openFolder) => _notify(message, openFolder);

    /// <summary>Set by the window: shows a plain Info notice; without it the message goes through Notify.</summary>
    public Action<string>? InfoNotice { get; init; }

    public void NotifyInfo(string message) { if (InfoNotice is { } info) info(message); else _notify(message, null); }

    public void Post(Action action) => _ui.Post(action);

    public IDisposable Every(TimeSpan period, Action tick)
    {
        var timer = _ui.CreateTimer(period, UiPriority.Background);
        timer.Tick += (_, _) => tick();
        timer.Start();
        return new TimerStop(timer);
    }

    private sealed class TimerStop : IDisposable
    {
        private readonly IUiTimer _timer;
        public TimerStop(IUiTimer timer) => _timer = timer;
        public void Dispose() => _timer.Stop();
    }
}
