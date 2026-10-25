using TabForge.Avalonia.Platform;
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
using TabForge.Services;
using TabForge.Avalonia.Views.Dialogs;

namespace TabForge.Avalonia.Views;

/// <summary>
/// The combined paste dialog, the twin of the WPF PasteOptionsDialog: its own title row ("Paste" and a close cross, the
/// window has no platform title bar), one group of options per question (the recommended one picked) each with its own "Remember my
/// choice" box, and a footer with Paste (default) and Cancel. Closing returns the answers, or null when the paste is cancelled.
/// Owns: the window's controls, the title-row drag and reading the controls back. Does not own: the order, labels, words or answers
/// (PasteOptionsPresenter), the questions it is given (the caller), or the paste.
/// Shape: a rounded card inset 12 DIP from the window edge, with a thin SoftBorder outline, on the WindowBrush window; the window
/// itself has square corners and no drop shadow (a transparent window is not used here).
/// Tests: TabForge.Avalonia.Tests.PasteOptionsSpecialAndDeleteBarsTests.
/// </summary>
public sealed class PasteOptionsWindow : Window
{
    // The card's inset from the window edge, the root margin of the WPF PasteOptionsDialog.
    private const double CardInset = 12;
    private readonly PasteOptionsPresenter _presenter;
    private readonly Dictionary<PasteQuestion, (RadioButton[] Options, CheckBox Remember)> _groups = new();

    public PasteOptionsWindow(IEnumerable<PasteQuestion> questions)
    {
        _presenter = new PasteOptionsPresenter(questions);
        Title = PasteOptionsPresenter.Title;
        Width = 470;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        SystemDecorations = SystemDecorations.None;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        this[!ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
        FontSize = DialogMetrics.FontSize;

        var body = new StackPanel { Margin = new Thickness(20, 14, 20, 14) };
        foreach (var (question, index) in _presenter.Questions.Select((q, i) => (q, i)))
            body.Children.Add(BuildQuestion(question, first: index == 0));

        var title = TitleRow();
        var footer = Footer();
        DockPanel.SetDock(title, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        var root = new DockPanel { Children = { title, footer, body } };
        var card = new Border { Margin = new Thickness(CardInset), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = root };
        card[!Border.BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
        card[!Border.BorderBrushProperty] = new DynamicResourceExtension("SoftBorder");
        Content = card;
        AddHandler(KeyDownEvent, (_, e) => { if (e.Key == Key.Escape) { Close(null); e.Handled = true; } }, RoutingStrategies.Tunnel);
    }

    /// <summary>The answers as the options stand now.</summary>
    public PasteAnswers Current() => _presenter.Answers(
        q => System.Array.FindIndex(_groups[q].Options, r => r.IsChecked == true), q => _groups[q].Remember.IsChecked == true);

    // "Paste" on the left and the close cross on the right; dragging the row moves the window.
    private Control TitleRow()
    {
        var close = new Button
        {
            Name = "CloseButton", Content = "×", Width = 34, Height = 28, Padding = new Thickness(0), BorderThickness = new Thickness(0),
            Background = Brushes.Transparent, Focusable = false, HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        AutomationProperties.SetName(close, "Cancel paste");
        ToolTip.SetTip(close, PasteOptionsPresenter.CancelToolTip);
        close.Click += (_, _) => Close(null);
        var title = new TextBlock { Text = PasteOptionsPresenter.Title, VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Margin = new Thickness(14, 0, 0, 0) };
        title[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
        var row = new DockPanel { Height = 34, Children = { close, title } };
        DockPanel.SetDock(close, Dock.Right);
        row.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(row).Properties.IsLeftButtonPressed && e.Source is not Button) BeginMoveDrag(e);
        };
        var strip = new Border { Child = row, CornerRadius = new CornerRadius(7, 7, 0, 0) };
        strip[!Border.BackgroundProperty] = new DynamicResourceExtension("ChromeStrip");
        return strip;
    }

    private Control Footer()
    {
        var paste = new Button { Name = "PasteButton", Content = "Paste", MinWidth = DialogMetrics.PromptButtonMinWidth, IsDefault = true };
        paste.Classes.Add(AvaloniaDefaultLooks.AccentLook);
        ToolTip.SetTip(paste, PasteOptionsPresenter.PasteToolTip);
        AutomationProperties.SetName(paste, "Paste");
        paste.Click += (_, _) => Close(Current());
        var cancel = new Button { Name = "CancelButton", Content = "Cancel", MinWidth = DialogMetrics.PromptButtonMinWidth, IsCancel = true };
        ToolTip.SetTip(cancel, PasteOptionsPresenter.CancelToolTip);
        AutomationProperties.SetName(cancel, "Cancel");
        cancel.Click += (_, _) => Close(null);
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(14, 10, 14, 12),
            Children = { paste, cancel },
        };
        var footer = new Border { Child = actions, BorderThickness = new Thickness(0, 1, 0, 0), CornerRadius = new CornerRadius(0, 0, 7, 7) };
        footer[!Border.BackgroundProperty] = new DynamicResourceExtension("Panel2Brush");
        footer[!Border.BorderBrushProperty] = new DynamicResourceExtension("SoftBorder");
        return footer;
    }

    private Control BuildQuestion(PasteQuestion question, bool first)
    {
        var panel = new StackPanel { Margin = new Thickness(0, first ? 0 : 14, 0, 0) };
        var heading = new TextBlock { Text = PasteQuestionInfo.Title(question), FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
        AutomationProperties.SetName(heading, PasteQuestionInfo.Title(question));
        panel.Children.Add(heading);
        var labels = _presenter.OptionLabels(question);
        var options = labels.Select((label, i) => new RadioButton
        {
            Name = $"Q{(int)question}Option{i}", Content = label, GroupName = "paste-" + (int)question, IsChecked = i == 0, Margin = new Thickness(6, 2, 0, 2), Padding = new Thickness(4, 0, 0, 0)
        }).ToArray();
        foreach (var option in options) panel.Children.Add(option);
        var remember = new CheckBox { Name = $"Q{(int)question}Remember", Content = PasteOptionsPresenter.RememberText, FontSize = 12, Margin = new Thickness(6, 6, 0, 0) };
        ToolTip.SetTip(remember, PasteOptionsPresenter.RememberToolTip);
        panel.Children.Add(remember);
        _groups[question] = (options, remember);
        return panel;
    }
}
