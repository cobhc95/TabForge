using TabForge.Audio.Contracts;
using TabForge.AudioEngine.Midi;
using TabForge.AudioEngine.Plugins;

namespace TabForge.AudioEngine.Mixing;

/// <summary>
/// One track's signal path: sound (a VST instrument and/or the built-in General MIDI synth) → effects in order
/// (each with wet/dry) → track level and pan. Level and pan come from the track's MIDI volume / pan messages (CC7 /
/// CC10, which already include the mixer groups), applied here so they work with any instrument.
/// All buffers are allocated up front; <see cref="Render"/> does not allocate.
/// </summary>
public sealed class TrackChain : IDisposable
{
    /// <summary>One plug-in of the serial chain (instrument or effect), in chain order.</summary>
    public sealed record Effect(IPluginInstance Plugin, float Wet, int Index, string Pins = "Stereo", float Gain = 1, bool IsInstrument = false)
    {
        /// <summary>Target gain (set from the engine thread, read by the audio thread) and the value reached so far.</summary>
        public volatile float Target = Gain;
        public float Current = Gain;
        /// <summary>Bypassed: stays loaded, but the audio thread does not process it (the signal passes through dry).</summary>
        public volatile bool Bypass;
        /// <summary>The MIDI arriving here also continues to the next plug-in.</summary>
        public volatile bool PassMidi = true;
        /// <summary>The plug-in's own MIDI output events go on to the next plug-in.</summary>
        public volatile bool MidiOutToNext = true;
        /// <summary>Instruments only: the output replaces the chain audio instead of being added to it.</summary>
        public volatile bool Replace;
        /// <summary>This plug-in's MIDI processors (engine thread swaps a whole immutable chain; null: none). They run right before the plug-in.</summary>
        public volatile MidiProcessorChain? Processors;
        /// <summary>Audio thread: the processor chain currently running (note-off pairing across swaps and bypass).</summary>
        public MidiProcessorChain? ProcActive;
        /// <summary>Sidechain source slot (post-fader audio into inputs 3/4); -1 none.</summary>
        public volatile int SideSlot = -1;
        /// <summary>Slot whose chain input receives this plug-in's MIDI output (next block); -1 none.</summary>
        public volatile int ForwardSlot = -1;
        /// <summary>Audio thread: MIDI output captured for forwarding this block.</summary>
        public readonly BlockMidi[] Fwd = new BlockMidi[256];
        public int FwdCount;
        /// <summary>Instruments: automatic pitch match, an implicit transpose right before the plug-in (after its MIDI processors).</summary>
        public readonly PitchMatch.Transposer AutoPitch = new();
        /// <summary><see cref="Pins"/> (the UI label, kept for persistence) mapped once, so the audio thread never compares strings.</summary>
        public readonly PinMode Mode = PinModes.Parse(Pins);
        /// <summary>
        /// Set by the audio thread: the plug-in produced a non-finite sample (NaN / infinity). That block's output was discarded and the
        /// plug-in is skipped from then on (its input passes through, as when bypassed) until it is switched on again or the chain is rebuilt.
        /// </summary>
        public volatile bool Misbehaved;
        /// <summary>Engine main thread: <see cref="Misbehaved"/> was reported to TabForge (once).</summary>
        public bool MisbehaveReported;
    }

    /// <summary>
    /// Incremented (lock-free) by an audio thread whenever a stage or a chain output turns non-finite; the engine main loop compares it
    /// with the value it last saw and only then looks for what to report (<see cref="CollectMisbehaved"/>).
    /// </summary>
    public static int MisbehaveSignal;

    /// <summary>MIDI messages dropped because a chain's event buffer was full (every chain of the process; the metrics log takes and resets it).</summary>
    public static long MidiDropped;

    private volatile bool _poisoned;
    private bool _poisonReported;

    /// <summary>
    /// Engine main thread: plug-ins of this chain that newly misbehaved (chain index, path), plus index -1 when the chain's own output was
    /// non-finite with every plug-in fine (General MIDI synth, clips or input); each is reported once.
    /// </summary>
    public void CollectMisbehaved(List<(int Index, string Path)> into)
    {
        foreach (var e in _effects)
            if (e.Misbehaved && !e.MisbehaveReported) { e.MisbehaveReported = true; into.Add((e.Index, e.Plugin.Path)); }
        if (_poisoned && !_poisonReported) { _poisonReported = true; into.Add((-1, _midiSynth is not null ? _midiSynth.Path : "track audio")); }
    }

