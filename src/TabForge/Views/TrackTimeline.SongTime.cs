using TabForge.Models;

namespace TabForge.Views;

// TrackTimeline: song seconds <-> timeline x, for audio and MIDI clips (the host supplies the bar mapping).
internal sealed partial class TrackTimeline : ITimelineSongTimeMapHost
{
    private TrackTimelineSongTimeMapController? _songTimeMap;
    /// <summary>Song seconds at the start of a bar (supplied by the host).</summary>
    public Func<int, double>? BarStartSec { get; set; }
    /// <summary>Bar and position in it at a song time (supplied by the host).</summary>
    public Func<double, (int Bar, double Fraction)>? BarOfSec { get; set; }

    private double SecOfBar(int bar)
    {
        if (BarStartSec is not null && Project is { } project)
        {
            var map = SongTimeMap(project);
            if (bar >= map.BarCount) return map.TerminalSec + Math.Max(0, bar - map.BarCount) * map.TerminalBarLengthSec;
            if (map.TryFirst(bar, out var first)) return first.StartSec;
        }
        if (BarStartSec is null) return bar * 2.0;
        var s = BarStartSec(bar);
        return bar > 0 && s <= 0 && bar >= BarCount ? SecOfBar(bar - 1) + 2 : s;
    }

    public double XOfSec(double sec)
    {
        if (BarOfSec is null) return sec / 2.0 * MeasureWidth;
        if (Project is { } project)
        {
            var map = SongTimeMap(project);
            var terminal = map.TerminalSec;
            if (sec >= terminal && BarCount > 0)
                return XOfBar(BarCount) + (sec - terminal) / Math.Max(0.1, map.TerminalBarLengthSec) * WidthOfBar(BarCount - 1);
            if (map.TryAt(sec, out var performed))
                return XOfBar(performed.Bar) + performed.FractionAt(sec) * WidthOfBar(performed.Bar);
        }
        var (bar, fraction) = BarOfSec(sec);
        var endOfSong = BarCount > 0 ? SecOfBar(BarCount - 1) + BarLengthSec(BarCount - 1) : 0;
        if (sec > endOfSong && BarCount > 0)   // past the song: continue at the last bar's pace
            return XOfBar(BarCount) + (sec - endOfSong) / Math.Max(0.1, BarLengthSec(BarCount - 1)) * WidthOfBar(BarCount - 1);
        return XOfBar(bar) + fraction * WidthOfBar(bar);
    }

    public double SecOfX(double x)
    {
        if (BarOfSec is null) return Math.Max(0, x / MeasureWidth * 2.0);
        if (Project is { } project)
        {
            var map = SongTimeMap(project);
            if (BarCount > 0 && x >= XOfBar(BarCount))
                return map.TerminalSec + (x - XOfBar(BarCount)) / Math.Max(1, WidthOfBar(BarCount - 1)) * map.TerminalBarLengthSec;
            var mappedBar = BarAt(Math.Max(0, x));
            var mappedFraction = Math.Clamp((x - XOfBar(mappedBar)) / Math.Max(1, WidthOfBar(mappedBar)), 0, 1);
            if (map.TryFirst(mappedBar, out var first)) return first.SecAtFraction(mappedFraction);
        }
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
        if (BarOfSec is not null && Project is { } project && SongTimeMap(project).TryFirst(bar, out var first)) return first.LengthSec;
        var start = SecOfBar(bar);
        var next = bar + 1 < BarCount && BarStartSec is not null ? BarStartSec(bar + 1) : 0;
        if (next > start) return next - start;
        // Last bar (or a repeat jump): estimate from the tempo.
        var tempo = Math.Max(20, Project?.Tempo ?? 120);
        return TabForge.Audio.Contracts.TempoMath.BeatsToSeconds(4, tempo);
    }

    private TrackTimelineSongTimeMap SongTimeMap(SongProject project) => (_songTimeMap ??= new TrackTimelineSongTimeMapController(this)).Get(project);
    internal (int Bar, double Fraction) BarAtSec(double sec)
    {
        if (BarOfSec is null) return (0, 0);
        return Project is { } project ? SongTimeMap(project).BarAt(sec) : BarOfSec(sec);
    }

    public double ClipEndX(double startSec, double endSec)
    {
        var startX = XOfSec(startSec);
        if (BarOfSec is not null && Project is { } project && BarCount > 0)
            return SongTimeMap(project).ClipEndX(startSec, endSec, startX);
        return endSec > startSec ? Math.Max(startX + 3, XOfSec(endSec)) : startX;
    }

    public double ClipSecOfX(double startSec, double x)
    {
        if (BarOfSec is not null && Project is { } project && BarCount > 0) return SongTimeMap(project).ClipSecOfX(startSec, x);
        return SecOfX(x);
    }
}
