using TabForge.Avalonia.Platform;
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Presenters;
using TabForge.Avalonia.Views.Dialogs;

namespace TabForge.Avalonia.Views;

// Owns: the four small bar questions of the Structure tools: time signature, key signature, score directions and the section marker, with
// the dialogs' texts, controls and answers (StandardDialogsPresenter parses what was typed).
// Does not own: the parsing and wording (StandardDialogsPresenter, SignatureChoices) or applying the answer (ScorePreviewEditor).
// Deviation: the windows use the platform title bar and the app theme; the dialogs draw the system ones.
// Tests: no dedicated test class.

/// <summary>The time signature answer: beats per bar, beat value, and whether it reaches only the cursor bar.</summary>
public sealed record TimeSignatureAnswer(int Numerator, int Denominator, bool OnlyThisBar);

/// <summary>The key signature answer: -7 (seven flats) to 7 (seven sharps), minor mode, and whether it reaches only the cursor bar.</summary>
public sealed record KeySignatureAnswer(int Signature, bool Minor, bool OnlyThisBar);

/// <summary>The directions answer: the stored direction names and the alternate ending (0 for none).</summary>
public sealed record DirectionsAnswer(string Directions, int Ending);

/// <summary>The marker answer: its trimmed name and #RRGGBB colour.</summary>
public sealed record MarkerAnswer(string Title, string ColourHex);

/// <summary>The shell all four questions share: a body, then OK (the default) and Cancel; closing returns the answer, or null.</summary>
public abstract class BarDialogWindow<TAnswer> : Window where TAnswer : class
{
    protected BarDialogWindow(string title, double width, string okText)
    {
        Title = title;
        Width = width;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        this[!BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
        this[!ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
        FontSize = DialogMetrics.FontSize;
        OkText = okText;
        AddHandler(KeyDownEvent, (_, e) => { if (e.Key == Key.Escape) { Close(null); e.Handled = true; } }, RoutingStrategies.Tunnel);
    }

    private string OkText { get; }

    /// <summary>The answer the controls hold now; null closes the window as Cancel does.</summary>
    protected abstract TAnswer? Answer();

    /// <summary>True when OK may close the window (a typed value that cannot be accepted keeps it open).</summary>
    protected virtual bool CanAccept() => true;

    /// <summary>Puts <paramref name="body"/> above the button row.</summary>
    protected void SetBody(params Control[] body)
    {
        var ok = PromptChrome.Action("OkButton", OkText, primary: true);
        ok.Click += (_, _) => { if (CanAccept()) Close(Answer()); };
        var cancel = PromptChrome.Action("CancelButton", "Cancel", primary: false, isCancel: true);
        cancel.Click += (_, _) => Close(null);
        var panel = new StackPanel { Margin = DialogMetrics.PromptInset, Spacing = 8 };
        foreach (var control in body) panel.Children.Add(control);
        panel.Children.Add(PromptChrome.Footer(null, null, cancel, ok));
        Content = panel;
    }

    protected static TextBlock Label(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };
}

/// <summary>"Time signature": beats per bar over a beat value, with the scope line.</summary>
public sealed class TimeSignatureWindow : BarDialogWindow<TimeSignatureAnswer>
{
    private readonly TextBox _beats;
    private readonly ComboBox _beatValue;
    private readonly CheckBox _onlyThisBar;

    /// <param name="selectedBars">"bars 3-6" when a range is selected (the change then reaches exactly those bars), else null.</param>
    public TimeSignatureWindow(int numerator, int denominator, string? selectedBars) : base("Time signature", 320, "OK")
    {
        _beats = new TextBox { Name = "Beats", Text = numerator.ToString(), Width = 56, VerticalAlignment = VerticalAlignment.Center };
        _beatValue = new ComboBox { Name = "BeatValue", Width = 76, ItemsSource = SignatureChoices.Denominators.Select(value => value.ToString()).ToList(), VerticalAlignment = VerticalAlignment.Center };
        _beatValue.SelectedItem = denominator.ToString();
        if (_beatValue.SelectedItem is null) _beatValue.SelectedItem = "4";
        _onlyThisBar = new CheckBox { Name = "OnlyThisBar", Content = "Only this bar", IsVisible = selectedBars is null };
        SetBody(
            Label("Beats per bar / beat value"),
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { _beats, new TextBlock { Text = "/", VerticalAlignment = VerticalAlignment.Center }, _beatValue } },
            Label(StandardDialogsPresenter.ScopeText("time signature", selectedBars)),
            _onlyThisBar);
    }

    protected override TimeSignatureAnswer? Answer() =>
        StandardDialogsPresenter.TimeSignature(_beats.Text ?? "", _beatValue.SelectedItem?.ToString(), _onlyThisBar.IsChecked == true) is { } answer
            ? new TimeSignatureAnswer(answer.Num, answer.Denom, answer.OnlyThisBar)
            : null;
}

/// <summary>"Key signature": the key list, minor mode and the scope line.</summary>
public sealed class KeySignatureWindow : BarDialogWindow<KeySignatureAnswer>
{
    private readonly ComboBox _key;
    private readonly CheckBox _minor;
    private readonly CheckBox _onlyThisBar;

