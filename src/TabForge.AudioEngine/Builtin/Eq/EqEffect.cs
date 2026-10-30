using TabForge.Audio.Contracts.Builtin;

namespace TabForge.AudioEngine.Builtin.Eq;

// Owns: the EQ's parameter list (ids and keys are saved in songs and presets: never change them), the index and id of every control, and the value texts
//   (slope names, shelf / bell). Layout: bypass, output, low cut, high cut, four bands (on, frequency, gain, Q), the type of band 1 and band 4.
// Does not own: the audio path (EqEffect.Process.cs), the filter maths and the curve (EqDesign), the editor window (Views/Builtin/Eq in the app).
// Tests: BuiltinEqTests.
// Threads: parameters and state on the engine's main thread; Process on the audio thread.

/// <summary>Parametric EQ: low cut and high cut (12 or 24 dB per octave) and four bands; band 1 and 4 are a shelf or a bell, band 2 and 3 a bell.</summary>
public sealed partial class EqEffect : BuiltinEffect
{
    public const uint BypassId = EqParameters.BypassId, OutputId = EqParameters.OutputId, LowCutFreqId = EqParameters.LowCutFreqId,
        LowCutSlopeId = EqParameters.LowCutSlopeId, HighCutFreqId = EqParameters.HighCutFreqId, HighCutSlopeId = EqParameters.HighCutSlopeId,
        Band1TypeId = EqParameters.Band1TypeId, Band4TypeId = EqParameters.Band4TypeId;
    public const int Bands = EqParameters.Bands;
    /// <summary>The fields of a band, in the order of its parameters.</summary>
    public const int OnField = EqParameters.OnField, FreqField = EqParameters.FreqField, GainField = EqParameters.GainField, QField = EqParameters.QField;
    public const int BypassIndex = 0, OutputIndex = 1, LowCutFreqIndex = 2, LowCutSlopeIndex = 3, HighCutFreqIndex = 4, HighCutSlopeIndex = 5;
    public const double MinHz = EqParameters.MinHz, MaxHz = EqParameters.MaxHz, MaxGainDb = EqParameters.MaxGainDb, MinQ = EqParameters.MinQ,
        MaxQ = EqParameters.MaxQ, ShelfQ = EqParameters.ShelfQ, BellQ = EqParameters.BellQ;
    /// <summary>Slope values: Off, 12 dB per octave, 24 dB per octave. Type values: shelf, bell.</summary>
    public const int SlopeOff = EqParameters.SlopeOff, Slope12 = EqParameters.Slope12, Slope24 = EqParameters.Slope24,
        TypeShelf = EqParameters.TypeShelf, TypeBell = EqParameters.TypeBell;

    public static IReadOnlyList<BuiltinParameter> Descriptors => EqParameters.Descriptors;

    /// <summary>The parameter id of a band's field (band 0..3).</summary>
    public static uint BandId(int band, int field) => EqParameters.BandId(band, field);

    /// <summary>The index in <see cref="Descriptors"/> of a band's field.</summary>
    public static int Index(int band, int field) => EqParameters.Index(band, field);

    /// <summary>The index of the type (shelf or bell) of band 0 or band 3.</summary>
    public static int TypeIndex(int band) => EqParameters.TypeIndex(band);

    public static bool HasType(int band) => EqParameters.HasType(band);

    public EqEffect(double sampleRate, int maxBlock)
        : base(BuiltinEffects.Find(BuiltinEffects.EqId)!, Descriptors, [], sampleRate, maxBlock)
    {
        InitialiseDsp();
    }

    protected override void OnReconfigured() => InitialiseDsp();

    protected override string TextOf(BuiltinParameter parameter, double value) => parameter.Id switch
    {
        LowCutSlopeId or HighCutSlopeId => value switch { >= Slope24 => "24 dB/oct", >= Slope12 => "12 dB/oct", _ => "Off" },
        Band1TypeId or Band4TypeId => value >= TypeBell ? "Bell" : "Shelf",
        _ => base.TextOf(parameter, value),
    };
}
