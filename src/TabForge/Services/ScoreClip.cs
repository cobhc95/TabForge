using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using TabForge.Models;

namespace TabForge.Services;

/// <summary>What a clip holds (docs/COPY_PASTE_DESIGN.md 3.1), decided at copy time.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ScoreClipKind>))]
public enum ScoreClipKind
{
    /// <summary>Whole bars: both voices and the bar settings, one entry per bar in <see cref="ScoreClipTrack.Bars"/>.</summary>
    Bars,
    /// <summary>A run of beats from one voice of one track, bar lines not stored: <see cref="ScoreClipTrack.Events"/>.</summary>
    Beats
}

// Owns: the versioned score clipboard format and its untrusted-input parsing.
// Does not own: the clipboard access (ClipboardService) and pasting.
// Tests: TestScoreClipJson, TestScoreClipRejectsUntrustedInput.
/// <summary>
/// The score clipboard format (design 3.8), versioned and JSON-serialisable. A clip read from the clipboard is untrusted: only
/// <see cref="Parse(string)"/> / <see cref="TryParse"/> create one from text, and they validate it fully. Lyrics are never carried.
/// Treat a clip returned by <see cref="ClipboardService"/> as read-only and clone its cells (<see cref="TabCell.Clone"/>) when placing them.
/// </summary>
public sealed class ScoreClip
{
    public const string FormatName = "TabForge.ScoreClip";
    public const int CurrentVersion = 1;
    /// <summary>Private Windows clipboard format name (UTF-8 JSON); the same JSON is also put on the clipboard as text.</summary>
    public const string ClipboardFormat = "TabForge.ScoreClip.v1";

    // Header initializers are deliberately "missing" values: a clip without them is rejected, never read as version 1.
    public string Format { get; set; } = "";
    public int Version { get; set; }
    public ScoreClipKind Kind { get; set; }
    /// <summary>Identifies the source document (caller-defined, may be empty): paste maps track i to track i only within the same song.</summary>
    public string SourceSongId { get; set; } = "";
    /// <summary>Track count of the source song when copied.</summary>
    public int SourceTrackCount { get; set; }
    /// <summary>Source song defaults that bars with null settings inherit (MusicTime rule: a null bar time signature is the song's).</summary>
    public int SongTimeSignatureNumerator { get; set; } = 4;
    public int SongTimeSignatureDenominator { get; set; } = 4;
    public int SongKeySignature { get; set; }
    public bool SongKeySignatureMinor { get; set; }
    /// <summary>Tempo in effect at the first copied bar (Bars clips; informational for Beats clips).</summary>
    public int StartTempo { get; set; } = 120;
    /// <summary>One entry per copied track, in the source track order. Beats clips hold exactly one.</summary>
    public List<ScoreClipTrack> Tracks { get; set; } = new();

    /// <summary>True when this clip was converted from the pre-beta.6 clipboard text (a raw project snapshot, no instrument info).</summary>
    [JsonIgnore] public bool IsLegacy { get; private set; }

    /// <summary>Number of bars in a Bars clip (every track holds the same count); 0 for a Beats clip.</summary>
    [JsonIgnore] public int BarCount => Kind == ScoreClipKind.Bars && Tracks.Count > 0 ? Tracks[0].Bars.Count : 0;

    /// <summary>Effective time signature of bar <paramref name="barIndex"/> of a Bars clip (the bar's own, else the source song's).</summary>
    public (int Numerator, int Denominator) TimeSignatureOf(int barIndex)
    {
        var bar = Kind == ScoreClipKind.Bars && Tracks.Count > 0 && barIndex >= 0 && barIndex < Tracks[0].Bars.Count ? Tracks[0].Bars[barIndex] : null;
        return (bar?.TimeSigNum ?? SongTimeSignatureNumerator, bar?.TimeSigDenom ?? SongTimeSignatureDenominator);
    }

    /// <summary>Length in 16th slots of bar <paramref name="barIndex"/> of a Bars clip (<see cref="MusicTime.BarSlots(int,int)"/>).</summary>
    public int BarSlots(int barIndex)
    {
        var (numerator, denominator) = TimeSignatureOf(barIndex);
        return MusicTime.BarSlots(numerator, denominator);
    }

