using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace TabForge.KeyboardMode;

// Owns: the pop-out window: Keyboard mode's control bar (KeyboardModeControlBar, the same controls as in the pane) plus a full screen button over the hosted keyboard view;
//   F11 toggles full screen and Esc leaves it.
// Does not own: the hosted view (lent by KeyboardModePane, handed back by KeyboardModePopout), what the controls do (KeyboardModeControls), or the session, MIDI, judge or wait mode
//   (all stay with the main window's Keyboard mode controller).
// Tests: TestKeyboardModePopout.
internal sealed class KeyboardModePopoutWindow : Window
{
    private readonly Button _full = new() { Padding = new Thickness(8, 1, 8, 1), MinHeight = 26 };
    private FrameworkElement? _keys;
    private bool _fullScreen;
    private WindowState _stateBefore;

    public KeyboardModePopoutWindow(FrameworkElement keys, KeyboardModeControls controls)
    {
        Title = "Keyboard mode: view";
        Width = 1100; Height = 680; MinWidth = 520; MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        AutomationProperties.SetName(this, "Keyboard mode keyboard view");

        Bar = new KeyboardModeControlBar(controls);
        _full.ToolTip = "Full screen on or off (F11; Esc leaves it)";
        AutomationProperties.SetName(_full, "Toggle full screen");
        _full.Click += (_, _) => ToggleFullScreen();
        Bar.AddExtra(_full);

        _keys = keys;
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(keys, 1);
        grid.Children.Add(Bar);
        grid.Children.Add(keys);
        Content = grid;
        SyncFull();
        Loaded += (_, _) => keys.Focus();
    }

    /// <summary>The window's control bar.</summary>
    public KeyboardModeControlBar Bar { get; }

    /// <summary>Lets go of the hosted view (it leaves the window's tree) so the pane can take it back.</summary>
    public void ReleaseKeys()
    {
        if (_keys is not null && Content is Grid grid) grid.Children.Remove(_keys);
        _keys = null;
        Content = null;
    }

    /// <summary>True while the window covers the screen.</summary>
    public bool IsFullScreen => _fullScreen;

    private void SyncFull() => _full.Content = _fullScreen ? "Leave full screen" : "Full screen";

    public void ToggleFullScreen()
    {
        if (!_fullScreen)
        {
            _stateBefore = WindowState;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Normal; WindowState = WindowState.Maximized;
            _fullScreen = true;
        }
        else
        {
            WindowStyle = WindowStyle.SingleBorderWindow; ResizeMode = ResizeMode.CanResize;
            WindowState = _stateBefore;
            _fullScreen = false;
        }
        SyncFull();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.F11) { ToggleFullScreen(); e.Handled = true; }
        else if (e.Key == Key.Escape && _fullScreen) { ToggleFullScreen(); e.Handled = true; }
    }
}
