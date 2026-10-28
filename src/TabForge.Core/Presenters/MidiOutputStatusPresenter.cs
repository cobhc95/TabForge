// Owns: the status bar's MIDI output text: the name of the device the selected track sends to, or the fallback when the list does not hold it.
// Does not own: the device list (WinMmMidiOutput.Devices, from the platform adapter), the selected track (the caller passes its device id),
// or the status bar controls (the Avalonia StatusBarView).
// Tests: TabForge.Avalonia.Tests.MidiOutputStatusPresenterTests.
using TabForge.Playback;

namespace TabForge.Presenters;

/// <summary>The status bar's MIDI output text. Thread: UI thread only (the caller passes the list it holds there).</summary>
public static class MidiOutputStatusPresenter
{
    /// <summary>The text shown when the track's device is not in the list.</summary>
    public const string UnknownOutputText = "MIDI output";

    /// <summary>The name of the output device with the given id, or <see cref="UnknownOutputText"/> when the list does not hold it.</summary>
    public static string OutputText(IReadOnlyList<MidiOutputDeviceInfo> devices, int deviceId)
        => devices.FirstOrDefault(device => device.DeviceId == deviceId)?.Name ?? UnknownOutputText;

    /// <summary>
    /// The name of the track's output device: the saved name resolved first (index fallback), the saved name with a
    /// missing mark when it is not connected, or <see cref="UnknownOutputText"/> when neither resolves.
    /// </summary>
    public static string OutputText(IReadOnlyList<MidiOutputDeviceInfo> devices, int deviceId, string? deviceName)
    {
        if (MidiOutputRouting.IsMissing(devices, deviceName)) return TrackOutputPresenter.MissingDeviceText(deviceName!);
        return devices.FirstOrDefault(device => device.DeviceId == MidiOutputRouting.Resolve(devices, deviceId, deviceName))?.Name ?? UnknownOutputText;
    }
}
