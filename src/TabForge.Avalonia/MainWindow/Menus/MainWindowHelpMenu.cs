using System.Collections.Generic;
using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Platform.Storage;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Presenters;
using TabForge.Services;
using Trace = TabForge.Services.Trace;

namespace TabForge.Avalonia;

// Owns: the Help and App menu rows that open a plain dialog: Help > About (the About text with Close and Third-party licences
// buttons, opening THIRD_PARTY.md / the licenses folder / the GitHub copy), App > Shortcuts
//   (the keyboard shortcut list), registered into the window's command table.
// Does not own: the menu rows (MainMenuTable), the texts (AboutText, ShortcutHelp) or the message box (AvaloniaMessageBox).
// Tests: TabForge.Avalonia.Tests.HelpMenuTests.
/// <summary>Adds the Help and App menu handlers to the window's command table.</summary>
public static class MainWindowHelpMenu
{
    private const string AboutCaption = "About TabForge";
    private const string ShortcutsCaption = "Keyboard shortcuts";
    // The About dialog's second button and the two buttons' tooltips (its yesToolTip/noToolTip).
    internal const string CloseButtonText = "Close";
    internal const string LicencesButtonText = "Third-party licences";
    internal const string CloseButtonToolTip = "Close this window";
    internal const string LicencesButtonToolTip = "Open the third-party licence notices";
    // Where the licences live: the file next to the exe, else the folder next to the exe, else the copy on GitHub.
    private const string LicencesFileName = "THIRD_PARTY.md";
    private const string LicencesFolderName = "licenses";
    internal const string LicencesUrl = "https://github.com/cobhc95/TabForge/blob/main/THIRD_PARTY.md";
    // The About dialog's size: the message box's width range, message width, button size and text size (one place, as the message box's are).
    internal const double DialogMinWidth = 320;
    internal const double DialogMaxWidth = 560;
    internal const double MessageMaxWidth = 500;
    internal const double MessageFontSize = 13;
    internal const double ButtonMinWidth = 84;
    internal const double ButtonHeight = 28;
    internal const double ButtonGap = 8;

    /// <summary>Registers the handlers; <paramref name="hotkeys"/> is read each time Shortcuts opens, so the list shows the current bindings.</summary>
    /// <summary>The Help menu command ids this class handles.</summary>
    public static readonly IReadOnlyList<string> Commands = new[] { "Help.About", "App.Shortcuts" };

    public static void Register(MainWindowCommands commands, Window owner, Func<HotkeySettings> hotkeys)
    {
        commands.Add("Help.About", () => _ = ShowAboutAsync(owner));
        commands.Add("App.Shortcuts", () => _ = ShowMessage(owner, ShortcutHelp.Build(hotkeys()), ShortcutsCaption));
    }

    private static async Task ShowAboutAsync(Window owner)
    {
        var openLicences = await DialogLauncher.Ask<AboutWindow, bool?>(owner, () => new AboutWindow(AboutCaption, AboutText.Body));
        if (openLicences == true) await OpenThirdPartyLicencesAsync(owner);
    }

    /// <summary>The licences target for an app directory: THIRD_PARTY.md next to the exe, else the licenses folder, else the copy on GitHub.</summary>
    internal static string LicencesTarget(string baseDir)
    {
        var target = System.IO.Path.Combine(baseDir, LicencesFileName);
        if (!System.IO.File.Exists(target))
        {
            target = System.IO.Path.Combine(baseDir, LicencesFolderName);
            if (!System.IO.Directory.Exists(target))
                target = LicencesUrl;
        }
        return target;
    }

    /// <summary>True for a target the About dialog may open: the fixed web copy, or a file or folder inside <paramref name="baseDir"/> (the app folder).</summary>
    internal static bool IsAllowedLicencesTarget(string target, string baseDir) =>
        target == LicencesUrl || Services.MediaPathPolicy.IsInside(target, baseDir);

