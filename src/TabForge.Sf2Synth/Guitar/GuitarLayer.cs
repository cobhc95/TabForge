namespace TabForge.Sf2Synth.Guitar;

// Owns: the modelled guitar/bass layer (GM programs 24-37, never the drum channel): per-channel MIDI state, the string pool
//   (six strings per channel, steal oldest), legato (a note starting while one other note of the channel is held within a few
//   semitones moves that string: hammer, pull-off or slide; undone if the old note is not released within the window, CC68
//   forces it), one GuitarBus per channel, the user's amp/cab swap (LoadRig) and the stereo mix it adds.
// Does not own: the SoundFont sound of every other channel (Synthesizer), DSP (PluckedString, GuitarBus), values (Voicings), files (GuitarRig).
// Tests: GuitarLayerTests.
public sealed class GuitarLayer : ILayer
{
    public const int Chunk = 64, Strings = 24;
    private const double TailSeconds = 0.3, LegatoAge = 0.03, LegatoWindow = 0.04, SlideSeconds = 0.06;
    private const int LegatoMax = 5, SlideFrom = 3;
    private const float Hammer = 0.08f, Pull = 0.12f;

    private sealed class Ch
    {
        public int Program, Volume = 100, Expression = 127, Pan = 64, Bend = 8192, RpnMsb = 127, RpnLsb = 127, Tail, Configured = -1, From = -1, To, Vel;
        public double BendRange = 2;
        public long Due;
        public bool Sustain, Retune, Marked, Legato;
        public PluckedString? Moved;
        public GuitarBus Bus = null!;
        public float[] Mono = null!;
        public double BendSemis => (Bend - 8192) / 8192.0 * BendRange;
        public void ResetControllers() => (Volume, Expression, Pan, Sustain, Bend, BendRange, RpnMsb, RpnLsb, Retune, Legato) = (100, 127, 64, false, 8192, 2, 127, 127, true, false);
    }

    private readonly Ch[] _ch = new Ch[16];
    private readonly PluckedString[] _strings;
    private readonly float[] _scratch;
    private readonly int _tail, _rate;
    private long _count, _clock;
    private uint _seed;
    private GuitarRig.Slot[]? _pending;
    private bool _swapPending;

    public GuitarLayer(int rate)
    {
        _rate = rate;
        var gCab = CabinetImpulse.Build(CabKind.Guitar, rate);
        var bCab = CabinetImpulse.Build(CabKind.Bass, rate);
        var rCab = CabinetImpulse.Build(CabKind.Room, rate);
        _strings = Enumerable.Range(0, Strings).Select(_ => new PluckedString(rate)).ToArray();
        _scratch = new float[(int)(rate / PluckedString.LowestHz) + 8];
        for (var i = 0; i < 16; i++) _ch[i] = new Ch { Bus = new GuitarBus(rate, Chunk, gCab, bCab, rCab), Mono = new float[Chunk] };
        _tail = (int)(TailSeconds * rate);
        Reset();
    }

    public static bool Models(int program) => Voicings.Models(program);
    public bool Owns(int channel) => channel != 9 && Models(_ch[channel & 15].Program);
    public int ActiveStrings => _strings.Count(s => s.Active);

    // Output gain: 1 inside Synthesizer (it applies MasterVolume after the layer); a host mixing the layer itself sets its master here.
    public float Gain { get; set; } = 1;

    // The clean pickup signal only, no amp or cabinet (for judging the strings).
    public bool DiOnly { set { foreach (var c in _ch) c.Bus.DiOnly = value; } }

    // Any thread but the audio thread: loads the electric guitar amp (.nam) and cabinet (.wav); null or empty = built-in.
    // The audio thread swaps them in at its next block. Returns what is in use, with the reason for any fallback.
    public string LoadRig(string? namPath, string? irPath)
    {
        var (slots, status) = GuitarRig.Build(namPath, irPath, _rate, Chunk, 16);
        Volatile.Write(ref _pending, slots);
        Volatile.Write(ref _swapPending, true);
        return status;
    }

