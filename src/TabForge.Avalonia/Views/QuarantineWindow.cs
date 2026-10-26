using TabForge.Platform.Theme;
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Markup.Xaml.MarkupExtensions;
using TabForge.Presenters;
using TabForge.Avalonia.Views.Dialogs;

namespace TabForge.Avalonia.Views;

/// <summary>
/// The "Plug-ins switched off after a crash" window, the Avalonia quarantine window: the same caption,
/// rows, texts and buttons, all read from QuarantinePresenter. The preview has no plug-in settings, so it opens on <see cref="SamplePaths"/>.
/// Allow again changes the saved plug-in settings and reloads the audio engine, so it needs the full app: its button says so on the
/// status line and changes nothing.
/// Owns: the layout, the rows and their event wiring. Does not own: the texts and rows (QuarantinePresenter), and the allow-again rule
/// and the settings change, which only the full app makes.
/// Tests: TabForge.Avalonia.Tests.QuarantineWindowTests.
/// </summary>
public sealed class QuarantineWindow : Window
{
    /// <summary>Sample plug-in paths for the preview. One has a trailing separator, as a stored folder path can.</summary>
    public static IReadOnlyList<string> SamplePaths { get; } = new[]
    {
        @"C:\Plug-ins\Sample Chorus.dll", @"C:\Plug-ins\Sample Delay.vst3", @"C:\Plug-ins\Sample Reverb\",
    };

    private readonly StackPanel _rows = new() { Name = "RowsPanel", Spacing = ChromeMetrics.QuarantineRowGap };
    private readonly TextBlock _status = new() { Name = "StatusText", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly IReadOnlyList<string> _quarantinedPaths;
    private readonly Action<string>? _allowAgain;

    /// <param name="quarantinedPaths">The paths listed; the list is read again after each Allow again, so pass the live list.</param>
    /// <param name="allowAgain">Takes the path off the plug-in list and saves it; null (the preview) keeps the status line text instead.</param>
    public QuarantineWindow(IReadOnlyList<string> quarantinedPaths, Action<string>? allowAgain = null)
    {
        _quarantinedPaths = quarantinedPaths;
        _allowAgain = allowAgain;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Title = QuarantinePresenter.Title;
        Width = ChromeMetrics.QuarantineWidth; Height = ChromeMetrics.QuarantineHeight; MinWidth = ChromeMetrics.QuarantineMinWidth; MinHeight = ChromeMetrics.QuarantineMinHeight;
        this[!BackgroundProperty] = new DynamicResourceExtension("WindowBackground");
        this[!ForegroundProperty] = new DynamicResourceExtension("TextPrimary");

        var caption = new TextBlock
        {
            Name = "CaptionText", Text = QuarantinePresenter.Caption, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
        };
        var close = new Button
        {
            Name = "CloseButton", Content = QuarantinePresenter.CloseText, IsCancel = true, IsDefault = true,
            Padding = new Thickness(14, 3, 14, 3), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0)
        };
        close.Click += (_, _) => Close();
        var footer = new StackPanel { Children = { _status, close } };

        var dock = new DockPanel { Margin = DialogMetrics.ContentInset };
        DockPanel.SetDock(caption, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        dock.Children.Add(caption);
        dock.Children.Add(footer);
        dock.Children.Add(new ScrollViewer { Content = _rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = dock;

        ShowRows(quarantinedPaths);
    }

    /// <summary>The text on the status line, empty until an Allow again button is pressed.</summary>
    public string StatusText => _status.Text ?? "";

    private void ShowRows(IReadOnlyList<string> paths)
    {
        _rows.Children.Clear();
        var presenter = new QuarantinePresenter(paths);
        if (presenter.IsEmpty)
        {
            var none = new TextBlock { Name = "EmptyText", Text = QuarantinePresenter.EmptyText };
            none[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");
            _rows.Children.Add(none);
            return;
        }
        foreach (var entry in presenter.Entries)
        {
            var allow = new Button
            {
                Name = "AllowAgainButton", Content = QuarantinePresenter.AllowAgainText, Padding = new Thickness(10, 2, 10, 2),
                Margin = new Thickness(8, 0, 0, 0)
            };
            ToolTip.SetTip(allow, QuarantinePresenter.AllowAgainToolTip);
            ToolWindowParts.NotAvailable(allow, _allowAgain is null);
            var path = entry.Path;
            allow.Click += (_, _) =>
            {
                if (_allowAgain is null) { _status.Text = "Allowing a plug-in again isn't available yet."; return; }
                _allowAgain(path);
                ShowRows(_quarantinedPaths);
            };
            DockPanel.SetDock(allow, Dock.Right);

            var name = new TextBlock { Text = entry.FileName, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            ToolTip.SetTip(name, entry.Path);

            var row = new DockPanel();
            row.Children.Add(allow);
            row.Children.Add(name);
            _rows.Children.Add(row);
        }
    }
}
