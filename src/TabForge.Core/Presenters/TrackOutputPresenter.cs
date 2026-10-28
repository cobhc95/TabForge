using TabForge.Playback;

namespace TabForge.Presenters;

/// <summary>
/// The MIDI output window's texts and the rule for the device it reports.
/// Owns: the window title, the button labels, the heading that names the track, the hint, and the rule that only a selected device id is a choice.
/// Does not own: the device list (the window's ComboBox over IMidiOutput devices), the track edit (the window's chose callback) or the test note.
/// Tests: TrackOutputWindowTests.
/// </summary>
public static class TrackOutputPresenter
{
    public const string Title = "MIDI output";
    public const string TestButton = "Test sound";
    public const string CloseButton = "Close";
    public const string Hint = "The track's notes are sent to this device. Test sound plays a note on it.";

    public static string Heading(string trackName) => $"MIDI output device for {trackName}";

    /// <summary>The device id a selection reports; clearing the selection (no value) reports nothing.</summary>
    public static int? ChosenDeviceId(object? selectedValue) => selectedValue is int id ? id : null;

    /// <summary>The mark shown for the track's saved device when it is not connected.</summary>
    public static string MissingDeviceText(string savedName) => $"{savedName} (missing)";

    /// <summary>The (id, name) a device-list selection reports; clearing the selection reports nothing.</summary>
    public static (int Id, string? Name)? ChosenDevice(object? selectedItem) =>
        selectedItem is MidiOutputDeviceInfo device ? (device.DeviceId, device.Name) : null;
}
