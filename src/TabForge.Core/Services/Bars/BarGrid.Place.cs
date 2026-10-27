using TabForge.Models;

namespace TabForge.Services;

// Owns: appending, overwriting and inserting bars.
// Does not own: the rest of this type (see the main file).
// Tests: no dedicated test class.
public static partial class BarGrid
{
    /// <summary>Appends <paramref name="count"/> empty bars to every track (keeping them aligned); each new bar takes its
    /// track's last bar's time signature, key and clef.</summary>
    public static void AppendBars(SongProject p, int count)
    {
        if (count <= 0) return;
        PadTracks(p);
        foreach (var track in p.Tracks)
        {
            var last = track.Measures.LastOrDefault();
            for (var i = 0; i < count; i++)
                track.Measures.Add(new MeasureModel
                {
                    TimeSigNum = last?.TimeSigNum, TimeSigDenom = last?.TimeSigDenom,
                    KeySignature = last?.KeySignature, KeySignatureMinor = last?.KeySignatureMinor,
                    Clef = last?.Clef ?? Clefs.Guitar,
                    Cells = EmptyCells(p, last?.TimeSigNum, last?.TimeSigDenom)
                });
            BarRangeEditor.Renumber(track);
        }
    }

    private static void PadTracks(SongProject p)
    {
        var total = BarRangeEditor.MaxMeasures(p);
        foreach (var track in p.Tracks)
        {
            if (track.Measures.Count >= total) continue;
            var last = track.Measures.LastOrDefault();
            while (track.Measures.Count < total)
                track.Measures.Add(new MeasureModel { Clef = last?.Clef ?? Clefs.Guitar });
            BarRangeEditor.Renumber(track);
        }
    }

    /// <summary>
    /// The bar settings a bars paste can carry: time signature, key, tempo (bar and mid-bar tempo
    /// changes), triplet feel, free time and pickup. Repeats, endings, directions and section names are form, not settings,
    /// and are never copied.
    /// </summary>
    public static void CopyBarSettings(MeasureModel from, MeasureModel to)
    {
        to.TimeSigNum = from.TimeSigNum; to.TimeSigDenom = from.TimeSigDenom;
        to.KeySignature = from.KeySignature; to.KeySignatureMinor = from.KeySignatureMinor;
        to.TempoChange = from.TempoChange; to.MidBarTempos = from.MidBarTempos?.ToList();
        to.TripletFeel = from.TripletFeel; to.TripletFeelKind = from.TripletFeelKind;
        to.FreeTime = from.FreeTime; to.Anacrusis = from.Anacrusis;
    }

    /// <summary>
    /// Overwrite: replaces both voices of bars [atBar, atBar + n) on each given track, appending bars on all tracks when the
    /// song is too short. With <paramref name="copySettings"/> the clip's bar settings are applied to that bar on every track
    /// (bars are global); otherwise the target keeps its own. Beats that do not fit the target bar are dropped and counted.
    /// </summary>
    public static PlacementResult OverwriteBars(SongProject p, int atBar, IReadOnlyList<TrackBars> tracks, bool copySettings)
    {
        var error = ValidateBars(p, atBar, tracks);
        if (error is not null) return PlacementResult.Refused(error);
        var count = tracks.Max(t => t.Bars.Count);
        var appended = Math.Max(0, atBar + count - BarRangeEditor.MaxMeasures(p));
        AppendBars(p, appended);
        PadTracks(p);
        if (copySettings)
            for (var i = 0; i < count; i++)
                if (SettingsSource(tracks, i) is { } source)
                    foreach (var track in p.Tracks) CopyBarSettings(source, track.Measures[atBar + i]);
        var dropped = 0;
        foreach (var clip in tracks)
            for (var i = 0; i < clip.Bars.Count; i++)
            {
                var target = p.Tracks[clip.TrackIndex].Measures[atBar + i];
                target.Cells = clip.Bars[i].Cells.Select(c => c.Clone()).ToList();
                target.Voice2Cells = clip.Bars[i].Voice2Cells.Select(c => c.Clone()).ToList();
                dropped += TrimToBar(p, target, atBar + i);
            }
        return new PlacementResult(true, null, atBar, atBar + count - 1, appended, 0, dropped);
    }

