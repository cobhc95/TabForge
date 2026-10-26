using TabForge.Avalonia.Platform;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Presenters;
using TabForge.Avalonia.Views.Dialogs;

namespace TabForge.Avalonia.Views;

/// <summary>What a prompt window closed with: the answer, and the radio choice and remember box as the user left them.</summary>
public readonly record struct PromptOutcome(PromptAnswer Answer, int SelectedChoice, bool RememberTicked);

/// <summary>
/// One small track or confirm prompt (a <see cref="PromptSpec"/>) as an Avalonia window. Enter presses the default button, Esc
/// cancels, Y and N answer Yes and No where the prompt did. Closing it any other way answers Cancel.
/// Owns: the window's layout and its key handling. Does not own: the wording or the answer mapping (PromptTextsPresenter).
/// Tests: PromptWindowTests.
/// </summary>
public sealed class PromptWindow : Window
{
    private readonly List<RadioButton> _choices = new();
    private readonly CheckBox? _remember;

    /// <summary>The answer the window closed with; null while it is open.</summary>
    public PromptOutcome? Outcome { get; private set; }

    public PromptWindow(PromptSpec spec)
    {
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Title = spec.Title;
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        MinWidth = 360;
        MaxWidth = 560;
        this[!BackgroundProperty] = new DynamicResourceExtension("WindowBackground");

        var body = new StackPanel { Margin = new Thickness(18), Spacing = 12 };
        body.Children.Add(new TextBlock { Name = "PromptMessage", Text = spec.Message, TextWrapping = TextWrapping.Wrap });
        if (spec.Choices is { Count: > 0 })
        {
            for (var i = 0; i < spec.Choices.Count; i++)
            {
                var radio = new RadioButton { Content = spec.Choices[i], GroupName = "PromptChoice", IsChecked = i == 0 };
                _choices.Add(radio);
                body.Children.Add(radio);
            }
        }
        if (spec.RememberText is not null)
        {
            _remember = new CheckBox { Name = "PromptRemember", Content = spec.RememberText };
            body.Children.Add(_remember);
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        buttons.Children.Add(AnswerButton(spec.YesText, PromptAnswer.Yes, isDefault: !spec.DefaultIsNo, isCancel: false));
        buttons.Children.Add(AnswerButton(spec.NoText, PromptAnswer.No, isDefault: spec.DefaultIsNo, isCancel: !spec.ShowCancel));
        if (spec.ShowCancel) buttons.Children.Add(AnswerButton("Cancel", PromptAnswer.Cancel, isDefault: false, isCancel: true));
        body.Children.Add(buttons);
        Content = body;

        AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (Outcome is not null) return;
            var answer = e.Key switch
            {
                Key.Enter => spec.DefaultIsNo ? PromptAnswer.No : PromptAnswer.Yes,
                Key.Escape => PromptAnswer.Cancel,
                Key.Y when !spec.DefaultIsNo => PromptAnswer.Yes,
                Key.N => PromptAnswer.No,
                _ => (PromptAnswer?)null,
            };
            if (answer is not { } chosen) return;
            e.Handled = true;
            Answer(chosen);
        }, RoutingStrategies.Tunnel);
        Closed += (_, _) => Outcome ??= new PromptOutcome(PromptAnswer.Cancel, SelectedChoice, RememberTicked);
    }

    /// <summary>The radio choice the user has picked (0 without choices).</summary>
    public int SelectedChoice => Math.Max(0, _choices.FindIndex(radio => radio.IsChecked == true));

    /// <summary>True when the remember box is ticked.</summary>
    public bool RememberTicked => _remember?.IsChecked == true;

    /// <summary>Shows the prompt over <paramref name="owner"/> and returns what it closed with.</summary>
    public static async Task<PromptOutcome> AskAsync(Window owner, PromptSpec spec) =>
        (await DialogLauncher.AskFrom<PromptWindow, PromptOutcome?>(owner, () => new PromptWindow(spec), window => window.Outcome))
        ?? new PromptOutcome(PromptAnswer.Cancel, 0, false);

    private Button AnswerButton(string text, PromptAnswer answer, bool isDefault, bool isCancel)
    {
        var button = new Button { Content = text, MinWidth = DialogMetrics.DialogButtonMinWidth, IsDefault = isDefault, IsCancel = isCancel };
        if (isDefault) button.Classes.Add(AvaloniaDefaultLooks.AccentLook);
        button.Click += (_, _) => Answer(answer);
        return button;
    }

    private void Answer(PromptAnswer answer)
    {
        Outcome = new PromptOutcome(answer, SelectedChoice, RememberTicked);
        Close();
    }
}
