using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TabForge.Models;

namespace TabForge.Services;

// Owns: the typed, self-contained copy of a timeline bar range (bars first..last of every track): per-track kind and tuning, the tab bars, the clip parts cut at the
//   range edges, the time signature / key / tempo of each bar; its private-clipboard JSON (write, validated read) and the plain ASCII tab for the text slot.
// Does not own: taking a snapshot from a song (RangeCapture), mapping it onto tracks (PasteMapper), the clipboard itself (ClipboardService) or the ScoreClip format (ScoreClip, untouched).
// Tests: RangeClipboardTests.
/// <summary>
/// One copied bar range. Treat it as read-only once captured. Clip parts are in song seconds relative to the range start (<see cref="RangeClipPart.OffsetSec"/>), first pass,
/// like every clip position.
/// </summary>
public sealed class RangeSnapshot
{
    public const string FormatName = "TabForge.RangeSnapshot";
    public const int CurrentVersion = 1;
    /// <summary>Private Windows clipboard format name (UTF-8 JSON); the text slot of the same copy holds the ASCII tab.</summary>
    public const string ClipboardFormat = "TabForge.RangeSnapshot.v1";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        MaxDepth = InputLimits.MaxJsonDepth,
    };

    public string Format { get; set; } = FormatName;
    public int Version { get; set; } = CurrentVersion;
    /// <summary>Index of the first copied bar in the source song (informational).</summary>
    public int FirstBar { get; set; }
    /// <summary>The length of the range in song seconds (first pass); 0 when the bars are not on the timeline.</summary>
    public double LengthSec { get; set; }
    /// <summary>One entry per copied bar: its effective time signature, key and tempo.</summary>
    public List<RangeSnapshotBar> Bars { get; set; } = new();
    /// <summary>Every source track in order, tab and audio.</summary>
    public List<RangeSnapshotTrack> Tracks { get; set; } = new();

    [JsonIgnore] public int BarCount => Bars.Count;

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Reads untrusted clipboard text; false with a user-facing <paramref name="error"/> when it is not a valid range snapshot.</summary>
    public static bool TryParse(string? text, out RangeSnapshot? snapshot, out string? error)
    {
        snapshot = null;
        error = ClipboardService.NotTabForgeNotesMessage;
        if (string.IsNullOrWhiteSpace(text) || Encoding.UTF8.GetByteCount(text) > InputLimits.MaxClipboardBytes) return false;
        try
        {
            var read = JsonSerializer.Deserialize<RangeSnapshot>(text, JsonOptions);
            if (read is null || !read.IsValid()) return false;
            snapshot = read;
            error = null;
            return true;
        }
        catch (JsonException) { return false; }
    }

    private bool IsValid() =>
        Format == FormatName && Version == CurrentVersion && Bars.Count is > 0 and <= InputLimits.MaxClipBars && double.IsFinite(LengthSec) && LengthSec >= 0
        && Tracks.All(t => t.Bars.Count == (t.Kind == TrackKind.Audio ? 0 : Bars.Count) && t.Clips.All(c => c.IsValid()));

    /// <summary>
    /// The copied bars of one tab track as a Bars score clip, for a paste into the score: the track <paramref name="preferredTrackId"/> was copied from, else the first tab track.
    /// Each bar carries the time signature and key it had in the range. Null when the range holds no tab track.
    /// </summary>
    public ScoreClip? ToScoreClip(Guid preferredTrackId)
    {
        var source = Tracks.FirstOrDefault(t => t.Kind != TrackKind.Audio && t.SourceTrackId == preferredTrackId) ?? Tracks.FirstOrDefault(t => t.Kind != TrackKind.Audio);
        if (source is null || Bars.Count == 0) return null;
        var track = new ScoreClipTrack
        {
            SourceTrackId = source.SourceTrackId, SourceTrackIndex = Tracks.IndexOf(source), Name = source.Name, InstrumentName = source.InstrumentName,
            TrackKind = source.Kind, IsDrums = source.Kind == TrackKind.Drums, StringTunings = source.StringTunings.ToList(), Capo = source.Capo,
        };
        for (var i = 0; i < source.Bars.Count; i++)
        {
            var bar = ProjectService.CloneMeasure(source.Bars[i]);
            (bar.TimeSigNum, bar.TimeSigDenom, bar.KeySignature, bar.KeySignatureMinor) = (Bars[i].Numerator, Bars[i].Denominator, Bars[i].Key, Bars[i].KeyMinor);
            track.Bars.Add(bar);
        }
        return new ScoreClip
        {
            Format = ScoreClip.FormatName, Version = ScoreClip.CurrentVersion, Kind = ScoreClipKind.Bars, SourceTrackCount = Tracks.Count,
            SongTimeSignatureNumerator = Bars[0].Numerator, SongTimeSignatureDenominator = Bars[0].Denominator, SongKeySignature = Bars[0].Key, SongKeySignatureMinor = Bars[0].KeyMinor,
            StartTempo = Math.Clamp(Bars[0].Tempo, InputLimits.MinTempo, InputLimits.MaxTempo), Tracks = { track },
        };
    }

    /// <summary>The tab bars as plain ASCII tablature (the text slot of the clipboard): one block per tab track, one line per string, a column per beat.</summary>
    public string ToAsciiTab()
    {
        var text = new StringBuilder();
        text.AppendLine($"Bars {FirstBar + 1}-{FirstBar + BarCount}");
        foreach (var track in Tracks.Where(t => t.Kind != TrackKind.Audio))
        {
            text.AppendLine().AppendLine($"[{track.Name}]");
            for (var s = 0; s < Math.Max(1, track.StringTunings.Count); s++)
            {
                text.Append('|');
                foreach (var bar in track.Bars)
                {
                    foreach (var cell in bar.Cells)
                    {
                        var note = cell.IsRest ? null : cell.Notes.FirstOrDefault(n => n.StringIndex == s);
                        text.Append((note is null ? "" : note.Fret.ToString()).PadRight(2, '-')).Append('-');
                    }
                    text.Append('|');
                }
                text.AppendLine();
            }
        }
        return text.ToString();
    }
}

