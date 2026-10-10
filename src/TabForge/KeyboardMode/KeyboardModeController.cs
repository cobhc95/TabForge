using TabForge.Docking;
using TabForge.Views;

namespace TabForge.KeyboardMode;

// Owns: Keyboard mode as a layout swap: entering takes a copy of the dock arrangement and shows the Keyboard mode layout, leaving puts the copy back exactly.
//   The copy (RealLayout) is what every save writes while the mode is on, so the swapped arrangement is never persisted.
// Does not own: the dock (DockWorkspace), the layout maths (KeyboardModeLayoutSwap), the drawing (KeyboardModeView) or the window's close (MainWindow.KeyboardMode.cs calls Exit).
// Tests: TestKeyboardModeLayout.
internal sealed class KeyboardModeController : IDisposable
{
    private readonly IKeyboardModeHost _host;
    private readonly Func<DockWorkspace?> _dock;
    private readonly KeyboardModePane _pane;
    private readonly Action _sourceChanged;
    private DockWorkspaceState? _snapshot;

    public KeyboardModeController(IKeyboardModeHost host, Func<DockWorkspace?> dock, KeyboardModePane pane, Action sourceChanged)
    {
        _host = host;
        _dock = dock;
        _pane = pane;
        _sourceChanged = sourceChanged;
        _pane.ExitRequested += Exit;
        Changed += TickMenuRow;
    }

    /// <summary>True while the Keyboard mode layout is shown.</summary>
    public bool IsOn => _snapshot is not null;

    /// <summary>On or off changed (the toolbar button follows it).</summary>
    public event Action? Changed;

    /// <summary>The arrangement every save must write while the mode is on (the one it replaced), null when the mode is off.</summary>
    public DockWorkspaceState? RealLayout => _snapshot is null ? null : DockLayoutTree.Clone(_snapshot);

    private KeyboardModeSettings Settings => _host.Settings.Learn ??= new KeyboardModeSettings();

    public void Toggle() { if (IsOn) Exit(); else Enter(); }

    public void Enter()
    {
        if (IsOn || _dock() is not { } dock) return;
        _snapshot = dock.CaptureLayout();
        Changed?.Invoke();
        dock.ApplyLayout(KeyboardModeLayoutSwap.Build(_snapshot, KeyboardModeSettings.ParseSize(Settings.KeyboardSize)));
        dock.SelectPanel(KeyboardModeLayoutSwap.PaneId);
        _sourceChanged();
        _host.SetStatus("Keyboard mode on");
    }

    /// <summary>The View menu row follows the mode with a check mark, like the other view toggles.</summary>
    private void TickMenuRow()
    {
        if (_host.Window.FindName("MainMenu") is not System.Windows.Controls.Menu menu) return;
        if (MenuHotkey.Find(menu.Items, KeyboardModeFeatureModule.CommandId) is { } row) { row.IsCheckable = true; row.IsChecked = IsOn; }
    }

    /// <summary>Puts the arrangement back that Enter took.</summary>
    public void Exit()
    {
        if (_snapshot is not { } before) return;
        if (_dock() is { } dock) dock.ApplyLayout(before);
        _snapshot = null;
        Changed?.Invoke();
        _host.SetStatus("");   // clears the "on" message
    }

    public void Dispose() { _pane.ExitRequested -= Exit; }
}
