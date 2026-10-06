using TabForge.Audio;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;

namespace TabForge.Views;

// Owns: cached performed-time to source-bar mapping for clip placement and snapping.
// Does not own: score edits, playback transport, or timeline geometry.
// Tests: TestTimelineSongTimeRepeatGrowth.
internal interface ITimelineSongTimeMapHost
{
    int BarAt(double x);
    double XOfBar(int bar);
    double BarWidthOf(int bar);
}

// Owns: the timeline's revision-keyed timing map and its cached quarter map.
// Does not own: score edits or playback transport.
// Tests: TestTimelineSongTimeRepeatGrowth.
internal sealed class TrackTimelineSongTimeMapController
{
    private readonly ITimelineSongTimeMapHost _host;
    private TrackTimelineSongTimeMap? _map;
    private (SongProject Project, (int Revision, int Measures) Key, SongQuarterMap Map)? _quarterMap;

    internal TrackTimelineSongTimeMapController(ITimelineSongTimeMapHost host) => _host = host;

    internal TrackTimelineSongTimeMap Get(SongProject project)
    {
        var key = SongClock.TimelineKey(project);
        var bars = project.Tracks.Count == 0 ? 0 : project.Tracks.Max(t => t.Measures.Count);
        if (_map is not { } map || !ReferenceEquals(map.Project, project) || map.Key != key || map.BarCount != bars)
            _map = TrackTimelineSongTimeMap.Build(project, key, bars, _host);
        return _map;
    }

    internal SongQuarterMap GetQuarterMap(SongProject project)
    {
        var key = SongClock.TimelineKey(project);
        if (_quarterMap is { } cached && ReferenceEquals(cached.Project, project) && cached.Key == key) return cached.Map;
        var map = SongQuarterMap.For(project);
        _quarterMap = (project, key, map);
        return map;
    }

}

internal sealed class TrackTimelineSongTimeMap
{
    internal readonly record struct BarTime(int Bar, double StartSec, double LengthSec, FermataSpan[]? Fermatas)
    {
        public double FractionAt(double sec)
        {
            var duration = Math.Max(0.001, LengthSec * 1000);
            var baseLength = Math.Max(1.0, duration - FermataSpan.TotalExtraMs(Fermatas));
            return Math.Clamp(FermataSpan.Unwarp(Fermatas, (sec - StartSec) * 1000) / baseLength, 0, 1);
        }

        public double SecAtFraction(double fraction)
        {
            var duration = Math.Max(0.001, LengthSec * 1000);
            var baseLength = Math.Max(1.0, duration - FermataSpan.TotalExtraMs(Fermatas));
            return StartSec + FermataSpan.Warp(Fermatas, baseLength * fraction) / 1000;
        }
    }

    private readonly BarTime[] _performed;
    private readonly BarTime?[] _first;
    private readonly ITimelineSongTimeMapHost _host;
    private readonly double[] _sourceStarts;
    private readonly double[] _sourceLengths;
    private readonly double _sourceEndSec;
    internal SongProject Project { get; }
    internal (int Revision, int Measures) Key { get; }
    internal int BarCount { get; }
    internal double TerminalSec { get; }
    internal double TerminalBarLengthSec { get; }

    private TrackTimelineSongTimeMap(SongProject project, (int Revision, int Measures) key, int barCount, BarTime[] performed, BarTime?[] first, ITimelineSongTimeMapHost host)
    {
        Project = project; Key = key; BarCount = barCount; _performed = performed; _first = first; _host = host;
        _sourceStarts = new double[first.Length]; _sourceLengths = new double[first.Length];
        for (var bar = 0; bar < first.Length; bar++)
        {
            _sourceStarts[bar] = bar == 0 ? 0 : _sourceStarts[bar - 1] + _sourceLengths[bar - 1];
            _sourceLengths[bar] = first[bar]?.LengthSec ?? LocalLengthSec(project, bar, MusicTime.TempoAt(project, bar));
        }
        _sourceEndSec = first.Length == 0 ? 0 : _sourceStarts[^1] + _sourceLengths[^1];
        var last = performed.Length == 0 ? default : performed[^1];
        TerminalBarLengthSec = Math.Max(0.1, performed.Length == 0 ? 2 : last.LengthSec);
        TerminalSec = performed.Length == 0 ? 0 : last.StartSec + last.LengthSec;
    }

    internal static TrackTimelineSongTimeMap Build(SongProject project, (int Revision, int Measures) key, int barCount, ITimelineSongTimeMapHost host)
    {
        var order = PlaybackOrder.Build(project, new PlaybackOptions { RepeatExpansion = true, RespectMuteSolo = false, SkipClips = true });
        var transport = SongClock.TransportBars(project);
        var count = Math.Min(order.Count, transport.Length);
        var performed = new BarTime[count];
        var first = new BarTime?[Math.Max(0, barCount)];
        var holdsByBarTempo = new Dictionary<(int Bar, int Tempo), FermataSpan[]?>();
        for (var i = 0; i < count; i++)
        {
            var bar = order[i];
            var start = transport[i].StartSec;
            var end = i + 1 < count ? transport[i + 1].StartSec : start + LocalLengthSec(project, bar, transport[i].Tempo);
            var length = Math.Max(0.001, end - start);
            var tempo = Math.Clamp((int)Math.Round(transport[i].Tempo), 20, 400);
            if (!holdsByBarTempo.TryGetValue((bar, tempo), out var holds)) holdsByBarTempo[(bar, tempo)] = holds = FermataTime.Spans(project, bar, 1, tempo);
            var timing = new BarTime(bar, start, length, holds);
            performed[i] = timing;
            if ((uint)bar < (uint)first.Length && first[bar] is null) first[bar] = timing;
        }
        return new TrackTimelineSongTimeMap(project, key, barCount, performed, first, host);
    }

