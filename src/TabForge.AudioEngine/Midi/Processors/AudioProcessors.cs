using TabForge.Audio.Contracts;
using TabForge.AudioEngine.Plugins;

namespace TabForge.AudioEngine.Midi.Processors;

/// <summary>
/// A MIDI processor that also sees (and may change) the audio at its position in the plug-in chain: the audio going into the
/// plug-in that follows it. Runs on the audio thread and must not allocate. The chain calls this instead of
/// <see cref="IMidiProcessor.Process"/> when audio is available; <c>ctx.Frames</c> is the block length.
/// </summary>
public interface IAudioAwareProcessor
{
    void ProcessAudio(MidiBuffer input, MidiBuffer output, in MidiContext ctx, Span<float> left, Span<float> right);
}

internal static class AudioProcUtil
{
    public static void PassThrough(MidiBuffer input, MidiBuffer output)
    {
        for (var i = 0; i < input.Count; i++) output.Add(input.Items[i]);
        if (input.Unsorted) output.Unsorted = true;
    }

    public static float MsCoef(double ms, int sr) => ms <= 0 ? 1f : (float)(1 - Math.Exp(-1.0 / (ms * 0.001 * sr)));
    public static float DbToLin(double db) => (float)Gain.FromDb(db);
}

/// <summary>Audio to MIDI drum trigger: a level above the open threshold fires a note-on (velocity from the peak), falling below the close threshold sends the note-off.</summary>
public sealed class AudioDrumTriggerProcessor : IMidiProcessor, IAudioAwareProcessor
{
    private readonly float _open, _close, _atk, _rel, _mix, _openDb;
    private readonly int _note, _ch, _peakWin;
    private readonly long _retrig;
    private float _env; private bool _gate; private long _pos, _lastTrig = long.MinValue / 2; private int _onNote = -1;

    public AudioDrumTriggerProcessor(MidiParams p, int sr)
    {
        _openDb = (float)Math.Min(p.Double("Open", -17, -60, 0), -0.5);
        _open = AudioProcUtil.DbToLin(_openDb);
        _close = AudioProcUtil.DbToLin(Math.Min(p.Double("Close", -18, -60, 0), _openDb));
        _atk = AudioProcUtil.MsCoef(p.Double("AttackMs", 0.1, 0, 50), sr);
        _rel = AudioProcUtil.MsCoef(p.Double("ReleaseMs", 20, 0.1, 500), sr);
        _retrig = (long)(p.Double("Retrigger", 30, 0, 500) * 0.001 * sr);
        _note = p.Int("Note", 38, 0, 127); _ch = p.Int("Channel", 10, 1, 16) - 1;
        _mix = (float)(p.Double("Mix", 100, 0, 100) / 100);
        _peakWin = Math.Max(1, (int)(0.0005 * sr));
    }

    public void Process(MidiBuffer input, MidiBuffer output, in MidiContext ctx) => AudioProcUtil.PassThrough(input, output);

    public void ProcessAudio(MidiBuffer input, MidiBuffer output, in MidiContext ctx, Span<float> l, Span<float> r)
    {
        AudioProcUtil.PassThrough(input, output);
        var n = Math.Min(ctx.Frames, Math.Min(l.Length, r.Length));
        for (var i = 0; i < n; i++)
        {
            var a = Math.Max(Math.Abs(l[i]), Math.Abs(r[i]));
            _env += (a - _env) * (a > _env ? _atk : _rel);
            if (!_gate && _env >= _open && _pos + i - _lastTrig >= _retrig)
            {
                var peak = 0f;
                for (var k = i; k < n && k < i + _peakWin; k++) peak = Math.Max(peak, Math.Max(Math.Abs(l[k]), Math.Abs(r[k])));
                var db = Gain.ToDb(Math.Max(peak, 1e-6f));
                var t = Math.Clamp((db - _openDb) / -_openDb, 0, 1);
                var vel = (byte)(1 + (int)Math.Round(126 * t));
                output.Add(i, (byte)(0x90 | _ch), (byte)_note, vel); output.Unsorted = true;
                _gate = true; _lastTrig = _pos + i; _onNote = _note;
            }
            else if (_gate && _env < _close)
            {
                _gate = false;
                if (_onNote >= 0) { output.Add(i, (byte)(0x80 | _ch), (byte)_onNote, 0); output.Unsorted = true; _onNote = -1; }
            }
            if (_mix < 1f) { l[i] *= _mix; r[i] *= _mix; }
        }
        _pos += ctx.Frames;
    }

