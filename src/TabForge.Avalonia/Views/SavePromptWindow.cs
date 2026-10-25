using TabForge.Avalonia.Platform;
using System.Collections.Generic;
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

/// <summary>
/// The save-changes, discard and degraded-save questions: a question, an optional "Show changes" list, an optional "Don't ask me again"
/// box, and Yes / No (/ Cancel). Y and N answer as their buttons do, and Esc answers Cancel.
/// Owns: building the window from a SavePrompt (its title, question and button tooltips), the key routing, and the answer it records.
/// Does not own: what each answer means (SavePromptPresenter.Answer, or the caller for the discard question), or showing it (AvaloniaDialogHost).
/// Deviation: the window uses the platform title bar, so its close box answers Cancel; the WPF window draws its own title bar and × button.
/// Tests: TabForge.Avalonia.Tests.CommandPaletteSavePromptTuningTests.
/// </summary>
public sealed class SavePromptWindow : Window
{
    private readonly bool _defaultIsNo;
    private readonly CheckBox? _remember;

    public SavePromptWindow(SavePrompt prompt, bool showCancel = true, IReadOnlyList<string>? details = null, string? rememberText = null, bool defaultIsNo = false)
    {
        _defaultIsNo = defaultIsNo;
        Title = prompt.Title;
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        MinWidth = 380;
        MaxWidth = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        this[!BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
        this[!ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
        FontSize = DialogMetrics.FontSize;

        var icon = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 12, 0) };
        icon[!Border.BackgroundProperty] = new DynamicResourceExtension("Accent");
        icon.Child = new TextBlock { Name = "IconGlyph", Text = "?", FontSize = 16, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        icon.Child[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextLegible");
        DockPanel.SetDock(icon, Dock.Left);
        var message = new TextBlock { Name = "MessageText", Text = prompt.Message, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };

        var footer = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        if (details is { Count: > 0 }) footer.Children.Add(BuildDetails(details));
        if (rememberText is not null)
        {
            _remember = new CheckBox { Name = "RememberBox", Content = rememberText, FontSize = 12, Margin = new Thickness(0, 0, 0, 8) };
            footer.Children.Add(_remember);
        }
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        actions.Children.Add(ActionButton("YesButton", "Yes", prompt.YesToolTip, PromptButton.Yes, isDefault: !defaultIsNo));
        actions.Children.Add(ActionButton("NoButton", "No", prompt.NoToolTip, PromptButton.No, isDefault: defaultIsNo));
        // Where "No" already means "go back", a Cancel button would just duplicate it.
        if (showCancel) actions.Children.Add(ActionButton("CancelButton", "Cancel", "Return without closing", PromptButton.Cancel));
        footer.Children.Add(actions);

        var body = new DockPanel { Margin = new Thickness(18), LastChildFill = true };
        DockPanel.SetDock(footer, Dock.Bottom);
        body.Children.Add(footer);
        body.Children.Add(icon);
        body.Children.Add(message);
        Content = body;

        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
    }

    /// <summary>The button pressed; null until one is. A close by any other route reads as Cancel.</summary>
    public PromptButton? Pressed { get; private set; }

    /// <summary>The answer to report: the button pressed, else Cancel.</summary>
    public PromptButton Result => Pressed ?? PromptButton.Cancel;

    /// <summary>The "Don't ask me again" box is ticked; false when the prompt has no box.</summary>
    public bool RememberChoice => _remember?.IsChecked == true;

    private void Answer(PromptButton answer)
    {
        if (Pressed is not null) return;
        Pressed = answer;
        Close(answer);
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: Answer(PromptButton.Cancel); break;
            case Key.Y when !_defaultIsNo: Answer(PromptButton.Yes); break;
            case Key.N: Answer(PromptButton.No); break;
            default: return;
        }
        e.Handled = true;
    }

    private Button ActionButton(string name, string text, string tip, PromptButton answer, bool isDefault = false)
    {
        var button = new Button { Name = name, Content = text, MinWidth = DialogMetrics.PromptButtonMinWidth, Height = 28, Padding = new Thickness(14, 0), IsDefault = isDefault };
        if (isDefault) button.Classes.Add(AvaloniaDefaultLooks.AccentLook);
        ToolTip.SetTip(button, ConfirmDialogPresenter.ToolTip(answer, tip, _defaultIsNo));
        button.Click += (_, _) => Answer(answer);
        return button;
    }

    // A collapsed "Show changes" link that expands into the list of edits.
    private static Control BuildDetails(IReadOnlyList<string> details)
    {
        var list = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        foreach (var line in details)
            list.Children.Add(new TextBlock { Text = "• " + line, TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 1) });
        var scroller = new ScrollViewer { Name = "DetailsList", Content = list, MaxHeight = 170, IsVisible = false };
        var link = new TextBlock { Name = "DetailsLink", Text = ConfirmDialogPresenter.DetailsLink(details.Count, open: false), FontSize = 12, TextDecorations = TextDecorations.Underline };
        link[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("Accent");
        link.PointerPressed += (_, _) =>
        {
            scroller.IsVisible = !scroller.IsVisible;
            link.Text = ConfirmDialogPresenter.DetailsLink(details.Count, scroller.IsVisible);
        };
        return new StackPanel { Children = { link, scroller } };
    }
}
