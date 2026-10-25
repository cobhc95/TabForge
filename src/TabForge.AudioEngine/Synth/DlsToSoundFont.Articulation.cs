namespace TabForge.AudioEngine.Synth;

// Owns: DLS articulation resolution into SF2 generator units (instrument and region merge, sustain and time-cent mapping).
public static partial class DlsToSoundFont
{
    // ------------------------------------------------------------------------------------------------
    // Articulation -> SF2 generators
    // ------------------------------------------------------------------------------------------------

    /// <summary>Resolved per-region synthesis parameters, in SF2 units.</summary>
    private sealed class Articulation
    {
        // Volume envelope (timecents; sustain in cB of attenuation).
        public double Eg1Delay = -12000, Eg1Attack = -12000, Eg1Hold = -12000, Eg1Decay = -12000, Eg1Release = -12000;
        public double Eg1SustainCb;
        public double KeyToEg1Decay, KeyToEg1Hold; // SF2 tc/key
        // Modulation envelope.
        public double Eg2Delay = -12000, Eg2Attack = -12000, Eg2Hold = -12000, Eg2Decay = -12000, Eg2Release = -12000;
        public double Eg2SustainPercentDrop; // SF2 sustainModEnv in 0.1% below peak
        public double KeyToEg2Decay, KeyToEg2Hold;
        public double Eg2ToPitch, Eg2ToFilter;
        // LFOs (absolute cents / timecents).
        public double LfoFreq = -851.3179, LfoDelay = -7972.6274; // DLS1 defaults: 5 Hz, 10 ms
        public double VibFreq = -851.3179, VibDelay = -7972.6274;
        public double LfoToPitch, LfoToVolumeCb, LfoToFilter, VibToPitch;
        // Misc.
        public double GainDb;      // constant gain from articulation (dB, negative = quieter)
        public double Pan;         // SF2 0.1% units
        public double FilterFc = 13500, FilterQCb;
        public double ReverbSend, ChorusSend; // 0.1%
        public double ScaleTuning = 100;
        public double PitchCents;
    }

    private static double Tc(int dlsScale) => dlsScale == TimeCentsZero ? -12000 : dlsScale / 65536.0;

