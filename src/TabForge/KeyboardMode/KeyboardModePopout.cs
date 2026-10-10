using System.Windows;

namespace TabForge.KeyboardMode;

// Owns: lending the keyboard surface of the Keyboard mode pane to its own window and taking it back: open (needs Keyboard mode on) and close (the player, leaving Keyboard mode,
//   or the main window closing).
// Does not own: the window's contents (KeyboardModePopoutWindow), the pane (KeyboardModePane), the mode (KeyboardModeController), or the session, MIDI listener, judge and wait mode (the main window's Keyboard mode controller).
// Tests: TestKeyboardModePopout.
internal sealed class KeyboardModePopout : IDisposable
{
    private readonly Window _owner;
    private readonly KeyboardModePane _pane;
    private readonly KeyboardModeController _mode;
    private readonly KeyboardModeControls _controls;
    private readonly KeyboardModeFrameController _learn;
    private KeyboardModePopoutWindow? _window;

    public KeyboardModePopout(Window owner, KeyboardModePane pane, KeyboardModeController mode, KeyboardModeFrameController learn, KeyboardModeControls controls)
    {
        _learn = learn;
        learn.PopoutToggle = Toggle;
        _owner = owner; _pane = pane; _mode = mode;
        _controls = controls;
        _pane.PopoutRequested += Toggle;
        _mode.Changed += OnModeChanged;
    }

    /// <summary>The pop-out window while open; null otherwise.</summary>
    public KeyboardModePopoutWindow? Window => _window;
    public bool IsOpen => _window is not null;

    public void Toggle()
    {
        if (_window is not null) { Close(); return; }
        Open();
        if (_window is null) _learn.Status("The keyboard pop-out needs Keyboard mode on");
    }

    /// <summary>Opens the window when Keyboard mode is on with the keyboard surface; otherwise does nothing.</summary>
    public void Open()
    {
        if (_window is not null || !_mode.IsOn || _pane.IsKeysDetached) return;
        var window = new KeyboardModePopoutWindow(_pane.DetachKeys(), _controls);
        if (_owner.IsLoaded) window.Owner = _owner;
        window.ShowActivated = _owner.IsActive;   // an off-screen capture never takes focus
        window.Closed += OnWindowClosed;
        _window = window;
        window.Show();
    }

    /// <summary>Closes the window; the view goes back to the pane.</summary>
    public void Close() => _window?.Close();

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is not KeyboardModePopoutWindow window) return;
        window.Closed -= OnWindowClosed;
        window.ReleaseKeys();   // the view leaves the window's tree before the pane takes it again
        _window = null;
        _pane.AttachKeys();
    }

    private void OnModeChanged() { if (!_mode.IsOn) Close(); }

    public void Dispose()
    {
        _pane.PopoutRequested -= Toggle;
        _mode.Changed -= OnModeChanged;
        _learn.PopoutToggle = null;
        Close();
    }
}
