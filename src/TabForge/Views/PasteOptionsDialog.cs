using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using TabForge.Services;

namespace TabForge.Views;

/// <summary>
/// The one combined paste dialog (docs/COPY_PASTE_DESIGN.md, Owner decisions): it shows only the questions it is given,
/// each as a small group of radio buttons (recommended option preselected) with its own "Remember my choice" box.
/// It sizes itself to its content, so a single question gives a compact window.
/// </summary>
internal sealed class PasteOptionsDialog : Window
{
    public const double DialogWidth = 470;

    private sealed class Group
    {
        public required PasteQuestion Question;
        public required RadioButton[] Radios;
        public required CheckBox Remember;
    }

    private readonly List<Group> _groups = new();
    private readonly Border _root;
    private bool _confirmed;

    public PasteOptionsDialog(IReadOnlyCollection<PasteQuestion> questions)
    {
        Title = "Paste";
        Width = DialogWidth;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Foreground = ResourceBrush("TextBrush");
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        AutomationProperties.SetName(this, "Paste options");
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 34,
            ResizeBorderThickness = new Thickness(0),
            GlassFrameThickness = new Thickness(0),
            UseAeroCaptionButtons = false
        });

        _root = new Border
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
        _root.Child = layout;
        Content = _root;

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
        titleGrid.Children.Add(new TextBlock
        {
            Text = "Paste", VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Foreground = ResourceBrush("TextBrush")
        });
        var close = new Button
        {
            Content = "×", Width = 34, Height = 28, Padding = new Thickness(0), Background = Brushes.Transparent,
            BorderThickness = new Thickness(0), Focusable = false, ToolTip = "Cancel (Esc)"
        };
        AutomationProperties.SetName(close, "Cancel paste");
        WindowChrome.SetIsHitTestVisibleInChrome(close, true);
        close.Click += (_, _) => Finish(false);
        Grid.SetColumn(close, 1);
        titleGrid.Children.Add(close);
        titleBar.Child = titleGrid;
        titleBar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not Button) DragMove();
        };
        layout.Children.Add(titleBar);

        var body = new StackPanel { Margin = new Thickness(20, 14, 20, 14) };
        var list = questions.Distinct().OrderBy(q => (int)q).ToList();
        for (var i = 0; i < list.Count; i++)
            body.Children.Add(BuildQuestion(list[i], first: i == 0));
        Grid.SetRow(body, 1);
        layout.Children.Add(body);

        var paste = new Button
        {
            Content = "Paste", MinWidth = 76, Height = 28, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(14, 0, 14, 0),
            IsDefault = true, ToolTip = "Paste with these choices (Enter)",
            Background = ResourceBrush("AccentBrush"), BorderBrush = ResourceBrush("AccentBrush"), Foreground = Brushes.White
        };
        AutomationProperties.SetName(paste, "Paste");
        UiIds.Id(paste, "Paste.Ok");
        paste.Click += (_, _) => Finish(true);
        var cancel = new Button
        {
            Content = "Cancel", MinWidth = 76, Height = 28, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(14, 0, 14, 0),
            IsCancel = true, ToolTip = "Cancel the paste (Esc)"
        };
        AutomationProperties.SetName(cancel, "Cancel");
        UiIds.Id(cancel, "Paste.Cancel");
        cancel.Click += (_, _) => Finish(false);
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(14, 10, 14, 12)
        };
        actions.Children.Add(paste);
        actions.Children.Add(cancel);
        var footer = new Border
        {
            Background = ResourceBrush("Panel2Brush"),
            BorderBrush = ResourceBrush("BorderSoftBrush"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            CornerRadius = new CornerRadius(0, 0, 8, 8),
            Child = actions
        };
        Grid.SetRow(footer, 2);
        layout.Children.Add(footer);

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            Finish(false);
            e.Handled = true;
        };
        Loaded += (_, _) => (_groups.Count > 0 ? _groups[0].Radios.FirstOrDefault(r => r.IsChecked == true) : null)?.Focus();
    }

    /// <summary>The user pressed Paste.</summary>
    public bool Confirmed => _confirmed;

    /// <summary>The questions shown, in display order.</summary>
    public IReadOnlyList<PasteQuestion> ShownQuestions => _groups.Select(g => g.Question).ToList();

    /// <summary>Test hook: selects an option (by index) and sets the remember box of a question.</summary>
    internal void Choose(PasteQuestion question, int optionIndex, bool remember)
    {
        var group = _groups.First(g => g.Question == question);
        group.Radios[optionIndex].IsChecked = true;
        group.Remember.IsChecked = remember;
    }

    /// <summary>The index of the checked option of a question (0 = recommended).</summary>
    internal int ChosenIndex(PasteQuestion question) =>
        Array.FindIndex(_groups.First(g => g.Question == question).Radios, r => r.IsChecked == true);

    /// <summary>Test hook: click Paste / Cancel without showing the window.</summary>
    internal void Finish(bool paste)
    {
        _confirmed = paste;
        if (!IsLoaded && !IsVisible) return;
        try { DialogResult = paste; }
        catch (InvalidOperationException) { Close(); }
    }

    /// <summary>The answers as chosen; null when the paste was cancelled.</summary>
    public PasteAnswers? Answers => _confirmed ? CurrentAnswers() : null;

    /// <summary>The current choices regardless of whether the dialog was confirmed.</summary>
    internal PasteAnswers CurrentAnswers()
    {
        string? IdOf(PasteQuestion q)
        {
            var g = _groups.FirstOrDefault(x => x.Question == q);
            if (g is null) return null;
            var i = Array.FindIndex(g.Radios, r => r.IsChecked == true);
            return PasteQuestionInfo.Options(q)[i < 0 ? 0 : i].Id;
        }
        bool Remembered(PasteQuestion q) => _groups.FirstOrDefault(x => x.Question == q)?.Remember.IsChecked == true;
        return PasteQuestionInfo.FromIds(IdOf, Remembered);
    }

    /// <summary>Height the window takes for its content at <see cref="DialogWidth"/> (what SizeToContent resolves to).</summary>
    internal double MeasureContentHeight()
    {
        _root.Measure(new Size(DialogWidth, double.PositiveInfinity));
        return _root.DesiredSize.Height;
    }

    private UIElement BuildQuestion(PasteQuestion question, bool first)
    {
        var options = PasteQuestionInfo.Options(question);
        var panel = new StackPanel { Margin = new Thickness(0, first ? 0 : 14, 0, 0) };
        var title = new TextBlock
        {
            Text = PasteQuestionInfo.Title(question), TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold,
            Foreground = ResourceBrush("TextBrush"), Margin = new Thickness(0, 0, 0, 6)
        };
        panel.Children.Add(title);
        var group = "paste-" + (int)question;
        var radios = new RadioButton[options.Count];
        for (var i = 0; i < options.Count; i++)
        {
            var label = options[i].Label + (i == 0 ? " (recommended)" : "");
            radios[i] = new RadioButton
            {
                Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap },
                GroupName = group,
                IsChecked = i == 0,
                Margin = new Thickness(6, 2, 0, 2),
                Foreground = ResourceBrush("TextBrush"),
                ToolTip = label
            };
            AutomationProperties.SetName(radios[i], $"{PasteQuestionInfo.Title(question)}: {label}");
            UiIds.Id(radios[i], $"Paste.Q{(int)question}.{i}");
            panel.Children.Add(radios[i]);
        }
        var remember = new CheckBox
        {
            Content = "Remember my choice", FontSize = 12, Margin = new Thickness(6, 6, 0, 0),
            Foreground = ResourceBrush("MutedBrush"),
            ToolTip = "Stop asking this question; change it later in Preferences > Editing > Copy and paste."
        };
        AutomationProperties.SetName(remember, "Remember my choice: " + PasteQuestionInfo.Title(question));
        panel.Children.Add(remember);
        AutomationProperties.SetName(panel, PasteQuestionInfo.Title(question));
        KeyboardNavigation.SetTabNavigation(panel, KeyboardNavigationMode.Local);
        _groups.Add(new Group { Question = question, Radios = radios, Remember = remember });
        return panel;
    }

    private Brush ResourceBrush(string key) => TryFindResource(key) as Brush ?? Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
}

/// <summary>Shows <see cref="PasteOptionsDialog"/> modally through <see cref="DialogHost"/>.</summary>
internal sealed class WpfPasteQuestionAsker : IPasteQuestionAsker
{
    private readonly Window? _owner;
    public WpfPasteQuestionAsker(Window? owner = null) => _owner = owner;

    public PasteAnswers? Ask(IReadOnlyCollection<PasteQuestion> questions)
    {
        if (questions.Count == 0) return new PasteAnswers();
        var dialog = new PasteOptionsDialog(questions);
        if (_owner is { IsLoaded: true }) dialog.Owner = _owner;
        return DialogHost.ShowModal(dialog) == true ? dialog.Answers ?? dialog.CurrentAnswers() : null;
    }
}