/// <summary>The effective bar settings of one copied bar.</summary>
public sealed record RangeSnapshotBar(int Numerator, int Denominator, int Key, bool KeyMinor, int Tempo);

/// <summary>One source track: what it was (kind, instrument, tuning), its bars (both voices, ties, bar settings) and its clip parts.</summary>
public sealed class RangeSnapshotTrack
{
    public Guid SourceTrackId { get; set; }
    public string Name { get; set; } = "";
    public TrackKind Kind { get; set; }
    public string InstrumentName { get; set; } = "";
    /// <summary>Open-string pitches, high to low; the count is the string count.</summary>
    public List<int> StringTunings { get; set; } = new();
    public int Capo { get; set; }
    /// <summary>One bar per copied bar; empty for an audio track.</summary>
    public List<MeasureModel> Bars { get; set; } = new();
    /// <summary>The clips that touch the range, cut at its edges.</summary>
    public List<RangeClipPart> Clips { get; set; } = new();
}

/// <summary>A clip cut to the part inside the range: <see cref="Clip"/> keeps its file, trim, gain, fades (kept only on an uncut edge) and notes; it starts <see cref="OffsetSec"/> after the range start.</summary>
public sealed class RangeClipPart
{
    public double OffsetSec { get; set; }
    public AudioClip Clip { get; set; } = new();

    internal bool IsValid() => double.IsFinite(OffsetSec) && OffsetSec >= 0 && double.IsFinite(Clip.SourceLengthSec) && Clip.SourceLengthSec > 0
        && double.IsFinite(Clip.OffsetSec) && Clip.OffsetSec >= 0 && double.IsFinite(Clip.Speed) && Clip.Speed > 0;
}
