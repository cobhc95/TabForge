using System.Runtime.InteropServices;
using System.Text;

namespace TabForge.AudioEngine.Plugins;

/// <summary>
/// Hosts a VST3 plug-in through tfvst3.dll (native\tfvst3, a small C interface over the Steinberg VST3 SDK).
/// Buffers and the event array are allocated once; <see cref="Process"/> never allocates.
/// </summary>
public sealed unsafe class Vst3Plugin : IPluginInstance
{
    private const string Lib = "tfvst3.dll";
    private const int MaxEvents = 512;

    [StructLayout(LayoutKind.Sequential)]
    private struct Midi { public int SampleOffset; public byte Status, Data1, Data2, Pad; }

    [DllImport(Lib, CharSet = CharSet.Unicode)] private static extern int tfv3_list_classes(string path, byte* names, int* isInstrument, int max);
    [DllImport(Lib, CharSet = CharSet.Unicode)] private static extern IntPtr tfv3_create(string path, int classIndex, double sampleRate, int maxBlock, byte* error);
    [DllImport(Lib)] private static extern int tfv3_process(IntPtr p, float** input, int inChannels, float** output, int outChannels, int frames, Midi* events, int count, double tempo, double ppq, int playing);
    [DllImport(Lib)] private static extern int tfv3_get_state(IntPtr p, byte* buffer, int capacity);
    [DllImport(Lib)] private static extern int tfv3_set_state(IntPtr p, byte* data, int size);
    [DllImport(Lib)] private static extern int tfv3_has_editor(IntPtr p);
    [DllImport(Lib)] private static extern int tfv3_open_editor(IntPtr p, IntPtr parent, int* width, int* height);
    [DllImport(Lib)] private static extern void tfv3_close_editor(IntPtr p);
    [DllImport(Lib)] private static extern int tfv3_latency(IntPtr p);
    [DllImport(Lib)] private static extern void tfv3_destroy(IntPtr p);
    [DllImport(Lib)] private static extern int tfv3_set_offline(IntPtr p, int offline);
    [DllImport(Lib)] private static extern int tfv3_tail(IntPtr p);
    // Newer exports (single-serialise state, live reconfigure, musical position); an older tfvst3.dll lacks them (see HasNewApi).
    [DllImport(Lib)] private static extern int tfv3_process_ex(IntPtr p, float** input, int inChannels, float** output, int outChannels, int frames, Midi* events, int count, double tempo, double ppq, int playing, int timeSigNum, int timeSigDen, double barPositionPpq);
    [DllImport(Lib)] private static extern int tfv3_state_begin(IntPtr p);
    [DllImport(Lib)] private static extern int tfv3_state_copy(IntPtr p, byte* buffer, int capacity);
    [DllImport(Lib)] private static extern int tfv3_set_processing(IntPtr p, double sampleRate, int maxBlock);

    /// <summary>
    /// True when the loaded tfvst3.dll has tfv3_process_ex, tfv3_state_begin/copy and tfv3_set_processing. Probed once on the
    /// main thread (first construction), so the audio thread never meets an EntryPointNotFoundException.
    /// </summary>
    private static readonly Lazy<bool> HasNewApi = new(() =>
    {
        IntPtr lib;
        if (!NativeLibrary.TryLoad(System.IO.Path.Combine(AppContext.BaseDirectory, Lib), out lib) && !NativeLibrary.TryLoad(Lib, out lib)) return false;
        return NativeLibrary.TryGetExport(lib, "tfv3_process_ex", out _) && NativeLibrary.TryGetExport(lib, "tfv3_state_begin", out _)
            && NativeLibrary.TryGetExport(lib, "tfv3_state_copy", out _) && NativeLibrary.TryGetExport(lib, "tfv3_set_processing", out _);
    });
    private readonly bool _newApi;

