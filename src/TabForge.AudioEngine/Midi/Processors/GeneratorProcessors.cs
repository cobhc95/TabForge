namespace TabForge.AudioEngine.Midi.Processors;

/// <summary>Note repeater: while a note is held its note-off / note-on pair is re-sent every <c>Size</c> beats with the original velocity.</summary>
public sealed class RepeaterProcessor : IMidiProcessor
{
    private readonly int _inCh;
    private readonly double _size;
    private readonly bool[] _held = new bool[2048];
    private readonly byte[] _vel = new byte[2048];
    private int _count;
    private double _next;

    public RepeaterProcessor(MidiParams p)
    {
        _inCh = p.Int("InCh", 0, 0, 16);
        _size = p.Double("Size", 0.5, 0.1, 4);
    }

    private void Fire(double limit, long blockStart, MidiBuffer output, in MidiContext ctx, double period)
    {
        while (_count > 0 && _next < limit)
        {
            var frame = (int)Math.Clamp(_next - blockStart, 0, ctx.Frames - 1);
            for (var key = 0; key < 2048; key++)
            {
                if (!_held[key]) continue;
                var ch = key >> 7; var note = (byte)(key & 0x7F);
                output.Add(frame, (byte)(0x80 | ch), note, 0);
                output.Add(frame, (byte)(0x90 | ch), note, _vel[key]);
            }
            output.Unsorted = true;
            _next += period;
        }
    }

    public void Process(MidiBuffer input, MidiBuffer output, in MidiContext ctx)
    {
        var tempo = ctx.Tempo > 1 ? ctx.Tempo : 120;
        var period = Math.Max(1.0, _size * 60.0 / tempo * ctx.SampleRate);
        for (var i = 0; i < input.Count; i++)
        {
            var e = input.Items[i];
            var st = e.Status; var kind = st & 0xF0;
            if (st >= 0xF0 || kind is not (0x80 or 0x90) || (_inCh != 0 && (st & 0x0F) != _inCh - 1)) { output.Add(e); continue; }
            var abs = ctx.BlockStart + e.Frame;
            Fire(abs, ctx.BlockStart, output, ctx, period);
            var key = ((st & 0x0F) << 7) | (e.Data1 & 0x7F);
            if (kind == 0x90 && e.Data2 > 0)
            {
                if (!_held[key]) { _held[key] = true; _count++; }
                _vel[key] = e.Data2;
                if (_count == 1) _next = abs + period;
            }
            else if (_held[key]) { _held[key] = false; _count--; }
            output.Add(e);
        }
        Fire(ctx.BlockStart + ctx.Frames, ctx.BlockStart, output, ctx, period);
    }

    public void Adopt(IMidiProcessor previous, bool sameParameters)
    {
        if (previous is not RepeaterProcessor p) return;
        Array.Copy(p._held, _held, _held.Length); Array.Copy(p._vel, _vel, _vel.Length);
        _count = p._count; _next = p._next;
    }

    public void Reset() { Array.Clear(_held); _count = 0; }
}

/// <summary>Arpeggiator: held notes are replaced by a tempo-synced run (down / up / down-alt / up-alt) with optional octave-style variants. Steps sit on the beat grid.</summary>
public sealed class ArpeggiatorProcessor : IMidiProcessor
{
    private readonly int _inCh, _mode, _variants, _varOffset, _velocity;
    private readonly double _rate, _length;
    private readonly bool[] _held = new bool[2048];
    private readonly byte[] _vel = new byte[2048];
    private readonly int[] _list = new int[2048];
    private readonly EventScheduler _sched = new();
    private int _count;
    private long _step;
    private double _next;

    public ArpeggiatorProcessor(MidiParams p)
    {
        _inCh = p.Int("InCh", 0, 0, 16);
        _rate = p.Double("Rate", 2, 0.25, 16);
        _length = p.Double("Length", 0.5, 0.01, 0.95);
        _mode = p.Int("Mode", 1, 0, 3);   // 0 down, 1 up, 2 down-alt, 3 up-alt
        _variants = p.Int("Variants", 0, 0, 3);
        _varOffset = p.Int("VarOffset", 12, -64, 64);
        _velocity = p.Int("Velocity", 0, 0, 127);
    }

