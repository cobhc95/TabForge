namespace TabForge.Views;

// TrackTimeline: song seconds <-> timeline x, for audio and MIDI clips (the host supplies the bar mapping).
internal sealed partial class TrackTimeline
{
    /// <summary>Song seconds at the start of a bar (supplied by the host).</summary>
    public Func<int, double>? BarStartSec { get; set; }
    /// <summary>Bar and position in it at a song time (supplied by the host).</summary>
    public Func<double, (int Bar, double Fraction)>? BarOfSec { get; set; }

    private double SecOfBar(int bar)
    {
        if (BarStartSec is null) return bar * 2.0;
        var s = BarStartSec(bar);
        return bar > 0 && s <= 0 && bar >= BarCount ? SecOfBar(bar - 1) + 2 : s;
    }

    private double XOfSec(double sec)
    {
        if (BarOfSec is null) return sec / 2.0 * MeasureWidth;
        var (bar, fraction) = BarOfSec(sec);
        var endOfSong = BarCount > 0 ? SecOfBar(BarCount - 1) + BarLengthSec(BarCount - 1) : 0;
        if (sec > endOfSong && BarCount > 0)   // past the song: continue at the last bar's pace
            return XOfBar(BarCount) + (sec - endOfSong) / Math.Max(0.1, BarLengthSec(BarCount - 1)) * WidthOfBar(BarCount - 1);
        return XOfBar(bar) + fraction * WidthOfBar(bar);
    }

    private double SecOfX(double x)
    {
        if (BarOfSec is null) return Math.Max(0, x / MeasureWidth * 2.0);
        if (BarCount > 0 && x >= XOfBar(BarCount))
        {
            var last = BarCount - 1;
            return SecOfBar(last) + BarLengthSec(last) + (x - XOfBar(BarCount)) / Math.Max(1, WidthOfBar(last)) * BarLengthSec(last);
        }
        var bar = BarAt(Math.Max(0, x));
        var fraction = Math.Clamp((x - XOfBar(bar)) / Math.Max(1, WidthOfBar(bar)), 0, 1);
        return SecOfBar(bar) + fraction * BarLengthSec(bar);
    }

    private double BarLengthSec(int bar)
    {
        var start = SecOfBar(bar);
        var next = bar + 1 < BarCount && BarStartSec is not null ? BarStartSec(bar + 1) : 0;
        if (next > start) return next - start;
        // Last bar (or a repeat jump): estimate from the tempo.
        var tempo = Math.Max(20, Project?.Tempo ?? 120);
        return 4 * 60.0 / tempo;
    }
}
