using TabForge.Models;
using TabForge.Presenters;

namespace TabForge.Services;

/// <summary>What the readout reads: the bars of the section (or the whole song) and the tracks it listens to.</summary>
/// <param name="SectionName">The section title; "" for the whole song.</param>
/// <param name="FirstBar">The first bar read (zero based).</param>
/// <param name="LastBar">The last bar read (zero based, inclusive).</param>
/// <param name="Marker">The section read; null for the whole song.</param>
/// <param name="TrackKey">The track id ("N" format) of the listened track, or "*" for all pitched tracks.</param>
/// <param name="TrackLabel">The track name, or "All pitched tracks".</param>
/// <param name="Tracks">The tracks read.</param>
public sealed record ScaleContext(string SectionName, int FirstBar, int LastBar, MarkerModel? Marker, string TrackKey, string TrackLabel, IReadOnlyList<TrackModel> Tracks)
{
    /// <summary>"Verse 1 · Lead Gtr · bars 19–32".</summary>
    public string Text => $"{(Marker is null ? "Whole song" : SectionName)}  ·  {TrackLabel}  ·  bars {FirstBar + 1}–{LastBar + 1}";

    /// <summary>The key a pick is remembered under for this session: the section and the track.</summary>
    public string SessionKey => $"{(Marker is null ? "song" : $"{Marker.MeasureIndex}:{Marker.Title}")}|{TrackKey}";
}

/// <summary>The readout for one position.</summary>
/// <param name="Text">The status bar text: "A minor ▾ auto", or "A minor ▾" when the scale is picked or comes from the key signature.</param>
/// <param name="Context">What was read.</param>
/// <param name="Effective">The scale shown (the pick, the key signature scale, or the best detected one).</param>
/// <param name="Top">The best detected scales, best first (at most <see cref="ScaleReadoutEngine.TopCount"/>); empty when the section has no pitched notes.</param>
/// <param name="IsAuto">True when <paramref name="Effective"/> was detected, not picked or read from the key signature.</param>
/// <param name="Pick">The user pick (a "Root Scale" highlight), or null.</param>
/// <param name="PickIsKept">True when the pick is saved in the song.</param>
public sealed record ScaleReadoutResult(string Text, ScaleContext Context, ScaleMatch Effective, IReadOnlyList<ScaleMatch> Top, bool IsAuto, string? Pick, bool PickIsKept);

// Owns: which bars and tracks the readout reads (the section at a bar, the whole song, the selected or all pitched tracks), the detected candidates cached per
//   song revision, section, track and settings, the user picks (kept in the song, or for this session), and the readout text.
// Does not own: the histogram and scoring (SectionScaleDetector), the settings (ScaleReadoutSettings), the popup and the status bar (Avalonia), undo of a kept pick (the caller runs it in an edit).
// Tests: TabForge.Avalonia.Tests.SectionScaleDetectorTests.
// Threads: UI thread only (it reads the song the UI edits). Detection is cheap (one pass over the section notes) and runs only when the section, track, settings or song content changed.
public sealed class ScaleReadoutEngine
{
    /// <summary>How many detected scales the popup lists.</summary>
    public const int TopCount = 3;

    private const int CacheLimit = 64;
    private readonly Dictionary<string, IReadOnlyList<ScaleMatch>> _cache = new();
    private readonly Dictionary<string, string> _session = new();

    /// <summary>How many detections ran (a test reads it to see that nothing is recomputed without a change).</summary>
    public int DetectionRuns { get; private set; }

    /// <summary>The bars and tracks read at <paramref name="bar"/> for <paramref name="track"/> (the selected track) under <paramref name="settings"/>.</summary>
    public static ScaleContext ContextOf(SongProject song, int bar, TrackModel? track, ScaleReadoutSettings settings)
    {
        var bars = Math.Max(1, BarRangeEditor.MaxMeasures(song));
        var section = ScaleReadFrom.Normalize(settings.ReadFrom) == ScaleReadFrom.Song ? null : SectionLayout.At(song, bar);
        var (first, last) = (0, bars - 1);
        if (section is not null && SectionLayout.TryGetBounds(song, section, out var start, out var end)) (first, last) = (start, Math.Max(start, end - 1));
        else section = null;
        track ??= song.FirstNotationTrack;
        if (ScaleListenTo.Normalize(settings.ListenTo) == ScaleListenTo.Pitched)
            return new ScaleContext(section?.Title ?? "", first, last, section, "*", "All pitched tracks", song.Tracks.Where(t => !t.IsDrumTrack).ToList());
        return new ScaleContext(section?.Title ?? "", first, last, section, track is null ? "-" : track.Id.ToString("N"), track?.Name ?? "No track",
            track is null ? Array.Empty<TrackModel>() : new[] { track });
    }