    // ---- JSON ----

    /// <summary>
    /// camelCase JSON. Model bodies (bars, beats, notes) use the lossless compaction of the in-memory snapshots (only values that
    /// differ from a new object's initializers); the clip and track headers are always written in full.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        MaxDepth = InputLimits.MaxJsonDepth,
        TypeInfoResolver = ProjectService.LosslessCompactResolver().WithAddedModifier(WriteHeadersInFull)
    };

    private static void WriteHeadersInFull(JsonTypeInfo info)
    {
        if (info.Type != typeof(ScoreClip) && info.Type != typeof(ScoreClipTrack) && info.Type != typeof(ScoreClipEvent)) return;
        foreach (var property in info.Properties) property.ShouldSerialize = null;
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public byte[] ToUtf8Json() => JsonSerializer.SerializeToUtf8Bytes(this, JsonOptions);

    /// <summary>Reads and validates clipboard text: a <see cref="FormatName"/> clip, or the legacy project-snapshot text. Throws <see cref="InvalidDataException"/>.</summary>
    public static ScoreClip Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > InputLimits.MaxClipboardBytes || Encoding.UTF8.GetByteCount(text) > InputLimits.MaxClipboardBytes)
            throw Invalid("The clipboard content is too large to paste.");
        return Parse(Encoding.UTF8.GetBytes(text));
    }

    /// <summary>UTF-8 form of <see cref="Parse(string)"/>.</summary>
    public static ScoreClip Parse(ReadOnlySpan<byte> json)
    {
        if (json.Length > InputLimits.MaxClipboardBytes) throw Invalid("The clipboard content is too large to paste.");
        var start = 0;
        while (start < json.Length && json[start] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') start++;
        if (start == json.Length || json[start] != (byte)'{') throw Invalid(ClipboardService.NotTabForgeNotesMessage);
        try
        {
            ProjectValidator.ValidateJsonShape(json);
            if (!ProjectService.HasRootProperty(json, nameof(Format)))
            {
                if (!ProjectService.HasRootProperty(json, nameof(SongProject.Tracks))) throw Invalid(ClipboardService.NotTabForgeNotesMessage);
                return FromLegacy(ProjectService.Restore(Encoding.UTF8.GetString(json)));
            }
            var clip = JsonSerializer.Deserialize<ScoreClip>(json, JsonOptions) ?? throw Invalid(ClipboardService.NotTabForgeNotesMessage);
            Validate(clip);
            return clip;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(ClipboardService.NotTabForgeNotesMessage, ex);
        }
        // One message for every damaged or foreign clip (design 3.8); the version message stays, it tells the user what to do.
        catch (InvalidDataException ex) when (!ex.Data.Contains(VersionProblem) && ex.Message != ClipboardService.NotTabForgeNotesMessage)
        {
            throw new InvalidDataException(ClipboardService.NotTabForgeNotesMessage, ex);
        }
    }

    private const string VersionProblem = "TabForge.ScoreClip.Version";

    private static InvalidDataException VersionInvalid(string message)
    {
        var ex = new InvalidDataException(message);
        ex.Data[VersionProblem] = true;
        return ex;
    }

    /// <summary>Non-throwing <see cref="Parse(string)"/>: <paramref name="error"/> is a user-facing reason when it returns false.</summary>
    public static bool TryParse(string? text, out ScoreClip? clip, out string? error)
    {
        clip = null;
        error = null;
        if (string.IsNullOrWhiteSpace(text)) { error = ClipboardService.NotTabForgeNotesMessage; return false; }
        try { clip = Parse(text); return true; }
        catch (InvalidDataException ex) { error = ex.Message; return false; }
    }

    // ---- Validation (untrusted input) ----

    /// <summary>
    /// Checks a deserialised clip against the format, version and count limits and every bar/beat/note against the project rules
    /// (<see cref="ProjectValidator"/>). Normalises harmless details in place: lyrics are removed, Beats-clip cells lose any
    /// <see cref="TabCell.RhythmicPosition"/> (the event offset is the onset). Throws <see cref="InvalidDataException"/>.
    /// </summary>
    public static void Validate(ScoreClip clip)
    {
        if (clip is null || !string.Equals(clip.Format, FormatName, StringComparison.Ordinal))
            throw Invalid(ClipboardService.NotTabForgeNotesMessage);
        if (clip.Version > CurrentVersion) throw VersionInvalid("The clipboard holds notes from a newer TabForge version; update TabForge to paste them.");
        if (clip.Version < 1) throw VersionInvalid("The clipboard notes use an unknown format version.");
        if (!Enum.IsDefined(clip.Kind)) throw Invalid("The clipboard notes are of an unknown kind.");
        if (!InputLimits.IsSafeText(clip.SourceSongId, 128, allowLineBreaks: false)) throw Invalid("The clipboard notes have an invalid source.");
        if (clip.SourceTrackCount is < 0 or > InputLimits.MaxTracks) throw Invalid("The clipboard notes have an invalid track count.");
        if (clip.SongTimeSignatureNumerator is < 1 or > InputLimits.MaxTimeSignatureNumerator ||
            !InputLimits.IsValidTimeSignatureDenominator(clip.SongTimeSignatureDenominator) ||
            clip.SongKeySignature is < -7 or > 7 || clip.StartTempo is < InputLimits.MinTempo or > InputLimits.MaxTempo)
            throw Invalid("The clipboard notes have invalid song settings.");

        var tracks = clip.Tracks ?? throw Invalid(ClipboardService.NotTabForgeNotesMessage);
        if (tracks.Count is < 1 or > InputLimits.MaxTracks) throw Invalid("The clipboard notes have no tracks or too many tracks.");
        if (clip.Kind == ScoreClipKind.Beats && tracks.Count != 1) throw Invalid("Copied beats must come from one track.");

        long totalCells = 0, totalNotes = 0, totalCurves = 0, totalTechniques = 0, totalBars = 0;
        var barCount = -1;
        foreach (var clipTrack in tracks)
        {
            if (clipTrack is null) throw Invalid("The clipboard notes contain an empty track.");
            var track = ValidateTrackHeader(clipTrack);
            if (clip.Kind == ScoreClipKind.Bars)
            {
                if (clipTrack.Voice is not null) throw Invalid("Copied bars carry both voices.");
                if (clipTrack.Events is { Count: > 0 }) throw Invalid("Copied bars cannot also hold beats.");
                clipTrack.Events = new List<ScoreClipEvent>();
                var bars = clipTrack.Bars ?? throw Invalid("Copied bars are missing.");
                if (bars.Count is < 1 or > InputLimits.MaxClipBars) throw Invalid($"Copied bars must number 1 to {InputLimits.MaxClipBars}.");
                if (barCount >= 0 && bars.Count != barCount) throw Invalid("Every copied track must hold the same number of bars.");
                barCount = bars.Count;
                if ((totalBars += bars.Count) > InputLimits.MaxTotalMeasures) throw Invalid("The clipboard holds too many bars.");
                foreach (var bar in bars)
                {
                    ProjectValidator.ValidateClipMeasure(bar, track, ref totalCells, ref totalNotes, ref totalCurves, ref totalTechniques);
                    foreach (var cell in bar.Cells.Concat(bar.Voice2Cells)) NormaliseCell(cell, track, keepPosition: true);
                }
                clipTrack.LengthSlots = 0;
            }
            else
            {
                if (clipTrack.Voice is not (0 or 1)) throw Invalid("Copied beats must name voice 1 or 2.");
                if (clipTrack.Bars is { Count: > 0 }) throw Invalid("Copied beats cannot also hold bars.");
                clipTrack.Bars = new List<MeasureModel>();
                var events = clipTrack.Events ?? throw Invalid("Copied beats are missing.");
                if (events.Count is < 1 or > InputLimits.MaxClipEvents) throw Invalid($"Copied beats must number 1 to {InputLimits.MaxClipEvents}.");
                if (!double.IsFinite(clipTrack.LengthSlots) || clipTrack.LengthSlots <= 0 || clipTrack.LengthSlots > InputLimits.MaxClipSlots)
                    throw Invalid("Copied beats have an invalid length.");
                var previous = 0.0;
                foreach (var entry in events)
                {
                    if (entry?.Cell is null) throw Invalid("Copied beats contain an empty beat.");
                    if (!double.IsFinite(entry.OffsetSlots) || entry.OffsetSlots < previous || entry.OffsetSlots > clipTrack.LengthSlots)
                        throw Invalid("Copied beats are out of order or outside the copied length.");
                    previous = entry.OffsetSlots;
                    ProjectValidator.ValidateClipCell(entry.Cell, track, ref totalNotes, ref totalCurves, ref totalTechniques);
                    NormaliseCell(entry.Cell, track, keepPosition: false);
                }
            }
        }
    }

    /// <summary>Checks a track header and returns the <see cref="TrackModel"/> stand-in the per-note rules are checked against.</summary>
    private static TrackModel ValidateTrackHeader(ScoreClipTrack clipTrack)
    {
        if (!InputLimits.IsSafeText(clipTrack.Name, InputLimits.MaxTitleLength, allowLineBreaks: false) ||
            !InputLimits.IsSafeText(clipTrack.InstrumentName, InputLimits.MaxTitleLength, allowLineBreaks: false) ||
            !InputLimits.IsSafeText(clipTrack.DrumMapPreset, InputLimits.MaxTitleLength, allowLineBreaks: false))
            throw Invalid("A copied track has an invalid or overlong name.");
        if (!Enum.IsDefined(clipTrack.TrackKind)) throw Invalid("A copied track has an unknown instrument type.");
        var tunings = clipTrack.StringTunings ?? throw Invalid("A copied track has no tuning.");
        if (tunings.Count is < 1 or > InputLimits.MaxStringsPerTrack || tunings.Any(t => t is < 0 or > 127))
            throw Invalid("A copied track has an invalid tuning.");
        if (clipTrack.Capo is < 0 or > 48 || clipTrack.NumberOfFrets is < 1 or > InputLimits.MaxFrets ||
            clipTrack.SourceTrackIndex is < -1 or >= InputLimits.MaxTracks || clipTrack.MidiChannel is < 0 or > 15)
            throw Invalid("A copied track has values outside the supported range.");
        return clipTrack.ToTrackModel();
    }

    private static void NormaliseCell(TabCell cell, TrackModel track, bool keepPosition)
    {
        if (cell.Notes.Count > track.StringTunings.Count) throw Invalid("A copied beat has more notes than its track has strings.");
        cell.Lyrics = "";
        if (!keepPosition) cell.RhythmicPosition = null;
    }

    // ---- Legacy clipboard text ----

    /// <summary>
    /// Converts the pre-beta.6 clipboard text (a whole-project snapshot of one track; already validated by
    /// <see cref="ProjectService.Restore"/>). One bar holding one beat was a single copied beat and becomes a Beats clip; anything
    /// else becomes a Bars clip. The old text carried no instrument, so the track header is the snapshot's (default guitar).
    /// </summary>
    private static ScoreClip FromLegacy(SongProject legacy)
    {
        var source = legacy.Tracks.FirstOrDefault(t => t.Measures.Count > 0) ?? throw Invalid(ClipboardService.NotTabForgeNotesMessage);
        if (source.Measures.Count > InputLimits.MaxClipBars) throw Invalid("The clipboard holds too many bars.");
        var header = ScoreClipTrack.FromTrack(source, -1);
        var clip = new ScoreClip
        {
            Format = FormatName,
            Version = CurrentVersion,
            SourceTrackCount = legacy.Tracks.Count,
            SongTimeSignatureNumerator = legacy.TimeSignatureNumerator,
            SongTimeSignatureDenominator = legacy.TimeSignatureDenominator,
            SongKeySignature = legacy.KeySignature,
            SongKeySignatureMinor = legacy.KeySignatureMinor,
            StartTempo = legacy.Tempo,
            Tracks = { header },
            IsLegacy = true
        };
        var only = source.Measures[0];
        if (source.Measures.Count == 1 && only.Cells.Count == 1 && only.Voice2Cells.Count == 0)
        {
            var cell = only.Cells[0].Clone();
            if (cell.Notes.Count == 0 && !cell.IsRest && !cell.HasAnnotation) throw Invalid(ClipboardService.NotTabForgeNotesMessage);
            clip.Kind = ScoreClipKind.Beats;
            header.Voice = 0;
            header.Events.Add(new ScoreClipEvent { OffsetSlots = 0, Cell = cell });
            header.LengthSlots = MusicTime.CellSlots(cell);
        }
        else
        {
            clip.Kind = ScoreClipKind.Bars;
            header.Bars.AddRange(source.Measures);
        }
        Validate(clip);
        return clip;
    }

    private static InvalidDataException Invalid(string message) => new(message);
}

