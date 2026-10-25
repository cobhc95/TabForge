using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using TabForge.Avalonia.Views;
using TabForge.Audio.Contracts;
using TabForge.Documents;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the status bar's audio interface button in the preview: always shown with the configured interface (or the running output), at the far right
//   of the bar, plain at rest and lit with the hover fill and accent-soft border under the pointer; the idle text's form and the last report's latency.
// Does not own: the running text (AudioDeviceStatusPresenter), the Preferences page the click opens (StatusBarViewTests), the bar's layout (StatusBarRightEndTests).
// Tests: this class.
public sealed class StatusBarAudioInterfaceTests
{
    private const string ConfiguredIdle = "WASAPI (shared): Windows default device (48 kHz)";

    [AvaloniaFact]
    public void IdleAppShowsTheConfiguredInterfaceAtTheFarRightAndNoMidiName()
    {
        var main = new MainWindow(first: new SongDocument(() => null) { Project = DemoSongFactory.Create() });
        main.Show();
        Dispatcher.UIThread.RunJobs();
        var bar = main.GetLogicalDescendants().OfType<StatusBarView>().Single();
        var audio = bar.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "AudioDevice");
        var output = bar.GetLogicalDescendants().OfType<TextBlock>().Single(t => t.Name == "StatusOutput");
        Assert.True(audio.IsVisible);
        Assert.Equal(ConfiguredIdle, bar.GetLogicalDescendants().OfType<TextBlock>().Single(t => t.Name == "AudioDeviceText").Text);
        Assert.False(output.IsVisible);
        var right = audio.TranslatePoint(new Point(audio.Bounds.Width, 0), bar)?.X ?? -1;
        Assert.True(right <= bar.Bounds.Width && right >= bar.Bounds.Width - 12, $"The button ends at {right} in a bar {bar.Bounds.Width} wide.");
        main.Close();
    }

    [AvaloniaFact]
    public void HoverLightsTheButtonAndRestIsPlain()
    {
        var main = new MainWindow(first: new SongDocument(() => null) { Project = DemoSongFactory.Create() });
        main.Show();
        Dispatcher.UIThread.RunJobs();
        var audio = main.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "AudioDevice");
        Assert.True(IsTransparent(audio.BorderBrush) && IsTransparent(audio.Background));

        var centre = audio.TranslatePoint(new Point(audio.Bounds.Width / 2, audio.Bounds.Height / 2), main) ?? default;
        main.MouseMove(centre);
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(audio.BorderBrush);
        Assert.False(IsTransparent(audio.BorderBrush));
        Assert.False(IsTransparent(audio.Background));

        main.MouseMove(new Point(2, 2));
        Dispatcher.UIThread.RunJobs();
        Assert.True(IsTransparent(audio.BorderBrush) && IsTransparent(audio.Background));
        main.Close();
    }

    [Fact]
    public void IdleTextNamesTheConfiguredDeviceAndKeepsTheLastReportedLatency()
    {
        var settings = new PluginSettings { Driver = AudioDriverNames.WasapiShared, Device = "", SampleRate = 48000 };
        var status = new StatusAudioInterface();
        Assert.Equal(ConfiguredIdle, status.Status(settings, null).Text);

        var running = status.Status(settings, (AudioDriverNames.WasapiShared, "Analogue 1/2 (Audient iD4)", 48000, 256, 12));
        Assert.Equal("WASAPI (shared): Analogue 1/2 (Audient iD4) (12 ms · 48 kHz · 256 smp)", running.Text);
        // Idle again: the configured interface, with the latency and buffer of the report the engine made in this session.
        Assert.Equal("WASAPI (shared): Windows default device (12 ms · 48 kHz · 256 smp)", status.Status(settings, null).Text);
    }

    [Fact]
    public void IdleTextNamesTheAsioDeviceWithItsRate()
    {
        var settings = new PluginSettings { Driver = AudioDriverNames.Asio, Device = "Audient USB", SampleRate = 44100 };
        Assert.Equal("ASIO: Audient USB (44.1 kHz)", StatusAudioInterface.ConfiguredText(settings, null));
    }

    [Fact]
    public void AnAsioRowWithNoDeviceNamesTheFirstInstalledDriverNotTheWindowsDefault()
    {
        var settings = new PluginSettings { Driver = AudioDriverNames.Asio, Device = "", SampleRate = 48000 };
        var first = SettingsCatalog.ConfiguredOutputDevice(settings);
        Assert.Equal(first.Length > 0 ? $"ASIO: {first} (48 kHz)" : $"ASIO: {StatusAudioInterface.DefaultDeviceName} (48 kHz)", StatusAudioInterface.ConfiguredText(settings, null));
        Assert.Equal(first.Length > 0 ? first : StatusAudioInterface.DefaultDeviceName, new StatusAudioInterface().Tip(settings, null, null).Device);
    }

    [Fact]
    public void TheTooltipChannelsLineNamesTheChosenAsioChannelsOnlyWhenTheDriverNamesAreKnown()
    {
        var names = (Inputs: (IReadOnlyList<string>)new[] { "Mic 1", "Mic 2", "Line 3" }, Outputs: (IReadOnlyList<string>)new[] { "Main L", "Main R" });
        var settings = new PluginSettings { Driver = AudioDriverNames.Asio, Device = "Audient USB", AsioInputChannel = 2, AsioOutputChannel = 1 };
        Assert.Equal("In: Line 3 · Out: Main R", new StatusAudioInterface().Tip(settings, null, names).Channels);
        Assert.Equal("In: Line 3 · Out: Main R", new StatusAudioInterface().Tip(settings, (AudioDriverNames.Asio, "Audient USB", 48000, 64, 4), names).Channels);
        Assert.Null(new StatusAudioInterface().Tip(settings, null, null).Channels);
        Assert.Null(new StatusAudioInterface().Tip(new PluginSettings { Driver = AudioDriverNames.WasapiShared }, null, names).Channels);
    }

    // A brush that is missing or fully transparent reads as no fill or border.
    private static bool IsTransparent(IBrush? brush) => brush is null || (brush as ISolidColorBrush)?.Color.A == 0;
}
