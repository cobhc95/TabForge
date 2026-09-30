using TabForge.Audio.Contracts;
using TabForge.Models;

namespace TabForge;

/// <summary>Automatic pitch matching: f0 estimator, octave folding, drums skipped, transpose pairing (part of <see cref="SelfTest"/>).</summary>
public static partial class SelfTest
{
    private static void TestPitchMatch()
    {
        const int rate = 48000;
        float[] Tone(double hz, bool harmonics)
        {
            var x = new float[rate * 4 / 10];
            for (var i = 0; i < x.Length; i++)
            {
                var t = i / (double)rate;
                var v = Math.Sin(2 * Math.PI * hz * t);
                // A strong 2nd harmonic (bass-like) invites octave errors.
                if (harmonics) v += 0.9 * Math.Sin(2 * Math.PI * 2 * hz * t) + 0.5 * Math.Sin(2 * Math.PI * 3 * hz * t);
                x[i] = (float)(0.3 * v);
            }
            return x;
        }
        var worst = 0.0; var detail = "";
        foreach (var hz in new[] { 40.0, 55, 82.41, 110, 196, 261.63, 440, 700, 1000 })
            foreach (var h in new[] { false, true })
            {
                var (f, _) = PitchMatch.EstimateF0(Tone(hz, h), rate);
                var err = f > 0 ? Math.Abs(12 * Math.Log2(f / hz)) : 99;
                if (err > worst) { worst = err; detail = $"{hz} Hz{(h ? " +harmonics" : "")} -> {f:0.00} Hz"; }
            }
        Check("f0 estimator: sines and harmonic tones 40 Hz..1 kHz within 0.1 semitone, no octave errors", worst < 0.1, $"worst {worst:0.000} st at {detail}");
        var noise = new float[rate / 2]; var rng = new Random(7);
        for (var i = 0; i < noise.Length; i++) noise[i] = (float)(rng.NextDouble() - 0.5);
        Check("f0 estimator: white noise is unpitched", PitchMatch.EstimateF0(noise, rate).Hz == 0);
        Check("f0 estimator: silence is unpitched", PitchMatch.EstimateF0(new float[rate / 2], rate).Hz == 0);
        Check("semitone offset: 2 octaves low = -24", Math.Abs(PitchMatch.SemitoneOffset(PitchMatch.MidiToHz(40) / 4, 40) + 24) < 1e-9);

        Eq("fold: two octaves low -> +24", (24, PitchMatch.Status.Ok), Take(PitchMatch.Fold(new[] { -24.1, -23.8, -24.3 })));
        Eq("fold: one octave high -> -12", (-12, PitchMatch.Status.Ok), Take(PitchMatch.Fold(new[] { 12.2, 11.9, double.NaN })));
        Eq("fold: in tune -> no offset", (0, PitchMatch.Status.NoOffset), Take(PitchMatch.Fold(new[] { 0.1, -0.2, 0.0 })));
        Eq("fold: notes disagreeing on the octave -> no offset", (0, PitchMatch.Status.NoOffset), Take(PitchMatch.Fold(new[] { -12.0, -24.0, -12.1 })));
        Eq("fold: detuned beyond 0.5 st -> no offset", (0, PitchMatch.Status.NoOffset), Take(PitchMatch.Fold(new[] { -11.2, -11.3, -11.1 })));
        Eq("fold: unpitched (drums) -> no offset", (0, PitchMatch.Status.Unpitched), Take(PitchMatch.Fold(new[] { double.NaN, double.NaN, -24.0 })));
        Eq("fold: beyond 3 octaves -> no offset", (0, PitchMatch.Status.NoOffset), Take(PitchMatch.Fold(new[] { -48.0, -48.0 })));

        var drums = new TrackModel { Kind = TrackKind.Drums, MidiChannel = 9 };
        var drumsOnOtherChannel = new TrackModel { Kind = TrackKind.Guitar, MidiChannel = 9 };
        var bass = new TrackModel { Kind = TrackKind.Bass, MidiChannel = 1 };
        Check("drum tracks (kind or channel 10) are skipped by pitch matching",
            Audio.AutoPitchMatcher.IsDrums(drums) && Audio.AutoPitchMatcher.IsDrums(drumsOnOtherChannel) && !Audio.AutoPitchMatcher.IsDrums(bass));
        Eq("test notes: lowest string, +7, +12", "40,47,52", string.Join(",", PitchMatch.TestNotes(40)));

        // Transpose pairing: a note-off always uses the shift of its note-on, even after the shift changed; channel 10 untouched.
        var tr = new PitchMatch.Transposer { Shift = 24 };
        byte st = 0x90, d1 = 40; tr.Map(ref st, ref d1, 100);
        var onKey = d1;
        tr.Shift = 12;
        byte st2 = 0x80, d2 = 40; tr.Map(ref st2, ref d2, 0);
        byte st3 = 0x90, d3 = 36; tr.Map(ref st3, ref d3, 0);   // velocity-0 note-on = note-off of a note never shifted: unchanged
        byte dr = 0x99, dk = 36; tr.Map(ref dr, ref dk, 100);
        byte hi = 0x90, hk = 120; var keep = tr.Map(ref hi, ref hk, 100);
        tr.Shift = 0;
        Check("transpose pairing: note-off matches its note-on after the shift changed", onKey == 64 && d2 == 64, $"on {onKey}, off {d2}");
        Check("transpose: unmatched note-off passes unchanged, drums (ch 10) never shifted", d3 == 36 && dk == 36);
        Check("transpose: a note pushed out of range is dropped", !keep);
        Check("transpose: inactive once no shifted note is held and the shift is 0", !tr.Active);
    }

    private static (int, PitchMatch.Status) Take((int Transpose, PitchMatch.Status Status, double Confidence) r) => (r.Transpose, r.Status);
}
