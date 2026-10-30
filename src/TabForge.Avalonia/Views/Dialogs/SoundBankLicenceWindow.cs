using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Services;

namespace TabForge.Avalonia.Views.Dialogs;

// Owns: the View licence window of a Sound bank: its name, copyright, licence name, the licence text (the package's file when there is
//   one, else the text stored in the app's resources), the download note and the Open source page / Close buttons.
// Does not own: the licence text itself (SoundBankCatalog) or the Settings list (PreferencesWindow).
// Tests: SoundBankCatalogTests (the text and the note); the window is checked by hand.
internal sealed class SoundBankLicenceWindow : Window
{
    internal const string DownloadNote = "TabForge downloads this file from its official source, checks it against a stored checksum, and does not bundle or alter it.";

    /// <summary>The window for a bank; <paramref name="licenceText"/> is read from the package file or the app's resources.</summary>
    public SoundBankLicenceWindow(SoundBank bank, string licenceText)
    {
        Title = bank.Name + " licence";
        ShowInTaskbar = false;
        DialogMetrics.CentreOnOwner(this);
        this[!BackgroundProperty] = new DynamicResourceExtension("WindowBackground");

        var head = new StackPanel { Spacing = 2 };
        head.Children.Add(Heading(bank.Name));
        head.Children.Add(Muted(bank.Copyright));
        head.Children.Add(Muted(bank.LicenceName));
        var text = new TextBox
        {
            Name = "LicenceText", Text = licenceText, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            MaxHeight = DialogMetrics.TextWindowHeight / 2,
        };
        var note = Muted(DownloadNote);
        note.TextWrapping = TextWrapping.Wrap;

        var source = new Button { Content = "Open source page", HorizontalAlignment = HorizontalAlignment.Left };
        source.Click += async (_, _) => await OpenSourceAsync(bank.SourcePage);
        ToolTip.SetTip(source, bank.SourcePage);
        var close = new Button { Content = "Close", IsDefault = true, IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Close();
        var actions = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(source, Dock.Left);
        DockPanel.SetDock(close, Dock.Right);
        actions.Children.Add(close);
        actions.Children.Add(source);

        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(head);
        stack.Children.Add(new ScrollViewer { Content = text });
        stack.Children.Add(note);
        stack.Children.Add(actions);
        Content = DialogMetrics.Inset(stack);
        Width = DialogMetrics.TextWindowWidth;
    }

    private static TextBlock Heading(string text) => new() { Text = text, FontWeight = FontWeight.SemiBold };

    private static TextBlock Muted(string text)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        block[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");
        return block;
    }

    private async Task OpenSourceAsync(string url)
    {
        if (TopLevel.GetTopLevel(this)?.Launcher is not { } launcher) return;
        try { await launcher.LaunchUriAsync(new Uri(url)); } catch (Exception) { /* swallow: no browser or handler */ }
    }
}