    /// <summary>Audio thread, allocation-free: true when every sample of the stereo block is finite (x - x is 0, or NaN for NaN / ±inf).</summary>
    private static bool Finite(float[][] block, int frames)
    {
        var l = block[0]; var r = block[1];
        var sum = 0f;
        for (var i = 0; i < frames; i++) sum += (l[i] - l[i]) + (r[i] - r[i]);
        return sum == 0f;
    }

    /// <summary>A pitch measurement in progress (engine thread sets / clears; audio thread runs it). Null: none.</summary>
    public volatile PitchProbe? Probe;
    private readonly BlockMidi[] _xpose = new BlockMidi[2048];

    /// <summary>Any thread: the automatic pitch-match transpose of the instrument at a chain index.</summary>
    public void SetAutoPitch(int index, int semitones)
    {
        foreach (var e in _effects) if (e.Index == index && e.IsInstrument) e.AutoPitch.Shift = semitones;
    }

    /// <summary>Audio thread: the stream through an instrument's automatic transpose, into a scratch list (the stream passed on stays untransposed).</summary>
    private ReadOnlySpan<BlockMidi> AutoPitched(Effect e, ReadOnlySpan<BlockMidi> input)
    {
        var n = 0;
        foreach (var m in input)
        {
            if (n >= _xpose.Length) break;
            var status = m.Status; var d1 = m.Data1;
            if (e.AutoPitch.Map(ref status, ref d1, m.Data2)) _xpose[n++] = new BlockMidi { Frame = m.Frame, Status = status, Data1 = d1, Data2 = m.Data2 };
        }
        return new ReadOnlySpan<BlockMidi>(_xpose, 0, n);
    }

    /// <summary>Every plug-in stage in chain order (engine thread sets processors and links on them).</summary>
    public IReadOnlyList<Effect> Effects => _effects;

    /// <summary>Bus or master chain: audio comes from the summed input, fader at unity.</summary>
    public bool IsBus => _slot >= MixEngine.BusBase;

    /// <summary>Post-fader output of the last block (sidechain source, stems).</summary>
    public float[] PostL { get; }
    public float[] PostR { get; }
    private readonly float[][] _side4 = new float[4][];
    private bool _unsorted;

    /// <summary>Live bypass (any thread): the plug-in stays loaded, only its processing stops.</summary>
    public void SetPluginBypass(int index, bool bypassed)
    {
        foreach (var e in _effects)
            if (e.Index == index)
            {
                e.Bypass = bypassed;
                if (!bypassed) { e.Misbehaved = false; e.MisbehaveReported = false; }   // switched on again: it gets another chance
                if (!bypassed && e.IsInstrument) _panic = true;
            }
    }

    private volatile bool _synthOn = true, _synthSilence;
    private readonly BlockMidi[] _synthStop = new BlockMidi[32];
    /// <summary>Set when the General MIDI synth is switched back on: the mixer replays the slot's channel state (program, controllers) into it.</summary>
    public volatile bool Reprime;

    /// <summary>
    /// Live (any thread): the General MIDI synth sounds or not. Off costs nothing (it is skipped); its notes are released first.
    /// No rebuild, so GM taking over from a bypassed instrument (or the chain switched off) keeps the timing and every plug-in editor.
    /// </summary>
    public void SetSynthOn(bool on)
    {
        if (_synthOn == on) return;
        _synthOn = on;
        if (on) Reprime = true; else _synthSilence = true;
    }

    /// <summary>Changes a plug-in's output volume live; the audio thread ramps to it per block (no zipper noise).</summary>
    public void SetPluginGain(int index, float gain)
    {
        foreach (var e in _effects) if (e.Index == index) e.Target = gain;
    }

    /// <summary>Engine thread: the chain id (PluginSpec.Id) of the plug-in at a chain index, or null.</summary>
    public string? IdAt(int index)
    {
        foreach (var e in _effects)
            if (e.Index == index) foreach (var (id, p) in ById) if (ReferenceEquals(p, e.Plugin)) return id;
        return null;
    }

    /// <summary>Live serial-chain options of one plug-in (any thread): bit 0 pass incoming MIDI on, bit 1 send its MIDI output on, bit 2 instrument audio replaces.</summary>
    public void SetPluginWiring(int index, int flags)
    {
        foreach (var e in _effects)
            if (e.Index == index) { e.PassMidi = (flags & 1) != 0; e.MidiOutToNext = (flags & 2) != 0; e.Replace = (flags & 4) != 0; }
    }

