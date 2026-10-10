using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using TabForge.Controllers;
using TabForge.Services.Video;

namespace TabForge.Views.Video;

// Owns: the WPF helpers of live video recording: an element's screen rectangle, UI-thread timers, the screen capture adapter and the
// "video saved" notice with its Open folder button.
// Does not own: the recording state (LiveVideoRecordController) or which element a region means (the window passes that in).
// Tests: TestLiveVideoRecord (the controller, through a fake host).
internal static class VideoRecordUi
{
    /// <summary>The element's rectangle in screen pixels, or null when it is not showing.</summary>
    public static ScreenRect? ScreenRectOf(FrameworkElement? element)
    {
        if (element is not { IsVisible: true, ActualWidth: >= 1, ActualHeight: >= 1 }) return null;
        var topLeft = element.PointToScreen(new Point(0, 0));
        var bottomRight = element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight));
        var rect = new ScreenRect((int)topLeft.X, (int)topLeft.Y, (int)(bottomRight.X - topLeft.X), (int)(bottomRight.Y - topLeft.Y));
        // A scrolled score is far taller than the window: only the part inside the window is on screen.
        for (DependencyObject? up = System.Windows.Media.VisualTreeHelper.GetParent(element); up is not null; up = System.Windows.Media.VisualTreeHelper.GetParent(up))
            if (up is System.Windows.Controls.ScrollViewer { IsVisible: true } viewer) return Intersect(rect, ScreenRectOf(viewer));
        return Window.GetWindow(element)?.Content is FrameworkElement { IsVisible: true } root && root != element ? Intersect(rect, ScreenRectOf(root)) : rect;
    }

    public static ScreenRect? Intersect(ScreenRect a, ScreenRect? b)
    {
        if (b is null) return a;
        int x = Math.Max(a.X, b.X), y = Math.Max(a.Y, b.Y), r = Math.Min(a.X + a.Width, b.X + b.Width), d = Math.Min(a.Y + a.Height, b.Y + b.Height);
        return r > x && d > y ? new ScreenRect(x, y, r - x, d - y) : null;
    }

    public static ScreenRect? Union(ScreenRect? a, ScreenRect? b)
    {
        if (a is null || b is null) return a ?? b;
        var x = Math.Min(a.X, b.X); var y = Math.Min(a.Y, b.Y);
        return new ScreenRect(x, y, Math.Max(a.X + a.Width, b.X + b.Width) - x, Math.Max(a.Y + a.Height, b.Y + b.Height) - y);
    }

    /// <summary>Calls <paramref name="tick"/> on the UI thread every <paramref name="period"/> until the result is disposed.</summary>
    public static IDisposable Every(Dispatcher dispatcher, TimeSpan period, Action tick)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = period };
        timer.Tick += (_, _) => tick();
        timer.Start();
        return new Stopper(timer);
    }

    public static IVideoCapture BeginCapture(int width, int height, Func<ScreenRect?> region) => new Capture(new DesktopRegionGrabber(width, height, region));

    private sealed class Stopper : IDisposable
    {
        private readonly DispatcherTimer _timer;
        public Stopper(DispatcherTimer timer) => _timer = timer;
        public void Dispose() => _timer.Stop();
    }

    private sealed class Capture : IVideoCapture
    {
        private readonly DesktopRegionGrabber _grabber;
        public Capture(DesktopRegionGrabber grabber) => _grabber = grabber;
        public bool Grab(byte[] bgra) => _grabber.Grab(bgra);
        public void Dispose() => _grabber.Dispose();
    }
}

/// <summary>The "video saved" row above the status bar (or a warning box for a failure).</summary>
internal sealed class VideoSavedNotice
{
    private readonly Window _window;
    private readonly IStatusNotices _notices;
    private IStatusNotice? _notice;
    private string? _folder;

    public VideoSavedNotice(Window window, IStatusNotices notices) { _window = window; _notices = notices; }

    public void Show(string message, string? openFolder)
    {
        if (openFolder is null) { MessageBox.Show(_window, message, "Record video", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        _folder = openFolder;
        _notice ??= _notices.Create("Video saved", "Open folder", OpenFolder);
        _notice.Show(message);
    }

    private void OpenFolder()
    {
        _notice?.Hide();
        if (_folder is { } folder) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }
}
