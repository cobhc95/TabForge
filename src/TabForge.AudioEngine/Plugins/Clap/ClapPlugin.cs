using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace TabForge.AudioEngine.Plugins.Clap;

// Owns: hosting one CLAP plug-in instance: create / init / activate, the process call (stereo audio in and out, note and MIDI events, transport),
//   latency and tail, and the buffers they use (allocated once; Process never allocates).
// Does not own: state, parameters and the editor (the other ClapPlugin.*.cs parts), loading the file (ClapModule), trust (PluginIdentity.Hold).
// Tests: ClapHostTests (a fake CLAP plug-in written in C#), the "CLAP real plug-in" self-test when a .clap is installed.
// Threads: Process on the audio thread only; everything else on the main thread. CLAP is hosted on every system; its editor only where an editor host exists.

/// <summary>
/// A CLAP plug-in (https://github.com/free-audio/clap, MIT) behind <see cref="IPluginInstance"/>. Audio is stereo in / out through the first
/// audio port of the plug-in (other ports get silence and are ignored); notes arrive as CLAP note events, other MIDI as CLAP MIDI events.
/// </summary>
public sealed unsafe partial class ClapPlugin : IPluginInstance, IPluginParameters, IEditorResizeSource, IEditorSizeSink
{
    private const int MaxEvents = 512 + 64, EventStride = 64, MaxChannels = 8, MaxPorts = 16;

    private readonly ClapModule _module;
    private readonly bool _ownsModule;
    private readonly EngineLogWriter _log;
    private ClapPluginT* _plugin;
    private ClapHost* _host;
    private GCHandle _hostHandle;
    private ClapPluginParams* _params;
    private ClapPluginState* _stateExt;
    private ClapPluginGui* _gui;
    private ClapPluginUInt32Query* _latency, _tail;
    private bool _active, _processing, _startFailed, _disposed;
    private double _sampleRate;
    private int _maxBlock;
    private volatile bool _callbackRequested, _restartRequested;

    // Preallocated, native: audio ports and the event list the plug-in reads.
    private readonly List<IntPtr> _allocs = new();
    private ClapAudioBuffer* _inBuffers, _outBuffers;
    private int _inPorts, _outPorts;
    private int _inChannels0 = 2, _outChannels0 = 2;
    private byte* _events;
    private int _eventCount;
    private ClapInputEvents* _inList;
    private ClapOutputEvents* _outList;
    private ClapProcess* _process;
    private ClapEventTransport* _transport;
    private long _steady;

    public string Path { get; }
    public bool IsInstrument { get; }
    /// <summary>The descriptor of the plug-in this instance runs (a .clap file can hold several).</summary>
    public ClapDescriptorInfo Descriptor { get; }
    /// <summary>The vendor and product name the plug-in reports (for the identify probe).</summary>
    public string Vendor => Descriptor.Vendor;
    public string ProductName => Descriptor.Name;

    /// <summary>Loads <paramref name="path"/> (already approved by the caller) and starts its first plug-in of the wanted role.</summary>
    public ClapPlugin(string path, bool wantInstrument, double sampleRate, int maxBlock, EngineLogWriter log)
        : this(ClapModule.Open(path, new NativePluginModuleLoader()), true, path, wantInstrument, sampleRate, maxBlock, log) { }

    /// <summary>Test seam and shared constructor: an opened module (fake entries in tests).</summary>
    internal ClapPlugin(ClapModule module, bool ownsModule, string path, bool wantInstrument, double sampleRate, int maxBlock, EngineLogWriter log)
    {
        _module = module;
        _ownsModule = ownsModule;
        _log = log;
        Path = path;
        var picked = module.Pick(wantInstrument);
        if (picked is null)
        {
            Abandon();
            throw new InvalidOperationException("No plug-in found in this CLAP file.");
        }
        Descriptor = picked;
        IsInstrument = picked.IsInstrument;
        try { Create(picked, sampleRate, Math.Clamp(maxBlock, 16, 8192)); }
        catch
        {
            DestroyPlugin();
            FreeNative();
            Abandon();
            throw;
        }
    }

    private void Abandon()
    {
        if (_ownsModule) _module.Dispose();   // each instance opens its own module: clap_entry init and deinit are paired per instance (CLAP 1.2 requires plug-ins to count them)
    }

