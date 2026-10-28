using System.IO;

namespace TabForge.Services.Video;

/// <summary>A pixel source for one recording: fills a top-down BGRA buffer, false for "no picture this tick".</summary>
internal interface IVideoCapture : IDisposable
{
    bool Grab(byte[] bgra);
    /// <summary>UI thread: lowers the render rate (the recording fell back to 30 fps).</summary>
    void SetFrameRate(int fps) { }
}

/// <summary>What the live video controller needs from its window (MainWindow implements it through a small host class).</summary>
internal interface ILiveVideoRecordHost
{
    LiveVideoSettings Settings { get; }
    /// <summary>The sample rate the engine plays at (Hz), checked before a recording starts; 0 when it is not known (checked by the encoder later).</summary>
    int OutputSampleRate => 0;
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
    /// <summary>A plain Info notice on the top bar (not an error).</summary>
    void NotifyInfo(string message) => Notify(message, null);
    /// <summary>Runs <paramref name="action"/> on the UI thread.</summary>
    void Post(Action action);
    /// <summary>UI thread: restores and activates the main window the recording captures (on start, and again when a picture is found black).</summary>
    void BringToFront() { }
    /// <summary>UI thread: why the window cannot show a picture (<see cref="LiveVideoFocus.BlackReason"/>), read before <see cref="BringToFront"/>.</summary>
    string BlackReason() => LiveVideoFocus.BlackReason(false, true, true);
    /// <summary>Calls <paramref name="tick"/> on the UI thread every <paramref name="period"/> until disposed.</summary>
    IDisposable Every(TimeSpan period, Action tick);
}

// Owns: the state of live video recording (idle or recording), the REC indicator ticks and the
// hand-over of a finished MP4 to the user.
// Does not own: capturing pixels or audio (LiveVideoSession, ScreenRegionGrabber, the engine tap), playback itself, the buttons.
// Tests: LiveVideoRecordTests.
/// <summary>
/// Record video records the screen frame and the app audio to an MP4 without touching the transport:
/// starting, stopping, saving or a capture error never starts, stops or pauses playback, and user
/// play/stop/pause during a capture is just captured. Pressing it again stops and finalises the MP4
/// on a worker thread. Zero cost when idle: no tap, no timers, no capture thread.
/// </summary>
internal sealed class LiveVideoRecordController
{
    /// <summary>The notice for an engine rate the MP4 audio cannot take ({0}: kHz).</summary>
    public const string UnsupportedRateText = "Record video needs the audio engine at 44.1 or 48 kHz (it runs at {0:0.#} kHz). Change the sample rate in Preferences > Audio.";

    /// <summary>Shown once when a 60 fps recording drops to 30 fps because the capture cannot keep up.</summary>
    public const string FellBackText = "Video recording dropped to 30 fps to keep up";

    private readonly ILiveVideoRecordHost _host;
    private LiveVideoSession? _session;
    private IVideoCapture? _capture;
    private IDisposable? _ticker, _regionTicker;
    private volatile ScreenRect? _region;
    private string _path = "";
    private string _folder = "";
    private bool _fellBackShown, _blackShown;
    private const string BlackText = "Recording captured a black picture: {0}. TabForge was brought to the front; keep it visible and record again.";

    public LiveVideoRecordController(ILiveVideoRecordHost host) => _host = host;

    /// <summary>The longest a stop may take to save: then the encoder is abandoned and the notice says so.</summary>
    public TimeSpan FinishTimeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>A test seam: adjusts the encoder options of each recording.</summary>
    internal Func<VideoEncoderOptions, VideoEncoderOptions> TuneOptions { get; init; } = options => options;

    public bool IsRecording => _session is not null;

    /// <summary>The last finalise (finished or failed); tests wait on it.</summary>
    public Task Finalizing { get; private set; } = Task.CompletedTask;

    /// <summary>The button and the hotkey.</summary>
    public void Toggle() { if (IsRecording) Stop(); else Start(); }

