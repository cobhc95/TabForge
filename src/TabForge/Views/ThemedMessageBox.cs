using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace TabForge.Views;

// Owns: the app's themed replacement for System.Windows.MessageBox.Show (a WPF window in the app theme, same signatures and results).
// Does not own: file and task dialogs, the start-up/crash message (App.xaml.cs keeps the system box), the unsaved-work prompt.
// Tests: none yet (the box is built without showing it by Build; a self-test for its buttons is still to add).
/// <summary>Themed message box with the System.Windows.MessageBox.Show signatures used by TabForge.</summary>
public static class ThemedMessageBox
{
    public static MessageBoxResult Show(string text, string caption, MessageBoxButton buttons, MessageBoxImage icon, MessageBoxResult defaultResult = MessageBoxResult.None)
        => Show(null, text, caption, buttons, icon, defaultResult);

    public static MessageBoxResult Show(Window? owner, string text, string caption)
        => Show(owner, text, caption, MessageBoxButton.OK, MessageBoxImage.None);

    public static MessageBoxResult Show(Window? owner, string text, string caption, MessageBoxButton buttons, MessageBoxImage icon, MessageBoxResult defaultResult = MessageBoxResult.None)
    {
        var app = Application.Current;
        if (app is null || !app.Dispatcher.CheckAccess())
            return owner is null
                ? System.Windows.MessageBox.Show(text, caption, buttons, icon, defaultResult)
                : System.Windows.MessageBox.Show(owner, text, caption, buttons, icon, defaultResult);

        var result = MessageBoxResult.None;
        var window = Build(owner, text, caption, buttons, icon, defaultResult, answer => result = answer);
        window.ShowDialog();
        return result;
    }

    /// <summary>Builds the box without showing it. <paramref name="answered"/> gets the button's result (or the close-box result).</summary>
    internal static Window Build(Window? owner, string text, string caption, MessageBoxButton buttons, MessageBoxImage icon, MessageBoxResult defaultResult, Action<MessageBoxResult> answered)
    {
        var window = new Window
        {
            Title = caption,
            SizeToContent = SizeToContent.WidthAndHeight,
            MinWidth = 320,
            MaxWidth = 560,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        window.SetResourceReference(Window.BackgroundProperty, "WindowBrush");
        window.SetResourceReference(Window.ForegroundProperty, "TextBrush");
        var active = owner ?? Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current?.MainWindow;
        if (active is not null && active.IsLoaded && !ReferenceEquals(active, window))
        {
            window.Owner = active;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        var body = new DockPanel { Margin = new Thickness(20, 18, 20, 14), LastChildFill = true };
        var iconPanel = IconGlyph(icon);
        DockPanel.SetDock(iconPanel, Dock.Left);
        body.Children.Add(iconPanel);

        var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var content = new StackPanel();
        var message = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 13, MaxWidth = 500, Margin = new Thickness(14, 0, 0, 0) };
        message.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        content.Children.Add(message);
        content.Children.Add(buttonRow);
        body.Children.Add(content);

        var scroller = new ScrollViewer { Content = body, MaxHeight = 480, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        window.Content = scroller;

        var labels = Buttons(buttons);
        var defaultChoice = defaultResult == MessageBoxResult.None ? labels[0].Result : defaultResult;
        var finished = false;
        void Finish(MessageBoxResult r)
        {
            finished = true;
            answered(r);
            window.Close();
        }

        foreach (var (label, result) in labels)
        {
            var button = new Button
            {
                Content = new AccessText { Text = label }, MinWidth = 84, Height = 28, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(14, 0, 14, 0),
                IsDefault = result == defaultChoice,
                IsCancel = result == MessageBoxResult.Cancel || (buttons == MessageBoxButton.OK && result == MessageBoxResult.OK)
            };
            if (result == defaultChoice) button.SetResourceReference(FrameworkElement.StyleProperty, "AccentButton");
            button.Click += (_, _) => Finish(result);
            buttonRow.Children.Add(button);
        }

        // The close box and Esc follow the system box: YesNo has no way out but a button, Cancel-less boxes answer OK.
        window.Closing += (_, e) =>
        {
            if (finished) return;
            if (buttons == MessageBoxButton.YesNo) { e.Cancel = true; return; }
            finished = true;
            answered(buttons == MessageBoxButton.OK ? MessageBoxResult.OK : MessageBoxResult.Cancel);
        };
        return window;
    }

    private static (string Label, MessageBoxResult Result)[] Buttons(MessageBoxButton buttons) => buttons switch
    {
        MessageBoxButton.OKCancel => new[] { ("OK", MessageBoxResult.OK), ("Cancel", MessageBoxResult.Cancel) },
        MessageBoxButton.YesNo => new[] { ("_Yes", MessageBoxResult.Yes), ("_No", MessageBoxResult.No) },
        MessageBoxButton.YesNoCancel => new[] { ("_Yes", MessageBoxResult.Yes), ("_No", MessageBoxResult.No), ("Cancel", MessageBoxResult.Cancel) },
        _ => new[] { ("OK", MessageBoxResult.OK) },
    };

    private static UIElement IconGlyph(MessageBoxImage icon)
    {
        var (glyph, brushKey) = icon switch
        {
            MessageBoxImage.Warning => ("!", "AmberBrush"),
            MessageBoxImage.Error => ("x", "DangerBrush"),
            MessageBoxImage.Question => ("?", "AccentBrush"),
            MessageBoxImage.Information => ("i", "AccentBrush"),
            _ => ("", "AccentBrush"),
        };
        var circle = new Ellipse { Width = 30, Height = 30, StrokeThickness = 2 };
        if (glyph.Length == 0) return new Border { Width = 30, Height = 30 };
        circle.SetResourceReference(Shape.StrokeProperty, brushKey);
        var label = new TextBlock
        {
            Text = glyph, FontSize = 16, FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        var grid = new Grid { Width = 30, Height = 30, VerticalAlignment = VerticalAlignment.Top };
        grid.Children.Add(circle);
        grid.Children.Add(label);
        return grid;
    }
}