    /// <summary>Scales _b by a linear ramp from <paramref name="from"/> to <paramref name="to"/> across the block.</summary>
    private void ScaleRamp(int frames, float from, float to)
    {
        if (from == 1 && to == 1) return;
        var step = (to - from) / frames;
        for (var c = 0; c < 2; c++)
        {
            var g = from; var b = _b[c];
            for (var i = 0; i < frames; i++) { b[i] *= g; g += step; }
        }
    }

    private readonly int _slot;
    private readonly int _instrumentIndex;
    // MIDI stream flowing down the chain: current stage input and the next stage's input (swapped per stage).
    private BlockMidi[] _cur = new BlockMidi[2048], _nxt = new BlockMidi[2048];
    private int _curCount;
    private readonly IPluginInstance? _midiSynth;
    private readonly Effect[] _effects;
    private readonly float[][] _a, _b, _silence;
    private readonly BlockMidi[] _events = new BlockMidi[1024];
    private int _eventCount;
    private float _volume = 100 / 127f, _pan = 0.5f;
    private volatile bool _panic;
    /// <summary>The track's audio clips (replaced as a whole by the main thread).</summary>
    public volatile Audio.ClipPlayer[] Clips = Array.Empty<Audio.ClipPlayer>();
    /// <summary>The song owner whose position drives <see cref="Clips"/> (0 .. <see cref="SongOwners.Max"/> - 1).</summary>
    public volatile int ClipOwner;
    /// <summary>Input monitoring: -1 off, 0 = input 1, 1 = input 2, 2 = inputs 1+2.</summary>
    public volatile int ArmMode = -1;
    /// <summary>Play the armed input through the chain (off: it is only metered and recorded).</summary>
    public volatile bool Monitor = true;
    // Audio (clips, input) is scaled so the default track volume (104, GP step 13) plays it at its own level.
    private const float AudioUnity = (127f / 104f) * (127f / 104f);
    private readonly float[] _inL, _inR;

    /// <summary>Track level and pan from the mixer (0..127 each).</summary>
    public void SetMix(int volume, int pan) { _volume = Math.Clamp(volume, 0, 127) / 127f; _pan = Math.Clamp(pan, 0, 127) / 127f; Silent = volume <= 0; }

    /// <summary>
    /// The mixer's mute gate: set by <see cref="SetMix"/> with level 0 (what the client sends for a muted / not-soloed track), cleared by any other level.
    /// A MIDI volume message (CC7) at a seek, loop wrap or restart re-primes <c>_volume</c> but can never lift this, so a muted track stays silent
    /// whatever its source (clips, input, GM synth, plug-in).
    /// </summary>
    public volatile bool Silent;

    /// <summary>Current level and pan (0..127), as CC7 / CC10 in the MIDI stream may have changed them.</summary>
    public (int Volume, int Pan) GetMix() => ((int)MathF.Round(_volume * 127f), (int)MathF.Round(_pan * 127f));

    /// <summary>Offline render: >= 0 selects this render worker's own crash breadcrumb instead of the shared single-slot one.</summary>
    public int CrumbWorker = -1;

    private void Enter(SharedBlock shared, int index, string path)
    {
        if (CrumbWorker >= 0) shared.EnterRender(CrumbWorker, _slot, index, path); else shared.EnterPlugin(_slot, index, path);
    }

    private void Leave(SharedBlock shared)
    {
        if (CrumbWorker >= 0) shared.LeaveRender(CrumbWorker); else shared.LeavePlugin();
    }

    /// <summary>Latency of what is processing now (instrument + effects that are not bypassed), in samples: the offline render's plug-in delay compensation.</summary>
    public int LatencySamples
    {
        get
        {
            var total = 0;
            foreach (var e in _effects) if (!e.Bypass) total += Math.Max(0, e.Plugin.LatencySamples);
            return total;
        }
    }

    /// <summary>The longest tail any plug-in of the chain reports, in samples.</summary>
    public int TailSamples
    {
        get
        {
            var tail = 0;
            foreach (var p in Plugins) { try { tail = Math.Max(tail, p.TailSamples); } catch (Exception) { } }
            return tail;
        }
    }
    private float _peak, _inputPeak;

