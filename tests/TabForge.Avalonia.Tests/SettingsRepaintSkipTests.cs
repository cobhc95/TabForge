using System;
using System.IO;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the settings saves that change nothing. The score repaints only when a score value or layout row changed, and the settings store
/// notifies its observers only when the settings text it last told them differs.
/// Does not own: the Score page rows (ScoreSettingsBindingTests), the settings file (SettingsFileService).
/// Tests: ScoreSettingsBinding (RefreshCount), AppSettingsStore (MarkChanged notifications).
/// </summary>
public class SettingsRepaintSkipTests
{
    [AvaloniaFact]
    public void ScoreRepaintsOnlyWhenAScoreValueChanges()
    {
        var settings = new AppSettings();
        var binding = new ScoreSettingsBinding(new ScorePreviewView(), settings);
        var afterOpen = binding.RefreshCount;

        binding.Apply(settings);
        Assert.Equal(afterOpen, binding.RefreshCount);

        settings.Appearance.ScoreTextSize += 1;
        binding.Apply(settings);
        Assert.Equal(afterOpen + 1, binding.RefreshCount);
    }

    [Fact]
    public void ChangesAfterTheStoreClosedAreDroppedAndNoTimerIsArmed()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tf-settings-closed-{Guid.NewGuid():N}.json");
        var store = AppSettingsStore.Open(path, TimeSpan.FromMilliseconds(20));
        try
        {
            store.MarkChanged();
            store.Dispose();
            Assert.True(File.Exists(path));
            File.Delete(path);

            store.MarkChanged();
            System.Threading.Thread.Sleep(150);

            Assert.False(File.Exists(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void SettingsObserversAreToldOnlyWhenTheSettingsTextChanged()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tf-settings-notify-{Guid.NewGuid():N}.json");
        var store = AppSettingsStore.Open(path, TimeSpan.FromHours(1));
        try
        {
            var notifications = 0;
            store.Changed += _ => notifications++;

            store.MarkChanged();
            Assert.Equal(1, notifications);

            store.MarkChanged();
            Assert.Equal(1, notifications);

            store.Settings.Appearance.ScoreSpacing += 0.1;
            store.MarkChanged();
            Assert.Equal(2, notifications);
        }
        finally
        {
            store.Dispose();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
