namespace TabForge.Sf2Synth.Humanize;

// Owns: the MIDI pre-stage of Synthesizer: collects the events that arrive at one instant (a batch, so a chord is known whole),
//   plans each channel's group (Phrase, only for families in HumanTable and never the drum channel), and schedules everything in
//   a sorted queue: note-ons only later (bounded by MaxDelay), never before an earlier event of the same key, after the channel's
//   earlier controllers; note-offs never before their note-on plus min(length, MinKeepMs), otherwise on time; other events keep
//   their order per channel. Draws each note's Nuance. Allocation-free, deterministic (Reset clears every state and count).
// Does not own: playing the events (Synthesizer), the player model (Phrase, Evidence), the values (HumanTable).
// Tests: HumanizerTests.
public sealed class Humanizer(double rate)
{
    public struct Event { public long Due; public byte Channel, Command, Data1, Data2; }

    private const int BatchCap = 512, QueueCap = 4096, Keys = 16 * 128;
    private readonly Event[] _in = new Event[BatchCap], _q = new Event[QueueCap];
    private readonly int[] _bd = new int[BatchCap], _idx = new int[128], _keys = new int[128], _vel = new int[128], _delay = new int[128];
    private readonly long[] _keyDue = new long[Keys], _keyAt = Enumerable.Repeat(-1L, Keys).ToArray(), _keyLast = new long[Keys], _chMax = new long[16], _chCtl = new long[16];
    private readonly int[] _pending = new int[16], _program = new int[16], _count = new int[16];
    private readonly bool[] _expressive = new bool[16];
    private readonly Phrase[] _ph = Enumerable.Range(0, 16).Select(_ => new Phrase()).ToArray();
    private readonly long _minKeep = (long)(HumanTable.MinKeepMs * rate / 1000);
    private int _nIn, _head, _tail;

    /// <summary>The longest delay any note-on can get, in frames (a host may report it as latency).</summary>
    public int MaxDelay { get; } = (int)Math.Ceiling(HumanTable.MaxDelayMs * rate / 1000);
    /// <summary>The longest delay given since Reset, in frames.</summary>
    public int Largest { get; private set; }
    public bool Busy => _nIn > 0 || _tail > _head;
    public long NextDue => _tail > _head ? _q[_head].Due : long.MaxValue;
    /// <summary>The channel's flatness (velocity, timing) 0..1 as last measured.</summary>
    public (double Velocity, double Timing) Flatness(int channel) => (_ph[channel & 15].Evidence.VelFlat, _ph[channel & 15].Evidence.TimeFlat);

    public void Reset()
    {
        (_nIn, _head, _tail, Largest) = (0, 0, 0, 0);
        Array.Fill(_keyAt, -1);
        Array.Clear(_keyDue); Array.Clear(_keyLast); Array.Clear(_chMax); Array.Clear(_chCtl);
        Array.Clear(_pending); Array.Clear(_program); Array.Clear(_count); Array.Clear(_expressive);
        for (var i = 0; i < 16; i++) _ph[i].Reset(i);
    }

    /// <summary>True: queued (played later by Take). False: play it now (nothing of its channel is waiting).</summary>
    public bool Add(int channel, int command, int d1, int d2, bool on)
    {
        if (command == 0xC0) _program[channel] = d1 & 0x7F;
        if (command == 0xB0 && d1 == 1 && d2 > 0 || command == 0xE0 && ((d1 & 0x7F) | (d2 & 0x7F) << 7) != 8192) _expressive[channel] = true;
        var mine = on && command is 0x90 or 0x80 && channel != 9 && HumanTable.Of(_program[channel]) is not null;
        if (!mine && _pending[channel] == 0 || _nIn == BatchCap) return false;
        _in[_nIn++] = new Event { Channel = (byte)channel, Command = (byte)command, Data1 = (byte)d1, Data2 = (byte)d2 };
        _pending[channel]++;
        return true;
    }

    /// <summary>Plans and schedules the batch that arrived at frame <paramref name="now"/>.</summary>
    public void Commit(long now, SynthOptions o)
    {
        if (_nIn == 0) return;
        Array.Clear(_bd, 0, _nIn);
        for (var ch = 0; ch < 16; ch++) Plan(ch, now, o);
        for (var i = 0; i < _nIn; i++) Schedule(_in[i], now, _bd[i]);
        _nIn = 0;
    }

    private void Plan(int ch, long now, SynthOptions o)
    {
        var n = 0;
        for (var i = 0; i < _nIn && n < 128; i++) if (_in[i].Channel == ch && _in[i].Command == 0x90 && _in[i].Data2 > 0) _idx[n++] = i;
        if (n == 0 || !o.Humanize || ch == 9 || HumanTable.Of(_program[ch]) is not { } p) return;
        for (var i = 1; i < n; i++)
            for (var j = i; j > 0 && _in[_idx[j]].Data1 < _in[_idx[j - 1]].Data1; j--) (_idx[j], _idx[j - 1]) = (_idx[j - 1], _idx[j]);
        var ph = _ph[ch];
        ph.Evidence.AddOnset(now);
        for (var i = 0; i < n; i++) { (_keys[i], _vel[i]) = (_in[_idx[i]].Data1, _in[_idx[i]].Data2); ph.Evidence.AddNote(_vel[i]); }
        ph.Evidence.Update(rate);
        ph.Plan(p, now, rate, _keys.AsSpan(0, n), _vel.AsSpan(0, n), _delay.AsSpan(0, n), o.HumanizeVelocity, o.HumanizeTiming, o.HumanizeDrift);
        for (var i = 0; i < n; i++) { _in[_idx[i]].Data2 = (byte)_vel[i]; _bd[_idx[i]] = _delay[i]; }
    }

    private void Schedule(Event e, long now, int delay)
    {
        int ch = e.Channel, k = ch * 128 + (e.Data1 & 0x7F);
        long due;
        if (e.Command == 0x90 && e.Data2 > 0)
        {
            due = Math.Max(now + delay, Math.Max(_keyLast[k], _chCtl[ch]));
            (_keyDue[k], _keyAt[k], _keyLast[k]) = (due, now, due);
            Largest = Math.Max(Largest, (int)(due - now));
        }
        else if (e.Command is 0x90 or 0x80)
        {
            due = Math.Max(now, _keyLast[k]);
            if (_keyAt[k] >= 0) { due = Math.Max(due, _keyDue[k] + Math.Min(now - _keyAt[k], _minKeep)); _ph[ch].Ended(now - _keyAt[k]); _keyAt[k] = -1; }
            _keyLast[k] = due;
        }
        else _chCtl[ch] = due = Math.Max(now, _chMax[ch]);
        _chMax[ch] = Math.Max(_chMax[ch], due);
        e.Due = due;
        Insert(e);
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
        if (!o.Humanize || channel == 9 || HumanTable.Of(_program[channel]) is not { } p) return default;
        var vib = o.HumanizeDrift && !_expressive[channel] && p.VibCents > 0;
        return o.HumanizeVoice || vib ? Nuance.Of(p, channel, key, _count[channel]++, o.HumanizeVoice, vib) : default;
    }
}
