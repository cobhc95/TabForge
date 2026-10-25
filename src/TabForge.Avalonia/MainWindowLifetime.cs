using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace TabForge.Avalonia;

// Owns: the window's subscriptions (key routing, the closing question) from attach to the window's Closed, and the close sequence:
//   a close with unsaved songs is held, the save question asked per song, and the close repeated once every song may go.
// Does not own: the key table (MainWindowKeyboard), the songs and the question (MainWindowTabs).
// Tests: TabForge.Avalonia.Tests.MainWindowLifetimeTests.
/// <summary>Attach and detach of the main window's handlers. UI thread only.</summary>
public sealed class MainWindowLifetime
{
    private readonly Window _window;
    private readonly MainWindowKeyboard _keyboard;
    private readonly MainWindowTabs _tabs;
    private readonly IDisposable[] _windowOwned;
    private bool _closeApproved;
    private bool _closing;   // true while a close is being answered (the save question, a running save, the finishing work): a second Closing only cancels

    public MainWindowLifetime(Window window, MainWindowKeyboard keyboard, MainWindowTabs tabs, params IDisposable[] windowOwned)
    {
        _windowOwned = windowOwned;
        _window = window;
        _keyboard = keyboard;
        _tabs = tabs;
        window.AddHandler(InputElement.KeyDownEvent, OnTunnelKeyDown, RoutingStrategies.Tunnel);
        window.KeyDown += OnKeyDown;
        window.Closing += OnClosing;
        window.Closed += OnClosed;
        Shell.Win32.NcHitTestHook.AttachIfWindows(window);
    }

    // Tunnelled: Space reaches Play/Pause before a focused button can take it as a click.
    private void OnTunnelKeyDown(object? sender, KeyEventArgs e)
    {
        if (!e.Handled) e.Handled = _keyboard.RouteBeforeControls(e.Key, e.KeyModifiers, _window.FocusManager?.GetFocusedElement());
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!e.Handled) e.Handled = _keyboard.Route(e.Key, e.KeyModifiers, _window.FocusManager?.GetFocusedElement());
    }

    /// <summary>Work that must end before the window goes (a live video recording finishing its MP4); asked after the save question. Null: nothing.</summary>
    public Func<Task>? FinishBeforeClose { get; init; }

    /// <summary>The longest the close waits for <see cref="FinishBeforeClose"/>: a recording's save watchdog (15 s) plus a margin, so closing never kills a save in progress.</summary>
    public TimeSpan FinishBeforeCloseTimeout { get; init; } = TimeSpan.FromSeconds(20);

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeApproved) return;
        if (_closing) { e.Cancel = true; return; }   // a close question or a running save is already being answered: a second X must not ask again
        if (!_tabs.HasUnsaved && !_tabs.IsSaving)
        {
            if (FinishBeforeClose?.Invoke() is not { IsCompleted: false } pending) return;
            e.Cancel = true;
            CloseAfter(pending);
            return;
        }
        e.Cancel = true;
        AskThenClose();
    }

    // async void: started from the Closing event, which cannot wait; an exception from the question surfaces on the UI thread.
    private async void AskThenClose()
    {
        _closing = true;
        try
        {
            if (!await _tabs.MayCloseWindow()) return;
            if (FinishBeforeClose?.Invoke() is { } pending) await Bounded(pending);
            _closeApproved = true;
            _window.Close();
        }
        finally { _closing = false; }
    }

    // async void: as above; the finishing work reports its own failure, so the window closes either way.
    private async void CloseAfter(Task pending)
    {
        _closing = true;
        try { await Bounded(pending); }
        finally { _closeApproved = true; _window.Close(); }
    }

    // The finishing work gets a bounded time (a stuck encoder must not keep the window open); a failure is reported by the work itself.
    private async Task Bounded(Task pending)
    {
        try { await pending.WaitAsync(FinishBeforeCloseTimeout); }
        catch (TimeoutException) { }   // swallow: close anyway
        catch (Exception) { }   // swallow: the work reports its own failure
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _window.RemoveHandler(InputElement.KeyDownEvent, OnTunnelKeyDown);
        _window.KeyDown -= OnKeyDown;
        _window.Closing -= OnClosing;
        _window.Closed -= OnClosed;
        foreach (var owned in _windowOwned) owned.Dispose();
    }
}
