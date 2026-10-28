using System.Text.Json;
using TabForge.Audio;
using TabForge.Audio.Contracts;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the saved output choice (driver, device, rate, buffer) surviving a save and load, and the session-only fallback to WASAPI when it will not open.
// Does not own: the engine process or the real driver.
// Tests: this file.
public class OutputFallbackTests
{
    private static EngineConfig Of(PluginSettings s) => new(s.Driver, s.Device, s.SampleRate, s.BufferSize, false);

    [Fact]
    public void TheChosenOutputRoundTripsThroughTheSettingsFile()
    {
        var saved = new PluginSettings { Driver = AudioDrivers.Asio, Device = "Focusrite USB ASIO", SampleRate = 96000, BufferSize = 128 };
        var back = JsonSerializer.Deserialize<PluginSettings>(JsonSerializer.Serialize(saved))!;
        Assert.Equal((AudioDrivers.Asio, "Focusrite USB ASIO", 96000, 128), (back.Driver, back.Device, back.SampleRate, back.BufferSize));
    }

    [Fact]
    public void AnUnavailableSavedDriverFallsBackForTheSessionWithoutChangingTheSetting()
    {
        var settings = new PluginSettings { Driver = AudioDrivers.Asio, Device = "Focusrite USB ASIO", SampleRate = 96000, BufferSize = 128 };
        var fallback = new OutputFallback();
        Assert.Equal(AudioDrivers.Asio, fallback.Apply(Of(settings)).Driver);   // tried first
        Assert.Equal("ASIO device Focusrite USB ASIO not available, using WASAPI", fallback.Failed(Of(settings)));
        var now = fallback.Apply(Of(settings));
        Assert.Equal((AudioDrivers.WasapiShared, ""), (now.Driver, now.Device));
        Assert.Equal((AudioDrivers.Asio, "Focusrite USB ASIO", 96000, 128), (settings.Driver, settings.Device, settings.SampleRate, settings.BufferSize));   // the saved choice stays
        Assert.Null(fallback.Failed(now));   // WASAPI failing has nothing further to fall back to
        settings.Device = "Other ASIO";
        Assert.Equal(AudioDrivers.Asio, fallback.Apply(Of(settings)).Driver);   // another choice is tried as chosen
        Assert.Equal(AudioDrivers.Asio, new OutputFallback().Apply(Of(new PluginSettings { Driver = AudioDrivers.Asio })).Driver);   // the next start tries it again
    }
}
