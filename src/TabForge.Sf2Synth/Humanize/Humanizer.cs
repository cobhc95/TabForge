using static TabForge.Sf2Synth.Humanize.HumanTable;

namespace TabForge.Sf2Synth.Humanize;

// Owns: the MIDI pre-stage of Synthesizer: collects the events that arrive at one instant (a batch, so a chord is known whole),
//   plans each channel's stroke (Phrase, Strum; only families in HumanTable, never the drum channel) and schedules everything in a
//   sorted queue. While on, every event of every channel is held for the style's look-ahead (reported as Latency), so a note-on
//   can land early as well as late; note-ons never before an earlier event of their key or an earlier controller of their channel;
//   note-offs follow their stroke's shift plus a length change, never before their note-on plus min(length, MinKeepMs), in order
//   per key; controllers after everything earlier on their channel. Draws each note's Nuance. Allocation-free, deterministic.
// Does not own: playing the events (Synthesizer), the player model (Phrase, Strum, Evidence), the values (HumanTable).
// Tests: HumanizerTests.
public sealed class Humanizer(double rate)
{
    public struct Event { public long Due; public byte Channel, Command, Data1, Data2; }

    private const int BatchCap = 512, QueueCap = 4096, Keys = 16 * 128;
    private readonly Event[] _in = new Event[BatchCap], _q = new Event[QueueCap];
    private readonly int[] _bd = new int[BatchCap], _bs = new int[BatchCap], _idx = new int[128], _keyShift = new int[Keys];
    private readonly double[] _db = new double[128], _ms = new double[128];
    private readonly long[] _keyDue = new long[Keys], _keyAt = Enumerable.Repeat(-1L, Keys).ToArray(), _keyLast = new long[Keys], _chMax = new long[16], _chCtl = new long[16];
    private readonly int[] _pending = new int[16], _program = new int[16], _count = new int[16];
    private readonly bool[] _expressive = new bool[16];
    private readonly Phrase[] _ph = Enumerable.Range(0, 16).Select(_ => new Phrase()).ToArray();
    private readonly long _minKeep = (long)(MinKeepMs * rate / 1000);
    private int _nIn, _head, _tail;

    /// <summary>The longest delay any event can get, in frames: two look-aheads (early budget, late cap) plus the longest strum.</summary>
    public int MaxDelay { get; } = Frames(2 * MaxLookMs + StrumCapMs, rate);
    public bool Busy => _nIn > 0 || _tail > _head;
    public long NextDue => _tail > _head ? _q[_head].Due : long.MaxValue;
    /// <summary>The look-ahead every event is held for while on (frames); a host shifts the output back by it.</summary>
    public int Latency(SynthOptions o) => o.Humanize ? Frames(Styles[(int)o.Feel].LookMs, rate) : 0;
    /// <summary>The channel's flatness (velocity, timing) 0..1 as last measured.</summary>
    public (double Velocity, double Timing) Flatness(int channel) => (_ph[channel & 15].Evidence.VelFlat, _ph[channel & 15].Evidence.TimeFlat);
    /// <summary>The channel's ghost strokes since Reset.</summary>
    public int Ghosts(int channel) => _ph[channel & 15].Ghosts;

    private static int Frames(double ms, double rate) => (int)Math.Round(ms * rate / 1000);

    public void Reset()
    {
        (_nIn, _head, _tail) = (0, 0, 0);
        Array.Fill(_keyAt, -1);
        Array.Clear(_keyDue); Array.Clear(_keyLast); Array.Clear(_keyShift); Array.Clear(_chMax); Array.Clear(_chCtl);
        Array.Clear(_pending); Array.Clear(_program); Array.Clear(_count); Array.Clear(_expressive);
        for (var i = 0; i < 16; i++) _ph[i].Reset(i);
    }

    /// <summary>True: queued (played later by Take). False: play it now (off, and nothing of its channel is waiting).</summary>
    public bool Add(int channel, int command, int d1, int d2, bool on)
    {
        if (command == 0xC0) _program[channel] = d1 & 0x7F;
        if (command == 0xB0 && d1 == 1 && d2 > 0 || command == 0xE0 && ((d1 & 0x7F) | (d2 & 0x7F) << 7) != 8192) _expressive[channel] = true;
        if (!on && _pending[channel] == 0 || _nIn == BatchCap) return false;
        _in[_nIn++] = new Event { Channel = (byte)channel, Command = (byte)command, Data1 = (byte)d1, Data2 = (byte)d2 };
        _pending[channel]++;
        return true;
    }

    /// <summary>Plans and schedules the batch that arrived at frame <paramref name="now"/>.</summary>
    public void Commit(long now, SynthOptions o)
    {
        if (_nIn == 0) return;
        var lat = Latency(o);
        Array.Clear(_bd, 0, _nIn);
        Array.Clear(_bs, 0, _nIn);
        for (var ch = 0; ch < 16; ch++) Plan(ch, now, o, lat);
        for (var i = 0; i < _nIn; i++) Schedule(_in[i], now, lat, i, o);
        _nIn = 0;
    }

