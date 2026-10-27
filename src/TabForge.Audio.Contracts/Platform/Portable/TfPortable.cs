using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace TabForge.Audio.Contracts;

// Owns: loading the native tfportable library (miniaudio audio + RtMidi MIDI behind a small C ABI, native/tfportable) and calling it through function
//   pointers, so a missing library is a normal "not available" answer and no P/Invoke is declared outside the platform folders.
// Does not own: what the audio and MIDI backends do with it (AudioEngine/Output/MiniaudioBackend.cs, Core/Platform/Portable/RtMidiBackend.cs).
// Tests: PortableBackendTests (against the library when present, the unavailable answers when not).
/// <summary>
/// The tfportable native library. <see cref="Current"/> is null when the library is not next to the program (the Windows build never ships it;
/// the Linux and macOS builds do). Every method is a thin call; strings are UTF-8.
/// </summary>
public sealed unsafe class TfPortable
{
    /// <summary>The ABI version this code was written for (tfportable.h TF_ABI_VERSION).</summary>
    public const int AbiVersion = 1;

    /// <summary>Environment variable naming the folder (or file) to load the library from before the program folder (dev and tests).</summary>
    public const string PathVariable = "TABFORGE_TFPORTABLE";

    private static readonly Lazy<TfPortable?> Loaded = new(Load);

    /// <summary>The loaded library, or null when it is absent or of another ABI version.</summary>
    public static TfPortable? Current => Loaded.Value;

    private readonly nint _library;

    private TfPortable(nint library) => _library = library;

    private static TfPortable? Load()
    {
        var names = new List<string>();
        var hint = Environment.GetEnvironmentVariable(PathVariable);
        var fileName = OperatingSystem.IsWindows() ? "tfportable.dll" : OperatingSystem.IsMacOS() ? "libtfportable.dylib" : "libtfportable.so";
        if (!string.IsNullOrWhiteSpace(hint)) names.Add(Directory.Exists(hint) ? Path.Combine(hint, fileName) : hint);
        names.Add(Path.Combine(AppContext.BaseDirectory, fileName));
        var assemblyDir = Path.GetDirectoryName(typeof(TfPortable).Assembly.Location);
        if (!string.IsNullOrEmpty(assemblyDir)) names.Add(Path.Combine(assemblyDir, fileName));
        foreach (var name in names)
        {
            if (!File.Exists(name) || !NativeLibrary.TryLoad(name, out var handle)) continue;
            var library = new TfPortable(handle);
            if (library.Has("tf_abi_version") && ((delegate* unmanaged[Cdecl]<int>)NativeLibrary.GetExport(handle, "tf_abi_version"))() == AbiVersion) return library;
            NativeLibrary.Free(handle);
        }
        return null;
    }

    private bool Has(string name) => NativeLibrary.TryGetExport(_library, name, out _);
    private nint Fn(string name) => NativeLibrary.GetExport(_library, name);

    private static string Utf8(byte* text) => Marshal.PtrToStringUTF8((nint)text) ?? "";

    // ---- Audio ----

    /// <summary>A context (the backends miniaudio can use). <paramref name="nullBackend"/>: miniaudio's null backend only (no hardware). Zero on failure.</summary>
    public nint AudioContextCreate(bool nullBackend) => ((delegate* unmanaged[Cdecl]<int, nint>)Fn("tf_audio_context_create"))(nullBackend ? 1 : 0);
    public void AudioContextDestroy(nint context) => ((delegate* unmanaged[Cdecl]<nint, void>)Fn("tf_audio_context_destroy"))(context);

    public string AudioBackendName(nint context)
    {
        var buffer = stackalloc byte[64];
        return ((delegate* unmanaged[Cdecl]<nint, byte*, int, int>)Fn("tf_audio_backend_name"))(context, buffer, 64) == 0 ? Utf8(buffer) : "";
    }

    public int AudioDeviceCount(nint context, bool capture) => ((delegate* unmanaged[Cdecl]<nint, int, int>)Fn("tf_audio_device_count"))(context, capture ? 1 : 0);

    /// <summary>One device of the list (0-based); false past the end.</summary>
    public bool AudioDeviceInfo(nint context, bool capture, int index, out string name, out bool isDefault, out int maxChannels, out int nativeRate)
    {
        var buffer = stackalloc byte[256];
        int isDef = 0, channels = 0, rate = 0;
        var ok = ((delegate* unmanaged[Cdecl]<nint, int, int, byte*, int, int*, int*, int*, int>)Fn("tf_audio_device_info"))(context, capture ? 1 : 0, index, buffer, 256, &isDef, &channels, &rate) == 0;
        name = ok ? Utf8(buffer) : "";
        isDefault = isDef != 0; maxChannels = channels; nativeRate = rate;
        return ok;
    }