    private void Start()
    {
        if (!VideoEncoderFactory.LiveRecordingSupported(TabForge.Platform.HostSystem.Current))
        {
            _host.Notify(VideoEncoderFactory.LiveRecordingUnavailableText, null);
            return;
        }
        var settings = _host.Settings;
        if (_host.OutputSampleRate is var rate and > 0 && !VideoEncoderRules.SupportsSampleRate(rate))
        {
            _host.Notify(string.Format(System.Globalization.CultureInfo.InvariantCulture, UnsupportedRateText, rate / 1000.0), null);
            return;
        }
        _fellBackShown = false; _blackShown = false;
        _host.BringToFront();   // the capture reads the main window: it must be restored, in front and painted before the first frame
        _region = _host.RegionRect(settings.Region);
        var (width, height) = LiveVideoChoices.SizeOf(settings.Resolution, _region);
        if (ValidatedFolder(settings) is not { } folder) return;
        _folder = folder;
        if (!MakeFolderWritable()) return;
        ChooseFilePath();
        if (!BeginSession(settings, width, height)) return;
        _host.MasterAudio += OnAudio;
        _host.SetMasterTap(true);
        _regionTicker = _host.Every(TimeSpan.FromMilliseconds(250), () => _region = _host.RegionRect(_host.Settings.Region));   // the window can move or resize
        _ticker = _host.Every(TimeSpan.FromMilliseconds(250), Tick);
        _host.ShowRecording(TimeSpan.Zero);
    }

