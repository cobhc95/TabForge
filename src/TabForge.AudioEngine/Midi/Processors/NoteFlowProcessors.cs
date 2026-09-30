namespace TabForge.AudioEngine.Midi.Processors;

/// <summary>Choke: a note-on in the choke set ends every sounding note of the affected set; while a choke note is held, affected note-ons are blocked (or allowed).</summary>
public sealed class ChokeProcessor : IMidiProcessor
{
    private readonly int _inCh, _cStart, _cCount, _aStart, _aCount, _action;
    private readonly int[] _extra = new int[4];
    private readonly bool[] _chokeHeld = new bool[128], _aSounding = new bool[128], _swallow = new bool[128];
    private readonly byte[] _aCh = new byte[128];
    private int _chokeCount;

    public ChokeProcessor(MidiParams p)
    {
        _inCh = p.Int("InCh", 0, 0, 16);
        _cStart = p.Int("ChokeStart", 42, 0, 127); _cCount = p.Int("ChokeCount", 1, 1, 16);
        _aStart = p.Int("AffStart", 46, 0, 127); _aCount = p.Int("AffCount", 1, 1, 16);
        _action = p.Int("Action", 0, 0, 1);   // 0 block, 1 allow
        for (var i = 0; i < 4; i++) _extra[i] = p.Int($"Extra{i + 1}", -1, -1, 127);
    }

    private bool IsChoke(int n) => (n >= _cStart && n < _cStart + _cCount) || n == _extra[0] || n == _extra[1] || n == _extra[2] || n == _extra[3];
    private bool IsAffected(int n) => n >= _aStart && n < _aStart + _aCount;

    public void Process(MidiBuffer input, MidiBuffer output, in MidiContext ctx)
    {
        for (var i = 0; i < input.Count; i++)
        {
            var e = input.Items[i];
            var st = e.Status; var kind = st & 0xF0;
            if (st >= 0xF0 || kind is not (0x80 or 0x90) || (_inCh != 0 && (st & 0x0F) != _inCh - 1)) { output.Add(e); continue; }
            int n = e.Data1 & 0x7F, ch = st & 0x0F;
            if (kind == 0x90 && e.Data2 > 0)
            {
                if (IsChoke(n))
                {
                    if (!_chokeHeld[n]) { _chokeHeld[n] = true; _chokeCount++; }
                    for (var a = 0; a < 128; a++)
                        if (_aSounding[a]) { output.Add(e.Frame, (byte)(0x80 | _aCh[a]), (byte)a, 0); _aSounding[a] = false; _swallow[a] = true; }
                    output.Add(e);
                }
                else if (IsAffected(n))
                {
                    if (_chokeCount > 0 && _action == 0) { _swallow[n] = true; continue; }
                    _swallow[n] = false; _aSounding[n] = true; _aCh[n] = (byte)ch;
                    output.Add(e);
                }
                else output.Add(e);
            }
            else
            {
                if (IsChoke(n)) { if (_chokeHeld[n]) { _chokeHeld[n] = false; _chokeCount--; } output.Add(e); }
                else if (IsAffected(n))
                {
                    if (_swallow[n]) { _swallow[n] = false; continue; }
                    _aSounding[n] = false;
                    output.Add(e);
                }
                else output.Add(e);
            }
        }
    }

    public void Adopt(IMidiProcessor previous, bool sameParameters)
    {
        if (previous is not ChokeProcessor p) return;
        Array.Copy(p._chokeHeld, _chokeHeld, 128); Array.Copy(p._aSounding, _aSounding, 128); Array.Copy(p._swallow, _swallow, 128); Array.Copy(p._aCh, _aCh, 128);
        _chokeCount = p._chokeCount;
    }

    public void Reset() { Array.Clear(_chokeHeld); Array.Clear(_aSounding); Array.Clear(_swallow); _chokeCount = 0; }
}

/// <summary>Choke group: notes of a range are mutually exclusive (a note-on ends the previous one); note-offs in the range are swallowed (one-shots).</summary>
public sealed class ChokeGroupProcessor : IMidiProcessor
{
    private readonly int _inCh, _start, _count;
    private int _cur = -1, _curCh;

    public ChokeGroupProcessor(MidiParams p)
    {
        _inCh = p.Int("InCh", 0, 0, 16);
        _start = p.Int("Start", 60, 0, 127);
        _count = p.Int("Count", 8, 1, 128);
    }

