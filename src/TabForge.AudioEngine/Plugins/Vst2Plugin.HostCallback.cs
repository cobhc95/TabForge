using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace TabForge.AudioEngine.Plugins;

// Owns: the host callback a VST2 plug-in calls back into (audioMaster opcodes) and its string writer, split from Vst2Plugin.cs.
// Does not own: the plug-in hosting itself (Vst2Plugin).
public sealed unsafe partial class Vst2Plugin
{
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static IntPtr HostCallback(AEffect* effect, int opcode, int index, IntPtr value, IntPtr ptr, float opt)
    {
        try
        {
            var self = effect is not null && Instances.TryGetValue((IntPtr)effect, out var found) ? found : _loading;
            switch (opcode)
            {
                case 1: return 2400;                                   // audioMasterVersion
                case 0: case 43: case 44:                              // automate / beginEdit / endEdit: the user changed something
                    self?.FlagEdited();                                // A flag, raised by the main loop (no allocation here)
                    return 0;
                case 6: return 1;                                      // wantMidi
                case 8: self?.CollectOut(ptr); return 1;               // processEvents: MIDI out of the plug-in
                case 7: return self is null ? 0 : (IntPtr)self._timeInfo; // getTime
                case 13: return 1;                                     // ioChanged
                case 15:                                               // sizeWindow
                    if (self is not null)
                    {
                        Interlocked.Exchange(ref self._pendingSize, ((long)index << 32) | (uint)(int)value);
                        Volatile.Write(ref self._resizeFlag, 1);
                        Interlocked.Increment(ref _hostSignal);
                    }
                    return 1;
                case 16: return self is null ? 0 : (IntPtr)(long)self._sampleRate; // getSampleRate
                case 17: return self is null ? 0 : self._maxBlock;     // getBlockSize
                case 23: return self is null ? 1 : self._threads.IsOfflineWorker ? 4 : self._threads.IsAudioThread ? 2 : 1;   // currentProcessLevel: offline / realtime / user
                case 32: return WriteString(ptr, "TabForge", 64);       // vendor
                case 33: return WriteString(ptr, "TabForge Audio Engine", 64);
                case 34: return 1;
                case 37: return CanDo(ptr) ? 1 : 0;                    // canDo: byte compare, no string allocation on the audio thread
                case 42: return 1;                                     // updateDisplay
                case Vst2ReaperHostApi.Opcode:                         // Reaper-only function lookup (the ReaPlugs): answer by name, 0 = unknown
                    return index == Vst2ReaperHostApi.Index ? Vst2ReaperHostApi.Resolve(ptr) : 0;
                default: return 0;
            }
        }
        catch { return 0; } // never let an exception cross into native code
    }

    // NUL-separated ASCII names in static read-only UTF-8 data: no managed collection and no per-callback allocation.
    private static ReadOnlySpan<byte> HostCanDo =>
        "sendVstEvents\0sendVstMidiEvent\0sizeWindow\0sendVstTimeInfo\0receiveVstEvents\0receiveVstMidiEvent\0"u8;

    /// <summary>audioMasterCanDo: compare the native ANSI string byte for byte (no per-callback allocation).</summary>
    internal static bool CanDo(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero) return false;
        var text = (byte*)ptr;
        var names = HostCanDo;
        for (var start = 0; start < names.Length;)
        {
            var end = start;
            while (names[end] != 0) end++;
            var length = end - start;
            var i = 0;
            for (; i < length; i++)
                if (text[i] != names[start + i]) break;
            if (i == length && text[i] == 0) return true;
            start = end + 1;
        }
        return false;
    }

    /// <summary>Writes an ASCII host string into the plug-in's buffer; no managed array per call (audio-thread safe).</summary>
    internal static IntPtr WriteString(IntPtr ptr, string text, int max)
    {
        if (ptr == IntPtr.Zero || max <= 0) return 0;
        var n = Math.Min(text.Length, max - 1);
        var bytes = (byte*)ptr;
        for (var i = 0; i < n; i++) bytes[i] = (byte)text[i];
        bytes[n] = 0;
        return 1;
    }
}