    private static Articulation Resolve(List<Connection>? instrumentArt, List<Connection>? regionArt)
    {
        // Later entries override earlier ones for the same (source, control, destination).
        var map = new Dictionary<(ushort, ushort, ushort), int>();
        if (instrumentArt is not null) foreach (var c in instrumentArt) map[(c.Source, c.Control, c.Destination)] = c.Scale;
        if (regionArt is not null) foreach (var c in regionArt) map[(c.Source, c.Control, c.Destination)] = c.Scale;

        bool Has(ushort src, ushort ctl, ushort dst, out int v) => map.TryGetValue((src, ctl, dst), out v);

        var a = new Articulation();
        if (Has(SrcNone, SrcNone, DstEg1Delay, out int v)) a.Eg1Delay = Tc(v);
        if (Has(SrcNone, SrcNone, DstEg1Attack, out v)) a.Eg1Attack = Tc(v);
        if (Has(SrcNone, SrcNone, DstEg1Hold, out v)) a.Eg1Hold = Tc(v);
        if (Has(SrcNone, SrcNone, DstEg1Decay, out v)) a.Eg1Decay = Tc(v);
        if (Has(SrcNone, SrcNone, DstEg1Release, out v)) a.Eg1Release = Tc(v);
        double sustainPct = 100; // DLS default: 100 %
        if (Has(SrcNone, SrcNone, DstEg1Sustain, out v)) sustainPct = Math.Clamp(v / 65536.0 / 10.0, 0, 100);
        a.Eg1SustainCb = SustainPercentToCb(sustainPct);

        // Key number scaling: DLS scale is tc over the full 0..127 key range (source normalised to key/128);
        // SF2 is tc per key relative to key 60 with the opposite sign convention.
        if (Has(SrcKeyNumber, SrcNone, DstEg1Decay, out v)) { double s = v / 65536.0; a.KeyToEg1Decay = -s / 128.0; a.Eg1Decay += s * 60 / 128.0; }
        if (Has(SrcKeyNumber, SrcNone, DstEg1Hold, out v)) { double s = v / 65536.0; a.KeyToEg1Hold = -s / 128.0; a.Eg1Hold += s * 60 / 128.0; }

        if (Has(SrcNone, SrcNone, DstEg2Delay, out v)) a.Eg2Delay = Tc(v);
        if (Has(SrcNone, SrcNone, DstEg2Attack, out v)) a.Eg2Attack = Tc(v);
        if (Has(SrcNone, SrcNone, DstEg2Hold, out v)) a.Eg2Hold = Tc(v);
        if (Has(SrcNone, SrcNone, DstEg2Decay, out v)) a.Eg2Decay = Tc(v);
        if (Has(SrcNone, SrcNone, DstEg2Release, out v)) a.Eg2Release = Tc(v);
        double eg2Sustain = 100;
        if (Has(SrcNone, SrcNone, DstEg2Sustain, out v)) eg2Sustain = Math.Clamp(v / 65536.0 / 10.0, 0, 100);
        a.Eg2SustainPercentDrop = (100 - eg2Sustain) * 10;
        if (Has(SrcKeyNumber, SrcNone, DstEg2Decay, out v)) { double s = v / 65536.0; a.KeyToEg2Decay = -s / 128.0; a.Eg2Decay += s * 60 / 128.0; }
        if (Has(SrcKeyNumber, SrcNone, DstEg2Hold, out v)) { double s = v / 65536.0; a.KeyToEg2Hold = -s / 128.0; a.Eg2Hold += s * 60 / 128.0; }
        if (Has(SrcEg2, SrcNone, DstPitch, out v)) a.Eg2ToPitch = v / 65536.0;
        if (Has(SrcEg2, SrcNone, DstFilterCutoff, out v)) a.Eg2ToFilter = v / 65536.0;

        if (Has(SrcNone, SrcNone, DstLfoFrequency, out v)) a.LfoFreq = v / 65536.0;
        if (Has(SrcNone, SrcNone, DstLfoStartDelay, out v)) a.LfoDelay = Tc(v);
        if (Has(SrcNone, SrcNone, DstVibFrequency, out v)) a.VibFreq = v / 65536.0; else a.VibFreq = a.LfoFreq;
        if (Has(SrcNone, SrcNone, DstVibStartDelay, out v)) a.VibDelay = Tc(v); else a.VibDelay = a.LfoDelay;
        if (Has(SrcLfo, SrcNone, DstPitch, out v)) a.LfoToPitch = v / 65536.0;
        if (Has(SrcLfo, SrcNone, DstAttenuation, out v)) a.LfoToVolumeCb = -v / 65536.0; // gain -> attenuation
        if (Has(SrcLfo, SrcNone, DstFilterCutoff, out v)) a.LfoToFilter = v / 65536.0;
        if (Has(SrcVibrato, SrcNone, DstPitch, out v)) a.VibToPitch = v / 65536.0;

        if (Has(SrcNone, SrcNone, DstAttenuation, out v)) a.GainDb = v / 655360.0;
        if (Has(SrcNone, SrcNone, DstPan, out v)) a.Pan = v / 65536.0;
        if (Has(SrcNone, SrcNone, DstPitch, out v)) a.PitchCents = v / 65536.0;
        if (Has(SrcNone, SrcNone, DstFilterCutoff, out v) && v != int.MaxValue) a.FilterFc = v / 65536.0;
        if (Has(SrcNone, SrcNone, DstFilterQ, out v)) a.FilterQCb = v / 65536.0;
        if (Has(SrcNone, SrcNone, DstReverb, out v)) a.ReverbSend = v / 65536.0;
        if (Has(SrcNone, SrcNone, DstChorus, out v)) a.ChorusSend = v / 65536.0;
        if (Has(SrcKeyNumber, SrcNone, DstPitch, out v)) a.ScaleTuning = v / 65536.0 / 128.0;
        return a;
    }

    /// <summary>
    /// DLS EG1 sustain is a fraction of peak on the envelope's 96 dB attenuation scale:
    /// attenuation = 96 dB * (1 - sustain%).
    /// </summary>
    private static double SustainPercentToCb(double pct) => 960.0 * (1.0 - pct / 100.0);
}
