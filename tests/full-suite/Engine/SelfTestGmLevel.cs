using TabForge.AudioEngine.Synth;

namespace TabForge;

/// <summary>Pins the measured GM engine level calibration (docs/LEVEL_MATCH_2026-09-29.md): engine-only, offline, deterministic.</summary>
public static partial class SelfTest
{
    private static void TestGmLevelCalibration()
    {
        static double Rms(int channel, int program, int note)
        {
            var events = new[]
            {
                new GmSynthProbe.Event(0, 0xC0 | channel, program, 0), new GmSynthProbe.Event(0, 0xB0 | channel, 7, 100),
                new GmSynthProbe.Event(50, 0x90 | channel, note, 100), new GmSynthProbe.Event(650, 0x80 | channel, note, 0),
            };
            var (l, r) = GmSynthProbe.Render(events, 700);
            double sum = 0; for (var i = 0; i < l.Length; i++) sum += l[i] * (double)l[i] + r[i] * (double)r[i];
            return 10 * Math.Log10(Math.Max(sum / (2.0 * l.Length), 1e-12));
        }
        Check("GM calibration table: only snares +2.5 dB and crashes/splash -4 dB; kick and every program unchanged; trim 0; bank stereo placement",
            GmSynthTuning.DrumGainDb.GetValueOrDefault(38) == 2.5 && GmSynthTuning.DrumGainDb.GetValueOrDefault(40) == 2.5
            && GmSynthTuning.DrumGainDb.GetValueOrDefault(49) == -4 && GmSynthTuning.DrumGainDb.GetValueOrDefault(55) == -4 && GmSynthTuning.DrumGainDb.GetValueOrDefault(57) == -4
            && GmSynthTuning.DrumGainDb.GetValueOrDefault(35) == 0 && GmSynthTuning.DrumGainDb.GetValueOrDefault(36) == 0
            && GmSynthTuning.ProgramGainDb.All(v => v == 0) && GmSynthTuning.OverallTrimDb == 0 && GmSynthTuning.PanWidth == 1.0);
        var saved = GmSynthTuning.Calibrate;
        try
        {
            GmSynthTuning.Calibrate = false;
            var raw = (Kick: Rms(9, 0, 36), Crash: Rms(9, 0, 49), Bass: Rms(0, 33, 40), Piano: Rms(0, 0, 60), Snare: Rms(9, 0, 38));
            GmSynthTuning.Calibrate = true;
            var cal = (Kick: Rms(9, 0, 36), Crash: Rms(9, 0, 49), Bass: Rms(0, 33, 40), Piano: Rms(0, 0, 60), Snare: Rms(9, 0, 38));
            Near("GM calibration applied: kick 36 unchanged", 0, cal.Kick - raw.Kick, 0.3);
            Near("GM calibration applied: snare 38 is +2.5 dB", 2.5, cal.Snare - raw.Snare, 0.3);
            Near("GM calibration applied: crash 49 is -4 dB", -4, cal.Crash - raw.Crash, 0.3);
            Near("GM calibration applied: fingered bass unchanged", 0, cal.Bass - raw.Bass, 0.3);
            Near("GM calibration applied: piano unchanged", 0, cal.Piano - raw.Piano, 0.3);
        }
        finally { GmSynthTuning.Calibrate = saved; }
    }
}
