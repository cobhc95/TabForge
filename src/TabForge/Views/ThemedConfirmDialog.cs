using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Shell;
using System.ComponentModel;

namespace TabForge.Views;

/// <summary>Dark, application-themed replacement for the native unsaved-work MessageBox.</summary>
internal sealed class ThemedConfirmDialog : Window
{
    private MessageBoxResult _result = MessageBoxResult.Cancel;

    /// <summary>Builds and measures one hidden dialog of each shape so the first real prompt skips the cold template, resource and JIT work.</summary>
    internal static void Prewarm()
    {
        try
        {
            foreach (var choices in new IReadOnlyList<string>?[] { null, new[] { "a", "b" } })
            {
                var warm = new ThemedConfirmDialog("Confirm", "Confirm", details: new[] { "-" }, rememberText: "Remember", choices: choices, scopes: choices);
                warm.ApplyTemplate();
                if (warm.Content is FrameworkElement root) root.Measure(new Size(600, 600));
                warm.Close();   // a never-shown window still counts as open and would keep the app alive after the last main window closes
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Dialog prewarm skipped: {ex.Message}"); }
    }

    public ThemedConfirmDialog(
        string title,
        string message,
        string yesToolTip = "Save the changes",
        string noToolTip = "Discard the changes",
        IReadOnlyList<string>? details = null,
        bool showCancel = true,
        string? rememberText = null,
        string yesText = "Yes",
        string noText = "No",
        bool defaultIsNo = false,
        IReadOnlyList<string>? choices = null,
        int defaultChoice = 0,
        IReadOnlyList<string>? scopes = null,
        int defaultScope = 0)
        : this(false, title, message, yesToolTip, noToolTip, details, showCancel, rememberText,
            yesText, noText, defaultIsNo, choices, defaultChoice, scopes, defaultScope)
    {
    }

    internal ThemedConfirmDialog(
        bool hideOnAnswer,
        string title,
        string message,
        string yesToolTip = "Save the changes",
        string noToolTip = "Discard the changes",
        IReadOnlyList<string>? details = null,
        bool showCancel = true,
        string? rememberText = null,
        string yesText = "Yes",
        string noText = "No",
        bool defaultIsNo = false,
        IReadOnlyList<string>? choices = null,
        int defaultChoice = 0,
        IReadOnlyList<string>? scopes = null,
        int defaultScope = 0)
    {
        _defaultIsNo = defaultIsNo;
        _hideOnAnswer = hideOnAnswer;
        Title = title;
        Width = choices is { Count: > 0 } ? 560 : 420;
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
        _messageText = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
            Foreground = ResourceBrush("TextBrush")
        };
        if (choices is { Count: > 0 })
        {
            var stack = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
            _messageText.Margin = new Thickness(0, 0, 0, 8);
            stack.Children.Add(_messageText);
            _choiceButtons = AddRadios(stack, choices, defaultChoice, "choice", Orientation.Vertical);
            if (scopes is { Count: > 0 })
            {
                stack.Children.Add(new TextBlock { Text = "Apply to", Margin = new Thickness(0, 10, 0, 2), Foreground = ResourceBrush("MutedBrush") });
                _scopeButtons = AddRadios(stack, scopes, defaultScope, "scope", Orientation.Horizontal);
            }
            Grid.SetColumn(stack, 1);
            body.Children.Add(stack);
        }
        else
        {
            Grid.SetColumn(_messageText, 1);
            body.Children.Add(_messageText);
        }
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
                ToolTip = choices is { Count: > 0 } ? "Delete then does this directly. Turn the prompt back on in Settings > Editing > Safety > Ask what Delete does on bars."
                    : "You can turn this warning back on in Settings → General → Safety."
            };
            footerStack.Children.Add(_remember);
        }
        footer.Child = footerStack;
        // A destructive question puts Enter and the focus on the safe answer (No) and has no Y shortcut.
        actions.Children.Add(ActionButton(yesText, MessageBoxResult.Yes, isDefault: !defaultIsNo, toolTip: yesToolTip));
        actions.Children.Add(ActionButton(noText, MessageBoxResult.No, isDefault: defaultIsNo, toolTip: noToolTip));
        // Where "No" already means "go back", a Cancel button would just duplicate it.
        if (showCancel) actions.Children.Add(ActionButton("Cancel", MessageBoxResult.Cancel, isCancel: true));
        else ((Button)actions.Children[1]).IsCancel = true;
        Grid.SetRow(footer, 2);
        layout.Children.Add(footer);

