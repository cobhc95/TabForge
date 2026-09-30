namespace TabForge.AudioEngine.Midi.Processors;

/// <summary>Scale tables and small parsing helpers shared by the P3 processors.</summary>
public static class MidiScales
{
    public static readonly int[][] Steps =
    {
        new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 },   // chromatic
        new[] { 0, 2, 4, 5, 7, 9, 11 },                   // major
        new[] { 0, 2, 3, 5, 7, 8, 10 },                   // natural minor
        new[] { 0, 2, 3, 5, 7, 8, 11 },                   // harmonic minor
        new[] { 0, 2, 3, 5, 7, 9, 11 },                   // melodic minor
        new[] { 0, 2, 3, 5, 7, 9, 10 },                   // dorian
        new[] { 0, 1, 3, 5, 7, 8, 10 },                   // phrygian
        new[] { 0, 2, 4, 6, 7, 9, 11 },                   // lydian
        new[] { 0, 2, 4, 5, 7, 9, 10 },                   // mixolydian
        new[] { 0, 2, 4, 7, 9 },                          // pentatonic major
        new[] { 0, 3, 5, 7, 10 },                         // pentatonic minor
        new[] { 0, 2, 4, 6, 8, 10 },                      // whole tone
    };

    public static int[] Get(int i) => Steps[Math.Clamp(i, 0, Steps.Length - 1)];

    public static bool[] Mask(int root, int scale)
    {
        var m = new bool[12];
        foreach (var s in Get(scale)) m[(root + s) % 12] = true;
        return m;
    }

    /// <summary>Nearest in-scale note (ties go down).</summary>
    public static int Snap(bool[] mask, int note)
    {
        for (var d = 0; d < 12; d++)
        {
            if (note - d >= 0 && mask[(note - d) % 12]) return note - d;
            if (note + d <= 127 && mask[(note + d) % 12]) return note + d;
        }
        return note;
    }

    private static int FloorDiv(int a, int b) => (int)Math.Floor(a / (double)b);

    /// <summary>Scale-degree index (octave * length + index) of the in-scale note nearest to <paramref name="note"/>.</summary>
    public static int DegreeOf(int root, int[] scale, bool[] mask, int note)
    {
        note = Snap(mask, note);
        var rel = note - root;
        var oct = FloorDiv(rel, 12);
        var pc = rel - oct * 12;
        var idx = Array.IndexOf(scale, pc);
        return oct * scale.Length + Math.Max(0, idx);
    }

    public static int NoteOfDegree(int root, int[] scale, int degree)
    {
        var oct = FloorDiv(degree, scale.Length);
        return oct * 12 + root + scale[degree - oct * scale.Length];
    }

    /// <summary>Whitespace / comma separated integers; "." "-" "x" "_" (or anything unparsable) give <paramref name="rest"/>.</summary>
    public static int[] ParseInts(string text, int min, int max, int rest)
    {
        var parts = text.Split(new[] { ' ', ',', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        var r = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++) r[i] = int.TryParse(parts[i], out var v) ? Math.Clamp(v, min, max) : rest;
        return r;
    }
}

/// <summary>Snap to scale / key: out-of-scale notes are moved to the nearest scale note (ties down) or blocked.</summary>
public sealed class ScaleSnapProcessor : NoteTransform
{
    private readonly bool[] _mask;
    private readonly int _mode, _low, _high;

    public ScaleSnapProcessor(MidiParams p)
    {
        InCh = p.Int("InCh", 0, 0, 16);
        _mask = MidiScales.Mask(p.Int("Root", 0, 0, 11), p.Int("Scale", 1, 0, 11));
        _mode = p.Int("Mode", 0, 0, 1);   // 0 remap, 1 block
        _low = p.Int("Low", 0, 0, 127);
        _high = p.Int("High", 127, 0, 127);
    }

    protected override bool MapNote(ref int ch, ref int note, ref int vel)
    {
        if (note < _low || note > _high || _mask[note % 12]) return true;
        if (_mode == 1) return false;
        note = MidiScales.Snap(_mask, note);
        return true;
    }
}

