using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using TabForge.Controllers;

namespace TabForge.Views;

/// <summary>The notice rows of one window: each is inserted into the dock panel that holds the status bar, directly after it.</summary>
internal sealed class StatusNoticeBars : IStatusNotices
{
    private readonly FrameworkElement _statusBar;

    public StatusNoticeBars(FrameworkElement statusBar) => _statusBar = statusBar;

    public IStatusNotice Create(string? automationName, string buttonText, Action onButton) =>
        new StatusNoticeBar(_statusBar, automationName, buttonText, onButton);
}

/// <summary>A message row with one button, in the panel colours. It joins the window the first time it is shown.</summary>
internal sealed class StatusNoticeBar : IStatusNotice
{
    private readonly FrameworkElement _statusBar;
    private readonly Border _bar;
    private readonly TextBlock _text;
    private bool _inserted;

    public StatusNoticeBar(FrameworkElement statusBar, string? automationName, string buttonText, Action onButton)
    {
        _statusBar = statusBar;
        _text = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        _text.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        var button = new Button { Content = buttonText, Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(10, 0, 0, 0) };
        button.Click += (_, _) => onButton();
        DockPanel.SetDock(button, Dock.Right);
        var row = new DockPanel { LastChildFill = true, Margin = new Thickness(8, 3, 8, 3) };
        row.Children.Add(button);
        row.Children.Add(_text);
        _bar = new Border { Child = row, BorderThickness = new Thickness(0, 1, 0, 0) };
        _bar.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        _bar.SetResourceReference(Border.BorderBrushProperty, "BorderSoftBrush");
        if (automationName is not null) AutomationProperties.SetName(_bar, automationName);
        DockPanel.SetDock(_bar, Dock.Bottom);
    }

    /// <summary>The row, once it has been shown for the first time (self-tests read its visibility).</summary>
    internal Border? Bar => _inserted ? _bar : null;

    public void Show(string text, string? toolTip = null)
    {
        if (!_inserted)
        {
            if (_statusBar.Parent is not DockPanel dock) return;
            dock.Children.Insert(dock.Children.IndexOf(_statusBar) + 1, _bar);
            _inserted = true;
        }
        _text.Text = text;
        _text.ToolTip = toolTip ?? text;
        _bar.Visibility = Visibility.Visible;
    }

    public void Hide()
    {
        if (_inserted) _bar.Visibility = Visibility.Collapsed;
    }
}
