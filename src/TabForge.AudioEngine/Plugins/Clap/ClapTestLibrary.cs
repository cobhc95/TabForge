#if TABFORGE_TEST_HOOKS
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace TabForge.AudioEngine.Plugins.Clap;

// Owns: a fake CLAP plug-in file written in C# (entry, factory, a gain effect and a test synth, params, state, audio ports, latency, tail, gui), so the CLAP host
//   is tested through its real native-struct path without a native build. Compiled only with TABFORGE_TEST_HOOKS (Debug and full-suite builds), never into Release.
// Does not own: the host (ClapPlugin, ClapModule).
// Tests: ClapHostTests, the CLAP self-test.

/// <summary>What the fake plug-in saw, copied out of native memory for tests.</summary>
public sealed record ClapTestSnapshot(int Kind, double Gain, double SampleRate, uint MaxFrames, bool Active, bool Processing, int NoteOns, int NoteOffs, int ParamEvents,
    int MidiEvents, int MainThreadCalls, int LastKey, double LastVelocity, uint TransportFlags, double Tempo, double BeatsPosition, int TsigNum, int TsigDenom,
    bool GuiCreated, bool GuiShown, IntPtr Parent, int Blocks, int SizeSet);

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct FakeClapState
{
    public int Kind;   // 0 gain effect, 1 synth
    public double Gain, SampleRate, Level, LastVelocity, Tempo;
    public uint MaxFrames, TransportFlags;
    public int Active, Processing, NoteOns, NoteOffs, ParamEvents, MidiEvents, MainThreadCalls, LastKey, GuiCreated, GuiShown, Blocks, SizeSet;
    public long BeatsFixed;
    public ushort TsigNum, TsigDenom;
    public void* Parent;
    public ClapHost* Host;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct FakeClapFactory
{
    public ClapPluginFactory Base;
    public ClapPluginDescriptor* Gain, Synth;
}

public unsafe static class ClapTestLibrary
{
    private static readonly ClapPluginEntry* Entry = BuildEntry();
    private static readonly FakeClapFactory* Factory = BuildFactory();
    private static readonly FakeClapState** LastSlot = (FakeClapState**)NativeMemory.AllocZeroed((nuint)sizeof(void*));
    private static readonly int* Counters = (int*)NativeMemory.AllocZeroed(16);   // 0: init calls, 1: deinit calls, 2: plug-ins created, 3: destroyed
    private static readonly ClapPluginParams* ParamsExt = BuildParams();
    private static readonly ClapPluginState* StateExt = BuildState();
    private static readonly ClapPluginAudioPorts* PortsExt = BuildPorts();
    private static readonly ClapPluginUInt32Query* LatencyExt = BuildQuery(&Latency);
    private static readonly ClapPluginUInt32Query* TailExt = BuildQuery(&Tail);
    private static readonly ClapPluginGui* GuiExt = BuildGui();

    public const string GainId = "tabforge.test.gain", SynthId = "tabforge.test.synth";
    public const uint GainParam = 7, HiddenParam = 8;
    public const int LatencyFrames = 64, TailFrames = 1000;

    public static int InitCalls => Counters[0];
    public static int DeinitCalls => Counters[1];
    public static int Created => Counters[2];
    public static int Destroyed => Counters[3];

    /// <summary>The fake file opened as a module (no loader; init and deinit are counted).</summary>
    internal static ClapModule OpenModule(string path = "fake.clap") => new(path, Entry, null);

    /// <summary>A fake plug-in instance of the wanted role, through the same constructor the loader path uses after opening the file.</summary>
    public static ClapPlugin Create(bool instrument, double sampleRate, int maxBlock, EngineLogWriter log) =>
        new(OpenModule(), true, "fake.clap", instrument, sampleRate, maxBlock, log);

    public static ClapTestSnapshot? Snapshot()
    {
        var s = *LastSlot;
        if (s == null) return null;
        return new ClapTestSnapshot(s->Kind, s->Gain, s->SampleRate, s->MaxFrames, s->Active != 0, s->Processing != 0, s->NoteOns, s->NoteOffs, s->ParamEvents, s->MidiEvents,
            s->MainThreadCalls, s->LastKey, s->LastVelocity, s->TransportFlags, s->Tempo, s->BeatsFixed / ClapIds.BeatTimeFactor, s->TsigNum, s->TsigDenom,
            s->GuiCreated != 0, s->GuiShown != 0, (IntPtr)s->Parent, s->Blocks, s->SizeSet);
    }

    /// <summary>The plug-in asks its host for a main-thread callback, as a real one does from a worker thread.</summary>
    public static void RequestHostCallback()
    {
        var s = *LastSlot;
        if (s != null && s->Host != null) s->Host->RequestCallback(s->Host);
    }

    /// <summary>The plug-in asks its host to resize its editor, as a real one does when the user changes a panel.</summary>
    public static void RequestHostResize(uint width, uint height)
    {
        var s = *LastSlot;
        if (s == null || s->Host == null) return;
        var gui = (ClapHostGui*)s->Host->GetExtension(s->Host, Utf8Temp(ClapIds.ExtGui));
        if (gui != null) gui->RequestResize(s->Host, width, height);
    }

    private static byte* Utf8Temp(string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text + "\0");
        var p = (byte*)NativeMemory.Alloc((nuint)bytes.Length);
        for (var i = 0; i < bytes.Length; i++) p[i] = bytes[i];
        return p;   // a few bytes per call in a test hook: left allocated
    }

    private static byte* Text(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text + "\0");
        var p = (byte*)NativeMemory.Alloc((nuint)bytes.Length);
        for (var i = 0; i < bytes.Length; i++) p[i] = bytes[i];
        return p;
    }

    private static ClapPluginEntry* BuildEntry()
    {
        var e = (ClapPluginEntry*)NativeMemory.AllocZeroed((nuint)sizeof(ClapPluginEntry));
        e->Version = ClapVersion.Current;
        e->Init = &EntryInit;
        e->Deinit = &EntryDeinit;
        e->GetFactory = &EntryGetFactory;
        return e;
    }

    private static ClapPluginDescriptor* Descriptor(string id, string name, params string[] features)
    {
        var d = (ClapPluginDescriptor*)NativeMemory.AllocZeroed((nuint)sizeof(ClapPluginDescriptor));
        d->Version = ClapVersion.Current;
        d->Id = Text(id); d->Name = Text(name); d->Vendor = Text("TabForge Test"); d->VersionText = Text("1.0"); d->Description = Text("A fake CLAP plug-in for tests");
        var list = (byte**)NativeMemory.AllocZeroed((nuint)((features.Length + 1) * sizeof(byte*)));
        for (var i = 0; i < features.Length; i++) list[i] = Text(features[i]);
        d->Features = list;
        return d;
    }

    private static FakeClapFactory* BuildFactory()
    {
        var f = (FakeClapFactory*)NativeMemory.AllocZeroed((nuint)sizeof(FakeClapFactory));
        f->Base.GetPluginCount = &FactoryCount;
        f->Base.GetPluginDescriptor = &FactoryDescriptor;
        f->Base.CreatePlugin = &FactoryCreate;
        f->Synth = Descriptor(SynthId, "TabForge Test Synth", "instrument", "synthesizer");   // listed first: the picker must still find the effect for an effect request
        f->Gain = Descriptor(GainId, "TabForge Test Gain", "audio-effect", "utility");
        return f;
    }

    private static ClapPluginParams* BuildParams()
    {
        var p = (ClapPluginParams*)NativeMemory.AllocZeroed((nuint)sizeof(ClapPluginParams));
        p->Count = &ParamCount; p->GetInfo = &ParamInfo; p->GetValue = &ParamValue; p->ValueToText = &ParamText; p->TextToValue = &ParamParse; p->Flush = &ParamFlush;
        return p;
    }

    private static ClapPluginState* BuildState()
    {
        var s = (ClapPluginState*)NativeMemory.AllocZeroed((nuint)sizeof(ClapPluginState));
        s->Save = &StateSave; s->Load = &StateLoad;
        return s;
    }

    private static ClapPluginAudioPorts* BuildPorts()
    {
        var p = (ClapPluginAudioPorts*)NativeMemory.AllocZeroed((nuint)sizeof(ClapPluginAudioPorts));
        p->Count = &PortCount; p->Get = &PortGet;
        return p;
    }

    private static ClapPluginUInt32Query* BuildQuery(delegate* unmanaged[Cdecl]<ClapPluginT*, uint> getter)
    {
        var q = (ClapPluginUInt32Query*)NativeMemory.AllocZeroed((nuint)sizeof(ClapPluginUInt32Query));
        q->Get = getter;
        return q;
    }

    private static ClapPluginGui* BuildGui()
    {
        var g = (ClapPluginGui*)NativeMemory.AllocZeroed((nuint)sizeof(ClapPluginGui));
        g->IsApiSupported = &GuiSupported; g->Create = &GuiCreate; g->Destroy = &GuiDestroy; g->SetScale = &GuiScale; g->GetSize = &GuiGetSize; g->CanResize = &GuiCanResize;
        g->AdjustSize = &GuiAdjust; g->SetSize = &GuiSetSize; g->SetParent = &GuiSetParent; g->Show = &GuiShow; g->Hide = &GuiHide;
        return g;
    }

    private static FakeClapState* State(ClapPluginT* p) => (FakeClapState*)p->PluginData;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static byte EntryInit(byte* path) { Interlocked.Increment(ref Counters[0]); return 1; }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void EntryDeinit() => Interlocked.Increment(ref Counters[1]);

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void* EntryGetFactory(byte* id) => ClapModule.Text(id) == ClapIds.FactoryId ? Factory : null;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static uint FactoryCount(ClapPluginFactory* f) => 2;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static ClapPluginDescriptor* FactoryDescriptor(ClapPluginFactory* f, uint index) => index == 0 ? Factory->Synth : index == 1 ? Factory->Gain : null;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static ClapPluginT* FactoryCreate(ClapPluginFactory* f, ClapHost* host, byte* id)
    {
        var name = ClapModule.Text(id);
        if (name != GainId && name != SynthId) return null;
        var state = (FakeClapState*)NativeMemory.AllocZeroed((nuint)sizeof(FakeClapState));
        state->Kind = name == SynthId ? 1 : 0;
        state->Gain = 1;
        state->Host = host;
        var p = (ClapPluginT*)NativeMemory.AllocZeroed((nuint)sizeof(ClapPluginT));
        p->Desc = state->Kind == 1 ? Factory->Synth : Factory->Gain;
        p->PluginData = state;
        p->Init = &PluginInit; p->Destroy = &PluginDestroy; p->Activate = &PluginActivate; p->Deactivate = &PluginDeactivate;
        p->StartProcessing = &PluginStart; p->StopProcessing = &PluginStop; p->Reset = &PluginReset; p->Process = &PluginProcess;
        p->GetExtension = &PluginExtension; p->OnMainThread = &PluginMainThread;
        Interlocked.Increment(ref Counters[2]);
        *LastSlot = state;
        return p;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static byte PluginInit(ClapPluginT* p) => 1;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void PluginDestroy(ClapPluginT* p)
    {
        var s = State(p);
        if (*LastSlot == s) *LastSlot = null;
        NativeMemory.Free(s);
        NativeMemory.Free(p);
        Interlocked.Increment(ref Counters[3]);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static byte PluginActivate(ClapPluginT* p, double sampleRate, uint minFrames, uint maxFrames)
    {
        var s = State(p);
        if (sampleRate < 8000) return 0;   // a plug-in may refuse a rate
        s->SampleRate = sampleRate; s->MaxFrames = maxFrames; s->Active = 1;
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void PluginDeactivate(ClapPluginT* p) => State(p)->Active = 0;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static byte PluginStart(ClapPluginT* p) { State(p)->Processing = 1; return 1; }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void PluginStop(ClapPluginT* p) => State(p)->Processing = 0;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void PluginReset(ClapPluginT* p) => State(p)->Level = 0;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void PluginMainThread(ClapPluginT* p) => State(p)->MainThreadCalls++;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int PluginProcess(ClapPluginT* p, ClapProcess* proc)
    {
        var s = State(p);
        s->Blocks++;
        if (proc->Transport != null)
        {
            s->TransportFlags = proc->Transport->Flags; s->Tempo = proc->Transport->Tempo; s->BeatsFixed = proc->Transport->SongPosBeats;
            s->TsigNum = proc->Transport->TsigNum; s->TsigDenom = proc->Transport->TsigDenom;
        }
        var events = proc->InEvents;
        for (uint i = 0, n = events->Size(events); i < n; i++)
        {
            var h = events->Get(events, i);
            switch (h->Type)
            {
                case ClapIds.NoteOn: s->NoteOns++; s->LastKey = ((ClapEventNote*)h)->Key; s->LastVelocity = ((ClapEventNote*)h)->Velocity; s->Level = s->LastVelocity * 0.5; break;
                case ClapIds.NoteOff: s->NoteOffs++; s->Level = 0; break;
                case ClapIds.ParamValue:
                    s->ParamEvents++;
                    if (((ClapEventParamValue*)h)->ParamId == GainParam) s->Gain = ((ClapEventParamValue*)h)->Value;
                    break;
                case ClapIds.Midi: s->MidiEvents++; break;
            }
        }
        var frames = (int)proc->FramesCount;
        var output = proc->AudioOutputs[0];
        for (var c = 0; c < (int)output.ChannelCount; c++)
        {
            var o = output.Data32[c];
            if (s->Kind == 1) for (var i = 0; i < frames; i++) o[i] = (float)s->Level;
            else if (proc->AudioInputsCount > 0 && c < (int)proc->AudioInputs[0].ChannelCount)
            {
                var input = proc->AudioInputs[0].Data32[c];
                for (var i = 0; i < frames; i++) o[i] = (float)(input[i] * s->Gain);
            }
        }
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void* PluginExtension(ClapPluginT* p, byte* id) => ClapModule.Text(id) switch
    {
        ClapIds.ExtParams => ParamsExt,
        ClapIds.ExtState => StateExt,
        ClapIds.ExtAudioPorts => PortsExt,
        ClapIds.ExtLatency => LatencyExt,
        ClapIds.ExtTail => TailExt,
        ClapIds.ExtGui => GuiExt,
        _ => null,
    };

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static uint Latency(ClapPluginT* p) => LatencyFrames;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static uint Tail(ClapPluginT* p) => TailFrames;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static uint ParamCount(ClapPluginT* p) => 2;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static byte ParamInfo(ClapPluginT* p, uint index, ClapParamInfo* info)
    {
        if (index > 1) return 0;
        *info = default;
        info->Id = index == 0 ? GainParam : HiddenParam;
        info->Flags = index == 0 ? 1u << 5 : 1u << 2;   // automatable / hidden
        info->Cookie = (void*)(index + 100);
        var name = index == 0 ? "Gain" : "Hidden";
        for (var i = 0; i < name.Length; i++) info->Name[i] = (byte)name[i];
        info->MinValue = 0; info->MaxValue = 2; info->DefaultValue = 1;
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static byte ParamValue(ClapPluginT* p, uint id, double* value)
    {
        if (id != GainParam) return 0;
        *value = State(p)->Gain;
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static byte ParamText(ClapPluginT* p, uint id, double value, byte* buffer, uint capacity) => 0;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static byte ParamParse(ClapPluginT* p, uint id, byte* text, double* value) => 0;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void ParamFlush(ClapPluginT* p, ClapInputEvents* input, ClapOutputEvents* output) { }

    private const uint Magic = 0x4C434654;   // TFCL

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static byte StateSave(ClapPluginT* p, ClapOStream* stream)
    {
        var s = State(p);
        var buffer = stackalloc byte[12];
        *(uint*)buffer = Magic;
        *(double*)(buffer + 4) = s->Gain;
        return stream->Write(stream, buffer, 12) == 12 ? (byte)1 : (byte)0;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static byte StateLoad(ClapPluginT* p, ClapIStream* stream)
    {
        var buffer = stackalloc byte[12];
        var got = 0;
        while (got < 12)
        {
            var n = stream->Read(stream, buffer + got, (ulong)(12 - got));
            if (n <= 0) return 0;
            got += (int)n;
        }
        if (*(uint*)buffer != Magic) return 0;   // a state of another plug-in: rejected
        State(p)->Gain = *(double*)(buffer + 4);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static uint PortCount(ClapPluginT* p, byte isInput) => isInput != 0 ? (State(p)->Kind == 1 ? 0u : 1u) : 1u;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static byte PortGet(ClapPluginT* p, uint index, byte isInput, ClapAudioPortInfo* info)
    {
        *info = default;
        info->Id = 0; info->Flags = 1; info->ChannelCount = 2;
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static byte GuiSupported(ClapPluginT* p, byte* api, byte floating) => floating == 0 && ClapModule.Text(api) == ClapPlugin.WindowApi ? (byte)1 : (byte)0;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static byte GuiCreate(ClapPluginT* p, byte* api, byte floating) { State(p)->GuiCreated = 1; return 1; }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void GuiDestroy(ClapPluginT* p) { State(p)->GuiCreated = 0; State(p)->GuiShown = 0; }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static byte GuiScale(ClapPluginT* p, double scale) => 1;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static byte GuiGetSize(ClapPluginT* p, uint* width, uint* height) { *width = 400; *height = 300; return 1; }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static byte GuiCanResize(ClapPluginT* p) => 1;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static byte GuiAdjust(ClapPluginT* p, uint* width, uint* height) => 1;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static byte GuiSetSize(ClapPluginT* p, uint width, uint height) { State(p)->SizeSet = (int)(width * 10000 + height); return 1; }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static byte GuiSetParent(ClapPluginT* p, ClapWindow* window) { State(p)->Parent = window->Handle; return 1; }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static byte GuiShow(ClapPluginT* p) { State(p)->GuiShown = 1; return 1; }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static byte GuiHide(ClapPluginT* p) { State(p)->GuiShown = 0; return 1; }
}
#endif