    // Any thread but the audio thread: the status LoadRig would report (loads and checks the files, keeps nothing).
    public static string DescribeRig(string? namPath, string? irPath) => GuitarRig.Build(namPath, irPath, 48000, Chunk, 0).Status;

    public void Reset()
    {
        foreach (var s in _strings) s.Stop();
        foreach (var c in _ch) { c.ResetControllers(); c.Program = 0; c.Tail = 0; c.Configured = -1; c.From = -1; c.Bus.Reset(); }
        (_count, _seed) = (0, 0x9E3779B9);
    }

    public void ProcessMidi(int channel, int command, int d1, int d2)
    {
        channel &= 15;
        var c = _ch[channel];
        switch (command)
        {
            case 0x90 when d2 > 0: NoteOn(channel, c, d1, d2); break;
            case 0x90 or 0x80:
                if (c.From == d1) { c.From = -1; break; }   // the legato's old note ends: confirmed
                foreach (var s in _strings)
                    if (s.Active && s.Channel == channel && s.Note == d1 && !s.Released) { if (c.Sustain) s.Held = true; else s.Release(); }
                break;
            case 0xC0: c.Program = d1 & 0x7F; break;
            case 0xE0: c.Bend = (d1 & 0x7F) | ((d2 & 0x7F) << 7); c.Retune = true; break;
            case 0xB0: Controller(channel, c, d1, d2); break;
        }
    }

    private void Controller(int channel, Ch c, int cc, int v)
    {
        var rpn0 = c.RpnMsb == 0 && c.RpnLsb == 0;
        switch (cc)
        {
            case 7: c.Volume = v; break;
            case 10: c.Pan = v; break;
            case 11: c.Expression = v; break;
            case 64: c.Sustain = v >= 64; if (!c.Sustain) foreach (var s in _strings) if (s.Active && s.Channel == channel && s.Held) s.Release(); break;
            case 68: c.Legato = v >= 64; break;
            case 101: c.RpnMsb = v; break;
            case 100: c.RpnLsb = v; break;
            case 6 when rpn0: c.BendRange = v + c.BendRange % 1; c.Retune = true; break;
            case 38 when rpn0: c.BendRange = Math.Floor(c.BendRange) + v / 100.0; c.Retune = true; break;
            case 120: foreach (var s in _strings) if (s.Active && s.Channel == channel) s.Stop(); c.Tail = 0; c.Bus.Reset(); break;
            case 121: c.ResetControllers(); break;
            case 123: foreach (var s in _strings) if (s.Active && s.Channel == channel) s.Release(); break;
        }
    }

    private static double Hz(Ch c, int note) => 440 * Math.Pow(2, (note - 69 + c.BendSemis) / 12);

    private void NoteOn(int channel, Ch c, int note, int velocity)
    {
        if (!Owns(channel)) return;
        var v = Voicings.Of(c.Program);
        if (c.Configured != c.Program) { c.Bus.Configure(v.Rig); c.Configured = c.Program; }
        _seed = _seed * 1664525 + 1013904223;
        c.Tail = _tail;
        if (LegatoFrom(channel, c, note) is { } s)
        {
            var step = note - s.Note;
            var glide = v.String.Glide > 0 ? v.String.Glide : Math.Abs(step) >= SlideFrom ? SlideSeconds : 0;   // fretless: every step glides
            (c.From, c.To, c.Vel, c.Moved, c.Due) = (s.Note, note, velocity, s, _clock + (long)(LegatoWindow * _rate));
            s.Legato(note, Hz(c, note), glide, glide > 0 ? 0 : step > 0 ? Hammer : Pull, _seed, _scratch);
            return;
        }
        Pick(channel, note, v.String.MaxStrings).Pluck(channel, note, Hz(c, note), velocity, v.String, ++_count, _seed, _scratch);
    }

