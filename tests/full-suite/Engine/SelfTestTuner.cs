using EA = TabForge.AudioEngine.Audio;

namespace TabForge;

/// <summary>Tuner pitch detection (part of <see cref="SelfTest"/>): synthetic tones through the engine's detector.</summary>
public static partial class SelfTest
{
    private static void TestTunerPitchDetection()
    {
        static float[] Tone(int rate, double hz, int frames, double amp = 0.5, bool harmonics = false)
        {
            var x = new float[frames];
            for (var i = 0; i < frames; i++)
            {
                var t = 2 * Math.PI * hz * i / rate;
                var v = Math.Sin(t);
                if (harmonics) v = 0.6 * v + 0.35 * Math.Sin(2 * t + 0.7) + 0.25 * Math.Sin(3 * t + 1.9);
                x[i] = (float)(amp * v);
            }
            return x;
        }
        static double CentsOff(double measured, double truth) => 1200 * Math.Log2(measured / truth);

        var worst = 0.0; var worstDesc = "";
        var failures = 0;
        foreach (var rate in new[] { 48000, 44100 })
        {
            var detector = new EA.PitchDetector(rate);
            foreach (var baseHz in new[] { 82.41, 110.0, 196.0, 440.0, 987.77 })
                foreach (var offset in new[] { 0.0, 20.0, -35.0 })
                {
                    var truth = baseHz * Math.Pow(2, offset / 1200);
                    var raw = Tone(rate, truth, detector.RawWindow + 100);
                    var hz = detector.Detect(raw, out _);
                    var err = hz > 0 ? Math.Abs(CentsOff(hz, truth)) : 999;
                    if (err > worst) { worst = err; worstDesc = $"{truth:0.00} Hz at {rate} Hz measured {hz:0.00}"; }
                    if (err > 2) failures++;
                }
            // Guitar-like tone (strong harmonics) must not jump an octave.
            var rich = Tone(rate, 82.41, detector.RawWindow, harmonics: true);
            var richHz = detector.Detect(rich, out var clarity);
            Check($"tuner: a harmonic-rich low E is read as 82.41 Hz, not an octave off ({rate} Hz)", richHz > 0 && Math.Abs(CentsOff(richHz, 82.41)) <= 3, $"{richHz:0.00} Hz, clarity {clarity:0.00}");
            // Silence and a very quiet hum give no pitch; noise-free DC gives none either.
            Check($"tuner: silence gives no pitch ({rate} Hz)", detector.Detect(new float[detector.RawWindow], out _) == 0);
            Check($"tuner: a window shorter than needed gives no pitch ({rate} Hz)", detector.Detect(new float[100], out _) == 0);
        }
        Check("tuner: sine waves 82.41 / 110 / 196 / 440 / 988 Hz, 0 / +20 / -35 cents, at 48 and 44.1 kHz, are all within 2 cents",
            failures == 0, $"{failures} outside 2 cents; worst {worst:0.00} cents ({worstDesc})");

        // End to end through the input ring: what the engine main thread reads for the tuner.
        var capture = new EA.InputCapture("selftest", 48000, 1, 5);
        var tone = Tone(48000, 110, 24000);
        capture.Feed(tone);
        var det = new EA.PitchDetector(48000);
        var window = new float[det.RawWindow];
        var got = capture.CopyLatestMono(window);
        var measured = got ? det.Detect(window, out _) : 0;
        Check("tuner: a tone fed to the input capture is read back and detected within 2 cents", got && measured > 0 && Math.Abs(CentsOff(measured, 110)) <= 2, $"{measured:0.00} Hz");
        Check("tuner: reading the input for the tuner does not consume it (the audio thread still gets every frame)",
            capture.TotalFrames == 24000);
        var (midi, cents) = EA.PitchDetector.NearestNote(440 * Math.Pow(2, 30.0 / 1200));
        Check("tuner: nearest note of 440 Hz +30 cents is A4 (MIDI 69) at +30 cents", midi == 69 && Math.Abs(cents - 30) < 0.01, $"{midi} {cents:0.0}");
    }
}