    private void Create(ClapDescriptorInfo descriptor, double sampleRate, int maxBlock)
    {
        _host = ClapHostCallbacks.Create(this, out _hostHandle);
        var id = Encoding.UTF8.GetBytes(descriptor.Id + "\0");
        var factory = _module.Factory;
        fixed (byte* p = id) _plugin = factory->CreatePlugin(factory, _host, p);
        if (_plugin == null) throw new InvalidOperationException($"The CLAP plug-in '{descriptor.Name}' could not be created.");
        if (_plugin->Init(_plugin) == 0) throw new InvalidOperationException($"The CLAP plug-in '{descriptor.Name}' failed to initialise.");
        _params = (ClapPluginParams*)Extension(ClapIds.ExtParams);
        _stateExt = (ClapPluginState*)Extension(ClapIds.ExtState);
        _gui = (ClapPluginGui*)Extension(ClapIds.ExtGui);
        _latency = (ClapPluginUInt32Query*)Extension(ClapIds.ExtLatency);
        _tail = (ClapPluginUInt32Query*)Extension(ClapIds.ExtTail);
        _events = (byte*)NativeMemory.AllocZeroed((nuint)(MaxEvents * EventStride));
        _inList = (ClapInputEvents*)NativeMemory.AllocZeroed((nuint)sizeof(ClapInputEvents));
        _outList = (ClapOutputEvents*)NativeMemory.AllocZeroed((nuint)sizeof(ClapOutputEvents));
        _process = (ClapProcess*)NativeMemory.AllocZeroed((nuint)sizeof(ClapProcess));
        _transport = (ClapEventTransport*)NativeMemory.AllocZeroed((nuint)sizeof(ClapEventTransport));
        _inList->Ctx = (void*)GCHandle.ToIntPtr(_hostHandle);   // the list callbacks find this instance through the same handle as the host
        _inList->Size = &ListSize;
        _inList->Get = &ListGet;
        _outList->TryPush = &ListTryPush;
        Activate(sampleRate, maxBlock);
    }

    private void* Extension(string id)
    {
        var bytes = Encoding.ASCII.GetBytes(id + "\0");
        fixed (byte* p = bytes) return _plugin->GetExtension(_plugin, p);
    }

    private void Activate(double sampleRate, int maxBlock)
    {
        _sampleRate = sampleRate;
        _maxBlock = maxBlock;
        AllocateAudio(maxBlock);
        if (_plugin->Activate(_plugin, sampleRate, 1, (uint)maxBlock) == 0) throw new InvalidOperationException($"The CLAP plug-in '{Descriptor.Name}' could not be activated at {sampleRate:0} Hz.");
        _active = true;
        _startFailed = false;
    }

    // One buffer set per declared port (the plug-in may insist on seeing all of them); only port 0 carries the song audio.
    private void AllocateAudio(int maxBlock)
    {
        FreeAudio();
        var ports = (ClapPluginAudioPorts*)Extension(ClapIds.ExtAudioPorts);
        _inPorts = Math.Clamp(ports == null ? (IsInstrument ? 0 : 1) : (int)ports->Count(_plugin, 1), 0, MaxPorts);
        _outPorts = Math.Clamp(ports == null ? 1 : (int)ports->Count(_plugin, 0), 0, MaxPorts);
        _inBuffers = BuildPorts(ports, _inPorts, 1, maxBlock, out _inChannels0);
        _outBuffers = BuildPorts(ports, _outPorts, 0, maxBlock, out _outChannels0);
    }

    private ClapAudioBuffer* BuildPorts(ClapPluginAudioPorts* ports, int count, byte isInput, int maxBlock, out int firstChannels)
    {
        firstChannels = 2;
        if (count == 0) return null;
        var buffers = (ClapAudioBuffer*)NativeMemory.AllocZeroed((nuint)(count * sizeof(ClapAudioBuffer)));
        _allocs.Add((IntPtr)buffers);
        for (var i = 0; i < count; i++)
        {
            var channels = 2;
            if (ports != null)
            {
                ClapAudioPortInfo info = default;
                if (ports->Get(_plugin, (uint)i, isInput, &info) != 0 && info.ChannelCount > 0) channels = (int)Math.Min(info.ChannelCount, (uint)MaxChannels);
            }
            if (i == 0) firstChannels = channels;
            var array = (float**)NativeMemory.AllocZeroed((nuint)(channels * sizeof(float*)));
            _allocs.Add((IntPtr)array);
            for (var c = 0; c < channels; c++)
            {
                array[c] = (float*)NativeMemory.AllocZeroed((nuint)(maxBlock * sizeof(float)));
                _allocs.Add((IntPtr)array[c]);
            }
            buffers[i].Data32 = array;
            buffers[i].ChannelCount = (uint)channels;
        }
        return buffers;
    }