    private void RunSteps(double limit, long blockStart, MidiBuffer output, in MidiContext ctx, double period)
    {
        while (_count > 0 && _next < limit)
        {
            var n = 0;
            for (var note = 0; note < 128; note++)
                for (var ch = 0; ch < 16; ch++)
                    if (_held[(ch << 7) | note]) _list[n++] = (ch << 7) | note;
            var s = _step++;
            long pass; int idx;
            if (_mode <= 1) { idx = (int)(s % n); pass = s / n; if (_mode == 0) idx = n - 1 - idx; }
            else
            {
                var cyc = Math.Max(1, 2 * n - 2);
                var pos = (int)(s % cyc);
                idx = pos < n ? pos : cyc - pos;
                pass = s / cyc;
                if (_mode == 2) idx = n - 1 - idx;
            }
            var key = _list[idx];
            var ch2 = key >> 7;
            var note2 = Math.Clamp((key & 0x7F) + (int)(pass % (_variants + 1)) * _varOffset, 0, 127);
            var vel = _velocity > 0 ? _velocity : _vel[key];
            var t = (long)_next;
            output.Add((int)Math.Clamp(t - blockStart, 0, ctx.Frames - 1), (byte)(0x90 | ch2), (byte)note2, (byte)vel);
            output.Unsorted = true;
            _sched.Schedule(t + Math.Max(1, (long)(_length * period)), (byte)(0x80 | ch2), (byte)note2, 0);
            _next += period;
        }
    }

    public void Process(MidiBuffer input, MidiBuffer output, in MidiContext ctx)
    {
        var tempo = ctx.Tempo > 1 ? ctx.Tempo : 120;
        var period = Math.Max(1.0, 60.0 / tempo * ctx.SampleRate / _rate);
        for (var i = 0; i < input.Count; i++)
        {
            var e = input.Items[i];
            var st = e.Status; var kind = st & 0xF0;
            if (st >= 0xF0 || kind is not (0x80 or 0x90) || (_inCh != 0 && (st & 0x0F) != _inCh - 1)) { output.Add(e); continue; }
            var abs = ctx.BlockStart + e.Frame;
            RunSteps(abs, ctx.BlockStart, output, ctx, period);
            var key = ((st & 0x0F) << 7) | (e.Data1 & 0x7F);
            if (kind == 0x90 && e.Data2 > 0)
            {
                if (!_held[key]) { _held[key] = true; _count++; }
                _vel[key] = e.Data2;
                if (_count == 1) { _next = Math.Ceiling(abs / period) * period; _step = 0; }
            }
            else if (_held[key]) { _held[key] = false; _count--; }
        }
        RunSteps(ctx.BlockStart + ctx.Frames, ctx.BlockStart, output, ctx, period);
        _sched.Drain(ctx.BlockStart, ctx.Frames, output);
    }

    public void Adopt(IMidiProcessor previous, bool sameParameters)
    {
        if (previous is not ArpeggiatorProcessor p) return;
        Array.Copy(p._held, _held, _held.Length); Array.Copy(p._vel, _vel, _vel.Length);
        _count = p._count; _step = p._step; _next = p._next; _sched.CopyFrom(p._sched);
    }

    public void Reset() { Array.Clear(_held); _count = 0; _step = 0; _sched.Reset(); }
}

/// <summary>Modal randomizer: every played note passes and also seeds a small cloud of random modal notes (scale-degree intervals with probabilities), each with its own timed note-off.</summary>
public sealed class ModalRandomizerProcessor : IMidiProcessor
{
    private static readonly int[] Major = { 0, 2, 4, 5, 7, 9, 11 };
    private readonly int _inCh, _notes, _octRand, _timing, _velRand;
    private readonly int[] _interval = new int[4], _prob = new int[4];
    private readonly double _length;
    private readonly EventScheduler _sched = new();
    private MidiRandom _rng = new(11223);

    public ModalRandomizerProcessor(MidiParams p)
    {
        _inCh = p.Int("InCh", 0, 0, 16);
        _notes = p.Int("Notes", 2, 0, 8);
        for (var i = 0; i < 4; i++) { _interval[i] = p.Int($"Interval{i + 1}", i == 0 ? 3 : i == 1 ? 5 : i == 2 ? 2 : 7, 1, 7); _prob[i] = p.Int($"Prob{i + 1}", i < 2 ? 60 : 30, 0, 100); }
        _octRand = p.Int("OctaveRandom", 20, 0, 100);
        _timing = p.Int("TimingRandom", 50, 0, 100);
        _velRand = p.Int("VelRandom", 30, 0, 100);
        _length = p.Double("Length", 0.5, 0.05, 4);
    }

