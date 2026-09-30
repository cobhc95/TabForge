namespace TabForge.AudioEngine.Plugins;

/// <summary>A MIDI message at a frame inside the current block.</summary>
public struct BlockMidi
{
    public int Frame;
    public byte Status, Data1, Data2;
}

/// <summary>Transport information passed to plug-ins each block.</summary>
public struct TransportInfo
{
    public double Tempo;
    public double PpqPosition;
    public bool Playing;
    /// <summary>Song time at the start of the block (seconds from the song's start), for audio clips.</summary>
    public double SongSec;
    /// <summary>
    /// RT-04: time signature and bar start (ppq) at the block start, from the song's bar map; <c>Meter.IsValid</c> false when unknown
    /// (then the plug-in gets no time-signature / bar flags).
    /// </summary>
    public TabForge.Audio.Contracts.TransportMeter Meter;
}

/// <summary>
/// One loaded plug-in (VST2, VST3, or a proxy to a plug-in running in its own process). Process is called on the
/// audio thread only and must not allocate; everything else is called on the engine's main thread.
/// </summary>
public interface IPluginInstance : IDisposable
{
    string Path { get; }
    bool IsInstrument { get; }
    bool HasEditor { get; }
    int LatencySamples { get; }

    /// <summary>
    /// Processes one block. <paramref name="input"/> holds the audio to process (effects) or is silent (instruments);
    /// the plug-in writes its output to <paramref name="output"/>. Both are stereo, non-interleaved, length ≥ frames.
    /// </summary>
    void Process(float[][] input, float[][] output, int frames, ReadOnlySpan<BlockMidi> midi, in TransportInfo transport);

    /// <summary>The plug-in's saved state, or null when it has none.</summary>
    byte[]? GetState();
    void SetState(byte[] state);

    /// <summary>Opens the plug-in's editor inside <paramref name="parent"/>; returns its size, or null.</summary>
    (int Width, int Height)? OpenEditor(IntPtr parent);
    void CloseEditor();
    /// <summary>Called ~30 times a second on the main thread while the editor is open.</summary>
    void EditorIdle();

    /// <summary>
    /// Main thread, while nothing processes: switches the plug-in between realtime and offline (render) processing
    /// (VST2: mains off/on, process level 4 on the render workers; VST3: kOffline setup cycle).
    /// </summary>
    void SetOfflineMode(bool offline) { }

    /// <summary>
    /// Main thread, while nothing processes (the audio device is stopped): the device changed its sample rate and / or block
    /// size. The instance keeps its live settings and afterwards processes blocks of up to <paramref name="maxBlock"/> frames at
    /// <paramref name="sampleRate"/> (VST2: stop / mains off / rate / block size / mains on / start; VST3 and isolated plug-ins:
    /// recreated with their current state; GM synth: a new synthesizer with the channel state replayed). Throws when the
    /// plug-in could not follow; it then stays loaded but silent.
    /// </summary>
    void Reconfigure(double sampleRate, int maxBlock) { }

    /// <summary>The plug-in's own audio tail (reverb / delay ring-out) in samples; 0 when it reports none.</summary>
    int TailSamples => 0;

    /// <summary>
    /// Audio thread, valid right after <see cref="Process"/>: the MIDI events the plug-in sent to the host during that block
    /// (VST2 audioMasterProcessEvents). Preallocated buffer; empty when the plug-in cannot report any (VST3 bridge, remote).
    /// </summary>
    ReadOnlySpan<BlockMidi> MidiOut => default;

    /// <summary>Audio input channels the plug-in takes (sidechain on 3/4 needs at least four). VST3 bridge and remote plug-ins: stereo only.</summary>
    int InputChannels => 2;
}
