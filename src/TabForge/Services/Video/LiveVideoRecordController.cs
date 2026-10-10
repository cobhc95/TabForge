using System.IO;

namespace TabForge.Services.Video;

/// <summary>A pixel source for one recording: fills a top-down BGRA buffer, false for "no picture this tick".</summary>
internal interface IVideoCapture : IDisposable
{
    bool Grab(byte[] bgra);
}

/// <summary>What the live video controller needs from its window (MainWindow implements it through a small host class).</summary>
internal interface ILiveVideoRecordHost
{
    LiveVideoSettings Settings { get; }
    /// <summary>True while the song plays (paused counts as not playing).</summary>
    bool IsPlaying { get; }
    void StartPlayback();
    void StopPlayback();
    /// <summary>Switches the engine's master output tap; its chunks arrive on <see cref="MasterAudio"/> on the engine reader thread.</summary>
    void SetMasterTap(bool on);
    event Action<int, long, float[], int> MasterAudio;
    /// <summary>UI thread: the screen rectangle of a region choice (<see cref="LiveVideoChoices"/>), or null when it is not on screen.</summary>
    ScreenRect? RegionRect(string region);
    /// <summary>Opens whatever grabs pixels for a frame of this size; <paramref name="region"/> is read on the capture thread. Dispose releases it.</summary>
    IVideoCapture BeginCapture(int width, int height, Func<ScreenRect?> region);
    /// <summary>The "REC 00:12" indicator: shown with the time, or hidden (null).</summary>
    void ShowRecording(TimeSpan? elapsed);
    /// <summary>A message with an optional folder the user can open from it (saved) or an error (no folder).</summary>
    void Notify(string message, string? openFolder);
    /// <summary>Runs <paramref name="action"/> on the UI thread.</summary>
    void Post(Action action);
    /// <summary>Calls <paramref name="tick"/> on the UI thread every <paramref name="period"/> until disposed.</summary>
    IDisposable Every(TimeSpan period, Action tick);
}

// Owns: the state of live video recording (idle or recording), what starts and stops it with playback, the REC indicator ticks and the
// hand-over of a finished MP4 to the user.
// Does not own: capturing pixels or audio (LiveVideoSession, ScreenRegionGrabber, the engine tap), playback itself, the buttons.
// Tests: TestLiveVideoRecord.
/// <summary>
/// Record video starts playback (when it is not running) and recording; pressing it again, Stop or the end of the song stops both and
/// finalises the MP4 on a worker thread. Zero cost when idle: no tap, no timers, no capture thread.
/// </summary>
internal sealed class LiveVideoRecordController
{
    private readonly ILiveVideoRecordHost _host;
    private LiveVideoSession? _session;
    private IVideoCapture? _capture;
    private IDisposable? _ticker, _regionTicker;
    private volatile ScreenRect? _region;
    private string _path = "";
    private string _folder = "";

    public LiveVideoRecordController(ILiveVideoRecordHost host) => _host = host;

    public bool IsRecording => _session is not null;

    /// <summary>The last finalise (finished or failed); tests wait on it.</summary>
    public Task Finalizing { get; private set; } = Task.CompletedTask;

    /// <summary>The button and the hotkey.</summary>
    public void Toggle() { if (IsRecording) Stop(); else Start(); }

    private void Start()
    {
        var settings = _host.Settings;
        _region = _host.RegionRect(settings.Region);
        var (width, height) = LiveVideoChoices.SizeOf(settings.Resolution, _region);
        _folder = LiveVideoChoices.FolderOf(settings);
        try
        {
            Directory.CreateDirectory(_folder);
            _path = Path.Combine(_folder, $"TabForge {DateTime.Now:yyyy-MM-dd HH-mm-ss}.mp4");
            for (var n = 2; File.Exists(_path); n++) _path = Path.Combine(_folder, $"TabForge {DateTime.Now:yyyy-MM-dd HH-mm-ss} ({n}).mp4");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _host.Notify($"The video folder could not be used: {ex.Message}", null);
            return;
        }
        _capture =_host.BeginCapture(width, height, () => _region);
        _session = LiveVideoSession.Start(_path, width, height, LiveVideoChoices.NormalizeFps(settings.Fps), _capture.Grab, VideoEncoderOptions.From(settings));
        _host.MasterAudio += OnAudio;
        _host.SetMasterTap(true);
        _regionTicker = _host.Every(TimeSpan.FromMilliseconds(250), () => _region = _host.RegionRect(_host.Settings.Region));   // the window can move or resize
        _ticker = _host.Every(TimeSpan.FromMilliseconds(250), Tick);
        _host.ShowRecording(TimeSpan.Zero);
        if (!_host.IsPlaying) _host.StartPlayback();
    }

    /// <summary>Stops recording and playback (the button pressed again).</summary>
    public void Stop() => StopRecording(stopPlayback: true);

    /// <summary>Playback stopped or the song ended: the recording ends with it.</summary>
    public void OnTransportStopped() => StopRecording(stopPlayback: false);

    private void OnAudio(int rate, long first, float[] data, int frames) => _session?.OnAudio(rate, first, data, frames);

    private void Tick()
    {
        if (_session is not { } session) return;
        if (session.Error is not null) { StopRecording(stopPlayback: true); return; }
        _host.ShowRecording(session.Elapsed);
    }

    private void StopRecording(bool stopPlayback)
    {
        if (_session is not { } session) return;
        var capture = _capture; _capture = null;
        _session = null;   // first: stopping playback calls back into OnTransportStopped
        _ticker?.Dispose(); _regionTicker?.Dispose();
        _ticker = _regionTicker = null;
        _host.MasterAudio -= OnAudio;
        _host.SetMasterTap(false);
        _host.ShowRecording(null);
        if (stopPlayback && _host.IsPlaying) _host.StopPlayback();
        var path = _path; var folder = _folder;
        Finalizing = Task.Run(() =>
        {
            string message; string? open = null;
            try { session.Finish(); message = $"Video saved: {Path.GetFileName(path)}" + (session.DroppedFrames > 0 ? $" ({session.DroppedFrames} frames were skipped)" : ""); open = folder; }
            catch (VideoEncoderException ex) { message = "The video could not be saved: " + ex.Message; TryDelete(path); }
            capture?.Dispose();
            _host.Post(() => _host.Notify(message, open));
        });
    }

    private static void TryDelete(string path) { try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
}
