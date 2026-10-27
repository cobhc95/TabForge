namespace TabForge.Platform.Contracts;

/// <summary>
/// The MIDI output device calls the playback layer needs: the device list, open, short message, reset and close.
/// Owns: the set of device calls and their result shapes. Does not own: which devices are listed or the handle bookkeeping
/// (WinMmMidiOutput), or the Windows call (the adapter is Core/Platform/Windows/WindowsMidiDevices.cs).
/// Tests: none dedicated(the adapter's answers equal the direct winmm calls).
/// </summary>
public interface IMidiDevices
{
    /// <summary>The name of the Microsoft MIDI Mapper entry (device id -1), the first entry of every device list.</summary>
    const string MapperName = "Microsoft MIDI Mapper (default)";

    /// <summary>Whether this adapter can list and open devices on this system (winmm on Windows; the portable adapter when its native library is present).</summary>
    bool IsSupported => OperatingSystem.IsWindows();

    /// <summary>The name of the first list entry (device id -1): the system's default output. The Windows adapter keeps the Microsoft MIDI Mapper name.</summary>
    string DefaultDeviceName => MapperName;

    /// <summary>How many output devices the system reports; the device ids are 0 to count minus one.</summary>
    uint OutputDeviceCount();

    /// <summary>The product name of output device <paramref name="deviceId"/>. False when the device cannot be read (it is left out of the list).</summary>
    bool TryGetOutputProductName(uint deviceId, out string productName);

    /// <summary>Opens output device <paramref name="deviceId"/> (the Microsoft MIDI Mapper is the id 0xFFFFFFFF). Returns <see cref="IntPtr.Zero"/> when it cannot be opened.</summary>
    IntPtr OpenOutput(uint deviceId);

    /// <summary>Sends one packed short message (status, data1, data2 in the low three bytes) on an open handle.</summary>
    void ShortMessage(IntPtr handle, uint message);

    /// <summary>Turns off the notes and controllers an open handle is holding.</summary>
    void ResetOutput(IntPtr handle);

    /// <summary>Closes an open handle.</summary>
    void CloseOutput(IntPtr handle);
}
