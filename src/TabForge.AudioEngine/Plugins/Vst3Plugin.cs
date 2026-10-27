using System.Runtime.InteropServices;
using System.Text;

namespace TabForge.AudioEngine.Plugins;

// Owns: hosting a VST3 plug-in through the tfvst3 shim (tfvst3.dll; libtfvst3.dylib / libtfvst3.so on macOS / Linux; preallocated buffers and event array, allocation-free process).

/// <summary>
/// Hosts a VST3 plug-in through tfvst3.dll (native\tfvst3, a small C interface over the Steinberg VST3 SDK).
/// Buffers and the event array are allocated once; <see cref="Process"/> never allocates.
/// </summary>
public sealed unsafe partial class Vst3Plugin : IPluginInstance, IEditorResizeSource, IEditorSizeSink
{
    private const string Lib = "tfvst3.dll";   // the name the entry points are declared under; ShimFile is the file of this system
    private static string ShimFile => OperatingSystem.IsWindows() ? Lib : OperatingSystem.IsMacOS() ? "libtfvst3.dylib" : "libtfvst3.so";
    /// <summary>The shim, loaded only from this program's own folder (a bare name would also be searched on PATH and in the working folder).</summary>
    private static readonly string LibPath = System.IO.Path.Combine(AppContext.BaseDirectory, ShimFile);
    private const int MaxEvents = 512;