    public int Slot => _slot;
    public IReadOnlyList<IPluginInstance> Plugins { get; }
    /// <summary>Plug-ins by their chain id (see PluginSpec.Id).</summary>
    public Dictionary<string, IPluginInstance> ById { get; } = new();
    /// <summary>Instances handed on to the chain that replaces this one: not disposed with it.</summary>
    public HashSet<IPluginInstance> Kept { get; } = new(ReferenceEqualityComparer.Instance);
    public IPluginInstance? MidiSynth => _midiSynth;

    /// <param name="effects">Every plug-in (instruments and effects) in chain order.</param>
    public TrackChain(int slot, int instrumentIndex, IPluginInstance? midiSynth, IReadOnlyList<Effect> effects, int maxBlock)
    {
        _slot = slot;
        _instrumentIndex = instrumentIndex;
        _midiSynth = midiSynth;
        _effects = effects.ToArray();
        _a = new[] { new float[maxBlock], new float[maxBlock] };
        _b = new[] { new float[maxBlock], new float[maxBlock] };
        _silence = new[] { new float[maxBlock], new float[maxBlock] };
        _inL = new float[maxBlock];
        _inR = new float[maxBlock];
        _wired = new[] { new float[maxBlock], new float[maxBlock] };   // Never allocated lazily on the audio thread
        PostL = new float[maxBlock];
        PostR = new float[maxBlock];
        Plugins = _effects.Select(e => e.Plugin).ToList();
    }

    /// <summary>Chain index of the VST instrument (-1 when there is none).</summary>
    public int InstrumentIndex => _instrumentIndex;

    // Instrument MIDI input routing, one packed int: (source + 2) << 16 | channel mask. Source -1 = this track's own
    // MIDI (default), -2 = none, >= 0 = another track's slot. Written by the engine thread, read by the audio thread.
    private volatile int _route = (1 << 16) | 0xFFFF;
    private readonly BlockMidi[] _instEvents = new BlockMidi[1024];
    private int _instCount;

    // Scratch output of the per-plug-in MIDI processors (used by one stage at a time, audio thread).
    private readonly MidiBuffer _procOut = new(2048);
    /// <summary>Log of the MIDI passing the log processors, read by the engine thread while the UI watches.</summary>
    public MidiLogRing MidiLog { get; } = new();

    /// <summary>Any thread: where the instrument's MIDI comes from (see <see cref="_route"/>) and which channels pass (bit n = channel n+1).</summary>
    public void SetRoute(int source, int channelMask) => _route = ((Math.Max(source, -2) + 2) << 16) | (channelMask & 0xFFFF);

    /// <summary>Audio thread: MIDI from another track that routes to this chain's instrument (already known to come from the chosen source).</summary>
    public void AddRouted(int frame, byte status, byte data1, byte data2)
    {
        var route = _route;
        if ((route >> 16) - 2 < 0) return;   // this chain does not take another track's MIDI
        AddInstrument(route, frame, status, data1, data2);
    }

    private void AddInstrument(int route, int frame, byte status, byte data1, byte data2)
    {
        if (status < 0xF0 && (route & (1 << (status & 0x0F))) == 0) return;   // channel filter (system messages always pass)
        if (_instCount < _instEvents.Length) _instEvents[_instCount++] = new BlockMidi { Frame = frame, Status = status, Data1 = data1, Data2 = data2 };
        else Interlocked.Increment(ref MidiDropped);
    }

    /// <summary>Audio thread: queues a MIDI message for the next <see cref="Render"/>.</summary>
    public void AddEvent(int frame, byte status, byte data1, byte data2)
    {
        var kind = status & 0xF0;
        // The General MIDI synth handles volume / pan itself, exactly like the Windows synth; a VST instrument gets
        // them as the chain's level and pan instead (many instruments ignore CC7 / CC10).
        if (kind == 0xB0 && data1 == 7) { _volume = data2 / 127f; return; }
        if (kind == 0xB0 && data1 == 10) { _pan = data2 / 127f; return; }
        if (_eventCount < _events.Length) _events[_eventCount++] = new BlockMidi { Frame = frame, Status = status, Data1 = data1, Data2 = data2 };
        else Interlocked.Increment(ref MidiDropped);
        var route = _route;
        if ((route >> 16) == 1) AddInstrument(route, frame, status, data1, data2);   // own MIDI
    }

