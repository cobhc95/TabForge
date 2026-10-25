using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using TabForge.Avalonia;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the Help and App menu rows that open a dialog (About, Shortcuts, the Detailed tutorial) are handled by the window's command table,
//   and the About text starts with the version line; the About dialog has the Close and Third-party licences buttons and the licences
//   target prefers the file next to the exe, then the folder, then the web copy.
// Does not own: the dialogs' contents (AvaloniaDialogTests) or the menu rows (MainWindowMenuTests).
public class HelpMenuTests
{
    [AvaloniaFact]
    public void HelpAboutShortcutsAndDetailedTutorialAreHandledAndAboutStartsWithTheVersion()
    {
        var commands = new MainWindowCommands(new Window(), () => { });
        MainWindowHelpMenu.Register(commands, new Window(), () => new HotkeySettings());
        Assert.True(commands.Handles("Help.About"));
        Assert.True(commands.Handles("App.Shortcuts"));
        Assert.True(commands.Handles("Help.TutorialDetailed"));
        Assert.StartsWith(AppInfo.VersionLine, AboutText.Body);
    }

    [AvaloniaFact]
    public void CheckForUpdatesIsHandled()
    {
        var commands = new MainWindowCommands(new Window(), () => { });
        MainWindowUpdateCheck.Register(commands, new Window(), null, _ => { });
        Assert.True(commands.Handles("Help.CheckForUpdates"));
    }

    [AvaloniaFact]
    public void AboutDialogHasCloseAndThirdPartyLicencesButtons()
    {
        var window = new AboutWindow("About TabForge", AboutText.Body);
        var buttons = window.GetLogicalDescendants().OfType<Button>().ToList();
        Assert.Equal(new[] { MainWindowHelpMenu.CloseButtonText, MainWindowHelpMenu.LicencesButtonText }, buttons.Select(b => (string?)b.Content).ToList());
        Assert.True(buttons[0].IsDefault);
        Assert.False(buttons[1].IsDefault);
        Assert.Equal(MainWindowHelpMenu.CloseButtonToolTip, ToolTip.GetTip(buttons[0]));
        Assert.Equal(MainWindowHelpMenu.LicencesButtonToolTip, ToolTip.GetTip(buttons[1]));
        Assert.Equal(AboutText.Body, window.GetLogicalDescendants().OfType<TextBlock>().First(t => t.Name == "MessageText").Text);
        window.Close();
    }

    [Fact]
    public void LicencesTargetPrefersTheFileThenTheFolderThenTheWebCopy()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tf-about-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var withFile = Directory.CreateDirectory(Path.Combine(root, "file")).FullName;
            File.WriteAllText(Path.Combine(withFile, "THIRD_PARTY.md"), "notices");
            Assert.Equal(Path.Combine(withFile, "THIRD_PARTY.md"), MainWindowHelpMenu.LicencesTarget(withFile));
            var withFolder = Directory.CreateDirectory(Path.Combine(root, "folder")).FullName;
            Directory.CreateDirectory(Path.Combine(withFolder, "licenses"));
            Assert.Equal(Path.Combine(withFolder, "licenses"), MainWindowHelpMenu.LicencesTarget(withFolder));
            var empty = Directory.CreateDirectory(Path.Combine(root, "empty")).FullName;
            Assert.Equal(MainWindowHelpMenu.LicencesUrl, MainWindowHelpMenu.LicencesTarget(empty));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