    public void Adopt(IMidiProcessor previous, bool sameParameters)
    {
        if (previous is not AudioDrumTriggerProcessor o) return;
        _env = o._env; _gate = o._gate; _pos = o._pos; _lastTrig = o._lastTrig; _onNote = o._onNote;
    }

    public void Reset() { _env = 0; _gate = false; _onNote = -1; }
}

/// <summary>MIDI EQ ducker: a matching note-on ramps a gain change (or a band of it) in with the attack time; the last matching note-off ramps it out with the release time.</summary>
public sealed class AudioDuckerProcessor : IMidiProcessor, IAudioAwareProcessor
{
    private readonly int _note, _inCh, _mode;
    private readonly float _gain, _atk, _rel;
    private readonly bool _velReact;
    private readonly float _b0, _b2, _a1, _a2;   // bandpass (unity peak) around the chosen frequency
    private readonly bool[] _held = new bool[2048];
    private int _count; private float _target, _env;
    private float _x1L, _x2L, _y1L, _y2L, _x1R, _x2R, _y1R, _y2R;

    public AudioDuckerProcessor(MidiParams p, int sr)
    {
        _note = p.Int("Note", -1, -1, 127); _inCh = p.Int("InCh", 0, 0, 16); _mode = p.Int("Mode", 0, 0, 1);
        _gain = AudioProcUtil.DbToLin(p.Double("GainDb", -12, -32, 32));
        _atk = AudioProcUtil.MsCoef(p.Double("AttackMs", 5, 0, 75), sr); _rel = AudioProcUtil.MsCoef(p.Double("ReleaseMs", 150, 0, 500), sr);
        _velReact = p.Bool("VelReact", false);
        var w0 = 2 * Math.PI * Math.Clamp(p.Double("Freq", 1000, 20, 15000), 20, sr * 0.45) / sr;
        var bw = p.Double("Width", 1, 0.1, 2);
        var alpha = Math.Sin(w0) * Math.Sinh(Math.Log(2) / 2 * bw * w0 / Math.Sin(w0));
        var a0 = 1 + alpha;
        _b0 = (float)(alpha / a0); _b2 = (float)(-alpha / a0); _a1 = (float)(-2 * Math.Cos(w0) / a0); _a2 = (float)((1 - alpha) / a0);
    }

    public void Process(MidiBuffer input, MidiBuffer output, in MidiContext ctx) => AudioProcUtil.PassThrough(input, output);

    private void Apply(in BlockMidi e)
    {
        if (e.Status >= 0xF0) return;
        var kind = e.Status & 0xF0;
        if (kind is not (0x90 or 0x80)) return;
        var ch = e.Status & 0x0F;
        if ((_inCh != 0 && ch + 1 != _inCh) || (_note >= 0 && e.Data1 != _note)) return;
        var key = (ch << 7) | (e.Data1 & 0x7F);
        if (kind == 0x90 && e.Data2 > 0)
        {
            if (!_held[key]) { _held[key] = true; _count++; }
            _target = _velReact ? e.Data2 / 127f : 1f;
        }
        else if (_held[key]) { _held[key] = false; _count--; if (_count <= 0) { _count = 0; _target = 0; } }
    }

    public void ProcessAudio(MidiBuffer input, MidiBuffer output, in MidiContext ctx, Span<float> l, Span<float> r)
    {
        AudioProcUtil.PassThrough(input, output);
        var n = Math.Min(ctx.Frames, Math.Min(l.Length, r.Length));
        var ei = 0;
        for (var i = 0; i < n; i++)
        {
            while (ei < input.Count && input.Items[ei].Frame <= i) Apply(input.Items[ei++]);
            _env += (_target - _env) * (_target > _env ? _atk : _rel);
            if (_env < 1e-5f && _target == 0) { _env = 0; continue; }
            if (_mode == 0)
            {
                var g = 1 + (_gain - 1) * _env;
                l[i] *= g; r[i] *= g;
            }
            else
            {
                var x = l[i]; var y = _b0 * x + _b2 * _x2L - _a1 * _y1L - _a2 * _y2L; _x2L = _x1L; _x1L = x; _y2L = _y1L; _y1L = y;
                l[i] = x + (_gain - 1) * _env * y;
                x = r[i]; y = _b0 * x + _b2 * _x2R - _a1 * _y1R - _a2 * _y2R; _x2R = _x1R; _x1R = x; _y2R = _y1R; _y1R = y;
                r[i] = x + (_gain - 1) * _env * y;
            }
        }
        while (ei < input.Count) Apply(input.Items[ei++]);
    }

