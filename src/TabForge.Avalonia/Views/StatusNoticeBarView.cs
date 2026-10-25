using System;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Views.Dialogs;

namespace TabForge.Avalonia.Views;

/// <summary>
/// A status notice row: a message with one optional action button and a close button, in the panel colours, hidden until shown; the window docks it
/// (the "Video saved" row at the top above the workspace, the others under the status bar).
/// Owns: the row, its text and tooltip (the given tooltip, else the text), its kind tint (Info/Warn/Error via theme tokens), showing or hiding it, and staying hidden while the status bar is hidden.
/// Does not own: what the notice says or when it shows (the caller, NotificationBar for the top bar), what the action button does, or transient messages (MainWindowStatusBar).
/// Tests: TabForge.Avalonia.Tests.PanelBarsTests, TabForge.Avalonia.Tests.NotificationBarTests.
/// </summary>
public enum NotificationKind
{
    /// <summary>A plain note (5 s when transient, 8 s with an action).</summary>
    Info,
    /// <summary>A warning that stays until dismissed, acted on or resolved.</summary>
    Warn,
    /// <summary>An error that stays until dismissed, acted on or resolved.</summary>
    Error,
}

/// <summary>
/// A status notice row: a message with one optional action button and a close button, in the panel colours, hidden until shown.
/// </summary>
public sealed class StatusNoticeBarView : UserControl
{
    private readonly TextBlock _text;
    private readonly Button _actionButton;
    private readonly Button _closeButton;
    private readonly Border _bar;
    private readonly DockPanel _dock;
    private Action? _onAction;
    private bool _shown;
    private bool _statusBarVisible = true;

    public StatusNoticeBarView(string? automationName, string buttonText, Action onButton)
    {
        Name = "StatusNoticeBar";
        IsVisible = false;
        _text = new TextBlock { Name = "NoticeText", VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        _text[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
        _actionButton = new Button { Name = "NoticeButton", Content = buttonText, Padding = DialogMetrics.NoticeActionPadding, Margin = DialogMetrics.NoticeButtonGap };
        _actionButton.Click += (_, _) => _onAction?.Invoke();
        DockPanel.SetDock(_actionButton, Dock.Right);
        _closeButton = new Button { Name = "NoticeClose", Content = "✕", Padding = DialogMetrics.NoticeClosePadding, Margin = DialogMetrics.NoticeButtonGap };
        ToolTip.SetTip(_closeButton, "Dismiss");
        AutomationProperties.SetName(_closeButton, "Dismiss notice");
        _closeButton.Click += (_, _) => CloseRequested?.Invoke();
        DockPanel.SetDock(_closeButton, Dock.Right);
        _onAction = onButton;
        // The close X only joins the row for the unified top bar (NotificationBar subscribes); the bottom rows keep their single action button,
        // so their PanelBarsTests.Single() still sees one button.
        _dock = new DockPanel { LastChildFill = true, Margin = new Thickness(8, 3), Children = { _actionButton, _text } };
        _bar = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = _dock,
        };
        _bar[!Border.BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
        _bar[!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderSoftBrush");
        if (automationName is not null) AutomationProperties.SetName(this, automationName);
        Content = _bar;
    }

    /// <summary>Raised when the close (X) button is pressed; the owner (NotificationBar) dismisses the current notice.</summary>
    public event Action? CloseRequested;

    /// <summary>Shows or hides the action button; null label or callback hides it.</summary>
    public void SetAction(string? label, Action? callback)
    {
        if (label is null || callback is null)
        {
            _onAction = null;
            _actionButton.IsVisible = false;
            return;
        }
        _onAction = callback;
        _actionButton.Content = label;
        _actionButton.IsVisible = true;
    }

    /// <summary>Tints the row's border via theme tokens (both themes): Info the soft border, Warn the warning icon, Error danger.</summary>
    public void SetKind(NotificationKind kind)
    {
        var key = kind switch
        {
            NotificationKind.Warn => "WarningIcon",
            NotificationKind.Error => "Danger",
            _ => "BorderSoftBrush",
        };
        _bar[!Border.BorderBrushProperty] = new DynamicResourceExtension(key);
    }

    public void Show(string text, string? toolTip = null)
    {
        _text.Text = text;
        ToolTip.SetTip(_text, toolTip ?? text);
        _shown = true;
        // The close X only joins the row for the unified top bar (NotificationBar subscribes); the bottom rows keep their single action button.
        if (CloseRequested is not null && !_dock.Children.Contains(_closeButton)) _dock.Children.Insert(0, _closeButton);
        else if (CloseRequested is null && _dock.Children.Contains(_closeButton)) _dock.Children.Remove(_closeButton);
        UpdateVisibility();
    }

    public void Hide()
    {
        _shown = false;
        UpdateVisibility();
    }

    /// <summary>Whether the status bar itself is shown (General.ShowStatusBar): the notice row hides with it.</summary>
    public bool StatusBarVisible
    {
        get => _statusBarVisible;
        set
        {
            _statusBarVisible = value;
            UpdateVisibility();
        }
    }

    private void UpdateVisibility() => IsVisible = _shown && _statusBarVisible;
}