    public void Process(MidiBuffer input, MidiBuffer output, in MidiContext ctx)
    {
        var tempo = ctx.Tempo > 1 ? ctx.Tempo : 120;
        var beat = 60.0 / tempo * ctx.SampleRate;
        for (var i = 0; i < input.Count; i++)
        {
            var e = input.Items[i];
            output.Add(e);
            var st = e.Status;
            if (st >= 0xF0 || (st & 0xF0) != 0x90 || e.Data2 == 0 || (_inCh != 0 && (st & 0x0F) != _inCh - 1)) continue;
            for (var n = 0; n < _notes; n++)
            {
                var pick = -1;
                for (var k = 0; k < 4 && pick < 0; k++) if (_rng.Next() % 100 < _prob[k]) pick = k;
                if (pick < 0) continue;
                var note = e.Data1 + Major[_interval[pick] - 1];
                if (_octRand > 0 && _rng.Next() % 100 < _octRand) note += (_rng.Next() & 1) == 0 ? 12 : -12;
                if (note < 0 || note > 127) continue;
                var vel = (int)Math.Clamp(Math.Round(e.Data2 * (1 - (_rng.Next() / (double)uint.MaxValue) * _velRand / 200.0)), 1, 127);
                var at = ctx.BlockStart + e.Frame + (long)(_rng.Next() / (double)uint.MaxValue * _timing / 100.0 * beat);
                _sched.Schedule(at, st, (byte)note, (byte)vel);
                _sched.Schedule(at + (long)(_length * beat), (byte)(0x80 | (st & 0x0F)), (byte)note, 0);
            }
        }
        _sched.Drain(ctx.BlockStart, ctx.Frames, output);
    }

    public void Adopt(IMidiProcessor previous, bool sameParameters)
    {
        if (previous is ModalRandomizerProcessor p) _sched.CopyFrom(p._sched);
    }

    public void Reset() => _sched.Reset();
}

/// <summary>LFO: sine / triangle / saw / square / sample-and-hold into a controller or the pitch wheel, in Hz or cycles per beat. Emits only when the quantized value changes; sends the off value (or bend centre) when it stops.</summary>
public sealed class LfoProcessor : IMidiProcessor
{
    private readonly int _target, _ch, _cc, _shape, _off;
    private readonly double _center, _range, _freq, _updates, _maxBend;
    private readonly bool _beats, _onlyPlaying, _enabled;
    private double _phase, _nextTick;
    private int _last = -1;
    private bool _active;

    public LfoProcessor(MidiParams p)
    {
        _target = p.Int("Target", 0, 0, 1);   // 0 controller, 1 pitch wheel
        _ch = p.Int("Channel", 1, 1, 16) - 1;
        _cc = p.Int("Cc", 1, 0, 127);
        _center = p.Double("Center", 64, 0, 127);
        _range = p.Double("Range", 63, 0, 127);
        _maxBend = p.Double("MaxBend", 50, 0, 100);
        _shape = p.Int("Shape", 0, 0, 4);
        _freq = p.Double("Freq", 1, 0, 32);
        _beats = p.Int("Sync", 1, 0, 1) == 1;   // 1 cycles per beat, 0 Hz
        _updates = p.Double("Updates", 64, 1, 512);
        _off = p.Int("OffValue", -1, -1, 127);
        _onlyPlaying = p.Bool("OnlyPlaying", true);
        _enabled = p.Bool("Enabled", true);
    }

    private static double Wave(int shape, double phase)
    {
        var f = phase - Math.Floor(phase);
        switch (shape)
        {
            case 1: return f < 0.5 ? f * 4 - 1 : 3 - f * 4;
            case 2: return f * 2 - 1;
            case 3: return f < 0.5 ? 1 : -1;
            case 4:
                var h = (uint)Math.Floor(phase) * 2654435761u; h ^= h >> 15; h *= 2246822519u; h ^= h >> 13;
                return h / (double)uint.MaxValue * 2 - 1;
            default: return Math.Sin(f * Math.PI * 2);
        }
    }

    private void Send(MidiBuffer output, int frame, int value)
    {
        if (_target == 0) output.Add(frame, (byte)(0xB0 | _ch), (byte)_cc, (byte)value);
        else output.Add(frame, (byte)(0xE0 | _ch), (byte)(value & 0x7F), (byte)(value >> 7));
        output.Unsorted = true;
    }

    public void Process(MidiBuffer input, MidiBuffer output, in MidiContext ctx)
    {
        for (var i = 0; i < input.Count; i++) output.Add(input.Items[i]);
        var run = _enabled && (ctx.Playing || !_onlyPlaying) && !ctx.PlayStopped;
        if (!run)
        {
            if (_active)
            {
                if (_target == 1) Send(output, 0, 8192); else if (_off >= 0) Send(output, 0, _off);
                _active = false; _last = -1;
            }
            return;
        }
        var tempo = ctx.Tempo > 1 ? ctx.Tempo : 120;
        var beat = 60.0 / tempo * ctx.SampleRate;
        var inc = _beats ? _freq / beat : _freq / ctx.SampleRate;
        var tick = Math.Max(1.0, beat / _updates);
        var end = ctx.BlockStart + ctx.Frames;
        if (!_active || _nextTick < ctx.BlockStart) _nextTick = ctx.BlockStart;
        _active = true;
        while (_nextTick < end)
        {
            var frame = (int)Math.Clamp(_nextTick - ctx.BlockStart, 0, ctx.Frames - 1);
            var w = Wave(_shape, _phase + (_nextTick - ctx.BlockStart) * inc);
            var v = _target == 0 ? (int)Math.Clamp(Math.Round(_center + w * _range), 0, 127) : (int)Math.Clamp(Math.Round(8192 + w * _maxBend / 100.0 * 8191), 0, 16383);
            if (v != _last) { Send(output, frame, v); _last = v; }
            _nextTick += tick;
        }
        _phase += ctx.Frames * inc;
        if (_phase > 4096) _phase -= 4096;
    }

