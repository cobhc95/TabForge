using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;

namespace TabForge.Shell;

/// <summary>Hover / pressed state of a caption button that is driven by native (non-client) mouse messages.</summary>
public static class CaptionButtonState
{
    public static readonly DependencyProperty IsHotProperty = DependencyProperty.RegisterAttached(
        "IsHot", typeof(bool), typeof(CaptionButtonState), new FrameworkPropertyMetadata(false));
    public static readonly DependencyProperty IsDownProperty = DependencyProperty.RegisterAttached(
        "IsDown", typeof(bool), typeof(CaptionButtonState), new FrameworkPropertyMetadata(false));

    public static bool GetIsHot(DependencyObject d) => (bool)d.GetValue(IsHotProperty);
    public static void SetIsHot(DependencyObject d, bool value) => d.SetValue(IsHotProperty, value);
    public static bool GetIsDown(DependencyObject d) => (bool)d.GetValue(IsDownProperty);
    public static void SetIsDown(DependencyObject d, bool value) => d.SetValue(IsDownProperty, value);
}

/// <summary>
/// WPF caption hit testing. WPF paints the caption controls; Win32 receives native caption-button hit
/// codes so Windows 11 shows the Snap Layouts flyout over the maximise button.
/// Once WM_NCHITTEST answers HTMAXBUTTON (or HTMINBUTTON / HTCLOSE) Windows stops sending client mouse messages
/// for that spot, so this class also owns what WPF would have done: hover / pressed visuals from
/// WM_NCMOUSEMOVE / WM_NCMOUSELEAVE, and the click from WM_NCLBUTTONDOWN + release.
/// The hit point comes from the message's own screen coordinates (never GetCursorPos), in physical pixels, and is
/// compared with the buttons' physical-pixel screen rectangles, so DPI scaling and the maximised offset are exact.
/// </summary>
internal sealed class WpfCaptionButtonFrame : IDisposable
{
    private const int WmMouseMove = 0x0200;
    private const int WmLButtonUp = 0x0202;
    private const int WmNcHitTest = 0x0084;
    private const int WmNcMouseMove = 0x00A0;
    private const int WmNcLButtonDown = 0x00A1;
    private const int WmNcLButtonUp = 0x00A2;
    private const int WmNcMouseLeave = 0x02A2;
    private const int HtMinButton = 8;
    private const int HtMaxButton = 9;
    private const int HtClose = 20;
    private readonly Window _window;
    private readonly FrameworkElement _minimize;
    private readonly FrameworkElement _maximize;
    private readonly FrameworkElement _close;
    private HwndSource? _source;
    private FrameworkElement? _hot;
    private FrameworkElement? _pressed;