    /// <summary>Audio thread: MIDI forwarded from another chain's plug-in output (delivered for this block; CC7 / CC10 are not taken as the track level).</summary>
    public void AddForwarded(int frame, byte status, byte data1, byte data2)
    {
        if (_eventCount < _events.Length) _events[_eventCount++] = new BlockMidi { Frame = frame, Status = status, Data1 = data1, Data2 = data2 };
        else Interlocked.Increment(ref MidiDropped);
        var route = _route;
        if ((route >> 16) == 1) AddInstrument(route, frame, status, data1, data2);
        _unsorted = true;
    }

    /// <summary>Audio thread, after every chain rendered: hands captured plug-in MIDI output to the destination chains (heard next block).</summary>
    public void DeliverForwards(TrackChain?[] chains)
    {
        foreach (var e in _effects)
        {
            var count = e.FwdCount;
            if (count == 0) continue;
            e.FwdCount = 0;
            var dest = e.ForwardSlot;
            if (dest < 0 || dest >= chains.Length || chains[dest] is not { } target || ReferenceEquals(target, this)) continue;
            for (var i = 0; i < count; i++) target.AddForwarded(e.Fwd[i].Frame, e.Fwd[i].Status, e.Fwd[i].Data1, e.Fwd[i].Data2);
        }
    }

    /// <summary>Stable insertion sort by frame (small lists, no allocation).</summary>
    private static void SortByFrame(BlockMidi[] items, int count)
    {
        for (var i = 1; i < count; i++)
        {
            var x = items[i]; var j = i - 1;
            while (j >= 0 && items[j].Frame > x.Frame) { items[j + 1] = items[j]; j--; }
            items[j + 1] = x;
        }
    }

    /// <summary>
    /// Audio thread: runs one stage's MIDI processors on the current stream (_cur), in place. A bypassed stage runs none, and a
    /// processor list that goes away (bypass, removed) first releases every note it let through, so nothing hangs.
    /// Returns true when the stream went through processors (or released notes).
    /// </summary>
    private bool RunProcessors(Effect e, int frames, in TransportInfo transport)
    {
        var wanted = e.Bypass || e.Misbehaved ? null : e.Processors;
        if (wanted is null && e.ProcActive is null) return false;
        _procOut.Clear();
        if (!ReferenceEquals(wanted, e.ProcActive))
        {
            if (wanted is null) e.ProcActive!.ReleaseAll(_procOut); else wanted.TakeOver(e.ProcActive, _procOut);
            e.ProcActive = wanted;
        }
        var input = new ReadOnlySpan<BlockMidi>(_cur, 0, _curCount);
        if (e.ProcActive is not null) e.ProcActive.Process(input, _procOut, frames, transport, MidiLog, _a[0], _a[1]);
        else foreach (var m in input) _procOut.Add(m);
        if (_procOut.Unsorted) _procOut.Sort();
        _curCount = Math.Min(_procOut.Count, _cur.Length);
        _procOut.Span[.._curCount].CopyTo(_cur);
        return true;
    }

    /// <summary>
    /// Audio thread, no allocation: the block's queue after a panic = all notes off + all sound off on every channel at frame 0, then the
    /// program changes, controllers and pitch bends that were queued (notes and other messages are dropped).
    /// </summary>
    internal static void PanicKeepingState(BlockMidi[] buffer, ref int count)
    {
        var kept = 0;
        for (var i = 0; i < count; i++)
        {
            var e = buffer[i];
            var kind = e.Status & 0xF0;
            if (kind == 0xC0 || kind == 0xE0 || (kind == 0xB0 && e.Data1 < 120)) buffer[kept++] = e;
        }
        const int PanicEvents = 32;
        if (buffer.Length < PanicEvents) { count = 0; return; }
        if (kept > buffer.Length - PanicEvents) kept = buffer.Length - PanicEvents;
        Array.Copy(buffer, 0, buffer, PanicEvents, kept);   // overlapping copy is handled
        for (var ch = 0; ch < 16; ch++)
        {
            buffer[2 * ch] = new BlockMidi { Frame = 0, Status = (byte)(0xB0 | ch), Data1 = 123 };
            buffer[2 * ch + 1] = new BlockMidi { Frame = 0, Status = (byte)(0xB0 | ch), Data1 = 120 };
        }
        count = kept + PanicEvents;
    }

    /// <summary>Any thread: silence every note at the next block.</summary>
    public void Panic() => _panic = true;

