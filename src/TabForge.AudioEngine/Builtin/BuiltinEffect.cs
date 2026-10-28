using TabForge.Audio.Contracts;
using TabForge.Audio.Contracts.Builtin;
using TabForge.AudioEngine.Plugins;

namespace TabForge.AudioEngine.Builtin;

// Owns: what every built-in effect shares as a plug-in instance: the parameter values behind the generic controls, automation and presets (IPluginParameters),
//   the saved state (BuiltinState: values and text such as file paths, merged so a partial state changes only what it names), the Edited and Problem events
//   the engine session forwards, and the stereo Process entry that hands each effect its two channels.
// Does not own: any effect's sound (the subclasses), which id makes which effect (BuiltinEffectFactory), the editor windows (Views/Builtin in the app).
// Tests: BuiltinEffectTests.
// Threads: parameters and state on the engine's main thread; values are read on the audio thread (one aligned double each); Process on the audio thread.

/// <summary>
/// Base of TabForge's own effects. A subclass passes its <see cref="BuiltinParameter"/> list and text keys, reads values with <see cref="Value"/>, and
/// implements <see cref="ProcessStereo"/> (no allocation). It may react to changes in <see cref="OnChanged"/> / <see cref="OnTextChanged"/> (main thread).
/// </summary>
public abstract partial class BuiltinEffect : IPluginInstance, IPluginParameters
{
    private readonly double[] _values;
    private readonly Dictionary<string, string> _text = new(StringComparer.Ordinal);
    private readonly IReadOnlyList<string> _textKeys;

    protected BuiltinEffect(BuiltinEffectInfo info, IReadOnlyList<BuiltinParameter> parameters, IReadOnlyList<string> textKeys, double sampleRate, int maxBlock)
    {
        Info = info;
        Parameters = parameters;
        _textKeys = textKeys;
        _values = parameters.Select(p => p.Default).ToArray();
        foreach (var key in textKeys) _text[key] = "";
        SampleRate = sampleRate;
        MaxBlock = Math.Clamp(maxBlock, 16, 8192);
    }

    public BuiltinEffectInfo Info { get; }
    public IReadOnlyList<BuiltinParameter> Parameters { get; }
    protected double SampleRate { get; private set; }
    protected int MaxBlock { get; private set; }

    public string Path => Info.Id;
    public bool IsInstrument => false;
    /// <summary>False: a built-in's window is TabForge's own (Views/Builtin), never an engine-side native window.</summary>
    public bool HasEditor => false;
    public virtual int LatencySamples => 0;
    public virtual int TailSamples => 0;

    /// <summary>Main thread: a value or text changed through SetParameter or SetState (the session tells TabForge the song changed).</summary>
    public event Action? Edited;
    /// <summary>
    /// Any thread: something the user should read (an IR file that is missing): the file and the sentence; the session shows it in TabForge's status line.
    /// A report made before anyone listened (the state applied while loading) is delivered to the first listener.
    /// </summary>
    public event Action<string, string>? Problem
    {
        add
        {
            (string, string)? missed;
            lock (_problemGate) { _problem += value; missed = _missed; _missed = null; }
            if (missed is { } m) value?.Invoke(m.Item1, m.Item2);
        }
        remove { lock (_problemGate) _problem -= value; }
    }

    private readonly object _problemGate = new();
    private Action<string, string>? _problem;
    private (string, string)? _missed;

    protected void Report(string path, string text)
    {
        Action<string, string>? listeners;
        lock (_problemGate)
        {
            listeners = _problem;
            if (listeners is null) _missed = (path, text);
        }
        listeners?.Invoke(path, text);
    }

    /// <summary>Any thread: the current value of the parameter at <paramref name="index"/> in <see cref="Parameters"/>.</summary>
    protected double Value(int index) => Volatile.Read(ref _values[index]);

    protected bool On(int index) => Value(index) >= 0.5;

    /// <summary>Any thread: sets a readout (a read-only parameter the effect reports).</summary>
    protected void SetReadout(int index, double value) => Volatile.Write(ref _values[index], value);

    /// <summary>Main thread: the text value of <paramref name="key"/> ("" when unset).</summary>
    public string Text(string key) => _text.GetValueOrDefault(key, "");

    protected virtual void OnChanged(BuiltinParameter parameter) { }
    protected virtual void OnTextChanged(string key) { }

    /// <summary>The value text the generic controls show; an effect may say more for a readout.</summary>
    protected virtual string TextOf(BuiltinParameter parameter, double value) => parameter.Format(value);

    /// <summary>Audio thread: the song tempo (quarter notes per minute) of the block being processed; 120 until the host reports one. A delay syncs to it.</summary>
    protected double Tempo { get; private set; } = DefaultTempo;

    private const double DefaultTempo = 120;

    public void Process(float[][] input, float[][] output, int frames, ReadOnlySpan<BlockMidi> midi, in TransportInfo transport)
    {
        Tempo = double.IsFinite(transport.Tempo) && transport.Tempo >= 1 ? transport.Tempo : DefaultTempo;
        ProcessStereo(input[0], input[1], output[0], output[1], frames);
    }

    /// <summary>Audio thread, no allocation: the effect's sound. The input and output arrays may be the same.</summary>
    protected abstract void ProcessStereo(float[] inLeft, float[] inRight, float[] outLeft, float[] outRight, int frames);

    public void Reconfigure(double sampleRate, int maxBlock)
    {
        SampleRate = sampleRate;
        MaxBlock = Math.Clamp(maxBlock, 16, 8192);
        OnReconfigured();
    }

    /// <summary>Main thread, nothing processing: the session rate or block size changed.</summary>
    protected virtual void OnReconfigured() { }

    public virtual void SetOfflineMode(bool offline) { }

    public (int Width, int Height)? OpenEditor(IntPtr parent) => null;
    public void CloseEditor() { }
    public void EditorIdle() { }

    public virtual void Dispose() => GC.SuppressFinalize(this);
}
