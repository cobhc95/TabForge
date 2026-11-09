using TabForge.Sf2Synth.Dsp;
using TabForge.Sf2Synth.Nam;

namespace TabForge.Sf2Synth.Guitar;

// Owns: loading the user's amp (.nam) and cabinet (.wav IR) for the electric guitar programs into one slot per channel, the
//   built-in captured amp (embedded Amps/captured.nam, parsed once; null if unusable, then the stage amp plays), and the status
//   line saying what is in use. Any missing or broken file falls back to the built-in part with the reason. Off the audio thread.
// Does not own: running them (GuitarBus), the swap (GuitarLayer.LoadRig), when the captured amp plays (GuitarBus, SynthOptions.DistAmp).
// Tests: GuitarLayerTests.
internal static class GuitarRig
{
    internal sealed record Slot(NamAmp? Amp, FftConvolver? Cab);

    private static readonly Lazy<NamModel?> CapturedModel = new(() =>
    {
        try
        {
            using var s = typeof(GuitarRig).Assembly.GetManifestResourceStream("TabForge.Sf2Synth.Guitar.Amps.captured.nam");
            return s is null ? null : NamFile.Load(s, "captured");
        }
        catch (Exception) { return null; }   // a broken resource: the stage amp plays
    });

    public static NamModel? Captured => CapturedModel.Value;

    public static (Slot[]? Slots, string Status) Build(string? namPath, string? irPath, double rate, int maxBlock, int channels)
    {
        NamModel? model = null;
        float[]? ir = null;
        string amp = "built-in amp", cab = "built-in cabinet";
        if (!string.IsNullOrWhiteSpace(namPath))
            try
            {
                model = NamFile.Load(namPath);
                amp = $"amp {model.Name} ({model.Architecture}, {model.Rate / 1000:0.#} kHz)";
            }
            catch (Exception e)   // any broken file: keep playing on the built-in amp
            {
                amp = $"built-in amp ({Path.GetFileName(namPath)} not usable: {Reason(e)})";
            }
        if (!string.IsNullOrWhiteSpace(irPath))
            try
            {
                ir = WavIr.Load(irPath, rate);
                cab = $"cabinet {Path.GetFileNameWithoutExtension(irPath)} ({1000.0 * ir.Length / rate:0} ms)";
            }
            catch (Exception e)
            {
                cab = $"built-in cabinet ({Path.GetFileName(irPath)} not usable: {Reason(e)})";
            }
        var status = $"Guitar: {amp}, {cab}.";
        if (model is null && ir is null) return (null, status);
        var slots = new Slot[channels];
        for (var i = 0; i < channels; i++) slots[i] = new Slot(model is null ? null : new NamAmp(model, rate, maxBlock), ir is null ? null : new FftConvolver(ir));
        return (slots, status);
    }

    private static string Reason(Exception e) => e is FileNotFoundException or DirectoryNotFoundException ? "file not found" : e.Message.Split('\n')[0];
}
