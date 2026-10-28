namespace TabForge.Audio.Contracts.Builtin;

// Owns: the list of TabForge's own (built-in) effects that both processes know: the stable id each is saved under (its PluginSlot path), its name,
//   and the format, vendor and role every built-in shows in the Add plug-in window. Built-ins are C# code inside the engine: no file, no scan, no trust step.
// Does not own: the effects themselves (TabForge.AudioEngine.Builtin: BuiltinEffectFactory makes one per id), their editors (Views/Builtin in the app).
// Tests: BuiltinEffectTests.

/// <summary>One built-in effect: <paramref name="Id"/> is stored in songs as the slot's path and never changes.</summary>
public sealed record BuiltinEffectInfo(string Id, string Name, string Description);

/// <summary>TabForge's own effects. To add one: an entry here, a case in the engine's BuiltinEffectFactory, and its class.</summary>
public static class BuiltinEffects
{
    /// <summary>The Format column (and PluginSlot.Format) of every built-in.</summary>
    public const string Format = "Built-in";
    public const string Vendor = "TabForge";
    public const string Role = "Effect";
    /// <summary>Every built-in id starts with this; a real plug-in path never does (it is a full path).</summary>
    public const string Prefix = "builtin:";

    public const string IrLoaderId = Prefix + "ir-loader";
    public const string EqId = Prefix + "eq";
    public const string CompressorId = Prefix + "compressor";
    public const string LimiterId = Prefix + "limiter";
    public const string DelayId = Prefix + "delay";
    public const string GateId = Prefix + "gate";

    /// <summary>Every built-in effect, in the order the Add plug-in window lists them.</summary>
    public static IReadOnlyList<BuiltinEffectInfo> All { get; } =
    [
        new(IrLoaderId, "IR Loader", "Impulse response (cabinet, room or speaker correction) by FFT convolution"),
        new(EqId, "EQ", "Parametric equaliser: low cut, high cut and four bands (shelf or bell) with a frequency curve"),
        new(CompressorId, "Compressor", "Dynamics compressor with soft knee, auto makeup, gain-reduction meter and transfer curve"),
        new(LimiterId, "Limiter", "Look-ahead true-peak limiter with ceiling in dBTP and gain-reduction and output meters"),
        new(DelayId, "Delay", "Stereo delay, synced to the song tempo or in milliseconds, with feedback, ping-pong and filters on the repeats"),
        new(GateId, "Noise Gate", "Noise gate with threshold, attack, hold, release and range"),
    ];

    /// <summary>True for a built-in id (a slot path that names TabForge's own effect, not a file).</summary>
    public static bool IsBuiltin(string? path) => path is not null && path.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>The built-in with this id, or null (an id from a newer TabForge: the slot stays in the song and loads nothing).</summary>
    public static BuiltinEffectInfo? Find(string? id) => All.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));
}
