using System.IO;

namespace TabForge.Audio.Contracts;

/// <summary>
/// The time signature and bar position of the block being processed. The engine fills it per block from the song's bar map
/// (<see cref="TransportMap"/>, sent with <see cref="EngineCommand.SetTransport"/>) and hands it to every plug-in in its transport
/// (VST2 <c>VstTimeInfo</c> timeSig / barStartPos, VST3 <c>ProcessContext</c> timeSigNumerator / timeSigDenominator / barPositionMusic).
/// <see cref="Numerator"/> 0 = unknown (no bar map yet): the plug-in gets no time-signature / bar flags.
/// </summary>
public struct TransportMeter
{
    public int Numerator, Denominator;
    /// <summary>Quarter-note (ppq) position of the start of the bar that contains the block start.</summary>
    public double BarStartPpq;

    public readonly bool IsValid => Numerator > 0 && Denominator > 0;
}

/// <summary>One performed bar of the song (repeats expanded) on the song-seconds clock of <see cref="EngineCommand.SetPosition"/>.</summary>
/// <param name="StartSec">Song seconds where the bar starts.</param>
/// <param name="StartPpq">Quarter notes from the song start to the bar start (continuous across the map).</param>
/// <param name="Tempo">Quarter notes per minute inside the bar.</param>
public readonly record struct TransportBar(double StartSec, double StartPpq, double Tempo, int Numerator, int Denominator);

/// <summary>
/// The song's bar map for the engine's live transport: written at the tail of <see cref="EngineCommand.SetTransport"/> and turned
/// into ppq position, tempo and <see cref="TransportMeter"/> per audio block by <see cref="Locate"/> (allocation-free).
/// </summary>
public static class TransportMap
{
    /// <summary>Most bars one map may carry (32 bytes each on the wire).</summary>
    public const int MaxBars = 32768;

    public static void Write(BinaryWriter w, ReadOnlySpan<TransportBar> bars)
    {
        var n = Math.Min(bars.Length, MaxBars);
        w.Write(n);
        for (var i = 0; i < n; i++)
        {
            var b = bars[i];
            w.Write(b.StartSec); w.Write(b.StartPpq); w.Write(b.Tempo); w.Write((short)b.Numerator); w.Write((short)b.Denominator);
        }
    }

    /// <summary>Reads and validates a map (bounded); throws <see cref="InvalidDataException"/> for a malformed one.</summary>
    public static TransportBar[] Read(BinaryReader r)
    {
        var n = r.ReadInt32();
        if (n is < 0 or > MaxBars) throw new InvalidDataException($"transport map: {n} bars");
        var bars = new TransportBar[n];
        for (var i = 0; i < n; i++)
        {
            var bar = new TransportBar(r.ReadDouble(), r.ReadDouble(), r.ReadDouble(), r.ReadInt16(), r.ReadInt16());
            if (!double.IsFinite(bar.StartSec) || !double.IsFinite(bar.StartPpq) || !double.IsFinite(bar.Tempo) || bar.Tempo is < 1 or > 2000
                || bar.Numerator is < 1 or > 64 || bar.Denominator is < 1 or > 64 || (bar.Denominator & (bar.Denominator - 1)) != 0
                || (i > 0 && (bar.StartSec < bars[i - 1].StartSec || bar.StartPpq < bars[i - 1].StartPpq)))
                throw new InvalidDataException($"transport map: bar {i} is not valid");
            bars[i] = bar;
        }
        return bars;
    }

    /// <summary>
    /// Audio thread, allocation-free: the transport at <paramref name="songSec"/>. <paramref name="cursor"/> is the caller's hint (the bar found
    /// last time, updated), so steady playback costs O(1); a jump falls back to a binary search. Before the first bar and after the last one the
    /// nearest bar's tempo and meter continue (whole bars). Returns false (outputs untouched) for an empty map.
    /// </summary>
    public static bool Locate(TransportBar[] bars, double songSec, ref int cursor, out double ppq, out double tempo, out TransportMeter meter)
    {
        ppq = 0; tempo = 0; meter = default;
        if (bars.Length == 0 || !double.IsFinite(songSec)) return false;
        var i = cursor;
        if ((uint)i >= (uint)bars.Length || bars[i].StartSec > songSec || (i + 1 < bars.Length && bars[i + 1].StartSec <= songSec))
        {
            if ((uint)(i + 1) < (uint)bars.Length && bars[i + 1].StartSec <= songSec && (i + 2 >= bars.Length || bars[i + 2].StartSec > songSec)) i++;
            else
            {
                int lo = 0, hi = bars.Length - 1;
                while (lo < hi)
                {
                    var mid = (lo + hi + 1) >> 1;
                    if (bars[mid].StartSec <= songSec) lo = mid; else hi = mid - 1;
                }
                i = lo;
            }
            cursor = i;
        }
        var bar = bars[i];
        ppq = bar.StartPpq + (songSec - bar.StartSec) * bar.Tempo / 60.0;
        tempo = bar.Tempo;
        var barQuarters = bar.Numerator * 4.0 / bar.Denominator;
        var intoBar = ppq - bar.StartPpq;
        // Inside the bar (the usual case) the bar start is exact; past the map's end (or before its start) whole bars continue.
        var whole = intoBar >= 0 && (i + 1 < bars.Length || intoBar < barQuarters) ? 0 : Math.Floor(intoBar / barQuarters);
        meter = new TransportMeter { Numerator = bar.Numerator, Denominator = bar.Denominator, BarStartPpq = bar.StartPpq + whole * barQuarters };
        return true;
    }
}
