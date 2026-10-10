using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace TabForge.KeyboardMode;

// Owns: the Keyboard mode pane's content: a slim header (the view's name and a note about left-out keys, the pop-out button, the close button that exits the mode), the control bar
//   (KeyboardModeControlBar) and the keyboard surface below them.
// Does not own: choosing the layout (KeyboardModeController) or drawing the view (KeyboardModeView).
// Tests: TestKeyboardModeLayout.
internal sealed class KeyboardModePane : Grid
{
    public const string Title = "Keyboard view";

    private readonly IKeyboardModeSurface _keys;
    private readonly TextBlock _label = new() { Text = Title, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Button _popout = new() { Content = "Pop out", Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(0, 2, 6, 2), VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _exit = new() { Content = "✕", Width = 26, Margin = new Thickness(0, 2, 4, 2), Padding = new Thickness(0), ToolTip = "Exit Keyboard mode and restore the previous layout" };
    private bool _detached;
    private string _note = "";

    public KeyboardModePane(IKeyboardModeSurface keys, FrameworkElement? controlBar = null)
    {
        _keys = keys;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _label.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        var header = new DockPanel { LastChildFill = true, MinHeight = 26 };
        header.SetResourceReference(BackgroundProperty, "PanelBrush");
        DockPanel.SetDock(_exit, Dock.Right);
        header.Children.Add(_exit);
        AutomationProperties.SetName(_popout, "Pop out the keyboard view into its own window");
        _popout.ToolTip = "Show the falling notes in their own window (it can go full screen)";
        _popout.Click += (_, _) => PopoutRequested?.Invoke();
        DockPanel.SetDock(_popout, Dock.Right);
        header.Children.Add(_popout);
        header.Children.Add(_label);
        SetRow(header, 0);
        SetRow(_keys.Element, 2);
        Children.Add(header);
        if (controlBar is not null) { SetRow(controlBar, 1); Children.Add(controlBar); }
        ControlBar = controlBar;
        Children.Add(_keys.Element);
        AutomationProperties.SetName(_exit, "Exit Keyboard mode");
        _exit.Click += (_, _) => ExitRequested?.Invoke();
    }

    /// <summary>The player pressed the pop-out button (it also closes an open pop-out).</summary>
    public event Action? PopoutRequested;
    /// <summary>The player pressed the header's close button.</summary>
    public event Action? ExitRequested;

    /// <summary>True while the keyboard surface lives in another window.</summary>
    public bool IsKeysDetached => _detached;

    /// <summary>Takes the keyboard surface out of the pane for another window to host; the pane says so in its header.</summary>
    public FrameworkElement DetachKeys()
    {
        _detached = true;
        Children.Remove(_keys.Element);
        _popout.Content = "Bring back";
        _label.Text = "The keyboard view is in its own window";
        return _keys.Element;
    }

    /// <summary>Puts the keyboard surface back (the other window let go of it first).</summary>
    public void AttachKeys()
    {
        if (!_detached) return;
        _detached = false;
        if (!Children.Contains(_keys.Element)) Children.Add(_keys.Element);
        _popout.Content = "Pop out";
        ApplyText();
    }

    public IKeyboardModeSurface Keys => _keys;
    /// <summary>The pane's control bar (null in a bare pane).</summary>
    public FrameworkElement? ControlBar { get; }

    /// <summary>The header text (self-test access).</summary>
    public string HeaderText => _label.Text;

    /// <summary>What the header adds after the name (notes left out of the 88 keys).</summary>
    public void SetNote(string note)
    {
        _note = note;
        if (!_detached) ApplyText();
    }

    private void ApplyText() => _label.Text = _note.Length > 0 ? Title + " (" + _note + ")" : Title;
}