/// <summary>One copied track: the source instrument (so paste can map notes, NoteMapper) and its bars or beats.</summary>
public sealed class ScoreClipTrack
{
    public Guid SourceTrackId { get; set; }
    /// <summary>Index in the source song's track list; -1 when unknown (legacy text).</summary>
    public int SourceTrackIndex { get; set; } = -1;
    public string Name { get; set; } = "";
    public string InstrumentName { get; set; } = "";
    public TrackKind TrackKind { get; set; }
    /// <summary>Drum track (kind Drums or MIDI channel 10): string/fret are drum-map positions, <see cref="TabNote.MidiValue"/> is the drum sound.</summary>
    public bool IsDrums { get; set; }
    public int MidiChannel { get; set; }
    /// <summary>Open-string pitches, high to low (<see cref="TrackModel.StringTunings"/>); the count is the string count.</summary>
    public List<int> StringTunings { get; set; } = new();
    public int Capo { get; set; }
    public int NumberOfFrets { get; set; } = 24;
    public string DrumMapPreset { get; set; } = "";
    /// <summary>Beats clips: the voice copied (0 = voice 1, 1 = voice 2). Bars clips: null (both voices).</summary>
    public int? Voice { get; set; }
    /// <summary>Bars clips: the copied bars in order, both voices and the bar settings (time signature, key, repeats, tempo, ...).</summary>
    public List<MeasureModel> Bars { get; set; } = new();
    /// <summary>Beats clips: beats in onset order, offsets in 16th slots from the first copied beat's onset.</summary>
    public List<ScoreClipEvent> Events { get; set; } = new();
    /// <summary>Beats clips: from the first beat's onset to the end of the last beat, in 16th slots.</summary>
    public double LengthSlots { get; set; }

