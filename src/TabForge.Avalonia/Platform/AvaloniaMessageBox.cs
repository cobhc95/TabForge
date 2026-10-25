using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Presenters;

namespace TabForge.Avalonia.Platform;

/// <summary>
/// The themed message box of the Avalonia app, the counterpart of the WPF <c>Views/ThemedMessageBox</c>.
/// Owns: building the window from MessageBoxPresenter (buttons, Enter default, Esc button, close-box answer, icon glyph and tone).
/// Does not own: the labels, answers and tones (MessageBoxPresenter) or showing it (AvaloniaDialogHost).
/// Tests: AvaloniaDialogTests.
/// </summary>
public sealed class AvaloniaMessageBox : Window
{
    private readonly MessageBoxButtons buttons;

    private readonly CheckBox? check;

    /// <summary>The button pressed; null until one is.</summary>
    public MessageBoxAnswer? Answer { get; private set; }

    /// <summary>True when the check box was ticked as a button was pressed; false when the box was closed another way or has no check box.</summary>
    public bool Ticked { get; private set; }

    /// <summary>Builds the box; <paramref name="checkText"/> adds an unticked check box at the lower left (null for none).</summary>
    public AvaloniaMessageBox(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxAnswer defaultAnswer, string? checkText = null)
    {
        this.buttons = buttons;
        Title = caption;
        ShowInTaskbar = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        MinWidth = 320;
        MaxWidth = 560;
        this[!BackgroundProperty] = new DynamicResourceExtension("WindowBackground");

        var (glyph, tone) = MessageBoxPresenter.Icon(icon);
        var body = new DockPanel { Margin = new Thickness(20, 18, 20, 14), LastChildFill = true };
        var content = new StackPanel();
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        // The check box sits at the lower left and the buttons at the lower right, on one row (as the WPF box).
        var actions = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 18, 0, 0) };
        if (checkText is not null)
        {
            check = new CheckBox { Name = "RememberCheck", Content = checkText.Replace("_", "", StringComparison.Ordinal), IsChecked = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            check[!ContentControl.ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
            DockPanel.SetDock(check, Dock.Left);
            actions.Children.Add(check);
        }
        actions.Children.Add(row);
        if (glyph.Length > 0)
        {
            var ring = new Ellipse { Width = 30, Height = 30, StrokeThickness = 2 };
            ring[!Shape.StrokeProperty] = new DynamicResourceExtension(TonePaletteName(tone));
            var mark = new TextBlock { Text = glyph, FontSize = 16, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            mark[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(TonePaletteName(tone));
            var iconPanel = new Grid { Name = "IconGlyph", Width = 30, Height = 30, VerticalAlignment = VerticalAlignment.Top };
            iconPanel.Children.Add(ring);
            iconPanel.Children.Add(mark);
            DockPanel.SetDock(iconPanel, Dock.Left);
            body.Children.Add(iconPanel);
        }
        var message = new TextBlock { Name = "MessageText", Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 13, MaxWidth = 500, Margin = new Thickness(14, 0, 0, 0) };
        message[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
        content.Children.Add(message);
        content.Children.Add(actions);
        body.Children.Add(content);

        var enter = MessageBoxPresenter.DefaultAnswer(buttons, defaultAnswer);
        foreach (var (label, answer) in MessageBoxPresenter.Buttons(buttons))
        {
            var isDefault = answer == enter;
            var button = new Button
            {
                Content = label.Replace("_", "", StringComparison.Ordinal), MinWidth = 84, Height = 28, Padding = new Thickness(14, 0),
                Margin = new Thickness(8, 0, 0, 0), Tag = answer,
                IsDefault = isDefault, IsCancel = MessageBoxPresenter.IsCancelButton(buttons, answer),
            };
            if (isDefault)
            {
                button.Classes.Add(AvaloniaDefaultLooks.AccentLook);
                button[!Button.BackgroundProperty] = new DynamicResourceExtension("AccentBrush");
                button[!Button.ForegroundProperty] = new DynamicResourceExtension("OnAccentBrush");
            }
            button.Click += (_, _) => { Answer = answer; Ticked = check?.IsChecked == true; Close(answer); };
            row.Children.Add(button);
        }
        Content = body;
    }

    /// <summary>The answer to report: the button pressed, else the close-box answer (YesNo has none, so it reports No).</summary>
    public MessageBoxAnswer Result => Answer ?? MessageBoxPresenter.CloseAnswer(buttons) ?? MessageBoxAnswer.No;

    /// <summary>Shows a message box over <paramref name="owner"/> through <paramref name="host"/> and returns its answer.</summary>
    public static async Task<MessageBoxAnswer> Show(AvaloniaDialogHost host, Window owner, string text, string caption,
        MessageBoxButtons buttons = MessageBoxButtons.OK, MessageBoxIcon icon = MessageBoxIcon.None, MessageBoxAnswer defaultAnswer = MessageBoxAnswer.None)
    {
        var (answer, _) = await ShowWithCheckBox(host, owner, text, caption, buttons, icon, checkText: null, defaultAnswer);
        return answer;
    }

    /// <summary>
    /// Shows a message box with the optional check box <paramref name="checkText"/> (null for none) and returns the answer and whether the
    /// check box was ticked when a button was pressed.
    /// </summary>
    public static async Task<(MessageBoxAnswer Answer, bool Ticked)> ShowWithCheckBox(AvaloniaDialogHost host, Window owner, string text, string caption,
        MessageBoxButtons buttons, MessageBoxIcon icon, string? checkText, MessageBoxAnswer defaultAnswer = MessageBoxAnswer.None)
    {
        var box = new AvaloniaMessageBox(text, caption, buttons, icon, defaultAnswer, checkText);
        var shown = await host.ShowModal<MessageBoxAnswer?>(box, owner);
        return (shown ?? box.Result, box.Ticked);
    }

    // The warning icon's amber, with a dark and a light value (the WPF AmberBrush uses the same key).
    private static string TonePaletteName(MessageBoxTone tone) => tone switch
    {
        MessageBoxTone.Amber => "WarningIcon",
        MessageBoxTone.Danger => "Danger",
        _ => "Accent",
    };
}
