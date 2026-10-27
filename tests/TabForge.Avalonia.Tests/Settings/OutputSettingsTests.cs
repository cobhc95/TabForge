using System;
using System.Linq;
using System.Text.Json;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the buffer size and sample rate remembered per output (driver + device): the store, the move of an old single value, switching,
// the plain rate wording, the rates each driver offers (fake device answers) and the Preferences rows.
// Does not own: the engine applying a rate (EngineRateAndLimiterTests) or the device queries themselves (WindowsAudioDevices).
// Tests: this file.
public class OutputSettingsTests
{
    private static PluginSettings Wasapi(int buffer = 512, int rate = 44100) => new() { Driver = AudioDrivers.WasapiShared, Device = "", BufferSize = buffer, SampleRate = rate };

    [Fact]
    public void AnOutputKeepsItsOwnBufferSizeAndRateWhenYouSwitchAwayAndBack()
    {
        var pl = Wasapi(512, 44100);
        pl.SelectOutput(AudioDrivers.Asio, "Focusrite", () => 128);
        Assert.Equal((128, 0), (pl.BufferSize, pl.SampleRate));   // never used: the driver's size and the device default
        pl.BufferSize = 64; pl.SampleRate = 96000;
        pl.SelectOutput(AudioDrivers.WasapiShared, "", () => throw new InvalidOperationException("a known output needs no default"));
        Assert.Equal((512, 44100), (pl.BufferSize, pl.SampleRate));
        pl.SelectOutput(AudioDrivers.Asio, "Focusrite", () => 999);
        Assert.Equal((64, 96000), (pl.BufferSize, pl.SampleRate));
    }

    [Fact]
    public void TheDeviceOfADriverIsAnOutputOfItsOwn()
    {
        var pl = Wasapi(512, 44100);
        pl.SelectOutput(AudioDrivers.WasapiShared, "Speakers", () => 256);
        pl.BufferSize = 1024;
        pl.SelectOutput(AudioDrivers.WasapiShared, "", () => 77);
        Assert.Equal(512, pl.BufferSize);
        pl.SelectOutput(AudioDrivers.WasapiShared, "Speakers", () => 77);
        Assert.Equal(1024, pl.BufferSize);
    }

    [Fact]
    public void LoadingAnOldFileSeedsTheCurrentOutputsProfileFromTheSingleValues()
    {
        const string json = """{"Plugins":{"Driver":"WASAPI (exclusive)","Device":"Dac","BufferSize":384,"SampleRate":96000}}""";
        var pl = SettingsMigration.Normalize(json, JsonSerializer.Deserialize<AppSettings>(json)!).Plugins!;
        var profile = pl.OutputProfiles[PluginSettings.OutputKey(AudioDrivers.WasapiExclusive, "Dac")];
        Assert.Equal((384, 96000), (profile.BufferSize, profile.SampleRate));
    }

    [Fact]
    public void ChoosingTheSameOutputChangesNothing()
    {
        var pl = Wasapi(512, 44100);
        pl.SelectOutput(AudioDrivers.WasapiShared, "", () => 1);
        Assert.Equal((512, 44100), (pl.BufferSize, pl.SampleRate));
        Assert.Empty(pl.OutputProfiles);
    }

    [Fact]
    public void TheOldSingleValuesBelongToTheOutputTheyWereSavedWith()
    {
        var file = JsonSerializer.Deserialize<AppSettings>("""{"Plugins":{"Driver":"WASAPI (exclusive)","Device":"Dac","BufferSize":384,"SampleRate":96000}}""");
        var pl = file!.Plugins!;
        Assert.Equal((384, 96000), (pl.BufferSize, pl.SampleRate));
        pl.SelectOutput(AudioDrivers.DirectSound, "", () => 256);
        pl.SelectOutput(AudioDrivers.WasapiExclusive, "Dac", () => 1);
        Assert.Equal((384, 96000), (pl.BufferSize, pl.SampleRate));
    }

