using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace TabForge.AudioEngine.Plugins;

/// <summary>
/// Hosts a 64-bit VST2 plug-in (.dll) through its plain C interface (AEffect + dispatcher opcodes). Buffers and the
/// event list are allocated once; <see cref="Process"/> never allocates. A native crash inside the plug-in ends the
/// engine process (not TabForge); the engine records which plug-in was running so TabForge can switch it off.
/// </summary>
public sealed unsafe partial class Vst2Plugin : IPluginInstance
{
    // ---- VST2 ABI ----
    private const int Magic = 0x56737450; // 'VstP'
    private const int EffOpen = 0, EffClose = 1, EffSetSampleRate = 10, EffSetBlockSize = 11, EffMainsChanged = 12,
        EffEditGetRect = 13, EffEditOpen = 14, EffEditClose = 15, EffEditIdle = 19, EffGetChunk = 23, EffSetChunk = 24,
        EffProcessEvents = 25, EffStartProcess = 71, EffStopProcess = 72;
    private const int FlagHasEditor = 1, FlagCanReplacing = 1 << 4, FlagProgramChunks = 1 << 5, FlagIsSynth = 1 << 8;
    private const int MaxEvents = 512;
    /// <summary>A plug-in declaring more audio inputs or outputs than this is refused (its pointer arrays are allocated as declared).</summary>
    public const int MaxChannels = 128;
    // VstTimeInfo flags.
    private const int KVstTransportChanged = 1, KVstTransportPlaying = 2, KVstPpqPosValid = 1 << 9, KVstTempoValid = 1 << 10,
        KVstBarsValid = 1 << 11, KVstTimeSigValid = 1 << 13;

