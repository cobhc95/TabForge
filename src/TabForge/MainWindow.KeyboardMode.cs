using System.Windows;
using TabForge.KeyboardMode;

namespace TabForge;

// MainWindow: thin hooks for Keyboard mode (experimental): the pane's controller and the command that shows or hides the pane.
// Owns: the lazily built Keyboard mode controller, its control bar's transport (the window's play, stop, speed and loop handlers) and the View.LearnMode handler.
// Does not own: the pane (KeyboardMode/), its dock row (DockPaneTable) or its settings (KeyboardModeFeatureModule).
// Tests: TestKeyboardModeNoteStream, TestDockPaneTable.
public partial class MainWindow : IKeyboardModeCommandHost
{
    private KeyboardModeFrameController? _learn;
    internal KeyboardModeFrameController Learn => _learn ??= new KeyboardModeFrameController(new KeyboardModeHost(this, () => _documents.Active, _engine, TogglePlayback));

    IKeyboardModeWaitCommands IKeyboardModeCommandHost.WaitCommands => Learn;

    private KeyboardModePane? _learnPane;
    private KeyboardModeController? _learnMode;
    /// <summary>The Keyboard mode pane's content: the header over the keyboard surface.</summary>
    /// <summary>What the Keyboard mode control bars drive: the window's own transport handlers and the mode's settings (kept by the controller).</summary>
    internal KeyboardModeControls KeyboardModeControls => Learn.Controls ??= new KeyboardModeControls(Learn, new KeyboardModeTransport(
        TogglePlayback, StopPlayback, () => _documents.Active.Playback.Engine is { IsPlaying: true, IsPaused: false }, () => _transport.Speed, ApplySpeed, () => _loop, () => SetLoopActive(!_loop)));
    internal KeyboardModePane KeyboardModePane => _learnPane ??= new KeyboardModePane(Learn.Keys = new KeyboardModeView(), new KeyboardModeControlBar(KeyboardModeControls));
    internal KeyboardModeController KeyboardMode => _learnMode ??= CreateKeyboardMode();

    private KeyboardModeController CreateKeyboardMode()
    {
        var mode = new KeyboardModeController(new KeyboardModeHost(this, () => _documents.Active, _engine), () => _dockWorkspace, KeyboardModePane, () => Learn.InvalidateSource());
        Learn.SourceNoteChanged += () => KeyboardModePane.SetNote(Learn.SourceNote);
        _ = new KeyboardModePopout(this, KeyboardModePane, mode, Learn, KeyboardModeControls);   // lives through its event hooks on the pane, the mode and the controller
        mode.Changed += () => KeyboardModeButton.Tag = mode.IsOn ? "on" : null;   // the toolbar button's lit state
        WorkspaceLayouts.RealLayout = () => mode.RealLayout;
        WorkspaceLayouts.LeaveTemporaryLayout = mode.Exit;
        return mode;
    }

    /// <summary>View.LearnMode and the toolbar button: enters the Keyboard mode layout, or puts the arrangement back that it replaced.</summary>
    public void ToggleKeyboardMode() => KeyboardMode.Toggle();
    private void ToggleKeyboardMode_Click(object sender, RoutedEventArgs e) => ToggleKeyboardMode();
}
