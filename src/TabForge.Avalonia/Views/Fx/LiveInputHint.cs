using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using TabForge.LiveInput;

namespace TabForge.Avalonia.Views.Fx;

// Owns: the line the live FX chain window shows where a track's options would be: "Live input is off" with a Turn on button, hidden while live input is on.
// Does not own: turning live input on (LiveInputSession), where the line sits (FxChainWindow asks its host for it).
// Tests: TabForge.Avalonia.Tests.LiveInputFxTitleTests.
public static class LiveInputHint
{
    public const string Text = "Live input is off: turn it on to hear the chain.";
    public const string TurnOnText = "Turn on", TurnOnTip = "Turn live input on";

    /// <summary>The hint line; it follows the session while it is in a window and lets go of it when it leaves.</summary>
    public static Control Build(LiveInputSession session)
    {
        var text = new TextBlock { Text = Text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), Name = "LiveHintText", [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextBrush") };
        var turnOn = new Button { Content = TurnOnText, Padding = new Thickness(10, 2), Name = "LiveTurnOn" };
        ToolTip.SetTip(turnOn, TurnOnTip);
        turnOn.Click += (_, _) => session.Toggle();
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4), Name = "LiveInputHint", Children = { text, turnOn } };
        void Show() => row.IsVisible = !session.IsOn;
        row.AttachedToVisualTree += (_, _) => { session.Changed -= Show; session.Changed += Show; Show(); };
        row.DetachedFromVisualTree += (_, _) => session.Changed -= Show;   // the closed window leaves nothing on the session
        Show();
        return row;
    }
}