    /// <summary>Audio thread: silence now, dropping the notes this block holds so far (events added after this call are kept).</summary>
    public void PanicNow()
    {
        _panic = false;
        _unsorted = false;
        foreach (var e in _effects) e.AutoPitch.Reset();
        foreach (var e in _effects) e.ProcActive?.Reset();   // held notes and delayed events are gone with the panic
        // The panic silences notes but must not eat channel state: a song start is "reset, then program / volume / bend setup", and the
        // setup travels by the shared ring, so the panic can land in the block that already holds the setup. Dropping it left the synth on
        // piano (GM program 0) until the setup was sent again (the first cold play).
        PanicKeepingState(_events, ref _eventCount);
        PanicKeepingState(_instEvents, ref _instCount);
    }

    /// <summary>Audio thread: renders one block and adds it into <paramref name="mixL"/> / <paramref name="mixR"/>.</summary>
    /// <param name="peers">Every chain by slot (sidechain sources); null: no sidechain.</param>
    /// <param name="busL">Bus / master chains: the summed input audio (added before the plug-ins, at unity).</param>
    public void Render(float[] mixL, float[] mixR, int offset, int frames, in TransportInfo transport, SharedBlock shared, Audio.InputBlock? monitor = null,
        TrackChain?[]? peers = null, float[]? busL = null, float[]? busR = null)
    {
        if (_panic) PanicNow();
        if (_unsorted) { SortByFrame(_events, _eventCount); SortByFrame(_instEvents, _instCount); _unsorted = false; }
        // The stream entering the plug-in list (routed, channel-filtered); each plug-in's MIDI processors run right before it.
        _curCount = Math.Min(_instCount, _cur.Length);
        new ReadOnlySpan<BlockMidi>(_instEvents, 0, _curCount).CopyTo(_cur);
        // Rule for the General MIDI synth: when the first slot is an instrument, its processors run first and the synth hears
        // that processed stream (if it takes the track's own MIDI); otherwise the synth hears the track's raw MIDI.
        var firstDone = _effects.Length > 0 && _effects[0].IsInstrument;
        var firstProcessed = firstDone && RunProcessors(_effects[0], frames, transport);
        var midi = firstProcessed && (_route >> 16) == 1 ? new ReadOnlySpan<BlockMidi>(_cur, 0, _curCount) : new ReadOnlySpan<BlockMidi>(_events, 0, _eventCount);
        _a[0].AsSpan(0, frames).Clear(); _a[1].AsSpan(0, frames).Clear();

        if (_midiSynth is not null && _synthOn)
        {
            Enter(shared, -1, "General MIDI synth");
            _midiSynth.Process(_silence, _a, frames, midi, transport);
        }
        else if (_midiSynth is not null && _synthSilence)
        {
            // Just switched off: one last block that ends its notes (output discarded), then it is skipped.
            _synthSilence = false;
            for (var ch = 0; ch < 16; ch++)
            {
                _synthStop[ch * 2] = new BlockMidi { Frame = 0, Status = (byte)(0xB0 | ch), Data1 = 123 };
                _synthStop[ch * 2 + 1] = new BlockMidi { Frame = 0, Status = (byte)(0xB0 | ch), Data1 = 120 };
            }
            Enter(shared, -1, "General MIDI synth");
            _midiSynth.Process(_silence, _a, frames, _synthStop, transport);
            _a[0].AsSpan(0, frames).Clear(); _a[1].AsSpan(0, frames).Clear();
        }
        _eventCount = 0; _instCount = 0;
        if (busL is not null && busR is not null)
            for (var i = 0; i < frames; i++) { _a[0][i] += busL[i]; _a[1][i] += busR[i]; }

        // Audio: clips at the song position, and the monitored input; they go through the effects too.
        var clips = Clips;
        var arm = ArmMode;
        if (clips.Length > 0 || arm >= 0)
        {
            Array.Clear(_inL, 0, frames); Array.Clear(_inR, 0, frames);
            foreach (var clip in clips) clip.Mix(_inL, _inR, frames, transport.SongSec, transport.Playing);
            if (arm >= 0 && monitor is not null && monitor.Frames >= frames)
            {
                // The block is shared by every armed chain (read once per mixer block): read-only here.
                var inL = monitor.L; var inR = monitor.R;
                var inputPeak = 0f;
                var monitoring = Monitor;
                for (var i = 0; i < frames; i++)
                {
                    var li = inL[i]; var ri = inR[i];
                    var (ml, mr) = arm switch { 1 => (ri, ri), 2 => (li, ri), _ => (li, li) };
                    if (monitoring) { _inL[i] += ml; _inR[i] += mr; }
                    inputPeak = MathF.Max(inputPeak, MathF.Max(MathF.Abs(ml), MathF.Abs(mr)));
                }
                _inputPeak = MathF.Max(inputPeak, _inputPeak * 0.9f);
                shared.SetInputPeak(_slot, _inputPeak);
            }
            for (var i = 0; i < frames; i++) { _a[0][i] += _inL[i] * AudioUnity; _a[1][i] += _inR[i] * AudioUnity; }
        }

        // Serial chain: each plug-in gets the audio and MIDI coming out of the previous one.
        for (var k = 0; k < _effects.Length; k++)
        {
            var effect = _effects[k];
            if (k > 0 || !firstDone) RunProcessors(effect, frames, transport);
            if (effect.Bypass || effect.Misbehaved) continue;
            Enter(shared, effect.Index, effect.Plugin.Path);
            var midiIn = new ReadOnlySpan<BlockMidi>(_cur, 0, _curCount);
            var input = effect.IsInstrument ? _silence : Wire(effect.Mode, frames);
            // Sidechain: the source track's post-fader audio on inputs 3/4 (VST2 with at least four inputs).
            var side = effect.SideSlot;
            if (side >= 0 && !effect.IsInstrument && peers is not null && side < peers.Length && peers[side] is { } source
                && !ReferenceEquals(source, this) && effect.Plugin.InputChannels >= 4)
            {
                _side4[0] = input[0]; _side4[1] = input[1]; _side4[2] = source.PostL; _side4[3] = source.PostR;
                input = _side4;
            }
            var probe = effect.IsInstrument ? Probe : null;
            if (probe is not null && probe.Index != effect.Index) probe = null;
            var plugMidi = probe is not null ? probe.Events(frames) : effect.IsInstrument && effect.AutoPitch.Active ? AutoPitched(effect, midiIn) : midiIn;
            effect.Plugin.Process(input, _b, frames, plugMidi, transport);
            if (!Finite(_b, frames))
            {
                // A NaN / infinity would poison the bus, the master, the meters and recorded stems. Discard the block,
                // skip the plug-in from now on (its input passes on) and let the main loop report it once.
                effect.Misbehaved = true;
                Interlocked.Increment(ref MisbehaveSignal);
                continue;
            }
            if (probe is not null)
            {
                // Measuring: the output goes to the probe's capture, never to the mix.
                probe.Take(_b, frames);
                _b[0].AsSpan(0, frames).Clear(); _b[1].AsSpan(0, frames).Clear();
            }
            var effectTarget = effect.Target;
            ScaleRamp(frames, effect.Current, effectTarget);
            effect.Current = effectTarget;

            // Next stage's MIDI: this plug-in's own output merged with the incoming stream passed through.
            var produced = effect.Plugin.MidiOut;
            if (effect.ForwardSlot >= 0 && produced.Length > 0)
            {
                var n = Math.Min(produced.Length, effect.Fwd.Length);
                produced[..n].CopyTo(effect.Fwd);
                effect.FwdCount = n;
            }
            var outSpan = effect.MidiOutToNext ? produced : default;
            var pass = effect.PassMidi ? midiIn : default;
            if (outSpan.Length > 0 || pass.Length != _curCount)
            {
                _curCount = MergeMidi(_nxt, pass, outSpan);
                (_cur, _nxt) = (_nxt, _cur);
            }

            if (effect.IsInstrument)
            {
                if (effect.Replace) { _b[0].AsSpan(0, frames).CopyTo(_a[0]); _b[1].AsSpan(0, frames).CopyTo(_a[1]); }
                else for (var c = 0; c < 2; c++) { var a = _a[c]; var b = _b[c]; for (var i = 0; i < frames; i++) a[i] += b[i]; }
                continue;
            }
            var wet = effect.Wet;
            if (wet >= 0.999f) { _b[0].AsSpan(0, frames).CopyTo(_a[0]); _b[1].AsSpan(0, frames).CopyTo(_a[1]); }
            else
            {
                var dry = 1 - wet;
                for (var c = 0; c < 2; c++) for (var i = 0; i < frames; i++) _a[c][i] = _a[c][i] * dry + _b[c][i] * wet;
            }
        }
        Leave(shared);

        // General MIDI volume curve (level²) and constant-power pan. The General MIDI synth keeps its own default
        // level (CC7 100), so its output is scaled relative to that: identical to the Windows synth at any volume.
        // Bus and master chains are pure inserts at unity: group / master level and pan are applied once, in each track's level.
        float gl = 1, gr = 1;
        if (!IsBus)
        {
            var level = Silent ? 0f : _volume * _volume * (_midiSynth is null || !_synthOn ? 1f : 1.6129f); // (127/100)²
            var angle = _pan * MathF.PI / 2;
            gl = level * MathF.Cos(angle) * 1.4142135f;
            gr = level * MathF.Sin(angle) * 1.4142135f;
        }
        var l = _a[0]; var r = _a[1];
        var postL = PostL; var postR = PostR;
        var peak = 0f;
        var poison = 0f;   // x - x is 0 for finite x, NaN otherwise (branch-free)
        for (var i = 0; i < frames; i++)
        {
            var ol = l[i] * gl; var or = r[i] * gr;
            postL[i] = ol; postR[i] = or;
            poison += (ol - ol) + (or - or);
            peak = MathF.Max(peak, MathF.Max(MathF.Abs(ol), MathF.Abs(or)));
        }
        if (poison != 0f)
        {
            // Non-finite with every plug-in fine (GM synth, clip, input): this chain's block is silenced, never summed; reported once.
            Array.Clear(postL, 0, frames); Array.Clear(postR, 0, frames);
            peak = 0;
            if (!_poisoned) { _poisoned = true; Interlocked.Increment(ref MisbehaveSignal); }
        }
        else
            for (var i = 0; i < frames; i++) { mixL[offset + i] += postL[i]; mixR[offset + i] += postR[i]; }
        if (arm < 0 && _inputPeak != 0) { _inputPeak = 0; shared.SetInputPeak(_slot, 0); }
        _peak = MathF.Max(peak, _peak * 0.9f); // short hold so meters are readable
        shared.SetPeak(_slot, _peak);
    }