    public void Adopt(IMidiProcessor previous, bool sameParameters)
    {
        if (previous is not AudioDuckerProcessor o) return;
        Array.Copy(o._held, _held, _held.Length); _count = o._count; _target = o._target; _env = o._env;
    }

    public void Reset() { Array.Clear(_held); _count = 0; _target = 0; }
}

/// <summary>Loop sampler with MIDI triggers: the record note records the chain audio while held (up to a fixed maximum); every other note plays the loop back.</summary>
public sealed class LoopSamplerProcessor : IMidiProcessor, IAudioAwareProcessor
{
    private const int Voices = 8;
    private float[] _bl, _br;
    private readonly int _recNote, _inCh, _root;
    private readonly bool _pitched, _loop;
    private readonly float _level;
    private int _len, _recPos; private bool _recording;
    private readonly double[] _pos = new double[Voices]; private readonly float[] _step = new float[Voices], _vel = new float[Voices];
    private readonly int[] _vnote = new int[Voices]; private readonly bool[] _active = new bool[Voices];
    private int _nextVoice;

    public LoopSamplerProcessor(MidiParams p, int sr)
    {
        _recNote = p.Int("RecNote", 50, 0, 127); _inCh = p.Int("InCh", 0, 0, 16); _root = p.Int("Root", 60, 0, 127);
        _pitched = p.Bool("Pitched", false); _loop = p.Bool("Loop", false); _level = AudioProcUtil.DbToLin(p.Double("Level", 0, -60, 12));
        var size = Math.Max(1, (int)(p.Double("MaxSec", 4, 1, 30) * sr));
        _bl = new float[size]; _br = new float[size];   // engine thread: allocated once here
        Array.Fill(_vnote, -1);
    }

    public void Process(MidiBuffer input, MidiBuffer output, in MidiContext ctx) => AudioProcUtil.PassThrough(input, output);

    private void Apply(in BlockMidi e)
    {
        if (e.Status >= 0xF0) return;
        var kind = e.Status & 0xF0;
        if (kind is not (0x90 or 0x80)) return;
        if (_inCh != 0 && (e.Status & 0x0F) + 1 != _inCh) return;
        var on = kind == 0x90 && e.Data2 > 0;
        if (e.Data1 == _recNote)
        {
            if (on) { _recording = true; _recPos = 0; _len = 0; for (var v = 0; v < Voices; v++) _active[v] = false; }
            else if (_recording) { _recording = false; _len = _recPos; }
            return;
        }
        if (on)
        {
            if (_len <= 0 || _recording) return;
            var v = _nextVoice; _nextVoice = (_nextVoice + 1) % Voices;
            _active[v] = true; _pos[v] = 0; _vnote[v] = e.Data1; _vel[v] = e.Data2 / 127f;
            _step[v] = _pitched ? (float)Math.Pow(2, (e.Data1 - _root) / 12.0) : 1f;
        }
        else if (_loop)
            for (var v = 0; v < Voices; v++) if (_active[v] && _vnote[v] == e.Data1) _active[v] = false;
    }