    // The one held string of the channel, close in pitch and not part of a strum; any held string when CC68 is on.
    private PluckedString? LegatoFrom(int channel, Ch c, int note)
    {
        PluckedString? one = null;
        foreach (var s in _strings)
            if (s.Active && s.Channel == channel && !s.Released && !s.Held)
            {
                if (one is not null) return null;
                one = s;
            }
        if (one is null || one.Note == note) return null;
        return c.Legato || (Math.Abs(note - one.Note) <= LegatoMax && one.Age >= LegatoAge) ? one : null;
    }

    // Same note ringing (re-pluck), else free, else the channel's oldest when it has all its strings, else the oldest.
    private PluckedString Pick(int channel, int note, int max)
    {
        PluckedString? free = null, oldHere = null, old = null;
        var here = 0;
        foreach (var s in _strings)
        {
            if (!s.Active) { free ??= s; continue; }
            if (s.Channel == channel)
            {
                if (s.Note == note) return s;
                here++;
                if (oldHere is null || s.Started < oldHere.Started) oldHere = s;
            }
            if (old is null || s.Started < old.Started) old = s;
        }
        var pick = here >= max ? oldHere! : free ?? old!;
        if (pick.Active) pick.Stop();
        return pick;
    }

    public void Render(Span<float> left, Span<float> right)
    {
        for (var at = 0; at < left.Length; at += Chunk)
        {
            var n = Math.Min(Chunk, left.Length - at);
            RenderChunk(left.Slice(at, n), right.Slice(at, n));
            _clock += n;
        }
    }

    // A legato whose old note stayed held was a let-ring: the old string returns, the new note is picked.
    private void Settle(int i, Ch c)
    {
        if (c.From < 0 || _clock < c.Due) return;
        if (c.Moved is { Active: true } s && s.Note == c.To && s.Channel == i)
        {
            s.Legato(c.From, Hz(c, c.From), 0, 0, _seed, _scratch);
            var v = Voicings.Of(c.Program);
            Pick(i, c.To, v.String.MaxStrings).Pluck(i, c.To, Hz(c, c.To), c.Vel, v.String, ++_count, _seed, _scratch);
        }
        (c.From, c.Moved) = (-1, null);
    }

    private void RenderChunk(Span<float> left, Span<float> right)
    {
        var n = left.Length;
        var swap = Volatile.Read(ref _swapPending);
        if (swap) _swapPending = false;
        var slots = swap ? Volatile.Read(ref _pending) : null;
        for (var i = 0; i < 16; i++)
        {
            var c = _ch[i];
            c.Marked = false;
            if (swap) c.Bus.Use(slots?[i], now: c.Tail <= 0);
            Settle(i, c);
            if (!c.Retune) continue;
            c.Retune = false;
            foreach (var s in _strings) if (s.Active && s.Channel == i) s.Retune(Hz(c, s.Note));
        }
        foreach (var s in _strings)
        {
            if (!s.Active) continue;
            var c = _ch[s.Channel];
            if (!c.Marked) { Array.Clear(c.Mono, 0, n); c.Marked = true; c.Tail = _tail; }
            s.Render(c.Mono.AsSpan(0, n));
        }
        foreach (var c in _ch)
        {
            if (c.Tail <= 0) continue;
            if (!c.Marked) Array.Clear(c.Mono, 0, n);
            var mono = c.Mono.AsSpan(0, n);
            c.Bus.Process(mono);
            if ((c.Tail -= n) <= 0) c.Bus.Reset();
            // GM gain curves: (cc/127)² each; constant-power pan, unity at centre.
            var gain = (float)(Gain * Math.Pow(c.Volume / 127.0, 2) * Math.Pow(c.Expression / 127.0, 2) * Math.Sqrt(2));
            var theta = c.Pan / 127.0 * Math.PI / 2;
            float gl = gain * (float)Math.Cos(theta), gr = gain * (float)Math.Sin(theta);
            for (var i = 0; i < n; i++) { left[i] += mono[i] * gl; right[i] += mono[i] * gr; }
        }
    }
}