    private IntPtr _handle;
    private int _maxBlock;
    private readonly int _classIndex;
    private bool _disposed;
    private readonly float*[] _in = new float*[2];
    private readonly float*[] _out = new float*[2];
    private readonly float** _inPtrs;
    private readonly float** _outPtrs;
    private readonly Midi* _events;
    private bool _editorOpen;

    public string Path { get; }
    public bool IsInstrument { get; }
    public bool HasEditor => _handle != IntPtr.Zero && tfv3_has_editor(_handle) != 0;
    public int LatencySamples => _handle == IntPtr.Zero ? 0 : tfv3_latency(_handle);

    // An older tfvst3.dll has neither export: offline mode and tails are then simply unavailable.
    private static bool _noOfflineApi;

    public int TailSamples
    {
        get
        {
            if (_handle == IntPtr.Zero || _noOfflineApi) return 0;
            try { return Math.Max(0, tfv3_tail(_handle)); }
            catch (EntryPointNotFoundException) { _noOfflineApi = true; return 0; }
        }
    }

    public void SetOfflineMode(bool offline)
    {
        if (_handle == IntPtr.Zero || _noOfflineApi) return;
        try { tfv3_set_offline(_handle, offline ? 1 : 0); }
        catch (EntryPointNotFoundException) { _noOfflineApi = true; }
    }

    /// <summary>True when tfvst3.dll is present (VST3 hosting available).</summary>
    public static bool Available => NativeLibrary.TryLoad(System.IO.Path.Combine(AppContext.BaseDirectory, Lib), out _) || NativeLibrary.TryLoad(Lib, out _);

    public Vst3Plugin(string path, bool wantInstrument, double sampleRate, int maxBlock)
    {
        Path = path;
        _maxBlock = maxBlock;
        _newApi = HasNewApi.Value;
        // Pick the class matching the requested role (the first instrument or first effect in the bundle).
        const int max = 32;
        var names = stackalloc byte[max * 128];
        var isInst = stackalloc int[max];
        var count = tfv3_list_classes(path, names, isInst, max);
        if (count <= 0) throw new InvalidOperationException("No VST3 audio processor found in this plug-in.");
        var index = 0;
        for (var i = 0; i < count; i++) if ((isInst[i] != 0) == wantInstrument) { index = i; break; }
        IsInstrument = isInst[index] != 0;
        _classIndex = index;
        _handle = CreateNative(path, index, sampleRate, maxBlock);
        _inPtrs = (float**)NativeMemory.AllocZeroed((nuint)(2 * sizeof(float*)));
        _outPtrs = (float**)NativeMemory.AllocZeroed((nuint)(2 * sizeof(float*)));
        AllocateBuffers(maxBlock);
        _events = (Midi*)NativeMemory.AllocZeroed((nuint)(MaxEvents * sizeof(Midi)));
    }

    private static IntPtr CreateNative(string path, int classIndex, double sampleRate, int maxBlock)
    {
        var error = stackalloc byte[512];
        var handle = tfv3_create(path, classIndex, sampleRate, maxBlock, error);
        if (handle == IntPtr.Zero)
            throw new InvalidOperationException(Encoding.UTF8.GetString(error, Math.Max(0, new ReadOnlySpan<byte>(error, 512).IndexOf((byte)0))) is { Length: > 0 } text ? text : "The VST3 plug-in could not be started.");
        return handle;
    }

    private void AllocateBuffers(int maxBlock)
    {
        for (var c = 0; c < 2; c++)
        {
            _inPtrs[c] = _in[c] = (float*)NativeMemory.AllocZeroed((nuint)(maxBlock * sizeof(float)));
            _outPtrs[c] = _out[c] = (float*)NativeMemory.AllocZeroed((nuint)(maxBlock * sizeof(float)));
        }
        _maxBlock = maxBlock;
    }

