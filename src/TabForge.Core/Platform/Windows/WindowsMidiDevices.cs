using System.Runtime.InteropServices;
using TabForge.Playback;

namespace TabForge.Platform.Windows;

// Owns: the winmm MIDI output calls of the app (midiOutGetNumDevs, midiOutGetDevCaps, midiOutOpen, midiOutShortMsg, midiOutReset, midiOutClose).
// Does not own: which devices are listed, the handle bookkeeping or the playback scheduling (Core/Playback/MidiOutputPort.cs).
// Tests: none dedicated(compares every answer with the direct winmm call).
/// <summary><see cref="IMidiDevices"/> over winmm, one native call per member and no caching. Calls are made on the calling thread.</summary>
public sealed class WindowsMidiDevices : IMidiDevices
{
    public uint OutputDeviceCount() => midiOutGetNumDevs();

    public bool TryGetOutputProductName(uint deviceId, out string productName)
    {
        if (midiOutGetDevCaps(deviceId, out var caps, (uint)Marshal.SizeOf<MIDIOUTCAPS>()) != 0)
        {
            productName = "";
            return false;
        }
        productName = caps.szPname;
        return true;
    }

    public IntPtr OpenOutput(uint deviceId) => midiOutOpen(out var handle, deviceId, IntPtr.Zero, IntPtr.Zero, 0) == 0 ? handle : IntPtr.Zero;

    // winmm reports failures as MMRESULT codes (a lost device just drops the message); it does not throw.
    public void ShortMessage(IntPtr handle, uint message) => midiOutShortMsg(handle, message);

    public void ResetOutput(IntPtr handle) => midiOutReset(handle);

    public void CloseOutput(IntPtr handle) => midiOutClose(handle);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MIDIOUTCAPS
    {
        public ushort wMid, wPid;
        public uint vDriverVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
        public ushort wTechnology, wVoices, wNotes, wChannelMask;
        public uint dwSupport;
    }

    [DllImport("winmm.dll")] private static extern uint midiOutGetNumDevs();
    [DllImport("winmm.dll", CharSet = CharSet.Auto)] private static extern int midiOutGetDevCaps(uint uDeviceID, out MIDIOUTCAPS caps, uint cbmoc);
    [DllImport("winmm.dll")] private static extern int midiOutOpen(out IntPtr handle, uint deviceId, IntPtr callback, IntPtr instance, uint flags);
    [DllImport("winmm.dll")] private static extern int midiOutShortMsg(IntPtr handle, uint message);
    [DllImport("winmm.dll")] private static extern int midiOutReset(IntPtr handle);
    [DllImport("winmm.dll")] private static extern int midiOutClose(IntPtr handle);
}
