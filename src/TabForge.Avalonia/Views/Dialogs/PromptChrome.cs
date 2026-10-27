// Owns: the one look the prompt dialogs share: window set-up (short OS title, theme colours, fixed width, sized to its content), the heading inside the dialog,
//   the single muted explanation line, the footer row (Remember choice left, small right-aligned actions: primary accent + secondary outline) and the action button.
//   Choice cards are RadioCard. A new prompt builds its body from these parts and inherits the style.
// Does not own: a dialog's content, its answer or its keys (the window), the sizes (DialogMetrics), the colours (theme keys).
// Tests: TabForge.Avalonia.Tests.PromptChromeTests, plus each prompt's own tests.
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Platform;

namespace TabForge.Avalonia.Views.Dialogs;

/// <summary>Builds the shared parts of the prompt dialogs.</summary>
internal static class PromptChrome
{
    /// <summary>Gives <paramref name="window"/> the prompt set-up: <paramref name="osTitle"/>, width, height from content, not resizable, centred on the owner.</summary>
    public static void Setup(Window window, string osTitle, double width)
    {
        window.Title = osTitle;
        window.Width = width;
        window.SizeToContent = SizeToContent.Height;
        window.CanResize = false;
        DialogMetrics.CenterOnOwner(window);
        window.ShowInTaskbar = false;
        window[!global::Avalonia.Controls.Primitives.TemplatedControl.BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
        window[!global::Avalonia.Controls.Primitives.TemplatedControl.ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
        window.FontSize = DialogMetrics.FontSize;
    }

    /// <summary>The heading inside the dialog: larger, semibold.</summary>
    public static TextBlock Heading(string text) =>
        new TextBlock { Name = "HeadingText", Text = text, FontSize = DialogMetrics.PromptHeadingSize, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap }.WithMargin(DialogMetrics.PromptHeadingGap);

    /// <summary>A muted, smaller explanation line (at most <paramref name="maxLines"/> lines when above 0); <paramref name="tip"/> shows in full on hover.</summary>
    public static TextBlock Muted(string name, string text, int maxLines = 0, string? tip = null)
    {
        var block = new TextBlock { Name = name, Text = text, FontSize = DialogMetrics.PromptMutedSize, TextWrapping = TextWrapping.Wrap }.WithMargin(DialogMetrics.PromptMutedGap);
        if (maxLines > 0) { block.MaxLines = maxLines; block.TextTrimming = TextTrimming.CharacterEllipsis; }
        block[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");
        if (tip is not null) ToolTip.SetTip(block, tip);
        return block;
    }

    /// <summary>The heading and the muted line a prompt starts with.</summary>
    public static StackPanel Column(string heading, TextBlock muted) =>
        new StackPanel { Children = { Heading(heading), muted } }.WithMargin(DialogMetrics.PromptInset);

    /// <summary>A small action button; <paramref name="primary"/> takes the accent look and is the default.</summary>
    public static Button Action(string name, string text, bool primary, bool isCancel = false)
    {
        var button = new Button { Name = name, Content = text, MinWidth = DialogMetrics.PromptActionMinWidth, Height = DialogMetrics.PromptButtonHeight, IsDefault = primary, IsCancel = isCancel, VerticalContentAlignment = VerticalAlignment.Center }
            .WithPadding(DialogMetrics.PromptActionPadding);
        if (primary) button.Classes.Add(AvaloniaDefaultLooks.AccentLook);
        return button;
    }

    /// <summary>The footer row: <paramref name="remember"/> (with its optional muted <paramref name="hint"/>) on the left, <paramref name="actions"/> right-aligned.</summary>
    public static Grid Footer(CheckBox? remember, Control? hint, params Button[] actions)
    {
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (remember is not null) left.Children.Add(remember);
        if (hint is not null) { hint.VerticalAlignment = VerticalAlignment.Center; left.Children.Add(hint.WithMargin(DialogMetrics.PromptHintGap)); }
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = DialogMetrics.PromptButtonSpacing, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var action in actions) right.Children.Add(action);
        Grid.SetColumn(right, 1);
        return new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { left, right } }.WithMargin(DialogMetrics.PromptFooterGap);
    }
}