    /// <summary>
    /// Device change (main thread, audio stopped). The live instance is re-set up in place (tfv3_set_processing:
    /// setActive(false) → setupProcessing → setActive(true)), keeping every setting. Only when that is refused (or the DLL
    /// predates it) is the instance recreated at the new rate / block size with its current state carried over. If the new
    /// instance cannot start, this one stays loaded but silent and the failure is thrown.
    /// </summary>
    public void Reconfigure(double sampleRate, int maxBlock)
    {
        if (_disposed) return;
        maxBlock = Math.Clamp(maxBlock, 16, 8192);
        if (_newApi && _handle != IntPtr.Zero)
        {
            // Same instance: the editor may stay open (VST3 allows setActive with an attached view).
            for (var c = 0; c < 2; c++) { NativeMemory.Free(_in[c]); NativeMemory.Free(_out[c]); }
            AllocateBuffers(maxBlock);
            if (tfv3_set_processing(_handle, sampleRate, maxBlock) != 0) return;
            EngineLog.Write($"VST3 reconfigure: live setupProcessing refused, recreating: {Path}");
        }
        RecreateWithState(sampleRate, maxBlock);
    }

    private void RecreateWithState(double sampleRate, int maxBlock)
    {
        byte[]? state = null;
        try { state = GetState(); }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException) { EngineLog.Write($"VST3 reconfigure: state not captured: {Path}: {ex.Message}"); }
        CloseEditor();
        if (_handle != IntPtr.Zero) tfv3_destroy(_handle);
        _handle = IntPtr.Zero;
        for (var c = 0; c < 2; c++) { NativeMemory.Free(_in[c]); NativeMemory.Free(_out[c]); }
        AllocateBuffers(maxBlock);
        _handle = CreateNative(Path, _classIndex, sampleRate, maxBlock);
        if (state is not null) SetState(state);
    }

    public void Process(float[][] input, float[][] output, int frames, ReadOnlySpan<BlockMidi> midi, in TransportInfo transport)
    {
        if (frames <= 0) return;
        if (_handle == IntPtr.Zero) { for (var c = 0; c < output.Length; c++) output[c].AsSpan(0, frames).Clear(); return; }
        frames = Math.Min(frames, _maxBlock);
        var count = Math.Min(midi.Length, MaxEvents);
        for (var i = 0; i < count; i++)
            _events[i] = new Midi { SampleOffset = Math.Clamp(midi[i].Frame, 0, frames - 1), Status = midi[i].Status, Data1 = midi[i].Data1, Data2 = midi[i].Data2 };
        for (var c = 0; c < 2; c++)
        {
            var dst = new Span<float>(_in[c], frames);
            if (c < input.Length) input[c].AsSpan(0, frames).CopyTo(dst); else dst.Clear();
        }
        int ok;
        if (_newApi)
        {
            MusicalPosition(transport, out var num, out var den, out var barPpq);
            ok = tfv3_process_ex(_handle, IsInstrument ? null : _inPtrs, IsInstrument ? 0 : 2, _outPtrs, 2, frames, _events, count,
                transport.Tempo, transport.PpqPosition, transport.Playing ? 1 : 0, num, den, barPpq);
        }
        else
            ok = tfv3_process(_handle, IsInstrument ? null : _inPtrs, IsInstrument ? 0 : 2, _outPtrs, 2, frames, _events, count,
                transport.Tempo, transport.PpqPosition, transport.Playing ? 1 : 0);
        if (ok == 0)
        {
            for (var c = 0; c < output.Length; c++) output[c].AsSpan(0, frames).Clear();
            return;
        }
        for (var c = 0; c < output.Length; c++) new ReadOnlySpan<float>(_out[Math.Min(c, 1)], frames).CopyTo(output[c]);
    }

    /// <summary>
    /// Time signature and the ppq of the current bar's start for the VST3 ProcessContext (kTimeSigValid /
    /// kBarPositionValid). Non-positive numerator/denominator or a negative bar position mean "unknown" and leave the flag clear.
    /// </summary>
    private static void MusicalPosition(in TransportInfo transport, out int numerator, out int denominator, out double barPositionPpq)
    {
        // The song's bar map (WP-5) via TransportInfo.Meter; the ppq position itself comes from TransportInfo.PpqPosition.
        var meter = transport.Meter;
        if (!meter.IsValid)
        {
            numerator = 0;
            denominator = 0;
            barPositionPpq = -1;
            return;
        }
        numerator = meter.Numerator;
        denominator = meter.Denominator;
        barPositionPpq = double.IsFinite(meter.BarStartPpq) && meter.BarStartPpq >= 0 ? meter.BarStartPpq : -1;
    }

    public byte[]? GetState()
    {
        if (_handle == IntPtr.Zero) return null;
        if (!_newApi) return GetStateLegacy();
        // One serialise (tfv3_state_begin keeps the blob on the instance; tfv3_state_copy copies and always frees it), so a
        // parameter moving between a size query and the copy can no longer make the save fail.
        var size = tfv3_state_begin(_handle);
        if (size <= 0) return null;
        byte[] bytes;
        try
        {
            CheckStateSize(size);
            bytes = new byte[size];
        }
        catch
        {
            tfv3_state_copy(_handle, null, 0);   // free the pending blob
            throw;
        }
        fixed (byte* p = bytes)
            return tfv3_state_copy(_handle, p, size) == size ? bytes : throw new InvalidOperationException("The plug-in did not write its state.");
    }

    // tfvst3.dll without tfv3_state_begin: size query, then a second serialise into the buffer.
    private byte[]? GetStateLegacy()
    {
        var size = tfv3_get_state(_handle, null, 0);
        if (size <= 0) return null;
        CheckStateSize(size);
        var bytes = new byte[size];
        fixed (byte* p = bytes)
            return tfv3_get_state(_handle, p, size) > 0 ? bytes : throw new InvalidOperationException("The plug-in did not write its state.");
    }

    private static void CheckStateSize(int size)
    {
        if (size > TabForge.Audio.Contracts.PluginStateLimits.MaxRawBytes)
            throw new InvalidDataException($"The plug-in's state ({size / 1048576.0:0.0} MiB) is larger than the {TabForge.Audio.Contracts.PluginStateLimits.MaxRawBytes / 1048576} MiB limit.");
    }

    /// <summary>
    /// Applies a saved state. Throws <see cref="InvalidDataException"/> when the blob is malformed or the plug-in rejects it
    /// (tfv3_set_state returns 0), so <see cref="PluginLoading.CreateWithState"/> disposes the instance and the host reports
    /// "its saved settings were rejected" instead of silently loading the defaults.
    /// </summary>
    public void SetState(byte[] state)
    {
        if (state.Length == 0 || _handle == IntPtr.Zero) return;
        int ok;
        fixed (byte* p = state) ok = tfv3_set_state(_handle, p, state.Length);
        if (ok == 0) throw new InvalidDataException($"The VST3 plug-in rejected its saved state ({state.Length} bytes).");
    }

    public (int Width, int Height)? OpenEditor(IntPtr parent)
    {
        if (_editorOpen || _handle == IntPtr.Zero) return null;
        int w, h;
        if (tfv3_open_editor(_handle, parent, &w, &h) == 0) return null;
        _editorOpen = true;
        return (Math.Max(100, w), Math.Max(60, h));
    }

    public void CloseEditor()
    {
        if (!_editorOpen) return;
        _editorOpen = false;
        if (_handle != IntPtr.Zero) tfv3_close_editor(_handle);
    }

    public void EditorIdle() { }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CloseEditor();
        if (_handle != IntPtr.Zero) tfv3_destroy(_handle);   // zero after a failed Reconfigure
        _handle = IntPtr.Zero;
        for (var c = 0; c < 2; c++) { NativeMemory.Free(_in[c]); NativeMemory.Free(_out[c]); }
        NativeMemory.Free(_inPtrs); NativeMemory.Free(_outPtrs); NativeMemory.Free(_events);
    }
}