    [StructLayout(LayoutKind.Sequential)]
    private struct AEffect
    {
        public int magic;
        public IntPtr dispatcher, process, setParameter, getParameter;
        public int numPrograms, numParams, numInputs, numOutputs, flags;
        public IntPtr resvd1, resvd2;
        public int initialDelay, realQualities, offQualities;
        public float ioRatio;
        public IntPtr obj, user;
        public int uniqueID, version;
        public IntPtr processReplacing, processDoubleReplacing;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct VstMidiEvent
    {
        public int type, byteSize, deltaFrames, flags, noteLength, noteOffset;
        public byte b0, b1, b2, b3;
        public byte detune, noteOffVelocity, reserved1, reserved2;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct VstTimeInfo
    {
        public double samplePos, sampleRate, nanoSeconds, ppqPos, tempo, barStartPos, cycleStartPos, cycleEndPos;
        public int timeSigNumerator, timeSigDenominator, smpteOffset, smpteFrameRate, samplesToNextClock, flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ERect { public short top, left, bottom, right; }

    // Plug-ins call back into the host with their AEffect*; map it to the instance.
    private static readonly ConcurrentDictionary<IntPtr, Vst2Plugin> Instances = new();
    [ThreadStatic] private static Vst2Plugin? _loading;

    private readonly IntPtr _library;
    private readonly AEffect* _effect;
    private readonly delegate* unmanaged[Cdecl]<AEffect*, int, int, IntPtr, IntPtr, float, IntPtr> _dispatch;
    private readonly delegate* unmanaged[Cdecl]<AEffect*, float**, float**, int, void> _processReplacing;
    private double _sampleRate;
    private int _maxBlock;
    private readonly IntPtr _events;       // VstEvents header + pointer array + events
    private readonly VstMidiEvent* _eventData;
    private readonly float** _inputs;
    private readonly float** _outputs;
    private readonly float*[] _inputBuffers;
    private readonly float*[] _outputBuffers;
    private readonly VstTimeInfo* _timeInfo;
    private readonly byte[] _nameBuffer = new byte[256];
    private bool _editorOpen;
    private bool _disposed;
    private double _samplePos;
    private bool _wasPlaying;
    private readonly BlockMidi[] _midiOut = new BlockMidi[MaxEvents];
    private int _midiOutCount, _midiOutFrames = 1;

    public ReadOnlySpan<BlockMidi> MidiOut => new(_midiOut, 0, _midiOutCount);

    /// <summary>Declared inputs (at least two); four or more take a sidechain on 3/4.</summary>
    public int InputChannels => _inputBuffers.Length;

    /// <summary>audioMasterProcessEvents: the plug-in hands MIDI to the host. Copied into the preallocated buffer (no allocation).</summary>
    private void CollectOut(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero) return;
        var n = Math.Min(*(int*)ptr, 1024);
        var list = (IntPtr*)((byte*)ptr + 16);
        for (var i = 0; i < n && _midiOutCount < _midiOut.Length; i++)
        {
            var e = (VstMidiEvent*)list[i];
            if (e is null || e->type != 1) continue;
            _midiOut[_midiOutCount++] = new BlockMidi { Frame = Math.Clamp(e->deltaFrames, 0, _midiOutFrames - 1), Status = e->b0, Data1 = e->b1, Data2 = e->b2 };
        }
    }

    public string Path { get; }
    public bool IsInstrument { get; }
    public bool HasEditor => (_effect->flags & FlagHasEditor) != 0;
    public int LatencySamples => _effect->initialDelay;

    /// <summary>effGetTailSize (52): the tail in samples; 0 and 1 mean "none / default".</summary>
    public int TailSamples { get { var t = (int)Dispatch(52, 0, IntPtr.Zero, IntPtr.Zero, 0); return t > 1 ? t : 0; } }

    public void SetOfflineMode(bool offline)
    {
        if (_disposed) return;
        // Process level 4 is answered on the render workers (see HostCallback); the mains cycle lets the plug-in re-read it.
        Dispatch(EffStopProcess, 0, IntPtr.Zero, IntPtr.Zero, 0);
        Dispatch(EffMainsChanged, 0, 0, IntPtr.Zero, 0);
        Dispatch(EffMainsChanged, 0, 1, IntPtr.Zero, 0);
        Dispatch(EffStartProcess, 0, IntPtr.Zero, IntPtr.Zero, 0);
        _samplePos = 0;
    }

    /// <summary>Vendor as the plug-in reports it (effGetVendorString).</summary>
    public string Vendor => AskString(47);
    /// <summary>Product name as the plug-in reports it (effGetProductString, else effGetEffectName).</summary>
    public string ProductName => AskString(48) is { Length: > 0 } p ? p : AskString(45);

    private string AskString(int opcode)
    {
        fixed (byte* buffer = _nameBuffer)
        {
            new Span<byte>(buffer, _nameBuffer.Length).Clear();
            Dispatch(opcode, 0, IntPtr.Zero, (IntPtr)buffer, 0);
            var length = new ReadOnlySpan<byte>(buffer, _nameBuffer.Length - 1).IndexOf((byte)0);
            return Encoding.ASCII.GetString(buffer, length < 0 ? _nameBuffer.Length - 1 : length).Trim();
        }
    }

    /// <summary>The plug-in's own programs (factory presets), by name.</summary>
    public IReadOnlyList<string> Programs
    {
        get
        {
            var count = Math.Clamp(_effect->numPrograms, 0, 512);
            var names = new List<string>(count);
            for (var i = 0; i < count; i++)
            {
                fixed (byte* buffer = _nameBuffer)
                {
                    new Span<byte>(buffer, _nameBuffer.Length).Clear();
                    Dispatch(29, i, IntPtr.Zero, (IntPtr)buffer, 0); // effGetProgramNameIndexed
                    var length = new ReadOnlySpan<byte>(buffer, _nameBuffer.Length - 1).IndexOf((byte)0);
                    var name = Encoding.ASCII.GetString(buffer, length < 0 ? 0 : length).Trim();
                    names.Add(name.Length > 0 ? name : $"Program {i + 1}");
                }
            }
            return names;
        }
    }

    public int CurrentProgram => (int)Dispatch(3, 0, IntPtr.Zero, IntPtr.Zero, 0);
    public void SetProgram(int index) => Dispatch(2, 0, index, IntPtr.Zero, 0);
    /// <summary>Raised on the main thread when the plug-in reports a parameter edit (the song has changed).</summary>
    public event Action? Edited;

    // Host callbacks arrive on any thread, the audio thread included (audioMasterAutomate from processReplacing: modulated
    // parameters, macro knobs). They only set these flags and bump the process-wide signal: no closure, no queue node, no allocation.
    // The main loop (EngineThreads.RunPending) raises Edited / ResizeRequested once per flag.
    private int _editedFlag, _resizeFlag;
    private long _pendingSize;
    private static int _hostSignal, _hostSeen;

    /// <summary>Any thread, allocation-free: the plug-in reported an edit.</summary>
    private void FlagEdited()
    {
        Volatile.Write(ref _editedFlag, 1);
        Interlocked.Increment(ref _hostSignal);
    }

    /// <summary>
    /// Engine / plug-in host main thread: raises <see cref="Edited"/> (once, however many automate / begin / end edits arrived) and
    /// <see cref="ResizeRequested"/> (the latest size) for the plug-ins that flagged them. Costs one read when nothing happened.
    /// </summary>
    public static void DeliverHostNotifications()
    {
        var signal = Volatile.Read(ref _hostSignal);
        if (signal == _hostSeen) return;
        _hostSeen = signal;
        foreach (var plugin in Instances.Values)
        {
            if (Interlocked.Exchange(ref plugin._editedFlag, 0) != 0)
            {
                try { plugin.Edited?.Invoke(); } catch (Exception ex) { EngineLog.Write($"plug-in edit notification failed: {ex.GetBaseException().Message}"); }
            }
            if (Interlocked.Exchange(ref plugin._resizeFlag, 0) != 0)
            {
                var size = Interlocked.Read(ref plugin._pendingSize);
                try { plugin.ResizeRequested?.Invoke((int)(size >> 32), (int)(uint)size); } catch (Exception ex) { EngineLog.Write($"plug-in resize failed: {ex.GetBaseException().Message}"); }
            }
        }
    }

    public Vst2Plugin(string path, double sampleRate, int maxBlock) : this(path, NativeLibrary.Load(path), IntPtr.Zero, sampleRate, maxBlock) { }

    /// <param name="library">The loaded module (freed only if the plug-in's entry point never ran); zero with an explicit <paramref name="entryPoint"/>.</param>
    /// <param name="entryPoint">VSTPluginMain; zero = look it up in <paramref name="library"/>.</param>
    private Vst2Plugin(string path, IntPtr library, IntPtr entryPoint, double sampleRate, int maxBlock)
    {
        Path = path;
        _sampleRate = sampleRate;
        _maxBlock = maxBlock;
        _library = library;
        bool entered = false, opened = false;
        try
        {
            // Time info and the event list first: a plug-in may ask for the time (audioMasterGetTime) from VSTPluginMain or effOpen.
            _timeInfo = (VstTimeInfo*)NativeMemory.AllocZeroed((nuint)sizeof(VstTimeInfo));
            var header = 16 + MaxEvents * IntPtr.Size;
            _events = (IntPtr)NativeMemory.AllocZeroed((nuint)(header + MaxEvents * sizeof(VstMidiEvent)));
            _eventData = (VstMidiEvent*)((byte*)_events + header);
            for (var i = 0; i < MaxEvents; i++) ((IntPtr*)((byte*)_events + 16))[i] = (IntPtr)(_eventData + i);

            var main = entryPoint;
            if (main == IntPtr.Zero && !NativeLibrary.TryGetExport(_library, "VSTPluginMain", out main) && !NativeLibrary.TryGetExport(_library, "main", out main))
                throw new InvalidOperationException("Not a VST2 plug-in (no VSTPluginMain).");
            var entry = (delegate* unmanaged[Cdecl]<IntPtr, AEffect*>)main;
            _loading = this;
            entered = true;
            try { _effect = entry((IntPtr)(delegate* unmanaged[Cdecl]<AEffect*, int, int, IntPtr, IntPtr, float, IntPtr>)&HostCallback); }
            finally { _loading = null; }
            if (_effect is null || _effect->magic != Magic) throw new InvalidOperationException("The plug-in did not start (bad VST2 entry).");
            if ((_effect->flags & FlagCanReplacing) == 0) throw new InvalidOperationException("The plug-in does not support 32-bit float processing.");
            Instances[(IntPtr)_effect] = this;
            _dispatch = (delegate* unmanaged[Cdecl]<AEffect*, int, int, IntPtr, IntPtr, float, IntPtr>)_effect->dispatcher;
            _processReplacing = (delegate* unmanaged[Cdecl]<AEffect*, float**, float**, int, void>)_effect->processReplacing;
            IsInstrument = (_effect->flags & FlagIsSynth) != 0;

            Dispatch(EffOpen, 0, IntPtr.Zero, IntPtr.Zero, 0);
            opened = true;
            // The channel pointer arrays hold exactly as many channels as the plug-in declares (read after effOpen, where some
            // plug-ins settle their I/O), at least stereo: the plug-in indexes them up to numInputs / numOutputs. Past 128: refused.
            var declaredIn = _effect->numInputs;
            var declaredOut = _effect->numOutputs;
            if (declaredIn is < 0 or > MaxChannels || declaredOut is < 0 or > MaxChannels)
                throw new InvalidOperationException($"The plug-in declares {declaredIn} inputs and {declaredOut} outputs (at most {MaxChannels} are supported).");
            var ins = Math.Max(2, declaredIn);
            var outs = Math.Max(2, declaredOut);
            _inputBuffers = new float*[ins];
            _outputBuffers = new float*[outs];
            _inputs = (float**)NativeMemory.AllocZeroed((nuint)(ins * sizeof(float*)));
            _outputs = (float**)NativeMemory.AllocZeroed((nuint)(outs * sizeof(float*)));
            for (var c = 0; c < ins; c++) _inputs[c] = _inputBuffers[c] = (float*)NativeMemory.AllocZeroed((nuint)(maxBlock * sizeof(float)));
            for (var c = 0; c < outs; c++) _outputs[c] = _outputBuffers[c] = (float*)NativeMemory.AllocZeroed((nuint)(maxBlock * sizeof(float)));

            Dispatch(EffSetSampleRate, 0, IntPtr.Zero, IntPtr.Zero, (float)sampleRate);
            Dispatch(EffSetBlockSize, 0, maxBlock, IntPtr.Zero, 0);
            Dispatch(EffMainsChanged, 0, 1, IntPtr.Zero, 0);
            Dispatch(EffStartProcess, 0, IntPtr.Zero, IntPtr.Zero, 0);
        }
        catch
        {
            // Close what was opened, free what was allocated. The module is unloaded only if the plug-in's own code never ran:
            // once VSTPluginMain / effOpen ran it may have started threads (Dispose never unloads it either, for the same reason).
            if (opened) Dispatch(EffClose, 0, IntPtr.Zero, IntPtr.Zero, 0);
            if (_effect is not null) Instances.TryRemove((IntPtr)_effect, out _);
            if (_inputBuffers is not null) foreach (var b in _inputBuffers) NativeMemory.Free(b);
            if (_outputBuffers is not null) foreach (var b in _outputBuffers) NativeMemory.Free(b);
            NativeMemory.Free(_inputs); NativeMemory.Free(_outputs);
            NativeMemory.Free((void*)_events); NativeMemory.Free(_timeInfo);
            if (!entered && _library != IntPtr.Zero) NativeLibrary.Free(_library);
            throw;
        }
    }

    /// <summary>
    /// Device change (main thread, audio stopped): the standard suspend / resume cycle with the new rate and block size; the
    /// channel buffers are reallocated for the new block size. The plug-in keeps its settings.
    /// </summary>
    public void Reconfigure(double sampleRate, int maxBlock)
    {
        if (_disposed) return;
        maxBlock = Math.Clamp(maxBlock, 16, 8192);
        Dispatch(EffStopProcess, 0, IntPtr.Zero, IntPtr.Zero, 0);
        Dispatch(EffMainsChanged, 0, 0, IntPtr.Zero, 0);
        if (maxBlock != _maxBlock)
        {
            for (var c = 0; c < _inputBuffers.Length; c++)
            {
                NativeMemory.Free(_inputBuffers[c]);
                _inputs[c] = _inputBuffers[c] = (float*)NativeMemory.AllocZeroed((nuint)(maxBlock * sizeof(float)));
            }
            for (var c = 0; c < _outputBuffers.Length; c++)
            {
                NativeMemory.Free(_outputBuffers[c]);
                _outputs[c] = _outputBuffers[c] = (float*)NativeMemory.AllocZeroed((nuint)(maxBlock * sizeof(float)));
            }
        }
        _sampleRate = sampleRate;
        _maxBlock = maxBlock;
        Dispatch(EffSetSampleRate, 0, IntPtr.Zero, IntPtr.Zero, (float)sampleRate);
        Dispatch(EffSetBlockSize, 0, maxBlock, IntPtr.Zero, 0);
        Dispatch(EffMainsChanged, 0, 1, IntPtr.Zero, 0);
        Dispatch(EffStartProcess, 0, IntPtr.Zero, IntPtr.Zero, 0);
        _samplePos = 0;
    }

    private IntPtr Dispatch(int opcode, int index, IntPtr value, IntPtr ptr, float opt) => _dispatch(_effect, opcode, index, value, ptr, opt);

    public void Process(float[][] input, float[][] output, int frames, ReadOnlySpan<BlockMidi> midi, in TransportInfo transport)
    {
        if (_disposed || frames <= 0) return;
        frames = Math.Min(frames, _maxBlock);
        _midiOutCount = 0; _midiOutFrames = frames;
        var count = Math.Min(midi.Length, MaxEvents);
        if (count > 0)
        {
            for (var i = 0; i < count; i++)
            {
                ref var e = ref _eventData[i];
                e.type = 1; e.byteSize = sizeof(VstMidiEvent); e.deltaFrames = Math.Clamp(midi[i].Frame, 0, frames - 1);
                e.flags = 1; // realtime
                e.b0 = midi[i].Status; e.b1 = midi[i].Data1; e.b2 = midi[i].Data2; e.b3 = 0;
            }
            *(int*)_events = count;
            Dispatch(EffProcessEvents, 0, IntPtr.Zero, _events, 0);
        }
        _timeInfo->sampleRate = _sampleRate;
        _timeInfo->samplePos = _samplePos;
        _timeInfo->tempo = transport.Tempo;
        _timeInfo->ppqPos = transport.PpqPosition;
        var flags = (transport.Playing ? KVstTransportPlaying : 0) | KVstPpqPosValid | KVstTempoValid;
        if (transport.Playing != _wasPlaying) { flags |= KVstTransportChanged; _wasPlaying = transport.Playing; }
        var meter = transport.Meter;
        if (meter.IsValid)
        {
            // The song's real meter and bar start (3/4, 6/8, odd meters), so arpeggiators, synced LFOs and gates stay on the bar.
            _timeInfo->timeSigNumerator = meter.Numerator; _timeInfo->timeSigDenominator = meter.Denominator;
            _timeInfo->barStartPos = meter.BarStartPpq;
            flags |= KVstTimeSigValid | KVstBarsValid;
        }
        else { _timeInfo->timeSigNumerator = 4; _timeInfo->timeSigDenominator = 4; _timeInfo->barStartPos = 0; }   // unknown: flags say so
        _timeInfo->flags = flags;
        _samplePos += frames;

        for (var c = 0; c < _inputBuffers.Length; c++)
        {
            var dst = new Span<float>(_inputBuffers[c], frames);
            if (c < input.Length) input[c].AsSpan(0, frames).CopyTo(dst); else dst.Clear();
        }
        for (var c = 0; c < _outputBuffers.Length; c++) new Span<float>(_outputBuffers[c], frames).Clear();
        _processReplacing(_effect, _inputs, _outputs, frames);
        for (var c = 0; c < output.Length; c++)
            new ReadOnlySpan<float>(_outputBuffers[Math.Min(c, _outputBuffers.Length - 1)], frames).CopyTo(output[c]);
    }

    public byte[]? GetState()
    {
        if ((_effect->flags & FlagProgramChunks) == 0) return null;
        IntPtr data;
        var size = (long)Dispatch(EffGetChunk, 0, IntPtr.Zero, (IntPtr)(&data), 0);
        if (size <= 0 || data == IntPtr.Zero) return null;
        if (size > TabForge.Audio.Contracts.PluginStateLimits.MaxRawBytes)
            throw new InvalidDataException($"The plug-in's state ({size / 1048576.0:0.0} MiB) is larger than the {TabForge.Audio.Contracts.PluginStateLimits.MaxRawBytes / 1048576} MiB limit.");
        var bytes = new byte[size];
        Marshal.Copy(data, bytes, 0, (int)size);
        return bytes;
    }

    public void SetState(byte[] state)
    {
        if ((_effect->flags & FlagProgramChunks) == 0 || state.Length == 0) return;
        fixed (byte* p = state) Dispatch(EffSetChunk, 0, state.Length, (IntPtr)p, 0);
    }

    public (int Width, int Height)? OpenEditor(IntPtr parent)
    {
        if (!HasEditor || _editorOpen) return null;
        ERect* rect = null;
        Dispatch(EffEditGetRect, 0, IntPtr.Zero, (IntPtr)(&rect), 0);
        Dispatch(EffEditOpen, 0, IntPtr.Zero, parent, 0);
        _editorOpen = true;
        // Many plug-ins (e.g. ReaEQ) only know their real size once the editor is open: ask again.
        ERect* opened = null;
        Dispatch(EffEditGetRect, 0, IntPtr.Zero, (IntPtr)(&opened), 0);
        if (opened is not null && opened->right - opened->left > 0) rect = opened;
        return rect is null ? (400, 300) : (Math.Max(100, rect->right - rect->left), Math.Max(60, rect->bottom - rect->top));
    }

    public void CloseEditor()
    {
        if (!_editorOpen) return;
        _editorOpen = false;
        Dispatch(EffEditClose, 0, IntPtr.Zero, IntPtr.Zero, 0);
    }

    public void EditorIdle()
    {
        if (_editorOpen) Dispatch(EffEditIdle, 0, IntPtr.Zero, IntPtr.Zero, 0);
    }

    /// <summary>Resize request from the plug-in (audioMasterSizeWindow).</summary>
    public event Action<int, int>? ResizeRequested;

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
                case 23: return EngineThreads.IsOfflineWorker ? 4 : EngineThreads.IsAudioThread ? 2 : 1;   // currentProcessLevel: offline / realtime / user
                case 32: return WriteString(ptr, "TabForge", 64);       // vendor
                case 33: return WriteString(ptr, "TabForge Audio Engine", 64);
                case 34: return 1;
                case 37:                                               // canDo
                    var what = ptr == IntPtr.Zero ? "" : Marshal.PtrToStringAnsi(ptr) ?? "";
                    return what is "sendVstEvents" or "sendVstMidiEvent" or "sizeWindow" or "sendVstTimeInfo" or "receiveVstEvents" or "receiveVstMidiEvent" ? 1 : 0;
                case 42: return 1;                                     // updateDisplay
                default: return 0;
            }
        }
        catch { return 0; } // never let an exception cross into native code
    }

    private static IntPtr WriteString(IntPtr ptr, string text, int max)
    {
        if (ptr == IntPtr.Zero) return 0;
        var bytes = Encoding.ASCII.GetBytes(text);
        var n = Math.Min(bytes.Length, max - 1);
        Marshal.Copy(bytes, 0, ptr, n);
        ((byte*)ptr)[n] = 0;
        return 1;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CloseEditor();
        Dispatch(EffStopProcess, 0, IntPtr.Zero, IntPtr.Zero, 0);
        Dispatch(EffMainsChanged, 0, 0, IntPtr.Zero, 0);
        Dispatch(EffClose, 0, IntPtr.Zero, IntPtr.Zero, 0);
        Instances.TryRemove((IntPtr)_effect, out _);
        foreach (var b in _inputBuffers) NativeMemory.Free(b);
        foreach (var b in _outputBuffers) NativeMemory.Free(b);
        NativeMemory.Free(_inputs); NativeMemory.Free(_outputs);
        NativeMemory.Free((void*)_events); NativeMemory.Free(_timeInfo);
        // The DLL is intentionally not unloaded: many plug-ins crash when unloaded while their threads still run.
        _ = _library; _ = _nameBuffer;
    }
}
