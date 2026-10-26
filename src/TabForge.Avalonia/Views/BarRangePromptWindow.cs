using TabForge.Avalonia.Platform;
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
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
/// The Delete-bars choice prompt: the question, "Leave empty bars" / "Remove bars", the clips row (Leave goes with the first, "Split and move" with
/// the second; the other is disabled), an "Apply to" scope row, "Remember my choice" and OK / Cancel. Closing returns the BarRangeAnswer, or null on Cancel / Esc.
/// Owns: the window's controls and reading them back. Does not own: the words, labels or answer mapping (BarRangePromptPresenter,
/// ConfirmDialogPresenter) or the edits.
/// Deviation: the window uses the platform title bar; the window draws its own and is reused per owner.
/// Tests: TabForge.Avalonia.Tests.PasteOptionsSpecialAndDeleteBarsTests.
/// </summary>
public sealed class BarRangePromptWindow : Window
{
    private readonly RadioButton[] _choices;
    private readonly RadioButton[] _scopes;
    private readonly RadioButton[] _clips;
    private readonly CheckBox _remember;

    public BarRangePromptWindow(string text, BarRangeAction preselect, bool allTracks, Func<string, string> shortcut)
    {
        Title = BarRangePromptPresenter.Title;
        Width = ConfirmDialogPresenter.WidthFor(hasChoices: true);
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        this[!BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
        this[!ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
        FontSize = DialogMetrics.FontSize;

        var body = new StackPanel { Margin = new Thickness(20, 18), Spacing = 2 };
        body.Children.Add(new TextBlock { Name = "MessageText", Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        var choice = BarRangePromptPresenter.DefaultChoice(preselect);
        _choices = BarRangePromptPresenter.Labels(shortcut).Select((label, i) =>
            new RadioButton { Name = "Choice" + i, Content = label, GroupName = "choice", IsChecked = i == choice }).ToArray();
        foreach (var radio in _choices)
        {
            radio.IsCheckedChanged += (_, _) => ShowClipChoice();
            body.Children.Add(radio);
        }
        _clips = AddGroup(body, BarRangePromptPresenter.ClipHeading, "Clip", BarRangePromptPresenter.ClipLabels, "clips", -1);
        ShowClipChoice();
        _scopes = AddGroup(body, ConfirmDialogPresenter.ScopeHeading, "Scope", BarRangePromptPresenter.Scopes, "scope", BarRangePromptPresenter.DefaultScope(allTracks));

        _remember = new CheckBox { Name = "RememberBox", Content = BarRangePromptPresenter.RememberText, FontSize = 12, Margin = new Thickness(0, 12, 0, 0) };
        _remember[!ForegroundProperty] = new DynamicResourceExtension("TextMuted");
        ToolTip.SetTip(_remember, ConfirmDialogPresenter.RememberToolTip(hasChoices: true));
        body.Children.Add(_remember);

        var ok = new Button { Name = "OkButton", Content = BarRangePromptPresenter.OkText, MinWidth = DialogMetrics.PromptButtonMinWidth, IsDefault = true };
        ok.Classes.Add(AvaloniaDefaultLooks.AccentLook);
        ToolTip.SetTip(ok, ConfirmDialogPresenter.ToolTip(PromptButton.Yes, BarRangePromptPresenter.OkToolTip, defaultIsNo: false));
        ok.Click += (_, _) => Close(Current());
        var cancel = new Button { Name = "CancelButton", Content = BarRangePromptPresenter.CancelText, MinWidth = DialogMetrics.PromptButtonMinWidth, IsCancel = true };
        ToolTip.SetTip(cancel, ConfirmDialogPresenter.ToolTip(PromptButton.No, BarRangePromptPresenter.CancelToolTip, defaultIsNo: false));
        cancel.Click += (_, _) => Close(null);
        body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = DialogMetrics.FooterGap, Children = { ok, cancel } });
        Content = body;
        // Y answers OK as in the prompt; N and Esc cancel.
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Y) Close(Current());
            else if (e.Key is Key.N or Key.Escape) Close(null);
            else return;
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
    }

    // A muted heading over one horizontal row of radio buttons (named prefix + index), the one at checkedIndex ticked.
    private static RadioButton[] AddGroup(StackPanel body, string heading, string prefix, IReadOnlyList<string> labels, string group, int checkedIndex)
    {
        var title = new TextBlock { Text = heading, Margin = new Thickness(0, 10, 0, 2) };
        title[!ForegroundProperty] = new DynamicResourceExtension("TextMuted");
        body.Children.Add(title);
        var radios = labels.Select((label, i) =>
            new RadioButton { Name = prefix + i, Content = label, GroupName = group, IsChecked = i == checkedIndex, Margin = new Thickness(0, 0, 16, 0) }).ToArray();
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.AddRange(radios);
        body.Children.Add(row);
        return radios;
    }

    // The clips follow the bar option: Remove splits and moves them (Leave disabled), Leave empty leaves them (Split disabled).
    private void ShowClipChoice()
    {
        var split = BarRangePromptPresenter.SplitsClips(Math.Max(0, Array.FindIndex(_choices, r => r.IsChecked == true)));
        _clips[0].IsEnabled = !split;
        _clips[0].IsChecked = !split;
        _clips[1].IsEnabled = split;
        _clips[1].IsChecked = split;
    }

    /// <summary>The answer the picked option, scope and remember box stand for.</summary>
    public BarRangeAnswer Current() => BarRangePromptPresenter.Answer(
        Math.Max(0, Array.FindIndex(_choices, r => r.IsChecked == true)), Math.Max(0, Array.FindIndex(_scopes, r => r.IsChecked == true)), _remember.IsChecked == true);
}
