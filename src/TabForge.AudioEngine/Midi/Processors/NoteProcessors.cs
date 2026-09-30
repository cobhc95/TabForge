namespace TabForge.AudioEngine.Midi.Processors;

/// <summary>Channel filter / remap: 16 rows, each off or an output channel (default: identity). Optional "solo channel". Every channel message follows its row.</summary>
public sealed class ChannelMapProcessor : NoteTransform
{
    private readonly int[] _map = new int[16];   // 0 = blocked, 1..16 = output channel
    private readonly int _solo;

    public ChannelMapProcessor(MidiParams p)
    {
        for (var i = 0; i < 16; i++) _map[i] = p.Int($"Ch{i + 1}", i + 1, 0, 16);
        _solo = p.Int("Solo", 0, 0, 16);
    }

    private bool Route(ref int ch)
    {
        if (_solo > 0 && ch != _solo - 1) return false;
        var m = _map[ch & 15];
        if (m == 0) return false;
        ch = m - 1;
        return true;
    }

    protected override bool MapNote(ref int ch, ref int note, ref int vel) => Route(ref ch);
    protected override bool MapOther(int kind, ref int ch, ref int d1, ref int d2) => Route(ref ch);
}

/// <summary>Channel router: one rule (input channel, note range, optional single note) that sends notes and/or other messages to another channel. Stack several for more rules.</summary>
public sealed class ChannelRouteProcessor : NoteTransform
{
    private readonly int _outCh, _scope, _low, _high, _note;

    public ChannelRouteProcessor(MidiParams p)
    {
        InCh = p.Int("InCh", 0, 0, 16);
        _outCh = p.Int("OutCh", 1, 1, 16) - 1;
        _scope = p.Int("Scope", 1, 0, 3);   // 0 off, 1 notes, 2 other messages, 3 both
        _low = p.Int("Low", 0, 0, 127);
        _high = p.Int("High", 127, 0, 127);
        _note = p.Int("Note", -1, -1, 127);
    }

    protected override bool MapNote(ref int ch, ref int note, ref int vel)
    {
        if ((_scope & 1) != 0 && note >= _low && note <= _high && (_note < 0 || note == _note)) ch = _outCh;
        return true;
    }

    protected override bool MapOther(int kind, ref int ch, ref int d1, ref int d2)
    {
        if ((_scope & 2) != 0) ch = _outCh;
        return true;
    }
}

/// <summary>Note range filter: notes outside low..high are dropped (with their note-offs); other events optionally pass.</summary>
public sealed class NoteRangeProcessor : NoteTransform
{
    private readonly int _low, _high;
    private readonly bool _otherPass;

    public NoteRangeProcessor(MidiParams p)
    {
        InCh = p.Int("InCh", 0, 0, 16);
        _low = p.Int("Low", 21, 0, 127);
        _high = p.Int("High", 108, 0, 127);
        _otherPass = p.Bool("OtherPass", true);
    }

    protected override bool MapNote(ref int ch, ref int note, ref int vel) => note >= _low && note <= _high;
    protected override bool MapOther(int kind, ref int ch, ref int d1, ref int d2) => _otherPass;
}

/// <summary>Transpose: out = in * premultiply + semitones, clamped to 0..127, for notes inside the key range.</summary>
public sealed class TransposeProcessor : NoteTransform
{
    private readonly int _semis, _low, _high;
    private readonly double _mult;

    public TransposeProcessor(MidiParams p)
    {
        InCh = p.Int("InCh", 0, 0, 16);
        _semis = p.Int("Semitones", 0, -64, 64);
        _mult = p.Double("Premultiply", 1, -16, 16);
        _low = p.Int("Low", 0, 0, 127);
        _high = p.Int("High", 127, 0, 127);
    }

    protected override bool MapNote(ref int ch, ref int note, ref int vel)
    {
        if (note < _low || note > _high) return true;
        note = (int)Math.Clamp(Math.Round(note * _mult + _semis), 0, 127);
        return true;
    }
}

/// <summary>Note map: a 128-entry table (-1 = drop). The app resolves drum-map presets into this table before sending.</summary>
public sealed class NoteMapProcessor : NoteTransform
{
    private readonly int[] _map;

    public NoteMapProcessor(MidiParams p)
    {
        InCh = p.Int("InCh", 0, 0, 16);
        _map = p.IntArray("Map", 128, -1, 127) ?? Identity();
    }

    private static int[] Identity() { var m = new int[128]; for (var i = 0; i < 128; i++) m[i] = i; return m; }

    protected override bool MapNote(ref int ch, ref int note, ref int vel)
    {
        var o = _map[note & 127];
        if (o < 0) return false;
        note = o;
        return true;
    }
}

/// <summary>Velocity shaper: scale + offset, fixed, or compressor; random, dry/wet mix, key and velocity ranges. Output note-on velocity is never below 1.</summary>
public sealed class VelocityProcessor : NoteTransform
{
    private readonly int _mode, _add, _min, _max, _fixed, _random, _mix, _inLow, _inHigh, _low, _high;
    private readonly double _mul, _threshold, _ratio, _makeup;
    private MidiRandom _rng = new(12345);

    public VelocityProcessor(MidiParams p)
    {
        InCh = p.Int("InCh", 0, 0, 16);
        _mode = p.Int("Mode", 0, 0, 2);   // 0 scale + offset, 1 fixed, 2 compressor
        _mul = p.Double("Mul", 1, -16, 16);
        _add = p.Int("Add", 0, -128, 128);
        _min = p.Int("Min", 1, 0, 127);
        _max = p.Int("Max", 127, 0, 127);
        _fixed = p.Int("Fixed", 100, 1, 127);
        _threshold = p.Double("Threshold", 80, 1, 127);
        _ratio = p.Double("Ratio", 2, 1, 20);
        _makeup = p.Double("Makeup", 0, -64, 64);
        _random = p.Int("Random", 0, 0, 100);
        _mix = p.Int("Mix", 100, 0, 100);
        _inLow = p.Int("InLow", 1, 1, 127);
        _inHigh = p.Int("InHigh", 127, 1, 127);
        _low = p.Int("Low", 0, 0, 127);
        _high = p.Int("High", 127, 0, 127);
    }

    protected override bool MapNote(ref int ch, ref int note, ref int vel)
    {
        if (note < _low || note > _high || vel < _inLow || vel > _inHigh) return true;   // outside the ranges: untouched
        double v = vel, y;
        switch (_mode)
        {
            case 1: y = _fixed; break;
            case 2: y = v > _threshold ? _threshold + (v - _threshold) / _ratio : v; y += _makeup; break;
            default: y = v * _mul + _add; break;
        }
        if (_random > 0) y += _rng.Bipolar() * 64 * _random / 100.0;
        y = v + (y - v) * _mix / 100.0;
        var lo = Math.Max(1, Math.Min(_min, _max));
        var hi = Math.Max(lo, _max);
        vel = (int)Math.Clamp(Math.Round(y), lo, hi);
        return true;
    }
}