    /// <summary>Merges two frame-ordered event lists into <paramref name="dst"/> (first list wins ties); returns the count. No allocation.</summary>
    private static int MergeMidi(BlockMidi[] dst, ReadOnlySpan<BlockMidi> first, ReadOnlySpan<BlockMidi> second)
    {
        int i = 0, j = 0, n = 0;
        while (n < dst.Length && (i < first.Length || j < second.Length))
            dst[n++] = j >= second.Length || (i < first.Length && first[i].Frame <= second[j].Frame) ? first[i++] : second[j++];
        return n;
    }

    private readonly float[][] _wired;

    /// <summary>The effect's input as its pin wiring asks (an in+out pin connector, simplified). No allocation, no string compare.</summary>
    private float[][] Wire(PinMode mode, int frames)
    {
        if (mode == PinMode.Stereo) return _a;
        var l = _a[0].AsSpan(0, frames); var r = _a[1].AsSpan(0, frames);
        var wl = _wired[0].AsSpan(0, frames); var wr = _wired[1].AsSpan(0, frames);
        switch (mode)
        {
            case PinMode.Mono:
                for (var i = 0; i < frames; i++) { var m = (l[i] + r[i]) * 0.5f; wl[i] = m; wr[i] = m; }
                break;
            case PinMode.LeftOnly: l.CopyTo(wl); l.CopyTo(wr); break;
            case PinMode.RightOnly: r.CopyTo(wl); r.CopyTo(wr); break;
            default: r.CopyTo(wl); l.CopyTo(wr); break;   // Swap
        }
        return _wired;
    }

    public void Dispose()
    {
        Audio.DiskStreamer.Unregister(Clips);
        foreach (var clip in Clips) clip.Dispose();
        if (_midiSynth is not null && !Kept.Contains(_midiSynth)) _midiSynth.Dispose();
        foreach (var e in _effects) if (!Kept.Contains(e.Plugin)) e.Plugin.Dispose();
    }
}

/// <summary>An effect's input pin wiring: what <see cref="PluginSpec.Pins"/> says, as a value the audio thread can switch on.</summary>
public enum PinMode : byte { Stereo, Mono, LeftOnly, RightOnly, Swap }

public static class PinModes
{
    /// <summary>The UI label (persisted in the song / preset) to its mode; anything unknown is plain stereo.</summary>
    public static PinMode Parse(string? pins) => pins switch
    {
        "Mono (L+R)" => PinMode.Mono,
        "Left only" => PinMode.LeftOnly,
        "Right only" => PinMode.RightOnly,
        "Swap L/R" => PinMode.Swap,
        _ => PinMode.Stereo,
    };
}
