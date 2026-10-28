using TabForge.Audio.Contracts;

namespace TabForge.AudioEngine.Builtin;

// Owns: a built-in effect's parameter list, parameter changes and saved state (whole or partial), split from BuiltinEffect.cs.
// Does not own: the values array or the sound (BuiltinEffect, the subclasses).
// Tests: BuiltinEffectTests.
public abstract partial class BuiltinEffect
{
    public IReadOnlyList<PluginParameterInfo> GetParameters() =>
        Parameters.Select((p, i) => new PluginParameterInfo(p.Id, p.Name, Info.Name, p.Min, p.Max, p.Default, Value(i), p.Stepped, p.ReadOnly, TextOf(p, Value(i)))).ToList();

    public bool SetParameter(uint id, double value)
    {
        var index = IndexOf(id);
        if (index < 0 || Parameters[index].ReadOnly) return false;
        var clamped = Parameters[index].Clamp(value);
        if (Value(index) == clamped) return true;
        Volatile.Write(ref _values[index], clamped);
        OnChanged(Parameters[index]);
        Edited?.Invoke();
        return true;
    }

    public byte[]? GetState()
    {
        var state = new BuiltinState();
        for (var i = 0; i < Parameters.Count; i++) if (!Parameters[i].ReadOnly) state.Values[Parameters[i].Key] = Value(i);
        foreach (var key in _textKeys) state.Text[key] = Text(key);
        return state.ToBytes();
    }

    /// <summary>Applies a whole or partial state: keys it names change, the others keep their value (the editor sends only the file it set).</summary>
    public void SetState(byte[] state)
    {
        var parsed = BuiltinState.Parse(state);
        var changed = false;
        for (var i = 0; i < Parameters.Count; i++)
        {
            var p = Parameters[i];
            if (p.ReadOnly || !parsed.Values.TryGetValue(p.Key, out var value)) continue;
            var clamped = p.Clamp(value);
            if (Value(i) == clamped) continue;
            Volatile.Write(ref _values[i], clamped);
            OnChanged(p);
            changed = true;
        }
        foreach (var key in _textKeys)
        {
            if (!parsed.Text.TryGetValue(key, out var text) || text == Text(key)) continue;
            _text[key] = text;
            OnTextChanged(key);
            changed = true;
        }
        if (changed) Edited?.Invoke();
    }

    private int IndexOf(uint id)
    {
        for (var i = 0; i < Parameters.Count; i++) if (Parameters[i].Id == id) return i;
        return -1;
    }
}
