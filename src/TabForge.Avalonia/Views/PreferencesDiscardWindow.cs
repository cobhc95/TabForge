using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views;

/// <summary>
/// The "Unsaved changes" question of the Preferences window: the message, the changed settings (colours as swatches) and the
/// Keep editing / Discard changes buttons. Keep editing is the Enter and Esc button; the close box also keeps editing.
/// Owns: building the window from a DiscardSummary and recording the answer (Yes discards, No keeps editing).
/// Does not own: the wording or the lines (DiscardPreferencesPresenter) or showing it (AvaloniaDialogHost).
/// Tests: PreferencesDiscardWindowTests.
/// </summary>
public sealed class PreferencesDiscardWindow : Window
{
    private MessageBoxAnswer? _answer;

    public PreferencesDiscardWindow(DiscardSummary summary)
    {
        Title = DiscardPreferencesPresenter.Title;
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        MinWidth = DialogMetrics.DiscardMinWidth;
        MaxWidth = DialogMetrics.DiscardMaxWidth;
        ShowInTaskbar = false;
        this[!BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
        this[!ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
        FontSize = DialogMetrics.FontSize;

        var list = new StackPanel { Name = "ChangeList", Spacing = DialogMetrics.DiscardLineSpacing }.WithMargin(DialogMetrics.DiscardListGap);
        foreach (var line in summary.Lines) list.Children.Add(BuildLine(line));
        if (summary.MoreText is not null) list.Children.Add(new TextBlock { Name = "MoreText", Text = summary.MoreText });

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = DialogMetrics.DiscardButtonSpacing }.WithMargin(DialogMetrics.DiscardActionsGap);
        var discard = ActionButton("DiscardButton", DiscardPreferencesPresenter.DiscardLabel, MessageBoxAnswer.Yes, safe: false);
        var keep = ActionButton("KeepButton", DiscardPreferencesPresenter.KeepLabel, MessageBoxAnswer.No, safe: true);
        keep.TabIndex = 0;
        actions.Children.Add(discard);
        actions.Children.Add(keep);
        Opened += (_, _) => keep.Focus();

        var body = new StackPanel().WithMargin(DialogMetrics.DiscardInset);
        body.Children.Add(new TextBlock { Name = "MessageText", Text = DiscardPreferencesPresenter.Message, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(list);
        body.Children.Add(actions);
        Content = body;
    }

    /// <summary>The answer to report: the button pressed, else No (keep editing).</summary>
    public MessageBoxAnswer Result => _answer ?? MessageBoxAnswer.No;

    private Button ActionButton(string name, string text, MessageBoxAnswer answer, bool safe)
    {
        var button = new Button { Name = name, Content = text, MinWidth = DialogMetrics.DialogButtonMinWidth, Height = DialogMetrics.DiscardButtonHeight, IsDefault = safe, IsCancel = safe };
        if (safe) button.Classes.Add(AvaloniaDefaultLooks.AccentLook);
        button.Click += (_, _) => { _answer = answer; Close(answer); };
        return button;
    }

    private static Control BuildLine(DiscardLine line)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = DialogMetrics.DiscardLineGap };
        row.Children.Add(new TextBlock { Text = line.Text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
        if (line.OldColour is not null && line.NewColour is not null)
        {
            row.Children.Add(Swatch("OldSwatch", line.OldColour));
            row.Children.Add(new TextBlock { Text = "→", VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(Swatch("NewSwatch", line.NewColour));
        }
        if (line.ToolTip is not null) ToolTip.SetTip(row, line.ToolTip);
        return row;
    }

    // The swatch shows the stored colour itself (data, not a theme token); its outline keeps a dark or light swatch visible on either theme.
    private static Control Swatch(string name, string hex)
    {
        var swatch = new Border { Name = name, Width = DialogMetrics.SwatchSize, Height = DialogMetrics.SwatchSize, CornerRadius = new CornerRadius(DialogMetrics.SwatchCorner), BorderThickness = DialogMetrics.SwatchOutline };
        swatch[!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderBrush");
        if (Color.TryParse(hex, out var colour)) swatch.Background = new SolidColorBrush(colour);
        return swatch;
    }
}
