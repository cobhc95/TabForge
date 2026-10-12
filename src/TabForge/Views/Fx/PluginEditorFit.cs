using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;

namespace TabForge.Views.Fx;

// Owns: fitting a docked plug-in editor in the FX window: growing the window to the editor's size (clamped to the
//       monitor's work area), sizing the host area to the visible part, and the two scroll bars that move the
//       plug-in's view inside the engine's editor window when the editor is larger than the area.
// Does not own: opening / closing editors, the engine's editor window itself (the engine keeps it over the host area),
//       the bypass overlay, floating plug-in windows.
// Tests: TestPluginEditorFit (the pure maths: window fit, clamp, scroll ranges, offsets, DPI).
internal sealed class PluginEditorFit
{
    /// <summary>Margin kept free of the work area when the window grows (DIPs, total per axis).</summary>
    public const double WorkAreaMargin = 40;

    /// <summary>The visible part of the editor and how far it can scroll (same unit as the inputs).</summary>
    public readonly record struct ScrollLayout(double ViewWidth, double ViewHeight, double RangeX, double RangeY);

    /// <summary>Editor pixels to DIPs at a DPI scale (1.5 = 150%).</summary>
    public static Size ToDip(int widthPx, int heightPx, double scaleX, double scaleY) => new(widthPx / scaleX, heightPx / scaleY);

    /// <summary>
    /// The window rectangle that shows the whole editor: grows only (never shrinks), at most the work area less
    /// <paramref name="margin"/>, top-left kept unless the grown window would leave the work area.
    /// <paramref name="chrome"/> is everything of the window that is not the editor area.
    /// </summary>
    public static Rect FitWindow(Rect window, Size chrome, Size editor, Rect work, double margin = WorkAreaMargin)
    {
        var w = Math.Max(window.Width, Math.Min(chrome.Width + editor.Width, work.Width - margin));
        var h = Math.Max(window.Height, Math.Min(chrome.Height + editor.Height, work.Height - margin));
        if (w <= window.Width && h <= window.Height) return window;
        var x = Math.Max(work.Left, Math.Min(window.Left, work.Right - w));
        var y = Math.Max(work.Top, Math.Min(window.Top, work.Bottom - h));
        return new Rect(x, y, w, h);
    }

    /// <summary>Which bars an editor needs in an area (a bar takes <paramref name="bar"/> from the other axis) and the scroll ranges (the overflow).</summary>
    public static ScrollLayout Scroll(double areaWidth, double areaHeight, double editorWidth, double editorHeight, double bar)
    {
        const double slack = 0.5;   // rounding between pixels and DIPs never shows a bar
        bool h = editorWidth > areaWidth + slack, v = editorHeight > areaHeight + slack;
        if (h && !v) v = editorHeight > areaHeight - bar + slack;
        if (v && !h) h = editorWidth > areaWidth - bar + slack;
        double viewW = Math.Max(0, areaWidth - (v ? bar : 0)), viewH = Math.Max(0, areaHeight - (h ? bar : 0));
        return new ScrollLayout(Math.Min(editorWidth, viewW), Math.Min(editorHeight, viewH), h ? editorWidth - viewW : 0, v ? editorHeight - viewH : 0);
    }

    /// <summary>Where the plug-in's view goes inside the editor window for a scroll offset in DIPs (pixels, zero or negative).</summary>
    public static (int X, int Y) ViewOrigin(double offsetX, double offsetY, double scaleX, double scaleY) =>
        (-(int)Math.Round(offsetX * scaleX), -(int)Math.Round(offsetY * scaleY));

    private readonly Window _window;
    private readonly FrameworkElement _frame, _dock;
    private readonly Func<IntPtr> _editorWindow;
    private readonly ScrollBar _horizontal = new() { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed, SmallChange = 40 };
    private readonly ScrollBar _vertical = new() { Orientation = Orientation.Vertical, Visibility = Visibility.Collapsed, SmallChange = 40 };
    private (int W, int H) _editorPx;

    /// <summary>The editor area with its scroll bars: put this where the area goes.</summary>
    public Grid Root { get; } = new();

