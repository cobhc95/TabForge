using System;
using System.Collections.Generic;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

// Owns: the state of the status bar's audio interface button in the Avalonia app, which is always shown: the running output's text (from
//   AudioDeviceStatusPresenter) while the engine reports one, else the configured interface (driver, device and sample rate from the settings),
//   with the latency and buffer of the engine's last report in this session. The idle text uses the same "Driver: Device (ms · kHz · smp)" form.
// Does not own: the button and its look (StatusBarView), the running text's rules (AudioDeviceStatusPresenter, shared and not changed
//   here), the engine (AudioEngineClient), the settings (PluginSettings).
// Tests: TabForge.Avalonia.Tests.StatusBarAudioInterfaceTests.
// Thread: UI thread only. Cache: the last report's latency and buffer, one value, replaced at each running report and kept after the engine stops.
/// <summary>The audio button's tooltip lines: driver and full device name, rate · buffer · latency, and the channels line (null when the driver's channel names are not known).</summary>
public sealed record AudioDeviceTip(string Driver, string Device, string Format, string? Channels);

public sealed class StatusAudioInterface
{
    /// <summary>The device name shown when the settings name none: the chosen driver's default device.</summary>
    public const string DefaultDeviceName = "Windows default device";

    private (int LatencyMs, int Buffer)? _lastReport;

    /// <summary>The button's state: the running output's text while the engine reports one, else the configured interface's idle text.</summary>
    public AudioDeviceStatus Status(PluginSettings settings, (string Driver, string Device, int Rate, int Buffer, int LatencyMs)? running)
    {
        if (running is { } output)
        {
            _lastReport = (output.LatencyMs, output.Buffer);
            var live = AudioDeviceStatusPresenter.Decide(new AudioDeviceStatusInput(
                true, output, settings.Driver, settings.Device, settings.SampleRate, FirstDeviceOfDriver: _ => null));
            return new AudioDeviceStatus(true, live.Text, live.Tooltip);
        }
        return new AudioDeviceStatus(true, ConfiguredText(settings, _lastReport), null);
    }

    /// <summary>The tooltip facts for the state <see cref="Status"/> just decided: the running output's, else the configured interface's with the last report. The channels line names the chosen ASIO input and output channels from <paramref name="asioChannels"/> (the driver's names; null: not known, so no line).</summary>
    public AudioDeviceTip Tip(PluginSettings settings, (string Driver, string Device, int Rate, int Buffer, int LatencyMs)? running, (IReadOnlyList<string> Inputs, IReadOnlyList<string> Outputs)? asioChannels)
    {
        var channels = settings.Driver == AudioDrivers.Asio && asioChannels is { } names ? ChannelsLine(settings, names.Inputs, names.Outputs) : null;
        if (running is { } output) return new AudioDeviceTip(output.Driver, output.Device, FormatLine(output.Rate, output.Buffer, output.LatencyMs), channels);
        var device = SettingsCatalog.ConfiguredOutputDevice(settings) is { Length: > 0 } named ? named : DefaultDeviceName;
        return new AudioDeviceTip(settings.Driver, device, _lastReport is { } report ? FormatLine(settings.SampleRate, report.Buffer, report.LatencyMs) : $"{settings.SampleRate / 1000.0:0.#} kHz", channels);
    }

    /// <summary>"In: Analogue 1 · Out: Main L": the first chosen input and output channel by the driver's names; null when it named neither.</summary>
    private static string? ChannelsLine(PluginSettings settings, IReadOnlyList<string> inputs, IReadOnlyList<string> outputs)
    {
        var parts = new List<string>();
        if (inputs.Count > 0 && settings.AsioInputsEnabled) parts.Add($"In: {inputs[Math.Clamp(settings.AsioInputChannel, 0, inputs.Count - 1)]}");
        if (outputs.Count > 0) parts.Add($"Out: {outputs[Math.Clamp(settings.AsioOutputChannel, 0, outputs.Count - 1)]}");
        return parts.Count > 0 ? string.Join(" · ", parts) : null;
    }

    private static string FormatLine(int rate, int buffer, int latencyMs) => $"{rate / 1000.0:0.#} kHz · {buffer} smp · {latencyMs} ms";

    /// <summary>The idle text, e.g. "WASAPI (shared): Windows default device (12 ms · 48 kHz · 256 smp)"; the latency and buffer wait for the first report.</summary>
    public static string ConfiguredText(PluginSettings settings, (int LatencyMs, int Buffer)? lastReport)
    {
        var device = SettingsCatalog.ConfiguredOutputDevice(settings) is { Length: > 0 } named ? named : DefaultDeviceName;
        var label = device.StartsWith(settings.Driver, StringComparison.OrdinalIgnoreCase) ? device : $"{settings.Driver}: {device}";
        var rate = $"{settings.SampleRate / 1000.0:0.#} kHz";
        return lastReport is { } report ? $"{label} ({report.LatencyMs} ms · {rate} · {report.Buffer} smp)" : $"{label} ({rate})";
    }
}
