using System;
using Avalonia.Controls;
using Avalonia.Input;
using TabForge.Platform;

namespace TabForge.Avalonia.Platform;

/// <summary>
/// <see cref="IWindowChrome"/> over a plain Avalonia window: the system move and resize drags, the caption actions and the window state.
/// Owns: the mapping of chrome calls to Window calls. Does not own: hit testing the frame (the Win32 hook) or the title row (Shell/TitleBarView).
/// Tests: TabForge.Avalonia.Tests.TitleBarTests.
/// </summary>
public class AvaloniaWindowChrome : IWindowChrome
{
    private readonly Window _window;
    // The press that starts a drag: the system drag needs the pointer event, so the title row records it before calling BeginMove.
    // Taken (and cleared) by the next BeginMove or BeginResize, so a later drag without a new press does nothing.
    private PointerPressedEventArgs? _press;

    public AvaloniaWindowChrome(Window window)
    {
        _window = window;
        window.PropertyChanged += (_, e) => { if (e.Property == Window.WindowStateProperty) StateChanged?.Invoke(this, EventArgs.Empty); };
    }

    /// <summary>The window this chrome acts on.</summary>
    protected Window HostWindow => _window;

    public WindowChromeState State => _window.WindowState switch
    {
        WindowState.Maximized => WindowChromeState.Maximised,
        WindowState.Minimized => WindowChromeState.Minimised,
        WindowState.FullScreen => WindowChromeState.FullScreen,
        _ => WindowChromeState.Normal,
    };

    public event EventHandler? StateChanged;

    /// <summary>Remembers the pointer press the next <see cref="BeginMove"/> or <see cref="BeginResize"/> drags from.</summary>
    public void NotePress(PointerPressedEventArgs press) => _press = press;

    /// <summary>True in full screen: the window is fixed there, so no move, resize or maximise toggle runs.</summary>
    protected bool IsFullScreen => _window.WindowState == WindowState.FullScreen;

    public virtual void BeginMove() { if (TakePress() is { } press && !IsFullScreen) _window.BeginMoveDrag(press); }

    public virtual void BeginResize(ResizeEdge edge) { if (TakePress() is { } press && !IsFullScreen) _window.BeginResizeDrag(EdgeOf(edge), press); }

    private PointerPressedEventArgs? TakePress()
    {
        var press = _press;
        _press = null;
        return press;
    }

    public void Minimize() => _window.WindowState = WindowState.Minimized;

    public void ToggleMaximise() { if (!IsFullScreen) _window.WindowState = _window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; }

    public void Close() => _window.Close();

    /// <summary>The Avalonia window edge of a chrome resize edge.</summary>
    private static WindowEdge EdgeOf(ResizeEdge edge) => edge switch
    {
        ResizeEdge.Left => WindowEdge.West,
        ResizeEdge.Top => WindowEdge.North,
        ResizeEdge.Right => WindowEdge.East,
        ResizeEdge.Bottom => WindowEdge.South,
        ResizeEdge.TopLeft => WindowEdge.NorthWest,
        ResizeEdge.TopRight => WindowEdge.NorthEast,
        ResizeEdge.BottomLeft => WindowEdge.SouthWest,
        ResizeEdge.BottomRight => WindowEdge.SouthEast,
        _ => throw new ArgumentOutOfRangeException(nameof(edge), edge, "Unknown resize edge."),
    };
}
