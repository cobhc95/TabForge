using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace TabForge.Sf2Synth.Nam;

// Owns: the activations a .nam file names (plain string or {"type": ..., params}) and a float-accurate rational tanh, SIMD on
//   rows for the common ones.
// Does not own: where they run (NamWaveNet, NamLstm).
// Tests: NamTests.
internal enum Act { Identity, Tanh, Hardtanh, ReLU, LeakyReLU, LeakyHardtanh, Sigmoid, SiLU, Hardswish, Softsign }

internal readonly record struct NamAct(Act Kind, float Slope = 0.01f, float Min = -1, float Max = 1, float MaxSlope = 0.01f)
{
    public static NamAct Parse(JsonElement e)
    {
        if (e.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return new(Act.Identity);
        var name = e.ValueKind == JsonValueKind.String ? e.GetString()! : e.GetProperty("type").GetString()!;
        float F(string key, float def) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : def;
        return name switch
        {
            "Tanh" or "Fasttanh" => new(Act.Tanh),
            "Hardtanh" => new(Act.Hardtanh),
            "ReLU" => new(Act.ReLU),
            "LeakyReLU" or "PReLU" => new(Act.LeakyReLU, F("negative_slope", 0.01f)),
            "LeakyHardtanh" or "LeakyHardTanh" => new(Act.LeakyHardtanh, F("min_slope", 0.01f), F("min_val", -1), F("max_val", 1), F("max_slope", 0.01f)),
            "Sigmoid" => new(Act.Sigmoid),
            "SiLU" => new(Act.SiLU),
            "Hardswish" => new(Act.Hardswish),
            "Softsign" => new(Act.Softsign),
            _ => throw new FormatException($"activation {name} not supported"),
        };
    }

    public float Of(float x) => Kind switch
    {
        Act.Tanh => Tanh(x),
        Act.Hardtanh => Math.Clamp(x, -1, 1),
        Act.ReLU => x > 0 ? x : 0,
        Act.LeakyReLU => x > 0 ? x : Slope * x,
        Act.LeakyHardtanh => x < Min ? Min + Slope * (x - Min) : x > Max ? Max + MaxSlope * (x - Max) : x,
        Act.Sigmoid => Sigmoid(x),
        Act.SiLU => x * Sigmoid(x),
        Act.Hardswish => x * Math.Clamp(x + 3, 0, 6) / 6,
        Act.Softsign => x / (1 + MathF.Abs(x)),
        _ => x,
    };

    public void Apply(Span<float> v)
    {
        if (Kind == Act.Identity) return;
        var vv = MemoryMarshal.Cast<float, Vector<float>>(v);
        var done = 0;
        if (Kind is Act.Tanh or Act.Sigmoid or Act.ReLU or Act.LeakyReLU)
        {
            var slope = new Vector<float>(Kind == Act.ReLU ? 0 : Slope);
            for (var i = 0; i < vv.Length; i++)
                vv[i] = Kind switch
                {
                    Act.Tanh => Tanh(vv[i]),
                    Act.Sigmoid => new Vector<float>(0.5f) + 0.5f * Tanh(0.5f * vv[i]),
                    _ => Vector.ConditionalSelect(Vector.GreaterThan(vv[i], Vector<float>.Zero), vv[i], vv[i] * slope),
                };
            done = vv.Length * Vector<float>.Count;
        }
        for (var i = done; i < v.Length; i++) v[i] = Of(v[i]);
    }

    public static float Sigmoid(float x) => 0.5f + 0.5f * Tanh(0.5f * x);

    // 13/6 rational fit, accurate to float rounding on the clamped range.
    public static float Tanh(float x) => Tanh(new Vector<float>(x))[0];

    public static Vector<float> Tanh(Vector<float> x)
    {
        x = Vector.Min(Vector.Max(x, new Vector<float>(-7.9053111f)), new Vector<float>(7.9053111f));
        var x2 = x * x;
        var p = x2 * -2.76076847742355e-16f + new Vector<float>(2.00018790482477e-13f);
        p = p * x2 + new Vector<float>(-8.60467152213735e-11f);
        p = p * x2 + new Vector<float>(5.12229709037114e-08f);
        p = p * x2 + new Vector<float>(1.48572235717979e-05f);
        p = p * x2 + new Vector<float>(6.37261928875436e-04f);
        p = (p * x2 + new Vector<float>(4.89352455891786e-03f)) * x;
        var q = x2 * 1.19825839466702e-06f + new Vector<float>(1.18534705686654e-04f);
        q = q * x2 + new Vector<float>(2.26843463243900e-03f);
        q = q * x2 + new Vector<float>(4.89352518554385e-03f);
        return p / q;
    }
}
