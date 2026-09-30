using System.Reflection;
using AlphaTab.Model;

namespace TabForge.Services;

/// <summary>
/// The standard percussion articulation table (from alphaTab's internal PercussionMapper, alphaTab 1.8.x):
/// Guitar Pro ids above the GM range (91 snare rim shot, 92 half-open hi-hat, 93 ride edge...) and the full
/// definitions (name, staff line, notehead) Guitar Pro 7/8 expects in its files. Returns null if unavailable.
/// </summary>
internal static class GpArticulations
{
    private static readonly MethodInfo? ById = typeof(InstrumentArticulation).Assembly
        .GetType("AlphaTab.Model.PercussionMapper")
        ?.GetMethod("GetArticulationById", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, new[] { typeof(double) });

    public static InstrumentArticulation? Get(int id)
    {
        try { return ById?.Invoke(null, new object[] { (double)id }) as InstrumentArticulation; }
        catch (TargetInvocationException) { return null; }
    }

    /// <summary>The GM drum sound for a Guitar Pro articulation id, or 0 when unknown.</summary>
    public static int OutputMidi(int id) => Get(id) is { } a ? (int)a.OutputMidiNumber : 0;
}
