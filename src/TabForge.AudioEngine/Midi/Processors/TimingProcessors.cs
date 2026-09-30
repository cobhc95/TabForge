namespace TabForge.AudioEngine.Midi.Processors;

/// <summary>
/// MIDI delay (milliseconds + beats + samples, summed). Note-ons and note-offs are delayed by the same amount, so notes stay
/// paired; "notes only" lets other messages pass straight through. Events past the block wait in the scheduler's carry-over ring.
/// </summary>
public sealed class DelayProcessor : IMidiProcessor
{
    private readonly double _ms, _beats;
    private readonly int _samples, _ch;
    private readonly bool _notesOnly;
    private readonly int _sampleRate;
    private readonly EventScheduler _sched = new();

    public DelayProcessor(MidiParams p, int sampleRate)
    {
        _sampleRate = sampleRate;
        _ms = p.Double("Ms", 100, 0, 1000);
        _beats = p.Double("Beats", 0, 0, 16);
        _samples = p.Int("Samples", 0, 0, 10000);
        _ch = p.Int("InCh", 0, 0, 16);
        _notesOnly = p.Bool("NotesOnly", false);
    }

    public void Process(MidiBuffer input, MidiBuffer output, in MidiContext ctx)
    {
        var tempo = ctx.Tempo > 1 ? ctx.Tempo : 120;
        var delay = (long)Math.Round(_ms * _sampleRate / 1000.0 + _beats * 60.0 / tempo * _sampleRate + _samples);
        for (var i = 0; i < input.Count; i++)
        {
            var e = input.Items[i];
            var isNote = e.Status < 0xF0 && (e.Status & 0xF0) is 0x80 or 0x90 or 0xA0;
            var delayed = e.Status < 0xF0 ? (_ch == 0 || (e.Status & 0x0F) == _ch - 1) && (!_notesOnly || isNote) : !_notesOnly;
            if (delay == 0 && !_sched.HasPending || !delayed) { output.Add(e); if (_sched.HasPending) output.Unsorted = true; continue; }
            if (!_sched.Schedule(ctx.BlockStart + e.Frame + delay, e.Status, e.Data1, e.Data2)) output.Add(e);
        }
        _sched.Drain(ctx.BlockStart, ctx.Frames, output);
    }

    public void Adopt(IMidiProcessor previous, bool sameParameters)
    {
        if (previous is DelayProcessor p) _sched.CopyFrom(p._sched);
    }

    public void Reset() => _sched.Reset();
}

/// <summary>
/// Humanizer: random timing (with a bias) and velocity. Early jitter needs a base delay, so everything is delayed by the base delay
/// and notes then move around it; a note-off gets the same shift as its note-on and never leaves before it.
/// </summary>
public sealed class HumanizeProcessor : IMidiProcessor
{
    private readonly double _timingMs, _biasMs, _baseMs;
    private readonly int _velRandom, _baseline, _ch, _sampleRate;
    private readonly EventScheduler _sched = new();
    private readonly long[] _shift = new long[2048];
    private MidiRandom _rng = new(987654321);

    public HumanizeProcessor(MidiParams p, int sampleRate)
    {
        _sampleRate = sampleRate;
        _timingMs = p.Double("TimingMs", 8, 0, 30);
        _biasMs = p.Double("BiasMs", 0, -10, 10);
        _baseMs = p.Double("BaseDelayMs", 20, 0, 100);
        _velRandom = p.Int("VelRandom", 8, 0, 64);
        _baseline = p.Int("Baseline", 0, 0, 127);   // 0 = keep the original velocity
        _ch = p.Int("InCh", 0, 0, 16);
    }

    public void Process(MidiBuffer input, MidiBuffer output, in MidiContext ctx)
    {
        var msToSamples = _sampleRate / 1000.0;
        var baseSamples = (long)Math.Round(_baseMs * msToSamples);
        for (var i = 0; i < input.Count; i++)
        {
            var e = input.Items[i];
            var st = e.Status; var d2 = e.Data2;
            var ours = st < 0xF0 && (_ch == 0 || (st & 0x0F) == _ch - 1) && (st & 0xF0) is 0x80 or 0x90;
            long shift = 0;
            if (ours)
            {
                var key = ((st & 0x0F) << 7) | (e.Data1 & 0x7F);
                if ((st & 0xF0) == 0x90 && d2 > 0)
                {
                    shift = (long)Math.Round((_biasMs + _rng.Bipolar() * _timingMs) * msToSamples);
                    _shift[key] = shift;
                    var v = _baseline > 0 ? _baseline : d2;
                    if (_velRandom > 0) v += (int)Math.Round(_rng.Bipolar() * _velRandom);
                    d2 = (byte)Math.Clamp(v, 1, 127);
                }
                else shift = _shift[key];
            }
            var when = ctx.BlockStart + e.Frame + Math.Max(0, baseSamples + shift);
            if (!_sched.Schedule(when, st, e.Data1, (byte)d2)) output.Add(e);
        }
        _sched.Drain(ctx.BlockStart, ctx.Frames, output);
    }

    public void Adopt(IMidiProcessor previous, bool sameParameters)
    {
        if (previous is not HumanizeProcessor p) return;
        _sched.CopyFrom(p._sched);
        Array.Copy(p._shift, _shift, _shift.Length);
    }

    public void Reset() { _sched.Reset(); Array.Clear(_shift); }
}