    /// <summary>
    /// Insert: puts the bars in before bar <paramref name="atBar"/> on ALL tracks (tracks without clip content get empty
    /// bars), so tracks stay aligned; markers and sections move as in the timeline area paste. "Insert after bar N" is
    /// <c>atBar = N + 1</c>. New bars take the clip's settings with <paramref name="copySettings"/>, otherwise the time
    /// signature, key and triplet feel in force at the insertion point. Returns the old-to-new bar map in
    /// <see cref="PlacementResult.BarMap"/>.
    /// </summary>
    public static PlacementResult InsertBars(SongProject p, int atBar, IReadOnlyList<TrackBars> tracks, bool copySettings)
    {
        var error = ValidateBars(p, 0, tracks);
        if (error is not null) return PlacementResult.Refused(error);
        PadTracks(p);
        var total = BarRangeEditor.MaxMeasures(p);
        atBar = Math.Clamp(atBar, 0, total);
        var count = tracks.Max(t => t.Bars.Count);
        if (total + count > InputLimits.MaxMeasuresPerTrack) return PlacementResult.Refused("The paste would make the song too long.");
        var clip = new List<List<MeasureModel>>();
        for (var t = 0; t < p.Tracks.Count; t++)
        {
            var track = p.Tracks[t];
            var neighbour = track.Measures.Count == 0 ? null : track.Measures[Math.Clamp(atBar - 1, 0, track.Measures.Count - 1)];
            var source = tracks.FirstOrDefault(x => x.TrackIndex == t);
            var bars = new List<MeasureModel>();
            for (var i = 0; i < count; i++)
            {
                var content = source is not null && i < source.Bars.Count ? source.Bars[i] : null;
                var bar = new MeasureModel
                {
                    Clef = neighbour?.Clef ?? Clefs.Guitar,
                    Cells = content?.Cells.Select(c => c.Clone()).ToList() ?? new MeasureModel().Cells,
                    Voice2Cells = content?.Voice2Cells.Select(c => c.Clone()).ToList() ?? new()
                };
                if (copySettings && SettingsSource(tracks, i) is { } settings) CopyBarSettings(settings, bar);
                else if (neighbour is not null)
                {
                    bar.TimeSigNum = neighbour.TimeSigNum; bar.TimeSigDenom = neighbour.TimeSigDenom;
                    bar.KeySignature = neighbour.KeySignature; bar.KeySignatureMinor = neighbour.KeySignatureMinor;
                    bar.TripletFeel = neighbour.TripletFeel; bar.TripletFeelKind = neighbour.TripletFeelKind;
                }
                if (content is null) bar.Cells = EmptyCells(p, bar.TimeSigNum, bar.TimeSigDenom);
                bars.Add(bar);
            }
            clip.Add(bars);
        }
        var map = BarInsert.Bars(p, atBar, clip, fillRests: false)!;   // the one shared bar insert: sections, ties and clips follow
        var dropped = 0;
        foreach (var track in p.Tracks)
            for (var i = 0; i < count; i++) dropped += TrimToBar(p, track.Measures[atBar + i], atBar + i);
        return new PlacementResult(true, null, atBar, atBar + count - 1, 0, 0, dropped, map);
    }

    private static string? ValidateBars(SongProject p, int atBar, IReadOnlyList<TrackBars> tracks)
    {
        if (tracks.Count == 0 || tracks.All(t => t.Bars.Count == 0)) return "Nothing to paste.";
        if (atBar < 0) return "Invalid paste position.";
        if (tracks.Any(t => t.TrackIndex < 0 || t.TrackIndex >= p.Tracks.Count)) return "No such track.";
        if (tracks.GroupBy(t => t.TrackIndex).Any(g => g.Count() > 1)) return "A track is pasted twice.";
        if (atBar + tracks.Max(t => t.Bars.Count) > InputLimits.MaxMeasuresPerTrack) return "The paste would make the song too long.";
        return null;
    }

    private static MeasureModel? SettingsSource(IReadOnlyList<TrackBars> tracks, int index) =>
        tracks.Select(t => index < t.Bars.Count ? t.Bars[index] : null).FirstOrDefault(b => b is not null);

    /// <summary>Clears beats that do not fit the bar's length (both voices); returns how many.</summary>
    private static int TrimToBar(SongProject p, MeasureModel measure, int bar)
    {
        var slots = SlotsOf(p, bar);
        var dropped = 0;
        foreach (var cells in new[] { measure.Cells, measure.Voice2Cells })
        {
            if (cells.Count > 0) while (cells.Count < slots) cells.Add(new TabCell());
            var onsets = Onsets(cells);
            for (var i = 0; i < cells.Count; i++)
                if (MusicTime.IsBeat(cells[i]) && onsets[i] + MusicTime.CellSlots(cells[i]) > slots + TickSlack)
                {
                    cells[i] = new TabCell();
                    dropped++;
                }
        }
        return dropped;
    }

    private static List<TabCell> EmptyCells(SongProject p, int? numerator, int? denominator) =>
        Enumerable.Range(0, MusicTime.BarSlots(numerator ?? p.TimeSignatureNumerator, denominator ?? p.TimeSignatureDenominator))
            .Select(_ => new TabCell()).ToList();


    private static bool IsTuplet(TabCell cell) => cell.Tuplet.Numerator > 0;

    private static double Snap(double value)
    {
        var rounded = Math.Round(value);
        return Math.Abs(value - rounded) < Eps ? rounded : value;
    }
}
