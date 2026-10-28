using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the output choice (driver, device, rate, buffer) made in Settings reaching the settings file with the live engine switch, and Cancel putting both back.
/// Does not own: the engine or the rows themselves (OutputSettingsTests).
/// Tests: MainWindowSettings.CreatePreferencesWindow.
/// </summary>
public class OutputChoiceSavedTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "tf-output-saved-" + Guid.NewGuid().ToString("N"));

    public OutputChoiceSavedTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, true);

    private int BufferOnDisk()
    {
        using var again = MainWindowSettings.Open(Path.Combine(_folder, "settings.json"));
        return again.Settings.Plugins.BufferSize;
    }

    [AvaloniaFact]
    public void ABufferChosenInSettingsIsSavedAtOnceAndCancelPutsSettingAndFileBack()
    {
        using var settings = MainWindowSettings.Open(Path.Combine(_folder, "settings.json"));
        var original = settings.Settings.Plugins.BufferSize;
        var window = settings.CreatePreferencesWindow();
        window.Show();
        window.ShowPage(TabForge.Services.SettingsCatalog.AudioVst);
        Dispatcher.UIThread.RunJobs();
        var box = (ComboBox)window.EditorOf("vst.buffer");
        var other = original == 512 ? "256" : "512";
        box.SelectedItem = other;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(int.Parse(other), settings.Settings.Plugins.BufferSize);   // the live engine follows this value
        settings.Flush();
        Assert.Equal(int.Parse(other), BufferOnDisk());   // saved without OK
        window.Close();   // Cancel
        Dispatcher.UIThread.RunJobs();
        settings.Flush();
        Assert.Equal(original, settings.Settings.Plugins.BufferSize);
        Assert.Equal(original, BufferOnDisk());
    }
}