    private void FreeAudio()
    {
        foreach (var p in _allocs) NativeMemory.Free((void*)p);
        _allocs.Clear();
        _inBuffers = _outBuffers = null;
    }

    /// <summary>Runs the host calls the plug-in asked for (request_callback) on the main thread.</summary>
    public void PumpMainThread()
    {
        if (_disposed || _plugin == null || !_callbackRequested) return;
        _callbackRequested = false;
        _plugin->OnMainThread(_plugin);
    }

    internal void NoteCallbackRequested() => _callbackRequested = true;
    internal void NoteRestartRequested() => _restartRequested = true;

    /// <summary>True once the plug-in asked for a restart (re-activation); the engine device-change path (Reconfigure) does that.</summary>
    public bool RestartRequested => _restartRequested;

    public int LatencySamples => _disposed || _latency == null ? 0 : (int)Math.Min(_latency->Get(_plugin), 1_000_000u);

    public int TailSamples
    {
        get
        {
            if (_disposed || _tail == null) return 0;
            var tail = _tail->Get(_plugin);
            return (int)Math.Min(tail, (uint)Math.Max(0, _sampleRate * 10));   // infinite (uint.MaxValue) is capped at ten seconds
        }
    }

    public int InputChannels => 2;

    public void Reconfigure(double sampleRate, int maxBlock)
    {
        if (_disposed) return;
        maxBlock = Math.Clamp(maxBlock, 16, 8192);
        PumpMainThread();
        StopProcessing();
        if (_active) { _plugin->Deactivate(_plugin); _active = false; }
        _restartRequested = false;
        Activate(sampleRate, maxBlock);   // throws when the plug-in cannot follow; it then stays loaded but silent
    }

    private void StopProcessing()
    {
        if (!_processing) return;
        _processing = false;
        _plugin->StopProcessing(_plugin);
    }

    public void SetOfflineMode(bool offline) { }

    public void Process(float[][] input, float[][] output, int frames, ReadOnlySpan<BlockMidi> midi, in TransportInfo transport)
    {
        if (frames <= 0) return;
        if (_disposed || _plugin == null || !_active || _startFailed || _outBuffers == null) { Silence(output, frames); return; }
        frames = Math.Min(frames, _maxBlock);
        if (!_processing)
        {
            if (_plugin->StartProcessing(_plugin) == 0) { _startFailed = true; Silence(output, frames); return; }
            _processing = true;
        }
        FillInput(input, frames);
        for (var p = 0; p < _outPorts; p++)
            for (var c = 0; c < (int)_outBuffers[p].ChannelCount; c++) new Span<float>(_outBuffers[p].Data32[c], frames).Clear();
        _eventCount = 0;
        WriteQueuedParameters();
        for (var i = 0; i < midi.Length && _eventCount < MaxEvents; i++) WriteMidi(midi[i], frames);
        WriteTransport(transport);
        var proc = _process;
        proc->SteadyTime = _steady;
        _steady += frames;
        proc->FramesCount = (uint)frames;
        proc->Transport = _transport;
        proc->AudioInputs = _inBuffers;
        proc->AudioOutputs = _outBuffers;
        proc->AudioInputsCount = (uint)_inPorts;
        proc->AudioOutputsCount = (uint)_outPorts;
        proc->InEvents = _inList;
        proc->OutEvents = _outList;
        var status = _plugin->Process(_plugin, proc);
        if (status == ClapIds.ProcessError) { Silence(output, frames); return; }
        ReadOutput(output, frames);
    }

    private static void Silence(float[][] output, int frames)
    {
        for (var c = 0; c < output.Length; c++) output[c].AsSpan(0, Math.Min(frames, output[c].Length)).Clear();
    }

    private void FillInput(float[][] input, int frames)
    {
        if (_inBuffers == null || IsInstrument) return;   // an instrument input stays silent
        var buf = _inBuffers[0];
        if (_inChannels0 == 1)
        {
            var mono = new Span<float>(buf.Data32[0], frames);
            input[0].AsSpan(0, frames).CopyTo(mono);
            if (input.Length > 1) for (var i = 0; i < frames; i++) mono[i] = (mono[i] + input[1][i]) * 0.5f;
            return;
        }
        for (var c = 0; c < 2; c++)
            if (c < input.Length) input[c].AsSpan(0, frames).CopyTo(new Span<float>(buf.Data32[c], frames));
    }

