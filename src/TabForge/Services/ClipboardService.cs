using System.IO;
using System.Text;
using TabForge.Models;

namespace TabForge.Services;

/// <summary>What the system clipboard returned: <see cref="Available"/> false means it could not be opened (use the in-memory clip).</summary>
public readonly record struct ScoreClipboardRead(bool Available, string? Text, bool TooLarge = false);

/// <summary>The system clipboard as the score clipboard sees it (the Windows one in the app; an in-memory fake in self-tests).</summary>
public interface IScoreClipboard
{
    /// <summary>Changes whenever the clipboard content changes; 0 when unknown (then every read re-parses).</summary>
    uint SequenceNumber { get; }
    /// <summary>Puts the clip JSON on the clipboard (private format + text). False when the clipboard is unavailable. Never throws.</summary>
    bool TryWrite(string json);
    /// <summary>The private format first, then text, at most <paramref name="maxBytes"/>. Never throws.</summary>
    ScoreClipboardRead TryRead(int maxBytes);
}

/// <summary>
/// The one score clipboard (design 3.8): builds clips from a track range (<see cref="CaptureBars"/>, <see cref="CaptureBeats"/>,
/// <see cref="CaptureSelection"/>), writes them to the system clipboard when it can, and reads them back as untrusted input.
/// The last clip is always kept in memory, so copy/paste keeps working when the system clipboard is unavailable. No WPF here
/// (the Windows adapter is <see cref="Views.WindowsScoreClipboard"/>); UI thread only (the Windows clipboard needs STA).
/// </summary>
public sealed class ClipboardService
{
    public const string NotTabForgeNotesMessage = "The clipboard does not contain TabForge notes.";

    private static ClipboardService? _shared;
    /// <summary>The application's clipboard (Windows clipboard adapter), created on first use.</summary>
    public static ClipboardService Shared => _shared ??= new ClipboardService(new Views.WindowsScoreClipboard());

    private readonly IScoreClipboard? _system;
    private uint _cachedSequence;
    private bool _cacheValid;
    private ScoreClip? _cachedClip;
    private string? _cachedError;

    /// <param name="system">The system clipboard; null keeps clips in memory only.</param>
    public ClipboardService(IScoreClipboard? system) => _system = system;

    /// <summary>The last clip copied here or read successfully from the system clipboard (read-only; clone cells before use).</summary>
    public ScoreClip? Current { get; private set; }

    /// <summary>
    /// Makes <paramref name="clip"/> the current clip and puts it on the system clipboard (skipped when larger than
    /// <see cref="InputLimits.MaxClipboardBytes"/>: other windows could not read it). Returns true when the system clipboard was written.
    /// </summary>
    public bool Copy(ScoreClip clip)
    {
        ArgumentNullException.ThrowIfNull(clip);
        Current = clip;
        var written = false;
        if (_system is not null)
        {
            var json = clip.ToJson();
            if (Encoding.UTF8.GetByteCount(json) <= InputLimits.MaxClipboardBytes) written = _system.TryWrite(json);
            // Remember the clipboard state this clip belongs to: until the clipboard changes, reads return it without parsing.
            _cachedSequence = _system.SequenceNumber;
            _cacheValid = _cachedSequence != 0;
        }
        _cachedClip = clip;
        _cachedError = null;
        return written;
    }

    /// <summary>
    /// The clip to paste: from the system clipboard (validated; cached until the clipboard changes), or the in-memory
    /// <see cref="Current"/> when the system clipboard is unavailable. Null with a user-facing <paramref name="error"/> when the
    /// clipboard holds something else, something damaged or too large.
    /// </summary>
    public ScoreClip? TryGetClip(out string? error)
    {
        error = null;
        if (_system is null)
        {
            if (Current is null) error = NotTabForgeNotesMessage;
            return Current;
        }
        var sequence = _system.SequenceNumber;
        if (_cacheValid && sequence == _cachedSequence)
        {
            error = _cachedError;
            return _cachedClip;
        }
        var read = _system.TryRead(InputLimits.MaxClipboardBytes);
        if (!read.Available)
        {
            if (Current is null) error = NotTabForgeNotesMessage;
            return Current;
        }
        ScoreClip? clip = null;
        if (read.TooLarge) error = "The clipboard content is too large to paste.";
        else if (ScoreClip.TryParse(read.Text, out clip, out error)) Current = clip;
        _cachedSequence = sequence;
        _cacheValid = sequence != 0;
        _cachedClip = clip;
        _cachedError = error;
        return clip;
    }