    /// <param name="frame">The element that is visible while an editor is docked.</param>
    /// <param name="dock">The host area inside it; this class sets its size.</param>
    /// <param name="editorWindow">The engine's docked editor window (zero when there is none).</param>
    public PluginEditorFit(Window window, UIElement content, FrameworkElement frame, FrameworkElement dock, Func<IntPtr> editorWindow)
    {
        _window = window; _frame = frame; _dock = dock; _editorWindow = editorWindow;
        Root.ColumnDefinitions.Add(new ColumnDefinition());
        Root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Root.RowDefinitions.Add(new RowDefinition());
        Root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Root.ClipToBounds = true;
        Root.Background = Brushes.Transparent;   // the wheel reaches the area between the editor and the bars
        Grid.SetColumn(_vertical, 1);
        Grid.SetRow(_horizontal, 1);
        Root.Children.Add(content);
        Root.Children.Add(_vertical);
        Root.Children.Add(_horizontal);
        Root.SizeChanged += (_, _) => Update();
        _frame.IsVisibleChanged += (_, _) => Update();
        _window.DpiChanged += (_, _) => Update();
        _horizontal.ValueChanged += (_, _) => MoveView();
        _vertical.ValueChanged += (_, _) => MoveView();
        // A wheel turn over the plug-in itself goes to the plug-in (its window is in the engine process): only the rest scrolls here.
        Root.MouseWheel += (_, e) => { if (_vertical.Visibility == Visibility.Visible) { _vertical.Value -= e.Delta / 120.0 * 60; e.Handled = true; } };
    }

    /// <summary>The docked editor opened or changed size (pixels): grow the window to show it whole, then lay out.</summary>
    public void SetEditor(int widthPx, int heightPx)
    {
        _editorPx = (widthPx, heightPx);
        _horizontal.Value = 0; _vertical.Value = 0;
        GrowWindow();
        Update();
    }

    private void GrowWindow()
    {
        var handle = new WindowInteropHelper(_window).Handle;
        if (handle == IntPtr.Zero || _window.WindowState != WindowState.Normal || Root.ActualWidth <= 0 || Root.ActualHeight <= 0) return;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(handle, 2 /*MONITOR_DEFAULTTONEAREST*/), ref info)) return;
        var dpi = VisualTreeHelper.GetDpi(_window);
        var work = new Rect(info.Work.Left / dpi.DpiScaleX, info.Work.Top / dpi.DpiScaleY,
            (info.Work.Right - info.Work.Left) / dpi.DpiScaleX, (info.Work.Bottom - info.Work.Top) / dpi.DpiScaleY);
        var now = new Rect(_window.Left, _window.Top, _window.ActualWidth, _window.ActualHeight);
        var chrome = new Size(Math.Max(0, now.Width - Root.ActualWidth), Math.Max(0, now.Height - Root.ActualHeight));
        var fit = FitWindow(now, chrome, ToDip(_editorPx.W, _editorPx.H, dpi.DpiScaleX, dpi.DpiScaleY), work);
        if (fit == now) return;
        _window.Left = fit.Left; _window.Top = fit.Top; _window.Width = fit.Width; _window.Height = fit.Height;
    }

    /// <summary>Sizes the host area to the visible part of the editor and shows the bars the rest needs.</summary>
    private void Update()
    {
        if (!_frame.IsVisible || _editorPx.W <= 0)
        {
            _horizontal.Visibility = _vertical.Visibility = Visibility.Collapsed;
            return;
        }
        var dpi = VisualTreeHelper.GetDpi(_window);
        var editor = ToDip(_editorPx.W, _editorPx.H, dpi.DpiScaleX, dpi.DpiScaleY);
        var layout = Scroll(Root.ActualWidth, Root.ActualHeight, editor.Width, editor.Height, SystemParameters.VerticalScrollBarWidth);
        _dock.Width = layout.ViewWidth; _dock.Height = layout.ViewHeight;
        Set(_horizontal, layout.RangeX, layout.ViewWidth);
        Set(_vertical, layout.RangeY, layout.ViewHeight);
    }

    private static void Set(ScrollBar bar, double range, double view)
    {
        bar.Visibility = range > 0 ? Visibility.Visible : Visibility.Collapsed;
        bar.ViewportSize = view; bar.LargeChange = Math.Max(1, view * 0.9);
        bar.Maximum = range;   // a smaller range pulls the value in, which moves the view
    }

    // ponytail: moves the first child of the engine's editor window (the plug-in's view) from this process;
    // a plug-in that puts its view back needs an engine-side offset message instead.
    private void MoveView()
    {
        var editor = _editorWindow();
        var view = editor == IntPtr.Zero ? IntPtr.Zero : GetWindow(editor, 5 /*GW_CHILD*/);
        if (view == IntPtr.Zero) return;
        var dpi = VisualTreeHelper.GetDpi(_window);
        var (x, y) = ViewOrigin(_horizontal.Value, _vertical.Value, dpi.DpiScaleX, dpi.DpiScaleY);
        // The view belongs to the engine process: ASYNCWINDOWPOS never blocks this thread on it.
        SetWindowPos(view, IntPtr.Zero, x, y, 0, 0, 0x1 /*NOSIZE*/ | 0x4 /*NOZORDER*/ | 0x10 /*NOACTIVATE*/ | 0x4000 /*ASYNCWINDOWPOS*/);
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32")] private static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("user32")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32")] private static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hh, uint flags);
}
