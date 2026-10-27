using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the Tools preview "Settings…" entry opens on the main window's live settings store, not a throw-away copy.
// Does not own: the Settings window itself (PreferencesWindow tests) or the menu rows (MainWindowMenuTests).
// Tests: this file.
public class PreviewSettingsDoorTests
{
    [AvaloniaFact]
    public void SettingsEntryUsesTheMainWindowsLiveStore()
    {
        var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tf-avalonia-tests", "door-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var settings = MainWindowSettings.Open(Path.Combine(folder, "settings.json"));
            var window = new MainWindow(settings);
            Assert.Same(settings, PreviewToolsMenu.SettingsOf(window));
            Assert.Null(PreviewToolsMenu.SettingsOf(new Window()));
            window.Close();
        }
        finally { Directory.Delete(folder, true); }
    }
}