        PreviewKeyDown += (_, e) =>
        {
            // Y = yes, N = no, Esc = cancel (shown in each button's tooltip).
            if (e.Key == Key.Escape) SetResult(MessageBoxResult.Cancel);
            else if (e.Key == Key.Y && !defaultIsNo) SetResult(MessageBoxResult.Yes);
            else if (e.Key == Key.N) SetResult(showCancel ? MessageBoxResult.No : MessageBoxResult.No);
            else return;
            e.Handled = true;
        };
        Loaded += (_, _) =>
        {
            if (!ShowActivated && !IsActive) return;
            if (_choiceButtons is { Count: > 0 }) FocusDefaultChoice();
            else actions.Children[defaultIsNo ? 1 : 0].Focus();
        };
        Activated += (_, _) => FocusDefaultChoice();
        if (_hideOnAnswer) Closing += HandleReusableClosing;
    }

    private readonly bool _defaultIsNo;
    private readonly List<RadioButton>? _choiceButtons;
    private readonly List<RadioButton>? _scopeButtons;
    private readonly TextBlock _messageText;
    private readonly bool _hideOnAnswer;
    private bool _disposingReusable;
    private bool _dialogHostPrepared;

    /// <summary>Refreshes the reusable choice dialog without closing its owner-bound window.</summary>
    internal void ResetReusable(string message, IReadOnlyList<string> choices, int defaultChoice, int defaultScope)
    {
        if (!_hideOnAnswer || _choiceButtons is null || _scopeButtons is null)
            throw new InvalidOperationException("Only a reusable choice dialog accepts per-show values.");
        if (choices.Count != _choiceButtons.Count) throw new ArgumentException("The reusable choice count must stay fixed.", nameof(choices));
        _messageText.Text = message;
        for (var i = 0; i < choices.Count; i++)
        {
            _choiceButtons[i].Content = choices[i];
            System.Windows.Automation.AutomationProperties.SetName(_choiceButtons[i], choices[i]);
            _choiceButtons[i].IsChecked = i == Math.Clamp(defaultChoice, 0, choices.Count - 1);
        }
        for (var i = 0; i < _scopeButtons.Count; i++)
            _scopeButtons[i].IsChecked = i == Math.Clamp(defaultScope, 0, _scopeButtons.Count - 1);
        if (_remember is not null) _remember.IsChecked = false;
        _result = MessageBoxResult.Cancel;
    }

    internal bool BeginDialogHostPreparation()
    {
        if (_dialogHostPrepared) return false;
        _dialogHostPrepared = true;
        return true;
    }

    internal void CloseReusable()
    {
        if (!_hideOnAnswer || _disposingReusable) return;
        _disposingReusable = true;
        Close();
    }

    private void FocusDefaultChoice()
    {
        if (!ShowActivated && !IsActive) return;
        if (_choiceButtons is { Count: > 0 }) _choiceButtons[Math.Clamp(SelectedChoice, 0, _choiceButtons.Count - 1)].Focus();
    }

    private void HandleReusableClosing(object? sender, CancelEventArgs e)
    {
        if (_disposingReusable) return;
        e.Cancel = true;
        SetResult(MessageBoxResult.Cancel);
    }

    /// <summary>The index of the option picked in the choice list (0 without one).</summary>
    public int SelectedChoice => Math.Max(0, _choiceButtons?.FindIndex(r => r.IsChecked == true) ?? 0);
    /// <summary>The index of the option picked in the scope row (0 without one).</summary>
    public int SelectedScope => Math.Max(0, _scopeButtons?.FindIndex(r => r.IsChecked == true) ?? 0);

    /// <summary>Test seam: picks options as arrow keys would.</summary>
    internal void PickForTest(int choice, int? scope = null)
    {
        if (_choiceButtons is { } c && choice >= 0 && choice < c.Count) c[choice].IsChecked = true;
        if (scope is int s && _scopeButtons is { } sc && s >= 0 && s < sc.Count) sc[s].IsChecked = true;
    }

    /// <summary>Test seam: ticks the "remember" box.</summary>
    internal void RememberForTest(bool on) { if (_remember is not null) _remember.IsChecked = on; }

    private List<RadioButton> AddRadios(StackPanel host, IReadOnlyList<string> labels, int selected, string group, Orientation orientation)
    {
        var panel = new StackPanel { Orientation = orientation };
        KeyboardNavigation.SetDirectionalNavigation(panel, KeyboardNavigationMode.Cycle);
        var list = new List<RadioButton>();
        for (var i = 0; i < labels.Count; i++)
        {
            var radio = new RadioButton
            {
                Content = labels[i], GroupName = group, IsChecked = i == Math.Clamp(selected, 0, labels.Count - 1),
                Margin = new Thickness(0, 2, orientation == Orientation.Horizontal ? 16 : 0, 2), Foreground = ResourceBrush("TextBrush")
            };
            System.Windows.Automation.AutomationProperties.SetName(radio, labels[i]);
            radio.GotKeyboardFocus += (_, _) => radio.IsChecked = true;   // arrows move the choice
            list.Add(radio);
            panel.Children.Add(radio);
        }
        host.Children.Add(panel);
        return list;
    }

    public MessageBoxResult Result => _result;

    /// <summary>Test seam: answers the prompt as a click on that button would (the dialog is never shown).</summary>
    internal void AnswerForTest(MessageBoxResult answer)
    {
        if (_hideOnAnswer && IsVisible) SetResult(answer);
        else _result = answer;
    }

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
                + (result == MessageBoxResult.Yes ? (_defaultIsNo ? "" : "  (Y / Enter)") : result == MessageBoxResult.No ? (_defaultIsNo ? "  (N / Enter / Esc)" : "  (N)") : "  (Esc)")
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
        if (_hideOnAnswer) throw new InvalidOperationException("A reusable prompt must be shown modally.");
        _modeless = true;
        Closed += (_, _) => done(_result);
        Show();
    }

    private void SetResult(MessageBoxResult result)
    {
        _result = result;
        if (_modeless) Close();
        else if (_hideOnAnswer) Hide();
        else DialogResult = true;
    }

    private Brush ResourceBrush(string key) => (Brush)FindResource(key);
}
