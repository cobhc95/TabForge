using System.Windows;
using System.Windows.Controls;

namespace TabForge.LiveInput;

// Owns: the line the live FX chain window shows where a track's options would be: "Live input is off" with a Turn on button,
//   hidden while live input is on.
// Does not own: turning it on (LiveInputController), where the line sits (FxChainWindow asks its host for it).
// Tests: TestLiveInput.
internal static class LiveInputHint
{
    public const string Text = "Live input is off: turn it on to hear the chain.";

    public static UIElement Build(LiveInputController controller)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        var text = new TextBlock { Text = Text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        var turnOn = new Button { Content = "Turn on", Padding = new Thickness(10, 2, 10, 2), ToolTip = "Turn live input on" };
        turnOn.Click += (_, _) => controller.Toggle();
        row.Children.Add(text);
        row.Children.Add(turnOn);
        void Show() => row.Visibility = controller.IsOn ? Visibility.Collapsed : Visibility.Visible;
        row.Loaded += (_, _) => { controller.Changed -= Show; controller.Changed += Show; Show(); };
        row.Unloaded += (_, _) => controller.Changed -= Show;   // the closed window leaves nothing on the controller
        Show();
        return row;
    }
}
