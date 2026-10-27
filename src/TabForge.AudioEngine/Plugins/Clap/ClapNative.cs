using System.Runtime.InteropServices;

namespace TabForge.AudioEngine.Plugins.Clap;

// Owns: the CLAP 1.2 C structs the host reads and writes (layouts checked against the official free-audio/clap headers, MIT), as blittable C# structs.
// Does not own: using them (ClapModule, ClapPlugin), the host callbacks (ClapHostCallbacks).
// Tests: ClapHostTests (struct sizes and offsets).
#pragma warning disable CS0649 // fields are written by native code

[StructLayout(LayoutKind.Sequential)]
internal struct ClapVersion
{
    public uint Major, Minor, Revision;
    public static ClapVersion Current => new() { Major = 1, Minor = 2, Revision = 10 };
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ClapPluginEntry
{
    public ClapVersion Version;
    public delegate* unmanaged[Cdecl]<byte*, byte> Init;
    public delegate* unmanaged[Cdecl]<void> Deinit;
    public delegate* unmanaged[Cdecl]<byte*, void*> GetFactory;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ClapPluginFactory
{
    public delegate* unmanaged[Cdecl]<ClapPluginFactory*, uint> GetPluginCount;
    public delegate* unmanaged[Cdecl]<ClapPluginFactory*, uint, ClapPluginDescriptor*> GetPluginDescriptor;
    public delegate* unmanaged[Cdecl]<ClapPluginFactory*, ClapHost*, byte*, ClapPluginT*> CreatePlugin;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ClapPluginDescriptor
{
    public ClapVersion Version;
    public byte* Id, Name, Vendor, Url, ManualUrl, SupportUrl, VersionText, Description;
    public byte** Features;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ClapPluginT
{
    public ClapPluginDescriptor* Desc;
    public void* PluginData;
    public delegate* unmanaged[Cdecl]<ClapPluginT*, byte> Init;
    public delegate* unmanaged[Cdecl]<ClapPluginT*, void> Destroy;
    public delegate* unmanaged[Cdecl]<ClapPluginT*, double, uint, uint, byte> Activate;
    public delegate* unmanaged[Cdecl]<ClapPluginT*, void> Deactivate;
    public delegate* unmanaged[Cdecl]<ClapPluginT*, byte> StartProcessing;
    public delegate* unmanaged[Cdecl]<ClapPluginT*, void> StopProcessing;
    public delegate* unmanaged[Cdecl]<ClapPluginT*, void> Reset;
    public delegate* unmanaged[Cdecl]<ClapPluginT*, ClapProcess*, int> Process;
    public delegate* unmanaged[Cdecl]<ClapPluginT*, byte*, void*> GetExtension;
    public delegate* unmanaged[Cdecl]<ClapPluginT*, void> OnMainThread;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ClapHost
{
    public ClapVersion Version;
    public void* HostData;
    public byte* Name, Vendor, Url, VersionText;
    public delegate* unmanaged[Cdecl]<ClapHost*, byte*, void*> GetExtension;
    public delegate* unmanaged[Cdecl]<ClapHost*, void> RequestRestart;
    public delegate* unmanaged[Cdecl]<ClapHost*, void> RequestProcess;
    public delegate* unmanaged[Cdecl]<ClapHost*, void> RequestCallback;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ClapAudioBuffer
{
    public float** Data32;
    public double** Data64;
    public uint ChannelCount, Latency;
    public ulong ConstantMask;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ClapProcess
{
    public long SteadyTime;
    public uint FramesCount;
    public ClapEventTransport* Transport;
    public ClapAudioBuffer* AudioInputs;
    public ClapAudioBuffer* AudioOutputs;
    public uint AudioInputsCount, AudioOutputsCount;
    public ClapInputEvents* InEvents;
    public ClapOutputEvents* OutEvents;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ClapInputEvents
{
    public void* Ctx;
    public delegate* unmanaged[Cdecl]<ClapInputEvents*, uint> Size;
    public delegate* unmanaged[Cdecl]<ClapInputEvents*, uint, ClapEventHeader*> Get;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ClapOutputEvents
{
    public void* Ctx;
    public delegate* unmanaged[Cdecl]<ClapOutputEvents*, ClapEventHeader*, byte> TryPush;
}

[StructLayout(LayoutKind.Sequential)]
internal struct ClapEventHeader
{
    public uint Size, Time;
    public ushort SpaceId, Type;
    public uint Flags;
}

/// <summary>clap_event_note_t (note on / off / choke / end).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ClapEventNote
{
    public ClapEventHeader Header;
    public int NoteId;
    public short PortIndex, Channel, Key;
    public double Velocity;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ClapEventParamValue
{
    public ClapEventHeader Header;
    public uint ParamId;
    public void* Cookie;
    public int NoteId;
    public short PortIndex, Channel, Key;
    public double Value;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ClapEventMidi
{
    public ClapEventHeader Header;
    public ushort PortIndex;
    public fixed byte Data[3];
}

[StructLayout(LayoutKind.Sequential)]
internal struct ClapEventTransport
{
    public ClapEventHeader Header;
    public uint Flags;
    public long SongPosBeats, SongPosSeconds;
    public double Tempo, TempoInc;
    public long LoopStartBeats, LoopEndBeats, LoopStartSeconds, LoopEndSeconds, BarStart;
    public int BarNumber;
    public ushort TsigNum, TsigDenom;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ClapParamInfo
{
    public uint Id, Flags;
    public void* Cookie;
    public fixed byte Name[256];
    public fixed byte Module[1024];
    public double MinValue, MaxValue, DefaultValue;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ClapAudioPortInfo
{
    public uint Id;
    public fixed byte Name[256];
    public uint Flags, ChannelCount;
    public byte* PortType;
    public uint InPlacePair;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ClapPluginParams
{
    public delegate* unmanaged[Cdecl]<ClapPluginT*, uint> Count;
    public delegate* unmanaged[Cdecl]<ClapPluginT*, uint, ClapParamInfo*, byte> GetInfo;
    public delegate* unmanaged[Cdecl]<ClapPluginT*, uint, double*, byte> GetValue;
    public delegate* unmanaged[Cdecl]<ClapPluginT*, uint, double, byte*, uint, byte> ValueToText;
    public delegate* unmanaged[Cdecl]<ClapPluginT*, uint, byte*, double*, byte> TextToValue;
    public delegate* unmanaged[Cdecl]<ClapPluginT*, ClapInputEvents*, ClapOutputEvents*, void> Flush;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ClapPluginAudioPorts
{
    public delegate* unmanaged[Cdecl]<ClapPluginT*, byte, uint> Count;
    public delegate* unmanaged[Cdecl]<ClapPluginT*, uint, byte, ClapAudioPortInfo*, byte> Get;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ClapPluginState
{
    public delegate* unmanaged[Cdecl]<ClapPluginT*, ClapOStream*, byte> Save;
    public delegate* unmanaged[Cdecl]<ClapPluginT*, ClapIStream*, byte> Load;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ClapOStream
{
    public void* Ctx;
    public delegate* unmanaged[Cdecl]<ClapOStream*, void*, ulong, long> Write;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ClapIStream
{
    public void* Ctx;
    public delegate* unmanaged[Cdecl]<ClapIStream*, void*, ulong, long> Read;
}

/// <summary>clap_plugin_latency_t and clap_plugin_tail_t (one getter each).</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ClapPluginUInt32Query
{
    public delegate* unmanaged[Cdecl]<ClapPluginT*, uint> Get;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ClapWindow
{
    public byte* Api;
    public void* Handle;   // the union: cocoa NSView, win32 HWND, x11 window id
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ClapPluginGui
{
    public delegate* unmanaged[Cdecl]<ClapPluginT*, byte*, byte, byte> IsApiSupported;
    public delegate* unmanaged[Cdecl]<ClapPluginT*, byte**, byte*, byte> GetPreferredApi;
    public delegate* unmanaged[Cdecl]<ClapPluginT*, byte*, byte, byte> Create;
    public delegate* unmanaged[Cdecl]<ClapPluginT*, void> Destroy;
    public delegate* unmanaged[Cdecl]<ClapPluginT*, double, byte> SetScale;
    public delegate* unmanaged[Cdecl]<ClapPluginT*, uint*, uint*, byte> GetSize;
    public delegate* unmanaged[Cdecl]<ClapPluginT*, byte> CanResize;
    public delegate* unmanaged[Cdecl]<ClapPluginT*, void*, byte> GetResizeHints;
    public delegate* unmanaged[Cdecl]<ClapPluginT*, uint*, uint*, byte> AdjustSize;
    public delegate* unmanaged[Cdecl]<ClapPluginT*, uint, uint, byte> SetSize;
    public delegate* unmanaged[Cdecl]<ClapPluginT*, ClapWindow*, byte> SetParent;
    public delegate* unmanaged[Cdecl]<ClapPluginT*, ClapWindow*, byte> SetTransient;
    public delegate* unmanaged[Cdecl]<ClapPluginT*, byte*, void> SuggestTitle;
    public delegate* unmanaged[Cdecl]<ClapPluginT*, byte> Show;
    public delegate* unmanaged[Cdecl]<ClapPluginT*, byte> Hide;
}

/// <summary>The host extensions this host offers: clap_host_gui_t, clap_host_params_t, clap_host_state_t, clap_host_latency_t.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ClapHostGui
{
    public delegate* unmanaged[Cdecl]<ClapHost*, void> ResizeHintsChanged;
    public delegate* unmanaged[Cdecl]<ClapHost*, uint, uint, byte> RequestResize;
    public delegate* unmanaged[Cdecl]<ClapHost*, byte> RequestShow;
    public delegate* unmanaged[Cdecl]<ClapHost*, byte> RequestHide;
    public delegate* unmanaged[Cdecl]<ClapHost*, byte, void> Closed;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ClapHostParams
{
    public delegate* unmanaged[Cdecl]<ClapHost*, uint, void> Rescan;
    public delegate* unmanaged[Cdecl]<ClapHost*, uint, uint, void> Clear;
    public delegate* unmanaged[Cdecl]<ClapHost*, void> RequestFlush;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ClapHostState { public delegate* unmanaged[Cdecl]<ClapHost*, void> MarkDirty; }

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ClapHostLatency { public delegate* unmanaged[Cdecl]<ClapHost*, void> Changed; }

internal static class ClapIds
{
    public const ushort NoteOn = 0, NoteOff = 1, ParamValue = 5, Midi = 10;
    public const uint TransportHasTempo = 1, TransportHasBeats = 2, TransportHasSeconds = 4, TransportHasTimeSignature = 8, TransportIsPlaying = 16;
    public const double BeatTimeFactor = 2147483648.0;
    public const int ProcessError = 0;
    public const string Entry = "clap_entry", FactoryId = "clap.plugin-factory";
    public const string ExtParams = "clap.params", ExtState = "clap.state", ExtGui = "clap.gui", ExtAudioPorts = "clap.audio-ports", ExtLatency = "clap.latency", ExtTail = "clap.tail";
    public const string FeatureInstrument = "instrument", FeatureAudioEffect = "audio-effect";
}