    public void Process(MidiBuffer input, MidiBuffer output, in MidiContext ctx)
    {
        for (var i = 0; i < input.Count; i++)
        {
            var e = input.Items[i];
            var st = e.Status; var kind = st & 0xF0;
            if (st >= 0xF0 || kind is not (0x80 or 0x90) || (_inCh != 0 && (st & 0x0F) != _inCh - 1) || e.Data1 < _start || e.Data1 >= _start + _count) { output.Add(e); continue; }
            if (kind == 0x90 && e.Data2 > 0)
            {
                if (_cur >= 0) output.Add(e.Frame, (byte)(0x80 | _curCh), (byte)_cur, 0);
                _cur = e.Data1; _curCh = st & 0x0F;
                output.Add(e);
            }
        }
    }

    public void Adopt(IMidiProcessor previous, bool sameParameters)
    {
        if (previous is ChokeGroupProcessor p) { _cur = p._cur; _curCh = p._curCh; }
    }

    public void Reset() => _cur = -1;
}

/// <summary>Note sanitizer / duplicate-note filter: a note-on while the note is already on is dropped (with its extra note-off); with a threshold it retriggers instead once that many 1/32 notes have passed.</summary>
public sealed class NoteSanitizerProcessor : IMidiProcessor
{
    private readonly int _inCh, _threshold;
    private readonly bool[] _on = new bool[2048];
    private readonly int[] _extra = new int[2048];
    private readonly long[] _since = new long[2048];

    public NoteSanitizerProcessor(MidiParams p)
    {
        _inCh = p.Int("InCh", 0, 0, 16);
        _threshold = p.Int("Retrigger", 0, 0, 128);
    }

    public void Process(MidiBuffer input, MidiBuffer output, in MidiContext ctx)
    {
        var tempo = ctx.Tempo > 1 ? ctx.Tempo : 120;
        var limit = _threshold * (ctx.SampleRate * 60.0 / tempo) / 8.0;   // 1/32 note = 1/8 beat
        for (var i = 0; i < input.Count; i++)
        {
            var e = input.Items[i];
            var st = e.Status; var kind = st & 0xF0;
            if (st >= 0xF0 || kind is not (0x80 or 0x90) || (_inCh != 0 && (st & 0x0F) != _inCh - 1)) { output.Add(e); continue; }
            var key = ((st & 0x0F) << 7) | (e.Data1 & 0x7F);
            var abs = ctx.BlockStart + e.Frame;
            if (kind == 0x90 && e.Data2 > 0)
            {
                if (_on[key])
                {
                    if (_threshold > 0 && abs - _since[key] >= limit) output.Add(e.Frame, (byte)(0x80 | (st & 0x0F)), e.Data1, 0);
                    else { _extra[key]++; continue; }
                }
                _on[key] = true; _since[key] = abs;
                output.Add(e);
            }
            else
            {
                if (_extra[key] > 0) { _extra[key]--; continue; }
                if (!_on[key]) continue;
                _on[key] = false;
                output.Add(e);
            }
        }
    }

    public void Adopt(IMidiProcessor previous, bool sameParameters)
    {
        if (previous is not NoteSanitizerProcessor p) return;
        Array.Copy(p._on, _on, _on.Length); Array.Copy(p._extra, _extra, _extra.Length); Array.Copy(p._since, _since, _since.Length);
    }

    public void Reset() { Array.Clear(_on); Array.Clear(_extra); }
}

/// <summary>Note hold: mono legato hold. A new note-on ends the previous note first; note-offs are swallowed, so the last note keeps sounding until the next one (or transport stop).</summary>
public sealed class NoteHoldProcessor : IMidiProcessor
{
    private readonly int _inCh;
    private readonly int[] _last = new int[16];

    public NoteHoldProcessor(MidiParams p)
    {
        _inCh = p.Int("InCh", 0, 0, 16);
        Array.Fill(_last, -1);
    }

    public void Process(MidiBuffer input, MidiBuffer output, in MidiContext ctx)
    {
        for (var i = 0; i < input.Count; i++)
        {
            var e = input.Items[i];
            var st = e.Status; var kind = st & 0xF0;
            if (st >= 0xF0 || kind is not (0x80 or 0x90) || (_inCh != 0 && (st & 0x0F) != _inCh - 1)) { output.Add(e); continue; }
            var ch = st & 0x0F;
            if (kind == 0x90 && e.Data2 > 0)
            {
                if (_last[ch] >= 0) output.Add(e.Frame, (byte)(0x80 | ch), (byte)_last[ch], 0);
                _last[ch] = e.Data1;
                output.Add(e);
            }
        }
    }

    public void Adopt(IMidiProcessor previous, bool sameParameters)
    {
        if (previous is NoteHoldProcessor p) Array.Copy(p._last, _last, 16);
    }

    public void Reset() => Array.Fill(_last, -1);
}
