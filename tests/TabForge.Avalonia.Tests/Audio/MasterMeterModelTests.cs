using System;
using System.IO;
using System.Linq;
using TabForge.Audio.Metering;
using TabForge.Avalonia.Views;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the bar model of the master meter (fall, hold, clip latch, number) and its saved choices with their Preferences rows.
/// Does not own: the measuring (MasterMeterTests) or the drawing (a capture twin shows it).
/// Tests: this class.
/// </summary>
public class MasterMeterModelTests
{
    private static MasterMeterModel ModelOf(MasterMeterSettings settings) => new(() => settings);

    [Fact]
    public void ClipLightLatchesUntilResetAndANewEngineIsNotAClip()
    {
        var model = ModelOf(new MasterMeterSettings());
        model.Feed(MasterMeterReading.Silent(clips: 5), 0);    // the first reading is the baseline: earlier clips do not light it
        Assert.False(model.ClipLit);
        model.Feed(MasterMeterReading.Silent(clips: 5), 33);
        Assert.False(model.ClipLit);
        Assert.True(model.Feed(MasterMeterReading.Silent(clips: 9), 66));
        Assert.True(model.ClipLit);
        model.Feed(MasterMeterReading.Silent(clips: 9), 99);
        Assert.True(model.ClipLit);                            // latched
        Assert.True(model.ResetClip());
        Assert.False(model.ClipLit);
        model.Feed(MasterMeterReading.Silent(clips: 2), 132);  // an engine restart counts from 0 again: a new baseline
        Assert.False(model.ClipLit);
        model.Feed(MasterMeterReading.Silent(clips: 3), 165);
        Assert.True(model.ClipLit);
    }

    [Fact]
    public void PeakBarsFallAndTheHoldLineStaysForTheChosenTime()
    {
        var settings = new MasterMeterSettings { HoldSeconds = 1 };
        var model = ModelOf(settings);
        var loud = new MasterMeterReading(0.5f, 0.25f, 0, 0, -23, 0);
        model.Feed(loud, 0);
        Assert.InRange(model.LeftDb, -6.1, -5.9);
        Assert.InRange(model.RightDb, -12.1, -11.9);
        model.Feed(MasterMeterReading.Silent(), 100);          // 100 ms later: the bar fell 4 dB, not to the floor
        Assert.InRange(model.LeftDb, -10.1, -9.9);
        Assert.InRange(model.HoldLeftDb, -6.1, -5.9);          // the hold line is still at the peak
        model.Feed(MasterMeterReading.Silent(), 900);
        Assert.InRange(model.HoldLeftDb, -6.1, -5.9);
        model.Feed(MasterMeterReading.Silent(), 1200);         // held for more than 1 s: it drops to the bar
        Assert.True(model.HoldLeftDb < -20);
        settings.HoldSeconds = 0;
        model.Feed(loud, 1300);
        Assert.False(model.HoldShown);
    }

    [Fact]
    public void EachModeShowsItsOwnNumber()
    {
        var settings = new MasterMeterSettings();
        var model = ModelOf(settings);
        var reading = new MasterMeterReading(0.1f, 0.05f, 0.0707f, 0.0354f, -23.04f, 0);
        model.Feed(reading, 0);
        Assert.Equal("-20", model.NumberText);                 // peak: the louder bar, whole dB
        settings.Mode = MasterMeterSettings.Rms;
        model.Feed(reading, 200);
        Assert.Equal("-23", model.NumberText);
        settings.Mode = MasterMeterSettings.Loudness;
        model.Feed(reading, 400);
        Assert.Equal("-23.0", model.NumberText);               // loudness: one decimal
        model.Feed(MasterMeterReading.Silent(), 600);
        Assert.Equal("-inf", model.NumberText);
    }

    [Fact]
    public void ChoicesPersistAndOddValuesReturnToTheDefaults()
    {
        var settings = new AppSettings();
        settings.MasterMeter.Mode = MasterMeterSettings.Loudness; settings.MasterMeter.ShowNumber = false; settings.MasterMeter.HoldSeconds = 3;
        var path = Path.Combine(Path.GetTempPath(), "tf-meter-settings-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            SettingsFileService.SaveAtomic(path, settings);
            var back = SettingsFileService.Load(path).MasterMeter;
            Assert.Equal((MasterMeterSettings.Loudness, false, 3), (back.Mode, back.ShowNumber, back.HoldSeconds));
            var odd = new MasterMeterSettings { Mode = "bogus", HoldSeconds = 7 };
            odd.Normalize();
            Assert.Equal((MasterMeterSettings.Peak, 1), (odd.Mode, odd.HoldSeconds));
            Assert.True(new AppSettings().MasterMeter.ShowNumber);
            Assert.Equal(MasterMeterSettings.Peak, new AppSettings().MasterMeter.Mode);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void PreferencesFindTheMasterMeterRowsAndWriteTheChoices()
    {
        var settings = new AppSettings();
        var rows = PreferencesCatalog.Build(settings);
        var found = SettingsCatalog.Registry(rows, new HotkeySettings()).Where(entry => SettingsCatalog.Matches(entry, "master meter")).Select(entry => entry.Id).ToList();
        Assert.Contains("meter.mode", found); Assert.Contains("meter.number", found); Assert.Contains("meter.hold", found);
        rows.Single(r => r.Key == "meter.mode").Set(MasterMeterSettings.ModeLabel(MasterMeterSettings.Rms));
        rows.Single(r => r.Key == "meter.number").Set(false);
        rows.Single(r => r.Key == "meter.hold").Set("3 s");
        Assert.Equal((MasterMeterSettings.Rms, false, 3), (settings.MasterMeter.Mode, settings.MasterMeter.ShowNumber, settings.MasterMeter.HoldSeconds));
        Assert.Equal("RMS (dB)", rows.Single(r => r.Key == "meter.mode").Get());
    }
}