    private static double LocalLengthSec(SongProject project, int bar, double tempoValue)
    {
        var tempo = Math.Clamp((int)Math.Round(tempoValue), 20, 400);
        var slots = Math.Max(1, MusicTime.BarSlots(project, bar));
        var baseMs = MusicTime.OffsetMs(MusicTime.BarOf(project, bar), slots, tempo);
        if (project.ImportedFrom is not null)
        {
            var content = ScoreToMidiCompiler.ContentSlots(project, bar);
            if (content > 0.25 && content < slots - 0.01) baseMs *= content / slots;
        }
        return Math.Max(0.001, baseMs + FermataSpan.TotalExtraMs(FermataTime.Spans(project, bar, 1, tempo))) / 1000;
    }

    internal bool TryFirst(int bar, out BarTime timing)
    {
        if ((uint)bar < (uint)_first.Length && _first[bar] is { } found) { timing = found; return true; }
        timing = default; return false;
    }

    internal bool TryAt(double sec, out BarTime timing)
    {
        var lo = 0; var hi = _performed.Length - 1; var hit = -1;
        while (lo <= hi)
        {
            var mid = lo + ((hi - lo) >> 1);
            if (sec < _performed[mid].StartSec) hi = mid - 1;
            else { hit = mid; lo = mid + 1; }
        }
        if (hit >= 0 && sec < _performed[hit].StartSec + _performed[hit].LengthSec) { timing = _performed[hit]; return true; }
        timing = default; return false;
    }

    internal (int Bar, double Fraction) BarAt(double sec)
    {
        if (sec >= TerminalSec && _performed.Length > 0)
        {
            var extra = Math.Max(0, (sec - TerminalSec) / TerminalBarLengthSec);
            var whole = (int)Math.Floor(extra);
            return (BarCount + whole, extra - whole);
        }
        if (TryAt(sec, out var performed)) return (performed.Bar, performed.FractionAt(sec));
        return (0, 0);
    }

    internal double ClipEndX(double startSec, double endSec, double startX)
    {
        if (endSec <= startSec) return startX;
        var visualStart = VisualSecAt(startSec);
        var target = visualStart + endSec - startSec;
        if (target >= _sourceEndSec && BarCount > 0)
            return _host.XOfBar(BarCount) + (target - _sourceEndSec) / Math.Max(0.1, TerminalBarLengthSec) * _host.BarWidthOf(BarCount - 1);
        var lo = 0; var hi = _sourceStarts.Length - 1; var hit = 0;
        while (lo <= hi)
        {
            var mid = lo + ((hi - lo) >> 1);
            if (target < _sourceStarts[mid]) hi = mid - 1;
            else { hit = mid; lo = mid + 1; }
        }
        var length = Math.Max(0.001, _sourceLengths[hit]);
        var fractionAt = TryFirst(hit, out var timing)
            ? timing.FractionAt(timing.StartSec + target - _sourceStarts[hit])
            : Math.Clamp((target - _sourceStarts[hit]) / length, 0, 1);
        return _host.XOfBar(hit) + fractionAt * _host.BarWidthOf(hit);
    }

    internal double ClipSecOfX(double startSec, double x)
    {
        if (BarCount <= 0) return startSec;
        double visual;
        if (x >= _host.XOfBar(BarCount))
            visual = _sourceEndSec + (x - _host.XOfBar(BarCount)) / Math.Max(1, _host.BarWidthOf(BarCount - 1)) * TerminalBarLengthSec;
        else
        {
            var bar = Math.Clamp(_host.BarAt(Math.Max(0, x)), 0, BarCount - 1);
            var fraction = Math.Clamp((x - _host.XOfBar(bar)) / Math.Max(1, _host.BarWidthOf(bar)), 0, 1);
            visual = _sourceStarts[bar] + VisualOffset(bar, fraction);
        }
        return startSec + visual - VisualSecAt(startSec);
    }

    private double VisualSecAt(double sec)
    {
        var (bar, fraction) = BarAt(sec);
        return bar >= BarCount
            ? _sourceEndSec + (bar - BarCount + fraction) * TerminalBarLengthSec
            : _sourceStarts[Math.Clamp(bar, 0, Math.Max(0, BarCount - 1))] + VisualOffset(Math.Clamp(bar, 0, Math.Max(0, BarCount - 1)), fraction);
    }

    private double VisualOffset(int bar, double fraction)
    {
        if (TryFirst(bar, out var timing)) return timing.SecAtFraction(fraction) - timing.StartSec;
        return fraction * _sourceLengths[bar];
    }

    internal bool TryFirst(int bar, out double startSec, out double lengthSec)
    {
        if (TryFirst(bar, out var timing)) { startSec = timing.StartSec; lengthSec = timing.LengthSec; return true; }
        startSec = 0; lengthSec = 0; return false;
    }
}