/// <summary>Chord: adds up to four voices to every played note, at fixed intervals (chorderizer) or diatonic scale steps (chord in key). A note-off releases all its voices.</summary>
public sealed class ChordProcessor : IMidiProcessor
{
    private readonly int _inCh, _mode, _low, _high, _root, _st2, _st3;
    private readonly int[] _scale, _fixed = new int[4];
    private readonly bool[] _mask;
    private readonly double _velScale;
    private readonly int[] _cnt = new int[2048];
    private readonly byte[] _notes = new byte[2048 * 5];

    public ChordProcessor(MidiParams p)
    {
        _inCh = p.Int("InCh", 0, 0, 16);
        _mode = p.Int("Mode", 0, 0, 1);   // 0 fixed intervals, 1 diatonic
        for (var i = 0; i < 4; i++) _fixed[i] = p.Int($"V{i + 1}", i == 0 ? 5 : 0, -24, 24);
        _root = p.Int("Root", 0, 0, 11);
        _scale = MidiScales.Get(p.Int("Scale", 1, 0, 11));
        _mask = MidiScales.Mask(_root, p.Int("Scale", 1, 0, 11));
        _st2 = p.Int("Step2", 2, -24, 24);
        _st3 = p.Int("Step3", 4, -24, 24);
        _velScale = p.Double("VelScale", 1, 0, 1);
        _low = p.Int("Low", 0, 0, 127);
        _high = p.Int("High", 127, 0, 127);
    }

    public void Process(MidiBuffer input, MidiBuffer output, in MidiContext ctx)
    {
        Span<int> voices = stackalloc int[5];
        for (var i = 0; i < input.Count; i++)
        {
            var e = input.Items[i];
            var st = e.Status;
            var kind = st & 0xF0;
            if (st >= 0xF0 || kind is not (0x80 or 0x90) || (_inCh != 0 && (st & 0x0F) != _inCh - 1)) { output.Add(e); continue; }
            var ch = st & 0x0F; var key = (ch << 7) | (e.Data1 & 0x7F);
            if (kind == 0x90 && e.Data2 > 0)
            {
                Release(key, ch, e.Frame, output, 0);
                var n = 0; voices[n++] = e.Data1;
                if (e.Data1 >= _low && e.Data1 <= _high)
                {
                    if (_mode == 0) { for (var v = 0; v < 4; v++) if (_fixed[v] != 0) voices[n++] = e.Data1 + _fixed[v]; }
                    else
                    {
                        var d = MidiScales.DegreeOf(_root, _scale, _mask, e.Data1);
                        if (_st2 != 0) voices[n++] = MidiScales.NoteOfDegree(_root, _scale, d + _st2);
                        if (_st3 != 0) voices[n++] = MidiScales.NoteOfDegree(_root, _scale, d + _st3);
                    }
                }
                var stored = 0;
                for (var v = 0; v < n; v++)
                {
                    var note = voices[v];
                    if (note < 0 || note > 127) continue;
                    var vel = v == 0 ? e.Data2 : (int)Math.Clamp(Math.Round(e.Data2 * _velScale), 1, 127);
                    _notes[key * 5 + stored++] = (byte)note;
                    output.Add(e.Frame, (byte)(0x90 | ch), (byte)note, (byte)vel);
                }
                _cnt[key] = stored;
            }
            else if (_cnt[key] > 0) Release(key, ch, e.Frame, output, e.Data2);
            else output.Add(e);
        }
    }

    private void Release(int key, int ch, int frame, MidiBuffer output, int vel)
    {
        for (var v = 0; v < _cnt[key]; v++) output.Add(frame, (byte)(0x80 | ch), _notes[key * 5 + v], (byte)vel);
        _cnt[key] = 0;
    }

    public void Adopt(IMidiProcessor previous, bool sameParameters)
    {
        if (previous is not ChordProcessor p) return;
        Array.Copy(p._cnt, _cnt, _cnt.Length); Array.Copy(p._notes, _notes, _notes.Length);
    }

