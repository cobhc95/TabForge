using TabForge.Models;

namespace TabForge.Playback;

// Owns: the rule that binds a track to its MIDI output device: the saved device name wins (a replugged
//   interface keeps its track whatever index it lands on), the saved index is the fallback (older songs,
//   which saved no name, keep working exactly as before).
// Does not own: the device list (IMidiOutput.Devices), the track model or any UI text.
// Tests: TabForge.Avalonia.Tests.MidiOutputRoutingTests.

/// <summary>
/// Binds a track to its MIDI output device. The saved device name is resolved first; the saved index
/// is the fallback. Thread: any (pure, no state).
/// </summary>
public static class MidiOutputRouting
{
    /// <summary>The device id to use: the listed device with <paramref name="deviceName"/>, else <paramref name="deviceId"/>.</summary>
    public static int Resolve(IReadOnlyList<MidiOutputDeviceInfo> devices, int deviceId, string? deviceName)
    {
        if (!string.IsNullOrWhiteSpace(deviceName))
        {
            var byName = devices.FirstOrDefault(d => string.Equals(d.Name, deviceName, StringComparison.Ordinal));
            if (byName is not null) return byName.DeviceId;
        }
        return deviceId;
    }

    /// <summary>The device id to use for <paramref name="track"/>.</summary>
    public static int Resolve(IReadOnlyList<MidiOutputDeviceInfo> devices, TrackModel track) =>
        Resolve(devices, track.MidiOutputDeviceId, track.MidiOutputDeviceName);

    /// <summary>True when the track saved a device name and no listed device carries it (the track output menu marks it).</summary>
    public static bool IsMissing(IReadOnlyList<MidiOutputDeviceInfo> devices, string? deviceName) =>
        !string.IsNullOrWhiteSpace(deviceName) && !devices.Any(d => string.Equals(d.Name, deviceName, StringComparison.Ordinal));

    /// <summary>True when the track's saved device is not connected.</summary>
    public static bool IsMissing(IReadOnlyList<MidiOutputDeviceInfo> devices, TrackModel track) =>
        IsMissing(devices, track.MidiOutputDeviceName);
}
