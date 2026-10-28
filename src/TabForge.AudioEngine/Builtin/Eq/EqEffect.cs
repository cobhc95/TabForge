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
    public const uint BypassId = 1, OutputId = 2, LowCutFreqId = 3, LowCutSlopeId = 4, HighCutFreqId = 5, HighCutSlopeId = 6, Band1TypeId = 7, Band4TypeId = 8;
    public const int Bands = 4;
    /// <summary>The fields of a band, in the order of its parameters.</summary>
    public const int OnField = 0, FreqField = 1, GainField = 2, QField = 3;
    public const int BypassIndex = 0, OutputIndex = 1, LowCutFreqIndex = 2, LowCutSlopeIndex = 3, HighCutFreqIndex = 4, HighCutSlopeIndex = 5;
    private const int FirstBand = 6, FieldsPerBand = 4, Band1TypeIndex = FirstBand + Bands * FieldsPerBand, Band4TypeIndex = Band1TypeIndex + 1;
    public const double MinHz = 20, MaxHz = 20000, MaxGainDb = 18, MinQ = 0.1, MaxQ = 18, ShelfQ = 0.71, BellQ = 1.0;
    /// <summary>Slope values: Off, 12 dB per octave, 24 dB per octave. Type values: shelf, bell.</summary>
    public const int SlopeOff = 0, Slope12 = 1, Slope24 = 2, TypeShelf = 0, TypeBell = 1;

    private static readonly IReadOnlyList<double> DefaultFreq = [100, 400, 2500, 8000];

    public static IReadOnlyList<BuiltinParameter> Descriptors { get; } = Build();

    /// <summary>The parameter id of a band's field (band 0..3).</summary>
    public static uint BandId(int band, int field) => (uint)(10 + 10 * band + field);

    /// <summary>The index in <see cref="Descriptors"/> of a band's field.</summary>
    public static int Index(int band, int field) => FirstBand + band * FieldsPerBand + field;

    /// <summary>The index of the type (shelf or bell) of band 0 or band 3.</summary>
    public static int TypeIndex(int band) => band == 0 ? Band1TypeIndex : Band4TypeIndex;

    public static bool HasType(int band) => band is 0 or Bands - 1;

    private static IReadOnlyList<BuiltinParameter> Build()
    {
        var list = new List<BuiltinParameter>
        {
            BuiltinParameter.Switch(BypassId, "bypass", "Bypass", false),
            new(OutputId, "output", "Output gain", -MaxGainDb, MaxGainDb, 0, Unit: "dB"),
            new(LowCutFreqId, "lcFreq", "Low cut frequency", MinHz, 1000, 80, Unit: "Hz"),
            new(LowCutSlopeId, "lcSlope", "Low cut slope", SlopeOff, Slope24, SlopeOff, Stepped: true),
            new(HighCutFreqId, "hcFreq", "High cut frequency", 1000, MaxHz, 12000, Unit: "Hz"),
            new(HighCutSlopeId, "hcSlope", "High cut slope", SlopeOff, Slope24, SlopeOff, Stepped: true),
        };
        for (var b = 0; b < Bands; b++)
        {
            var n = b + 1;
            list.Add(BuiltinParameter.Switch(BandId(b, OnField), $"b{n}on", $"Band {n} on", true));
            list.Add(new(BandId(b, FreqField), $"b{n}freq", $"Band {n} frequency", MinHz, MaxHz, DefaultFreq[b], Unit: "Hz"));
            list.Add(new(BandId(b, GainField), $"b{n}gain", $"Band {n} gain", -MaxGainDb, MaxGainDb, 0, Unit: "dB"));
            list.Add(new(BandId(b, QField), $"b{n}q", $"Band {n} Q", MinQ, MaxQ, HasType(b) ? ShelfQ : BellQ));
        }
        list.Add(new(Band1TypeId, "b1type", "Band 1 type (shelf or bell)", TypeShelf, TypeBell, TypeShelf, Stepped: true));
        list.Add(new(Band4TypeId, "b4type", "Band 4 type (shelf or bell)", TypeShelf, TypeBell, TypeShelf, Stepped: true));
        return list;
    }

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
