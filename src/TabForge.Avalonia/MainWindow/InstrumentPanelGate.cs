using System;
using TabForge.Avalonia.Views.Docking;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: when the instrument panel (fretboard / keyboard / drum map) is on screen: it shows iff the current mode's remembered choice is on.
//   The switch to the piano roll is a transient change of mode, never a user choice: it applies the piano roll's own remembered value, and the
//   way back to the score applies the Tab mode's value. With no settings (the tests' bare dock) it keeps the older hold-and-restore behaviour.
// Does not own: the toggle itself (MainWindowViewMenu), the dock (DockWorkspaceController), the pane switch (EditorPaneSwitch.ModeChanged),
//   the remembered values (FretboardVisibilityMemory, MainWindowSettings).
// Tests: TabForge.Avalonia.Tests.InstrumentPanelRollHoldTests, FretboardVisibilityMemoryTests.
public sealed class InstrumentPanelGate
{
    private const string PanelId = "instrument";
    private readonly DockWorkspaceController _dock;
    private readonly Action<bool> _showCheck;
    private readonly Func<FretboardMode>? _mode;
    private readonly Func<FretboardMode, bool?>? _remembered;
    private readonly Action<FretboardMode, bool>? _remember;
    private bool _userOn;
    private bool _roll;
    private bool _heldByRoll;   // the panel is closed only because the roll is shown
    private bool _applying;     // the gate itself is moving the pane: a rebuild then is not the user's choice

    /// <param name="showCheck">Sets the check of the View menu row for the panel (the state on screen).</param>
    /// <param name="mode">The editor mode on screen; null keeps the session-only hold (a bare dock with no settings).</param>
    /// <param name="remembered">Reads a mode's remembered value; null treats the piano roll as off.</param>
    /// <param name="remember">Stores a user's choice for a mode (Band mode ignores it); null remembers nothing.</param>
    public InstrumentPanelGate(DockWorkspaceController dock, Action<bool> showCheck,
        Func<FretboardMode>? mode = null, Func<FretboardMode, bool?>? remembered = null, Action<FretboardMode, bool>? remember = null)
    {
        _dock = dock;
        _showCheck = showCheck;
        _mode = mode;
        _remembered = remembered;
        _remember = remember;
        _userOn = remembered?.Invoke(FretboardMode.Tab) ?? dock.IsPanelVisible(PanelId);
        dock.LayoutRebuilt += OnLayoutRebuilt;
    }

    /// <summary>The user's choice: true when the panel would show over the score.</summary>
    public bool UserOn => _userOn;

    /// <summary>The pane switched to the piano roll (true) or back to the score (false).</summary>
    public void RollChanged(bool roll)
    {
        if (roll == _roll) return;
        _roll = roll;
        if (_dock.SuppressInstrumentPanel) { _heldByRoll = false; return; }   // keyboard mode never shows the fretboard
        if (_mode is null)
        {
            if (roll)
            {
                _userOn = _dock.IsPanelVisible(PanelId);
                _heldByRoll = _userOn;
                if (_userOn) _dock.SetPanelVisible(PanelId, false);
            }
            else
            {
                _heldByRoll = false;
                if (_userOn != _dock.IsPanelVisible(PanelId)) _dock.SetPanelVisible(PanelId, _userOn);
            }
        }
        else if (roll)
        {
            _userOn = _remembered?.Invoke(FretboardMode.Tab) ?? _dock.IsPanelVisible(PanelId);
            SetVisible(_remembered?.Invoke(FretboardMode.PianoRoll) ?? false);
        }
        else
        {
            var shown = _remembered?.Invoke(FretboardMode.Tab) ?? _userOn;
            _userOn = shown;
            SetVisible(shown);
        }
        _showCheck(_dock.IsPanelVisible(PanelId));
    }

    // Any rebuild (its own toggle, close button, a layout, the dock's deferred batches): shown means the user wants it; closed means they do, unless the roll closed it.
    private void OnLayoutRebuilt()
    {
        if (_dock.SuppressInstrumentPanel || _applying) return;
        if (_mode is null)
        {
            if (_dock.IsPanelVisible(PanelId)) { _userOn = true; _heldByRoll = false; }
            else if (!_heldByRoll) _userOn = false;
            return;
        }
        var mode = _mode();
        if (mode == FretboardMode.Band) return;   // each band track keeps its own settings: no shared value to write
        var shown = _dock.IsPanelVisible(PanelId);
        if (mode == FretboardMode.PianoRoll) _remember?.Invoke(FretboardMode.PianoRoll, shown);
        else
        {
            _userOn = shown;
            _remember?.Invoke(FretboardMode.Tab, shown);
        }
    }

    /// <summary>Moves the pane for a mode switch, so its rebuild is never mistaken for the user's own choice.</summary>
    private void SetVisible(bool shown)
    {
        if (_dock.IsPanelVisible(PanelId) == shown) return;
        _applying = true;
        try { _dock.SetPanelVisible(PanelId, shown); }
        finally { _applying = false; }
    }
}
