using System;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using TabForge.Platform;
using TabForge.Shell;
using static TabForge.Shell.NativeHitCodes;

namespace TabForge.Avalonia.Shell.Win32;

// Owns: the Windows window-procedure hook of a borderless Avalonia window from open to close: WM_NCHITTEST answered from
//       ChromeHitRules (resize edges, caption buttons so Windows 11 shows Snap Layouts on maximise, caption as HTCAPTION so
//       Aero Snap, drag and double-click stay native, client otherwise), the caption buttons' native hover, press and click,
//       and a middle-click on the empty caption (WM_NCMBUTTON*, never an Avalonia pointer event) asking the title bar for a tab.
// Does not own: the hit rules (Core/Shell/ChromeHitRules), the title bar (ICaptionButtonRects), the window actions (WindowsChromeAdapter).
// Tests: TabForge.Avalonia.Tests.NcHitTestHookTests (mapping table, attach/detach); the native reply is proven by a SendMessage harness.
/// <summary>Native hit testing for a borderless window on Windows. UI thread only.</summary>
public sealed class NcHitTestHook
{
    /// <summary>The resize border, in DIPs, scaled to physical pixels per monitor.</summary>
    public const double ResizeBorderDips = 6;

    private readonly Window _window;
    private readonly IWindowChrome _chrome;
    private readonly Win32Properties.CustomWndProcHookCallback _callback;
    // The title bar found in the window's visual tree: key none, ceiling one, UI thread; looked up until one is found.
    private ICaptionButtonRects? _bar;
    private ChromeHit? _hot, _pressed;
    // A middle-click that went down on the empty caption: a new tab follows if it is released on the empty caption too.
    private bool _middleCaptionPressed;
    // This window's own click tracker; the Avalonia path has another, and no static is shared between windows.
    private readonly MiddleClickTracker _middle = new();
    // When the window was last activated by a mouse click (Environment.TickCount64), or -1: only a release this soon after can be the eaten half of a click.
    private long _activatedAt = -1;
    /// <summary>How long after a mouse activation a lone middle release still counts as the release of the activating click.</summary>
    public const long LoneReleaseWindowMs = 500;

    public bool IsAttached { get; private set; }

    private NcHitTestHook(Window window, IWindowChrome chrome)
    {
        _window = window;
        _chrome = chrome;
        _callback = WindowProc;
    }

