using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Shell;

namespace TabForge.Views;

/// <summary>Dark, application-themed replacement for the native unsaved-work MessageBox.</summary>
internal sealed class ThemedConfirmDialog : Window
{
    private MessageBoxResult _result = MessageBoxResult.Cancel;

    public ThemedConfirmDialog(
        string title,
        string message,
        string yesToolTip = "Save the changes",
        string noToolTip = "Discard the changes",
        IReadOnlyList<string>? details = null,
        bool showCancel = true,
        string? rememberText = null,
        string yesText = "Yes",
        string noText = "No")
    {
        Title = title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        // Transparent window so the rounded border is the real outline (no square corners behind it).
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Foreground = ResourceBrush("TextBrush");
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 34,
            ResizeBorderThickness = new Thickness(0),
            GlassFrameThickness = new Thickness(0),
            UseAeroCaptionButtons = false
        });

        var root = new Border
        {
            Background = ResourceBrush("PanelBrush"),
            BorderBrush = ResourceBrush("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(12),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.45, Color = Colors.Black }
        };
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Child = layout;
        Content = root;

        var titleBar = new Border
        {
            Background = ResourceBrush("ChromeStripBrush"),
            CornerRadius = new CornerRadius(8, 8, 0, 0),
            Padding = new Thickness(14, 0, 2, 0)
        };
        WindowChrome.SetIsHitTestVisibleInChrome(titleBar, true);
        var titleGrid = new Grid();
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titleText = new TextBlock
        {
            Text = title,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12,
            Foreground = ResourceBrush("TextBrush")
        };
        titleGrid.Children.Add(titleText);
        var close = new Button
        {
            Content = "×",
            Width = 34,
            Height = 28,
            Margin = new Thickness(0),
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Focusable = false,
            ToolTip = "Cancel"
        };
        WindowChrome.SetIsHitTestVisibleInChrome(close, true);
        close.Click += (_, _) => SetResult(MessageBoxResult.Cancel);
        Grid.SetColumn(close, 1);
        titleGrid.Children.Add(close);
        titleBar.Child = titleGrid;
        titleBar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not Button) DragMove();
        };
        Grid.SetRow(titleBar, 0);
        layout.Children.Add(titleBar);

        var body = new Grid { Margin = new Thickness(20, 18, 20, 18) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var icon = new Grid { Width = 26, Height = 26, VerticalAlignment = VerticalAlignment.Center };
        icon.Children.Add(new Ellipse { Fill = ResourceBrush("AccentBrush"), Stroke = ResourceBrush("BorderBrush"), StrokeThickness = 1 });
        icon.Children.Add(new TextBlock
        {
            Text = "?",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });
        body.Children.Add(icon);
        var messageText = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
            Foreground = ResourceBrush("TextBrush")
        };
        Grid.SetColumn(messageText, 1);
        body.Children.Add(messageText);
        Grid.SetRow(body, 1);
        layout.Children.Add(body);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(14, 10, 14, 12)
        };
        // Footer band: a slightly different surface with a hairline, like native task dialogs.
        var footer = new Border
        {
            Background = ResourceBrush("Panel2Brush"),
            BorderBrush = ResourceBrush("BorderSoftBrush"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            CornerRadius = new CornerRadius(0, 0, 8, 8),
        };
        var footerStack = new StackPanel();
        footerStack.Children.Add(actions);
        if (details is { Count: > 0 })
            footerStack.Children.Add(BuildDetails(details));
        if (rememberText is not null)
        {
            _remember = new CheckBox
            {
                Content = rememberText, FontSize = 12, Margin = new Thickness(16, 0, 16, 12),
                Foreground = ResourceBrush("MutedBrush"),
                ToolTip = "You can turn this warning back on in Settings → General → Safety."
            };
            footerStack.Children.Add(_remember);
        }
        footer.Child = footerStack;
        actions.Children.Add(ActionButton(yesText, MessageBoxResult.Yes, isDefault: true, toolTip: yesToolTip));
        actions.Children.Add(ActionButton(noText, MessageBoxResult.No, toolTip: noToolTip));
        // Where "No" already means "go back", a Cancel button would just duplicate it.
        if (showCancel) actions.Children.Add(ActionButton("Cancel", MessageBoxResult.Cancel, isCancel: true));
        else ((Button)actions.Children[1]).IsCancel = true;
        Grid.SetRow(footer, 2);
        layout.Children.Add(footer);

        PreviewKeyDown += (_, e) =>
        {
            // Y = yes, N = no, Esc = cancel (shown in each button's tooltip).
            if (e.Key == Key.Escape) SetResult(MessageBoxResult.Cancel);
            else if (e.Key == Key.Y) SetResult(MessageBoxResult.Yes);
            else if (e.Key == Key.N) SetResult(showCancel ? MessageBoxResult.No : MessageBoxResult.No);
            else return;
            e.Handled = true;
        };
        Loaded += (_, _) => actions.Children[0].Focus();
    }

    public MessageBoxResult Result => _result;

    /// <summary>Test seam: answers the prompt as a click on that button would (the dialog is never shown).</summary>
    internal void AnswerForTest(MessageBoxResult answer) => _result = answer;

    private CheckBox? _remember;
    /// <summary>The user ticked "don't ask again" (only meaningful together with a Yes result).</summary>
    public bool RememberChoice => _remember?.IsChecked == true;

    /// <summary>A collapsed "Show changes" link under the buttons that expands into the list of edits.</summary>
    private UIElement BuildDetails(IReadOnlyList<string> details)
    {
        var list = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        foreach (var line in details)
            list.Children.Add(new TextBlock
            {
                Text = "• " + line, TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 1, 0, 1),
                Foreground = ResourceBrush("MutedBrush")
            });
        var scroller = new ScrollViewer
        {
            Content = list, MaxHeight = 170, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Visibility = Visibility.Collapsed, Margin = new Thickness(0, 2, 0, 0)
        };
        var label = $"Show changes ({details.Count})";
        var link = new TextBlock
        {
            Text = "▸ " + label, Cursor = Cursors.Hand, FontSize = 12, Foreground = ResourceBrush("AccentBrush"),
            TextDecorations = TextDecorations.Underline, Focusable = false, ToolTip = "List what was changed"
        };
        link.MouseLeftButtonUp += (_, _) =>
        {
            var open = scroller.Visibility != Visibility.Visible;
            scroller.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            link.Text = (open ? "▾ Hide changes" : "▸ " + label);
        };
        var panel = new StackPanel { Margin = new Thickness(16, 0, 16, 12) };
        panel.Children.Add(link);
        panel.Children.Add(scroller);
        return panel;
    }

    private Button ActionButton(string text, MessageBoxResult result, bool isDefault = false, bool isCancel = false, string? toolTip = null)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 76,
            Height = 28,
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(14, 0, 14, 0),
            IsDefault = isDefault,
            IsCancel = isCancel,
            ToolTip = (toolTip ?? (result == MessageBoxResult.Yes ? "Save the changes" : result == MessageBoxResult.No ? "Discard the changes" : "Return without closing"))
                + (result == MessageBoxResult.Yes ? "  (Y / Enter)" : result == MessageBoxResult.No ? "  (N)" : "  (Esc)")
        };
        if (isDefault)
        {
            button.Background = ResourceBrush("AccentBrush");
            button.BorderBrush = ResourceBrush("AccentBrush");
            button.Foreground = Brushes.White;
        }
        button.Click += (_, _) => SetResult(result);
        return button;
    }

    private bool _modeless;

    /// <summary>Shows the prompt without blocking the owner; <paramref name="done"/> gets the answer (Cancel when closed another way).</summary>
    public void ShowModeless(Action<MessageBoxResult> done)
    {
        _modeless = true;
        Closed += (_, _) => done(_result);
        Show();
    }

    private void SetResult(MessageBoxResult result)
    {
        _result = result;
        if (_modeless) Close();
        else DialogResult = true;
    }

    private Brush ResourceBrush(string key) => (Brush)FindResource(key);
}