    public WpfCaptionButtonFrame(Window window, FrameworkElement minimize, FrameworkElement maximize, FrameworkElement close)
    {
        _window = window;
        _minimize = minimize;
        _maximize = maximize;
        _close = close;
        _poll.Tick += OnPoll;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero) Attach(handle);
        else window.SourceInitialized += OnSourceInitialized;
    }

    /// <summary>The native hit code (HTMINBUTTON / HTMAXBUTTON / HTCLOSE) for a screen point in physical pixels, or 0.</summary>
    internal int HitCode(Point screen)
    {
        if (double.IsNaN(screen.X)) return 0;
        if (Contains(_close, screen)) return HtClose;
        if (Contains(_maximize, screen)) return HtMaxButton;
        if (Contains(_minimize, screen)) return HtMinButton;
        return 0;
    }

    private static bool Contains(FrameworkElement element, Point screen)
    {
        var rect = ScreenPoints.ScreenRect(element);
        return !rect.IsEmpty && rect.Contains(screen);
    }

    private void OnSourceInitialized(object? sender, EventArgs e) =>
        Attach(new WindowInteropHelper(_window).Handle);

    private void Attach(IntPtr handle)
    {
        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WindowProc);
    }

    private FrameworkElement? ElementFor(int hit) => hit switch
    {
        HtClose => _close,
        HtMaxButton => _maximize,
        HtMinButton => _minimize,
        _ => null
    };

    private readonly System.Windows.Threading.DispatcherTimer _poll = new() { Interval = TimeSpan.FromMilliseconds(60) };

    private void SetHot(FrameworkElement? element)
    {
        if (ReferenceEquals(_hot, element)) return;
        Log($"hot {NameOf(_hot)} -> {NameOf(element)}");
        if (_hot is not null) CaptionButtonState.SetIsHot(_hot, false);
        _hot = element;
        if (_hot is not null)
        {
            CaptionButtonState.SetIsHot(_hot, true);
            _hot.InvalidateVisual();
        }
        // Native (non-client) messages do not trigger a repaint by themselves: nudge the button at render priority.
        var repaint = _hot;
        if (repaint is not null)
            repaint.Dispatcher.BeginInvoke(new Action(() => { repaint.InvalidateVisual(); repaint.UpdateLayout(); }), System.Windows.Threading.DispatcherPriority.Render);
        // Windows may never send WM_NCMOUSELEAVE for these synthetic caption hits, so while a button is hot the cursor is
        // polled (only then) and the highlight is dropped as soon as the pointer is elsewhere.
        if (_hot is null) _poll.Stop();
        else if (!_poll.IsEnabled) _poll.Start();
    }

    private void OnPoll(object? sender, EventArgs e)
    {
        if (_hot is null) { _poll.Stop(); return; }
        var cursor = CursorScreenPoint();
        var over = ElementFor(HitCode(cursor));
        if (!ReferenceEquals(over, _hot)) SetHot(over);
    }

    // Opt-in diagnostics: with TABFORGE_CAPTION_LOG set, hot-state changes are appended to the temp folder's tabforge-caption.log.
    // TABFORGE_TRACE=ui sends the same lines to the diagnostics folder's trace-ui.log.
    private static readonly bool LogEnabled = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TABFORGE_CAPTION_LOG"));

    private static void Log(string text)
    {
        if (!LogEnabled) { Services.Trace.Write(Services.Trace.Ui, "caption " + text); return; }
        try { System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tabforge-caption.log"), $"{DateTime.Now:HH:mm:ss.fff} {text}{Environment.NewLine}"); }
        catch (System.IO.IOException) { } // Not logged: this is the caption log writer; logging here would recurse.
    }

    private string NameOf(FrameworkElement? element) =>
        element is null ? "none" : ReferenceEquals(element, _close) ? "close" : ReferenceEquals(element, _maximize) ? "max" : ReferenceEquals(element, _minimize) ? "min" : "?";

    private static string Fmt(Point p) => $"({p.X:0},{p.Y:0})";

    private void ClearPressed()
    {
        if (_pressed is not null) CaptionButtonState.SetIsDown(_pressed, false);
        _pressed = null;
    }

    private static Point ScreenPointOf(IntPtr lParam)
    {
        var value = lParam.ToInt64();
        return new Point((short)(value & 0xFFFF), (short)((value >> 16) & 0xFFFF));
    }

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (!_window.IsVisible) return IntPtr.Zero;
        try
        {
            switch (message)
            {
                case WmNcHitTest:
                {
                    var hit = HitCode(ScreenPointOf(lParam));
                    if (hit == 0) return IntPtr.Zero;
                    handled = true;
                    return new IntPtr(hit);
                }
                case WmNcMouseMove:
                {
                    var element = ElementFor((int)wParam);
                    SetHot(element);
                    return IntPtr.Zero; // Windows still runs its own handling (Snap Layouts hover timer)
                }
                case WmNcMouseLeave:
                    if (ElementFor(HitCode(CursorScreenPoint())) is null) SetHot(null); // ignore a leave while still over a button
                    return IntPtr.Zero;
                case WmNcLButtonDown:
                {
                    var element = ElementFor((int)wParam);
                    if (element is null) return IntPtr.Zero;
                    _pressed = element;
                    CaptionButtonState.SetIsDown(element, true);
                    SetCapture(hwnd); // the release arrives even if the pointer left the button
                    handled = true;
                    return IntPtr.Zero;
                }
                case WmLButtonUp when _pressed is not null:
                {
                    var pressed = _pressed;
                    ClearPressed();
                    ReleaseCapture();
                    var releasedOn = ElementFor(HitCode(CursorScreenPoint()));
                    if (ReferenceEquals(releasedOn, pressed) && pressed is ButtonBase button)
                        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
                    handled = true;
                    return IntPtr.Zero;
                }
                case WmNcLButtonUp when _pressed is not null:
                    ClearPressed();
                    ReleaseCapture();
                    return IntPtr.Zero;
            }
        }
        catch (InvalidOperationException) { } // mid-layout / disconnected visual: keep WPF's normal hit test // Not logged: hit test on every pointer move.
        return IntPtr.Zero;
    }

    private static Point CursorScreenPoint() => GetCursorPos(out var p) ? new Point(p.X, p.Y) : new Point(double.NaN, double.NaN);

    public void Dispose()
    {
        _window.SourceInitialized -= OnSourceInitialized;
        _source?.RemoveHook(WindowProc);
        _source = null;
        _poll.Stop();
        _poll.Tick -= OnPoll;
        SetHot(null);
        ClearPressed();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr SetCapture(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }
}