    public KeySignatureWindow(int signature, bool minor, string? selectedBars) : base("Key signature", 340, "OK")
    {
        _key = new ComboBox { Name = "Key", HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = SignatureChoices.KeyNames.ToList(), SelectedIndex = StandardDialogsPresenter.KeyIndex(signature) };
        _minor = new CheckBox { Name = "Minor", Content = "Minor mode", IsChecked = minor };
        _onlyThisBar = new CheckBox { Name = "OnlyThisBar", Content = "Only this bar", IsVisible = selectedBars is null };
        SetBody(Label("Key"), _key, _minor, Label(StandardDialogsPresenter.ScopeText("key signature", selectedBars)), _onlyThisBar);
    }

    protected override bool CanAccept() => _key.SelectedIndex >= 0;

    protected override KeySignatureAnswer? Answer() =>
        new(StandardDialogsPresenter.KeySignature(_key.SelectedIndex), _minor.IsChecked == true, _onlyThisBar.IsChecked == true);
}

/// <summary>"Score directions": a tick for each navigation mark and the alternate ending.</summary>
public sealed class DirectionsWindow : BarDialogWindow<DirectionsAnswer>
{
    private readonly Dictionary<string, CheckBox> _checks = new(StringComparer.OrdinalIgnoreCase);
    private readonly TextBox _ending;
    private readonly string[] _existing;

    public DirectionsWindow(string current, int ending) : base("Score directions", 370, "Apply")
    {
        _existing = StandardDialogsPresenter.SplitDirections(current);
        var body = new List<Control> { new TextBlock { Text = "Musical navigation marks and jumps", FontWeight = FontWeight.SemiBold } };
        foreach (var name in StandardDialogsPresenter.DirectionNames)
        {
            var check = new CheckBox
            {
                Content = StandardDialogsPresenter.DirectionLabels[name],
                IsChecked = _existing.Any(value => value.Equals(name, StringComparison.OrdinalIgnoreCase)),
            };
            _checks.Add(name, check);
            body.Add(check);
        }
        _ending = new TextBox { Name = "Ending", Text = StandardDialogsPresenter.ClampEnding(ending).ToString(), Width = 72, HorizontalAlignment = HorizontalAlignment.Left };
        body.Add(Label("Alternate ending (0–8)"));
        body.Add(_ending);
        SetBody(body.ToArray());
    }

    protected override bool CanAccept() => StandardDialogsPresenter.ParseEnding(_ending.Text ?? "") is not null;

    protected override DirectionsAnswer? Answer() => new(
        StandardDialogsPresenter.JoinDirections(_existing, _checks.Where(pair => pair.Value.IsChecked == true).Select(pair => pair.Key)),
        StandardDialogsPresenter.ParseEnding(_ending.Text ?? "") ?? 0);
}

/// <summary>"Add score marker": the name, where it starts and its colour.</summary>
public sealed class MarkerWindow : BarDialogWindow<MarkerAnswer>
{
    private readonly TextBox _name;
    private readonly ComboBox _colour;

    /// <param name="action">"Add" or "Save": the window title and the OK button.</param>
    /// <param name="bar">The 0-based bar the marker starts at.</param>
    public MarkerWindow(string action, string title, string colourHex, int bar) : base($"{action} score marker", 340, action)
    {
        _name = new TextBox { Name = "MarkerName", Text = title };
        var choices = StandardDialogsPresenter.MarkerColours.Select(item => Swatch(item.Name, item.Hex)).ToList();
        var current = choices.FirstOrDefault(item => string.Equals((string?)item.Tag, colourHex, StringComparison.OrdinalIgnoreCase));
        if (current is null && Color.TryParse(colourHex, out _))
        {
            current = Swatch($"Custom ({colourHex.ToUpperInvariant()})", colourHex);
            choices.Add(current);
        }
        _colour = new ComboBox { Name = "MarkerColour", HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = choices, SelectedItem = current ?? choices[0] };
        AutomationProperties.SetName(_colour, "Colour");
        SetBody(Label("Marker name"), _name, Label($"Starts at bar {bar + 1}"), Label("Colour"), _colour);
    }

    // A drop-down row: a small rounded swatch of the colour and its name; the row's Tag is the #RRGGBB text.
    private static ComboBoxItem Swatch(string name, string hex) => new()
    {
        Tag = hex,
        Content = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Children =
            {
                new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(3), VerticalAlignment = VerticalAlignment.Center, Background = Color.TryParse(hex, out var colour) ? new SolidColorBrush(colour) : Brushes.Transparent },
                new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center },
            },
        },
    };

    protected override MarkerAnswer? Answer() =>
        StandardDialogsPresenter.MarkerResult(_name.Text ?? "", (_colour.SelectedItem as ComboBoxItem)?.Tag as string) is { } answer ? new MarkerAnswer(answer.Title, answer.Colour) : null;
}
