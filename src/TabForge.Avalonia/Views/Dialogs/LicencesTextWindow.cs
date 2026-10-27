using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;

namespace TabForge.Avalonia.Views.Dialogs;

// Owns: the read-only window that shows the third-party licences text inside TabForge (no .md handler or editor needed, same on every OS).
// Does not own: finding the licences file (MainWindowHelpMenu).
// Tests: HelpMenuTests.
internal sealed class LicencesTextWindow : Window
{
    internal const string Caption = "Third-party licences";

    public LicencesTextWindow(string text)
    {
        Title = Caption;
        ShowInTaskbar = false;
        DialogMetrics.CentreOnOwner(this);
        this[!BackgroundProperty] = new DynamicResourceExtension("WindowBackground");
        var box = DialogMetrics.Inset(new TextBox { Name = "LicencesText", Text = text, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontFamily = TabForge.Avalonia.Platform.AvaloniaFonts.Family("Consolas, Menlo, monospace") });
        var close = DialogMetrics.Inset(new Button { Content = "Close", IsDefault = true, IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right });
        close.Click += (_, _) => Close();
        var dock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(close, Dock.Bottom);
        dock.Children.Add(close);
        dock.Children.Add(box);
        Content = dock;
        Width = DialogMetrics.TextWindowWidth;
        Height = DialogMetrics.TextWindowHeight;
    }
}