    [JsonIgnore] public int StringCount => StringTunings.Count;

    /// <summary>Header of <paramref name="track"/> (no bars or beats yet).</summary>
    public static ScoreClipTrack FromTrack(TrackModel track, int sourceTrackIndex) => new()
    {
        SourceTrackId = track.Id,
        SourceTrackIndex = sourceTrackIndex,
        Name = track.Name,
        InstrumentName = track.InstrumentName,
        TrackKind = track.Kind,
        IsDrums = track.Kind == TrackKind.Drums || track.MidiChannel == 9,
        MidiChannel = track.MidiChannel,
        StringTunings = track.StringTunings.ToList(),
        Capo = track.Capo,
        NumberOfFrets = track.NumberOfFrets,
        DrumMapPreset = track.DrumMapPreset
    };

    /// <summary>
    /// A bar-less <see cref="TrackModel"/> with the source instrument (tuning, capo, frets, kind, drum map, channel), for
    /// <see cref="TrackModel.PitchOf"/>/<see cref="TrackModel.FretOf"/> and <see cref="DrumMaps.For(TrackModel,int)"/> during mapping.
    /// </summary>
    public TrackModel ToTrackModel() => new()
    {
        Id = SourceTrackId == Guid.Empty ? Guid.NewGuid() : SourceTrackId,
        Name = Name,
        InstrumentName = InstrumentName,
        Kind = TrackKind,
        MidiChannel = IsDrums && TrackKind != TrackKind.Drums ? 9 : MidiChannel,
        StringTunings = StringTunings.ToList(),
        Capo = Capo,
        NumberOfFrets = NumberOfFrets,
        DrumMapPreset = string.IsNullOrEmpty(DrumMapPreset) ? new TrackModel().DrumMapPreset : DrumMapPreset,
        Measures = new List<MeasureModel>()
    };
}

/// <summary>One beat of a Beats clip. <see cref="Cell"/> has no <see cref="TabCell.RhythmicPosition"/>: the offset is its onset.</summary>
public sealed class ScoreClipEvent
{
    public double OffsetSlots { get; set; }
    public TabCell Cell { get; set; } = new();
}
