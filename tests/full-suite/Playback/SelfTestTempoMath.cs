using System.Text;
using EM = TabForge.AudioEngine.Midi;
using EP = TabForge.AudioEngine.Plugins;
using TabForge.Audio.Contracts;
using TabForge.Models;
using TabForge.Services;

namespace TabForge;

/// <summary>
/// Tempo and time conversions (part of <see cref="SelfTest"/>): the shared <c>TempoMath</c> helpers and every place that converts
/// between beats, seconds, milliseconds and samples must give bit-identical results over a tempo / tick sweep. The reference
/// expressions below are written out in full, so they do not depend on the helpers they check.
/// </summary>
public static partial class SelfTest
{
    private static readonly double[] TempoSweep =
    {
        double.NaN, 0, 1, 1.5, 20, 59.9, 60, 87, 100, 119.99, 120, 133.3, 174.5, 200, 240, 399, 400, 999.5, 2000,
    };

    private static readonly int[] IntTempoSweep = { 1, 19, 20, 33, 60, 77, 90, 119, 120, 133, 150, 187, 200, 301, 399, 400, 401, 500 };

    private static long FnvStep(long hash, string text)
    {
        foreach (var ch in text) hash = (hash ^ ch) * 1099511628211L;
        return hash;
    }

    private static string ProcessorSweepHash(string type, string json)
    {
        long hash = unchecked((long)14695981039346656037UL);
        var events = 0;
        foreach (var tempo in TempoSweep)
        {
            var chain = EM.MidiProcessorChain.Create(new List<MidiProcSpec> { new(type, true, json) }, 48000)!;
            var blocks = new[]
            {
                new[] { Ev(0, 0x90, 60, 100), Ev(100, 0x90, 64, 90), Ev(3000, 0x90, 67, 80) },
                new[] { Ev(10, 0x80, 64, 0), Ev(500, 0x90, 60, 100), Ev(9000, 0x80, 60, 0) },
                new[] { Ev(20, 0x80, 60, 0), Ev(21, 0x80, 67, 0), Ev(5000, 0x90, 72, 70) },
                Array.Empty<EP.BlockMidi>(),
            };
            hash = FnvStep(hash, "T" + BitConverter.DoubleToInt64Bits(tempo));
            foreach (var input in blocks)
            {
                var buffer = new EM.MidiBuffer();
                chain.Process(input, buffer, 24000, new EP.TransportInfo { Tempo = tempo, Playing = true }, null);
                foreach (var e in buffer.Span.ToArray()) { hash = FnvStep(hash, $"{e.Frame}:{e.Status:X2}:{e.Data1}:{e.Data2};"); events++; }
            }
        }
        return $"{hash:X16}/{events}";
    }

    // The conversions as they were written before they were shared (the bit-identity reference).
    private static double RefConstMs(double slots, double tempo, double tempoScale)
        => slots / 4 * (60000.0 / Math.Clamp(tempo, 20, 400)) * tempoScale;

    private static double RefRampMs(double x, double t0, double t1, double ramp, double tempoScale)
    {
        if (x <= 0) return 0;
        if (Math.Abs(t1 - t0) < 1e-9 || ramp <= 0) return RefConstMs(x, t0, tempoScale);
        var tx = t0 + (t1 - t0) * x / ramp;
        return 60000.0 / 4 * ramp / (t1 - t0) * Math.Log(tx / t0) * tempoScale;
    }

    private static double RefOffsetMs(MeasureModel? bar, double slot, int startTempo, double tempoScale)
    {
        if (bar?.MidBarTempos is not { Count: > 0 } points) return slot / 4 * (60000.0 / Math.Clamp(startTempo, 20, 400)) * tempoScale;
        double ms = 0, at = 0, tempo = startTempo;
        for (var i = 0; i < points.Count; i++)
        {
            var point = points[i];
            if (point.Slot >= slot) break;
            ms += RefConstMs(Math.Max(0, point.Slot - at), tempo, tempoScale);
            at = Math.Max(at, point.Slot);
            var target = Math.Clamp(point.Tempo, 20, 400);
            var next = i + 1 < points.Count ? points[i + 1].Slot : double.MaxValue;
            var length = point.RampSlots > 0 ? Math.Min(point.RampSlots, next - at) : 0;
            if (length <= 0) { tempo = target; continue; }
            var used = Math.Min(slot, at + length) - at;
            ms += RefRampMs(used, tempo, target, point.RampSlots, tempoScale);
            if (at + length >= slot) return ms;
            tempo += (target - tempo) * length / point.RampSlots;
            at += length;
        }
        return ms + RefConstMs(Math.Max(0, slot - at), tempo, tempoScale);
    }

