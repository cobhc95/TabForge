using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using TabForge.Services;

namespace TabForge.Views;

/// <summary>
/// Paste Special (docs/COPY_PASTE_DESIGN.md 3.6): repeat count, placement mode, octave shift, "keep string and fret" and
/// "copy bar settings". Themed like the combined paste dialog and sized to its content; it only collects the choices, the
/// paste itself runs through <see cref="EditCommands.PasteSpecial"/>.
/// </summary>
internal sealed class PasteSpecialDialog : Window
{
    public const double DialogWidth = 470;

    private readonly ScoreClipKind _kind;
    private readonly Border _root;
    private readonly TextBox _repeat;
    private readonly RadioButton[] _modes;
    private readonly TextBlock _octaveText;
    private readonly Button _octaveDown;
    private readonly Button _octaveUp;
    private readonly TextBlock _octaveLabel;
    private readonly CheckBox _keepFingering;
    private readonly CheckBox? _barSettings;
    private int _octave;
    private bool _confirmed;

    public PasteSpecialDialog(ScoreClipKind kind)
    {
        _kind = kind;
        Title = "Paste special";
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
        AutomationProperties.SetName(this, "Paste special");
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 34, ResizeBorderThickness = new Thickness(0), GlassFrameThickness = new Thickness(0), UseAeroCaptionButtons = false
        });

        _root = new Border
        {
            Background = ResourceBrush("PanelBrush"), BorderBrush = ResourceBrush("BorderBrush"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Margin = new Thickness(12),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.45, Color = Colors.Black }
        };
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.Child = layout;
        Content = _root;

        // Title bar.
        var titleBar = new Border
        {
            Background = ResourceBrush("ChromeStripBrush"), CornerRadius = new CornerRadius(8, 8, 0, 0), Padding = new Thickness(14, 0, 2, 0)
        };
        WindowChrome.SetIsHitTestVisibleInChrome(titleBar, true);
        var titleGrid = new Grid();
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleGrid.Children.Add(new TextBlock { Text = "Paste special", VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Foreground = ResourceBrush("TextBrush") });
        var close = new Button
        {
            Content = "×", Width = 34, Height = 28, Padding = new Thickness(0), Background = Brushes.Transparent,
            BorderThickness = new Thickness(0), Focusable = false, ToolTip = "Cancel (Esc)"
        };
        AutomationProperties.SetName(close, "Cancel paste special");
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

        // Body.
        var body = new StackPanel { Margin = new Thickness(20, 14, 20, 14) };

        body.Children.Add(Heading("Repeat", first: true));
        var repeatRow = new WrapPanel { Margin = new Thickness(6, 0, 0, 0) };
        var down = StepButton("−", "Fewer copies");
        _repeat = new TextBox { Text = "1", Width = 52, MaxLength = 2, TextAlignment = TextAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, Height = 26, Margin = new Thickness(4, 0, 4, 0) };
        AutomationProperties.SetName(_repeat, "Number of copies (1 to 99)");
        _repeat.PreviewTextInput += (_, e) => e.Handled = !e.Text.All(char.IsDigit);
        DataObject.AddPastingHandler(_repeat, (_, e) => { if (e.DataObject.GetData(DataFormats.Text) is not string t || !t.All(char.IsDigit)) e.CancelCommand(); });
        _repeat.LostFocus += (_, _) => _repeat.Text = RepeatValue().ToString();
        var up = StepButton("+", "More copies");
        down.Click += (_, _) => _repeat.Text = Math.Max(1, RepeatValue() - 1).ToString();
        up.Click += (_, _) => _repeat.Text = Math.Min(PasteSpecialOptions.MaxRepeat, RepeatValue() + 1).ToString();
        repeatRow.Children.Add(down);
        repeatRow.Children.Add(_repeat);
        repeatRow.Children.Add(up);
        repeatRow.Children.Add(new TextBlock { Text = "copies, one after another (1-99)", Foreground = ResourceBrush("MutedBrush"), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        body.Children.Add(repeatRow);

        body.Children.Add(Heading(kind == ScoreClipKind.Bars ? "Pasting whole bars" : "Pasting beats"));
        var labels = kind == ScoreClipKind.Bars
            ? new[] { "Overwrite the bars", "Insert before (all tracks)", "Insert after (all tracks)" }
            : new[] { "Replace the notes at the cursor", "Insert and push the following notes along (this track only)" };
        _modes = new RadioButton[labels.Length];
        for (var i = 0; i < labels.Length; i++)
        {
            _modes[i] = new RadioButton
            {
                Content = new TextBlock { Text = labels[i], TextWrapping = TextWrapping.Wrap }, GroupName = "paste-special-mode", IsChecked = i == 0,
                Margin = new Thickness(6, 2, 0, 2), Foreground = ResourceBrush("TextBrush"), ToolTip = labels[i]
            };
            AutomationProperties.SetName(_modes[i], "Mode: " + labels[i]);
            body.Children.Add(_modes[i]);
        }

        body.Children.Add(Heading("Notes"));
        var octaveRow = new WrapPanel { Margin = new Thickness(6, 0, 0, 0) };
        _octaveLabel = new TextBlock { Text = "Octave shift", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        _octaveDown = StepButton("−", "Down an octave");
        _octaveText = new TextBlock { Width = 96, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _octaveUp = StepButton("+", "Up an octave");
        _octaveDown.Click += (_, _) => SetOctave(_octave - 1);
        _octaveUp.Click += (_, _) => SetOctave(_octave + 1);
        octaveRow.Children.Add(_octaveLabel);
        octaveRow.Children.Add(_octaveDown);
        octaveRow.Children.Add(_octaveText);
        octaveRow.Children.Add(_octaveUp);
        body.Children.Add(octaveRow);

        _keepFingering = new CheckBox
        {
            Content = new TextBlock { Text = "Keep string and fret (don't re-finger)", TextWrapping = TextWrapping.Wrap },
            Margin = new Thickness(6, 8, 0, 0), Foreground = ResourceBrush("TextBrush"),
            ToolTip = "Put each note on the same string and fret, even when the instrument is tuned differently (the pitch follows the target's tuning). Octave shift is then not used."
        };
        AutomationProperties.SetName(_keepFingering, "Keep string and fret (don't re-finger)");
        _keepFingering.Checked += (_, _) => UpdateOctaveEnabled();
        _keepFingering.Unchecked += (_, _) => UpdateOctaveEnabled();
        body.Children.Add(_keepFingering);
        SetOctave(0);

        if (kind == ScoreClipKind.Bars)
        {
            _barSettings = new CheckBox
            {
                Content = new TextBlock { Text = "Copy bar settings (time signature, key, tempo and similar)", TextWrapping = TextWrapping.Wrap },
                IsChecked = true, Margin = new Thickness(6, 6, 0, 0), Foreground = ResourceBrush("TextBrush"),
                ToolTip = "Untick to keep the target bars' own settings."
            };
            AutomationProperties.SetName(_barSettings, "Copy bar settings");
            body.Children.Add(_barSettings);
        }
        Grid.SetRow(body, 1);
        layout.Children.Add(body);

        // Footer.
        var paste = new Button
        {
            Content = "Paste", MinWidth = 76, Height = 28, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(14, 0, 14, 0),
            IsDefault = true, ToolTip = "Paste with these choices (Enter)",
            Background = ResourceBrush("AccentBrush"), BorderBrush = ResourceBrush("AccentBrush"), Foreground = Brushes.White
        };
        AutomationProperties.SetName(paste, "Paste");
        paste.Click += (_, _) => Finish(true);
        var cancel = new Button
        {
            Content = "Cancel", MinWidth = 76, Height = 28, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(14, 0, 14, 0),
            IsCancel = true, ToolTip = "Cancel the paste (Esc)"
        };
        AutomationProperties.SetName(cancel, "Cancel");
        cancel.Click += (_, _) => Finish(false);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(14, 10, 14, 12) };
        actions.Children.Add(paste);
        actions.Children.Add(cancel);
        var footer = new Border
        {
            Background = ResourceBrush("Panel2Brush"), BorderBrush = ResourceBrush("BorderSoftBrush"), BorderThickness = new Thickness(0, 1, 0, 0),
            CornerRadius = new CornerRadius(0, 0, 8, 8), Child = actions
        };
        Grid.SetRow(footer, 2);
        layout.Children.Add(footer);

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            Finish(false);
            e.Handled = true;
        };
        Loaded += (_, _) => { _repeat.Focus(); _repeat.SelectAll(); };
    }

    /// <summary>The user pressed Paste.</summary>
    public bool Confirmed => _confirmed;

    /// <summary>The choices; null when cancelled.</summary>
    public PasteSpecialOptions? Result => _confirmed ? Current() : null;

    /// <summary>The choices as they stand now (repeat 1..99, octave -2..+2).</summary>
    internal PasteSpecialOptions Current()
    {
        var mode = Math.Max(0, Array.FindIndex(_modes, r => r.IsChecked == true));
        return new PasteSpecialOptions(
            RepeatValue(),
            mode == 1 && _kind == ScoreClipKind.Beats ? BeatPasteMode.Insert : BeatPasteMode.Replace,
            _kind == ScoreClipKind.Bars ? mode switch { 1 => BarsOntoNotesAnswer.InsertBefore, 2 => BarsOntoNotesAnswer.InsertAfter, _ => BarsOntoNotesAnswer.Overwrite }
                : BarsOntoNotesAnswer.Overwrite,
            _keepFingering.IsChecked == true ? 0 : _octave,
            _keepFingering.IsChecked == true,
            _barSettings?.IsChecked != false).Normalized();
    }

    /// <summary>Test hook: sets every control (mode by index; copySettings is ignored for beats clips).</summary>
    internal void Set(int repeat, int mode, int octave, bool keepStringAndFret, bool copySettings)
    {
        _repeat.Text = repeat.ToString();
        _modes[Math.Clamp(mode, 0, _modes.Length - 1)].IsChecked = true;
        SetOctave(octave);
        _keepFingering.IsChecked = keepStringAndFret;
        if (_barSettings is not null) _barSettings.IsChecked = copySettings;
    }

    internal bool OctaveEnabled => _octaveUp.IsEnabled;
    internal bool HasBarSettings => _barSettings is not null;
    internal int ModeCount => _modes.Length;

    /// <summary>Test hook: click Paste / Cancel without showing the window.</summary>
    internal void Finish(bool paste)
    {
        _confirmed = paste;
        if (!IsLoaded && !IsVisible) return;
        try { DialogResult = paste; }
        catch (InvalidOperationException) { Close(); }
    }

    /// <summary>Height the window takes for its content at <see cref="DialogWidth"/>.</summary>
    internal double MeasureContentHeight()
    {
        _root.Measure(new Size(DialogWidth, double.PositiveInfinity));
        return _root.DesiredSize.Height;
    }

    private int RepeatValue() => int.TryParse(_repeat.Text, out var n) ? Math.Clamp(n, 1, PasteSpecialOptions.MaxRepeat) : 1;

    private void SetOctave(int value)
    {
        _octave = Math.Clamp(value, -PasteSpecialOptions.MaxOctaveShift, PasteSpecialOptions.MaxOctaveShift);
        _octaveText.Text = _octave == 0 ? "none" : $"{(_octave > 0 ? "+" : "−")}{Math.Abs(_octave)} octave{(Math.Abs(_octave) == 1 ? "" : "s")}";
        AutomationProperties.SetName(_octaveText, "Octave shift: " + _octaveText.Text);
        UpdateOctaveEnabled();
    }

    private void UpdateOctaveEnabled()
    {
        var on = _keepFingering.IsChecked != true;
        _octaveDown.IsEnabled = on && _octave > -PasteSpecialOptions.MaxOctaveShift;
        _octaveUp.IsEnabled = on && _octave < PasteSpecialOptions.MaxOctaveShift;
        var brush = on ? ResourceBrush("TextBrush") : ResourceBrush("MutedBrush");
        _octaveLabel.Foreground = brush;
        _octaveText.Foreground = brush;
    }

    private TextBlock Heading(string text, bool first = false) => new()
    {
        Text = text, FontWeight = FontWeights.SemiBold, Foreground = ResourceBrush("TextBrush"), Margin = new Thickness(0, first ? 0 : 14, 0, 6)
    };

    private static Button StepButton(string glyph, string name)
    {
        var b = new Button { Content = glyph, Width = 28, Height = 26, Padding = new Thickness(0), ToolTip = name };
        AutomationProperties.SetName(b, name);
        return b;
    }

    private Brush ResourceBrush(string key) => TryFindResource(key) as Brush ?? Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
}