    [Fact]
    public void TheRememberedOutputsSurviveSavingAndLoading()
    {
        var pl = Wasapi(512, 44100);
        pl.SelectOutput(AudioDrivers.Asio, "Focusrite", () => 128);
        pl.BufferSize = 64;
        var back = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(new AppSettings { Plugins = pl }))!.Plugins!;
        back.SelectOutput(AudioDrivers.WasapiShared, "", () => 1);
        Assert.Equal(512, back.BufferSize);
        back.SelectOutput(AudioDrivers.Asio, "Focusrite", () => 1);
        Assert.Equal(64, back.BufferSize);
    }

    [Theory]
    [InlineData(44100, "44.1 kHz")]
    [InlineData(48000, "48 kHz")]
    [InlineData(88200, "88.2 kHz")]
    [InlineData(192000, "192 kHz")]
    [InlineData(0, "Device default")]
    public void RatesAreWrittenInPlainKilohertzAndReadBack(int hz, string text)
    {
        Assert.Equal(text, OutputRates.Label(hz));
        Assert.Equal(hz, OutputRates.Parse(text));
    }

    [Fact]
    public void AsioOffersTheRatesTheDriverAccepts()
    {
        var accepted = OutputRates.Supported(AudioDrivers.Asio, hz => hz is 44100 or 48000 or 96000, () => 0);
        Assert.Equal(new[] { 44100, 48000, 96000 }, accepted);
        Assert.Equal(new[] { "Device default", "44.1 kHz", "48 kHz", "96 kHz" }, OutputRates.Choices(accepted, 48000));
    }

    [Fact]
    public void SharedWasapiOffersOnlyTheWindowsMixRateAndExclusiveWhatTheDeviceAccepts()
    {
        Assert.Equal(new[] { 44100 }, OutputRates.Supported(AudioDrivers.WasapiShared, _ => true, () => 44100));
        Assert.Equal(new[] { 48000, 96000 }, OutputRates.Supported(AudioDrivers.WasapiExclusive, hz => hz is 48000 or 96000, () => 44100));
    }

    [Fact]
    public void DirectSoundOffersTheUsualRates()
    {
        Assert.Equal(new[] { 44100, 48000, 88200, 96000 }, OutputRates.Supported(AudioDrivers.DirectSound, _ => false, () => 0));
    }

    [Fact]
    public void AStoredRateTheDeviceCannotDoStaysInTheListAndAnUnaskedDeviceOffersTheUsualRates()
    {
        Assert.Contains("176.4 kHz", OutputRates.Choices(new[] { 48000 }, 176400));
        Assert.Equal(AudioDrivers.SampleRates.Length + 1, OutputRates.Choices(Array.Empty<int>(), 0).Count);
    }

    [Fact]
    public void TheSampleRateRowSitsBesideTheBufferSizeNotUnderMoreOptions()
    {
        var rows = SettingsCatalog.Build(new AppSettings()).Where(r => r.Category == SettingsCatalog.AudioVst && r.Group == "Output device").ToList();
        var buffer = rows.First(r => r.Key == "vst.buffer");
        var rate = rows.First(r => r.Key == "vst.samplerate");
        Assert.False(rate.More);
        Assert.Equal("Sample rate", rate.Title);
        Assert.Equal(buffer.Order + 1, rate.Order);
        Assert.Equal(5, rows.Count(r => r.More));   // the other five stay behind More options
        Assert.Contains("rate", rate.SearchText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheRateRowStoresTheChoiceAndSwitchingTheDriverShowsThatDriversOwnValues()
    {
        var settings = new AppSettings();
        var pl = settings.Plugins!;
        pl.BufferSize = 512;
        var rows = SettingsCatalog.Build(settings);
        rows.First(r => r.Key == "vst.samplerate").Set("96 kHz");
        Assert.Equal(96000, pl.SampleRate);
        rows.First(r => r.Key == "vst.driver").Set(AudioDrivers.DirectSound);
        Assert.Equal((AudioDrivers.DefaultBuffer, 0), (pl.BufferSize, pl.SampleRate));
        Assert.Equal("Device default", SettingsCatalog.Build(settings).First(r => r.Key == "vst.samplerate").Get());
        SettingsCatalog.Build(settings).First(r => r.Key == "vst.driver").Set(AudioDrivers.WasapiShared);
        Assert.Equal((512, 96000), (pl.BufferSize, pl.SampleRate));
    }

    [Fact]
    public void TheValidatorKeepsRemembersOutputsInsideTheirLimits()
    {
        var pl = Wasapi();
        pl.OutputProfiles["WASAPI (shared)|x"] = new OutputProfile { BufferSize = 5, SampleRate = 12345 };
        var checkedSettings = SettingsValidator.Normalize(new AppSettings { Plugins = pl });
        var kept = checkedSettings.Plugins!.OutputProfiles["WASAPI (shared)|x"];
        Assert.Equal((AudioDrivers.MinBuffer, 0), (kept.BufferSize, kept.SampleRate));
    }
}