    /// <summary>The video folder, checked before anything is created through it; a refused device path or link ends the start, an access or disk-full failure falls back to the Videos folder. Null (after the notice) when there is none.</summary>
    private string? ValidatedFolder(LiveVideoSettings settings)
    {
        try { return TabForge.Services.FilePathPolicy.ValidateWritableFolder(LiveVideoChoices.FolderOf(settings), "video folder"); }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or NotSupportedException)
        {
            Trace.Error(Trace.Ui, $"video folder: validate: {ex.Message} hresult=0x{ex.HResult:X8}");
            var deniedValidate = TabForge.Services.FilePathPolicy.IsAccessDenied(ex);
            var fullValidate = !deniedValidate && TabForge.Services.FilePathPolicy.IsDiskFull(ex);
            if (deniedValidate || fullValidate)
            {
                var winValidate = TabForge.Services.FilePathPolicy.FallbackWritableChain("Videos", null, out _);
                if (winValidate is not null) return winValidate;
                var hintValidate = deniedValidate ? TabForge.Services.FilePathPolicy.AccessDeniedHint : "";
                _host.Notify($"The video folder could not be used: {ex.Message}{hintValidate}", null);
                return null;
            }
            _host.Notify($"The video folder could not be used: {ex.Message}", null);
            return null;
        }
    }

    /// <summary>Creates the folder and probes that it can be written; on a failure it falls back to the Videos folder and says so. False (after the notice) when nothing works.</summary>
    private bool MakeFolderWritable()
    {
        Exception? createFailure = null;
        try
        {
            Directory.CreateDirectory(_folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Trace.Error(Trace.Ui, $"video folder: create: {ex.Message} hresult=0x{ex.HResult:X8}");
            createFailure = ex;
        }
        // Post-create probe, before the encoder opens (skipped when creation already failed: the chain below covers both).
        Exception? probeFailure = null;
        if (createFailure is null && !TabForge.Services.FilePathPolicy.TryProbeWritableFolder(_folder, out probeFailure) && probeFailure is null)
            probeFailure = new IOException("The write probe reported failure without detail.");
        if (probeFailure is not null)
            Trace.Error(Trace.Ui, $"video folder: probe: {probeFailure.Message} denied={TabForge.Services.FilePathPolicy.IsAccessDenied(probeFailure)} hresult=0x{probeFailure.HResult:X8}");
        var firstFailure = createFailure ?? probeFailure;
        if (firstFailure is not null)
        {
            // One terminal notice naming the winning path: the blocked-by-Windows and full tails name the
            // fallback; anything else (link/device already refused above) keeps the could-not-be-used text.
            var denied = TabForge.Services.FilePathPolicy.IsAccessDenied(firstFailure);
            var full = !denied && TabForge.Services.FilePathPolicy.IsDiskFull(firstFailure);
            string? win = null;
            if (denied || full) win = TabForge.Services.FilePathPolicy.FallbackWritableChain("Videos", null, out _);
            if (win is null)
            {
                var hint = denied ? TabForge.Services.FilePathPolicy.AccessDeniedHint : "";
                _host.Notify($"The video folder could not be used: {firstFailure.Message}{hint}", null);
                return false;
            }
            _folder = win;
            _host.Notify(denied
                ? $"Saved to {win} because the usual folder was blocked by Windows"
                : $"Saved to {win} because the usual folder is full", _folder);
        }
        return true;
    }

    /// <summary>One frozen stamp per start (the clock may tick over mid-name) with a GUID past "(n)", mirroring the audio takes.</summary>
    private void ChooseFilePath()
    {
        var stamp = DateTime.Now;
        _path = Path.Combine(_folder, $"TabForge {stamp:yyyy-MM-dd HH-mm-ss}.mp4");
        for (var n = 2; n < 1000 && File.Exists(_path); n++) _path = Path.Combine(_folder, $"TabForge {stamp:yyyy-MM-dd HH-mm-ss} ({n}).mp4");
        if (File.Exists(_path)) _path = Path.Combine(_folder, $"TabForge {stamp:yyyy-MM-dd HH-mm-ss}-{Guid.NewGuid():N}.mp4");
    }

    /// <summary>Opens the capture and the encoder session; a capture that cannot open leaves nothing running or open.</summary>
    private bool BeginSession(LiveVideoSettings settings, int width, int height)
    {
        try
        {
            _capture = _host.BeginCapture(width, height, () => _region);
            // The capture, its render, the capture thread and the encoder all run at the capped rate (see LiveVideoChoices.CaptureFps).
            var options = VideoEncoderOptions.Remembering(settings);
            Trace.Error(Trace.Ui, $"video: start {width}x{height} {settings.Fps} fps encoder={options.Encoder} hardware-probe='{options.HardwareProbe}' -> {_path}");
            _session = LiveVideoSession.Start(_path, width, height, LiveVideoChoices.CaptureFps(settings.Fps), _capture.Grab, TuneOptions(options));
        }
        catch (Exception ex)   // a capture that cannot open: nothing runs and nothing is left open
        {
            _capture?.Dispose(); _capture = null;
            _host.Notify("The recording could not start: " + ex.Message, null);
            return false;
        }
        return true;
    }

    /// <summary>Stops recording and finalises the MP4; playback is untouched.</summary>
    public void Stop() => StopRecording();

    private void OnAudio(int rate, long first, float[] data, int frames) => _session?.OnAudio(rate, first, data, frames);

    private void Tick()
    {
        if (_session is not { } session) return;
        if (session.Error is not null) { StopRecording(); return; }
        if (session.FellBackTo30 && !_fellBackShown) { _fellBackShown = true; _capture?.SetFrameRate(30); _host.NotifyInfo(FellBackText); }
        if (session.AllBlack && !_blackShown)
        {
            _blackShown = true;
            var reason = _host.BlackReason();   // read before the window is brought forward again
            _host.BringToFront();
            _host.NotifyInfo(string.Format(System.Globalization.CultureInfo.InvariantCulture, BlackText, reason));
        }
        _host.ShowRecording(session.Elapsed);
    }

    private void StopRecording()
    {
        if (_session is not { } session) return;
        var capture = _capture; _capture = null;
        _session = null;
        _ticker?.Dispose(); _regionTicker?.Dispose();
        _ticker = _regionTicker = null;
        _host.MasterAudio -= OnAudio;
        _host.SetMasterTap(false);
        _host.ShowRecording(null);
        var path = _path; var folder = _folder;
        Finalizing = Task.Run(async () =>
        {
            string message; string? open = null;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            Trace.Error(Trace.Ui, $"video: stop requested, saving {path} (watchdog {FinishTimeout.TotalSeconds:0.#} s)");
            var finish = Task.Run(session.Finish);
            try
            {
                if (await Task.WhenAny(finish, Task.Delay(FinishTimeout)) != finish)
                {
                    session.Abort($"saving did not finish within {FinishTimeout.TotalSeconds:0.#} s");   // the unfinished file stays where it is: nothing is deleted or moved after this
                    _ = finish.ContinueWith(t => Trace.Error(Trace.Ui, "video: late finish ended: " + (t.Exception?.GetBaseException().Message ?? "ok")), TaskScheduler.Default);
                    throw new VideoEncoderException($"saving took longer than {FinishTimeout.TotalSeconds:0.#} s and was abandoned; the unfinished file was kept at {session.StagingFile}");
                }
                await finish;
                message = $"Recording saved to {folder}" + (session.DroppedFrames > 0 ? $" ({session.DroppedFrames} frames were skipped)" : ""); open = folder;
                Trace.Error(Trace.Ui, $"video: saved {path} in {clock.ElapsedMilliseconds} ms");
            }
            catch (Exception ex)   // any failure: the notice names the log (the session removed its own half file, or kept it and said where)
            {
                message = "The video could not be saved: " + ex.Message + LogHint();
                Trace.Error(Trace.Ui, $"video: save failed after {clock.ElapsedMilliseconds} ms: {ex.Message}");
            }
            finally { capture?.Dispose(); }
            _host.Post(() => _host.Notify(message, open));
        });
    }

    // Where the details of a failure are: the always-on errors.log in the diagnostics folder.
    private static string LogHint()
    {
        try { return $" (details: {TabForge.Services.FilePathPolicy.DefaultDiagnosticsPath("errors.log")})"; }
        catch (IOException) { return ""; }   // no diagnostics folder: nothing to point at
    }
}
