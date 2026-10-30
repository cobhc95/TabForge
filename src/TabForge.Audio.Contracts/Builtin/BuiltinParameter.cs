using System.Globalization;

namespace TabForge.Audio.Contracts.Builtin;

// Owns: the descriptor of one built-in effect parameter (stable id and state key, name, range, default, stepped, read-only readout, units, value text).
// Does not own: the value (the engine's BuiltinEffect keeps it), the wire form (PluginParameterInfo).
// Tests: BuiltinEffectTests.

/// <summary>
/// One parameter of a built-in effect. <paramref name="Id"/> is what the generic controls, automation and SetParam use; <paramref name="Key"/> is the
/// name in the saved state. Neither may change once shipped (old songs and presets keep them). A read-only one is a readout (latency, CPU).
/// </summary>
public sealed record BuiltinParameter(uint Id, string Key, string Name, double Min, double Max, double Default, bool Stepped = false, bool ReadOnly = false, string Unit = "")
{
    /// <summary>An on / off switch (0 or 1).</summary>
    public static BuiltinParameter Switch(uint id, string key, string name, bool on) => new(id, key, name, 0, 1, on ? 1 : 0, Stepped: true);

    /// <summary>A read-only value the effect reports (never saved).</summary>
    public static BuiltinParameter Readout(uint id, string key, string name, double max, string unit) => new(id, key, name, 0, max, 0, ReadOnly: true, Unit: unit);

    public bool IsSwitch => Stepped && Min == 0 && Max == 1;

    /// <summary>The value clamped to the range (and rounded when stepped).</summary>
    public double Clamp(double value)
    {
        if (!double.IsFinite(value)) return Default;
        var clamped = Math.Clamp(value, Min, Max);
        return Stepped ? Math.Round(clamped) : clamped;
    }

    /// <summary>The value as the generic controls show it ("On", "-6.0 dB", "100 %").</summary>
    public string Format(double value) => IsSwitch ? (value >= 0.5 ? "On" : "Off")
        : (value.ToString(Stepped || Math.Abs(value) >= 100 ? "0" : "0.0", CultureInfo.InvariantCulture) + (Unit.Length > 0 ? " " + Unit : ""));
}