    /// <summary>Hooks <paramref name="window"/> on Windows (now if open, else when it opens) and unhooks it when it closes.</summary>
    public static NcHitTestHook? AttachIfWindows(Window window)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var hook = new NcHitTestHook(window, WindowChromeFactory.Create(window));
        if (window.IsVisible) hook.Attach(); else window.Opened += hook.OnOpened;
        window.Closed += hook.OnClosed;
        return hook;
    }

    private void OnOpened(object? sender, EventArgs e) => Attach();

    private void Attach()
    {
        _window.Opened -= OnOpened;
        if (_window.TryGetPlatformHandle()?.HandleDescriptor != "HWND") return; // headless or another back end: no native hook
        Win32Properties.AddWndProcHookCallback(_window, _callback);
        IsAttached = true;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _window.Opened -= OnOpened;
        _window.Closed -= OnClosed;
        if (IsAttached) Win32Properties.RemoveWndProcHookCallback(_window, _callback);
        IsAttached = false;
        _bar = null;
        _middleCaptionPressed = false;
        _middle.Cancel();
    }

    /// <summary>
    /// The WM_NCHITTEST code for a screen point in physical pixels. <paramref name="windowRect"/> is the native window rectangle,
    /// <paramref name="scale"/> the monitor scale and <paramref name="maximisedInset"/> the physical overhang of a maximised
    /// window past its monitor (Avalonia's OffScreenMargin; ignored: points and button rects share the window-relative space,
    /// so the overhang cancels on both sides). Without a title bar only the
    /// resize edges are answered (0 elsewhere: Avalonia decides). In full screen the whole window is client: the caption row is
    /// hidden (its stale rectangles must not claim the menu bar), nothing drags, resizes or shows Snap Layouts.
    /// </summary>
    public static int HitCode(PixelPoint screen, PixelRect windowRect, double scale, bool maximised, PixelPoint maximisedInset,
        ICaptionButtonRects? bar, bool fullScreen = false)
    {
        if (fullScreen) return HtClient;
        var client = new UiPoint((screen.X - windowRect.X) / scale, (screen.Y - windowRect.Y) / scale);
        var hit = bar is null ? ChromeHit.Client
            : ChromeHitRules.Classify(client, bar.TabBounds, bar.TabActionBounds, bar.CaptionButtons, bar.CaptionBounds);
        // Caption buttons win over the resize border, as in the window (its caption-button hook runs before its resize hook):
        // the top row above Minimise, Maximise and Close and the right column beside Close press the button, so the top-right
        // corner pixel is Close in a normal and a maximised window alike.
        if (hit is ChromeHit.MinButton or ChromeHit.MaxButton or ChromeHit.CloseButton) return CodeOfHit(hit);
        var edge = ChromeHitRules.ClassifyResizeEdge(new UiPoint(screen.X, screen.Y),
            new UiRect(windowRect.X, windowRect.Y, windowRect.Width, windowRect.Height), ResizeBorderDips * scale, maximised);
        if (edge is { } resize) return CodeOf(resize);
        return bar is null ? 0 : CodeOfHit(hit);
    }

    private static int CodeOfHit(ChromeHit hit) => hit switch
    {
        ChromeHit.MinButton => HtMinButton,
        ChromeHit.MaxButton => HtMaxButton,
        ChromeHit.CloseButton => HtClose,
        ChromeHit.Caption => HtCaption,
        _ => HtClient,
    };

    internal static int CodeOf(ResizeEdge edge) => edge switch
    {
        ResizeEdge.Left => HtLeft,
        ResizeEdge.Right => HtRight,
        ResizeEdge.Top => HtTop,
        ResizeEdge.Bottom => HtBottom,
        ResizeEdge.TopLeft => HtTopLeft,
        ResizeEdge.TopRight => HtTopRight,
        ResizeEdge.BottomLeft => HtBottomLeft,
        ResizeEdge.BottomRight => HtBottomRight,
        _ => throw new ArgumentOutOfRangeException(nameof(edge), edge, "Unknown resize edge."),
    };

    private static ChromeHit? ButtonOf(int code) => code switch
    {
        HtMinButton => ChromeHit.MinButton,
        HtMaxButton => ChromeHit.MaxButton,
        HtClose => ChromeHit.CloseButton,
        _ => null,
    };

    private IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (message)
        {
            case WmNcHitTest:
            {
                var code = HitCodeAt(hwnd, ScreenPointOf(lParam));
                if (code == 0) return IntPtr.Zero;
                handled = true;
                return new IntPtr(code);
            }
            case WmNcMouseMove:
                ArmLeaveTracking(hwnd); // one-shot: re-armed on every non-client move, so the leave is never missed
                SetHot(ButtonOf((int)wParam)); // not handled: Windows still runs its Snap Layouts hover timer
                return IntPtr.Zero;
            case WmNcMouseLeave:
            case WmMouseMove when _hot is not null && _pressed is null:
                if (!GetCursorPos(out var cursor) || ButtonOf(HitCodeAt(hwnd, new PixelPoint(cursor.X, cursor.Y))) is null) SetHot(null);
                return IntPtr.Zero;
            case WmNcLButtonDown when ButtonOf((int)wParam) is { } down:
                SetPressed(down);
                SetCapture(hwnd); // the release arrives as WM_LBUTTONUP even if the pointer left the button
                handled = true;
                return IntPtr.Zero;
            case WmNcLButtonUp or WmLButtonUp when _pressed is { } pressed:
            {
                SetPressed(null);
                ReleaseCapture();
                var releasedOn = GetCursorPos(out var at) ? ButtonOf(HitCodeAt(hwnd, new PixelPoint(at.X, at.Y))) : null;
                if (releasedOn == pressed) Invoke(pressed);
                handled = true;
                return IntPtr.Zero;
            }
            // A middle-click on the empty caption opens a tab on release. The press counts whatever code Windows gave it (the top resize
            // band answers HTTOP, a corner HTTOPLEFT) as long as the point is empty caption, so no part of the title row is a dead spot.
            case WmNcMButtonDown when MiddleDown(hwnd, ScreenPointOf(lParam)):
                _middleCaptionPressed = true;
                SetCapture(hwnd); // the release arrives as WM_MBUTTONUP even if the pointer left the caption
                handled = true;
                return IntPtr.Zero;
            // Released within 8 DIPs of the press and on empty caption too (any HT code): a tab, button or client release does nothing.
            case WmNcMButtonUp or WmMButtonUp when _middleCaptionPressed:
            {
                _middleCaptionPressed = false;
                ReleaseCapture();
                if (GetCursorPos(out var middleAt) && MiddleUp(hwnd, new PixelPoint(middleAt.X, middleAt.Y))) _bar?.NativeMiddleClick();
                handled = true;
                return IntPtr.Zero;
            }
            // The press never reached us (the first click that activates the window can be eaten): a lone release on empty caption still opens the tab.
            // A release long after activation is the end of a drag that began in another app, so it does nothing.
            case WmNcMButtonUp when _window.WindowState != WindowState.FullScreen && AcceptsLoneRelease(Environment.TickCount64, _activatedAt) && WindowRectOf(hwnd) is { } rect && MiddleClickAlone(ScreenPointOf(lParam), rect):
                _bar?.NativeMiddleClick();
                handled = true;
                return IntPtr.Zero;
            case WmMouseActivate:
                _activatedAt = Environment.TickCount64;
                return IntPtr.Zero;
            case WmCaptureChanged: // capture lost (Alt-Tab, another app): the pending middle press is void
                _middleCaptionPressed = false;
                _middle.Cancel();
                return IntPtr.Zero;
            default:
                return IntPtr.Zero;
        }
    }

    /// <summary>True when a lone middle release (its press never seen) is within <see cref="LoneReleaseWindowMs"/> after the activation at <paramref name="activatedAt"/> (-1: never).</summary>
    public static bool AcceptsLoneRelease(long now, long activatedAt) => activatedAt >= 0 && now - activatedAt is >= 0 and <= LoneReleaseWindowMs;

    private bool MiddleClickAlone(PixelPoint screen, PixelRect windowRect) =>
        _bar is { MiddleClickEnabled: true } && IsMiddleClickSpot(screen, windowRect, _window.RenderScaling, _bar);

    /// <summary>True when a screen point is empty title-row space for a middle-click: the caption, including the resize band (not a tab, tab action or caption button).</summary>
    public static bool IsMiddleClickSpot(PixelPoint screen, PixelRect windowRect, double scale, ICaptionButtonRects? bar) =>
        bar is not null && ChromeHitRules.Classify(new UiPoint((screen.X - windowRect.X) / scale, (screen.Y - windowRect.Y) / scale),
            bar.TabBounds, bar.TabActionBounds, bar.CaptionButtons, bar.CaptionBounds) == ChromeHit.Caption;

    /// <summary>
    /// A native middle press: starts a click (tracked in DIPs) when the setting is on and the point is empty caption. Per instance, so each
    /// window has its own state. True when the press was taken.
    /// </summary>
    public bool MiddleDown(PixelPoint screen, PixelRect windowRect, double scale, ICaptionButtonRects? bar)
    {
        if (bar is not { MiddleClickEnabled: true } || !IsMiddleClickSpot(screen, windowRect, scale, bar)) return false;
        _middle.Press(DipsOf(screen, windowRect, scale));
        return true;
    }

    /// <summary>A native middle release: true (open a tab) only when it is empty caption too and within 8 DIPs of the press.</summary>
    public bool MiddleUp(PixelPoint screen, PixelRect windowRect, double scale, ICaptionButtonRects? bar)
    {
        if (!IsMiddleClickSpot(screen, windowRect, scale, bar)) { _middle.Cancel(); return false; }
        return _middle.Release(DipsOf(screen, windowRect, scale));
    }

    private static UiPoint DipsOf(PixelPoint screen, PixelRect windowRect, double scale) =>
        new((screen.X - windowRect.X) / scale, (screen.Y - windowRect.Y) / scale);

    private bool MiddleDown(IntPtr hwnd, PixelPoint screen) => _window.WindowState != WindowState.FullScreen && WindowRectOf(hwnd) is { } r && MiddleDown(screen, r, _window.RenderScaling, _bar);

    private bool MiddleUp(IntPtr hwnd, PixelPoint screen) => _window.WindowState != WindowState.FullScreen && WindowRectOf(hwnd) is { } r && MiddleUp(screen, r, _window.RenderScaling, _bar);

    private PixelRect? WindowRectOf(IntPtr hwnd)
    {
        if (!GetWindowRect(hwnd, out var r)) return null;
        _bar ??= _window.GetVisualDescendants().OfType<ICaptionButtonRects>().FirstOrDefault();
        return new PixelRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    }

    private int HitCodeAt(IntPtr hwnd, PixelPoint screen)
    {
        if (!GetWindowRect(hwnd, out var r)) return 0;
        var scale = _window.RenderScaling;
        var margin = _window.OffScreenMargin;
        var inset = new PixelPoint((int)Math.Round(margin.Left * scale), (int)Math.Round(margin.Top * scale));
        _bar ??= _window.GetVisualDescendants().OfType<ICaptionButtonRects>().FirstOrDefault();
        return HitCode(screen, new PixelRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top), scale,
            _window.WindowState == WindowState.Maximized, inset, _bar, _window.WindowState == WindowState.FullScreen);
    }

    private void Invoke(ChromeHit button)
    {
        SetHot(null);
        switch (button)
        {
            case ChromeHit.MinButton: _chrome.Minimize(); break;
            case ChromeHit.MaxButton: _chrome.ToggleMaximise(); break;
            case ChromeHit.CloseButton: _chrome.Close(); break;
        }
    }

    // Every non-client move reports the button under the pointer: repeating the same button is harmless (the title bar's SetHot only
    // sets pseudo-classes), and skipping it left the hover stale when a WM_NCMOUSELEAVE never arrived.
    private void SetHot(ChromeHit? button)
    {
        _hot = button;
        _bar?.SetHot(button);
    }

    // Tracking is one-shot (Windows stops it when it posts WM_NCMOUSELEAVE), so each entry into the non-client area or a caption button
    // re-arms it: the leave then clears a hot button even when the pointer jumps straight out of the window.
    private static void ArmLeaveTracking(IntPtr hwnd)
    {
        var track = new NativeTrackMouseEvent
        {
            Size = (uint)Marshal.SizeOf<NativeTrackMouseEvent>(), Flags = TmeLeave | TmeNonClient, Window = hwnd, HoverTime = 0,
        };
        TrackMouseEvent(ref track);
    }

    private void SetPressed(ChromeHit? button)
    {
        _pressed = button;
        _bar?.SetPressed(button);
    }

    private static PixelPoint ScreenPointOf(IntPtr lParam)
    {
        var value = lParam.ToInt64();
        return new PixelPoint((short)(value & 0xFFFF), (short)((value >> 16) & 0xFFFF));
    }

    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern IntPtr SetCapture(IntPtr hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TrackMouseEvent(ref NativeTrackMouseEvent track);

    // TME_LEAVE asks for WM_NCMOUSELEAVE; TME_NONCLIENT says the pointer is tracked against the window's non-client area.
    private const uint TmeLeave = 0x00000002, TmeNonClient = 0x00000010;

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }

    // TRACKMOUSEEVENT: cbSize, dwFlags, hwndTrack, dwHoverTime; sequential layout pads hwndTrack to 8 bytes and the struct to 24 on x64.
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeTrackMouseEvent { public uint Size, Flags; public IntPtr Window; public uint HoverTime; }
}