    private void Plan(int ch, long now, SynthOptions o, int lat)
    {
        var n = 0;
        for (var i = 0; i < _nIn && n < 128; i++) if (_in[i].Channel == ch && _in[i].Command == 0x90 && _in[i].Data2 > 0) _idx[n++] = i;
        if (n == 0 || lat == 0 || ch == 9 || Of(_program[ch]) is not { } f) return;
        for (var i = 1; i < n; i++)
            for (var j = i; j > 0 && _in[_idx[j]].Data1 < _in[_idx[j - 1]].Data1; j--) (_idx[j], _idx[j - 1]) = (_idx[j - 1], _idx[j]);
        var ph = _ph[ch];
        var s = Styles[(int)o.Feel];
        ph.Evidence.AddOnset(now);
        var top = 1;
        for (var i = 0; i < n; i++) { ph.Evidence.AddNote(_in[_idx[i]].Data2); top = Math.Max(top, _in[_idx[i]].Data2); }
        ph.Evidence.Update(rate);
        var vf = o.HumanizeVelocity ? ph.Evidence.VelFlat : 0;
        var tf = o.HumanizeTiming ? ph.Evidence.TimeFlat : 0;
        var st = ph.Plan(f, s, now, rate, n, _in[_idx[0]].Data1, o.HumanizeDrift, lat * 1000.0 / rate);
        Strum.Spread(f, s, st, 40 * Math.Log10(top / 127.0), _db.AsSpan(0, n), _ms.AsSpan(0, n), ph.Rng);
        var shift = (int)Math.Round(tf * st.Ms * rate / 1000);
        for (var i = 0; i < n; i++)
        {
            ref var e = ref _in[_idx[i]];
            if (vf > 0) e.Data2 = (byte)Math.Clamp((int)Math.Round(e.Data2 * Math.Pow(10, vf * (st.Db + _db[i]) / 40)), 1, 127);
            _bs[_idx[i]] = shift;
            _bd[_idx[i]] = shift + (int)Math.Round(tf * _ms[i] * rate / 1000);
        }
    }

    private void Schedule(Event e, long now, int lat, int i, SynthOptions o)
    {
        int ch = e.Channel, k = ch * 128 + (e.Data1 & 0x7F);
        long due;
        if (e.Command == 0x90 && e.Data2 > 0)
        {
            due = Math.Max(now + Math.Max(0, lat + _bd[i]), Math.Max(_keyLast[k], _chCtl[ch]));
            (_keyDue[k], _keyAt[k], _keyLast[k], _keyShift[k]) = (due, now, due, _bs[i]);
        }
        else if (e.Command is 0x90 or 0x80)
        {
            due = now + lat;
            if (_keyAt[k] >= 0)
            {
                var len = now - _keyAt[k];
                if (lat > 0) due += _keyShift[k] + Stretch(ch, len, now, o);
                due = Math.Max(due, _keyDue[k] + Math.Min(len, _minKeep));
                _ph[ch].Ended(len);
                _keyAt[k] = -1;
            }
            _keyLast[k] = due = Math.Max(Math.Max(due, now), Math.Max(_keyLast[k], _chCtl[ch]));
        }
        else _chCtl[ch] = due = Math.Max(now + lat, _chMax[ch]);
        _chMax[ch] = Math.Max(_chMax[ch], due);
        e.Due = due;
        Insert(e);
    }

    // A note's length change in frames: legato overlap or a gap, the same for every key the channel releases at this instant.
    private long Stretch(int ch, long len, long now, SynthOptions o)
    {
        if (!o.HumanizeTiming || ch == 9 || Of(_program[ch]) is null) return 0;
        var h = (uint)(now * 0x9E3779B1L) ^ (uint)(ch + 1) * 0x85EBCA6Bu;
        h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12;
        var u = (h & 0xFFFFFF) / (double)0x800000 - 1;
        var ms = Math.Min(len * 1000.0 / rate * LenFrac, LenMaxMs) * Styles[(int)o.Feel].KMs * _ph[ch].Evidence.TimeFlat * u;
        return (long)Math.Round(ms * rate / 1000);
    }

    private void Insert(Event e)
    {
        if (_tail == QueueCap && _head > 0) { Array.Copy(_q, _head, _q, 0, _tail - _head); (_tail, _head) = (_tail - _head, 0); }
        if (_tail == QueueCap) { _pending[e.Channel]--; return; }
        var j = _tail++;
        for (; j > _head && _q[j - 1].Due > e.Due; j--) _q[j] = _q[j - 1];
        _q[j] = e;
    }

    /// <summary>The next event due at or before <paramref name="now"/>, in order.</summary>
    public bool Take(long now, out Event e)
    {
        e = default;
        if (_tail == _head || _q[_head].Due > now) return false;
        e = _q[_head++];
        _pending[e.Channel]--;
        if (_head == _tail) _head = _tail = 0;
        return true;
    }

    /// <summary>This note's voice variation (default when off, on the drum channel or for an untouched family).</summary>
    public Nuance Vary(int channel, int key, SynthOptions o)
    {
        if (!o.Humanize || channel == 9 || Of(_program[channel]) is not { } f) return default;
        var vib = o.HumanizeDrift && !_expressive[channel] && f.VibCents > 0;
        return o.HumanizeVoice || vib ? Nuance.Of(f, channel, key, _count[channel]++, o.HumanizeVoice, vib) : default;
    }
}