    public void Reset() => Array.Clear(_cnt);
}

/// <summary>Note randomizer: the trigger note is (with the mix probability) replaced by a random note in a range.</summary>
public sealed class NoteRandomizerProcessor : NoteTransform
{
    private readonly int _trigger, _low, _high, _mix;
    private MidiRandom _rng = new(24680);

    public NoteRandomizerProcessor(MidiParams p)
    {
        InCh = p.Int("InCh", 0, 0, 16);
        _trigger = p.Int("Trigger", 60, 0, 127);
        _low = p.Int("Low", 48, 0, 127);
        _high = Math.Max(_low, p.Int("High", 72, 0, 127));
        _mix = p.Int("Mix", 100, 0, 100);
    }

    protected override bool MapNote(ref int ch, ref int note, ref int vel)
    {
        if (note == _trigger && _rng.Next() % 100 < _mix) note = _low + (int)(_rng.Next() % (uint)(_high - _low + 1));
        return true;
    }
}

/// <summary>Pattern / scale randomizer: notes in a range become scale degrees over an octave range, random or as an ascending / descending run.</summary>
public sealed class ScaleVariationProcessor : NoteTransform
{
    private readonly int _low, _high, _root, _mode, _lowOct, _highOct;
    private readonly int[] _scale;
    private MidiRandom _rng = new(13579);
    private int _step;

    public ScaleVariationProcessor(MidiParams p)
    {
        InCh = p.Int("InCh", 0, 0, 16);
        _low = p.Int("Low", 0, 0, 127);
        _high = p.Int("High", 127, 0, 127);
        _root = p.Int("Root", 0, 0, 11);
        _scale = MidiScales.Get(p.Int("Scale", 1, 0, 11));
        _lowOct = p.Int("LowOct", 4, 0, 10);
        _highOct = Math.Max(_lowOct, p.Int("HighOct", 5, 0, 10));
        _mode = p.Int("Mode", 0, 0, 2);   // 0 random, 1 ascending run, 2 descending run
    }

    protected override bool MapNote(ref int ch, ref int note, ref int vel)
    {
        if (note < _low || note > _high) return true;
        var len = _scale.Length;
        var count = (_highOct - _lowOct + 1) * len;
        int d;
        if (_mode == 0) d = (int)(_rng.Next() % (uint)count);
        else { d = _mode == 1 ? _step % count : count - 1 - _step % count; _step++; }
        note = Math.Min(127, MidiScales.NoteOfDegree(_root, _scale, _lowOct * len + d));
        return true;
    }

    public override void Reset() { base.Reset(); _step = 0; }
}

/// <summary>Velocity variation: base velocity (0 = keep) with random variation and an accent pattern advanced by every note-on.</summary>
public sealed class VelocityVariationProcessor : NoteTransform
{
    private readonly int _base, _var, _accentAmount, _low, _high;
    private readonly int[] _accent;
    private MidiRandom _rng = new(97531);
    private int _step;

    public VelocityVariationProcessor(MidiParams p)
    {
        InCh = p.Int("InCh", 0, 0, 16);
        _base = p.Int("Base", 0, 0, 127);
        _var = p.Int("Variation", 20, 0, 100);
        _accentAmount = p.Int("AccentAmount", 25, 0, 127);
        var a = MidiScales.ParseInts(p.String("Accent", "1 0 0 0"), 0, 1, 0);
        _accent = a.Length == 0 ? new[] { 0 } : a;
        _low = p.Int("Low", 0, 0, 127);
        _high = p.Int("High", 127, 0, 127);
    }

    protected override bool MapNote(ref int ch, ref int note, ref int vel)
    {
        if (note < _low || note > _high) return true;
        double v = _base > 0 ? _base : vel;
        if (_accent[_step % _accent.Length] == 1) v += _accentAmount;
        _step++;
        v += _rng.Bipolar() * _var / 100.0 * 63;
        vel = (int)Math.Clamp(Math.Round(v), 1, 127);
        return true;
    }

    public override void Reset() { base.Reset(); _step = 0; }
}
