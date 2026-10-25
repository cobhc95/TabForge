using TabForge.Models;
using TabForge.Services;

namespace TabForge.Presenters;

/// <summary>
/// The logic of the Scale finder window: the search scope labels, which tracks are analysed, the status text and the
/// likely-scale rows for a search, the chosen highlight, and the split of a saved "Root Scale" highlight.
/// Owns: the scope rules, the status and row texts, and the "Root Scale" highlight format.
/// Does not own: the window's controls and dialog result (ScaleFinderWindow), the note analysis (ScaleDetector) or the scale tables (MusicTheoryService).
/// Tests: no dedicated test class.
/// </summary>
public static class ScaleFinderPresenter
{
    /// <summary>Highlight value that clears the fretboard highlight.</summary>
    public const string OffHighlight = "Off";

    /// <summary>Root shown when the window opens without a saved highlight.</summary>
    public const string DefaultRoot = "C";

    /// <summary>Most likely scales listed on the Likely scales tab.</summary>
    public const int MaxLikelyRows = 14;

    /// <summary>Passage to analyse when the user picks "Selection": bars and cells inclusive.</summary>
    public sealed record SearchRange(int StartMeasure, int StartCell, int EndMeasure, int EndCell, string Label);

    /// <summary>One likely scale: its "Root Scale" highlight and its detail line.</summary>
    public sealed record SearchRow(string Highlight, string Detail);

    /// <summary>What the window shows for one search. NoPitchedNotes sends the user to the All scales tab.</summary>
    public sealed record SearchResult(string Status, bool NoPitchedNotes, IReadOnlyList<SearchRow> Rows);

    public static IReadOnlyList<string> RootChoices => MusicTheoryService.NoteNames;

    public static IReadOnlyList<string> ScaleChoices => MusicTheoryService.Scales.Keys.ToList();

    public static string RootOrDefault(string? currentRoot) => currentRoot ?? DefaultRoot;

    public static string SelectionLabel(string? selectionLabel) =>
        selectionLabel is null ? "Selection (nothing selected)" : $"Selection ({selectionLabel})";

    public static string AllTracksLabel(string? trackName) =>
        trackName is null ? "All tracks" : $"All tracks (not only {trackName})";

    /// <summary>A drum track (or one on MIDI channel 10, index 9) has no pitches to analyse, so "All tracks" starts checked for it.</summary>
    public static bool AllTracksStartsChecked(TrackModel? track) =>
        track is null || track.IsDrumTrack;

    /// <summary>Every track when "All tracks" is checked or no track is selected, otherwise only the selected track.</summary>
    public static IEnumerable<TrackModel> TracksToAnalyse(SongProject project, TrackModel? track, bool allTracksChecked) =>
        allTracksChecked || track is null ? project.Tracks : new List<TrackModel> { track };

    /// <summary>Analyses the chosen passage (or the whole song) and ranks the scales it fits.</summary>
    public static SearchResult Search(SongProject project, TrackModel? track, bool allTracksChecked, bool useSelection, SearchRange? selection)
    {
        var tracks = TracksToAnalyse(project, track, allTracksChecked);
        var summary = useSelection && selection is not null
            ? ScaleDetector.Collect(tracks, selection.StartMeasure, selection.StartCell, selection.EndMeasure, selection.EndCell)
            : ScaleDetector.Collect(tracks, 0, 0, int.MaxValue - 1, int.MaxValue - 1);
        if (summary.NoteCount == 0)
            return new SearchResult("No pitched notes to analyse here. Pick a scale on the All scales tab instead.", true, Array.Empty<SearchRow>());

        var used = Enumerable.Range(0, 12).Where(pc => summary.Weights[pc] > 0).Select(pc => MusicTheoryService.NoteNames[pc]);
        var status = $"{summary.NoteCount} notes analysed · notes used: {string.Join(" ", used)}. Usually several scales fit; pick one.";
        var rows = ScaleDetector.Rank(summary).Take(MaxLikelyRows).Select(c => new SearchRow(c.Highlight, DetailText(c))).ToList();
        return new SearchResult(status, false, rows);
    }

    private static string DetailText(ScaleCandidate candidate) =>
        $"{candidate.Coverage:P0} of the notes fit" +
        (candidate.UnusedScaleNotes > 0 ? $" · {candidate.UnusedScaleNotes} scale note(s) not played" : " · every scale note is played") +
        $"   ({candidate.NoteList})";

    /// <summary>
    /// The highlight the window returns: the likely row's highlight while that tab is open, otherwise the browse root and
    /// scale as "Root Scale"; null when nothing is picked.
    /// </summary>
    public static string? Chosen(bool likelyTabOpen, string? likelyHighlight, string? browseRoot, string? browseScale) =>
        likelyTabOpen ? likelyHighlight : browseRoot is not null && browseScale is not null ? $"{browseRoot} {browseScale}" : null;

    /// <summary>Splits a saved "Root Scale" highlight; "Off", blank text or text without a space gives neither part.</summary>
    public static (string? Root, string? Scale) Split(string? highlight)
    {
        if (string.IsNullOrWhiteSpace(highlight) || highlight == OffHighlight) return (null, null);
        var space = highlight.IndexOf(' ');
        return space <= 0 ? (null, null) : (highlight[..space], highlight[(space + 1)..]);
    }
}
