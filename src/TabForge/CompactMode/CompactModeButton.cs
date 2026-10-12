using System.Windows;
using System.Windows.Controls;
using TabForge.Views;

namespace TabForge.CompactMode;

// Owns: the toolbar button: a click runs the View > Compact mode menu row of its window (the button, the menu and the hotkey share one command path), and the button is lit while that row is checked.
// Does not own: the command and the row's checked state (CompactModeFeatureModule.cs) and the button's look (MainWindow.xaml).
// Tests: TestCompactMode.
public sealed class CompactModeButton : Button
{
    private MenuItem? _row;

    public CompactModeButton()
    {
        Click += (_, _) => Row()?.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Loaded += (_, _) => Row();
    }

    /// <summary>The menu row, found once; its checked state lights the button (Tag "on").</summary>
    private MenuItem? Row()
    {
        if (_row is not null || Window.GetWindow(this)?.FindName("MainMenu") is not Menu menu) return _row;
        if ((_row = MenuHotkey.Find(menu.Items, CompactModeFeatureModule.CommandId)) is not { } row) return null;
        row.Checked += (_, _) => Tag = "on";
        row.Unchecked += (_, _) => Tag = null;
        return row;
    }
}