    /// <summary>Opens a playback or capture stream (not started). The callbacks are static methods marked UnmanagedCallersOnly(Cdecl): (user, output, input, frames) on the real-time thread and (user, reason) for device events. Zero on failure (error code in <paramref name="error"/>).</summary>
    public nint AudioStreamOpen(nint context, bool capture, string? device, int channels, int sampleRate, int periodFrames,
        delegate* unmanaged[Cdecl]<nint, float*, float*, uint, void> data, delegate* unmanaged[Cdecl]<nint, int, void> onEvent, nint user,
        out int actualChannels, out int actualRate, out int latencyFrames, out int error)
    {
        var name = string.IsNullOrEmpty(device) ? null : Encoding.UTF8.GetBytes(device + "\0");
        int ch = 0, rate = 0, latency = 0, err = 0;
        nint stream;
        fixed (byte* namePtr = name)
            stream = ((delegate* unmanaged[Cdecl]<nint, int, byte*, int, int, int, delegate* unmanaged[Cdecl]<nint, float*, float*, uint, void>, delegate* unmanaged[Cdecl]<nint, int, void>, nint, int*, int*, int*, int*, nint>)Fn("tf_audio_stream_open"))(
                context, capture ? 1 : 0, namePtr, channels, sampleRate, periodFrames, data, onEvent, user, &ch, &rate, &latency, &err);
        actualChannels = ch; actualRate = rate; latencyFrames = latency; error = err;
        return stream;
    }

    public int AudioStreamStart(nint stream) => ((delegate* unmanaged[Cdecl]<nint, int>)Fn("tf_audio_stream_start"))(stream);
    public int AudioStreamStop(nint stream) => ((delegate* unmanaged[Cdecl]<nint, int>)Fn("tf_audio_stream_stop"))(stream);
    public void AudioStreamClose(nint stream) => ((delegate* unmanaged[Cdecl]<nint, void>)Fn("tf_audio_stream_close"))(stream);

    // ---- MIDI ----

    public int MidiCount(bool input) => input ? ((delegate* unmanaged[Cdecl]<int>)Fn("tf_midi_in_count"))() : ((delegate* unmanaged[Cdecl]<int>)Fn("tf_midi_out_count"))();

    public string? MidiName(bool input, int index)
    {
        var buffer = stackalloc byte[256];
        var result = input ? ((delegate* unmanaged[Cdecl]<int, byte*, int, int>)Fn("tf_midi_in_name"))(index, buffer, 256) : ((delegate* unmanaged[Cdecl]<int, byte*, int, int>)Fn("tf_midi_out_name"))(index, buffer, 256);
        return result == 0 ? Utf8(buffer) : null;
    }

    /// <summary>Opens MIDI input <paramref name="index"/>; zero when it cannot be opened. <paramref name="callback"/> runs on the driver thread: (user, message, length, delta seconds).</summary>
    public nint MidiInOpen(int index, delegate* unmanaged[Cdecl]<nint, byte*, int, double, void> callback, nint user) =>
        ((delegate* unmanaged[Cdecl]<int, delegate* unmanaged[Cdecl]<nint, byte*, int, double, void>, nint, nint>)Fn("tf_midi_in_open"))(index, callback, user);
    public void MidiInClose(nint port) => ((delegate* unmanaged[Cdecl]<nint, void>)Fn("tf_midi_in_close"))(port);

    public nint MidiOutOpen(int index) => ((delegate* unmanaged[Cdecl]<int, nint>)Fn("tf_midi_out_open"))(index);

    /// <summary>Sends one short message (<paramref name="length"/> 2 or 3 bytes).</summary>
    public void MidiOutSend(nint port, byte status, byte data1, byte data2, int length)
    {
        var message = stackalloc byte[3];
        message[0] = status; message[1] = data1; message[2] = data2;
        _ = ((delegate* unmanaged[Cdecl]<nint, byte*, int, int>)Fn("tf_midi_out_send"))(port, message, length);
    }

    public void MidiOutClose(nint port) => ((delegate* unmanaged[Cdecl]<nint, void>)Fn("tf_midi_out_close"))(port);
}
