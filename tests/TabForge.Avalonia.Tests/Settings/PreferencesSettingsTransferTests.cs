using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// The Settings window's Import settings… and Export settings… (a round trip through SettingsFileService, and a malformed file refused with
/// nothing changed), and the Check now row's click reaching the main window's update command.
/// Owns: these checks. Does not own: the file rules (SettingsFileService, SettingsValidator) or the rows of the window (PreferencesWindowTests).
/// Tests: this class.
/// </summary>
public class PreferencesSettingsTransferTests
{
    private const string RestoreWindowKey = "general.restorewindow";

    private static bool RestoreWindowOf(PreferencesWindow window) =>
        PreferencesCatalog.Build(window.Working).Single(r => r.Key == RestoreWindowKey).Get() is true;

    private static string TempFile() => Path.Combine(Path.GetTempPath(), "tf-prefs-transfer-" + Guid.NewGuid().ToString("N") + ".json");

    /// <summary>Export writes the working settings; importing that file into another window stages the same values.</summary>
    [AvaloniaFact]
    public void ExportedSettingsImportBackWithTheSameValues()
    {
        var path = TempFile();
        try
        {
            var source = new PreferencesWindow(new AvaloniaDialogHost(), new AppSettings());
            source.Write(RestoreWindowKey, false);
            source.ExportSettingsTo(path);

            var target = new PreferencesWindow(new AvaloniaDialogHost(), new AppSettings());
            Assert.True(target.ImportSettingsFrom(path));
            Assert.False(RestoreWindowOf(target));
            Assert.Equal("Settings imported · review, then Apply", target.StatusText);
        }
        finally { File.Delete(path); }
    }

    /// <summary>A file that is not complete JSON is refused: the status line says why, the working settings are the same object with no pending change.</summary>
    [AvaloniaFact]
    public void MalformedSettingsFileIsRefusedAndNothingChanges()
    {
        var path = TempFile();
        try
        {
            File.WriteAllText(path, "{ \"General\": ");
            var window = new PreferencesWindow(new AvaloniaDialogHost(), new AppSettings());
            var working = window.Working;
            var before = RestoreWindowOf(window);
            var pendingBefore = window.PendingChanges();

            Assert.False(window.ImportSettingsFrom(path));
            Assert.StartsWith("Import failed", window.StatusText);
            Assert.Same(working, window.Working);
            Assert.Equal(before, RestoreWindowOf(window));
            Assert.Equal(pendingBefore, window.PendingChanges());
        }
        finally { File.Delete(path); }
    }

    /// <summary>The Check now button, clicked, runs the main window's update command through the row actions.</summary>
    [AvaloniaFact]
    public void CheckNowButtonRunsTheUpdateCommand()
    {
        var ran = new List<string>();
        var actions = new PreferencesRowActions { RunCommand = ran.Add, ManageLinkedAudio = _ => Task.CompletedTask, ManageQuarantine = _ => Task.CompletedTask };
        var window = new PreferencesWindow(new AvaloniaDialogHost(), new AppSettings(), actions);
        window.ShowPage(SettingsCatalog.General);

        var button = Assert.IsType<Button>(window.EditorOf("general.checknow"));
        Assert.True(button.IsEnabled);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(new[] { "Help.CheckForUpdates" }, ran);
    }
}
