using TabForge.Controllers;

namespace TabForge.CompactMode;

/// <summary>What compact mode needs from the arrangement panel (the track rows).</summary>
internal interface ICompactModeRows
{
    /// <summary>Hides every track row and shows the master track lane at twice its height; the user's own settings stay as they are.</summary>
    bool CompactMode { get; set; }
    double ActualHeight { get; }
    double PreferredHeight();
}

// Owns: compact mode's on/off state for one window, the track-list pane height around it (shrunk to the lane while on, put back after) and hiding the side panel while it is on (restored only when compact mode hid it and the user left it hidden).
// Does not own: hiding the rows and the lane's geometry (ArrangementPanel.CompactMode.cs, MasterLane.cs), the command, hotkey, menu row and button (CompactModeFeatureModule.cs).
// Tests: TestCompactMode.
internal sealed class CompactModeController
{
    private readonly ICompactModeRows _rows;
    private readonly Func<ITrackListDock?> _dock;
    private readonly Func<bool>? _sideShown;
    private readonly Action? _toggleSide;
    private double _heightBefore;
    private bool _hidSide, _watching;

    /// <summary><paramref name="sideShown"/> and <paramref name="toggleSide"/> are the window's side panel: its state and its own show/hide path.</summary>
    public CompactModeController(ICompactModeRows rows, Func<ITrackListDock?> dock, Func<bool>? sideShown = null, Action? toggleSide = null)
    {
        _rows = rows;
        _dock = dock;
        _sideShown = sideShown;
        _toggleSide = toggleSide;
    }

    public event Action? Changed;

    public bool IsOn => _rows.CompactMode;

    public void Toggle() => Set(!IsOn);

    public void Set(bool on)
    {
        if (on == IsOn) return;
        if (on) HideSide();
        if (on) _heightBefore = _rows.ActualHeight + TrackListFitController.Chrome;
        _rows.CompactMode = on;
        // Leaving puts the pane back at the height it had when compact mode started, whatever it was dragged to meanwhile.
        var height = on ? _rows.PreferredHeight() : _heightBefore;
        if (height > TrackListFitController.Chrome) _dock()?.FitPanelHeight(TrackListFitController.PanelId, height);
        if (!on && _hidSide && _sideShown?.Invoke() == false) _toggleSide?.Invoke();   // only what compact mode hid comes back
        if (!on) _hidSide = false;
        Changed?.Invoke();
    }

    /// <summary>Hides the side panel when it is shown, before the pane height is taken. A side panel the user shows again while compact mode is on is theirs: it is not hidden again and not touched on leaving.</summary>
    private void HideSide()
    {
        if (!_watching && _dock() is { } dock)
        {
            _watching = true;
            dock.LayoutChanged += (_, _) => { if (IsOn && _sideShown?.Invoke() == true) _hidSide = false; };
        }
        if (_sideShown?.Invoke() != true) return;
        _toggleSide?.Invoke();
        _hidSide = true;
    }
}