    /// <summary>The scale of the key signature at <paramref name="bar"/>: the major key, or its relative natural minor.</summary>
    public static ScaleMatch KeyScale(SongProject song, int bar)
    {
        var measure = MusicTime.BarOf(song, bar);
        var signature = Math.Clamp(measure?.KeySignature ?? song.KeySignature, -7, 7);
        var minor = measure?.KeySignatureMinor ?? song.KeySignatureMinor;
        var major = (((signature * 7) % 12) + 12) % 12;
        return minor ? new ScaleMatch((major + 9) % 12, "Natural Minor", 1, 0) : new ScaleMatch(major, "Major", 1, 0);
    }

    /// <summary>Reads a "Root Scale" highlight; null when the text is not one.</summary>
    public static ScaleMatch? Parse(string? highlight)
    {
        var (root, scale) = ScaleFinderPresenter.Split(highlight);
        if (root is null || scale is null) return null;
        var index = Array.FindIndex(MusicTheoryService.NoteNames, n => n.Equals(root, StringComparison.OrdinalIgnoreCase));
        var canonical = MusicTheoryService.Scales.Keys.FirstOrDefault(k => k.Equals(scale, StringComparison.OrdinalIgnoreCase));
        return index < 0 || canonical is null ? null : new ScaleMatch(index, canonical, 1, 0);
    }

    /// <summary>The readout at <paramref name="bar"/> (the playhead or the cursor) for the selected <paramref name="track"/>.</summary>
    public ScaleReadoutResult Compute(SongProject song, int bar, TrackModel? track, ScaleReadoutSettings settings)
    {
        var context = ContextOf(song, bar, track, settings);
        var top = Detect(song, context, settings);
        var key = KeyScale(song, bar);
        var flats = (MusicTime.BarOf(song, bar)?.KeySignature ?? song.KeySignature) < 0;
        var pick = PickOf(song, context, out var kept);
        var picked = Parse(pick);
        ScaleMatch effective;
        var auto = false;
        if (picked is not null) effective = picked;
        else if (ScaleReadFrom.Normalize(settings.ReadFrom) == ScaleReadFrom.Key || top.Count == 0) effective = key;
        else { effective = top[0]; auto = true; }
        return new ScaleReadoutResult($"{effective.Display(flats, settings.MusicalKeyNames)} ▾{(auto ? " auto" : "")}", context, effective, top, auto, picked is null ? null : pick, picked is not null && kept);
    }

    private IReadOnlyList<ScaleMatch> Detect(SongProject song, ScaleContext context, ScaleReadoutSettings settings)
    {
        var key = $"{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(song)}|{song.ContentRevision}|{context.FirstBar}|{context.LastBar}|{context.TrackKey}|{settings.Signature()}";
        if (_cache.TryGetValue(key, out var cached)) return cached;
        DetectionRuns++;
        var histogram = SectionScaleDetector.Collect(song, context.Tracks, context.FirstBar, context.LastBar);
        var scales = ScaleFamilies.ScalesOf(ScaleFamilies.Normalize(settings.Families));
        var top = SectionScaleDetector.Rank(histogram, scales, settings.PreferCommon).Take(TopCount).ToList();
        if (_cache.Count >= CacheLimit) _cache.Clear();
        _cache[key] = top;
        return top;
    }

    /// <summary>The pick for the context: this session's, else the one kept in the song; null for none. <paramref name="kept"/> tells which.</summary>
    public string? PickOf(SongProject song, ScaleContext context, out bool kept)
    {
        kept = false;
        if (_session.TryGetValue(context.SessionKey, out var session)) return session;
        var store = context.Marker is null ? song.KeptScales : context.Marker.KeptScales;
        if (store is not null && store.TryGetValue(context.TrackKey, out var saved)) { kept = true; return saved; }
        return null;
    }

    /// <summary>Remembers a pick for this session only (nothing is written to the song).</summary>
    public void PickForSession(ScaleContext context, string highlight) => _session[context.SessionKey] = highlight;

    /// <summary>Forgets this session's pick of the context (nothing is written to the song).</summary>
    public void ForgetSessionPick(ScaleContext context) => _session.Remove(context.SessionKey);

    /// <summary>Saves a pick in the song for the context (call it inside an edit); false when it already held it.</summary>
    public bool KeepPick(SongProject song, ScaleContext context, string highlight)
    {
        _session.Remove(context.SessionKey);
        var store = context.Marker is null ? (song.KeptScales ??= new()) : (context.Marker.KeptScales ??= new());
        if (store.TryGetValue(context.TrackKey, out var existing) && existing == highlight) return false;
        store[context.TrackKey] = highlight;
        return true;
    }

    /// <summary>Drops the pick of the context, session and kept (call it inside an edit); true when the song changed.</summary>
    public bool ReturnToAuto(SongProject song, ScaleContext context)
    {
        _session.Remove(context.SessionKey);
        var store = context.Marker is null ? song.KeptScales : context.Marker.KeptScales;
        if (store is null || !store.Remove(context.TrackKey)) return false;
        if (store.Count == 0) { if (context.Marker is null) song.KeptScales = null; else context.Marker.KeptScales = null; }
        return true;
    }
}
