using TabForge.Avalonia.Platform;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using TabForge.Avalonia.Views.Dialogs;

namespace TabForge.Avalonia.Views;

/// <summary>
/// A one-line text prompt: a label, a text box holding the default (selected, focused) and OK / Cancel. Closing returns the typed
/// text, or null on Cancel / Esc. Enter presses OK.
/// Owns: the window's controls and reading the text back. Does not own: the wording or what the text means (a presenter such as RepeatSelectionPromptPresenter).
/// Deviation: the window uses the platform title bar; the prompt (GpDialogs.Prompt) draws the system one.
/// Tests: TabForge.Avalonia.Tests.TextPromptWindowTests.
/// </summary>
public sealed class TextPromptWindow : Window
{
    private readonly TextBox _box;

    public TextPromptWindow(string title, string label, string initial)
    {
        Title = title;
        Width = 380;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        this[!BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
        this[!ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
        FontSize = DialogMetrics.FontSize;

        _box = new TextBox { Name = "PromptText", Text = initial };
        var ok = new Button { Name = "OkButton", Content = "OK", MinWidth = DialogMetrics.PromptButtonMinWidth, IsDefault = true };
        ok.Classes.Add(AvaloniaDefaultLooks.AccentLook);
        ok.Click += (_, _) => Close(_box.Text);
        var cancel = new Button { Name = "CancelButton", Content = "Cancel", MinWidth = DialogMetrics.PromptButtonMinWidth, IsCancel = true };
        cancel.Click += (_, _) => Close(null);
        Content = new StackPanel
        {
            Margin = new Thickness(20, 16), Spacing = 10,
            Children =
            {
                new TextBlock { Name = "PromptLabel", Text = label, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap },
                _box,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { ok, cancel } },
            },
        };
        AddHandler(KeyDownEvent, (_, e) => { if (e.Key == Key.Escape) { Close(null); e.Handled = true; } }, RoutingStrategies.Tunnel);
        Opened += (_, _) => { _box.Focus(); _box.SelectAll(); };
    }
}
