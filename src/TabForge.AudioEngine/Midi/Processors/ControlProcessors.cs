namespace TabForge.AudioEngine.Midi.Processors;

/// <summary>Passes everything through; sends its own messages first (frame 0) when it is created, when playback starts, or when its parameters changed.</summary>
public abstract class SenderProcessor : IMidiProcessor
{
    private bool _pending;
    private readonly bool _onStart;

    protected SenderProcessor(bool onLoad, bool onStart) { _pending = onLoad; _onStart = onStart; }

    protected abstract void Emit(MidiBuffer output);

    public void Process(MidiBuffer input, MidiBuffer output, in MidiContext ctx)
    {
        if (ctx.PlayStarted && _onStart) _pending = true;
        if (_pending) { _pending = false; Emit(output); }
        for (var i = 0; i < input.Count; i++) output.Add(input.Items[i]);
    }

    public void Adopt(IMidiProcessor previous, bool sameParameters)
    {
        if (previous is SenderProcessor p && sameParameters) _pending = p._pending;
        else _pending = true;   // edited: send the new values now, so the change is heard
    }

    public void Reset() { }
}

/// <summary>Bank select (CC0 / CC32) and program change on a channel; -1 leaves a part out.</summary>
public sealed class ProgramBankProcessor : SenderProcessor
{
    private readonly int _ch, _msb, _lsb, _program;

    public ProgramBankProcessor(MidiParams p) : base(p.Bool("OnLoad", true), p.Bool("OnStart", true))
    {
        _ch = p.Int("Channel", 1, 1, 16) - 1;
        _msb = p.Int("Msb", -1, -1, 127);
        _lsb = p.Int("Lsb", -1, -1, 127);
        _program = p.Int("Program", 0, -1, 127);
    }

    protected override void Emit(MidiBuffer output)
    {
        if (_msb >= 0) output.Add(0, (byte)(0xB0 | _ch), 0, (byte)_msb);
        if (_lsb >= 0) output.Add(0, (byte)(0xB0 | _ch), 32, (byte)_lsb);
        if (_program >= 0) output.Add(0, (byte)(0xC0 | _ch), (byte)_program, 0);
    }
}

/// <summary>Up to four controller values (CC number -1 = unused) sent on a channel.</summary>
public sealed class CcSenderProcessor : SenderProcessor
{
    private readonly int _ch;
    private readonly int[] _cc = new int[4], _val = new int[4];

    public CcSenderProcessor(MidiParams p) : base(p.Bool("OnLoad", true), p.Bool("OnStart", true))
    {
        _ch = p.Int("Channel", 1, 1, 16) - 1;
        for (var i = 0; i < 4; i++)
        {
            _cc[i] = p.Int($"Cc{i + 1}", i == 0 ? 7 : -1, -1, 127);
            _val[i] = p.Int($"Value{i + 1}", 100, 0, 127);
        }
    }

    protected override void Emit(MidiBuffer output)
    {
        for (var i = 0; i < 4; i++) if (_cc[i] >= 0) output.Add(0, (byte)(0xB0 | _ch), (byte)_cc[i], (byte)_val[i]);
    }
}

/// <summary>Panic: all notes off and all sound off on every channel when playback stops (and/or starts). The chain also releases every note it let through.</summary>
public sealed class PanicProcessor : IMidiProcessor
{
    private readonly bool _onStop, _onStart;

    public PanicProcessor(MidiParams p) { _onStop = p.Bool("OnStop", true); _onStart = p.Bool("OnStart", false); }

    public void Process(MidiBuffer input, MidiBuffer output, in MidiContext ctx)
    {
        if ((ctx.PlayStopped && _onStop) || (ctx.PlayStarted && _onStart))
            for (var ch = 0; ch < 16; ch++)
            {
                output.Add(0, (byte)(0xB0 | ch), 123, 0);
                output.Add(0, (byte)(0xB0 | ch), 120, 0);
            }
        for (var i = 0; i < input.Count; i++) output.Add(input.Items[i]);
    }

    public void Adopt(IMidiProcessor previous, bool sameParameters) { }
    public void Reset() { }
}

/// <summary>CC mapper: controller in to controller out, value clamped (not scaled); optionally the source passes on too.</summary>
public sealed class CcMapperProcessor : IMidiProcessor
{
    private readonly int _ch, _src, _dst, _lo, _hi;
    private readonly bool _pass;

    public CcMapperProcessor(MidiParams p)
    {
        _ch = p.Int("InCh", 0, 0, 16);
        _src = p.Int("Source", 1, 0, 127);
        _dst = p.Int("Target", 1, 0, 127);
        _lo = p.Int("ClampLow", 0, 0, 127);
        _hi = p.Int("ClampHigh", 127, 0, 127);
        _pass = p.Bool("PassSource", false);
    }

    public void Process(MidiBuffer input, MidiBuffer output, in MidiContext ctx)
    {
        for (var i = 0; i < input.Count; i++)
        {
            var e = input.Items[i];
            if (e.Status < 0xF0 && (e.Status & 0xF0) == 0xB0 && e.Data1 == _src && (_ch == 0 || (e.Status & 0x0F) == _ch - 1))
            {
                if (_pass && _dst != _src) output.Add(e);
                var v = Math.Clamp((int)e.Data2, Math.Min(_lo, _hi), Math.Max(_lo, _hi));
                output.Add(e.Frame, e.Status, (byte)_dst, (byte)v);
            }
            else output.Add(e);
        }
    }

    public void Adopt(IMidiProcessor previous, bool sameParameters) { }
    public void Reset() { }
}

/// <summary>MIDI log: copies what passes here into the engine-to-UI ring (only while the window watches). Pass-through, no latency.</summary>
public sealed class LogProcessor : IMidiProcessor
{
    private readonly int _show;
    private readonly byte _stage;

    public LogProcessor(MidiParams p, int stage) { _show = p.Int("Show", 0, 0, 3); _stage = (byte)stage; }

    public void Process(MidiBuffer input, MidiBuffer output, in MidiContext ctx)
    {
        var log = ctx.Log;
        var watching = log is { Watching: true };
        for (var i = 0; i < input.Count; i++)
        {
            var e = input.Items[i];
            output.Add(e);
            if (!watching) continue;
            var kind = e.Status & 0xF0;
            var isNote = e.Status < 0xF0 && kind is 0x80 or 0x90 or 0xA0;
            var isCc = e.Status < 0xF0 && kind == 0xB0;
            var pass = _show switch { 1 => isNote, 2 => isCc, 3 => !isNote && !isCc, _ => true };
            if (pass) log!.Write((float)(ctx.SongSec + (double)e.Frame / ctx.SampleRate), e.Status, e.Data1, e.Data2, _stage);
        }
    }

    public void Adopt(IMidiProcessor previous, bool sameParameters) { }
    public void Reset() { }
}