    public void ProcessAudio(MidiBuffer input, MidiBuffer output, in MidiContext ctx, Span<float> l, Span<float> r)
    {
        AudioProcUtil.PassThrough(input, output);
        var n = Math.Min(ctx.Frames, Math.Min(l.Length, r.Length));
        var ei = 0;
        for (var i = 0; i < n; i++)
        {
            while (ei < input.Count && input.Items[ei].Frame <= i) Apply(input.Items[ei++]);
            if (_recording)
            {
                if (_recPos < _bl.Length) { _bl[_recPos] = l[i]; _br[_recPos] = r[i]; _recPos++; }
                else { _recording = false; _len = _recPos; }
            }
            if (_len <= 0) continue;
            float oL = 0, oR = 0;
            for (var v = 0; v < Voices; v++)
            {
                if (!_active[v]) continue;
                var pos = _pos[v]; var i0 = (int)pos; var f = (float)(pos - i0); var i1 = i0 + 1;
                if (i1 >= _len) i1 = _loop ? 0 : i0;
                var g = _vel[v] * _level;
                oL += (_bl[i0] + (_bl[i1] - _bl[i0]) * f) * g; oR += (_br[i0] + (_br[i1] - _br[i0]) * f) * g;
                pos += _step[v];
                if (pos >= _len) { if (_loop) pos -= _len; else _active[v] = false; }
                _pos[v] = pos;
            }
            l[i] += oL; r[i] += oR;
        }
        while (ei < input.Count) Apply(input.Items[ei++]);
    }

    public void Adopt(IMidiProcessor previous, bool sameParameters)
    {
        if (previous is not LoopSamplerProcessor o || o._bl.Length != _bl.Length) return;
        _bl = o._bl; _br = o._br; _len = o._len; _recPos = o._recPos; _recording = o._recording; _nextVoice = o._nextVoice;
        Array.Copy(o._pos, _pos, Voices); Array.Copy(o._step, _step, Voices); Array.Copy(o._vel, _vel, Voices);
        Array.Copy(o._vnote, _vnote, Voices); Array.Copy(o._active, _active, Voices);
    }

    public void Reset() { _recording = false; if (_recPos > 0 && _len == 0) _len = _recPos; Array.Clear(_active); }
}

/// <summary>
/// Super8-style synchronized looper: N loop tracks share one loop length (the first recording, rounded to whole beats of the
/// tempo). Notes: BaseNote + action * 8 + track; action 0 record / overdub toggle, 1 play / stop, 2 clear. With Quantize on,
/// later record and overdub changes wait for the loop start.
/// </summary>
public sealed class LooperProcessor : IMidiProcessor, IAudioAwareProcessor
{
    private const int Empty = 0, Recording = 1, Playing = 2, Overdub = 3, Stopped = 4;
    private readonly int _tracks, _base, _inCh, _sr;
    private readonly bool _quantize, _restart;
    private readonly float _level;
    private float[][] _bl, _br;
    private readonly int[] _state, _pending, _recPos, _used;
    private int _master, _phase;
    private double _tempo = 120;

    public LooperProcessor(MidiParams p, int sr)
    {
        _sr = sr; _tracks = p.Int("Tracks", 4, 1, 8); _base = p.Int("BaseNote", 36, 0, 103); _inCh = p.Int("InCh", 0, 0, 16);
        _quantize = p.Bool("Quantize", true); _restart = p.Bool("RestartOnPlay", true); _level = AudioProcUtil.DbToLin(p.Double("Level", 0, -60, 12));
        var size = Math.Max(1, (int)(p.Double("MaxSec", 8, 1, 30) * sr));
        _bl = new float[_tracks][]; _br = new float[_tracks][];
        for (var t = 0; t < _tracks; t++) { _bl[t] = new float[size]; _br[t] = new float[size]; }   // engine thread: allocated once here
        _state = new int[_tracks]; _pending = new int[_tracks]; _recPos = new int[_tracks]; _used = new int[_tracks];
        Array.Fill(_pending, -1);
    }

    public void Process(MidiBuffer input, MidiBuffer output, in MidiContext ctx) => AudioProcUtil.PassThrough(input, output);

    private void FinishMaster(int t)
    {
        var bufLen = _bl[t].Length;
        var bf = 60.0 / Math.Max(20, _tempo) * _sr;
        var len = _quantize ? Math.Max(1, (int)Math.Round(_recPos[t] / bf)) * bf : _recPos[t];
        _master = (int)Math.Clamp(len, 1, bufLen);
        _used[t] = Math.Max(_used[t], _recPos[t]);
        _state[t] = Playing; _phase = 0;
    }