    private static bool SameBits(double a, double b) => BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b);

    private static void TestTempoMath()
    {
        // MIDI processors: output over the tempo sweep equals the output recorded before the conversions were shared.
        var processors = new (string Type, string Json, string Golden)[]
        {
            ("repeater", "{\"Size\":0.3}", "330988BFBF0D8CEE/1943"),
            ("arp", "{\"Rate\":3,\"Mode\":3,\"Variants\":2}", "EEB783E9D710529D/1045"),
            ("modalRandom", "{\"Notes\":3,\"TimingRandom\":80}", "8D43CB205D87C3F1/581"),
            ("lfo", "{\"Cc\":1,\"Shape\":0,\"Freq\":2,\"Sync\":1,\"Updates\":16}", "A49D42C1A82B2D8B/3097"),
            ("lfo", "{\"Cc\":1,\"Shape\":1,\"Freq\":3,\"Sync\":0,\"Updates\":24}", "57CBD1958E777C40/4459"),
            ("stepSeq", "{\"Pattern1\":\"0 . 7 3\",\"Steps\":4,\"StepsPerBeat\":3,\"Gate\":50,\"Swing\":20}", "6568951A8A5BD793/990"),
            ("sanitizer", "{\"Retrigger\":40}", "E94FB612D83A678D/137"),
            ("delay", "{\"Ms\":7,\"Beats\":1.5,\"Samples\":3}", "28607414052F470E/137"),
        };
        foreach (var (type, json, golden) in processors)
        {
            var first = ProcessorSweepHash(type, json);
            var second = ProcessorSweepHash(type, json);
            Log.Add($"  info  tempo sweep {type} {json}: {first}");
            Check($"tempo sweep: {type} is deterministic", first == second);
            Check($"tempo sweep: {type} output over {TempoSweep.Length} tempos is unchanged ({json})", first == golden, first);
        }

        // MusicTime: slots to milliseconds, ramps and mid-bar tempo maps.
        var constantOk = true; var rampOk = true; var offsetOk = true; var firstBad = "";
        foreach (var scale in new[] { 1.0, 0.5, 1.37 })
        {
            foreach (var tempo in IntTempoSweep)
                foreach (var slots in new[] { 0.0, 0.25, 1, 4, 7.5, 16, 33.333 })
                    if (!SameBits(MusicTime.SlotsToMsAt(slots, tempo, scale), slots / 4 * (60000.0 / Math.Clamp(tempo, 20, 400)) * scale))
                    { constantOk = false; firstBad = $"SlotsToMsAt {slots} @{tempo} x{scale}"; }
            foreach (var t0 in new[] { 40.0, 90, 133.5, 200, 380 })
                foreach (var t1 in new[] { 40.0, 61, 133.5, 220, 400 })
                    foreach (var ramp in new[] { 0.0, 4, 16, 24 })
                        foreach (var x in new[] { -1.0, 0, 1, 7.25, 16, 24 })
                            if (!SameBits(MusicTime.RampMs(x, t0, t1, ramp, scale), RefRampMs(x, t0, t1, ramp, scale)))
                            { rampOk = false; firstBad = $"RampMs {x} {t0}->{t1} over {ramp} x{scale}"; }
            var maps = new[]
            {
                null,
                new List<TempoPoint> { new(0, 200, 16) },
                new List<TempoPoint> { new(6, 90), new(10, 150, 4) },
                new List<TempoPoint> { new(2, 60, 8), new(8, 300, 4), new(14, 133) },
            };
            foreach (var map in maps)
            {
                var bar = new MeasureModel { MidBarTempos = map };
                foreach (var start in new[] { 20, 77, 133, 400 })
                    foreach (var slot in new[] { 0.0, 1, 3.5, 6, 8, 10, 12, 15.9, 16, 20 })
                        if (!SameBits(MusicTime.OffsetMs(bar, slot, start, scale), RefOffsetMs(bar, slot, start, scale)))
                        { offsetOk = false; firstBad = $"OffsetMs slot {slot} start {start} x{scale} map {map?.Count}"; }
            }
        }
        Check("tempo math: MusicTime slots-to-ms is bit-identical over the sweep", constantOk, firstBad);
        Check("tempo math: MusicTime ramp integral is bit-identical over the sweep", rampOk, firstBad);
        Check("tempo math: MusicTime offset over mid-bar tempo maps is bit-identical over the sweep", offsetOk, firstBad);

        // Song seconds <-> quarter notes (imported MIDI clips and plug-in transport share one bar map).
        var bars = new List<TransportBar>();
        double sec = 0, ppq = 0;
        foreach (var tempo in new[] { 120.0, 133.3, 90, 187, 60, 240 })
        {
            bars.Add(new TransportBar(sec, ppq, tempo, 4, 4));
            sec += 4 * 60.0 / tempo; ppq += 4;
        }
        var map2 = new SongQuarterMap(bars, 111);
        var quarterOk = true;
        for (var s = -1.0; s < sec + 5; s += 0.37)
        {
            var expect = ExpectedQuarterAt(bars, s);
            if (!SameBits(map2.QuarterAt(s), expect)) { quarterOk = false; firstBad = $"QuarterAt {s}"; }
            var q = ExpectedQuarterAt(bars, s);
            if (!SameBits(map2.SecAt(q), ExpectedSecAt(bars, q))) { quarterOk = false; firstBad = $"SecAt {q}"; }
        }
        Check("tempo math: song quarter map is bit-identical over a bar map", quarterOk, firstBad);

        // Song extent: the length of one more bar.
        var extentOk = true;
        foreach (var bpm in new[] { 40, 77, 120, 133, 187, 300 })
            foreach (var (num, den) in new[] { (4, 4), (3, 4), (6, 8), (7, 8), (5, 16), (2, 2) })
            {
                var p = SingleTrack(3, bpm);
                p.Tracks[0].Measures[2].TimeSigNum = num; p.Tracks[0].Measures[2].TimeSigDenom = den;
                var measured = SongExtent.Measure(p);
                // The last bar's own length is not audible to the helper under test; recompute the reference from the same inputs.
                if (!SameBits(measured.BarSec, num * (4.0 / den) * 60.0 / bpm)) { extentOk = false; firstBad = $"extent {num}/{den} @{bpm}: {measured.BarSec}"; }
            }
        Check("tempo math: song extent bar length is bit-identical over tempos and meters", extentOk, firstBad);

        // The shared helpers against the expressions they replace, bit for bit.
        var helperOk = true;
        foreach (var bpm in TempoSweep.Concat(IntTempoSweep.Select(t => (double)t)))
        {
            if (!SameBits(TempoMath.Effective(bpm), bpm > 1 ? bpm : 120)) { helperOk = false; firstBad = $"Effective {bpm}"; }
            if (!SameBits(TempoMath.SecondsPerBeat(bpm), 60.0 / bpm)) { helperOk = false; firstBad = $"SecondsPerBeat {bpm}"; }
            if (!SameBits(TempoMath.MsPerBeat(bpm), 60000.0 / bpm)) { helperOk = false; firstBad = $"MsPerBeat {bpm}"; }
            foreach (var beats in new[] { 0.1, 0.25, 0.3, 1, 1.5, 4, 5.25, 16 })
            {
                if (!SameBits(TempoMath.BeatsToSeconds(beats, bpm), beats * 60.0 / bpm)) { helperOk = false; firstBad = $"BeatsToSeconds {beats} @{bpm}"; }
                if (!SameBits(TempoMath.SecondsToBeats(beats, bpm), beats * bpm / 60.0)) { helperOk = false; firstBad = $"SecondsToBeats {beats} @{bpm}"; }
                foreach (var rate in new[] { 22050, 44100, 48000, 96000 })
                {
                    if (!SameBits(TempoMath.BeatsToSamples(beats, bpm, rate), beats * 60.0 / bpm * rate)) { helperOk = false; firstBad = $"BeatsToSamples {beats} @{bpm} {rate}"; }
                    if (!SameBits(TempoMath.SamplesPerBeat(bpm, rate), 60.0 / bpm * rate)) { helperOk = false; firstBad = $"SamplesPerBeat @{bpm} {rate}"; }
                    if (!SameBits(TempoMath.SampleRateBeat(rate, bpm), rate * 60.0 / bpm)) { helperOk = false; firstBad = $"SampleRateBeat @{bpm} {rate}"; }
                }
            }
        }
        Check("tempo math: TempoMath helpers equal the plain expressions bit for bit", helperOk, firstBad);
    }

    private static int BarIndexAt(List<TransportBar> bars, double value, Func<TransportBar, double> key)
    {
        int lo = 0, hi = bars.Count - 1;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) >> 1;
            if (key(bars[mid]) <= value) lo = mid; else hi = mid - 1;
        }
        return lo;
    }

    private static double ExpectedQuarterAt(List<TransportBar> bars, double sec)
    {
        var i = BarIndexAt(bars, sec, b => b.StartSec);
        return bars[i].StartPpq + (sec - bars[i].StartSec) * Math.Clamp(bars[i].Tempo, 1, 2000) / 60.0;
    }

    private static double ExpectedSecAt(List<TransportBar> bars, double quarter)
    {
        var i = BarIndexAt(bars, quarter, b => b.StartPpq);
        return bars[i].StartSec + (quarter - bars[i].StartPpq) * 60.0 / Math.Clamp(bars[i].Tempo, 1, 2000);
    }
}
