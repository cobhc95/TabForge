using System.Windows;
using System.Windows.Controls;
using TabForge.Playback;

namespace TabForge.Views;

/// <summary>
/// MIDI output of the selected track (Sound > MIDI / Audio setup): the device its notes are sent to, applied as soon as it
/// changes, and a test note. Esc closes the window. Owns: the window's layout. Does not own: the track edit (the
/// <paramref name="chose"/> callback) or the test note (the <paramref name="test"/> callback).
/// </summary>
public static class TrackOutputWindow
{
    public static void Show(Window? owner, string trackName, IReadOnlyList<MidiOutputDeviceInfo> devices, int current,
        Action<int> chose, Action test)
    {
        var w = new Window
        {
            Title = "MIDI output", Width = 420, Height = 210, MinWidth = 340, MinHeight = 190,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Owner = owner, ShowInTaskbar = false,
        };
        w.SetResourceReference(Window.BackgroundProperty, "WindowBrush");
        w.SetResourceReference(Window.ForegroundProperty, "TextBrush");

        var root = new DockPanel { Margin = new Thickness(14) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var testButton = new Button { Content = "Test sound", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0) };
        var close = new Button { Content = "Close", Padding = new Thickness(12, 4, 12, 4), IsCancel = true };
        buttons.Children.Add(testButton);
        buttons.Children.Add(close);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = $"MIDI output device for {trackName}", FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        var device = new ComboBox
        {
            ItemsSource = devices, DisplayMemberPath = "Name", SelectedValuePath = "DeviceId", Margin = new Thickness(0, 8, 0, 0)
        };
        device.SelectedValue = current;
        device.SelectionChanged += (_, _) => { if (device.SelectedValue is int id) chose(id); };
        body.Children.Add(device);
        var hint = new TextBlock { Text = "The track's notes are sent to this device. Test sound plays a note on it.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        body.Children.Add(hint);
        root.Children.Add(body);

        testButton.Click += (_, _) => test();
        w.Content = root;
        DialogHost.ShowModal(w);
    }
}
