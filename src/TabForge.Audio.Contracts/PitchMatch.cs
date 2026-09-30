namespace TabForge.Audio.Contracts;

/// <summary>
/// Automatic pitch matching of VST instruments: f0 estimation (YIN with parabolic interpolation), octave folding of the
/// per-note offsets, and the implicit transpose that runs right before an instrument (note-offs paired with their note-ons).
/// Pure code shared by the engine, the client and the self-test.
/// </summary>
public static class PitchMatch
{
    /// <summary>Result status of one <c>MeasurePitch</c> request.</summary>
    public enum Status : byte { Ok = 0, NoOffset = 1, Unpitched = 2, Deferred = 3, Failed = 4 }

    public static double MidiToHz(double note) => 440.0 * Math.Pow(2, (note - 69) / 12.0);

    /// <summary>
    /// YIN f0 estimate of <paramref name="x"/> (mono) between <paramref name="minHz"/> and <paramref name="maxHz"/>.
    /// Returns (0, 1) when unpitched (no dip under the threshold). Aperiodicity: 0 = perfectly periodic.
    /// </summary>
    public static (double Hz, double Aperiodicity) EstimateF0(ReadOnlySpan<float> x, int sampleRate, double minHz = 30, double maxHz = 2000, double threshold = 0.15)
    {
        var tauMin = Math.Max(2, (int)(sampleRate / maxHz));
        var tauMax = (int)(sampleRate / minHz) + 2;
        var w = x.Length - tauMax - 1;
        if (w < tauMax || tauMax <= tauMin + 2) return (0, 1);
        double energy = 0;
        for (var i = 0; i < x.Length; i++) energy += x[i] * (double)x[i];
        if (energy / x.Length < 1e-10) return (0, 1);   // silence
        var d = new double[tauMax + 1];
        for (var tau = 1; tau <= tauMax; tau++)
        {
            double s = 0;
            for (var i = 0; i < w; i++) { var v = x[i] - (double)x[i + tau]; s += v * v; }
            d[tau] = s;
        }
        // Cumulative mean normalised difference.
        var cm = new double[tauMax + 1];
        cm[0] = 1; double run = 0;
        for (var tau = 1; tau <= tauMax; tau++) { run += d[tau]; cm[tau] = run > 0 ? d[tau] * tau / run : 1; }
        var best = -1;
        for (var tau = tauMin; tau < tauMax; tau++)
        {
            if (cm[tau] < threshold)
            {
                while (tau + 1 < tauMax && cm[tau + 1] < cm[tau]) tau++;
                best = tau; break;
            }
        }
        if (best < 0) return (0, 1);
        double refined = best;
        if (best > 1 && best < tauMax)
        {
            double a = cm[best - 1], b = cm[best], c = cm[best + 1];
            var den = a - 2 * b + c;
            if (Math.Abs(den) > 1e-12) refined = best + 0.5 * (a - c) / den;
        }
        return (sampleRate / refined, cm[best]);
    }

    /// <summary>Sounding offset of a measured frequency against the played note, in semitones (positive: sounds higher).</summary>
    public static double SemitoneOffset(double hz, int note) => 12 * Math.Log2(hz / MidiToHz(note));

    /// <summary>
    /// Folds per-note sounding offsets (semitones; NaN = unpitched) into the correcting transpose: a multiple of 12 within ±36 when every
    /// voiced note lies on the same octave within ±0.5 semitone and at least two notes (or all, when fewer were played) are voiced.
    /// Returns the transpose to apply (e.g. +24 when the preset sounds two octaves low) and the status.
    /// </summary>
    public static (int Transpose, Status Status, double Confidence) Fold(IReadOnlyList<double> offsets)
    {
        var voiced = 0; int? octave = null; var agree = true; double worst = 0;
        foreach (var o in offsets)
        {
            if (double.IsNaN(o)) continue;
            voiced++;
            var oct = (int)Math.Round(o / 12.0);
            var residual = Math.Abs(o - 12 * oct);
            worst = Math.Max(worst, residual);
            if (residual > 0.5) agree = false;
            if (octave is null) octave = oct; else if (octave != oct) agree = false;
        }
        if (voiced == 0 || voiced < Math.Min(2, offsets.Count)) return (0, Status.Unpitched, 0);
        if (!agree || octave is null || Math.Abs(octave.Value) > 3) return (0, Status.NoOffset, 0);
        var confidence = Math.Round(voiced / (double)offsets.Count * (1 - worst), 2);
        return (-12 * octave.Value, octave.Value == 0 ? Status.NoOffset : Status.Ok, confidence);
    }

    /// <summary>Test notes for a track: its lowest open string (or <paramref name="fallback"/>), +7 and +12.</summary>
    public static int[] TestNotes(int? lowest, int fallback = 40)
    {
        var n = Math.Clamp(lowest ?? fallback, 12, 100);
        return new[] { n, n + 7, n + 12 };
    }

    /// <summary>
    /// Implicit transpose before one instrument. Remembers the shift used at every note-on (per channel and key) so its note-off
    /// always matches, even when the transpose changes while notes are held. Channel 10 (drums) is never transposed.
    /// Audio thread safe: no allocation after construction.
    /// </summary>
    public sealed class Transposer
    {
        private readonly sbyte[] _held = new sbyte[16 * 128];   // shift + 1 per sounding key (0 = not sounding here)
        private int _heldCount;
        /// <summary>Semitones (set from any thread).</summary>
        public volatile int Shift;
        /// <summary>True when events must go through <see cref="Map"/> (a shift is set or shifted notes are still held).</summary>
        public bool Active => Shift != 0 || _heldCount > 0;

        /// <summary>Maps one event in place; false: drop it (the transposed key is out of range).</summary>
        public bool Map(ref byte status, ref byte data1, byte data2)
        {
            var kind = status & 0xF0; var ch = status & 0x0F;
            if ((kind != 0x90 && kind != 0x80 && kind != 0xA0) || ch == 9) return true;
            var key = ch * 128 + (data1 & 0x7F);
            if (kind == 0x90 && data2 > 0)
            {
                var shift = Math.Clamp(Shift, -48, 48);
                var target = data1 + shift;
                if (target is < 0 or > 127) return false;
                if (_held[key] == 0) _heldCount++;
                _held[key] = (sbyte)(shift + 1 > 0 ? shift + 1 : shift - 1);   // never 0
                data1 = (byte)target;
                return true;
            }
            var stored = _held[key];
            if (stored == 0) return true;   // a note that started before (not shifted here)
            var used = stored > 0 ? stored - 1 : stored + 1;
            var mapped = data1 + used;
            if (kind != 0xA0) { _held[key] = 0; _heldCount--; }
            if (mapped is < 0 or > 127) return false;
            data1 = (byte)mapped;
            return true;
        }

        /// <summary>Forgets every held note (after a panic / all-notes-off).</summary>
        public void Reset() { Array.Clear(_held); _heldCount = 0; }
    }
}