    /// <summary>True when <see cref="TryGetClip"/> has a clip (cheap while the clipboard is unchanged: for menu states).</summary>
    public bool CanPaste => TryGetClip(out _) is not null;

    // ---- Capture ----

    /// <summary>
    /// Bars clip of bars <paramref name="firstBar"/>..<paramref name="lastBar"/> of the given tracks: both voices and the bar
    /// settings, lyrics removed. Throws <see cref="InvalidDataException"/> (user-facing) when the range is empty or over <see cref="InputLimits.MaxClipBars"/>.
    /// </summary>
    public static ScoreClip CaptureBars(SongProject project, IReadOnlyList<int> trackIndices, int firstBar, int lastBar, string sourceSongId = "")
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(trackIndices);
        if (lastBar < firstBar) (firstBar, lastBar) = (lastBar, firstBar);
        firstBar = Math.Max(0, firstBar);
        if (trackIndices.Count == 0) throw new InvalidDataException("Nothing to copy.");
        var clip = NewClip(project, ScoreClipKind.Bars, sourceSongId);
        clip.StartTempo = Math.Clamp(MusicTime.TempoAt(project, firstBar), InputLimits.MinTempo, InputLimits.MaxTempo);
        var count = -1;
        foreach (var index in trackIndices)
        {
            if (index < 0 || index >= project.Tracks.Count) throw new ArgumentOutOfRangeException(nameof(trackIndices));
            var track = project.Tracks[index];
            var last = Math.Min(lastBar, track.Measures.Count - 1);
            var bars = last - firstBar + 1;
            if (bars <= 0) throw new InvalidDataException("Nothing to copy.");
            if (bars > InputLimits.MaxClipBars) throw new InvalidDataException($"Copy at most {InputLimits.MaxClipBars} bars at once.");
            if (count >= 0 && bars != count) throw new InvalidDataException("The tracks have different bar counts.");
            count = bars;
            var clipTrack = ScoreClipTrack.FromTrack(track, index);
            for (var m = firstBar; m <= last; m++)
            {
                var bar = ProjectService.CloneMeasure(track.Measures[m]);
                foreach (var cell in bar.Cells.Concat(bar.Voice2Cells)) cell.Lyrics = "";
                clipTrack.Bars.Add(bar);
            }
            clip.Tracks.Add(clipTrack);
        }
        return clip;
    }

    /// <summary>
    /// Beats clip of one voice of one track: every beat whose grid cell lies in (<paramref name="startBar"/>, <paramref name="startCell"/>) ..
    /// (<paramref name="endBar"/>, <paramref name="endCell"/>) inclusive (<paramref name="endCell"/> &lt; 0 = to the bar end), across bar lines.
    /// Offsets are 16th slots from the first beat's onset (bar lines not stored); the length runs to the end of the last beat.
    /// A beat that only carried lyrics becomes a rest. Throws <see cref="InvalidDataException"/> (user-facing) when no beat is in range.
    /// </summary>
    public static ScoreClip CaptureBeats(SongProject project, int trackIndex, int voice,
        int startBar, int startCell, int endBar, int endCell, string sourceSongId = "")
    {
        ArgumentNullException.ThrowIfNull(project);
        if (trackIndex < 0 || trackIndex >= project.Tracks.Count) throw new ArgumentOutOfRangeException(nameof(trackIndex));
        if (voice is not (0 or 1)) throw new ArgumentOutOfRangeException(nameof(voice));
        if (endBar < startBar || (endBar == startBar && endCell >= 0 && endCell < startCell))
            (startBar, startCell, endBar, endCell) = (endBar, Math.Max(0, endCell), startBar, startCell);
        var track = project.Tracks[trackIndex];
        var clipTrack = ScoreClipTrack.FromTrack(track, trackIndex);
        clipTrack.Voice = voice;
        double? first = null;
        var end = 0.0;
        var barStart = 0.0;
        for (var m = Math.Max(0, startBar); m <= endBar && m < track.Measures.Count; m++)
        {
            var measure = track.Measures[m];
            var cells = voice == 0 ? measure.Cells : measure.Voice2Cells;
            foreach (var (index, source, start) in BeatOnsets(cells))
            {
                var inRange = (m > startBar || index >= startCell) && (m < endBar || endCell < 0 || index <= endCell);
                if (!inRange) continue;
                var at = barStart + start;
                first ??= at;
                var cell = source.Clone();
                cell.RhythmicPosition = null;
                cell.Lyrics = "";
                if (cell.Notes.Count == 0 && !cell.HasAnnotation) cell.IsRest = true;
                clipTrack.Events.Add(new ScoreClipEvent { OffsetSlots = at, Cell = cell });
                if (clipTrack.Events.Count > InputLimits.MaxClipEvents)
                    throw new InvalidDataException($"Copy at most {InputLimits.MaxClipEvents} beats at once.");
                end = Math.Max(end, at + MusicTime.CellSlots(cell));
            }
            barStart += MusicTime.BarSlots(project, m);
        }
        if (first is not { } origin) throw new InvalidDataException("Nothing to copy.");
        foreach (var entry in clipTrack.Events) entry.OffsetSlots -= origin;
        // Imported tuplets can list a beat before the previous cell index; the clip is in onset order.
        clipTrack.Events = clipTrack.Events.OrderBy(e => e.OffsetSlots).ToList();
        clipTrack.LengthSlots = end - origin;
        var clip = NewClip(project, ScoreClipKind.Beats, sourceSongId);
        clip.StartTempo = Math.Clamp(MusicTime.TempoAt(project, Math.Max(0, startBar)), InputLimits.MinTempo, InputLimits.MaxTempo);
        clip.Tracks.Add(clipTrack);
        return clip;
    }

    /// <summary>
    /// Score selection -> clip (design 3.1): whole bars (start cell 0 and the end at the bar end, or <paramref name="endCell"/> &lt; 0)
    /// give a Bars clip of the track (both voices); anything else a Beats clip of <paramref name="voice"/>.
    /// For a lone cursor beat call <see cref="CaptureBeats"/> with the same start and end.
    /// </summary>
    public static ScoreClip CaptureSelection(SongProject project, int trackIndex, int voice,
        int startBar, int startCell, int endBar, int endCell, string sourceSongId = "")
    {
        ArgumentNullException.ThrowIfNull(project);
        if (endBar < startBar || (endBar == startBar && endCell >= 0 && endCell < startCell))
            (startBar, startCell, endBar, endCell) = (endBar, endCell, startBar, startCell);
        var wholeBars = startCell <= 0 && (endCell < 0 || endCell >= MusicTime.BarSlots(project, endBar) - 1);
        return wholeBars
            ? CaptureBars(project, new[] { trackIndex }, startBar, endBar, sourceSongId)
            : CaptureBeats(project, trackIndex, voice, startBar, startCell, endBar, endCell, sourceSongId);
    }

    private static ScoreClip NewClip(SongProject project, ScoreClipKind kind, string sourceSongId) => new()
    {
        Format = ScoreClip.FormatName,
        Version = ScoreClip.CurrentVersion,
        Kind = kind,
        SourceSongId = sourceSongId ?? "",
        SourceTrackCount = project.Tracks.Count,
        SongTimeSignatureNumerator = project.TimeSignatureNumerator,
        SongTimeSignatureDenominator = project.TimeSignatureDenominator,
        SongKeySignature = project.KeySignature,
        SongKeySignatureMinor = project.KeySignatureMinor
    };

    /// <summary>
    /// Beats of a voice with their onsets in 16th slots from the bar start: the MusicTime rule
    /// (<c>RhythmicPosition ?? max(index, cursor)</c>, cursor advanced by <see cref="MusicTime.ConsumeSlots"/>).
    /// Kept here until BarGrid (design C2) owns the one onset walk.
    /// </summary>
    internal static IEnumerable<(int Index, TabCell Cell, double Start)> BeatOnsets(IReadOnlyList<TabCell> cells)
    {
        var consumed = 0.0;
        for (var i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            if (cell.Notes.Count == 0 && !cell.IsRest && !cell.HasAnnotation)
            {
                if (consumed <= i) consumed = i + 1;
                continue;
            }
            var start = cell.RhythmicPosition is { } exact && double.IsFinite(exact) ? Math.Max(0, exact) : Math.Max(i, consumed);
            yield return (i, cell, start);
            consumed = Math.Max(consumed, start + MusicTime.ConsumeSlots(cell));
        }
    }
}