    /// <summary>Shows the licences file inside TabForge (many PCs have no app for .md); the folder or web copy opens through the launcher.</summary>
    private static async Task OpenThirdPartyLicencesAsync(Window owner)
    {
        var baseDir = AppContext.BaseDirectory;
        var target = LicencesTarget(baseDir);
        if (!IsAllowedLicencesTarget(target, baseDir)) return;
        if (target != LicencesUrl && System.IO.File.Exists(target))
        {
            try
            {
                await new LicencesTextWindow(await System.IO.File.ReadAllTextAsync(target)).ShowDialog(owner);
                return;
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { Trace.Error(Trace.Ui, "licences: " + ex.Message); /* fall through to the launcher */ }
        }
        await LaunchLicencesAsync(owner.Launcher, target);
    }

    private static async Task LaunchLicencesAsync(ILauncher launcher, string target)
    {
        try
        {
            var opened = target == LicencesUrl ? await launcher.LaunchUriAsync(new Uri(target))
                : System.IO.Directory.Exists(target) ? await launcher.LaunchDirectoryInfoAsync(new System.IO.DirectoryInfo(target))
                : await launcher.LaunchFileInfoAsync(new System.IO.FileInfo(target));
            if (!opened) Trace.Error(Trace.Ui, "open file or folder: no handler");
        }
        catch (Exception ex) when (ex is System.IO.IOException or InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            Trace.Error(Trace.Ui, "open file or folder: no handler: failed"); // no browser or folder handler: nothing more to try
        }
    }

    private static Task<MessageBoxAnswer> ShowMessage(Window owner, string text, string caption) =>
        AvaloniaMessageBox.Show(new AvaloniaDialogHost(), owner, text, caption, MessageBoxButtons.OK, MessageBoxIcon.Information, MessageBoxAnswer.OK);
}

/// <summary>The About dialog: the About text with Close (default) and Third-party licences buttons.</summary>
internal sealed class AboutWindow : Window
{
    public AboutWindow(string caption, string text)
    {
        Title = caption;
        ShowInTaskbar = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        MinWidth = MainWindowHelpMenu.DialogMinWidth;
        MaxWidth = MainWindowHelpMenu.DialogMaxWidth;
        this[!BackgroundProperty] = new DynamicResourceExtension("WindowBackground");

        var (glyph, tone) = MessageBoxPresenter.Icon(MessageBoxIcon.Information);
        var body = new DockPanel { Margin = new Thickness(20, 18, 20, 14), LastChildFill = true };
        var content = new StackPanel();
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var actions = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 18, 0, 0) };
        actions.Children.Add(row);
        if (glyph.Length > 0)
        {
            var ring = new Ellipse { Width = 30, Height = 30, StrokeThickness = 2 };
            ring[!Shape.StrokeProperty] = new DynamicResourceExtension(TonePaletteName(tone));
            var mark = new TextBlock { Text = glyph, FontSize = 16, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            mark[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(TonePaletteName(tone));
            var iconPanel = new Grid { Name = "IconGlyph", Width = 30, Height = 30, VerticalAlignment = VerticalAlignment.Top };
            iconPanel.Children.Add(ring);
            iconPanel.Children.Add(mark);
            DockPanel.SetDock(iconPanel, Dock.Left);
            body.Children.Add(iconPanel);
        }
        var message = new TextBlock { Name = "MessageText", Text = text, TextWrapping = TextWrapping.Wrap, FontSize = MainWindowHelpMenu.MessageFontSize, MaxWidth = MainWindowHelpMenu.MessageMaxWidth };
        message[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
        content.Children.Add(message);
        content.Children.Add(actions);
        body.Children.Add(content);

        var close = AboutButton(MainWindowHelpMenu.CloseButtonText, MainWindowHelpMenu.CloseButtonToolTip, isDefault: true);
        close.Click += (_, _) => Close(false);
        var licences = AboutButton(MainWindowHelpMenu.LicencesButtonText, MainWindowHelpMenu.LicencesButtonToolTip, isDefault: false);
        licences.Click += (_, _) => Close(true);
        row.Children.Add(close);
        row.Children.Add(licences);
        Content = body;
    }

    private static Button AboutButton(string text, string tip, bool isDefault)
    {
        var button = new Button
        {
            Content = text, MinWidth = MainWindowHelpMenu.ButtonMinWidth, Height = MainWindowHelpMenu.ButtonHeight,
            Padding = new Thickness(14, 0), Margin = new Thickness(MainWindowHelpMenu.ButtonGap, 0, 0, 0), IsDefault = isDefault,
        };
        if (isDefault)
        {
            button.Classes.Add(AvaloniaDefaultLooks.AccentLook);
            button[!Button.BackgroundProperty] = new DynamicResourceExtension("AccentBrush");
            button[!Button.ForegroundProperty] = new DynamicResourceExtension("OnAccentBrush");
        }
        ToolTip.SetTip(button, tip);
        return button;
    }

    // The warning icon's amber, with a dark and a light value.
    private static string TonePaletteName(MessageBoxTone tone) => tone switch
    {
        MessageBoxTone.Amber => "WarningIcon",
        MessageBoxTone.Danger => "Danger",
        _ => "Accent",
    };
}