    static Vst3Plugin()
    {
        // Every DllImport of the shim resolves to the copy beside TabForge.exe; when that copy is absent the load fails.
        NativeLibrary.SetDllImportResolver(typeof(Vst3Plugin).Assembly, (name, _, _) => name == Lib ? NativeLibrary.Load(LibPath) : IntPtr.Zero);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Midi { public int SampleOffset; public byte Status, Data1, Data2, Pad; }


    /// <summary>
    /// True when the loaded tfvst3.dll has tfv3_process_ex, tfv3_state_begin/copy and tfv3_set_processing. Probed once on the
    /// main thread (first construction), so the audio thread never meets an EntryPointNotFoundException.
    /// </summary>
    private static readonly Lazy<bool> HasNewApi = new(() =>
    {
        IntPtr lib;
        if (!NativeLibrary.TryLoad(LibPath, out lib)) return false;
        return NativeLibrary.TryGetExport(lib, "tfv3_process_ex", out _) && NativeLibrary.TryGetExport(lib, "tfv3_state_begin", out _)
            && NativeLibrary.TryGetExport(lib, "tfv3_state_copy", out _) && NativeLibrary.TryGetExport(lib, "tfv3_set_processing", out _);
    });
    private readonly bool _newApi;

    private static readonly Lazy<bool> HasEditorApi = new(() =>
    {
        if (!NativeLibrary.TryLoad(LibPath, out var lib)) return false;
        var found = NativeLibrary.TryGetExport(lib, "tfv3_editor_poll_resize", out _)
            && NativeLibrary.TryGetExport(lib, "tfv3_editor_set_size", out _) && NativeLibrary.TryGetExport(lib, "tfv3_editor_set_scale", out _);
        NativeLibrary.Free(lib);   // release the probe's own reference
        return found;
    });

    /// <summary>True when the loaded tfvst3.dll has tfv3_editor_idle (the feed that keeps plug-in meters and displays moving); probed once on the main thread.</summary>
    private static readonly Lazy<bool> HasIdleApi = new(() =>
    {
        if (!NativeLibrary.TryLoad(LibPath, out var lib)) return false;
        var found = NativeLibrary.TryGetExport(lib, "tfv3_editor_idle", out _);
        NativeLibrary.Free(lib);   // release the probe's own reference
        return found;
    });

    /// <summary>Runs the shim's internal checks (output-parameter ring, message copy and queueing); 0 = all passed, -1 when this tfvst3.dll lacks them.</summary>
    internal static int NativeSelfTest() => HasSelfTest.Value ? tfv3_selftest() : -1;
    private static readonly Lazy<bool> HasSelfTest = new(() =>
    {
        if (!NativeLibrary.TryLoad(LibPath, out var lib)) return false;
        var found = NativeLibrary.TryGetExport(lib, "tfv3_selftest", out _);
        NativeLibrary.Free(lib);
        return found;
    });

    private IntPtr _handle;
    /// <summary>Diagnostics: the native instance pointer (for the editor log lines).</summary>
    internal IntPtr NativeHandle => _handle;
    private int _maxBlock;
    private readonly int _classIndex;
    /// <summary>Which of the file's audio classes this instance is (see <see cref="Vst3ClassInfo"/>).</summary>
    public int ClassIndex => _classIndex;
    /// <summary>The native instance exists, so its module is loaded and initialised in this process.</summary>
    public bool IsLive => _handle != IntPtr.Zero && !_disposed;
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
    private static volatile bool _noOfflineApi;

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
    public static bool Available => NativeLibrary.TryLoad(LibPath, out _);

    private readonly EngineLogWriter _log;

    public Vst3Plugin(string path, bool wantInstrument, double sampleRate, int maxBlock, EngineLogWriter log, IPluginModuleLoader? loader = null)
    {
        RequireModule(path, loader, OperatingSystem.IsWindows());
        Path = path;
        _log = log;
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

    /// <summary>
    /// On macOS and Linux the shim loads the bundle itself (Contents/MacOS, Contents/&lt;arch&gt;-linux); this asks the module loader first whether the
    /// bundle has a binary for this system, so a missing or foreign bundle gives a plain message instead of the shim's. Windows: nothing to check (the shim reports).
    /// </summary>
    public static void RequireModule(string path, IPluginModuleLoader? loader, bool windows)
    {
        if (windows) return;
        if ((loader ?? new NativePluginModuleLoader()).ResolveBinary(path) is null) throw new FileNotFoundException($"The VST3 plug-in has no module for this system: {path}");
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
            _log.Write($"VST3 reconfigure: live setupProcessing refused, recreating: {Path}");
        }
        RecreateWithState(sampleRate, maxBlock);
    }

    private void RecreateWithState(double sampleRate, int maxBlock)
    {
        byte[]? state = null;
        try { state = GetState(); }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException) { _log.Write($"VST3 reconfigure: state not captured: {Path}: {ex.Message}"); }
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

    private const float BaseDpi = 96f;
    private const uint GwOwner = 4;
    private IntPtr _editorParent;
    private uint _editorDpi;

    /// <summary>Tells the editor the scale of the monitor its owner window (the FX or main window, already placed) is on, when that changed; the popup itself is not placed yet when it opens.</summary>
    private void SyncScale()
    {
        var owner = GetWindow(_editorParent, GwOwner);
        if (GetDpiForWindow(owner != IntPtr.Zero ? owner : _editorParent) is not (> 0 and var dpi) || dpi == _editorDpi) return;
        _editorDpi = dpi;
        tfv3_editor_set_scale(_handle, dpi / BaseDpi);
    }

    public (int Width, int Height)? OpenEditor(IntPtr parent)
    {
        if (_editorOpen || _handle == IntPtr.Zero) return null;
        int w, h;
        (_editorParent, _editorDpi) = (parent, 0);
        if (HasEditorApi.Value) SyncScale();
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

    /// <summary>The view asked to resize (resizeView); the editor registry sizes the window and tells the view back through <see cref="SetEditorSize"/>.</summary>
    public event Action<int, int>? ResizeRequested;

    /// <summary>The plug-in asked for a restart (bit mask: 1 latency changed, 2 bus layout changed, 4 reload requested, 8 parameter values changed); raised on the main thread from <see cref="EditorIdle"/>.</summary>
    public event Action<int>? RestartRequested;

    public void EditorIdle()
    {
        if (!_editorOpen) return;
        // Processor output parameters (meters, gain reduction) and queued processor -> controller messages reach the editor here.
        if (HasIdleApi.Value && tfv3_editor_idle(_handle) is var restart and not 0) RestartRequested?.Invoke(restart);
        if (!HasEditorApi.Value) return;
        SyncScale();
        int w, h;
        if (tfv3_editor_poll_resize(_handle, &w, &h) != 0) ResizeRequested?.Invoke(Math.Max(100, w), Math.Max(60, h));
    }

    public void SetEditorSize(int width, int height)
    {
        if (_editorOpen && HasEditorApi.Value) tfv3_editor_set_size(_handle, width, height);
    }

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