    private void ReadOutput(float[][] output, int frames)
    {
        var buf = _outBuffers[0];
        for (var c = 0; c < output.Length; c++)
            new ReadOnlySpan<float>(buf.Data32[Math.Min(c, _outChannels0 - 1)], frames).CopyTo(output[c]);
    }

    private void WriteMidi(in BlockMidi m, int frames)
    {
        var at = _events + _eventCount * EventStride;
        var time = (uint)Math.Clamp(m.Frame, 0, frames - 1);
        var kind = m.Status & 0xF0;
        if (kind is 0x80 or 0x90)
        {
            var note = (ClapEventNote*)at;
            *note = default;
            note->Header = new ClapEventHeader { Size = (uint)sizeof(ClapEventNote), Time = time, Type = kind == 0x90 && m.Data2 > 0 ? ClapIds.NoteOn : ClapIds.NoteOff };
            note->NoteId = -1;
            note->PortIndex = 0;
            note->Channel = (short)(m.Status & 0x0F);
            note->Key = m.Data1;
            note->Velocity = m.Data2 / 127.0;
        }
        else
        {
            var raw = (ClapEventMidi*)at;
            *raw = default;
            raw->Header = new ClapEventHeader { Size = (uint)sizeof(ClapEventMidi), Time = time, Type = ClapIds.Midi };
            raw->Data[0] = m.Status; raw->Data[1] = m.Data1; raw->Data[2] = m.Data2;
        }
        _eventCount++;
    }

    private void WriteTransport(in TransportInfo t)
    {
        var e = _transport;
        *e = default;
        e->Header = new ClapEventHeader { Size = (uint)sizeof(ClapEventTransport), Type = 9 };
        e->Flags = ClapIds.TransportHasTempo | ClapIds.TransportHasBeats | ClapIds.TransportHasSeconds | (t.Playing ? ClapIds.TransportIsPlaying : 0);
        e->Tempo = t.Tempo > 0 ? t.Tempo : 120;
        e->SongPosBeats = (long)(t.PpqPosition * ClapIds.BeatTimeFactor);
        e->SongPosSeconds = (long)(t.SongSec * ClapIds.BeatTimeFactor);
        if (!t.Meter.IsValid) return;
        e->Flags |= ClapIds.TransportHasTimeSignature;
        e->TsigNum = (ushort)Math.Clamp(t.Meter.Numerator, 1, ushort.MaxValue);
        e->TsigDenom = (ushort)Math.Clamp(t.Meter.Denominator, 1, ushort.MaxValue);
        if (double.IsFinite(t.Meter.BarStartPpq) && t.Meter.BarStartPpq >= 0) e->BarStart = (long)(t.Meter.BarStartPpq * ClapIds.BeatTimeFactor);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static uint ListSize(ClapInputEvents* list) => Of(list->Ctx)?.EventCountForList ?? 0;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static ClapEventHeader* ListGet(ClapInputEvents* list, uint index) =>
        Of(list->Ctx) is { } p && index < p.EventCountForList ? (ClapEventHeader*)(p._events + (int)index * EventStride) : null;

    // Output events (parameter changes, notes) are accepted and dropped.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static byte ListTryPush(ClapOutputEvents* list, ClapEventHeader* header) => 1;

    private uint EventCountForList => (uint)_eventCount;

    private static ClapPlugin? Of(void* ctx) => ctx == null ? null : GCHandle.FromIntPtr((IntPtr)ctx).Target as ClapPlugin;

    public void Dispose()
    {
        if (_disposed) return;
        CloseEditor();
        _disposed = true;
        DestroyPlugin();
        FreeNative();
        Abandon();
    }

    private void DestroyPlugin()
    {
        if (_plugin == null) return;
        try
        {
            StopProcessing();
            if (_active) _plugin->Deactivate(_plugin);
            _active = false;
            _plugin->Destroy(_plugin);
        }
        catch (Exception ex) { _log.Write($"CLAP dispose failed: {ex.Message}: {Path}"); }
        _plugin = null;
    }

    private void FreeNative()
    {
        FreeAudio();
        ClapHostCallbacks.Release(_host, _hostHandle);
        _host = null;
        _hostHandle = default;
        foreach (var p in new[] { (IntPtr)_events, (IntPtr)_inList, (IntPtr)_outList, (IntPtr)_process, (IntPtr)_transport })
            if (p != IntPtr.Zero) NativeMemory.Free((void*)p);
        _events = null; _inList = null; _outList = null; _process = null; _transport = null;
    }
}
