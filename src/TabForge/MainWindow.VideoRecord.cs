using System.Windows;
using TabForge.Services.Video;
using TabForge.Views;
using TabForge.Views.Video;

namespace TabForge;

// MainWindow: thin hooks for Record video (button, command, transport-stopped call, REC indicator). The logic lives in
// Services/Video/LiveVideoRecordController; the WPF helpers in Views/Video/VideoRecordUi.
// Owns: thin hooks for Record video: the button, the command, the call when transport stops and the REC indicator.
// Does not own: the recording logic (Services/Video/LiveVideoRecordController) and the WPF helpers (Views/Video/VideoRecordUi).
// Tests: listed in docs/feature-map/recording.md.
public partial class MainWindow : IVideoCommandHost
{
    private LiveVideoRecordController? _videoRecord;
    private LiveVideoRecordController VideoRecord => _videoRecord ??= new LiveVideoRecordController(new VideoRecordHost(this));

    private void RecordVideo_Click(object sender, RoutedEventArgs e) => ToggleVideoRecording();

    public void ExportVideo() => Views.VideoExportWindow.OpenDialog(RenderContext(), this);

    /// <summary>Transport Record video: starts playback and recording, or stops both.</summary>
    public void ToggleVideoRecording() => VideoRecord.Toggle();

    /// <summary>Playback stopped or ended: a running video recording ends with it. Costs nothing when none runs.</summary>
    private void VideoRecordTransportStopped() => _videoRecord?.OnTransportStopped();

    private sealed class VideoRecordHost : ILiveVideoRecordHost
    {
        private readonly MainWindow _window;
        private readonly VideoSavedNotice _notice;

        public VideoRecordHost(MainWindow window) { _window = window; _notice = new VideoSavedNotice(window, new StatusNoticeBars(window.MainStatusBar)); }

        public LiveVideoSettings Settings => _window._settings.LiveVideo ??= new LiveVideoSettings();
        public bool IsPlaying => _window._midi.IsPlaying && !_window._midi.IsPaused;
        public void StartPlayback() => _window.StartPlayback();
        public void StopPlayback() => _window.StopPlayback();
        public void SetMasterTap(bool on) => _window._engine.SetMasterTap(on);
        public event Action<int, long, float[], int> MasterAudio { add => _window._engine.MasterAudio += value; remove => _window._engine.MasterAudio -= value; }
        public void Post(Action action) => _window.Dispatcher.BeginInvoke(action);
        public IVideoCapture BeginCapture(int width, int height, Func<ScreenRect?> region) => VideoRecordUi.BeginCapture(width, height, region);
        public IDisposable Every(TimeSpan period, Action tick) => VideoRecordUi.Every(_window.Dispatcher, period, tick);
        public void Notify(string message, string? openFolder) => _notice.Show(message, openFolder);

        public ScreenRect? RegionRect(string region)
        {
            var window = _window;
            var score = VideoRecordUi.ScreenRectOf(window.Editor);
            return region switch
            {
                LiveVideoChoices.Window => VideoRecordUi.ScreenRectOf(window.Content as FrameworkElement) ?? score,
                LiveVideoChoices.Band when window._dockWorkspace?.IsPanelVisible("band") == true => VideoRecordUi.ScreenRectOf(window.Band.View) ?? score,
                LiveVideoChoices.ScoreAndInstrument => VideoRecordUi.Union(score, VideoRecordUi.ScreenRectOf(window.InstrumentHost)),
                _ => score,
            };
        }

        public void ShowRecording(TimeSpan? elapsed)
        {
            var window = _window;
            window.RecIndicator.Visibility = window.VideoRecDot.Visibility = elapsed is null ? Visibility.Collapsed : Visibility.Visible;
            if (elapsed is { } t) window.RecIndicator.Text = $"REC {(int)t.TotalMinutes:00}:{t.Seconds:00}";
        }
    }
}