    public void Adopt(IMidiProcessor previous, bool sameParameters)
    {
        if (previous is not LfoProcessor p) return;
        _phase = p._phase; _nextTick = p._nextTick; _last = p._last; _active = p._active;
    }

    public void Reset() { _active = false; _last = -1; }
}

/// <summary>Step sequencer (Sequencer Baby / Megababy): up to four step patterns of semitone offsets from a root note, with per-step velocity, gate %, swing and pattern chaining. Runs while the transport plays, from the start of playback.</summary>
public sealed class StepSequencerProcessor : IMidiProcessor
{
    private const int Rest = int.MinValue;
    private readonly int _ch, _root, _steps, _chain, _velDefault, _gateDefault;
    private readonly double _spb, _swing;
    private readonly bool _passInput;
    private readonly int[][] _notes;
    private readonly int[] _vel, _gate;
    private readonly EventScheduler _sched = new();
    private bool _running;
    private double _grid;
    private long _k;

    public StepSequencerProcessor(MidiParams p)
    {
        _ch = p.Int("Channel", 1, 1, 16) - 1;
        _root = p.Int("Root", 60, 0, 127);
        _steps = p.Int("Steps", 16, 4, 128);
        _spb = p.Double("StepsPerBeat", 4, 1, 16);
        _gateDefault = p.Int("Gate", 50, 1, 100);
        _swing = p.Double("Swing", 0, 0, 100);
        _velDefault = p.Int("Velocity", 100, 1, 127);
        _chain = p.Int("Chain", 1, 1, 4);
        _passInput = p.Bool("PassInput", true);
        _notes = new int[4][];
        for (var i = 0; i < 4; i++)
        {
            var parts = p.String($"Pattern{i + 1}", i == 0 ? "0 . 7 . 12 . 7 ." : "").Split(new[] { ' ', ',', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            _notes[i] = new int[Math.Max(1, parts.Length)];
            if (parts.Length == 0) _notes[i][0] = Rest;
            for (var s = 0; s < parts.Length; s++) _notes[i][s] = int.TryParse(parts[s], out var v) ? Math.Clamp(v, -127, 127) : Rest;
        }
        _vel = MidiScales.ParseInts(p.String("Velocities", ""), 1, 127, 0);
        _gate = MidiScales.ParseInts(p.String("Gates", ""), 1, 100, 0);
    }

    public void Process(MidiBuffer input, MidiBuffer output, in MidiContext ctx)
    {
        if (_passInput) for (var i = 0; i < input.Count; i++) output.Add(input.Items[i]);
        if (ctx.Playing)
        {
            var tempo = ctx.Tempo > 1 ? ctx.Tempo : 120;
            var sl = 60.0 / tempo * ctx.SampleRate / _spb;
            var end = ctx.BlockStart + ctx.Frames;
            if (!_running || ctx.PlayStarted) { _running = true; _grid = ctx.BlockStart; _k = 0; }
            while (_grid < end)
            {
                var pattern = _notes[(int)((_k / _steps) % _chain)];
                var inPat = (int)(_k % _steps);
                var n = pattern[inPat % pattern.Length];
                if (n != Rest)
                {
                    var note = Math.Clamp(_root + n, 0, 127);
                    var at = (long)(_grid + ((_k & 1) == 1 ? _swing / 100.0 * sl * 0.5 : 0));
                    var vel = _vel.Length > 0 && _vel[inPat % _vel.Length] > 0 ? _vel[inPat % _vel.Length] : _velDefault;
                    var gate = _gate.Length > 0 && _gate[inPat % _gate.Length] > 0 ? _gate[inPat % _gate.Length] : _gateDefault;
                    _sched.Schedule(at, (byte)(0x90 | _ch), (byte)note, (byte)vel);
                    _sched.Schedule(at + Math.Max(1, (long)(sl * gate / 100.0)), (byte)(0x80 | _ch), (byte)note, 0);
                }
                _grid += sl; _k++;
            }
        }
        else _running = false;
        _sched.Drain(ctx.BlockStart, ctx.Frames, output);
    }

    public void Adopt(IMidiProcessor previous, bool sameParameters)
    {
        if (previous is not StepSequencerProcessor p) return;
        _running = p._running; _grid = p._grid; _k = p._k; _sched.CopyFrom(p._sched);
    }

    public void Reset() { _running = false; _sched.Reset(); }
}