    private void Action(int t, int action)
    {
        switch (action)
        {
            case 0:
                switch (_state[t])
                {
                    case Empty:
                        if (_master == 0) { _state[t] = Recording; _recPos[t] = 0; }
                        else if (_quantize) _pending[t] = Recording; else _state[t] = Recording;
                        break;
                    case Recording:
                        if (_master == 0) FinishMaster(t);
                        else if (_quantize) _pending[t] = Playing; else _state[t] = Playing;
                        break;
                    case Playing or Stopped:
                        if (_quantize) _pending[t] = Overdub; else _state[t] = Overdub;
                        break;
                    case Overdub:
                        if (_quantize) _pending[t] = Playing; else _state[t] = Playing;
                        break;
                }
                break;
            case 1:
                if (_state[t] is Playing or Overdub) _state[t] = Stopped;
                else if (_state[t] == Stopped) _state[t] = Playing;
                _pending[t] = -1;
                break;
            case 2:
                Array.Clear(_bl[t], 0, Math.Min(_bl[t].Length, Math.Max(_used[t], _master)));
                Array.Clear(_br[t], 0, Math.Min(_br[t].Length, Math.Max(_used[t], _master)));
                _state[t] = Empty; _pending[t] = -1; _recPos[t] = 0; _used[t] = 0;
                var any = false;
                for (var k = 0; k < _tracks; k++) if (_state[k] != Empty) any = true;
                if (!any) { _master = 0; _phase = 0; }
                break;
        }
    }

    private void Apply(in BlockMidi e)
    {
        if (e.Status >= 0xF0 || (e.Status & 0xF0) != 0x90 || e.Data2 == 0) return;
        if (_inCh != 0 && (e.Status & 0x0F) + 1 != _inCh) return;
        var rel = e.Data1 - _base;
        if (rel < 0) return;
        var action = rel >> 3; var t = rel & 7;
        if (action <= 2 && t < _tracks) Action(t, action);
    }

    public void ProcessAudio(MidiBuffer input, MidiBuffer output, in MidiContext ctx, Span<float> l, Span<float> r)
    {
        AudioProcUtil.PassThrough(input, output);
        if (ctx.Tempo > 0) _tempo = ctx.Tempo;
        if (ctx.PlayStarted && _restart) _phase = 0;
        var n = Math.Min(ctx.Frames, Math.Min(l.Length, r.Length));
        var ei = 0;
        for (var i = 0; i < n; i++)
        {
            while (ei < input.Count && input.Items[ei].Frame <= i) Apply(input.Items[ei++]);
            if (_master > 0 && _phase == 0)
                for (var t = 0; t < _tracks; t++) if (_pending[t] >= 0) { _state[t] = _pending[t]; _pending[t] = -1; }
            float xl = l[i], xr = r[i], oL = 0, oR = 0;
            for (var t = 0; t < _tracks; t++)
            {
                switch (_state[t])
                {
                    case Recording:
                        if (_master == 0)
                        {
                            var rp = _recPos[t];
                            if (rp < _bl[t].Length) { _bl[t][rp] = xl; _br[t][rp] = xr; _recPos[t] = rp + 1; }
                            else FinishMaster(t);
                        }
                        else { _bl[t][_phase] = xl; _br[t][_phase] = xr; if (_phase >= _used[t]) _used[t] = _phase + 1; }
                        break;
                    case Playing:
                        oL += _bl[t][_phase]; oR += _br[t][_phase];
                        break;
                    case Overdub:
                        oL += _bl[t][_phase]; oR += _br[t][_phase];
                        _bl[t][_phase] += xl; _br[t][_phase] += xr; if (_phase >= _used[t]) _used[t] = _phase + 1;
                        break;
                }
            }
            l[i] = xl + oL * _level; r[i] = xr + oR * _level;
            if (_master > 0 && ++_phase >= _master) _phase = 0;
        }
        while (ei < input.Count) Apply(input.Items[ei++]);
    }

    public void Adopt(IMidiProcessor previous, bool sameParameters)
    {
        if (previous is not LooperProcessor o || o._tracks != _tracks || o._bl[0].Length != _bl[0].Length) return;
        _bl = o._bl; _br = o._br; _master = o._master; _phase = o._phase; _tempo = o._tempo;
        Array.Copy(o._state, _state, _tracks); Array.Copy(o._pending, _pending, _tracks); Array.Copy(o._recPos, _recPos, _tracks); Array.Copy(o._used, _used, _tracks);
    }

    public void Reset() { }
}
