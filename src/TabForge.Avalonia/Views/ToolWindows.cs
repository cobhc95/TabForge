using System;
using System.Collections.Generic;
using Avalonia.Controls;

namespace TabForge.Avalonia.Views;

// Owns: the open tool windows of one owner (the mixer, Settings, the live FX chain, a track's output window, the keyboard pop-out): one window per kind and key,
//   a second open brings the first forward (restored when minimised) instead of making another; the entry goes when the window closes.
// Does not own: the windows themselves, nor the FX chain windows of tracks (FxChainWindows, which brings its windows forward through here).
// Tests: TabForge.Avalonia.Tests.ToolWindowsTests.
// Threads: UI thread only. Key: the kind and a key of the caller's choosing (a track's id or a song, or none for a window of which there is one); ceiling: one window per kind and key.
public sealed class ToolWindows
{
    private readonly Dictionary<(string Kind, object Key), Window> _open = new();

    /// <summary>The open window of the kind and key, or null.</summary>
    public Window? Find(string kind, object? key = null) => _open.TryGetValue((kind, key ?? ""), out var window) ? window : null;

    /// <summary>Shows the window <paramref name="create"/> makes over <paramref name="over"/>, or brings the open one of that kind and key forward (then nothing is made).</summary>
    public T OpenOrActivate<T>(string kind, object? key, Func<T> create, Window? over, Action<T>? show = null) where T : Window
    {
        key ??= "";
        if (Find(kind, key) is T existing && existing.IsVisible) { Bring(existing); return existing; }
        var window = create();
        _open[(kind, key)] = window;
        window.Closed += (_, _) => { if (_open.TryGetValue((kind, key), out var current) && ReferenceEquals(current, window)) _open.Remove((kind, key)); };
        if (show is not null) show(window); else if (over is null) window.Show(); else window.Show(over);
        return window;
    }

    /// <summary>Restores the window when minimised and brings it to the front.</summary>
    public void Bring(Window window)
    {
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
    }
}
