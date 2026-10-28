using TabForge.Audio.Contracts.Builtin;

namespace TabForge.AudioEngine.Builtin;

// Owns: the one place a built-in id (BuiltinEffects) becomes an instance. Adding an effect: an entry in BuiltinEffects, one case here, and its class.
// Does not own: loading real plug-in files (PluginFactory), the chain (EngineSession).
// Tests: BuiltinEffectTests.
public static class BuiltinEffectFactory
{
    /// <summary>A new instance of the built-in <paramref name="id"/>; throws <see cref="InvalidDataException"/> for an id this version does not have.</summary>
    public static BuiltinEffect Create(string id, double sampleRate, int maxBlock) => BuiltinEffects.Find(id)?.Id switch
    {
        BuiltinEffects.IrLoaderId => new IrLoader.IrLoaderEffect(sampleRate, maxBlock),
        BuiltinEffects.EqId => new Eq.EqEffect(sampleRate, maxBlock),
        BuiltinEffects.CompressorId => new Compressor.CompressorEffect(sampleRate, maxBlock),
        BuiltinEffects.LimiterId => new Limiter.LimiterEffect(sampleRate, maxBlock),
        BuiltinEffects.DelayId => new Delay.DelayEffect(sampleRate, maxBlock),
        BuiltinEffects.GateId => new Gate.GateEffect(sampleRate, maxBlock),
        _ => throw new InvalidDataException($"This built-in effect is not part of this TabForge version ({id})."),
    };
}
