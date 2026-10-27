using System;
using TabForge.Avalonia.Views.Docking;

namespace TabForge.Avalonia;

// Owns: when the instrument panel (fretboard / keyboard / drum map) is on screen: it shows iff the user's own choice (the panel's toggle, close button or a
//   layout) is on AND the score is shown, never the piano roll. The roll's hiding is a transient hold kept here; it never changes the user's choice, so
//   every way back to the score (a track or song change, a tab click, a pill, the menu, undo) restores exactly what the user had.
// Does not own: the toggle itself (MainWindowViewMenu), the dock (DockWorkspaceController), the pane switch (EditorPaneSwitch.ModeChanged).
// Tests: TabForge.Avalonia.Tests.InstrumentPanelGateTests.
public sealed class InstrumentPanelGate
{
    private const string PanelId = "instrument";
    private readonly DockWorkspaceController _dock;
    private readonly Action<bool> _showCheck;
    private bool _userOn;
    private bool _roll;
    private bool _heldByRoll;   // the panel is closed only because the roll is shown

    /// <param name="showCheck">Sets the check of the View menu row for the panel (the state on screen).</param>
    public InstrumentPanelGate(DockWorkspaceController dock, Action<bool> showCheck)
    {
        _dock = dock;
        _showCheck = showCheck;
        _userOn = dock.IsPanelVisible(PanelId);
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
        _showCheck(_dock.IsPanelVisible(PanelId));
    }

    // Any rebuild (its own toggle, close button, a layout, the dock's deferred batches): shown means the user wants it; closed means they do, unless the roll closed it.
    private void OnLayoutRebuilt()
    {
        if (_dock.SuppressInstrumentPanel) return;
        if (_dock.IsPanelVisible(PanelId)) { _userOn = true; _heldByRoll = false; }
        else if (!_heldByRoll) _userOn = false;
    }
}
