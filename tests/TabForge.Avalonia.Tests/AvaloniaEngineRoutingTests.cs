using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using TabForge.Audio;
using TabForge.Avalonia.Transport;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the engine routing checks: the master percent is read from and saved to the attached audio settings (clamped to 0-100), the engine
/// mixer options take the settings' auto GM sound and play-all-through-engine after a sync, and the demo song from bar 85 sends the engine stream.
/// Does not own: the engine sync itself (needs a running engine) or the knob and mixer wiring (the window tests).
/// Tests: this class.
/// </summary>
public class AvaloniaEngineRoutingTests
{
    [Fact]
    public void MasterPercentReadsTheAttachedAudioSettings()
    {
        var routing = new AvaloniaEngineRouting();
        routing.Attach(new AudioSettings { MasterVolume = 64 }, new PluginSettings());

        Assert.Equal(64, routing.MasterPercent);
    }

    [Fact]
    public void SetMasterPercentClampsAndSavesIntoTheAudioSettings()
    {
        var routing = new AvaloniaEngineRouting();
        var audio = new AudioSettings();
        routing.Attach(audio, new PluginSettings());

        routing.SetMasterPercent(150);

        Assert.Equal(100, audio.MasterVolume);
        Assert.Equal(100, routing.MasterPercent);
    }

    [Fact]
    public void EngineMixerOptionsEqualTheSettingsAfterApply()
    {
        var settings = new PluginSettings { PlayAllThroughEngine = true, AutoGmSound = false };
        var mixer = new MixerOptions();

        AvaloniaEngineRouting.ApplyMixerOptions(settings, mixer);

        Assert.Equal(settings.AutoGmSound, mixer.AutoGmSound);
        Assert.Equal(settings.PlayAllThroughEngine, mixer.PlayAllThroughEngine);
    }

    /// <summary>
    /// The demo song's compiled events from the first performed bar 85 to the end (the tempo changes, the D.S. al Coda), sent through this
    /// routing on a fake running engine, equal the stream the WPF AudioRouting.Apply sends with the same default settings: every message to
    /// the same engine slot, none to Windows MIDI. The golden is the stream's line count and SHA-256 ("ms Eslot status d1 d2" per line);
    /// the WPF AudioRouting no longer exists, so when the demo song or its compile changes on purpose, diff the new stream against one built from the previous source (write the text built below to a file), explain every difference, then paste the new line count and SHA-256.
    /// </summary>
    [Fact]
    public void DemoSongFromBar85SendsTheWpfEngineStream()
    {
        const int FromBar = 84;   // 0-based: bar 85
        const int GoldenLines = 11909;
        const string GoldenSha256 = "29836B03ACC72A8CF697749B5FFC452507506E5ACF08239F57242D9D9891A571";
        var project = FullDemoSongFactory.Create();
        using var client = new AudioEngineClient(new TabForge.Plugins.PluginTrust());
        client.AttachFakeForTest();
        var stream = new StringBuilder();
        var lines = 0;
        var at = 0.0;
        void Line(string message) { stream.Append(string.Create(CultureInfo.InvariantCulture, $"{at:0.0} {message}\n")); lines++; }
        client.WrittenForTest = m => Line(string.Create(CultureInfo.InvariantCulture, $"E{m.Slot} {m.Status:X2} {m.Data1} {m.Data2}"));
        var routing = new RoutedMidiOutput(new WindowsMidiCapture(Line), client);
        AvaloniaEngineRouting.Apply(project, routing, client, new PluginSettings(), AvaloniaEngineRouting.FullMasterPercent, new object());

        var timeline = MidiTimelineBuilder.Build(project, new PlaybackOptions());
        var fromMs = timeline.Bars.First(b => b.Bar == FromBar).StartMs;
        foreach (var e in timeline.Events.Where(e => !e.IsSetup && e.TimeMs >= fromMs))
        {
            at = e.TimeMs;
            routing.Send(e.DeviceId, e.Status, e.Data1, e.Data2);
        }

        var text = stream.ToString();
        var sha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        Assert.True(lines == GoldenLines && sha == GoldenSha256, $"{lines} lines, sha256 {sha}; first lines:\n{string.Join('\n', text.Split('\n').Take(5))}");
    }

    /// <summary>The Windows MIDI side of the routing: every message that would go to the Windows synth instead of the engine.</summary>
    private sealed class WindowsMidiCapture(Action<string> line) : IMidiOutput
    {
        public IReadOnlyList<MidiOutputDeviceInfo> Devices { get; } = Array.Empty<MidiOutputDeviceInfo>();
        public void Send(int deviceId, int status, int data1, int data2) => line(string.Create(CultureInfo.InvariantCulture, $"W {status:X2} {data1} {data2}"));
        public void ResetAll() { }
        public void Close() { }
        public void Dispose() { }
    }
}
