using System.Text.Json;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine.Midi.Processors;
using TabForge.AudioEngine.Plugins;

namespace TabForge.AudioEngine.Midi;

/// <summary>
/// An ordered list of MIDI processors in front of one instrument. Built (JSON parsed, everything allocated) on the engine
/// thread and swapped into the track chain with Volatile.Write; <see cref="Process"/> runs on the audio thread without
/// allocating or locking. The chain itself tracks every note it lets through, so a transport stop, a panic, or an edit that
/// changes the chain's shape releases every sounding note (no stuck notes, whatever the processors did).
/// </summary>
public sealed class MidiProcessorChain
{
    private readonly IMidiProcessor[] _procs;
    private readonly string[] _types, _json;
    private readonly MidiBuffer _a = new(), _b = new();
    private readonly bool[] _sounding = new bool[2048];
    private int _soundingCount;
    private readonly int _sampleRate;
    private long _clock;
    private bool _wasPlaying, _primed;

    public int Count => _procs.Length;

    private MidiProcessorChain(IMidiProcessor[] procs, string[] types, string[] json, int sampleRate)
    {
        _procs = procs; _types = types; _json = json; _sampleRate = sampleRate;
    }

    /// <summary>Engine thread: builds a chain from the specs (disabled and unknown entries are skipped); null when nothing is left to run.</summary>
    public static MidiProcessorChain? Create(IReadOnlyList<MidiProcSpec> specs, int sampleRate)
    {
        var procs = new List<IMidiProcessor>(); var types = new List<string>(); var jsons = new List<string>();
        foreach (var spec in specs)
        {
            if (!spec.Enabled) continue;
            JsonDocument? doc = null;
            try
            {
                try { doc = spec.ParamsJson.Length == 0 ? null : JsonDocument.Parse(spec.ParamsJson); }
                catch (JsonException) { doc = null; }
                var processor = CreateProcessor(spec.Type, MidiParams.Parse(doc), sampleRate, procs.Count);
                if (processor is null) continue;
                procs.Add(processor); types.Add(spec.Type); jsons.Add(spec.ParamsJson);
            }
            finally { doc?.Dispose(); }
        }
        return procs.Count == 0 ? null : new MidiProcessorChain(procs.ToArray(), types.ToArray(), jsons.ToArray(), sampleRate);
    }

    /// <summary>Known processor type names (shared with the app's catalog).</summary>
    public static IMidiProcessor? CreateProcessor(string type, MidiParams p, int sampleRate, int position)
    {
        var inner = CreateCore(type, p, sampleRate, position);
        if (inner is null || type is not ("channelRoute" or "noteRange" or "transpose" or "noteMap" or "velocity" or "humanize" or "delay"
            or "scaleSnap" or "chord" or "sanitizer" or "noteHold" or "repeater" or "arp" or "noteRandom" or "modalRandom" or "scaleVariation" or "velVariation")) return inner;
        var mode = p.Int("NoteMode", 0, 0, 2);   // 0 all notes (default, also for older files), 1 only these, 2 all except these
        return mode == 0 ? inner : new NoteFilteredProcessor(inner, mode, NoteSetText.Parse(p.String("NoteSet", "")));
    }

    private static IMidiProcessor? CreateCore(string type, MidiParams p, int sampleRate, int position) => type switch
    {
        "channelMap" => new ChannelMapProcessor(p),
        "channelRoute" => new ChannelRouteProcessor(p),
        "noteRange" => new NoteRangeProcessor(p),
        "transpose" => new TransposeProcessor(p),
        "noteMap" => new NoteMapProcessor(p),
        "velocity" => new VelocityProcessor(p),
        "humanize" => new HumanizeProcessor(p, sampleRate),
        "delay" => new DelayProcessor(p, sampleRate),
        "scaleSnap" => new ScaleSnapProcessor(p),
        "chord" => new ChordProcessor(p),
        "choke" => new ChokeProcessor(p),
        "chokeGroup" => new ChokeGroupProcessor(p),
        "sanitizer" => new NoteSanitizerProcessor(p),
        "noteHold" => new NoteHoldProcessor(p),
        "repeater" => new RepeaterProcessor(p),
        "arp" => new ArpeggiatorProcessor(p),
        "noteRandom" => new NoteRandomizerProcessor(p),
        "modalRandom" => new ModalRandomizerProcessor(p),
        "scaleVariation" => new ScaleVariationProcessor(p),
        "velVariation" => new VelocityVariationProcessor(p),
        "lfo" => new LfoProcessor(p),
        "stepSeq" => new StepSequencerProcessor(p),
        "programBank" => new ProgramBankProcessor(p),
        "ccSender" => new CcSenderProcessor(p),
        "ccMapper" => new CcMapperProcessor(p),
        "log" => new LogProcessor(p, position),
        "panic" => new PanicProcessor(p),
        "audioDrum" => new AudioDrumTriggerProcessor(p, sampleRate),
        "audioDucker" => new AudioDuckerProcessor(p, sampleRate),
        "loopSampler" => new LoopSamplerProcessor(p, sampleRate),
        "looper" => new LooperProcessor(p, sampleRate),
        _ => null,
    };

    private bool SameShape(MidiProcessorChain other)
    {
        if (other._types.Length != _types.Length) return false;
        for (var i = 0; i < _types.Length; i++) if (other._types[i] != _types[i]) return false;
        return true;
    }

    /// <summary>
    /// Audio thread: this chain replaces <paramref name="previous"/> (null: none). Same shape: held notes, pending delayed events
    /// and the clock carry over. Different shape: every note the old chain let through is released into <paramref name="output"/> first.
    /// </summary>
    public void TakeOver(MidiProcessorChain? previous, MidiBuffer output)
    {
        if (previous is null) return;
        _clock = previous._clock; _wasPlaying = previous._wasPlaying; _primed = previous._primed;
        if (SameShape(previous))
        {
            for (var i = 0; i < _procs.Length; i++) _procs[i].Adopt(_procs[i] is NoteFilteredProcessor ? previous._procs[i] : NoteFilteredProcessor.Unwrap(previous._procs[i]), _json[i] == previous._json[i]);
            Array.Copy(previous._sounding, _sounding, _sounding.Length);
            _soundingCount = previous._soundingCount;
        }
        else previous.ReleaseAll(output);
    }

    /// <summary>Audio thread: note-off for every note this chain let through and has not released; clears processor state.</summary>
    public void ReleaseAll(MidiBuffer output)
    {
        if (_soundingCount > 0)
            for (var key = 0; key < 2048; key++)
                if (_sounding[key]) { output.Add(0, (byte)(0x80 | (key >> 7)), (byte)(key & 0x7F), 0); _sounding[key] = false; }
        _soundingCount = 0;
        foreach (var p in _procs) p.Reset();
    }

    /// <summary>Audio thread: forget everything without sending anything (the caller already silenced the instrument).</summary>
    public void Reset()
    {
        Array.Clear(_sounding); _soundingCount = 0;
        foreach (var p in _procs) p.Reset();
    }

    /// <summary>Audio thread: runs one block. Appends the result to <paramref name="output"/> (frame-sorted).</summary>
    public void Process(ReadOnlySpan<BlockMidi> input, MidiBuffer output, int frames, in TransportInfo transport, MidiLogRing? log,
        float[]? audioL = null, float[]? audioR = null)
    {
        var started = false; var stopped = false;
        if (_primed) { started = transport.Playing && !_wasPlaying; stopped = !transport.Playing && _wasPlaying; }
        _primed = true; _wasPlaying = transport.Playing;
        if (stopped) ReleaseAll(output);

        var ctx = new MidiContext
        {
            Frames = frames, SampleRate = _sampleRate, Tempo = transport.Tempo, Playing = transport.Playing,
            PlayStarted = started, PlayStopped = stopped, SongSec = transport.SongSec, BlockStart = _clock, Log = log,
        };
        var cur = _a; var next = _b;
        cur.Clear();
        var watching = log is { Watching: true };
        foreach (var e in input)
        {
            cur.Add(e);
            if (watching && e.Status < 0xF0 && (e.Status & 0xF0) == 0x90 && e.Data2 > 0)   // raw note-ons for the window's Learn button (stage 250)
                log!.Write((float)(transport.SongSec + (double)e.Frame / _sampleRate), e.Status, e.Data1, e.Data2, 250);
        }
        for (var i = 0; i < _procs.Length; i++)
        {
            next.Clear();
            if (audioL is not null && audioR is not null && _procs[i] is IAudioAwareProcessor aware)
                aware.ProcessAudio(cur, next, ctx, new Span<float>(audioL, 0, Math.Min(frames, audioL.Length)), new Span<float>(audioR, 0, Math.Min(frames, audioR.Length)));
            else _procs[i].Process(cur, next, ctx);
            if (next.Unsorted) next.Sort();
            (cur, next) = (next, cur);
        }
        for (var i = 0; i < cur.Count; i++)
        {
            ref var e = ref cur.Items[i];
            if (e.Status < 0xF0)
            {
                var kind = e.Status & 0xF0;
                var key = ((e.Status & 0x0F) << 7) | (e.Data1 & 0x7F);
                if (kind == 0x90 && e.Data2 > 0) { if (!_sounding[key]) { _sounding[key] = true; _soundingCount++; } }
                else if (kind is 0x80 or 0x90) { if (_sounding[key]) { _sounding[key] = false; _soundingCount--; } }
                else if (kind == 0xB0 && e.Data1 is 123 or 120) ClearChannel(e.Status & 0x0F);
            }
            output.Add(e);
        }
        _clock += frames;
    }

    private void ClearChannel(int ch)
    {
        for (var n = 0; n < 128; n++) if (_sounding[(ch << 7) | n]) { _sounding[(ch << 7) | n] = false; _soundingCount--; }
    }
}
